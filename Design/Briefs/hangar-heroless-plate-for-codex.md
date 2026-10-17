# Hangar plate without the Vanguard — brief for Codex

2026-10-16. Asked by Manuel (via the project coordinator). Builder: Claude on Manuel's Mac.

## Who made the current plate

The plate came from **Codex's rich-hangar task** (`~/Documents/Codex/2026-10-13/task-2/rich-hangar/`).

- `art-provenance.json` records the method as a "Built-in image_gen targeted scene edit". The edit preserved the scenery and logo, swapped the reference figure for the approved Heavy, and removed the baked controls.
- The prompts are in `image-prompts.txt` in the same folder:
  1. a scene edit of `Art Direction/Concepts/Hangar/03 Pending Approval Mockups/hangar-rich-scene-light-ui.png`, using `heavy-unarmed.png` as the identity reference
  2. an outpaint over `outpaint/guide.png`

So this asks the same tool and the same task to run one more targeted edit. Nothing here needs new keys, purchases or a different generator.

## What we need

Two new PNGs, each **alongside** the current ones. Do not overwrite anything:

| new file | matches | size |
|---|---|---|
| `RichHangar/Art/scene-rich-heroless.png` | `scene-rich-heavy.png` | 1672 × 941 |
| `RichHangar/Art/scene-rich-extended-heroless.png` | `scene-rich-extended.png` | 1835 × 857 (the 2160 × 1008 outpaint, downscaled exactly like the current one) |

- **Same framing, camera, perspective and resolution.** Every pixel outside the hero's area should match the current plate. That includes the logo, the sky, the ships, the workers, the holo table and the outpainted strips.
  - Matching matters because RichHangarArtFit maps the 2160 × 1008 guide with the original 1672 × 941 at (244, 0) from the top left. Our live layers (HangarLife lights, holo shimmer, snow) are placed in those painting coordinates.
- **No Vanguard / Heavy, no figure at all.** Rebuild the background where he stood:
  - the hangar floor tiles and their warm reflections
  - the far docked ships and crates, and the workers left of him (x ≈ 0–360, y ≈ 450–540 in the 1672 canvas), continued behind where his body was
  - the bay rail and clouds behind his head and backpack (x ≈ 380–780, y ≈ 180–330)
- **Remove his contact shadow** too, since the 3D model casts its own.
- Do not add anything new in his place. The spot has to stay an empty, readable patch of floor.

Suggested edit prompt (same style as the existing ones):

> precise-object-edit. Input is the exact edit target. Keep the same canvas, camera, perspective, planet vista, station, all aircraft, warm sunlight, workers, crates, holographic cyan maze table, foreground framing and MAZE SWARM logo. Remove ONLY the bald bearded armored Heavy standing left of the table (and his backpack and contact shadow). Continue the floor tiles, reflections, docked ships, crates, workers, rail and clouds naturally behind where he stood. No new characters, objects, text or UI. Same exact composition and size.

Do the extended version the same way: run the edit on the 2160 × 1008 outpaint, or re-run the outpaint on the new 1672 plate with the same guide.

## His spot — where the 3D model will stand

Measured on `scene-rich-heavy.png` (1672 × 941, pixels from the top left):

- **Feet / floor contact:** centre x ≈ 535, y ≈ 718. The prompt's spec was "feet near y=748".
  - left boot ≈ x 365–525
  - right boot ≈ x 575–705
- **Head top:** y ≈ 190. The spec was "head near y=185".
- **Height:** ≈ 530 px, about 56% of the canvas height.
- **Body bounds** (backpack included): x ≈ 355–770.
- **Facing:** three-quarter toward camera-right, looking at the holo table.

In the extended texture's painting coordinates (u right, v down, 0–1):

- feet ≈ (0.361, 0.712)
- head ≈ (0.361, 0.188)
- height ≈ 0.524 of the painting height

## Light to match

- **Key:** warm sun (≈ #FFD9A0), from the bright sky in the bay opening, upper centre-left behind him (sun disc near x 650, y 140).
  - On the figure it reads as warm light from the front-left and above: lit face, bright white armour, warm floor glints.
  - It gives a soft warm rim on the left shoulder and the head.
- **Fill:** cool cyan (≈ #6FD8FF) from the holo table at his right (centre ≈ x 1160, y 600), low and to camera-right.
  - Visible as blue bounce on the right glove and knee.
- **Ambient:** bright and slightly warm. The floor is glossy and mirrors the sky.
- **Contact shadow:** small, soft and short. It falls slightly toward the camera and the lower right, because the sun is behind and to the upper left.

## How it gets used

A toggle-off 3D Vanguard (HangarHero3D) already stands on this spot over the current plate. Once these PNGs land, we point the plate at the hero-less file (with the toggle on) and the live model replaces the painted one. The painted plate stays the default until Manuel signs off.
