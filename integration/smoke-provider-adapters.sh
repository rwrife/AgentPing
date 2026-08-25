#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PORT=${AGENTPING_ADAPTER_SMOKE_PORT:-18743}
STATE_DIR=$(mktemp -d /tmp/agentping-adapter-smoke-XXXXXX)
LOG="$STATE_DIR/bridge.log"
PID=""

cleanup() {
  if [[ -n "$PID" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID"
    wait "$PID" 2>/dev/null || true
  fi
  rm -rf "$STATE_DIR"
}
trap cleanup EXIT

Kestrel__Endpoints__Http__Url="http://127.0.0.1:$PORT" \
Bridge__PersistencePath="$STATE_DIR/state.json" \
Bridge__DeviceTokensPath="$STATE_DIR/device-tokens.json" \
Adapters__Codex__Enabled=true \
Adapters__ClaudeCode__Enabled=true \
  dotnet run \
    --project "$ROOT/bridge/AgentPing.Bridge/AgentPing.Bridge.csproj" \
    --configuration Release \
    --no-build >"$LOG" 2>&1 &
PID=$!

for _ in {1..30}; do
  if curl --fail --silent "http://127.0.0.1:$PORT/health" >/dev/null; then
    break
  fi
  if ! kill -0 "$PID" 2>/dev/null; then
    printf 'Bridge exited before adapter smoke readiness. Log:\n' >&2
    tee /dev/stderr <"$LOG" >/dev/null
    exit 1
  fi
  sleep 1
done

python3 "$ROOT/tools/agentping_provider_relay.py" \
  --bridge "http://127.0.0.1:$PORT" --provider codex \
  <"$ROOT/bridge/provider-fixtures/codex-turn-complete.json"
python3 "$ROOT/tools/agentping_provider_relay.py" \
  --bridge "http://127.0.0.1:$PORT" --provider claude_code \
  <"$ROOT/bridge/provider-fixtures/claude-permission-request.json"
# Replay the same provider hook to exercise bridge-side idempotency.
python3 "$ROOT/tools/agentping_provider_relay.py" \
  --bridge "http://127.0.0.1:$PORT" --provider claude_code \
  <"$ROOT/bridge/provider-fixtures/claude-permission-request.json"

STATUS=$(curl --fail --silent "http://127.0.0.1:$PORT/api/status")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["sessionCount"] == 2; assert d["attentionCount"] == 1; assert d["lastServerSequence"] == 3; enabled={a["name"]:a["enabled"] for a in d["adapters"]}; assert enabled["codex"] and enabled["claude_code"]; assert not enabled["copilot_cli"] and not enabled["manual"]' <<<"$STATUS"

printf 'ADAPTER_SMOKE_RESULT=PASS sessions=2 attentions=1 serverSequence=3 replay=idempotent\n'
