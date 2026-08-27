# AgentPing Bridge

The bridge is the PC-side source of normalized AgentPing session and attention state. It keeps provider credentials on the PC, persists bounded state atomically, and exposes protocol-v1 state to authenticated displays.

## Runtime endpoints

| Endpoint | Purpose | Access |
|---|---|---|
| `GET /health` | Process liveness | Loopback by default |
| `GET /api/status` | Non-secret counts and server sequence | Loopback by default |
| `POST /api/events` | Strict protocol-v1 `event` envelope ingestion | Loopback by default |
| `POST /api/attentions` | Strict protocol-v1 `attention` envelope ingestion | Loopback by default |
| `POST /api/adapters/{provider}` | Default-off local provider hook normalization | Loopback only |
| `GET /ws` | Protocol-v1 display WebSocket | Device bearer token required |

The HTTP ingestion routes reject unmapped fields, credential-like metadata keys, invalid enum/bound values, non-UTC or more-than-300-second-skewed timestamps, bodies over 16,384 UTF-8 bytes, and noncontiguous sequences within a provider `connectionId`. New provider connections begin at sequence 1. Events deterministically update session state; attention messages must reference an existing session and carry a newer revision. Exact duplicate message, event, and attention IDs return the recorded outcome without applying the operation twice; changed-payload or cross-type reuse fails with a state conflict.

The WebSocket requires a schema-valid `capability` message at sequence 1, negotiates only protocol `1.0`, and replays bounded state newer than `resumeFromSequence`. A missed replay window is explicitly framed by `resetState`, `snapshotItemCount`, and `snapshotCheckpoint` in the bridge capability response, including zero-item snapshots. Replay/live items carry durable `serverSequence` checkpoints. The bridge accepts only schema-valid contiguous heartbeats for the negotiated connection and closes on malformed, oversized, unsupported, or ambiguous input. Live committed session/attention changes are buffered and fanned out in global server-sequence order through bounded per-device queues.

## Safe defaults and configuration

The checked-in listener remains `http://127.0.0.1:8742`. Do not expose this HTTP listener to a LAN. A LAN listener requires HTTPS/WSS certificate provisioning and explicit interface selection; that pairing UI/provisioning work remains in issue #7.

Standard .NET configuration keys are supported through `appsettings.json`, environment variables, or command-line configuration:

| Key | Default |
|---|---|
| `Kestrel__Endpoints__Http__Url` | `http://127.0.0.1:8742` |
| `Bridge__PersistencePath` | user local app data + `AgentPing/bridge-state.json` |
| `Bridge__DeviceTokensPath` | user local app data + `AgentPing/device-tokens.json` |
| `Bridge__MaxHistory` | `256` (maximum `256`) |
| `Bridge__StaleSessionSeconds` | `300` |
| `Bridge__StaleSweepSeconds` | `30` |
| `Logging__LogLevel__Default` | `Information` |

Provider adapters are independently enabled with `Adapters__Manual__Enabled`, `Adapters__Codex__Enabled`, `Adapters__ClaudeCode__Enabled`, and `Adapters__CopilotCli__Enabled`. All default to `false`. Their status and capability level appear in `GET /api/status`; see [`docs/provider-adapters.md`](../docs/provider-adapters.md) for provider setup, supported events, privacy limits, fixtures, and troubleshooting.

State transitions use copy-on-write commit semantics: state is written through a temporary file and atomic replacement before the in-memory transition becomes authoritative. A lifetime-held lock file enforces one bridge writer per persistence path. Serialization, cancellation, or filesystem failure rolls back session, history, sequence, and deduplication changes so a retry is not falsely classified as a duplicate. Corrupt or inaccessible persisted state, or a second writer using the same path, fails startup rather than silently discarding ordering or deduplication history.

## Development device credential file

Pairing and protected credential provisioning are not implemented yet. For integration development, provision the configured token file out of band with only a SHA-256 digest of a full-entropy token:

```json
{
  "devices": [
    {
      "deviceId": "display-development-1",
      "tokenSha256": "64-lowercase-hex-characters",
      "revoked": false
    }
  ]
}
```

Send the original token only with the WebSocket Bearer authorization scheme. Query-string credentials are not accepted. The bridge reloads the digest file for every handshake, so setting `revoked` to `true` denies new connections immediately. Never commit the token file or original token.

Production Windows protected storage, token issuance/rotation, connection termination on revocation, and pairing UX remain issue #7 scope.

## Verification

From the repository root:

```bash
dotnet restore AgentPing.sln --locked-mode
dotnet build AgentPing.sln --configuration Release --no-restore
dotnet test AgentPing.sln --configuration Release --no-build
./integration/smoke-bridge.sh
docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:local .
```

The automated evidence covers HTTP validation, deterministic state transitions, attention queueing, idempotency, bounded history, atomic restart recovery, stale-session handling, digest authentication, capability negotiation, replay/live fan-out, heartbeat handling, provider fixture mapping/relay safety, and process/container startup. It does not prove live provider accounts, provider action execution, LAN TLS, physical-device connectivity, pairing, Windows protected storage, tray behavior, or bench hardware behavior.
