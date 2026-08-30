#!/usr/bin/env python3
"""Relay one provider hook payload to the loopback-only AgentPing bridge."""

from __future__ import annotations

import argparse
import ipaddress
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import BinaryIO

MAX_MESSAGE_BYTES = 16_384
ACTION_WAIT_TIMEOUT_SECONDS = 20
PROVIDERS = ("manual", "codex", "claude_code", "copilot_cli")
PERMISSION_PROVIDERS = frozenset({"codex", "claude_code", "copilot_cli"})


def validated_endpoint(base_url: str, provider: str) -> str:
    parsed = urllib.parse.urlparse(base_url)
    if parsed.scheme != "http" or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError("bridge URL must be a plain loopback http origin")
    if parsed.path not in ("", "/") or not parsed.hostname:
        raise ValueError("bridge URL must not contain a path")
    try:
        address = ipaddress.ip_address(parsed.hostname)
    except ValueError as exc:
        raise ValueError("bridge URL must use a literal loopback address") from exc
    if not address.is_loopback:
        raise ValueError("bridge URL must use a loopback address")
    origin = f"http://[{address}]" if address.version == 6 else f"http://{address}"
    if parsed.port is not None:
        origin += f":{parsed.port}"
    return f"{origin}/api/adapters/{urllib.parse.quote(provider, safe='')}"


def load_payload(argument: str | None, stdin_buffer: BinaryIO = sys.stdin.buffer) -> bytes:
    raw = argument.encode("utf-8") if argument is not None else stdin_buffer.read(MAX_MESSAGE_BYTES + 1)
    if len(raw) > MAX_MESSAGE_BYTES:
        raise ValueError("provider hook payload exceeds 16384 UTF-8 bytes")
    try:
        value = json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("provider hook payload must be one JSON object") from exc
    if not isinstance(value, dict):
        raise ValueError("provider hook payload must be one JSON object")
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def provider_decision(provider: str, outcome: dict[str, object]) -> dict[str, object]:
    action = outcome.get("action")
    status = outcome.get("status")
    if action not in {"approve", "deny", "cancel", "acknowledge", "reply"} or not isinstance(status, str):
        raise ValueError("bridge returned an invalid action outcome")

    if provider == "manual":
        result: dict[str, object] = {"action": action, "status": status}
        if action == "reply":
            text = outcome.get("text")
            if not isinstance(text, str) or not text or len(text) > 512:
                raise ValueError("bridge returned an invalid reply")
            result["text"] = text
        return result

    if action == "reply":
        raise ValueError("permission hooks cannot consume a short reply")
    decision = "allow" if action == "approve" and status == "recorded" else "deny"
    reason = "Denied by AgentPing device."
    if provider in {"claude_code", "codex"}:
        value: dict[str, object] = {"behavior": decision}
        if decision == "deny":
            value["message"] = reason
        return {
            "hookSpecificOutput": {
                "hookEventName": "PermissionRequest",
                "decision": value,
            }
        }
    if provider == "copilot_cli":
        result = {"permissionDecision": decision}
        if decision == "deny":
            result["permissionDecisionReason"] = reason
        return result
    raise ValueError("provider does not support action decisions")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", required=True, choices=PROVIDERS)
    parser.add_argument("--bridge", default="http://127.0.0.1:8742")
    parser.add_argument(
        "--wait-for-action",
        action="store_true",
        help="wait up to the protocol deadline and emit one provider decision JSON object",
    )
    parser.add_argument("payload", nargs="?", help="Codex notify JSON argument; otherwise JSON is read from stdin")
    args = parser.parse_args()

    try:
        endpoint = validated_endpoint(args.bridge, args.provider)
        if args.wait_for_action:
            endpoint += "?waitForAction=true"
        payload = load_payload(args.payload)
        request = urllib.request.Request(
            endpoint,
            data=payload,
            method="POST",
            headers={"Content-Type": "application/json", "User-Agent": "AgentPing-provider-relay/1"},
        )
        with urllib.request.urlopen(
            request,
            timeout=ACTION_WAIT_TIMEOUT_SECONDS if args.wait_for_action else 3,
        ) as response:
            expected_status = 200 if args.wait_for_action else 202
            if response.status != expected_status:
                raise RuntimeError(f"bridge returned HTTP {response.status}")
            if args.wait_for_action:
                raw_outcome = response.read(MAX_MESSAGE_BYTES + 1)
                if len(raw_outcome) > MAX_MESSAGE_BYTES:
                    raise RuntimeError("bridge action outcome exceeded protocol limit")
                outcome = json.loads(raw_outcome)
                if not isinstance(outcome, dict):
                    raise RuntimeError("bridge returned an invalid action outcome")
                print(json.dumps(provider_decision(args.provider, outcome), separators=(",", ":")))
        return 0
    except urllib.error.HTTPError as exc:
        print(f"AgentPing relay rejected by bridge (HTTP {exc.code}); payload was not logged", file=sys.stderr)
    except (OSError, RuntimeError, ValueError):
        print("AgentPing relay failed; payload was not logged", file=sys.stderr)
    if args.wait_for_action and args.provider in PERMISSION_PROVIDERS:
        denial = provider_decision(args.provider, {"action": "deny", "status": "failed"})
        print(json.dumps(denial, separators=(",", ":")))
        return 0
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
