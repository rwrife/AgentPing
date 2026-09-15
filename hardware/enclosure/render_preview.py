import bpy, math
from mathutils import Vector
from pathlib import Path
out=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def mat(name,color,metal=0):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=.42;p.inputs['Metallic'].default_value=metal;return m
shellmat=mat('White PLA',(.82,.84,.85));wingmat=shellmat;glass=mat('Black AMOLED',(.001,.002,.003))
glass.node_tree.nodes.get('Principled BSDF').inputs['Specular IOR Level'].default_value=.12
for name,material in [('front-shell',shellmat),('back-cover',wingmat)]:
 bpy.ops.wm.stl_import(filepath=str(out/(name+'.stl')));o=bpy.context.object;o.name=name;o.data.materials.append(material)
 if name=='front-shell':o.rotation_euler.x=math.pi;o.location.z=18.8
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0,17.65));o=bpy.context.object;o.name='Illustrative screen';o.dimensions=(26.7,41.6,.9);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(glass);bevel=o.modifiers.new('Rounded glass','BEVEL');bevel.width=2;bevel.segments=6
# Illustrative luminous face on the display; not a printed feature.
cyan=mat('Cyan pixels',(.0,.65,1))
shader=cyan.node_tree.nodes.get('Principled BSDF')
shader.inputs['Emission Color'].default_value=(0,.65,1,1)
shader.inputs['Emission Strength'].default_value=2
for x in [-5,5]:
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=(x,4,18.15))
 o=bpy.context.object;o.name='Eye';o.scale=(1.7,2.8,.08);o.data.materials.append(cyan)
curve=bpy.data.curves.new('Smile','CURVE');curve.dimensions='3D';curve.bevel_depth=.65;curve.bevel_resolution=4
spline=curve.splines.new('POLY');spline.points.add(32)
for i,point in enumerate(spline.points):
 t=math.pi+math.pi*i/32;point.co=(4*math.cos(t),-3+3*math.sin(t),18.15,1)
o=bpy.data.objects.new('Smile',curve);bpy.context.collection.objects.link(o);o.data.materials.append(cyan)
bpy.ops.object.camera_add(location=(-82,-100,130));cam=bpy.context.object;target=Vector((0,0,5));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=105;bpy.context.scene.camera=cam
for loc,power,size in [((-30,-40,100),180000,80),((70,30,80),120000,65)]:
 bpy.ops.object.light_add(type='AREA',location=loc);l=bpy.context.object;l.data.energy=power;l.data.shape='DISK';l.data.size=size;l.rotation_euler=(Vector((0,0,0))-l.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=32;s.world.color=(.25,.25,.25);s.render.resolution_x=1100;s.render.resolution_y=900;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.filepath=str(out/'preview.png');s.render.film_transparent=False;bpy.ops.wm.save_as_mainfile(filepath=str(out/'preview.blend'));bpy.ops.render.render(write_still=True)
