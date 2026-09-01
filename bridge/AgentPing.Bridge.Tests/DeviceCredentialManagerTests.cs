using System.Security.Cryptography;
using System.Text;
using AgentPing.Bridge.Security;
using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Tests;

public sealed class DeviceCredentialManagerTests
{
    [Fact]
    public async Task Issue_rotate_and_revoke_are_committed_and_never_store_plaintext()
    {
        var persistence = new MemoryCredentialPersistence();
        var lifecycle = new RecordingLifecycle();
        var manager = new DeviceCredentialManager(persistence, new XorProtector(), lifecycle);

        var token = await manager.IssueAsync("display-1");
        Assert.Equal(32, token.Length);
        Assert.DoesNotContain(Base64Url.Encode(token), persistence.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(token), persistence.Content, StringComparison.Ordinal);
        Assert.Equal("display-1", (await manager.AuthenticateAsync(Base64Url.Encode(token)))?.DeviceId);

        var rotated = await manager.RotateAsync("display-1");
        Assert.Null(await manager.AuthenticateAsync(Base64Url.Encode(token)));
        Assert.Equal("display-1", (await manager.AuthenticateAsync(Base64Url.Encode(rotated)))?.DeviceId);
        await manager.RevokeAsync("display-1");
        Assert.Null(await manager.AuthenticateAsync(Base64Url.Encode(rotated)));
        Assert.Equal(2, lifecycle.Invalidated.Count);
    }

    [Fact]
    public async Task Failed_commit_does_not_return_an_issued_token()
    {
        var manager = new DeviceCredentialManager(new FailingPersistence(), new XorProtector(), new RecordingLifecycle());
        await Assert.ThrowsAsync<IOException>(() => manager.IssueAsync("display-1"));
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> value) => value.ToArray().Select(value => (byte)(value ^ 0xa5)).ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> value) => Protect(value);
    }

    private sealed class RecordingLifecycle : IDeviceLifecycleInvalidator
    {
        public List<string> Invalidated { get; } = [];
        public Task InvalidateAsync(string deviceId, CancellationToken cancellationToken = default)
        { Invalidated.Add(deviceId); return Task.CompletedTask; }
    }

    private sealed class FailingPersistence : ICredentialPersistence
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteAtomicAsync(string content, CancellationToken cancellationToken = default) => throw new IOException("no commit");
    }
}
