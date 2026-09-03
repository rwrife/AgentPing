#!/usr/bin/env python3
"""Generate the AgentPing carrier PCB placement and enforceable keepouts."""

from __future__ import annotations

import argparse
import os
import xml.etree.ElementTree as ET
from pathlib import Path

import pcbnew

BOARD_WIDTH_MM = 70.0
BOARD_HEIGHT_MM = 60.0
MODULE_CENTER = (20.0, 30.0)
# STEP evidence: header centers x=±11.43, first pin y=-9.15, 2.54 mm pitch.
PLACEMENTS: dict[str, tuple[float, float, float]] = {
    "J1": (8.57, 20.85, 0.0),
    "J2": (31.43, 20.85, 0.0),
    "J3": (50.0, 4.8, 0.0),
    "R1": (43.0, 11.5, 0.0),
    "R2": (49.0, 11.5, 0.0),
    "D1": (60.0, 13.5, 0.0),
    "F1": (62.0, 19.5, 0.0),
    "D2": (56.0, 24.0, 0.0),
    "C1": (48.0, 21.0, 0.0),
    "R3": (42.0, 31.0, 0.0),
    "R4": (47.0, 35.0, 90.0),
    "Q1": (52.0, 34.0, 0.0),
    "D3": (58.0, 32.0, 90.0),
    "C2": (59.0, 38.0, 90.0),
    "J4": (66.0, 35.0, 90.0),
    "TP1": (40.0, 16.0, 0.0),
    "TP2": (42.0, 22.0, 0.0),
    "TP3": (41.0, 43.0, 0.0),
    "TP4": (39.0, 28.0, 0.0),
    "TP5": (47.0, 30.0, 0.0),
    "TP6": (58.0, 43.0, 0.0),
}

MODULE_HOLES = {
    "HM1": (9.0, 10.25), "HM2": (31.0, 10.25),
    "HM3": (9.0, 48.75), "HM4": (31.0, 48.75),
}
CARRIER_HOLES = {
    "H1": (4.0, 4.0), "H2": (66.0, 4.0),
    "H3": (4.0, 56.0), "H4": (66.0, 56.0),
}


def mm(value: float) -> int:
    return pcbnew.FromMM(value)


def point(x: float, y: float) -> pcbnew.VECTOR2I:
    return pcbnew.VECTOR2I_MM(x, y)


def footprint_root() -> Path:
    return Path(os.environ.get("KICAD_FOOTPRINT_DIR", "/usr/share/kicad/footprints"))


def load_footprint(fp_id: str) -> pcbnew.FOOTPRINT:
    library, name = fp_id.split(":", 1)
    path = footprint_root() / f"{library}.pretty"
    footprint = pcbnew.FootprintLoad(str(path), name)
    if footprint is None:
        raise SystemExit(f"unable to load {fp_id} from {path}")
    return footprint


def add_outline(board: pcbnew.BOARD) -> None:
    corners = [(0.0, 0.0), (BOARD_WIDTH_MM, 0.0), (BOARD_WIDTH_MM, BOARD_HEIGHT_MM), (0.0, BOARD_HEIGHT_MM)]
    for start, end in zip(corners, corners[1:] + corners[:1]):
        segment = pcbnew.PCB_SHAPE(board)
        segment.SetShape(pcbnew.SHAPE_T_SEGMENT)
        segment.SetLayer(pcbnew.Edge_Cuts)
        segment.SetStart(point(*start))
        segment.SetEnd(point(*end))
        segment.SetWidth(mm(0.05))
        board.Add(segment)


def add_rectangle(board: pcbnew.BOARD, x1: float, y1: float, x2: float, y2: float, layer: int) -> None:
    corners = [(x1, y1), (x2, y1), (x2, y2), (x1, y2)]
    for start, end in zip(corners, corners[1:] + corners[:1]):
        segment = pcbnew.PCB_SHAPE(board)
        segment.SetShape(pcbnew.SHAPE_T_SEGMENT)
        segment.SetLayer(layer)
        segment.SetStart(point(*start))
        segment.SetEnd(point(*end))
        segment.SetWidth(mm(0.15))
        board.Add(segment)


def add_rule_area(
    board: pcbnew.BOARD,
    layer: int,
    name: str,
    bounds: tuple[float, float, float, float],
    *,
    block_footprints: bool,
) -> None:
    zone = pcbnew.ZONE(board)
    zone.SetLayer(layer)
    zone.SetZoneName(name)
    zone.SetIsRuleArea(True)
    zone.SetDoNotAllowCopperPour(True)
    zone.SetDoNotAllowTracks(True)
    zone.SetDoNotAllowVias(True)
    zone.SetDoNotAllowPads(True)
    zone.SetDoNotAllowFootprints(block_footprints)
    outline = zone.Outline()
    outline.NewOutline()
    x1, y1, x2, y2 = bounds
    for x, y in [(x1, y1), (x2, y1), (x2, y2), (x1, y2)]:
        outline.Append(mm(x), mm(y))
    board.Add(zone)


def add_text(board: pcbnew.BOARD, text: str, x: float, y: float, *, layer: int = pcbnew.F_SilkS, size: float = 0.8, rotation: float = 0.0) -> None:
    item = pcbnew.PCB_TEXT(board)
    item.SetText(text)
    item.SetPosition(point(x, y))
    item.SetLayer(layer)
    item.SetTextSize(pcbnew.VECTOR2I_MM(size, size))
    item.SetTextThickness(mm(max(0.12, size * 0.15)))
    item.SetTextAngle(pcbnew.EDA_ANGLE(rotation, pcbnew.DEGREES_T))
    board.Add(item)


def configure_rules(board: pcbnew.BOARD) -> None:
    settings = board.GetDesignSettings()
    settings.m_TrackMinWidth = mm(0.20)
    settings.m_MinClearance = mm(0.20)
    settings.m_ViasMinSize = mm(0.60)
    settings.m_MinThroughDrill = mm(0.30)
    settings.m_HoleClearance = mm(0.25)
    settings.m_CopperEdgeClearance = mm(0.50)
    settings.m_SilkClearance = mm(0.20)
    settings.m_BoardThickness = mm(1.60)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--netlist", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("agentping-carrier.kicad_pcb"))
    args = parser.parse_args()

    root = ET.parse(args.netlist).getroot()
    components = root.findall("./components/comp")
    refs = {component.get("ref", "") for component in components}
    if refs != set(PLACEMENTS):
        raise SystemExit(f"placement map mismatch: XML-only={sorted(refs-set(PLACEMENTS))} map-only={sorted(set(PLACEMENTS)-refs)}")

    board = pcbnew.BOARD()
    board.SetFileName(str(args.output))
    configure_rules(board)
    add_outline(board)
    add_rectangle(board, 5.44, 7.3, 34.56, 51.5, pcbnew.F_Fab)
    for layer in (pcbnew.F_Cu, pcbnew.B_Cu):
        add_rule_area(
            board, layer, "WAVESHARE MODULE RF - NO COPPER/TRACKS/VIAS/PADS",
            (11.5, 7.5, 28.5, 52.5), block_footprints=False,
        )
        add_rule_area(
            board, layer, "WAVESHARE MODULE BODY - NO COMPONENTS/COPPER",
            (12.0, 7.5, 28.0, 52.5), block_footprints=True,
        )

    footprints: dict[str, pcbnew.FOOTPRINT] = {}
    for component in components:
        ref = component.get("ref", "")
        fp_id = component.findtext("footprint", "")
        if not fp_id:
            raise SystemExit(f"{ref} has no footprint")
        footprint = load_footprint(fp_id)
        footprint.SetFPIDAsString(fp_id)
        footprint.SetReference(ref)
        footprint.SetValue(component.findtext("value", ""))
        x, y, rotation = PLACEMENTS[ref]
        footprint.SetPosition(point(x, y))
        footprint.SetOrientationDegrees(rotation)
        footprint.Reference().SetTextSize(pcbnew.VECTOR2I_MM(0.8, 0.8))
        footprint.Reference().SetTextThickness(mm(0.12))
        footprint.Value().SetVisible(False)
        if ref.startswith("TP") or ref == "J4":
            footprint.Reference().SetVisible(False)
        board.Add(footprint)
        footprints[ref] = footprint

    for ref, xy in MODULE_HOLES.items():
        footprint = load_footprint("MountingHole:MountingHole_2.2mm_M2")
        footprint.SetReference(ref)
        footprint.SetValue("WAVESHARE MODULE M2")
        footprint.SetBoardOnly(True)
        footprint.SetPosition(point(*xy))
        footprint.Reference().SetVisible(False)
        footprint.Value().SetVisible(False)
        board.Add(footprint)
    for ref, xy in CARRIER_HOLES.items():
        footprint = load_footprint("MountingHole:MountingHole_3.2mm_M3")
        footprint.SetReference(ref)
        footprint.SetValue("CARRIER M3")
        footprint.SetBoardOnly(True)
        footprint.SetPosition(point(*xy))
        footprint.Reference().SetVisible(False)
        footprint.Value().SetVisible(False)
        board.Add(footprint)

    net_items: dict[str, pcbnew.NETINFO_ITEM] = {}
    assigned = 0
    for net in root.findall("./nets/net"):
        name = net.get("name", "")
        net_item = pcbnew.NETINFO_ITEM(board, name)
        board.Add(net_item)
        net_items[name] = net_item
        for node in net.findall("node"):
            ref, pin = node.get("ref", ""), node.get("pin", "")
            matches = [pad for pad in footprints[ref].Pads() if pad.GetNumber() == pin]
            if not matches:
                raise SystemExit(f"netlist node {ref}.{pin} has no matching footprint pad")
            for pad in matches:
                pad.SetNet(net_item)
                assigned += 1

    add_text(board, "AGENTPING CARRIER REV A0 - USB 5V SELV", 35.0, 58.5)
    add_text(board, "MODULE / RF KEEP CLEAR", 20.0, 30.0, layer=pcbnew.F_Fab, size=1.0, rotation=90.0)
    add_text(board, "HAPTIC 1:+5V 2:SW", 63.0, 29.0, rotation=90.0)
    for ref, label in {"TP1":"VBUS", "TP2":"5V", "TP3":"GND", "TP4":"GPIO6", "TP5":"GATE", "TP6":"HAPTIC"}.items():
        x, y, _ = PLACEMENTS[ref]
        add_text(board, label, x, y - 2.2)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    pcbnew.SaveBoard(str(args.output), board)
    print(
        f"generated {args.output}: {len(footprints)} schematic footprints + "
        f"{len(MODULE_HOLES)} M2 + {len(CARRIER_HOLES)} M3 holes, "
        f"{len(net_items)} nets, {assigned} assigned pads, {board.GetAreaCount()} rule areas"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
