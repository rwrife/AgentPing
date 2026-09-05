#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT_DIR"

dotnet test bridge/AgentPing.Bridge.Tests/AgentPing.Bridge.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~BridgeEndToEndSimulationTests"
