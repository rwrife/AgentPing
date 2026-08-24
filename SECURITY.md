# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability or exposed secret. Use GitHub's private vulnerability reporting for this repository. Include affected versions, reproduction steps, impact, and any suggested mitigation. Do not include live credentials.

## Security baseline

- The bridge binds to loopback by default.
- Provider credentials stay on the PC and must never be sent to the ESP32.
- Logs and fixtures must redact secrets.
- Pairing credentials are device-specific, revocable, and protected at rest.
- Short numeric PIN-only LAN pairing is forbidden; protocol v1 requires full-entropy provisioning material and TLS certificate pinning through a secure local channel.
- Approval flows fail closed and are idempotent before they can trigger provider actions.
- Destructive approvals require a second explicit confirmation bound to the exact displayed prompt, current revision, and deadline.

The protocol-v1 schema and pairing design are checked in and statically tested. The bridge now implements loopback event/attention ingestion, atomic state persistence, digest-authenticated WebSockets, capability/heartbeat validation, bounded reconnect replay, and live state fan-out. It still has no provider adapter, pairing endpoint/UI, Windows protected token issuance/rotation, LAN TLS certificate provisioning, or approval executor. See [`docs/protocol.md`](docs/protocol.md) for the threat model and requirements. Keep the default listener on loopback until those remaining controls are implemented.

Supported versions will be listed once the project publishes its first release. Until then, only the latest `main` branch is maintained.
