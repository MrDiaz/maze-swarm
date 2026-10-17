import json,sys,io,contextlib
from pathlib import Path
import numpy as np
from PIL import Image
OUT=Path(__file__).resolve().parent;BASE=OUT.parent/'lavender-spitter-animated'
sys.path.insert(0,str(BASE))
with contextlib.redirect_stdout(io.StringIO()):from audit_glb import glb
j,b,_=glb(BASE/'lavender-spitter-animated_LOD0.glb')
im=j['images'][j['textures'][j['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']]['source']];bv=j['bufferViews'][im['bufferView']]
src=np.array(Image.open(io.BytesIO(b[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']])).convert('RGB')).astype(float)/255
PALETTES={'grunt':{'body':[143,128,199],'belly':[238,221,185],'eye':[49,49,75],'accent':[228,157,123]},'runner':{'body':[164,115,175],'belly':[246,185,155],'eye':[49,49,75],'accent':[232,130,125]},'carrier':{'body':[153,124,194],'belly':[123,204,205],'eye':[49,49,75],'accent':[238,219,177]},'carapace':{'body':[90,102,142],'belly':[220,206,174],'eye':[37,42,65],'accent':[245,226,185]},'tick':{'body':[168,144,203],'belly':[239,184,133],'eye':[49,49,75],'accent':[226,143,90]}}
r,g,bl=src[:,:,0],src[:,:,1],src[:,:,2]
cyan=np.clip((g/np.maximum(r,.001)-1.05)/.45,0,1);cyan=cyan*cyan*(3-2*cyan)
eye=(np.maximum(np.maximum(r,g),bl)<.62)&(g>.16)&(r>.12)
for role,p in PALETTES.items():
 body=np.array(p['body']);belly=np.array(p['belly']);dark=np.array(p['eye'])
 out=body[None,None,:]*(1-cyan[:,:,None])+belly[None,None,:]*cyan[:,:,None]
 out[eye]=dark
 source=Image.fromarray(np.uint8(np.clip(out,0,255))).resize((1024,992),Image.Resampling.LANCZOS)
 atlas=Image.new('RGB',(1024,1024),tuple(p['body']));atlas.paste(source,(0,0))
 for i,k in enumerate(['body','belly','eye','accent']):atlas.paste(tuple(p[k]),(i*256,992,(i+1)*256,1024))
 d=OUT/role;d.mkdir(exist_ok=True);atlas.save(d/(role+'-albedo.png'))
(OUT/'palettes.json').write_text(json.dumps(PALETTES,indent=2))
print('Prepared',len(PALETTES),'source-derived flat-color atlases; source maps unchanged.')
