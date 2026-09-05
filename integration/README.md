# Integration tooling

`smoke-bridge.sh` launches a locally built bridge on loopback, checks `/health`, and validates the non-secret protocol-v1 `/api/status` JSON contract with empty startup state. It does not mock a successful response.

Run after a Release build:

```bash
dotnet build AgentPing.sln --configuration Release
./integration/smoke-bridge.sh
```

Provider/device bridge-core behavior is covered by in-process HTTP/WebSocket integration tests in `bridge/AgentPing.Bridge.Tests`.

`smoke-e2e-simulator.sh` runs the new end-to-end simulation tests (`BridgeEndToEndSimulationTests`) that exercise provider event ingestion, actionable attention fanout, device approval, and reconnect replay behavior without hardware or live provider credentials:

```bash
./integration/smoke-e2e-simulator.sh
```

Firmware/device simulation breadth (display rendering and touch-driver timing under RTOS) plus release-level hardware-in-the-loop harnesses remain follow-on issue #9 work.
