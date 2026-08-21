# Integration tooling

`smoke-bridge.sh` launches a locally built bridge on loopback, checks `/health`, and validates the baseline `/api/status` JSON contract. It does not mock a successful response.

Run after a Release build:

```bash
dotnet build AgentPing.sln --configuration Release
./integration/smoke-bridge.sh
```

End-to-end provider/device simulation belongs to issue #9 after the protocol and core bridge are implemented.
