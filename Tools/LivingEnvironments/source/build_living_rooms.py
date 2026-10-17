"""Authored 3D menu rooms. Unity metres, Y up, Z forward; no generated imagery or external models."""
import bpy, math, pathlib, json
from mathutils import Vector
OUT=pathlib.Path(__file__).resolve().parent.parent/'exports';OUT.mkdir(exist_ok=True)
PALETTE={'Ivory':'#e7e1cf','White':'#e6edf0','Navy':'#172d52','Blue':'#426799','Floor':'#aeb8c7','Seam':'#6f819d','Gold':'#ffc347','Cyan':'#29d6ef','Glass':'#56bdec','Wood':'#d3b58c','Leaf':'#67b64e','LeafDark':'#357959','Earth':'#b79978','Garden':'#7bcf50','Ice':'#c4e6fc','Rock':'#7994bc','Desert':'#ffb45c','Cloud':'#ffe8dd','Sky':'#9ddcf5','Ceiling':'#e7e1cf'}
M={};objs=[]
def V(p):return Vector((p[0],-p[2],p[1]))
def init():
 global M,objs
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);M={};objs=[]
 for n,h in PALETTE.items():
  m=bpy.data.materials.new('Living_'+n);rgb=tuple(int(h[k:k+2],16)/255 for k in [1,3,5]);m.diffuse_color=(*rgb,1);M[n]=m
 bpy.context.scene.unit_settings.system='METRIC'
def finish(o,n,mat,r=0,parent=None):
 o.name=n;o.data.materials.append(M[mat]);bpy.context.view_layer.objects.active=o
 if r:
  mod=o.modifiers.new('Soft rounded edges','BEVEL');mod.width=r;mod.segments=4;bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in o.data.polygons:p.use_smooth=True
 if o.type=='MESH':
  mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');mod.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=mod.name)
 if parent:o.parent=parent
 objs.append(o);return o
def box(n,p,s,mat,r=.12,parent=None,rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=V(p));o=bpy.context.object;o.dimensions=(s[0],s[2],s[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if rot:o.rotation_euler=[math.radians(a) for a in rot]
 return finish(o,n,mat,min(r,min(s)*.4),parent)
def cyl(n,p,r,h,mat,parent=None):
 bpy.ops.mesh.primitive_cylinder_add(vertices=40,radius=r,depth=h,location=V(p));return finish(bpy.context.object,n,mat,min(.1,h*.2),parent)
def ball(n,p,s,mat,parent=None):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,radius=1,location=V(p));o=bpy.context.object;o.scale=(s[0],s[2],s[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return finish(o,n,mat,0,parent)
def ring(n,p,r,t,mat,parent=None):
 bpy.ops.mesh.primitive_torus_add(major_segments=48,minor_segments=8,major_radius=r,minor_radius=t,location=V(p));return finish(bpy.context.object,n,mat,0,parent)
def empty(n,p=(0,0,0)):
 o=bpy.data.objects.new(n,None);o.location=V(p);bpy.context.collection.objects.link(o);return o
def parent_keep(o,p):
 bpy.context.view_layer.update();matrix=o.matrix_world.copy();o.parent=p;bpy.context.view_layer.update();o.matrix_world=matrix;bpy.context.view_layer.update()
 return o
def pillar(n,p,s,mat='Ivory'):return box(n,p,s,mat,.22)
def plant(x,y,z,size=1):
 cyl('Plant pot',(x,y+.24*size,z),.36*size,.5*size,'Ivory')
 for k in range(7):
  a=k*2.4;ball('Broad sculpted leaf',(x+math.sin(a)*.28*size,y+.7*size+(k%3)*.15*size,z+math.cos(a)*.28*size),(.20*size,.45*size,.12*size),'Leaf' if k%2 else 'LeafDark')
def floor():
 box('Room continuous base',(0,-.30,-4.5),(28,.40,20),'Navy',.35);box('Room rounded deck',(0,-.08,-4.5),(27.8,.25,19.8),'Floor',.30)
 box('Overhead ivory canopy',(0,6.7,0),(28,.40,23),'Ceiling',.30)
 for x in [-8,8]:box('Warm ceiling practical',(x,6.45,.5),(.16,.08,11),'Gold',.03)
 for z in [-4,-1,2,5]:box('Floor tile seam',(0,.052,z),(22,.015,.025),'Seam',.005)
 for x in [-9,-6,-3,0,3,6,9]:box('Floor short seam',(x,.054,1),( .013,.015,12),'Seam',.005)
 for x in [-10.8,10.8]:
  box('Low side hull',(x,1.1,2),(.6,2.2,13),'Ivory',.24);box('Side blue rub rail',(x*.99,.70,2),(.18,.55,12.5),'Navy',.07)
def sky():
 box('Panoramic sky',(0,4,38),(70,30,.2),'Sky',0)
 for i in range(11):
  x=-18+i*3.5;y=1.6+(i%3)*.4;z=12+(i%2)*3
  group=empty('CloudDrift'+str(i))
  for j in range(3):parent_keep(ball('Soft cloud',(x+j*.9,y+(j%2)*.2,z),(1.1,.45,.75),'Cloud'),group)
 for i,(x,z,s) in enumerate([(-6,12,1.2),(1,15,1.6),(8,13,.9)]):
  bpy.ops.mesh.primitive_cone_add(vertices=9,radius1=s*.35,radius2=s,depth=s*1.5,location=V((x,2.0,z)));finish(bpy.context.object,'Floating island cliff','Rock',.04)
  cyl('Distant island grass',(x,2.05+s*.75,z),s*1.03,.13,'Garden')
  for k in range(5):
   tx=x+math.sin(k*2.4)*s*.6;tz=z+math.cos(k*2.4)*s*.6;ty=2.2+s*.75
   cyl('Distant tree trunk',(tx,ty+.13,tz),.035,.25,'Wood');ball('Distant treetop',(tx,ty+.39,tz),(.25,.28,.25),'LeafDark')
 ball('Warm distant sun',(6.2,5.5,15),(.9,.9,.3),'Gold')
def windows():
 for x in [-10.5,-5.3,0,5.3,10.5]:
  pillar('Window structural rib',(x,3.3,5.3),(.48,6.5,.65));box('Window rib blue inset',(x,3.4,4.94),(.19,4.8,.08),'Navy',.05);box('Window rib cyan accent',(x,4.7,4.87),(.09,.8,.06),'Cyan',.025)
 box('Panoramic lower sill',(0,.9,5.25),(21.4,.5,.75),'Ivory',.18);box('Panoramic gold handrail',(0,1.24,4.93),(21,.10,.16),'Gold',.05)
 box('Overhead ceiling beam',(0,6.3,5.2),(22,.6,1.1),'Ivory',.22);box('Ceiling blue inset',(0,6.16,4.6),(20,.16,.18),'Navy',.05)
def monitor(n,x,y,z,w=1.4,h=.75):
 box(n+' housing',(x,y,z),(w+.28,h+.25,.32),'Ivory',.15);box(n+' face',(x,y,z-.18),(w,h,.055),'Navy',.06)
 for k in range(3):box(n+' readout',(x-w*.3+k*w*.28,y-h*.12,z-.217),(w*.16,h*.11,.017),'Cyan',.015)
 box(n+' header',(x-w*.14,y+h*.24,z-.217),(w*.47,h*.08,.018),'Cyan',.012)
 ball(n+' status',(x+w*.35,y+h*.27,z-.22),(.05,.05,.018),'Gold')
def console(x,z):
 box('Navigation console',(x,.7,z),(2.5,1.4,1.0),'Navy',.22);box('Navigation console hood',(x,1.45,z),(2.7,.65,1.15),'Ivory',.24);monitor('Navigation display',x,1.5,z-.65,1.8,.67)
 cyl('Pilot chair base',(x,.15,z-1.15),.45,.2,'Navy');box('Pilot chair seat',(x,.7,z-1.15),(1.0,.28,.9),'Gold',.16);box('Pilot chair back',(x,1.3,z-.85),(1.0,1.0,.28),'Gold',.18)
def holo_island(name,x,y,z,mat):
 group=empty(name)
 bpy.ops.mesh.primitive_cone_add(vertices=9,radius1=.43,radius2=.72,depth=.34,location=V((x,y,z)));parent_keep(finish(bpy.context.object,name+' floating cliff','Rock',.03),group)
 parent_keep(cyl(name+' terrain',(x,y+.20,z),.75,.12,mat),group)
 for k in range(5):
  tx=x+math.sin(k*2.4)*.44;tz=z+math.cos(k*2.4)*.4
  if mat=='Garden':
   parent_keep(cyl(name+' tree trunk',(tx,y+.38,tz),.035,.20,'Wood'),group);parent_keep(ball(name+' tree canopy',(tx,y+.55,tz),(.14,.18,.14),'Leaf'),group)
  elif mat=='Ice':
   parent_keep(box(name+' arctic station',(tx,y+.43,tz),(.21,.25,.22),'Ice',.04),group);parent_keep(box(name+' blue station cap',(tx,y+.57,tz),(.22,.06,.23),'Blue',.02),group)
  else:
   parent_keep(cyl(name+' refinery column',(tx,y+.48,tz),.12,.43,'Ivory'),group);parent_keep(ring(name+' refinery orange band',(tx,y+.55,tz),.125,.025,'Gold'),group);parent_keep(cyl(name+' refinery chimney',(tx,y+.76,tz),.055,.17,'Navy'),group)
 pin=ring(name+' location marker',(x,y+1.05,z),.10,.028,'Cyan' if mat!='Desert' else 'Gold');pin.rotation_euler[0]=math.pi/2;parent_keep(pin,group);parent_keep(box(name+' marker stem',(x,y+.89,z),(.035,.20,.035),'Cyan' if mat!='Desert' else 'Gold',.012),group)
 bpy.context.view_layer.update();matrices=[(o,o.matrix_world.copy()) for o in group.children];group.location=V((x,y,z));bpy.context.view_layer.update()
 for o,m in matrices:o.matrix_world=m
 return group
def hangar():
 init();floor();sky();windows();console(-3.5,3.3);console(5.0,3.3)
 cyl('Heavy command deck inset',(-4.5,.07,-1.1),1.4,.06,'Ivory');ring('Heavy command deck gold trim',(-4.5,.11,-1.1),1.42,.035,'Gold')
 # curved-looking lounge set with individual soft cushions
 box('Left lounge shell',(-8,.55,1.7),(3.7,.75,1.4),'Ivory',.28)
 for x in [-9.1,-8,-6.9]:box('Lounge gold cushion',(x,.97,1.45),(.98,.32,1.1),'Gold',.15);box('Lounge gold back',(x,1.44,1.99),(.98,.95,.26),'Gold',.15)
 plant(-9.5,0,2.2,1.4);plant(9.8,0,2.4,1.15)
 # central tactical table, recognisable thick white rim and cyan glass projection
 x,z=1.35,-.45
 cyl('Tactical table navy foot',(x,.14,z),2.75,.27,'Navy');cyl('Tactical table ivory shell',(x,.52,z),2.90,.66,'Ivory');cyl('Tactical table blue lip',(x,.90,z),2.76,.16,'Blue');cyl('Hologram display',(x,1.00,z),2.53,.055,'Cyan')
 for r in [1.35,1.95,2.48]:ring('Tactical hologram orbit',(x,1.065,z),r,.018,'White')
 for a in range(0,360,45):
  q=math.radians(a);box('Table gold control',(x+math.sin(q)*2.72,.84,z+math.cos(q)*2.72),(.36,.18,.28),'Gold',.07)
 holo_island('HoloIslandVerdantColony',x-.85,1.28,z+.5,'Garden');holo_island('HoloIslandKesslerRefinery',x+.9,1.28,z+.5,'Desert');holo_island('HoloIslandCryoStation',x+.1,1.25,z-.75,'Ice')
 # side airlock avoids the UI button area in the front-right
 box('Airlock blue surround',(8,2.5,4.9),(3.2,4.7,.7),'Navy',.35);box('Airlock white frame',(8,2.5,4.48),(2.8,4.4,.45),'Ivory',.25);box('Airlock recessed door',(8,2.42,4.18),(2.24,3.78,.16),'Blue',.2)
 for dx in [-1.05,1.05]:box('Airlock warm edge',(8+dx,2.45,4.05),(.10,3.4,.08),'Gold',.035)
 for y in [2.2,2.65]:box('Airlock chevron',(8,y,4.04),(1.0,.12,.06),'Gold',.04)
 export('CommandShip')
def tool(x,y,z,kind):
 if kind%2==0:
  box('Hanging wrench stem',(x,y,z),(.09,.62,.09),'Ivory',.025);ring('Wrench ring',(x,y+.32,z),.14,.06,'Navy').rotation_euler[0]=math.pi/2
 else:
  box('Workshop screwdriver',(x,y-.15,z),(.12,.35,.14),'Gold',.04);box('Workshop driver shaft',(x,y+.19,z),(.045,.37,.045),'Ivory',.01)
def armory():
 init();floor();sky()
 # rear workshop walls and broad round portal into the city/sky
 wall=box('Workshop back wall',(0,3.2,6.0),(24,6.5,.5),'Ivory',.16)
 bpy.ops.mesh.primitive_cylinder_add(vertices=64,radius=2.18,depth=2.0,location=V((6.7,3.4,6.0)));cut=bpy.context.object;cut.rotation_euler[0]=math.pi/2;bpy.context.view_layer.objects.active=wall;mod=wall.modifiers.new('True round window opening','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cut;bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cut,do_unlink=True)
 for i in range(8):box('Distant city tower',(4+i*.65,2.5+(i%3)*.45,12+i%2),(.48,2.8+(i%3)*.9,.7),'Ivory' if i%2 else 'Rock',.12)
 # open panoramic portion to the right is layered in front of the wall as a luminous portal
 rim=ring('Round window ivory rim',(6.7,3.4,5.42),2.32,.19,'Ivory');rim.rotation_euler[0]=math.pi/2
 # chunky cabinetry, toe recesses, lit worktop, shelves and real tools
 for x in [-8.5,-5.6,-2.7]:
  box('Armory navy toe',(x,.14,4.7),(2.7,.26,1.2),'Navy',.08);box('Armory cabinet',(x,.83,4.7),(2.72,1.26,1.35),'Blue',.16);box('Ivory work counter',(x,1.55,4.65),(2.92,.22,1.55),'Ivory',.10)
  for q in [-.66,.66]:box('Cabinet front',(x+q,.9,3.98),(1.23,1.0,.10),'Blue',.10);box('Gold cabinet handle',(x+q,1.19,3.89),(.6,.07,.08),'Gold',.03)
 box('Tool pegboard',(-5.1,3.35,5.65),(9.6,2.65,.14),'Wood',.12)
 for x in [-9,-7,-5,-3,-1]:
  for y in [2.4,3.0,3.6,4.2]:ball('Pegboard fastener',(x,y,5.55),(.035,.035,.025),'Navy')
 for k in range(9):tool(-8.5+k*.85,3.55 if k%2 else 3.1,5.40,k)
 for x in [-6.8,-3.5]:
  box('Service drill body',(x,3.6,5.17),(.70,.38,.30),'Gold',.08);box('Service drill grip',(x+.18,3.29,5.17),(.20,.40,.26),'Navy',.06);box('Service drill chuck',(x-.48,3.6,5.17),(.28,.23,.23),'Navy',.05)
 for x in [-8,-6.4,-4.8,-1.5]:
  cyl('Shelf coolant tin',(x,5.15,5.0),.18,.56,'Blue');ring('Shelf tin gold band',(x,5.16,5),.185,.025,'Gold')
 for y in [2.25,4.8]:box('Workshop shelf',(-5.0,y,5.1),(10.3,.16,1.15),'Ivory',.07);box('Shelf warm underlight',(-5.0,y-.10,4.9),(9.8,.04,.1),'Gold',.012)
 for x in [-8.6,-7.4,-2.0,-.8]:
  box('Stored equipment case',(x,2.6,5.0),(.9,.55,.65),'Navy',.10);box('Case gold latch',(x,2.55,4.64),(.19,.12,.04),'Gold',.03)
 plant(8.5,1.4,3.7,1.2)
 # visible workbench positioned behind Heavy, left of the inventory overlay
 x,z=-3.7,.5
 box('Workbench navy toe',(x,.16,z),(6.0,.27,2.1),'Navy',.16);box('Workbench blue body',(x,.7,z),(6.2,1.15,2.25),'Blue',.25);box('Workbench soft ivory top',(x,1.4,z),(6.6,.36,2.65),'Ivory',.25)
 for q in [-2,-.7,.7,2]:box('Workbench drawer',(x+q,.85,z-1.15),(1.12,.65,.08),'Navy',.08);box('Workbench gold pull',(x+q,1.01,z-1.21),(.5,.07,.06),'Gold',.025)
 for dx in [-2.8,2.8]:box('Workbench warm corner lamp',(x+dx,1.37,z-1.35),(.50,.10,.05),'Gold',.025)
 # articulated maintenance arm; joints are actual transform groups, animated at runtime
 arm=empty('ServiceArm'); shoulder=empty('ServiceShoulder');shoulder.location=V((-2.4,5.3,2));shoulder.parent=arm
 o=box('Service arm ceiling mount',(-2.4,5.9,2),(1.4,.45,1.3),'Navy',.15)
 for n,p,s,mat in [('Service upper arm',(-2.3,4.8,1.9),(.48,1.2,.58),'Ivory'),('Service elbow',(-2.25,4.25,1.85),(.63,.48,.68),'Gold'),('Service forearm',(-1.8,3.8,1.5),(.85,.4,.4),'Ivory'),('Service clamp',(-1.32,3.57,1.3),(.3,.56,.45),'Navy')]:parent_keep(box(n,p,s,mat,.10),shoulder)
 # compact blue holographic diagnostic screen behind the service bench
 box('Diagnostic screen frame',(.15,2.8,3.5),(2.5,1.55,.12),'Navy',.12);box('Diagnostic screen',( .15,2.8,3.41),(2.28,1.33,.04),'Cyan',.08)
 for k in range(6):box('Diagnostic readout',(-.7+k*.33,2.5,3.37),(.14,.12+(k%3)*.1,.015),'White',.015)
 cyl('Workbench mug',(-6.15,1.8,-.1),.17,.35,'Ivory');ring('Mug gold rim',(-6.15,1.99,-.1),.15,.025,'Gold')
 export('ArmoryWorkshop')
def contacts(name):
 import numpy as np
 bpy.context.view_layer.update();N=512;X,Z=np.meshgrid(np.linspace(-13,13,N),np.linspace(-6,20,N));A=np.ones((N,N))
 for o in objs:
  bb=[o.matrix_world@Vector(v) for v in o.bound_box];lo=[min(v[k] for v in bb) for k in range(3)];hi=[max(v[k] for v in bb) for k in range(3)]
  if lo[2]>.30 or hi[2]<.12 or hi[0]-lo[0]>9 or hi[1]-lo[1]>9:continue
  dx=np.maximum(np.maximum(lo[0]-X,X-hi[0]),0);z0,z1=-hi[1],-lo[1];dz=np.maximum(np.maximum(z0-Z,Z-z1),0)
  A=np.minimum(A,1-.42*np.exp(-(dx*dx+dz*dz)/.13))
 image=bpy.data.images.new(name+'_contact',width=N,height=N);rgba=np.stack([A,A,A,np.ones_like(A)],axis=-1);image.pixels.foreach_set(rgba.astype(np.float32).ravel());image.filepath_raw=str(OUT/(name+'_contact.png'));image.file_format='PNG';image.save();bpy.data.images.remove(image)
def export(name):
 polish_room(name)
 bake_vertex_contacts()
 contacts(name)
 bpy.ops.object.select_all(action='DESELECT')
 for o in bpy.context.scene.objects:o.select_set(o.type in ['MESH','EMPTY'])
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(name+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True,path_mode='AUTO',colors_type='LINEAR')
 (OUT/(name+'.json')).write_text(json.dumps({'room':name,'objects':len(objs),'polygons':sum(len(o.data.polygons) for o in objs),'materials':list(PALETTE),'recipe_sha256':__import__('hashlib').sha256(pathlib.Path(__file__).read_bytes()+pathlib.Path(__file__).with_name('polish_layers.py').read_bytes()).hexdigest()},indent=2))
exec(compile(pathlib.Path(__file__).with_name('polish_layers.py').read_text(), 'polish_layers.py', 'exec'))
hangar();armory()
