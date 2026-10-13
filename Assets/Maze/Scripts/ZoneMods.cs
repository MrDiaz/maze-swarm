using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Zone modifiers in play (upgrade brief 9 / 11): Hard turns on the zone's first modifier, Nightmare the first two,
    // the Hot Zone adds the first. The zones' authored modifiers are text; each maps by its name onto a generic
    // mutator (the brief's mutator list) or an existing hazard. Unmapped names stay briefing text only.
    public static class ZoneMods
    {
        public const int Frenzy = 0, Armoured = 1, Brood = 2, AcidRain = 3, EliteHunt = 4, Blackout = 5, Glass = 6, Regen = 7, Storm = 8, Volatile = 9;
        public static readonly string[] Names = { "FRENZY", "ARMOURED SWARM", "BROOD SURGE", "ACID RAIN", "ELITE HUNT", "BLACKOUT", "GLASS CANNON", "REGENERATING HIVE", "LIGHTNING STORM", "VOLATILE" };
        public static readonly string[] Text =
        {
            "+25% ENEMY SPEED", "DRONES TAKE 60% MORE TO KILL", "+40% ENEMIES AND BIOMASS", "STANDING STILL 2 S BURNS 2 HP/S", "AN ELITE A FLOOR; ITS KILL IS A FREE CARD",
            "DARKNESS: ONLY LIT ENEMIES ARE TARGETS", "EVERYONE DEALS +50%", "NESTS AND BOSSES REGENERATE 1%/S", "TELEGRAPHED LIGHTNING STRIKES", "DEATHS BURST NEAR YOU",
        };
        // a zone modifier's name (before the ':') -> a mutator (or -1) and a hazard id (or null)
        static readonly (string key, int mut, string haz)[] Map =
        {
            ("Deep Freeze", -1, "ice"), ("Hatchery", Brood, null), ("Blizzard", -1, "fog"), ("Acid Rain", AcidRain, null),
            ("Power Surge", Storm, null), ("Abandoned Armoury", -1, "turrets"), ("Tick Infestation", Brood, null),
            ("Overdrive", Frenzy, null), ("Heat Wave", AcidRain, null), ("Press Gang", Armoured, null),
            ("Overgrowth", Brood, null), ("Pollen Storm", -1, "toxicrain"), ("Fertile Soil", Regen, null),
            ("Dead Batteries", Blackout, null), ("Flare Shortage", Blackout, null), ("Surge", Storm, null), ("Night Shift", Frenzy, null),
            ("Storm", Storm, null), ("Short Circuit", Storm, null),
            ("Live Cargo", Brood, null), ("Acid Downpour", AcidRain, null), ("Toxic Swarm", Volatile, null), ("Leaky Roofs", -1, "toxicrain"),
            ("Black Site", Blackout, null), ("Ghost Protocol", Blackout, null), ("Overclocked", Frenzy, null),
            ("Unstable", -1, "collapse"), ("Deep Dark", Blackout, null), ("Burrowing Swarm", Brood, null),
            ("Pandemic", Volatile, null), ("Overcrowded Ward", Brood, null), ("Field Medics", Regen, null),
            ("Superstorm", Storm, null), ("Sniper Nest", -1, "turrets"),
            ("Rotten Decking", -1, "collapse"), ("Pouncers", Frenzy, null), ("Ion Storm", Storm, null),
            ("Inferno", AcidRain, null), ("Rogue Defences", -1, "turrets"), ("Armoured Boarders", Armoured, null),
            ("Hot Core", -1, "vents"), ("Overpressure", Volatile, null), ("Containment Breach", Brood, null), ("Blackout", Blackout, null),
            ("Pitch Black", Blackout, null), ("Ambush Burrows", Frenzy, null), ("Egg Carpet", Brood, null), ("Stalker Pack", Frenzy, null),
            ("Fortified Hive", Regen, null), ("Acid Seep", AcidRain, null), ("Hive-linked Swarm", Regen, null),
            ("Overtime", -1, "@overtime"), ("Hard Tilt", -1, "@hardtilt"), ("High Tide", -1, "@hightide"), ("Gale Warning", -1, "@gale"), ("Hurricane", -1, "@hurricane"), ("Lockdown Protocol", -1, "@lockdown"),   // existing systems, applied at floor start
            ("Final Tide", Brood, null), ("Royal Guard", EliteHunt, null), ("Crumbling Spire", -1, "collapse"), ("Endless Brood", Brood, null),
        };
        public static string NameOf(string mod) { int i = mod.IndexOf(':'); return (i > 0 ? mod.Substring(0, i) : mod).Trim(); }
        public static (int mut, string haz) Lookup(string mod)
        {
            string n = NameOf(mod);
            foreach (var m in Map) if (n.StartsWith(m.key, System.StringComparison.OrdinalIgnoreCase)) return (m.mut, m.haz);
            return (-1, null);
        }
        public static int CountFor(int difficulty, bool hot) => Mathf.Max(difficulty == 2 ? 2 : difficulty == 1 ? 1 : 0, hot ? 1 : 0);

        // ---- the run's active set
        public static readonly bool[] On = new bool[10];
        public static readonly List<string> Hazards = new(), Active = new(), Specials = new();
        // the zone-specific modifiers on existing systems, each floor start (after Hazards.Begin)
        static float specialT;
        // OVERTIME: floors 1-4 the belts reverse every 15 s (1.5 s warning), each with 4 Drones + 2 Ticks; F4 the belts never stop.
        // HARD TILT: floors 1-3 and 5 the drift flips every 20 s (2 s hint), a Bloater on the new uphill side, a 0.3 s flash.
        public static void Tick(Game g, float dt)
        {
            if (Specials.Count == 0) return;
            float t0 = specialT; specialT += dt;
            bool over = Specials.Contains("@overtime") && g.floor <= 4, tilt = Specials.Contains("@hardtilt") && g.floor != 4 && Weather.I != null;
            if (Specials.Contains("@overtime") && g.floor == 4 && CellFloors.BeltDir == 0f) CellFloors.BeltDir = 1f;   // they never stop
            if (over && Crossed(t0, specialT, 15f, 1.5f)) g.hud.ShowHint("BELTS REVERSING");
            if (over && Crossed(t0, specialT, 15f, 0f))
            {
                if (CellFloors.BeltDir != 0f) CellFloors.BeltDir = -CellFloors.BeltDir;
                Pack(g, "drone", 4, null); Pack(g, "tick", 2, null); if (Game.SimRun) Debug.Log($"Maze overtime: F{g.floor} belts reversed ({CellFloors.BeltDir}) speed {CellFloors.BeltSpeed}");
            }
            if (tilt && Crossed(t0, specialT, 20f, 2f)) g.hud.ShowHint("THE KEEL IS SHIFTING");
            if (tilt && Crossed(t0, specialT, 20f, 0f))
            {
                Weather.I.DriftDir = -Weather.I.DriftDir; Feel.Flash(Color.white, 0.25f, 0.3f);
                Pack(g, "bloater", 1, g.player.p - Weather.I.DriftDir * 9f);   // uphill: against the drift
                if (Game.SimRun) Debug.Log($"Maze hard tilt: F{g.floor} drift flipped to {Weather.I.DriftDir} at {Weather.I.DriftSpeed} m/s");
            }
        }
        static bool Crossed(float a, float b, float period, float before) { float k = Mathf.Floor((b + before) / period); return k >= 1 && Mathf.Floor((a + before) / period) < k; }
        static void Pack(Game g, string type, int n, Vector2? near)
        {
            int ti = g.swarm.TypeByName(type); if (ti < 0) return;
            Vector2 at = near ?? (MazeMap.spawnPoints.Count > 0 ? MazeMap.spawnPoints[Random.Range(0, MazeMap.spawnPoints.Count)] : g.player.p + Random.insideUnitCircle.normalized * 9f);
            MazeMap.CollideGrid(ref at, 0.4f);
            for (int i = 0; i < n; i++) { var p = at + Random.insideUnitCircle * 1.2f; var e = g.swarm.Spawn(p, g.wave, ti); e.p = p; }
        }
        public static void ApplySpecials(Game g)
        {
            specialT = 0;
            foreach (var sp in Specials)
            {
                if (sp == "@hightide" && CellWater.I != null) { CellWater.I.Flood(0.5f); CellWater.I.Flood(0.5f); }   // every 'w' starts deep
                if (sp == "@gale" && Weather.I != null) Weather.I.SetWind(true, Weather.ParseDir(new List<object> { "E", "W", "N", "S" }), 12f, 4f);   // gusts every 12 s
                if (sp == "@hurricane" && Weather.I != null) Weather.I.SetWind(true, Weather.ParseDir(new List<object> { "E", "W", "N", "S" }), 8f, 3f);   // every 8 s, a random direction
                if (sp == "@overtime") { CellFloors.BeltSpeed = 5f; if (g.floor == 4) g.OvertimeSurvive(75f); }   // belts 5 m/s; F4 SURVIVE 75 s
                if (sp == "@hardtilt" && Weather.I != null) { Weather.I.Drift = true; Weather.I.DriftSpeed = 2f; if (Weather.I.DriftDir == Vector2.zero) Weather.I.DriftDir = Vector2.right; }   // drift 2 m/s
                if (sp == "@lockdown") foreach (var h in global::Maze.Hazards.Live) if (h is CellFences f) { f.OnSec = 6f; f.OffSec = 2f; }   // fences 6 s on, 2 s off
            }
        }
        public static void Setup(SectorDef s, int difficulty, bool hot)
        {
            for (int i = 0; i < On.Length; i++) On[i] = false; Hazards.Clear(); Active.Clear(); Specials.Clear();
            if (s == null || s.endless || s.modifiers == null) return;
            if (Ops.WeeklyOn)
            {   // the week's pair on every zone
                var (wa, wb) = Ops.WeeklyPair; On[wa] = On[wb] = true; Active.Add("WEEKLY: " + Names[wa] + " + " + Names[wb]);
                if ((wa == Blackout || wb == Blackout) && (s.hazards == null || System.Array.IndexOf(s.hazards, "dark") < 0)) Hazards.Add("dark");
            }
            int n = Mathf.Min(CountFor(difficulty, hot), s.modifiers.Length);
            for (int i = 0; i < n; i++)
            {
                var (mut, haz) = Lookup(s.modifiers[i]);
                if (mut >= 0) On[mut] = true;
                if (mut == Blackout && (s.hazards == null || System.Array.IndexOf(s.hazards, "dark") < 0) && !Hazards.Contains("dark")) Hazards.Add("dark");
                if (haz != null && haz.StartsWith("@")) Specials.Add(haz);
                else if (haz != null && (s.hazards == null || System.Array.IndexOf(s.hazards, haz) < 0)) Hazards.Add(haz);
                Active.Add(NameOf(s.modifiers[i]).ToUpperInvariant() + (mut >= 0 ? $" ({Names[mut]})" : haz != null ? "" : " (BRIEFING ONLY)"));
            }
            if (Game.SimRun && Active.Count > 0) Debug.Log($"Maze zone mods {s.id}: {string.Join(", ", Active)}  hazards+ {string.Join(",", Hazards)}");
        }
        public static void Clear() { for (int i = 0; i < On.Length; i++) On[i] = false; Hazards.Clear(); Active.Clear(); Specials.Clear(); }
        // the briefing line for a tier
        public static string Line(SectorDef s, int difficulty, bool hot)
        {
            if (s == null || s.endless || s.modifiers == null) return "";
            int n = Mathf.Min(CountFor(difficulty, hot), s.modifiers.Length); var l = new List<string>();
            for (int i = 0; i < n; i++) { var (mut, _) = Lookup(s.modifiers[i]); l.Add(NameOf(s.modifiers[i]).ToUpperInvariant() + (mut >= 0 ? ": " + Text[mut] : "")); }
            if (Ops.WeeklyOn) { var (wa, wb) = Ops.WeeklyPair; l.Add($"WEEKLY: {Names[wa]} + {Names[wb]}"); }
            return string.Join("  +  ", l);
        }
        public static string[] WithHazards(string[] baseIds)
        {
            if (Hazards.Count == 0) return baseIds;
            var l = new List<string>(baseIds ?? new string[0]); foreach (var h in Hazards) if (!l.Contains(h)) l.Add(h); return l.ToArray();
        }
    }
}
