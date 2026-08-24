# Integration tooling

`smoke-bridge.sh` launches a locally built bridge on loopback, checks `/health`, and validates the non-secret protocol-v1 `/api/status` JSON contract with empty startup state. It does not mock a successful response.

Run after a Release build:

```bash
dotnet build AgentPing.sln --configuration Release
./integration/smoke-bridge.sh
```

Provider/device bridge-core behavior is covered by in-process HTTP/WebSocket integration tests in `bridge/AgentPing.Bridge.Tests`. Full provider simulators, firmware/device simulation, and release-level end-to-end harnesses remain issue #9 scope.
