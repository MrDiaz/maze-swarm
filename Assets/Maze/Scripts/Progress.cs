using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Meta progression, saved on the device (PlayerPrefs, JSON): currencies, weapon levels, gear, cleared sectors.
    // Combat Power sums hero + weapon + gear; a sector's Recommended Power grows x1.45 per sector (sector 1: 900 vs a
    // new player's 1000). Damage / HP multipliers come from the same sources, so being short of power stretches
    // time-to-kill (at 75% of the recommended power a Bloater takes ~4/3 the hits).
    [Serializable]
    public class GearItem
    {
        public int uid, slot, rarity, level = 1;
        public int set = -1; public bool locked;   // gear set (Meta.Sets, -1 none) and the long-press lock
    }

    [Serializable]
    public class SaveData
    {
        public int credits, parts, alloy, sectorsCleared, nextUid = 1, runs;
        public int[] weaponLv = { 1, 1, 1, 1, 1, 1, 1, 1, 1 };   // RIFLE, SHOTGUN, SMG, MINIGUN, FRAG, LANCE, THERMAL, RAIL
        public List<GearItem> gear = new();
        public int[] equipped = { 0, 0, 0, 0, 0 };        // uid per slot (0 = empty)
        public int cores;                                 // premium-ish currency (first S grades, first clears; upgrade phase 3)
        public List<string> cleared = new();              // drop zones cleared (SectorDef ids)
        public List<string> stars = new();                // earned stars as "id:index" (Hard / Nightmare: "id:tier:index")
        // upgrade phase 3 (Meta.cs): Commander Rank, Research, heroes, weapon tiers, difficulty clears, the Star Track
        public int rank = 1, cxp, starTrackClaimed, nightmareClears;
        public List<string> research = new(), heroLv = new(), heroStars = new(), shards = new(), tierCleared = new();
        public int[] weaponTier = { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        public List<string> blueprints = new();   // blueprint fragments as "WEAPON:n" (10 unlock it)
        public List<string> relics = new(); public int equippedRelic = -1;   // relics as "index:level"
        // Ops.cs: lifetime counters, achievements, Daily Ops
        public List<string> stats = new(), achievements = new(), daily = new(), talents = new();   // talents as "hero.row:choice"
        public string dailyDay, dailyLast, title, weeklyKey; public List<string> weeklyClears = new(); public int dailyRerolls, dailyStreak; public bool dailyChest;
    }

    public static class Progress
    {
        public static readonly string[] Slots = { "HELMET", "VEST", "GLOVES", "BOOTS", "IMPLANT" };
        public static readonly bool[] SlotIsDamage = { false, false, true, false, true }; // main stat: HP or damage
        public static readonly string[] Rarities = { "COMMON", "RARE", "EPIC", "LEGENDARY", "MYTHIC" };
        public static readonly Color[] RarityColors =
        {
            new Color(0.72f, 0.76f, 0.82f), new Color(0.3f, 0.65f, 1f), new Color(0.72f, 0.38f, 1f), new Color(1f, 0.72f, 0.18f), new Color(1f, 0.25f, 0.3f),
        };
        public static int MaxLevel(int rarity) => Meta.GearCap[rarity];   // (upgrade brief: main stat 2/4/7/11/16%, +10% of it a level)
        public static readonly string[] WeaponNames = { "RIFLE", "SHOTGUN", "SMG", "MINIGUN", "FRAG", "LANCE", "THERMAL", "RAIL", "NEEDLER" };
        public static readonly string[] WeaponModels = { "AK-47", "SHOTGUN", "SMG", "MINIGUN", "FRAG LAUNCHER", "CRYO LANCE", "THERMAL PROJECTOR", "RAIL CANNON", "TWIN NEEDLERS" };

        static SaveData d;
        public static SaveData D { get { if (d == null) Load(); return d; } }
        const string Key = "ms.progress";

        public static void Load()
        {
            var json = PlayerPrefs.GetString(Key, "");
            try { d = string.IsNullOrEmpty(json) ? new SaveData() : JsonUtility.FromJson<SaveData>(json); } catch { d = new SaveData(); }
            if (d.weaponLv == null || d.weaponLv.Length < 9) { var w = new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }; if (d.weaponLv != null) Array.Copy(d.weaponLv, w, Mathf.Min(9, d.weaponLv.Length)); d.weaponLv = w; }
            if (d.equipped == null || d.equipped.Length < 5) d.equipped = new int[5];
            d.gear ??= new List<GearItem>();
            d.cleared ??= new List<string>(); d.stars ??= new List<string>();
            d.research ??= new(); d.relics ??= new(); d.stats ??= new(); d.weeklyClears ??= new(); d.talents ??= new(); d.achievements ??= new(); d.daily ??= new(); d.heroLv ??= new(); d.heroStars ??= new(); d.shards ??= new(); d.tierCleared ??= new();
            if (d.weaponTier == null || d.weaponTier.Length < 9) { var t = new int[9]; if (d.weaponTier != null) Array.Copy(d.weaponTier, t, Mathf.Min(9, d.weaponTier.Length)); d.weaponTier = t; } d.blueprints ??= new(); if (d.rank < 1) d.rank = 1;
            // (older saves counted cleared tiers: the first zones of those tiers count as cleared)
            if (d.sectorsCleared > 0 && d.cleared.Count == 0) foreach (var z in SectorDefs.All) if (z.tier <= d.sectorsCleared && z.unlockAfter == null || z.tier < d.sectorsCleared) d.cleared.Add(z.id);
        }
        public static bool Sim;   // the editor RunSim: a throwaway save (never written)
        public static void Save() { if (Sim) return; PlayerPrefs.SetString(Key, JsonUtility.ToJson(D)); PlayerPrefs.Save(); }
        public static void UseTemp(SaveData t)
        {   // (sims / captures) the same padding a load gets: 9 weapon slots
            d = t;
            if (d.weaponLv == null || d.weaponLv.Length < 9) { var w = new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }; if (d.weaponLv != null) Array.Copy(d.weaponLv, w, Mathf.Min(9, d.weaponLv.Length)); d.weaponLv = w; }
            if (d.weaponTier == null || d.weaponTier.Length < 9) { var tt = new int[9]; if (d.weaponTier != null) Array.Copy(d.weaponTier, tt, Mathf.Min(9, d.weaponTier.Length)); d.weaponTier = tt; }
        }
        public static void ResetAll() { d = new SaveData(); Save(); } // Settings: the hidden dev reset

        // ---- weapons: +8% damage per level, a perk every 10 levels (Lv 10 Pierce +1, Lv 20 Crit +10%)
        public static int WeaponIndexFor(string name) { int i = Array.IndexOf(WeaponNames, name); return i < 0 ? 0 : i; }
        public static int Level(int w) => w >= 0 && w < D.weaponLv.Length ? D.weaponLv[w] : 1;
        public static float WeaponDamageMul(int w) => 1f + 0.08f * (Level(w) - 1);
        public static int PerkPierce(int w) => Level(w) >= 10 ? 1 : 0;
        public static float PerkCrit(int w) => Level(w) >= 20 ? 0.1f : 0f;
        public static int UpgradeCredits(int lv) => Mathf.RoundToInt(250f * Mathf.Pow(1.22f, lv - 1) / 10f) * 10;
        public static int UpgradeParts(int lv) => 2 + lv;
        public static bool CanUpgrade(int w) => Level(w) < Meta.WeaponCap(w) && D.credits >= Meta.LevelCredits(w) && D.parts >= Meta.LevelParts(w);
        public static bool Upgrade(int w) => Meta.UpgradeWeapon(w);

        // ---- gear
        public static GearItem Item(int uid) { foreach (var g in D.gear) if (g.uid == uid) return g; return null; }
        public static GearItem Equipped(int slot) => D.equipped[slot] == 0 ? null : Item(D.equipped[slot]);
        public static float Stat(GearItem g) => Meta.GearBase[g.rarity] * (1f + 0.1f * (g.level - 1));
        public static string StatText(GearItem g) => $"+{Stat(g) * 100:0}% {(SlotIsDamage[g.slot] ? "DAMAGE" : "HP")}";
        public static int GearLevelCost(GearItem g) => Meta.GearAlloy(g);
        public static bool LevelGear(GearItem g)
        {
            if (g.level >= MaxLevel(g.rarity) || D.alloy < Meta.GearAlloy(g) || D.credits < Meta.GearCredits(g)) return false;
            D.alloy -= Meta.GearAlloy(g); D.credits -= Meta.GearCredits(g); g.level++; Save(); Ops.Bump("upg"); return true;
        }
        public static void Equip(GearItem g) { D.equipped[g.slot] = g.uid; Save(); }
        public static GearItem AddGear(int slot, int rarity, int set = -1) { var g = new GearItem { uid = D.nextUid++, slot = slot, rarity = rarity, set = set }; D.gear.Add(g); return g; }
        // three of the same slot + rarity (not equipped ones first) merge into one of the next rarity
        public static List<GearItem> MergeSet(GearItem g)
        {
            if (g.rarity >= Rarities.Length - 1) return null;
            var set = new List<GearItem> { g };
            int need = Meta.MergeNeed[g.rarity];   // 3 / 3 / 4 / 5
            foreach (var o in D.gear) if (o != g && o.slot == g.slot && o.rarity == g.rarity && set.Count < need && !o.locked && D.equipped[o.slot] != o.uid) set.Add(o);
            return set.Count == need ? set : null;
        }
        public static GearItem Merge(GearItem g)
        {
            var set = MergeSet(g); if (set == null) return null;
            bool wasEquipped = false; int lvl = 1;
            foreach (var o in set) { wasEquipped |= D.equipped[o.slot] == o.uid; lvl = Mathf.Max(lvl, o.level); D.gear.Remove(o); }
            var n = AddGear(g.slot, g.rarity + 1, g.set); n.level = Mathf.Min(lvl, MaxLevel(n.rarity));   // keeps the picked piece's set
            if (wasEquipped || D.equipped[g.slot] == 0 || Item(D.equipped[g.slot]) == null) D.equipped[g.slot] = n.uid;
            Save(); Ops.Bump("merge", set.Count); return n;
        }
        public static float GearDamage() { float s = 0; for (int i = 0; i < 5; i++) { var g = Equipped(i); if (g != null && SlotIsDamage[i]) s += Stat(g); } return s; }
        public static float GearHp() { float s = 0; for (int i = 0; i < 5; i++) { var g = Equipped(i); if (g != null && !SlotIsDamage[i]) s += Stat(g); } return s; }

        // ---- combat power and the multipliers it stands for
        public static int HeroWeapon => Prefs.Vanguard ? 3 : Prefs.Scout ? 8 : WeaponIndexFor(Prefs.Weapon);
        // CP = 1000 * sqrt(D*H) (Meta): D = weapon level x gear x research x hero level, H = gear x research x hero x Cohesion
        public static float DamageMul(int w) => Meta.D_(w, Meta.HeroId);
        public static float HpMul => Meta.H_(Meta.HeroId);
        public static int Power(int w) => Meta.CP(w, Meta.HeroId);
        public static int Power() => Meta.CP();
        public static int Recommended(int tier) => Mathf.RoundToInt(900f * Mathf.Pow(1.45f, tier - 1) / 10f) * 10;
        public static float SectorScale(int tier) => 1.15f * Mathf.Pow(1.45f, tier - 1);   // enemy HP vs the base tuning (tuned with RunSim)
        public static float SectorDmgScale(int tier) => 1.2f * Mathf.Pow(1.45f, tier - 1); // enemy damage
        public static bool Unlocked(int tier) => tier <= D.sectorsCleared + 1;
        // drop zones: open once the zone before it is cleared; 3 stars each (clear, under 5:00, above 50% HP)
        public static bool Unlocked(SectorDef s)
        {
            if (D.sectorsCleared >= 99) return true;
            if (s.unlockAny != null) { if (s.unlockAny.Length == 0) return true; foreach (var r in s.unlockAny) if (D.cleared.Contains(r)) return true; return false; }
            return s.unlockAfter == null || D.cleared.Contains(s.unlockAfter);
        }
        public static int Recommended(SectorDef s) => s != null && s.power > 0 ? s.power : Recommended(s?.tier ?? 1);
        // a zone's enemy scale follows its recommended power (the old tiers: 900 x1.45 per tier)
        public static float PowerScale(SectorDef s, int tier) => s != null && s.power > 0 ? s.power / 900f : Mathf.Pow(1.45f, tier - 1);
        public static bool Cleared(SectorDef s) => D.cleared.Contains(s.id);
        public static bool HasStar(SectorDef s, int i) => D.stars.Contains(s.id + ":" + i);
        public static int StarCount(SectorDef s) { int n = 0; for (int i = 0; i < 3; i++) if (HasStar(s, i)) n++; return n; }
        public static int TotalStars() => D.stars.Count;
        // a won run: the zone counts as cleared, the stars it earned; returns the new stars
        public static List<int> CompleteZone(SectorDef s, float time, float hpFrac)
        {
            var got = new List<int>(); int t = Meta.Difficulty;
            if (!D.cleared.Contains(s.id)) D.cleared.Add(s.id);
            if (t > 0 && !D.tierCleared.Contains(s.id + ":" + t)) D.tierCleared.Add(s.id + ":" + t);
            if (t == 2) D.nightmareClears++;
            if (t > 0)
            {   // Hard / Nightmare keep their own three stars
                bool[] okT = { true, time < s.par * (t == 1 ? 1.15f : 1.3f), false };
                for (int i = 0; i < 3; i++) if (okT[i] && !D.stars.Contains(Meta.StarKey(s, t, i))) { D.stars.Add(Meta.StarKey(s, t, i)); got.Add(i); }
                Save(); return got;
            }
            bool[] ok = { true, time < s.par, s.floors == null && hpFrac >= 0.5f };   // spec zones' 3rd star needs its own tracking (not yet)
            for (int i = 0; i < 3; i++) if (ok[i] && !HasStar(s, i)) { D.stars.Add(s.id + ":" + i); got.Add(i); }
            Save(); return got;
        }
        public static Color PowerColor(int power, int rec) => power >= rec ? Holo.Acid : power >= rec * 0.85f ? new Color(1f, 0.72f, 0.2f) : Holo.Red;

        // ---- run rewards: a win pays 100%, a death 20% per floor reached (a run never pays zero)
        public struct Loot { public int credits, parts, alloy; public List<GearItem> gear; public string relic, blueprint; }
        public static Loot Reward(int tier, int floorReached, bool won, int kills)
        {
            float k = won ? 1f : 0.2f * Mathf.Clamp(floorReached, 1, 4);
            float s = Mathf.Pow(1.4f, tier - 1);
            var l = new Loot
            {
                credits = Mathf.RoundToInt((420f * s + kills * 2f) * k / 10f) * 10,
                parts = Mathf.Max(1, Mathf.RoundToInt(7f * s * k)),
                alloy = Mathf.Max(5, Mathf.RoundToInt(30f * s * k)),
                gear = new List<GearItem>(),
            };
            int drops = won ? 2 : floorReached >= 3 ? 1 : 0; // the boss drops 2; a deep death still finds one
            for (int i = 0; i < drops; i++) l.gear.Add(AddGear(UnityEngine.Random.Range(0, 5), RollRarity(tier, won)));
            D.credits += l.credits; D.parts += l.parts; D.alloy += l.alloy; D.runs++;
            Save();
            return l;
        }
        static int RollRarity(int tier, bool boss)
        {
            float r = UnityEngine.Random.value - 0.04f * (tier - 1) - (boss ? 0.05f : 0);
            return r < 0.02f ? 3 : r < 0.1f ? 2 : r < 0.38f ? 1 : 0;
        }

        // the cheapest upgrade that closes some of the gap: the hero's weapon level (always one step in reach)
        public static string Suggestion(out int weapon, out bool affordable)
        {
            weapon = HeroWeapon; int lv = Level(weapon);
            int c = UpgradeCredits(lv), p = UpgradeParts(lv);
            affordable = D.credits >= c && D.parts >= p;
            return $"{WeaponModels[weapon]} Lv {lv}→{lv + 1}  +8% DAMAGE  //  {c:N0} CREDITS + {p} PARTS  (YOU HAVE {D.credits:N0} + {D.parts})";
        }
    }
}
