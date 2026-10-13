using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Drop zones as data. Each SectorDef carries its look (palette, light, fog, ambient particles), its floors
    // (layout templates, objective plan rules), its enemies (a weighted table per floor, elites, the boss), its hazards
    // (ids into Hazards.cs), and its progression (tier -> recommended power, rewards, unlock, star goals).
    // The planet shows every def as a pin; MapSelect.Sectors is built from these (plus the Escape Test map).
    public enum Particles { None, Snow, Spores, Ash, Rain, Embers }

    public class EnemyEntry { public string type; public float weight; public int fromFloor = 1; public EnemyEntry(string t, float w, int from = 1) { type = t; weight = w; fromFloor = from; } }

    // one authored floor of a spec zone (Design/zones-*.json)
    public class ZoneFloor
    {
        public Game.Goal goal; public string spec, param, layout, events, script;   // script: the floor event list (JSON, FloorEvents)
        public EnemyEntry[] enemies;
    }

    public class SectorDef
    {
        public string id, name, art, blurb;
        // spec zones (ZoneData.cs, generated): authored floors, mechanics, power, unlock-any, par, boss text
        public int num, power, par = 300; public string act, pitch, signature, bossBody, bossLook, starBonus;
        public bool endless;   // Swarm Gate: shuffled floors from cleared zones
        public ZoneFloor[] floors; public string[] mechanics = new string[0], modifiers = new string[0], unlockAny;
        public Dictionary<string, string> layoutNotes;
        public int tier;                       // difficulty step: recommended power and enemy scale x1.45 per tier
        public float lat, lon;                 // the pin on the holo planet
        public string unlockAfter;             // the sector to clear first (null = open)
        // look: tints over the facility materials (no new textures), light, fog, particles
        public Color floorTint = Color.white, wallTint = Color.white, accent = new Color(0.3f, 0.85f, 1f);
        public Color sun = new Color(1f, 0.96f, 0.92f), ambient = new Color(0.62f, 0.66f, 0.74f), fog = new Color(0.86f, 0.88f, 0.92f);
        public float sunIntensity = 1.25f, fogStart = 60f, fogEnd = 150f;
        public Particles particles = Particles.None;
        // floors: arena templates (indices into MazeMap.FloorTemplates, arenas only), escape chains, objective rules
        public int[] arenas = { 0, 1, 2, 3, 4, 5 }, escapes = { 6, 7 };
        public Game.Goal[] order;                                       // authored floor order (5 goals); null = drawn from the pool
        public Game.Goal first = Game.Goal.Purge;                       // floor 1
        public (Game.Goal goal, float weight)[] pool =                   // floors 2-4: weighted, no repeats
            { (Game.Goal.Survive, 1), (Game.Goal.Escape, 1), (Game.Goal.Hold, 1), (Game.Goal.Hunt, 1) };
        // enemies
        public EnemyEntry[] enemies = { new("grunt", 1f), new("brute", 0.12f, 2), new("leaper", 0.1f, 2), new("bloater", 0.05f, 2) };
        public float eliteChance = 0.03f;
        public string boss = "bloater";
        public string[] hazards = new string[0];
        public float rewardMul = 1f;
        public string[] stars = { "CLEAR THE SECTOR", "CLEAR UNDER 5:00", "CLEAR ABOVE 50% HP" };
    }

    public static class SectorDefs
    {
        static List<SectorDef> all;
        // the spec zones (ZoneData, generated from Design/zones-*.json) replace the first-pass 16 (kept in Legacy())
        public static List<SectorDef> All => all ??= ZoneData.Build();
        public static List<SectorDef> Legacy() => Build();
        public static SectorDef Get(string id) { foreach (var s in All) if (s.id == id) return s; return null; }

        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        static List<SectorDef> Build() => new()
        {
            // ---- tier 1
            new SectorDef { id = "cryo", name = "CRYO STATION", art = "cryo", tier = 1, lat = 53, lon = 17, blurb = "A frozen research deck, overrun.",
                floorTint = C(0xeef4fa), wallTint = C(0xdfe9f4), accent = C(0x7fd6ff), sun = C(0xf2f6ff), ambient = C(0x9fb4d0), fog = C(0xdfe8f2), particles = Particles.Snow,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.08f, 2), new("bloater", 0.04f, 2) } },
            new SectorDef { id = "rust", hazards = new[] { "turrets" }, name = "RUST YARDS", art = "kessler", tier = 1, lat = 22, lon = 58, unlockAfter = "cryo", blurb = "Scrapyards baking under a dead sun.",
                floorTint = C(0xe8d2bc), wallTint = C(0xc99a78), accent = C(0xff9a4a), sun = C(0xffe0b8), ambient = C(0xb09080), fog = C(0xd8b896), particles = Particles.Ash,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("brute", 0.18f, 1), new("bloater", 0.04f, 2) } },
            // ---- tier 2
            new SectorDef { id = "verdant", hazards = new[] { "acid" }, name = "VERDANT COLONY", art = "verdant", tier = 2, lat = 40, lon = -53, unlockAfter = "cryo", blurb = "A greenhouse colony gone feral.",
                floorTint = C(0xd8ecd0), wallTint = C(0xa8c8a0), accent = C(0x9cff6a), sun = C(0xf4ffe0), ambient = C(0x88a888), fog = C(0xb8d4b0), particles = Particles.Spores,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.16f, 1), new("bloater", 0.05f, 2) } },
            new SectorDef { id = "dust", hazards = new[] { "fog" }, name = "DUSTFALL OUTPOST", art = "kessler", tier = 2, lat = -8, lon = 84, unlockAfter = "rust", blurb = "Sandstorms and buried bunkers.",
                floorTint = C(0xf2e2c0), wallTint = C(0xd8c090), accent = C(0xffd060), sun = C(0xfff0c8), ambient = C(0xc0a880), fog = C(0xe8d2a0), fogStart = 40, fogEnd = 110, particles = Particles.Ash,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("brute", 0.16f, 1), new("leaper", 0.1f, 2) } },
            // ---- tier 3
            new SectorDef { id = "kessler", hazards = new[] { "vents" }, name = "KESSLER REFINERY", art = "kessler", tier = 3, lat = -21, lon = -89, unlockAfter = "verdant", blurb = "Fuel lines, furnaces and fire.",
                floorTint = C(0xe0d4c8), wallTint = C(0xb0a090), accent = C(0xff7a30), sun = C(0xffd8b0), ambient = C(0x9a8070), fog = C(0xc8a888), particles = Particles.Embers,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("brute", 0.2f, 1), new("bloater", 0.07f, 2) } },
            new SectorDef { id = "drowned", hazards = new[] { "toxicrain" }, name = "DROWNED CITY", art = "drowned", tier = 3, lat = -34, lon = 20, unlockAfter = "dust", blurb = "Flooded streets under endless rain.",
                floorTint = C(0xc8dce0), wallTint = C(0x90b0b8), accent = C(0x50e0ff), sun = C(0xd0e8ff), sunIntensity = 1.0f, ambient = C(0x708898), fog = C(0x8aa4b0), fogStart = 35, fogEnd = 105, particles = Particles.Rain,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.14f, 1), new("bloater", 0.08f, 2) } },
            // ---- tier 4
            new SectorDef { id = "neon", hazards = new[] { "dark", "conveyor" }, name = "NEON DOCKS", art = "drowned", tier = 4, lat = 8, lon = -128, unlockAfter = "kessler", blurb = "Night markets lit in magenta.",
                floorTint = C(0xc0b8d8), wallTint = C(0x8070a8), accent = C(0xff4ad8), sun = C(0xd8c0ff), sunIntensity = 0.95f, ambient = C(0x6a5890), fog = C(0x5a4878), fogStart = 40, fogEnd = 120, particles = Particles.Rain,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.2f, 1), new("brute", 0.1f, 2) } },
            new SectorDef { id = "sulfur", hazards = new[] { "vents", "acid" }, name = "SULFUR VENTS", art = "kessler", tier = 4, lat = -52, lon = 66, unlockAfter = "drowned", blurb = "Toxic yellow steam from the deep.",
                floorTint = C(0xe8ecb0), wallTint = C(0xc0c070), accent = C(0xe8ff40), sun = C(0xf8ffc0), ambient = C(0xa0a060), fog = C(0xc8c880), fogStart = 30, fogEnd = 100, particles = Particles.Embers,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("bloater", 0.1f, 1), new("leaper", 0.1f, 2) } },
            // ---- tier 5
            new SectorDef { id = "glass", hazards = new[] { "collapse" }, name = "GLASS DESERT", art = "kessler", tier = 5, lat = 66, lon = -100, unlockAfter = "neon", blurb = "Fused sand that shines like a mirror.",
                floorTint = C(0xfff4e4), wallTint = C(0xf0d8b0), accent = C(0xffb860), sun = C(0xfff8e8), sunIntensity = 1.45f, ambient = C(0xd8c0a0), fog = C(0xfff0d8), particles = Particles.Ash,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("brute", 0.22f, 1), new("leaper", 0.14f, 1) } },
            new SectorDef { id = "blacksite", hazards = new[] { "dark", "turrets" }, name = "BLACKSITE LABS", art = "hive", tier = 5, lat = -68, lon = -30, unlockAfter = "sulfur", blurb = "Lights out. Something got loose.",
                floorTint = C(0xa8b0bc), wallTint = C(0x707884), accent = C(0xff3040), sun = C(0xb0c0e0), sunIntensity = 0.7f, ambient = C(0x404858), fog = C(0x20242c), fogStart = 25, fogEnd = 80,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.22f, 1), new("bloater", 0.06f, 2) } },
            // ---- tier 6
            new SectorDef { id = "fungal", hazards = new[] { "acid", "fog" }, name = "FUNGAL DEEP", art = "verdant", tier = 6, lat = 28, lon = 150, unlockAfter = "glass", blurb = "Glowing spores in a buried garden.",
                floorTint = C(0xc8b8d8), wallTint = C(0x8a70a0), accent = C(0x60ffd0), sun = C(0xd0c0ff), sunIntensity = 0.9f, ambient = C(0x605878), fog = C(0x504068), fogStart = 30, fogEnd = 100, particles = Particles.Spores,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("bloater", 0.12f, 1), new("leaper", 0.12f, 2) } },
            new SectorDef { id = "magma", hazards = new[] { "vents", "collapse" }, name = "MAGMA FOUNDRY", art = "kessler", tier = 6, lat = -40, lon = 132, unlockAfter = "blacksite", blurb = "Molten metal, rivers of fire.",
                floorTint = C(0xd8b8a8), wallTint = C(0x885848), accent = C(0xff5020), sun = C(0xffb880), sunIntensity = 1.1f, ambient = C(0x804838), fog = C(0x702818), fogStart = 35, fogEnd = 110, particles = Particles.Embers,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("brute", 0.25f, 1), new("bloater", 0.08f, 2) } },
            // ---- tier 7
            new SectorDef { id = "elevator", hazards = new[] { "lowgrav", "conveyor" }, name = "ORBITAL ELEVATOR", art = "cryo", tier = 7, lat = 80, lon = 40, unlockAfter = "fungal", blurb = "A tether into the sky, crawling with them.",
                floorTint = C(0xd8e0ec), wallTint = C(0x98a8c0), accent = C(0x80a8ff), sun = C(0xe0e8ff), ambient = C(0x8090a8), fog = C(0x101828), fogStart = 50, fogEnd = 140,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.2f, 1), new("brute", 0.14f, 1), new("bloater", 0.08f, 2) } },
            new SectorDef { id = "crystal", hazards = new[] { "ice", "collapse" }, name = "CRYSTAL CAVERNS", art = "drowned", tier = 7, lat = -10, lon = -160, unlockAfter = "magma", blurb = "Violet crystal that sings when shot.",
                floorTint = C(0xd8d0f0), wallTint = C(0xa090d0), accent = C(0xc080ff), sun = C(0xe8d8ff), ambient = C(0x8070b0), fog = C(0x403060), fogStart = 30, fogEnd = 100, particles = Particles.Spores,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.24f, 1), new("bloater", 0.08f, 2) } },
            // ---- tier 8
            new SectorDef { id = "hive", hazards = new[] { "acid", "dark" }, name = "HIVE CORE", art = "hive", tier = 8, lat = 35, lon = 119, unlockAfter = "elevator", blurb = "The heart of the swarm.",
                floorTint = C(0xc8a8c8), wallTint = C(0x704870), accent = C(0xd040ff), sun = C(0xe0b0ff), sunIntensity = 0.9f, ambient = C(0x603060), fog = C(0x301028), fogStart = 30, fogEnd = 95, particles = Particles.Spores,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.2f, 1), new("brute", 0.15f, 1), new("bloater", 0.12f, 1) }, eliteChance = 0.08f },
            new SectorDef { id = "maw", hazards = new[] { "fog", "toxicrain", "collapse" }, name = "THE MAW", art = "hive", tier = 8, lat = -75, lon = 150, unlockAfter = "crystal", blurb = "Where the swarm is born. No one returns.",
                floorTint = C(0xd0a0a0), wallTint = C(0x803838), accent = C(0xff3020), sun = C(0xffa090), sunIntensity = 0.95f, ambient = C(0x602020), fog = C(0x400808), fogStart = 25, fogEnd = 90, particles = Particles.Embers,
                enemies = new EnemyEntry[] { new("grunt", 1f), new("leaper", 0.24f, 1), new("brute", 0.18f, 1), new("bloater", 0.14f, 1) }, eliteChance = 0.1f },
        };
    }
}
