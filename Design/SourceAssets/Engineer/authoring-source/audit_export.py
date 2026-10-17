"""Independent glTF interpolation, skin, bind and contact audit."""
from pathlib import Path
import sys,json,numpy as np
from scipy.spatial.transform import Rotation as R,Slerp
from scipy.spatial import cKDTree
from author_motion import load_glb,accessor,BASE,FOOT,GAITS
OUT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16/gait-011')
class Model:
 def __init__(self,path):
  self.j,self.b=load_glb(path);j=self.j;b=self.b;self.p=j['meshes'][0]['primitives'][0];self.v=accessor(j,b,self.p['attributes']['POSITION']);self.jo=accessor(j,b,self.p['attributes']['JOINTS_0']);self.w=accessor(j,b,self.p['attributes']['WEIGHTS_0']);self.nodes=j['skins'][0]['joints'];self.names={j['nodes'][n]['name']:i for i,n in enumerate(self.nodes)};lookup={n:i for i,n in enumerate(self.nodes)};self.parent=np.full(len(self.nodes),-1)
  for n in self.nodes:
   for c in j['nodes'][n].get('children',[]):
    if c in lookup:self.parent[lookup[c]]=lookup[n]
  self.local=np.array([j['nodes'][n].get('translation',[0,0,0]) for n in self.nodes],dtype=float);self.q=np.array([j['nodes'][n].get('rotation',[0,0,0,1]) for n in self.nodes],dtype=float);self.ib=accessor(j,b,j['skins'][0]['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);self.anims={}
  for a in j['animations']:
   channels=[]
   for c in a['channels']:
    s=a['samplers'][c['sampler']];times=accessor(j,b,s['input'])[:,0].astype(float);values=accessor(j,b,s['output']).astype(float);path=c['target']['path'];channels.append((lookup[c['target']['node']],path,times,values,Slerp(times,R.from_quat(values)) if path=='rotation' else None))
   self.anims[a['name']]=channels
 def pose(self,clip,t):
  loc=self.local.copy();q=self.q.copy()
  for i,path,times,values,sl in self.anims[clip]:
   ts=float(np.clip(t,times[0],times[-1]))
   if path=='rotation':q[i]=sl([ts]).as_quat()[0]
   else:
    k=min(len(times)-1,max(1,int(np.searchsorted(times,ts,side='right'))));u=(ts-times[k-1])/(times[k]-times[k-1]);loc[i]=values[k-1]*(1-u)+values[k]*u
  gp=loc.copy();gq=q.copy()
  for i,p in enumerate(self.parent):
   if p>=0:gp[i]=gp[p]+R.from_quat(gq[p]).apply(loc[i]);gq[i]=(R.from_quat(gq[p])*R.from_quat(q[i])).as_quat()
  return gp,gq
 def skin(self,gp,gq):
  out=np.zeros_like(self.v,dtype=float)
  for k in range(4):
   ids=self.jo[:,k];v=np.einsum('nij,nj->ni',self.ib[ids,:3,:3],self.v)+self.ib[ids,:3,3];out+=(R.from_quat(gq[ids]).apply(v)+gp[ids])*self.w[:,k,None]
  return out
 def marker(self,gp,gq,bone,offset):
  i=self.names[bone];return gp[i]+R.from_quat(gq[i]).apply(offset)
new=Model(OUT/'hero_engineer_chibi.glb');old=Model(BASE);imports=np.load(OUT/'blender-import-pose-samples.npz');comparisons=[]
for key in imports.files:
 clip,ts=key.rsplit('_',1);gp,gq=new.pose(clip,float(ts));expected=new.skin(gp,gq);actual=imports[key];d=cKDTree(expected).query(actual)[0];dr=cKDTree(actual).query(expected)[0];comparisons.append({'key':key,'maxErrorAuthorMeters':float(max(d.max(),dr.max()))})
(OUT/'independent-sample-comparisons.json').write_text(json.dumps(comparisons,indent=2)+'\n')
print('maximum imported discrepancy',max(c['maxErrorAuthorMeters'] for c in comparisons),flush=True)
assert max(c['maxErrorAuthorMeters'] for c in comparisons)<1e-4
low=np.ones(3)*1e9;high=-low;clips={};preserved={};boundsByClip={}
durations={'Idle':2,'Walking':.8,'Jogging':.62,'Running':.5,'Attack':.4,'Death':1.5,'Dash':.16}
for clip,duration in durations.items():
 floor=1e9;grip=0;sourceDiff=0;clow=np.ones(3)*1e9;chigh=-clow;rootMax=0
 times=np.r_[0,duration,(np.arange(round(duration*480))+.5)/480] if clip in ['Walking','Jogging','Running'] else np.linspace(0,duration,round(duration*240)+1)
 for t in times:
  gp,gq=new.pose(clip,t);v=new.skin(gp,gq);clow=np.minimum(clow,v.min(0));chigh=np.maximum(chigh,v.max(0));floor=min(floor,float(v[:,1].min()));rootMax=max(rootMax,float(np.linalg.norm(gp[0])))
  if clip in ['Idle','Attack','Death']:
   op,oq=old.pose(clip,t);sourceDiff=max(sourceDiff,float(np.linalg.norm(v-old.skin(op,oq),axis=1).max()))
  if clip in ['Walking','Jogging','Running','Dash']:
   bob=(gp[new.names['Hips'],1]-.29)*.35
   for s,x,z in [('Right',-.06,.13),('Left',.06,.15)]:grip=max(grip,float(np.linalg.norm(gp[new.names[s+'Palm']]-[x,.35+bob,z])))
 low=np.minimum(low,clow);high=np.maximum(high,chigh);boundsByClip[clip]=[clow.tolist(),chigh.tolist()]
 a,b=new.pose(clip,0),new.pose(clip,duration);loop=max(float(np.max(abs(a[0]-b[0]))),float(np.max(np.minimum(np.linalg.norm(a[1]-b[1],axis=1),np.linalg.norm(a[1]+b[1],axis=1)))))
 clips[clip]={'durationSeconds':duration,'minimumSkinYAuthor':floor,'maximumGroundPenetrationRuntimeMeters':max(0,-floor*1.65),'maximumPalmErrorRuntimeMeters':grip*1.65,'rootTranslationMax':rootMax,'loopPoseMaxError':loop}
 if clip in ['Idle','Attack','Death']:preserved[clip]=sourceDiff
assert max(preserved.values())<1e-6
# Measure the exported contact anchor after interpolation and native root travel.
contacts={};localLandmarks={}
for side,anatomy in FOOT.items():
 f=new.names[side+'Foot'];toe=new.names[side+'Toe'];fb=-new.ib[f,:3,3];tb=-new.ib[toe,:3,3]
 localLandmarks[side]={'Foot':{k:(anatomy[k]-fb).tolist() for k in ['soleCenter','heel','toePad','toeBall']},'Toe':{k:(anatomy[k]-tb).tolist() for k in ['toePad','toeBall']},'UnityFoot':{k:((anatomy[k]-fb)*[-1,1,1]).tolist() for k in ['soleCenter','heel','toePad','toeBall']},'UnityToe':{k:((anatomy[k]-tb)*[-1,1,1]).tolist() for k in ['toePad','toeBall']}}
for clip,spec in GAITS.items():
 results={}
 for side,phase in [('Right',0),('Left',.5)]:
  segments=[('heel',0,.16),('flat',.16,.73),('toe',.73,1)] if clip=='Walking' else [('toe',0,1)]
  items=[]
  for label,start,end in segments:
   # Interior samples avoid strike discontinuities and curve endpoint rounding.
   times=(phase+spec['stanceFraction']*np.linspace(start+.001,end-.001,41))*spec['period'];pos=[]
   for t in times:
    gp,gq=new.pose(clip,t%spec['period'])
    if label=='toe':v=new.marker(gp,gq,side+'Toe',localLandmarks[side]['Toe']['toePad'])
    else:v=new.marker(gp,gq,side+'Foot',localLandmarks[side]['Foot']['heel' if label=='heel' else 'soleCenter'])
    pos.append(v)
   pos=np.array(pos);vel=np.diff(pos,axis=0)/np.diff(times)[:,None];world=vel+[0,0,spec['nativeAuthorSpeed']]
   items.append({'anchor':label,'stanceFractionInterval':[start,end],'meanBackwardAuthorMetersPerSecond':float(-vel[:,2].mean()),'nativeRuntimeMetersPerSecond':float(-vel[:,2].mean()*1.65),'maxPlanarDriftAtNativeRuntimeMetersPerSecond':float(np.linalg.norm(world[:,[0,2]],axis=1).max()*1.65),'medianPlanarDriftAtNativeRuntimeMetersPerSecond':float(np.median(np.linalg.norm(world[:,[0,2]],axis=1))*1.65),'minimumAnchorRuntimeHeight':float(pos[:,1].min()*1.65),'maximumAnchorRuntimeHeight':float(pos[:,1].max()*1.65)})
  results[side]=items
 contacts[clip]=results
restgp=new.local.copy();restq=new.q.copy()
for i,p in enumerate(new.parent):
 if p>=0:restgp[i]+=restgp[p]
restSurfaceError=float(np.linalg.norm(new.skin(restgp,restq)-new.v,axis=1).max())
report={'sourceGlb':str(BASE),'derivativeGlb':str(OUT/'hero_engineer_chibi.glb'),'independentBlenderImportedSkinMatchesGltfInterpolation':True,'comparisonSamples':comparisons,'restSurfaceMaxErrorAuthorMeters':restSurfaceError,'preservedIdleAttackDeathMaxSurfaceDifferenceAuthorMeters':preserved,'sampleRate':240,'clips':clips,'boundsByClip':boundsByClip,'nativeModelAnimationEnvelope':[low.tolist(),high.tolist()],'unityModelAnimationEnvelope':[[-float(high[0]),float(low[1]),float(low[2])],[-float(low[0]),float(high[1]),float(high[2])]],'recommendedUnityBounds':{'center':[0,.50,.14],'extents':[.46,.57,.67]},'footLocalLandmarks':localLandmarks,'measuredStanceVelocities':contacts,'limitations':['Straight native-speed offline audit. Runtime blend, collision, turning and world-contact locking must be checked by integration owner.','Contact anchor can roll heel-to-toe; a fixed ankle or sole-center lock is not correct during toe roll.','Dash is an airborne/glide presentation; no planted feet at24m/s.']}
(OUT/'independent-validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k not in ['comparisonSamples','footLocalLandmarks','boundsByClip']},indent=2))
