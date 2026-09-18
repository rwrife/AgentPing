"""Generate the unprinted ESP32-S3-LCD-1.47B C6-style snap-fit pod; mm."""
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
BUTTON_ENVELOPE_W = BOARD_W + 2 * BUTTON_PROTRUSION
BUTTON_WIDTH_ALLOWANCE = 3.0
# Match the C6 rear vent interface, including its through-floor depth.
FLOOR = 2.8
VENT_W, VENT_H = 10.0, 2.0
VENT_Y = (-8.0, -2.0, 4.0, 10.0)
MODULE_HEIGHT = 7.7  # User: rear support/feet to top of glass, 2026-09-18.
SUPPORT_PAD = 0.5  # Printed risers replace the former 0.5 mm adhesive allowance.
SUPPORT_RADIUS = 2.0
SCREW_RADIUS = 1.15
HEAD_RADIUS, HEAD_DEPTH = 2.1, 1.3
# Manufacturer dimensioned rear photo, USB toward -Y. The two pairs differ.
# USB pair: 2.40 mm from short edge, 2.00 mm from long edges.
# Antenna pair: 1.97 mm from short edge, 3.52 mm from long edges.
SUPPORTS = [(side*(BOARD_W/2-edge), y) for edge,y in (
    (2.0, -BOARD_H/2+2.4), (3.52, BOARD_H/2-1.97)) for side in (-1,1)]
GLASS_CLEARANCE = 0.3
FRONT_RIM = 1.2
GLASS_TOP = FLOOR + SUPPORT_PAD + MODULE_HEIGHT
BEZEL_Z = GLASS_TOP + GLASS_CLEARANCE
SPLIT = 6.9
FIT_CLEARANCE = 0.25
POCKET_DEPTH = BEZEL_Z - FLOOR
APERTURE_W = BOARD_W + 2 * CLEARANCE
POCKET_W = APERTURE_W + BUTTON_WIDTH_ALLOWANCE
POCKET_H = BOARD_H + 2 * CLEARANCE
BODY_W, BODY_H = 42.0, 56.0
DEPTH = BEZEL_Z + FRONT_RIM
SERVICE_TOP_Y = -7.0
USB_TOP = SPLIT + 3.0
EPS = 0.1


def box(w, h, d, x, y, z):
    return Part.makeBox(w, h, d, V(x, y, z))


def button_keepouts():
    # Centred, adhesively located board; the wide pocket leaves 0.5 mm per side.
    # Check the entire length rather than assuming button positions.
    inner_x = BOARD_W / 2 - CLEARANCE
    outer_x = BUTTON_ENVELOPE_W / 2
    return [
        box(
            outer_x - inner_x, POCKET_H,
            POCKET_DEPTH-SUPPORT_PAD, x, -POCKET_H / 2, FLOOR+SUPPORT_PAD,
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


def rounded(w, h, r, z, depth):
    shape = box(w-2*r, h, depth, -w/2+r, -h/2, z).fuse(
        box(w, h-2*r, depth, -w/2, -h/2+r, z))
    for x in (-w/2+r, w/2-r):
        for y in (-h/2+r, h/2-r):
            shape = shape.fuse(Part.makeCylinder(r, depth, V(x,y,z)))
    return shape.removeSplitter()


def snap_features():
    # C6 interface shifted down 2.6 mm for the thinner S3 stack.
    shift = DEPTH - 15.1
    for side in (-1, 1):
        for y in (-8, 8):
            beam = box(1, 5, 9.5+shift, 18, y-2.5, FLOOR)
            pts = [V(18.9,y-2.5,10.9+shift), V(19.5,y-2.5,10.9+shift),
                   V(18.9,y-2.5,12.3+shift), V(18.9,y-2.5,10.9+shift)]
            catch = beam.fuse(Part.Face(Part.makePolygon(pts)).extrude(V(0,5,0)))
            channel = box(3,5.6,10.5+shift,17,y-2.8,FLOOR)
            pocket = box(.7,5.6,1,19.15,y-2.8,10.7+shift)
            release = box(1,5.6,FLOOR+.2,17,y-2.8,-.1)
            shapes = (catch, channel, pocket, release)
            if side < 0:
                shapes = tuple(shape.mirror(V(),V(1,0,0)) for shape in shapes)
            yield shapes


def make_tray():
    tray = rounded(BODY_W, BODY_H, 5, 0, SPLIT)
    tray = tray.fuse(rounded(38,52,3,SPLIT-.1,2.9))
    # Full rectangular board envelope avoids assuming rounded glass corners.
    cavity = box(POCKET_W, POCKET_H, DEPTH, -POCKET_W/2,-POCKET_H/2,FLOOR)
    tray = tray.cut(cavity)
    support_shapes = [Part.makeCylinder(SUPPORT_RADIUS,SUPPORT_PAD,V(x,y,FLOOR))
                      for x,y in SUPPORTS]
    for support in support_shapes:
        tray = tray.fuse(support)
        cavity = cavity.cut(support)
    for x,y in SUPPORTS:
        tray = tray.cut(Part.makeCylinder(SCREW_RADIUS,FLOOR+SUPPORT_PAD+.2,V(x,y,-.1)))
        tray = tray.cut(Part.makeCylinder(HEAD_RADIUS,HEAD_DEPTH+.1,V(x,y,-.1)))
    # A bottom-facing USB tunnel; leave the rear floor intact for the mounts.
    service = box(14,12,USB_TOP-3.6,-7,-29,3.6)
    tray = tray.cut(service)
    for catch, channel, pocket, release in snap_features():
        tray = tray.cut(channel).fuse(catch).cut(release)
    for y in VENT_Y:
        tray = tray.cut(box(VENT_W,VENT_H,FLOOR+2*EPS,-VENT_W/2,y,-EPS))
    tray = tray.removeSplitter()
    assert tray.isValid() and len(tray.Solids) == 1
    assert tray.common(cavity).Volume < 1e-6
    assert tray.common(service).Volume < 1e-6
    for keepout in button_keepouts():
        assert tray.common(keepout).Volume < 1e-6
    check_rear_vents(tray)
    return tray, cavity, service


def make_bezel():
    bezel = rounded(BODY_W,BODY_H,5,SPLIT,DEPTH-SPLIT)
    top = [e for e in bezel.Edges
           if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001]
    bezel = bezel.makeChamfer(2,top)
    bezel = bezel.cut(rounded(38.5,52.5,3.25,SPLIT-.1,3.2))
    bezel = bezel.cut(box(POCKET_W+.6,POCKET_H+.6,BEZEL_Z-SPLIT+.1,
                         -POCKET_W/2-.3,-POCKET_H/2-.3,SPLIT-.1))
    bezel = bezel.cut(box(APERTURE_W,POCKET_H,DEPTH,-APERTURE_W/2,-POCKET_H/2,SPLIT-.1))
    for catch, channel, pocket, release in snap_features():
        bezel = bezel.cut(pocket)
    aperture = [e for e in bezel.Edges
                if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001
                and e.BoundBox.XMin >= -APERTURE_W/2-.001
                and e.BoundBox.XMax <= APERTURE_W/2+.001
                and e.BoundBox.YMin >= -POCKET_H/2-.001
                and e.BoundBox.YMax <= POCKET_H/2+.001]
    bezel = bezel.makeChamfer(.5,aperture).removeSplitter()
    assert bezel.isValid() and len(bezel.Solids) == 1
    return bezel


def check_supports(tray):
    # Verify a continuous 0.5 mm annular land around all four M2 holes,
    # above the rear head recess, including the top of each printed pad.
    for x,y in SUPPORTS:
        shaft = Part.makeCylinder(SCREW_RADIUS,FLOOR+SUPPORT_PAD+.2,V(x,y,-.1))
        head = Part.makeCylinder(HEAD_RADIUS,HEAD_DEPTH,V(x,y,0))
        assert tray.common(shaft).Volume < 1e-6
        assert tray.common(head).Volume < 1e-6
        ring = Part.makeCylinder(SCREW_RADIUS+.5,FLOOR+SUPPORT_PAD-HEAD_DEPTH,
                                 V(x,y,HEAD_DEPTH)).cut(shaft)
        assert ring.cut(tray).Volume < 1e-6, "missing support or thin M2 hole land"


def check_assembly(tray, bezel):
    check_supports(tray)
    assert tray.common(bezel).Volume < 1e-6
    glass = box(
        APERTURE_W, POCKET_H, 0.1, -APERTURE_W / 2, -POCKET_H / 2, GLASS_TOP - 0.1,
    )
    for shape in (tray, bezel):
        assert shape.common(glass).Volume < 1e-6
        for keepout in button_keepouts():
            assert shape.common(keepout).Volume < 1e-6
    gap = bezel.distToShape(glass)[0]
    assert abs(gap - GLASS_CLEARANCE) < 1e-6
    # Depress the catches through the rear release slots before removal.
    released = tray
    for catch, channel, pocket, release in snap_features():
        released = released.cut(catch)
    for lift in (0.25, 1.0, 2.0, 4.0):
        lifted = bezel.copy()
        lifted.translate(V(0, 0, lift))
        assert released.common(lifted).Volume < 1e-6
    return {
        "part_overlap_mm3": tray.common(bezel).Volume,
        "assumed_glass_envelope_clearance_mm": round(gap, 6),
        "button_keepout_overlap_mm3": [
            shape.common(keepout).Volume
            for shape in (tray, bezel) for keepout in button_keepouts()
        ],
        "released_catch_removal_sweep_sampled_clear": True,
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
            "outer_corner_radius": 5, "front_chamfer": 2, "floor": FLOOR, "floor_to_rim_underside": POCKET_DEPTH,
            "printed_support_pad": SUPPORT_PAD,
            "glass_clearance": GLASS_CLEARANCE,
            "front_rim": FRONT_RIM,
            "aperture": [APERTURE_W, POCKET_H],
            "internal_pocket": [POCKET_W, POCKET_H],
            "button_width_allowance": BUTTON_WIDTH_ALLOWANCE,
            "centred_button_side_clearance": (POCKET_W-BUTTON_ENVELOPE_W)/2,
            "aperture_basis": "PCB envelope + clearance; glass outline/active-area offset NOT dimensioned",
            "button_z_limit": BEZEL_Z,
            "internal_button_keepout_top_y": POCKET_H/2,
            "usb_tunnel": {"width": 14, "bottom_z": 3.6, "top_z": USB_TOP},
        },
        "assembly": {
            "size_mm": [round(BODY_W, 2), round(BODY_H, 2), DEPTH],
            "printed_parts": 2,
            "mounting_holes": "four 2.3 mm M2 clearance holes; manufacturer-dimensioned positions",
            "support_centres_mm": [[round(x,3),round(y,3)] for x,y in SUPPORTS],
            "printed_support_height_mm": SUPPORT_PAD,
            "printed_support_diameter_mm": 2*SUPPORT_RADIUS,
            "screw_head_recess_mm": [2*HEAD_RADIUS,HEAD_DEPTH],
            "screw_grip_mm": FLOOR+SUPPORT_PAD-HEAD_DEPTH,
            "board_retention": "M2 screws into board metal standoffs, on four printed pads; physical fit unverified",
            "board_interference_checked": False,
            "front_retention": "four C6-style spring catches; rear tool-release slots; no glass clamping",
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
