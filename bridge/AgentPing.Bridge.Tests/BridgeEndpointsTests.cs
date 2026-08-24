using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPing.Bridge.Tests;

public sealed class BridgeEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _deviceTokensPath;

    public BridgeEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"agentping-http-{Guid.NewGuid():N}");
        _deviceTokensPath = Path.Combine(stateDirectory, "device-tokens.json");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(stateDirectory, "state.json"));
            builder.UseSetting("Bridge:DeviceTokensPath", _deviceTokensPath);
            builder.UseSetting("Bridge:MaxHistory", "2");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.Zero)));
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_endpoint_returns_the_baseline_contract()
    {
        var response = await _client.GetFromJsonAsync<BridgeStatus>("/api/status");

        Assert.NotNull(response);
        Assert.Equal("agentping-bridge", response.Service);
        Assert.Equal("ok", response.Status);
        Assert.Equal("1.0", response.ApiVersion);
        Assert.Equal(0, response.SessionCount);
        Assert.Equal(0, response.AttentionCount);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.Zero), response.TimestampUtc);
    }

    [Fact]
    public async Task Event_endpoint_normalizes_protocol_event_into_status_state()
    {
        const string json = """
            {
              "protocolVersion":"1.0",
              "messageId":"40000000-0000-4000-8000-000000000001",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-http-test",
              "sequence":1,
              "payload":{
                "eventId":"http-event-1",
                "sessionId":"http-session-1",
                "provider":"codex",
                "eventKind":"started",
                "summary":"HTTP ingestion",
                "severity":"info"
              }
            }
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/events", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal("1.0", status.RootElement.GetProperty("apiVersion").GetString());
        Assert.Equal(1, status.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal(0, status.RootElement.GetProperty("attentionCount").GetInt32());
    }

    [Fact]
    public async Task Event_endpoint_rejects_noncontiguous_connection_sequence()
    {
        const string json = """
            {
              "protocolVersion":"1.0",
              "messageId":"40000000-0000-4000-8000-000000000002",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-sequence-test",
              "sequence":2,
              "payload":{
                "eventId":"http-sequence-gap",
                "sessionId":"http-sequence-session",
                "provider":"codex",
                "eventKind":"started",
                "summary":"Must not apply",
                "severity":"info"
              }
            }
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/events", content);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal(0, status.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal<ulong>(0, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
    }

    [Fact]
    public async Task Event_endpoint_returns_conflict_for_changed_identifier_reuse()
    {
        var first = new
        {
            protocolVersion = "1.0",
            messageId = Guid.NewGuid(),
            type = "event",
            sentAt = new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.Zero),
            connectionId = "provider-fingerprint-http",
            sequence = 1,
            payload = new
            {
                eventId = "http-fingerprint-event",
                sessionId = "http-fingerprint-session",
                provider = "codex",
                eventKind = "started",
                summary = "Original",
                severity = "info",
            },
        };
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/events", first)).StatusCode);
        var changed = new
        {
            first.protocolVersion,
            messageId = Guid.NewGuid(),
            first.type,
            sentAt = first.sentAt.AddSeconds(1),
            first.connectionId,
            sequence = 2,
            payload = new
            {
                first.payload.eventId,
                first.payload.sessionId,
                first.payload.provider,
                first.payload.eventKind,
                summary = "Changed",
                first.payload.severity,
            },
        };

        using var response = await _client.PostAsJsonAsync("/api/events", changed);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Event_endpoint_rejects_credential_like_metadata()
    {
        const string json = """
            {
              "protocolVersion":"1.0",
              "messageId":"80000000-0000-4000-8000-000000000001",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-invalid-test",
              "sequence":1,
              "payload":{
                "eventId":"invalid-credential-event",
                "sessionId":"invalid-session",
                "provider":"codex",
                "eventKind":"message",
                "summary":"Reject this",
                "severity":"error",
                "metadata":{"provider_token":"not-a-real-secret"}
              }
            }
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/events", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal(0, status.RootElement.GetProperty("sessionCount").GetInt32());
    }

    [Fact]
    public async Task Event_endpoint_rejects_payload_over_wire_limit()
    {
        var json = "{\"padding\":\"" + new string('x', AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes) + "\"}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/events", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Event_endpoint_returns_sanitized_service_unavailable_when_persistence_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentping-http-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var failingFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(root, "state.json")));
        using var client = failingFactory.CreateClient();
        _ = await client.GetAsync("/api/status");
        Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
        const string json = """
            {
              "protocolVersion":"1.0",
              "messageId":"81000000-0000-4000-8000-000000000001",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-persistence-failure",
              "sequence":1,
              "payload":{
                "eventId":"persistence-http-event",
                "sessionId":"persistence-http-session",
                "provider":"codex",
                "eventKind":"started",
                "summary":"Must roll back",
                "severity":"error"
              }
            }
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/events", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("State unavailable", problem.GetProperty("title").GetString());
        Assert.Equal("Bridge state could not be committed.", problem.GetProperty("detail").GetString());
        Assert.DoesNotContain(root, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var status = JsonDocument.Parse(await client.GetStringAsync("/api/status"));
        Assert.Equal(0, status.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal<ulong>(0, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
    }

    [Fact]
    public async Task Attention_endpoint_queues_protocol_attention_for_existing_session()
    {
        const string eventJson = """
            {
              "protocolVersion":"1.0",
              "messageId":"60000000-0000-4000-8000-000000000001",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-attention-test",
              "sequence":1,
              "payload":{
                "eventId":"attention-seed-event",
                "sessionId":"attention-session",
                "provider":"codex",
                "eventKind":"started",
                "summary":"Attention test",
                "severity":"info"
              }
            }
            """;
        using var eventContent = new StringContent(eventJson, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/events", eventContent)).StatusCode);
        const string attentionJson = """
            {
              "protocolVersion":"1.0",
              "messageId":"60000000-0000-4000-8000-000000000002",
              "type":"attention",
              "sentAt":"2026-08-22T19:00:01Z",
              "connectionId":"provider-attention-test",
              "sequence":2,
              "payload":{
                "attentionId":"attention-http-1",
                "sessionId":"attention-session",
                "revision":2,
                "category":"approval",
                "title":"Proceed?",
                "body":"The agent requires a decision.",
                "responseDeadlineAt":"2026-08-22T19:00:31Z",
                "destructive":false,
                "allowedActions":["approve","deny"]
              }
            }
            """;
        using var attentionContent = new StringContent(attentionJson, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/attentions", attentionContent);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal(1, status.RootElement.GetProperty("attentionCount").GetInt32());
    }

    [Fact]
    public async Task Authenticated_WebSocket_negotiates_protocol_capability()
    {
        var token = TestCredential(nameof(Authenticated_WebSocket_negotiates_protocol_capability));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"50000000-0000-4000-8000-000000000001",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"display-connection-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["events","sessions","attention","resume"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[Protocol.ProtocolV1.MaxMessageBytes];

        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Text, received.MessageType);
        using var message = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
        Assert.Equal("capability", message.RootElement.GetProperty("type").GetString());
        Assert.Equal("1.0", message.RootElement.GetProperty("protocolVersion").GetString());
        Assert.Equal("bridge", message.RootElement.GetProperty("payload").GetProperty("role").GetString());
        var features = message.RootElement.GetProperty("payload").GetProperty("features")
            .EnumerateArray()
            .Select(feature => feature.GetString())
            .ToArray();
        Assert.Equal(new[] { "events", "sessions", "attention", "resume" }, features);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task WebSocket_rejects_schema_invalid_capability_features()
    {
        var token = TestCredential(nameof(WebSocket_rejects_schema_invalid_capability_features));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"52000000-0000-4000-8000-000000000001",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"display-invalid-capability",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["heartbeat"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];

        var close = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "close acknowledged", CancellationToken.None);
    }

    [Fact]
    public async Task Connected_display_receives_normalized_session_after_event_ingestion()
    {
        var token = TestCredential(nameof(Connected_display_receives_normalized_session_after_event_ingestion));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"50000000-0000-4000-8000-000000000002",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"display-live-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions","resume"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);
        const string eventJson = """
            {
              "protocolVersion":"1.0",
              "messageId":"50000000-0000-4000-8000-000000000003",
              "type":"event",
              "sentAt":"2026-08-22T19:00:01Z",
              "connectionId":"provider-live-test",
              "sequence":1,
              "payload":{
                "eventId":"live-event-1",
                "sessionId":"live-session-1",
                "provider":"codex",
                "eventKind":"progress",
                "summary":"Live fan-out",
                "severity":"info"
              }
            }
            """;
        using var content = new StringContent(eventJson, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/events", content)).StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var received = await socket.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal(WebSocketMessageType.Text, received.MessageType);
        using var message = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
        Assert.Equal("session", message.RootElement.GetProperty("type").GetString());
        Assert.Equal("live-session-1", message.RootElement.GetProperty("payload").GetProperty("sessionId").GetString());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Reconnecting_display_replays_committed_session_state()
    {
        const string eventJson = """
            {
              "protocolVersion":"1.0",
              "messageId":"90000000-0000-4000-8000-000000000001",
              "type":"event",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"provider-replay-test",
              "sequence":1,
              "payload":{
                "eventId":"replay-event-1",
                "sessionId":"replay-session-1",
                "provider":"codex",
                "eventKind":"started",
                "summary":"Replay me",
                "severity":"info"
              }
            }
            """;
        using var eventContent = new StringContent(eventJson, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/events", eventContent)).StatusCode);
        var token = TestCredential(nameof(Reconnecting_display_replays_committed_session_state));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"90000000-0000-4000-8000-000000000002",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:01Z",
              "connectionId":"display-replay-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions","resume"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);

        var replay = await socket.ReceiveAsync(buffer, CancellationToken.None);

        using var message = JsonDocument.Parse(buffer.AsMemory(0, replay.Count));
        Assert.Equal("session", message.RootElement.GetProperty("type").GetString());
        Assert.Equal<ulong>(1, message.RootElement.GetProperty("serverSequence").GetUInt64());
        Assert.Equal("replay-session-1", message.RootElement.GetProperty("payload").GetProperty("sessionId").GetString());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Reconnecting_display_receives_fresh_snapshot_when_replay_window_is_missed()
    {
        for (var index = 1; index <= 3; index++)
        {
            using var response = await _client.PostAsJsonAsync("/api/events", new
            {
                protocolVersion = "1.0",
                messageId = Guid.NewGuid(),
                type = "event",
                sentAt = new DateTimeOffset(2026, 8, 22, 19, 0, index, TimeSpan.Zero),
                connectionId = "provider-snapshot-test",
                sequence = index,
                payload = new
                {
                    eventId = $"snapshot-event-{index}",
                    sessionId = "snapshot-session-1",
                    provider = "codex",
                    eventKind = "progress",
                    summary = $"Snapshot {index}",
                    severity = "info",
                },
            });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using (var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status")))
        {
            Assert.Equal(2, status.RootElement.GetProperty("historyCount").GetInt32());
            Assert.Equal<ulong>(3, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
        }

        var token = TestCredential(nameof(Reconnecting_display_receives_fresh_snapshot_when_replay_window_is_missed));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"91000000-0000-4000-8000-000000000001",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:04Z",
              "connectionId":"display-snapshot-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions","resume"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];
        var capabilityResponse = await socket.ReceiveAsync(buffer, CancellationToken.None);
        using var capabilityMessage = JsonDocument.Parse(buffer.AsMemory(0, capabilityResponse.Count));
        var capabilityPayload = capabilityMessage.RootElement.GetProperty("payload");
        Assert.True(capabilityPayload.GetProperty("resetState").GetBoolean());
        Assert.Equal(1, capabilityPayload.GetProperty("snapshotItemCount").GetInt32());
        var checkpoint = capabilityPayload.GetProperty("snapshotCheckpoint").GetUInt64();
        Assert.Equal<ulong>(3, checkpoint);

        var snapshot = await socket.ReceiveAsync(buffer, CancellationToken.None);

        using var message = JsonDocument.Parse(buffer.AsMemory(0, snapshot.Count));
        Assert.Equal("session", message.RootElement.GetProperty("type").GetString());
        Assert.False(message.RootElement.TryGetProperty("serverSequence", out _));
        Assert.Equal("snapshot-session-1", message.RootElement.GetProperty("payload").GetProperty("sessionId").GetString());
        Assert.Equal("Snapshot 3", message.RootElement.GetProperty("payload").GetProperty("displayName").GetString());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "snapshot applied", CancellationToken.None);

        using var resumedSocket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var resumedCapability = Encoding.UTF8.GetBytes($$"""
            {
              "protocolVersion":"1.0",
              "messageId":"91000000-0000-4000-8000-000000000002",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:05Z",
              "connectionId":"display-snapshot-resume-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions","resume"],
                "maxMessageBytes":16384,
                "resumeFromSequence":{{checkpoint}}
              }
            }
            """);
        await resumedSocket.SendAsync(resumedCapability, WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await resumedSocket.ReceiveAsync(buffer, CancellationToken.None);
        using var fourthEventResponse = await _client.PostAsJsonAsync("/api/events", new
        {
            protocolVersion = "1.0",
            messageId = Guid.NewGuid(),
            type = "event",
            sentAt = new DateTimeOffset(2026, 8, 22, 19, 0, 6, TimeSpan.Zero),
            connectionId = "provider-snapshot-test",
            sequence = 4,
            payload = new
            {
                eventId = "snapshot-event-4",
                sessionId = "snapshot-session-1",
                provider = "codex",
                eventKind = "progress",
                summary = "Snapshot 4",
                severity = "info",
            },
        });
        Assert.Equal(HttpStatusCode.Accepted, fourthEventResponse.StatusCode);

        var live = await resumedSocket.ReceiveAsync(buffer, CancellationToken.None);
        using var liveMessage = JsonDocument.Parse(buffer.AsMemory(0, live.Count));
        Assert.Equal<ulong>(4, liveMessage.RootElement.GetProperty("serverSequence").GetUInt64());
        Assert.Equal("Snapshot 4", liveMessage.RootElement.GetProperty("payload").GetProperty("displayName").GetString());
        await resumedSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Missed_replay_window_frames_an_empty_snapshot_with_a_persistable_checkpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentping-empty-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statePath = Path.Combine(root, "state.json");
        var tokensPath = Path.Combine(root, "tokens.json");
        await File.WriteAllTextAsync(statePath, "{\"lastServerSequence\":3}");
        var token = TestCredential(nameof(Missed_replay_window_frames_an_empty_snapshot_with_a_persistable_checkpoint));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(tokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", statePath);
            builder.UseSetting("Bridge:DeviceTokensPath", tokensPath);
        });
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var request = """
            {"protocolVersion":"1.0","messageId":"a1000000-0000-4000-8000-000000000001","type":"capability","sentAt":"2026-08-22T19:00:00Z","connectionId":"empty-snapshot","sequence":1,"payload":{"deviceId":"display-test","role":"display","supportedVersions":["1.0"],"features":["resume"],"maxMessageBytes":16384,"resumeFromSequence":0}}
            """u8.ToArray();
        await socket.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[Protocol.ProtocolV1.MaxMessageBytes];
        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        using var response = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
        var payload = response.RootElement.GetProperty("payload");
        Assert.True(payload.GetProperty("resetState").GetBoolean());
        Assert.Equal(0, payload.GetProperty("snapshotItemCount").GetInt32());
        Assert.Equal<ulong>(3, payload.GetProperty("snapshotCheckpoint").GetUInt64());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "checkpoint persisted", CancellationToken.None);
    }

    [Fact]
    public async Task Out_of_sequence_heartbeat_closes_fail_closed()
    {
        var token = TestCredential(nameof(Out_of_sequence_heartbeat_closes_fail_closed));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"90000000-0000-4000-8000-000000000003",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"display-heartbeat-test",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var heartbeat = """
            {
              "protocolVersion":"1.0",
              "messageId":"90000000-0000-4000-8000-000000000004",
              "type":"heartbeat",
              "sentAt":"2026-08-22T19:00:01Z",
              "connectionId":"display-heartbeat-test",
              "sequence":3,
              "payload":{"uptimeMs":1000,"status":"ready","lastReceivedSequence":1,"queueDepth":0}
            }
            """u8.ToArray();
        await socket.SendAsync(heartbeat, WebSocketMessageType.Text, true, CancellationToken.None);

        var close = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "close acknowledged", CancellationToken.None);
    }

    [Fact]
    public async Task Schema_invalid_heartbeat_closes_fail_closed()
    {
        var token = TestCredential(nameof(Schema_invalid_heartbeat_closes_fail_closed));
        Directory.CreateDirectory(Path.GetDirectoryName(_deviceTokensPath)!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(_deviceTokensPath, $$"""
            {"devices":[{"deviceId":"display-test","tokenSha256":"{{digest}}","revoked":false}]}
            """);
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        var capability = """
            {
              "protocolVersion":"1.0",
              "messageId":"92000000-0000-4000-8000-000000000001",
              "type":"capability",
              "sentAt":"2026-08-22T19:00:00Z",
              "connectionId":"display-invalid-heartbeat",
              "sequence":1,
              "payload":{
                "deviceId":"display-test",
                "role":"display",
                "supportedVersions":["1.0"],
                "features":["sessions"],
                "maxMessageBytes":16384,
                "resumeFromSequence":0
              }
            }
            """u8.ToArray();
        await socket.SendAsync(capability, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[AgentPing.Bridge.Protocol.ProtocolV1.MaxMessageBytes];
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var heartbeat = """
            {
              "protocolVersion":"1.0",
              "messageId":"92000000-0000-4000-8000-000000000002",
              "type":"heartbeat",
              "sentAt":"2026-08-22T19:00:01Z",
              "connectionId":"display-invalid-heartbeat",
              "sequence":2,
              "payload":{"uptimeMs":1000,"status":"unknown","lastReceivedSequence":1,"queueDepth":257}
            }
            """u8.ToArray();
        await socket.SendAsync(heartbeat, WebSocketMessageType.Text, true, CancellationToken.None);

        var close = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "close acknowledged", CancellationToken.None);
    }

    [Fact]
    public async Task WebSocket_endpoint_rejects_missing_device_token()
    {
        using var response = await _client.GetAsync("/ws");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string TestCredential(string purpose) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"agentping-test:{purpose}")))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
