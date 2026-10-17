# Living Hangar and Armory

The canonical game remains `/Volumes/T7 Shield/Projects/Maze Swarm`. The temporary review checkout was removed before final canonical validation and packaging.

`source/build_living_rooms.py` authors the rounded command-ship and armory geometry in Blender. Editable scenes are in `exports/`. The imported room prefabs, batched meshes, materials, scoped shader and runtime component are under `Assets/Maze/LivingEnvironments`.

The existing Hangar and Equipment controls remain authoritative. `LivingEnvironmentView` renders the actual Heavy and room geometry on layer 29 into a bounded render texture, and disables each camera when its screen is hidden. Its per-camera light restores the original scene sun after rendering. It adds no gameplay collider, audio listener or event system.

The map table is decorative and uses actual `SectorDefs` IDs/names for Cryo Station, Verdant Colony and Kessler Refinery. Deployment still opens the existing complete zone selection and progression.

The ten existing Meshy Cryo props use the delivered optimized models and textures. Their source assets and articulation were preserved. The room-material pass skips unrelated unlit status-ring materials. Existing map collision data and gameplay camera are unchanged.

Editor review: `Maze.EditorTools.LivingEnvironmentBuilder.ImportAndProof` imports the authored geometry, then runs the existing UI proof plus optional `living` cases. `Review` runs without rebuilding the authored prefabs. Set `BLUE_UI_CASE=menu,equipment,dialog,living`, `BLUE_UI_OUTPUT` and `LIVING_INPUT_MANIFEST` for saved captures and input hashes. `LIVING_REVIEW_PROGRESS_SEED` is restricted to an isolated review company and is never used by the canonical build.
