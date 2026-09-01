using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AgentPing.Companion.Core;

public static class ListenerPolicy
{
    public static bool IsAllowed(IPAddress address, bool lanEnabled, bool tlsEnabled)
    {
        if (IPAddress.IsLoopback(address)) return !lanEnabled || tlsEnabled;
        if (!lanEnabled || !tlsEnabled || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
}

public static class ManagementAccessHeader
{
    public const string Name = "X-AgentPing-Management";
    public const string Value = "companion-v1";
}

public sealed class BridgeManagementClient
{
    private readonly HttpClient _httpClient;

    public BridgeManagementClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.Contains(ManagementAccessHeader.Name))
        {
            _httpClient.DefaultRequestHeaders.Add(ManagementAccessHeader.Name, ManagementAccessHeader.Value);
        }
    }

    public async Task<BridgeManagementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<BridgeManagementStatus>("management/status", cancellationToken)
        ?? throw new InvalidDataException("Bridge returned an empty status response.");

    public async Task<PairingProvisioningBundle> OpenPairingAsync(PairingConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.Validate();
        using var response = await _httpClient.PostAsJsonAsync("management/pairing-window", new
        {
            tlsEndpoint = configuration.TlsEndpoint.AbsoluteUri,
            certificateSha256 = configuration.CertificateSha256,
            lifetimeSeconds = 300,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PairingProvisioningBundle>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Bridge returned an empty pairing response.");
    }

    public async Task CancelPairingAsync(CancellationToken cancellationToken = default) =>
        (await _httpClient.DeleteAsync("management/pairing-window", cancellationToken)).EnsureSuccessStatusCode();

    public async Task<string> RotateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"management/devices/{Uri.EscapeDataString(deviceId)}/rotate", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken: cancellationToken))?.Token
            ?? throw new InvalidDataException("Bridge returned an empty rotation response.");
    }

    public async Task RevokeAsync(string deviceId, CancellationToken cancellationToken = default) =>
        (await _httpClient.DeleteAsync($"management/devices/{Uri.EscapeDataString(deviceId)}", cancellationToken)).EnsureSuccessStatusCode();
}

public sealed record PairingConfiguration(IPAddress InterfaceAddress, Uri TlsEndpoint, string CertificateSha256, int DiscoveryPort = 8744)
{
    public void Validate()
    {
        if (!ListenerPolicy.IsAllowed(InterfaceAddress, true, true)) throw new InvalidOperationException("Select a private RFC1918 IPv4 interface.");
        if (TlsEndpoint.Scheme != Uri.UriSchemeHttps || !IPAddress.TryParse(TlsEndpoint.Host, out var endpointAddress) || !endpointAddress.Equals(InterfaceAddress))
            throw new InvalidOperationException("The TLS enrollment endpoint must use the selected private interface address.");
        if (CertificateSha256.Length != 64 || !CertificateSha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("A SHA-256 certificate fingerprint is required.");
    }
}

public sealed record BridgeManagementStatus(string Bridge, IReadOnlyList<AdapterSummary> Adapters, IReadOnlyList<AttentionSummary> RecentAttentions, IReadOnlyList<DeviceSummary> Devices, PairingStatus Pairing);
public sealed record DeviceSummary(string DeviceId, bool Revoked, DateTimeOffset UpdatedUtc);
public sealed record AdapterSummary(string Name, string DisplayName, bool Enabled, string Integration, string Capabilities);
public sealed record AttentionSummary(string AttentionId, string Title, string Category, DateTimeOffset ResponseDeadlineAt);
public sealed record PairingStatus(bool Open, DateTimeOffset? ExpiresUtc, int AttemptsRemaining);
public sealed record PairingProvisioningBundle(string TlsEndpoint, string CertificateSha256, string EnrollmentSecret, DateTimeOffset ExpiresUtc);
public sealed record DeviceTokenResponse(string Token);

public enum LogSensitivity { Public, Secret }
public sealed class SafeLogBuffer(int capacity)
{
    private readonly Queue<string> _entries = new();
    public void Add(string message, LogSensitivity sensitivity)
    {
        lock (_entries) { _entries.Enqueue(sensitivity == LogSensitivity.Public ? message : "[REDACTED]"); while (_entries.Count > capacity) _entries.Dequeue(); }
    }
    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        string content; lock (_entries) content = string.Join(Environment.NewLine, _entries);
        await destination.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }
}

public interface IStartupRegistration { Task<bool> IsRegisteredAsync(); Task RegisterAsync(); Task UnregisterAsync(); }
public sealed class MemoryStartupRegistration : IStartupRegistration
{
    private bool _enabled;
    public Task<bool> IsRegisteredAsync() => Task.FromResult(_enabled);
    public Task RegisterAsync() { _enabled = true; return Task.CompletedTask; }
    public Task UnregisterAsync() { _enabled = false; return Task.CompletedTask; }
}
public sealed class StartupPreference(IStartupRegistration registration)
{
    public Task<bool> IsEnabledAsync() => registration.IsRegisteredAsync();
    public async Task SetEnabledAsync(bool enabled) { if (enabled == await registration.IsRegisteredAsync()) return; if (enabled) await registration.RegisterAsync(); else await registration.UnregisterAsync(); }
}

public static class StartupLaunchMode
{
    public static bool IsBackground(IEnumerable<string> arguments) =>
        arguments.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
}
