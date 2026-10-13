# Iron Vanguard auto-rig (Blender, headless): fit the Arctic Sentinel's Meshy/Mixamo skeleton to the Vanguard's
# T-pose by measured landmarks, skin his decimated mesh with automatic (bone heat) weights and export a glb that
# carries the Sentinel's clips (same mixamorig bone names, so Walking / Running / Run_and_Shoot play on him).
#   Blender -b --factory-startup --python vanguard_rig.py -- <hero.glb (Sentinel, 4 clips)> <vanguard.glb> <out.glb> [render dir]
import bpy, sys, bisect, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
hero, vang, out = argv[:3]; rdir = argv[3] if len(argv) > 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=hero)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for o in [o for o in bpy.data.objects if o.type == 'MESH']: bpy.data.objects.remove(o)   # the Sentinel's body (and a stray sphere)
bpy.ops.import_scene.gltf(filepath=vang)
body = next(o for o in bpy.data.objects if o.type == 'MESH')
body.name = "IronVanguard"
# stand him on the floor (Meshy centres the texture export on the origin)
zmin = min((body.matrix_world @ v.co).z for v in body.data.vertices)
body.location.z -= zmin
bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def lerp_table(tab, x):
    xs = [t[0] for t in tab]; i = max(1, min(len(tab) - 1, bisect.bisect_left(xs, x)))
    (x0, y0), (x1, y1) = tab[i - 1], tab[i]; return y0 + (y1 - y0) * (x - x0) / (x1 - x0)

# landmarks (metres, Blender Z up, character faces -Y), measured with bl_marks.py
S_Z = [(0.0, 0.0), (0.922, 0.76), (1.332, 1.33), (1.65, 1.706)]          # floor, hip joint, shoulder line, top of head
ARM_X = (0.163, 0.836, 0.28, 0.95)                                       # Sentinel shoulder-joint x, fingertip x -> Vanguard
V_ARM_Z = [(0.2, 1.335), (0.4, 1.33), (0.5, 1.317), (0.6, 1.306), (0.7, 1.285), (0.8, 1.278), (0.9, 1.254), (1.0, 1.24)]
V_ARM_Y = [(0.2, -0.06), (0.4, -0.11), (0.5, -0.122), (0.6, -0.17), (0.7, -0.21), (0.8, -0.25), (0.9, -0.27), (1.0, -0.28)]
K_X, K_Y, Y_CORE = 1.55, 1.5, -0.05
s_arm_z = lambda x: 1.332 + (1.295 - 1.332) * (x - 0.163) / (0.621 - 0.163)
ARMS = ("Shoulder", "Arm", "ForeArm", "Hand")
def is_arm(name): n = name.replace("mixamorig:", ""); return (n.startswith("Left") or n.startswith("Right")) and any(n[4 if n.startswith("Left") else 5:].startswith(a) for a in ARMS) and "Shoulder" not in n

def fit(p, arm_bone):
    x, y, z = p; s = 1 if x >= 0 else -1; ax = abs(x)
    if arm_bone:
        sx0, sx1, vx0, vx1 = ARM_X
        nx = vx0 + (ax - sx0) * (vx1 - vx0) / (sx1 - sx0)
        nz = lerp_table(V_ARM_Z, nx) + (z - s_arm_z(ax))
        ny = lerp_table(V_ARM_Y, nx) + (y - 0.03)
        return Vector((s * nx, ny, nz))
    return Vector((x * K_X, (y - 0.0) * K_Y + Y_CORE, lerp_table(S_Z, z)))

# the clips key every bone's location in Sentinel proportions: keep rotations only (plus the Hips' motion)
def fcurves(a):
    if hasattr(a, "fcurves") and len(getattr(a, "fcurves", [])): return list(a.fcurves), a.fcurves
    from bpy_extras import anim_utils
    cb = anim_utils.action_get_channelbag_for_slot(a, a.slots[0]); return list(cb.fcurves), cb.fcurves
for a in bpy.data.actions:
    lst, coll = fcurves(a); dropped = 0
    for fc in lst:
        if fc.data_path.endswith(".location") and '"mixamorig:Hips"' not in fc.data_path: coll.remove(fc); dropped += 1
    print("CLIP", a.name, "dropped", dropped, "location curves")

bpy.context.view_layer.objects.active = arm; bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
# move and scale the joints, but keep every bone's direction and roll exactly as the Sentinel's, so the clips'
# local rotations mean the same thing on him (re-aiming the bones bends the poses)
new = {}
for b in eb:
    h, t = fit(b.head, is_arm(b.name)), fit(b.tail, is_arm(b.name))
    new[b.name] = (h, h + (b.tail - b.head).normalized() * max((t - h).length, 0.01), b.roll)
for b in eb: b.use_connect = False
for b in eb: b.head, b.tail, b.roll = new[b.name]
bpy.ops.object.mode_set(mode='OBJECT')
for b in arm.data.bones: print("FIT", b.name, [round(c, 3) for c in b.head_local])

# skin: automatic weights (bone heat); helper bones don't deform
for b in arm.data.bones:
    if b.name in ("headfront",) or b.name.endswith("_End") or b.name.endswith("4"): b.use_deform = False
# weights are computed against the CURRENT pose: put the skeleton in its rest (T) pose first (the importer leaves a clip active)
arm.animation_data.action = None
for tr in list(arm.animation_data.nla_tracks): arm.animation_data.nla_tracks.remove(tr)
arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
# bone heat needs a closed surface: weight a voxel-remeshed proxy, then copy the weights onto the real mesh
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = "Proxy"; bpy.context.scene.collection.objects.link(proxy)
rm = proxy.modifiers.new("Remesh", 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.025
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = proxy; proxy.select_set(True)
bpy.ops.object.modifier_apply(modifier="Remesh")
print("PROXY", len(proxy.data.vertices), "verts")
bpy.ops.object.select_all(action='DESELECT'); proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print("PROXY SKIN", sum(1 for v in proxy.data.vertices if not v.groups), "without weights")
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_NAME')   # armature modifier + empty groups
dt = body.modifiers.new("Weights", 'DATA_TRANSFER'); dt.object = proxy
dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.modifier_move_to_index(modifier="Weights", index=0)
bpy.ops.object.modifier_apply(modifier="Weights")
bpy.data.objects.remove(proxy)
unw = sum(1 for v in body.data.vertices if not any(g.weight > 0.001 for g in v.groups))
print("SKIN", len(body.data.vertices), "verts,", unw, "without weights,", len(body.vertex_groups), "groups")
if unw:   # bone heat failed somewhere: give those vertices to the nearest deforming bone segment
    deform = [b for b in arm.data.bones if b.use_deform]
    def seg_d(p, a, b):
        ab = b - a; t = max(0, min(1, (p - a).dot(ab) / max(ab.length_squared, 1e-9))); return (a + ab * t - p).length
    for v in body.data.vertices:
        if any(g.weight > 0.001 for g in v.groups): continue
        best = min(deform, key=lambda b: seg_d(v.co, b.head_local, b.tail_local))
        vg = body.vertex_groups.get(best.name) or body.vertex_groups.new(name=best.name)
        vg.add([v.index], 1.0, 'REPLACE')
arm.data.pose_position = 'POSE'
# glTF: up to 4 influences per vertex, normalized
bpy.context.view_layer.objects.active = body
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)

# keep every clip as its own glTF animation
arm.animation_data_create()
for tr in list(arm.animation_data.nla_tracks): arm.animation_data.nla_tracks.remove(tr)   # the importer's (unmuted) tracks
for a in bpy.data.actions:
    tr = arm.animation_data.nla_tracks.new(); tr.name = a.name; tr.strips.new(a.name, int(a.frame_range[0]), a)
    tr.mute = True
arm.animation_data.action = None

if rdir:   # deformation check: Workbench renders, front and side, a few frames of each clip
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 480; sc.render.resolution_y = 640
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    for clip, frames in (("REST", (0,)), ("Walking", (0, 6, 12, 18)), ("Run_and_Shoot", (0, 8)), ("Running", (4,))):
        arm.animation_data.action = bpy.data.actions.get(clip)
        if clip == "REST":
            for pb in arm.pose.bones: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
        if arm.animation_data.action is not None and hasattr(arm.animation_data, "action_slot") and arm.animation_data.action.slots:
            arm.animation_data.action_slot = arm.animation_data.action.slots[0]
        for f in frames:
            sc.frame_set(f)
            pb = arm.pose.bones["mixamorig:LeftUpLeg"]
            print("POSE", clip, f, getattr(arm.animation_data, "action_slot", None), [round(c, 3) for c in pb.rotation_quaternion])
            for view, loc in (("front", (0.6, -3.4, 1.15)), ("side", (3.4, -0.4, 1.15))):
                cam.location = loc
                d = Vector((0, 0, 0.9)) - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
                sc.render.filepath = f"{rdir}/{clip}_{f:02d}_{view}.png"; bpy.ops.render.render(write_still=True)
    arm.animation_data.action = None
    sc.frame_set(0)

bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True)
bpy.ops.export_scene.gltf(filepath=out, use_selection=True, export_animation_mode='NLA_TRACKS', export_skins=True,
                          export_all_influences=False, export_image_format='AUTO', export_yup=True)
print("WROTE", out)
