import json,struct,math,hashlib
from pathlib import Path
OUT=Path(__file__).resolve().parent
def glb(p):
 raw=Path(p).read_bytes();n=struct.unpack_from('<I',raw,12)[0]
 j=json.loads(raw[20:20+n]);return j,raw[28+n:],raw
def acc(j,b,i):
 a=j['accessors'][i];bv=j['bufferViews'][a['bufferView']]
 code,size={5120:('b',1),5121:('B',1),5122:('h',2),5123:('H',2),5125:('I',4),5126:('f',4)}[a['componentType']]
 count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
 stride=bv.get('byteStride',count*size);off=bv.get('byteOffset',0)+a.get('byteOffset',0)
 return [struct.unpack_from('<'+code*count,b,off+k*stride) for k in range(a['count'])]
src=Path('/Volumes/T7 Shield/Assets/Enemies/Lavender Spitter 2026-10-16/mobile-006/LOD0/lavender-spitter_LOD0.glb')
s,sb,sraw=glb(src);j,b,raw=glb(OUT/'lavender-spitter-animated_LOD0.glb')
primitive=j['meshes'][0]['primitives'][0];source=s['meshes'][0]['primitives'][0]
ps=acc(s,sb,source['attributes']['POSITION']);pt=acc(j,b,primitive['attributes']['POSITION'])
nearest=max(min(math.dist(a,c) for c in ps) for a in pt)
weights=acc(j,b,primitive['attributes']['WEIGHTS_0']);indices=acc(j,b,primitive['indices'])
animations={}
for a in j['animations']:
 times=[x[0] for q in a['samplers'] for x in acc(j,b,q['input'])]
 scales=[abs(v-1) for c in a['channels'] if c['target']['path']=='scale' for row in acc(j,b,a['samplers'][c['sampler']]['output']) for v in row]
 animations[a['name']]={'startSeconds':min(times),'endSeconds':max(times),'durationSeconds':max(times)-min(times),'channels':len(a['channels']),'maxScaleDeviation':max(scales,default=0),'interpolations':sorted(set(s['interpolation'] for s in a['samplers']))}
imgs=[]
for im in j['images']:
 bv=j['bufferViews'][im['bufferView']];r=b[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']]
 imgs.append({'name':im.get('name'),'mimeType':im.get('mimeType'),'bytes':len(r),'sha256':hashlib.sha256(r).hexdigest()})
report={'file':str(OUT/'lavender-spitter-animated_LOD0.glb'),'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'sourceSha256':hashlib.sha256(sraw).hexdigest(),'materialCount':len(j['materials']),'primitiveCount':sum(len(m['primitives']) for m in j['meshes']),'triangles':len(indices)//3,'verticesIncludingUvSplits':len(pt),'maximumRestPositionNearestSourceErrorMeters':nearest,'skinCount':len(j['skins']),'skinJointCount':len(j['skins'][0]['joints']),'allJointsNamed':all('name'in j['nodes'][i] for i in j['skins'][0]['joints']),'muzzleAnchorPresent':any(n.get('name')=='Muzzle' for n in j['nodes']),'maximumInfluences':max(sum(x>.00001 for x in w) for w in weights),'unweightedVertices':sum(sum(w)<.999 for w in weights),'maximumWeightNormalizationError':max(abs(sum(w)-1) for w in weights),'allNumericDataFinite':all(math.isfinite(c) for row in pt+weights for c in row),'animations':animations,'embeddedImages':imgs,'noExternalUris':all('uri'not in x for x in j.get('images',[])+j.get('buffers',[]))}
assert report['triangles']==4000 and report['materialCount']==1 and report['primitiveCount']==1
assert report['maximumRestPositionNearestSourceErrorMeters']<.000001
assert set(animations)=={'Idle','Walk','Spit'}
assert report['muzzleAnchorPresent'] and report['skinJointCount']==15
assert report['maximumInfluences']<=4 and report['unweightedVertices']==0
assert all(a['maxScaleDeviation']<.000001 for a in animations.values())
(OUT/'glb-audit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
