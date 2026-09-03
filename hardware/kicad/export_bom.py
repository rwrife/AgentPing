#!/usr/bin/env python3
"""Export the BOM directly from AgentPing KiCad symbol properties."""

from __future__ import annotations

import argparse
import csv
import io
import os
import re
from collections import defaultdict
from decimal import Decimal
from pathlib import Path


def natural_ref(ref: str) -> tuple[str, int, str]:
    match = re.match(r"([A-Za-z]+)(\d+)(.*)", ref)
    return (match.group(1), int(match.group(2)), match.group(3)) if match else (ref, 0, "")


def prop(component, name: str) -> str:
    value = component.get_property(name)
    if isinstance(value, dict):
        value = value.get("value")
    return "" if value is None else str(value)


def render(schematic_path: Path) -> str:
    os.environ.setdefault("KICAD_SYMBOL_DIR", "/usr/share/kicad/symbols")
    from kicad_sch_api import load_schematic

    schematic = load_schematic(schematic_path)
    fields = [
        "Reference", "Qty", "Value", "Footprint", "Manufacturer", "MPN",
        "Supplier", "Supplier PN", "Estimated Unit Cost USD", "Extended Cost USD",
        "Cost Basis", "Datasheet", "Datasheet Checked UTC", "BOM Comments",
    ]
    group_fields = [
        "Value", "Footprint", "Manufacturer", "MPN", "Supplier", "Supplier PN",
        "Estimated Unit Cost USD", "Cost Basis", "Datasheet", "Datasheet Checked UTC",
    ]
    groups: dict[tuple[str, ...], list[object]] = defaultdict(list)
    for component in schematic.components.all():
        if component.in_bom and not component.reference.startswith("#"):
            groups[tuple(prop(component, name) for name in group_fields)].append(component)

    rows: list[dict[str, str]] = []
    for key, components in groups.items():
        components.sort(key=lambda item: natural_ref(item.reference))
        values = dict(zip(group_fields, key))
        qty = len(components)
        notes = list(dict.fromkeys(prop(component, "BOM Comments") for component in components))
        rows.append({
            "Reference": ",".join(component.reference for component in components),
            "Qty": str(qty),
            **values,
            "Extended Cost USD": f"{Decimal(values['Estimated Unit Cost USD']) * qty:.4f}",
            "BOM Comments": " | ".join(note for note in notes if note),
        })
    rows.sort(key=lambda row: natural_ref(row["Reference"].split(",", 1)[0]))
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    return output.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--schematic", default="hardware/kicad/agentping-carrier.kicad_sch")
    parser.add_argument("--output", default="hardware/bom/agentping-carrier-bom.csv")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    generated = render(Path(args.schematic))
    output = Path(args.output)
    if args.check:
        if not output.exists() or output.read_text(encoding="utf-8") != generated:
            raise SystemExit(f"stale BOM: run {Path(__file__).name} --output {output}")
        print(f"BOM is current: {output}")
        return 0
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(generated, encoding="utf-8", newline="")
    print(f"exported {generated.count(chr(10)) - 1} BOM lines to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
