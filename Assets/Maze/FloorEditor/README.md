# Maze Swarm floor editor

Open the canonical Unity project, then choose **Maze → Floor Editor**. The editor uses UI Toolkit and requires no extra packages.

1. Choose a zone and its floor. A shared source layout is copied into a floor-specific draft; saving floor 1 does not change another floor that uses the same layout.
2. Drag a prop card onto the plan, or click a card and then click the plan. The ten Cryo prop cards show renders of the actual integrated models.
3. Select and drag a prop to move it. Shift adds objects to the selection. Drag empty space to select a group. Q/E rotates by 15 degrees; Delete removes; Cmd/Ctrl+D duplicates. Undo and Redo cover complete edits, drags and paint strokes. F frames the selection.
4. Use the grid tools below the prop cards to paint walls, cargo, doors, enemy spawns and the game's existing objective/mechanic markers. Painting P relocates the unique player start. Boundary walls stay protected. Middle mouse or Alt-drag pans; the wheel zooms.
5. Change position, height, yaw, uniform scale, fit dimensions and collision in the inspector. The plan shows collision, reachability and markers. The 3D pane supports orbit, pan, zoom, top view and selection framing.
6. **Save floor** explicitly writes the selected floor. Invalid data is rejected. Replacing a save retains the previous JSON as a timestamped `.bak`; writes use a validated temporary file and atomic replacement. A changed-on-disk file blocks overwrite until you export your draft and reload.
7. **Playtest draft** runs the current unsaved draft on the selected floor through the existing Test Mode route. **Stop test**, or leaving Play mode, restores the original progress, all typed game preferences and the original loaded scenes. Unsaved scenes block launch so scene work cannot be discarded. Interrupted tests retain a private recovery file and can be resumed through **Maze → Recover Floor Playtest**.

## Collision and runtime behavior

Grid cells are two metres. Rows use the existing v2 gameplay legend and remain authoritative for walls, doors, spawns and mechanics. Moving a visual prop does not silently move an existing wall or island: the remaining grid obstacle is visible in the plan and generated room.

Prop collision choices:

- `none`: decorative only; no new blocked cells.
- `existing`: checks that the footprint covers existing blocked cells.
- `solid`: derives conservative blocked cells from the prop's footprint, yaw and uniform scale. Moving or deleting the prop removes only its derived occupancy, retaining the base grid.

Imported Cryo anchors preserve existing turret/door/motion behavior. New props start decorative. **Keep asset behavior** explicitly enables the existing asset behavior. Functional doors are authored with D grid cells; the decorative blast-door model does not create a door by itself.

Saved overrides live at `Assets/Maze/FloorEditor/Resources/FloorEditor/Floors/<zone>/<floor>.json`. `MazeMap` reads those exact documents in ordinary gameplay as well as in playtest. Without an override, the original game path remains in use. Cryo's baked rooms remain available when their original collision rows match; an edited collision grid uses matching generated geometry instead of a stale baked room. The 3D pane is a cutaway authoring view rather than final game lighting.

## Limits

Protected playtesting currently supports the macOS Unity Editor. Other platforms reject launch before changing preferences. Enemy entry points and existing objective markers are editable; enemy budgets, event scripts, rewards and progression remain in the current zone specifications. Grid reachability is a static four-neighbour check; actual playtest is needed for actor-size and timed-mechanic behavior. One existing source layout, `skyhook/skyhook-b`, has an unreachable objective and is reported by validation rather than silently changed.

The installed phone build 20261015.1556 is unaffected. This editor task creates no production floor override by default.
