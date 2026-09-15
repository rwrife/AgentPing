"""Generate monitor pod prototype with FreeCAD 1.1 bundled Python. Units: mm."""
from pathlib import Path
import json
import FreeCAD as App
import Part, MeshPart
OUT=Path(__file__).resolve().parent
V=App.Vector
# Manufacturer envelope plus 0.4 mm clearance on each side.
BODY_W,BODY_H,DEPTH=42.0,56.0,18.8
CAVITY_W,CAVITY_H=29.92,45.0
SCREWS=[(x,y) for x in [-18,18] for y in [-24,24]]
def rounded(w,h,r,z,height):
    shape=Part.makeBox(w-2*r,h,height,V(-w/2+r,-h/2,z)).fuse(Part.makeBox(w,h-2*r,height,V(-w/2,-h/2+r,z)))
    for x in [-w/2+r,w/2-r]:
        for y in [-h/2+r,h/2-r]:shape=shape.fuse(Part.makeCylinder(r,height,V(x,y,z)))
    return shape.removeSplitter()
def box(w,h,d,x,y,z):return Part.makeBox(w,h,d,V(x,y,z))
# Front shell: open at back, full-size pocket, retaining rim only on module perimeter.
shell=rounded(BODY_W,BODY_H,5,2.8,16)
# Broad rolled outer shoulder and a smaller soft lip at the screen aperture.
front_edges=[e for e in shell.Edges if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001]
shell=shell.makeFillet(3.0,front_edges)
shell=shell.cut(rounded(CAVITY_W,CAVITY_H,3.5,2.7,14.5))
shell=shell.cut(rounded(27.2,42.2,2.6,17.1,2))
aperture_edges=[e for e in shell.Edges if abs(e.BoundBox.ZMin-DEPTH)<.001 and abs(e.BoundBox.ZMax-DEPTH)<.001 and e.BoundBox.XMin>=-13.601 and e.BoundBox.XMax<=13.601 and e.BoundBox.YMin>=-21.101 and e.BoundBox.YMax<=21.101]
shell=shell.makeFillet(.65,aperture_edges)
# Broad USB plug and button access; no battery assumed.
shell=shell.cut(box(16,8,11,-8,-29,3.5))
for x in [-22,14]:shell=shell.cut(box(8,10,8,x,-20,5))
for x,y in SCREWS:shell=shell.cut(Part.makeCylinder(1.05,6,V(x,y,2.7)))
shell=shell.removeSplitter()
# Plain removable rear cover. Adhesive goes on either flat SIDE of the shell.
cover=rounded(BODY_W,BODY_H,5,0,2.8)
for x,y in SCREWS:cover=cover.cut(Part.makeCylinder(1.4,4,V(x,y,-.1)))
# Rear ventilation inside the shell outline, away from adhesive strip and fasteners.
for y in [-8,-2,4,10]:cover=cover.cut(box(10,2,4,-5,y,-.1))
cover=cover.removeSplitter()
parts={'front-shell':shell,'back-cover':cover}
report={}
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
    b=shape.BoundBox
    report[name]={'valid_solid':True,'volume_mm3':round(shape.Volume,1),'size_mm':[round(b.XLength,2),round(b.YLength,2),round(b.ZLength,2)],'triangles':mesh.CountFacets}
assert shell.common(cover).Volume<.01

doc.recompute();doc.saveAs(str(OUT/'pixel-pal-monitor-pod.FCStd'))
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
