LAVENDER SPITTER — APPROVED AUTHORED ANIMATION STAGING

Approved for the canonical owner to import and validate in game. This worker did
not launch Unity/Xcode, write game files, run inference, or contact Claude.
The final CPU lease was released at 2026-10-16 23:32:04.877 UTC.

USE THESE FILES
  lavender-spitter-animated_LOD0.glb
  SHA256 7bc01d38c339ad7d7e5cf4f7e67c19e2b6ccfb3d18533f015af5e3935c60a814
  lavender-spitter-authored.blend — editable rig, packed images, baked actions,
  neutral review camera/lights. REVIEW_ONLY objects are absent from the GLB.
  INTEGRATION.json — dimensions, clip timing, bounds, evidence and limitations.

SOURCE AND PRESERVATION
Only the approved mobile-006 LOD0 from:
  /Volumes/T7 Shield/Assets/Enemies/Lavender Spitter 2026-10-16
was used. All three selected original GLBs still match their starting hashes.
The exported rest positions have zero measured difference from the source.
All embedded albedo, normal and material map pixels match the source exactly.
No topology, UV, material design or texture repaint was introduced by this rig.
Original local Hunyuan provenance/license remain in the source delivery folder.

RIG AND CLIPS
4,000 triangles; 3,347 vertices including UV copies; one opaque PBR material;
15 bones, 13 deforming; four normalized influences maximum. Metres, +Y up,
+Z forward, common floor pivot, original rest height 1.000005 m.

Root > Hips > Spine > Head > Muzzle
Spine also parents UpperArm.L/R > Forearm.L/R.
Hips parents Thigh.L/R > Shin.L/R > Foot.L/R.

Idle: 2.0 s, 30 Hz baked keys, looping. Subtle articulated breathing.
Walk: 0.8 s, 30 Hz baked keys, looping. 0.28 m travel per cycle, 0.35 m/s native
      ground speed, 56% stance per foot, 45 mm maximum foot lift. Deliberate
      forward/back foot trajectories, double support and restrained arm motion.
Spit: 0.8 s, 30 Hz baked keys, one shot. Gather/brace, release at 11/30 s =
      0.366667 s, then recovery. The approved closed cavity is preserved; this
      is head/torso/arm motion, not an invented jaw-opening animation.

The keys are baked local FK. Runtime IK, constraints, scaling or a procedural
deformation controller are unnecessary. There is no root motion. Every bone
scale is one apart from floating-point decomposition noise below 3e-7.

RUNTIME INTEGRATION
Use generic/Legacy playback, Bone4 skin quality and source PBR colors. Loop Idle
and Walk; clamp Spit. Avoid applying the old roster tint or generic model lunge
to this character. A Spine mixing mask recursively includes head and arms while
excluding the Hips/leg branch, so upper-body Spit can layer over locomotion.

Correct rate: clip.speed = signed ground speed / (0.35 * total uniform world
model scale). Existing Swarm's stride denominator must be 0.35 for this clip,
not 0.28. Reverse phase for retreat while facing the target. At a 4x rate cap,
movement above 1.4 * worldScale m/s necessarily slides; do not hide this with
whole-body scaling. Idle is available when movement stops.

Align the Muzzle release pose to the existing projectile launch by beginning
anticipation 0.366667 s before it. Starting the clip at an immediate launch
puts anticipation after the shot. The canonical owner retains responsibility
for combat timing, cast interruption, freeze/thaw, death and pool reset.

Sampled animated mesh bounds in glTF model coordinates (X,Y-up,Z):
  min (-0.499093, -0.000000010, -0.503254)
  max ( 0.532859,  1.000926,     0.418072)
Use a small margin such as 0.02 m when setting skinned culling bounds. Gameplay
collision remains the canonical grid/circle; this staging adds no collider.

VERIFICATION AND REVIEW
glb-audit.json: exact rest geometry, one material/primitive, three embedded
maps, 15 joints, four weights, named clips/durations, no external file URIs.
independent-skin-audit.json: independent glTF animation/inverse-bind evaluation
of all 111 baked key samples; exactly zero UV seam separation; foot target and
stance errors below 0.00000015 m; loop closure zero within 1e-15 m; source maps
pixel-identical. Fixed UV copies share the same skin weights.
export-review-final.json: Blender reimport of the actual delivered GLB; all
three named actions retained; 77 real 480x480 CPU render frames captured.
Quarter, side and back contact sheets were inspected for joint/neck/belly
continuity, foot contact, lift, anticipation/release and recovery. Final sheets
show no visible UV seam or previous belly spike. Walk and Spit GIFs derive from
the actual renders, with an 800 ms loop matching the clip duration.

Review files are under review/: Walk/Spit Contact-Sheet PNGs, Walk/Spit Playback
GIFs, Back-Review PNG, Idle-Review PNG and every source render under frames/.
Rendering used Cycles CPU, nice priority 10 and two threads under the shared
/tmp/asset-studio-hunyuan.lock. No GPU render or inference ran from this worker.

LIMITATIONS
Only LOD0 is rigged here. Original LOD1/2 remain static; do not substitute them
as animated LODs. Fused sculpt joints permit this modest motion but retain some
close-up knee compression; this is not deformation retopology or a full mesh
self-intersection certification. Review the character at the actual gameplay
camera and movement speeds. No Unity import, AI flow, sound, device or phone
performance result is claimed by this asset worker.

REPRODUCTION
measure_semantics.py uses the existing paint to protect the cyan belly.
skin_weights.py creates measured joint collars and equal weights at UV copies.
build_rig.py creates the skeleton and bakes authored contact/attack schedules.
refine_weights.py records the bounded skin-only correction with unchanged
geometry/animation/texture bytes. sync_blend_weights.py keeps the editable
scene aligned with the final GLB. review_export.py reimports and captures it.
audit_glb.py, audit_skin.py and package_review.py produce the evidence.
Rejected draft captures are retained in review/rejected-draft-* for provenance.
