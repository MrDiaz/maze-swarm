# Upgrade and replay system (condensed; the formulas are canonical)

## Decisions
- CP = round(1000 * sqrt(D*H) * (1 + 0.01*talentsPicked + setBonus% + relic% + capstones%)). This replaces the phone formula. CP is derived on load, so saves need no migration.
  - D = weaponLevelMult * (1+gearDmg%) * (1+researchDmg%) * (1+heroLevel%)
  - H = (1+gearHP%) * (1+researchHP%) * (1+heroLevel%)
  - weaponLevelMult = 1 + 0.08*(weaponLevel-1) + 0.05*(perks unlocked)
  - New Sentinel with the rifle and no gear = exactly 1000. setBonus is +2% per active 2-piece and +5% per 4-piece. A relic gives +2% per level. Each capstone gives +3%.
  - Armory buttons show "+N CP" before buying.
- RP per zone: zone 1 = 900, then x1.10 for zones 2-4, x1.15 for 5-10, x1.17 for 11-18 (use the drop-zones RP you already have). Hard x1.9, Nightmare x3.4. Enemy HP and damage are multiplied by threat = RP/1000, and floors still ramp 1.0-1.6.
  - Win-chance target for tuning: p = 1/(1+e^(-28*(CP/RP-0.91))), which gives 30% at 88% and 70% at 94%.
  - Briefing colour: green when CP >= RP, amber 85-99%, red below.
- Merge: Common to Rare 3, Rare to Epic 3, Epic to Legendary 4, Legendary to Mythic 5. Same slot and rarity, any set. The player picks which input's set the result keeps. The result keeps the highest input level, capped.
- Weapon levels are per weapon. Catch-up: a weapon below half your highest weapon level costs 25% until it reaches that half.
- Iron Vanguard stays free. The Scout unlocks at 30 Scout shards, which first drop in Drowned City (zone 6). Show the Scout as a locked silhouette with a shard bar until the model is routed.
- Relic slot (6th) opens at 60 stars.
- Cores come only from play. Sinks: blueprint exchange (40 Cores = 1 fragment, 2 per day) and Daily Op reroll (10).

## 1. Currencies and run rewards
- Credits, Weapon Parts, Alloy, Gear, Relics, Hero Shards (per hero), Blueprint fragments (per new weapon; 10 unlock it), Cores, Commander XP.
- Win on Normal: Credits = 160*1.15^(z-1), Parts = 6*1.10^(z-1), Alloy = 5*1.11^(z-1). Hard pays x1.6, Nightmare x2.4.
- A death pays 20% per floor reached (30% with Deep Pockets maxed).
- First clear per tier: Credits x3, Parts x2, plus Cores (Normal 10, Hard 20, Nightmare 40).
- Gear: 2 pieces per win (+1 on a first clear). A death on floor 3+ gives 1 piece.
- Blueprint fragments in blueprint zones: Normal win 30% for 1, Hard 1, Nightmare 2.
- Commander XP = 12 per floor reached + 10 per run + 50 per first clear. Grade XP bonuses stack on top.
- Shards: the boss drops 1-3 of the zone's featured hero on Normal, x2 Hard, x3 Nightmare.

## 2. Zone feed table (z: featured hero | sets | blueprint | relic)
1 Sentinel | Frostline+Bulwark | - | Cryo Heart
2 Vanguard | Bulwark+Overclock | - | Override Key
3 Sentinel | Overclock+Scavenger | - | Slag Furnace
4 Vanguard | Hivebreaker+Scavenger | - | Spore Lantern
5 Sentinel | Volt+Overclock | - | Dark Lens
6 Scout | Volt+Frostline | Frag Launcher | Tide Bell
7 Sentinel | Scavenger+Bulwark | Frag Launcher | Crane Hook
8 Vanguard | Hivebreaker+Scavenger | Frag Launcher | Acid Gland
9 Scout | Volt+Bulwark | Frag Launcher | Helix Drive
10 Sentinel | Overclock+Scavenger | Cryo Lance | Seismic Charge
11 Vanguard | Hivebreaker+Bulwark | Cryo Lance | Quarantine Seal
12 Scout | Volt+Overclock | Cryo Lance | Relay Coil
13 Sentinel | Volt+Frostline | Cryo Lance | Tether Spike
14 Vanguard | Overclock+Bulwark | Thermal Projector | Valiant Dog Tags
15 Scout | Overclock+Volt | Thermal Projector | Reactor Core
16 Sentinel | Hivebreaker+Scavenger | Thermal Projector | Burrower Fang
17 Vanguard | Hivebreaker+Juggernaut | Thermal Projector | Hive Pulse
18 Scout | Juggernaut+Hivebreaker | Rail Cannon | Brood Crown
Swarm Gate: all heroes, all sets.

## 3. Weapons
- Level L to L+1: Credits = round(250*1.075^(L-1)), Parts = floor(L/2)+2.
- Each level is +8% damage. Every 10th level unlocks a perk, also +5% in CP.
- Tiers raise the level cap. Each is a one-off purchase gated by a zone clear, with a "Mk" stamp and a plate colour (grey/green/blue/purple/gold):
  - Mk I: cap 20
  - Mk II: cap 40, clear zone 5, 2000 Credits / 40 Parts
  - Mk III: cap 60, zone 10, 8000/120. Unlocks the alt evolution.
  - Mk IV: cap 80, zone 15, 25000/250
  - Mk V: cap 100, zone 18 plus any Nightmare clear, 70000/500
- Perk ladder: 10 +15% reload; 20 +10% crit chance; 30 signature 1; 40 start the run with the first evolution card at level 1; 50 +25% crit damage; 60 signature 2; 70 +10% fire rate; 80 evolutions need one fewer card level; 90 signature 3; 100 Mastered (gold skin, +10% damage, kill counter).
- Signatures (Lv30 / 60 / 90):
  - Rifle: +1 Pierce / first shot after a reload crits / kills have a 10% chance to refund 3 rounds
  - SMG: +15% move speed while firing / every 30th bullet ricochets / reload 40% faster
  - Shotgun: +2 pellets / +40% at 2 m or closer / shove stuns 0.5 s
  - Minigun: spin-up 30% faster / bullets travel 20% further / taking damage while firing heals 1 HP (once per s)
  - Frag: +0.5 m blast / blasts pull 1 m inward / +2 drum
  - Cryo Lance: slow +1 s / ramp 30% faster / frozen enemies take +25% from all sources
  - Thermal: burn stacks +1 / cone 15% wider / burning deaths spread burn
  - Rail: charge 20% faster / slugs shove 2 m / full-charge slugs leave a lightning trail
  - Needlers: +1 needle per volley / pops +30% / homing range +3 m
- Evolutions (base / alt at Mk III+). When both are possible, the gold card offers both:
  - Rifle: Railgun (Pierce max + Range max) / Burst DMR (Crit max + Fire Rate max): 3-round bursts, the first target always crits
  - SMG: Arc SMG (Ricochet max) / Twin Vipers (Multishot max + Fire Rate max): dual streams
  - Shotgun: Breacher (Shove max) / Dragon's Breath (Range max + Fire Rate max): burning floor for 3 s
  - Minigun: Inferno Gatling (Fire Rate max): instant spin and fire floor / Shredder (Pierce max + Heavy Rounds max): pierce 2 and +20% damage taken
  - Frag: Cluster Mortar (Multishot max): 4 bomblets / Sticky Charges (Pierce max + Crit max): the 3rd stack detonates for x3
  - Cryo Lance: Absolute Zero (Pierce max): freezes the line 1 s / Shatter Lance (Crit max + Range max): frozen enemies shatter for 40% HP to neighbours
  - Thermal: Napalm Sprayer (Range max): +3 m and burning pools / Plasma Torch (Crit max + Fire Rate max): +60% to elites
  - Rail: Annihilator (Crit max): every slug crits and explodes / Gauss Repeater (Fire Rate max + Multishot max): 3 slugs, 0.4 s charge
  - Needlers: Hornet Swarm (Ricochet max): pops seek 3 targets / Venom Needles (Crit max + Pierce max): poison spreads on death
- New weapons at Lv1, using MazeConfig fields (reach / range / moveMul / rate / damage / magazine / reload):
  - Frag Launcher (Sentinel): 12 / 14 / 0.92 / 1.4 / 70 / 4 / 2.2. Arcing grenade, 2.5 m blast, 50% at the edge, shove 1 m. Reuse the grenade code.
  - Cryo Lance (Sentinel): 11 / 11 / 0.95 / 10 / 12 / 60 / 2.0. Beam that ramps to x2 after 2 s on one target and slows 30%.
  - Thermal Projector (Vanguard): 6 / 7 / 0.9 / 12 / 9 / 120 / 2.6. 60° cone. Burn stack 6 dmg/s, max 5, 3 s. Ignores armour.
  - Rail Cannon (Vanguard): 14 / 30 / 0.85 / 0.7 / 260 / 5 / 2.5. 1.2 s charge included in the rate. Pierces everything.
  - Twin Needlers (Scout): 12 / 14 / 1.08 / 14 / 9 / 48 / 1.4. Homing 10°/s. Every 6th needle on a target pops for +60.
- Tier deltas (Mk II / III / IV / V; tiers never add damage):
  - Frag: +1 mag / +0.3 m blast / rate +10% / +1 mag and +0.3 m blast
  - Lance: reach +1 / ramp 1.6 s / slow 40% / reach +1 and ramp to x2.5
  - Thermal: reach +0.5 / 6 stacks / cone 70° / reach +0.5 and burn 8 dmg/s
  - Rail: charge 1.1 / +1 mag / reach +1 and range +5 / charge 0.9
  - Needlers: +8 mag / pop every 5th / rate +10% / +8 mag and pop +90

## 4. Gear
- Five slots, main stat fixed per slot:
  - Helmet HP%: Epic affix +10% pickup radius; Legendary affix Shield card +1 charge
  - Vest HP%: Epic 8% less damage from Bloater and boss blasts; Legendary +1 HP regen every 4 s
  - Gloves Dmg%: Epic +5% fire rate; Legendary +5% crit chance
  - Boots HP% plus move speed (+2/3/4/5/6% by rarity): Epic dash cooldown -0.5 s; Legendary dash passes through enemies and shoves them
  - Implant Dmg%: Epic +10% crit damage; Legendary level-ups heal 10%
  - Mythic doubles both affixes.
- Main stat at level L = base*(1+0.10*(L-1)).
  - Common: base 2%, cap 10, salvage 5 Alloy
  - Rare: 4%, cap 15, salvage 15
  - Epic: 7%, cap 20, salvage 50
  - Legendary: 11%, cap 25, salvage 150
  - Mythic: 16%, cap 30, salvage 400
- Level cost L to L+1: Credits = round(80*1.12^(L-1)), Alloy = round(4*1.13^(L-1)), times the rarity multiplier (Common 1, Rare 1.5, Epic 2.2, Legendary 3.2, Mythic 4.5).
- Salvage also returns 80% of the Alloy spent levelling. Long-press locks a piece. MERGE ALL never consumes equipped or locked pieces.
- Drop odds (Common / Rare / Epic / Legendary / Mythic %). Hard counts as zone+6, Nightmare as zone+12, and neither drops Common. Rows are zone: Normal | Hard | Nightmare:
  - 1: 73.7/23.5/2.8/0/0 | 0/91.4/8.6/0/0 | 0/81.7/17.2/1.2/0
  - 2: 71.6/24.8/3.6/0/0 | 0/90.2/9.8/0/0 | 0/79.4/19.0/1.6/0
  - 3: 69.3/26.2/4.5/0/0 | 0/88.9/11.1/0/0 | 0/77.0/20.9/2.1/0
  - 4: 66.9/27.7/5.4/0/0 | 0/87.5/12.5/0/0 | 0/74.3/23.0/2.6/0
  - 5: 64.3/29.2/6.4/0/0 | 0/85.7/13.9/0.4/0 | 0/71.5/25.3/3.2/0
  - 6: 61.6/30.9/7.5/0/0 | 0/83.8/15.5/0.7/0 | 0/69.5/26.8/3.7/0
  - 7: 58.7/32.7/8.6/0/0 | 0/81.7/17.2/1.2/0 | 0/68.6/27.3/4.0/0
  - 8: 55.6/34.6/9.8/0/0 | 0/79.4/19.0/1.6/0 | 0/67.8/27.8/4.3/0
  - 9: 52.2/36.7/11.1/0/0 | 0/77.0/20.9/2.1/0 | 0/67.1/28.3/4.7/0
  - 10: 48.6/38.9/12.5/0/0 | 0/74.3/23.0/2.6/0 | 0/66.3/28.7/5.0/0
  - 11: 44.6/41.1/13.9/0.4/0 | 0/71.5/25.3/3.2/0 | 0/65.6/29.1/5.2/0.1
  - 12: 40.2/43.5/15.5/0.7/0 | 0/69.5/26.8/3.7/0 | 0/64.8/29.4/5.5/0.3
  - 13: 35.6/46.1/17.2/1.2/0 | 0/68.6/27.3/4.0/0 | 0/64.1/29.8/5.7/0.4
  - 14: 30.5/48.9/19.0/1.6/0 | 0/67.8/27.8/4.3/0 | 0/63.5/30.1/6.0/0.5
  - 15: 25.1/51.9/20.9/2.1/0 | 0/67.1/28.3/4.7/0 | 0/62.8/30.4/6.2/0.6
  - 16: 19.2/55.1/23.0/2.6/0 | 0/66.3/28.7/5.0/0 | 0/62.2/30.7/6.4/0.7
  - 17: 12.8/58.7/25.3/3.2/0 | 0/65.6/29.1/5.2/0.1 | 0/61.7/31.0/6.6/0.8
  - 18: 9.2/60.3/26.8/3.7/0 | 0/64.8/29.4/5.5/0.3 | 0/61.1/31.2/6.8/0.9
- Sets (2-piece / 4-piece):
  - Frostline: +10% damage to slowed / shots slow 15% for 1.5 s, and a slowed death chills enemies within 2 m
  - Hivebreaker: +15% to elites, nests and bosses / nests and bosses +25%, elite kills drop a 10 HP orb
  - Overclock: dash cooldown -15% / for 3 s after a dash, +40% fire rate and instant reload
  - Bulwark: +10% max HP / shield every 6 s instead of 10, and it shoves 3 m when it breaks
  - Scavenger: +40% pickup radius / every 60 biomass drops a supply pod (heal 8 or refill), +10% Credits
  - Volt: Ricochet +1 / every 8th shot arcs to 4 enemies for 60%
  - Juggernaut: standing still 1 s gives -20% damage taken / standing still gives +25% fire rate and no shove
- Relics: one per zone boss. 35% per Hard win, every Nightmare win. Levels 1-5 come from duplicates, or cost Alloy = 500*1.11^(z-1)*level. Values between L1 and L5 are interpolated. Each level is +2% CP. Only one can be equipped. Effects (L1 → L5):
  - Cryo Heart: dash leaves a cryo vent that freezes 1 → 1.8 s
  - Override Key: captured turrets follow you 10 → 20 s, hacks 30 → 60% faster
  - Slag Furnace: kills 5 → 12% chance of a 3 s slag pool
  - Spore Lantern: poison cloud around you every 10 → 6 s
  - Dark Lens: +15 → 40% damage beyond 8 m
  - Tide Bell: shove within 4 m every 12 → 7 s
  - Crane Hook: dash pulls the nearest elite 3 m and stuns it 0.5 → 1.2 s
  - Acid Gland: hits make enemies take +8 → 20% for 3 s
  - Helix Drive: every 4th → 3rd level-up offers an extra rare card
  - Seismic Charge: floor start clears 5 → 8 m after 2 s
  - Quarantine Seal: below 30% HP a 2 → 4 s bubble, once per floor
  - Relay Coil: every 15th → 8th shot calls a lightning strike
  - Tether Spike: bosses slowed 10 → 25%
  - Valiant Dog Tags: revive once per run at 25 → 60% HP
  - Reactor Core: +1% fire rate per 10 biomass this floor, up to 10 → 25%
  - Burrower Fang: pierce 1 → 2 extra burrowed enemies and reveal them
  - Hive Pulse: elite kills heal 5 → 12%
  - Brood Crown: 1 → 3 mini-Drone allies, respawning every 10 s

## 5. Research
Four branches, 4 tiers per branch, 4 levels per node. Credits per level = base*1.35^(lvl-1). Base by tier: 600 / 6000 / 45000 / 220000, times 1.1 for Tactics and Logistics. Tiers open at rank 3 / 7 / 11 / 15. Buying a node needs the node above it at level 2+.
- Firepower: Calibrated Rounds +3% damage | Precision Optics +5% damage (+0.5 m range at max) | Depleted Cores +7% (+10% crit damage at max) | Plasma Jacketing +9%
- Armor: Reinforced Plating +3% HP | Nano Weave +5% (+1 HP every 5 s at max) | Reactive Armor +7% (-10% contact damage at max) | Titan Frame +9%
- Tactics: Biomass Magnet +10% radius and first level-up 10% cheaper | Field Rerolls +1 reroll | Second Wind revive at 30/45/60/75% HP | Commander's Pick 4 cards on the first 2/4/6/all level-ups
- Logistics: Salvage Rights +5% Credits | Alloy Refinery +6% Alloy | Quartermaster +5% chance of an extra gear drop | Deep Pockets deaths pay +2.5% per floor
- Capstones: rank 18, 300000 Credits, +3% CP each, need the whole branch maxed. Opening Salvo (start with 1 random Weapon card), Last Stand (below 25% HP take 30% less damage), Tactical Cache (4th crate in caches: pick 1 of 3 weapon cards), Contracts (+1 Daily Op and +10% Cores).
- No randomness anywhere in Research. Tactics and Logistics buttons say "no CP change" plus the effect line.

## 6. Commander Rank
- XP to next = round(80*rank^1.35). Each rank gives 10 Cores, 50 on every 5th rank.
- Unlocks by rank: 2 Daily Ops, 3 Research T1 (plus one free level of Calibrated Rounds), 4 Hot Zone, 5 Hard, 6 Star Track rewards, 7 Research T2, 8 Weekly Mutation and Hive Hunt, 10 Nightmare, 11 Research T3, 15 T4, 18 capstones, 20 Prestige frame.

## 7. Heroes
- Level L to L+1 costs round(400*1.11^(L-1)) Credits. Each level gives +2% damage and +2% HP. Talent rows open at levels 5, 10 ... 50.
- Stars raise the cap and cost shards:
  - 1★ cap 10: unlock (Scout 30 shards), ability rank 1
  - 2★ cap 20: 20 shards, ability rank 2
  - 3★ cap 30: 40 shards, passive
  - 4★ cap 40: 80 shards, ability rank 3
  - 5★ cap 50: 160 shards, 2nd passive and the Ascended look
- Squad Cohesion: every 10 levels across your other heroes give +1% HP.
- Daily Ops chest: 5 shards of choice.
- Abilities (rank 1 / rank 2 / rank 3, then the 3★ and 5★ passives). The Sentinel keeps Flash Freeze, which you built, in place of the spec's Frag Volley; apply the rank structure to Flash Freeze (rank 2: 2 s shorter cooldown, rank 3: +1 m radius).
  - Vanguard Barrage (15 s): 3 s of +100% rate and no shove / 4 s, 13 s cooldown / rounds pierce all. 3★: 15% less damage while firing. 5★: ends with a 5 m shockwave.
  - Sentinel passives: 3★ dash reloads the magazine. 5★ kills within 2 s after a dash refund 0.5 s of dash cooldown.
  - Scout Decoy (14 s): hologram pulls aggro 3 s / 4 s, 12 s cooldown / explodes for 300% weapon damage. 3★: dash range +40% and 1 s invisible. 5★: hitting a decoy-targeting enemy always crits.
- Talents: two choices per row, respec is free, each pick +1% CP.
  - Sentinel:
    - (5) Steady Aim +15% when still / Run and Gun no move penalty
    - (10) Field Medic biomass heals 1 / Scavenger +15% Credits
    - (15) +1 frag (use Freeze: +1 m radius) / ability cooldown -2 s
    - (20) dash frost trail / dash 0.5 s invulnerable
    - (25) +1 reroll / start 1 level ahead
    - (30) first time below 20% heal 30% / below 30% +30% fire rate
    - (35) evolution cards one level earlier / both evolutions in one run
    - (40) +20% vs bosses / +10% per 5 enemies within 5 m (max 30%)
    - (45) caches offer 4 / 15% chance of +1 gear drop
    - (50) +10% damage and HP / +1 card choice
  - Vanguard:
    - (5) keeps 50% spin / +15% HP
    - (10) -10% contact damage / dash shoves 2 m
    - (15) +40% magazine / +25% damage after 3 s of firing
    - (20) immune to pounce shove / 30% of damage taken returns as a shockwave every 5 s
    - (25) Heavy Rounds more often / barrage +1 s
    - (30) Shield card also +10% damage / +25% HP and -5% speed
    - (35) burns +1 s / shove +50%
    - (40) barrage cooldown -3 s / barrage heals 15%
    - (45) revive at 50% / below 25% no shove and +20% damage
    - (50) +20% HP and size / Cohesion doubled
  - Scout:
    - (5) +10% speed / +2 m range
    - (10) +15% reload / +20% vs Leapers and Spitters
    - (15) decoy gives 1 s invisibility / 2 decoy charges
    - (20) first hit marks for +15% / every 5th volley double
    - (25) rare cards +20% / 15% dodge of contact damage
    - (30) +25% for 2 s after a dash / +25% at max range
    - (35) poison and burn +30% / +1 multishot at start
    - (40) decoy taunts bosses / +30% crit vs stunned
    - (45) dash cooldown -1.5 s / crits +50%
    - (50) +15% damage and range / every 10 s the next dash is free

## 8. Difficulty and stars
- Hard needs the zone cleared on Normal and rank 5. It adds elites from floor 2, the zone's first modifier always on, no Common gear, relic 35%, a guaranteed blueprint fragment and shards x2.
- Nightmare needs the zone cleared on Hard and rank 10. It adds the zone's first two modifiers, telegraphs 30% shorter, a relic every win, possible Mythic drops and shards x3.
- 3 stars per tier: clear / no revive and under par / the zone's signature challenge. That is 9 per zone, 162 in total.
- Star Track (account-wide, always show the next three rewards):
  - Every 10 stars (10, 30, 50 ...): 2000*(stars/10) Credits, 40 Parts and a Rare+ piece
  - 20, 40, 80 ...: 30 Cores, 10 shards of choice and an Epic+ piece
  - 60: Relic slot plus a Cryo Heart gift
  - 100: Legendary piece of choice
  - 130: "Whiteout" Sentinel skin (placeholder tint)
  - 162: Mythic of choice, gold planet, "Swarm Breaker" title

## 9. Rotating content
- Daily Ops (rank 2): 3 missions a day, reset 04:00 local. 1 free reroll, then 10 Cores. Completing all 3 opens a chest with 5 shards of choice, 20 Cores and about 3 runs' worth of Credits.
  - factor = 1.15^(furthest zone - 1)
  - Pool: clear 3 zones 1000*f Cr / kill 300 enemies 20*f Parts / pick 2 evolutions 30*f Alloy / clear a Hard zone 10 Cores / earn 2 new stars 10 Cores / kill 5 Bloaters without being burst 20*f Alloy / finish a run with each owned hero 5 shards / clear the Hot Zone 15 Cores / open 4 caches 15*f Parts / merge 3 pieces 30*f Alloy / upgrade 5 times 800*f Cr / win without a Survival card 10 Cores / a zone's bonus challenge 15 Cores
- Hot Zone (rank 4): one cleared zone a day, never one of the last 3. One positive mutator (Overclocked / Supply Lines / Bounty / Evolution Rush, not yesterday's) plus the zone's first modifier. x2 of one material by weekday: Cr Mon/Thu, Parts Tue/Fri, Alloy Wed/Sat, featured-hero shards Sun. Orange HOT pin.
- Weekly (rank 8):
  - Weekly Mutation: 2 mutators on all zones, +25% Credits, first clear of a zone that week pays 20 Cores (max 5).
  - Bounties: 3 zone bonus challenges, 100*f Alloy each; all 3 give an Epic+ piece in a chosen slot.
  - Hive Hunt: 3 bosses in a row at the frontier RP, 3 attempts, best counts. Brackets: 50% → 15 Cores + Rare + 3 shards; 100% → 30 + Epic + 5; 200% → 50 + Epic in a chosen slot + 10; 300% → 80 + Legendary in a chosen slot + 15.
- Mutators:
  - Volatile: death bursts for 10% max HP
  - Frenzy: +25% enemy speed
  - Armoured Swarm: Drones take at least 2 hits
  - Brood Surge: +40% enemies and +40% biomass
  - Acid Rain: standing still 2 s deals 2 HP/s
  - Elite Hunt: 1 elite per floor that drops a free card
  - Blackout: 10 m light radius
  - Glass Cannon: everyone deals +50%
  - Overclocked: level-ups 30% faster
  - Supply Lines: take 2 of 3 caches
  - Bounty: +1 Credit per kill
  - Evolution Rush: evolutions need one fewer card level
  - Regenerating Hive: nests and bosses regenerate 1%/s
  - Lightning Storm: random telegraphed strikes
- Swarm Gate rewards: 5*wave Alloy (use floor reached in your build). Cores at waves 10/20/30: 25/50/100, once a week. A relic of choice at 40, once a week. Show the best on the main menu.

## 10. Achievements (toasts at run end only)
- 20 Cores: First Drop (clear zone 1); Evolved; Near Miss (win under 5% HP)
- 30 Cores: Alt Path; Squad Up (every hero); Bloater Popper (500 Bloaters); Leap of Faith (dodge 200 pounces); Purple Rain (50k biomass); Comeback (clear a zone you failed 3 times)
- 40 Cores: Full Set (a 4-piece bonus)
- 50 Cores: All Four (also title Gunsmith); Scout's Honour; Landfall (zone 5); Hardened (5 Hard clears); Starstruck (50 stars); Perfectionist (9 stars in a zone); Untouchable (no damage on floor 5); Speedrunner (a clear under 2:30); Daily Grind (Daily Ops 7 days in a row); Bounty Hunter (12 Bounties)
- 75 Cores: Outbreak (zone 10); Relic Hunter (6 relics); Swarm Breaker (10k kills); Researcher (a capstone); Gatekeeper (Swarm Gate 30)
- 100 Cores: Five-Star (also title Ascended); Deep Infection (zone 15); Nightmare Fuel; Mythic (equip one); Mk V
- 150 Cores: Queen Slayer (zone 18, also title Voidwalker); Mastered (a level-100 weapon, also a gold frame)

## 12. Defeat screen
List the 3 cheapest affordable upgrades by CP gain per Credit and show the gap closed ("1,560 / 1,800 → 1,690 / 1,800") with an UPGRADE ALL 3 button. If nothing is affordable, point to the Hot Zone if it's cleared, otherwise the best Credits-per-minute cleared zone. Suggest the untried branch sibling if there is one.

## 15. Screens
- Armory:
  - Top bar: currencies plus hero portrait and CP.
  - Left: weapon card with Mk plate, level/cap, next perk and LEVEL UP (+CP), which becomes TIER UP or shows the gate. Weapon tabs; locked weapons show blueprint x/10 and where they drop. Tapping the card opens the evolutions panel, with the alt greyed until Mk III.
  - Right: gear doll with 5 slots plus the relic slot ("★ 60" lock). The item sheet has stats, affixes, set progress, LEVEL UP, SWAP and LOCK.
  - Bottom tabs: EQUIPMENT | INVENTORY | MERGE (stacks, set picker, MERGE ALL, SALVAGE).
  - A red dot only when something affordable gives CP, never on Cores items.
- Research: 4 columns with tiers as rows and rank locks, level pips, a node sheet with BUY, gold capstones, and a header with rank, XP bar and next unlock.
- Squad: carousel. Locked heroes show a shard bar and "Drops in". LEVEL UP, STAR UP, ability card, passives, Cohesion line, a TALENTS tab with free RESET, and SELECT. The selected hero shows everywhere.
- Briefing: NORMAL/HARD/NIGHTMARE selector, 3 stars for the tier, drop table (set icons, shard hero, blueprint, relic silhouette, first-clear tick) and modifiers.

## 17. Save additions
commander{rank,xp}; currencies{credits,parts,alloy,cores}; weapons{id:{owned,level,tier,blueprintFragments}}; heroes{id:{owned,level,stars,shards,talents[10]}}; gear[{uid,slot,rarity,level,set,locked,equippedBy}]; research{node:level}; zones{id:{stars[9],attempts,normal/hard/nightmare:{cleared,bestTime,bestGrade,bestScore}}}; relics{id:level}, equippedRelic; starTrackClaimed; achievements; daily{date,missions,rerollsUsed,chestClaimed}; weekly{weekId,bounties,hiveHuntAttempts,hiveBest}; swarmGate{bestFloor,weekMilestones}

## 18. Build order (each step shippable)
1. Numbers swap: CP formula, reward formulas by zone RP, and the 3-upgrade defeat panel.
2. Commander Rank and Research tiers 1-2.
3. Hero levels and stars, shards from bosses, Cohesion, ability ranks.
4. Weapon tiers and perks, catch-up, alt evolutions on the gold card.
5. Difficulty tiers and stars: briefing selector, Hard/Nightmare multipliers and extras, star pins, Star Track, Cores.
6. Gear depth: sets, affixes, merge ratios, salvage, lock, MERGE ALL, drop odds. Then relics.
7. Daily Ops, Hot Zone (start with 6 mutators), achievements.
8. Talents.
9. Blueprints and new weapons (Frag first). Scout as a locked silhouette until the model arrives.
10. Weekly content.
11. Bot tuning to 30% at 88% RP and 70% at 94% RP.

Placeholder UI is fine (no new 3D). Get as far down this list as you can, installing whenever the phone shows up. Send me a short note at each 2-3 steps.
