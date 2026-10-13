# Wiring the Cryo floor into Unity (Cryo Station only)

Manuel, 2026-10-13 12:13Z: "show me the floor in the game, wire it in".
Files on the Mac: Maze Swarm/Design/Concepts/2026-10-13-cryo-floor/textures/ (also in /mnt/project-files/art/cryo-floor/textures/).
Target look: compare_now_vs_new.jpg / preview_close.jpg in the same folder (three.js preview; walls were placeholder boxes).

## Floor material
- Tile covers 8 x 8 m (2048 px, 256 px/m), seamless. World-space UVs: uv = worldXZ / 8.
- cryo_floor_albedo.png: sRGB base map (AO already lightly baked in).
- cryo_floor_normal.png: OpenGL-convention normal (+Y green). Import as Normal map; Unity expects OpenGL, so no flip. Strength about 1.6 looked right.
- cryo_floor_rough.png: roughness (white = rough). URP Lit wants smoothness, so invert into the smoothness channel (metallic map alpha) or bake 1 - rough on import. Metallic about 0.15.
- cryo_floor_ao.png / height.png: optional (occlusion map; height unused).
- macro.png: large-scale variation multiplied over the albedo at one repeat per 30 m (0.93 to 1.05), to hide the 8 m repeat. A detail map or a tiny shader tweak is fine; skip if costly.
- Keep 2048 and mipmaps, anisotropic 4 to 8 (the camera is at a grazing angle).
- Replace only Cryo Station's floor (the current ProcTex floor stays for other zones). Keep the level layout, collision, blue ice pads and gameplay markings readable (yellow hazard zones and spawn docks can stay as overlays on top).

## Decals (flat quads a few mm above the floor, no shadows, no collision)
- frost_drift.png (2 x 0.6 m, tiles along X, top edge = wall side): along every wall foot, about 0.65 m deep, against the wall.
- hazard_albedo.png (2 x 0.3 m, tiles along X, alpha edges): worn yellow/black trim about 0.8 m out from wall runs.
- grate_albedo/normal/emissive.png (1.5 m square): a few vent grates in open floor, emissive cyan around 1.6 so they bloom if bloom exists.
- strip_albedo/emissive.png (2 x 0.08 m, tiles along X): cyan inset light strips beside a few walls.
- goo_splat.png (1 m): optional, purple splats; the game already has goo splats, so use only if they fit.
Batch decals by material to keep draw calls low.

## Proof before the phone
Same-angle before/after captures on the Mac at the phone ratio, gameplay in Cryo with enemies on screen, and an fps check. Muted, short, single run. Phone install only on Manuel's yes, with the usual save backup.
