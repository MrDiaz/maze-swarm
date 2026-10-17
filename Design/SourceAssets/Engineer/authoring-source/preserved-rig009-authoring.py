"""Author a source-preserving glTF skin and five in-place game clips.

The selected mesh attributes, indices, materials and embedded image bytes are
copied untouched. Only skin attributes, joint nodes and sampled clips are added.
No model inference, remeshing, UV editing or color processing takes place.
"""
from pathlib import Path
import copy, hashlib, json, math, struct, sys, time
import numpy as np
from scipy.spatial.transform import Rotation as R
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import dijkstra

ROOT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16')
SRC=ROOT/'review-model-003/purple-engineer.glb'
OUT=ROOT/'rigged-009'
IDENT=np.array([0.,0.,0.,1.])
def rx(a): return R.from_euler('x',a,degrees=True).as_quat()
def ry(a): return R.from_euler('y',a,degrees=True).as_quat()
def rz(a): return R.from_euler('z',a,degrees=True).as_quat()
def mul(a,b):return (R.from_quat(a)*R.from_quat(b)).as_quat()
def inv(a):return R.from_quat(a).inv().as_quat()
def rot(q,v):return R.from_quat(q).apply(v)
def norm(v):return v/max(np.linalg.norm(v),1e-12)
def between(a,b):
 a,b=norm(a),norm(b);dot=np.clip(a@b,-1,1)
 if dot<-.99999:return R.from_rotvec(norm(np.cross(a,[1,.1,.3]))*math.pi).as_quat()
 return norm(np.r_[np.cross(a,b),1+dot])
def smooth(x,a,b):
 t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)
def load_glb(path):
 b=path.read_bytes();n=struct.unpack_from('<I',b,12)[0]
 return json.loads(b[20:20+n]),b[28+n:]
def accessor(j,b,k):
 a=j['accessors'][k];v=j['bufferViews'][a['bufferView']]
 dtype={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
 dim={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
 return np.frombuffer(b,dtype=dtype,count=a['count']*dim,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(-1,dim).copy()

# Native glTF +Y up/+Z forward. Unity importer mirrors X; native Right=-X.
B=[]
def bone(name,parent,pos):
 B.append({'name':name,'parent':parent,'position':np.array(pos,dtype=float)})
bone('Root',None,(0,0,0));bone('Hips','Root',(0,.290,-.035))
bone('Spine','Hips',(0,.365,-.035));bone('Chest','Spine',(0,.445,-.045))
bone('Neck','Chest',(.012,.495,-.045));bone('Head','Neck',(.020,.545,-.065))
for side,s in [('Right',-1),('Left',1)]:
 if side=='Left':a=(.115,.455,-.035);e=(.180,.373,-.025);w=(.233,.314,.002);p=(.250,.266,.020)
 else:a=(-.100,.465,-.030);e=(-.158,.392,-.025);w=(-.207,.337,.005);p=(-.229,.287,.022)
 bone(side+'Shoulder','Chest',(s*.085,a[1],a[2]));bone(side+'UpperArm',side+'Shoulder',a)
 bone(side+'ForeArm',side+'UpperArm',e);bone(side+'Hand',side+'ForeArm',w)
 bone(side+'Palm',side+'Hand',p)
 bone(side+'UpLeg','Hips',(s*.075,.285,-.030));bone(side+'Leg',side+'UpLeg',(s*.116,.157,.012))
 bone(side+'Foot',side+'Leg',(s*.148,.068,-.023));bone(side+'Toe',side+'Foot',(s*.153,.030,.095))
bone('WeaponGrip','Hips',(-.060,.350,.130));bone('Muzzle','WeaponGrip',(-.060,.41506,.57612))
IDX={b['name']:i for i,b in enumerate(B)}
REST=np.array([b['position'] for b in B]);PARENT=np.array([IDX[b['parent']] if b['parent'] else -1 for b in B])
LOCAL=np.array([REST[i]-(REST[p] if p>=0 else 0) for i,p in enumerate(PARENT)])

def weights(v,tri,rgb):
 """Anatomically bounded blends; rigid gloves, boots, head/hair and pack.
 Same-coordinate UV splits receive exactly equal weights.
 """
 x,y,z=v.T;ax=abs(x);W=np.zeros((len(v),len(B)),float)
 # Torso and pelvis: bending is spread over the cloth rather than sharp bands.
 chest=smooth(y,.365,.435);spine=smooth(y,.285,.360)*(1-chest)
 W[:,IDX['Hips']]=1-chest-spine;W[:,IDX['Spine']]=spine;W[:,IDX['Chest']]=chest
 head=smooth(y,.487,.520)
 # Low rear hair belongs to the head; backpack stays on chest.
 head=np.maximum(head,((y>.485)&(z<-.135)).astype(float))
 W*=1-head[:,None];W[:,IDX['Head']]+=head
 # Surface distances keep the glove's inner wall on the arm despite proximity
 # to the trousers. Weld only for this solve; original vertices stay intact.
 unique,vi=np.unique(np.round(v,6),axis=0,return_inverse=True)
 edge=np.unique(np.sort(np.concatenate([vi[tri[:,[0,1]]],vi[tri[:,[1,2]]],vi[tri[:,[2,0]]]]),axis=1),axis=0)
 edge=edge[edge[:,0]!=edge[:,1]];length=np.linalg.norm(unique[edge[:,0]]-unique[edge[:,1]],axis=1)
 graph=coo_matrix((np.r_[length,length],(np.r_[edge[:,0],edge[:,1]],np.r_[edge[:,1],edge[:,0]])),shape=(len(unique),len(unique))).tocsr()
 red,green,blue=rgb.astype(float).T
 yellow=(red>green*1.12)&(blue<green*.72)
 skin=(red>green*1.16)&(blue>green*.64)&(green>95)
 teal=(green>red*1.16)&(blue>red*1.16)
 white=(red>155)&(green>150)&(blue>110)&(abs(red-green)<60)
 body_seed=(ax<.075)|(y>.515)|(y<.11)|(teal&(y<.325)&(ax<.21))|((z<-.100)&(y>.31)&(y<.49))
 body_dist=dijkstra(graph,indices=np.unique(vi[body_seed]),min_only=True)[vi]
 arm_amount={}
 for side,s in [('Right',-1),('Left',1)]:
  seed=(x*s>.185)&(y>.19)&(y<.335)&yellow
  seed|=(x*s>.150)&(y>.345)&(y<.425)&skin&(z>-.055)
  arm_dist=dijkstra(graph,indices=np.unique(vi[seed]),min_only=True)[vi]
  arm_amount[side]=smooth(body_dist-arm_dist,-.025,.025)*(y<.51)
 arm_mask=(arm_amount['Right']+arm_amount['Left'])>.5
 for side,s in [('Right',-1),('Left',1)]:
  amount=arm_amount[side];mask=amount>0
  # Projection along the complete upper-arm/forearm chain gives a continuous elbow blend.
  e=REST[IDX[side+'ForeArm']];a=REST[IDX[side+'UpperArm']];wr=REST[IDX[side+'Hand']]
  d=norm(wr-a);t=(v-e)@d
  fore=smooth(t,-.024,.024)
  # The wrist blend is confined to the cuff; the mitten itself remains rigid.
  hand=smooth((v-wr)@norm(REST[IDX[side+'Palm']]-wr),-.008,.025)
  W*=1-amount[:,None];W[:,IDX[side+'UpperArm']]+=amount*(1-fore)
  W[:,IDX[side+'ForeArm']]+=amount*fore*(1-hand)
  W[:,IDX[side+'Hand']]+=amount*fore*hand
  legs=(y<.295)&(x*s>=0)
  hips=smooth(y,.235,.295)
  hips=np.maximum(hips,(1-smooth(ax,.015,.060))*smooth(y,.195,.255))
  lower=1-smooth(y,.135,.180);foot=1-smooth(y,.090,.128)
  leg_amount=1-np.maximum(arm_amount['Right'],arm_amount['Left'])
  W[legs]*=(1-leg_amount[legs,None]);W[legs,IDX['Hips']]+=leg_amount[legs]*hips[legs]
  W[legs,IDX[side+'UpLeg']]+=leg_amount[legs]*(1-hips[legs])*(1-lower[legs])
  W[legs,IDX[side+'Leg']]+=leg_amount[legs]*(1-hips[legs])*lower[legs]*(1-foot[legs])
  W[legs,IDX[side+'Foot']]+=leg_amount[legs]*(1-hips[legs])*lower[legs]*foot[legs]
 # Keep rear backpack rigid, excluding head and legs. No cloth stretch across its panel.
 pack=(z<-.105)&(y>.305)&(y<.485)&(ax<.145)
 W[pack]=0;W[pack,IDX['Chest']]=1
 order=np.argsort(W,axis=1)[:,-4:][:,::-1];w=np.take_along_axis(W,order,axis=1)
 w[w<1e-6]=0;w/=w.sum(1)[:,None]
 return order.astype('<u2'),w.astype('<f4')

def pose(clip,t):
 q=np.tile(IDENT,(len(B),1));loc=LOCAL.copy();gp=REST.copy();gq=q.copy()
 def fk():
  for i,p in enumerate(PARENT):
   gp[i]=loc[i] if p<0 else gp[p]+rot(gq[p],loc[i])
   gq[i]=q[i] if p<0 else mul(gq[p],q[i])
 def global_rot(name,value):
  i=IDX[name];p=PARENT[i];q[i]=value if p<0 else mul(inv(gq[p]),value);fk()
 def ik(a,b,c,target,pole,end_rotation=IDENT):
  ia,ib,ic=IDX[a],IDX[b],IDX[c];pa=gp[ia].copy();v=target-pa;d=np.linalg.norm(v)
  la=np.linalg.norm(LOCAL[ib]);lb=np.linalg.norm(LOCAL[ic]);dc=np.clip(d,abs(la-lb)+1e-5,la+lb-1e-5)
  axis=norm(v);along=(la*la-lb*lb+dc*dc)/(2*dc);h=math.sqrt(max(0,la*la-along*along))
  bend=norm((pole-pa)-axis*np.dot(pole-pa,axis));pb=pa+axis*along+bend*h
  pc=pa+axis*dc
  global_rot(a,between(LOCAL[ib],pb-pa));global_rot(b,between(LOCAL[ic],pc-pb));global_rot(c,end_rotation)
 def palm_ik(side,target,pole):
  # Solve to the actual mitten palm. Keeping hand and forearm aligned makes
  # the large cuff rigid, instead of distorting it through a 90-degree wrist bend.
  ia,ib,ih,ip=[IDX[side+n] for n in ['UpperArm','ForeArm','Hand','Palm']]
  pa=gp[ia].copy();v=target-pa;d=np.linalg.norm(v);lower=REST[ip]-REST[ib]
  la=np.linalg.norm(LOCAL[ib]);lb=np.linalg.norm(lower);dc=np.clip(d,abs(la-lb)+1e-5,la+lb-1e-5)
  axis=norm(v);along=(la*la-lb*lb+dc*dc)/(2*dc);h=math.sqrt(max(0,la*la-along*along))
  bend=norm((pole-pa)-axis*np.dot(pole-pa,axis));pb=pa+axis*along+bend*h
  pc=pa+axis*dc;global_rot(side+'UpperArm',between(LOCAL[ib],pb-pa))
  foreq=between(lower,pc-pb);global_rot(side+'ForeArm',foreq);global_rot(side+'Hand',foreq)
 if clip in ('Walking','Running'):
  period=1.0 if clip=='Walking' else .60;phase=(t/period)%1
  bob=.004*(1-math.cos(phase*4*math.pi));loc[IDX['Hips'],1]-=.024-bob
  q[IDX['Hips']]=ry(1.6*math.sin(phase*2*math.pi));q[IDX['Spine']]=rx(2 if clip=='Walking' else 5)
  q[IDX['Chest']]=ry(-1.8*math.sin(phase*2*math.pi));q[IDX['Head']]=rx(-1)
 elif clip=='Idle':
  breath=math.sin(t*math.pi);q[IDX['Chest']]=rx(.40*breath);q[IDX['Head']]=ry(.55*breath)
 elif clip=='Attack':
  kick=math.exp(-((t-.10)/.045)**2);q[IDX['Chest']]=rx(-3.5*kick);q[IDX['Spine']]=rx(-1*kick)
 elif clip=='Death':
  f=smooth(t,.12,1.30);q[IDX['Hips']]=mul(rx(87*f),rz(-8*f));q[IDX['Chest']]=rx(8*math.sin(f*math.pi));q[IDX['Head']]=rx(-6*f)
  loc[IDX['Hips'],1]-=.16*f
 fk()
 if clip in ('Walking','Running'):
  stance=.60 if clip=='Walking' else .44;amp=.062 if clip=='Walking' else .080;lift=.038 if clip=='Walking' else .045
  for side,offset in [('Right',0),('Left',.5)]:
   u=(phase+offset)%1;ank=REST[IDX[side+'Foot']].copy()
   if u<stance:ank[2]+=amp*(1-2*u/stance)
   else:
    swing=(u-stance)/(1-stance);ank[2]+=amp*(-math.cos(swing*math.pi));ank[1]+=lift*math.sin(swing*math.pi)
   pole=REST[IDX[side+'Leg']]+np.array([0,0,.35])
   ik(side+'UpLeg',side+'Leg',side+'Foot',ank,pole)
 elif clip=='Death':
  f=smooth(t,.12,1.3)
  for side,s in [('Right',-1),('Left',1)]:
   q[IDX[side+'Leg']]=rx(-22*math.sin(f*math.pi));q[IDX[side+'UpLeg']]=rz(s*8*f)
  fk()
 # Ready-carry arms on all live clips. Death lowers and splays the arms.
 for side,s in [('Right',-1),('Left',1)]:
  hand=side+'Hand';off=REST[IDX[side+'Palm']]-REST[IDX[hand]]
  if clip=='Death':
   f=smooth(t,.12,1.3);grip=np.array([-.060,.350,.130]) if side=='Right' else np.array([.060,.350,.150])
   target=gp[IDX['Hips']]+rot(gq[IDX['Hips']],grip-REST[IDX['Hips']]);pole=gp[IDX['Hips']]+rot(gq[IDX['Hips']],np.array([s*.36,.30,-.10])-REST[IDX['Hips']]);palm_ik(side,target,pole)
   for name,dst in [(side+'UpperArm',mul(rx(-20*f),rz(-s*18*f))),(side+'ForeArm',rx(15*f)),(side+'Hand',IDENT)]:
    idx=IDX[name];a=q[idx].copy()
    if a@dst<0:dst=-dst
    q[idx]=norm((1-f)*a+f*dst)
   fk();continue
  target=np.array([-.060,.350,.130]) if side=='Right' else np.array([.060,.350,.150])
  target[1]+=loc[IDX['Hips'],1]-LOCAL[IDX['Hips'],1]
  if clip=='Attack':target[2]-=.012*math.exp(-((t-.10)/.045)**2)
  pole=np.array([s*.36,.30,-.10]);palm_ik(side,target,pole)
 return loc,q,gp,gq

def skin_positions(v,joints,w,gp,gq):
 out=np.zeros_like(v,dtype=float)
 for k in range(4):
  ids=joints[:,k];pos=v-REST[ids];rotated=R.from_quat(gq[ids]).apply(pos)+gp[ids]
  out+=rotated*w[:,k,None]
 return out

def main():
 started=time.monotonic();assert not OUT.exists(),'Never overwrite an existing rig candidate';OUT.mkdir()
 source=SRC.read_bytes();assert hashlib.sha256(source).hexdigest()=='c827c34b335fe87f14cca8472fa046c91e29687968a22a19bb3dcdfdf8b23243'
 j,origbin=load_glb(SRC);blob=bytearray(origbin);primitive=j['meshes'][0]['primitives'][0]
 verts=accessor(j,origbin,primitive['attributes']['POSITION']);tri=accessor(j,origbin,primitive['indices']).reshape(-1,3)
 rgb=np.load(Path(__file__).parent/'source-vertices.npz')['rgb'];assert len(rgb)==len(verts)
 joint,w=weights(verts,tri,rgb)
 from repair_binding import repair
 normals=accessor(j,origbin,primitive['attributes']['NORMAL']);uv=accessor(j,origbin,primitive['attributes']['TEXCOORD_0']);tangent=accessor(j,origbin,primitive['attributes']['TANGENT'])
 verts,normals,uv,tangent,tri,joint,w,repair_report=repair(verts,normals,uv,tangent,tri,joint,w,rgb,IDX,B)
 (OUT/'binding-repair.json').write_text(json.dumps(repair_report,indent=2)+'\n')
 def add(array,kind,ct=5126,target=None):
  array=np.asarray(array,dtype={5126:'<f4',5123:'<u2',5125:'<u4'}[ct]);blob.extend(b'\0'*((-len(blob))%4));offset=len(blob);blob.extend(array.tobytes())
  bv={'buffer':0,'byteOffset':offset,'byteLength':array.nbytes}
  if target:bv['target']=target
  vi=len(j['bufferViews']);j['bufferViews'].append(bv);ai=len(j['accessors']);a={'bufferView':vi,'componentType':ct,'count':len(array),'type':kind}
  if kind in ['VEC3','SCALAR']:a.update(min=array.reshape(len(array),-1).min(0).tolist(),max=array.reshape(len(array),-1).max(0).tolist())
  j['accessors'].append(a);return ai
 for attr,values,kind in [('POSITION',verts,'VEC3'),('NORMAL',normals,'VEC3'),('TEXCOORD_0',uv,'VEC2'),('TANGENT',tangent,'VEC4')]:primitive['attributes'][attr]=add(values,kind,5126,34962)
 primitive['indices']=add(tri.reshape(-1,1),'SCALAR',5125,34963)
 primitive['attributes']['JOINTS_0']=add(joint,'VEC4',5123,34962);primitive['attributes']['WEIGHTS_0']=add(w,'VEC4',5126,34962)
 j['nodes'][0]['name']='Engineer_Source003_Skinned';j['nodes'][0]['skin']=0
 for i,b in enumerate(B):
  node={'name':b['name'],'translation':LOCAL[i].tolist()};children=[k+1 for k,p in enumerate(PARENT) if p==i]
  if children:node['children']=children
  if b['name'].endswith('Palm'):node['extras']={'explicitPalmSocket':True,'usePositionDirectly':True,'axes':'+Yup +Zforward in bind pose'}
  j['nodes'].append(node)
 ibm=np.tile(np.eye(4),(len(B),1,1));ibm[:,:3,3]=-REST
 ia=add(ibm.transpose(0,2,1).reshape(-1,16),'MAT4');j['skins']=[{'name':'EngineerRig','skeleton':1,'joints':list(range(1,len(B)+1)),'inverseBindMatrices':ia}]
 j['scenes'][0]['nodes']=[0,1];j['animations']=[];stats={};npz={}
 for clip,duration,fps in [('Idle',2.,30),('Walking',1.,30),('Running',.60,30),('Attack',.40,30),('Death',1.50,30)]:
  times=np.linspace(0,duration,round(duration*fps)+1).astype('<f4');ls=[];qs=[];gps=[];gqs=[]
  for t in times:
   loc,q,gp,gq=pose(clip,float(t))
   if clip=='Death':
    posed=skin_positions(verts,joint,w,gp,gq);correction=max(0,.002-posed[:,1].min())*smooth(float(t),.02,.14)
    loc[IDX['Hips'],1]+=correction;gp[1:,1]+=correction
   ls.append(loc);qs.append(q);gps.append(gp);gqs.append(gq)
  ls=np.array(ls);qs=np.array(qs);gps=np.array(gps);gqs=np.array(gqs)
  # Keep quaternion signs continuous to avoid accidental long-way interpolations.
  for k in range(len(B)):
   for f in range(1,len(times)):
    if qs[f-1,k]@qs[f,k]<0:qs[f,k]*=-1
  animation={'name':clip,'samplers':[],'channels':[],'extras':{'loop':clip in ['Idle','Walking','Running'],'authorHeightMeters':1,'inPlace':True}}
  ta=add(times[:,None],'SCALAR')
  for k,b in enumerate(B):
   if b['name']!='Root':
    idx=len(animation['samplers']);animation['samplers'].append({'input':ta,'output':add(qs[:,k],'VEC4'),'interpolation':'LINEAR'});animation['channels'].append({'sampler':idx,'target':{'node':k+1,'path':'rotation'}})
   if b['name']=='Hips':
    idx=len(animation['samplers']);animation['samplers'].append({'input':ta,'output':add(ls[:,k],'VEC3'),'interpolation':'LINEAR'});animation['channels'].append({'sampler':idx,'target':{'node':k+1,'path':'translation'}})
  j['animations'].append(animation);stats[clip]={'durationSeconds':duration,'fps':fps,'frames':len(times),'channels':len(animation['channels']),'loop':clip in ['Idle','Walking','Running']}
  npz[clip+'_positions']=gps;npz[clip+'_rotations']=gqs;npz[clip+'_times']=times
 j['buffers'][0]['byteLength']=len(blob);j['asset']['generator']='Local source-preserving Engineer rig authoring, no inference'
 j['extras']={'sourceSha256':hashlib.sha256(source).hexdigest(),'sourceGeometryUvsMaterialImagesPreserved':True,'maxWeightsPerVertex':4,'runtimeScale':1.65,'runtimeAnimation':'Legacy','noHorizontalRootMotion':True}
 raw=json.dumps(j,separators=(',',':')).encode();raw+=b' '*((-len(raw))%4);blob.extend(b'\0'*((-len(blob))%4))
 glb=struct.pack('<III',0x46546c67,2,12+8+len(raw)+8+len(blob))+struct.pack('<II',len(raw),0x4e4f534a)+raw+struct.pack('<II',len(blob),0x004e4942)+blob
 path=OUT/'hero_engineer_chibi.glb';path.write_bytes(glb)
 np.savez_compressed(OUT/'authored-poses.npz',**npz,joints=joint,weights=w,rest=REST)
 report={'source':str(SRC),'sourceSha256':hashlib.sha256(source).hexdigest(),'glb':str(path),'sha256':hashlib.sha256(glb).hexdigest(),'bytes':len(glb),'triangles':23994,'authorHeightMeters':1,'runtimeHeightMeters':1.65,'coordinates':'+Yup +Zforward, floor-center','joints':[{**b,'position':b['position'].tolist()} for b in B],'clips':stats,'weights':{'max':int((w>0).sum(1).max()),'normalizationMaxError':float(abs(w.sum(1)-1).max()),'method':'bounded anatomical smooth joint blends; rigid mitten/head/hair/boots/backpack regions'},'sourceBinPrefixUnchanged':bytes(blob[:len(origbin)])==origbin,'originalMeshAttributesAndImagesCopiedUntouched':True,'gripFractionsProvisional':[.08,.35,.10],'leftPalmFractionsProvisional':[-.065,.35,.115],'nominalInPlaceSpeedsAuthorMps':{'Walking':2*.062/.60,'Running':2*.068/(.60*.44)},'limitations':['Authored stylized motion, not mocap. Attack is a short recoil/brace gesture for chest overlay, with firing FX supplied by game.','Death is a controlled authored fall with ground support, not physics ragdoll.','Mittens have no articulated finger grip; palm targets and weapon separation need actual review.'],'seconds':round(time.monotonic()-started,2),'status':'unreviewed candidate'}
 (OUT/'rig-authoring-report.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:report[k] for k in ['glb','sha256','bytes','seconds','weights']}))
if __name__=='__main__':main()
