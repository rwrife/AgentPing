using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Core;

public sealed class BridgeOptions
{
    public string PersistencePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentPing",
        "bridge-state.json");

    public string DeviceTokensPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentPing",
        "device-tokens.json");
    public bool AllowLegacyDevelopmentTokenFile { get; set; }

    public string DeviceCredentialsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentPing", "device-credentials.json");

    public int MaxHistory { get; set; } = ProtocolV1.MaxReplayWindowMessages;
    public int StaleSessionSeconds { get; set; } = 300;
    public int StaleSweepSeconds { get; set; } = 30;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PersistencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceTokensPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceCredentialsPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxHistory, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxHistory, ProtocolV1.MaxReplayWindowMessages);
        ArgumentOutOfRangeException.ThrowIfLessThan(StaleSessionSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(StaleSweepSeconds, 1);
    }
}
