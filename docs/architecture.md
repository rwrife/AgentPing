# Architecture

AgentPing has three trust-separated runtime layers:

1. **Provider adapters (PC only)** translate supported local coding-agent hook events into bounded protocol-v1 state. GitHub, OpenAI, Anthropic, and other provider credentials remain on the PC and never enter device messages. Codex CLI completion notifications and Claude Code/Copilot CLI lifecycle hooks are implemented behind independent default-off switches. Provider action execution remains issue #6 scope.
2. **AgentPing Bridge (PC)** owns normalized session/attention state, bounded history, durable idempotency, stale-session handling, device authentication, reconnect replay, live fan-out, policy, and future provider dispatch. It binds to loopback by default.
3. **AgentPing Display (ESP32-C6)** is a thin presentation/input client. It may hold only its own revocable device credential and pinned bridge certificate, never provider credentials.

```text
provider process -> adapter -> bridge policy/state -> authenticated WSS -> display
                                      ^                <- bounded actions -|
                                      |-- idempotent provider dispatch
```

## Current executable boundary

The bridge binds to `127.0.0.1:8742` by default. `/health` is liveness; `/api/status` reports non-secret protocol-v1 counts and sequence state. `/api/events` and `/api/attentions` accept strict bounded protocol envelopes, enforce contiguous sequence ordering per provider connection, normalize and transactionally persist state, suppress duplicates, and publish only committed changes. Authenticated `/ws` connections negotiate capability, validate heartbeat ordering, replay bounded history or send a fresh snapshot after a missed window, and receive live session/attention updates.

Device credentials are currently provisioned out of band as SHA-256 digests and reloaded per handshake for immediate denial of revoked entries. Interactive pairing, Windows protected credential storage, token issuance/rotation, non-loopback TLS certificate provisioning, and approval dispatch are not implemented yet. Provider hook ingestion is loopback-only, default-off, bounded, and secret-redacted. The checked-in HTTP listener must remain loopback-only.

## Trust boundaries

- **Provider boundary:** adapters normalize only bounded display-safe data; raw provider tokens, prompts, environment variables, and secret-bearing logs are excluded.
- **Bridge policy boundary:** only the bridge may translate a device action into a provider action. It revalidates authentication, revision, deadline, explicit confirmation, authorization, and idempotency.
- **LAN boundary:** future device traffic uses TLS with a provisioned certificate pin and a device-specific revocable token. Loopback remains the default until an authenticated listener exists.
- **Device boundary:** compromise of one device token must not expose provider credentials or authorize another device. Revocation closes connections and clears pending actions.

See [`protocol.md`](protocol.md) for versioning, message flow, threat model, pairing, resume, and fail-closed approval rules.
