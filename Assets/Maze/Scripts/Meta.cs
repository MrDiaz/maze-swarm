using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Upgrade phase 3 (Design/upgrade-brief.md; the formulas are canonical): Combat Power, run rewards by zone,
    // Commander Rank, Research, hero levels / stars / shards / Cohesion, weapon tiers (Mk I-V) and catch-up,
    // difficulty tiers with per-tier stars, the Star Track, and the defeat screen's three best upgrades.
    // Saved inside Progress.SaveData (lists of "key:value" strings: JsonUtility has no dictionaries).
    public static class Meta
    {
        static SaveData D => Progress.D;
        static int Get(List<string> l, string k) { foreach (var s in l) { int i = s.LastIndexOf(':'); if (i > 0 && s.Substring(0, i) == k) return int.Parse(s.Substring(i + 1)); } return 0; }
        static void Set(List<string> l, string k, int v) { for (int i = 0; i < l.Count; i++) { int j = l[i].LastIndexOf(':'); if (j > 0 && l[i].Substring(0, j) == k) { l[i] = k + ":" + v; return; } } l.Add(k + ":" + v); }

        // ================= heroes
        public static readonly string[] Heroes = { "vanguard" };
        public static string HeroId=>"vanguard";
        public static int HeroLevel(string h) => Mathf.Max(1, Get(D.heroLv, h));
        public static int HeroStars(string h) => h == "scout" ? Get(D.heroStars, h) : Mathf.Max(1, Get(D.heroStars, h));
        public static int Shards(string h) => Get(D.shards, h);
        public static int HeroCap(string h) => HeroStars(h) * 10;
        public static int HeroLevelCost(int L) => Mathf.RoundToInt(400f * Mathf.Pow(1.11f, L - 1));
        static readonly int[] StarShards = { 30, 20, 40, 80, 160 };   // to reach 1..5 stars
        public static int StarCost(string h) { int s = HeroStars(h); return s >= 5 ? 0 : StarShards[s]; }
        public static bool LevelHero(string h) { int L = HeroLevel(h); if (L >= HeroCap(h) || D.credits < HeroLevelCost(L)) return false; D.credits -= HeroLevelCost(L); Set(D.heroLv, h, L + 1); Progress.Save(); Ops.Bump("upg"); return true; }
        public static bool StarHero(string h) { int c = StarCost(h); if (c == 0 || Shards(h) < c) return false; Set(D.shards, h, Shards(h) - c); Set(D.heroStars, h, HeroStars(h) + 1); if (Get(D.heroLv, h) == 0) Set(D.heroLv, h, 1); Progress.Save(); return true; }
        public static void AddShards(string h, int n) { Set(D.shards, h, Shards(h) + n); }
        public static float HeroLvBonus(string h) => 0.02f * (HeroLevel(h) - 1);
        // Squad Cohesion: every 10 levels across the other heroes = +1% HP
        public static float Cohesion(string h) { int sum = 0; foreach (var o in Heroes) if (o != h && HeroStars(o) > 0) sum += HeroLevel(o); return 0.01f * (sum / 10); }
        public static int AbilityRank(string h) { int s = HeroStars(h); return s >= 4 ? 3 : s >= 2 ? 2 : 1; }

        // ================= Commander Rank
        public static int Rank => Mathf.Max(1, D.rank);
        public static int XpToNext(int r) => Mathf.RoundToInt(80f * Mathf.Pow(r, 1.35f));
        public static List<string> AddXp(int xp)
        {
            var news = new List<string>(); if (D.rank < 1) D.rank = 1;
            D.cxp += xp;
            while (D.cxp >= XpToNext(D.rank))
            {
                D.cxp -= XpToNext(D.rank); D.rank++; D.cores += D.rank % 5 == 0 ? 50 : 10;
                news.Add($"RANK {D.rank}" + RankUnlock(D.rank));
                if (D.rank == 3 && ResearchLv("fp1") == 0) SetResearch("fp1", 1);   // a free level of Calibrated Rounds
            }
            return news;
        }
        static string RankUnlock(int r) => r switch
        {
            2 => "  //  DAILY OPS", 3 => "  //  RESEARCH T1", 4 => "  //  HOT ZONE", 5 => "  //  HARD", 6 => "  //  STAR TRACK", 7 => "  //  RESEARCH T2",
            8 => "  //  WEEKLY", 10 => "  //  NIGHTMARE", 11 => "  //  RESEARCH T3", 15 => "  //  RESEARCH T4", 18 => "  //  CAPSTONES", 20 => "  //  PRESTIGE FRAME", _ => "",
        };

        // ================= Research: 4 branches x 4 tiers x 4 levels, no randomness
        public class Node { public string id, name, effect; public int branch, tier; public float per; }
        public static readonly string[] Branches = { "FIREPOWER", "ARMOR", "TACTICS", "LOGISTICS" };
        public static readonly Node[] Nodes =
        {
            new Node { id = "fp1", branch = 0, tier = 0, name = "CALIBRATED ROUNDS", per = 0.03f, effect = "+3% DAMAGE A LEVEL" },
            new Node { id = "fp2", branch = 0, tier = 1, name = "PRECISION OPTICS", per = 0.05f, effect = "+5% DAMAGE A LEVEL" },
            new Node { id = "fp3", branch = 0, tier = 2, name = "DEPLETED CORES", per = 0.07f, effect = "+7% DAMAGE A LEVEL" },
            new Node { id = "fp4", branch = 0, tier = 3, name = "PLASMA JACKETING", per = 0.09f, effect = "+9% DAMAGE A LEVEL" },
            new Node { id = "ar1", branch = 1, tier = 0, name = "REINFORCED PLATING", per = 0.03f, effect = "+3% HP A LEVEL" },
            new Node { id = "ar2", branch = 1, tier = 1, name = "NANO WEAVE", per = 0.05f, effect = "+5% HP A LEVEL" },
            new Node { id = "ar3", branch = 1, tier = 2, name = "REACTIVE ARMOR", per = 0.07f, effect = "+7% HP A LEVEL" },
            new Node { id = "ar4", branch = 1, tier = 3, name = "TITAN FRAME", per = 0.09f, effect = "+9% HP A LEVEL" },
            new Node { id = "ta1", branch = 2, tier = 0, name = "BIOMASS MAGNET", per = 0.1f, effect = "+10% PICKUP RADIUS A LEVEL" },
            new Node { id = "ta2", branch = 2, tier = 1, name = "FIELD REROLLS", per = 1, effect = "+1 CARD REROLL A LEVEL" },
            new Node { id = "ta3", branch = 2, tier = 2, name = "SECOND WIND", per = 0.15f, effect = "REVIVE ONCE AT 30/45/60/75% HP" },
            new Node { id = "ta4", branch = 2, tier = 3, name = "COMMANDER'S PICK", per = 2, effect = "4 CARDS ON THE FIRST 2/4/6/ALL LEVEL-UPS" },
            new Node { id = "lo1", branch = 3, tier = 0, name = "SALVAGE RIGHTS", per = 0.05f, effect = "+5% CREDITS A LEVEL" },
            new Node { id = "lo2", branch = 3, tier = 1, name = "ALLOY REFINERY", per = 0.06f, effect = "+6% ALLOY A LEVEL" },
            new Node { id = "lo3", branch = 3, tier = 2, name = "QUARTERMASTER", per = 0.05f, effect = "+5% EXTRA GEAR CHANCE A LEVEL" },
            new Node { id = "lo4", branch = 3, tier = 3, name = "DEEP POCKETS", per = 0.025f, effect = "DEATHS PAY +2.5% A FLOOR A LEVEL" },
        };
        static readonly int[] TierRank = { 3, 7, 11, 15 }; static readonly float[] TierBase = { 600, 6000, 45000, 220000 };
        public static int ResearchLv(string id) => Get(D.research, id);
        static void SetResearch(string id, int lv) => Set(D.research, id, lv);
        public static Node NodeOf(string id) { foreach (var n in Nodes) if (n.id == id) return n; return null; }
        public static int ResearchCost(Node n) => Mathf.RoundToInt(TierBase[n.tier] * (n.branch >= 2 ? 1.1f : 1f) * Mathf.Pow(1.35f, ResearchLv(n.id)));
        public static string ResearchGate(Node n)
        {
            if (ResearchLv(n.id) >= 4) return "MAX";
            if (Rank < TierRank[n.tier]) return $"RANK {TierRank[n.tier]}";
            if (n.tier > 0) { var above = Nodes[Array.IndexOf(Nodes, n) - 1]; if (ResearchLv(above.id) < 2) return $"{above.name} LV 2"; }
            return null;
        }
        public static bool BuyResearch(Node n) { if (ResearchGate(n) != null || D.credits < ResearchCost(n)) return false; D.credits -= ResearchCost(n); SetResearch(n.id, ResearchLv(n.id) + 1); Progress.Save(); Ops.Bump("upg"); return true; }
        static float Branch(int b) { float s = 0; foreach (var n in Nodes) if (n.branch == b) s += n.per * ResearchLv(n.id); return s; }
        public static float ResearchDmg => Branch(0);
        public static float ResearchHp => Branch(1);
        public static float CreditsBonus => 0.05f * ResearchLv("lo1");
        public static float AlloyBonus => 0.06f * ResearchLv("lo2");
        public static float ExtraGearChance => 0.05f * ResearchLv("lo3");
        public static float DeathPayPerFloor => 0.2f + 0.025f * ResearchLv("lo4");
        public static float PickupRadiusBonus => 0.1f * ResearchLv("ta1");
        public static float SecondWindHp => ResearchLv("ta3") == 0 ? 0 : 0.15f + 0.15f * ResearchLv("ta3");
        public static int CommandersPick => ResearchLv("ta4") >= 4 ? 999 : 2 * ResearchLv("ta4");

        // ================= weapons: tiers raise the cap; costs; perks; catch-up
        public static readonly string[] Mk = { "MK I", "MK II", "MK III", "MK IV", "MK V" };
        public static readonly int[] MkCap = { 20, 40, 60, 80, 100 }, MkZone = { 0, 5, 10, 15, 18 }, MkCredits = { 0, 2000, 8000, 25000, 70000 }, MkParts = { 0, 40, 120, 250, 500 };
        public static readonly Color[] MkColor = { new Color(0.7f, 0.72f, 0.75f), new Color(0.4f, 0.9f, 0.4f), new Color(0.35f, 0.65f, 1f), new Color(0.72f, 0.4f, 1f), new Color(1f, 0.78f, 0.2f) };
        public static int WeaponTier(int w) => D.weaponTier != null && w < D.weaponTier.Length ? D.weaponTier[w] : 0;
        public static int WeaponCap(int w) => MkCap[WeaponTier(w)];
        public static int MaxWeaponLevel { get { int m = 1; foreach (var l in D.weaponLv) m = Mathf.Max(m, l); return m; } }
        public static float CatchUp(int w) => Progress.Level(w) < MaxWeaponLevel / 2 ? 0.25f : 1f;   // a weapon below half your best costs 25%
        public static int LevelCredits(int w) => Mathf.RoundToInt(250f * Mathf.Pow(1.075f, Progress.Level(w) - 1) * CatchUp(w));
        public static int LevelParts(int w) => Mathf.Max(1, Mathf.RoundToInt((Progress.Level(w) / 2 + 2) * CatchUp(w)));
        public static int Perks(int w) => Progress.Level(w) / 10;
        public static float WeaponLevelMult(int w) => 1f + 0.08f * (Progress.Level(w) - 1) + 0.05f * Perks(w);
        public static string TierGate(int w)
        {
            int t = WeaponTier(w) + 1; if (t >= Mk.Length) return "MAX TIER";
            int z = MkZone[t]; bool zoneOk = false; foreach (var s in SectorDefs.All) if (s.num == z && Progress.Cleared(s)) zoneOk = true;
            if (!zoneOk) return $"CLEAR ZONE {z}";
            if (t == 4 && D.nightmareClears <= 0) return "A NIGHTMARE CLEAR";
            return null;
        }
        public static bool TierUp(int w)
        {
            int t = WeaponTier(w) + 1; if (TierGate(w) != null || D.credits < MkCredits[t] || D.parts < MkParts[t]) return false;
            D.credits -= MkCredits[t]; D.parts -= MkParts[t]; D.weaponTier[w] = t; Progress.Save(); return true;
        }

        // ================= Combat Power (replaces the old formula; derived, so old saves need nothing)
        public static float D_(int w, string hero) => WeaponLevelMult(w) * (1 + Progress.GearDamage()) * (1 + ResearchDmg) * (1 + HeroLvBonus(hero)) * (TalH(hero, 9, 1) && hero != "vanguard" ? 1.1f : 1f);
        public static float H_(string hero) => (1 + Progress.GearHp()) * (1 + ResearchHp) * (1 + HeroLvBonus(hero)) * (1 + Cohesion(hero) * (hero == "vanguard" && TalH(hero, 9, 2) ? 2 : 1)) * (Set2On(3) ? 1.1f : 1f) * TalentHp(hero);
        static float TalentHp(string h) => h == "vanguard" ? (TalH(h, 0, 2) ? 1.15f : 1f) * (TalH(h, 5, 2) ? 1.25f : 1f) * (TalH(h, 9, 1) ? 1.2f : 1f) : TalH(h, 9, 1) ? 1.1f : 1f;
        public static int CP(int w, string hero) => Mathf.RoundToInt(1000f * Mathf.Sqrt(D_(w, hero) * H_(hero)) * (1 + SetCp + RelicCp + 0.01f * TalentsPicked(hero)));
        public static int CP() => CP(Progress.HeroWeapon, HeroId);

        // ================= difficulty tiers
        public static readonly string[] Tiers = { "NORMAL", "HARD", "NIGHTMARE" };
        public static readonly float[] TierPower = { 1f, 1.9f, 3.4f }, TierPay = { 1f, 1.6f, 2.4f };
        public static int Difficulty;   // the briefing's selector (0 normal, 1 hard, 2 nightmare)
        public static bool TierCleared(SectorDef s, int t) => t == 0 ? Progress.Cleared(s) : D.tierCleared.Contains(s.id + ":" + t);
        public static string TierGateFor(SectorDef s, int t)
        {
            if (t == 0) return null;
            if (!TierCleared(s, t - 1)) return t == 1 ? "CLEAR ON NORMAL" : "CLEAR ON HARD";
            if (Rank < (t == 1 ? 5 : 10)) return $"COMMANDER RANK {(t == 1 ? 5 : 10)}";
            return null;
        }
        public static int RecommendedAt(SectorDef s, int t) => Mathf.RoundToInt(Progress.Recommended(s) * TierPower[t] / 10f) * 10;
        // stars per tier: "id:t:i" (normal keeps the old "id:i")
        public static string StarKey(SectorDef s, int t, int i) => t == 0 ? s.id + ":" + i : s.id + ":" + t + ":" + i;
        public static int StarsTotal() => D.stars.Count;

        // ================= talents (7): a row opens every 5 hero levels; two choices a row, free respec, +1% CP a pick
        public static readonly string[,] Talents =
        {   // sentinel
            { "STEADY AIM: +15% DAMAGE STANDING STILL", "RUN AND GUN: +8% MOVE SPEED" }, { "FIELD MEDIC: BIOMASS HEALS 1", "SCAVENGER: +15% CREDITS" },
            { "FLASH FREEZE +1 M RADIUS", "SKILL COOLDOWN -2 S" }, { "DASH LEAVES A FROST TRAIL", "DASH: 0.5 S INVULNERABLE" },
            { "FIRST LEVEL-UP IS A RARE OFFER", "START 1 LEVEL AHEAD" }, { "FIRST TIME BELOW 20%: HEAL 30%", "BELOW 30% HP: +30% FIRE RATE" },
            { "EVOLUTIONS ONE CARD LEVEL EARLIER", "+15% DAMAGE ONCE EVOLVED" }, { "+20% VS BOSSES", "+10% PER 5 ENEMIES WITHIN 5 M (MAX 30%)" },
            { "CACHES: TAKE 2 CRATES", "15% CHANCE OF +1 GEAR DROP" }, { "+10% DAMAGE AND HP", "+1 CARD CHOICE" },
            // vanguard
            { "KEEPS 50% SPIN: +15% FIRE RATE", "+15% HP" }, { "-10% DAMAGE TAKEN", "DASH SHOVES 2 M" },
            { "+40% MAGAZINE", "+25% DAMAGE AFTER 3 S OF FIRING" }, { "IMMUNE TO POUNCE SHOVE", "30% OF DAMAGE TAKEN RETURNS AS A SHOCKWAVE" },
            { "HEAVY ROUNDS OFFERED MORE", "BARRAGE +1 S" }, { "SHIELD CARD ALSO +10% DAMAGE", "+25% HP, -5% SPEED" },
            { "BURNS LAST +1 S", "SHOVE +50%" }, { "BARRAGE COOLDOWN -3 S", "BARRAGE HEALS 15%" },
            { "REVIVE ONCE A RUN AT 50%", "BELOW 25% HP: +20% DAMAGE" }, { "+20% HP", "COHESION DOUBLED" },
            // scout (not playable yet)
            { "+10% MOVE SPEED", "+2 M RANGE" }, { "+15% RELOAD", "+20% VS LEAPERS AND SPITTERS" },
            { "DECOY: 1 S INVISIBLE", "2 DECOY CHARGES" }, { "FIRST HIT MARKS FOR +15%", "EVERY 5TH VOLLEY DOUBLE" },
            { "RARE CARDS +20%", "15% DODGE OF CONTACT DAMAGE" }, { "+25% FOR 2 S AFTER A DASH", "+25% AT MAX RANGE" },
            { "POISON AND BURN +30%", "+1 MULTISHOT AT START" }, { "DECOY TAUNTS BOSSES", "+30% CRIT VS STUNNED" },
            { "DASH COOLDOWN -1.5 S", "CRITS +50%" }, { "+10% DAMAGE AND HP", "+1 CARD CHOICE" },
        };
        public static int HeroIdx(string h) => h == "vanguard" ? 1 : h == "scout" ? 2 : 0;
        public static bool RowOpen(string h, int row) => HeroStars(h) > 0 && HeroLevel(h) >= 5 * (row + 1);
        public static int Talent(string h, int row) => RowOpen(h, row) ? Get(D.talents, h + "." + row) : 0;
        public static void PickTalent(string h, int row, int c) { if (!RowOpen(h, row)) return; Set(D.talents, h + "." + row, Talent(h, row) == c ? 0 : c); Progress.Save(); }
        public static void ResetTalents(string h) { for (int r = 0; r < 10; r++) Set(D.talents, h + "." + r, 0); Progress.Save(); }
        public static int TalentsPicked(string h) { int n = 0; for (int r = 0; r < 10; r++) if (Talent(h, r) > 0) n++; return n; }
        public static bool TalH(string h, int row, int c) => Talent(h, row) == c;
        public static bool Tal(string h, int row, int c) => HeroId == h && Talent(h, row) == c;   // the run's hero has this talent

        // ================= run rewards by zone (1)
        public static Progress.Loot Reward(SectorDef zone, int reached, bool won, int kills, bool firstClear)
        {
            int z = zone != null && zone.num > 0 ? zone.num : FrontierNum();
            float pay = TierPay[Mathf.Clamp(Difficulty, 0, 2)], k = won ? 1f : DeathPayPerFloor * Mathf.Clamp(reached, 1, 4);
            var l = new Progress.Loot
            {
                credits = Mathf.RoundToInt(160f * Mathf.Pow(1.15f, z - 1) * pay * k * (1 + CreditsBonus + (Set4On(4) ? 0.1f : 0) + (Tal("sentinel", 1, 2) ? 0.15f : 0) + (Ops.WeeklyOn ? 0.25f : 0)) * (firstClear ? 3 : 1)),
                parts = Mathf.Max(1, Mathf.RoundToInt(6f * Mathf.Pow(1.10f, z - 1) * pay * k * (firstClear ? 2 : 1))),
                alloy = Mathf.Max(1, Mathf.RoundToInt(5f * Mathf.Pow(1.11f, z - 1) * pay * k * (1 + AlloyBonus))),
                gear = new List<GearItem>(),
            };
            int drops = won ? 2 + (firstClear ? 1 : 0) : reached >= 3 ? 1 : 0;
            if (firstClear && zone != null && zone.num == 1 && Difficulty == 0) l.gear.Add(Progress.AddGear(2, 1, RollSet(1)));   // onboarding: Rare Gloves
            if (UnityEngine.Random.value < ExtraGearChance) drops++;
            if (Tal("sentinel", 8, 2) && UnityEngine.Random.value < 0.15f) drops++;
            for (int i = 0; i < drops; i++) l.gear.Add(Progress.AddGear(UnityEngine.Random.Range(0, 5), RollRarity(z, Difficulty), RollSet(z)));
            if (firstClear) D.cores += Difficulty == 0 ? 10 : Difficulty == 1 ? 20 : 40;
            var bp = won && zone != null ? BlueprintFor(zone.num) : null;
            if (bp != null) { int n = Difficulty == 2 ? 2 : Difficulty == 1 ? 1 : UnityEngine.Random.value < 0.3f ? 1 : 0; if (n > 0) l.blueprint = GrantFragments(bp, n); }
            if (won && zone != null && zone.num >= 1 && zone.num <= 18 && (Difficulty == 2 || Difficulty == 1 && UnityEngine.Random.value < 0.35f)) l.relic = GrantRelic(zone.num - 1);
            if (Ops.R.hot) { int m = Ops.HotMaterial; if (m == 0) l.credits *= 2; else if (m == 1) l.parts *= 2; else if (m == 2) l.alloy *= 2; }   // Hot Zone x2
            D.credits += l.credits; D.parts += l.parts; D.alloy += l.alloy; D.runs++;
            Progress.Save();
            return l;
        }
        public static int FrontierNum() { int f = 1; foreach (var s in SectorDefs.All) if (!s.endless && Progress.Unlocked(s)) f = Mathf.Max(f, s.num); return f; }

        // gear drop odds by zone (Normal / Hard = zone + 6 / Nightmare = zone + 12, the last two never Common)
        static readonly float[,] Odds =
        {
            { 73.7f, 23.5f, 2.8f, 0, 0 }, { 71.6f, 24.8f, 3.6f, 0, 0 }, { 69.3f, 26.2f, 4.5f, 0, 0 }, { 66.9f, 27.7f, 5.4f, 0, 0 }, { 64.3f, 29.2f, 6.4f, 0, 0 },
            { 61.6f, 30.9f, 7.5f, 0, 0 }, { 58.7f, 32.7f, 8.6f, 0, 0 }, { 55.6f, 34.6f, 9.8f, 0, 0 }, { 52.2f, 36.7f, 11.1f, 0, 0 }, { 48.6f, 38.9f, 12.5f, 0, 0 },
            { 44.6f, 41.1f, 13.9f, 0.4f, 0 }, { 40.2f, 43.5f, 15.5f, 0.7f, 0 }, { 35.6f, 46.1f, 17.2f, 1.2f, 0 }, { 30.5f, 48.9f, 19.0f, 1.6f, 0 }, { 25.1f, 51.9f, 20.9f, 2.1f, 0 },
            { 19.2f, 55.1f, 23.0f, 2.6f, 0 }, { 12.8f, 58.7f, 25.3f, 3.2f, 0 }, { 9.2f, 60.3f, 26.8f, 3.7f, 0 },
        };
        static readonly float[,] OddsHard =
        {
            { 0, 91.4f, 8.6f, 0, 0 }, { 0, 90.2f, 9.8f, 0, 0 }, { 0, 88.9f, 11.1f, 0, 0 }, { 0, 87.5f, 12.5f, 0, 0 }, { 0, 85.7f, 13.9f, 0.4f, 0 }, { 0, 83.8f, 15.5f, 0.7f, 0 },
            { 0, 81.7f, 17.2f, 1.2f, 0 }, { 0, 79.4f, 19.0f, 1.6f, 0 }, { 0, 77.0f, 20.9f, 2.1f, 0 }, { 0, 74.3f, 23.0f, 2.6f, 0 }, { 0, 71.5f, 25.3f, 3.2f, 0 }, { 0, 69.5f, 26.8f, 3.7f, 0 },
            { 0, 68.6f, 27.3f, 4.0f, 0 }, { 0, 67.8f, 27.8f, 4.3f, 0 }, { 0, 67.1f, 28.3f, 4.7f, 0 }, { 0, 66.3f, 28.7f, 5.0f, 0 }, { 0, 65.6f, 29.1f, 5.2f, 0.1f }, { 0, 64.8f, 29.4f, 5.5f, 0.3f },
        };
        static readonly float[,] OddsNm =
        {
            { 0, 81.7f, 17.2f, 1.2f, 0 }, { 0, 79.4f, 19.0f, 1.6f, 0 }, { 0, 77.0f, 20.9f, 2.1f, 0 }, { 0, 74.3f, 23.0f, 2.6f, 0 }, { 0, 71.5f, 25.3f, 3.2f, 0 }, { 0, 69.5f, 26.8f, 3.7f, 0 },
            { 0, 68.6f, 27.3f, 4.0f, 0 }, { 0, 67.8f, 27.8f, 4.3f, 0 }, { 0, 67.1f, 28.3f, 4.7f, 0 }, { 0, 66.3f, 28.7f, 5.0f, 0 }, { 0, 65.6f, 29.1f, 5.2f, 0.1f }, { 0, 64.8f, 29.4f, 5.5f, 0.3f },
            { 0, 64.1f, 29.8f, 5.7f, 0.4f }, { 0, 63.5f, 30.1f, 6.0f, 0.5f }, { 0, 62.8f, 30.4f, 6.2f, 0.6f }, { 0, 62.2f, 30.7f, 6.4f, 0.7f }, { 0, 61.7f, 31.0f, 6.6f, 0.8f }, { 0, 61.1f, 31.2f, 6.8f, 0.9f },
        };
        public static int RollRarity(int zone, int diff)
        {
            var t = diff == 0 ? Odds : diff == 1 ? OddsHard : OddsNm; int row = Mathf.Clamp(zone, 1, 18) - 1;
            float r = UnityEngine.Random.value * 100f;
            for (int i = 0; i < 5; i++) { r -= t[row, i]; if (r <= 0) return i; }
            return 1;
        }

        // ================= relics (3): one per zone boss; 35% a Hard win, every Nightmare win; levels 1-5 from duplicates or Alloy.
        // One equipped (the slot opens at 60 stars), +2% CP a level; effect values interpolate L1 -> L5.
        public static readonly string[] Relics = { "CRYO HEART", "OVERRIDE KEY", "SLAG FURNACE", "SPORE LANTERN", "DARK LENS", "TIDE BELL", "CRANE HOOK", "ACID GLAND", "HELIX DRIVE",
            "SEISMIC CHARGE", "QUARANTINE SEAL", "RELAY COIL", "TETHER SPIKE", "VALIANT DOG TAGS", "REACTOR CORE", "BURROWER FANG", "HIVE PULSE", "BROOD CROWN" };
        static readonly (string text, float a, float b)[] RelicFx =
        {
            ("DASH FREEZES ENEMIES NEARBY {0:0.0} S", 1f, 1.8f), ("TURRET HACKS {0:P0} FASTER", 0.3f, 0.6f), ("KILLS: {0:P0} CHANCE OF A SLAG BURST", 0.05f, 0.12f),
            ("POISON CLOUD AROUND YOU EVERY {0:0} S", 10f, 6f), ("+{0:P0} DAMAGE BEYOND 8 M", 0.15f, 0.4f), ("SHOVE WITHIN 4 M EVERY {0:0} S", 12f, 7f),
            ("DASH PULLS THE NEAREST ELITE, STUN {0:0.0} S", 0.5f, 1.2f), ("HITS: ENEMIES TAKE +{0:P0} FOR 3 S", 0.08f, 0.2f), ("EVERY {0:0}TH LEVEL-UP IS A RARE OFFER", 4f, 3f),
            ("FLOOR START: CLEARS {0:0} M AFTER 2 S", 5f, 8f), ("BELOW 30% HP: A {0:0.0} S BUBBLE, ONCE A FLOOR", 2f, 4f), ("EVERY {0:0}TH HIT CALLS LIGHTNING", 15f, 8f),
            ("BOSSES SLOWED {0:P0}", 0.1f, 0.25f), ("REVIVE ONCE A RUN AT {0:P0} HP", 0.25f, 0.6f), ("FIRE RATE +1% PER 10 BIOMASS THIS FLOOR (MAX {0:P0})", 0.1f, 0.25f),
            ("BULLETS PIERCE {0:0} MORE BURROWERS, AND SEE THEM", 1f, 2f), ("ELITE KILLS HEAL {0:P0} HP", 0.05f, 0.12f), ("{0:0} MINI-DRONE ALLIES FIGHT FOR YOU", 1f, 3f),
        };
        public static int RelicLv(int i) => Get(D.relics, i.ToString());
        public static bool RelicSlotOpen => StarsTotal() >= 60 || D.starTrackClaimed >= 60;
        public static int EquippedRelic => RelicSlotOpen && D.equippedRelic >= 0 && D.equippedRelic < Relics.Length && RelicLv(D.equippedRelic) > 0 ? D.equippedRelic : -1;
        public static bool Relic(int i) => EquippedRelic == i;
        static float Rv(int i, int lv) => Mathf.Lerp(RelicFx[i].a, RelicFx[i].b, (Mathf.Clamp(lv, 1, 5) - 1) / 4f);
        public static float RelicV => EquippedRelic < 0 ? 0 : Rv(EquippedRelic, RelicLv(EquippedRelic));   // the equipped relic's value
        public static float RelicCp => EquippedRelic < 0 ? 0 : 0.02f * RelicLv(EquippedRelic);
        public static string RelicDesc(int i) => string.Format(RelicFx[i].text, Rv(i, Mathf.Max(1, RelicLv(i))));
        public static int RelicCost(int i) => RelicLv(i) >= 5 || RelicLv(i) == 0 ? 0 : Mathf.RoundToInt(500f * Mathf.Pow(1.11f, i) * RelicLv(i));
        public static bool LevelRelic(int i) { int c = RelicCost(i); if (c == 0 || D.alloy < c) return false; D.alloy -= c; Set(D.relics, i.ToString(), RelicLv(i) + 1); Progress.Save(); Ops.Bump("upg"); return true; }
        public static int RelicsOwned { get { int n = 0; for (int i = 0; i < Relics.Length; i++) if (RelicLv(i) > 0) n++; return n; } }
        public static string GrantRelic(int i)
        {
            int L = RelicLv(i);
            if (L >= 5) { D.alloy += 250; return Relics[i] + " (MAX) +250 ALLOY"; }
            Set(D.relics, i.ToString(), L + 1); Progress.Save();
            return L == 0 ? "NEW RELIC: " + Relics[i] : Relics[i] + " LV " + (L + 1);
        }

        // ================= blueprints (step 9): zones 6-9 Frag, 10-13 Lance, 14-17 Thermal, 18 Rail; 10 fragments unlock
        public static string BlueprintFor(int zone) => zone >= 6 && zone <= 9 ? "FRAG" : zone >= 10 && zone <= 13 ? "LANCE" : zone >= 14 && zone <= 17 ? "THERMAL" : zone == 18 ? "RAIL" : null;
        public static int Fragments(string w) => Get(D.blueprints, w);
        public static bool WeaponOwned(string w) => Array.IndexOf(new[] { "FRAG", "LANCE", "THERMAL", "RAIL" }, w) < 0 || Fragments(w) >= 10 || Get(D.blueprints, w + ".dev") > 0;   // (the Sandbox dev unlock)
        public static string BlueprintZones(string w) => w == "FRAG" ? "ZONES 6-9" : w == "LANCE" ? "ZONES 10-13" : w == "THERMAL" ? "ZONES 14-17" : "ZONE 18";
        public static string GrantFragments(string w, int n)
        {
            int had = Fragments(w); Set(D.blueprints, w, had + n); Progress.Save();
            return had < 10 && had + n >= 10 ? $"{Progress.WeaponModels[Progress.WeaponIndexFor(w)]} UNLOCKED" : $"{Progress.WeaponModels[Progress.WeaponIndexFor(w)]} BLUEPRINT {Mathf.Min(10, had + n)}/10";
        }

        // ================= Star Track (account-wide)
        public static string StarTrackReward(int stars) =>
            stars == 60 ? "RELIC SLOT" : stars == 100 ? "LEGENDARY OF CHOICE" : stars == 130 ? "WHITEOUT SKIN" : stars == 162 ? "MYTHIC + SWARM BREAKER" :
            stars % 20 == 0 ? "30 CORES + 10 SHARDS + EPIC" : stars % 10 == 0 ? $"{2000 * (stars / 10):N0} CR + 40 PARTS + RARE" : null;
        public static List<string> ClaimStarTrack()
        {
            var got = new List<string>();
            for (int s = D.starTrackClaimed + 1; s <= StarsTotal(); s++)
            {
                var r = StarTrackReward(s); if (r == null) continue;
                if (s % 10 == 0 && s % 20 != 0) { D.credits += 2000 * (s / 10); D.parts += 40; Progress.AddGear(UnityEngine.Random.Range(0, 5), 1); }
                if (s == 60) { GrantRelic(0); if (D.equippedRelic < 0) D.equippedRelic = 0; }
                if (s % 20 == 0) { D.cores += 30; AddShards(HeroId, 10); Progress.AddGear(UnityEngine.Random.Range(0, 5), 2); }
                got.Add($"{s} STARS  //  {r}");
            }
            D.starTrackClaimed = Mathf.Max(D.starTrackClaimed, StarsTotal()); Progress.Save();
            return got;
        }
        public static string NextStarRewards()
        {
            var l = new List<string>();
            for (int s = StarsTotal() + 1; s <= 162 && l.Count < 3; s++) { var r = StarTrackReward(s); if (r != null) l.Add($"{s}★ {r}"); }
            return string.Join("   ", l);
        }

        // ================= the defeat screen: the 3 best affordable upgrades by CP per Credit (12)
        public class Pick { public string name; public int credits, gain; public Func<bool> buy, canBuy; }
        public static List<Pick> BestUpgrades(int n)
        {   // greedy: the best CP per Credit that still fits the wallet, applied in place, then the next; all undone at the end
            var picks = new List<Pick>(); int w = Progress.HeroWeapon; string h = HeroId; int budget = D.credits, alloy = D.alloy;
            var undo = new List<Action>();
            for (int k = 0; k < n; k++)
            {
                int cp0 = CP(); Pick best = null; float bestR = 0; Action apply = null, revert = null;
                void Consider(string name, int c, Action up, Action down, Func<bool> buy, Func<bool> canBuy)
                {
                    up(); int g = CP() - cp0; down();
                    if (g <= 0) return; float r = g / (float)Mathf.Max(1, c);
                    bool fits = c <= budget, bestFits = best != null && best.credits <= budget;
                    if (best == null || fits && !bestFits || fits == bestFits && r > bestR) { best = new Pick { name = name, credits = c, gain = g, buy = buy, canBuy = canBuy }; bestR = r; apply = up; revert = down; }
                }
                if (Progress.Level(w) < WeaponCap(w)) { int lv = D.weaponLv[w]; Consider($"{Progress.WeaponModels[w]} LV {lv + 1}", LevelCredits(w), () => D.weaponLv[w] = lv + 1, () => D.weaponLv[w] = lv, () => UpgradeWeapon(w), () => Progress.CanUpgrade(w)); }
                if (HeroLevel(h) < HeroCap(h)) { int L = HeroLevel(h); Consider($"{h.ToUpperInvariant()} LV {L + 1}", HeroLevelCost(L), () => Set(D.heroLv, h, L + 1), () => Set(D.heroLv, h, L), () => LevelHero(h), () => HeroLevel(h) < HeroCap(h) && D.credits >= HeroLevelCost(HeroLevel(h))); }
                foreach (var nd in Nodes)
                {
                    if (nd.branch > 1 || ResearchGate(nd) != null) continue;
                    int L = ResearchLv(nd.id); var node = nd; Consider(nd.name, ResearchCost(nd), () => SetResearch(node.id, L + 1), () => SetResearch(node.id, L), () => BuyResearch(node), () => ResearchGate(node) == null && D.credits >= ResearchCost(node));
                }
                for (int sl = 0; sl < 5; sl++)
                {   // equipped gear (Credits + Alloy)
                    var g = Progress.Equipped(sl); if (g == null || g.level >= GearCap[g.rarity] || GearAlloy(g) > alloy) continue;
                    var gi = g; int L = g.level; Consider($"{Progress.Rarities[g.rarity]} {Progress.Slots[g.slot]} LV {L + 1}", GearCredits(g), () => gi.level = L + 1, () => gi.level = L, () => Progress.LevelGear(gi), () => gi.level < GearCap[gi.rarity] && D.alloy >= GearAlloy(gi) && D.credits >= GearCredits(gi));
                }
                if (best == null || picks.Count > 0 && best.credits > budget) break;
                picks.Add(best); budget -= best.credits; apply(); undo.Add(revert);
            }
            for (int u = undo.Count - 1; u >= 0; u--) undo[u]();
            return picks;
        }
        // the defeat screen's pointer: an untried sibling zone you already out-power, else the Hot Zone
        public static string BranchTip(SectorDef zone)
        {
            if (zone == null || zone.unlockAny == null || zone.unlockAny.Length == 0) return null;
            foreach (var s in SectorDefs.All)
            {
                if (s == zone || s.endless || s.unlockAny == null || s.unlockAny.Length == 0 || s.unlockAny[0] != zone.unlockAny[0] || Progress.Cleared(s) || !Progress.Unlocked(s)) continue;
                int rec = RecommendedAt(s, 0); if (CP() >= rec) return $"TRY THE OTHER BRANCH: {s.name.ToUpperInvariant()} ({100 * CP() / Mathf.Max(1, rec)}% POWER)";
            }
            var hot = Ops.HotZone; return hot != null && hot != zone ? $"FARM THE HOT ZONE: {hot.name.ToUpperInvariant()} (x2 {Ops.MaterialNames[Ops.HotMaterial]})" : null;
        }
        public static bool UpgradeWeapon(int w)
        {
            if (Progress.Level(w) >= WeaponCap(w)) return false;
            int c = LevelCredits(w), p = LevelParts(w); if (D.credits < c || D.parts < p) return false;
            D.credits -= c; D.parts -= p; D.weaponLv[w]++; Progress.Save(); Ops.Bump("upg"); return true;
        }

        // ================= gear sets (4) and affixes
        public static readonly string[] Sets = { "FROSTLINE", "HIVEBREAKER", "OVERCLOCK", "BULWARK", "SCAVENGER", "VOLT", "JUGGERNAUT" };
        public static readonly string[] Set2 = { "+10% DMG TO SLOWED", "+15% VS ELITES, NESTS, BOSSES", "DASH COOLDOWN -15%", "+10% MAX HP", "+40% PICKUP RADIUS", "RICOCHET +1", "STILL 1 S: -20% DAMAGE TAKEN" };
        public static readonly string[] Set4 = { "SHOTS SLOW 15% FOR 1.5 S", "NESTS/BOSSES +25%, ELITE KILLS HEAL 10", "3 S AFTER A DASH: +40% FIRE RATE", "A SHIELD EVERY 6 S", "+10% CREDITS", "EVERY 8TH SHOT ARCS TO 4 (60%)", "STANDING STILL: +25% FIRE RATE" };
        // the two sets each zone drops (upgrade brief 2)
        static readonly int[,] ZoneSets = { {0,3},{3,2},{2,4},{1,4},{5,2},{5,0},{4,3},{1,4},{5,3},{2,4},{1,3},{5,2},{5,0},{2,3},{2,5},{1,4},{1,6},{6,1} };
        public static int RollSet(int zone) => zone < 1 || zone > 18 ? UnityEngine.Random.Range(0, 7) : ZoneSets[zone - 1, UnityEngine.Random.Range(0, 2)];
        public static int SetPieces(int set) { int n = 0; for (int i = 0; i < 5; i++) { var g = Progress.Equipped(i); if (g != null && g.set == set) n++; } return n; }
        public static bool Set2On(int set) => SetPieces(set) >= 2;
        public static bool Set4On(int set) => SetPieces(set) >= 4;
        public static float SetCp { get { float s = 0; for (int i = 0; i < 7; i++) { if (Set4On(i)) s += 0.05f; else if (Set2On(i)) s += 0.02f; } return s; } }
        // slot affixes: Epic (rarity 2+) and Legendary (3+); Mythic doubles both
        static GearItem Eq(int slot) => Progress.Equipped(slot);
        public static float AffixMul(int slot) { var g = Eq(slot); return g != null && g.rarity >= 4 ? 2f : 1f; }
        public static bool Epic(int slot) { var g = Eq(slot); return g != null && g.rarity >= 2; }
        public static bool Leg(int slot) { var g = Eq(slot); return g != null && g.rarity >= 3; }
        public static float BootsSpeed { get { var g = Eq(3); return g == null ? 0 : 0.02f + 0.01f * g.rarity; } }
        public static float GloveRate => Epic(2) ? 0.05f * AffixMul(2) : 0;
        public static float GloveCrit => Leg(2) ? 0.05f * AffixMul(2) : 0;
        public static float ImplantCritDmg => Epic(4) ? 0.1f * AffixMul(4) : 0;
        public static float BootsDashCut => Epic(3) ? 0.5f * AffixMul(3) : 0;
        public static string Affixes(GearItem g)
        {
            string[] ep = { "+10% PICKUP RADIUS", "8% LESS BLAST DAMAGE", "+5% FIRE RATE", "DASH COOLDOWN -0.5 S", "+10% CRIT DAMAGE" };
            string[] lg = { "SHIELD CARD +1 CHARGE", "+1 HP EVERY 4 S", "+5% CRIT CHANCE", "DASH SHOVES ENEMIES", "LEVEL-UPS HEAL 10%" };
            var s = ""; if (g.rarity >= 2) s += "EPIC: " + ep[g.slot]; if (g.rarity >= 3) s += "   LEG: " + lg[g.slot]; if (g.rarity >= 4) s += "  (x2)";
            return s;
        }

        // ================= gear (4): main stat, caps, costs, merge ratios, salvage
        public static readonly float[] GearBase = { 0.02f, 0.04f, 0.07f, 0.11f, 0.16f };
        public static readonly int[] GearCap = { 10, 15, 20, 25, 30 }, MergeNeed = { 3, 3, 4, 5, 0 }, SalvageAlloy = { 5, 15, 50, 150, 400 };
        static readonly float[] RarityCostMul = { 1f, 1.5f, 2.2f, 3.2f, 4.5f };
        public static int GearCredits(GearItem g) => Mathf.RoundToInt(80f * Mathf.Pow(1.12f, g.level - 1) * RarityCostMul[g.rarity]);
        public static int GearAlloy(GearItem g) => Mathf.RoundToInt(4f * Mathf.Pow(1.13f, g.level - 1) * RarityCostMul[g.rarity]);
        public static int SalvageItem(GearItem g)
        {
            if (g.locked || D.equipped[g.slot] == g.uid) return 0;
            int a = Salvage(g); D.alloy += a; D.gear.Remove(g); Progress.Save(); return a;
        }
        // MERGE ALL: every full stack, lowest first, never equipped or locked pieces
        public static int MergeAll()
        {
            int merged = 0;
            for (int pass = 0; pass < 20; pass++)
            {
                GearItem pick = null;
                foreach (var g in D.gear) { if (g.locked || D.equipped[g.slot] == g.uid) continue; if (Progress.MergeSet(g) != null) { pick = g; break; } }
                if (pick == null) break;
                if (Progress.Merge(pick) != null) merged++;
            }
            return merged;
        }
        public static int Salvage(GearItem g)
        {
            int spent = 0; for (int L = 1; L < g.level; L++) spent += Mathf.RoundToInt(4f * Mathf.Pow(1.13f, L - 1) * RarityCostMul[g.rarity]);
            return SalvageAlloy[g.rarity] + Mathf.RoundToInt(spent * 0.8f);
        }
    }
}
