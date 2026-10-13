# Game feel brief (sections 1-3 of design/game-feel.md, condensed; numbers in game-feel-numbers.csv)

## Decisions
- No global hit-stop on ordinary hits. Hits stagger only the target. Global time dips only for big kills and set pieces, max 250 ms except boss moments.
- Chain multiplies score fully and biomass at half strength (x1.0 to x1.5).
- Chain drains continuously at 1 + 0.08*pool per s. No hard reset.
- Only wins get a grade (S/A/B/C). Deaths keep the SWARMED screen with a run score and no letter.
- Grade pays Credits only (C 0, B +5, A +12, S +25%) plus 5 Cores for the first S per zone and tier.
- Last Breath: once per floor, a hit that would kill from >=35% HP leaves 1 HP, with 1.2 s invulnerability and a 4 m shockwave (shove 2 m, Drones stunned 0.8 s).
- Damage numbers off by default. Setting "Bonus hits only" shows weak-spot (yellow), frozen (blue) and execute (white) numbers: 14 px, rise 0.6 m over 0.5 s, max 12.

## 1. Global rules
Screen zones (landscape):
- Thumb zones: left and right 35% of width, bottom 55% of height. No text or opaque effects there.
- Play window: centre 60% of width, 15-85% of height.
- Top band: top 22% of height, centre 50% of width. The only place for banners, max 12% of screen height tall.
- Edge strips: outer 6% each side, for the chain chip, power-up icons and vignette.

Never list:
1. Flashes cap at alpha 0.35, 0.15 s. Exceptions: boss kill 0.5/0.12 s and Last Breath 0.45/0.1 s.
2. While any hostile telegraph aimed at the hero has <0.6 s left: shake capped at 0.04, time dips suppressed, top-band callouts wait. Hero damage is exempt.
3. Hero input is never frozen. Slow-mo is timed time-scale only.
4. One time-scale request at a time (lowest wins). Max 1.6 s of slow-mo per 5 s, except boss intro and kill.
5. Max 3 flashes per s through one shared limiter.
6. Haptics: min gap 80 ms, max 6/s. One manager with priorities; a lower one is dropped, not queued.
7. Pools: 600 particles, 80 decals, 12 floaters. Quality governor: if the 30-frame mean is over 9.5 ms, drop decals, then halve particles, then disable flashes. Restore one step after 5 s under 8 ms.
8. Fight text max 1.4 s (callouts) or 2.4 s (hints). Never needs a tap.

Units:
- Shake s: offset s*0.5 m, roll s*2 deg, noise 22 Hz, linear decay. Combined as min(0.35, sqrt(sum s^2)).
- Flash: a full-screen quad, additive for white/gold, normal blend for red.
- Haptics: tick = selection; light/medium/heavy = impact; success/warning = notification; pattern = Core Haptics, falling back to the nearest impact.
- Time dip = (scale, duration) with a 60 ms ease back. UI uses unscaled time.

Settings, new "Feel" group:
- Screen shake: Full/Half/Off
- Haptics: Full/Light/Off. Light drops one level and halves rates.
- Reduce flashes: halves alpha, min gap 0.6 s
- Damage numbers: Off/Bonus hits only
- Combat callouts: On/Off
- Tutorial hints: On/Off. Turns itself off after run 3.

## 2.1 Hit feedback
Every hit:
- The enemy flashes white (_FlashAmount 0 to 1 and back), at most once per 90 ms per enemy.
- A spark at the contact point.
- Hit sound with ±8% pitch and a 35 ms voice limit per enemy.

Per weapon:
- Rifle: flash 60 ms/0.7. Stagger (anim speed x0.2) 40 ms on M+ only. Knock 0.05 m, cap 0.30 within 0.5 s. No shake. Weapon kick 0.03 m. No haptic. 3 sparks.
- SMG: flash 50 ms/0.6. No stagger. Knock 0.03, cap 0.20. Kick 0.02. 2 sparks.
- Shotgun: flash 90 ms/0.9 and stagger 70 ms on targets hit by 3+ pellets. Knock 0.6 m at 5-8 pellets, scaled below 5, x1.6 inside 3 m. Shake 0.06/0.12 s per blast. Recoil 0.12 m/0.18 s. Medium haptic per blast (limit 0.45 s). 6 sparks plus 2 dust puffs. Goo burst on kill x1.5.
- Minigun: flash 40 ms/0.5 (limit 120 ms). Knock 0.08, cap 0.40. Sustained shake 0.03 ramping over the 0.4 s spin-up, never above 0.04. No recoil (stays level at the hip). Rigid haptic ticks 0.35 every 100 ms while firing. 3 sparks plus brass.

Readability variants:
- Armoured front: grey spark, no flash, metallic clank.
- Weak spot or crit: yellow-white flash 100 ms, double spark, bright tink.
- Frozen: pale-blue flash, ice chips, glassy tick.
- Burning: orange flicker.
- Boss: rim-only tint 0.4 for 50 ms.

## 2.2 Kill feedback by size
- S (radius <=0.5: Drone, mini-Drone, Tick, Splitter child): goo pop, 12 particles, body gone 0.8 s. No shake or dip. Light 0.25 haptic tick, at most every 120 ms screen-wide. 1-2 orbs.
- M (0.6-1.0: Leaper, Spitter, Weaver, Lancer, Mender, Shrieker, Stalker, Burrower, Splitter): 24 particles and a chunk, gone 1.2 s. Shake 0.04/0.1 s. Light 0.45. 3 orbs.
- L (Carapace, Bloater, Spore Pod, guards): 48 particles plus a decal, gone 2 s. Shake 0.10 (Bloater 0.16)/0.2 s. Dip 0.3 for 50 ms. Medium 0.6. 6 orbs in a ring.
- Elite: as its class, plus a 3 m pale ring shockwave (visual) and a 1.2 s light beam on its power-up drop. Shake 0.14/0.25 s. Dip 0.25 for 80 ms. Medium plus a light tail 120 ms later. 8 orbs.
- Mini-boss: giant goo burst, and adds within 6 m die. Shake 0.25/0.5 s. Dip 0.2 for 250 ms with a 200 ms ease. Heavy haptic. 12 orbs as a fountain.

Kill rules:
- No dip under 30% HP or during the telegraph window.
- Decals fade over 12 s, oldest first at 80.
- Kills within 0.1 s merge into one louder sound, max 3 voices.
- A Shrieker death releases a blue ring that dims its frenzied pack.
- A Mender death snaps its green tethers.
- Frozen kills shatter into blue shards with a glass sound.

## 2.3 Swarm Chain
Pool points per kill: S 1, M 2, L 5, Elite 10, Mini-boss 20. Near-miss +3, perfect dash +10. Boss damage gives none.

Drain: 1 + 0.08*pool per s. Taking damage removes 25% of the pool.

Tiers (pool, name, score mult, biomass mult, colour):
- 0: none, x1, grey
- 10: CHAIN, x1.25 / x1.125, white
- 25: RAMPAGE, x1.5 / x1.25, yellow
- 45: CARNAGE, x1.75 / x1.375, orange
- 70: EXTERMINATION, x2.0 / x1.5, red-gold

The multiplier is read at kill time. Biomass uses a fractional accumulator. Expect ~1.08-1.23 average biomass multiplier for 2-4 kills/s.

Chip:
- Left edge strip, 30% down, 64x28 dp. Shows "x1.5" with a 3 dp progress bar to the next tier; the drain shows as the bar shrinking.
- 60% opacity idle, 100% for 0.4 s on a kill. Hidden at tier 0 with an empty pool.
- Tier-up: scale 1-1.35-1 over 150 ms, tier colour, rising-pitch tick, light haptic (tier 4 medium), and a name callout at most once per 8 s per tier.
- Tier-down: dims and shrinks 8% over 200 ms, silent.
- Tier 4: faint red rim on the weapon.
- The chain carries across floors (frozen during cards and transitions), not across runs. It still runs on boss floors.

## 2.4 Callouts
One top-band queue, one at a time. Priority: boss > objective > Last Breath > chain tier > PURGED x5 (5 kills in 0.6 s) > 100 KILLS milestones. Lower ones wait up to 1 s, then are dropped.
- World-space floaters: ELITE DOWN above the corpse (0.8 s); PERFECT and CLOSE CALL above the hero (0.7 s).
- Tier name 22 sp/0.9 s, sliding in over 0.15 s. PURGED 24 sp/0.9 s.
- With the callouts setting Off, only boss, objective and Last Breath show.

## 2.5 Near-miss and dodge
- CLOSE CALL: a single-hit telegraphed attack lands within 0.9 m without hitting. Dip 0.6 for 120 ms, white streak ring, floater, light haptic, whoosh. +3 chain. Once per 1.5 s.
- PERFECT DASH: a dash starts within 0.25 s before a telegraphed attack on the hero lands and carries the hero out of its area (checked at impact). Dip 0.5 for 180 ms, gold trail, gold ring at impact, floater, medium haptic, chime. +10 chain and 33% of the dash cooldown refunded. Once per 2 s.
- Hit by a telegraphed attack: the telegraph flashes solid red for 0.1 s, and the chain loses 25%.

## 2.6 Hero damage
Hit:
- Hero flash white 60 ms.
- Red edge vignette 0.35 for 0.18 s.
- Shake 0.12 (<=10 HP) or 0.18, for 0.16 s.
- Haptic medium (heavy above 25 HP).
- Grunt with a 90 ms limit.
- Contact ticks: vignette 0.12 and a light haptic, no shake.

Low HP:
- <=40%: red edge pulse at 0.8 Hz, alpha 0.18, inner radius 62% so the centre stays clear.
- <=25%: 1.3 Hz, 0.28, saturation -10%, music low-pass 2.5 kHz, heartbeat at 75 bpm plus a haptic tick pair every 0.8 s.
- <=15%: 2.0 Hz, 0.35, saturation -25%, low-pass 1.2 kHz, heartbeat 110 bpm, haptic every 0.55 s.
- Heals reverse all of it in 0.3 s with a green edge flash 0.3, sparkles and a chime.

Last Breath feel: white-red flash 0.45/0.1 s, dip 0.4 for 0.5 s, shake 0.2, heavy haptic plus a triple tick, "LAST BREATH" in the top band in red for 1.2 s, and the hero flickers.

## 2.7 Bosses
Intro (the boss is invulnerable and passive until 1.8 s):
- 0-0.4 s: time ramps to 0.25. Camera dollies out 10% over 0.8 s. Vignette 0.25.
- 0.2 s: banner (name 36 sp, subtitle 16 sp).
- 0.4-1.4 s: roar. Shake ramps 0.05 to 0.2 with a rumble haptic, then a heavy tick.
- 1.4-1.8 s: time returns to 1. The health bar fills over 0.4 s (top centre, 50% width, notches at phase changes).
- The hero can move during it.
- Mini-boss intro: 0.8 s at 0.5, banner 1.0 s, no dolly.

Phase change:
- Dip 0.3 for 0.4 s, white flash 0.25/0.1 s, shake 0.2 (subject to the telegraph rule).
- Red rim on the boss, roar, heavy haptic.
- Phase word banner 14 sp for 1.0 s.

Kill (the hero is invulnerable from the final blow until 1.8 s; a tap after 0.5 s skips to 1.8):
- 0 s: flash 0.5/0.12 s, time 0.2, heavy haptic.
- 0-1.2 s: camera pushes in 8% towards 60% hero / 40% boss, keeping the hero 15% inside the edge. Shake 0.3 decaying over 1 s.
- 0.3 s: all remaining enemies die in a goo cascade (0.05 s stagger, no biomass lost).
- 0.5 s: the boss bursts at x1.5 size, with 12 orbs and 2 power-ups.
- 1.2 s: time ramps back over 0.5 s.
- 1.8 s: SECTOR CLEARED banner.

## 2.8 Stings (banner 20 sp, 1.0 s)
- PURGE done: green ring pulse, pickups fly to the hero, dip 0.3 for 150 ms, shake 0.08, success haptic, OBJECTIVE COMPLETE.
- SURVIVE end: shockwave, vacuum, shake 0.12, success.
- HOLD 100%: ring flares gold and collapses into the hero, dip 0.4 for 200 ms, UPLOAD COMPLETE.
- HUNT nest: 5 m goo ring, shake 0.14, medium haptic, "NEST 2/3".
- ESCAPE room: doors flash green, ROOM CLEAR. Lift: light ticks while charging, then success, FLOOR ESCAPED.
- Interact (hack/plant): a tick each quarter, medium at the end.
- Goal timer at 10 s: tracker bar goes amber, light tick.
- Floor clear: "FLOOR n CLEARED" 18 sp.
- Level-up: blue ring burst, pause after 0.25 s. After the pick, time runs 0.5 to 1 over 0.4 s, and any hostile telegraph active at the pause gets +0.3 s.

## 2.9 Drop-in (after the 3-2-1)
- 0 s: camera 6 m higher and 15 deg steeper; an orange pod streak falls.
- 0.9 s: impact dust ring 4 m, shake 0.25 over 0.5 s, flash 0.2/0.1 s, heavy haptic, boom.
- 0.9-1.7 s: camera eases into the play view; the hero rises.
- 1.2 s: controls unlock, the HUD fades in, and the zone banner shows.
- 2.5 s: first spawns. Keep an 8 m safe radius until controls unlock.
- Floors 2-5 arrive faster: camera in over 0.5 s, controls at 0.5 s, spawns at 1.5 s.

## 3.1 Grade (wins only), sub-scores 0-100
- Time: 100*(1.5*par - t)/(0.8*par), clamped. t is gameplay time only. Par is x1.15 on Hard and x1.3 on Nightmare.
- Safety: 100*(1 - f/0.8), where f = damage taken / (maxHP*5). Heals don't refund.
- Chain: 50*peakTier/4 + 50*min(1, shareOfTimeAtTier2+/0.5).
- Stars: 100*stars/3.
- Points = 0.30T + 0.30S + 0.25C + 0.15St. S >= 88, A 70-87, B 45-69, C < 45.
- Check: Cryo, par 210 s; 190 s, 60 dmg, peak tier 4, 2 stars = 79.7 (A).
- Swarm Gate: no grade, only the score and the floor reached.

## 3.2 Run score
RunScore = sum(kill points * chain mult) + 500*floorNumber per cleared floor + 30*max(0, 1.5*par - t) + 400 per flawless floor.
- Kill points: S 10, M 30, L 100, Elite 300, Mini 1000, Boss 4000.
- A typical Cryo win is 14-18k.
- Save a local run record: version, zoneId, tier, heroId, weaponId, weaponTier, combatPower, seed, win, floorsCleared, timeMs, damageTaken, peakChainTier, chainTimeShareMs[5], kills[5], starsEarned, grade, gradePoints, runScore.
- Personal bests (time, score, grade) per zone and tier show on the pin and the summary.

## 3.3 Grade rewards
- Credits bonus on the win Credits: C 0, B 5, A 12, S 25%.
- Commander XP: A +10, S +20.
- First S per zone and tier: 5 Cores.
- The best grade letter shows on the map pin and in the briefing.

## 3.4 Summary screen (win; a tap skips to the end state and never loses rewards; max 6 s)
1. 0 s: dimmed last frame. SECTOR CLEARED stamp with zone and tier (scale 1.6 to 1 in 0.2 s, shake 0.15, heavy haptic). Below it, time with par and a green/red delta, and the score.
2. 0.8 s: four bars fill on the left, 0.45 s each (TIME, SAFETY, CHAIN, STARS), each with a tick and a light haptic.
3. 2.6 s: the grade letter slams into the centre (2 to 1 in 0.18 s, heavy haptic, shake 0.15). S gets a gold ring and flash 0.3/0.2 s; A white-gold, B white, C grey. The verdict line comes from the weakest sub-score: Time "Faster next time.", Safety "Watch the telegraphs.", Chain "Keep the kills coming."; a top grade reads "Fast and clean."
4. 3.4 s: the star conditions tick off with chimes; new stars pop.
5. 4.2 s: rewards count up over 1.2 s on the right (Credits with a gold "+12%" grade chip, Parts, Alloy, Commander XP). Then gear flips one at a time, rarity-coloured. A FIRST CLEAR strip if relevant.
6. 5.6 s: one big pulsing button at 62-94% width, 70-90% height. Priority: UPGRADE (if any affordable CP upgrade) > EQUIP (if new gear beats the equipped piece) > NEXT ZONE: name (if this clear unlocked one) > RETRY (straight to the countdown). Small MAP and RETRY/MENU buttons beside it.

Defeat keeps SWARMED (power vs recommended, the three cheapest upgrades) and adds floor reached, run score, time and best chain. No grade, a 1.6 s count-up, no slam. The button is UPGRADE if affordable, else RETRY, else MAP.
