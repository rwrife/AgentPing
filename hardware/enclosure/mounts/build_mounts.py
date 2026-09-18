"""FreeCAD mount prototypes, mm. Run with FreeCAD's bundled Python."""
from pathlib import Path
import json
import FreeCAD as App
import Part, MeshPart

OUT = Path(__file__).resolve().parent
V = App.Vector
LENGTH, WIDTH, THICKNESS, CABLE = 152.4, 26., 6., 7.8
MONITOR_PAD, PAD_SLOT_GAP = 42., 4.
MONITOR_PAD_RISE = 4.
STANDOFF = 4.
ARM_TOP, EDGE_RADIUS = 15., 3.
MOUNT_BOTTOM, SLOT_MOUNT_GAP = -11., 10.
SLOT_TOP = MOUNT_BOTTOM-SLOT_MOUNT_GAP
# Enclosure coordinates: rear exterior z=0, electronics toward +z.
# The two selected slots are x=-5..5, y=-8..-6 and y=10..12.
TAB_WIDTH, TAB_DEPTH = 9.7, 2.6
TAB_GRIP = 2.16  # 0.1 mm thicker than the original 2.06 mm grip.

def box(w,h,d,x,y,z):
    return Part.makeBox(w,h,d,V(x,y,z))

def round_plate(w,h,r,z,d,cx=0,cy=0):
    # Canonical coordinates keep shared curved edges identical after fusion.
    left,right=round(cx-w/2,10),round(cx+w/2,10)
    bottom,top=round(cy-h/2,10),round(cy+h/2,10)
    s=box(w-2*r,h,d,left+r,bottom,z).fuse(box(w,h-2*r,d,left,bottom+r,z))
    for x in (left+r,right-r):
        for y in (bottom+r,top-r):
            s=s.fuse(Part.makeCylinder(r,d,V(x,y,z)))
    return s.removeSplitter()

def tab(y,grip):
    # Split tapered blade: elastic pressure across the 2 mm slot dimension.
    p=[V(-TAB_WIDTH/2,y-grip/2,-.15),V(-TAB_WIDTH/2,y+grip/2,-.15),
       V(-TAB_WIDTH/2,y+.85,TAB_DEPTH),V(-TAB_WIDTH/2,y-.85,TAB_DEPTH)]
    s=Part.Face(Part.makePolygon(p+[p[0]])).extrude(V(TAB_WIDTH,0,0))
    return s.cut(box(TAB_WIDTH+.2,.6,2.3,-TAB_WIDTH/2-.1,y-.3,.4))

def adapter(grip=TAB_GRIP):
    # Match the arm's outline and blend the raised plate into the solid neck.
    # The 10 mm ramp rises from the arm face to the 4 mm raised tab plate.
    s=round_plate(WIDTH,ARM_TOP-SLOT_TOP,EDGE_RADIUS,-4,4,
                  cy=(ARM_TOP+SLOT_TOP)/2)
    # Keep the ramp toe straight across the arm; only the top corners round.
    s=s.fuse(box(WIDTH,ARM_TOP-SLOT_TOP-EDGE_RADIUS,4,
                 -WIDTH/2,SLOT_TOP,-4)).removeSplitter()
    p=[V(-WIDTH/2,SLOT_TOP,-4),V(-WIDTH/2,MOUNT_BOTTOM,0),
       V(-WIDTH/2,ARM_TOP,0),V(-WIDTH/2,ARM_TOP,-4)]
    profile=Part.Face(Part.makePolygon(p+[p[0]])).extrude(V(WIDTH,0,0))
    s=s.common(profile)
    s.translate(V(0,0,-STANDOFF))
    for y in (-7,11):
        # Raised shoulder rails set the air gap; tips still stop inside the floor.
        # Inset shoulders 1 mm from each side, clear of the rounded outer edge.
        s=s.fuse(round_plate(WIDTH-2,4,1,-STANDOFF-.1,STANDOFF+.1,cy=y)).fuse(tab(y,grip))
    return s.removeSplitter()

def arm(desk=False):
    s=round_plate(WIDTH,LENGTH,EDGE_RADIUS,-10,THICKNESS,cy=ARM_TOP-LENGTH/2)
    bottom=15-LENGTH
    if not desk:
        s=s.fuse(round_plate(MONITOR_PAD,MONITOR_PAD,3,-10,
                             THICKNESS+MONITOR_PAD_RISE,cy=bottom+MONITOR_PAD/2))
    # Through-slot widened 30% from 6 mm for USB connector clearance.
    slot_bottom=bottom+(14 if desk else MONITOR_PAD+PAD_SLOT_GAP)
    slot_top=SLOT_TOP
    s=s.cut(round_plate(CABLE,slot_top-slot_bottom,2,-10.1,6.2,cy=(slot_top+slot_bottom)/2))
    if desk:
        # Integral tongue extends an extra 6 mm into the separate flat base.
        s=s.fuse(box(18,6.2,6,-9,15-LENGTH-6,-10))
    s.translate(V(0,0,-STANDOFF))
    return s.removeSplitter()

def foot():
    # Broad platform projects toward the screen (+z), making an L in profile.
    # Construct in print coordinates, then orient into assembly coordinates.
    s=round_plate(80,85,5,0,6,cy=27.5)
    s=s.cut(box(18.3,6.3,6.2,-9.15,-3.15,-.1))
    # Lead-in for the tongue, 0.15 mm per-side assembly clearance.
    s.rotate(V(0,0,0),V(1,0,0),90)
    s.translate(V(0,15-LENGTH,-7-STANDOFF))
    return s.removeSplitter()

parts={'monitor-arm':arm().fuse(adapter()).removeSplitter(),'desk-arm':arm(True).fuse(adapter()).removeSplitter(),'desk-foot':foot()}
for grip in (1.98,2.06,2.14,TAB_GRIP):
    s=round_plate(14,8,1,-4,4).fuse(tab(0,grip)).removeSplitter()
    parts[f'fit-tab-{grip:.2f}']=s

doc=App.newDocument('AgentPingMounts')
report={'prototype':True,'arm_length_mm':LENGTH,'cable_slot_mm':CABLE,
        'cable_slot_to_tab_plate_mm':SLOT_MOUNT_GAP,
        'mount_transition_length_mm':SLOT_MOUNT_GAP,
        'housing_to_adapter_air_gap_mm':STANDOFF,
        'housing_to_riser_front_mm':STANDOFF+4,
        'monitor_adhesive_pad_mm':[MONITOR_PAD,MONITOR_PAD],
        'monitor_adhesive_pad_rise_mm':MONITOR_PAD_RISE,
        'monitor_pad_thickness_mm':THICKNESS+MONITOR_PAD_RISE,
        'monitor_arm_to_monitor_clearance_mm':MONITOR_PAD_RISE,
        'monitor_adhesive_face':'front (+Z)',
        'monitor_slot_end_from_bottom_mm':MONITOR_PAD+PAD_SLOT_GAP,
        'tab_grip_mm':TAB_GRIP,
        'tab_insertion_mm':TAB_DEPTH,'rear_floor_mm':2.8,'parts':{},'checks':{}}
for name,shape in parts.items():
    assert shape.isValid() and len(shape.Solids)==1,name
    obj=doc.addObject('PartDesign::Feature',name.replace('-','_').replace('.','_'))
    obj.Label=name;obj.Shape=shape
    shape.exportStep(str(OUT/(name+'.step')))
    printable=shape.copy()
    if name=='desk-foot':
        printable.rotate(V(),V(1,0,0),-90)
    b=printable.BoundBox
    printable.translate(V(-b.XMin,-b.YMin,-b.ZMin))
    mesh=MeshPart.meshFromShape(Shape=printable,LinearDeflection=.06,AngularDeflection=.15,Relative=False)
    assert mesh.isSolid(),name
    mesh.write(str(OUT/(name+'.stl')))
    b=mesh.BoundBox
    report['parts'][name]={'valid_single_solid':True,'closed_mesh':True,
        'print_size_mm':[round(b.XLength,2),round(b.YLength,2),round(b.ZLength,2)],'triangles':mesh.CountFacets}

for a,b in [('desk-arm','desk-foot')]:
    overlap=parts[a].common(parts[b]).Volume
    assert overlap<.001,(a,b,overlap)
    report['checks'][a+' / '+b+' overlap_mm3']=round(overlap,6)
cover=Part.Shape();cover.read(str(OUT.parent/'esp32-c6-touch-amoled-1.64'/'back-cover.step'))
# Press-fit tabs intentionally interfere with vent walls. The rest must clear.
adapter_body=parts['monitor-arm'].common(box(100,100,20,-50,-50,-20))
assert adapter_body.common(cover).Volume<.001
assert parts['desk-arm'].common(box(100,100,20,-50,-50,-20)).common(cover).Volume<.001
report['checks']['adapter_body_clears_enclosure']=True
gap_probe=box(24,13.8,STANDOFF-.2,-12,-4.9,-STANDOFF+.1)
assert parts['monitor-arm'].common(gap_probe).Volume<.001
report['checks']['air_gap_between_shoulders_clear']=True
report['checks']['tabs_stop_before_inner_floor_mm']=round(2.8-TAB_DEPTH,2)
report['checks']['nominal_tab_interference_per_side_mm']=round((TAB_GRIP-2)/2,2)
# Keep the raised bonding face on +Z and the cable-bearing stem behind it.
monitor=parts['monitor-arm']
pad_bottom=ARM_TOP-LENGTH
added=round_plate(MONITOR_PAD,MONITOR_PAD,3,-10-STANDOFF+THICKNESS,
                  MONITOR_PAD_RISE,cy=pad_bottom+MONITOR_PAD/2)
assert added.cut(monitor).Volume < .001
clearance=box(100,SLOT_TOP-(pad_bottom+MONITOR_PAD),MONITOR_PAD_RISE,
              -50,pad_bottom+MONITOR_PAD,-10-STANDOFF+THICKNESS)
assert monitor.common(clearance).Volume < .001
report['checks']['monitor_arm_clear_of_adhesive_plane_mm']=MONITOR_PAD_RISE
preview_dir=OUT/'preview-geometry';preview_dir.mkdir(exist_ok=True)
front=Part.Shape();front.read(str(OUT.parent/'esp32-c6-touch-amoled-1.64'/'front-shell.step'))
for name,shape in {**{k:v for k,v in parts.items() if not k.startswith('fit-tab')},'back-cover':cover,'front-shell':front}.items():
    MeshPart.meshFromShape(Shape=shape,LinearDeflection=.08,AngularDeflection=.15,Relative=False).write(str(preview_dir/(name+'.stl')))
doc.recompute();doc.saveAs(str(OUT/'mounts.FCStd'))
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
