"""Isolated authored animation staging; no canonical project or source writes.
Fixed authored gait/contact and attack envelopes are baked onto a measured skeleton.
"""
import bpy,math,json,hashlib,shutil,sys
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion,Euler
OUT=Path(__file__).resolve().parent
SRC=Path('/Volumes/T7 Shield/Assets/Enemies/Lavender Spitter 2026-10-16/mobile-006')
FPS=30
def ss(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
def lerp(a,b,t):return a*(1-t)+b*t
def env(keys,t):
 for (x,a),(y,b) in zip(keys,keys[1:]):
  if t<=y:return lerp(a,b,ss(x,y,t))
 return keys[-1][1]
def v(a):return Vector(a)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.fps=FPS;scene.frame_start=1;scene.frame_end=61
bpy.ops.import_scene.gltf(filepath=str(SRC/'LOD0/lavender-spitter_LOD0.glb'))
mesh=next(o for o in scene.objects if o.type=='MESH');mesh.name='LavenderSpitter_LOD0_Skin'
for o in list(scene.objects):
 if o!=mesh and o.type!='MESH':bpy.data.objects.remove(o,do_unlink=True)
bpy.context.view_layer.objects.active=mesh
bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True)
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
mesh.data.name='Approved_mobile006_LOD0_4000tri'
rest_verts=[list(x.co) for x in mesh.data.vertices]
source_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in SRC.glob('LOD*/*.glb')}
data=bpy.data.armatures.new('LavenderSpitter_AuthoredSkeleton')
rig=bpy.data.objects.new('LavenderSpitter_Rig',data);scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;mesh.select_set(False);rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
spec={}
def bone(n,head,tail,parent=None,deform=True):
 b=data.edit_bones.new(n);b.head=head;b.tail=tail;b.use_deform=deform
 if parent:b.parent=data.edit_bones[parent]
 spec[n]={'head':list(head),'tail':list(tail),'parent':parent,'deform':deform}
bone('Root',(0,0,0),(0,0,.10),None,False)
bone('Hips',(0,.07,.25),(0,.07,.36),'Root')
bone('Spine',(0,.07,.36),(0,.04,.54),'Hips')
bone('Head',(0,.04,.54),(.045,.04,.83),'Spine')
bone('Muzzle',(.045,-.31,.59),(.045,-.41,.59),'Head',False)
for side,s in [('L',1),('R',-1)]:
 bone('UpperArm.'+side,(s*.27,.045,.445),(s*.365,-.015,.355),'Spine')
 bone('Forearm.'+side,(s*.365,-.015,.355),(s*.445,-.055,.295),'UpperArm.'+side)
 bone('Thigh.'+side,(s*.225,.045,.25),(s*.27,-.025,.15),'Hips')
 bone('Shin.'+side,(s*.27,-.025,.15),(s*.27,-.015,.055),'Thigh.'+side)
 bone('Foot.'+side,(s*.27,-.015,.055),(s*.27,-.13,.055),'Shin.'+side)
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front=True;rig.display_type='WIRE';data.display_type='OCTAHEDRAL'
mod=mesh.modifiers.new('Measured_joint_skin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False
mesh.parent=rig
groups={n:mesh.vertex_groups.new(name=n) for n,x in spec.items() if x['deform']}
sys.path.insert(0,str(OUT))
from skin_weights import compute_weights
semantic=json.loads((OUT/'semantic-colors.json').read_text())
weights=compute_weights([list(q.co) for q in mesh.data.vertices],[tuple(e.vertices) for e in mesh.data.edges],semantic)
for vertex,w in zip(mesh.data.vertices,weights):
 for n,a in w.items():groups[n].add([vertex.index],a,'REPLACE')
for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
bpy.context.view_layer.update()
rest={n:b.matrix_local.copy() for n,b in data.bones.items()}
clips={};contact_report=[]
def reset():
 for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 bpy.context.view_layer.update()
def orient_bone(n,head,tail,roll=0):
 b=data.bones[n]
 q=(b.tail_local-b.head_local).rotation_difference(tail-head) @ rest[n].to_quaternion()
 if roll:q=Quaternion((tail-head).normalized(),roll)@q
 rig.pose.bones[n].matrix=Matrix.LocRotScale(head,q,Vector((1,1,1)))
 bpy.context.view_layer.update()
def solve_leg(side,ankle):
 s=1 if side=='L' else -1
 hip=rig.pose.bones['Hips'].matrix @ (rest['Hips'].inverted() @ data.bones['Thigh.'+side].head_local)
 b1=data.bones['Thigh.'+side];b2=data.bones['Shin.'+side]
 l1=b1.length;l2=b2.length;dvec=ankle-hip;d=dvec.length;u=dvec.normalized()
 d=min(d,l1+l2-.00001)
 a=(l1*l1-l2*l2+d*d)/(2*d);h=math.sqrt(max(0,l1*l1-a*a))
 rest_u=(b2.tail_local-b1.head_local).normalized()
 pole=b1.tail_local-b1.head_local;pole=(pole-rest_u*pole.dot(rest_u)).normalized()
 pole=(pole-u*pole.dot(u)).normalized()
 knee=hip+u*a+pole*h
 orient_bone('Thigh.'+side,hip,knee)
 orient_bone('Shin.'+side,knee,ankle)
 # Feet have world-flat orientation throughout stance and swing; no toe-through-floor roll.
 rig.pose.bones['Foot.'+side].matrix=Matrix.LocRotScale(ankle,rest['Foot.'+side].to_quaternion(),Vector((1,1,1)))
 bpy.context.view_layer.update()
 return {'hip':list(hip),'knee':list(knee),'ankle':list(ankle),'reachable':dvec.length<=l1+l2+.00002}
def pose(t,kind):
 reset();hips=rig.pose.bones['Hips'];spine=rig.pose.bones['Spine'];head=rig.pose.bones['Head']
 # Fixed hand-authored schedules; never scale the model or any bone.
 if kind=='Walk':
  phase=t/.8;theta=phase*math.tau
  hips.location=(.009*math.sin(theta),-.032+.003*(1-math.cos(theta*2)),0)
  # Hips local Y follows vertical bone axis; small sideways weight transfer, 32mm crouch.
  spine.rotation_quaternion=Euler((math.radians(-2),math.radians(1.4)*math.sin(theta),0),'XYZ').to_quaternion()
  head.rotation_quaternion=Euler((math.radians(1),math.radians(-1.0)*math.sin(theta),0),'XYZ').to_quaternion()
 elif kind=='Idle':
  breath=math.sin(math.tau*t/2.)
  spine.rotation_quaternion=Euler((math.radians(.7)*breath,0,0),'XYZ').to_quaternion()
  head.rotation_quaternion=Euler((math.radians(-.4)*breath,0,0),'XYZ').to_quaternion()
 elif kind=='Spit':
  # Stable planted feet, 0-.30 gather/brace, .30-.40 release, .40-.80 recovery.
  lean=env([(0,0),(.18,-4),(.30,-7),(11/30,8),(.46,3),(.66,-1),(.80,0)],t)
  hp=env([(0,0),(.18,-3),(.30,-6),(11/30,6),(.46,2),(.66,-.5),(.80,0)],t)
  crouch=env([(0,0),(.18,.008),(.30,.015),(11/30,.003),(.52,.008),(.80,0)],t)
  hips.location=(0,-crouch,0)
  spine.rotation_quaternion=Euler((math.radians(lean),0,0),'XYZ').to_quaternion()
  head.rotation_quaternion=Euler((math.radians(hp),0,0),'XYZ').to_quaternion()
 bpy.context.view_layer.update()
 for side,s in [('L',1),('R',-1)]:
  if kind=='Walk':
   p=(t/.8+(0 if s==1 else .5))%1
   stance=.56;travel=.28*stance
   if p<stance:
    yy=-travel/2+travel*(p/stance);zz=0.;contact=True
   else:
    u=(p-stance)/(1-stance)
    m=.35*.8*(1-stance)
    yy=(2*u**3-3*u*u+1)*(travel/2)+(u**3-2*u*u+u)*m+(-2*u**3+3*u*u)*(-travel/2)+(u**3-u*u)*m
    zz=.045*math.sin(math.pi*u)**2;contact=False
   foot=v((s*.27,-.015+yy,.055+zz))
   angle=math.radians(8)*math.sin(math.tau*(t/.8)+(0 if s==1 else math.pi))
   rig.pose.bones['UpperArm.'+side].rotation_quaternion=Euler((angle,0,math.radians(1.5)*s),'XYZ').to_quaternion()
   rig.pose.bones['Forearm.'+side].rotation_quaternion=Euler((math.radians(-2),0,0),'XYZ').to_quaternion()
  else:
   foot=v((s*.27,-.015,.055));contact=True
   if kind=='Spit':
    brace=env([(0,0),(.18,7),(.30,13),(11/30,-4),(.50,2),(.80,0)],t)
    rig.pose.bones['UpperArm.'+side].rotation_quaternion=Euler((math.radians(brace),0,math.radians(-brace*.18*s)),'XYZ').to_quaternion()
    rig.pose.bones['Forearm.'+side].rotation_quaternion=Euler((math.radians(-brace*.4),0,0),'XYZ').to_quaternion()
  bpy.context.view_layer.update()
  sol=solve_leg(side,foot)
  contact_report.append({'clip':kind,'t':t,'side':side,'stance':contact,**sol})
 bpy.context.view_layer.update()
def key_pose(frame):
 for pb in rig.pose.bones:
  if pb.name in ('Root','Muzzle'):continue
  pb.keyframe_insert('location',frame=frame,group=pb.name)
  pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
  pb.keyframe_insert('scale',frame=frame,group=pb.name)
for name,duration in [('Idle',2.),('Walk',.8),('Spit',.8)]:
 rig.animation_data_create();rig.animation_data.action=None
 action=bpy.data.actions.new(name);rig.animation_data.action=action
 for i in range(round(duration*FPS)+1):
  frame=i+1;scene.frame_set(frame);pose(i/FPS,name);key_pose(frame)
 clips[name]=action
 action.use_fake_user=True
 # Dense baked FK samples are linear; endpoints are exact loop closure.
 for slot in action.slots:
  for layer in action.layers:
   for strip in layer.strips:
    try:
     for fc in strip.channelbag(slot).fcurves:
      for k in fc.keyframe_points:k.interpolation='LINEAR'
    except:pass
 action['duration_seconds']=duration
action=clips['Idle'];rig.animation_data.action=action;scene.frame_set(1)
rig['source_version']='mobile-006 only';rig['author_height_m']=1.0000046491622925
rig['walk_stride_m_per_cycle']=.28;rig['walk_cycle_seconds']=.8;rig['walk_native_speed_m_s']=.35
rig['spit_release_seconds']=.36666666666666664;rig['no_root_motion']=True
rig['authorship']='Measured skeleton, semantic joint collars, authored foot contact IK solved offline and baked into FK keys. No runtime deformation controller required.'
mesh['source_sha256']=source_hashes[str(SRC/'LOD0/lavender-spitter_LOD0.glb')]
rig.animation_data.action=clips['Idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
args={'filepath':str(OUT/'lavender-spitter-animated_LOD0.glb'),'export_format':'GLB','use_selection':True,'export_animations':True,'export_animation_mode':'ACTIONS','export_force_sampling':True,'export_frame_range':False,'export_skins':True,'export_all_influences':False,'export_def_bones':False,'export_anim_single_armature':True,'export_anim_slide_to_zero':True,'export_tangents':True,'export_normals':True,'export_materials':'EXPORT','export_yup':True}
allowed=bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
args={k:a for k,a in args.items() if k in allowed}
bpy.ops.export_scene.gltf(**args)
# Save editable rig and baked actions, plus a small neutral CPU review stage.
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.cycles.use_denoising=True
scene.cycles.max_bounces=3;scene.cycles.diffuse_bounces=2;scene.cycles.glossy_bounces=1
scene.render.threads_mode='FIXED';scene.render.threads=2
scene.render.resolution_x=480;scene.render.resolution_y=480;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
scene.view_settings.view_transform='AgX'
try:scene.view_settings.look='AgX - Medium High Contrast'
except:pass
world=bpy.data.worlds.new('Neutral review environment');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.16,.19,.26,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
mat=bpy.data.materials.new('Review ground only');mat.diffuse_color=(.16,.19,.25,1);mat.use_nodes=True
mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.16,.19,.25,1)
mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.003));floor=bpy.context.object;floor.name='REVIEW_ONLY_Ground';floor.data.materials.append(mat)
def aim(o,at):o.rotation_euler=(Vector(at)-o.location).to_track_quat('-Z','Y').to_euler()
def area(name,loc,energy,size):
 d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.shape='DISK';d.size=size
 o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;aim(o,(0,0,.4));return o
area('REVIEW_ONLY_Key',(-2,-3,4),230,3)
area('REVIEW_ONLY_Fill',(3,-1,2),100,3)
area('REVIEW_ONLY_Rim',(0,3,3),130,2)
cd=bpy.data.cameras.new('REVIEW_ONLY_Camera');cam=bpy.data.objects.new('REVIEW_ONLY_Camera',cd);scene.collection.objects.link(cam)
cam.location=(1.9,-3.3,1.8);aim(cam,(0,.03,.48));cd.type='ORTHO';cd.ortho_scale=1.38;scene.camera=cam
rig.animation_data.action=clips['Idle'];scene.frame_set(1)
for im in bpy.data.images:
 if im.source=='FILE' and not im.packed_file:im.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lavender-spitter-authored.blend'))
validation={'sourceHashes':source_hashes,'rig':spec,'boneCount':len(data.bones),'deformBoneCount':sum(b.use_deform for b in data.bones),'clips':{name:{'durationSeconds':float(a['duration_seconds']),'fps':FPS,'samples':round(a['duration_seconds']*FPS)+1,'loop':name!='Spit'} for name,a in clips.items()},'walk':{'strideMetersPerCycle':.28,'cycleSeconds':.8,'nativeGroundSpeedMetersPerSecond':.35,'stanceFractionPerFoot':.56,'maximumFootLiftMeters':.045,'bothFeetGroundedFraction':.12},'spit':{'releaseTimeSeconds':11/30,'releaseFrameAt30HzZeroBased':11,'durationSeconds':.8,'jawDeformation':False},'weights':{'vertices':len(weights),'maximumInfluences':max(len(w) for w in weights),'maxNormalizationError':max(abs(sum(w.values())-1) for w in weights),'unweightedVertices':sum(not w for w in weights)},'allIKTargetsReachable':all(q['reachable'] for q in contact_report),'restVertexCount':len(rest_verts),'restTriangles':len(mesh.data.polygons),'restPositionsUnchanged':all((Vector(a)-b.co).length==0 for a,b in zip(rest_verts,mesh.data.vertices)),'exportArgs':args,'sourceOriginalsUnchanged':all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in source_hashes.items())}
(OUT/'rig-validation.json').write_text(json.dumps(validation,indent=2))
(OUT/'contact-schedule.json').write_text(json.dumps(contact_report,indent=2))
print('STAGING_RIG_COMPLETE',json.dumps({k:validation[k] for k in ['boneCount','deformBoneCount','clips','weights','allIKTargetsReachable','restPositionsUnchanged','sourceOriginalsUnchanged']}))
