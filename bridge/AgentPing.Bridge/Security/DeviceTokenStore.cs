using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPing.Bridge.Security;

public sealed class DeviceTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _path;
    private readonly ILogger<DeviceTokenStore>? _logger;

    public DeviceTokenStore(string path, ILogger<DeviceTokenStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _logger = logger;
    }

    public async Task<AuthenticatedDevice?> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (token is null || token.Length is < 32 or > 512 || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var document = await JsonSerializer.DeserializeAsync<DeviceTokenDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            if (document?.Devices is null)
            {
                return null;
            }

            var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            foreach (var record in document.Devices)
            {
                if (record.Revoked
                    || string.IsNullOrWhiteSpace(record.DeviceId)
                    || string.IsNullOrWhiteSpace(record.TokenSha256))
                {
                    continue;
                }

                byte[] storedDigest;
                try
                {
                    storedDigest = Convert.FromHexString(record.TokenSha256);
                }
                catch (FormatException)
                {
                    continue;
                }

                if (storedDigest.Length == presentedDigest.Length
                    && CryptographicOperations.FixedTimeEquals(presentedDigest, storedDigest))
                {
                    return new AuthenticatedDevice(record.DeviceId);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(
                "Device credential store could not be read; authentication denied ({ExceptionType})",
                exception.GetType().Name);
        }

        return null;
    }

    private sealed class DeviceTokenDocument
    {
        public IReadOnlyList<DeviceTokenRecord> Devices { get; init; } = [];
    }

    private sealed class DeviceTokenRecord
    {
        public required string DeviceId { get; init; }
        public required string TokenSha256 { get; init; }
        public bool Revoked { get; init; }
    }
}

public sealed record AuthenticatedDevice(string DeviceId);
