#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$ROOT"

python3 -m unittest discover -s tools/tests -v
firmware/tests/run_host_tests.sh
python3 protocol/validate.py
dotnet restore AgentPing.sln --locked-mode
dotnet build AgentPing.sln --configuration Release --no-restore
dotnet test AgentPing.sln --configuration Release --no-build --logger "console;verbosity=normal"
./integration/smoke-bridge.sh
./integration/smoke-provider-adapters.sh

if command -v platformio >/dev/null 2>&1; then
  platformio run -d firmware
elif command -v pio >/dev/null 2>&1; then
  pio run -d firmware
else
  printf 'PlatformIO is required. Install the pinned tool with: python3 -m pip install platformio==6.1.18\n' >&2
  exit 127
fi
