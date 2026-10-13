using System;
using UnityEngine;

namespace Maze
{
    // Every gameplay / camera / balance knob. Lives on the Game component, so it can be tuned
    // live in the Inspector while playing. Defaults match the three.js prototype.
    [Serializable]
    public class MazeConfig
    {
        public CameraTuning camera = new CameraTuning();
        public PlayerTuning player = new PlayerTuning();
        public EnemyTuning enemy = new EnemyTuning();
        public EnemyType[] enemyTypes =
        {
            new EnemyType { name = "grunt",  model = "drone", hp = 1.0f,  speed = 1.0f,  scale = 1.0f, damage = 1.0f, weight = 1.0f,  fromWave = 1, color = new Color32(0x2a, 0x2c, 0x33, 255), score = 10 },
            // Leaper (the reworked runner): stalks to ~7 m, crouches with a telegraph, pounces ~8 m in a line (LeaperTuning).
            // On a schedule like the Bloater (weight 0). ~104 HP at any wave: 4-5 rifle hits. Model: Meshy from
            // Concepts/Enemies/leaper.png, rigged + animated in Blender (Tools/leaper_rig.py: Walk, Crouch, Leap, Land,
            // Sidestep), ~1.2 m tall hunched with scythe arms.
            new EnemyType { name = "leaper", model = "leaper", hp = 2.6f, speed = 1.15f, scale = 1.35f, stride = 1.2f, damage = 1.0f, weight = 0, fromWave = 999, color = new Color32(0xc8, 0x4a, 0x22, 255), score = 35, leaper = true, fixedHp = true, knockback = 0.8f },
            new EnemyType { name = "brute",  hp = 3.6f,  speed = 0.72f, scale = 1.4f, damage = 2.2f, weight = 0.12f, fromWave = 3, color = new Color32(0x14, 0x15, 0x19, 255), score = 40 },
            // not in the random mix (weight 0): Bloaters come on a schedule (BloaterTuning), minis from Bloaters.
            // Bloater: ~2.4 m wide, ~1.6 m/s, 420 HP at wave 3 (~17 AK hits), barely shoved. Mini: a small
            // green Drone, ~4.5 m/s, 20 HP at every wave (1 AK hit / 2 SMG hits), a small bite.
            new EnemyType { name = "bloater", model = "bloater", hp = 8.75f, speed = 0.43f, scale = 2.5f, damage = 2f, weight = 0, fromWave = 999, color = new Color32(0x6c, 0xc2, 0x2a, 255), score = 150, bloater = true, knockback = 0.15f, stride = 0.76f },
            // HUNT floors' hive nest: rooted, ~600 HP at sector 1 (~25 rifle hits), keeps spawning Drones until it dies
            new EnemyType { name = "nest",   hp = 60f, speed = 0f, scale = 1.7f, damage = 0f, weight = 0, fromWave = 999, color = new Color32(0x3a, 0x1c, 0x4e, 255), score = 100, nest = true, fixedHp = true, knockback = 0f },
            new EnemyType { name = "mini",   model = "drone", hp = 0.5f,  speed = 1.22f, scale = 0.55f, damage = 0.4f, weight = 0, fromWave = 999, color = new Color32(0x9c, 0xe0, 0x6a, 255), score = 5, mini = true, fixedHp = true },
            // ---- the enemies.json roster (placeholder looks: a base model, tint, scale). hp = spec RH / 2 (a Drone),
            // damage = spec contact / 8, never in the random wave mix (weight 0): zone tables name them
            new EnemyType { name = "carapace",  ai = "carapace",  model = "bloater", hp = 5f,    speed = 0.6f,  scale = 1.9f,  damage = 1.5f,  weight = 0, fromWave = 999, color = new Color32(0x3a, 0x40, 0x48, 255), score = 60, knockback = 0.3f, stride = 0.76f },
            new EnemyType { name = "spitter",   ai = "spitter",   model = "drone",   hp = 2f,    speed = 0.8f,  scale = 1.2f,  damage = 1f,    weight = 0, fromWave = 999, color = new Color32(0x9b, 0xc5, 0x3d, 255), score = 30 },
            new EnemyType { name = "burrower",  ai = "burrower",  model = "leaper",  hp = 2.5f,  speed = 1.4f,  scale = 1.2f,  damage = 1f,    weight = 0, fromWave = 999, color = new Color32(0x8b, 0x6b, 0x4a, 255), score = 40, stride = 1.2f },
            new EnemyType { name = "shrieker",  ai = "shrieker",  model = "drone",   hp = 3f,    speed = 0.7f,  scale = 1.1f,  damage = 0.8f,  weight = 0, fromWave = 999, color = new Color32(0xd8, 0xcb, 0xb8, 255), score = 50 },
            new EnemyType { name = "tick",      ai = "tick",      model = "drone",   hp = 0.5f,  speed = 1.6f,  scale = 0.45f, damage = 0.5f,  weight = 0, fromWave = 999, color = new Color32(0xd0, 0x39, 0x9c, 255), score = 8 },
            new EnemyType { name = "weaver",    ai = "weaver",    model = "leaper",  hp = 2.5f,  speed = 0.9f,  scale = 1.08f, damage = 0.8f,  weight = 0, fromWave = 999, color = new Color32(0xec, 0xe4, 0xd0, 255), score = 40, stride = 1.2f },
            new EnemyType { name = "lancer",    ai = "lancer",    model = "drone",   hp = 2.5f,  speed = 0.7f,  scale = 1.2f,  damage = 1f,    weight = 0, fromWave = 999, color = new Color32(0x1f, 0x4f, 0x55, 255), score = 50 },
            new EnemyType { name = "splitter",  ai = "splitter",  model = "bloater", hp = 3f,    speed = 0.8f,  scale = 1.12f, damage = 1f,    weight = 0, fromWave = 999, color = new Color32(0xe8, 0x6a, 0xa6, 255), score = 20, stride = 0.76f },
            new EnemyType { name = "splitter_m",ai = "splitter",  model = "bloater", hp = 1.5f,  speed = 1.0f,  scale = 0.75f, damage = 0.75f, weight = 0, fromWave = 999, color = new Color32(0xe8, 0x6a, 0xa6, 255), score = 10, stride = 0.76f },
            new EnemyType { name = "splitter_s",ai = "splitter",  model = "bloater", hp = 0.5f,  speed = 1.4f,  scale = 0.5f,  damage = 0.5f,  weight = 0, fromWave = 999, color = new Color32(0xe8, 0x6a, 0xa6, 255), score = 5, stride = 0.76f },
            new EnemyType { name = "mender",    ai = "mender",    model = "drone",   hp = 2.5f,  speed = 0.8f,  scale = 1.1f,  damage = 0.6f,  weight = 0, fromWave = 999, color = new Color32(0x7f, 0xd8, 0xa6, 255), score = 50 },
            new EnemyType { name = "stalker",   ai = "stalker",   model = "leaper",  hp = 3f,    speed = 1.3f,  scale = 1.35f, damage = 1f,    weight = 0, fromWave = 999, color = new Color32(0x14, 0x12, 0x18, 255), score = 60, stride = 1.2f },
            // data bosses (BossBrain, Resources/Design/bosses.json): a stand-in body per base model, HP / size / speed set on spawn
            new EnemyType { name = "boss_leaper",   ai = "boss", model = "leaper",  hp = 10f, speed = 1f, scale = 1.35f, damage = 1.5f, weight = 0, fromWave = 999, color = new Color32(0x8f, 0x1d, 0x1d, 255), score = 500, fixedHp = true, knockback = 0.1f, stride = 1.2f },
            new EnemyType { name = "boss_bloater",  ai = "boss", model = "bloater", hp = 10f, speed = 1f, scale = 2.5f,  damage = 1.5f, weight = 0, fromWave = 999, color = new Color32(0x4b, 0x1f, 0x5c, 255), score = 500, fixedHp = true, knockback = 0.05f, stride = 0.76f },
            new EnemyType { name = "boss_drone",    ai = "boss", model = "drone",   hp = 10f, speed = 1f, scale = 1.0f,  damage = 1.5f, weight = 0, fromWave = 999, color = new Color32(0x6f, 0x9a, 0x2a, 255), score = 500, fixedHp = true, knockback = 0.1f },
            new EnemyType { name = "boss_vanguard", ai = "boss",                    hp = 10f, speed = 1f, scale = 1.6f,  damage = 1.5f, weight = 0, fromWave = 999, color = new Color32(0x2d, 0x32, 0x38, 255), score = 500, fixedHp = true, knockback = 0.1f },
            new EnemyType { name = "spore_pod", ai = "spore_pod", hp = 4f,    speed = 0f,    scale = 1.1f,  damage = 0f,    weight = 0, fromWave = 999, color = new Color32(0x4f, 0x5a, 0x2a, 255), score = 30, nest = true, fixedHp = true, knockback = 0f },
        };
        public BloaterTuning bloater = new BloaterTuning();
        public LeaperTuning leaper = new LeaperTuning();
        public WaveTuning waves = new WaveTuning();
        public PickupTuning pickups = new PickupTuning();
        public BarrelTuning barrels = new BarrelTuning();
        public Weapon[] weapons =
        {
            // reach = auto-fire circle radius, range = how far bullets fly, moveMul = walk speed while equipped
            // magSize / reloadTime: rounds per magazine and a full reload (the shotgun loads shell by shell)
            //   sized with Editor/MazeSim so an idle player / the demo bot lose at most ~1 wave vs bottomless mags
            //   RIFLE: longest reach, accurate, steady damage, heaviest (slowest walk)
            //   SMG: medium reach, huge fire rate but sprays, light
            //   SHOTGUN: short reach, devastating close burst and knockback, lightest (fastest walk)
            new Weapon { name = "RIFLE",   color = new Color32(0xf5, 0xc4, 0x00, 255), rate = 7f,   damage = 24f, pellets = 1, spread = 0.02f, speed = 80f, knockback = 1.8f, range = 30f, reach = 15f, moveMul = 0.9f,  shake = 0.06f, magSize = 36, reloadTime = 1.8f },
            new Weapon { name = "SHOTGUN", color = new Color32(0x3a, 0xa8, 0xff, 255), rate = 1.5f, damage = 16f, pellets = 9, spread = 0.30f, speed = 60f, knockback = 5f,   range = 10f, reach = 7f,  moveMul = 1.08f, shake = 0.38f, magSize = 8,  reloadTime = 2.4f, perShell = true },
            new Weapon { name = "SMG",     color = new Color32(0xff, 0x6a, 0x3d, 255), rate = 16f,  damage = 11f, pellets = 1, spread = 0.13f, speed = 68f, knockback = 0.9f, range = 16f, reach = 10f, moveMul = 1f,    shake = 0.04f, magSize = 60, reloadTime = 1.0f },
        };
    }

    public static class BlueprintWeapons
    {   // upgrade step 9: blueprint weapons (upgrade brief 3), appended at startup (the scene's serialized config predates them)
        public static void Ensure(MazeConfig cfg)
        {
            var l = new System.Collections.Generic.List<Weapon>(cfg.weapons);
            if (!l.Exists(w => w.name == "FRAG")) l.Add(new Weapon { name = "FRAG", kind = "frag", color = new Color32(0xff, 0x9a, 0x3a, 255), rate = 1.4f, damage = 70f, pellets = 1, spread = 0.02f, speed = 24f, knockback = 1f, range = 14f, reach = 12f, moveMul = 0.92f, shake = 0.1f, magSize = 4, reloadTime = 2.2f });
            if (!l.Exists(w => w.name == "LANCE")) l.Add(new Weapon { name = "LANCE", kind = "lance", color = new Color32(0x7a, 0xe8, 0xff, 255), rate = 10f, damage = 12f, pellets = 1, spread = 0.01f, speed = 90f, knockback = 0.2f, range = 11f, reach = 11f, moveMul = 0.95f, shake = 0.01f, magSize = 60, reloadTime = 2.0f });
            if (!l.Exists(w => w.name == "THERMAL")) l.Add(new Weapon { name = "THERMAL", kind = "thermal", color = new Color32(0xff, 0x5a, 0x1e, 255), rate = 12f, damage = 9f, pellets = 3, spread = 0.5f, speed = 30f, knockback = 0.1f, range = 7f, reach = 6f, moveMul = 0.9f, shake = 0.02f, magSize = 120, reloadTime = 2.6f });
            if (!l.Exists(w => w.name == "RAIL")) l.Add(new Weapon { name = "RAIL", kind = "rail", color = new Color32(0xb0, 0x8c, 0xff, 255), rate = 0.7f, damage = 260f, pellets = 1, spread = 0f, speed = 120f, knockback = 2f, range = 30f, reach = 14f, moveMul = 0.85f, shake = 0.15f, magSize = 5, reloadTime = 2.5f });
            if (!l.Exists(w => w.name == "NEEDLER")) l.Add(new Weapon { name = "NEEDLER", kind = "needler", color = new Color32(0x3a, 0xf0, 0xd0, 255), rate = 14f, damage = 12f, pellets = 1, spread = 0.07f, speed = 38f, knockback = 0.3f, range = 14f, reach = 12f, moveMul = 1.08f, shake = 0.01f, magSize = 48, reloadTime = 1.4f });   // the Scout's Twin Needlers
            cfg.weapons = l.ToArray();
        }
    }

    [Serializable]
    public class CameraTuning
    {
        public float fov = 30f;          // narrow FOV = flatter, near-isometric look
        public float distance = 40f;     // closer than the web build: phone screens are small (pinch zooms)
        public float minDistance = 24f, maxDistance = 70f;
        public float pitch = 57f;        // degrees above the horizon
        public float yaw = -14f;         // rotation around the player (gives the slanted walls)
        public float follow = 5f;        // follow stiffness
        public float lead = 0f;          // lean toward the aim direction (0 = camera follows only the player)
        public float shake = 0.5f;
    }

    [Serializable]
    public class PlayerTuning
    {
        public float hp = 100f, speed = 7.2f, radius = 0.42f, modelHeight = 1.6f;
        public float dashSpeed = 24f, dashTime = 0.16f, dashCooldown = 6f; // was 1 s
        public float contactPush = 0.15f; // how much the swarm shoves the player
        public float topUpIdle = 1.2f;    // reload a part-empty magazine after this long with nothing in reach
    }

    [Serializable]
    public class EnemyTuning
    {
        public float hp = 40f, speed = 3.7f, radius = 0.4f;
        public float damage = 7f, attackCooldown = 0.85f, attackReach = 0.3f;
        public float knockbackDecay = 7f, flowRefresh = 0.15f;
    }

    [Serializable]
    public class EnemyType
    {
        public string name;
        public string model;   // Resources/Models/<model>.glb (skinned, with a "Walk" clip); null = blocky robot
        public float hp, speed, scale, damage, weight;
        public int fromWave, score;
        public Color color;
        public bool bloater, mini, leaper, nest, fixedHp;   // fixedHp: no per-wave HP growth
        public string ai;                     // EnemyAI behaviour (the enemies.json roster); null = a plain walker
        public float knockback = 1f;          // multiplier on bullet knockback
        public float stride;                  // ground per second of Walk clip at model scale 1 (0 = the Drone's 1.25)
    }

    [Serializable]
    public class LeaperTuning
    {
        public int fromWave = 4;                       // Cryo: 1 at wave 4-5, 2 at 6-7, 3 from 8 (spread through the wave)
        public int CountFor(int wave) => wave < fromWave ? 0 : Mathf.Min(3, 1 + (wave - fromWave) / 2);
        public float range = 7f, minRange = 2.2f;      // pounces from inside this, with a clear line to the hero
        public float crouch = 0.6f, lockAt = 0.38f;    // telegraph; it tracks the hero until lockAt, then the line is fixed
        public float distance = 8f, time = 0.45f;      // the pounce: straight line, stops at walls
        public float hitDamage = 3.0f, knock = 11f;    // x enemy damage (7) = ~21 HP; shove along the line
        public float missStun = 0.8f, hitRecover = 0.5f, cooldown = 2.2f;
        public float dodgeChance = 0.35f, dodgeCooldown = 2.5f, dodgeSpeed = 7f; // sidestep when shot (stalking only)
    }

    [Serializable]
    public class BloaterTuning
    {
        public int fromWave = 3;               // 1 per wave, 2 from wave 5, 3 from wave 8
        public int twoFrom = 5, threeFrom = 8;
        public float spawnEvery = 5f, spawnRange = 18f, castTime = 0.7f; // stops, swells, bursts out minis
        public int minisMin = 2, minisMax = 3, capPerBloater = 6, capTotal = 30;
        public int deathMinisMin = 3, deathMinisMax = 4;
        public float deathShake = 0.85f;       // a barrel is 0.7
        public int CountFor(int wave) => wave < fromWave ? 0 : wave >= threeFrom ? 3 : wave >= twoFrom ? 2 : 1;
    }

    [Serializable]
    public class WaveTuning
    {
        public int firstCount = 14, perWave = 9;
        public float hpGrowth = 0.10f, speedGrowth = 0.035f, maxSpeedMult = 1.5f;
        public int maxAlive = 90;         // lower than web (110) for phones
        public float spawnInterval = 0.2f, breakTime = 3.5f, minSpawnDistance = 18f;
    }

    [Serializable]
    public class PickupTuning { public float dropChance = 0.05f, heal = 25f, life = 18f; }

    [Serializable]
    public class BarrelTuning
    {
        public float hp = 30f;                 // rifle: 2 shots, shotgun: 1 close blast
        public float height = 1.35f, collide = 0.46f;
        public int perSpot = 2;                // each 'X' on the map holds this many barrels
        public float radius = 4.8f;            // blast radius
        public float enemyDamage = 140f, playerDamage = 40f; // at the center; 30% at the edge
        public float knockback = 14f, fuse = 0.12f;
        public float chainRadius = 5f, chainDelay = 0.18f;
        public float shake = 0.7f;
        public bool respawnEachWave = true;
    }

    [Serializable]
    public class Weapon
    {
        public string name;
        public Color color;
        public float rate, damage, spread, speed, knockback, range, shake;
        public float reach = 12f, moveMul = 1f;
        public int pellets;
        public int magSize = 30;          // 0 = bottomless
        public float reloadTime = 1.8f;
        public bool perShell;             // shells go in one by one (reloadTime / magSize each)
        public string kind;               // blueprint weapons: "frag" (an arcing blast), "lance" (a ramping, slowing beam)
    }
}
