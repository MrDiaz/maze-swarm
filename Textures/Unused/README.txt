Cryo Station re-skin textures, moved out of Assets/Maze/Resources on Oct 11, 2026 after the look was reverted
(Game.CryoLook = false), so they no longer ship in the app. Nothing was deleted.
  Cryo/        the sheet-tile textures (Tools/cryo_tiles.py)
  Cryo/pbr/    the CC0 PBR sets processed by Tools/cryo_pbr.mjs (sources in Textures/Source, credits in Textures/CREDITS.txt)
To bring the re-skin back: move Cryo (and Cryo.meta) back to Assets/Maze/Resources/, set Game.CryoLook = true,
run Maze > Setup Project.
