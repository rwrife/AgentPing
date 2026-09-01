using AgentPing.Companion.Core;
using Microsoft.Win32;

namespace AgentPing.Companion.Windows;

internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public Task<bool> IsRegisteredAsync() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return Task.FromResult(key?.GetValue("AgentPing") is string); }
    public Task RegisterAsync() { using var key = Registry.CurrentUser.CreateSubKey(RunKey); key.SetValue("AgentPing", $"\"{Environment.ProcessPath}\" --background", RegistryValueKind.String); return Task.CompletedTask; }
    public Task UnregisterAsync() { using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true); key?.DeleteValue("AgentPing", throwOnMissingValue: false); return Task.CompletedTask; }
}
