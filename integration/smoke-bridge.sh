#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PORT=${AGENTPING_SMOKE_PORT:-18742}
LOG=$(mktemp /tmp/agentping-bridge-smoke-XXXXXX.log)
PID=""

cleanup() {
  if [[ -n "$PID" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID"
    wait "$PID" 2>/dev/null || true
  fi
  rm -f "$LOG"
}
trap cleanup EXIT

Kestrel__Endpoints__Http__Url="http://127.0.0.1:$PORT" \
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
    printf 'Bridge exited before becoming ready. Log:\n' >&2
    tee /dev/stderr <"$LOG" >/dev/null
    exit 1
  fi
  sleep 1
done

HEALTH=$(curl --fail --silent "http://127.0.0.1:$PORT/health")
STATUS=$(curl --fail --silent "http://127.0.0.1:$PORT/api/status")

[[ "$HEALTH" == "Healthy" ]]
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["service"] == "agentping-bridge"; assert d["status"] == "ok"; assert d["apiVersion"] == "baseline-v0"' <<<"$STATUS"

printf 'SMOKE_RESULT=PASS health=%s service=agentping-bridge apiVersion=baseline-v0\n' "$HEALTH"
