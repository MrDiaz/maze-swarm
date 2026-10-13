# Mission briefing — measured from the approved PNG (1672 × 941)

The measurements come from crops and pixel samples of `exec-c3967dd4-….png`.

**Units.** The game canvas scales by height to 1080, so 1 ref px = 1080/941 = **1.148 canvas units**. The reference width maps to **1919 units**.

**Phone.** On the phone (2556×1179, safe area 59 pt sides / 21 pt home bar) the safe canvas is about 2017 × 1022 units.

## Anchoring rules (so both aspects hold)

- **Header:** anchored top-left.
- **Preview panel (left):** stretches horizontally. Its left edge is at 44 and its right edge is 688 from the right, so it takes up any extra width.
- **Operative panel (right):** right-anchored, fixed width.
- **Bottom bar:** anchored bottom.
  - BACK sits on the left.
  - The difficulty group is centred at x = 0.5 − 38.
  - LAUNCH sits on the right.
- **Vertical:** both panels stretch vertically. They sit 176 from the top and 205 from the bottom, so the bottom bar never overlaps them on the shorter phone safe area.

## Rects (ref px → canvas units)

### Header

| Element | Ref position / size | Canvas | Notes |
|---|---|---|---|
| MISSION BRIEFING label | (76, 37), cap 13 | font 24 | letter-spaced, #8aa3b8 |
| Title | x 76, top 62, cap 68 | font ≈118 | bold, #fcfdfa, auto-fits up to 640 wide |
| "5 FLOORS • RECOMMENDED POWER 900" | (642, 58), cap 14 | font 24 | letter-spaced, #cfe9f5; the value is bold white |
| Modifier chip | (640, 86) 266×35 | (735, 99) 305×40 | see below |
| Explanation line | (642, 128) | (737, 147) | font 21, #c8d6e0, single line |

Modifier chip detail:

- Fill #2a1608 at about 0.9.
- 2 px edge #f08a24.
- Chamfered corners.
- A round orange icon.
- Text: bold 24, #ffcb39.

### Preview panel

- Outer: (38, 153) → (1072, 762), canvas (44, 176) → (1231, 875).
- Chamfer about 14 px at top-left and bottom-right.
- Frame: a 3 px steel edge #365061, inner glass #031725.
- Image: inset 12 / 14, height 505 (canvas 580), with a slight dark vignette.
- Goal row: centred at y 710.
  - Icons are 48 ref (55 units) in a ring outline.
  - Label baseline at y 747, font 22, bold.
  - Spacing 182 ref (209 units), the five icons centred in the panel.
  - Thin connectors #3a5a70 with small cream dots #f3e7b0 at the midpoints.
  - Colours: Hunt #ffd05f, Boss #ff6b5b, others #e8f2f8 with cyan icons #6fc7ef.

### Operative panel

- Outer: (1085, 157) → (1635, 762), canvas width 631.
- The same chamfered steel frame as the preview.
- Portrait art fills the top area and bleeds right/up a little. It fades into the glass at the left and bottom.
- Text:
  - "OPERATIVE" label at (1113, 180): font 22, letter-spaced, #8aa3b8.
  - Name at (1113, 205), cap 32: bold 56, #ffffff.
  - Role at (1113, 255): font 28, #5fc8f0 (example: "HEAVY / MINIGUN").
- Abilities sub-panel: top y 575, label "ABILITIES" at (1109, 586), font 24.
- Ability tiles: 160×125 ref (184×144 units), 18 ref gap, top y 615.
  - **Ready tile:** cyan edge #429cd1 with glow. White icon, name bold 26. Sub-line #80cef9, font 19 (e.g. "6 s COOLDOWN").
  - **Locked tile:** steel edge #3a5468. Icon and name #9fb3c4. Sub-line "LOCKED", font 18.

### Bottom bar

The bar is a darker band from y 775 down.

| Element | Ref rect | Canvas | Notes |
|---|---|---|---|
| BACK | (45, 800) 275×87 | (52, 918) 316×100 | steel frame #355165, chevron + divider, "BACK" bold 42 #e6eef4 |
| DIFFICULTY label | (454, 790) | — | font 22, letter-spaced, #8aa3b8, with a fading rule |
| Normal | (451, 817) 237×73 | 272×84 | |
| Hard | (707, 817) 211×73 | 242×84 | |
| Nightmare | (937, 817) 221×73 | 254×84 | |
| LAUNCH | (1212, 791) 413×102 | 474×117 | see below |

Difficulty buttons:

- **Selected:** cyan edge #429cd1 plus glow, a fill a little lighter (#0d2c40), a bar-chart icon, name bold 28 white, the recommended power below (19, #3fa9f5), and a check circle on the right.
- **Locked:** a lock icon, steel edge, the name bold 28 #c6d2dd, the gate text 18 #7c9ebb.

LAUNCH:

- Fill lime #c6e73f to #cdeb44, chamfered.
- A black play triangle and "LAUNCH" bold 62 #091904.
- A soft lime glow.

## Background

A dark industrial hangar (≈ #091b2c to #021725), heavily dimmed. The game uses the hero backdrop, dimmed to about 0.8.

## Typography

- Rajdhani SemiBold is the only bundled face. Bold weights use Unity's faux bold.
- The reference's heavier display weight (the title) is approximated, and that is a known difference.
- Letter spacing is emulated with spaces.

## Copy rules (bound to data, not the example values)

- **Title:** the selected zone's name.
- **Floors / power:** "5 FLOORS" (or "ENDLESS" for Swarm Gate) and the recommended power at the selected difficulty.
- **Chip:** the active modifiers (zone mods for the difficulty, Hot Zone, the weekly pair). Each one is explained with its own text.
  - Glass Cannon: "Enemies move 25% faster. Your projectile hits deal 50% more damage; you take 50% more damage." (the wording the brief requires).
- **Goal row:** the real plan for the run (`Game.PendingPlan`).
- **Operative:** `Prefs.HeroName` / `HeroRole` / portrait.
- **Abilities:** DASH with the configured cooldown, then the hero's real skill and lock states.
- **Difficulty:** `Meta.TierGateFor` / `RecommendedAt`.
- **Preview:** a baked overhead render of the zone's real floor-1 layout, captioned "FLOOR 1 LAYOUT". If that render is missing, the cover art is used and labelled "ILLUSTRATIVE".
