# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability or exposed secret. Use GitHub's private vulnerability reporting for this repository. Include affected versions, reproduction steps, impact, and any suggested mitigation. Do not include live credentials.

## Security baseline

- The bridge binds to loopback by default.
- Provider credentials stay on the PC and must never be sent to the ESP32.
- Logs and fixtures must redact secrets.
- Pairing credentials must be revocable and protected at rest when implemented.
- Approval flows must fail closed and be idempotent before they can trigger provider actions.

The current baseline does not implement device pairing, LAN exposure, provider adapters, or approval actions. Those features must receive focused security tests as they are introduced.

Supported versions will be listed once the project publishes its first release. Until then, only the latest `main` branch is maintained.
