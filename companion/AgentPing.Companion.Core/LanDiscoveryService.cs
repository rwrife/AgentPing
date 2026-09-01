using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AgentPing.Companion.Core;

public sealed record LanDiscoveryConfiguration(IPAddress InterfaceAddress, int DiscoveryPort, Uri TlsEndpoint, string CertificateSha256)
{
    public void Validate()
    {
        if (!ListenerPolicy.IsAllowed(InterfaceAddress, lanEnabled: true, tlsEnabled: true)) throw new InvalidOperationException("A private IPv4 interface is required.");
        if (DiscoveryPort is < 1024 or > 65535) throw new InvalidOperationException("Discovery port is invalid.");
        if (!TlsEndpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && !TlsEndpoint.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A TLS endpoint is required.");
        if (!IPAddress.TryParse(TlsEndpoint.Host, out var endpointAddress) || !endpointAddress.Equals(InterfaceAddress)) throw new InvalidOperationException("TLS endpoint must use the selected private interface literal.");
        if (CertificateSha256.Length != 64 || !CertificateSha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("A SHA-256 certificate fingerprint is required.");
    }
}

/// <summary>Explicit, pairing-window-only UDP discovery. It advertises only a TLS endpoint and certificate fingerprint.</summary>
public sealed class LanDiscoveryService(LanDiscoveryConfiguration configuration) : IAsyncDisposable
{
    private static readonly byte[] Probe = "agentping-discover-v1"u8.ToArray();
    private UdpClient? _udp;
    private CancellationTokenSource? _lifetime;
    private Task? _worker;

    public void Start()
    {
        if (_udp is not null) throw new InvalidOperationException("Discovery is already active.");
        configuration.Validate();
        _udp = new UdpClient(new IPEndPoint(configuration.InterfaceAddress, configuration.DiscoveryPort));
        _lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _worker = RunAsync(_udp, _lifetime.Token);
    }

    private async Task RunAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            service = "agentping",
            version = 1,
            endpoint = configuration.TlsEndpoint.AbsoluteUri,
            certificateSha256 = configuration.CertificateSha256.ToLowerInvariant(),
        });
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var request = await udp.ReceiveAsync(cancellationToken);
                if (request.Buffer.AsSpan().SequenceEqual(Probe)) await udp.SendAsync(response, request.RemoteEndPoint, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null) await _lifetime.CancelAsync();
        _udp?.Dispose();
        if (_worker is not null) { try { await _worker; } catch (ObjectDisposedException) { } }
        _lifetime?.Dispose(); _lifetime = null; _udp = null; _worker = null;
    }
}
