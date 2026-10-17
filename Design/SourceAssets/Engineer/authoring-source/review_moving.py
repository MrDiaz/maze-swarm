"""Continuous actual imported GLB motion at stated world speeds. Offline review."""
import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector,Matrix
MODE=sys.argv[sys.argv.index('--')+1];ROOT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16/gait-011');OUT=ROOT/'moving-review';OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene;scene.render.fps=60
def import_model(path,label):
 oldob=set(scene.objects);olda=set(bpy.data.actions);bpy.ops.import_scene.gltf(filepath=str(path));objects=set(scene.objects)-oldob;actions={a.name.split('.')[0]:a for a in set(bpy.data.actions)-olda};arm=next(o for o in objects if o.type=='ARMATURE');root=bpy.data.objects.new(label,None);scene.collection.objects.link(root)
 for o in objects:
  if o.parent not in objects:o.parent=root
 return {'objects':objects,'actions':actions,'arm':arm,'root':root}
new=import_model(ROOT/'hero_engineer_chibi.glb','New world travel')
old=import_model(Path('/Users/manuel.diaz/Documents/Codex/2026-10-15/task-2/engineer-gait-trial/baseline-rig009.glb'),'Old world travel') if MODE=='compare' else None
def show(which):
 for m in [new,old]:
  if m:
   for o in m['objects']:o.hide_render=m is not which or (o.type=='MESH' and len(o.data.vertices)<100)
def pose(m,clip,t):
 a=m['actions'][clip];arm=m['arm'];arm.animation_data_create();arm.animation_data.action=a;arm.animation_data.action_slot=a.slots[0];f=t*60;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=4;scene.render.resolution_x=320;scene.render.resolution_y=400;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
world=bpy.data.worlds.new('Neutral review');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.55,.55,1);world.node_tree.nodes['Background'].inputs[1].default_value=.8
mat=bpy.data.materials.new('Measured quarter-meter ground');mat.use_nodes=True;nt=mat.node_tree;bs=nt.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.9;tex=nt.nodes.new('ShaderNodeTexChecker');tex.inputs['Color1'].default_value=(.18,.19,.21,1);tex.inputs['Color2'].default_value=(.25,.26,.28,1);tex.inputs['Scale'].default_value=4;coords=nt.nodes.new('ShaderNodeTexCoord');nt.links.new(coords.outputs['Object'],tex.inputs['Vector']);nt.links.new(tex.outputs['Color'],bs.inputs['Base Color']);bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.001));floor=bpy.context.object;floor.data.materials.append(mat)
lampRoot=bpy.data.objects.new('Tracking light rig',None);scene.collection.objects.link(lampRoot)
for name,pos,power,size in [('Key',(2,-3,4),70,2),('Fill',(-3,-1,2),35,2),('Rim',(1,3,3),65,2)]:
 light=bpy.data.lights.new(name,'AREA');light.energy=power;light.shape='DISK';light.size=size;o=bpy.data.objects.new(name,light);scene.collection.objects.link(o);o.parent=lampRoot;o.location=pos;o.rotation_euler=(Vector((0,0,.5))-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('Tracking motion camera',bpy.data.cameras.new('Tracking motion camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def camera(angle,location):
 c=Vector(location)+Vector((0,-.13,.47));a=math.radians(angle);cam.location=c+Vector((math.sin(a),-math.cos(a),.11))*3;cam.rotation_euler=(c-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=1.50
before=set(scene.objects);bpy.ops.import_scene.gltf(filepath='/Volumes/T7 Shield/Projects/Maze Swarm/Characters/VanguardChibi/minigun_chibi.glb');gunobjects=list(set(scene.objects)-before);gun=next(o for o in gunobjects if o.name.startswith('MinigunChibi'));gun.rotation_mode='XYZ';gun.rotation_euler=(0,0,-math.pi/2);scale=(.82/1.65)/(.919999958+.14943942);gun.scale=(scale,)*3;grip=Vector((-.04,0,-.14));gunbase=Vector((-.06,-.13,.35))-gun.rotation_euler.to_matrix()@(grip*scale)
specs=[]
if MODE=='compare':specs=[('before009-run-cap3',old,'Running',.6,3.,6.48,60,60,90),('after011-run-native',new,'Running',.5,1.,6.48,60,60,90)]
elif MODE=='lower':specs=[('after011-walk-native',new,'Walking',.8,1.,.65,48,30,90),('after011-jog-native',new,'Jogging',.62,1.,2.5,38,30,90),('after011-dash-glide',new,'Dash',.16,1.,24.,10,60,25)]
elif MODE=='turn':specs=[('after011-run-turn',new,'Running',.5,1.,6.48,60,60,25)]
proof=[]
for label,m,clip,period,rate,speed,count,fps,angle in specs:
 folder=OUT/label;folder.mkdir(exist_ok=False);show(m);gun.parent=m['root'];positions=[];position=Vector((0,0,0));lastyaw=0
 for f in range(count):
  seconds=f/fps;t=min(seconds,.159999) if clip=='Dash' else (seconds*rate)%period;pose(m,clip,t)
  yaw=(math.pi*.5*seconds) if MODE=='turn' else 0
  if MODE=='turn':
   if f:mid=(yaw+lastyaw)*.5;position+=Vector((math.sin(mid),-math.cos(mid),0))*(speed/1.65/fps)
  else:position=Vector((0,-speed/1.65*seconds,0))
  m['root'].matrix_world=Matrix.Translation(position)@Matrix.Rotation(yaw,4,'Z');lampRoot.location=position;hip=m['arm'].pose.bones['Hips'].head.z;bob=(hip-.29)*(.35 if m is new else 1.);gun.location=gunbase+Vector((0,0,bob));camera(angle,position);bpy.context.view_layer.update();scene.render.filepath=str(folder/f'{f:03d}.png');bpy.ops.render.render(write_still=True);lastyaw=yaw
  positions.append({'frame':f,'timeSeconds':seconds,'clipSeconds':t,'runtimeSpeed':speed,'positionAuthor':list(position),'yawRadians':yaw,'forwardBlender':[math.sin(yaw),-math.cos(yaw),0]})
 proof.append({'label':label,'clip':clip,'durationSeconds':count/fps,'fps':fps,'frames':count,'nativePlaybackRate':rate,'worldRuntimeSpeed':speed,'sourceRig':'011' if m is new else '009','framesData':positions})
(OUT/(MODE+'-proof.json')).write_text(json.dumps({'render':'Actual Blender imported GLB, Cycles CPU4threads8samples','offlineWorldMotion':True,'notUnityRuntimeOrPhone':True,'floorSquareAuthorMeters':.25,'floorSquareRuntimeMeters':.4125,'weaponSourceUnchanged':True,'reviewSequences':proof},indent=2)+'\n');print('MOVING_REVIEW_COMPLETE '+MODE,flush=True)
