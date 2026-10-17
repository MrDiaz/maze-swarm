"""New Cryo environment. Blender 5.2.2. No imported kit geometry or textures.
Call boundary uses Unity metres, +Y up, map plane Z negative. FBX -Z/Y.
"""
import bpy, math, json, sys, pathlib
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parent.parent
OUT=ROOT/'exports'; OUT.mkdir(exist_ok=True)
# --layout <id> (cryo-a / cryo-b / cryo-c / cryo-boss, from source/layouts.json): the room for that Cryo Station layout.
# No --layout: the original first-floor room (source/map.json, cryo-a), files named as before.
LAYOUT=sys.argv[sys.argv.index('--layout')+1] if '--layout' in sys.argv else None
SUFFIX=('_'+LAYOUT) if LAYOUT else ''
# --props (2026-10-15): the islands keep their plinths, the kit's machines become anchors for the Meshy Cryo objects
# (Assets/Maze/CryoProps), and the rear wall leaves a bay for a blast door; anchors go to CryoSoftRoom_<id>_props.json
PROPS='--props' in sys.argv
ANCHORS=[]
DOOR_X={'cryo-a':16,'cryo-b':14,'cryo-c':22,'cryo-boss':24}; DOOR_W=4.7
# what stands on each island, in scan order (cycled): w = wide machine islands, s = small ones, c = cargo islands
SEQ={'cryo-a':{'w':['reactor'],'s':['gun','flame'],'c':['crates','cart']},
     'cryo-b':{'w':['reactor','pods','pump','tank'],'s':['gun','crates'],'c':['crates']},
     'cryo-c':{'w':['reactor'],'s':['gun','pump','flame','cart'],'c':['crate_canister']},
     'cryo-boss':{'w':['reactor'],'s':['gun'],'c':['gun','crates','flame','tank']}}
ROOM_CTR=[22,-16]
def edge_cell(cx,cz,w,d):
 # the island cell centre nearest the room centre (a turret there is within the hero's reach from the walkable edge)
 dx,dz=ROOM_CTR[0]-cx,ROOM_CTR[1]-cz
 if abs(dx)*d>=abs(dz)*w:return cx+(w/2-1.0)*(1 if dx>0 else -1),cz
 return cx,cz+(d/2-1.0)*(1 if dz>0 else -1)
def place(kind,cx,cz,w,d):
 iw,idd=w-.9,d-.9   # inside the island's rim / bumpers
 if kind=='reactor':anchor('cryo-cooling-reactor',cx,cz,180,fw=iw,fd=idd)
 elif kind=='pods':anchor('cryo-pod',cx-w/4,cz,180,name='pod_a',fw=iw/2-.2,fd=idd);anchor('cryo-pod',cx+w/4,cz,180,name='pod_b',fw=iw/2-.2,fd=idd)
 elif kind=='pump':anchor('cryo-coolant-pumping-station',cx,cz,180,fw=iw,fd=idd)
 elif kind=='tank':
  if w>=6:anchor('cryo-low-coolant-tank',cx-w*.12,cz,180,fw=iw*.7,fd=idd);anchor('cryo-freeze-canister',cx+w*.36,cz,200,name='canister_a',fw=iw*.22,fd=idd*.6,maxh=2.0)
  else:anchor('cryo-low-coolant-tank',cx,cz+d*.12,180,fw=iw,fd=idd*.6);anchor('cryo-freeze-canister',cx+w*.3,cz-d*.3,200,name='canister_a',fw=1.1,fd=1.1,maxh=1.8)
 elif kind in('gun','flame'):
  ex,ez=edge_cell(cx,cz,w,d);anchor('cryo-gun-turret' if kind=='gun' else 'cryo-flamethrower-turret',ex,ez,180,name='gun_turret' if kind=='gun' else 'flame_turret',fw=1.9,fd=1.9,maxh=2.2)
  if w>=4 and d>=4:anchor('cryo-supply-crate',cx+(cx-ex)*.9,cz+(cz-ez)*.9,170,name='turret_crate',fw=1.6,fd=1.6)   # the island's far half: a crate
 elif kind=='crates':anchor('cryo-supply-crate',cx-w/4,cz+d/4,180,name='crate_a',fw=iw/2,fd=idd/2+.3);anchor('cryo-supply-crate',cx+w/4,cz-d/4,170,name='crate_b',fw=iw/2,fd=idd/2+.3)
 elif kind=='cart':anchor('cryo-maintenance-cart',cx-w*.12,cz,200,name='cart',fw=iw*.7,fd=idd*.8);anchor('cryo-freeze-canister',cx+w*.34,cz-d*.25,180,name='canister',fw=1.0,fd=1.0,maxh=1.8)
 elif kind=='crate_canister':
  big=max(w,d);anchor('cryo-supply-crate',cx,cz+(d*.22 if d>w else 0),180,name='cargo_crate',fw=min(w,d)-.7,fd=min(w,d)-.7);anchor('cryo-freeze-canister',cx,cz-(d*.28 if d>w else 0),180,name='cargo_canister',fw=1.0,fd=1.0,maxh=1.8)
def anchor(slug,x,z,yaw=180,y=.33,name=None,fw=0,fd=0,maxh=3.4):
 # fw / fd: the footprint (m, x / z) the prop is scaled to fill (the game fits it uniformly, capped at maxh tall); 0 = as is
 ANCHORS.append({'prop':slug,'x':round(x,3),'y':round(y,3),'z':round(z,3),'yaw':yaw,'name':name or slug,'fw':round(fw,3),'fd':round(fd,3),'maxh':maxh})
def load_grid():
 if LAYOUT:return json.loads((ROOT/'source/layouts.json').read_text())[LAYOUT]
 return json.loads((ROOT/'source/map.json').read_text())['layout']
PALETTE={
 'Ivory':('#cbdce9',.70), 'White':('#e7eef4',.62), 'Blue':('#355a97',.64),
 'Navy':('#162c4f',.72), 'Floor':('#8393aa',.82), 'FloorLight':('#899bb1',.82),
 'FloorShade':('#7b8da5',.82), 'Seam':('#6e819f',.84), 'Ice':('#9dd1e7',.72),
 'Dock':('#4063b0',.74), 'Cyan':('#45d7ef',.32), 'Glass':('#4bdaef',.32),
 'Amber':('#ffc266',.48)}
MATS={}; objects=[]; records=[]
def rgb(h):return tuple(int(h[i:i+2],16)/255 for i in (1,3,5))
def linear(c):return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
def init():
 global MATS,objects,records
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);objects=[];records=[];MATS={}
 for key,(hex,rough) in PALETTE.items():
  m=bpy.data.materials.new('Soft_'+key);m.diffuse_color=(*rgb(hex),1);m.use_nodes=True
  bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*[linear(c) for c in rgb(hex)],1);bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=0
  if key in ('Cyan','Amber','Glass'): bs.inputs['Emission Color'].default_value=(*[linear(c) for c in rgb(hex)],1);bs.inputs['Emission Strength'].default_value=.35
  MATS[key]=m
 bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1

def v3(p):return Vector((p[0],-p[2],p[1]))
def finish(o,name,mat,category,bevel):
 o.name=name;o.data.materials.append(MATS[mat]);o['category']=category
 if bevel:
  b=o.modifiers.new('Broad soft radius','BEVEL');b.width=bevel;b.segments=6;b.affect='EDGES';bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
 for p in o.data.polygons:p.use_smooth=True
 w=o.modifiers.new('Quiet weighted normals','WEIGHTED_NORMAL');w.keep_sharp=True;w.weight=75;bpy.ops.object.modifier_apply(modifier=w.name)
 objects.append(o);return o

def box(name,p,size,mat,category='obstacle',radius=.16):
 bpy.ops.mesh.primitive_cube_add(size=1,location=v3(p));o=bpy.context.object;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(o,name,mat,category,min(radius,min(size)*.45))
def cyl(name,p,r,h,mat,category='obstacle',radius=.09):
 bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=r,depth=h,location=v3(p));return finish(bpy.context.object,name,mat,category,min(radius,h*.22))
def torus(name,p,r,t,mat,category='obstacle'):
 bpy.ops.mesh.primitive_torus_add(major_segments=48,minor_segments=10,location=v3(p),major_radius=r,minor_radius=t);return finish(bpy.context.object,name,mat,category,0)
def pipe(name,points,r,mat,category='obstacle'):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.resolution_u=12;c.bevel_depth=r;c.bevel_resolution=5;c.use_fill_caps=True
 s=c.splines.new('BEZIER');s.bezier_points.add(len(points)-1)
 for b,p in zip(s.bezier_points,points):b.co=v3(p);b.handle_left_type='AUTO';b.handle_right_type='AUTO'
 o=bpy.data.objects.new(name,c);bpy.context.scene.collection.objects.link(o);o.data.materials.append(MATS[mat]);o['category']=category;bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o=bpy.context.object
 for p in o.data.polygons:p.use_smooth=True
 objects.append(o);o.select_set(False);return o

def island(cx,cz,w,d,label,rim=False):
 box(label+' continuous solid plinth',(cx,.11,cz),(w-.06,.22,d-.06),'Navy',radius=.25)
 box(label+' rounded deck',(cx,.24,cz),(w-.20,.18,d-.20),'Blue',radius=.27)
 # Broad rim, no fencing or greebles. The whole unchanged collision footprint reads as solid.
 for sign in [-1,1]:
  if rim:   # the pod's raised white rim (reference): chunky rounded rails on all four sides
   box(label+' side white rim',(cx+sign*(w/2-.30),.55,cz),(.46,.70,d-.40),'White',radius=.21)
   box(label+' end white rim',(cx,.52,cz+sign*(d/2-.30)),(w-.95,.62,.46),'White',radius=.21)
   continue
  box(label+' side ivory bumper',(cx+sign*(w/2-.29),.44,cz),(.42,.48,d-.40),'Ivory',radius=.18)
  box(label+' end blue bumper',(cx,.39,cz+sign*(d/2-.30)),(w-.65,.36,.42),'Blue',radius=.17)
  box(label+' amber end',(cx+sign*(w/2-.39),.46,cz-d/2+.20),(.28,.30,.14),'Amber',radius=.08)

def pod(cx,cz):
 # Claude pass 4 (reference cryo pod): a cyan glass column on a round blue/white seal, between two rounded white
 # towers with blue caps and navy side panels, under a white crown with a dark display; amber lamps at the front corners.
 # Footprint: inside the 6 x 4 island (cx-3..cx+3, cz-2..cz+2).
 gz=cz-.15
 cyl('Pod round base seal',(cx,.58,gz),1.05,.26,'Blue',radius=.10)
 torus('Pod white base ring',(cx,.72,gz),.98,.10,'White')
 cyl('Pod optical cryo chamber',(cx,1.62,gz),.82,1.75,'Glass',radius=.10)
 cyl('Pod luminous inner coolant core',(cx,1.62,gz),.52,1.55,'Cyan',radius=.18)
 torus('Pod upper cyan seal',(cx,2.50,gz),.84,.08,'Cyan')
 for q in [-1,1]:
  tx=cx+q*1.62
  box('Pod white tower',(tx,1.55,cz+.10),(1.05,2.65,2.10),'White',radius=.30)
  box('Pod tower blue cap',(tx,2.92,cz+.40),(1.00,.20,1.10),'Blue',radius=.10)
  box('Pod tower navy panel',(tx+q*.53,1.55,cz+.10),(.05,1.60,1.50),'Navy',radius=.04)
  box('Pod tower front display',(tx,1.85,cz-.96),(.50,.32,.05),'Cyan',radius=.05)
  box('Pod tower front plate',(tx,1.20,cz-.96),(.50,.36,.05),'Navy',radius=.05)
 box('Pod white crown',(cx,3.30,cz+.45),(4.10,.60,1.45),'White',radius=.28)
 box('Pod crown dark display',(cx,3.61,cz+.42),(2.10,.04,.90),'Navy',radius=.05)
 for q in [-1,1]:
  for r in [-1,1]:box('Pod amber corner lamp',(cx+q*2.72,.62,cz+r*1.74),(.30,.30,.30),'Amber',radius=.08)

def twin_pod(cx,cz):
 # variant: two slimmer cryo columns sharing one long crown, a white service tower between them
 gz=cz-.15
 for q in [-1,1]:
  px=cx+q*1.85
  cyl('Twin pod base seal',(px,.56,gz),.78,.24,'Blue',radius=.09)
  torus('Twin pod white ring',(px,.69,gz),.72,.08,'White')
  cyl('Twin pod chamber',(px,1.50,gz),.60,1.55,'Glass',radius=.09)
  cyl('Twin pod coolant core',(px,1.50,gz),.38,1.38,'Cyan',radius=.14)
  torus('Twin pod cyan seal',(px,2.30,gz),.62,.07,'Cyan')
 box('Twin pod centre tower',(cx,1.45,cz+.15),(1.15,2.45,1.90),'White',radius=.30)
 box('Twin pod tower display',(cx,1.80,cz-.82),(.55,.34,.05),'Cyan',radius=.05)
 box('Twin pod tower plate',(cx,1.15,cz-.82),(.55,.38,.05),'Navy',radius=.05)
 box('Twin pod long crown',(cx,3.05,cz+.40),(6.20,.50,1.30),'White',radius=.24)
 box('Twin pod crown display',(cx,3.31,cz+.38),(3.20,.04,.80),'Navy',radius=.05)
 for q in [-1,1]:
  for r in [-1,1]:box('Twin pod amber lamp',(cx+q*3.60,.62,cz+r*1.55),(.28,.28,.28),'Amber',radius=.08)

def console_bank(cx,cz):
 # variant: three control consoles under one rounded white hood, cyan screens facing the camera, amber status lights
 box('Bank navy body',(cx,.92,cz+.10),(6.40,1.20,2.30),'Navy',radius=.26)
 box('Bank white hood',(cx,1.78,cz+.25),(6.70,.66,2.30),'White',radius=.32)
 box('Bank dark top plate',(cx,2.13,cz+.25),(5.60,.05,1.40),'Navy',radius=.05)
 for k in range(3):
  bx=cx+(k-1)*2.10
  box('Bank cyan screen',(bx,1.10,cz-1.06),(1.35,.46,.06),'Cyan',radius=.10)
  box('Bank amber status',(bx-.45,1.45,cz-1.08),(.26,.10,.05),'Amber',radius=.03)
  box('Bank blue key strip',(bx+.30,1.45,cz-1.08),(.55,.10,.05),'Blue',radius=.03)
  for i in range(3):box('Bank vent slot',(bx,2.17,cz-.15+i*.30),(1.20,.04,.10),'Blue',radius=.02)
 for q in [-1,1]:box('Bank white end cabinet',(cx+q*3.15,1.30,cz+.20),(.60,1.90,1.80),'White',radius=.22)

def coolant_tank(cx,cz):
 # variant: two tall rounded coolant tanks with blue bands and cyan gauges, joined by a pipe bridge, a pump box
 for q in [-1,1]:
  tx=cx+q*1.95
  cyl('Tank white body',(tx,1.55,cz+.10),1.05,2.50,'White',radius=.45)
  for y in [.85,2.25]:torus('Tank blue band',(tx,y,cz+.10),1.06,.07,'Blue')
  cyl('Tank blue cap',(tx,2.88,cz+.10),.62,.18,'Blue',radius=.08)
  box('Tank cyan gauge',(tx,1.55,cz-.97),(.42,.42,.05),'Cyan',radius=.12)
 pipe('Tank pipe bridge',[(cx-1.95,2.45,cz+.10),(cx-1.95,2.80,cz+.10),(cx+1.95,2.80,cz+.10),(cx+1.95,2.45,cz+.10)],.13,'Blue')
 box('Tank pump box',(cx,.95,cz+.05),(1.25,1.25,1.70),'Navy',radius=.22)
 box('Tank pump cyan window',(cx,1.05,cz-.82),(.70,.32,.05),'Cyan',radius=.08)
 box('Tank pump amber light',(cx,1.42,cz-.82),(.26,.10,.05),'Amber',radius=.03)

def pump(cx,cz):
 # Claude pass 3 (reference console): a navy body with glowing cyan windows under a chunky rounded white hood, a dark
 # vented top plate, and a white side cabinet with a blue band. Inside the island footprint.
 box('Console navy body',(cx-.20,.95,cz+.05),(2.55,1.25,2.35),'Navy',radius=.26)
 box('Console cyan front window',(cx-.20,1.02,cz-1.13),(1.20,.42,.06),'Cyan',radius=.10)
 box('Console cyan side window',(cx-1.48,1.02,cz-.15),(.06,.50,1.10),'Cyan',radius=.10)
 box('Console white hood',(cx-.30,1.86,cz+.10),(2.75,.78,2.45),'White',radius=.34)
 box('Console dark vent plate',(cx-.30,2.27,cz+.12),(1.85,.06,1.55),'Navy',radius=.05)
 for i in range(4):box('Console vent slot',(cx-.30,2.31,cz-.43+i*.36),(1.55,.04,.12),'Blue',radius=.02)
 box('Console side cabinet',(cx+1.22,1.45,cz-.10),(.90,1.70,1.30),'White',radius=.24)
 box('Console side blue band',(cx+1.22,1.25,cz-.76),(.86,.55,.06),'Blue',radius=.08)
 box('Console side dark cap',(cx+1.22,2.12,cz-.30),(.62,.30,.62),'Navy',radius=.08)
 for q in [-1,1]:cyl('Console coolant pipe',(cx+q*.55,.72,cz-1.16),.16,.62,'Blue',radius=.06)
 box('Console amber status',(cx-.20,1.42,cz-1.16),(.40,.12,.06),'Amber',radius=.04)

def cargo(cx,cz,w,d,label):
 island(cx,cz,w,d,label)
 for dx,dz in [(-.83,.43),(.65,-.57)]:
  box('Cargo white crate',(cx+dx,.98,cz+dz),(1.45,1.10,1.20),'White',radius=.26)
  box('Cargo blue band',(cx+dx,.82,cz+dz-.61),(1.20,.38,.05),'Blue',radius=.08)
  box('Cargo navy face',(cx+dx,1.12,cz+dz-.615),(.70,.22,.035),'Navy',radius=.05)

def wall(cx,cz,w,d,h,label,inward=None):
 # Claude pass (reference match): a run of chunky rounded white blocks, each on a blue foot with an amber lamp on the
 # face toward the room. All inside the wall cells (w x d), no overhang into walkable cells.
 along_x=w>=d; L=w if along_x else d; n=max(1,round(L/3.6)); seg=L/n
 for k in range(n):
  c=-L/2+seg*(k+.5)
  px,pz=(cx+c,cz) if along_x else (cx,cz+c)
  sw,sd=(seg-.16,d-.10) if along_x else (w-.10,seg-.16)
  box(label+' blue foot',(px,.26,pz),(sw,.52,sd),'Blue','wall',.22)
  box(label+' white block',(px,.52+(h-.52)/2,pz),(sw-.06,h-.52,(min(sd-.06,1.6) if label.startswith('Rear') and along_x else sd-.06)) if along_x else (sw-.06,h-.52,sd-.06),'Ivory','wall',min(.42,(h-.52)*.45))
  if inward is not None and k%2==0:
   # the amber lamp: a short strip low on the room-facing side of the foot
   ox,oz=inward
   lx=px+ox*(sw/2+.0) if not along_x else px
   lz=pz+oz*(sd/2+.0) if along_x else pz
   box(label+' amber lamp',(lx,.30,lz),(.70,.14,.07) if along_x else (.07,.14,.70),'Amber','wall',.03)

def floor(w,d):
 box('Quiet seam foundation',(w/2,-.14,-d/2),(w,.20,d),'Seam','floor',.01)
 # few broad, subtly alternating panels; no noise/normal textures or densely tiled seams
 for zi,z in enumerate(range(0,int(d),8)):
  for xi,x in enumerate(range(0,int(w),8)):
   ww=min(8,w-x);dd=min(8,d-z)
   box('Broad floor panel',(x+ww/2,-.08,-z-dd/2),(ww-.11,.16,dd-.11),['Floor','FloorLight','Floor','FloorShade'][(xi+zi*2)%4],'floor',.07)

def plate(name,p,w,d,mat,radius=.32):
 radius=min(radius,w*.25,d*.25);outline=[]
 for cx,cz,a0 in [(w/2-radius,d/2-radius,0),(-w/2+radius,d/2-radius,90),(-w/2+radius,-d/2+radius,180),(w/2-radius,-d/2+radius,270)]:
  for k in range(9):
   a=math.radians(a0+k*90/8);outline.append((p[0]+cx+radius*math.cos(a),p[1],p[2]+cz+radius*math.sin(a)))
 verts=[v3(p)]+[v3(q) for q in outline];faces=[(0,(i+1)%len(outline)+1,i+1) for i in range(len(outline))]
 m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update();o=bpy.data.objects.new(name,m);bpy.context.scene.collection.objects.link(o);o.data.materials.append(MATS[mat]);o['category']='marking';objects.append(o);return o

def rectangles(grid,char):
 seen=set();rect=[]
 for z,row in enumerate(grid):
  for x,c in enumerate(row):
   if c!=char or (x,z) in seen:continue
   w=1
   while x+w<len(row) and row[x+w]==char and (x+w,z) not in seen:w+=1
   h=1
   while z+h<len(grid) and all(grid[z+h][k]==char and (k,z+h) not in seen for k in range(x,x+w)):h+=1
   for zz in range(z,z+h):
    for xx in range(x,x+w):seen.add((xx,zz))
   rect.append((x*2,-(z+h)*2,w*2,h*2))
 return rect

def lane_partition(x,z,w,d):
 # Solid low cover silhouette, entirely inside the same blocked grid rectangle.
 long_z=d>=w
 body=(min(w-.36,1.14),.92,d-.34) if long_z else (w-.34,.92,min(d-.36,1.14))
 foot=(min(w-.20,1.38),.24,d-.24) if long_z else (w-.24,.24,min(d-.20,1.38))
 cap=(min(w-.28,1.24),.20,d-.30) if long_z else (w-.30,.20,min(d-.28,1.24))
 box('Rounded lane cover body',(x+w/2,.52,z+d/2),body,'Ivory','rail',.18)
 box('Lane cover blue plinth',(x+w/2,.13,z+d/2),foot,'Blue','rail',.10)
 box('Lane cover broad cap',(x+w/2,1.02,z+d/2),cap,'Ice','rail',.09)
 for sign in [-1,1]:
  px,pz=(x+w/2,z+d/2+sign*d*.23) if long_z else (x+w/2+sign*w*.23,z+d/2)
  size=(.78,.014,max(.4,d*.32)) if long_z else (max(.4,w*.32),.014,.78)
  box('Recessed cryo access panel',(px,1.125,pz),size,'Blue','rail',.005)
 key=(.34,.012,.10) if long_z else (.10,.012,.34)
 box('Cover ice-blue identifier',(x+w/2,1.136,z+d/2),key,'Ivory','rail',.004)

def deck_pads(grid):
 for x,z,w,d in rectangles(grid,'i'):
  plate('Ice traction zone',(x+w/2,.049,z+d/2),w-.02,d-.02,'Ice',.32)
 for x,z,w,d in rectangles(grid,'~'):
  cx,cz=x+w/2,z+d/2
  plate('Entry pad recessed navy edge',(cx,.050,cz),w-.04,d-.04,'Navy',.32)
  plate('Entry pad blue inset',(cx,.057,cz),w-.48,d-.48,'Dock',.26)
  # Four short flush amber corner keys replace the unrelated floating white spawn rectangle.
  for sx in [-1,1]:
   for sz in [-1,1]:
    box('Entry pad corner key',(cx+sx*(w/2-.38),.065,cz+sz*(d/2-.48)),(.16,.014,.50),'Amber','marking',.006)

def room():
 grid=json.loads((ROOT/'source/map.json').read_text())['layout'];floor(44,32)
 # Four large rear masses, two long side masses per side, one continuous foreground cutaway.
 for cx,w in [(5.5,11),(18,14),(31,12),(40.5,7)]:wall(cx,-1,w,2,3.10,'Rear coherent wall',(0,-1))
 for x,ox in [(1,1),(43,-1)]:
  wall(x,-9,2,14,2.15,'Side coherent wall',(ox,0));wall(x,-24,2,16,1.55,'Side cutaway',(ox,0))
 wall(22,-31,40,2,1.15,'Foreground cutaway',(0,1))
 # purposeful asymmetry: broad cyan recess to left, one coolant assembly to right, a single service cabinet
 box('Rear observation dark surround',(7,1.58,-1.91),(5.50,1.21,.12),'Navy','wall',.18)
 box('Rear sealed cryo observation',(7,1.58,-1.981),(5.16,.91,.028),'Ice','wall',.14)
 # front face must stay in the blocked Z -2..0 band; fitted plane just within boundary
 for o in objects[-2:]:o.location.y=min(o.location.y,1.981)
 for x in [27,33]:box('Rear amber cue',(x,1.18,-1.95),(.65,.20,.08),'Amber','wall',.08)
 pipe('Rear large coolant line',[(34.2,.95,-1.84),(34.2,1.70,-1.84),(35.8,1.85,-1.84),(38,1.85,-1.84)],.14,'Blue','wall')
 box('Rear service blue inset',(19,1.67,-1.93),(1.80,1.0,.08),'Blue','wall',.18)
 box('Rear service cyan monitor',(19,1.80,-1.986),(.92,.32,.022),'Cyan','wall',.1)
 # A real internal blocked divider, kept low and explicit.
 wall(21,-4,2,4,1.45,'Internal divider')
 # Exact B and C islands, with all new equipment contained within them.
 for i,(x,z,w,d) in enumerate(rectangles(grid,'B')):
  cx=x+w/2;cz=z+d/2;island(cx,cz,w,d,'Machine island '+str(i),rim=w>=6)
  if w>=6:pod(cx+.05,cz-.10)
  elif cx>22:pump(cx,cz)
  else:
   box('Maintenance white cabinet',(cx,1.02,cz),(2.70,1.35,1.95),'White',radius=.32)
   box('Maintenance navy face',(cx,1.11,cz-1.0),(2.08,.67,.13),'Navy',radius=.15)
   box('Maintenance cyan display',(cx-.45,1.17,cz-1.075),(.80,.26,.035),'Cyan',radius=.09)
   box('Maintenance blue desk hood',(cx,1.67,cz),(2.90,.30,2.04),'White',radius=.14)
 for i,(x,z,w,d) in enumerate(rectangles(grid,'C')):cargo(x+w/2,z+d/2,w,d,'Cargo island '+str(i))
 # Collision-accurate long hedges/rails become generous rounded cutaway partitions.
 for x,z,w,d in rectangles(grid,'r'):
  lane_partition(x,z,w,d)
 deck_pads(grid)
 for z,row in enumerate(grid):
  for x,c in enumerate(row):
   if c=='o':
    cx=x*2+1;cz=-z*2-1
    box('Cryo vent smooth bezel',(cx,.045,cz),(1.78,.06,1.78),'Ivory','marking',.02)
    box('Cryo vent deep recess',(cx,.080,cz),(1.40,.035,1.42),'Navy','marking',.01)
    for k in range(5):box('Cryo vent broad blue vane',(cx,.104,cz-.51+k*.25),(1.27,.034,.13),'Blue','marking',.013)
 # Sparse amber cues only, not a repeated wall-panel rhythm.
 pass

def rear_run(W,h):
 x0=0;segs=[]
 for frac in [11,14,12,7]:
  w=W*frac/44;segs.append((x0,x0+w));x0+=w
 if PROPS:
  g0,g1=DOOR_X[LAYOUT]-DOOR_W/2,DOOR_X[LAYOUT]+DOOR_W/2;out=[]
  for a,b in segs:
   if b<=g0 or a>=g1:out.append((a,b));continue
   if a<g0-.4:out.append((a,g0))
   if b>g1+.4:out.append((g1,b))
  segs=out
 for a,b in segs:wall((a+b)/2,-1,b-a,2,h,'Rear coherent wall',(0,-1))
 if PROPS:
  dx=DOOR_X[LAYOUT]
  box('Door bay dark corridor',(dx,1.15,-.22),(DOOR_W-.3,2.3,.36),'Navy','wall',.05)
  anchor('cryo-blast-door',dx,-1.0,180,0.0,'blast_door')
 ROOM_CTR[0]=W/2;ROOM_CTR[1]=-len(load_grid())
def in_gap(x,half=1.2):
 return PROPS and abs(x-DOOR_X[LAYOUT])<DOOR_W/2+half
def room_generic():
 # 2026-10-14: every Cryo Station layout from the same kit. Perimeter: a tall rear run, side runs (taller near the
 # back, cut away toward the camera), a low foreground cutaway; interior '#' cells become low rounded dividers. All
 # geometry stays inside its cells; islands, cargo, lane partitions, ice / dock plates, spawn docks and vents as cryo-a.
 grid=load_grid();W=len(grid[0])*2;D=len(grid)*2;floor(W,D)
 x0=0
 for frac in [11,14,12,7]:
  w=W*frac/44;x0+=w
 rear_run(W,3.10)
 split=-2-round((D-4)*14/28)
 for x,ox in [(1,1),(W-1,-1)]:
  wall(x,(-2+split)/2,2,-2-split,2.15,'Side coherent wall',(ox,0));wall(x,(split-D)/2,2,D+split,1.55,'Side cutaway',(ox,0))
 wall(W/2,-(D-1),W-4,2,1.15,'Foreground cutaway',(0,1))
 k=W/44
 if not in_gap(7*k,2.8):
  box('Rear observation dark surround',(7*k,1.58,-1.91),(5.50,1.21,.12),'Navy','wall',.18)
  box('Rear sealed cryo observation',(7*k,1.58,-1.981),(5.16,.91,.028),'Ice','wall',.14)
  for o in objects[-2:]:o.location.y=min(o.location.y,1.981)
 for x in [27*k,33*k]:
  if not in_gap(x):box('Rear amber cue',(x,1.18,-1.95),(.65,.20,.08),'Amber','wall',.08)
 if not in_gap(36*k,2.2):pipe('Rear large coolant line',[(34.2*k,.95,-1.84),(34.2*k,1.70,-1.84),(35.8*k,1.85,-1.84),(38*k,1.85,-1.84)],.14,'Blue','wall')
 sx=15*k if not in_gap(15*k) else W*0.62
 box('Rear service blue inset',(sx,1.67,-1.93),(1.80,1.0,.08),'Blue','wall',.18)
 box('Rear service cyan monitor',(sx,1.80,-1.986),(.92,.32,.022),'Cyan','wall',.1)
 inner=[''.join(c if (0<x<len(row)-1 and 0<z<len(grid)-1) else '.' for x,c in enumerate(row)) for z,row in enumerate(grid)]
 for x,z,w,d in rectangles(inner,'#'):wall(x+w/2,z+d/2,w,d,1.45,'Internal divider')
 cells(grid,W)

def cells(grid,W):
 wide=0;small=0
 for i,(x,z,w,d) in enumerate(rectangles(grid,'B')):
  cx=x+w/2;cz=z+d/2;island(cx,cz,w,d,'Machine island '+str(i),rim=w>=6)
  if PROPS:
   q=SEQ[LAYOUT]['w' if w>=6 else 's']
   if w>=6:place(q[wide%len(q)],cx,cz,w,d);wide+=1
   else:place(q[small%len(q)],cx,cz,w,d);small+=1
   continue
  if w>=6:
   k=wide;wide+=1   # deterministic per island (scan order): single pod, twin pod, console bank, coolant tank
   [pod,twin_pod,console_bank,coolant_tank][k%4](cx+.05,cz-.10) if w>=7.5 else pod(cx+.05,cz-.10)
  elif cx>W/2:pump(cx,cz)
  else:
   box('Maintenance white cabinet',(cx,1.02,cz),(min(2.70,w-1.3),1.35,min(1.95,d-1.0)),'White',radius=.32)
   box('Maintenance navy face',(cx,1.11,cz-min(1.0,d/2-.5)),(min(2.08,w-1.9),.67,.13),'Navy',radius=.15)
   box('Maintenance cyan display',(cx-.45,1.17,cz-min(1.075,d/2-.42)),(.80,.26,.035),'Cyan',radius=.09)
   box('Maintenance blue desk hood',(cx,1.67,cz),(min(2.90,w-1.1),.30,min(2.04,d-.9)),'White',radius=.14)
 for i,(x,z,w,d) in enumerate(rectangles(grid,'C')):
  cx,cz=x+w/2,z+d/2
  if PROPS:   # cargo islands: supply crates (two on a square island, a crate and a canister on a narrow one)
   island(cx,cz,w,d,'Cargo island '+str(i))
   q=SEQ[LAYOUT]['c'];kind=q[i%len(q)]
   if kind in ('crates','crate_canister') and not (w>=4 and d>=4):kind='crate_canister'
   place(kind,cx,cz,w,d)
  else:cargo(cx,cz,w,d,'Cargo island '+str(i))
 for x,z,w,d in rectangles(grid,'r'):
  lane_partition(x,z,w,d)
 deck_pads(grid)
 for z,row in enumerate(grid):
  for x,c in enumerate(row):
   if c=='o':
    cx=x*2+1;cz=-z*2-1
    box('Cryo vent smooth bezel',(cx,.045,cz),(1.78,.06,1.78),'Ivory','marking',.02)
    box('Cryo vent deep recess',(cx,.080,cz),(1.40,.035,1.42),'Navy','marking',.01)
    for k in range(5):box('Cryo vent broad blue vane',(cx,.104,cz-.51+k*.25),(1.27,.034,.13),'Blue','marking',.013)

def render(path,centre,dist=22,res=(1440,960)):
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.cycles.device='CPU';scene.render.threads_mode='FIXED';scene.render.threads=6
 scene.world.color=(.28,.34,.43)
 world=scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.46,.60,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45
 bpy.ops.object.light_add(type='AREA',location=v3((centre[0]-7,14,centre[2]-6)));key=bpy.context.object;key.data.energy=2100;key.data.shape='DISK';key.data.size=8;key.rotation_euler=(v3(centre)-key.location).to_track_quat('-Z','Y').to_euler()
 bpy.ops.object.light_add(type='AREA',location=v3((centre[0]+8,8,centre[2]+4)));fill=bpy.context.object;fill.data.energy=800;fill.data.size=10;fill.rotation_euler=(v3(centre)-fill.location).to_track_quat('-Z','Y').to_euler()
 bpy.ops.object.camera_add();cam=bpy.context.object;pitch=math.radians(57);yaw=math.radians(-14);off=(math.sin(yaw)*math.cos(pitch)*dist,math.sin(pitch)*dist,-math.cos(yaw)*math.cos(pitch)*dist);cam.location=v3(tuple(centre[k]+off[k] for k in range(3)));cam.rotation_euler=(v3(centre)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=18;scene.camera=cam
 scene.render.resolution_x=res[0];scene.render.resolution_y=res[1];scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);scene.view_settings.view_transform='AgX';bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')));bpy.ops.render.render(write_still=True)

def export():
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('CryoSoftRoom'+SUFFIX+'.blend')))
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 groups={}
 for o in objects:groups.setdefault((o['category'],o.data.materials[0].name),[]).append(o)
 merged=[]
 for (category,mat),obs in groups.items():
  bpy.ops.object.select_all(action='DESELECT')
  for o in obs:o.select_set(True)
  bpy.context.view_layer.objects.active=obs[0];
  if len(obs)>1:bpy.ops.object.join()
  o=bpy.context.object;o.name='SC_'+category+'_'+mat;o['category']=category;merged.append(o)
 objects[:]=merged
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(OUT/('CryoSoftRoom'+SUFFIX+'.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,use_custom_props=True,path_mode='AUTO',embed_textures=False)
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('CryoSoftRoom'+SUFFIX+'-Batched.blend')))
 G=load_grid();manifest={'metres':True,'layout':LAYOUT or 'cryo-a','unityBoundsExpected':[0,len(G[0])*2,-len(G)*2,0],'textureCount':0,'materials':{k:{'hex':v[0],'roughness':v[1]} for k,v in PALETTE.items()},'objects':[],'triangles':0}
 for o in objects:
  o.data.calc_loop_triangles(); n=len(o.data.loop_triangles);manifest['triangles']+=n; vs=[o.matrix_world@v.co for v in o.data.vertices];unity=[(v.x,v.z,-v.y) for v in vs]
  manifest['objects'].append({'name':o.name,'category':o['category'],'triangles':n,'boundsMin':[min(v[k] for v in unity) for k in range(3)],'boundsMax':[max(v[k] for v in unity) for k in range(3)]})
 (OUT/('CryoSoftRoom'+SUFFIX+'_props.json')).write_text(json.dumps({'layout':LAYOUT,'anchors':ANCHORS,'door_x':DOOR_X.get(LAYOUT) if PROPS else None},indent=1)) if PROPS else None
 (OUT/('manifest'+SUFFIX+'.json')).write_text(json.dumps(manifest,indent=2));print('SOFT_ROOM_EXPORT',len(objects),manifest['triangles'])

if "--room-only" not in sys.argv:
 init();floor(14,12);wall(7,-1,14,2,2.65,'Slice broad wall');box('Slice dark observation recess',(3.5,1.65,-1.90),(4,1.10,.12),'Navy','wall',.17);box('Slice frosted pane',(3.5,1.65,-1.97),(3.70,.83,.035),'Ice','wall',.13);island(8,-6,6,6,'Slice pod island');pod(8,-6);box('Slice ice pad',(2.9,.051,-7.5),(3.75,.03,5.6),'Ice','marking',.015);render(OUT/'CryoSoft-slice-final.png',(7,0,-5.5))
init()
if LAYOUT and (LAYOUT!='cryo-a' or PROPS):room_generic()
else:room()
if "--bake" in sys.argv:
 G=load_grid();sys.path.insert(0,str(ROOT/'source'));from bake_soft_contacts import bake_contacts;bake_contacts(objects,len(G[0])*2,len(G)*2,SUFFIX)
export()
