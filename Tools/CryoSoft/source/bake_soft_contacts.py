"""Pure geometry contact shading; no surface noise, normal maps or stock textures."""
import bpy, math, json, pathlib
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=pathlib.Path(__file__).resolve().parent.parent

def bake_contacts(objects,W=44,D=32,suffix=''):
 verts=[];faces=[]
 for o in objects:
  if o.data.materials[0].name.startswith('Soft_Glass') or o['category']=='marking':continue
  offset=len(verts);verts += [o.matrix_world@v.co for v in o.data.vertices]
  faces += [tuple(offset+i for i in p.vertices) for p in o.data.polygons]
 tree=BVHTree.FromPolygons(verts,faces,all_triangles=False,epsilon=.00001)
 rays=[]
 for i in range(12):
  z=(i+.5)/12;r=math.sqrt(1-z*z);a=i*2.399963229728653;rays.append(Vector((r*math.cos(a),r*math.sin(a),z)))
 def ao(p,n,reach=1.65):
  q=Vector((0,0,1)).rotation_difference(n.normalized());origin=p+n*.013;weight=0
  for ray in rays:
   hit,normal,index,d=tree.ray_cast(origin,q@ray,reach)
   if hit is not None:weight+=(1-d/reach)
  return max(.16,1-weight/len(rays))
 for o in objects:
  attr=o.data.color_attributes.new(name='SoftContact',type='FLOAT_COLOR',domain='POINT');mat=o.data.materials[0].name
  if o['category']=='floor' or o['category']=='marking' or mat.startswith('Soft_Glass'):
   for v in attr.data:v.color=(1,1,1,1)
  else:
   normalMat=o.matrix_world.to_3x3().inverted().transposed()
   for v,c in zip(o.data.vertices,attr.data):
    value=ao(o.matrix_world@v.co,(normalMat@v.normal).normalized(),.9);c.color=(value,value,value,1)
  o.data.color_attributes.active_color=attr
 print('Vertex contacts baked',flush=True)
 w,h=int(512*W/44),384;px=[1.]*(w*h*4)
 for y in range(h):
  # Unity world Z negative maps to Blender Y positive. PNG V increases with plane distance south.
  for x in range(w):
   value=ao(Vector(((x+.5)/w*W,(y+.5)/h*D,.07)),Vector((0,0,1)))
   k=(y*w+x)*4;px[k:k+4]=(value,value,value,1)
  if y%64==0:print('Floor contact rows',y,'/',h,flush=True)
 image=bpy.data.images.new('Cryo quiet contact shading',width=w,height=h,alpha=False,float_buffer=False);image.colorspace_settings.name='Non-Color';image.pixels.foreach_set(px);image.filepath_raw=str(ROOT/('exports/SoftFloorAO'+suffix+'.png'));image.file_format='PNG';image.save()
 (ROOT/'exports/contact-bake.json').write_text(json.dumps({'floorMap':[w,h],'samples':12,'radiusMetres':1.65,'surfaceVertexRadiusMetres':.9,'source':'Blender geometry BVH; smooth ambient contact only; no albedo noise','textureCount':1},indent=2))
 print('Soft contact map saved',flush=True)
