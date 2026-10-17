# Rounded chibi HUD kit · v1

Reusable navy / ivory / cyan interface pieces inspired by the approved gameplay mockup. These are authored vector approximations of its visual language, not pixel-exact extractions. They are deliberately cleaner and less metallic for small-screen use. No game files were changed.

## Files

- `svg/`: scalable source for every vector-authored component
- `png/`: 1× RGBA exports, with real transparent pixels outside shapes
- `manifest.json`: file names, dimensions, normalized pivots, 9-slice borders, alpha checks, and provenance
- `preview/contact-sheet.png`: labeled asset inventory; labels and checkerboard are preview only
- `preview/assembly-preview.png`: sample layered composition, for review only
- `build_kit.py`: reproducible source generator; requires Python Pillow and Inkscape

## Fidelity and portrait

All panels, controls and icons are newly authored approximations. The rifle silhouette is a generic readability-first icon, not a precise model of the hero's weapon. No mockup text, game scenery, or flattened gameplay screen is included as an implementation asset.

`portrait_hero_exact_crop.png` uses the supplied hero image, cropped to source box [55, 0, 930, 875], resized and rounded-alpha-masked. The face and hair were not redrawn. The source blue background is retained inside the crop. This is not a separately segmented character cutout. Source filename and SHA-256 are recorded in the manifest. Use under `portrait_frame`; keep a small inset so the crop does not overlap the border. The optional portrait is raster-only by design.

## Layer recipes

- Health: panel_status → health_track → health_fill → live health numbers. Place the portrait to the left as a separate hierarchy. Crop the fill from the right as health decreases; do not stretch or scale the fill width, which distorts end caps. A very low fill will naturally have a clipped right edge. For a rounded moving cap, implement a shader/SDF rounded fill.
- XP: xp_track → xp_fill, clipped left-to-right → separate live level label. Objective uses its own larger track and cyan fill.
- Action button: button_circle_normal → optional cooldown_dim_overlay → icon → live action label / timer → cooldown_ring_track → dynamic cooldown_ring_fill. Ring track and fill share circle center (128,126), radius 101 in a 256×256 canvas. Use the same rect for all ring layers.
- Ready: remove timer/dim layer and show cooldown_ready_ring. Animate alpha/scale gently at runtime if desired. Do not use a static partial arc to communicate real progress.
- Selected: swap to button_circle_selected, optionally show button_selected_accent. Pressed and disabled backgrounds are separate state assets. Use disabled icon variants and suppress ready pulses while disabled. Provide hit targets separately from decorative artwork.
- Joystick: base and thumb are independent. Move only the thumb inside your joystick clamp radius. Direction arrows are decorative and baked into the base.
- Pause: button_pause + icon_pause_cyan. Currency: panel_currency + icon_coin + live value.

## Dynamic ring and fill details

The ring-fill PNG is a full ring, intended for an engine radial-360 mask or equivalent shader. In Unity uGUI set Image Type=Filled, Fill Method=Radial360, Fill Origin=Top and clockwise=true; test the visible progress direction against your cooldown convention. A ready fraction of 0→1 should fill the ring; a remaining fraction requires inversion. Since the ring center is vertically offset by 2 px for its paired button's optical center, a custom radial shader should use (0.5,126/256) as its center. Basic uGUI Radial360 is acceptable at these sizes; regenerate a centered ring if exact radial alignment is required.

Health/XP/objective full fills are all separate transparent layers. Use RectMask2D or equivalent clipping; preserve native height. All numbers, action names, objective names, level labels and cooldown seconds must be real text and localized independently. Use your game's licensed rounded bold UI typeface with an optional restrained navy outline; no font is bundled.

## Import, slicing and layout

PNG uses straight alpha in sRGB. For Unity: Texture Type=Sprite (2D and UI), alpha source=input, Alpha Is Transparency=true, filter=bilinear, wrap=clamp, mipmaps=off for ordinary screen-space UI. Start with uncompressed/RGBA32 and compare your platform compression before shipping. Assets are padded individually; preserve transparent bounds and import with a full-rect mesh for sliced UI. Atlas packing should add at least 4 px padding and avoid tight rotation when it complicates masking.

Manifest dimensions are native pixels. Pivots are normalized in bottom-left Unity convention. `nine_slice_lbrt_px` is left,bottom,right,top; zero borders mean use Simple instead of Sliced. Rounded panel/button backgrounds may be 9-sliced horizontally and vertically; do not shrink below the sum of opposing borders plus 4 px. Progress tracks may be 9-sliced, but their textured fills should be clipped rather than sliced or scaled. Circular buttons, icons, rings, portrait and joystick must retain uniform scale.

Use a CanvasScaler or equivalent, anchor health top-left, objective top-center, currency/pause top-right, joystick bottom-left and action controls bottom-right; honor phone safe areas. Reference mockup canvas is 1846×852; this is visual guidance, not an assumption about the game's actual target canvas. Ring/icon art is provided at 256/128 px to support approximately 96–192 px controls. Test at actual handset resolution and keep essential touch targets at least platform-recommended size.

## Verification and limits

Every export is checked to have transparent alpha. The contact sheet and assembly preview are generated from the actual PNGs. No functional runtime UI, animations, cooldown logic, accessibility states, sound, particles, weapon VFX or engine-specific prefab is included. No in-engine rendering or device integration has been tested. Exact target HUD hierarchy and dimensions should be confirmed before wiring.
