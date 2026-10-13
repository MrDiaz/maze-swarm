# Select Drop Zone — measured from the approved PNG (1672 × 941)

These measurements come from crops and pixel samples.

**Units.** 1 ref px = 1.148 canvas units (the canvas is 1080 high). The reference is 1919 units wide.

**Anchoring.**
- The title, ROUTE / BACK, the planet and the carousel are anchored left.
- The right panel is anchored right, at a fixed width.
- The carousel stretches from x 52 to the right panel minus 37.
- The bottom is anchored to the bottom of the safe area.

## Header

| Element | Ref position / size | Canvas | Notes |
|---|---|---|---|
| "SELECT DROP ZONE" | x 135, cap 36→72 | (155, −30), font ≈ 64 | bold, #f4f8fb, faint glow |
| Sub-line | y 91, cap 11 | (157, −104), font 19 | "19 SECTORS • RANK 1 • STARS 0/162"; tracked, #a9c6d8, bound to real data |
| ROUTE | (706, 36) 170 × 51 | (810, −41) 195 × 59 | route icon + text; dark glass, steel edge #3a5a72, chamfer |
| BACK | (896, 36) 170 × 51 | (1029, −41) 195 × 59 | chevron + text |

The TEST chip (test mode only) sits under the sub-line, amber, small.

## Planet

- The sphere spans about x 205–955 and y 60–610 (ref), so it is about 860 units in diameter. Its centre is at canvas (666, −385) from the top-left.
- A thin cyan orbit arc goes around it.
- **Locked pin:** a lock glyph in a 46-unit dark disc with a thin ring, and a small diamond below it.
- **Selected pin:** a lime target (core dot 30, ring 64, outer reticle 120 with ticks). Next to it is a dark label chip, bold 22 #f4f8fb: "CRYO STATION".
- **Open pins:** the zone's accent colour. **Cleared:** gold. **Hot:** orange.

## Carousel (destination artwork)

- Cards start at x 45 and step 207 (ref): 195 × 228 ref, which is 224 × 262 units, with a 14-unit gap.
- The bottom edge is at y 875 ref, 76 units above the bottom.
- **Card layout:**
  - The art fills the top ~70% (inset 6).
  - Below it is a dark band with the name (bold 24, #e6eef4) and 3 outline stars (gold when earned).
- **Locked:** the art stays at full colour, and a 40-unit dark disc with a lock sits top-right. There is no shade.
- **Selected:** a cyan #6ba7de edge plus glow; the others have a steel edge.

## Right panel

- Panel: (1100, 66) to (1643, 878) ref = 623 × 932 units, right margin 33.
- Glass #071d32 at about 0.92, steel edge, chamfer.
- Contents:
  - **Name:** bold 66, #f4f8fb, at (42, −30).
  - **Art:** (34, −102), 562 × 309, steel edge.
  - **Rows:** label / value pairs with thin dividers (#3a5a72 at 0.6). The label is tracked, 21, #a9c6d8; the value is bold 32, white.
    - RECOMMENDED POWER (bars icon + value), at row y 448.
    - BEST ATTEMPT (3 stars + grade or "—"; "NO ATTEMPT YET" when empty), at row y 506.
    - THREAT (5 bolts; red = active, #3a5468 = inactive), at row y 565.
  - **Blurb:** sentence case, 22, #d0dbe3, at y 628.
  - **Divider** at y 668.
  - **WEEKLY CHALLENGE label:** tracked 20, #a9c6d8, at y 696. An (i) ring icon sits on the right.
  - **Chip:** (42, −722), 357 × 48. Orange #f08a24 edge, text #feb637 bold 24, orange round icon.
    - Shown only when the weekly is active (Rank 8 or the dev override).
    - Otherwise it reads "UNLOCKS AT COMMANDER RANK 8" in grey with a lock.
  - **BRIEFING:** (34, −800), 557 × 106. Lime #cee74e → #d6ef58, chamfer, lime glow. A black document icon, "BRIEFING" bold 56 #091904, and chevrons ».
  - **Locked zone:** a red-edged box with a lock and "CLEAR <zone> TO UNLOCK", replacing BRIEFING.

## Copy and data

- Everything shown comes from SectorDefs, Progress and Meta (power at the selected difficulty).
- The weekly (i) shows the same accurate per-modifier copy as the briefing.
- The placeholder XP/CACHE rewards and the MISSION tiles are removed.
