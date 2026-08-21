# Device protocol status

A device protocol is **not implemented in the repository baseline**. Issue #2 owns protocol v1, including schemas, version negotiation, pairing, authentication, message IDs, ordering, replay/idempotency, bounded payloads, reconnect/resume, and fail-closed approvals.

The only current HTTP contract is local bridge observability:

- `GET /health` returns `200 Healthy` when the process is live.
- `GET /api/status` returns a non-secret JSON object with `service`, `status`, `apiVersion`, and `timestampUtc`.

`apiVersion: "baseline-v0"` explicitly identifies this as pre-protocol scaffolding. Firmware must not depend on it as the future device protocol.
