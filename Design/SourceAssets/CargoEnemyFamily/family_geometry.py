"""CPU-only, source-derived closed-mouth mesh authoring. No rendering or canonical writes."""
import bpy,bmesh,math,json,statistics
from mathutils import Vector
def ss(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
def warp(q,role):
 x,y,z=q;x0=x;y0=y;z0=z
 h=ss(.43,.55,z)
 if role in ('grunt','tick'):
  x*=1+.05*h;y=.04+(y-.04)*(1-.10*h);z=.60+(z-.60)*(1-.08*h)
  arm=ss(.29,.39,abs(x0))*ss(.19,.28,z0)*(1-ss(.45,.52,z0))
  x+=(1 if x>=0 else -1)*.018*arm
 elif role=='runner':
  x*=.88;y*=.90;z=z*1.12 if z<.26 else z+.0312
  y+=.018*ss(.68,.90,z0)
  foot=1-ss(.075,.13,z0);y=-.015+(y+.015)*(1+.12*foot)
 else:
  core=(1-ss(.43,.56,z))*(1-ss(.28,.40,abs(x)))
  x*=1+.12*core;y*=1+.15*core if y<0 else 1
  x*=1-.12*h;y=.04+(y-.04)*(1-.12*h);z=.54+(z-.54)*(1-.20*h)
 return Vector((x,y,z))
def build_geometry(mesh,rig,role,root):
 rgb=json.loads((root/'source-vertex-colors.json').read_text());semantic=json.loads((root.parent/'lavender-spitter-animated'/'semantic-colors.json').read_text())
 bypos={};cyan={}
 for q,c,s in zip(mesh.data.vertices,rgb,semantic):
  k=tuple(round(v,6) for v in q.co)
  pink=c[0]>.82 and .5<c[1]<.72 and c[2]>.8
  cavity=c[0]<.45 and c[1]<.18 and c[2]<.5 and q.co.y<-.1 and .35<q.co.z<.8
  bypos[k]=bypos.get(k,False) or pink or cavity;cyan[k]=max(cyan.get(k,0),s['cyanBody'])
 bm=bmesh.new();bm.from_mesh(mesh.data);deform=bm.verts.layers.deform.verify();uv=bm.loops.layers.uv.verify()
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
 selected={v for v in bm.verts if bypos.get(tuple(round(c,6) for c in v.co),False)}
 cut=[f for f in bm.faces if any(v in selected for v in f.verts)];removed=len(cut)
 bmesh.ops.delete(bm,geom=cut,context='FACES')
 todo=set(bm.verts);components=[]
 while todo:
  seed=todo.pop();seen={seed};stack=[seed]
  while stack:
   v=stack.pop()
   for e in v.link_edges:
    w=e.other_vert(v)
    if w in todo:todo.remove(w);seen.add(w);stack.append(w)
  components.append(seen)
 components.sort(key=len,reverse=True)
 for c in components[1:]:bmesh.ops.delete(bm,geom=list(c),context='VERTS')
 for it in range(8):
  bad=[v for v in bm.verts if sum(e.is_boundary for e in v.link_edges)>2]
  if not bad:break
  fs={f for v in bad for f in v.link_faces};removed+=len(fs);bmesh.ops.delete(bm,geom=list(fs),context='FACES')
 todo={e for e in bm.edges if e.is_boundary};loops=[]
 while todo:
  e=todo.pop();vs=[e.verts[0],e.verts[1]]
  while vs[-1]!=vs[0]:
   nxt=[q for q in vs[-1].link_edges if q in todo]
   assert len(nxt)==1,'Non-simple mouth patch boundary'
   e=nxt[0];todo.remove(e);vs.append(e.other_vert(vs[-1]))
  loops.append(vs[:-1])
 assert len(loops)==1,'Mouth patch must have one boundary'
 loop=loops[0];headIndex=mesh.vertex_groups['Head'].index
 # Remap the original atlas into 992 rows; the lower 32 rows are a flat palette.
 for f in bm.faces:
  for l in f.loops:l[uv].uv.y=32/1024+l[uv].uv.y*992/1024
 # Shorten and round the source fins instead of retaining a sharp insect silhouette.
 original={v:tuple(v.co) for v in bm.verts}
 for v in bm.verts:
  if v.co.z>.84:v.co.z=.84+(v.co.z-.84)*(.32 if role!='runner' else .50)
  if v.co.y>.25:v.co.y=.25+(v.co.y-.25)*.38
 smooth=[v for v in bm.verts if original[v][2]>.85 or original[v][1]>.29]
 if smooth:bmesh.ops.smooth_vert(bm,verts=smooth,factor=.28,use_axis_x=True,use_axis_y=True,use_axis_z=True)
 # Remove the old raised rim depth; close the whole opening with a convex patch.
 for v in loop:v.co.y=.5*v.co.y+.5*(-.205)
 center=Vector((sum(v.co.x for v in loop)/len(loop),-.245,sum(v.co.z for v in loop)/len(loop)))
 def patchY(p,r):return p.y*r+center.y*(1-r)-.015*(1-r*r)
 rings=[loop]
 for r in [2/3,1/3]:
  ring=[]
  for old in loop:
   p=center+(old.co-center)*r;p.y=patchY(old.co,r);nv=bm.verts.new(p)
   w=dict(old[deform]);w={g:a*r for g,a in w.items()};w[headIndex]=w.get(headIndex,0)+(1-r)
   for g,a in w.items():nv[deform][g]=a
   ring.append(nv)
  rings.append(ring)
 cv=bm.verts.new((center.x,center.y-.015,center.z));cv[deform][headIndex]=1
 newFaces=[];n=len(loop)
 for outer,inner in zip(rings,rings[1:]):
  for i in range(n):newFaces.append(bm.faces.new((outer[i],outer[(i+1)%n],inner[(i+1)%n],inner[i])))
 for i in range(n):newFaces.append(bm.faces.new((rings[-1][i],rings[-1][(i+1)%n],cv)))
 for f in newFaces:
  for l in f.loops:l[uv].uv=(.125,16/1024)
 # Match the new closed surface with a tiny closed lip tube, not a painted cavity.
 smileCenter=Vector((center.x,center.y-.021,center.z-.024));lip=[]
 for i in range(9):
  u=i/8*2-1;p=smileCenter+Vector((u*.050,0,-.006*(1-u*u)));ring=[]
  for k in range(6):
   a=k/6*math.tau;nv=bm.verts.new(p+Vector((0,.0045*math.cos(a),.0045*math.sin(a))));nv[deform][headIndex]=1;ring.append(nv)
  lip.append(ring)
 lipfaces=[]
 for a,b in zip(lip,lip[1:]):
  for k in range(6):lipfaces.append(bm.faces.new((a[k],a[(k+1)%6],b[(k+1)%6],b[k])))
 lipfaces.extend([bm.faces.new(tuple(reversed(lip[0]))),bm.faces.new(tuple(lip[-1]))])
 for f in lipfaces:
  for l in f.loops:l[uv].uv=(.625,16/1024)
 # Preserve source head/limb identity while changing actual role proportions.
 for v in bm.verts:v.co=warp(v.co,role)
 # Restrained warm crest accent on the small Tick; no procedural monster overlay.
 if role in ('tick','runner'):
  for f in bm.faces:
   if all(v.co.z>(.84 if role=='tick' else .87) for v in f.verts):
    for l in f.loops:l[uv].uv=(.875,16/1024)
 # Add carrier anatomy with named, authored skin parts and a safe non-legacy Belly contract.
 podSpecs=[]
 if role in ('carrier','carapace'):
  bellyIndex=mesh.vertex_groups['CarrierBelly'].index
  # Original vertex coordinates were retained for semantic lookup through the warp.
  for v,sourcePos in original.items():
   k=tuple(round(c,6) for c in sourcePos);c=cyan.get(k,0);a=.78*c*(1-ss(.39,.48,sourcePos[2]))
   if a>0:
    old=dict(v[deform]);v[deform].clear()
    for g,w in old.items():v[deform][g]=w*(1-a)
    v[deform][bellyIndex]=a
  for name,pos,size in [('CarrierPod.L',(.28,.235,.40),(.15,.145,.17)),('CarrierPod.R',(-.28,.235,.40),(.15,.145,.17)),('CarrierPod.Back',(0,.29,.55),(.17,.17,.19))]:
   c=warp(Vector(pos),role);ret=bmesh.ops.create_uvsphere(bm,u_segments=12,v_segments=6,radius=1)
   verts=ret['verts'];idx=mesh.vertex_groups[name].index
   for v in verts:
    v.co=Vector((v.co.x*size[0],v.co.y*size[1],v.co.z*size[2]))+c;v[deform][idx]=1
   faces={f for v in verts for f in v.link_faces}
   for f in faces:
    for l in f.loops:l[uv].uv=(.875,16/1024)
   podSpecs.append({'name':name,'centre':list(c),'radii':list(size)})
 # Exact four-weight normalization, shared inherently by welded surface vertices.
 for v in bm.verts:
  w=dict(sorted(v[deform].items(),key=lambda q:-q[1])[:4]);total=sum(w.values());v[deform].clear()
  for g,a in w.items():v[deform][g]=a/total
 bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
 for f in bm.faces:f.smooth=True
 report={'mouthSourceFacesRemoved':removed,'mouthBoundaryVertices':len(loop),'closedMouthCapTriangles':len(newFaces)*2-len(loop),'bodyTopologyBoundaryEdges':sum(e.is_boundary for e in bm.edges),'bodyTopologyNonmanifoldEdges':sum(not e.is_manifold for e in bm.edges),'triangles':len(bm.faces),'verticesWelded':len(bm.verts),'signedVolume':bm.calc_volume(signed=True),'pods':podSpecs}
 assert report['bodyTopologyBoundaryEdges']==0 and report['bodyTopologyNonmanifoldEdges']==0
 bm.to_mesh(mesh.data);bm.free();mesh.data.update()
 return report,podSpecs
