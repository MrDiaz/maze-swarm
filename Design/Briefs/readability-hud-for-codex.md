# Readability pass: HUD items for Codex

From the Archero 2 comparison Manuel approved on 2026-10-15 ("let's try it"). The world-space half of the pass is already on the Mac in `Assets/Maze/Scripts/ReadabilityFx.cs`, which has one static toggle per change. These items live in your UI files (`Hud.cs`, `RunUi.cs`, `Holo.cs`, `HudStyle`), so they're yours. Nothing in those files was touched.

## 1. Level-up moment (target: under 4 s from level-up to back in play)
- **Dim:** the arena darkens to about 55% over 0.15 s. Gameplay is already paused while choosing (`Game.State.Choosing`).
- **Cards:** three cards flip in, staggered 80 ms apart, each flipping 0.25 s on its Y axis.
- **Rarity:** each card has a rarity glow behind it (common grey-blue, rare blue, epic violet, legendary gold) and a soft pulse.
- **Pick:** one tap picks. The chosen card scales to 1.08 and flies to the hero's level badge (0.2 s), and the others fade.
- **Gold burst:** on the hero is already done world-side (`ReadabilityFx.LevelUpBurst`). Time the dim to start right after it.

## 2. Wave tracker pop-in
- On each wave or objective change, the top tracker ("Wave 2 of 3", "Nests 1/3") scales 1 → 1.15 → 1 over 0.2 s with a short colour flash in the accent colour.
- The new number counts up.

## 3. Boss portrait pop
- On the boss intro (`BossBrain.Intro`), slide a boss portrait plate in from the top (0.25 s ease-out).
- Show the name, a frost-rim frame for Cryo, and the HP bar filling from empty to full in 0.6 s.
- Hold for 1.5 s, then shrink to the normal boss HP bar.

## 4. Small notes from the world-side pass
- **Ammo arc:** gone with no reload. Any HUD ammo counter or reload bar can go too, or stay hidden.
- **Damage numbers:** world-space (toggle `ReadabilityFx.DamageNumbers`). If the HUD also shows hit numbers anywhere, drop one of the two.
- **Camera:** the default framing is now CLOSE at 24 m (`ReadabilityFx.CameraNew`; FAR 40 m is still in Settings). HUD safe areas and the joystick zone may want a re-check at this framing.

## 5. Floating joystick (2026-10-16)
The left-thumb stick visuals live in `HudStyle/Runtime/Hud.Rounded.cs`, so this one is yours:
- **Placement:** the stick appears where the thumb lands, anywhere in the left ~45% of the screen. There's no fixed base.
- **Look:** a translucent ring (about 120 dp, white at 25%) with a solid knob (about 52 dp).
- **Direction arc:** a soft blue arc on the ring that points the way you're dragging and grows with the drag distance.
- **Release:** the stick fades out over 0.15 s.
- **Unchanged:** input feel, dead zone and max radius. This is visual only.

## 6. More from the Archero comparison
- **Level-up:** see section 1 (the dim, the three-card flip with rarity glow, a one-tap pick, under 4 s).
- **Chunkier HUD numbers:** a rounded heavy font for coins, gems, kills and the wave counter, with a dark outline and a short scale pop (1 → 1.15 → 1, 0.15 s) when a number changes.
- **Boss intro card:** see section 3.
- **Hero HP:** a world-space HP bar now sits over the hero (`FeelFx.HeroHpBar`). If the top-left HUD HP bar stays, the two show the same value; your call whether to keep both.
