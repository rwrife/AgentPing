# Architecture baseline

AgentPing has three trust-separated runtime layers:

1. **Provider adapters (PC only)** translate coding-agent events. Provider credentials remain on the PC and must never enter device messages.
2. **AgentPing Bridge (PC)** is the future source of normalized session and attention state. The current baseline exposes only loopback HTTP health and status endpoints.
3. **AgentPing Display (ESP32-C6)** is a thin presentation/input client. The current firmware only emits serial heartbeats; networking, display, touch, pairing, and actions are not implemented yet.

```text
provider process -> adapter -> bridge -> authenticated LAN protocol -> display
                                      <- bounded user actions -------
```

## Current executable boundary

The bridge binds to `127.0.0.1:8742` by default. `/health` is a liveness endpoint and `/api/status` reports a minimal non-secret process status. There is no device WebSocket, provider ingestion, persistence, or approval broker in this baseline.

## Planned security boundaries

Protocol and pairing design is tracked by issue #2. Until it lands:

- do not expose the bridge on a LAN or public interface;
- do not put provider credentials or API tokens in firmware;
- do not treat the baseline status endpoint as device authentication;
- do not implement consequential approve/deny actions without fail-closed, replay-safe semantics.
