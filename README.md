<p align="center">
  <img src="assets/agentping-logo.png" alt="AgentPing logo" width="180" />
</p>

# AgentPing

**A tiny desk-side inbox for AI coding agents.**

AgentPing puts an animated 3D robot on a **Waveshare ESP32-C6 Touch AMOLED 1.64** (280 × 456 portrait). A Windows USB worker connects it to **Codex, Claude Code, and GitHub Copilot CLI**. Agent notifications show a provider logo and message; CLI and MCP controls let you play animations, pose limbs, and display custom icons.

The physical robot uses the `live3d_usb` firmware. This path needs **USB only**: no Wi-Fi, .NET bridge, tray app, Node.js, or simulator is required.

## Quick start: physical robot on Windows

### 1. Buy the board and prepare Windows

1. Get the **Waveshare ESP32-C6 Touch AMOLED 1.64** display board. Here is the
   [Amazon purchase link supplied for this project](https://www.amazon.com/dp/B0GTDXXYV8).
   Check that the selected listing variant matches this board; the firmware is
   configured for its 280 × 456 AMOLED display, not a bare ESP32 development board.
2. Have a USB **data** cable ready. The same connection powers the board and carries
   commands; a charge-only cable will not expose a serial port.
3. Install Git and Python 3.11 or newer (tested with 3.13), with their commands
   available on PATH. Open a new PowerShell terminal and check:

```powershell
git --version
python --version
```

Install and sign into your chosen agent CLIs separately. The USB Windows client
below is a background Python application controlled by `robot.cmd`; the optional
.NET tray application has separate instructions later in this guide.

### 2. Download the project and install the Windows USB client

```powershell
git clone https://github.com/rwrife/AgentPing.git
cd AgentPing
.\companion\setup-robot.ps1
```

Setup creates `.venv-robot` and installs the USB and MCP dependencies. Wait for
the `Ready` message, then check the launcher:

```powershell
.\companion\robot.cmd --help
```

If PowerShell blocks the setup script, perform the same installation directly:

```powershell
python -m venv .venv-robot
.\.venv-robot\Scripts\python.exe -m pip install -r tools/requirements-robot.txt
```

For an existing checkout, preserve local work, then use `git switch main` and
`git pull --ff-only`. Run setup again when Python dependencies change. All commands
below run from the repository root; virtual environment activation is unnecessary.

### 3. Build the firmware

Skip steps 3–4 if the board already has the current `live3d_usb` firmware.
Create a separate build environment, install the pinned dependencies, and compile:

```powershell
python -m venv .venv-firmware
.\.venv-firmware\Scripts\python.exe -m pip install -r firmware/requirements-ci.txt
.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb
```

Wait for `[SUCCESS]`. The first build downloads the ESP-IDF toolchain and can take
several minutes. The output is `firmware/.pio/build/live3d_usb/firmware.bin`.
Use **`-e live3d_usb`** explicitly; the default network firmware is a different
application. Model, textures, and animations are checked in, so FBX conversion,
Node.js, and the simulator are not needed. See [live renderer details](firmware/LIVE3D.md).

### 4. Connect and flash the board

1. Plug the board into the PC using the USB data cable.
2. Find its serial port in Device Manager under **Ports (COM & LPT)**, or run:

```powershell
.\.venv-robot\Scripts\python.exe -m serial.tools.list_ports
```

3. Replace `COM5` below with the reported port. Close serial monitors and stop an
   existing robot worker before flashing:

```powershell
# Only needed if a worker is already running:
.\companion\robot.cmd stop

.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb -t upload --upload-port COM5
```

4. Wait for the upload's `[SUCCESS]` message and automatic reset. The robot should
   play its startup sequence and enter idle with the initial connection message.
   If no port appears, try another data cable/USB port before retrying.

### 5. Start the Windows USB client and verify the display

```powershell
.\companion\robot.cmd start
.\companion\robot.cmd status
```

A successful live response includes `"ok": true` and `VIEW state=...`. Wait a few seconds after plugging in or resetting the device before checking status. One background worker owns USB for both notifications and MCP.

```powershell
.\companion\robot.cmd worker-status  # Worker health and delivery counters
.\companion\robot.cmd reset          # Return to idle
.\companion\robot.cmd stop           # Release USB before flashing
```

Start the worker again after restarting Windows; setup does not install an automatic startup service. The initial connection caption disappears after the first desktop signal.

Send the first message:

```powershell
.\companion\robot.cmd message "Hello world!"
```

Look for `PAL STATE attention OK` in the command response and a waving robot with
a message bubble on the device. The client runs in the background, so no app window
is expected. Continue with the [terminal demo](#terminal-demo), then
[wire up your CLI agents](#wire-up-the-cli-agents) for automatic notices and MCP.

## Terminal demo

From the repository root, start the worker with `.\companion\robot.cmd start`.
Run the examples **one at a time**, leaving time to see each state. Sending the
next command replaces the current animation or message.

### Startup, thinking, and attention

```powershell
.\companion\robot.cmd state boot
.\companion\robot.cmd message "Consulting my rubber duck..." --state thinking
.\companion\robot.cmd message "Your approval is needed!"
.\companion\robot.cmd message "The changes are ready. Please review the pull request." --state attention
.\companion\robot.cmd reset
```

### Error messages

These demonstrate the error animation, close-up, and message bubble without
causing an actual failure:

```powershell
.\companion\robot.cmd message "Build failed: please check the terminal." --state error
.\companion\robot.cmd message "Tests failed. I need your help!" --state error
.\companion\robot.cmd message "Connection lost. Please check the USB cable." --state error
.\companion\robot.cmd reset
```

### Custom icons and Teams demo

```powershell
.\companion\robot.cmd icon --file assets/icons/heart-48.bin --color "#ff8800" --message "Hello world!"
.\companion\robot.cmd icon --file assets/icons/teams-48.bin --color "#8b88ff" --message "Teams: Your meeting starts now"
.\companion\robot.cmd icon --file assets/icons/enghub.bin --color "#0078D4" --message "EngHub: Your review is needed"

# Error state overrides the requested green/purple and renders the icon red.
.\companion\robot.cmd icon --file assets/icons/heart-48.bin --color "#00ff00" --state error --message "Demo error: something needs fixing!"
.\companion\robot.cmd icon --file assets/icons/teams-48.bin --color "#8b88ff" --state error --message "Teams demo: unable to join the meeting."
.\companion\robot.cmd reset
```

The Teams icon is a demo graphic; these commands do not connect to Teams.

### Dances and limb controls

```powershell
.\companion\robot.cmd dance chicken
.\companion\robot.cmd dance twist
.\companion\robot.cmd dance twist
.\companion\robot.cmd reset
.\companion\robot.cmd joint right_shoulder --z -90 --duration-ms 1000
.\companion\robot.cmd joint head --y 25 --duration-ms 800
.\companion\robot.cmd reset
```

### Short automatic demo

Paste this whole block to cycle through thinking, attention, and a red Teams-style
error icon, with six seconds to view each notice, then finish in idle:

```powershell
.\companion\robot.cmd start
.\companion\robot.cmd message "Thinking through the next step..." --state thinking
Start-Sleep -Seconds 6
.\companion\robot.cmd message "Ready for your review!" --state attention
Start-Sleep -Seconds 6
.\companion\robot.cmd icon --file assets/icons/teams-48.bin --color "#8b88ff" --state error --message "Demo error: please check the terminal."
Start-Sleep -Seconds 6
.\companion\robot.cmd reset
```

Messages and custom icons dismiss after 30 seconds and return to idle. Error icons are always red, overriding the requested color. Manual joint poses hold until reset or another state; offsets are relative to the captured animation pose. These move the rendered skeleton. Full command and bitmap details: [robot controls](docs/robot-control.md).

## Wire up the CLI agents

| Connection | Purpose | Setup |
| --- | --- | --- |
| Notification hooks | Automatically show permission/input requests, completion, and supported errors | Install hooks below |
| MCP tools | Let an agent deliberately show messages, dance, move joints, or send icons | Register MCP in each client |

Both share the USB worker. MCP registration alone does not install notification hooks. Hooks notify you; they do not approve, deny, or resume agent tasks.

Automatic thinking notices are limited to once every two minutes across all
agents. Repeats are dropped so the message can dismiss after 30 seconds and return
to idle. Attention and error notices still get through immediately. Explicit
CLI/MCP demo commands bypass this cooldown.

### Automatic notifications

Preview the configuration paths, then install the additive hooks for all three providers:

```powershell
.\.venv-robot\Scripts\python.exe tools/install_usb_hooks.py
.\.venv-robot\Scripts\python.exe tools/install_usb_hooks.py --apply
.\companion\robot.cmd start
```

The installer preserves unrelated handlers and backs up existing settings under `%USERPROFILE%\.agentping\usb\backups`.

| Agent | Configuration written | Events wired |
| --- | --- | --- |
| Codex | `%USERPROFILE%\.codex\hooks.json` | `PermissionRequest`, `Stop` |
| Claude Code | `%USERPROFILE%\.claude\settings.json` | `PermissionRequest`, `Notification`, `Stop`, `StopFailure` |
| Copilot CLI | `%USERPROFILE%\.copilot\hooks\agentping-usb.json` | `userPromptSubmitted`, `permissionRequest`, `postToolUse`, `notification`, `awaitingUserInput`, `agentStop`, `errorOccurred` |

Restart your CLI sessions after installation. **In Codex, open `/hooks` and review/trust the AgentPing entries**; untrusted hooks are skipped. Rerun the installer after moving the checkout, replacing its Python environment, or updating the hook implementation.

Run a normal task in each agent, then check `robot.cmd worker-status` for an incremented provider delivery counter and inspect the display. Claude and Copilot live completion hooks were verified during bring-up. Codex's injected permission event was verified, but live Codex hook delivery still requires review/activation and validation in your session. See [notification details and tested versions](docs/usb-notifications.md).

For a **synthetic pipeline test**, not proof of a live agent event:

```powershell
'{}' | .\.venv-robot\Scripts\python.exe tools/agentping_usb_notifications.py hook --provider codex --event PermissionRequest
Start-Sleep -Seconds 2
.\companion\robot.cmd worker-status
```

### MCP registration in each CLI

**Copilot CLI on Windows:** this repository includes [`.github/mcp.json`](.github/mcp.json).
After installing the desktop tools, launch Copilot from the repository root:

```powershell
.\companion\robot.cmd start
copilot mcp list
copilot
```

Accept the repository trust prompt, then use `/mcp show agentping` to inspect the
connection. The configuration uses the relative `companion\robot.cmd` launcher,
so start Copilot in the repository root. No user-level registration is needed
for this checkout. The USB worker must already be running. Use the registration
commands below if you want the robot available from other repositories too.

With the worker running, resolve the checkout paths in PowerShell:

```powershell
$robotPython = (Resolve-Path .\.venv-robot\Scripts\python.exe).Path
$robotServer = (Resolve-Path .\tools\agentping_robot.py).Path
```

Run the registration command for each installed client you want to use:

```powershell
# Codex
codex mcp add agentping -- "$robotPython" "$robotServer" mcp
codex mcp list

# Claude Code: available across your projects
claude mcp add --transport stdio --scope user agentping -- "$robotPython" "$robotServer" mcp
claude mcp list

# GitHub Copilot CLI
copilot mcp add agentping -- "$robotPython" "$robotServer" mcp
copilot mcp list
```

Reconnect/restart the agent session and inspect its MCP tools. If an `agentping` entry exists, inspect/update it instead of registering a duplicate. For older Copilot CLI versions without `copilot mcp add`, use interactive `/mcp add`: local/stdio, the Python executable above, and the script path plus `mcp` as arguments.

Official references: [Codex MCP](https://developers.openai.com/codex/mcp), [Claude Code MCP](https://code.claude.com/docs/en/mcp), and [Copilot CLI MCP](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers). Command syntax was also checked against the installed CLIs.

For another MCP client, configure a **stdio** server with the Python executable as its command and `[absolute-script-path, "mcp"]` as its two arguments. A complete JSON example is in [robot-control.md](docs/robot-control.md#mcp-configuration). The client launches MCP; start the USB worker separately.

Available tools: `robot_status`, `robot_message`, `robot_animation`, `robot_dance`, `robot_move_joint`, `robot_icon`, and `robot_reset`.

Example demo prompts:

> Use AgentPing to show "Hello world!", then do the chicken dance.

> Reset the robot, turn its head 25 degrees on Y over 800 milliseconds, then reset it again.

> Show "Waiting for your approval" on the robot in the attention state.

> Use AgentPing's robot_message tool with state "error" to show "Build failed: please check the terminal." Leave it visible.

> Read assets/icons/teams-48.bin as hexadecimal and use AgentPing's robot_icon tool with color "#8b88ff", state "error", and message "Teams demo: unable to join the meeting." The device should show the icon in red.

> Use AgentPing's robot_reset tool to clear the demo and return to idle.

### Verify and troubleshoot

```powershell
# Software checks; no board required
.\.venv-robot\Scripts\python.exe -m unittest discover -s tools/tests

# MCP-to-device smoke test; changes poses/messages and ends in idle
.\.venv-robot\Scripts\python.exe tools/check_robot_mcp.py
```

- **USB unavailable/no acknowledgment:** check the data cable and USB port, close serial monitors, then run `robot.cmd stop`. Reconnect or reset the board, wait for boot, and run `robot.cmd start` followed by `robot.cmd status`. The worker finds the board by USB VID/PID, so it does not matter which port it is plugged into.
- **MCP tools missing:** check the registered absolute paths, reconnect the client, and confirm `.venv-robot` exists. MCP speaks a machine protocol over stdio; it does not present a terminal UI.
- **MCP works but automatic notices do not:** check hook installation and provider-session reload; review Codex `/hooks`. Compare delivery counters before/after a real event. Identical provider events can be coalesced for three seconds.
- **Firmware upload fails:** stop the worker and other serial clients, then flash `live3d_usb` on the correct port.

USB rendering, CLI/MCP control, provider logos, red error icons, and timed return to idle have been tested on the physical device. Notifications replace the visible notice; there is no on-device queue or approval-response flow.

## Forward Windows notifications

The Windows companion can send new notifications from selected apps to the USB
robot, including the app's logo and message. Open **Windows notifications** in the
packaged companion to grant access and select apps. Messages show for 30 seconds,
then the robot returns to idle.

See [Windows notification setup](docs/windows-notifications.md) for the Windows 11
package build/install commands, permission setup, and testing steps. The USB host
must be running; the network bridge is not required.

## Optional network bridge and tray application

### Build and launch the Windows tray app

This is the separate .NET management UI for the network bridge. It does not start
or replace the USB robot worker above. For the USB demo, use `robot.cmd`.

1. Install a **stable .NET 10.0 SDK**. `global.json` selects the latest installed
   10.0 feature band or patch (10.0.100 or newer).
   Confirm it appears in `dotnet --list-sdks`.
2. From the repository root, restore the solution and publish the app and its
   companion bridge together:

```powershell
dotnet restore AgentPing.sln --locked-mode
dotnet publish companion/AgentPing.Companion.Windows/AgentPing.Companion.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64/app
dotnet publish bridge/AgentPing.Bridge/AgentPing.Bridge.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64/app/bridge
```

3. Launch the published application:

```powershell
Start-Process -FilePath .\artifacts\win-x64\app\AgentPing.Companion.exe -WindowStyle Normal
```

4. Use its management window/tray icon for bridge controls. For ARM64 Windows,
   replace `win-x64` with `win-arm64` in all publish and launch paths.
   Keep the complete output folder, including `bridge/`, together when moving it.

Publishing self-contained includes the .NET runtime. These local builds are
unsigned. Network device pairing requires additional TLS/provisioning setup;
see [companion details](companion/README.md) and
[Windows troubleshooting](docs/windows-troubleshooting.md).

### Bridge

- ASP.NET Core 10 executable with structured JSON console logging
- loopback-only default listener at `http://127.0.0.1:8742`
- `GET /health` and non-secret protocol-v1 `GET /api/status`
- strict, 16 KiB-bounded `POST /api/events` and `POST /api/attentions` ingestion
- deterministic session/attention state, contiguous ingress ordering, idempotency, bounded history, transactional atomic persistence, stale-session handling, and restart recovery
- digest-authenticated protocol-v1 `/ws` capability negotiation, heartbeat validation, reconnect replay, and live state fan-out
- integration tests, process-level smoke coverage, and a reproducible Docker image build
- default-off, loopback-only adapters for Codex CLI, Claude Code, and Copilot CLI hooks, with bounded secret-redacted mapping, durable approve/deny outcomes, and synthetic fixtures

### Windows companion

- Windows Forms tray controls and live bridge/device/adapter/attention status
- explicit private-interface TLS pairing, bounded discovery, token rotation/revocation, and redacted log export
- current-user DPAPI credential protection, opt-in startup, high-DPI/accessibility metadata, and `.resx` localization extension points
- signed-ready WiX packaging plus explicitly unsigned `win-x64` and `win-arm64` CI artifacts

### Firmware

- pinned PlatformIO 6.1.18 / ESP-IDF 5.5.0 ESP32-C6 project with locked managed components
- compile-tested CO5300 AMOLED, FT6146 touch, and LVGL initialization from pinned Waveshare schematic/BSP evidence
- accessible disconnected/idle/running/waiting/completed/error UI with burn-in movement plus touch approve/deny/cancel/acknowledge/reply controls and two-tap destructive confirmation
- strict host-tested protocol parser/action policy/state reducer, authenticated WSS capability/heartbeat/replay/action transport, persistent resume state, and bounded reconnect backoff
- USB-serial NVS provisioning and physical factory reset with no hardcoded network/provider credentials or secret-bearing logs

### Protocol contract

- JSON Schema Draft 2020-12 contract for all nine v1 message kinds
- bounded envelopes, revisioned/idempotent actions, version negotiation, and reconnect/resume rules
- LAN threat model and full-entropy token pairing/rotation/revocation design
- golden valid/fail-closed fixtures consumed by Python validation and bridge serialization tests

The network firmware described in this section has compile/host-test evidence; USB robot validation does not validate its Wi-Fi, touch actions, or network pairing. The bridge implements protected credential lifecycle and TLS-only enrollment; operators must separately configure the RFC1918 Kestrel HTTPS/WSS endpoint and certificate. Provider permission decisions were not exercised with live provider accounts.

## Repository layout

```text
bridge/       ASP.NET Core bridge and tests
firmware/     ESP32-C6 PlatformIO firmware, native logic tests, and bring-up guide
protocol/     shared machine-readable protocol schema, fixtures, and validator
hardware/     hardware scope and future editable KiCad sources
docs/         architecture and protocol status
integration/  process-level integration tooling
scripts/      canonical local verification command
```

## Optional Pixel Pal portrait simulator

Preview the imported Meshy FBX and dynamic face states locally at the device's
280 × 456 portrait resolution. Run `npm ci` then `npm run dev` from `simulator/`
and open `http://127.0.0.1:5173`. See [simulator/README.md](simulator/README.md)
for controls, validation, asset provenance, and the strategy for moving to hardware.

## Build and test the bridge

Requires the .NET 10 SDK.

```bash
dotnet restore AgentPing.sln --locked-mode
dotnet build AgentPing.sln --configuration Release --no-restore
dotnet test AgentPing.sln --configuration Release --no-build
./integration/smoke-bridge.sh
```

Run the service:

```bash
dotnet run --project bridge/AgentPing.Bridge
curl http://127.0.0.1:8742/health
curl http://127.0.0.1:8742/api/status
```

Expected health response: `Healthy`. The status JSON identifies `agentping-bridge`, reports `status: ok`, uses protocol `apiVersion: 1.0`, and exposes only non-secret session/attention/history counts plus the latest server sequence. See [`bridge/README.md`](bridge/README.md) for ingestion, authenticated WebSocket, persistence, and configuration details.

## Build the firmware

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt
python3 -m pip install --requirement protocol/requirements-ci.txt
firmware/tests/run_host_tests.sh
python3 protocol/validate.py
platformio run -d firmware
```

These commands build the network firmware. CI compilation does not validate physical Wi-Fi, touch, or pairing behavior. Use the explicit `live3d_usb` instructions above for the USB robot. See [`firmware/README.md`](firmware/README.md).

## Container build

```bash
docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:local .
docker run --rm -p 127.0.0.1:8742:8742 agentping-bridge:local
```

The container listens on `0.0.0.0:8742` inside its isolated network namespace so Docker can forward traffic; the documented host publish is explicitly restricted to `127.0.0.1`.

## Architecture and protocol

The ESP32 remains a thin client; provider credentials stay on the PC. The bridge binds to loopback by default. LAN pairing requires a separately configured RFC1918 HTTPS/WSS endpoint and certificate; the default HTTP listener must never be exposed to the LAN.

- [`docs/architecture.md`](docs/architecture.md) describes current and planned boundaries.
- [`docs/protocol.md`](docs/protocol.md) specifies protocol v1, secure pairing, compatibility, ordering, resume, and fail-closed action rules.
- [`docs/generated/protocol-v1-reference.md`](docs/generated/protocol-v1-reference.md) is generated directly from the canonical schema.
- [`docs/provider-adapters.md`](docs/provider-adapters.md) documents default-off Codex CLI, Claude Code, Copilot CLI, and manual/test hook ingestion.
- [`docs/release/mvp-release-runbook.md`](docs/release/mvp-release-runbook.md), [`docs/release/security-privacy-review.md`](docs/release/security-privacy-review.md), [`docs/release/dependency-license-inventory.md`](docs/release/dependency-license-inventory.md), and [`docs/release/rollback-recovery.md`](docs/release/rollback-recovery.md) define MVP release gates and operations.
- [`hardware/README.md`](hardware/README.md) states the current hardware evidence and future KiCad scope.

## Contributing and security

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for exact verification commands and [`SECURITY.md`](SECURITY.md) for private vulnerability reporting and trust-boundary requirements.

## License

MIT. See [`LICENSE`](LICENSE).

## Desktop robot controls

Use the [USB CLI and MCP server](docs/robot-control.md) to show custom messages,
play animations and dances, and pose individual robot joints from the desktop.
