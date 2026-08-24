using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;
using AgentPing.Bridge.Security;

namespace AgentPing.Bridge.Transport;

public sealed class WebSocketSessionHandler(
    BridgeStateStore stateStore,
    DeviceConnectionHub connectionHub,
    TimeProvider timeProvider,
    ILogger<WebSocketSessionHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task HandleAsync(
        WebSocket socket,
        AuthenticatedDevice authenticatedDevice,
        CancellationToken cancellationToken)
    {
        var firstMessage = await ReceiveMessageAsync(socket, cancellationToken);
        if (firstMessage is null)
        {
            return;
        }

        ProtocolEnvelope<CapabilityPayload>? capability;
        try
        {
            capability = JsonSerializer.Deserialize<ProtocolEnvelope<CapabilityPayload>>(firstMessage, JsonOptions);
        }
        catch (JsonException)
        {
            await ClosePolicyViolationAsync(socket, "Malformed capability message.", cancellationToken);
            return;
        }

        if (!ProtocolValidation.IsValidCapability(capability, authenticatedDevice.DeviceId))
        {
            await ClosePolicyViolationAsync(socket, "Capability negotiation failed.", cancellationToken);
            return;
        }

        var inboundConnectionId = capability!.ConnectionId;
        await using var subscription = connectionHub.Subscribe();
        var snapshot = await stateStore.GetSnapshotAsync(cancellationToken);
        var resumeFromSequence = capability!.Payload.ResumeFromSequence;
        if (resumeFromSequence > snapshot.LastServerSequence)
        {
            await ClosePolicyViolationAsync(socket, "Resume sequence exceeds server state.", cancellationToken);
            return;
        }

        var connectionId = $"bridge-{Guid.NewGuid():N}";
        ulong outboundSequence = 1;
        var replayWindowMissed = resumeFromSequence < snapshot.LastServerSequence
            && (snapshot.History.Count == 0
                || resumeFromSequence + 1 < snapshot.History[0].ServerSequence);
        var snapshotItemCount = snapshot.Sessions.Count + snapshot.Attentions.Count;
        await SendAsync(socket, new ProtocolEnvelope<CapabilityPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.NewGuid(),
            Type = MessageKind.Capability,
            SentAt = timeProvider.GetUtcNow(),
            ConnectionId = connectionId,
            Sequence = outboundSequence,
            Payload = new CapabilityPayload
            {
                DeviceId = "agentping-bridge",
                Role = "bridge",
                SupportedVersions = [ProtocolV1.Version],
                Features = ["events", "sessions", "attention", "resume"],
                MaxMessageBytes = ProtocolV1.MaxMessageBytes,
                ResumeFromSequence = resumeFromSequence,
                ResetState = replayWindowMissed,
                SnapshotItemCount = replayWindowMissed ? snapshotItemCount : null,
                SnapshotCheckpoint = replayWindowMissed ? snapshot.LastServerSequence : null,
                SoftwareVersion = typeof(WebSocketSessionHandler).Assembly.GetName().Version?.ToString(),
            },
        }, cancellationToken);

        var lastPublishedServerSequence = resumeFromSequence;
        if (replayWindowMissed)
        {
            foreach (var session in snapshot.Sessions)
            {
                outboundSequence++;
                await SendSnapshotItemAsync(
                    socket,
                    MessageKind.Session,
                    session.UpdatedAt,
                    session,
                    connectionId,
                    outboundSequence,
                    null,
                    cancellationToken);
            }

            foreach (var attention in snapshot.Attentions)
            {
                outboundSequence++;
                await SendSnapshotItemAsync(
                    socket,
                    MessageKind.Attention,
                    timeProvider.GetUtcNow(),
                    attention,
                    connectionId,
                    outboundSequence,
                    null,
                    cancellationToken);
            }

            lastPublishedServerSequence = snapshot.LastServerSequence;
        }
        else
        {
            foreach (var item in snapshot.History.Where(item => item.ServerSequence > lastPublishedServerSequence))
            {
                outboundSequence++;
                await SendHistoryItemAsync(socket, item, connectionId, outboundSequence, cancellationToken);
                lastPublishedServerSequence = item.ServerSequence;
            }
        }

        var processedMessages = new Dictionary<Guid, (MessageKind Type, ulong Sequence)>
        {
            [capability.MessageId] = (MessageKind.Capability, 1),
        };
        var processedHeartbeatOrder = new Queue<Guid>();
        ulong expectedInboundSequence = 2;
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ReceiveMessageAsync(socket, sessionCancellation.Token);
        var outboundTask = subscription.Reader.ReadAsync(sessionCancellation.Token).AsTask();
        try
        {
            while (socket.State == WebSocketState.Open && !sessionCancellation.IsCancellationRequested)
            {
                var completedTask = await Task.WhenAny(receiveTask, outboundTask);
                if (completedTask == outboundTask)
                {
                    var item = await outboundTask;
                    if (item.ServerSequence > lastPublishedServerSequence)
                    {
                        outboundSequence++;
                        await SendHistoryItemAsync(socket, item, connectionId, outboundSequence, sessionCancellation.Token);
                        lastPublishedServerSequence = item.ServerSequence;
                    }

                    outboundTask = subscription.Reader.ReadAsync(sessionCancellation.Token).AsTask();
                    continue;
                }

                var message = await receiveTask;
                if (message is null)
                {
                    return;
                }

                ProtocolEnvelope<HeartbeatPayload>? heartbeat;
                try
                {
                    heartbeat = JsonSerializer.Deserialize<ProtocolEnvelope<HeartbeatPayload>>(message, JsonOptions);
                }
                catch (JsonException)
                {
                    await ClosePolicyViolationAsync(socket, "Malformed heartbeat message.", sessionCancellation.Token);
                    return;
                }

                if (!ProtocolValidation.IsValidHeartbeat(heartbeat, inboundConnectionId))
                {
                    await ClosePolicyViolationAsync(socket, "Unexpected message or sequence.", sessionCancellation.Token);
                    return;
                }

                var validHeartbeat = heartbeat!;
                if (processedMessages.TryGetValue(validHeartbeat.MessageId, out var processed))
                {
                    if (processed.Type != MessageKind.Heartbeat
                        || processed.Sequence != validHeartbeat.Sequence)
                    {
                        await ClosePolicyViolationAsync(socket, "Message ID reuse is inconsistent.", sessionCancellation.Token);
                        return;
                    }

                    receiveTask = ReceiveMessageAsync(socket, sessionCancellation.Token);
                    continue;
                }

                if (validHeartbeat.Sequence != expectedInboundSequence)
                {
                    await ClosePolicyViolationAsync(socket, "Unexpected message or sequence.", sessionCancellation.Token);
                    return;
                }

                processedMessages.Add(
                    validHeartbeat.MessageId,
                    (MessageKind.Heartbeat, validHeartbeat.Sequence));
                processedHeartbeatOrder.Enqueue(validHeartbeat.MessageId);
                if (processedHeartbeatOrder.Count > ProtocolV1.MaxReplayWindowMessages)
                {
                    processedMessages.Remove(processedHeartbeatOrder.Dequeue());
                }

                expectedInboundSequence++;
                logger.LogDebug(
                    "Heartbeat received from device {DeviceId} at sequence {Sequence}",
                    authenticatedDevice.DeviceId,
                    validHeartbeat.Sequence);

                receiveTask = ReceiveMessageAsync(socket, sessionCancellation.Token);
            }
        }
        finally
        {
            sessionCancellation.Cancel();
        }
    }

    private static async Task<byte[]?> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>(1024);
        while (true)
        {
            var memory = writer.GetMemory(4096);
            var result = await socket.ReceiveAsync(memory, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State is WebSocketState.CloseReceived or WebSocketState.Open)
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed.",
                        cancellationToken);
                }

                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "Only text messages are accepted.",
                    cancellationToken);
                return null;
            }

            writer.Advance(result.Count);
            if (writer.WrittenCount > ProtocolV1.MaxMessageBytes)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "Message exceeds protocol limit.",
                    cancellationToken);
                return null;
            }

            if (result.EndOfMessage)
            {
                return writer.WrittenMemory.ToArray();
            }
        }
    }

    private static Task SendSnapshotItemAsync<TPayload>(
        WebSocket socket,
        MessageKind type,
        DateTimeOffset sentAt,
        TPayload payload,
        string connectionId,
        ulong outboundSequence,
        ulong? serverSequence,
        CancellationToken cancellationToken) =>
        SendAsync(socket, new ProtocolEnvelope<TPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.NewGuid(),
            Type = type,
            SentAt = sentAt,
            ConnectionId = connectionId,
            Sequence = outboundSequence,
            ServerSequence = serverSequence,
            Payload = payload,
        }, cancellationToken);

    private static Task SendHistoryItemAsync(
        WebSocket socket,
        BridgeHistoryItem item,
        string connectionId,
        ulong outboundSequence,
        CancellationToken cancellationToken) =>
        SendAsync(socket, new OutboundEnvelope(
            ProtocolV1.Version,
            item.SourceMessageId == Guid.Empty ? Guid.NewGuid() : item.SourceMessageId,
            item.Type,
            item.SentAt,
            connectionId,
            outboundSequence,
            item.ServerSequence,
            item.Payload), cancellationToken);

    private static async Task SendAsync<T>(
        WebSocket socket,
        T message,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > ProtocolV1.MaxMessageBytes)
        {
            throw new InvalidOperationException("Outbound protocol message exceeds the 16 KiB limit.");
        }

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static Task ClosePolicyViolationAsync(
        WebSocket socket,
        string description,
        CancellationToken cancellationToken) =>
        socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, description, cancellationToken);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private sealed record OutboundEnvelope(
        string ProtocolVersion,
        Guid MessageId,
        MessageKind Type,
        DateTimeOffset SentAt,
        string ConnectionId,
        ulong Sequence,
        ulong ServerSequence,
        JsonElement Payload);
}
