Cargo first-route enemy family — staging handoff

Scope and source
These five mesh/skin/animation derivatives come only from approved local Hunyuan mobile-006 Lavender Spitter LOD0. Original source GLBs, texture maps, approved Spitter GLB and editable Spitter scene were read only. The approved animated Spitter SHA256 remains 7bc01d38c339ad7d7e5cf4f7e67c19e2b6ccfb3d18533f015af5e3935c60a814. No new inference was used. The Spitter retains its recognizable open trumpet and existing authored Spit; no Spitter bytes changed.

Delivery is staged only. No canonical assets, Editor, Unity scene, device, save, gameplay values or camera were modified by this worker. The CPU-preview directory contains early exported skinned triangles with simplified software lighting. Final render-review contains 62 actual Blender Cycles CPU material/pose frames from reimported final GLBs, directly inspected and approved for owner integration. This establishes local authored material/skin quality; Unity and device quality remain with the integration owner.

Authored art
Grunt: rounded periwinkle head, cream belly, short crest/back nubs and mitten arms.
Runner: narrower body and longer planted legs, swept coral crest and peach belly.
Carrier: broad mint belly, smaller head and three cream rounded back pods.
Carapace: Carrier anatomy with slate shell and cream belly; distinct from the mint Carrier.
Tick: warm peach/orange accents; retains the existing small runtime scale .45. Minis reuse Grunt at their existing .55 scale. These existing gameplay scales are not baked into the files.

The old trumpet cavity/rim was removed as real topology (660 source faces). The remaining single 34-vertex boundary is closed with a convex 170-triangle patch. A small closed dark lip tube gives a readable mouth. Original fins are shortened/rounded, role proportions change actual vertex and corresponding bind-bone positions. Carrier pods are closed skinned geometry, not runtime overlays. Body, lip and pods remain individually closed components in one skinned primitive, sharing one matte opaque PBR atlas. Smooth normals/UVs and normalized <=4 weights are exported. No normal, metallic or emission noise maps. Preserve albedo and Bone4; a flat single color would erase eyes/belly/role cues.

Files and axes
Each role directory contains cargo-family-<role>.glb, the editable .blend with packed atlas, the albedo PNG, asset.json, contact-schedule.json and independent-audit.json. All files face +Z with +Y up in glTF. Floor/base pivot remains at zero. Dimensions and exact current hashes are authoritative in asset.json and INTEGRATION.json. These are animation-ready LOD0 assets; no animated LOD1/2 is claimed.

Grunt, Runner and Tick: 3,602 triangles, 14 exported skin joints.
Carrier and Carapace: 3,962 triangles, 18 exported skin joints.
Common joints: Root, Hips, Spine, Head, UpperArm.L/R, Forearm.L/R, Thigh.L/R, Shin.L/R, Foot.L/R. Root has no authored motion. Carrier adds CarrierBelly and CarrierPod.L/R/Back, direct children of Hips. There is deliberately no joint named exactly Belly or Muzzle in the family.

Clips: all 60Hz baked FK, no runtime IK or root motion
Every role: Idle 2.0s; MeleeWindup .15s; Melee .25s.
Grunt/Tick Walk .8s, .28m per cycle, native .35m/s.
Runner Walk .666666667s, .3136m per cycle, native .4704m/s.
Carrier/Carapace Walk 1.0s, .24m per cycle, native .24m/s.
Runner also: Crouch .6s; Leap .466666667s; Land .8s; Sidestep .35s.
Carrier/Carapace also: Bloat .7s; Burst .35s.
Idle and Walk close exactly. Feet are solved offline with 56% stance and baked FK contact. During Walk, phase advance = worldSpeed*dt / (nativeStride * model.lossyScale.x). The cycle duration converts phase to clip time; do not reuse old e.type.stride for actual new gait. Idle is selected when stopped, rather than freezing a mid-step pose. The short source sculpt has no separate toes/fingers/jaw; articulation is intentionally restrained. Tick's existing fast AI at .45 scale can yield high cycle rates; inspect readability in Unity. No gameplay slow-down is proposed.

Melee contact is frame zero, preserving the current immediate hit at atkAnim=.25. Its .25s clip is recovery. MeleeWindup .15s is cosmetic cooldown anticipation only when the same target is in contact range; it cannot delay/release a hit. Playback masks these torso clips at Spine, retaining the planted authored lower gait. Runner Crouch/Leap/Land/Sidestep sample existing state timers; the game's current wrapper root hop remains the only pounce arc. No second root leap is baked. Carrier Bloat samples existing castTime; Burst observes the existing squash timer after the original spawn tick. There are no new attack/spawn callbacks.

Only CarrierBelly has authored local swelling scale (Idle .992–1.008; Bloat to1.085; Burst max1.0935). Other joints remain unit scale. Pods translate/rotate independently during charge. Do not apply the old Belly multiply/shiver to the authored bone.

Runtime proposal and canonical integration
integration/ contains a complete narrow overlaid CohesiveCharacterPolicy.cs, Swarm.cs and new CohesiveFamilyPlayback.cs, plus unified diffs and baseline canonical source hashes. It is a reviewable proposal; merge the diffs with current owner edits rather than replacing a changed canonical file blindly. make_overlay.py reproduces the staged files from its exact guarded snippets, but should not be rerun after canonical integration.

Canonical resource aliases are exactly Models/cargo-family-{grunt,runner,carrier,carapace,tick}; imported file paths Assets/Maze/Resources/Models/cargo-family-{role}.glb. Spitter alias remains Models/cohesive_lavender_spitter.
Pure policy API: RoleFor(EnemyType), ResourceFor(EnemyType), AuthoredEnemyPrefab(EnemyType), IsAuthoredFamilyPrefab(GameObject). Resource availability and identity are route independent for validation. EnemyPrefab(EnemyType,string) alone is route gated by CohesiveZone.Active. Current exact mappings: null-ai grunt -> grunt; mini flag/name -> grunt; ai/name tick -> tick; leaper flag/name -> runner; bloater flag/name or boss_bloater -> carrier; ai/name carapace -> carapace; ai/name spitter -> existing Spitter. Nest and unlisted specialist roles retain existing resources. There is no invented ai="grunt" requirement.

Swarm.Init precedes StartRun assigning run/sector. The proposal therefore keeps the route-aware lookup at Init and repeats it immediately before each pool pop. A changed prefab retires only inactive pooled views and resets modelFit/modelLift. Active enemies, pending spawns and dying views defer the change. Existing LoadFloorMap calls Clear before spawning; Clear also releases dying views. First Cargo spawn selects the family even if menu prewarm cached old models. Returning to another route selects legacy prefabs and retires only the inactive family pool. Existing scale, radii and footprint formula 2.4*cfg.enemy.radius/max(width,depth) remain unchanged. NewModelView's cohesive fitting, materials, palette and Bone4 choices are scoped to CohesiveZone.For(game). Mini green-tint overwrite is suppressed only in Cargo. Current Spitter also follows this lazy route-aware selection.

RNG preservation contract
The actual current Swarm.LateUpdate branch is v.belly!=null && e.cast>0, containing Random.Range(-.03f,.03f)*k. Carrier views explicitly bind v.belly to CarrierBelly so this exact conditional draw and pulse remain, including while frozen. Only the old transform assignment is skipped for the authored model. AI cooldown, mini-count, spawn, damage and all other random calls are unchanged.
Manual gait must not silently remove the old BloaterStep Random.value eligibility. The proposal keeps a separate legacy 1.2000000477s Bloater Walk phase/speed clock, seeded from the same existing Spawn Random.value through the Walk normalized phase, with the original old speed formula. Its fit uses the measured old bloater.glb maximum X/Z extent 1.87635005m, not the new family footprint. LateUpdate uses that legacy clock for step pulse/RNG eligibility and the new distance phase solely for skin movement. No new Random call is made. The new clock has not been compared against Unity's old Animation update order in a live baseline, so bit-identical RNG-stream behavior is NOT yet established. The integration owner should compare same-seed branch counts/state before claiming it; do not ignore this pending runtime check.

Verification completed
Independent CPU glTF TRS + inverse-bind skin evaluation passes all 1,263 authored sample poses: no UV seam cracks, finite skin, normalized <=4 weights, unit normals, no external textures, exact Idle/Walk loops, planted feet and native-distance stance contacts. Maximum foot-target error is below 2.16e-7m; minimum floor deviation is floating-point noise. Root remains static; only CarrierBelly scales. Closed topology has zero boundary/nonmanifold edges. CPU four-view/pose sheets and walk GIFs were inspected for the rounded family/closed-mouth shape. These checks do not replace actual smooth-normal/material or live game review.

All108 existing canonical runtime sources plus the new family driver compile with the staged policy/Swarm overlay using Unity's Roslyn compiler, exit0. Existing unrelated deprecation/analyzer warnings remain. No Editor was launched; compile logs/report and source response-file hash are in integration/.

Actual review and remaining owner verification
The first actual 57-frame review caught contact arms pulling backward about 6cm due to the local bone-axis sign. A bounded correction changed only Melee/MeleeWindup arm keys; the corrected contact moves distal hands forward about 4.3cm and preserves the rounded mitten silhouette. Geometry, gameplay timing and all other clip poses were unchanged. First-pass generated assets/renders are preserved under history/contact-arm-v1. Final current hashes are in FINAL-GLB-SHA256.json and every asset.json.

The final authorized shared CPU batch reimported the corrected GLBs and rendered 62 frames (57 planned plus five windup proofs), at 384px, eight samples, two CPU threads, GPU disabled. It completed successfully; shared lease held 2026-10-17 00:21:41.388 UTC to 00:22:34.756 UTC and was released. Actual smooth normals, opaque matte palette, closed faces, corrected contact, Walk phases, Runner coil/leap/landing and Carrier charge/pods/recovery were inspected. The family-actual-material-sheet.png, family-actual-walk-sheet.png, per-role actual-pose-sheet.png, four-key Walk GIFs and REVIEW-MANIFEST.json are in render-review. GIFs show four rendered quarter-cycle keys at native duration, not a 60fps capture. Independent exported-skin checks still pass all 1,263 poses after correction. INTEGRATION.json actualMaterialRenderApproved=true marks this local art review, not device approval.

Integration owner still verifies Unity resources/Legacy import, first Cargo and other-sector transitions, fast Tick readability, melee contact timing, freeze/thaw, death/pool reset, culling and legacy RNG clock behavior. No game/device test is claimed by this staging handoff. The separate canonical Spitter OnDisable Idle0 reset made by the integration owner was not touched or overwritten.
