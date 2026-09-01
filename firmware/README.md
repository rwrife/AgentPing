# AgentPing display firmware

This directory contains the reproducible ESP-IDF/PlatformIO firmware for the **Waveshare ESP32-C6-Touch-AMOLED-1.64**. It is a thin authenticated protocol-v1 display client: provider credentials and provider actions remain on the PC.

## Implemented vertical slice

- CO5300 280 × 456 AMOLED over 80 MHz QSPI, including the panel's 0x14 X offset and DMA-completion-driven LVGL flushing
- FT6146 touch controller over I²C using its FT3168-compatible register protocol, with LVGL pointer input and bounded coordinate-only serial reports
- LVGL status surface for disconnected, idle, active/running, waiting for input, completed, and failed/error states
- negotiated approve/deny/cancel/acknowledge/reply controls, an on-device reply keyboard, and required second-tap confirmation for destructive approval
- redundant icon + text + color status cues, dark high-contrast styling, 65% brightness, and periodic pixel shifting for AMOLED burn-in mitigation
- USB-serial provisioning into NVS with no compiled credentials and no secret-bearing logs
- physical BOOT-button factory reset (hold for three seconds during boot)
- Wi-Fi station mode with WPA2-or-better authentication threshold and protected management-frame capability
- RFC1918-literal-only `wss://.../ws` endpoint policy, exact provisioned self-signed bridge leaf certificate as TLS trust anchor, and Bearer device authentication
- protocol-v1 capability negotiation, bounded multi-chunk and multi-frame text-message assembly, strict JSON/payload/action validation, contiguous connection ordering, reconnect replay/snapshot handling, persistent durable resume checkpoints, heartbeats, and 1–60 second exponential backoff with jitter
- host-native tests for parsing, state reduction, action preparation, replay snapshots, fail-closed behavior, endpoint policy, and backoff bounds

An action is bound to the displayed attention ID, source message ID, revision, and deadline. Approval includes a SHA-256 digest of the canonical displayed title/body; destructive approval is not serialized until a second explicit tap. Reply text is bounded to 512 Unicode characters and omitted from logs. The device shows success only after the bridge echoes the committed action. These controls are compile/host-tested but not physically bench-tested.

## Pinned toolchain and source evidence

| Item | Pin / evidence |
|---|---|
| PlatformIO Core | `6.1.18` (`requirements-ci.txt`) |
| Platform | `espressif32@6.12.0` |
| ESP-IDF | `5.5.0` supplied by the pinned platform |
| LVGL | `9.3.0` in `src/idf_component.yml` and `dependencies.lock` |
| CO5300 driver | Espressif component `2.0.3` |
| WebSocket client | Espressif component `1.5.0` |
| Board evidence | Waveshare repository commit [`b90e28c953c1fc882258fa8dbd56b7706bc888b7`](https://github.com/waveshareteam/ESP32-C6-Touch-AMOLED-1.64/commit/b90e28c953c1fc882258fa8dbd56b7706bc888b7), Apache-2.0 |
| Product page | [Waveshare documentation](https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64), checked 2026-08-26 |

The current product documentation and schematic specify **CO5300 + FT6146**, LCD reset GPIO20, touch interrupt GPIO1, QSPI CS/clock/data GPIO10/11/4/5/7/19, and touch I²C SDA/SCL GPIO18/8. Waveshare's older `Arduino-V3.2.0/06_LVGL_Test` example instead uses SH8601 and reset GPIO21; it is a different/legacy revision and is not used here. See [`THIRD_PARTY.md`](THIRD_PARTY.md).

## Build and host tests

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt \
  --requirement protocol/requirements-ci.txt
firmware/tests/run_host_tests.sh
python3 protocol/validate.py
platformio run -d firmware
```

Expected static result: `protocol_core: all tests passed`, protocol fixture validation passes, and PlatformIO writes the ESP32-C6 images under `firmware/.pio/build/waveshare_esp32_c6_touch_amoled_1_64/`.

A successful host test and firmware compile do **not** prove that a physical panel, touch controller, radio, TLS session, or bridge connection works. Follow [`BRINGUP.md`](BRINGUP.md) on real hardware and record results separately.

## Provisioning

Provisioning is intentionally out of band. With no valid settings, the device shows **PROVISIONING REQUIRED** and reads one compact JSON object from USB serial at 115200 baud:

```json
{
  "ssid": "PRIVATE_WIFI_SSID",
  "wifiPassword": "REDACTED",
  "wssUrl": "wss://192.168.1.20:8743/ws",
  "deviceId": "display-desk-1",
  "deviceToken": "REDACTED_FULL_ENTROPY_DEVICE_TOKEN",
  "serverCertificatePem": "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----\n",
  "unixTime": 1787774400
}
```

Rules:

- `wssUrl` must use an RFC1918 IPv4 literal and exact `/ws` path. DNS names, public addresses, HTTP, userinfo, fragments, and query strings fail closed.
- `serverCertificatePem` is the exact self-signed **leaf** certificate provisioned by the bridge, not a public/root CA bundle. This makes the configured trust anchor the certificate pin; its subject alternative names must include the provisioned IP literal.
- the token must be 32–512 printable non-separator characters; it is sent only in the WebSocket Authorization header.
- unknown/missing fields reject the entire document before NVS writes.
- accepted enrollment is committed as one versioned, checksummed NVS blob. Resume and clock records carry that enrollment's random generation, so stale records from a prior pairing are ignored after replacement.
- logs never print the SSID, Wi-Fi password, token, certificate, reply text, or received protocol payload.
- the initial UTC time permits certificate validation; SNTP refresh runs after Wi-Fi association and a checkpoint is retained for restart fallback.

The bridge still binds to loopback by default. Its enrollment endpoint and Windows companion require an operator-configured RFC1918 HTTPS/WSS Kestrel endpoint and matching leaf-certificate fingerprint before a pairing window can open; AgentPing does not generate that listener or certificate automatically. Do not expose the checked-in HTTP listener directly. Real WSS interoperability remains physical bring-up evidence, not a CI claim.

## Credential storage limitation

Development builds store the Wi-Fi password, device token, and pinned certificate in the device's NVS partition. They are not logged, but the default CI image does **not** enable ESP32 flash encryption or encrypted NVS. Treat development boards as secret-bearing devices and erase them before disposal. Production provisioning must enable Secure Boot, flash encryption, and encrypted NVS with per-device keys before field deployment; this repository does not claim those fuses or production settings.

Inbound WebSocket text messages may arrive as driver-buffer chunks and/or RFC 6455 continuation frames. The firmware assembles both forms under one 16 KiB aggregate bound. Unexpected continuation order, interleaved data messages, binary data, invalid metadata names (including secret-bearing names), and oversized messages close the connection without applying partial state.

## Recovery

Hold **BOOT** continuously for three seconds during startup. The firmware erases only the `agentping` NVS namespace and reboots into serial provisioning. See [`BRINGUP.md`](BRINGUP.md) for flash recovery and expected serial/display states.
