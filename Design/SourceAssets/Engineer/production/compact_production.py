"""Compact approved appearance002 without changing any used binary payload.

No Blender, rendering, mesh operation, texture re-encoding or inference.
"""
from pathlib import Path
import copy,datetime,hashlib,json,struct,shutil

ROOT=Path('/Volumes/T7 Shield/Assets/Heroes/Purple Engineer 2026-10-16')
SOURCE=ROOT/'appearance-002/hero_engineer_clean_palette_comparison.glb'
GAIT=ROOT/'gait-011/hero_engineer_chibi.glb'
PAINT=ROOT/'appearance-002/engineer-clean-palette-2k.png'
OUT=ROOT/'production-001'
def sha(data):return hashlib.sha256(data).hexdigest()
def load(path):
 raw=path.read_bytes();magic,version,total=struct.unpack_from('<III',raw);assert magic==0x46546c67 and version==2 and total==len(raw);n,kind=struct.unpack_from('<II',raw,12);assert kind==0x4e4f534a;j=json.loads(raw[20:20+n]);nb,kb=struct.unpack_from('<II',raw,20+n);assert kb==0x004e4942;return j,raw[28+n:28+n+nb]
def view_bytes(j,b,index):
 v=j['bufferViews'][index];assert v['buffer']==0;off=v.get('byteOffset',0);return b[off:off+v['byteLength']]
def acc_bytes(j,b,index):
 a=j['accessors'][index];assert 'sparse' not in a;v=j['bufferViews'][a['bufferView']];assert 'byteStride' not in v;dim={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];size={5121:1,5123:2,5125:4,5126:4}[a['componentType']];payload=view_bytes(j,b,a['bufferView']);off=a.get('byteOffset',0);return payload[off:off+a['count']*dim*size]
def material_textures(obj):
 refs=[]
 if isinstance(obj,dict):
  for key,value in obj.items():
   if key.lower().endswith('texture') and isinstance(value,dict) and 'index' in value:refs.append(value['index'])
   else:refs.extend(material_textures(value))
 elif isinstance(obj,list):
  for v in obj:refs.extend(material_textures(v))
 return refs
def remap_material(obj,mapping):
 if isinstance(obj,dict):
  for key,value in obj.items():
   if key.lower().endswith('texture') and isinstance(value,dict) and 'index' in value:value['index']=mapping[value['index']]
   else:remap_material(value,mapping)
 elif isinstance(obj,list):
  for value in obj:remap_material(value,mapping)
def used_accessors(j):
 used=set()
 for m in j['meshes']:
  for p in m['primitives']:
   used.update(p['attributes'].values())
   if 'indices' in p:used.add(p['indices'])
   for target in p.get('targets',[]):used.update(target.values())
 for s in j['skins']:used.add(s['inverseBindMatrices'])
 for a in j['animations']:
  for s in a['samplers']:used.update([s['input'],s['output']])
 return used
def normalized_accessor(a):return {k:v for k,v in a.items() if k!='bufferView'}

def main():
 assert not OUT.exists(),'Preserve production outputs; choose a new version.'
 expectedSource='d653625a1e7f4f1359bc40fd88c1dde60725be55a20ba0909ac89d8c96f205a1';expectedGait='3904d2d8dae2e1ba9da400274103df480f04fd26a9826640251103c9da5abc5d'
 assert sha(SOURCE.read_bytes())==expectedSource and sha(GAIT.read_bytes())==expectedGait
 archives=[ROOT/'gait-011/engineer-gait011-source-and-review.zip',ROOT/'appearance-002/engineer-appearance002-source-and-review.zip'];archiveHashes={str(p):sha(p.read_bytes()) for p in archives}
 original,b=load(SOURCE);gait,gb=load(GAIT);assert not original.get('extensionsRequired') and not original.get('extensionsUsed');j=copy.deepcopy(original)
 usedMaterials=sorted({p['material'] for m in j['meshes'] for p in m['primitives'] if 'material' in p});mm={v:i for i,v in enumerate(usedMaterials)};j['materials']=[j['materials'][i] for i in usedMaterials]
 for m in j['meshes']:
  for p in m['primitives']:
   if 'material' in p:p['material']=mm[p['material']]
 usedTextures=sorted(set(material_textures(j['materials'])));tm={v:i for i,v in enumerate(usedTextures)};j['textures']=[j['textures'][i] for i in usedTextures];remap_material(j['materials'],tm)
 assert all(not t.get('extensions') for t in j['textures'])
 usedImages=sorted({t['source'] for t in j['textures']});im={v:i for i,v in enumerate(usedImages)};j['images']=[j['images'][i] for i in usedImages]
 usedSamplers=sorted({t['sampler'] for t in j['textures'] if 'sampler' in t});sm={v:i for i,v in enumerate(usedSamplers)};j['samplers']=[j['samplers'][i] for i in usedSamplers]
 for t in j['textures']:
  t['source']=im[t['source']]
  if 'sampler' in t:t['sampler']=sm[t['sampler']]
 used=sorted(used_accessors(j));am={v:i for i,v in enumerate(used)};j['accessors']=[j['accessors'][i] for i in used]
 for m in j['meshes']:
  for p in m['primitives']:
   p['attributes']={k:am[v] for k,v in p['attributes'].items()}
   if 'indices' in p:p['indices']=am[p['indices']]
   for target in p.get('targets',[]):
    for key in target:target[key]=am[target[key]]
 for s in j['skins']:s['inverseBindMatrices']=am[s['inverseBindMatrices']]
 for a in j['animations']:
  for s in a['samplers']:s['input']=am[s['input']];s['output']=am[s['output']]
 usedViews=sorted({a['bufferView'] for a in j['accessors']}|{i['bufferView'] for i in j['images']});vm={v:i for i,v in enumerate(usedViews)};views=[];binary=bytearray();viewProof=[]
 for index in usedViews:
  v=copy.deepcopy(original['bufferViews'][index]);payload=view_bytes(original,b,index);binary.extend(b'\0'*((-len(binary))%4));v['byteOffset']=len(binary);binary.extend(payload);views.append(v);viewProof.append({'sourceView':index,'productionView':vm[index],'bytes':len(payload),'sha256':sha(payload)})
 j['bufferViews']=views
 for a in j['accessors']:a['bufferView']=vm[a['bufferView']]
 for image in j['images']:image['bufferView']=vm[image['bufferView']]
 j['buffers']=[{'byteLength':len(binary)}];j.setdefault('extras',{}).update(productionVariant='001 / selected appearance002 + exact gait011 / compact',appearanceDerivative='002 selected by canonical owner after actual Unity three-way comparison',sourceComparisonSha256=expectedSource,sourceGait011Sha256=expectedGait,packingOnlyNoRendering=True)
 OUT.mkdir();target=OUT/'hero_engineer_chibi.glb';js=json.dumps(j,separators=(',',':')).encode();js+=b' '*((-len(js))%4);binary.extend(b'\0'*((-len(binary))%4));raw=struct.pack('<III',0x46546c67,2,28+len(js)+len(binary))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(binary),0x004e4942)+binary;target.write_bytes(raw);final,fb=load(target)
 assert used_accessors(final)==set(range(len(final['accessors'])))
 assert {a['bufferView'] for a in final['accessors']}|{i['bufferView'] for i in final['images']}==set(range(len(final['bufferViews'])))
 assert set(material_textures(final['materials']))==set(range(len(final['textures'])))
 assert {t['source'] for t in final['textures']}==set(range(len(final['images'])))
 assert {t['sampler'] for t in final['textures']}==set(range(len(final['samplers'])))
 accessorProof=[]
 for old,new in am.items():
  before=acc_bytes(original,b,old);after=acc_bytes(final,fb,new);assert before==after and normalized_accessor(original['accessors'][old])==normalized_accessor(final['accessors'][new]);accessorProof.append({'comparisonAccessor':old,'productionAccessor':new,'bytes':len(after),'sha256':sha(after)})
 assert original['nodes']==gait['nodes']==final['nodes'] and original['scenes']==gait['scenes']==final['scenes']
 gaitProof=[]
 def prove(role,old,new):
  before=acc_bytes(gait,gb,old);after=acc_bytes(final,fb,new);assert before==after and normalized_accessor(gait['accessors'][old])==normalized_accessor(final['accessors'][new]);gaitProof.append({'role':role,'gait011Accessor':old,'productionAccessor':new,'bytes':len(after),'sha256':sha(after)})
 for mi,(gm,fm) in enumerate(zip(gait['meshes'],final['meshes'])):
  for pi,(gp,fp) in enumerate(zip(gm['primitives'],fm['primitives'])):
   assert gp['attributes'].keys()==fp['attributes'].keys()
   for key in gp['attributes']:prove(f'mesh{mi}/primitive{pi}/{key}',gp['attributes'][key],fp['attributes'][key])
   prove(f'mesh{mi}/primitive{pi}/indices',gp['indices'],fp['indices'])
 for si,(gs,fs) in enumerate(zip(gait['skins'],final['skins'])):
  assert {k:v for k,v in gs.items() if k!='inverseBindMatrices'}=={k:v for k,v in fs.items() if k!='inverseBindMatrices'};prove(f'skin{si}/inverseBindMatrices',gs['inverseBindMatrices'],fs['inverseBindMatrices'])
 clipProof=[]
 for ga,fa in zip(gait['animations'],final['animations']):
  assert {k:v for k,v in ga.items() if k!='samplers'}=={k:v for k,v in fa.items() if k!='samplers'} and len(ga['samplers'])==len(fa['samplers'])
  for si,(gs,fs) in enumerate(zip(ga['samplers'],fa['samplers'])):
   assert {k:v for k,v in gs.items() if k not in ['input','output']}=={k:v for k,v in fs.items() if k not in ['input','output']}
   for key in ['input','output']:prove(f"clip/{ga['name']}/sampler{si}/{key}",gs[key],fs[key])
  clipProof.append({'name':ga['name'],'channels':len(ga['channels']),'samplerCount':len(ga['samplers']),'allInputOutputPayloadsExact':True})
 assert [a['name'] for a in final['animations']]==['Idle','Walking','Jogging','Running','Attack','Death','Dash']
 images=[]
 for old,new in im.items():
  originalBytes=view_bytes(original,b,original['images'][old]['bufferView']);finalBytes=view_bytes(final,fb,final['images'][new]['bufferView']);assert originalBytes==finalBytes;images.append({'comparisonImage':old,'productionImage':new,'name':final['images'][new].get('name'),'bytes':len(finalBytes),'sha256':sha(finalBytes)})
 tex=final['textures'][final['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']];paint=view_bytes(final,fb,final['images'][tex['source']]['bufferView']);assert paint==PAINT.read_bytes();pbr=final['materials'][0]['pbrMetallicRoughness'];assert pbr['roughnessFactor']==.85 and pbr['metallicFactor']==0
 for row in viewProof:assert sha(view_bytes(final,fb,row['productionView']))==row['sha256']
 removedViews=[{'sourceView':i,'bytes':v['byteLength'],'sha256':sha(view_bytes(original,b,i))} for i,v in enumerate(original['bufferViews']) if i not in vm]
 for row in removedViews:assert row['sha256'] not in {v['sha256'] for v in viewProof}
 for p in archives:assert sha(p.read_bytes())==archiveHashes[str(p)]
 assert sha(SOURCE.read_bytes())==expectedSource and sha(GAIT.read_bytes())==expectedGait
 proof={'status':'PASS — packing only, every retained binary payload exact','productionSha256':sha(raw),'comparisonSha256':expectedSource,'gait011Sha256':expectedGait,'all197ComparisonAccessorsPreserved':len(accessorProof)==197,'accessorMap':accessorProof,'gait011RoleMap':gaitProof,'clipProof':clipProof,'nodeCount':len(final['nodes']),'nodesAndScenesJsonExact':True,'skinJointsAndMetadataExact':True,'viewMap':viewProof,'selectedAlbedoSha256':sha(paint),'retainedImages':images,'removedViews':removedViews,'removedTextureIndices':sorted(set(range(len(original['textures'])))-set(usedTextures)),'removedImageIndices':sorted(set(range(len(original['images'])))-set(usedImages)),'removedMaterialIndices':sorted(set(range(len(original['materials'])))-set(usedMaterials)),'noOrphanAccessorsViewsTexturesImagesSamplers':True,'sourceArchivesUnchanged':archiveHashes}
 (OUT/'COMPACT-BYTE-PROOF.json').write_text(json.dumps(proof,indent=2)+'\n')
 manifest=json.loads((ROOT/'gait-011/INTEGRATION.json').read_text());manifest.update(status='Production packaging selected by canonical integration owner: appearance002 clean palette + Unity smoothness.15; final resource/regression pending owner.',generatedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),selectedGlb=str(target),sha256=sha(raw),bytes=len(raw),sourceGait011Sha256=expectedGait,sourceComparisonSha256=expectedSource,meshUvNormalsIndicesImagesPreservedByteExact=False,meshUvNormalsIndicesSkinNodesSevenClipPayloadsPreservedByteExact=True,appearance={'selection':'appearance002 exact embedded2048x2048 albedo; selected Unity smoothness.15','baseColorPng':str(PAINT),'baseColorSha256':sha(paint),'gltfRoughness':.85,'gltfMetallic':0,'scope':'Engineer body only; shared gun and Heavy materials unchanged by this asset packaging.','referenceLibraryId':'libfile_f6b8a179527c81919ca35ed467433d18','comparisonSource':str(SOURCE),'comparisonLibraryId':'libfile_13dc2999c04c81919b5d6de20c117859'},packing={'inputBytes':SOURCE.stat().st_size,'productionBytes':len(raw),'removedBytes':SOURCE.stat().st_size-len(raw),'reductionPercent':(1-len(raw)/SOURCE.stat().st_size)*100,'materials':len(final['materials']),'textures':len(final['textures']),'images':len(final['images']),'accessors':len(final['accessors']),'bufferViews':len(final['bufferViews']),'removedOrphanViews':removedViews,'removedUnusedImages':proof['removedImageIndices'],'removedUnusedTextures':proof['removedTextureIndices'],'byteProof':str(OUT/'COMPACT-BYTE-PROOF.json'),'reencodedImages':False,'rendered':False,'inference':False},contactContract={'unchangedFromGait011':True,'contactManifest':str(ROOT/'gait-011/CONTACT-MANIFEST.json'),'contactManifestLibraryId':'libfile_51a72510d0208191a0ee7d18d4a5424f','manifestOriginalModelSha256':expectedGait,'appliesToProductionByExactNodeSkinClipPayloadProof':True},canonicalOwner='01a13737-9ca8-728a-9baa-fc7ac2c030ee',noCanonicalWrites=True,sharedLeaseClaimed=False)
 for key,value in list(manifest['reviews'].items()):
  if isinstance(value,str) and value.startswith('review/'):manifest['reviews'][key]=str(ROOT/'gait-011'/value)
 manifest['clipNames']=[a['name'] for a in final['animations']]
 manifest['byteProofSummary']={'allComparisonAccessorsExact':len(accessorProof),'gait011SemanticRoleComparisons':len(gaitProof),'nodeJsonEntriesExact':len(final['nodes']),'skinJointCount':len(final['skins'][0]['joints']),'allSevenClipSamplerInputsOutputsExact':True,'selectedAlbedoBytesExact':True,'sourceArchivesByteIntact':True}
 manifest['reviews']['productionNote']='No new render required: selected appearance image/material values unchanged; exact geometry/skin/node/clip data proven. Owner selected appearance002 after actual Unity A/B and will validate final imported resource.'
 manifest['limitations']=[s for s in manifest['limitations'] if 'separate appearance comparison pending' not in s]
 (OUT/'PRODUCTION-INTEGRATION.json').write_text(json.dumps(manifest,indent=2)+'\n');shutil.copy2(Path(__file__),OUT/'compact_production.py')
 (OUT/'README-PRODUCTION.txt').write_text('Engineer production001\n\nGLB SHA256 '+sha(raw)+'\n\nExact appearance002 clean-palette albedo and roughness.85/metallic0. Canonical owner selects Unity smoothness.15 for Engineer body only.\nGait011 geometry, UVs, normals, tangents, indices, weights, bind matrices, all27node JSON entries and all7clip input/output payloads preserved exactly. No render, inference or pose change.\nOrphan original albedo buffer payload and unused ORM texture/image removed; referenced flat normal retained unchanged.\nSee COMPACT-BYTE-PROOF.json for complete source/production accessor and image hashes. Existing gait011 contact contract remains applicable through exact binary proof.\nSource appearance002, source gait011 and both original source/review archives remain byte-intact. Final canonical import/regression belongs to owner.\n')
 print(json.dumps({'glb':str(target),'sha256':sha(raw),'bytes':len(raw),'comparisonBytes':SOURCE.stat().st_size,'removedBytes':SOURCE.stat().st_size-len(raw),'accessors':len(accessorProof),'joints':len(final['skins'][0]['joints']),'clips':[a['name'] for a in final['animations']],'retainedImages':images,'removedViews':removedViews,'manifest':str(OUT/'PRODUCTION-INTEGRATION.json'),'proof':str(OUT/'COMPACT-BYTE-PROOF.json')},indent=2))
if __name__=='__main__':main()
