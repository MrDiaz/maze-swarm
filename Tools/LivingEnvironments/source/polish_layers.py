"""Authored detail and vertex contact shading for the existing two living rooms.
Runs inside build_living_rooms.py's scene namespace. All additions are visual geometry.
"""
import random
from mathutils.bvhtree import BVHTree

def beam(n,a,b,width,depth,mat='Ivory',r=.07):
    mid=tuple((a[k]+b[k])*.5 for k in range(3))
    o=box(n,mid,(width,(Vector(b)-Vector(a)).length,depth),mat,r)
    o.rotation_mode='QUATERNION';o.rotation_quaternion=(V(b)-V(a)).to_track_quat('Z','Y')
    return o

def front_ring(n,p,r,t,mat):
    o=ring(n,p,r,t,mat);o.rotation_euler[0]=math.pi/2;return o

def chevrons(n,x,y,z,w=.55,mat='Gold',direction=-1):
    for dy in [0,.28]:
        beam(n,(x-w*.5,y+dy,z),(x,y+direction*.17+dy,z),.10,.06,mat,.025)
        beam(n,(x,y+direction*.17+dy,z),(x+w*.5,y+dy,z),.10,.06,mat,.025)

def panel_fasteners(n,x,y,z,w,h):
    for dx in [-w*.42,w*.42]:
        for dy in [-h*.36,h*.36]:
            ball(n+' recessed fastening',(x+dx,y+dy,z),(.035,.035,.018),'Navy')

def plant(x,y,z,size=1):
    cyl('Ivory sculpted plant pot',(x,y+.24*size,z),.34*size,.48*size,'Ivory')
    ring('Plant pot rolled lip',(x,y+.47*size,z),.34*size,.045*size,'Ivory')
    cyl('Plant dark soil',(x,y+.465*size,z),.29*size,.025*size,'Earth')
    for k in range(7):
        a=k*2.4;start=(x,y+.40*size,z)
        end=(x+math.sin(a)*(.52+(k%2)*.12)*size,y+(1.05+(k%3)*.14)*size,z+math.cos(a)*(.52+(k%2)*.12)*size)
        mid=tuple((start[j]+end[j])*.5 for j in range(3))
        beam('Plant sweeping stem',start,end,.026*size,.026*size,'LeafDark',.008)
        o=ball('Broad angled plant leaf',mid,(.19*size,.43*size,.08*size),'Leaf' if k%2 else 'LeafDark')
        o.rotation_mode='QUATERNION';o.rotation_quaternion=(V(end)-V(start)).to_track_quat('Z','Y')

def organic_island(x,y,z,s,seed,depth=1.34):
    rng=random.Random(seed);count=12;radii=[s*(.88+rng.random()*.19) for _ in range(count)]
    verts=[]
    for height,mult in [(y-s*depth,.17),(y-s*depth*.57,.48),(y-s*.13,.94),(y+.01,1)]:
        for k,r in enumerate(radii):
            a=k*math.tau/count;verts.append(V((x+math.sin(a)*r*mult,height,z+math.cos(a)*r*mult)))
    faces=[tuple(reversed(range(count)))]
    for row in range(3):
        for k in range(count):faces.append((row*count+k,row*count+(k+1)%count,(row+1)*count+(k+1)%count,(row+1)*count+k))
    faces.append(tuple(range(3*count,4*count)))
    mesh=bpy.data.meshes.new('Rounded organic island cliff');mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new('Rounded organic island cliff',mesh);bpy.context.collection.objects.link(o);finish(o,o.name,'Rock',.055*s)
    # One irregular capped slab, so the foliage rests on a deliberate organic silhouette.
    verts=[]
    for height in [y+.01,y+.19]:
        for k,r in enumerate(radii):
            a=k*math.tau/count;verts.append(V((x+math.sin(a)*r*1.04,height,z+math.cos(a)*r*1.04)))
    faces=[tuple(reversed(range(count))),tuple(range(count,2*count))]
    for k in range(count):faces.append((k,(k+1)%count,count+(k+1)%count,count+k))
    mesh=bpy.data.meshes.new('Organic garden plateau');mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new('Organic garden plateau',mesh);bpy.context.collection.objects.link(o);finish(o,o.name,'Garden',.07*s)

def flight_deck():
    # A broad circular inset organises the room around the central tactical table.
    cyl('Circular pale command floor',(0,.062,-.4),8.5,.025,'Deck')
    ring('Circular command deck navy seam',(0,.081,-.4),8.35,.032,'Blue')
    ring('Circular command deck brass line',(0,.083,-.4),7.78,.034,'Gold')
    for angle in [-65,-25,18,58,110,155,210,250]:
        a=math.radians(angle)
        beam('Deck expansion joint',(math.sin(a)*3.4,.085,-.4+math.cos(a)*3.4),(math.sin(a)*8.25,.085,-.4+math.cos(a)*8.25),.018,.018,'Seam',.005)
    # One curved panoramic hull, with no duplicated original straight ribs behind it.
    for o in list(objs):
        if o.name.startswith(('Window structural rib','Window rib blue inset','Window rib cyan accent','Panoramic lower sill','Panoramic gold handrail','Overhead ceiling beam','Ceiling blue inset')):
            o.hide_render=True;o.hide_viewport=True
    points=[]
    for x in [-10.5,-5.3,0,5.3,10.5]:
        z=6.3-3.7*(abs(x)/10.5)**1.65
        points.append((x,z))
        top=x*.94
        beam('Angled ivory window rib',(x,1.18,z),(top,6.35,z+.28),.50,.66,'Ivory',.17)
        beam('Inset navy rib',(x,1.64,z-.37),(top,6.02,z-.09),.20,.08,'Navy',.065)
        beam('Inset cyan guide',(x*.98,4.35,z-.44),(x*.97,5.08,z-.39),.085,.055,'LightCyan',.025)
    for (x1,z1),(x2,z2) in zip(points,points[1:]):
        beam('Curved hull blue lower band',(x1,.46,z1),(x2,.46,z2),.54,.85,'Blue',.13)
        beam('Curved ivory panoramic sill',(x1,.92,z1-.08),(x2,.92,z2-.08),.38,.86,'Ivory',.13)
        beam('Curved gold handrail',(x1,1.20,z1-.49),(x2,1.20,z2-.49),.10,.13,'Gold',.04)
        beam('Curved warm hull practical',(x1,.74,z1-.48),(x2,.74,z2-.48),.095,.07,'LightWarm',.025)
        beam('Curved overhead window arch',(x1*.94,6.35,z1+.22),(x2*.94,6.35,z2+.22),.52,1.0,'Ivory',.16)
        beam('Curved overhead navy groove',(x1*.94,6.12,z1-.30),(x2*.94,6.12,z2-.30),.14,.12,'Navy',.04)

def cloud_bank(name,center,lobes):
    group=empty(name,center)
    for dx,dy,dz,rx,ry,rz in lobes:
        p=(center[0]+dx,center[1]+dy,center[2]+dz)
        parent_keep(ball('Billowing authored cloud',p,(rx,ry,rz),'Cloud'),group)

def colony_house(x,y,z,w,h,roof='Blue'):
    box('Colony house',(x,y+h*.5,z),(w,h,w*.82),'Ivory',w*.12)
    bpy.ops.mesh.primitive_cone_add(vertices=4,radius1=w*.84,radius2=w*.12,depth=w*.30,location=V((x,y+h+w*.13,z)))
    finish(bpy.context.object,'Colony tapered roof',roof,w*.04)
    box('Colony dark doorway',(x,y+.19*h,z-w*.425),(.22*w,.38*h,.025),'Navy',.025)

def sculpted_tree(x,y,z,size=1):
    beam('Leaning garden tree trunk',(x,y,z),(x+.16*size,y+.70*size,z),.09*size,.09*size,'Wood',.025)
    beam('Garden tree branch',(x+.12*size,y+.48*size,z),(x-.26*size,y+.82*size,z),.055*size,.055*size,'Wood',.018)
    ball('Broad garden canopy',(x+.13*size,y+.90*size,z),(.48*size,.30*size,.37*size),'Leaf')
    ball('Offset garden canopy',(x-.28*size,y+.94*size,z+.03*size),(.30*size,.25*size,.30*size),'LeafDark')
    ball('Raised garden canopy',(x+.04*size,y+1.12*size,z+.02*size),(.29*size,.25*size,.30*size),'Leaf')

def richer_sky():
    # Three authored silhouettes. Their different heights and depth replace the repeated ball-tree forest.
    for o in list(objs):
        if o.name.startswith(('Distant island grass','Floating island cliff','Distant tree','Distant treetop','Soft cloud')):
            o.hide_render=True;o.hide_viewport=True
        if o.name.startswith('Warm distant sun'):
            o.location=V((7.2,5.1,32));o.scale*=.72
    for group in [o for o in bpy.context.scene.objects if o.type=='EMPTY' and o.name.startswith('CloudDrift')]:
        if group.type=='EMPTY':
            for child in list(group.children):
                if child in objs:objs.remove(child)
                bpy.data.objects.remove(child,do_unlink=True)
            bpy.data.objects.remove(group,do_unlink=True)
    # Near village: broad, low rock shelf; two pitched houses and a single branched tree.
    x,y,z,scale=-7.4,2.55,16,1.65
    organic_island(x,y,z,scale,911,1.05)
    colony_house(x-.30*scale,y+.20,z+.08*scale,.52*scale,.60*scale)
    colony_house(x+.38*scale,y+.20,z+.27*scale,.36*scale,.78*scale,'Gold')
    sculpted_tree(x-.51*scale,y+.20,z-.37*scale,.79*scale)
    # Far citadel: a tall, restrained pale silhouette over a narrow taper, softened by atmospheric depth.
    x,y,z,scale=2.5,3.25,28,2.1
    organic_island(x,y,z,scale,912,1.85)
    box('Distant citadel lower tier',(x,y+.36,z),(1.15*scale,.43*scale,.83*scale),'Ivory',.10*scale)
    colony_house(x,y+.55,z+.10*scale,.40*scale,1.18*scale)
    colony_house(x-.40*scale,y+.52,z+.18*scale,.25*scale,.71*scale)
    colony_house(x+.39*scale,y+.52,z+.24*scale,.25*scale,.62*scale)
    # Right terrace: a low garden profile, one broad canopy and a clean stone marker.
    x,y,z,scale=10.5,2.75,21,1.45
    organic_island(x,y,z,scale,913,.96)
    sculpted_tree(x-.20*scale,y+.20,z,.98*scale)
    box('Distant garden stone marker',(x+.57*scale,y+.48,z+.15*scale),(.19*scale,.55*scale,.23*scale),'Ivory',.07*scale)
    cloud_bank('CloudDriftAuthoredVillage',(-9.0,1.0,19),[(0,0,0,1.6,.65,1.0),(-1.3,.05,0,1.15,.60,.82),(.95,.20,.1,1.05,.78,.85),(-.30,.55,0,1.1,.88,.88)])
    cloud_bank('CloudDriftAuthoredCitadel',(1.2,.72,30),[(0,-.1,0,2.3,.65,1.25),(1.45,.24,.2,1.35,.88,.96),(-1.32,.40,-.1,1.23,1.02,.97),(.2,.77,.12,1.05,.92,.88)])
    cloud_bank('CloudDriftAuthoredTerrace',(8.4,1.05,24),[(0,-.1,0,1.7,.63,.9),(-1.08,.10,.1,1.1,.76,.9),(.93,.47,.1,1.1,.99,.85),(-.08,.72,0,.87,.78,.8)])
    cloud_bank('CloudDriftAuthoredFar',(-15.5,1.6,33),[(0,0,0,2.3,.83,1.3),(-1.9,.2,0,1.6,.9,1.1),(1.4,.55,.1,1.6,1.13,1.2)])

def monitor_detail():
    for x,z in [(-3.5,3.3),(5.0,3.3)]:
        front_ring('Navigation radar rim',(x+.16,1.56,z-.702),.22,.022,'LightCyan')
        front_ring('Navigation radar inner',(x+.16,1.56,z-.709),.14,.009,'Cyan')
        for k in range(3):
            box('Navigation telemetry bar',(x-.56+k*.17,1.47,z-.71),(.09,.12+k*.055,.012),'LightCyan',.008)
        box('Console recessed footing',(x,.13,z), (2.3,.20,.90),'Blue',.09)
        box('Console amber kick strip',(x,.34,z-.53),(1.95,.075,.06),'LightWarm',.025)
        for dx in [-1.02,1.02]:ball('Console fastener',(x+dx,1.69,z-.70),(.035,.035,.02),'Blue')
        cyl('Chair supporting pedestal',(x,.45,z-1.15),.13,.55,'Navy')
        cyl('Chair brass pedestal collar',(x,.48,z-1.15),.17,.09,'Gold')
        box('Chair ivory back shell',(x,1.29,z-.67),(1.12,1.08,.18),'Ivory',.16)
        for dx in [-.58,.58]:
            box('Chair navy arm support',(x+dx,.91,z-1.13),(.12,.43,.47),'Navy',.055)
            box('Chair warm arm pad',(x+dx,1.12,z-1.13),(.19,.14,.74),'Gold',.055)

def table_detail():
    x,z=1.35,-.45
    ring('Table substantial ivory rim',(x,1.02,z),2.73,.095,'Ivory')
    ring('Table lower cyan seam',(x,.23,z),2.92,.035,'LightCyan')
    ring('Table internal dark glass edge',(x,1.055,z),2.47,.045,'Blue')
    # Segmented armour on the outer drum, with compact insets and fasteners.
    for a in range(0,360,45):
        q=math.radians(a)
        o=box('Tactical table segmented armour',(x+math.sin(q)*2.91,.55,z+math.cos(q)*2.91),(.85,.54,.24),'Blue',.10)
        o.rotation_euler[2]=-q
        o=box('Table cyan status panel',(x+math.sin(q)*3.05,.56,z+math.cos(q)*3.05),(.45,.11,.035),'LightCyan',.035);o.rotation_euler[2]=-q
        for da in [-.12,.12]:
            qq=q+da;ball('Table captive bolt',(x+math.sin(qq)*2.88,.78,z+math.cos(qq)*2.88),(.045,.045,.045),'Navy')
    # A quiet translucent projection perimeter and crosshair strokes.
    cyl('Hologram projection volume',(x,1.30,z),2.48,.50,'HoloGlass')
    ring('Hologram upper projection rim',(x,1.56,z),2.48,.018,'LightCyan')
    for a in range(0,360,30):
        q=math.radians(a)
        beam('Hologram radial tick',(x+math.sin(q)*2.27,1.074,z+math.cos(q)*2.27),(x+math.sin(q)*2.42,1.074,z+math.cos(q)*2.42),.016,.016,'LightCyan',.004)
    for group in [o for o in bpy.context.scene.objects if o.type=='EMPTY' and o.name.startswith('HoloIsland')]:
        pos=group.location;gx,gy,gz=pos.x,pos.z,-pos.y
        # Chunky interlocking terrain cells soften the previous plain circular discs.
        kind='Ice' if 'Cryo' in group.name else 'Garden' if 'Verdant' in group.name else 'Desert'
        for j in range(7):
            a=j*2.4;r=.34 if j else 0
            t=box(group.name+' terrain lobe',(gx+math.sin(a)*r,gy+.17,gz+math.cos(a)*r),(.48,.22,.49),kind,.10)
            parent_keep(t,group)
        if 'Cryo' in group.name:
            for dx in [-.33,.1,.45]:
                parent_keep(box('Cryo miniature cyan window',(gx+dx,gy+.47,gz-.30),(.11,.09,.035),'LightCyan',.02),group)
        # Teardrop location pins have a readable outline rather than a loose ring-and-stick shape.
        parent_keep(front_ring('Location pin quiet halo',(gx,gy+1.04,gz-.025),.125,.025,'LightCyan' if kind!='Desert' else 'LightWarm'),group)

def airlock_detail():
    x,z=8,4.02
    box('Airlock crown housing',(x,4.81,4.26),(2.3,.40,.55),'Navy',.12)
    box('Airlock upper amber practical',(x,4.68,3.93),(1.78,.13,.09),'LightWarm',.045)
    box('Airlock cyan badge',(x,4.86,3.96),(.63,.09,.06),'LightCyan',.035)
    for dx in [-1.32,1.32]:
        box('Airlock outer armoured jamb',(x+dx,2.47,4.12),(.31,3.75,.35),'Ivory',.11)
        box('Airlock outer amber guide',(x+dx,2.63,3.91),(.08,2.35,.06),'LightWarm',.03)
        box('Airlock lower dark bumper',(x+dx,.64,3.91),(.38,.62,.38),'Navy',.10)
    for o in objs:
        if o.name.startswith('Airlock chevron'):o.hide_render=True;o.hide_viewport=True
    chevrons('Airlock directional chevrons',x,2.5,z-.01,.9,'Gold',-1)
    box('Airlock ramp',(x,.13,3.24),(3.0,.25,1.9),'Navy',.16)
    for q in [-.7,0,.7]:box('Ramp tread',(x,.27,3.24+q),(2.3,.035,.06),'Blue',.01)
    for dx in [-1.4,1.4]:box('Ramp brass edge',(x+dx,.28,3.26),(.09,.05,1.7),'Gold',.025)
    # Lounge end caps and supporting navy plinth add depth to the existing cushions.
    for xx in [-10.0,-6.0]:box('Lounge rounded arm',(xx,1.1,1.66),(.36,.66,1.5),'Ivory',.16)
    box('Lounge navy footing',(-8,.22,1.7),(4.0,.23,1.24),'Navy',.10)
    group=empty('AngledAirlockAssembly',(8,0,4.2))
    for o in objs:
        if o.name.startswith(('Airlock','Ramp')):parent_keep(o,group)
    group.rotation_euler[2]=math.radians(12)

def workshop_detail():
    # Smaller holes make this a coherent pegboard rather than a few floating tools.
    for x in range(17):
        for y in range(5):ball('Pegboard mounting hole',(-9+x*.49,2.56+y*.39,5.553),(.025,.025,.012),'Navy')
    for xx in [-8.7,-5.1,-1.55]:
        box('Pegboard rail fixture',(xx,2.15,5.32),(2.7,.10,.20),'Navy',.03)
    for xx in [-8.2,-7.5,-6.8,-3.0,-2.3,-1.6]:
        cyl('Shelf spare cartridge',(xx,1.85,4.95),.10,.40,'Gold')
        cyl('Shelf cartridge sleeve',(xx,1.80,4.95),.107,.12,'Navy')
    # Open shelf cubbies and layered panels create broader authored shapes behind the hero.
    for x in [-9.4,-6.3,-3.2,-.1]:
        box('Workshop vertical shelf divider',(x,3.65,5.25),(.16,2.9,1.0),'Blue',.05)
    for x in [-9,-5.6,-2.2]:
        box('Workshop upper case',(x,5.5,5.15),(2.75,.85,.95),'Navy',.14)
        box('Workshop case white face',(x,5.49,4.65),(2.47,.60,.09),'Ivory',.11)
        box('Workshop case brass latch',(x,5.37,4.57),(.48,.09,.08),'Gold',.025)
    # Capsule ends, thick lip, framed drawer fronts and a central service mat.
    x,z=-3.7,.5
    box('Bench lower ivory bumper',(x,.35,z-1.16),(5.9,.20,.19),'Ivory',.09)
    box('Bench layered navy lip',(x,1.24,z),(6.65,.13,2.63),'Navy',.15)
    box('Bench central working mat',(x,1.59,z-.10),(4.7,.035,1.75),'Blue',.16)
    for dx in [-3.0,3.0]:
        box('Workbench ivory corner armour',(x+dx,.82,z-1.12),(.35,.82,.28),'Ivory',.13)
        box('Bench recessed amber lamp',(x+dx,1.33,z-1.38),(.31,.11,.055),'LightWarm',.035)
    # A folded cloth, mug badge, tool handles and an ammunition tray are readable at menu distance.
    box('Bench folded blue cloth',(-5.75,1.635,-.48),(.65,.04,1.60),'Blue',.025)
    box('Bench hanging blue cloth',(-5.75,1.30,-.805),(.65,.68,.045),'Blue',.025)
    chevrons('Cloth gold insignia',-5.75,1.28,-.837,.31,'Gold',-1)
    front_ring('Mug handle',(-6.36,1.83,-.08),.11,.035,'Ivory')
    chevrons('Mug small insignia',-6.15,1.83,-.275,.16,'Gold',-1)
    box('Ammunition service tray',(-1.12,1.72,-.05),(.75,.20,.68),'Navy',.08)
    for a in range(3):
        for b in range(2):
            xx=-1.36+a*.23;zz=-.20+b*.28
            cyl('Brass bench cartridge',(xx,1.99,zz),.065,.39,'Gold')
            bpy.ops.mesh.primitive_cone_add(vertices=12,radius1=.063,radius2=.012,depth=.11,location=V((xx,2.24,zz)))
            finish(bpy.context.object,'Bench cartridge rounded tip','Gold',.012)
    for a in range(3):
        xx=-4.5+a*.75;zz=-.38
        box('Bench precision driver grip',(xx,1.68,zz),(.15,.13,.43),'Gold',.04)
        box('Bench precision driver steel',(xx,1.68,zz+.38),(.045,.045,.43),'Ivory',.012)
    # Sculpted arm joints and a small work light, attached to the existing animated shoulder.
    shoulder=bpy.data.objects.get('ServiceShoulder')
    for xx,yy,zz,r in [(-2.4,5.3,2,.36),(-2.25,4.25,1.85,.30),(-1.32,3.57,1.3,.23)]:
        for dz in [-.34,.34]:
            o=front_ring('Service arm dark articulated joint',(xx,yy,zz+dz),r,.085,'Navy')
            if shoulder:parent_keep(o,shoulder)
            o=ball('Service arm brass pivot',(xx,yy,zz+dz-.03),(.12,.12,.045),'Gold')
            if shoulder:parent_keep(o,shoulder)
    for dx in [-.18,.18]:
        o=beam('Service arm clamp finger',(-1.32+dx,3.45,1.3),(-1.23+dx,3.12,1.22),.10,.16,'Ivory',.035)
        if shoulder:parent_keep(o,shoulder)
    o=cyl('Service downlight',(-2.75,4.05,1.12),.32,.17,'Navy')
    if shoulder:parent_keep(o,shoulder)
    o=cyl('Service warm light lens',(-2.75,3.95,1.12),.26,.035,'LightWarm')
    if shoulder:parent_keep(o,shoulder)
    # Architectural framing around the visible portal and workshop.
    for xx in [-10.1,-.15]:
        box('Workshop ivory wall rib',(xx,3.3,5.45),(.4,6.3,.7),'Ivory',.14)
        box('Workshop blue rib inset',(xx,3.5,5.05),(.16,4.8,.09),'Blue',.04)
    front_ring('Portal navy outer seam',(6.7,3.4,5.41),2.58,.13,'Navy')
    front_ring('Portal brass inner rim',(6.7,3.4,5.30),2.19,.07,'Gold')
    for angle in range(0,360,45):
        a=math.radians(angle)
        ball('Portal recessed bolt',(6.7+math.sin(a)*2.39,3.4+math.cos(a)*2.39,5.18),(.065,.065,.04),'Navy')
    box('Workshop upper fascia',(-3,6.25,4.8),(15,.32,1.0),'Blue',.11)
    box('Workshop upper warm strip',(-3,6.04,4.30),(14,.09,.06),'LightWarm',.03)

def polish_room(name):
    # Purpose-specific luminous materials leave the painted gold surfaces comfortably below bloom.
    for n,h in {'LightWarm':'#ffd36c','LightCyan':'#64eeff','HoloGlass':'#43d7f6','Deck':'#dde2e5'}.items():
        PALETTE[n]=h;m=bpy.data.materials.new('Living_'+n);m.diffuse_color=(*tuple(int(h[k:k+2],16)/255 for k in [1,3,5]),1);M[n]=m
    if name=='CommandShip':flight_deck();richer_sky();monitor_detail();table_detail();airlock_detail()
    else:workshop_detail()
    for o in objs:
        if o.name.startswith('Heavy command deck inset'):o.data.materials.clear();o.data.materials.append(M['Deck'])
    # Hidden superseded primitives must not remain in either the export or contact bake.
    for o in list(objs):
        if o.hide_render or (o.name.startswith('HoloIsland') and o.name.endswith(' terrain')):objs.remove(o);bpy.data.objects.remove(o,do_unlink=True)

def bake_vertex_contacts():
    """Short-range AO in vertex colours; no per-frame SSAO pass or extra runtime texture."""
    bpy.context.view_layer.update()
    vertices=[];polygons=[]
    candidates=[o for o in objs if o.type=='MESH' and not any(k in o.name.lower() for k in ['cloud','sky','sun','hologram projection volume'])]
    for o in candidates:
        offset=len(vertices);vertices.extend(o.matrix_world@v.co for v in o.data.vertices)
        polygons.extend(tuple(offset+i for i in p.vertices) for p in o.data.polygons)
    tree=BVHTree.FromPolygons(vertices,polygons,all_triangles=False,epsilon=.0001)
    rays=12;max_distance=1.15;total=0
    golden=math.pi*(3-math.sqrt(5))
    for o in objs:
        mesh=o.data
        if o.type!='MESH':continue
        if not mesh.color_attributes:mesh.color_attributes.new(name='LivingContact',type='FLOAT_COLOR',domain='POINT')
        attr=mesh.color_attributes.active_color
        if o not in candidates:
            for c in attr.data:c.color=(1,1,1,1)
            continue
        mat=o.matrix_world;normal_matrix=mat.to_3x3().inverted().transposed();ao=[]
        for v in mesh.vertices:
            p=mat@v.co;n=(normal_matrix@v.normal).normalized();t=n.cross(Vector((0,0,1)))
            if t.length<.01:t=n.cross(Vector((1,0,0)))
            t.normalize();b=n.cross(t);occlusion=0
            origin=p+n*.008
            for k in range(rays):
                z=math.sqrt((k+.5)/rays);r=math.sqrt(1-z*z);a=k*golden
                direction=n*z+t*(math.cos(a)*r)+b*(math.sin(a)*r)
                hit,_,_,distance=tree.ray_cast(origin,direction,max_distance)
                if hit is not None:occlusion+=(1-distance/max_distance)**.65
            ao.append(max(.38,1-occlusion/rays*.72))
        for c,value in zip(attr.data,ao):c.color=(value,value,value,1)
        total+=len(ao)
    print('Vertex contacts:',total,'vertices,',rays,'rays each',flush=True)
