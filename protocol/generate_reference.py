#!/usr/bin/env python3
"""Generate a human-readable protocol reference from protocol/v1/agentping.schema.json."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ROOT / "protocol" / "v1" / "agentping.schema.json"
DEFAULT_OUTPUT = ROOT / "docs" / "generated" / "protocol-v1-reference.md"


def load_schema(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def summary_from_property(schema: dict[str, Any]) -> str:
    parts: list[str] = []
    if "$ref" in schema:
        parts.append(f"ref `{schema['$ref']}`")
    if "const" in schema:
        parts.append(f"const `{schema['const']}`")
    if "enum" in schema:
        parts.append("enum: " + ", ".join(f"`{item}`" for item in schema["enum"]))
    if "type" in schema:
        parts.append(f"type `{schema['type']}`")
    if "format" in schema:
        parts.append(f"format `{schema['format']}`")
    if "pattern" in schema:
        parts.append(f"pattern `{schema['pattern']}`")
    if "minimum" in schema or "maximum" in schema:
        lo = schema.get("minimum", "-")
        hi = schema.get("maximum", "-")
        parts.append(f"range `{lo}..{hi}`")
    if "minLength" in schema or "maxLength" in schema:
        lo = schema.get("minLength", "-")
        hi = schema.get("maxLength", "-")
        parts.append(f"length `{lo}..{hi}`")
    if "minItems" in schema or "maxItems" in schema:
        lo = schema.get("minItems", "-")
        hi = schema.get("maxItems", "-")
        parts.append(f"items `{lo}..{hi}`")
    if schema.get("uniqueItems"):
        parts.append("unique items")
    if schema.get("additionalProperties") is False:
        parts.append("no extra properties")
    if "description" in schema:
        parts.append(schema["description"])
    return "; ".join(parts) if parts else "-"


def extract_type_payload_map(schema: dict[str, Any]) -> dict[str, str]:
    mapping: dict[str, str] = {}
    for rule in schema.get("allOf", []):
        kind = rule.get("if", {}).get("properties", {}).get("type", {}).get("const")
        payload_ref = rule.get("then", {}).get("properties", {}).get("payload", {}).get("$ref")
        if isinstance(kind, str) and isinstance(payload_ref, str):
            mapping[kind] = payload_ref.split("/")[-1]
    return mapping


def render_table(headers: list[str], rows: list[list[str]]) -> list[str]:
    lines = ["| " + " | ".join(headers) + " |", "|" + "|".join(["---"] * len(headers)) + "|"]
    for row in rows:
        lines.append("| " + " | ".join(row) + " |")
    return lines


def render(schema: dict[str, Any]) -> str:
    version = schema["properties"]["protocolVersion"]["const"]
    types: list[str] = schema["properties"]["type"]["enum"]
    defs: dict[str, Any] = schema.get("$defs", {})
    type_payload_map = extract_type_payload_map(schema)

    lines: list[str] = []
    lines.append("# AgentPing protocol v1 reference (generated)")
    lines.append("")
    lines.append(
        "This file is generated from `protocol/v1/agentping.schema.json` by "
        "`python3 protocol/generate_reference.py`. Do not edit by hand."
    )
    lines.append("")
    lines.append(f"- Protocol version: `{version}`")
    lines.append("- Schema: Draft 2020-12")
    lines.append("")

    limits = schema.get("x-agentping-limits", {})
    lines.append("## Wire limits")
    lines.append("")
    limit_rows = [[f"`{name}`", f"`{value}`"] for name, value in limits.items()]
    lines.extend(render_table(["Limit", "Value"], limit_rows))
    lines.append("")

    envelope_required = set(schema.get("required", []))
    envelope_properties = schema.get("properties", {})
    lines.append("## Envelope")
    lines.append("")
    envelope_rows: list[list[str]] = []
    for field in sorted(envelope_properties):
        required = "yes" if field in envelope_required else "no"
        envelope_rows.append([f"`{field}`", required, summary_from_property(envelope_properties[field])])
    lines.extend(render_table(["Field", "Required", "Rules"], envelope_rows))
    lines.append("")

    lines.append("## Message payloads")
    lines.append("")
    lines.append("Message kinds:")
    for message_type in types:
        lines.append(f"- `{message_type}`")
    lines.append("")

    for message_type in types:
        payload_name = type_payload_map.get(message_type)
        lines.append(f"### `{message_type}` payload")
        lines.append("")
        if payload_name is None:
            lines.append("No payload schema mapping was found in `allOf`.")
            lines.append("")
            continue

        payload_schema = defs[payload_name]
        required = set(payload_schema.get("required", []))
        properties = payload_schema.get("properties", {})
        payload_rows: list[list[str]] = []
        for field in sorted(properties):
            payload_rows.append(
                [
                    f"`{field}`",
                    "yes" if field in required else "no",
                    summary_from_property(properties[field]),
                ]
            )

        lines.append(f"Schema definition: `#/$defs/{payload_name}`")
        lines.append("")
        lines.extend(render_table(["Field", "Required", "Rules"], payload_rows))

        payload_all_of = payload_schema.get("allOf", [])
        if payload_all_of:
            lines.append("")
            lines.append("Conditional rules:")
            for rule in payload_all_of:
                condition = json.dumps(rule.get("if", {}), separators=(",", ":"))
                consequence = json.dumps(rule.get("then", {}), separators=(",", ":"))
                lines.append(f"- if `{condition}` then `{consequence}`")
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--schema", type=Path, default=SCHEMA_PATH)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Validate that the output file is current instead of writing it.",
    )
    args = parser.parse_args()

    schema = load_schema(args.schema)
    rendered = render(schema)

    if args.check:
        if not args.output.exists() or args.output.read_text(encoding="utf-8") != rendered:
            print(
                f"protocol reference is stale: run python3 protocol/generate_reference.py --output {args.output}",
            )
            return 1
        print(f"protocol reference is current: {args.output}")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(rendered, encoding="utf-8")
    print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
