"""Add optional rear-header housings from released STEP solids; mm.

Run with FreeCAD Python. --check verifies released artifacts without rewriting.
Original housings, fronts and mounts are never overwritten.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import FreeCAD as App
import MeshPart
import Part

OUT = Path(__file__).resolve().parent
ROOT = OUT.parent
V = App.Vector
FLOOR = 2.8
SLOT_WIDTH = 4.0
# Rear-view coordinates: board centred, USB down (-Y), pins towards -Z.
# C6: manufacturer drawing, 22.86 row spacing; 11 pins at 2.54 pitch,
# nearest USB pin 9.33 above the lower edge of the 43.5 mm PCB.
# S3 B: dimensioned photo, 17.78 row spacing; 9 pins at 2.54 pitch,
# nearest USB pin 11.31 from the USB-side edge of the 36.37 mm PCB.
BOARDS = {
    'c6': dict(directory='esp32-c6-touch-amoled-1.64', rear='back-cover',
               front='front-shell', depth=15.1, row_x=11.43, first_y=-12.42,
               count=11, slot_length=29.4, foot_to_rear=4.3, slot_radius=0),
    's3': dict(directory='esp32-s3-lcd-1.47b', rear='clearance-tray',
               front='front-bezel', depth=14.8, row_x=8.89, first_y=-6.875,
               count=9, slot_length=24.0, foot_to_rear=5.6, slot_radius=2.0),
}


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def box(w, h, d, x, y, z):
    return Part.makeBox(w, h, d, V(x, y, z))


def read_step(path):
    shape = Part.Shape()
    shape.read(str(path))
    assert shape.isValid() and len(shape.Solids) == 1, path
    return shape


def slot_y(spec):
    centre = spec['first_y'] + (spec['count']-1)*2.54/2
    return centre-spec['slot_length']/2


def slots(spec, z=-.1, depth=None):
    if depth is None:
        depth = spec['foot_to_rear'] + .2
    result = []
    for side in (-1,1):
        x, y, radius = side*spec['row_x'], slot_y(spec), spec['slot_radius']
        if radius:
            slot = box(SLOT_WIDTH,spec['slot_length']-2*radius,depth,
                       x-SLOT_WIDTH/2,y+radius,z)
            for cy in (y+radius,y+spec['slot_length']-radius):
                slot = slot.fuse(Part.makeCylinder(radius,depth,V(x,cy,z)))
        else:
            slot = box(SLOT_WIDTH,spec['slot_length'],depth,x-SLOT_WIDTH/2,y,z)
        result.append(slot.removeSplitter())
    return result


def rear_variant(spec):
    shape = read_step(ROOT/spec['directory']/(spec['rear']+'.step'))
    for slot in slots(spec):
        shape = shape.cut(slot)
    return shape.removeSplitter()


def export_part(directory, name, shape):
    assert shape.isValid() and len(shape.Solids) == 1, name
    shape.exportStep(str(directory/(name+'.step')))
    printable = shape.copy()
    mesh = MeshPart.meshFromShape(Shape=printable, LinearDeflection=.06,
                                  AngularDeflection=.15, Relative=False)
    assert mesh.isSolid() and not mesh.hasNonManifolds()
    assert not mesh.hasSelfIntersections() and mesh.countComponents() == 1
    mesh.write(str(directory/(name+'.stl')))
    # Assembled coordinates for repeatable previews, not slicer input.
    preview = OUT/'preview-geometry'
    preview.mkdir(exist_ok=True)
    MeshPart.meshFromShape(Shape=shape, LinearDeflection=.06,
                           AngularDeflection=.15, Relative=False).write(str(preview/(name+'.stl')))


def document(path, shapes):
    doc = App.newDocument('PinAccess')
    for name, shape in shapes.items():
        obj = doc.addObject('PartDesign::Feature', name)
        obj.Label = name.replace('_', ' ')
        obj.Shape = shape
    doc.recompute()
    doc.saveAs(str(path))
    App.closeDocument(doc.Name)


def build():
    for spec in BOARDS.values():
        directory = ROOT/spec['directory']
        rear = rear_variant(spec)
        export_part(directory, spec['rear']+'-pin-access', rear)
        document(directory/'pin-access.FCStd', {
            'RearPinAccess': rear,
            'OriginalFront': read_step(directory/(spec['front']+'.step')),
        })

def verify():
    checks = load_module('enclosure_checks', ROOT/'verify_enclosures.py')
    parameters = load_module('s3_geometry', ROOT/BOARDS['s3']['directory']/'build_enclosure.py')
    report = {'prototype': True, 'floor_mm': FLOOR, 'slot_width_mm': SLOT_WIDTH,
              'boards': {}, 'mounts': {}}
    covers = {}
    for board, spec in BOARDS.items():
        directory = ROOT/spec['directory']
        name = spec['rear']+'-pin-access'
        rear = checks.check_part(directory, name)
        original = read_step(directory/(spec['rear']+'.step'))
        expected = rear_variant(spec)
        assert rear.cut(expected).Volume+expected.cut(rear).Volume < .001
        assert rear.cut(original).Volume < .001, 'variant adds material'
        removed = original.cut(rear)
        allowed = slots(spec)[0].fuse(slots(spec)[1])
        assert removed.cut(allowed).Volume < .001, 'changes outside header slots'
        assert abs(removed.Volume-original.common(allowed).Volume) < .001
        assert parameters.check_rear_vents(rear) == parameters.check_rear_vents(original)
        # Verify the nominal continuous header bodies and each square pin have
        # a straight path through the floor, not just matching hole outlines.
        for side in (-1, 1):
            x = side*spec['row_x']
            header = box(2.54, spec['count']*2.54, spec['foot_to_rear']+.2,
                         x-1.27, spec['first_y']-1.27, -.1)
            assert rear.common(header).Volume < .001
            for pin in range(spec['count']):
                probe = box(.64,.64,FLOOR+.2,x-.32,spec['first_y']+pin*2.54-.32,-.1)
                assert rear.common(probe).Volume < .001
        for slot in slots(spec):
            assert rear.common(slot).Volume < .001
        front = read_step(directory/(spec['front']+'.step'))
        assert rear.common(front).Volume < .001
        if board == 's3':
            parameters.check_supports(rear)
        checks.check_document(directory/'pin-access.FCStd', {
            'RearPinAccess': rear, 'OriginalFront': front})
        web = spec['row_x']-SLOT_WIDTH/2-5
        assert web >= 1.8, 'insufficient web beside mounting vents'
        covers[board] = rear
        report['boards'][board] = {
            'part': (directory/name).relative_to(ROOT).as_posix(),
            'rows': 2, 'pins_per_row': spec['count'], 'pin_pitch_mm': 2.54,
            'row_x_mm': [-spec['row_x'], spec['row_x']],
            'first_pin_y_mm': spec['first_y'],
            'slot_y_mm': [round(slot_y(spec),3), round(slot_y(spec)+spec['slot_length'],3)],
            'slot_length_mm': spec['slot_length'], 'vent_to_slot_web_mm': round(web,3),
            'slot_end_radius_mm': spec['slot_radius'],
            'maximum_pin_projection_from_support_feet_for_flush_rear_mm': spec['foot_to_rear'],
            'actual_pin_tip_recess_measured': False,
            'original_front_compatible': True, 'rear_vents_unchanged': True,
            'slot_tunnels_clear': True, 'single_valid_solid': True,
            'volume_mm3': round(rear.Volume,3),
        }
    for name in ('monitor-arm', 'desk-arm'):
        mount = checks.check_part(ROOT/'mounts', name)
        # A pressure fit deliberately interferes with the two vent walls only.
        allowed = box(10,2.06,2.8,-5,-8.03,0).fuse(box(10,2.06,2.8,-5,9.97,0))
        for board, spec in BOARDS.items():
            assert mount.common(covers[board]).cut(allowed).Volume < .001
            # Pins are required to remain at/inside rear datum z=0. Original
            # mount bodies sit at z<=0; only central vent tabs enter the floor.
            for slot in slots(spec, z=0, depth=FLOOR+.1):
                assert mount.common(slot).Volume < .001, (board,name,'recessed pin collision')
            for y in (-2,4):
                assert mount.common(box(10,2,17,-5,y,-14.1)).Volume < .001
        if name == 'desk-arm':
            foot = read_step(ROOT/'mounts'/'desk-foot.step')
            assert mount.common(foot).Volume < .001
        report['mounts'][name] = {'recessed_pin_envelopes_clear': True,
            'original_mount_used': True, 'both_middle_vents_clear': True,
            'single_valid_solid': True, 'volume_mm3': round(mount.Volume,3)}
    print('PASS: optional rear slots; original mounts clear recessed pin envelopes; common vents unchanged.')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    if not args.check:
        build()
    report = verify()
    if args.check:
        assert report == json.loads((OUT/'validation.json').read_text()), 'stale validation report'
    else:
        (OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
