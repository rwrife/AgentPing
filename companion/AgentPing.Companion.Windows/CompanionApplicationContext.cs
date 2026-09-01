using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using AgentPing.Companion.Core;

namespace AgentPing.Companion.Windows;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly MainForm _window;

    public CompanionApplicationContext(bool showWindow)
    {
        _window = new MainForm();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AgentPing", null, (_, _) => ShowWindow());
        menu.Items.Add("Start bridge", null, async (_, _) => await _window.StartBridgeAsync());
        menu.Items.Add("Stop bridge", null, (_, _) => _window.StopBridge());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Text = "AgentPing companion — bridge stopped",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        if (showWindow)
        {
            ShowWindow();
        }
    }

    private void ShowWindow() { _window.Show(); _window.Activate(); }
    protected override void ExitThreadCore() { _window.StopBridge(); _tray.Visible = false; _tray.Dispose(); _window.Dispose(); base.ExitThreadCore(); }
}

internal sealed class MainForm : Form
{
    private static readonly ResourceManager Strings = new(
        "AgentPing.Companion.Windows.Strings",
        typeof(MainForm).Assembly);
    private Process? _bridge;
    private readonly Label _bridgeStatus = new() { AutoSize = true, Text = TextFor("BridgeStopped", "Stopped"), AccessibleName = "Bridge status" };
    private readonly ListView _devices = List("Paired devices", "Device", "State");
    private readonly ListView _adapters = List("Provider adapter status", "Adapter", "State");
    private readonly ListView _attentions = List("Recent attentions", "Time", "Provider", "Summary");
    private readonly CheckBox _startup = new() { Text = "Start AgentPing when I sign in", AutoSize = true, TabIndex = 8 };
    private readonly SafeLogBuffer _logs = new(500);
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:8742/"), Timeout = TimeSpan.FromSeconds(3) };
    private readonly BridgeManagementClient _management;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 3000 };
    private LanDiscoveryService? _discovery;

    public MainForm()
    {
        _management = new BridgeManagementClient(_http);
        Text = TextFor("ApplicationTitle", "AgentPing Companion");
        AccessibleName = "AgentPing Companion management window";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Companion management sections" };
        tabs.TabPages.Add(BuildOverview());
        tabs.TabPages.Add(BuildDevices());
        tabs.TabPages.Add(BuildActivity());
        Controls.Add(tabs);
        var startupPreference = new StartupPreference(new RegistryStartupRegistration());
        Shown += async (_, _) => _startup.Checked = await startupPreference.IsEnabledAsync();
        _startup.CheckedChanged += async (_, _) => await startupPreference.SetEnabledAsync(_startup.Checked);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _refreshTimer.Start();
    }

    private TabPage BuildOverview()
    {
        var page = Page(TextFor("OverviewTab", "Overview"));
        page.Controls.Add(Row(new Label { Text = "Bridge", AutoSize = true }, _bridgeStatus,
            Button("Start", async (_, _) => await StartBridgeAsync()), Button("Stop", (_, _) => StopBridge())));
        page.Controls.Add(_adapters);
        page.Controls.Add(_startup);
        return page;
    }

    private TabPage BuildDevices()
    {
        var page = Page(TextFor("DevicesTab", "Devices & pairing"));
        var explanation = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Text = "LAN pairing is off by default. Enabling it requires a private IPv4 interface, TLS, a bounded single-use window, and confirmation here. Public interfaces are rejected." };
        page.Controls.Add(explanation);
        page.Controls.Add(Row(Button("Open pairing window…", async (_, _) => await ConfirmPairingAsync()), Button("Rotate token", async (_, _) => await RotateSelectedAsync()), Button("Revoke device", async (_, _) => await RevokeSelectedAsync())));
        page.Controls.Add(_devices);
        return page;
    }

    private TabPage BuildActivity()
    {
        var page = Page(TextFor("ActivityTab", "Activity & support"));
        page.Controls.Add(_attentions);
        page.Controls.Add(Row(Button("Export redacted logs…", async (_, _) => await ExportLogsAsync()), Button("Open troubleshooting guide", (_, _) => OpenTroubleshooting())));
        return page;
    }

    public async Task StartBridgeAsync()
    {
        if (_bridge is { HasExited: false }) return;
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "bridge", "AgentPing.Bridge.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("The packaged bridge executable is missing.", executable);
            _bridge = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true });
            _bridgeStatus.Text = "Starting…";
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(250);
                if (_bridge?.HasExited != false) break;
                try { using var response = await _http.GetAsync("health"); if (response.IsSuccessStatusCode) { await RefreshStatusAsync(); _logs.Add("Bridge health check succeeded.", LogSensitivity.Public); return; } } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            }
            throw new IOException("Bridge did not become healthy within five seconds.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _bridgeStatus.Text = "Failed to start";
            _logs.Add("Bridge start failed; details omitted.", LogSensitivity.Public);
            MessageBox.Show(this, "The bridge could not be started. See the troubleshooting guide and exported logs.", "AgentPing", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void StopBridge()
    {
        StopDiscoveryAsync().GetAwaiter().GetResult();
        if (_bridge is { HasExited: false }) { _bridge.Kill(entireProcessTree: true); _bridge.WaitForExit(5000); }
        _bridge?.Dispose(); _bridge = null; _bridgeStatus.Text = TextFor("BridgeStopped", "Stopped"); ClearViews();
    }

    private async Task ConfirmPairingAsync()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .Where(a => ListenerPolicy.IsAllowed(a, true, true)).Distinct().ToArray();
        if (addresses.Length == 0) { MessageBox.Show(this, "No active private RFC1918 IPv4 interface is available. Pairing remains closed.", "Pairing unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        using var dialog = new PairingSetupDialog(addresses);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PairingProvisioningBundle? bundle = null;
        LanDiscoveryService? candidate = null;
        try
        {
            var configuration = dialog.Configuration;
            await StopDiscoveryAsync();
            await VerifyTlsEndpointAsync(configuration);
            bundle = await _management.OpenPairingAsync(configuration);
            candidate = new LanDiscoveryService(new LanDiscoveryConfiguration(configuration.InterfaceAddress, configuration.DiscoveryPort, configuration.TlsEndpoint, configuration.CertificateSha256));
            candidate.Start();
            _discovery = candidate;
            candidate = null;
            MessageBox.Show(this, $"Provision over USB or another secure channel before {bundle.ExpiresUtc:u}:\n\nEndpoint: {bundle.TlsEndpoint}\nCertificate SHA-256: {bundle.CertificateSha256}\nEnrollment secret: {bundle.EnrollmentSecret}", "Single-use pairing bundle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshStatusAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or SocketException or AuthenticationException or FormatException or TaskCanceledException)
        {
            if (candidate is not null)
            {
                await candidate.DisposeAsync();
            }
            if (bundle is not null)
            {
                try { await _management.CancelPairingAsync(); } catch (Exception cleanup) when (cleanup is HttpRequestException or TaskCanceledException) { }
            }
            MessageBox.Show(this, "Pairing did not open. Verify that the bridge has a TLS certificate and is listening on the selected private endpoint.", "Pairing unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopDiscoveryAsync()
    {
        var discovery = _discovery;
        _discovery = null;
        if (discovery is not null)
        {
            await discovery.DisposeAsync();
        }
    }

    private async Task RotateSelectedAsync()
    {
        if (_devices.SelectedItems.Count != 1) { MessageBox.Show(this, "Select one paired device first.", "Rotate token"); return; }
        var id = _devices.SelectedItems[0].Text;
        if (MessageBox.Show(this, $"Rotate credentials for {id}? Its active connection and queued outbound actions will be invalidated.", "Confirm rotation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { var token = await _management.RotateAsync(id); MessageBox.Show(this, $"Provision this replacement token over USB or another secure channel:\n\n{token}", "Replacement device token"); await RefreshStatusAsync(); }
        catch (HttpRequestException) { MessageBox.Show(this, "The bridge rejected or could not complete rotation.", "Rotation failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task RevokeSelectedAsync()
    {
        if (_devices.SelectedItems.Count != 1) { MessageBox.Show(this, "Select one paired device first.", "Revoke device"); return; }
        var id = _devices.SelectedItems[0].Text;
        if (MessageBox.Show(this, $"Revoke {id}? This closes its active connection and cannot be undone without pairing again.", "Confirm revocation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _management.RevokeAsync(id); await RefreshStatusAsync(); } catch (HttpRequestException) { MessageBox.Show(this, "The bridge rejected or could not complete revocation.", "Revocation failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _management.GetStatusAsync(); _bridgeStatus.Text = "Running (management connected)";
            _devices.Items.Clear(); foreach (var d in status.Devices) _devices.Items.Add(new ListViewItem([d.DeviceId, d.Revoked ? "Revoked" : "Active"]));
            _adapters.Items.Clear(); foreach (var a in status.Adapters) _adapters.Items.Add(new ListViewItem([a.DisplayName, a.Enabled ? "Enabled" : "Disabled"]));
            _attentions.Items.Clear(); foreach (var a in status.RecentAttentions) _attentions.Items.Add(new ListViewItem([a.ResponseDeadlineAt.LocalDateTime.ToString("g"), a.Category, a.Title]));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException) { _bridgeStatus.Text = _bridge is { HasExited: false } ? "Process running; API unavailable" : "Stopped"; ClearViews(); }
    }
    private void ClearViews() { _devices.Items.Clear(); _adapters.Items.Clear(); _attentions.Items.Clear(); }
    private static async Task VerifyTlsEndpointAsync(PairingConfiguration configuration)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(configuration.InterfaceAddress, configuration.TlsEndpoint.Port);
        var matched = false;
        using var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
            matched = actual.Equals(configuration.CertificateSha256, StringComparison.OrdinalIgnoreCase);
            return matched;
        });
        await tls.AuthenticateAsClientAsync(configuration.TlsEndpoint.Host);
        if (!matched) throw new AuthenticationException("TLS certificate fingerprint mismatch.");
    }

    private async Task ExportLogsAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "Text log (*.log)|*.log", FileName = $"agentping-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await using var stream = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await _logs.ExportAsync(stream);
    }

    private static void OpenTroubleshooting()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "windows-troubleshooting.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static TabPage Page(string title) => new(title) { Padding = new Padding(12), AutoScroll = true };
    private static string TextFor(string key, string fallback) =>
        Strings.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
    private static FlowLayoutPanel Row(params Control[] controls) { var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true }; row.Controls.AddRange(controls); return row; }
    private static Button Button(string text, EventHandler handler) { var button = new Button { Text = text, AutoSize = true, UseVisualStyleBackColor = true }; button.Click += handler; return button; }
    private static ListView List(string name, params string[] columns)
    {
        var view = new ListView { AccessibleName = name, View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Top, Height = 180, MultiSelect = false };
        foreach (var column in columns) view.Columns.Add(column, 180);
        return view;
    }
}

internal sealed class PairingSetupDialog : Form
{
    private readonly ComboBox _address = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _endpoint = new() { Width = 360 };
    private readonly TextBox _fingerprint = new() { Width = 520 };
    public PairingConfiguration Configuration => new(IPAddress.Parse(_address.Text), new Uri(_endpoint.Text), _fingerprint.Text.Replace(":", "", StringComparison.Ordinal).Trim());
    public PairingSetupDialog(IPAddress[] addresses)
    {
        Text = "Secure pairing setup"; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(12);
        _address.Items.AddRange(addresses.Select(a => (object)a.ToString()).ToArray()); _address.SelectedIndex = 0;
        _endpoint.Text = $"https://{_address.Text}:8743/enroll"; _address.SelectedIndexChanged += (_, _) => _endpoint.Text = $"https://{_address.Text}:8743/enroll";
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        panel.Controls.AddRange([new Label { Text = "Private interface", AutoSize = true }, _address, new Label { Text = "TLS enrollment endpoint", AutoSize = true }, _endpoint, new Label { Text = "TLS certificate SHA-256 fingerprint", AutoSize = true }, _fingerprint]);
        var ok = new Button { Text = "Open five-minute window", DialogResult = DialogResult.OK, AutoSize = true }; panel.Controls.Add(ok); Controls.Add(panel); AcceptButton = ok;
    }
}
