MAZE SWARM — STYLIZED STATUS VFX / v0.1

10 independent vector-authored designs, each supplied as color SVG/PNG and a white tint-mask SVG/PNG (40 sprite files). Contact sheet is presentation only, not a texture atlas.

These are authored approximations of the approved chibi scene direction, not extracted originals. All sprite PNGs are transparent RGBA 512 x 512. Bold silhouettes, broad highlights, cyan freeze, green poison, warm yellow rewards. No gunfire assets are included.

USAGE
Use individual png/color sprites with white particle vertex color to preserve palette. Use png/masks with your chosen particle tint. A mask preserves source alpha but intentionally flattens colored facet/highlight detail. All pivots are centered (0.5, 0.5). Dash points right (+X); ice shard points up (+Y). Ring and puddle are flat source sprites intended for an appropriately oriented world quad. They are not Unity materials, prefabs, animated sheets, or automatically functioning decals.

Start with a compatible transparent particle shader, Clamp wrap, bilinear filtering, and Alpha Is Transparency enabled. Use alpha blending for puff/poison to avoid washing out the silhouette. Additive is optional for sparkle/ring; test against the bright blue floor. Transparent padding is built into each image; do not trim it away without updating bounds and UV handling. PNG alpha is straight, not premultiplied. White-mask PNG RGB is white even under zero alpha for clean tinted filtering.

manifest.json lists paths, centered pivots, blend recommendations, tint hints, emission and lifetime ranges. These ranges are unverified starting points, not measured gameplay values. Burst counts are per event, rates per second, and dash distance emission depends on world scale. Fade alpha to zero before particle death. Suggested ring scale 0.3x to 1.8x; puffs expand gently; bubbles rise slowly. No code or game integration has been performed.

VALIDATION
Offline SVG rasterization, image dimensions, transparency, bounds/padding, and contact-sheet review only. Actual Unity rendering, alpha behavior under compression, readability at gameplay size, sorting, performance, effects timing, and match to final camera remain unverified. Validate in the target render pipeline and on device before use in production.
