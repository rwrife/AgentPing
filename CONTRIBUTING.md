# Contributing

## Prerequisites

- .NET SDK 10.0.300 (enforced by `global.json` with roll-forward disabled)
- Python 3.12.3 and the locked PlatformIO dependencies in `firmware/requirements-ci.txt`
- Locked JSON Schema validator dependencies in `protocol/requirements-ci.txt`
- Docker for the bridge container check

## Verify a change

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt --requirement protocol/requirements-ci.txt
./scripts/verify.sh
docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:local .
```

Keep physical bench results distinct from builds, automated tests, and simulation. Never claim display, touch, RF, hardware, signing, or fabrication validation without the corresponding real evidence.

## Pull requests

- Work on a topic branch; do not commit directly to `main`.
- Keep changes scoped and use conventional commit messages.
- Link the issue with `Closes #N` when the PR completes it.
- Include exact verification commands and outcomes.
- Never commit provider credentials, Wi-Fi credentials, device tokens, private keys, or secret-bearing logs.
