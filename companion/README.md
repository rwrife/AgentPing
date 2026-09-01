# Windows companion

The companion is a Windows Forms `net10.0-windows` tray application backed by a cross-platform management/security core. The tray and keyboard-accessible management window cover bridge start/stop/status, paired-device rotation/revocation, adapter state, recent attentions, redacted log export, troubleshooting, and opt-in startup.

Security defaults are deliberate: the bridge remains on `127.0.0.1`; LAN mode must be explicitly enabled for one RFC1918 interface and TLS; wildcard, link-local, public, and plaintext LAN listeners are rejected. Pairing uses 256-bit random provisioning material in a single-use, expiring, attempt-bounded confirmation window. Each device receives a revocable 256-bit token. Server lookup uses an HMAC-SHA-256 digest with a protected random key, while recoverable keys/tokens use current-user DPAPI. Tokens, pairing material, and reply text are classified secret and replaced wholesale in exported logs. Rotation/revocation closes device sockets and clears queued actions.

The implementation does not move provider credentials into the device or companion state. The existing bridge protocol and provider endpoints remain unchanged.

Build on Windows with .NET SDK 10.0.300:

```powershell
dotnet restore AgentPing.sln --locked-mode
dotnet test companion/AgentPing.Companion.Core.Tests -c Release --no-restore
dotnet publish companion/AgentPing.Companion.Windows -c Release -r win-x64 --self-contained true --no-restore
```

CI also publishes an unsigned `win-arm64` tree. See `installer/README.md`; no signing claim is made. Physical device, LAN TLS interoperability, accessibility-tool, and installer upgrade/uninstall exercises remain manual release evidence.
