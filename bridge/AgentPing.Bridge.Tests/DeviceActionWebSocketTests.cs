using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPing.Bridge.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPing.Bridge.Tests;

public sealed class DeviceActionWebSocketTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 19, 0, 2, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;
    private readonly string _token;

    public DeviceActionWebSocketTests()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentping-action-ws-{Guid.NewGuid():N}");
        var tokensPath = Path.Combine(root, "tokens.json");
        _token = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(root)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Directory.CreateDirectory(root);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_token))).ToLowerInvariant();
        File.WriteAllText(tokensPath, $$"""
            {"devices":[{"deviceId":"display-action-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(root, "state.json"));
            builder.UseSetting("Bridge:DeviceTokensPath", tokensPath);
            builder.UseSetting("Bridge:AllowLegacyDevelopmentTokenFile", "true");
            builder.UseSetting("Bridge:MaxHistory", "16");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });
        });
        _http = _factory.CreateClient();
    }

    [Fact]
    public async Task Authenticated_device_approval_is_committed_echoed_and_fanned_out()
    {
        await SeedAttentionAsync();
        var socketClient = _factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {_token}";
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        await SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = "60000000-0000-4000-8000-000000000001",
            type = "capability",
            sentAt = Now,
            connectionId = "display-action-connection",
            sequence = 1,
            payload = new
            {
                deviceId = "display-action-test",
                role = "display",
                supportedVersions = new[] { "1.0" },
                features = new[] { "sessions", "attention", "approve", "deny", "reply", "resume" },
                maxMessageBytes = 16384,
                resumeFromSequence = 2,
            },
        });
        _ = await ReceiveAsync(socket);
        await SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = "60000000-0000-4000-8000-000000000002",
            type = "approval",
            sentAt = Now,
            connectionId = "display-action-connection",
            sequence = 2,
            payload = new
            {
                actionId = "70000000-0000-4000-8000-000000000001",
                attentionId = "attention-ws-1",
                expectedRevision = 2,
                destructive = false,
            },
        });

        using var outcome = await ReceiveAsync(socket);
        using var session = await ReceiveAsync(socket);

        Assert.Equal("approval", outcome.RootElement.GetProperty("type").GetString());
        Assert.Equal("70000000-0000-4000-8000-000000000001", outcome.RootElement.GetProperty("payload").GetProperty("actionId").GetString());
        Assert.Equal("session", session.RootElement.GetProperty("type").GetString());
        Assert.Equal("running", session.RootElement.GetProperty("payload").GetProperty("state").GetString());
        using var status = JsonDocument.Parse(await _http.GetStringAsync("/api/status"));
        Assert.Equal(0, status.RootElement.GetProperty("attentionCount").GetInt32());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Replayed_committed_action_after_reconnect_wakes_retried_provider_waiter()
    {
        await SeedAttentionAsync();
        var socketClient = _factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {_token}";
        using (var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None))
        {
            await SendCapabilityAsync(socket, "display-action-first", resumeFromSequence: 2);
            _ = await ReceiveAsync(socket);
            await SendApprovalAsync(socket, "display-action-first");
            _ = await ReceiveAsync(socket);
            _ = await ReceiveAsync(socket);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect test", CancellationToken.None);
        }

        var broker = _factory.Services.GetRequiredService<ProviderActionBroker>();
        var original = await broker.WaitAsync("attention-ws-1", Now.AddSeconds(29), CancellationToken.None);
        Assert.Equal("approve", original.Action);

        using var waiterCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var replayWait = broker.WaitAsync("attention-ws-1", Now.AddSeconds(29), waiterCancellation.Token);
        var reconnectingClient = _factory.Server.CreateWebSocketClient();
        reconnectingClient.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {_token}";
        using var reconnected = await reconnectingClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        await SendCapabilityAsync(reconnected, "display-action-reconnect", resumeFromSequence: 3);
        _ = await ReceiveAsync(reconnected);
        await SendApprovalAsync(reconnected, "display-action-reconnect");
        using var echoed = await ReceiveAsync(reconnected);

        var replayed = await replayWait;
        Assert.Equal("approval", echoed.RootElement.GetProperty("type").GetString());
        Assert.Equal("approve", replayed.Action);
        Assert.Equal("recorded", replayed.Status);
        await reconnected.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    private static Task SendCapabilityAsync(WebSocket socket, string connectionId, ulong resumeFromSequence) =>
        SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = Guid.NewGuid(),
            type = "capability",
            sentAt = Now,
            connectionId,
            sequence = 1,
            payload = new
            {
                deviceId = "display-action-test",
                role = "display",
                supportedVersions = new[] { "1.0" },
                features = new[] { "sessions", "attention", "approve", "deny", "reply", "resume" },
                maxMessageBytes = 16384,
                resumeFromSequence,
            },
        });

    private static Task SendApprovalAsync(WebSocket socket, string connectionId) =>
        SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = "60000000-0000-4000-8000-000000000002",
            type = "approval",
            sentAt = Now,
            connectionId,
            sequence = 2,
            payload = new
            {
                actionId = "70000000-0000-4000-8000-000000000001",
                attentionId = "attention-ws-1",
                expectedRevision = 2,
                destructive = false,
            },
        });

    private async Task SeedAttentionAsync()
    {
        using var eventResponse = await _http.PostAsJsonAsync("/api/events", new
        {
            protocolVersion = "1.0",
            messageId = "61000000-0000-4000-8000-000000000001",
            type = "event",
            sentAt = Now.AddSeconds(-2),
            connectionId = "provider-action-ws",
            sequence = 1,
            payload = new
            {
                eventId = "event-ws-1",
                sessionId = "session-ws-1",
                provider = "manual",
                eventKind = "started",
                summary = "WebSocket action test",
                severity = "info",
            },
        });
        Assert.Equal(HttpStatusCode.Accepted, eventResponse.StatusCode);
        using var attentionResponse = await _http.PostAsJsonAsync("/api/attentions", new
        {
            protocolVersion = "1.0",
            messageId = "61000000-0000-4000-8000-000000000002",
            type = "attention",
            sentAt = Now.AddSeconds(-1),
            connectionId = "provider-action-ws",
            sequence = 2,
            payload = new
            {
                attentionId = "attention-ws-1",
                sessionId = "session-ws-1",
                revision = 2,
                category = "approval",
                title = "Run action?",
                body = "Execute the bounded test action.",
                responseDeadlineAt = Now.AddSeconds(29),
                destructive = false,
                allowedActions = new[] { "approve", "deny" },
            },
        });
        Assert.Equal(HttpStatusCode.Accepted, attentionResponse.StatusCode);
    }

    private static async Task SendAsync(WebSocket socket, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var buffer = new byte[16_384];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
