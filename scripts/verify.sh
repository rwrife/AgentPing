#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$ROOT"

python3 -m unittest discover -s tools/tests -v
firmware/tests/run_host_tests.sh
python3 protocol/validate.py
python3 protocol/generate_reference.py --check
dotnet restore AgentPing.sln --locked-mode
dotnet build AgentPing.sln --configuration Release --no-restore
dotnet test AgentPing.sln --configuration Release --no-build --logger "console;verbosity=normal"
./integration/smoke-bridge.sh
./integration/smoke-provider-adapters.sh
./integration/smoke-e2e-simulator.sh

if command -v platformio >/dev/null 2>&1; then
  platformio run -d firmware -e waveshare_esp32_c6_touch_amoled_1_64 -e live3d_usb -e live3d_usb_s3
elif command -v pio >/dev/null 2>&1; then
  pio run -d firmware -e waveshare_esp32_c6_touch_amoled_1_64 -e live3d_usb -e live3d_usb_s3
else
  printf 'PlatformIO is required. Install the pinned tool with: python3 -m pip install platformio==6.1.18\n' >&2
  exit 127
fi
