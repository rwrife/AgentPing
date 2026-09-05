using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace AgentPing.Bridge.Tests;

public sealed class BridgeEndToEndSimulationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;
    private readonly string _token;

    public BridgeEndToEndSimulationTests()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentping-e2e-sim-{Guid.NewGuid():N}");
        var tokensPath = Path.Combine(root, "device-tokens.json");
        _token = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(root)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Directory.CreateDirectory(root);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_token))).ToLowerInvariant();
        File.WriteAllText(tokensPath, $$"""
            {"devices":[{"deviceId":"display-e2e-sim","tokenSha256":"{{digest}}","revoked":false}]}
            """);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(root, "state.json"));
            builder.UseSetting("Bridge:DeviceTokensPath", tokensPath);
            builder.UseSetting("Bridge:AllowLegacyDevelopmentTokenFile", "true");
            builder.UseSetting("Bridge:MaxHistory", "64");
            builder.UseSetting("Adapters:Manual:Enabled", "true");
        });

        _http = _factory.CreateClient();
    }

    [Fact]
    public async Task Simulated_provider_attention_round_trips_through_device_approval()
    {
        var device = await ConnectDeviceAsync("display-e2e-conn-1");
        try
        {
            var providerTask = _http.PostAsJsonAsync("/api/adapters/manual?waitForAction=true", new
            {
                eventId = "manual-sim-event-1",
                sessionId = "manual-sim-session-1",
                kind = "message",
                summary = "Simulated provider asks for approval",
                attention = new
                {
                    attentionId = "manual-sim-attention-1",
                    category = "approval",
                    title = "Allow simulated operation?",
                    body = "Bridge should wait for a simulated device decision.",
                    destructive = false,
                    allowedActions = new[] { "approve", "deny" },
                },
            });

            using var attention = await ReceiveUntilAsync(
                device,
                message => message.GetProperty("type").GetString() == "attention",
                TimeSpan.FromSeconds(5));

            var canonicalAttentionId = attention.RootElement.GetProperty("payload").GetProperty("attentionId").GetString();
            Assert.StartsWith("manual-attention-", canonicalAttentionId);

            var revision = attention.RootElement.GetProperty("payload").GetProperty("revision").GetUInt32();
            await SendApprovalAsync(device, "display-e2e-conn-1", canonicalAttentionId!, revision);

            using var outcome = await ReceiveUntilAsync(
                device,
                message => message.GetProperty("type").GetString() == "approval",
                TimeSpan.FromSeconds(5));
            Assert.Equal(canonicalAttentionId, outcome.RootElement.GetProperty("payload").GetProperty("attentionId").GetString());

            using var session = await ReceiveUntilAsync(
                device,
                message => message.GetProperty("type").GetString() == "session",
                TimeSpan.FromSeconds(5));
            Assert.Equal("running", session.RootElement.GetProperty("payload").GetProperty("state").GetString());

            using var providerResponse = await providerTask;
            Assert.Equal(HttpStatusCode.OK, providerResponse.StatusCode);
            using var providerJson = JsonDocument.Parse(await providerResponse.Content.ReadAsStringAsync());
            Assert.StartsWith("manual-attention-", providerJson.RootElement.GetProperty("attentionId").GetString());
            Assert.Equal("approve", providerJson.RootElement.GetProperty("action").GetString());
            Assert.Equal("recorded", providerJson.RootElement.GetProperty("status").GetString());

            using var status = JsonDocument.Parse(await _http.GetStringAsync("/api/status"));
            Assert.Equal(0, status.RootElement.GetProperty("attentionCount").GetInt32());
        }
        finally
        {
            await device.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            device.Dispose();
        }
    }

    [Fact]
    public async Task Pending_attention_survives_disconnect_and_is_actioned_after_reconnect()
    {
        using var ingestResponse = await _http.PostAsJsonAsync("/api/adapters/manual", new
        {
            eventId = "manual-sim-event-2",
            sessionId = "manual-sim-session-2",
            kind = "message",
            summary = "Reconnect simulation",
            attention = new
            {
                attentionId = "manual-sim-attention-2",
                category = "approval",
                title = "Reconnect should preserve pending request",
                body = "The simulated device will disconnect and reconnect before responding.",
                destructive = false,
                allowedActions = new[] { "approve", "deny" },
            },
        });
        Assert.Equal(HttpStatusCode.Accepted, ingestResponse.StatusCode);

        var firstConnection = await ConnectDeviceAsync("display-e2e-conn-2a");
        try
        {
            using var firstAttention = await ReceiveUntilAsync(
                firstConnection,
                message => message.GetProperty("type").GetString() == "attention",
                TimeSpan.FromSeconds(5));
            var firstCanonicalAttentionId = firstAttention.RootElement.GetProperty("payload").GetProperty("attentionId").GetString();
            Assert.StartsWith("manual-attention-", firstCanonicalAttentionId);
            await firstConnection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
        }
        finally
        {
            firstConnection.Dispose();
        }

        var secondConnection = await ConnectDeviceAsync("display-e2e-conn-2b", resumeFromSequence: 0);
        try
        {
            using var replayedAttention = await ReceiveUntilAsync(
                secondConnection,
                message =>
                    message.GetProperty("type").GetString() == "attention"
                    && message.GetProperty("payload").GetProperty("attentionId").GetString()!.Contains("manual-sim-attention-2", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var canonicalAttentionId = replayedAttention.RootElement.GetProperty("payload").GetProperty("attentionId").GetString();
            var revision = replayedAttention.RootElement.GetProperty("payload").GetProperty("revision").GetUInt32();
            await SendApprovalAsync(secondConnection, "display-e2e-conn-2b", canonicalAttentionId!, revision);

            using var outcome = await ReceiveUntilAsync(
                secondConnection,
                message => message.GetProperty("type").GetString() == "approval",
                TimeSpan.FromSeconds(5));
            Assert.Equal(canonicalAttentionId, outcome.RootElement.GetProperty("payload").GetProperty("attentionId").GetString());
        }
        finally
        {
            await secondConnection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            secondConnection.Dispose();
        }

        using var status = JsonDocument.Parse(await _http.GetStringAsync("/api/status"));
        Assert.Equal(0, status.RootElement.GetProperty("attentionCount").GetInt32());

        using var waitResponse = await _http.PostAsJsonAsync("/api/adapters/manual?waitForAction=true", new
        {
            eventId = "manual-sim-event-2b",
            sessionId = "manual-sim-session-2b",
            kind = "message",
            summary = "Quick no-attention failure case",
        });
        Assert.Equal(HttpStatusCode.Conflict, waitResponse.StatusCode);
    }

    private async Task<WebSocket> ConnectDeviceAsync(string connectionId, ulong resumeFromSequence = 0)
    {
        var socketClient = _factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {_token}";
        var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = Guid.NewGuid(),
            type = "capability",
            sentAt = DateTimeOffset.UtcNow,
            connectionId,
            sequence = 1,
            payload = new
            {
                deviceId = "display-e2e-sim",
                role = "display",
                supportedVersions = new[] { "1.0" },
                features = new[] { "sessions", "attention", "approve", "deny", "reply", "resume" },
                maxMessageBytes = 16384,
                resumeFromSequence,
            },
        });

        using var capability = await ReceiveUntilAsync(
            socket,
            message => message.GetProperty("type").GetString() == "capability",
            TimeSpan.FromSeconds(5));
        Assert.Equal("1.0", capability.RootElement.GetProperty("protocolVersion").GetString());

        return socket;
    }

    private static Task SendApprovalAsync(WebSocket socket, string connectionId, string attentionId, uint revision) =>
        SendAsync(socket, new
        {
            protocolVersion = "1.0",
            messageId = Guid.NewGuid(),
            type = "approval",
            sentAt = DateTimeOffset.UtcNow,
            connectionId,
            sequence = 2,
            payload = new
            {
                actionId = Guid.NewGuid(),
                attentionId,
                expectedRevision = revision,
                destructive = false,
            },
        });

    private static async Task SendAsync(WebSocket socket, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveUntilAsync(
        WebSocket socket,
        Func<JsonElement, bool> predicate,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            var message = await ReceiveAsync(socket, cts.Token);
            if (predicate(message.RootElement))
            {
                return message;
            }

            message.Dispose();
        }
    }

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16_384];
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException("WebSocket closed before expected test message was received.");
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            throw new InvalidOperationException($"Unexpected message type '{result.MessageType}'.");
        }

        return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }
}
