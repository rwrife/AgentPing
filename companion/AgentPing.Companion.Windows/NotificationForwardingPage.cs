using System.Text.Json;
using AgentPing.Companion.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AgentPing.Companion.Windows;

internal sealed class NotificationForwardingPage : TabPage
{
    private sealed record AppChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record Preferences(bool Enabled, Dictionary<string, string> Apps);
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentping", "usb");
    private readonly CheckBox _enabled = new() { Text = "Forward Windows notifications to the robot", AutoSize = true };
    private readonly CheckedListBox _apps = new() { Dock = DockStyle.Fill, CheckOnClick = true, AccessibleName = "Apps allowed to forward notifications" };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(680, 0), Text = "Enable access, then select apps. The USB host must be running." };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly NotificationForwardingPolicy _policy = new();
    private Preferences _preferences = new(false, new());
    private bool _busy, _loading, _disposed;
    private int _generation;

    public NotificationForwardingPage() : base("Windows notifications")
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_enabled);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(680, 0), Text = "Apps appear here when they have a Windows notification. Only checked apps are forwarded. Existing notifications are skipped; new messages show for 30 seconds." });
        layout.Controls.Add(_apps);
        layout.Controls.Add(_status);
        Controls.Add(layout);
        try
        {
            if (File.Exists(SettingsPath)) _preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? _preferences;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _status.Text = "Could not load notification preferences. Select apps again."; }
        foreach (var app in _preferences.Apps) _apps.Items.Add(new AppChoice(app.Key, app.Value), true);
        _enabled.Checked = _preferences.Enabled;
        _enabled.CheckedChanged += async (_, _) => await ToggleAsync();
        _apps.ItemCheck += (_, e) =>
        {
            if (_loading) return;
            var app = (AppChoice)_apps.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _preferences.Apps[app.Id] = app.Name;
            else _preferences.Apps.Remove(app.Id);
            Save();
        };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    private string SettingsPath => Path.Combine(_root, "windows-notifications.json");
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_root);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_preferences));
            File.Move(temp, SettingsPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "Unable to save notification preferences."; }
    }

    private async Task ToggleAsync()
    {
        _generation++;
        _policy.Reset();
        _preferences = _preferences with { Enabled = _enabled.Checked };
        if (_enabled.Checked)
        {
            try
            {
                var access = await UserNotificationListener.Current.RequestAccessAsync();
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    _enabled.Checked = false;
                    _status.Text = "Notification access was not granted. Allow AgentPing in Windows Settings > Privacy & security > Notifications.";
                }
                else _status.Text = "Access granted. Select apps below; send a Windows notification if an app is missing.";
            }
            catch (Exception ex) { _enabled.Checked = false; _status.Text = $"Windows notification access is unavailable (0x{ex.HResult:X8}). Install the packaged companion using the notification setup script."; }
        }
        else _status.Text = "Windows notification forwarding is off.";
        Save();
    }

    private async Task PollAsync()
    {
        if (_busy || !_enabled.Checked || _disposed) return;
        _busy = true;
        var generation = _generation;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            {
                _status.Text = "Windows notification access is unavailable. Disable and re-enable forwarding to request access.";
                _policy.Reset();
                return;
            }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (_disposed || !_enabled.Checked || generation != _generation) return;
            var notices = new List<DesktopNotice>();
            var sources = new Dictionary<string, UserNotification>();
            foreach (var notification in notifications.OrderBy(n => n.CreationTime))
            {
                try
                {
                    var id = notification.AppInfo.AppUserModelId;
                    var name = notification.AppInfo.DisplayInfo.DisplayName;
                    if (id.StartsWith("AgentPing.Companion", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!_apps.Items.Cast<AppChoice>().Any(a => a.Id == id))
                    {
                        _loading = true;
                        try { _apps.Items.Add(new AppChoice(id, name), _preferences.Apps.ContainsKey(id)); }
                        finally { _loading = false; }
                    }
                    var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (binding is null) continue;
                    var text = string.Join(" ", binding.GetTextElements().Select(t => t.Text));
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var key = $"{id}:{notification.Id}:{notification.CreationTime.UtcTicks}";
                    notices.Add(new DesktopNotice(key, id, name, text));
                    sources[key] = notification;
                }
                catch (Exception) { /* A malformed or removed toast must not stop other apps. */ }
            }
            var fresh = _policy.TakeNew(notices, _preferences.Apps.Keys.ToHashSet(StringComparer.Ordinal));
            // Coalesce a burst to its newest notification instead of interrupting the robot repeatedly.
            if (fresh.LastOrDefault() is { } notice)
            {
                var icon = await ReadIconAsync(sources[notice.Key]);
                if (_disposed || !_enabled.Checked || generation != _generation || !_preferences.Apps.ContainsKey(notice.AppId)) return;
                var message = NotificationForwardingPolicy.RobotText(notice.AppName, notice.Text);
                await SendAsync(icon, message);
                if (!_disposed) _status.Text = $"Forwarded {notice.AppName} at {DateTime.Now:t}.";
            }
        }
        catch (Exception) { if (!_disposed) _status.Text = "Could not forward notification. Check Windows access and run companion\\robot.cmd start. Messages are not retried."; }
        finally { _busy = false; }
    }

    private static async Task<string?> ReadIconAsync(UserNotification notification)
    {
        try
        {
            using var input = await notification.AppInfo.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(128, 128)).OpenReadAsync();
            using var reader = new global::Windows.Storage.Streams.DataReader(input);
            if (input.Size > 4 * 1024 * 1024) return null;
            await reader.LoadAsync((uint)input.Size);
            var bytes = new byte[(int)input.Size]; reader.ReadBytes(bytes);
            using var stream = new MemoryStream(bytes);
            using var original = Image.FromStream(stream);
            using var bitmap = new Bitmap(48, 48);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                var scale = Math.Min(44f / original.Width, 44f / original.Height);
                var w = original.Width * scale; var h = original.Height * scale;
                graphics.DrawImage(original, (48-w)/2, (48-h)/2, w, h);
            }
            var mask = new byte[288];
            // Distinguish opaque tile backgrounds from transparent logo silhouettes.
            var background = bitmap.GetPixel(2, 2);
            var opaqueTile = background.A > 200;
            for (var y = 0; y < 48; y++) for (var x = 0; x < 48; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var contrast = Math.Abs(c.R-background.R)+Math.Abs(c.G-background.G)+Math.Abs(c.B-background.B);
                if (c.A >= 128 && (!opaqueTile || contrast > 100))
                { var bit = y * 48 + x; mask[bit / 8] |= (byte)(1 << (bit % 8)); }
            }
            return EnlargeIcon(mask);
        }
        catch (Exception) { return null; }
    }

    private static string? EnlargeIcon(byte[] mask)
    {
        bool Ink(int x, int y) => (mask[(y * 48 + x) / 8] & (1 << ((y * 48 + x) % 8))) != 0;
        var left = 48; var top = 48; var right = -1; var bottom = -1;
        for (var y = 0; y < 48; y++) for (var x = 0; x < 48; x++)
        {
            if (!Ink(x, y)) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left) return null;
        // Windows logos often contain substantial transparent padding. Fit the
        // visible mark, preserving its aspect ratio, inside a one-pixel margin.
        var width = right - left + 1; var height = bottom - top + 1;
        var scale = 46.0 / Math.Max(width, height);
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        var dx = (48 - targetWidth) / 2; var dy = (48 - targetHeight) / 2;
        var result = new byte[288];
        for (var y = 0; y < targetHeight; y++) for (var x = 0; x < targetWidth; x++)
        {
            if (!Ink(left + Math.Min(width - 1, x * width / targetWidth),
                     top + Math.Min(height - 1, y * height / targetHeight))) continue;
            var bit = (y + dy) * 48 + x + dx;
            result[bit / 8] |= (byte)(1 << (bit % 8));
        }
        return Convert.ToHexString(result);
    }

    private async Task SendAsync(string? icon, string message)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_root, "commands");
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder, "*.json").Take(32).Count() >= 32) throw new IOException("Queue full");
        var path = Path.Combine(folder, id + ".json");
        var result = Path.Combine(_root, "results", id + ".json");
        object args = icon is null ? new { state = "attention", message } : (object)new { state = "attention", message, data_hex = icon, color = "#50DFFF" };
        var payload = new { action = icon is null ? "state" : "icon", args, expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0 + 5 };
        try
        {
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(payload));
            File.Move(path + ".tmp", path);
            for (var attempt = 0; attempt < 80; attempt++)
            {
                await Task.Delay(100);
                if (!File.Exists(result)) continue;
                using var response = JsonDocument.Parse(await File.ReadAllTextAsync(result));
                if (!response.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("Robot rejected command");
                return;
            }
            throw new IOException("USB host did not acknowledge");
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); File.Delete(result); }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
