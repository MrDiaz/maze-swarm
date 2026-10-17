"""Lightweight CPU data authoring only. Never calls a render or acquires a GPU lease."""
import bpy,json,hashlib,sys,math
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent;BASE=OUT.parent/'lavender-spitter-animated';sys.path.insert(0,str(OUT))
from family_geometry import warp,build_geometry
from family_animation import author
SPITTER=BASE/'lavender-spitter-animated_LOD0.glb';SPITTER_HASH='7bc01d38c339ad7d7e5cf4f7e67c19e2b6ccfb3d18533f015af5e3935c60a814'
assert hashlib.sha256(SPITTER.read_bytes()).hexdigest()==SPITTER_HASH
summaries=[]
roles=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['grunt','runner','carrier','carapace','tick']
for role in roles:
 bpy.ops.wm.open_mainfile(filepath=str(BASE/'lavender-spitter-authored.blend'))
 scene=bpy.context.scene;scene.render.threads_mode='FIXED';scene.render.threads=2
 rig=bpy.data.objects['LavenderSpitter_Rig'];mesh=bpy.data.objects['LavenderSpitter_LOD0_Skin']
 for o in list(scene.objects):
  if o not in (rig,mesh):bpy.data.objects.remove(o,do_unlink=True)
 rig.animation_data.action=None
 for tr in list(rig.animation_data.nla_tracks):rig.animation_data.nla_tracks.remove(tr)
 for a in list(bpy.data.actions):bpy.data.actions.remove(a)
 for pb in rig.pose.bones:pb.matrix_basis.identity()
 bpy.context.view_layer.update();rig.name='CargoFamily_'+role+'_Rig';mesh.name='CargoFamily_'+role+'_Skin'
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
 eb=rig.data.edit_bones
 if 'Muzzle'in eb:eb.remove(eb['Muzzle'])
 for bone in list(eb):
  bone.head=warp(bone.head,role);bone.tail=warp(bone.tail,role)
 if role in ('carrier','carapace'):
  for name,pos in [('CarrierBelly',(0,-.15,.35)),('CarrierPod.L',(.28,.235,.40)),('CarrierPod.R',(-.28,.235,.40)),('CarrierPod.Back',(0,.29,.55))]:
   bone=eb.new(name);bone.head=warp(Vector(pos),role);bone.tail=bone.head+Vector((0,0,.10));bone.parent=eb['Hips'];bone.use_deform=True
 bpy.ops.object.mode_set(mode='OBJECT')
 for bone in rig.data.bones:
  if bone.use_deform and bone.name not in mesh.vertex_groups:mesh.vertex_groups.new(name=bone.name)
 geometry,pods=build_geometry(mesh,rig,role,OUT)
 material=bpy.data.materials.new('CargoFamily_'+role+'_Matte');material.use_nodes=True
 tree=material.node_tree;p=tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.68;p.inputs['Metallic'].default_value=0
 tex=tree.nodes.new('ShaderNodeTexImage');im=bpy.data.images.load(str(OUT/role/(role+'-albedo.png')),check_existing=False);im.colorspace_settings.name='sRGB';im.pack();tex.image=im
 tree.links.new(tex.outputs['Color'],p.inputs['Base Color']);material.use_backface_culling=True;mesh.data.materials.clear();mesh.data.materials.append(material)
 for poly in mesh.data.polygons:poly.material_index=0
 motion,schedule=author(rig,role)
 folder=OUT/role;path=folder/('cargo-family-'+role+'.glb')
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
 args={'filepath':str(path),'export_format':'GLB','use_selection':True,'export_animations':True,'export_animation_mode':'ACTIONS','export_force_sampling':True,'export_frame_range':False,'export_skins':True,'export_all_influences':False,'export_def_bones':False,'export_anim_single_armature':True,'export_anim_slide_to_zero':True,'export_tangents':True,'export_normals':True,'export_materials':'EXPORT','export_yup':True}
 allowed=bpy.ops.export_scene.gltf.get_rna_type().properties.keys();bpy.ops.export_scene.gltf(**{k:v for k,v in args.items() if k in allowed})
 scene.frame_set(0);bpy.ops.wm.save_as_mainfile(filepath=str(folder/('cargo-family-'+role+'.blend')))
 points=[v.co for v in mesh.data.vertices];bounds=[[min(v[k] for v in points) for k in range(3)],[max(v[k] for v in points) for k in range(3)]]
 summary={'role':role,'asset':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'editableScene':str(folder/('cargo-family-'+role+'.blend')),'sourceSpitter':str(SPITTER),'sourceSpitterSha256Unchanged':SPITTER_HASH,'geometry':geometry,'boundsBlenderXYZ':bounds,'dimensionsGltfXYZ':[bounds[1][0]-bounds[0][0],bounds[1][2]-bounds[0][2],bounds[1][1]-bounds[0][1]],'skinBones':len(rig.data.bones),'motion':motion,'material':'One matte opaque PBR material, flat source-derived sRGB atlas; no normal/noise/emission maps','safeCarrierBellyContract':'CarrierBelly' if role in ('carrier','carapace') else None,'readyForCanonicalIntegration':False,'actualRenderedReview':'Pending explicit shared render lease from integration owner'}
 (folder/'asset.json').write_text(json.dumps(summary,indent=2));(folder/'contact-schedule.json').write_text(json.dumps(schedule))
 summaries.append(summary);print('CPU_FAMILY_ASSET',role,geometry['triangles'],motion['allLegTargetsReachable'],summary['dimensionsGltfXYZ'],flush=True)
assert hashlib.sha256(SPITTER.read_bytes()).hexdigest()==SPITTER_HASH
summaries=[json.loads((OUT/role/'asset.json').read_text()) for role in ['grunt','runner','carrier','carapace','tick']]
(OUT/'CPU-AUTHORING.json').write_text(json.dumps({'sourceSpitterUnchanged':True,'gpuRenderUsed':False,'sharedLeaseAcquired':False,'canonicalWrites':False,'assets':summaries},indent=2))
print('CPU_FAMILY_BUILD_COMPLETE',len(summaries),flush=True)
