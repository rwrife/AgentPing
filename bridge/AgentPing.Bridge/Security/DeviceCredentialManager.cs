using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Security;

public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> value);
    byte[] Unprotect(ReadOnlySpan<byte> value);
}

public interface ICredentialPersistence
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAtomicAsync(string content, CancellationToken cancellationToken = default);
}

public sealed class FileCredentialPersistence(string path) : ICredentialPersistence
{
    private readonly string _path = Path.GetFullPath(path);

    public Task<string?> ReadAsync(CancellationToken cancellationToken = default) =>
        File.Exists(_path) ? File.ReadAllTextAsync(_path, cancellationToken).ContinueWith<string?>(t => t.Result, cancellationToken) : Task.FromResult<string?>(null);

    public async Task WriteAtomicAsync(string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Credential path needs a parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16_384, FileOptions.Asynchronous | FileOptions.WriteThrough))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, _path, true);
    }
}

public sealed class MemoryCredentialPersistence : ICredentialPersistence
{
    public string Content { get; private set; } = "";
    public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(Content);
    public Task WriteAtomicAsync(string content, CancellationToken cancellationToken = default) { Content = content; return Task.CompletedTask; }
}

public interface IDeviceLifecycleInvalidator
{
    Task InvalidateAsync(string deviceId, CancellationToken cancellationToken = default);
}

public sealed class DeviceCredentialManager(
    ICredentialPersistence persistence,
    ISecretProtector protector,
    IDeviceLifecycleInvalidator lifecycle,
    LegacyDevelopmentTokenAuthenticator? legacy = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<byte[]> IssueAsync(string deviceId, CancellationToken cancellationToken = default) => ReplaceAsync(deviceId, cancellationToken);

    public async Task<byte[]> RotateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var token = await ReplaceAsync(deviceId, cancellationToken);
        await lifecycle.InvalidateAsync(deviceId, cancellationToken);
        return token;
    }

    public async Task RevokeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            if (!document.Devices.TryGetValue(deviceId, out var record)) throw new KeyNotFoundException("Device was not found.");
            record.Revoked = true;
            await SaveAsync(document, cancellationToken);
        }
        finally { _gate.Release(); }
        await lifecycle.InvalidateAsync(deviceId, cancellationToken);
    }

    public async Task<AuthenticatedDevice?> AuthenticateAsync(string? encodedToken, CancellationToken cancellationToken = default)
    {
        if (!Base64Url.TryDecode(encodedToken, out var token) || token.Length != 32) return null;
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var document = await LoadAsync(cancellationToken);
                if (string.IsNullOrEmpty(document.ProtectedLookupKey)) return legacy is null ? null : await legacy.AuthenticateAsync(encodedToken!, cancellationToken);
                var key = protector.Unprotect(Convert.FromBase64String(document.ProtectedLookupKey));
                try
                {
                    var digest = HMACSHA256.HashData(key, token);
                    foreach (var (deviceId, record) in document.Devices)
                    {
                        if (!record.Revoked && CryptographicOperations.FixedTimeEquals(digest, Convert.FromBase64String(record.LookupDigest)))
                            return new AuthenticatedDevice(deviceId);
                    }
                    return legacy is null ? null : await legacy.AuthenticateAsync(encodedToken!, cancellationToken);
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            finally { _gate.Release(); }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or IOException or UnauthorizedAccessException) { return null; }
        finally { CryptographicOperations.ZeroMemory(token); }
    }

    public async Task<IReadOnlyList<DeviceCredentialSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Devices.Select(pair => new DeviceCredentialSummary(pair.Key, pair.Value.Revoked, pair.Value.UpdatedUtc))
                .OrderBy(item => item.DeviceId, StringComparer.Ordinal).ToArray();
        }
        finally { _gate.Release(); }
    }

    private async Task<byte[]> ReplaceAsync(string deviceId, CancellationToken cancellationToken)
    {
        ValidateDeviceId(deviceId);
        var token = RandomNumberGenerator.GetBytes(32);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            byte[] key;
            if (string.IsNullOrEmpty(document.ProtectedLookupKey))
            {
                key = RandomNumberGenerator.GetBytes(32);
                document.ProtectedLookupKey = Convert.ToBase64String(protector.Protect(key));
            }
            else key = protector.Unprotect(Convert.FromBase64String(document.ProtectedLookupKey));
            try
            {
                document.Devices[deviceId] = new CredentialRecord
                {
                    LookupDigest = Convert.ToBase64String(HMACSHA256.HashData(key, token)),
                    ProtectedToken = Convert.ToBase64String(protector.Protect(token)),
                    UpdatedUtc = DateTimeOffset.UtcNow,
                };
                await SaveAsync(document, cancellationToken);
                return token;
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch { CryptographicOperations.ZeroMemory(token); throw; }
        finally { _gate.Release(); }
    }

    private async Task<CredentialDocument> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await persistence.ReadAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? new() : JsonSerializer.Deserialize<CredentialDocument>(content, JsonOptions) ?? throw new JsonException();
    }
    private Task SaveAsync(CredentialDocument document, CancellationToken cancellationToken) => persistence.WriteAtomicAsync(JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
    public static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 || deviceId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("Device ID must contain 1-128 ASCII letters, digits, '.', '-' or '_'.", nameof(deviceId));
    }

    private sealed class CredentialDocument
    {
        public string ProtectedLookupKey { get; set; } = "";
        public Dictionary<string, CredentialRecord> Devices { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed class CredentialRecord
    {
        public string LookupDigest { get; set; } = "";
        public string ProtectedToken { get; set; } = "";
        public bool Revoked { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}

public sealed class LegacyDevelopmentTokenAuthenticator(string path, bool enabled)
{
    public async Task<AuthenticatedDevice?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (!enabled || !File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            foreach (var item in document.RootElement.GetProperty("devices").EnumerateArray())
            {
                if (item.TryGetProperty("revoked", out var revoked) && revoked.GetBoolean()) continue;
                if (!item.TryGetProperty("tokenSha256", out var stored) || !item.TryGetProperty("deviceId", out var id)) continue;
                byte[] candidate; try { candidate = Convert.FromHexString(stored.GetString() ?? ""); } catch (FormatException) { continue; }
                if (CryptographicOperations.FixedTimeEquals(digest, candidate)) return new(id.GetString()!);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        return null;
    }
}

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
        try { bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); return true; }
        catch (FormatException) { return false; }
    }
}

public sealed record DeviceCredentialSummary(string DeviceId, bool Revoked, DateTimeOffset UpdatedUtc);
public sealed record AuthenticatedDevice(string DeviceId);
