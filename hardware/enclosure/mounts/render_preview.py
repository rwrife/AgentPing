"""Render the actual generated geometry with Blender; no AI illustration."""
from pathlib import Path
import bpy
from mathutils import Vector
OUT=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def material(name,color):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(*color,1)
    return m
white=material('Warm white polymer',(.75,.78,.8))
blue=material('Mounts',(.12,.36,.48))
black=material('Screen',(.008,.018,.024))
def load(name,offset):
    bpy.ops.wm.stl_import(filepath=str(OUT/'preview-geometry'/(name+'.stl')))
    o=bpy.context.object;o.location.x+=offset;o.data.materials.append(white if name in ('front-shell','back-cover') else blue)
for offset,parts in [(-65,['front-shell','back-cover','monitor-arm']),
                     (65,['front-shell','back-cover','desk-arm','desk-foot'])]:
    for part in parts:load(part,offset)
    bpy.ops.mesh.primitive_cube_add(size=1,location=(offset,0,13.65))
    o=bpy.context.object;o.dimensions=(26.5,41.5,.7);o.data.materials.append(black)
def label(text,location,size=6):
    c=bpy.data.curves.new(text,'FONT');c.body=text;c.size=size;c.align_x='CENTER'
    o=bpy.data.objects.new(text,c);bpy.context.collection.objects.link(o);o.location=location;o.data.materials.append(black)
label('MONITOR',(-65,43,0));label('DESK',(65,43,0))
label('6 in / 152.4 mm arm',(-65,-159,0),4)
label('Flat-print arm + foot',(65,-164,0),4)
bpy.ops.object.camera_add(location=(240,-230,450))
cam=bpy.context.object;target=Vector((0,-55,0));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=310;bpy.context.scene.camera=cam
for loc,power,size in [((0,100,300),2200000,250),((-200,-100,150),1400000,180)]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=32;s.world.color=(.65,.65,.65)
s.render.resolution_x=1500;s.render.resolution_y=1300;s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG';s.render.filepath=str(OUT/'preview.png');s.render.film_transparent=False
bpy.ops.render.render(write_still=True)
cam.location=(-200,-55,35)
cam.rotation_euler=(Vector((-65,0,-2))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=85
s.render.filepath=str(OUT/'standoff-detail.png')
bpy.ops.render.render(write_still=True)
