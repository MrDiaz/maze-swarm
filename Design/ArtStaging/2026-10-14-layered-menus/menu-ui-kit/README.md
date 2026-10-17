# Maze Swarm menu UI kit

40 transparent PNGs plus matching SVG source files. Blank buttons contain no baked text. All artwork is a clean vector adaptation of the approved Heroes and Gear menu references, not a pixel-exact extraction. Character, gear and weapon illustrations are intentionally absent. Objective and navigation pictograms are newly drawn complementary symbols.

## Import
Use manifest.json for dimensions, center pivots and Unity sprite borders. Border order is left, bottom, right, top, in source pixels. Set panels, cards and buttons to UI Image Type Sliced; use Full Rect meshes. Use preserveAspect for icons and Simple image type. Keep alpha, disable mipmaps and texture compression, use Bilinear/Clamp. Default PPU is 100; Canvas reference resolution should match the app, not these texture dimensions.

## Composition
Put card_frame below illustrations; put selected_outline above them, then badge_check_cyan at lower right. card_selected is a filled alternative. Use button_yellow_blank with runtime navy text, button_navy_blank with ivory text. Reserve padding of 36 pixels on yellow buttons and 48 pixels on main panels. Main-panel top decoration is stylized; stretch only center using provided borders. Outline has a transparent center.

Icons are 128x128 with intentional outer padding and three variants: ivory, cyan, navy. Deploy is a play triangle; Heroes is a team silhouette; Gear is an armored vest; objective meanings: Purge=flame, Hunt=target, Hold=shield, Survive=hourglass, Boss=horned skull. Interior icon details are true transparent cutouts, so each variant works on its intended contrasting background. For dark-on-gold use navy icons.

## Source references
maze-swarm-heroes-menu.png and maze-swarm-gear-menu.png. No reference raster was copied or retouched. SVG gradients and silhouettes were redrawn to keep assets clean and resizable.
