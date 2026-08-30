# AgentPing device protocol v1

Status: **bridge transport is integration-tested on loopback; firmware WSS/replay logic is implemented, host-tested, and compile-tested; LAN TLS provisioning, pairing UI, and physical device validation are not implemented yet**.

The canonical contract is [`protocol/v1/agentping.schema.json`](../protocol/v1/agentping.schema.json), a JSON Schema Draft 2020-12 document. Golden valid and fail-closed fixtures live beside it. Protocol messages never carry coding-provider credentials, Wi-Fi passwords, device enrollment secrets, or bridge private keys.

## Transport and trust boundary

Protocol v1 is intended for an authenticated `wss://` connection between one Windows bridge and a paired display on the same private LAN. The bridge remains loopback-only unless the user explicitly enables a paired-device listener. Implementations must:

- bind only to selected RFC1918/ULA interfaces, never wildcard/public interfaces by default;
- require TLS 1.2 or newer and pin the bridge certificate during secure provisioning;
- authenticate every connection with a revocable, device-specific 256-bit random token;
- reject credentials in query strings and redact authorization headers, tokens, pairing material, message bodies marked sensitive, and reply text from normal logs;
- enforce the 16,384-byte UTF-8 message limit before JSON parsing;
- close on authentication failure, malformed frames, unsupported versions, replay, or policy uncertainty.

A LAN peer can observe broadcast traffic, probe ports, race pairing, replay captured packets, and attempt resource exhaustion. TLS pinning resists interception; high-entropy one-time enrollment and attempt limits resist guessing; sequence/message identifiers and idempotency resist replay. Agent-provider compromise and a fully compromised Windows host are outside the pairing protocol's protection, so provider actions still require bridge-side policy checks.

## Envelope

Every message has:

| Field | Rule |
|---|---|
| `protocolVersion` | Exact major/minor string; v1 is `1.0`. |
| `messageId` | Globally unique UUID. Receivers deduplicate it. |
| `type` | One of the nine schema-defined message kinds. |
| `sentAt` | RFC 3339 timestamp with UTC offset zero. Event and attention ingestion rejects more than 300 seconds of skew from bridge UTC time. |
| `connectionId` | New opaque ID for each authenticated connection. |
| `sequence` | Monotonically increasing integer within a connection. |
| `serverSequence` | Optional bridge-only durable state checkpoint on replay/live state messages. Providers/displays must not send it. |
| `payload` | Type-specific object; unknown fields fail closed. |

The schema defines bounded strings, arrays, metadata, revisions, queues, and action payloads. The total compact UTF-8 message is capped at 16 KiB. A receiver must check wire size before allocation/parsing and schema-validity before dispatch.

## Message kinds

- `capability`: first message in each direction; advertises supported versions/features and resume position.
- `heartbeat`: liveness, queue depth, and highest contiguous received sequence.
- `session`: revisioned normalized provider-session state.
- `event`: bounded provider event summary with non-secret metadata.
- `attention`: revisioned request or notification with explicit allowed actions and deadline.
- `approval`: idempotent approval action. Destructive approvals require a confirmation proof.
- `denial`: idempotent denial with bounded reason/note.
- `reply`: idempotent short text response, at most 512 characters.
- `error`: bounded protocol error. Error text must not echo secrets or raw rejected payloads.

An attention `responseDeadlineAt` must use UTC offset zero, be later than `sentAt`, and be no more than 30 seconds later.

## Negotiation and compatibility

After authentication, each peer sends `capability` as sequence 1. The bridge chooses the highest mutually supported major/minor version. No overlap means `UNSUPPORTED_VERSION` followed by connection close. A peer must not send operational messages until negotiation succeeds.

- A major version changes incompatible semantics and requires explicit support on both peers.
- A minor version may add optional message kinds or fields, but v1.0 uses `additionalProperties: false`; peers only send fields negotiated by feature.
- Existing fields never change meaning within a major version.
- Deprecated fields remain accepted for one documented minor migration window before removal in the next major.
- Stored queues record their protocol version. Unsupported queued messages are discarded safely and surfaced on the PC, never translated into an approval.

## Ordering, deduplication, and idempotency

Messages are ordered by `sequence` within `connectionId`; transport order alone is not trusted. Receivers:

1. accept the next contiguous sequence;
2. acknowledge it in heartbeat/capability state;
3. return the recorded outcome only when an already-processed identifier has the same message kind and semantic fingerprint; changed-payload or cross-type reuse is a state conflict;
4. buffer at most 256 out-of-order messages;
5. reconnect on a gap that cannot be filled without exceeding that window.

Action payloads include `actionId`, `attentionId`, and `expectedRevision`. The bridge atomically records an action outcome before invoking an adapter. A duplicate returns the same outcome and never calls the provider twice. A revision mismatch is `STALE_REVISION` and fails closed.

## Reconnect and resume

The display reconnects with exponential backoff and jitter (1 second minimum, 60 seconds maximum), authenticates again, then reports the highest fully applied bridge `serverSequence` as `resumeFromSequence` in `capability`. The bridge keeps at most 256 recent messages per device. It replays only messages newer than the acknowledged contiguous server sequence, preserving each original `messageId`, attaching its durable `serverSequence`, and assigning a new connection `sequence`. If the gap is outside the replay window, the bridge capability response sets `resetState: true`, `snapshotItemCount`, and `snapshotCheckpoint`. The display clears prior session and attention state before applying exactly that many following snapshot items, then persists `snapshotCheckpoint`; with zero items it clears and persists immediately. Snapshot items do not carry individual checkpoints. Pending approvals are revalidated against current revision and deadline; they are never assumed approved after reconnect.

## Pairing, rotation, and revocation

Pairing is a provisioning operation, not a protocol message:

1. The user explicitly opens a five-minute pairing window on the PC. This is the only time an unauthenticated enrollment endpoint may exist.
2. The bridge creates a single-use 256-bit enrollment secret and TLS certificate fingerprint. They are transferred through a secure local channel (USB serial is the v1 baseline; a future QR channel may carry the same full-entropy bundle). Short numeric PIN-only LAN pairing is forbidden.
3. The display pins the fingerprint and submits the enrollment secret plus its generated device ID over TLS. The bridge permits at most five failures per window and invalidates the secret after the first success.
4. The bridge returns a different 256-bit device token exactly once. The enrollment secret is erased. Each device receives a distinct token and least-privilege record.
5. Windows stores token material protected with DPAPI; the display uses ESP-IDF NVS encryption where production flash encryption is enabled. Server-side lookup uses a keyed digest rather than plaintext token storage.

Rotation creates a replacement token on an already authenticated channel, commits it on both sides, and accepts the prior token only for a bounded 60-second handoff. Revocation immediately disables the device record, closes its sockets, clears queued actions, and requires fresh physical provisioning. Factory reset erases the display token and certificate pin. No provider credential ever crosses this boundary.

## Approval safety

An attention item declares `destructive`, `allowedActions`, `revision`, and a response deadline (default and maximum action window: 30 seconds). The display must show the exact bounded prompt and target before enabling approval.

For a destructive action, one tap is insufficient. The user must enter a second explicit confirmation gesture; the `approval` includes the presented attention message ID, confirmation time, and SHA-256 digest of the canonical displayed prompt. The bridge independently verifies:

- authenticated paired device and negotiated capability;
- unexpired deadline and clock tolerance;
- current attention revision and allowed action;
- unused `actionId` or matching prior outcome;
- prompt digest and destructive confirmation presence;
- bridge policy and provider adapter authorization.

Any missing, stale, malformed, ambiguous, disconnected, timed-out, or policy-rejected state is denial/no-op. Timeouts never imply approval. `cancel` and `acknowledge` use the denial envelope with explicit `user_cancelled` and `acknowledged` reasons, so neither can be confused with execution approval. Device UI success is not authoritative until the bridge returns a recorded success outcome.

## Bridge implementation status

The bridge enforces the 16 KiB limit on HTTP and WebSocket input, strictly deserializes protocol envelopes, enforces contiguous provider/device sequence ordering, rolls back in-memory transitions when durable commit fails, authenticates display upgrades against revocable token digests, requires display capability at sequence 1, validates contiguous heartbeats, persists bounded replay/action-deduplication history, and replays server-sequence entries newer than `resumeFromSequence` or sends fresh state snapshots when the retained window is insufficient. Device actions are bound to the exact pending attention message and revision, committed before provider notification, and returned idempotently after retry or bridge restart. HTTP provider ingestion and action waiting remain loopback-only. Firmware implements bounded parsing, touch controls, two-step destructive confirmation, reply entry, action serialization, persistent resume/snapshot reduction, authenticated WSS framing, heartbeat, and reconnect behavior. This has compile, host-test, and synthetic integration evidence only. Bridge-side HTTPS/WSS certificate provisioning for LAN use, token issuance/rotation, live provider-account validation, and physical-device validation remain deferred.

## Verification

Run:

```bash
python3 protocol/validate.py
```

The validator checks the schema itself, every valid message kind, fail-closed fixtures (including credential smuggling and missing destructive confirmation), the wire-size canary, and firmware generated constants. Bridge tests deserialize and round-trip the same valid fixture corpus and reject unknown payload fields.
