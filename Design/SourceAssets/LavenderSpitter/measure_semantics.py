import sys,io,json
from pathlib import Path
from PIL import Image
OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(OUT))
from audit_glb import glb,acc
j,b,_=glb('/Volumes/T7 Shield/Assets/Enemies/Lavender Spitter 2026-10-16/mobile-006/LOD0/lavender-spitter_LOD0.glb')
pr=j['meshes'][0]['primitives'][0]
positions=acc(j,b,pr['attributes']['POSITION']);uvs=acc(j,b,pr['attributes']['TEXCOORD_0'])
m=j['materials'][pr.get('material',0)];ti=m['pbrMetallicRoughness']['baseColorTexture']['index']
im=j['images'][j['textures'][ti]['source']];bv=j['bufferViews'][im['bufferView']]
r=b[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']];tex=Image.open(io.BytesIO(r)).convert('RGB')
def ss(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
out=[]
for xyz,(u,v) in zip(positions,uvs):
 x=min(tex.width-1,max(0,int(u*tex.width)));y=min(tex.height-1,max(0,int(v*tex.height)))
 r,g,b=tex.getpixel((x,y));cyan=ss(1.05,1.5,g/max(r,1)) if b>r*1.15 else 0.
 out.append({'positionBlender':[xyz[0],-xyz[2],xyz[1]],'cyanBody':cyan})
(OUT/'semantic-colors.json').write_text(json.dumps(out))
print('Measured cyan body mask vertices',sum(q['cyanBody']>.5 for q in out),'of',len(out))
