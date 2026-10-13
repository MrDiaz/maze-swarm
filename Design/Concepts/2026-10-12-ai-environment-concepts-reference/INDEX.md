# Maze environment concepts — reference only

These 19 images are newly AI-generated aspirational environment concepts based on the exact current Maze zone roster: 18 campaign zones plus Swarm Gate (endless). They are visual targets for the next environment art planning pass. They are not actual gameplay screenshots, finished 3D assets, or evidence that the game already renders at this quality.

Keep the current gameplay work, mechanics, progression, builds, and tasks moving. This handoff supplies context for later art planning; it does not request an abrupt map rewrite or immediate wholesale implementation.

## Shared visual targets

- An elevated gameplay camera that makes combat routes and arena structure readable.
- A tiny operative relative to the environment, with clear traversal and combat space.
- Strong, distinct architecture and silhouettes for each zone, rather than palette swaps.
- Richer materials, purposeful lighting, contact shadows, environmental depth, and restrained atmosphere that keeps enemies and hazards legible.
- HUD-free reference views. The absence of HUD in these images is a presentation choice, not a request to remove the game's working HUD.

## How to use these references

Treat the images as mood, material, lighting, scale, and architecture references. Preserve the zone mechanics and current progress. Do not treat rendered paths as exact collision layouts or assume that every depicted bridge, prop, enemy, or architectural feature must be reproduced. Adapt promising visual ideas to the mobile performance budget and available assets, with staged art planning before implementation.

The existing design JSON and runtime zone data remain authoritative for mechanics, objectives, progression, and collision layouts. The concepts can guide choices of modular architecture, prop families, materials, and lighting. Where the art implies a mechanic or asset that does not exist, record that as a planning idea rather than changing gameplay automatically.

## Complete reference index

| # | Exact zone | Image | Existing design theme |
|---|---|---|---|
| 1 | Cryo Station | [01-cryo-station.png](01-cryo-station.png) | A frozen research outpost where the floor fights you as much as the swarm: ice sheets, cryo vents and the first egg fields. |
| 2 | Evac Deck | [02-evac-deck.png](02-evac-deck.png) | The Escape Test map grown into a zone: a burning evacuation deck of sealed rooms, hijacked turrets and laser fences between you and the last shuttle. |
| 3 | Kessler Refinery | [03-kessler-refinery.png](03-kessler-refinery.png) | A slag refinery still running on autopilot: conveyor belts carry you and the swarm toward molten channels and hydraulic presses. |
| 4 | Verdant Colony | [04-verdant-colony.png](04-verdant-colony.png) | A farming dome gone feral: hedgerows you can shoot over but not walk through, spore pods that hide the swarm, and the first hive creep crawling between the crops. |
| 5 | Blackout Labs | [05-blackout-labs.png](05-blackout-labs.png) | A research wing with the power cut: you see only what your light, your flares and the pylons you relight can reach, and something in the dark is hunting you. |
| 6 | Drowned City | [06-drowned-city.png](06-drowned-city.png) | A flooded colony district where the tide is the clock: water slows you, live cables turn it into a trap, and on one floor a flood surge chases you to the lift. |
| 7 | Hadley Cargo Port | [07-hadley-cargo-port.png](07-hadley-cargo-port.png) | A storm-lashed container terminal still running on autopilot: belts herd the swarm, cranes drop steel boxes that rebuild the map mid-fight, and the cargo lifts throw you between split decks. |
| 8 | Acid Marsh | [08-acid-marsh.png](08-acid-marsh.png) | A drowned agri-dome turned into a green, steaming swamp where the sky itself burns: run between roofs when the rain comes, and push the creep back with fire. |
| 9 | Helix Data Vault | [09-helix-data-vault.png](09-helix-data-vault.png) | A cold, humming server fortress the hive is trying to read: steal its cores through laser lattices and dark aisles while cloaked Stalkers hunt you between the racks. |
| 10 | Deepcut Mine | [10-deepcut-mine.png](10-deepcut-mine.png) | A collapsing deep-core mine where the swarm swims through rock: read the dust, ride the ore cart through the dark, blow the shaft behind you and face the thing that ate the mine. |
| 11 | St. Aster Quarantine | [11-st-aster-quarantine.png](11-st-aster-quarantine.png) | A sealed hospital where the infection has started rebuilding the building: hive walls grow across corridors, toxic vents breathe on a rhythm, and the healers on the other side keep the swarm alive. |
| 12 | Tyndall Relay Spire | [12-tyndall-relay-spire.png](12-tyndall-relay-spire.png) | A comms tower above the clouds in a permanent lightning storm: hide under steel from the sky, snipe the snipers, and hold the uplink on dishes where the wind wants to throw you off the edge. |
| 13 | Skyhook Anchor | [13-skyhook-anchor.png](13-skyhook-anchor.png) | The ground station of a space elevator, bolted to a mesa over a 2 km drop: every gust of wind is a weapon, and the swarm has never learned to hold on. |
| 14 | Wreck of the Valiant | [14-wreck-of-the-valiant.png](14-wreck-of-the-valiant.png) | A fleet carrier that fell out of orbit and broke its back on the plateau, still burning, still tilting, its guns still looking for targets. |
| 15 | Halvorsen Reactor | [15-halvorsen-reactor.png](15-halvorsen-reactor.png) | The colony's fusion plant, ninety seconds from going critical and full of things that like the heat: turn the valves, save the pumps, scram the core and get out. |
| 16 | The Burrows | [16-the-burrows.png](16-the-burrows.png) | Below the reactor the swarm has dug a city: pitch-black tunnels, egg-lined walls that breathe, and things that hunt by sound. |
| 17 | Hive Core | [17-hive-core.png](17-hive-core.png) | The living centre of the infestation: a cathedral of flesh where the floor grows back under your feet and the walls have a pulse. |
| 18 | Broodmother's Spire | [18-broodmothers-spire.png](18-broodmothers-spire.png) | A kilometre-tall tower of chitin grown out of the hive, with the Broodmother at the top: every mechanic you have learned, in the order that hurts most, and then her. |
| 19 | Swarm Gate (endless) | [19-swarm-gate-endless.png](19-swarm-gate-endless.png) | Endless Breach: the swarm's own gate, and every floor of every zone you have cleared stitched into one descent that only ends when you do. |

## Source and integrity

Images were materialized from the user's confirmed ChatGPT Library references. All 19 files passed PNG signature, chunk CRC, decompression, byte count, and Library identity checks before installation. Original image bytes are preserved. See provenance.json for canonical Library filenames, IDs, versions, dimensions, byte counts, hashes, and runtime zone IDs.

Concept filename numbering uses 19 for Swarm Gate for browsing convenience; the runtime stores Swarm Gate as num: 0. Zone design sources are Design/zones-1-7.json, Design/zones-8-12.json, Design/zones-13-16.json, and Design/zones-17-19.json; the generated runtime roster is Assets/Maze/Scripts/ZoneData.cs.

These references were installed in a new folder under Design/Concepts. No game assets or source files were overwritten.
