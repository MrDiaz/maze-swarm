"""Change only staged JOINTS_0/WEIGHTS_0 bytes; preserve all geometry, clips and textures."""
import sys,json,struct,hashlib,shutil
from pathlib import Path
OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(OUT))
from audit_glb import glb,acc
from skin_weights import compute_weights
p=OUT/'lavender-spitter-animated_LOD0.glb';j,b,raw=glb(p)
pr=j['meshes'][0]['primitives'][0]
positions=acc(j,b,pr['attributes']['POSITION'])
positions=[(q[0],-q[2],q[1]) for q in positions]
ix=[x[0] for x in acc(j,b,pr['indices'])]
edges=set()
for n in range(0,len(ix),3):
 a,c,d=ix[n:n+3]
 for x,y in [(a,c),(c,d),(d,a)]:edges.add(tuple(sorted([x,y])))
weights=compute_weights(positions,edges,json.loads((OUT/'semantic-colors.json').read_text()))
jointmap={j['nodes'][n]['name']:i for i,n in enumerate(j['skins'][0]['joints'])}
buf=bytearray(b);ranges=[]
for name,ctype,components in [('JOINTS_0',None,4),('WEIGHTS_0',5126,4)]:
 a=j['accessors'][pr['attributes'][name]];bv=j['bufferViews'][a['bufferView']]
 code,size={5121:('B',1),5123:('H',2),5126:('f',4)}[a['componentType']]
 stride=bv.get('byteStride',components*size);base=bv.get('byteOffset',0)+a.get('byteOffset',0)
 for i,w in enumerate(weights):
  vals=[jointmap[n] for n in w] if name=='JOINTS_0' else list(w.values())
  vals=vals+[0]*(4-len(vals));off=base+i*stride
  struct.pack_into('<'+code*components,buf,off,*vals);ranges.append((off,off+size*components))
new=raw[:-len(b)]+buf
archive=OUT/'review'/'rejected-draft-002';archive.mkdir(exist_ok=True)
if not (archive/p.name).exists():shutil.copy2(p,archive/p.name)
report={'beforeSha256':hashlib.sha256(raw).hexdigest(),'afterSha256':hashlib.sha256(new).hexdigest(),'changedBytes':sum(a!=c for a,c in zip(raw,new)),'fileBytesUnchanged':len(new)==len(raw),'onlySkinIndicesAndWeightsChanged':all(any(a<=i<b for a,b in ranges) for i,(x,y) in enumerate(zip(b,buf)) if x!=y),'geometryAnimationsAndTexturesByteUnchanged':True,'weldedWeightGroups':len({tuple(round(c,6) for c in q) for q in positions}),'vertices':len(weights)}
assert report['onlySkinIndicesAndWeightsChanged'];p.write_bytes(new)
(OUT/'refined-skin-weights.json').write_text(json.dumps(weights))
(OUT/'weight-refinement.json').write_text(json.dumps(report,indent=2));print('WEIGHT_REFINEMENT',json.dumps(report))
