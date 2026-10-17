# Chibi Iron Vanguard, built by script (2026-10-16, Manuel: "Blender rebuild"; no generation, no credits).
#   blender -b --factory-startup --python build_vanguard_chibi.py -- <hero_vanguard_meshy.glb> <outdir>
# 1. the game's Meshy rig + its clips (Running / Walking / run_fast_3_inplace / Dead) imported, the old mesh dropped
# 2. the skeleton re-proportioned to chibi: every bone keeps its direction (so the clips' rotations pose it the same),
#    lengths scaled: legs ~0.6, spine ~0.7, arms ~0.6, a much bigger head; the hips' keyed motion scaled to match
# 3. a new body from a few big rounded parts, each rigidly skinned to one bone (toy-like, no stretching seams):
#    big bald head + simple oval eyes, stern brows, a rounded dark beard; navy suit; white chest plate, shoulder pads,
#    gauntlets, knee pads and chunky boots; a small backpack with two blue vents; blue glow accents
# 4. flat colours: one 2048 swatch map (VanguardChibiMat_base.png), every part's UVs on its swatch
# 5. a stubby rounded minigun (separate glb) with a "Barrels" child that spins about its own X
# Saves vanguard_chibi.blend, hero_vanguard_chibi.glb, minigun_chibi.glb in <outdir>.
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; SRC, OUT = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
for o in list(bpy.context.scene.objects):
    if o.type == "MESH": bpy.data.objects.remove(o, do_unlink=True)
arm.name = "VanguardChibiRig"
P = "smartrig:"

# ---------------------------------------------------------------- 2. re-proportion
# per bone: (offset scale for its children's heads as xyz, own length scale)
def rule(n):
    n = n.replace(P, "")
    if n == "Hips": return Vector((0.9, 1, 0.5)), 0.6
    if n in ("Spine", "Spine1"): return Vector((1, 0.7, 0.7)), 0.7
    if n == "Spine2": return Vector((1.1, 0.8, 0.7)), 0.7
    if n.endswith("Shoulder"): return Vector((0.78, 1, 1)), 0.78
    if n.endswith("UpLeg"): return Vector((0.6, 0.6, 0.6)), 0.6
    if n.endswith("Leg"): return Vector((0.55, 0.55, 0.55)), 0.55
    if n.endswith("Foot"): return Vector((1, 0.9, 0.8)), 0.85
    if n.endswith("Arm") or n.endswith("ForeArm"): return Vector((0.6, 0.6, 0.6)), 0.6
    if n == "Neck": return Vector((1, 1, 0.5)), 0.5
    if n == "Head": return Vector((1.3, 1.3, 1.75)), 1.75
    if "Hand" in n: return Vector((0.8, 0.8, 0.8)), 0.8
    return Vector((1, 1, 1)), 1.0
bones = arm.data.bones
old = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in bones}
new = {}
HIPS_Z = 0.47
def place(b):
    h, t = old[b.name]
    if b.parent is None: nh = Vector((h.x, h.y * 0.8, HIPS_Z))
    else:
        ph = old[b.parent.name][0]; s, _ = rule(b.parent.name)
        d = h - ph; nh = new[b.parent.name][0] + Vector((d.x * s.x, d.y * s.y, d.z * s.z))
    _, k = rule(b.name); new[b.name] = (nh, nh + (t - h) * k)
    for c in b.children: place(c)
place(next(b for b in bones if b.parent is None))
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode="EDIT")
for eb in arm.data.edit_bones:
    roll = eb.roll; eb.head, eb.tail = new[eb.name]; eb.roll = roll
bpy.ops.object.mode_set(mode="OBJECT")
def fcurves(act):
    try: return list(act.fcurves)
    except Exception: pass
    out = []
    for L in act.layers:
        for st in L.strips:
            for cb in st.channelbags: out += list(cb.fcurves)
    return out
for act in bpy.data.actions:
    for fc in fcurves(act):
        if fc.data_path == f'pose.bones["{P}Hips"].location':
            for kp in fc.keyframe_points: kp.co.y *= 0.6; kp.handle_left.y *= 0.6; kp.handle_right.y *= 0.6
B = lambda n: new[P + n]
print("RIG hips", round(B("Hips")[0].z, 3), "neck", round(B("Neck")[0].z, 3), "head", round(B("Head")[0].z, 3), "knee", round(B("LeftLeg")[0].z, 3), "ankle", round(B("LeftFoot")[0].z, 3), "hand", round(B("LeftHand")[0].z, 3))

# ---------------------------------------------------------------- 4. palette swatches
PAL = {"white": (0.93, 0.95, 0.98), "navy": (0.11, 0.19, 0.42), "dark": (0.17, 0.18, 0.23), "glow": (0.32, 0.86, 1.0),
       "skin": (0.96, 0.74, 0.6), "beard": (0.14, 0.1, 0.09), "eye": (0.07, 0.07, 0.11), "gold": (1.0, 0.76, 0.2),
       "grey": (0.6, 0.65, 0.72), "shine": (1, 1, 1)}
NAMES = list(PAL); G = 4
def swatch_uv(name):
    i = NAMES.index(name); return ((i % G) + 0.5) / G, 1 - ((i // G) + 0.5) / G
def bake_swatch_png(path):
    S = 2048; img = bpy.data.images.new("VanguardChibiMat_base", S, S, alpha=False)
    import numpy as np
    px = np.zeros((S, S, 4), np.float32); px[..., 3] = 1; c = S // G
    for i, n in enumerate(NAMES):
        x, y = i % G, G - 1 - i // G
        px[y * c:(y + 1) * c, x * c:(x + 1) * c, :3] = PAL[n]   # sRGB-ish values written straight (the map is read as sRGB colour)
    img.pixels.foreach_set(px.ravel()); img.filepath_raw = path; img.file_format = "PNG"; img.save(); return img

def material(name, img):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"); tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = "Closest"
    nt.links.new(tx.outputs["Color"], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.7
    try: bsdf.inputs["Specular IOR Level"].default_value = 0.2
    except Exception: pass
    return m

# ---------------------------------------------------------------- 3. parts
parts = []
def finish(o, colour, bone, smooth=True, sub=0):
    if sub:
        md = o.modifiers.new("s", "SUBSURF"); md.levels = sub; md.render_levels = sub
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="s")
    me = o.data
    if not me.uv_layers: me.uv_layers.new()
    u, v = swatch_uv(colour)
    for l in me.uv_layers.active.data: l.uv = (u, v)
    for p in me.polygons: p.use_smooth = smooth
    vg = o.vertex_groups.new(name=P + bone); vg.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    parts.append(o); return o
def xform(o, loc, rot=None, scale=None):
    M = Matrix.Translation(loc) @ (rot.to_matrix().to_4x4() if rot is not None else Matrix.Identity(4))
    if scale is not None: M = M @ Matrix.Diagonal((*scale, 1))
    o.data.transform(M); o.location = (0, 0, 0)
def sphere(loc, r, colour, bone, scale=(1, 1, 1), rot=None, seg=32, ring=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=ring, radius=r); o = bpy.context.object
    xform(o, loc, rot, scale); return finish(o, colour, bone)
def rbox(loc, size, colour, bone, rot=None, bevel=0.35):
    bpy.ops.mesh.primitive_cube_add(size=1); o = bpy.context.object
    md = o.modifiers.new("b", "BEVEL"); md.width = min(size) * bevel; md.segments = 4; md.limit_method = "NONE"
    xform(o, Vector((0, 0, 0)), None, size)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="b")
    xform(o, loc, rot); return finish(o, colour, bone, sub=1)
def capsule(p0, p1, r, colour, bone, r1=None):
    r1 = r if r1 is None else r1; d = p1 - p0; L = d.length
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=1, depth=1, end_fill_type="NOTHING"); o = bpy.context.object   # (open: the end spheres close it; flat caps z-fought them)
    me = o.data
    for vtx in me.vertices:
        top = vtx.co.z > 0; rr = r1 if top else r; vtx.co.x *= rr; vtx.co.y *= rr; vtx.co.z = L if top else 0
    bm = bmesh.new(); bm.from_mesh(me)
    # round the caps: add hemispheres
    bm.to_mesh(me); bm.free()
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()); xform(o, p0, rot)
    finish(o, colour, bone)
    sphere(p0, r, colour, bone, seg=24, ring=12); sphere(p1, r1, colour, bone, seg=24, ring=12)
def disc(loc, normal, r, colour, bone, depth=0.012):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=depth); o = bpy.context.object
    xform(o, loc, Vector((0, 0, 1)).rotation_difference(normal.normalized())); return finish(o, colour, bone, smooth=False)

FWD = Vector((0, -1, 0))
for side, sx in (("Left", 1), ("Right", -1)):
    arm_h, arm_t = B(side + "Arm"); fa_h, fa_t = B(side + "ForeArm"); hd_h, hd_t = B(side + "Hand")
    ul_h, ul_t = B(side + "UpLeg"); lg_h, lg_t = B(side + "Leg"); ft_h, ft_t = B(side + "Foot"); toe = B(side + "ToeBase")[1]
    # shoulder pad (white dome) with a navy rim and a gold chevron dot
    pad = arm_h + Vector((0.03 * sx, 0, 0.02))
    sphere(pad, 0.135, "white", side + "Arm", scale=(1.05, 1.0, 0.85))
    disc(pad + Vector((0.11 * sx, -0.02, 0.0)), Vector((sx, -0.25, 0.1)), 0.045, "gold", side + "Arm")
    capsule(arm_h, arm_t, 0.06, "navy", side + "Arm")
    capsule(fa_h + (fa_t - fa_h) * 0.15, fa_t, 0.085, "white", side + "ForeArm", 0.09)
    disc(fa_t + (fa_h - fa_t) * 0.35 + Vector((0.0, -0.085, 0)), Vector((0, -1, 0)), 0.03, "glow", side + "ForeArm")
    sphere(hd_h + (hd_t - hd_h) * 0.6, 0.08, "dark", side + "Hand", scale=(1, 1, 1.1))
    capsule(ul_h, ul_t, 0.095, "navy", side + "UpLeg", 0.085)
    sphere(lg_h + Vector((0, -0.03, 0.01)), 0.085, "white", side + "Leg", scale=(1, 0.9, 0.95))
    disc(lg_h + Vector((0, -0.105, 0.01)), FWD, 0.035, "glow", side + "Leg")
    capsule(lg_h + (lg_t - lg_h) * 0.2, lg_t + Vector((0, 0, 0.02)), 0.095, "white", side + "Leg", 0.11)
    # chunky boot: a rounded block under the ankle reaching to the toes, a dark sole
    mid = (ft_h + toe) * 0.5; boot = Vector((mid.x, mid.y - 0.01, 0.075))
    rbox(boot, (0.19, 0.3, 0.13), "white", side + "Foot", rot=Vector((0, -1, 0)).rotation_difference(Vector((toe.x - ft_h.x, toe.y - ft_h.y, 0)).normalized()), bevel=0.45)
    rbox(Vector((boot.x, boot.y, 0.035)), (0.2, 0.31, 0.07), "dark", side + "Foot", rot=Vector((0, -1, 0)).rotation_difference(Vector((toe.x - ft_h.x, toe.y - ft_h.y, 0)).normalized()), bevel=0.45)

hips_h = B("Hips")[0]; sp1 = B("Spine1")[0]; sp2 = B("Spine2")[0]; neck = B("Neck")[0]; head_h, head_t = B("Head")
rbox(Vector((0, hips_h.y, hips_h.z - 0.015)), (0.36, 0.26, 0.17), "navy", "Hips", bevel=0.45)          # shorts
rbox(Vector((0, hips_h.y - 0.02, hips_h.z + 0.055)), (0.38, 0.28, 0.06), "dark", "Hips", bevel=0.45)    # belt
disc(Vector((0, hips_h.y - 0.165, hips_h.z + 0.055)), FWD, 0.035, "gold", "Hips")                          # buckle
rbox(Vector((0, sp1.y, sp1.z + 0.01)), (0.38, 0.27, 0.2), "navy", "Spine1", bevel=0.45)                    # suit torso
chest = Vector((0, sp2.y - 0.035, sp2.z + 0.02))
rbox(chest, (0.48, 0.32, 0.25), "white", "Spine2", bevel=0.45)                                             # chest plate
disc(chest + Vector((0, -0.18, 0.0)), FWD, 0.05, "glow", "Spine2")                                         # core
disc(chest + Vector((0, -0.168, 0.0)), FWD, 0.07, "navy", "Spine2", depth=0.012)
rbox(chest + Vector((0, 0.2, 0.02)), (0.32, 0.15, 0.27), "white", "Spine2", bevel=0.45)                   # backpack
for vx in (-0.07, 0.07): rbox(chest + Vector((vx, 0.28, 0.02)), (0.07, 0.03, 0.17), "glow", "Spine2", bevel=0.45)
capsule(neck, head_h + Vector((0, 0, 0.02)), 0.07, "navy", "Neck")
# head: big round bald head, ears, eyes, brows, beard
HR = 0.3; hc = head_h + Vector((0, -0.01, HR * 0.95))
sphere(hc, HR, "skin", "Head", scale=(1.04, 0.98, 0.97), seg=48, ring=24)
for sx in (1, -1):
    sphere(hc + Vector((HR * 1.0 * sx, 0.02, -0.03)), 0.06, "skin", "Head", scale=(0.6, 1, 1.1))
    e = hc + Vector((0.115 * sx, -HR * 0.92, 0.0))
    sphere(e, 0.046, "eye", "Head", scale=(0.85, 0.45, 1.3))
    sphere(e + Vector((0.012 * sx, -0.017, 0.022)), 0.011, "shine", "Head")
    brow = hc + Vector((0.12 * sx, -HR * 0.9, 0.095))
    rbox(brow, (0.11, 0.035, 0.032), "beard", "Head", rot=Matrix.Rotation(math.radians(-14 * sx), 4, "Y").to_quaternion() @ Matrix.Rotation(math.radians(-8), 4, "X").to_quaternion(), bevel=0.45)
# beard: a thin shell just outside the head, each half cut by its own tilted plane (low at the chin, rising along the
# cheek to a sideburn at the ear) and trimmed behind the ears: a clean U-shaped beard line
for sx in (1, -1):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=HR * 1.025); bd = bpy.context.object
    xform(bd, hc, None, (1.04, 0.98, 0.97))
    bm = bmesh.new(); bm.from_mesh(bd.data)
    g = lambda: bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=g(), plane_co=hc, plane_no=Vector((-sx, 0, 0)), clear_outer=True)       # this half only
    p1 = hc + Vector((0, -HR, -0.12)); p2 = hc + Vector((HR * sx, -0.02, 0.0)); p3 = hc + Vector((0, HR, -0.02))
    n = (p2 - p1).cross(p3 - p1).normalized()
    if n.z < 0: n = -n
    bmesh.ops.bisect_plane(bm, geom=g(), plane_co=p1, plane_no=n, clear_outer=True)                          # keep below the beard line
    bmesh.ops.bisect_plane(bm, geom=g(), plane_co=hc + Vector((0, 0.05, 0)), plane_no=Vector((0, 1, 0)), clear_outer=True)   # not behind the ears
    bm.to_mesh(bd.data); bm.free(); finish(bd, "beard", "Head")
# mouth hint: none (chibi). Join everything into one mesh
bpy.ops.object.select_all(action="DESELECT")
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join(); body = bpy.context.object; body.name = "VanguardChibi"
os.makedirs(OUT, exist_ok=True)
img = bake_swatch_png(os.path.join(OUT, "VanguardChibiMat_base.png"))
mat = material("VanguardChibiMat", img); body.data.materials.clear(); body.data.materials.append(mat)
body.parent = arm; md = body.modifiers.new("Armature", "ARMATURE"); md.object = arm
tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
print("BODY tris", tris, "verts", len(body.data.vertices))

# ---------------------------------------------------------------- 5. minigun (own scene objects, exported separately)
gparts = []
def gfin(o, colour, smooth=True):
    me = o.data
    if not me.uv_layers: me.uv_layers.new()
    u, v = swatch_uv(colour)
    for l in me.uv_layers.active.data: l.uv = (u, v)
    for p in me.polygons: p.use_smooth = smooth
    gparts.append(o); return o
def gbox(loc, size, colour, bevel=0.4):
    bpy.ops.mesh.primitive_cube_add(size=1); o = bpy.context.object
    md = o.modifiers.new("b", "BEVEL"); md.width = min(size) * bevel; md.segments = 4; md.limit_method = "NONE"
    xform(o, Vector((0, 0, 0)), None, size); bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="b")
    md = o.modifiers.new("s", "SUBSURF"); md.levels = 1; bpy.ops.object.modifier_apply(modifier="s")
    xform(o, loc); return gfin(o, colour)
def gcyl(x0, x1, r, colour, y=0.0, z=0.0, verts=28):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=x1 - x0); o = bpy.context.object
    xform(o, Vector(((x0 + x1) / 2, y, z)), Vector((0, 0, 1)).rotation_difference(Vector((1, 0, 0))))
    md = o.modifiers.new("b", "BEVEL"); md.width = min(r * 0.3, (x1 - x0) * 0.3); md.segments = 3; md.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="b"); return gfin(o, colour)
gbox(Vector((0.08, 0, 0)), (0.46, 0.22, 0.24), "dark")                 # receiver
gbox(Vector((0.05, 0, 0.16)), (0.26, 0.07, 0.06), "white")             # carry handle
gbox(Vector((-0.04, 0, -0.14)), (0.08, 0.07, 0.16), "dark")            # rear grip
gbox(Vector((0.1, 0.0, -0.15)), (0.16, 0.2, 0.12), "gold")             # ammo box
gcyl(0.28, 0.5, 0.15, "white")                                          # front housing
gcyl(0.46, 0.49, 0.155, "glow")                                         # glow ring
body_g = gparts[:]
gparts.clear()
for i in range(6):
    ang = i * math.pi / 3; gcyl(0.48, 0.92, 0.03, "grey", y=0.075 * math.cos(ang), z=0.075 * math.sin(ang), verts=16)
gcyl(0.48, 0.92, 0.045, "dark", verts=20)                               # hub
gcyl(0.86, 0.9, 0.115, "white")                                         # muzzle ring
bar = gparts[:]
bpy.ops.object.select_all(action="DESELECT")
for o in bar: o.select_set(True)
bpy.context.view_layer.objects.active = bar[0]; bpy.ops.object.join(); barrels = bpy.context.object; barrels.name = "Barrels"
bpy.ops.object.select_all(action="DESELECT")
for o in body_g: o.select_set(True)
bpy.context.view_layer.objects.active = body_g[0]; bpy.ops.object.join(); gun = bpy.context.object; gun.name = "MinigunChibi"
gmat = material("MinigunChibiMat", img); gun.data.materials.clear(); gun.data.materials.append(gmat); barrels.data.materials.clear(); barrels.data.materials.append(gmat)
# barrels: origin on the spin axis, parented to the gun
cx = 0.7; barrels.data.transform(Matrix.Translation((-cx, 0, 0))); barrels.location = (cx, 0, 0); barrels.parent = gun
gtris = sum(len(p.vertices) - 2 for o in (gun, barrels) for p in o.data.polygons)
print("GUN tris", gtris)
gun.location = (0, 0, -5)   # out of the way in the .blend (exported at the origin)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "vanguard_chibi.blend"))

# ---------------------------------------------------------------- export
def export(objs, path, anim):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    kw = dict(filepath=path, use_selection=True, export_format="GLB", export_yup=True, export_apply=False, export_animations=anim)
    if anim: kw.update(export_animation_mode="ACTIONS", export_force_sampling=True)
    try: bpy.ops.export_scene.gltf(**kw)
    except TypeError as e: print("export kw", e); kw.pop("export_animation_mode", None); bpy.ops.export_scene.gltf(**kw)
export([arm, body], os.path.join(OUT, "hero_vanguard_chibi.glb"), True)
gun.location = (0, 0, 0)
export([gun, barrels], os.path.join(OUT, "minigun_chibi.glb"), False)
gun.location = (0, 0, -5)
print("DONE")
