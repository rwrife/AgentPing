# Waveshare ESP32-C6 Touch AMOLED 1.64 bring-up

This checklist separates reproducible software evidence from physical bench evidence. Do not convert an unchecked item into a claim.

## Evidence recorded for this revision

| Layer | Status | Evidence |
|---|---|---|
| Protocol schema/fixtures | automated | `python3 protocol/validate.py` |
| Native parser/reducer/backoff/endpoint tests | automated | `firmware/tests/run_host_tests.sh` |
| ESP32-C6 firmware compile/link | automated | `platformio run -d firmware` |
| Manufacturer pin/controller cross-check | static | Waveshare product page, schematic, and ESP-IDF example at `b90e28c…` |
| Flash/boot on target | **not performed in CI** | requires physical module |
| AMOLED pixels/brightness/burn-in movement | **not performed in CI** | requires visual inspection |
| FT6146 touch coordinates | **not performed in CI** | requires physical touch |
| Wi-Fi association/SNTP/RF | **not performed in CI** | requires local network and RF bench |
| Pinned-certificate WSS to AgentPing Bridge | **not performed in CI** | requires operator-configured RFC1918 TLS listener/certificate and physical device |

## Equipment

- Waveshare ESP32-C6-Touch-AMOLED-1.64 matching the current CO5300/FT6146 revision
- data-capable USB-C cable
- 5 V USB supply with adequate current
- private WPA2/WPA3 WLAN
- serial terminal at 115200 baud
- a development WSS endpoint only after its leaf certificate and revocable device token have been provisioned securely

Do not test against the older SH8601/GPIO21 Arduino example. Confirm the module/SKU and schematic revision before flashing.

## 1. Reproduce the software checks

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt \
  --requirement protocol/requirements-ci.txt
firmware/tests/run_host_tests.sh
python3 protocol/validate.py
platformio run -d firmware
```

Record exact command output and commit SHA. These are host/static checks only.

## 2. Flash and monitor

```bash
platformio run -d firmware --target upload
platformio device monitor -d firmware
```

If upload does not start:

1. Hold **BOOT**.
2. Tap **PWR/RESET** or reconnect USB.
3. Release **BOOT** after the serial downloader appears.
4. Retry upload.

Expected first unprovisioned display:

- near-black background
- close/disconnected symbol
- `DISCONNECTED`
- `Provisioning required`
- USB enrollment guidance

Expected non-secret serial evidence:

```text
CO5300 280x456 QSPI panel and FT6146 touch (FT3168 register protocol) initialized
configuration absent; send one compact JSON line over USB serial
```

Any panel-init, I²C, DMA, or allocation error is a failed bring-up. Capture the full serial log but redact any accidental credentials before sharing.

## 3. Validate display and burn-in behavior

- [ ] image orientation is portrait, 280 × 456
- [ ] content is not shifted/cropped (CO5300 X gap is 0x14)
- [ ] disconnected icon, title, and detail are legible at arm's length
- [ ] status remains identifiable without color (symbol and words are present)
- [ ] brightness is visibly below maximum
- [ ] after at least 60 seconds, content moves by a few pixels without clipping
- [ ] no tearing, stale bands, or corrupted partial flushes appear during repeated state updates

Photographs are useful evidence but do not replace serial/build records.

## 4. Validate touch

Touch the center and four near-corners without touching the bezel. Expected serial records contain coordinates only:

```text
touch down x=140 y=228
touch up
```

- [ ] X increases left to right and remains 0–279
- [ ] Y increases top to bottom and remains 0–455
- [ ] one physical contact produces one down and one up report
- [ ] no phantom touches occur for five minutes
- [ ] LVGL remains responsive during touch activity

The current firmware maps the negotiated action set to visible controls. Verify each control below; no unchecked item is a claim of physical validation.

## 5. Provision safely

Generate a compact JSON line as documented in [`README.md`](README.md). Transfer the full-entropy token and exact self-signed bridge leaf certificate over the local USB serial channel. Never paste provider credentials.

Expected serial result:

```text
configuration stored; secret fields were not logged; rebooting
```

Negative tests:

- [ ] public-IP, hostname, `ws://`, query-string, and non-`/ws` endpoints are rejected
- [ ] malformed/extra-field documents write nothing
- [ ] a token containing whitespace or separators is rejected
- [ ] serial output does not contain the password, token, certificate, or raw provisioning JSON

## 6. Validate Wi-Fi, time, TLS, and protocol (when bridge LAN TLS exists)

The current bridge does not yet provide the required LAN WSS listener/certificate issuer, so this section cannot be completed against the checked-in default bridge alone.

Once that prerequisite exists:

- [ ] Wi-Fi associates only with the intended private WLAN
- [ ] SNTP succeeds, or the persisted UTC checkpoint remains within certificate validity
- [ ] wrong certificate, hostname/public endpoint, revoked token, malformed capability, oversized frame, and sequence gap all fail closed
- [ ] a schema-valid message split across driver chunks and WebSocket continuation frames applies once; malformed continuation order and aggregate payloads over 16 KiB fail closed
- [ ] valid connection sends display capability at sequence 1
- [ ] bridge capability arrives at sequence 1 before operational state
- [ ] heartbeat cadence is 15 seconds and acknowledges the latest bridge connection sequence
- [ ] cable/AP interruption shows `DISCONNECTED`, then reconnects with bounded jittered backoff
- [ ] durable replay resumes after the stored server checkpoint
- [ ] replay-window miss clears state, consumes exactly the announced snapshot count, and persists the snapshot checkpoint
- [ ] idle, active/running, waiting, completed, and failed/error views match injected bridge state

Keep packet captures and logs secret-redacted. Do not bypass certificate validation to make the test pass.

## 7. Validate safe response flows (physical bench matrix)

For every row, record the displayed provider/session identity, allowed-action scope, UTC deadline, serial timestamps, bridge result, and provider-visible result. Use synthetic non-secret prompts. Do not test destructive commands against real data.

| Scenario | Procedure | Required result | Status |
|---|---|---|---|
| Approve, non-destructive | Inject an approval with `approve`; tap once | progress appears, bridge records once, provider receives allow, device shows completed only after bridge echo | [ ] |
| Approve, destructive | Inject `destructive: true`; tap approve once, then tap the changed **CONFIRM** control | first tap sends nothing; second tap sends one prompt-bound approval before the deadline | [ ] |
| Deny | Inject `deny`; tap deny | bridge records one denial and provider receives deny | [ ] |
| Cancel | Inject `cancel` without `deny`; tap cancel | bridge records `user_cancelled`; no provider action executes | [ ] |
| Acknowledge | Inject `acknowledge`; tap ACK | bridge records `acknowledged`; no approval is inferred | [ ] |
| Short reply | Enter a non-secret reply and tap reply | bridge returns the bounded text only to the manual/test waiter; logs and persisted state omit it | [ ] |
| Reply cancel | Enter text, press the keyboard cancel control | text is cleared and no action is queued | [ ] |
| Oversized/invalid reply | Attempt more than 512 Unicode characters or malformed input | UI/parser rejects it and sends nothing | [ ] |
| Duplicate tap/replay | Repeat the same action ID/message after reconnect | provider side effect occurs at most once; bridge returns the recorded outcome | [ ] |
| Stale revision | Change the attention revision before submitting | bridge returns `STALE_REVISION`; no action executes | [ ] |
| Deadline | Wait until the displayed UTC deadline, then tap | device rejects locally or bridge returns `ACTION_EXPIRED`; timeout never approves | [ ] |
| Disconnect before send | Remove Wi-Fi immediately before tapping | no success state appears; reconnect/reload is required | [ ] |
| Disconnect after commit | Break Wi-Fi after bridge commit but before device receives the echo, then reconnect/replay | durable outcome is returned without a second provider execution | [ ] |
| Bridge/network failure | Stop the bridge while a provider hook waits | relay emits an explicit deny before its 20-second local timeout; no payload or reply text is logged | [ ] |
| Replay-window miss | Reconnect behind retained history | reset snapshot restores the exact session/provider before actions become available | [ ] |

Copilot CLI command hooks have a provider-level fail-open timeout. Keep `timeoutSec` above AgentPing's 20-second relay timeout (the documented default is 30 seconds), and do not use AgentPing as the sole Copilot policy boundary. An externally killed or hung relay process cannot be made fail-closed by AgentPing.

## 8. Recovery and erase

To erase settings, hold **BOOT** continuously for three seconds during boot. Expected log:

```text
BOOT held; keep holding for three seconds to erase AgentPing settings
AgentPing NVS namespace erased; rebooting
```

The device should return to provisioning. For a fully secret-free retirement, also erase flash with the appropriate Espressif/PlatformIO erase target; verify the command for the connected port before executing it.

## Bring-up record template

```text
Date/UTC:
Device SKU / PCB revision:
Firmware commit:
USB supply/cable:
WLAN/AP:
Bridge commit/config:
Static checks:
Flash/boot:
Display:
Touch:
Wi-Fi/SNTP:
TLS/auth:
Reconnect/replay:
Factory reset:
Open failures/blockers:
Operator:
```
