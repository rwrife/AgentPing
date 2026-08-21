# Architecture

AgentPing has three trust-separated runtime layers:

1. **Provider adapters (PC only)** translate coding-agent events and actions. GitHub, OpenAI, Anthropic, and other provider credentials remain on the PC and never enter device messages.
2. **AgentPing Bridge (PC)** owns normalized session/attention state, device enrollment, policy, idempotency, provider dispatch, and audit records. The executable currently exposes only loopback health/status endpoints; device transport is intentionally deferred to bridge-core issue #3.
3. **AgentPing Display (ESP32-C6)** is a thin presentation/input client. It may hold only its own revocable device credential and pinned bridge certificate, never provider credentials.

```text
provider process -> adapter -> bridge policy/state -> authenticated WSS -> display
                                      ^                <- bounded actions -|
                                      |-- idempotent provider dispatch
```

## Current executable boundary

The bridge binds to `127.0.0.1:8742` by default. `/health` is liveness and `/api/status` reports minimal non-secret process status. There is no LAN listener, device WebSocket, provider ingestion, persistence, pairing UI, or approval broker yet.

Protocol v1 is now specified as machine-readable JSON Schema, golden fixtures, bridge serialization models/tests, and generated firmware limits. This is contract/static-test evidence—not evidence that networking or pairing is implemented.

## Trust boundaries

- **Provider boundary:** adapters normalize only bounded display-safe data; raw provider tokens, prompts, environment variables, and secret-bearing logs are excluded.
- **Bridge policy boundary:** only the bridge may translate a device action into a provider action. It revalidates authentication, revision, deadline, explicit confirmation, authorization, and idempotency.
- **LAN boundary:** future device traffic uses TLS with a provisioned certificate pin and a device-specific revocable token. Loopback remains the default until an authenticated listener exists.
- **Device boundary:** compromise of one device token must not expose provider credentials or authorize another device. Revocation closes connections and clears pending actions.

See [`protocol.md`](protocol.md) for versioning, message flow, threat model, pairing, resume, and fail-closed approval rules.
