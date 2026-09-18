"""Read-only enclosure artifact checks. Run with FreeCAD's bundled Python."""
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True

import FreeCAD as App
import Mesh
import Part

ROOT = Path(__file__).resolve().parent
C6 = ROOT / "esp32-c6-touch-amoled-1.64"
S3 = ROOT / "esp32-s3-lcd-1.47b"


def check_part(directory, name, flip_height=None):
    shape = Part.Shape()
    shape.read(str(directory / (name + ".step")))
    assert shape.isValid() and len(shape.Solids) == 1, (directory.name, name, "solid")
    mesh = Mesh.Mesh(str(directory / (name + ".stl")))
    assert mesh.isSolid(), (name, "open mesh")
    assert not mesh.hasNonManifolds(), (name, "non-manifold")
    assert not mesh.hasSelfIntersections(), (name, "self-intersection")
    assert mesh.countComponents() == 1, (name, "disconnected mesh")
    assert abs(mesh.BoundBox.ZMin) < 1e-5, (name, "not on print bed")
    volume_error = abs(abs(mesh.Volume) - shape.Volume) / shape.Volume
    assert volume_error < 0.01, (name, "mesh/STEP volume mismatch", volume_error)
    if directory == C6 and name == "front-shell":
        flip_height = 15.1
    if flip_height is not None:
        mesh.transform(App.Placement(
            App.Vector(0, 0, flip_height), App.Rotation(App.Vector(1, 0, 0), 180),
        ).toMatrix())
    if directory.name == "mounts":
        mesh.translate(shape.BoundBox.XMin-mesh.BoundBox.XMin,
                       shape.BoundBox.YMin-mesh.BoundBox.YMin,
                       shape.BoundBox.ZMin-mesh.BoundBox.ZMin)
    for axis in ("XMin", "XMax", "YMin", "YMax", "ZMin", "ZMax"):
        assert abs(getattr(mesh.BoundBox, axis) - getattr(shape.BoundBox, axis)) < 0.1, (
            name, "mesh/STEP extent mismatch", axis,
        )
    print(f"{directory.name}/{name}: valid single solid; closed manifold mesh; "
          f"{mesh.CountFacets} triangles; volume error {volume_error:.6f}")
    return shape


def check_document(path, expected):
    doc = App.openDocument(str(path))
    try:
        for name, step in expected.items():
            obj = doc.getObject(name)
            assert obj is not None, (path.name, name, "missing object")
            shape = obj.Shape
            assert shape.isValid() and len(shape.Solids) == 1, (path.name, name)
            mismatch = shape.cut(step).Volume + step.cut(shape).Volume
            assert mismatch < 0.01, (path.name, name, "FCStd/STEP mismatch", mismatch)
    finally:
        App.closeDocument(doc.Name)
    print(f"{path.name}: document solids match STEP")


def main():
    preserved = json.loads((ROOT / "c6-preservation.json").read_text())["files"]
    for name, expected in preserved.items():
        content = (C6 / name).read_bytes()
        actual = hashlib.sha256(content).hexdigest()
        normalized = hashlib.sha256(content.replace(b"\r\n", b"\n")).hexdigest()
        assert actual == expected["sha256"] or normalized == expected.get("lf_sha256"), (
            "C6 preservation", name,
        )
    print(f"C6 preservation: {len(preserved)} original files "
          "(Git text line-ending conversion allowed)")
    front = check_part(C6, "front-shell")
    rear = check_part(C6, "back-cover")
    overlap = front.common(rear).Volume
    assert overlap < 0.01, ("C6 assembled overlap", overlap)
    print(f"C6 assembled overlap: {overlap:.9f} mm^3")
    check_document(C6 / "pixel-pal-monitor-pod.FCStd", {
        "front_shell": front, "back_cover": rear,
    })
    tray = check_part(S3, "clearance-tray")
    # Check that the released S3 solid matches the parametric generator.
    import importlib.util
    spec = importlib.util.spec_from_file_location("s3_parameters", S3 / "build_enclosure.py")
    parameters = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parameters)
    bezel = check_part(S3, "front-bezel", flip_height=parameters.DEPTH)
    check_document(S3 / "s3b-monitor-tray.FCStd", {"ClearanceTray": tray, "FrontBezel": bezel})
    generated_bezel = parameters.make_bezel()
    assert bezel.cut(generated_bezel).Volume + generated_bezel.cut(bezel).Volume < 1e-6
    assembly_checks = parameters.check_assembly(tray, bezel)
    assert (parameters.VENT_W, parameters.VENT_H, parameters.FLOOR) == (10.0, 2.0, 2.8)
    assert abs(parameters.POCKET_W-parameters.APERTURE_W-3.0) < 1e-6
    assert abs((parameters.POCKET_W-parameters.BUTTON_ENVELOPE_W)/2-.5) < 1e-6
    assert parameters.VENT_Y == (-8.0, -2.0, 4.0, 10.0)
    c6_vents = parameters.check_rear_vents(rear, floor_depth=2.8)
    s3_vents = parameters.check_rear_vents(tray, floor_depth=2.8)
    assert c6_vents == s3_vents, "rear vent interface differs between boards"
    # Check the shipped arms, including their pressure-fit blades, on both cases.
    for name in ("monitor-arm", "desk-arm"):
        mount = check_part(ROOT / "mounts", name)
        for board, cover in (("C6", rear), ("S3", tray)):
            body = mount.common(Part.makeBox(100,200,30,App.Vector(-50,-160,-30)))
            assert body.common(cover).Volume < 1e-6, (board,name,"mount body collision")
            tips = mount.common(Part.makeBox(100,100,20,App.Vector(-50,-50,0)))
            assert abs(tips.BoundBox.ZMax - 2.6) < 1e-6
            assert 2.8-tips.BoundBox.ZMax >= .199999, "tab enters electronics cavity"
            # Only 0.03 mm/side at the roots may interfere for the friction fit.
            allowed = Part.Shape()
            for y in (-7,11):
                slot = Part.makeBox(10,2.06,2.8,App.Vector(-5,y-1.03,0))
                allowed = slot if allowed.isNull() else allowed.fuse(slot)
            assert mount.common(cover).cut(allowed).Volume < 1e-6
            for y in (-2,4):
                air = Part.makeBox(10,2,17,App.Vector(-5,y,-14.1))
                assert mount.common(air).Volume < 1e-6, "middle vent blocked"
            print(f"{board}/{name}: body clears; tabs stop 0.2 mm inside floor; middle vents open")
    generated, cavity, service = parameters.make_tray()
    assert tray.cut(generated).Volume + generated.cut(tray).Volume < 1e-6
    assert tray.common(cavity).Volume < 1e-6
    assert tray.common(service).Volume < 1e-6
    for keepout in parameters.button_keepouts():
        assert tray.common(keepout).Volume < 1e-6, "S3 button clearance"
    button_gap = (
        (tray.BoundBox.XLength - parameters.BOARD_W) / 2
        - parameters.CLEARANCE - parameters.BUTTON_PROTRUSION
    )
    assert button_gap >= parameters.BUTTON_PRINT_CLEARANCE - 1e-6
    report = json.loads((S3 / "validation.json").read_text())
    assert abs(tray.Volume - report["clearance-tray"]["volume_mm3"]) < 0.001
    assert report["assembly"]["board_interference_checked"] is False
    assert report["user_measurements_mm"]["boot_reset_lateral_protrusion_approx"] == 1.5
    assert report["user_measurements_mm"]["support_feet_to_glass_top"] == 7.7
    for key, value in assembly_checks.items():
        assert report["assembly"][key] == value, key
    assert abs(bezel.Volume - report["front-bezel"]["volume_mm3"]) < 0.001
    assert report["assembly"]["button_keepout_width_mm"] == round(parameters.BUTTON_ENVELOPE_W, 2)
    assert report["clearance-tray"]["button_keepout_overlap_mm3"] == [0.0, 0.0]
    assert report["rear_vent_interface"]["openings"] == s3_vents["openings"]
    assert report["rear_vent_interface"]["pitch_mm"] == [6.0, 6.0, 6.0]
    print("C6/S3 rear vents identical: four 10 x 2 mm rectangles, 6 mm pitch, "
          "lower Y edges -8/-2/4/10 mm; rear tunnel depth 2.8 mm.")
    print("S3 two-piece case: no part overlap; assumed glass envelope has 0.3 mm clearance; "
          "removal sweep samples clear. Actual glass outline remains unverified.")
    print(f"S3 button keepouts clear: 1.5 mm user protrusion + "
          f"{parameters.BUTTON_PRINT_CLEARANCE} mm print clearance + "
          f"centred board; full-length pocket widened 3 mm; mount-plane gap {button_gap:.2f} mm.")
    optional_spec = importlib.util.spec_from_file_location(
        "pin_access_checks", ROOT / "pin-access" / "build_pin_access.py")
    optional = importlib.util.module_from_spec(optional_spec)
    optional_spec.loader.exec_module(optional)
    optional_report = optional.verify()
    assert optional_report == json.loads((ROOT / "pin-access" / "validation.json").read_text())
    print("PASS: CAD checks only. Neither board fit nor retention is physically validated.")


if __name__ == "__main__":
    main()
