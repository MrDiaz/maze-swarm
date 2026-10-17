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
