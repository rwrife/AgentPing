using System.Security.Cryptography;

namespace AgentPing.Bridge.Security;

public sealed class PairingWindowService(TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _secret;
    private DateTimeOffset _expiresUtc;
    private int _attemptsRemaining;

    public async Task<PairingWindowOpened> OpenAsync(TimeSpan requestedLifetime, CancellationToken cancellationToken = default)
    {
        var lifetime = requestedLifetime <= TimeSpan.Zero || requestedLifetime > TimeSpan.FromMinutes(5)
            ? TimeSpan.FromMinutes(5) : requestedLifetime;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ClearSecret();
            _secret = RandomNumberGenerator.GetBytes(32);
            _expiresUtc = timeProvider.GetUtcNow() + lifetime;
            _attemptsRemaining = 5;
            return new PairingWindowOpened(Base64Url.Encode(_secret), _expiresUtc, _attemptsRemaining);
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { ClearSecret(); }
        finally { _gate.Release(); }
    }

    public async Task<PairingWindowStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ExpireIfNeeded();
            return new(_secret is not null, _secret is null ? null : _expiresUtc, _secret is null ? 0 : _attemptsRemaining);
        }
        finally { _gate.Release(); }
    }

    public async Task<byte[]?> EnrollAsync(string suppliedSecret, string deviceId, DeviceCredentialManager credentials, CancellationToken cancellationToken = default)
    {
        DeviceCredentialManager.ValidateDeviceId(deviceId);
        if (!Base64Url.TryDecode(suppliedSecret, out var supplied) || supplied.Length != 32) return null;
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ExpireIfNeeded();
                if (_secret is null || _attemptsRemaining <= 0) return null;
                _attemptsRemaining--;
                if (!CryptographicOperations.FixedTimeEquals(_secret, supplied))
                {
                    if (_attemptsRemaining == 0) ClearSecret();
                    return null;
                }

                // The credential write is atomic and must finish before the one-use secret is consumed or returned.
                var token = await credentials.IssueAsync(deviceId, cancellationToken);
                ClearSecret();
                return token;
            }
            finally { _gate.Release(); }
        }
        finally { CryptographicOperations.ZeroMemory(supplied); }
    }

    private void ExpireIfNeeded()
    {
        if (_secret is not null && timeProvider.GetUtcNow() >= _expiresUtc) ClearSecret();
    }
    private void ClearSecret()
    {
        if (_secret is not null) CryptographicOperations.ZeroMemory(_secret);
        _secret = null; _expiresUtc = default; _attemptsRemaining = 0;
    }
}

public sealed record PairingWindowOpened(string EnrollmentSecret, DateTimeOffset ExpiresUtc, int AttemptsRemaining);
public sealed record PairingWindowStatus(bool Open, DateTimeOffset? ExpiresUtc, int AttemptsRemaining);
