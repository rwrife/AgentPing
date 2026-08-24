using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPing.Bridge.Security;

namespace AgentPing.Bridge.Tests;

public sealed class DeviceTokenStoreTests
{
    [Fact]
    public async Task Digest_backed_active_token_authenticates_without_plaintext_storage()
    {
        var token = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(nameof(Digest_backed_active_token_authenticates_without_plaintext_storage))));
        var directory = Path.Combine(Path.GetTempPath(), $"agentping-token-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-tokens.json");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            devices = new[]
            {
                new { deviceId = "display-1", tokenSha256 = digest, revoked = false },
                new { deviceId = "display-revoked", tokenSha256 = digest, revoked = true },
            },
        }));
        var store = new DeviceTokenStore(path);

        var device = await store.AuthenticateAsync(token);

        Assert.Equal("display-1", device?.DeviceId);
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        Assert.Null(await store.AuthenticateAsync("wrong-token-with-enough-characters-000000"));
    }
}
