# Local batch 2026-10-16

Twenty selected models are integrated into **Maze → Floor Editor → Show → Local batch**. This adds 17 props and three clearly labelled unrigged enemy previews. Their stable IDs are `local-20261016-<slug>`; existing floor IDs and palette entries are preserved.

Models use metre scale, a shared floor-centred pivot across LODs, native thumbnails and measured placement footprints. Existing place/move/rotate/scale, undo/redo, Save/Reload and protected playtest paths support every new ID. Enemy cards are static previews with no rig, animation, attacks or AI. Local models cannot retain legacy gameplay behaviour.

`Resources/FloorModels/catalog.json` records selected versions, bounds and mobile LOD defaults; prefabs in that directory resolve at runtime. Manifold 016 uses its 3,000-triangle LOD1 before 2,200. Enemy mobile chains start at LOD1. Source contains all 51 exact selected GLBs; Generated contains standalone Unity meshes and URP/Lit materials. Textures retain embedded base/normal/raw material maps and the correctly packed URP metallic-smoothness maps. Runtime prefab dependencies exclude source GLBs and unused raw material maps.

Mobile texture defaults: mipmaps, trilinear sampling, ASTC 6×6 overrides for iPhone and Android, max dimension 512/1024 according to source plan. Runtime meshes are not CPU-readable. These settings and triangle counts are verified configuration, not device frame-rate results.

Bounds combine every selected LOD and add a 10 mm margin; the old pipe-elbow review proxy is not used. Prefab BoxColliders are disabled and are removed by the runtime factory. Optional Solid uses the existing conservative grid footprint. These are placement boxes, not anatomical collision shapes.

Editor/LocalBatchBuilder.cs reproduces mesh/material/prefab creation from Editor/import-plan.json. Editor/LocalBatchProof.cs verifies runtime/material/save/load integration. Editor/LocalBatchCapture.cs captures the actual attached floor editor using temporary unsaved review placements, then restores the original document. Preserve floor documents and use explicit Save for intended level edits.

Delivery evidence is in `/Users/manuel.diaz/Documents/Codex/2026-10-13/task-2/floor-editor/local-batch-integration/HANDOFF.md`: 400 import checks, 1,169 integration checks, and two 32-check attached-panel interaction runs passed. All 51 original source GLBs remain unchanged; no production floor override was retained.
