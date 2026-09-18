"""Render the actual optional rear STLs in Blender; no illustrative pins."""
from pathlib import Path
import math
import bpy
from mathutils import Vector

OUT = Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)


def material(name, color):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color,1)
    result.use_nodes = True
    shader = result.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color,1)
    shader.inputs['Roughness'].default_value = .48
    return result


white = material('White PLA',(.82,.84,.85))
text = material('Labels',(.55,.8,.95))
objects = []
for x, label, name in [(-29,'C6','back-cover-pin-access'),
                       (29,'S3','clearance-tray-pin-access')]:
    bpy.ops.wm.stl_import(filepath=str(OUT/'preview-geometry'/(name+'.stl')))
    obj = bpy.context.object
    obj.name = label+' rear housing'
    obj.location.x = x
    obj.data.materials.append(white)
    objects.append(obj)
    curve = bpy.data.curves.new(label,'FONT')
    curve.body = label
    curve.size = 6
    curve.align_x = 'CENTER'
    caption = bpy.data.objects.new(label,curve)
    bpy.context.collection.objects.link(caption)
    caption.location = (x,36,16)
    caption.data.materials.append(text)

target = Vector((0,0,5))
bpy.ops.object.camera_add(location=(0,-110,200))
camera = bpy.context.object
camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 130
scene = bpy.context.scene
scene.camera = camera
for location, power, size in [((-70,-60,160),320000,100),((75,55,140),240000,80)]:
    bpy.ops.object.light_add(type='AREA',location=location)
    light = bpy.context.object
    light.data.energy = power
    light.data.size = size
    light.rotation_euler = (target-light.location).to_track_quat('-Z','Y').to_euler()
scene.world.color = (.13,.13,.13)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.render.resolution_x = 1400
scene.render.resolution_y = 1050
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(OUT/'interior-options.png')
bpy.ops.render.render(write_still=True)
for obj in objects:
    obj.rotation_euler.x = math.pi
    obj.location.z = max(v.co.z for v in obj.data.vertices)
scene.render.filepath = str(OUT/'rear-options.png')
bpy.ops.render.render(write_still=True)
