using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.TimestampFormat = "O");
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false,
});

app.MapGet("/api/status", () => TypedResults.Ok(new BridgeStatus(
        Service: "agentping-bridge",
        Status: "ok",
        ApiVersion: "baseline-v0",
        TimestampUtc: DateTimeOffset.UtcNow)))
    .WithName("GetBridgeStatus");

app.Logger.LogInformation(
    "AgentPing Bridge initialized in {Environment}",
    app.Environment.EnvironmentName);

app.Run();

public sealed record BridgeStatus(
    string Service,
    string Status,
    string ApiVersion,
    DateTimeOffset TimestampUtc);

public partial class Program;
