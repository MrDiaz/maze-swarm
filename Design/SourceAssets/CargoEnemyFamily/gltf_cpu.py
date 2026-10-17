"""Independent glTF reader and CPU skin evaluator. No application imports."""
import json,struct,math,io
from pathlib import Path
import numpy as np
from PIL import Image
def glb(path):
 raw=Path(path).read_bytes();n=struct.unpack_from('<I',raw,12)[0]
 return json.loads(raw[20:20+n]),raw[28+n:],raw
def acc(j,b,i):
 a=j['accessors'][i];bv=j['bufferViews'][a['bufferView']]
 code,size={5120:('b',1),5121:('B',1),5122:('h',2),5123:('H',2),5125:('I',4),5126:('f',4)}[a['componentType']]
 n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];stride=bv.get('byteStride',n*size);off=bv.get('byteOffset',0)+a.get('byteOffset',0)
 return np.array([struct.unpack_from('<'+code*n,b,off+k*stride) for k in range(a['count'])])
def quat(q):
 x,y,z,w=q;d=math.sqrt(x*x+y*y+z*z+w*w);x/=d;y/=d;z/=d;w/=d
 return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0],[0,0,0,1]])
class Asset:
 def __init__(self,path):
  self.j,self.b,self.raw=glb(path);j=self.j;b=self.b;self.nodes=j['nodes'];p=j['meshes'][0]['primitives'][0]
  self.pos=acc(j,b,p['attributes']['POSITION']);self.normal=acc(j,b,p['attributes']['NORMAL']);self.uv=acc(j,b,p['attributes']['TEXCOORD_0']);self.ids=acc(j,b,p['attributes']['JOINTS_0']).astype(int);self.weights=acc(j,b,p['attributes']['WEIGHTS_0']);self.indices=acc(j,b,p['indices']).astype(int).reshape(-1,3)
  self.skin=j['skins'][0];self.inv=acc(j,b,self.skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);self.hom=np.column_stack([self.pos,np.ones(len(self.pos))]);self.parents={c:i for i,n in enumerate(self.nodes) for c in n.get('children',[])};self.names={n.get('name'):i for i,n in enumerate(self.nodes)}
  self.anims={}
  for a in j['animations']:
   self.anims[a['name']]=[(c['target']['node'],c['target']['path'],acc(j,b,a['samplers'][c['sampler']]['input']).flatten(),acc(j,b,a['samplers'][c['sampler']]['output']),a['samplers'][c['sampler']]['interpolation']) for c in a['channels']]
  im=j['images'][0];v=j['bufferViews'][im['bufferView']];self.texture=np.array(Image.open(io.BytesIO(b[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert('RGB'))
 def evaluate(self,name,t):
  values={}
  for n,path,ts,vs,interp in self.anims[name]:
   k=int(np.argmin(np.abs(ts-t)))
   if abs(ts[k]-t)<.000001:val=vs[k]
   else:
    k=max(0,min(len(ts)-2,int(np.searchsorted(ts,t)-1)));u=(t-ts[k])/(ts[k+1]-ts[k]);q0=vs[k];q1=vs[k+1]
    if path=='rotation' and np.dot(q0,q1)<0:q1=-q1
    val=q0 if interp=='STEP' else q0*(1-u)+q1*u
   values[(n,path)]=val
  local=[]
  for i,n in enumerate(self.nodes):
   if 'matrix'in n:local.append(np.array(n['matrix']).reshape(4,4).T);continue
   T=np.identity(4);T[:3,3]=values.get((i,'translation'),n.get('translation',[0,0,0]));R=quat(values.get((i,'rotation'),n.get('rotation',[0,0,0,1])));S=np.diag([*values.get((i,'scale'),n.get('scale',[1,1,1])),1]);local.append(T@R@S)
  worlds={}
  def world(i):
   if i not in worlds:worlds[i]=world(self.parents[i])@local[i] if i in self.parents else local[i]
   return worlds[i]
  mats=np.array([world(i)@self.inv[k] for k,i in enumerate(self.skin['joints'])]);posed=np.zeros((len(self.pos),4))
  for k in range(4):posed+=np.einsum('nij,nj->ni',mats[self.ids[:,k]],self.hom)*self.weights[:,k,None]
  return posed[:,:3],world,values
