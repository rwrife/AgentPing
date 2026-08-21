using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Tests;

public sealed class ProtocolFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static readonly IReadOnlyDictionary<string, Type> EnvelopeTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["event"] = typeof(ProtocolEnvelope<EventPayload>),
            ["session"] = typeof(ProtocolEnvelope<SessionPayload>),
            ["attention"] = typeof(ProtocolEnvelope<AttentionPayload>),
            ["approval"] = typeof(ProtocolEnvelope<ApprovalPayload>),
            ["denial"] = typeof(ProtocolEnvelope<DenialPayload>),
            ["reply"] = typeof(ProtocolEnvelope<ReplyPayload>),
            ["heartbeat"] = typeof(ProtocolEnvelope<HeartbeatPayload>),
            ["error"] = typeof(ProtocolEnvelope<ErrorPayload>),
            ["capability"] = typeof(ProtocolEnvelope<CapabilityPayload>),
        };

    [Fact]
    public void Every_golden_message_deserializes_and_round_trips_with_bridge_models()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath("valid-messages.json")));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fixture in document.RootElement.EnumerateArray())
        {
            var kind = fixture.GetProperty("type").GetString();
            Assert.NotNull(kind);
            Assert.True(EnvelopeTypes.TryGetValue(kind, out var envelopeType), $"Unknown fixture kind: {kind}");

            var value = JsonSerializer.Deserialize(fixture.GetRawText(), envelopeType, JsonOptions);
            Assert.NotNull(value);

            var serialized = JsonSerializer.Serialize(value, envelopeType, JsonOptions);
            var roundTripped = JsonSerializer.Deserialize(serialized, envelopeType, JsonOptions);
            Assert.NotNull(roundTripped);
            seen.Add(kind);
        }

        Assert.Equal(EnvelopeTypes.Keys.Order().ToArray(), seen.Order().ToArray());
    }

    [Fact]
    public void Bridge_constants_match_the_machine_readable_schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var root = schema.RootElement;
        var limits = root.GetProperty("x-agentping-limits");

        Assert.Equal(ProtocolV1.Version, root.GetProperty("properties").GetProperty("protocolVersion").GetProperty("const").GetString());
        Assert.Equal(ProtocolV1.MaxMessageBytes, limits.GetProperty("maxMessageBytes").GetInt32());
        Assert.Equal(ProtocolV1.MaxReplyCharacters, limits.GetProperty("maxReplyCharacters").GetInt32());
        Assert.Equal(ProtocolV1.MaxReplayWindowMessages, limits.GetProperty("maxReplayWindowMessages").GetInt32());
        Assert.Equal(ProtocolV1.ApprovalTimeoutSeconds, limits.GetProperty("approvalTimeoutSeconds").GetInt32());
    }

    [Fact]
    public void Unmapped_payload_fields_fail_closed_in_bridge_deserialization()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath("invalid-messages.json")));
        var credentialFixture = document.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "provider credential smuggled into event")
            .GetProperty("message");

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProtocolEnvelope<EventPayload>>(
            credentialFixture.GetRawText(),
            JsonOptions));
    }

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

    private static string FixturePath(string name) => Path.Combine(RepositoryRoot(), "protocol", "v1", "fixtures", name);

    private static string SchemaPath() => Path.Combine(RepositoryRoot(), "protocol", "v1", "agentping.schema.json");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "protocol", "v1")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root from test output.");
    }
}
