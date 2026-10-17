"""Actual Blender-imported derivative review; no source or canonical edits."""
import bpy,sys,math,json,numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16/gait-011')
OUT=ROOT/'preflight-review';OUT.mkdir(exist_ok=False)
bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene;scene.render.fps=60
bpy.ops.import_scene.gltf(filepath=str(ROOT/'hero_engineer_chibi.glb'))
arm=next(o for o in scene.objects if o.type=='ARMATURE');main=max((o for o in scene.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices));actions={a.name:a for a in bpy.data.actions}
def pose(clip,t):
 a=actions[clip];arm.animation_data_create();arm.animation_data.action=a;arm.animation_data.action_slot=a.slots[0];f=t*60;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
# Independent evaluated skin samples, including exact strike/roll/flight phases.
samples={};proof=[]
for clip,duration in [('Idle',2),('Walking',.8),('Jogging',.62),('Running',.5),('Attack',.4),('Death',1.5),('Dash',.16)]:
 for phase in [0,.05,.1,.125,.25,.3,.5,.55,.6,.75,.9,1]:
  t=duration*phase;pose(clip,t);deps=bpy.context.evaluated_depsgraph_get();ob=main.evaluated_get(deps);mesh=ob.to_mesh();points=np.array([ob.matrix_world@v.co for v in mesh.vertices]);points=points[:,[0,2,1]]*np.array([1,1,-1]);ob.to_mesh_clear();key=f'{clip}_{t:.6f}';samples[key]=points
  proof.append({'key':key,'seconds':t,'vertices':len(points),'palmsBlender':{s:list(arm.matrix_world@arm.pose.bones[s+'Palm'].head) for s in ['Right','Left']}})
np.savez_compressed(ROOT/'blender-import-pose-samples.npz',**samples)
(ROOT/'blender-import-proof.json').write_text(json.dumps({'clips':list(actions),'boneCount':len(arm.data.bones),'actualImportedSamples':proof},indent=2)+'\n')
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=10;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.resolution_x=512;scene.render.resolution_y=576;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
world=bpy.data.worlds.new('Neutral review');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.55,.55,1);world.node_tree.nodes['Background'].inputs[1].default_value=.8
mat=bpy.data.materials.new('Measured ground grid');mat.use_nodes=True;nt=mat.node_tree;bs=nt.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.9;tex=nt.nodes.new('ShaderNodeTexChecker');tex.inputs['Color1'].default_value=(.18,.19,.21,1);tex.inputs['Color2'].default_value=(.25,.26,.28,1);tex.inputs['Scale'].default_value=4;coords=nt.nodes.new('ShaderNodeTexCoord');nt.links.new(coords.outputs['Object'],tex.inputs['Vector']);nt.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.001));floor=bpy.context.object;floor.data.materials.append(mat)
for name,pos,power,size in [('Key',(2,-3,4),70,2),('Fill',(-3,-1,2),35,2),('Rim',(1,3,3),65,2)]:
 light=bpy.data.lights.new(name,'AREA');light.energy=power;light.shape='DISK';light.size=size;o=bpy.data.objects.new(name,light);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,.5))-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('Actual import camera',bpy.data.cameras.new('Actual import camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def camera(angle,center=(0,-.07,.47),scale=1.38):
 c=Vector(center);a=math.radians(angle);cam.location=c+Vector((math.sin(a),-math.cos(a),.12))*3;cam.rotation_euler=(c-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
def render(name):scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for clip,t in [('Walking',0),('Walking',.20),('Jogging',.031),('Jogging',.186),('Running',.025),('Running',.15),('Dash',0)]:
 pose(clip,t)
 for label,angle in [('quarter',-25),('side',90)]:camera(angle);render(f'{clip}-{t:.3f}-{label}')
pose('Running',.025);camera(180);render('Running-contact-back')
pose('Running',.15);camera(180);render('Running-flight-back')
pose('Idle',0);camera(-25);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'engineer-gait-review.blend'))
print('PREFLIGHT_IMPORTED_REVIEW_COMPLETE',flush=True)
