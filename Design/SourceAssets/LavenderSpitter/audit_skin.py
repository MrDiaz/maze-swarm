"""Independent glTF skin evaluation, without launching Blender/Unity or using GPU."""
import json,struct,math,sys,hashlib,io
from pathlib import Path
import numpy as np
from PIL import Image
OUT=Path(__file__).resolve().parent
sys.path.insert(0,str(OUT))
from audit_glb import glb,acc
j,b,raw=glb(OUT/'lavender-spitter-animated_LOD0.glb')
nodes=j['nodes'];prim=j['meshes'][0]['primitives'][0]
pos=np.array(acc(j,b,prim['attributes']['POSITION']))
ids=np.array(acc(j,b,prim['attributes']['JOINTS_0']),dtype=int)
weights=np.array(acc(j,b,prim['attributes']['WEIGHTS_0']))
skin=j['skins'][0];inv=np.array(acc(j,b,skin['inverseBindMatrices'])).reshape(-1,4,4).transpose(0,2,1)
hom=np.column_stack([pos,np.ones(len(pos))])
parents={c:i for i,n in enumerate(nodes) for c in n.get('children',[])}
names={n.get('name'):i for i,n in enumerate(nodes)}
def quat(q):
 x,y,z,w=q;d=math.sqrt(x*x+y*y+z*z+w*w);x/=d;y/=d;z/=d;w/=d
 return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0],[0,0,0,1]])
anims={}
for a in j['animations']:
 cs=[]
 for c in a['channels']:
  s=a['samplers'][c['sampler']]
  cs.append((c['target']['node'],c['target']['path'],np.array(acc(j,b,s['input'])).flatten(),np.array(acc(j,b,s['output'])),s['interpolation']))
 anims[a['name']]=cs
def evaluate(name,t):
 values={}
 for n,path,ts,vs,interp in anims[name]:
  k=int(np.argmin(np.abs(ts-t)))
  if abs(ts[k]-t)<.000001:val=vs[k]
  else:
   k=max(0,min(len(ts)-2,int(np.searchsorted(ts,t)-1)));u=(t-ts[k])/(ts[k+1]-ts[k]);q0=vs[k];q1=vs[k+1]
   if path=='rotation' and np.dot(q0,q1)<0:q1=-q1
   val=q0 if interp=='STEP' else q0*(1-u)+q1*u
  values[(n,path)]=val
 local=[]
 for i,n in enumerate(nodes):
  if 'matrix'in n:local.append(np.array(n['matrix']).reshape(4,4).T);continue
  T=np.identity(4);T[:3,3]=values.get((i,'translation'),n.get('translation',[0,0,0]))
  R=quat(values.get((i,'rotation'),n.get('rotation',[0,0,0,1])))
  S=np.diag([*values.get((i,'scale'),n.get('scale',[1,1,1])),1])
  local.append(T@R@S)
 worlds={}
 def world(i):
  if i not in worlds:worlds[i]=world(parents[i])@local[i] if i in parents else local[i]
  return worlds[i]
 matrices=np.array([world(i)@inv[k] for k,i in enumerate(skin['joints'])])
 posed=np.zeros((len(pos),4))
 for k in range(4):posed+=np.einsum('nij,nj->ni',matrices[ids[:,k]],hom)*weights[:,k,None]
 return posed[:,:3],worlds,world
schedule=json.loads((OUT/'contact-schedule.json').read_text());plans={(q['clip'],round(q['t']*30),q['side']):q for q in schedule}
sole={s:np.where((pos[:,1]<.012)&((pos[:,0]>0) if s=='L' else (pos[:,0]<0)))[0] for s in ('L','R')}
indices=np.array(acc(j,b,prim['indices'])).flatten().reshape(-1,3)
edges=np.concatenate([indices[:,[0,1]],indices[:,[1,2]],indices[:,[2,0]]]);lens=np.linalg.norm(pos[edges[:,0]]-pos[edges[:,1]],axis=1)
weld={}
for i,p in enumerate(pos):weld.setdefault(tuple(np.round(p,6)),[]).append(i)
report={'maximumUvSeamCrackMeters':0.,'method':'Independent glTF TRS animation + inverse-bind linear blend skin evaluation using exported file; no Blender evaluation shortcuts.','allSamples':0,'minimumMeshHeightMeters':1e9,'maximumStanceSoleShiftMeters':0.,'maximumTargetFootErrorMeters':0.,'maximumEdgeStretchRatio':0.,'maximumEdgeCompressionRatio':1.,'loopClosureMeters':{},'finiteSamples':True,'soleVerticesPerFoot':{s:len(q) for s,q in sole.items()},'maximumWorldFootSlipAtNativeSpeedMeters':0.,'embeddedTexturePixelEquality':{},'animatedBoundsGltfXYZ':None}
for name,dur in [('Idle',2),('Walk',.8),('Spit',.8)]:
 start=None;last=None;stance_refs={}
 for fr in range(round(dur*30)+1):
  t=fr/30;vs,worlds,world=evaluate(name,t)
  if start is None:start=vs
  last=vs
  report['maximumUvSeamCrackMeters']=max(report['maximumUvSeamCrackMeters'],max((float(np.linalg.norm(vs[ix]-vs[ix[0]],axis=1).max()) for ix in weld.values() if len(ix)>1),default=0.))
  bounds=[vs.min(axis=0),vs.max(axis=0)]
  if report['animatedBoundsGltfXYZ'] is None:report['animatedBoundsGltfXYZ']=[q.tolist() for q in bounds]
  else:report['animatedBoundsGltfXYZ']=[np.minimum(report['animatedBoundsGltfXYZ'][0],bounds[0]).tolist(),np.maximum(report['animatedBoundsGltfXYZ'][1],bounds[1]).tolist()]
  report['allSamples']+=1;report['minimumMeshHeightMeters']=min(report['minimumMeshHeightMeters'],float(vs[:,1].min()))
  ratio=np.linalg.norm(vs[edges[:,0]]-vs[edges[:,1]],axis=1)/lens
  report['maximumEdgeStretchRatio']=max(report['maximumEdgeStretchRatio'],float(ratio.max()))
  report['maximumEdgeCompressionRatio']=min(report['maximumEdgeCompressionRatio'],float(ratio.min()))
  report['finiteSamples'] &= bool(np.isfinite(vs).all())
  for s in ('L','R'):
   plan=plans[name,fr,s];p=plan['ankle'];expected=np.array([p[0],p[2],-p[1]])
   actual=world(names['Foot.'+s])[:3,3]
   report['maximumTargetFootErrorMeters']=max(report['maximumTargetFootErrorMeters'],float(np.linalg.norm(actual-expected)))
   if plan['stance']:
    report['maximumStanceSoleShiftMeters']=max(report['maximumStanceSoleShiftMeters'],float(np.max(np.abs(vs[sole[s],1]-pos[sole[s],1]))))
    if name=='Walk':
     worldpos=actual+np.array([0,0,.35*t])
     if s not in stance_refs:stance_refs[s]=worldpos
     report['maximumWorldFootSlipAtNativeSpeedMeters']=max(report['maximumWorldFootSlipAtNativeSpeedMeters'],float(np.linalg.norm(worldpos-stance_refs[s])))
   elif s in stance_refs:del stance_refs[s]
 if name!='Spit':report['loopClosureMeters'][name]=float(np.linalg.norm(start-last,axis=1).max())
src,sb,_=glb('/Volumes/T7 Shield/Assets/Enemies/Lavender Spitter 2026-10-16/mobile-006/LOD0/lavender-spitter_LOD0.glb')
def images(doc,buf):
 out=[]
 for im in doc['images']:
  bv=doc['bufferViews'][im['bufferView']];r=buf[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']]
  out.append(np.array(Image.open(io.BytesIO(r)).convert('RGBA')))
 return out
orig=images(src,sb);new=images(j,b)
for k,arr in enumerate(orig):
 report['embeddedTexturePixelEquality'][src['images'][k].get('name',str(k))]=any(a.shape==arr.shape and np.array_equal(a,arr) for a in new)
report['passed']=report['maximumUvSeamCrackMeters']<.00001 and report['finiteSamples'] and report['minimumMeshHeightMeters']>-.0001 and report['maximumTargetFootErrorMeters']<.0001 and report['maximumStanceSoleShiftMeters']<.0001 and max(report['loopClosureMeters'].values())<.00001 and report['maximumWorldFootSlipAtNativeSpeedMeters']<.0001 and all(report['embeddedTexturePixelEquality'].values())
(OUT/'independent-skin-audit.json').write_text(json.dumps(report,indent=2));print('SKIN_AUDIT',json.dumps(report,indent=2))
