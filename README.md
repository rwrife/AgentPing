<p align="center">
  <img src="assets/agentping-logo.png" alt="AgentPing logo" width="180" />
</p>

# AgentPing

**A tiny desk-side inbox for AI coding agents.**

AgentPing is a buildable foundation for a Windows/.NET bridge and a Waveshare ESP32-C6 Touch AMOLED 1.64 display. The long-term product is a physical notification and response surface for coding agents. The current repository implements the tested software slices described below without claiming unperformed physical validation.

## What works today

### Bridge

- ASP.NET Core 10 executable with structured JSON console logging
- loopback-only default listener at `http://127.0.0.1:8742`
- `GET /health` and non-secret protocol-v1 `GET /api/status`
- strict, 16 KiB-bounded `POST /api/events` and `POST /api/attentions` ingestion
- deterministic session/attention state, contiguous ingress ordering, idempotency, bounded history, transactional atomic persistence, stale-session handling, and restart recovery
- digest-authenticated protocol-v1 `/ws` capability negotiation, heartbeat validation, reconnect replay, and live state fan-out
- integration tests, process-level smoke coverage, and a reproducible Docker image build
- default-off, loopback-only adapters for Codex CLI, Claude Code, and Copilot CLI hooks, with bounded secret-redacted mapping, durable approve/deny outcomes, and synthetic fixtures

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

The firmware implementation is compile-tested and its pure protocol/action logic is host-tested, but no physical module was available to validate pixels, touch, Wi-Fi/RF, or TLS on device. The bridge still does **not** implement token issuance/rotation, a non-loopback LAN TLS listener, or tray/pairing UI, so the checked-in stack does not yet provide a complete live device connection. Provider permission decisions are synthetic-test validated against documented hook contracts but were not exercised with live provider accounts. Device credentials remain out-of-band development inputs; Windows protected storage and pairing UX are issue #7 scope. See the open issues for dependency-ordered implementation work.

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

A successful compile is static evidence only. Flashing, display, touch, Wi-Fi, and physical bench behavior are not CI-validated. See [`firmware/README.md`](firmware/README.md).

## Container build

```bash
docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:local .
docker run --rm -p 127.0.0.1:8742:8742 agentping-bridge:local
```

The container listens on `0.0.0.0:8742` inside its isolated network namespace so Docker can forward traffic; the documented host publish is explicitly restricted to `127.0.0.1`.

## Architecture and protocol

The ESP32 remains a thin client; GitHub/OpenAI/Anthropic credentials stay on the PC. Its transport accepts only RFC1918-literal WSS endpoints, a provisioned leaf-certificate trust anchor, and a revocable device token. The bridge binds to loopback by default and authenticates `/ws` with token digests, but non-loopback HTTPS/WSS certificate provisioning and interactive pairing remain deferred, so the checked-in HTTP listener must not be exposed to the LAN.

- [`docs/architecture.md`](docs/architecture.md) describes current and planned boundaries.
- [`docs/protocol.md`](docs/protocol.md) specifies protocol v1, secure pairing, compatibility, ordering, resume, and fail-closed action rules.
- [`docs/provider-adapters.md`](docs/provider-adapters.md) documents default-off Codex CLI, Claude Code, Copilot CLI, and manual/test hook ingestion.
- [`hardware/README.md`](hardware/README.md) states the current hardware evidence and future KiCad scope.

## Contributing and security

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for exact verification commands and [`SECURITY.md`](SECURITY.md) for private vulnerability reporting and trust-boundary requirements.

## License

MIT. See [`LICENSE`](LICENSE).
