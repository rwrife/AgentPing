using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;
using AgentPing.Bridge.Providers;
using AgentPing.Bridge.Security;
using AgentPing.Bridge.Transport;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.TimestampFormat = "O");
builder.Services.AddHealthChecks();
builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
builder.Services.Configure<ProviderAdapterOptions>(builder.Configuration.GetSection("Adapters"));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ProtocolV1.MaxMessageBytes;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BridgeOptions>>().Value;
    options.Validate();
    return new DeviceTokenStore(
        options.DeviceTokensPath,
        serviceProvider.GetRequiredService<ILogger<DeviceTokenStore>>());
});
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BridgeOptions>>().Value;
    options.Validate();
    return new BridgeStateStore(
        options.PersistencePath,
        options.MaxHistory,
        TimeSpan.FromSeconds(options.StaleSessionSeconds),
        serviceProvider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<DeviceConnectionHub>();
builder.Services.AddSingleton<ProviderActionBroker>();
builder.Services.AddSingleton<WebSocketSessionHandler>();
builder.Services.AddSingleton<IProviderAdapter, ManualProviderAdapter>();
builder.Services.AddSingleton<IProviderAdapter, CodexCliProviderAdapter>();
builder.Services.AddSingleton<IProviderAdapter, ClaudeCodeProviderAdapter>();
builder.Services.AddSingleton<IProviderAdapter, CopilotCliProviderAdapter>();
builder.Services.AddSingleton<ProviderAdapterDispatcher>();
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BridgeOptions>>().Value;
    return new StaleSessionMonitor(
        serviceProvider.GetRequiredService<BridgeStateStore>(),
        serviceProvider.GetRequiredService<DeviceConnectionHub>(),
        TimeSpan.FromSeconds(options.StaleSweepSeconds));
});
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<StaleSessionMonitor>());

var app = builder.Build();
var stateStore = app.Services.GetRequiredService<BridgeStateStore>();
await stateStore.InitializeAsync(app.Lifetime.ApplicationStopping);
var connectionHub = app.Services.GetRequiredService<DeviceConnectionHub>();
var initialSnapshot = await stateStore.GetSnapshotAsync(app.Lifetime.ApplicationStopping);
connectionHub.InitializeLastPublishedSequence(initialSnapshot.LastServerSequence);

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > ProtocolV1.MaxMessageBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodySizeFeature is { IsReadOnly: false })
    {
        bodySizeFeature.MaxRequestBodySize = ProtocolV1.MaxMessageBytes;
    }

    await next(context);
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false,
});

app.MapGet("/api/status", async (
    BridgeStateStore store,
    ProviderAdapterDispatcher adapters,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetSnapshotAsync(cancellationToken);
    return TypedResults.Ok(new BridgeStatus(
        Service: "agentping-bridge",
        Status: "ok",
        ApiVersion: ProtocolV1.Version,
        TimestampUtc: timeProvider.GetUtcNow(),
        SessionCount: snapshot.Sessions.Count,
        AttentionCount: snapshot.Attentions.Count,
        HistoryCount: snapshot.History.Count,
        LastServerSequence: snapshot.LastServerSequence,
        Adapters: adapters.GetStatuses()));
}).WithName("GetBridgeStatus");

app.MapPost("/api/adapters/{provider}", async Task<IResult> (
    string provider,
    bool? waitForAction,
    JsonElement source,
    HttpContext context,
    ProviderAdapterDispatcher adapters,
    ILogger<ProviderAdapterDispatcher> adapterLogger,
    CancellationToken cancellationToken) =>
{
    if (context.Connection.RemoteIpAddress is { } remoteAddress
        && !System.Net.IPAddress.IsLoopback(remoteAddress))
    {
        return Results.Problem(
            title: "Loopback required",
            detail: "Provider hook ingestion is available only over the local loopback listener.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        var result = await adapters.DispatchAsync(provider, source, cancellationToken);
        if (waitForAction == true)
        {
            return Results.Ok(await adapters.WaitForActionAsync(result, cancellationToken));
        }

        return Results.Accepted(value: result);
    }
    catch (ProviderAdapterNotFoundException)
    {
        return Results.Problem(
            title: "Unsupported provider adapter",
            detail: "The requested provider adapter is not registered.",
            statusCode: StatusCodes.Status404NotFound);
    }
    catch (ProviderAdapterDisabledException)
    {
        return Results.Problem(
            title: "Provider adapter disabled",
            detail: "Enable the provider adapter explicitly in bridge configuration before using it.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ProviderActionNotAvailableException)
    {
        return Results.Problem(
            title: "Action unavailable",
            detail: "The provider event did not create an actionable attention item.",
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (ProviderPayloadException)
    {
        return Results.Problem(
            title: "Unsupported provider payload",
            detail: "The hook payload is missing required fields or uses an unsupported event kind.",
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (BridgeStateConflictException exception)
    {
        return Results.Problem(
            title: "State conflict",
            detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        adapterLogger.LogWarning(
            "Provider adapter state commit failed; request rejected ({ExceptionType})",
            exception.GetType().Name);
        return Results.Problem(
            title: "State unavailable",
            detail: "Bridge state could not be committed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithName("IngestProviderHook");

app.MapPost("/api/events", async Task<IResult> (
    ProtocolEnvelope<EventPayload> envelope,
    BridgeStateStore store,
    DeviceConnectionHub connectionHub,
    TimeProvider timeProvider,
    ILogger<Program> bridgeLogger,
    CancellationToken cancellationToken) =>
{
    var errors = ProtocolValidation.ValidateEvent(envelope, timeProvider);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest);
    }

    IngestResult result;
    try
    {
        result = await store.IngestEventAsync(envelope, cancellationToken);
    }
    catch (BridgeStateConflictException exception)
    {
        return Results.Problem(
            title: "State conflict",
            detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        bridgeLogger.LogWarning(
            "Event state commit failed; request rejected with service unavailable ({ExceptionType})",
            exception.GetType().Name);
        return Results.Problem(
            title: "State unavailable",
            detail: "Bridge state could not be committed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!result.Duplicate)
    {
        connectionHub.Publish(result.Snapshot.History[^1]);
    }

    return Results.Accepted(value: new EventIngestResponse(
        result.Duplicate,
        result.RecordedServerSequence));
}).WithName("IngestProviderEvent");

app.MapPost("/api/attentions", async Task<IResult> (
    ProtocolEnvelope<AttentionPayload> envelope,
    BridgeStateStore store,
    DeviceConnectionHub connectionHub,
    TimeProvider timeProvider,
    ILogger<Program> bridgeLogger,
    CancellationToken cancellationToken) =>
{
    var errors = ProtocolValidation.ValidateAttention(envelope, timeProvider);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest);
    }

    IngestResult result;
    try
    {
        result = await store.IngestAttentionAsync(envelope, cancellationToken);
    }
    catch (BridgeStateConflictException exception)
    {
        return Results.Problem(
            title: "State conflict",
            detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        bridgeLogger.LogWarning(
            "Attention state commit failed; request rejected with service unavailable ({ExceptionType})",
            exception.GetType().Name);
        return Results.Problem(
            title: "State unavailable",
            detail: "Bridge state could not be committed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!result.Duplicate)
    {
        connectionHub.Publish(result.Snapshot.History[^1]);
    }

    return Results.Accepted(value: new EventIngestResponse(
        result.Duplicate,
        result.RecordedServerSequence));
}).WithName("IngestAttention");

app.MapGet("/ws", async (
    HttpContext context,
    DeviceTokenStore tokens,
    WebSocketSessionHandler sessionHandler,
    ILogger<WebSocketSessionHandler> transportLogger,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context.Request.Headers.Authorization.ToString());
    var device = await tokens.AuthenticateAsync(token, cancellationToken);
    if (device is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    try
    {
        await sessionHandler.HandleAsync(socket, device, cancellationToken);
    }
    catch (Exception exception) when (
        exception is WebSocketException or IOException or ObjectDisposedException
        || exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
    {
        transportLogger.LogDebug(
            "Device {DeviceId} WebSocket disconnected during transport shutdown: {ExceptionType}",
            device.DeviceId,
            exception.GetType().Name);
    }
});

app.Logger.LogInformation(
    "AgentPing Bridge initialized in {Environment}",
    app.Environment.EnvironmentName);

app.Run();

static string? GetBearerToken(string? authorizationHeader)
{
    const string prefix = "Bearer ";
    if (authorizationHeader is null
        || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = authorizationHeader[prefix.Length..];
    return token.Length > 0 && !token.Contains(' ') ? token : null;
}

public sealed record BridgeStatus(
    string Service,
    string Status,
    string ApiVersion,
    DateTimeOffset TimestampUtc,
    int SessionCount,
    int AttentionCount,
    int HistoryCount,
    ulong LastServerSequence,
    IReadOnlyList<ProviderAdapterStatus> Adapters);

public sealed record EventIngestResponse(bool Duplicate, ulong LastServerSequence);

public partial class Program;
