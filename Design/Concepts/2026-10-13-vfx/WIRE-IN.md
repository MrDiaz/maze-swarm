# Hazard VFX batch 1: wire-in

For the game thread. The look target is the preview Manuel saw: https://claude.ai/artifact/NGxReqCqH7Tqro7u9c6eBZ (same maths and textures; toggle Now / New, tap a label for a close-up).

What changes on screen: Spitter / Acid Matriarch / toxic-elite / boss acid puddles, the acid-pool zone hazard, toxic-rain puddles and `a` cells become real acid (splash on landing, bubbling, swirling liquid with a bright edge, a burnt stain under it). Spore clouds become churning yellow-green gas. `f` cells burn (flames, embers, smoke, glowing char). `*` slag becomes cracked glowing lava. Laser fences get posts, emitters, three beams, sparks and a floor glow. Sentry turrets paint a thin aim line, then fire a heavy beam with an impact burst and a scorch line. Gameplay, damage numbers, timings and saves are untouched; every hook falls back to the old look if the kit is missing.

## Files (base commit 10aae72 on main)
Copy into the Unity project at the same paths:
- `Assets/Maze/Resources/Shaders/VfxPool.shader`, `VfxBeam.shader`, `VfxParticle.shader` (URP HLSL, written like HeroRim.shader; in Resources so they ship in the build)
- `Assets/Maze/Resources/Fx/Vfx/*.png`: vfx_noise (512, 4 noises in RGBA), smoke_8x8 and fire_8x8 (2048 flipbooks, 64 looping frames), ember, glow, bubble, droplet, spark (spark is spare)
- `Assets/Maze/Scripts/VfxKit.cs`: the kit (surfaces, beams, fences, 7 pooled particle systems)
- `Assets/Maze/Editor/VfxTextureImport.cs`: import settings for Fx/Vfx (sRGB off, 2048, mipmaps; noise repeat + uncompressed). Reimport the folder after copying.
Then apply the hooks: `git apply --3way vfx-batch1-hooks.patch` (touches Fx.cs, EnemyAI.cs, ZoneMechanics.cs, ZoneCells.cs). If the Mac has moved on and a hunk fails, the patch is small and commented; apply it by hand.

## Where each hook is
- Fx.Init builds `VfxKit` (child of Fx); Fx.ClearAll clears it.
- EnemyAI.AcidPuddle: VfxPool (Acid) that grows in over 0.35 s and shrinks away over the last 1.5 s; splash when r >= 1.3 m (the toxic elite's 1 m trail stays quiet). TickGlobal drives life and bubbles. Spore clouds: `VfxKit.Gas` each frame while a cloud lives (replaces the cube smoke).
- ZoneMechanics.AcidPools: VfxPool (AcidPool) + ambient; ToxicRain: the warning ring stays, then becomes a pool + splash; Turrets: paint / shot / scorch beams, impact sparks, flashes and smoke (`Reach` stops the beam at the first wall).
- ZoneCells.CellDecals: `a`, `*`, `f` cells become one VfxCells surface per letter (a blurred cell mask, so neighbouring cells merge into one blob) with ambient emitters; CellFences: `VfxKit.Fence` (posts every 2 cells, beams at 0.35 / 0.8 / 1.25 m, floor strip), driven by the existing on / off / flicker cycle.

## Checks before showing Manuel
1. Compile and run Cryo Station plus one zone with each of: acid (Acid Marsh), fences, turrets, fire / slag (Kessler or Halvorsen). Before / after shots at the game camera and one close-up each, on a light and a dark floor.
2. Colour space: the project is Linear. Shaders treat colours in gamma and convert at the end, so colours should match the preview. If the PARTICLES look washed out (too bright, pastel), set `_VColLinear` to 0 on the VfxParticle materials (VfxKit.Make); if they look too dark, leave it at 1. Pools and beams don't depend on this.
3. Flipbooks: fire and smoke should animate (64 frames, random start). If they show the whole 8x8 sheet, the texture-sheet module isn't on; if they flicker between unrelated frames, check `startFrame` units (frame index, as GunFx uses).
4. Spark streaks use the round ember sprite stretched along velocity, so orientation can't be wrong.
5. fps on the Mac editor demo bot with a fire zone: fire is the fill-rate risk. Caps: fire 240 particles, smoke 260; `VfxKit.Ambient` scales emission with pool area and caps it at 4x. If a big fire floor drops frames, lower the `default:` case rates in `Ambient` (flames 8/s per 12 m²) or the fire maxParticles.
6. Saves: nothing here reads or writes the save.

## Not in batch 1
Shared Sparks / Smoke / Debris (still cubes in Fx.MakePS: hits, kills, skills), explosions, the red attack warnings (all in batch 2 below), steam / cryo / toxic vents, radiation, grates, creep, water, ice, weather. Inventory: /mnt/project-files/design/vfx/vfx-inventory.md.

# Batch 2: sparks, smoke, explosions, attack warnings

Look target: the same preview, **Combat** tab (bullet hits, dash and impact dust, explosion, strike warning, charge lane, spore shot).

What changes on screen: every `Fx.Sparks` call (kills, crits, skills, freezes, perfect dash; about 40 call sites) draws glowing streaks with a small flash on big bursts instead of cubes; every `Fx.Smoke` call (dash dust, slams, steam, door puffs) draws soft billowing puffs. `Fx.Boom` (barrels, Bloaters, grenades, Frag Launcher) becomes a fireball: swelling white-hot to orange to dark-red blasts, a flash, sparks, embers and a smoke column; the shockwave ring, scorch decal, debris chunks and the point light stay. Enemy attack warnings (`EnemyAI.Tele` circles and lanes) get a bright edge, a fill that sweeps in toward the moment of impact, and stripes (circles) or chevrons (lanes) flowing toward the danger; they are alpha-blended now (were additive), so they read on the light Cryo floor. Debris chunks, GunFx (muzzle, tracers, casings) and the goo are unchanged. Gameplay, timings, hit tests, the bot's dodge logic and saves are untouched.

## Files (on top of batch 1)
- Replace `Assets/Maze/Scripts/VfxKit.cs` and `Assets/Maze/Resources/Shaders/VfxParticle.shader` with these versions. The only batch-1 delta the Mac made that I know of (the line-149 brace) is already in. If you changed anything else in either file, diff before replacing.
- New: `Assets/Maze/Resources/Shaders/VfxTele.shader` ("Maze/VfxTele"). No new textures (the fireball reuses smoke_8x8).
- Hooks: `git apply --3way vfx-batch2-hooks.patch` (Fx.cs, EnemyAI.cs only).

## Where each hook is
- `Fx.Sparks` / `Fx.Smoke` call `VfxKit.FxSparks` / `FxSmoke` with the same arguments when the kit exists (cubes otherwise).
- `Fx.Boom` calls `VfxKit.Explosion(p, R)` and skips the sphere puffs, core, cube sparks and cube smoke; ring, scorch, debris, light unchanged. `FinishBoom` and `Tick` tolerate a null core.
- `EnemyAI.Tele.Get` uses `VfxKit.TeleMat()` (falls back to `Mats.Glow`) and sets `_Shape` (circle 0, lane 1, thin ring 2 = plain colour as before); `TickAll` sets `_Progress` = t / life next to the existing `_BaseColor` write.
- `VfxParticle` gained `_MulTexAlpha` (fireballs) and `_FloorFade` (smoke 0.6 m, fire 0.12, glow 0.5, blast 0.9), using world y with the floor at 0.

## Checks before showing Manuel
1. A barrel or Bloater explosion at the game camera on Cryo and on a dark floor, plus a close-up: no hard square edges on the fireballs, no hard line where sprites meet the floor.
2. A Spitter (green circle), a strike or slam (red circle) and a charger (lane): the fill should sweep from the centre out (circle) or from the attacker to the far end (lane). If a lane fills backwards, the quad's uv.y is flipped: use `1 - i.uv.y` in VfxTele's lane branch.
3. Kill bursts and skills: streaks in the caller's colour, not white squares.
4. fps with a busy fight (Swarm Gate or a boss): blast max 120, smoke 260 shared with hazards. If smoke runs out on fire floors, raise VfxSmoke maxParticles to 400.
