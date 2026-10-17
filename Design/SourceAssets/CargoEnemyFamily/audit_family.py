"""Exported-GLB math checks, independently of authoring application."""
from pathlib import Path
import json,hashlib
import numpy as np
from gltf_cpu import Asset
OUT=Path(__file__).resolve().parent;reports=[]
for role in ['grunt','runner','carrier','carapace','tick']:
 folder=OUT/role;info=json.loads((folder/'asset.json').read_text());a=Asset(info['asset']);j=a.j
 schedule=json.loads((folder/'contact-schedule.json').read_text());plans={(q['clip'],round(q['time']*60),q['side']):q for q in schedule}
 groups={}
 for i,p in enumerate(a.pos):groups.setdefault(tuple(np.round(p,6)),[]).append(i)
 twins=[g for g in groups.values() if len(g)>1]
 edges=np.concatenate([a.indices[:,[0,1]],a.indices[:,[1,2]],a.indices[:,[2,0]]]);lengths=np.linalg.norm(a.pos[edges[:,0]]-a.pos[edges[:,1]],axis=1);valid=lengths>1e-8;edges=edges[valid];lengths=lengths[valid]
 sole={s:np.where((a.pos[:,1]<.012)&((a.pos[:,0]>0) if s=='L' else(a.pos[:,0]<0)))[0] for s in ('L','R')}
 r={'role':role,'method':'Independent exported glTF TRS + inverse-bind matrix CPU evaluation, all authored 60Hz samples','sha256':hashlib.sha256(a.raw).hexdigest(),'triangles':len(a.indices),'skinJoints':len(a.skin['joints']),'oneMaterialPrimitive':len(j['materials'])==1 and len(j['meshes'])==1 and len(j['meshes'][0]['primitives'])==1,'embeddedTexturesOnly':all('uri'not in n for n in j.get('images',[])+j.get('buffers',[])),'finite':True,'maximumInfluences':int((a.weights>1e-6).sum(axis=1).max()),'weightSumError':float(abs(a.weights.sum(axis=1)-1).max()),'unitNormalError':float(abs(np.linalg.norm(a.normal,axis=1)-1).max()),'uvRange':[a.uv.min(axis=0).tolist(),a.uv.max(axis=0).tolist()],'sampleCount':0,'maximumSeamCrackMeters':0.,'minimumMeshHeightMeters':1e9,'maximumFootTargetErrorMeters':0.,'maximumPlantedSoleHeightDeviationMeters':0.,'maximumStanceSlipAtNativeSpeedMeters':0.,'maximumEdgeStretchRatio':0.,'minimumEdgeRatio':1.,'loopClosureMeters':{},'scaledBones':{},'animations':{}}
 for clip,dur in info['motion']['clips'].items():
  first=None;stanceRefs={};channels=a.anims[clip];times=[float(t) for c in channels for t in c[2]];r['animations'][clip]={'durationSeconds':max(times)-min(times),'channels':len(channels),'interpolation':sorted(set(c[4] for c in channels))}
  for frame in range(round(dur*60)+1):
   t=frame/60;v,world,values=a.evaluate(clip,t)
   if first is None:first=v.copy()
   r['sampleCount']+=1;r['finite'] &= bool(np.isfinite(v).all());r['minimumMeshHeightMeters']=min(r['minimumMeshHeightMeters'],float(v[:,1].min()));r['maximumSeamCrackMeters']=max(r['maximumSeamCrackMeters'],max((float(np.linalg.norm(v[g]-v[g[0]],axis=1).max()) for g in twins),default=0.))
   ratios=np.linalg.norm(v[edges[:,0]]-v[edges[:,1]],axis=1)/lengths;r['maximumEdgeStretchRatio']=max(r['maximumEdgeStretchRatio'],float(ratios.max()));r['minimumEdgeRatio']=min(r['minimumEdgeRatio'],float(ratios.min()))
   for (node,path),val in values.items():
    if path=='scale' and abs(val-1).max()>1e-5:r['scaledBones'][a.nodes[node]['name']]=max(r['scaledBones'].get(a.nodes[node]['name'],0),float(abs(val-1).max()))
   for side in ('L','R'):
    plan=plans[clip,frame,side];p=plan['targetBlender'];expected=np.array([p[0],p[2],-p[1]]);actual=world(a.names['Foot.'+side])[:3,3]
    r['maximumFootTargetErrorMeters']=max(r['maximumFootTargetErrorMeters'],float(np.linalg.norm(actual-expected)))
    if plan['contact']:
     r['maximumPlantedSoleHeightDeviationMeters']=max(r['maximumPlantedSoleHeightDeviationMeters'],float(abs(v[sole[side],1]-a.pos[sole[side],1]).max()))
     if clip=='Walk':
      q=actual+np.array([0,0,info['motion']['nativeGroundSpeedMetersPerSecond']*t])
      if side not in stanceRefs:stanceRefs[side]=q
      r['maximumStanceSlipAtNativeSpeedMeters']=max(r['maximumStanceSlipAtNativeSpeedMeters'],float(np.linalg.norm(q-stanceRefs[side])))
    elif side in stanceRefs:del stanceRefs[side]
  if clip in ('Idle','Walk'):r['loopClosureMeters'][clip]=float(np.linalg.norm(first-v,axis=1).max())
 r['passed']=all([r['oneMaterialPrimitive'],r['embeddedTexturesOnly'],r['finite'],r['maximumInfluences']<=4,r['weightSumError']<1e-5,r['unitNormalError']<1e-4,r['maximumSeamCrackMeters']<1e-5,r['minimumMeshHeightMeters']>-.0001,r['maximumFootTargetErrorMeters']<.0001,r['maximumPlantedSoleHeightDeviationMeters']<.0001,r['maximumStanceSlipAtNativeSpeedMeters']<.0001,max(r['loopClosureMeters'].values())<1e-5,set(r['scaledBones'])<=set(info['motion']['localScaleBones'])])
 (folder/'independent-audit.json').write_text(json.dumps(r,indent=2));reports.append(r);print('AUDIT',role,r['passed'],'foot',r['maximumFootTargetErrorMeters'],'floor',r['minimumMeshHeightMeters'],'seam',r['maximumSeamCrackMeters'],'scale',r['scaledBones'],flush=True)
 unchanged=hashlib.sha256((OUT.parent/'lavender-spitter-animated/lavender-spitter-animated_LOD0.glb').read_bytes()).hexdigest()=='7bc01d38c339ad7d7e5cf4f7e67c19e2b6ccfb3d18533f015af5e3935c60a814'
 (OUT/'INDEPENDENT-AUDIT.json').write_text(json.dumps({'allPassed':all(q['passed'] for q in reports),'sourceSpitterHashUnchanged':unchanged,'assets':reports},indent=2))
assert all(q['passed'] for q in reports) and unchanged
