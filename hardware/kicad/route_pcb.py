#!/usr/bin/env python3
"""Specctra exchange and ground-pour finalization for AgentPing carrier."""

from __future__ import annotations

import argparse
from pathlib import Path

import pcbnew

POWER_NETS = {"/VBUS", "/VBUS_FUSED", "/+5V_MODULE", "/HAPTIC_LOW"}


def mm(value: float) -> int:
    return pcbnew.FromMM(value)


def make_class(name: str, width: float) -> pcbnew.NETCLASS:
    netclass = pcbnew.NETCLASS(name)
    netclass.SetClearance(mm(0.20))
    netclass.SetTrackWidth(mm(width))
    netclass.SetViaDiameter(mm(0.60))
    netclass.SetViaDrill(mm(0.30))
    return netclass


def apply_netclasses(board: pcbnew.BOARD) -> None:
    classes = {"Default": make_class("Default", 0.25), "POWER": make_class("POWER", 0.50)}
    class_map = board.GetNetClasses()
    for name, netclass in classes.items():
        class_map[name] = netclass
    for name, net in board.GetNetsByName().items():
        net.SetNetClass(classes["POWER"] if name in POWER_NETS else classes["Default"])
    board.SynchronizeNetsAndNetClasses(True)


def export_dsn(board_path: Path, output: Path) -> None:
    board = pcbnew.LoadBoard(str(board_path))
    apply_netclasses(board)
    if not pcbnew.ExportSpecctraDSN(board, str(output)):
        raise SystemExit("KiCad failed to export Specctra DSN")
    print(f"exported {output}: POWER=0.50mm, Default=0.25mm")


def import_ses(board_path: Path, session: Path) -> None:
    board = pcbnew.LoadBoard(str(board_path))
    apply_netclasses(board)
    if not pcbnew.ImportSpecctraSES(board, str(session)):
        raise SystemExit("KiCad failed to import Specctra session")
    pcbnew.SaveBoard(str(board_path), board)
    tracks = list(board.GetTracks())
    print(f"imported {session}: tracks={len(tracks)}, vias={sum(isinstance(item, pcbnew.PCB_VIA) for item in tracks)}")


def add_ground_zones(board_path: Path) -> None:
    board = pcbnew.LoadBoard(str(board_path))
    nets = board.GetNetsByName()
    ground = nets["/GND"] if nets.has_key("/GND") else None
    if ground is None:
        raise SystemExit("/GND net not found")
    copper_zones = [zone for zone in board.Zones() if not zone.GetIsRuleArea()]
    if not copper_zones:
        for layer, priority, name in (
            (pcbnew.B_Cu, 0, "GND RETURN PLANE"),
            (pcbnew.F_Cu, 1, "GND TOP POUR"),
        ):
            zone = pcbnew.ZONE(board)
            zone.SetLayer(layer)
            zone.SetNet(ground)
            zone.SetZoneName(name)
            zone.SetLocalClearance(mm(0.30))
            zone.SetMinThickness(mm(0.20))
            zone.SetPadConnection(pcbnew.ZONE_CONNECTION_FULL)
            zone.SetAssignedPriority(priority)
            outline = zone.Outline()
            outline.NewOutline()
            for x, y in ((0.5, 0.5), (69.5, 0.5), (69.5, 59.5), (0.5, 59.5)):
                outline.Append(mm(x), mm(y))
            board.Add(zone)
    pcbnew.ZONE_FILLER(board).Fill(board.Zones())
    pcbnew.SaveBoard(str(board_path), board)
    print(f"filled two GND pours; total zones/rule areas={board.GetAreaCount()}")


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    export = sub.add_parser("export-dsn")
    export.add_argument("board", type=Path)
    export.add_argument("output", type=Path)
    imp = sub.add_parser("import-ses")
    imp.add_argument("board", type=Path)
    imp.add_argument("session", type=Path)
    zones = sub.add_parser("add-ground-zones")
    zones.add_argument("board", type=Path)
    args = parser.parse_args()
    if args.command == "export-dsn":
        export_dsn(args.board, args.output)
    elif args.command == "import-ses":
        import_ses(args.board, args.session)
    else:
        add_ground_zones(args.board)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
