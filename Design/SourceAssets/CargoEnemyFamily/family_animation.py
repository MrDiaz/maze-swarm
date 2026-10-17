"""Offline authored FK keys with measured two-bone leg contacts; no runtime IK."""
import bpy,math
from mathutils import Vector,Matrix,Quaternion,Euler
def ss(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
def env(keys,t):
 for (x,a),(y,b) in zip(keys,keys[1:]):
  if t<=y:return a+(b-a)*ss(x,y,t)
 return keys[-1][1]
def author(rig,role):
 scene=bpy.context.scene;scene.render.fps=60;FPS=60;data=rig.data
 rest={n:b.matrix_local.copy() for n,b in data.bones.items()}
 restQ={n:m.to_quaternion() for n,m in rest.items()}
 footBase={s:data.bones['Foot.'+s].head_local.copy() for s in ('L','R')}
 profiles={'grunt':(.8,.28,.032,.045,8),'tick':(.8,.28,.028,.042,3),'runner':(40/60,.3136,.0358,.05,10),'carrier':(1.,.24,.026,.04,5),'carapace':(1.,.24,.026,.04,5)}
 cycle,stride,crouch,lift,armSwing=profiles[role]
 clips={'Idle':2.,'Walk':cycle,'MeleeWindup':.15,'Melee':.25}
 if role=='runner':clips.update({'Crouch':.6,'Leap':14/30,'Land':.8,'Sidestep':.35})
 if role in ('carrier','carapace'):clips.update({'Bloat':.7,'Burst':.35})
 for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
 def reset():
  for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
  bpy.context.view_layer.update()
 def rot(name,x=0,y=0,z=0):rig.pose.bones[name].rotation_quaternion=Euler(tuple(math.radians(a) for a in (x,y,z)),'XYZ').to_quaternion()
 def hips(delta):rig.pose.bones['Hips'].location=restQ['Hips'].inverted()@Vector(delta)
 def orient(name,head,tail):
  b=data.bones[name];q=(b.tail_local-b.head_local).rotation_difference(tail-head)@restQ[name]
  rig.pose.bones[name].matrix=Matrix.LocRotScale(head,q,Vector((1,1,1)));bpy.context.view_layer.update()
 def solve(side,ankle):
  b1=data.bones['Thigh.'+side];b2=data.bones['Shin.'+side]
  hip=rig.pose.bones['Hips'].matrix@(rest['Hips'].inverted()@b1.head_local)
  u=ankle-hip;actualDist=u.length;u.normalize();l1=b1.length;l2=b2.length;d=min(actualDist,l1+l2-.00001)
  a=(l1*l1-l2*l2+d*d)/(2*d);h=math.sqrt(max(0,l1*l1-a*a))
  ru=(b2.tail_local-b1.head_local).normalized();pole=b1.tail_local-b1.head_local;pole=(pole-ru*pole.dot(ru)).normalized();pole=(pole-u*pole.dot(u)).normalized()
  knee=hip+u*a+pole*h;orient('Thigh.'+side,hip,knee);orient('Shin.'+side,knee,ankle)
  rig.pose.bones['Foot.'+side].matrix=Matrix.LocRotScale(ankle,restQ['Foot.'+side],Vector((1,1,1)));bpy.context.view_layer.update()
  return actualDist<=l1+l2+.00002
 schedule=[];actions={}
 def pose(name,t):
  reset();theta=t/cycle*math.tau;targets={s:footBase[s].copy() for s in ('L','R')};contact={s:True for s in targets}
  if name=='Idle':
   b=math.sin(math.tau*t/2);rot('Spine',.65*b);rot('Head',-.35*b)
   if 'CarrierBelly'in rig.pose.bones:rig.pose.bones['CarrierBelly'].scale=Vector((1+.008*b,1+.006*b,1+.008*b))
  elif name=='Walk':
   amp=.003 if role=='tick' else .009
   hips((amp*math.sin(theta),0,-crouch+.003*(1-math.cos(theta*2))))
   rot('Spine',5 if role=='runner' else -2,1.2*math.sin(theta));rot('Head',-2 if role=='runner' else 1,-.8*math.sin(theta))
   for side,phase in [('L',0),('R',.5)]:
    p=(t/cycle+phase)%1;stance=.56;travel=stride*stance
    if p<stance:y=-travel/2+travel*p/stance;z=0
    else:
     u=(p-stance)/(1-stance);m=stride*(1-stance)
     y=(2*u**3-3*u*u+1)*travel/2+(u**3-2*u*u+u)*m+(-2*u**3+3*u*u)*(-travel/2)+(u**3-u*u)*m
     z=lift*math.sin(math.pi*u)**2;contact[side]=False
    targets[side]+=Vector((0,y,z));rot('UpperArm.'+side,armSwing*math.sin(theta+phase*math.tau));rot('Forearm.'+side,-2)
  elif name in ('Melee','MeleeWindup'):
   k=env([(0,1),(.07,.72),(.25,0)],t) if name=='Melee' else ss(0,.15,t)
   sign=1 if name=='Melee' else -1
   hips((0,-.012*k*sign,-.009*k));rot('Spine',sign*10*k);rot('Head',sign*4*k)
   for side in targets:
    rot('UpperArm.'+side,(-18 if name=='Melee' else 10)*k)
    rot('Forearm.'+side,(3 if name=='Melee' else -2)*k)
  elif name=='Crouch':
   k=ss(0,.6,t);hips((0,.025*k,-.045*k));rot('Spine',8*k);rot('Head',3*k)
   for s in targets:rot('UpperArm.'+s,-20*k);rot('Forearm.'+s,-8*k)
  elif name=='Leap':
   u=t/(14/30);coil=1-ss(0,.25,u);tuck=env([(0,0),(.25,.06),(.68,.075),(1,0)],u)
   hips((0,.025*coil,-.045*coil));rot('Spine',env([(0,8),(.30,13),(.75,8),(1,4)],u));rot('Head',2)
   for s,sign in [('L',1),('R',-1)]:
    targets[s]+=Vector((0,.015*math.sin(math.pi*u),tuck));contact[s]=u<.001 or u>.999
    rot('UpperArm.'+s,env([(0,-20),(.28,18),(1,10)],u));rot('Forearm.'+s,-10)
  elif name=='Land':
   k=env([(0,.55),(.10,1),(.28,.65),(.60,.12),(.8,0)],t);hips((0,0,-.045*k));rot('Spine',8*k);rot('Head',3*k)
   for s in targets:rot('UpperArm.'+s,12*k);rot('Forearm.'+s,-5*k)
  elif name=='Sidestep':
   u=t/.35;hips((.018*math.sin(math.pi*u),0,-.018*math.sin(math.pi*u)));rot('Spine',0,0,-4*math.sin(math.pi*u))
   for s,sign in [('L',1),('R',-1)]:
    p=ss(0,.5,u) if sign==1 else ss(.5,1,u)
    z=.025*math.sin(math.pi*p)**2;targets[s]+=Vector((0,0,z));contact[s]=z<.000001
    rot('UpperArm.'+s,0,0,sign*8*math.sin(math.pi*u))
  elif name in ('Bloat','Burst'):
   k=ss(0,.7,t) if name=='Bloat' else env([(0,1),(.05,1.10),(.18,.55),(.35,0)],t)
   hips((0,0,-.012*min(k,1)));rot('Spine',-2*k);rot('Head',1*k)
   rig.pose.bones['CarrierBelly'].scale=Vector((1+.085*k,1+.045*k,1+.085*k))
   for s,sgn in [('L',1),('R',-1)]:rot('UpperArm.'+s,-7*k,0,-sgn*5*k)
   for n,sgn in [('CarrierPod.L',1),('CarrierPod.R',-1),('CarrierPod.Back',0)]:
    rot(n,6*k,sgn*8*k,0);rig.pose.bones[n].location=restQ[n].inverted()@Vector((sgn*.018*k,.014*k,0))
  bpy.context.view_layer.update()
  reachable=[]
  for side in targets:
   reachable.append(solve(side,targets[side]));schedule.append({'clip':name,'time':t,'side':side,'contact':contact[side],'targetBlender':list(targets[side])})
  return all(reachable)
 reachable=True
 for name,dur in clips.items():
  rig.animation_data_create();rig.animation_data.action=None;action=bpy.data.actions.new(name);rig.animation_data.action=action
  for frame in range(round(dur*FPS)+1):
   scene.frame_set(frame);reachable &= pose(name,frame/FPS)
   for pb in rig.pose.bones:
    if not pb.bone.use_deform:continue
    for path in ['location','rotation_quaternion','scale']:pb.keyframe_insert(path,frame=frame,group=pb.name)
  for slot in action.slots:
   for layer in action.layers:
    for strip in layer.strips:
     try:
      for fc in strip.channelbag(slot).fcurves:
       for k in fc.keyframe_points:k.interpolation='LINEAR'
     except:pass
  action.use_fake_user=True;actions[name]=action
 rig.animation_data.action=actions['Idle'];scene.frame_set(0)
 return {'fps':FPS,'clips':clips,'walkCycleSeconds':cycle,'strideMetersPerCycle':stride,'nativeGroundSpeedMetersPerSecond':stride/cycle,'allLegTargetsReachable':reachable,'rootMotion':False,'localScaleBones':['CarrierBelly'] if role in ('carrier','carapace') else [],'meleeContactTimeSeconds':0,'crouchSeconds':.6 if role=='runner' else None,'leapSeconds':14/30 if role=='runner' else None,'bloatReleaseSeconds':.7 if role in ('carrier','carapace') else None},schedule
