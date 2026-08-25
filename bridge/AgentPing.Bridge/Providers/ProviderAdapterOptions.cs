namespace AgentPing.Bridge.Providers;

public sealed class ProviderAdapterOptions
{
    public ProviderSwitchOptions Manual { get; set; } = new();
    public ProviderSwitchOptions Codex { get; set; } = new();
    public ProviderSwitchOptions ClaudeCode { get; set; } = new();
    public ProviderSwitchOptions CopilotCli { get; set; } = new();
}

public sealed class ProviderSwitchOptions
{
    public bool Enabled { get; set; }
}
