"""Small authored Cryo presentation prop. Original models remain untouched."""
import bpy,math,pathlib,json
from mathutils import Vector
OUT=pathlib.Path(__file__).resolve().parent.parent/'exports';OUT.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.context.scene.unit_settings.system='METRIC'
COLORS={'Red':(.83,.19,.16,1),'Gold':(1,.62,.17,1),'Navy':(.07,.12,.23,1),'Ivory':(.91,.94,.97,1)}
material=bpy.data.materials.new('Polish_VertexPaint');material.diffuse_color=(1,1,1,1)
objects=[]
def V(p):return Vector((p[0],-p[2],p[1]))
def finish(o,name,color,bevel=0):
 o.name=name;o.data.materials.append(material);bpy.context.view_layer.objects.active=o
 if bevel:
  m=o.modifiers.new('Rounded moulded edge','BEVEL');m.width=bevel;m.segments=2;bpy.ops.object.modifier_apply(modifier=m.name)
 for p in o.data.polygons:p.use_smooth=True
 m=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');m.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=m.name)
 colors=o.data.color_attributes.new(name='Paint',type='FLOAT_COLOR',domain='POINT')
 paint=COLORS[color];linear=tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in paint[:3])
 for c in colors.data:c.color=(*linear,paint[3])
 objects.append(o);return o
def cyl(name,y,r,h,color,x=0,z=0,bevel=.02):
 bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=h,location=V((x,y,z)));return finish(bpy.context.object,name,color,min(bevel,h*.3))
def box(name,p,size,color,bevel=.004):
 bpy.ops.mesh.primitive_cube_add(size=1,location=V(p));o=bpy.context.object;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return finish(o,name,color,bevel)
def triangle(name,y,z,w,h,color):
 mesh=bpy.data.meshes.new(name);mesh.from_pydata([V((0,y+h*.5,z)),V((-w*.5,y-h*.5,z)),V((w*.5,y-h*.5,z))],[],[(0,2,1)]);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);return finish(o,name,color)
cyl('Red moulded pressure vessel',.70,.43,1.24,'Red',bevel=.055)
cyl('Bottom dark bumper',.055,.44,.11,'Navy')
cyl('Red top cap',1.34,.405,.08,'Red')
cyl('Top dark seal',1.295,.425,.045,'Navy')
for y in [.30,1.04]:
 cyl('Dark hoop seam',y,.447,.13,'Navy')
 cyl('Brass safety hoop',y,.457,.085,'Gold')
cyl('Top inset ring',1.39,.13,.025,'Navy',x=.16,z=.05)
cyl('Top brass valve',1.407,.072,.025,'Gold',x=.16,z=.05)
for angle in [0,90,180,270]:
 parts=[triangle('Hazard dark outline',.705,-.434,.35,.33,'Navy'),triangle('Hazard amber face',.707,-.438,.29,.27,'Gold'),box('Hazard exclamation stem',(0,.725,-.445),(.033,.10,.012),'Navy'),box('Hazard exclamation dot',(0,.638,-.445),(.033,.033,.012),'Navy')]
 for o in parts:o.rotation_euler[2]=math.radians(angle);o.location=Vector((math.cos(math.radians(angle))*o.location.x-math.sin(math.radians(angle))*o.location.y,math.sin(math.radians(angle))*o.location.x+math.cos(math.radians(angle))*o.location.y,o.location.z))
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='CryoExplosiveBarrel'
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CryoExplosiveBarrel.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUT/'CryoExplosiveBarrel.fbx'),use_selection=True,object_types={'MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,colors_type='LINEAR')
(OUT/'CryoExplosiveBarrel.json').write_text(json.dumps({'source':'Authored geometry; no generated assets or credits','vertices':len(joined.data.vertices),'triangles':sum(len(p.vertices)-2 for p in joined.data.polygons),'height_m':1.42,'material_count':1},indent=2)+'\n')
