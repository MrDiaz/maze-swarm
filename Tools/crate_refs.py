# Crate reference renders for image-to-3D (Meshy): each crate in the maps, built in Blender to the game's sizes and
# look (pale pine plank crate, darker frame, X braces, steel corner brackets), rendered on plain white from
# front / side / back / three-quarter / top. Headless:
#   Blender -b --factory-startup --python crate_refs.py -- <out dir>
import bpy, sys, math
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1:][0]
CRATES = [("crate_4x4", 4.0, 4.0, 1.4), ("crate_4x6", 6.0, 4.0, 1.4)]   # (name, length x, depth y, height) metres

def mat(name, base, rough=0.7, metal=0.0, grain=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if grain is None:
        b.inputs["Base Color"].default_value = (*base, 1)
    else:   # wood grain: stretched noise -> colour ramp between two tones
        tc = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = grain
        nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 6; nz.inputs["Detail"].default_value = 8
        cr = nt.nodes.new("ShaderNodeValToRGB")
        cr.color_ramp.elements[0].color = (*[c * 0.78 for c in base], 1); cr.color_ramp.elements[1].color = (*base, 1)
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], nz.inputs["Vector"])
        nt.links.new(nz.outputs["Fac"], cr.inputs["Fac"]); nt.links.new(cr.outputs["Color"], b.inputs["Base Color"])
    return m

def box(name, loc, size, m, bevel=0.012):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.object; o.name = name; o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    bpy.ops.object.transform_apply(scale=True)
    if bevel:
        bv = o.modifiers.new("b", "BEVEL"); bv.width = bevel; bv.segments = 2
    o.data.materials.append(m); return o

def beam(name, a, b, t, m):   # a square beam between two points
    a, b = Vector(a), Vector(b); d = b - a
    bpy.ops.mesh.primitive_cube_add(location=(a + b) / 2)
    o = bpy.context.object; o.name = name; o.scale = (d.length / 2, t / 2, t / 2)
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((1, 0, 0)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(scale=True, rotation=True)
    bv = o.modifiers.new("b", "BEVEL"); bv.width = 0.01; bv.segments = 2
    o.data.materials.append(m); return o

def crate(L, D, H):
    plank = mat("Plank", (0.86, 0.70, 0.47), 0.75, grain=(1, 8, 1))
    frame = mat("Frame", (0.62, 0.46, 0.28), 0.7, grain=(1, 6, 1))
    steel = mat("Steel", (0.32, 0.34, 0.37), 0.4, 0.85)
    t = 0.14   # frame thickness
    # body: planks running along the length on the sides and top, small gaps between them
    n = max(3, round(H / 0.32))
    ph = (H - 2 * t) / n
    for i in range(n):
        z = t + ph * (i + 0.5)
        for s in (-1, 1):
            box(f"PlankFB{i}{s}", (0, s * (D / 2 - 0.03), z), (L - 2 * t, 0.06, ph - 0.02), plank)
            box(f"PlankLR{i}{s}", (s * (L / 2 - 0.03), 0, z), (0.06, D - 2 * t, ph - 0.02), plank)
    m = max(4, round(D / 0.36))
    for i in range(m):
        y = -D / 2 + t + (D - 2 * t) / m * (i + 0.5)
        box(f"Top{i}", (0, y, H - 0.03), (L - 2 * t, (D - 2 * t) / m - 0.02, 0.06), plank)
    box("Core", (0, 0, H / 2), (L - 0.1, D - 0.1, H - 0.1), frame, 0)   # hidden inner fill
    # frame: the 12 edges
    xs, ys, zs = (-L / 2 + t / 2, L / 2 - t / 2), (-D / 2 + t / 2, D / 2 - t / 2), (t / 2, H - t / 2)
    for x in xs:
        for y in ys: beam("FrameV", (x, y, 0), (x, y, H), t, frame)
    for z in zs:
        for y in ys: beam("FrameX", (-L / 2, y, z), (L / 2, y, z), t, frame)
        for x in xs: beam("FrameY", (x, -D / 2, z), (x, D / 2, z), t, frame)
    # X braces on every side (on the long sides one X per ~2 m bay, with a centre post)
    def braces(axis, span, other, s):
        bays = max(1, round(span / 2.0)); w = span / bays
        for k in range(bays):
            a0 = -span / 2 + w * k; a1 = a0 + w
            for (p, q) in (((a0, t), (a1, H - t)), ((a0, H - t), (a1, t))):
                if axis == 'x': beam("Brace", (p[0], s * (other / 2 + 0.02), p[1]), (q[0], s * (other / 2 + 0.02), q[1]), t * 0.8, frame)
                else: beam("Brace", (s * (other / 2 + 0.02), p[0], p[1]), (s * (other / 2 + 0.02), q[0], q[1]), t * 0.8, frame)
            if k > 0:
                if axis == 'x': beam("Post", (a0, s * (other / 2 + 0.02), 0), (a0, s * (other / 2 + 0.02), H), t, frame)
                else: beam("Post", (s * (other / 2 + 0.02), a0, 0), (s * (other / 2 + 0.02), a0, H), t, frame)
    for s in (-1, 1): braces('x', L, D, s); braces('y', D, L, s)
    # steel corner brackets (L-shaped caps on the 8 corners) and a stencil plate on the front
    cb = 0.32
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (0, 1):
                zc = H - cb / 2 + 0.01 if z else cb / 2 - 0.01
                box("Bracket", (x * (L / 2 - cb / 2 + 0.02), y * (D / 2 + 0.005), zc), (cb, 0.02, cb), steel, 0.004)
                box("Bracket", (x * (L / 2 + 0.005), y * (D / 2 - cb / 2 + 0.02), zc), (0.02, cb, cb), steel, 0.004)
                box("Bracket", (x * (L / 2 - cb / 2 + 0.02), y * (D / 2 - cb / 2 + 0.02), H + 0.005 if z else -0.005), (cb, cb, 0.02), steel, 0.004)

def scene():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 1024
    sc.render.film_transparent = False
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    sc.view_settings.view_transform = 'Standard'
    for loc, e in (((6, -8, 9), 3.0), ((-8, 4, 6), 1.2)):
        l = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sc.collection.objects.link(l)
        l.data.energy = e; l.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'
    return sc, cam

for name, L, D, H in CRATES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc, cam = scene(); crate(L, D, H)
    target = Vector((0, 0, H / 2)); size = max(L, D) * 1.25
    views = {"front": (0, -1, 0.0), "side": (1, 0, 0.0), "back": (0, 1, 0.0), "three_quarter": (0.8, -1, 0.75), "top": (0, -0.0001, 1)}
    for v, d in views.items():
        dv = Vector(d).normalized(); cam.location = target + dv * 20
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = size * (1.05 if v in ("three_quarter", "top") else 1.0)
        sc.render.filepath = f"{out}/{name}_{v}.png"; bpy.ops.render.render(write_still=True)
    print("CRATE", name, L, D, H)
