# Leaper (Meshy image-to-3D, static, hunched biped with scythe arms, faces -Y, Z up) -> rigged glb with the clips its
# behaviour needs: Walk (loop), Crouch (0.6 s coil), Leap (0.45 s stretched), Land (0.8 s impact + stunned wobble),
# Sidestep (0.35 s). Custom skeleton fitted to the mesh (measured with slices), automatic weights on a voxel proxy in
# rest pose, then copied to the real mesh (as for the Iron Vanguard).
#   Blender -b --factory-startup --python leaper_rig.py -- <leaper.glb> <out.glb> [render dir]
import bpy, sys, math
from mathutils import Vector, Euler
argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[:2]; rdir = argv[2] if len(argv) > 2 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
body = next(o for o in bpy.data.objects if o.type == 'MESH'); body.name = "Leaper"
zmin = min((body.matrix_world @ v.co).z for v in body.data.vertices)
body.location.z -= zmin
bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
Z = -zmin   # the measurements below are in the original centred space: add Z to stand on the floor
def P(x, y, z): return Vector((x, y, z + Z))

# skeleton (head, tail, parent); L = +X side
B = {}
def bone(n, h, t, parent=None): B[n] = (h, t, parent)
bone("Hips", P(0, 0.05, -0.08), P(0, 0.0, 0.12))
bone("Spine", P(0, 0.0, 0.12), P(0, -0.08, 0.32), "Hips")
bone("Neck", P(0, -0.08, 0.32), P(0, -0.32, 0.44), "Spine")
bone("Head", P(0, -0.32, 0.44), P(0, -0.62, 0.48), "Neck")
for s, n in ((1, "L"), (-1, "R")):
    bone(f"Shoulder.{n}", P(s * 0.1, -0.05, 0.3), P(s * 0.3, -0.06, 0.24), "Spine")
    bone(f"Arm.{n}", P(s * 0.3, -0.06, 0.24), P(s * 0.55, -0.1, 0.12), f"Shoulder.{n}")
    bone(f"ForeArm.{n}", P(s * 0.55, -0.1, 0.12), P(s * 0.72, -0.1, 0.04), f"Arm.{n}")
    bone(f"Blade.{n}", P(s * 0.72, -0.1, 0.04), P(s * 0.92, 0.0, -0.75), f"ForeArm.{n}")
    bone(f"Thigh.{n}", P(s * 0.12, 0.05, -0.1), P(s * 0.27, -0.05, -0.42), "Hips")
    bone(f"Shin.{n}", P(s * 0.27, -0.05, -0.42), P(s * 0.34, 0.15, -0.76), f"Thigh.{n}")
    bone(f"Foot.{n}", P(s * 0.34, 0.15, -0.76), P(s * 0.48, 0.05, -0.88), f"Shin.{n}")
bpy.ops.object.armature_add(location=(0, 0, 0)); arm = bpy.context.object; arm.name = "LeaperRig"
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones; eb.remove(eb[0])
for n, (h, t, p) in B.items():
    b = eb.new(n); b.head, b.tail = h, t
for n, (h, t, p) in B.items():
    if p: eb[n].parent = eb[p]; eb[n].use_connect = (eb[p].tail - eb[n].head).length < 1e-4
bpy.ops.object.mode_set(mode='OBJECT')

# weights: bone heat on a closed voxel proxy (rest pose), copied onto the real mesh
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = "Proxy"; bpy.context.scene.collection.objects.link(proxy)
rm = proxy.modifiers.new("Remesh", 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.02
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = proxy; proxy.select_set(True)
bpy.ops.object.modifier_apply(modifier="Remesh")
bpy.ops.object.select_all(action='DESELECT'); proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print("PROXY", len(proxy.data.vertices), "verts,", sum(1 for v in proxy.data.vertices if not v.groups), "without weights")
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_NAME')
dt = body.modifiers.new("Weights", 'DATA_TRANSFER'); dt.object = proxy
dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.modifier_move_to_index(modifier="Weights", index=0)
bpy.ops.object.modifier_apply(modifier="Weights")
bpy.data.objects.remove(proxy)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
print("SKIN", len(body.data.vertices), "verts,", sum(1 for v in body.data.vertices if not v.groups), "without weights")

# ---- clips (30 fps). Rotations are given about the RIG's axes (X lateral, Y back, Z up; the Leaper faces -Y),
# relative to each bone's rest pose, so the numbers read naturally:
#   legs (pointing down): +X swings the foot back, -X forward;  spine (pointing up): +X leans forward
#   arms (pointing out, blades hanging): +X swings the blade back, -X forward; +Y lowers the left arm (mirrored on the right)
FPS = 30
pb = arm.pose.bones
for p in pb: p.rotation_mode = 'QUATERNION'
rest = {b.name: b.matrix_local.to_quaternion() for b in arm.data.bones}
def setrot(bn, deg):
    q = Euler([math.radians(c) for c in deg]).to_quaternion(); R = rest[bn]
    pb[bn].rotation_quaternion = R.inverted() @ q @ R
def clip(name, keys):
    a = bpy.data.actions.new(name); arm.animation_data_create(); arm.animation_data.action = a
    for f in sorted(keys):
        for p in pb: p.rotation_quaternion = (1, 0, 0, 0); p.location = (0, 0, 0)
        for bn, val in keys[f].items():
            if bn == "lift": pb["Hips"].location = (0, val, 0); continue   # the hips bone points up: local Y = up
            setrot(bn, val)
        for p in pb:
            p.keyframe_insert("rotation_quaternion", frame=f); p.keyframe_insert("location", frame=f)
    a.use_fake_user = True
    tr = arm.animation_data.nla_tracks.new(); tr.name = name; tr.strips.new(name, 0, a); tr.mute = True
    arm.animation_data.action = None
def sym(d):   # mirror the .L keys onto .R: X stays, Y and Z flip
    o = dict(d)
    for k, v in d.items():
        if k.endswith(".L") and k[:-2] + ".R" not in d: o[k[:-2] + ".R"] = (v[0], -v[1], -v[2])
    return o
W = {}
for i in range(9):   # Walk: 24 frames (0.8 s), 9 keys for a smooth loop
    f = i * 3; ph = f / 24 * 2 * math.pi
    sL, sR = math.sin(ph), math.sin(ph + math.pi)
    W[f] = {"lift": 0.035 * abs(math.cos(ph)), "Spine": (10, 5 * sL, 0), "Neck": (-6, 0, 0), "Head": (-4 * math.cos(2 * ph), 0, 0),
            "Thigh.L": (-28 * sL, 0, 0), "Shin.L": (32 * max(0, math.cos(ph)), 0, 0), "Foot.L": (-10 * sL, 0, 0),
            "Thigh.R": (-28 * sR, 0, 0), "Shin.R": (32 * max(0, -math.cos(ph)), 0, 0), "Foot.R": (-10 * sR, 0, 0),
            "Arm.L": (14 * sL, 0, 0), "Arm.R": (14 * sR, 0, 0), "Blade.L": (-8, 0, 0), "Blade.R": (-8, 0, 0)}
clip("Walk", W)
coil = {"lift": -0.17, "Spine": (28, 0, 0), "Neck": (-22, 0, 0), "Head": (-12, 0, 0),
        "Thigh.L": (-42, -6, 0), "Shin.L": (72, 0, 0), "Foot.L": (-28, 0, 0),
        "Arm.L": (35, -28, 0), "ForeArm.L": (20, 0, 0), "Blade.L": (35, 0, 0)}
clip("Crouch", {0: {}, 6: sym(coil), 18: sym({**coil, "lift": -0.19, "Spine": (32, 0, 0)})})
stretch = {"lift": 0.04, "Spine": (40, 0, 0), "Neck": (-25, 0, 0), "Head": (-8, 0, 0),
           "Thigh.L": (55, 0, 0), "Shin.L": (25, 0, 0), "Foot.L": (30, 0, 0),
           "Arm.L": (-75, -15, 0), "ForeArm.L": (-15, 0, 0), "Blade.L": (-30, 0, 0)}
clip("Leap", {0: sym(coil), 4: sym(stretch), 14: sym({**stretch, "Thigh.L": (-10, 0, 0), "Shin.L": (40, 0, 0), "Arm.L": (-45, 0, 0)})})
land = {"lift": -0.13, "Spine": (32, 0, 0), "Neck": (-15, 0, 0), "Thigh.L": (-35, -8, 0), "Shin.L": (58, 0, 0), "Foot.L": (-22, 0, 0),
        "Arm.L": (-25, 18, 0), "Blade.L": (-10, 0, 0)}
L = {0: sym(land)}
for k, f in enumerate((6, 10, 14, 18, 22)):   # stunned: the body and head sway side to side, decaying
    w = 12 * (0.72 ** k) * (1 if k % 2 == 0 else -1)
    L[f] = sym({**land, "lift": -0.11 + 0.02 * k, "Spine": (28 - 4 * k, w, 0), "Head": (0, -w * 1.4, 0)})
L[24] = {}
clip("Land", L)
side = {"lift": -0.05, "Spine": (10, 16, 0), "Thigh.L": (0, -18, 0), "Thigh.R": (0, -10, 0), "Shin.L": (20, 0, 0), "Shin.R": (20, 0, 0), "Arm.L": (0, -20, 0), "Arm.R": (0, -8, 0)}
clip("Sidestep", {0: {}, 4: side, 10: {}})

if rdir:
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 480; sc.render.resolution_y = 480
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.6
    for name, frames in (("REST", (0,)), ("Walk", (0, 6, 12, 18)), ("Crouch", (18,)), ("Leap", (4, 12)), ("Land", (0, 10)), ("Sidestep", (4,))):
        act = bpy.data.actions.get(name); arm.animation_data.action = act
        if act and hasattr(arm.animation_data, "action_slot") and act.slots: arm.animation_data.action_slot = act.slots[0]
        if name == "REST":
            for p in pb: p.rotation_quaternion = (1, 0, 0, 0); p.location = (0, 0, 0)
        for f in frames:
            sc.frame_set(f)
            for view, d in (("front", (0.3, -1, 0.15)), ("side", (1, -0.1, 0.1))):
                dv = Vector(d).normalized(); c = Vector((0, 0, 0.85)); cam.location = c + dv * 6
                cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
                sc.render.filepath = f"{rdir}/{name}_{f:02d}_{view}.png"; bpy.ops.render.render(write_still=True)
    arm.animation_data.action = None

for tr in arm.animation_data.nla_tracks: tr.mute = True
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True)
bpy.ops.export_scene.gltf(filepath=out, use_selection=True, export_animation_mode='NLA_TRACKS', export_skins=True, export_all_influences=False, export_yup=True)
print("WROTE", out)
