"""Generate monitor pod prototype with FreeCAD 1.1 bundled Python. Units: mm."""
from pathlib import Path
import json
import FreeCAD as App
import Part, MeshPart
OUT=Path(__file__).resolve().parent
V=App.Vector
# User measured: standoff foot to screen top 9.3; symmetric 22 x 39 hole pattern.
BODY_W,BODY_H=42.0,56.0
BASE=2.8
MODULE_HEIGHT=9.3
SCREEN_CLEARANCE=.3
FRONT_RIM=1.2
DEPTH=BASE+MODULE_HEIGHT+SCREEN_CLEARANCE+FRONT_RIM
SPLIT=8.0
CAVITY_W,CAVITY_H=29.92,45.0
SCREWS=[(x,y) for x in [-11,11] for y in [-19.5,19.5]]
def rounded(w,h,r,z,height):
    shape=Part.makeBox(w-2*r,h,height,V(-w/2+r,-h/2,z)).fuse(Part.makeBox(w,h-2*r,height,V(-w/2,-h/2+r,z)))
    for x in [-w/2+r,w/2-r]:
        for y in [-h/2+r,h/2-r]:shape=shape.fuse(Part.makeCylinder(r,height,V(x,y,z)))
    return shape.removeSplitter()
def box(w,h,d,x,y,z):return Part.makeBox(w,h,d,V(x,y,z))
# Thin front bezel slides over the recessed locating lip on the deeper rear tray.
shell=rounded(BODY_W,BODY_H,5,SPLIT,DEPTH-SPLIT)
front_edges=[e for e in shell.Edges if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001]
shell=shell.makeChamfer(2.0,front_edges)
shell=shell.cut(rounded(38.5,52.5,3.25,SPLIT-.1,3.2))
shell=shell.cut(rounded(CAVITY_W,CAVITY_H,3.5,SPLIT-.1,BASE+MODULE_HEIGHT+SCREEN_CLEARANCE-SPLIT+.1))
shell=shell.cut(rounded(27.2,42.2,2.6,DEPTH-FRONT_RIM-.1,FRONT_RIM+.2))
aperture_edges=[e for e in shell.Edges if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001 and e.BoundBox.XMin>=-13.601 and e.BoundBox.XMax<=13.601 and e.BoundBox.YMin>=-21.101 and e.BoundBox.YMax<=21.101]
shell=shell.makeChamfer(.5,aperture_edges)
cover=rounded(BODY_W,BODY_H,5,0,SPLIT)
cover=cover.fuse(rounded(38,52,3,SPLIT-.1,2.9))
cover=cover.cut(rounded(CAVITY_W,CAVITY_H,3.5,BASE,12))
# Metal standoffs sit directly on the rear floor. Screw heads recess from the back.
for x,y in SCREWS:
    cover=cover.cut(Part.makeCylinder(1.15,BASE+.2,V(x,y,-.1)))
    cover=cover.cut(Part.makeCylinder(2.1,1.4,V(x,y,-.1)))
# Connector/button openings stay in the rear tray; the front rim is continuous.
cutouts=[box(13,8,7.6,-6.5,-29,3.6)]
cutouts += [box(8,10,6,x,-20,4) for x in [-22,14]]
for opening in cutouts:
    cover=cover.cut(opening)
# Isolated rear-tray spring catches engage pockets inside the front bezel.
for side in [-1,1]:
    for y in [-8,8]:
        beam=box(1,5,8,18,y-2.5,BASE)
        pts=[V(18.9,y-2.5,9.4),V(19.5,y-2.5,9.4),V(18.9,y-2.5,10.8),V(18.9,y-2.5,9.4)]
        catch=beam.fuse(Part.Face(Part.makePolygon(pts)).extrude(V(0,5,0)))
        channel=box(3,5.6,9,17,y-2.8,BASE)
        pocket=box(.7,5.6,1,19.15,y-2.8,9.2)
        release=box(1,5.6,BASE+.2,17,y-2.8,-.1)
        if side<0:
            catch=catch.mirror(V(0,0,0),V(1,0,0));channel=channel.mirror(V(0,0,0),V(1,0,0));pocket=pocket.mirror(V(0,0,0),V(1,0,0));release=release.mirror(V(0,0,0),V(1,0,0))
        cover=cover.cut(channel).fuse(catch).cut(release)
        shell=shell.cut(pocket)
for y in [-8,-2,4,10]:cover=cover.cut(box(10,2,BASE+.2,-5,y,-.1))
shell=shell.removeSplitter();cover=cover.removeSplitter()
parts={'front-shell':shell,'back-cover':cover}
report={'assembly':{'size_mm':[BODY_W,BODY_H,DEPTH],'module_height_mm':MODULE_HEIGHT,'hole_pitch_mm':[22,39],'screen_clearance_mm':SCREEN_CLEARANCE,'front_bezel_band_mm':DEPTH-SPLIT,'button_access':'plain side openings; no caps'}}
doc=App.newDocument('PixelPalMonitorPod')
for name,shape in parts.items():
    assert shape.isValid() and len(shape.Solids)==1,(name,'invalid solid')
    obj=doc.addObject('PartDesign::Feature',name.replace('-','_'));obj.Label=name;obj.Shape=shape
    shape.exportStep(str(OUT/(name+'.step')))
    printable=shape.copy()
    if name=='front-shell':
        printable.rotate(V(0,0,0),V(1,0,0),180)
        printable.translate(V(0,0,DEPTH))
    mesh=MeshPart.meshFromShape(Shape=printable,LinearDeflection=.08,AngularDeflection=.15,Relative=False)
    assert mesh.isSolid(),name
    mesh.write(str(OUT/(name+'.stl')))
    b=mesh.BoundBox
    report[name]={'valid_solid':True,'volume_mm3':round(shape.Volume,1),'size_mm':[round(b.XLength,2),round(b.YLength,2),round(b.ZLength,2)],'triangles':mesh.CountFacets}
assert shell.common(cover).Volume<.01
doc.recompute();doc.saveAs(str(OUT/'pixel-pal-monitor-pod.FCStd'))
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
