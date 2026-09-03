#!/usr/bin/env python3
"""Add deterministic constrained routes FreeRouting cannot infer safely."""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

import pcbnew


def mm(value: float) -> int:
    return pcbnew.FromMM(value)


def point(x: float, y: float) -> pcbnew.VECTOR2I:
    return pcbnew.VECTOR2I_MM(x, y)


def segment_key(net: str, layer: int, start: tuple[float, float], end: tuple[float, float]) -> tuple:
    return (net, layer, tuple(round(v, 4) for v in start), tuple(round(v, 4) for v in end))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("board", type=Path)
    args = parser.parse_args()
    board = pcbnew.LoadBoard(str(args.board))
    nets = board.GetNetsByName()

    # Remove superseded first-pass geometry so this repair remains repeatable.
    tracks_snapshot = list(board.GetTracks())
    to_remove = []
    for item in tracks_snapshot:
        if isinstance(item, pcbnew.PCB_VIA):
            p = item.GetPosition()
            if item.GetNetname() == "/GPIO6_HAPTIC" and abs(p.x / 1e6 - 8.57) < 0.001 and abs(p.y / 1e6 - 38.63) < 0.001:
                to_remove.append(item)
            elif item.GetNetname() == "/CC2" and any(
                abs(p.x / 1e6 - x) < 0.001 and abs(p.y / 1e6 - y) < 0.001
                for x, y in ((51.3249, 2.2033), (47.4327, 10.7577))
            ):
                to_remove.append(item)
        elif item.GetNetname() == "/VBUS" and item.GetLayer() == pcbnew.B_Cu:
            s, e = item.GetStart(), item.GetEnd()
            points = {(round(s.x / 1e6, 3), round(s.y / 1e6, 3)), (round(e.x / 1e6, 3), round(e.y / 1e6, 3))}
            if points in ({(52.4, 1.12), (52.4, 6.8)}, {(52.4, 6.8), (56.0, 10.4)}):
                to_remove.append(item)
        elif item.GetNetname() == "/CC2" and item.GetLayer() == pcbnew.B_Cu:
            to_remove.append(item)
        elif item.GetNetname() == "/GND" and item.GetLayer() == pcbnew.F_Cu:
            s, e = item.GetStart(), item.GetEnd()
            points = {(round(s.x / 1e6, 3), round(s.y / 1e6, 3)), (round(e.x / 1e6, 3), round(e.y / 1e6, 3))}
            removable_gnd = {
                frozenset({(45.68, 4.673), (54.32, 4.673)}),
                frozenset({(45.68, 9.645), (47.97, 9.645)}),
                frozenset({(47.97, 9.645), (49.825, 11.5)}),
            }
            if frozenset(points) in removable_gnd:
                to_remove.append(item)

    removed = len(to_remove)
    for obsolete in to_remove:
        board.Remove(obsolete)
    if removed:
        pcbnew.SaveBoard(str(args.board), board)
        print(f"cleanup pass removed {removed} superseded item(s); restarting pcbnew")
        os.execv(sys.executable, [sys.executable, str(Path(__file__).resolve()), str(args.board)])

    widened = 0
    for track in board.GetTracks():
        if not isinstance(track, pcbnew.PCB_VIA) and track.GetWidth() < mm(0.20):
            track.SetWidth(mm(0.20))
            widened += 1

    existing_segments = set()
    existing_vias = set()
    for item in board.GetTracks():
        if isinstance(item, pcbnew.PCB_VIA):
            p = item.GetPosition()
            existing_vias.add((item.GetNetname(), round(p.x / 1e6, 4), round(p.y / 1e6, 4)))
        else:
            s, e = item.GetStart(), item.GetEnd()
            a, z = (s.x / 1e6, s.y / 1e6), (e.x / 1e6, e.y / 1e6)
            existing_segments.add(segment_key(item.GetNetname(), item.GetLayer(), a, z))
            existing_segments.add(segment_key(item.GetNetname(), item.GetLayer(), z, a))

    added_segments = 0
    added_vias = 0

    def add_segment(net_name: str, layer: int, start: tuple[float, float], end: tuple[float, float], width: float) -> None:
        nonlocal added_segments
        key = segment_key(net_name, layer, start, end)
        if key in existing_segments:
            return
        track = pcbnew.PCB_TRACK(board)
        track.SetNet(nets[net_name])
        track.SetLayer(layer)
        track.SetStart(point(*start))
        track.SetEnd(point(*end))
        track.SetWidth(mm(width))
        board.Add(track)
        existing_segments.add(key)
        added_segments += 1

    def add_via(net_name: str, xy: tuple[float, float]) -> None:
        nonlocal added_vias
        key = (net_name, round(xy[0], 4), round(xy[1], 4))
        if key in existing_vias:
            return
        via = pcbnew.PCB_VIA(board)
        via.SetNet(nets[net_name])
        via.SetPosition(point(*xy))
        via.SetWidth(mm(0.60))
        via.SetDrill(mm(0.30))
        via.SetLayerPair(pcbnew.F_Cu, pcbnew.B_Cu)
        board.Add(via)
        existing_vias.add(key)
        added_vias += 1

    # USB VBUS duplicated pad groups: bottom-side via-in-pad escapes avoid
    # crossing CC/D+/D-/SBU pads on the 0.65 mm-pitch top side.
    for xy in [(47.6, 1.12), (52.4, 1.12), (58.6, 13.5)]:
        add_via("/VBUS", xy)
    for start, end in [
        ((47.6, 1.12), (52.4, 1.12)),
        ((50.0, 1.12), (50.0, 6.8)),
        ((50.0, 6.8), (56.0, 10.4)),
        ((56.0, 10.4), (58.6, 13.5)),
    ]:
        add_segment("/VBUS", pcbnew.B_Cu, start, end, 0.50)

    # The GND pours bond J3's shell tabs. With the redundant top-side shell
    # strap removed, CC2 can remain entirely on F.Cu without crossing VBUS.
    add_segment("/CC2", pcbnew.F_Cu, (51.3249, 2.2033), (47.4327, 10.7577), 0.20)

    # GPIO6 must go around—not beneath—the mechanically verified module and RF
    # keepout. Use bottom copper with endpoint vias and a 1 mm keepout margin.
    # J1.8 is plated through and needs no co-located via; TP4 is SMD.
    for xy in [(39.0, 28.0)]:
        add_via("/GPIO6_HAPTIC", xy)
    gpio_path = [(8.57, 38.63), (6.0, 41.2), (6.0, 53.5), (34.0, 53.5), (34.0, 33.0), (39.0, 28.0)]
    for start, end in zip(gpio_path, gpio_path[1:]):
        add_segment("/GPIO6_HAPTIC", pcbnew.B_Cu, start, end, 0.25)

    pcbnew.SaveBoard(str(args.board), board)
    print(f"finish route: removed={removed}, widened={widened}, added_segments={added_segments}, added_vias={added_vias}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
