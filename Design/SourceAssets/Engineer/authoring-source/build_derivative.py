from pathlib import Path
import copy,hashlib,json,struct,time,numpy as np
from author_motion import *
OUT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16/gait-011')

def write_glb(j,blob,path):
 j['buffers']=[{'byteLength':len(blob)}];raw=json.dumps(j,separators=(',',':')).encode();raw+=b' '*((-len(raw))%4);blob=bytearray(blob);blob.extend(b'\0'*((-len(blob))%4));data=struct.pack('<III',0x46546c67,2,28+len(raw)+len(blob))+struct.pack('<II',len(raw),0x4e4f534a)+raw+struct.pack('<II',len(blob),0x004e4942)+blob;path.write_bytes(data)

def compact(j,blob):
 j=copy.deepcopy(j);used=set()
 for m in j['meshes']:
  for p in m['primitives']:used.update(p['attributes'].values());used.add(p['indices'])
 for skin in j['skins']:used.add(skin['inverseBindMatrices'])
 for a in j['animations']:
  for s in a['samplers']:used.update([s['input'],s['output']])
 amap={a:k for k,a in enumerate(sorted(used))};ac=[copy.deepcopy(j['accessors'][i]) for i in sorted(used)];bvs=sorted(set(a['bufferView'] for a in ac)|set(i['bufferView'] for i in j['images']));vmap={a:k for k,a in enumerate(bvs)};bv=[];binary=bytearray()
 for i in bvs:
  v=copy.deepcopy(j['bufferViews'][i]);off=v.get('byteOffset',0);payload=blob[off:off+v['byteLength']];binary.extend(b'\0'*((-len(binary))%4));v['byteOffset']=len(binary);binary.extend(payload);bv.append(v)
 for a in ac:a['bufferView']=vmap[a['bufferView']]
 for i in j['images']:i['bufferView']=vmap[i['bufferView']]
 for m in j['meshes']:
  for p in m['primitives']:p['attributes']={k:amap[v] for k,v in p['attributes'].items()};p['indices']=amap[p['indices']]
 for skin in j['skins']:skin['inverseBindMatrices']=amap[skin['inverseBindMatrices']]
 for a in j['animations']:
  for s in a['samplers']:s['input']=amap[s['input']];s['output']=amap[s['output']]
 j['accessors']=ac;j['bufferViews']=bv;return j,binary

def main():
 assert not OUT.exists();OUT.mkdir();j=copy.deepcopy(J);blob=bytearray(BIN);oldhash=hashlib.sha256(BASE.read_bytes()).hexdigest();assert oldhash=='67efe1187f47d8706d4910cdfc57f95ded0b56748731ceff323ca01744fc2c93'
 def add(values,kind,ctype=5126,target=None):
  arr=np.asarray(values,dtype={5126:'<f4',5123:'<u2'}[ctype]);blob.extend(b'\0'*((-len(blob))%4));off=len(blob);blob.extend(arr.tobytes());view={'buffer':0,'byteOffset':off,'byteLength':arr.nbytes}
  if target:view['target']=target
  vi=len(j['bufferViews']);j['bufferViews'].append(view);ai=len(j['accessors']);a={'bufferView':vi,'componentType':ctype,'count':len(arr),'type':kind}
  if kind in ['SCALAR','VEC3']:a.update(min=arr.reshape(len(arr),-1).min(0).tolist(),max=arr.reshape(len(arr),-1).max(0).tolist())
  j['accessors'].append(a);return ai
 p=j['meshes'][0]['primitives'][0];p['attributes']['JOINTS_0']=add(ORDER,'VEC4',5123,34962);p['attributes']['WEIGHTS_0']=add(WEIGHTS,'VEC4',5126,34962)
 inverse=accessor(J,BIN,J['skins'][0]['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
 for side in ['Right','Left']:
  ti=IDX[side+'Toe'];j['nodes'][ti+1]['translation']=LOCAL[ti].tolist();inverse[ti,:3,3]=-REST[ti]
 j['skins'][0]['inverseBindMatrices']=add(inverse.transpose(0,2,1).reshape(-1,16),'MAT4')
 clips={};dense={};poses={};metrics={}
 for name,duration in [('Walking',.8),('Jogging',.62),('Running',.5),('Dash',.16)]:
  if name=='Dash':times=np.array([0,.16]);spec=None
  else:
   spec=GAITS[name];phaseEvents=[0,.5,1,spec['stanceFraction'],(.5+spec['stanceFraction'])%1]
   for s in [0,.5]:
    phaseEvents.extend((s+spec['stanceFraction']*f)%1 for f in [.16,.4,.55,.73])
   contactTimes=np.r_[np.linspace(0,spec['contactSeconds'],round(spec['contactSeconds']*480)+1),duration*.5+np.linspace(0,spec['contactSeconds'],round(spec['contactSeconds']*480)+1)]%duration
   times=np.unique(np.round(np.r_[np.linspace(0,duration,round(duration*240)+1),np.array(phaseEvents)*duration,contactTimes],9))
  ls=[];qs=[];gpall=[];gqall=[];samples=[]
  for k,t in enumerate(times):
   loc,q,gp,gq,d=pose(name,0 if k==len(times)-1 and name!='Dash' else t);ls.append(loc);qs.append(q);gpall.append(gp);gqall.append(gq);samples.append({'seconds':float(t),**d})
  ls=np.array(ls);qs=np.array(qs);gpall=np.array(gpall);gqall=np.array(gqall)
  for node in range(len(REST)):
   for k in range(1,len(times)):
    if qs[k-1,node]@qs[k,node]<0:qs[k,node]*=-1
  animation={'name':name,'samplers':[],'channels':[],'extras':{'loop':name!='Dash','noRootMotion':True,'authorHeightMeters':1,'runtimeHeightMeters':1.65,'nominalSampleRate':240,'contactSampleRate':480,'nativeRuntimeMetersPerSecond':None if name=='Dash' else spec['nativeRuntimeSpeed']}}
  ta=add(times[:,None],'SCALAR')
  for node in range(1,len(REST)):
   si=len(animation['samplers']);animation['samplers'].append({'input':ta,'output':add(qs[:,node],'VEC4'),'interpolation':'LINEAR'});animation['channels'].append({'sampler':si,'target':{'node':node+1,'path':'rotation'}})
  si=len(animation['samplers']);animation['samplers'].append({'input':ta,'output':add(ls[:,IDX['Hips']],'VEC3'),'interpolation':'LINEAR'});animation['channels'].append({'sampler':si,'target':{'node':IDX['Hips']+1,'path':'translation'}})
  clips[name]=animation;dense[name]=samples;poses[name+'_times']=times;poses[name+'_positions']=gpall;poses[name+'_rotations']=gqall
  metrics[name]={'durationSeconds':duration,'keysPerChannel':len(times),'maxPalmErrorAuthor':max(p['errorAuthor'] for d in samples for p in d['palms'].values()),'maxAnkleReachErrorAuthor':max(f['ankleErrorAuthor'] for d in samples for f in d['feet'].values()),'unreachableSamples':sum(not f['reachable'] for d in samples for f in d['feet'].values()),'loopLocalTranslationMaxError':float(np.max(abs(ls[0]-ls[-1]))),'loopQuaternionMaxError':float(np.max(abs(qs[0]-qs[-1])))}
 old={a['name']:a for a in j['animations']};j['animations']=[old['Idle'],clips['Walking'],clips['Jogging'],clips['Running'],old['Attack'],old['Death'],clips['Dash']]
 j['extras'].update(gaitDerivative='011 staging',sourceRig009Sha256=oldhash,meshGeometryUvColorIndexBytesUnchanged=True,toeBindingDerivative=True,maxWeightsPerVertex=int((WEIGHTS>0).sum(1).max()),rootFixedHipsLateralSwayMaxAuthor=.010,weaponBobMultiplier=.35)
 j,binary=compact(j,blob);path=OUT/'hero_engineer_chibi.glb';write_glb(j,binary,path)
 # Exact source preservation gates after compaction.
 result,rb=load_glb(path);rp=result['meshes'][0]['primitives'][0]
 for attr in ['POSITION','NORMAL','TEXCOORD_0','TANGENT']:assert accessor(result,rb,rp['attributes'][attr]).tobytes()==accessor(J,BIN,PRIM['attributes'][attr]).tobytes()
 assert accessor(result,rb,rp['indices']).tobytes()==accessor(J,BIN,PRIM['indices']).tobytes()
 for oldim,newim in zip(J['images'],result['images']):
  ov=J['bufferViews'][oldim['bufferView']];nv=result['bufferViews'][newim['bufferView']];assert BIN[ov['byteOffset']:ov['byteOffset']+ov['byteLength']]==rb[nv['byteOffset']:nv['byteOffset']+nv['byteLength']]
 for name in ['Idle','Attack','Death']:
  before=next(a for a in J['animations'] if a['name']==name);after=next(a for a in result['animations'] if a['name']==name);assert before['channels']==after['channels']
  for a,b in zip(before['samplers'],after['samplers']):
   for k in ['input','output']:assert accessor(J,BIN,a[k]).tobytes()==accessor(result,rb,b[k]).tobytes()
 # At rest every skin joint maps the original surface to itself, including new toes.
 restInverse=accessor(result,rb,result['skins'][0]['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);restError=0
 for i in range(len(REST)):
  world=np.eye(4);world[:3,3]=REST[i];restError=max(restError,float(np.max(abs(world@restInverse[i]-np.eye(4)))))
 assert restError<1e-6 and (WEIGHTS>0).sum(1).max()<=3
 np.savez_compressed(OUT/'authored-poses.npz',**poses)
 contact={'phaseConvention':'Normalized phase0 Right initial strike; .5 Left initial strike; increasing phase always forward. Loop endpoint1=0.','runtimeScale':1.65,'gaits':GAITS,'feet':{s:{k:(v.tolist() if isinstance(v,np.ndarray) else v) for k,v in f.items() if k!='soleVertexIds'} for s,f in FOOT.items()},'phaseWindows':{name:{'Right':[[0,spec['stanceFraction']]],'Left':[[.5,min(1,.5+spec['stanceFraction'])]]+([[0,spec['stanceFraction']-.5]] if spec['stanceFraction']>.5 else [])} for name,spec in GAITS.items()},'denseSamples':dense,'dash':{'durationSeconds':.16,'plantedContacts':False,'nativeSpeedClaim':None},'landmarkCoordinates':'Native glTF model-space. Derive foot local by subtracting original Foot bind position, Toe local by subtracting revised Toe bind position; all bind rotations identity. Unity mirrors X.','footBindPositions':{s:REST[IDX[s+'Foot']].tolist() for s in FOOT},'toeBindPositions':{s:REST[IDX[s+'Toe']].tolist() for s in FOOT},'hipsSway':'Cyclic bounded X ±.01m; Root position fixed. Do not strip this cosmetic sway.','weaponBobMultiplier':.35}
 (OUT/'CONTACTS.json').write_text(json.dumps(contact,indent=2)+'\n')
 report={'source':str(BASE),'sourceSha256':oldhash,'glb':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'triangles':24190,'joints':26,'maxWeights':int((WEIGHTS>0).sum(1).max()),'restBindIdentityMaxError':restError,'sourceMeshAttributesIndicesImagesByteIdentical':True,'IdleAttackDeathSamplerBytesIdentical':True,'toeBindingOnly':{'old':{s:OLD_REST[IDX[s+'Toe']].tolist() for s in FOOT},'new':{s:REST[IDX[s+'Toe']].tolist() for s in FOOT},'nonToeJointsAndBindMatricesUnchanged':True},'clips':metrics,'status':'unreviewed staging derivative'}
 (OUT/'authoring-report.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
