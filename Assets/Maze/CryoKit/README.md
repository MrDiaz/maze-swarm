# Cryo Station environment kit

Twenty reusable native Unity prefabs live in `Resources/CryoKit/Prefabs`.
Source glTF, buffers, shared textures and the finished kit manifest live in
`CryoKit/Source`; the original Blender and interchange deliverables remain
outside the game in the separate environment-art workspace.

`CryoKitEnvironment` replaces only the Cryo Station V2 run's B/C/# visuals.
The authoritative map, movement/shot blocking, objective scripts, hazards,
characters, camera, floor deck and functional Escape Test doors are unchanged.
Pods and service equipment sit on restricted machinery islands; continuous
low housings identify the full existing blocked footprints. Kit 06 is a
sealed service facade: authored Cryo maps contain no functional door cells.
All art prefabs are free of Colliders. `CryoKitEnvironment.On = false` restores
the prior visual path for comparisons.

The selective detailed wall coverage keeps each floor below 280,000 native
triangles and 72 native instances. This is an aspirational environment-art
prototype, not a claim of measured phone performance. Textures use mipmaps,
maximum 1024 pixels and ASTC 6x6 mobile overrides. There are no additional
LODs. Materials retain native UV0 base/normal/metal-roughness and UV1 AO.
Glass uses a mobile transparent approximation; volumetric refraction and
anisotropic highlights are not reproduced.

Editor tools:

- `Maze/Cryo Kit/Build Native Art Assets` recreates native meshes and materials
  from the staged source; retain existing meta files so asset GUIDs persist.
- `Maze/Cryo Kit/Capture In-engine Proof` exercises actual Unity Play mode
  with a temporary save and captures into `Logs/CryoKit`. The terminal entry
  point is `Maze.EditorTools.CryoKitProof.Run`, without `-quit`; it exits after
  the asynchronous frames/checks complete.

Proof covers matching gameplay-camera before/after frames, all five floor
startups, exact map hashes and walkable connectivity, native asset envelopes,
zero new Colliders, objective/hazard initialization, and existing Escape Test
door traversal/animation. It does not complete all five missions or measure a
phone build. The pod inspection view is a documented temporary close-up,
not a replacement gameplay camera.
