"""Generate the unprinted ESP32-S3-LCD-1.47B removable-bezel case; mm."""
import json
from pathlib import Path

import FreeCAD as App
import MeshPart
import Part

OUT = Path(__file__).resolve().parent
V = App.Vector

# Only the PCB outline is dimensioned in the manufacturer's B-board drawing.
BOARD_W, BOARD_H = 20.32, 36.37
CLEARANCE = 0.5
# User measured approximately 1.5 mm button protrusion on 2026-09-18.
BUTTON_PROTRUSION = 1.5
BUTTON_PRINT_CLEARANCE = 0.5
BUTTON_ENVELOPE_W = BOARD_W + 2 * (
    CLEARANCE + BUTTON_PROTRUSION + BUTTON_PRINT_CLEARANCE
)
WALL = 2.0
# Match the C6 rear vent interface, including its through-floor depth.
FLOOR = 2.8
VENT_W, VENT_H = 10.0, 2.0
VENT_Y = (-8.0, -2.0, 4.0, 10.0)
MODULE_HEIGHT = 7.7  # User: rear support/feet to top of glass, 2026-09-18.
SUPPORT_PAD = 0.5  # Assumed installed insulating adhesive thickness; measure it.
GLASS_CLEARANCE = 0.3
FRONT_RIM = 1.2
GLASS_TOP = FLOOR + SUPPORT_PAD + MODULE_HEIGHT
BEZEL_Z = GLASS_TOP + GLASS_CLEARANCE
SPLIT = 8.0
LIP_WALL = 0.75
FIT_CLEARANCE = 0.25
TRAY_TOP = BEZEL_Z - FIT_CLEARANCE
POCKET_DEPTH = BEZEL_Z - FLOOR
POCKET_W = BOARD_W + 2 * CLEARANCE
POCKET_H = BOARD_H + 2 * CLEARANCE
BODY_W = POCKET_W + 2 * WALL
BODY_H = POCKET_H + 2 * WALL
DEPTH = BEZEL_Z + FRONT_RIM
SERVICE_TOP_Y = -7.0
USB_FLOOR_NOTCH_W = 14.0
USB_FLOOR_NOTCH_DEPTH = 5.0
EPS = 0.1


def box(w, h, d, x, y, z):
    return Part.makeBox(w, h, d, V(x, y, z))


def button_keepouts():
    # Include worst-case board float in both directions. Longitudinal location
    # and Z are deliberately broad assumptions, not measured switch bounds.
    inner_x = BOARD_W / 2 - CLEARANCE
    outer_x = BUTTON_ENVELOPE_W / 2
    return [
        box(
            outer_x - inner_x, SERVICE_TOP_Y + BODY_H / 2,
            POCKET_DEPTH, x, -BODY_H / 2, FLOOR,
        )
        for x in (-outer_x, inner_x)
    ]


def check_rear_vents(shape, floor_depth=FLOOR):
    openings = []
    for face in shape.Faces:
        if abs(face.BoundBox.ZMin) > 1e-6 or abs(face.BoundBox.ZMax) > 1e-6:
            continue
        for wire in face.Wires:
            if wire.isSame(face.OuterWire):
                continue
            bounds = wire.BoundBox
            if abs(bounds.XMin + bounds.XMax) < 1e-6:
                openings.append((bounds, Part.Face(wire).Area, len(wire.Edges)))
    openings.sort(key=lambda entry: entry[0].YMin)
    assert len(openings) == 4, "expected four centred rear vents"
    measured = []
    for (bounds, area, edges), y in zip(openings, VENT_Y):
        assert abs(bounds.XMin + VENT_W / 2) < 1e-6
        assert abs(bounds.XLength - VENT_W) < 1e-6, "vent width changed"
        assert abs(bounds.YMin - y) < 1e-6
        assert abs(bounds.YLength - VENT_H) < 1e-6, "vent height changed"
        assert edges == 4 and abs(area - VENT_W * VENT_H) < 1e-6
        tunnel = box(VENT_W, VENT_H, floor_depth + 2 * EPS, -VENT_W / 2, y, -EPS)
        assert shape.common(tunnel).Volume < 1e-6, "blocked rear vent"
        # Probe the wall just beside each slot to verify the rear engagement depth.
        wall_probe = box(0.1, 0.1, floor_depth + 1, VENT_W / 2 + 0.1, y + 0.5, 0)
        wall = shape.common(wall_probe)
        assert abs(wall.BoundBox.ZLength - floor_depth) < 1e-6, "vent tunnel depth changed"
        measured.append({
            "width_mm": round(bounds.XLength, 6),
            "height_mm": round(bounds.YLength, 6),
            "lower_left_mm": [round(bounds.XMin, 6), round(bounds.YMin, 6)],
            "centre_mm": [
                round((bounds.XMin + bounds.XMax) / 2, 6),
                round((bounds.YMin + bounds.YMax) / 2, 6),
            ],
            "tunnel_depth_mm": round(wall.BoundBox.ZLength, 6),
        })
    pitch = [
        round(measured[i + 1]["centre_mm"][1] - measured[i]["centre_mm"][1], 6)
        for i in range(3)
    ]
    assert pitch == [6.0, 6.0, 6.0], "rear vent pitch changed"
    return {"openings": measured, "pitch_mm": pitch}


def make_tray():
    tray = box(BODY_W, BODY_H, TRAY_TOP, -BODY_W / 2, -BODY_H / 2, 0)
    cavity = box(
        POCKET_W, POCKET_H, POCKET_DEPTH + EPS,
        -POCKET_W / 2, -POCKET_H / 2, FLOOR,
    )
    tray = tray.cut(cavity)
    lip_w, lip_h = POCKET_W + 2 * LIP_WALL, POCKET_H + 2 * LIP_WALL
    outer = box(BODY_W + 2, BODY_H + 2, DEPTH, -BODY_W / 2 - 1, -BODY_H / 2 - 1, SPLIT)
    inner = box(lip_w, lip_h, DEPTH, -lip_w / 2, -lip_h / 2, SPLIT)
    tray = tray.cut(outer.cut(inner))
    # Open the entire USB end and both adjacent button regions: no guessed
    # connector/button centres or close-fitting holes are used.
    service = box(
        max(BODY_W, BUTTON_ENVELOPE_W) + 2 * EPS,
        SERVICE_TOP_Y + BODY_H / 2 + EPS, POCKET_DEPTH + EPS,
        -max(BODY_W, BUTTON_ENVELOPE_W) / 2 - EPS, -BODY_H / 2 - EPS, FLOOR,
    )
    tray = tray.cut(service)
    # Let the USB plug extend below the floor without removing the corner lands.
    tray = tray.cut(box(
        USB_FLOOR_NOTCH_W, USB_FLOOR_NOTCH_DEPTH + EPS, FLOOR + 2 * EPS,
        -USB_FLOOR_NOTCH_W / 2, -BODY_H / 2 - EPS, -EPS,
    ))
    for y in VENT_Y:
        tray = tray.cut(box(
            VENT_W, VENT_H, FLOOR + 2 * EPS, -VENT_W / 2, y, -EPS,
        ))
    tray = tray.removeSplitter()
    assert tray.isValid() and len(tray.Solids) == 1
    assert tray.common(cavity).Volume < 1e-6
    assert tray.common(service).Volume < 1e-6
    assert BODY_W >= BUTTON_ENVELOPE_W - 1e-6, "buttons too close to monitor mounting plane"
    for keepout in button_keepouts():
        assert tray.common(keepout).Volume < 1e-6, "button preload/access interference"
    check_rear_vents(tray)
    return tray, cavity, service


def make_bezel():
    bezel = box(BODY_W, BODY_H, DEPTH - SPLIT, -BODY_W / 2, -BODY_H / 2, SPLIT)
    socket_w = POCKET_W + 2 * (LIP_WALL + FIT_CLEARANCE)
    socket_h = POCKET_H + 2 * (LIP_WALL + FIT_CLEARANCE)
    bezel = bezel.cut(box(
        socket_w, socket_h, BEZEL_Z - SPLIT + EPS,
        -socket_w / 2, -socket_h / 2, SPLIT - EPS,
    ))
    # Full PCB envelope plus lateral tolerance, NOT an invented glass outline.
    bezel = bezel.cut(box(
        POCKET_W, POCKET_H, DEPTH, -POCKET_W / 2, -POCKET_H / 2, SPLIT - EPS,
    ))
    bezel = bezel.cut(box(
        BODY_W + 2 * EPS, SERVICE_TOP_Y + BODY_H / 2 + EPS,
        BEZEL_Z - SPLIT + EPS, -BODY_W / 2 - EPS, -BODY_H / 2 - EPS, SPLIT - EPS,
    )).removeSplitter()
    assert bezel.isValid() and len(bezel.Solids) == 1
    return bezel


def check_assembly(tray, bezel):
    assert tray.common(bezel).Volume < 1e-6
    glass = box(
        POCKET_W, POCKET_H, 0.1, -POCKET_W / 2, -POCKET_H / 2, GLASS_TOP - 0.1,
    )
    for shape in (tray, bezel):
        assert shape.common(glass).Volume < 1e-6
        for keepout in button_keepouts():
            assert shape.common(keepout).Volume < 1e-6
    gap = bezel.distToShape(glass)[0]
    assert abs(gap - GLASS_CLEARANCE) < 1e-6
    # Removing the bezel along +Z must never trap the lip.
    for lift in (0.25, 1.0, 2.0, 4.0):
        lifted = bezel.copy()
        lifted.translate(V(0, 0, lift))
        assert tray.common(lifted).Volume < 1e-6
    return {
        "part_overlap_mm3": tray.common(bezel).Volume,
        "assumed_glass_envelope_clearance_mm": round(gap, 6),
        "button_keepout_overlap_mm3": [
            shape.common(keepout).Volume
            for shape in (tray, bezel) for keepout in button_keepouts()
        ],
        "removal_sweep_sampled_clear": True,
    }


def build():
    tray, cavity, service = make_tray()
    bezel = make_bezel()
    assembly_checks = check_assembly(tray, bezel)
    mesh = MeshPart.meshFromShape(
        Shape=tray, LinearDeflection=0.08, AngularDeflection=0.15, Relative=False,
    )
    assert mesh.isSolid()
    assert not mesh.hasNonManifolds()
    assert not mesh.hasSelfIntersections()
    assert mesh.countComponents() == 1
    assert abs(mesh.BoundBox.ZMin) < 1e-6
    assert abs(abs(mesh.Volume) - tray.Volume) / tray.Volume < 0.01

    doc = App.newDocument("S3BMonitorTray")
    doc.FileName = str(OUT / "s3b-monitor-tray.FCStd")
    obj = doc.addObject("PartDesign::Feature", "ClearanceTray")
    obj.Label = "ESP32-S3-LCD-1.47B - UNPRINTED clearance tray"
    obj.Shape = tray
    front = doc.addObject("PartDesign::Feature", "FrontBezel")
    front.Label = "Removable front - full-board aperture; glass outline UNVERIFIED"
    front.Shape = bezel
    doc.recompute()
    tray.exportStep(str(OUT / "clearance-tray.step"))
    mesh.write(str(OUT / "clearance-tray.stl"))
    bezel.exportStep(str(OUT / "front-bezel.step"))
    printable = bezel.copy()
    printable.rotate(V(0, 0, 0), V(1, 0, 0), 180)
    printable.translate(V(0, 0, DEPTH))
    front_mesh = MeshPart.meshFromShape(
        Shape=printable, LinearDeflection=0.08, AngularDeflection=0.15, Relative=False,
    )
    assert front_mesh.isSolid() and not front_mesh.hasNonManifolds()
    assert not front_mesh.hasSelfIntersections() and front_mesh.countComponents() == 1
    assert abs(front_mesh.BoundBox.ZMin) < 1e-6
    assert abs(abs(front_mesh.Volume) - bezel.Volume) / bezel.Volume < 0.01
    front_mesh.write(str(OUT / "front-bezel.stl"))
    doc.save()
    report = {
        "board": "Waveshare ESP32-S3-LCD-1.47B (no headers)",
        "status": "UNPRINTED fit-check prototype; NOT verified board fit",
        "source_dimensions_mm": {"pcb_outline": [BOARD_W, BOARD_H]},
        "user_measurements_mm": {
            "boot_reset_lateral_protrusion_approx": BUTTON_PROTRUSION,
            "support_feet_to_glass_top": MODULE_HEIGHT,
        },
        "rear_vent_interface": {
            "basis": "user-required exact C6 rear vent compatibility, 2026-09-18",
            **check_rear_vents(tray),
        },
        "assumptions_mm": {
            "lateral_clearance_per_side": CLEARANCE,
            "button_print_clearance": BUTTON_PRINT_CLEARANCE,
            "button_datum": "protrusion applied outside PCB long edges; confirm against actual display",
            "wall": WALL, "floor": FLOOR, "floor_to_rim_underside": POCKET_DEPTH,
            "installed_support_pad": SUPPORT_PAD,
            "glass_clearance": GLASS_CLEARANCE,
            "front_rim": FRONT_RIM,
            "aperture": [POCKET_W, POCKET_H],
            "aperture_basis": "PCB envelope + clearance; glass outline/active-area offset NOT dimensioned",
            "button_z_limit": BEZEL_Z,
            "service_cutout_top_y": SERVICE_TOP_Y,
            "usb_floor_notch": [USB_FLOOR_NOTCH_W, USB_FLOOR_NOTCH_DEPTH],
        },
        "assembly": {
            "size_mm": [round(BODY_W, 2), round(BODY_H, 2), DEPTH],
            "printed_parts": 2,
            "mounting_holes": "none; no guessed board hole pattern",
            "board_retention": "removable insulating adhesive on measured standoff feet; not validated",
            "board_interference_checked": False,
            "front_retention": "slip-fit locating lip + removable exterior tape; no glass clamping",
            "split_z_mm": SPLIT,
            "glass_top_z_mm": GLASS_TOP,
            "rim_underside_z_mm": BEZEL_Z,
            **assembly_checks,
            "button_keepout_width_mm": round(BUTTON_ENVELOPE_W, 2),
            "button_to_mount_plane_clearance_mm": round(
                (BODY_W - BOARD_W) / 2 - CLEARANCE - BUTTON_PROTRUSION, 2,
            ),
        },
        "clearance-tray": {
            "valid_solid": True, "solid_count": len(tray.Solids),
            "volume_mm3": round(tray.Volume, 3),
            "size_mm": [round(v, 2) for v in (
                mesh.BoundBox.XLength, mesh.BoundBox.YLength, mesh.BoundBox.ZLength,
            )],
            "triangles": mesh.CountFacets,
            "closed_mesh": mesh.isSolid(),
            "non_manifold": mesh.hasNonManifolds(),
            "self_intersections": mesh.hasSelfIntersections(),
            "mesh_components": mesh.countComponents(),
            "mesh_volume_error_fraction": abs(abs(mesh.Volume) - tray.Volume) / tray.Volume,
            "print_z_min_mm": mesh.BoundBox.ZMin,
            "nominal_cavity_overlap_mm3": tray.common(cavity).Volume,
            "service_opening_overlap_mm3": tray.common(service).Volume,
            "button_keepout_overlap_mm3": [
                tray.common(keepout).Volume for keepout in button_keepouts()
            ],
        },
        "front-bezel": {
            "valid_solid": True,
            "volume_mm3": round(bezel.Volume, 3),
            "triangles": front_mesh.CountFacets,
            "closed_mesh": front_mesh.isSolid(),
            "non_manifold": front_mesh.hasNonManifolds(),
            "self_intersections": front_mesh.hasSelfIntersections(),
            "print_z_min_mm": front_mesh.BoundBox.ZMin,
            "mesh_volume_error_fraction": abs(abs(front_mesh.Volume) - bezel.Volume) / bezel.Volume,
        },
    }
    (OUT / "validation.json").write_text(json.dumps(report, indent=2) + "\n")
    App.closeDocument(doc.Name)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    build()
