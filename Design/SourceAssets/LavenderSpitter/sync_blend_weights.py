import bpy,json
from pathlib import Path
OUT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lavender-spitter-authored.blend'))
mesh=bpy.data.objects['LavenderSpitter_LOD0_Skin'];weights=json.loads((OUT/'refined-skin-weights.json').read_text())
assert len(weights)==len(mesh.data.vertices)
for group in mesh.vertex_groups:group.remove(range(len(mesh.data.vertices)))
for i,w in enumerate(weights):
 for n,a in w.items():mesh.vertex_groups[n].add([i],a,'REPLACE')
rig=bpy.data.objects['LavenderSpitter_Rig'];rig['UV_seam_skin_weights_welded']=True
rig.animation_data.action=bpy.data.actions['Idle'];bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lavender-spitter-authored.blend'))
print('EDITABLE_BLEND_SKIN_SYNCED',len(weights),flush=True)
