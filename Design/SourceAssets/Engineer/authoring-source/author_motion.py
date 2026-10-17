"""Revised gait poses, authored against per-foot source sole geometry.

No mesh attribute, index, UV, image or material edits. Toe binding is a documented
derivative: existing Toe joints get ball-of-foot pivots and bounded boot weights.
"""
from pathlib import Path
import sys,math,json,numpy as np
from scipy.spatial.transform import Rotation as R
sys.path.insert(0,str(Path(__file__).resolve().parent.parent/'engineer-rig-trial'))
from build_rig import B as OLD_B,REST as OLD_REST,LOCAL as OLD_LOCAL,PARENT,IDX,IDENT,rx,ry,rz,mul,inv,rot,norm,between,smooth,load_glb,accessor
BASE=Path(__file__).parent/'baseline-rig009.glb'
J,BIN=load_glb(BASE);PRIM=J['meshes'][0]['primitives'][0];V=accessor(J,BIN,PRIM['attributes']['POSITION'])
JOINT=accessor(J,BIN,PRIM['attributes']['JOINTS_0']);WEIGHT=accessor(J,BIN,PRIM['attributes']['WEIGHTS_0'])
REST=OLD_REST.copy();FOOT={};W=np.zeros((len(V),len(REST)));np.put_along_axis(W,JOINT,WEIGHT,axis=1)
for side in ['Right','Left']:
 fi,ti=IDX[side+'Foot'],IDX[side+'Toe'];mask=(W[:,fi]>.99)&(V[:,1]<.035);sole=V[mask];lo=sole.min(0);hi=sole.max(0);center=(lo+hi)*.5
 heelz=float(np.quantile(sole[:,2],.025));toez=float(np.quantile(sole[:,2],.975));length=toez-heelz;ball=heelz+.70*length
 REST[ti]=[center[0],lo[1]+.025,ball]
 fraction=smooth(V[:,2],heelz+.58*length,heelz+.88*length)*(1-smooth(V[:,1],.065,.125))
 toeWeight=W[:,fi]*fraction;W[:,fi]-=toeWeight;W[:,ti]+=toeWeight
 FOOT[side]={'soleCenter':np.array([center[0],lo[1],center[2]]),'soleMinY':float(lo[1]),'heel':np.array([center[0],lo[1],heelz]),'toePad':np.array([center[0],lo[1],toez]),'toeBall':REST[ti].copy(),'neutralAnkleZ':float(REST[fi,2]-center[2]),'neutralAnkleX':float(REST[fi,0]-center[0]+(-.148 if side=='Right' else .148)),'soleVertexIds':np.where(mask)[0]}
LOCAL=np.array([REST[i]-(REST[p] if p>=0 else 0) for i,p in enumerate(PARENT)])
ORDER=np.argsort(W,axis=1)[:,-4:][:,::-1];WEIGHTS=np.take_along_axis(W,ORDER,axis=1);WEIGHTS[WEIGHTS<1e-6]=0;WEIGHTS/=WEIGHTS.sum(1)[:,None]
GAITS={
 'Walking':{'period':.80,'nativeRuntimeSpeed':.65,'stanceFraction':.55,'lift':.035,'hipCenter':.270,'hipAmplitude':.005,'hipSway':.010,'lean':2.0},
 'Jogging':{'period':.62,'nativeRuntimeSpeed':2.50,'stanceFraction':.20,'lift':.055,'hipCenter':.272,'hipAmplitude':.016,'hipSway':.009,'lean':3.5},
 'Running':{'period':.50,'nativeRuntimeSpeed':6.48,'stanceFraction':.10,'lift':.067,'hipCenter':.286,'hipAmplitude':.024,'hipSway':.008,'lean':4.0},
}
for spec in GAITS.values():
 spec['nativeAuthorSpeed']=spec['nativeRuntimeSpeed']/1.65;spec['contactSeconds']=spec['period']*spec['stanceFraction'];spec['stanceTravelAuthor']=spec['nativeAuthorSpeed']*spec['contactSeconds'];spec['amplitude']=spec['stanceTravelAuthor']/2

def foot_shape(side,pitch,toeWorldPitch):
 """Sole relative to ankle for a given foot and toe rotation, independently of leg IK."""
 f,t=IDX[side+'Foot'],IDX[side+'Toe'];qf,qt=rx(pitch),rx(toeWorldPitch);toe=rot(qf,REST[t]-REST[f]);ids=FOOT[side]['soleVertexIds'];v=V[ids];fw=W[ids,f];tw=W[ids,t];a=rot(qf,v-REST[f]);b=toe+rot(qt,v-REST[t]);points=a*fw[:,None]+b*tw[:,None]
 return points,float(points[:,1].min())

def trajectory(side,u,spec):
 stance=spec['stanceFraction'];amp=spec['amplitude'];speed=spec['nativeAuthorSpeed'];period=spec['period'];anatomy=FOOT[side];f,t=IDX[side+'Foot'],IDX[side+'Toe']
 contact=bool(u<stance)
 if contact:
  s=u/stance;z=amp*(1-2*s);lift=0
  if spec is GAITS['Walking']:
   if s<.16:pitch=-12*(1-smooth(s,0,.16));toePitch=pitch;anchor='heel'
   elif s<.73:pitch=0;toePitch=0;anchor='flat'
   else:pitch=22*smooth(s,.73,1);toePitch=0;anchor='toe'
  else:
   # Forefoot contact, compress to flat then push off with a planted toe pad.
   pitch=8*(1-smooth(s,0,.40))+21*smooth(s,.55,1);toePitch=0;anchor='toe'
 else:
  s=(u-stance)/(1-stance);D=period*(1-stance)
  # Short endpoint velocity caps match the fast backwards stance velocity
  # without making the whole swing overshoot beyond leg reach.
  k=36;cap=-speed*D*(s*(1-s)**k-(1-s)*s**k)
  z=-amp+2*amp*smooth(s,0,1)+cap;lift=spec['lift']*math.sin(math.pi*s)**1.25
  start=22 if spec is GAITS['Walking'] else 21;end=-12 if spec is GAITS['Walking'] else 8
  pitch=(1-smooth(s,.12,.90))*start+smooth(s,.12,.90)*end
  pitch+=-16*math.sin(math.pi*s)**2
  toePitch=pitch*smooth(s,0,.16)
  if spec is not GAITS['Walking']:toePitch*=1-smooth(s,.80,1)
  anchor='air'
 basez=anatomy['neutralAnkleZ']+z;qf,qt=rx(pitch),rx(toePitch)
 if anchor=='heel':
  ref=anatomy['heel'];offset=rot(qf,ref-REST[f]);anklez=basez+(ref[2]-REST[f,2])-offset[2]
 elif anchor in ['toe','flat']:
  offset=rot(qf,REST[t]-REST[f]);anklez=basez+(REST[t,2]-REST[f,2])-offset[2]
 else:
  # Continue the rolled contact endpoint into flight without teleporting the
  # ankle or Toe orientation at toe-off/strike.
  delta=REST[t]-REST[f];startPitch=22 if spec is GAITS['Walking'] else 21
  startCorrection=delta[2]-rot(rx(startPitch),delta)[2]
  endDelta=(anatomy['heel']-REST[f]) if spec is GAITS['Walking'] else delta
  endPitch=-12 if spec is GAITS['Walking'] else 8
  endCorrection=endDelta[2]-rot(rx(endPitch),endDelta)[2]
  blend=smooth(s,0,1);anklez=basez+(1-blend)*startCorrection+blend*endCorrection
 sole,minY=foot_shape(side,pitch,toePitch);ankley=-minY+lift
 target=np.array([anatomy['neutralAnkleX'],ankley,anklez])
 return target,qf,qt,{'contact':contact,'anchor':anchor,'pitchDegrees':pitch,'toeWorldPitchDegrees':toePitch,'soleLiftAuthor':lift,'u':u}

def pose(clip,t):
 dash=clip=='Dash';spec=GAITS['Running' if dash else clip];period=spec['period'];phase=.30 if dash else (float(t)/period)%1;step=(phase*2)%1
 loc=LOCAL.copy();q=np.tile(IDENT,(len(REST),1));gp=REST.copy();gq=q.copy()
 def fk():
  for i,p in enumerate(PARENT):
   gp[i]=loc[i] if p<0 else gp[p]+rot(gq[p],loc[i]);gq[i]=q[i] if p<0 else mul(gq[p],q[i])
 def world(name,value):
  i=IDX[name];p=PARENT[i];q[i]=value if p<0 else mul(inv(gq[p]),value);fk()
 def solve(a,b,c,target,pole,endq,lowerVector=None):
  ia,ib,ic=IDX[a],IDX[b],IDX[c];pa=gp[ia].copy();v=target-pa;dist=np.linalg.norm(v);lower=LOCAL[ic] if lowerVector is None else lowerVector
  la=np.linalg.norm(LOCAL[ib]);lb=np.linalg.norm(lower);d=np.clip(dist,abs(la-lb)+1e-5,la+lb-1e-5);axis=norm(v);along=(la*la-lb*lb+d*d)/(2*d);h=math.sqrt(max(0,la*la-along*along));bend=norm(pole-pa-axis*np.dot(pole-pa,axis));pb=pa+axis*along+bend*h;pc=pa+axis*d
  world(a,between(LOCAL[ib],pb-pa));bq=between(lower,pc-pb);world(b,bq);world(c,bq if endq is None else endq)
  return dist<=la+lb-1e-5,abs(dist-d)
 centerShift=0 if clip=='Walking' else spec['stanceFraction']
 loc[IDX['Hips'],0]=-spec['hipSway']*math.cos(math.tau*phase)
 loc[IDX['Hips'],1]=spec['hipCenter']-spec['hipAmplitude']*math.cos(math.tau*(step-centerShift))
 if dash:loc[IDX['Hips'],0]=0;loc[IDX['Hips'],1]=.270
 q[IDX['Hips']]=mul(ry(1.3*math.sin(math.tau*phase)),rz(1.1*math.cos(math.tau*phase)))
 fk();world('Chest',rx(9 if dash else spec['lean']));world('Head',rx(0 if dash else .30*math.sin(math.tau*step)))
 diagnostics={'phase':phase,'feet':{},'palms':{}}
 for side,offset,s in [('Right',0,-1),('Left',.5,1)]:
  u=(phase+offset)%1;target,qf,qt,info=trajectory(side,u,spec);pole=np.array([s*.14,.14,.40])
  if not info['contact']:
   hip=gp[IDX[side+'UpLeg']];reach=.985*(np.linalg.norm(LOCAL[IDX[side+'Leg']])+np.linalg.norm(LOCAL[IDX[side+'Foot']]))
   horizontal=float(np.sum((target[[0,2]]-hip[[0,2]])**2));oldy=target[1];target[1]=max(target[1],hip[1]-math.sqrt(max(.001,reach*reach-horizontal)));info['extraSwingKneeClearanceAuthor']=float(target[1]-oldy)
  reachable,error=solve(side+'UpLeg',side+'Leg',side+'Foot',target,pole,qf);world(side+'Toe',qt)
  if dash:
   _,minY=foot_shape(side,10,10);target=np.array([FOOT[side]['neutralAnkleX'],-minY+.025,FOOT[side]['neutralAnkleZ']-.09]);qf=rx(10);qt=qf;reachable,error=solve(side+'UpLeg',side+'Leg',side+'Foot',target,pole,qf);world(side+'Toe',qt);info={'contact':False,'anchor':'dash-glide','pitchDegrees':10,'toeWorldPitchDegrees':10,'soleLiftAuthor':.025,'u':0}
  info.update(reachable=bool(reachable),ankleTarget=target.tolist(),ankleErrorAuthor=float(error),actualAnkle=gp[IDX[side+'Foot']].tolist(),actualToe=gp[IDX[side+'Toe']].tolist());diagnostics['feet'][side]=info
 # Receiver bob is damped to35% of hip displacement. Palms match that exact
 # attachment convention; arm movement remains small through the bounce.
 bob=(loc[IDX['Hips'],1]-OLD_LOCAL[IDX['Hips'],1])*.35
 for side,s in [('Right',-1),('Left',1)]:
  target=np.array([-.06,.35,.13]) if side=='Right' else np.array([.06,.35,.15]);target[1]+=bob
  a,b,c,p=[IDX[side+n] for n in ['UpperArm','ForeArm','Hand','Palm']];lower=REST[p]-REST[b]
  reachable,error=solve(side+'UpperArm',side+'ForeArm',side+'Hand',target,np.array([s*.36,.30,-.10]),None,lowerVector=lower)
  # The virtual lower endpoint is the palm; keeping hand local identity leaves
  # the rigid cuff intact and puts that palm exactly on the receiver.
  q[c]=IDENT;fk();diagnostics['palms'][side]={'reachable':bool(reachable),'errorAuthor':float(np.linalg.norm(gp[p]-target)),'target':target.tolist()}
 return loc,q,gp,gq,diagnostics

def skin(v,joints,weights,gp,gq):
 out=np.zeros_like(v,dtype=float)
 for k in range(4):
  ids=joints[:,k];out+=(R.from_quat(gq[ids]).apply(v-REST[ids])+gp[ids])*weights[:,k,None]
 return out

if __name__=='__main__':
 report={};low=np.ones(3)*1e9;high=-low
 for name,spec in GAITS.items():
  unreachable=[];maxp=0;minfloor=100;maxankle=0
  for frame,t in enumerate(np.linspace(0,spec['period'],121)):
   loc,q,gp,gq,d=pose(name,t);points=skin(V,ORDER,WEIGHTS,gp,gq);minfloor=min(minfloor,float(points[:,1].min()));low=np.minimum(low,points.min(0));high=np.maximum(high,points.max(0));maxp=max(maxp,*[p['errorAuthor'] for p in d['palms'].values()]);maxankle=max(maxankle,*[f['ankleErrorAuthor'] for f in d['feet'].values()])
   for side,f in d['feet'].items():
    if not f['reachable']:unreachable.append({'frame':frame,'side':side,'error':f['ankleErrorAuthor'],'contact':f['contact']})
  report[name]={'spec':spec,'unreachable':unreachable,'maxAnkleError':maxankle,'maxPalmError':maxp,'minimumSkinY':minfloor}
 print(json.dumps(report,indent=2));print('envelope',low,high)
 Path(__file__).with_name('light-authoring-audit.json').write_text(json.dumps(report,indent=2)+'\n')
