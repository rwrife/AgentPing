# Windows packaging

`AgentPing.wxs` harvests the complete architecture-specific publish tree, including the companion, `bridge/`, and offline troubleshooting documentation. CI pins WiX and builds separate x64 and arm64 MSIs.

The configuration is signing-ready, not signed. A release operator must Authenticode-sign the executable and MSI and verify both signatures before publication. CI intentionally uploads unsigned architecture-specific publish trees.

Startup-at-login is opt-in. Credential/settings files are not installer-owned, so uninstall leaves per-user bridge data in place. Upgrade/uninstall behavior has not been exercised in a Windows VM.
