# Architecture

AgentPing has three trust-separated runtime layers:

1. **Provider adapters (PC only)** translate supported local coding-agent hook events into bounded protocol-v1 state and translate a committed device outcome into each provider's documented permission-decision JSON. GitHub, OpenAI, Anthropic, and other provider credentials remain on the PC and never enter device messages. Adapters are independent and default off.
2. **AgentPing Bridge (PC)** owns normalized session/attention state, bounded history, durable action idempotency, stale-session handling, device authentication, reconnect replay, live fan-out, policy, and commit-before-provider-notify dispatch. It binds to loopback by default.
3. **AgentPing Display (ESP32-C6)** is a thin presentation/input client. It may hold only its own revocable device credential and pinned bridge certificate, never provider credentials.

```text
provider process -> adapter -> bridge policy/state -> authenticated WSS -> display
                                      ^                <- bounded actions -|
                                      |-- idempotent provider dispatch
```

## Current executable boundary

The bridge binds to `127.0.0.1:8742` by default. `/health` is liveness; `/api/status` reports non-secret protocol-v1 counts and sequence state. `/api/events` and `/api/attentions` accept strict bounded protocol envelopes, enforce contiguous sequence ordering per provider connection, normalize and transactionally persist state, suppress duplicates, and publish only committed changes. Authenticated `/ws` connections negotiate capability, validate heartbeat ordering, replay bounded history or send a fresh snapshot after a missed window, and receive live session/attention updates.

The bridge owns device enrollment and credential lifecycle. On Windows it persists DPAPI-protected token/key material plus a keyed lookup digest. Enrollment is HTTPS-only, single-use, capped at five minutes and five attempts. The operator must separately configure the RFC1918 Kestrel HTTPS/WSS endpoint and certificate. Physical-device and live-LAN validation remain unperformed.

## Trust boundaries

- **Provider boundary:** adapters normalize only bounded display-safe data; raw provider tokens, prompts, environment variables, and secret-bearing logs are excluded.
- **Bridge policy boundary:** only the bridge may translate a device action into a provider action. It revalidates authentication, revision, deadline, explicit confirmation, authorization, and idempotency.
- **LAN boundary:** firmware device traffic requires WSS with a provisioned exact leaf certificate and a device-specific revocable token. Loopback remains the bridge default until an authenticated TLS listener exists.
- **Device boundary:** compromise of one device token must not expose provider credentials or authorize another device. Revocation closes connections and clears pending actions.

See [`protocol.md`](protocol.md) for versioning, message flow, threat model, pairing, resume, and fail-closed approval rules.

## Windows companion boundary

`AgentPing.Bridge` owns pairing and credentials. `AgentPing.Companion.Core` supplies a typed loopback management client, listener policy, redacted logs, startup preference, and projections. The Windows UI verifies the configured TLS certificate fingerprint before opening discovery and pairing.
