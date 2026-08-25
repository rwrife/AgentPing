using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPing.Bridge.Providers;

public interface IProviderAdapter
{
    string Name { get; }
    string DisplayName { get; }
    string Integration { get; }
    ProviderMappedMessage Map(JsonElement source);
}

public sealed record ProviderMappedMessage(ProviderMappedEvent Event, ProviderMappedAttention? Attention = null);

public sealed record ProviderMappedEvent(
    string EventId,
    string SessionId,
    string Kind,
    string Summary,
    string? Detail,
    string Severity,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ProviderMappedAttention(
    string AttentionId,
    string Category,
    string Title,
    string Body,
    bool Destructive,
    IReadOnlyList<string> AllowedActions);

public sealed class ProviderPayloadException(string message) : Exception(message);

internal static partial class ProviderPayload
{
    public static string RequiredString(JsonElement source, params string[] names)
    {
        var value = OptionalString(source, names);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ProviderPayloadException($"Required provider field '{names[0]}' is missing.")
            : value;
    }

    public static string? OptionalString(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty(name, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }

        return null;
    }

    public static string Identifier(string value, string prefix)
    {
        var cleaned = InvalidIdentifierCharacters().Replace(value.Trim(), "-").Trim('-');
        if (cleaned.Length == 0)
        {
            cleaned = Hash(value)[..24];
        }

        var combined = $"{prefix}-{cleaned}";
        return combined.Length <= 128 ? combined : $"{prefix}-{Hash(combined)}";
    }

    public static string SourceIdentifier(JsonElement source, string prefix, params string[] preferredNames)
    {
        var preferred = OptionalString(source, preferredNames);
        return Identifier(preferred ?? Hash(source.GetRawText()), prefix);
    }

    public static string Text(string? value, int maxLength, string fallback)
    {
        var redacted = SecretRedactor.Redact(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength];
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z0-9._:-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidIdentifierCharacters();
}

internal static partial class SecretRedactor
{
    public static string Redact(string value)
    {
        var redacted = AuthorizationHeader().Replace(value, "authorization=[REDACTED]");
        redacted = BearerToken().Replace(redacted, "Bearer [REDACTED]");
        redacted = CredentialAssignment().Replace(redacted, "$1=[REDACTED]");
        redacted = KnownToken().Replace(redacted, "[REDACTED]");
        return redacted;
    }

    [GeneratedRegex("(?i)\\bauthorization\\b\\s*[:=]\\s*[^,;\\r\\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex("(?i)\\b(api[_-]?key|token|password|secret|authorization|cookie|credential)\\b\\s*[:=]\\s*([^\\s,;]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignment();

    [GeneratedRegex("(?i)\\b(sk-(?:ant-)?[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9_]{12,}|github_pat_[A-Za-z0-9_]{12,})\\b", RegexOptions.CultureInvariant)]
    private static partial Regex KnownToken();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]{8,}=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();
}
