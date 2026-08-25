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
PROVIDERS = ("manual", "codex", "claude_code", "copilot_cli")


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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", required=True, choices=PROVIDERS)
    parser.add_argument("--bridge", default="http://127.0.0.1:8742")
    parser.add_argument("payload", nargs="?", help="Codex notify JSON argument; otherwise JSON is read from stdin")
    args = parser.parse_args()

    try:
        endpoint = validated_endpoint(args.bridge, args.provider)
        payload = load_payload(args.payload)
        request = urllib.request.Request(
            endpoint,
            data=payload,
            method="POST",
            headers={"Content-Type": "application/json", "User-Agent": "AgentPing-provider-relay/1"},
        )
        with urllib.request.urlopen(request, timeout=3) as response:
            if response.status != 202:
                raise RuntimeError(f"bridge returned HTTP {response.status}")
        return 0
    except urllib.error.HTTPError as exc:
        print(f"AgentPing relay rejected by bridge (HTTP {exc.code}); payload was not logged", file=sys.stderr)
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"AgentPing relay failed: {exc}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
