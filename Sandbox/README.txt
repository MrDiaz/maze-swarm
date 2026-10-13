MAZE SWARM SANDBOX  -  a test arena for models, animations and effects
========================================================================

OPEN IT
  In Unity: menu  Maze > Open Sandbox , then press Play.
  (Maze > Setup Project also rebuilds the scene: Assets/Maze/Scenes/Sandbox.unity)
  On the iPhone: tap SANDBOX at the bottom right of the main menu. BACK returns to the game.

DROP FOLDER (Mac / editor only)
  Put .glb or .gltf files in:   Maze Swarm/Sandbox/Drop/
  They appear in the MODELS list marked DROP (press RESCAN after adding files while playing).
  They load at runtime with glTFast, so no Unity import is needed. A raw Meshy export
  says "NO RIG / NO ANIMATIONS" when it has none.
  The list always has the game's own models: DRONE, BLOATER, MINI and HERO (at game size).
  The phone shows only those; the drop folder isn't reachable on iOS.

CAMERA
  Mouse: left-drag to orbit, scroll to zoom, right-drag to pan. Click a model to select it.
  Phone: one-finger drag to orbit, two fingers to orbit and pinch-zoom, tap a model to select it.
  GAME CAMERA (MODELS tab) snaps to the in-game view: same angle, distance and lens.

MODELS TAB
  Pick a model in the list, then SPAWN. DELETE removes the selected one, CLEAR ALL removes everything.
  COMPARE (with a DROP file picked): the raw file on the left, its built game version on the right,
  scaled to the same footprint (matched by name: bloater, drone, hero).
  SELECT NEXT cycles through spawned models. The selected model's triangles, bones and clips are shown.

ANIM TAB
  Clip buttons pick a clip. |< and >| step one frame (and pause), PLAY/PAUSE, LOOP.
  SPEED 0.1x to 3x. SCALE 0.25x to 4x.
  SKELETON draws the bones as green lines; WIREFRAME draws the posed mesh as cyan lines (x-ray).
  STAND / WALK CIRCLE / TAP TO WALK: the model walks a 4.5 m circle, or walks to where you click/tap
  on the floor, at WALK SPEED. While walking the clip speed is matched to the ground speed the same
  way the game does it (game enemies use their real stride; dropped files assume 1 m per clip second).

FX / HERO TAB
  DRONE DEATH GOO, BLOATER SAC BURST, MINIS SPAWN, BLOATER DEATH, HIT: the game's own effects and
  sounds on the selected model.
  SPAWN HERO puts the player next to it; FIRE makes the hero shoot the selected model (tracers, muzzle
  flash, casings, impacts). WEAPON cycles RIFLE / SHOTGUN / SMG.

FILES
  Assets/Maze/Scripts/Sandbox.cs        the arena, panel and tools
  Assets/Maze/Editor/SandboxShot.cs     editor capture: Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.SandboxShot.Run
