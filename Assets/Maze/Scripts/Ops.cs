using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Rotating content and achievements (Design/upgrade-brief.md sections 9-10; upgrade step 7):
    //   Daily Ops (rank 2)  - 3 missions a day (reset 04:00 local), 1 free reroll then 10 Cores; all 3 open a chest
    //   Hot Zone (rank 4)   - one cleared zone a day (never the 3 newest), one positive mutator (not yesterday's),
    //                         x2 of one material by weekday (Cr Mon/Thu, Parts Tue/Fri, Alloy Wed/Sat, shards Sun)
    //   Achievements        - one-time Cores, toasted on the run summary only
    // Lifetime counters and the run log feed all three. Saved inside Progress.SaveData.
    public static class Ops
    {
        static SaveData D => Progress.D;
        static int Get(List<string> l, string k) { foreach (var s in l) { int i = s.LastIndexOf(':'); if (i > 0 && s.Substring(0, i) == k) return int.Parse(s.Substring(i + 1)); } return 0; }
        static void Set(List<string> l, string k, int v) { for (int i = 0; i < l.Count; i++) { int j = l[i].LastIndexOf(':'); if (j > 0 && l[i].Substring(0, j) == k) { l[i] = k + ":" + v; return; } } l.Add(k + ":" + v); }

        // ================= lifetime counters
        public static int Stat(string k) => Get(D.stats, k);
        public static void AddStat(string k, int n) { if (n != 0) Set(D.stats, k, Stat(k) + n); }
        // menu actions (upgrades, merges) count toward today's missions straight away
        public static void Bump(string what, int n = 1)
        {
            if (Game.SimRun) return;
            Roll(); AddStat(what, n);
            foreach (var m in Today()) if (m.def.id == (what == "upg" ? "upg5" : what == "merge" ? "merge3" : "")) Advance(m, n);
        }

        // ================= the run log (filled by the game during a run, read at run end)
        public class RunLog
        {
            public int kills, bloaterKills, pounceDodges, biomass, evos, caches; public bool survivalCard, hot;
            public void Reset() { kills = bloaterKills = pounceDodges = biomass = evos = caches = 0; survivalCard = hot = false; }
        }
        public static readonly RunLog R = new();

        // ================= Daily Ops
        public class MissionDef { public string id, text; public int goal; public string reward; public Func<float, int> amount; }
        static float F => Mathf.Pow(1.15f, Meta.FrontierNum() - 1);
        public static readonly MissionDef[] Pool =
        {
            new() { id = "clear3",  text = "CLEAR 3 ZONES",                   goal = 3,   reward = "cr",    amount = f => Mathf.RoundToInt(1000 * f) },
            new() { id = "kill300", text = "KILL 300 ENEMIES",                goal = 300, reward = "parts", amount = f => Mathf.RoundToInt(20 * f) },
            new() { id = "evo2",    text = "PICK 2 EVOLUTIONS",               goal = 2,   reward = "alloy", amount = f => Mathf.RoundToInt(30 * f) },
            new() { id = "hard",    text = "CLEAR A HARD ZONE",               goal = 1,   reward = "cores", amount = f => 10 },
            new() { id = "stars2",  text = "EARN 2 NEW STARS",                goal = 2,   reward = "cores", amount = f => 10 },
            new() { id = "bloat5",  text = "KILL 5 BLOATERS",                 goal = 5,   reward = "alloy", amount = f => Mathf.RoundToInt(20 * f) },
            new() { id = "heroes",  text = "FINISH A RUN WITH EACH HERO",     goal = 2,   reward = "shards", amount = f => 5 },
            new() { id = "hot",     text = "CLEAR THE HOT ZONE",              goal = 1,   reward = "cores", amount = f => 15 },
            new() { id = "caches4", text = "OPEN 4 CACHES",                   goal = 4,   reward = "parts", amount = f => Mathf.RoundToInt(15 * f) },
            new() { id = "merge3",  text = "MERGE 3 PIECES",                  goal = 3,   reward = "alloy", amount = f => Mathf.RoundToInt(30 * f) },
            new() { id = "upg5",    text = "UPGRADE 5 TIMES",                 goal = 5,   reward = "cr",    amount = f => Mathf.RoundToInt(800 * f) },
            new() { id = "nosurv",  text = "WIN WITHOUT A SURVIVAL CARD",     goal = 1,   reward = "cores", amount = f => 10 },
            new() { id = "bonus",   text = "EARN A ZONE'S BONUS STAR",        goal = 1,   reward = "cores", amount = f => 15 },
        };
        public class Mission { public MissionDef def; public int progress; public bool done; public int slot; }

        public static bool DailyOpen => Meta.Rank >= 2;
        public static DateTime OpsNow => DateTime.Now.AddHours(-4);   // the day turns at 04:00 local
        public static string DayKey => OpsNow.ToString("yyyy-MM-dd");
        static int DayIndex => (int)(OpsNow.Date - new DateTime(2026, 1, 1)).TotalDays;
        static MissionDef Def(string id) { foreach (var m in Pool) if (m.id == id) return m; return null; }
        static bool Allowed(MissionDef m) =>
            (m.id != "hot" || HotOpen) && (m.id != "hard" || Meta.Rank >= 5) && (m.id != "heroes" || Meta.HeroStars("vanguard") > 0) && (m.id != "evo2" || Meta.Rank >= 3);

        // a new day: three missions from the pool (seeded by the date)
        public static void Roll()
        {
            if (D.dailyDay == DayKey && D.daily.Count == 3) return;
            if (D.dailyDay != null && D.dailyChest && (OpsNow.Date - DateTime.Parse(D.dailyDay)).TotalDays > 1.5) D.dailyStreak = 0;   // a missed day breaks the streak
            D.dailyDay = DayKey; D.daily.Clear(); D.dailyRerolls = 0; D.dailyChest = false;
            var rng = new System.Random(DayIndex * 7919);
            var pool = new List<MissionDef>(); foreach (var m in Pool) if (Allowed(m)) pool.Add(m);
            for (int i = 0; i < 3 && pool.Count > 0; i++) { int k = rng.Next(pool.Count); D.daily.Add(pool[k].id + ":0"); pool.RemoveAt(k); }
            Progress.Save();
        }
        public static List<Mission> Today()
        {
            Roll();
            var l = new List<Mission>();
            for (int i = 0; i < D.daily.Count; i++)
            {
                int j = D.daily[i].LastIndexOf(':'); var def = Def(D.daily[i].Substring(0, j)); if (def == null) continue;
                int p = int.Parse(D.daily[i].Substring(j + 1));
                l.Add(new Mission { def = def, progress = Mathf.Min(p, def.goal), done = p >= def.goal, slot = i });
            }
            return l;
        }
        static readonly List<string> justDone = new();
        static void Advance(Mission m, int n)
        {
            if (m.done || n <= 0) return;
            int p = m.progress + n; D.daily[m.slot] = m.def.id + ":" + p; m.progress = Mathf.Min(p, m.def.goal);
            if (p >= m.def.goal) { m.done = true; justDone.Add($"DAILY OPS  //  {m.def.text}  +{Pay(m.def.reward, m.def.amount(F))}"); }
            Progress.Save();
        }
        public static string RewardText(MissionDef m) { int a = m.amount(F); return m.reward switch { "cr" => $"{a:N0} CR", "parts" => $"{a} PARTS", "alloy" => $"{a} ALLOY", "cores" => $"{a} CORES", _ => $"{a} SHARDS" }; }
        static string Pay(string kind, int a)
        {
            switch (kind)
            {
                case "cr": D.credits += a; return $"{a:N0} CR";
                case "parts": D.parts += a; return $"{a} PARTS";
                case "alloy": D.alloy += a; return $"{a} ALLOY";
                case "cores": D.cores += a; return $"{a} CORES";
                default: Meta.AddShards(Meta.HeroId, a); return $"{a} SHARDS";
            }
        }
        public static int RerollCost => D.dailyRerolls == 0 ? 0 : 10;
        public static bool Reroll(int slot)
        {
            var ms = Today(); if (slot < 0 || slot >= ms.Count || ms[slot].done) return false;
            if (D.cores < RerollCost) return false;
            var pool = new List<MissionDef>(); foreach (var m in Pool) if (Allowed(m) && !ms.Exists(x => x.def == m)) pool.Add(m);
            if (pool.Count == 0) return false;
            D.cores -= RerollCost; D.dailyRerolls++;
            D.daily[slot] = pool[UnityEngine.Random.Range(0, pool.Count)].id + ":0"; Progress.Save(); return true;
        }
        public static bool ChestReady { get { var ms = Today(); return !D.dailyChest && ms.Count == 3 && ms.TrueForAll(m => m.done); } }
        public static string OpenChest()
        {
            if (!ChestReady) return null;
            int cr = Mathf.RoundToInt(3 * 160f * F);
            D.dailyChest = true; D.cores += 20; D.credits += cr; Meta.AddShards(Meta.HeroId, 5);
            D.dailyStreak = D.dailyLast == PrevDayKey ? D.dailyStreak + 1 : 1; D.dailyLast = DayKey;
            Progress.Save();
            return $"20 CORES + {cr:N0} CR + 5 SHARDS";
        }
        static string PrevDayKey => OpsNow.AddDays(-1).ToString("yyyy-MM-dd");

        // ================= Hot Zone
        public static bool HotOpen => Meta.Rank >= 4;
        public static readonly string[] Mutators = { "OVERCLOCKED", "SUPPLY LINES", "BOUNTY", "EVOLUTION RUSH" };
        public static readonly string[] MutatorText = { "LEVEL-UPS 30% FASTER", "TAKE 2 OF 3 CACHE CRATES", "+1 CREDIT A KILL", "EVOLUTIONS NEED ONE LESS CARD" };
        public static SectorDef HotZone
        {
            get
            {
                if (!HotOpen) return null;
                var cl = new List<SectorDef>(); foreach (var s in SectorDefs.All) if (!s.endless && s.num > 0 && Progress.Cleared(s)) cl.Add(s);
                if (cl.Count == 0) return null;
                cl.Sort((a, b) => a.num.CompareTo(b.num));
                if (cl.Count > 3) cl.RemoveRange(cl.Count - 3, 3);   // never one of the 3 newest
                return cl[new System.Random(DayIndex * 104729).Next(cl.Count)];
            }
        }
        public static int HotMutator => ((DayIndex % 4) + 4) % 4;   // a different one each day
        // x2 by weekday: 0 credits, 1 parts, 2 alloy, 3 shards
        public static int HotMaterial => OpsNow.DayOfWeek switch { DayOfWeek.Monday or DayOfWeek.Thursday => 0, DayOfWeek.Tuesday or DayOfWeek.Friday => 1, DayOfWeek.Wednesday or DayOfWeek.Saturday => 2, _ => 3 };
        public static readonly string[] MaterialNames = { "CREDITS", "PARTS", "ALLOY", "SHARDS" };
        public static bool IsHot(SectorDef s) { var h = HotZone; return h != null && s != null && h.id == s.id; }
        public static string HotLine { get { var h = HotZone; return h == null ? null : $"HOT ZONE: {h.name.ToUpperInvariant()}  //  {Mutators[HotMutator]} ({MutatorText[HotMutator]})  //  x2 {MaterialNames[HotMaterial]}"; } }
        // the in-run mutator (only on the Hot Zone)
        public static bool Mut(int i) => R.hot && HotMutator == i;

        // ================= Weekly Mutation (rank 8; upgrade-system 9.3): a fixed PAIR of the mutator pool on every zone for the ISO week,
        // the same for everyone (seeded by the week); at most one of the harsh ones (the second is Elite Hunt or Glass Cannon).
        // +25% Credits on runs; the first clear of a zone that week pays 20 Cores, at most 5 a week.
        public static bool SimWeekly;   // (sims: off unless asked, so regressions stay comparable)
        public static bool WeeklyDev { get => PlayerPrefs.GetInt("ms.dev.weekly", 0) == 1; set { PlayerPrefs.SetInt("ms.dev.weekly", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool WeeklyOn => Game.SimRun ? SimWeekly : Meta.Rank >= 8 || WeeklyDev;
        public static int IsoWeek { get { var d = OpsNow.Date; return System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday); } }
        public static string WeekKey => OpsNow.Year + "-W" + IsoWeek;
        public static (int a, int b) WeeklyPair
        {
            get
            {
                var rng = new System.Random(OpsNow.Year * 100 + IsoWeek);
                int[] harsh = { ZoneMods.Frenzy, ZoneMods.Armoured, ZoneMods.Brood, ZoneMods.AcidRain, ZoneMods.Blackout, ZoneMods.Regen, ZoneMods.Storm, ZoneMods.Volatile }, kind = { ZoneMods.EliteHunt, ZoneMods.Glass };
                return (harsh[rng.Next(harsh.Length)], kind[rng.Next(kind.Length)]);
            }
        }
        public static string WeeklyLine { get { var (a, b) = WeeklyPair; return $"WEEKLY MUTATION: {ZoneMods.Names[a]} + {ZoneMods.Names[b]}  //  +25% CREDITS"; } }
        static void WeekRoll() { if (D.weeklyKey != WeekKey) { D.weeklyKey = WeekKey; D.weeklyClears.Clear(); } }
        public static string WeeklyClear(SectorDef zone)
        {   // the first clear of a zone this week: 20 Cores (5 a week)
            if (!WeeklyOn || zone == null || zone.endless) return null; WeekRoll();
            if (D.weeklyClears.Contains(zone.id) || D.weeklyClears.Count >= 5) return null;
            D.weeklyClears.Add(zone.id); D.cores += 20; Progress.Save();
            return $"WEEKLY MUTATION  //  FIRST CLEAR THIS WEEK +20 CORES ({D.weeklyClears.Count}/5)";
        }

        // ================= achievements
        public class Ach { public string id, name, desc, title; public int cores; public Func<bool> check; }
        static int ClearedNum(int n) { foreach (var s in SectorDefs.All) if (s.num == n && Progress.Cleared(s)) return 1; return 0; }
        static bool AllWeaponsUsed => Stat("w0") > 0 && Stat("w1") > 0 && Stat("w2") > 0 && Stat("w3") > 0;
        static bool AnySet4 { get { for (int i = 0; i < Meta.Sets.Length; i++) if (Meta.Set4On(i)) return true; return false; } }
        static bool AnyMythic { get { for (int i = 0; i < 5; i++) { var g = Progress.Equipped(i); if (g != null && g.rarity >= 4) return true; } return false; } }
        static bool AnyMk5 { get { for (int w = 0; w < 4; w++) if (Meta.WeaponTier(w) >= 4) return true; return false; } }
        static bool AnyLv100 { get { foreach (var l in D.weaponLv) if (l >= 100) return true; return false; } }
        static bool AnyCapstone { get { foreach (var n in Meta.Nodes) if (n.tier == 3 && Meta.ResearchLv(n.id) > 0) return true; return false; } }
        static bool Perfect9 { get { foreach (var s in SectorDefs.All) { if (s.endless) continue; int n = 0; for (int t = 0; t < 3; t++) for (int i = 0; i < 3; i++) if (D.stars.Contains(Meta.StarKey(s, t, i))) n++; if (n >= 9) return true; } return false; } }
        static bool AltPath
        {
            get
            {   // cleared both zones that open from the same zone
                var by = new Dictionary<string, int>();
                foreach (var s in SectorDefs.All) if (s.unlockAny != null && s.unlockAny.Length > 0 && Progress.Cleared(s)) { by.TryGetValue(s.unlockAny[0], out int n); by[s.unlockAny[0]] = n + 1; }
                foreach (var v in by.Values) if (v >= 2) return true; return false;
            }
        }
        public static readonly Ach[] All =
        {
            new() { id = "first_drop", name = "FIRST DROP", desc = "Clear zone 1", cores = 20, check = () => ClearedNum(1) > 0 },
            new() { id = "evolved", name = "EVOLVED", desc = "Pick an evolution", cores = 20, check = () => Stat("evos") > 0 },
            new() { id = "near_miss", name = "NEAR MISS", desc = "Win under 5% HP", cores = 20, check = () => Stat("nearmiss") > 0 },
            new() { id = "alt_path", name = "ALT PATH", desc = "Clear both branches of a fork", cores = 30, check = () => AltPath },
            new() { id = "squad_up", name = "SQUAD UP", desc = "Unlock every hero", cores = 30, check = () => Meta.HeroStars("vanguard") > 0 && Meta.HeroStars("scout") > 0 },
            new() { id = "bloater_popper", name = "BLOATER POPPER", desc = "Kill 500 Bloaters", cores = 30, check = () => Stat("bloaters") >= 500 },
            new() { id = "leap_of_faith", name = "LEAP OF FAITH", desc = "Dodge 200 pounces", cores = 30, check = () => Stat("dodges") >= 200 },
            new() { id = "purple_rain", name = "PURPLE RAIN", desc = "Collect 50,000 biomass", cores = 30, check = () => Stat("biomass") >= 50000 },
            new() { id = "comeback", name = "COMEBACK", desc = "Clear a zone you failed 3 times", cores = 30, check = () => Stat("comeback") > 0 },
            new() { id = "full_set", name = "FULL SET", desc = "Turn on a 4-piece set bonus", cores = 40, check = () => AnySet4 },
            new() { id = "all_four", name = "ALL FOUR", desc = "Finish a run with every weapon", cores = 50, title = "GUNSMITH", check = () => AllWeaponsUsed },
            new() { id = "scouts_honour", name = "SCOUT'S HONOUR", desc = "Unlock the Scout", cores = 50, check = () => Meta.HeroStars("scout") > 0 },
            new() { id = "landfall", name = "LANDFALL", desc = "Clear zone 5", cores = 50, check = () => ClearedNum(5) > 0 },
            new() { id = "hardened", name = "HARDENED", desc = "5 Hard clears", cores = 50, check = () => Stat("hard") >= 5 },
            new() { id = "starstruck", name = "STARSTRUCK", desc = "Earn 50 stars", cores = 50, check = () => Meta.StarsTotal() >= 50 },
            new() { id = "perfectionist", name = "PERFECTIONIST", desc = "All 9 stars in a zone", cores = 50, check = () => Perfect9 },
            new() { id = "untouchable", name = "UNTOUCHABLE", desc = "No damage on a floor 5", cores = 50, check = () => Stat("untouch5") > 0 },
            new() { id = "speedrunner", name = "SPEEDRUNNER", desc = "Clear a zone under 2:30", cores = 50, check = () => Stat("speed") > 0 },
            new() { id = "daily_grind", name = "DAILY GRIND", desc = "Daily Ops 7 days in a row", cores = 50, check = () => D.dailyStreak >= 7 },
            new() { id = "bounty_hunter", name = "BOUNTY HUNTER", desc = "Finish 12 Bounties", cores = 50, check = () => Stat("bounties") >= 12 },
            new() { id = "outbreak", name = "OUTBREAK", desc = "Clear zone 10", cores = 75, check = () => ClearedNum(10) > 0 },
            new() { id = "relic_hunter", name = "RELIC HUNTER", desc = "Own 6 relics", cores = 75, check = () => Meta.RelicsOwned >= 6 },
            new() { id = "swarm_breaker", name = "SWARM BREAKER", desc = "10,000 kills", cores = 75, check = () => Stat("kills") >= 10000 },
            new() { id = "researcher", name = "RESEARCHER", desc = "Buy a capstone", cores = 75, check = () => AnyCapstone },
            new() { id = "gatekeeper", name = "GATEKEEPER", desc = "Swarm Gate floor 30", cores = 75, check = () => PlayerPrefs.GetInt("ms.gate.best", 0) >= 30 },
            new() { id = "five_star", name = "FIVE-STAR", desc = "A hero at 5 stars", cores = 100, title = "ASCENDED", check = () => Meta.HeroStars("sentinel") >= 5 || Meta.HeroStars("vanguard") >= 5 || Meta.HeroStars("scout") >= 5 },
            new() { id = "deep_infection", name = "DEEP INFECTION", desc = "Clear zone 15", cores = 100, check = () => ClearedNum(15) > 0 },
            new() { id = "nightmare_fuel", name = "NIGHTMARE FUEL", desc = "A Nightmare clear", cores = 100, check = () => D.nightmareClears > 0 },
            new() { id = "mythic", name = "MYTHIC", desc = "Equip a Mythic", cores = 100, check = () => AnyMythic },
            new() { id = "mk5", name = "MK V", desc = "A weapon at Mk V", cores = 100, check = () => AnyMk5 },
            new() { id = "queen_slayer", name = "QUEEN SLAYER", desc = "Clear zone 18", cores = 150, title = "VOIDWALKER", check = () => ClearedNum(18) > 0 },
            new() { id = "mastered", name = "MASTERED", desc = "A weapon at level 100", cores = 150, check = () => AnyLv100 },
        };
        public static bool Has(Ach a) => D.achievements.Contains(a.id);
        public static int AchDone { get { int n = 0; foreach (var a in All) if (Has(a)) n++; return n; } }
        // grants what's newly earned; returns the toast lines
        public static List<string> CheckAchievements()
        {
            var l = new List<string>();
            foreach (var a in All)
            {
                if (Has(a) || !a.check()) continue;
                D.achievements.Add(a.id); D.cores += a.cores; if (a.title != null && string.IsNullOrEmpty(D.title)) D.title = a.title;
                l.Add($"ACHIEVEMENT  //  {a.name}  +{a.cores} CORES" + (a.title != null ? $"  (TITLE: {a.title})" : ""));
            }
            if (l.Count > 0) Progress.Save();
            return l;
        }

        // ================= run end: the counters, today's missions, then the achievements (toasts on the summary)
        public static List<string> RunEnd(Game g, SectorDef zone, bool won, int tier, List<int> newStars, float hpFrac, float time, bool floor5Clean, int weapon, string hero)
        {
            var toasts = new List<string>();
            if (Game.SimRun) return toasts;
            Roll(); justDone.Clear();
            AddStat("kills", R.kills); AddStat("bloaters", R.bloaterKills); AddStat("dodges", R.pounceDodges); AddStat("biomass", R.biomass); AddStat("evos", R.evos);
            AddStat("w" + weapon, 1);
            if (zone != null && !zone.endless)
            {
                if (won)
                {
                    if (Stat("fail." + zone.id) >= 3 && !Progress.Cleared(zone)) AddStat("comeback", 1);
                    if (tier == 1) AddStat("hard", 1);
                    if (hpFrac < 0.05f) AddStat("nearmiss", 1);
                    if (time < 150f) AddStat("speed", 1);
                    if (floor5Clean) AddStat("untouch5", 1);
                }
                else AddStat("fail." + zone.id, 1);
            }
            foreach (var m in Today())
            {
                switch (m.def.id)
                {
                    case "clear3": if (won) Advance(m, 1); break;
                    case "kill300": Advance(m, R.kills); break;
                    case "evo2": Advance(m, R.evos); break;
                    case "hard": if (won && tier == 1) Advance(m, 1); break;
                    case "stars2": Advance(m, newStars != null ? newStars.Count : 0); break;
                    case "bloat5": Advance(m, R.bloaterKills); break;
                    case "heroes":
                    {   // a bit per hero
                        int bit = hero == "vanguard" ? 2 : hero == "scout" ? 4 : 1, mask = Stat("heroes." + DayKey);
                        if ((mask & bit) == 0) { AddStat("heroes." + DayKey, bit); Advance(m, 1); }
                        break;
                    }
                    case "hot": if (won && R.hot) Advance(m, 1); break;
                    case "caches4": Advance(m, R.caches); break;
                    case "nosurv": if (won && !R.survivalCard) Advance(m, 1); break;
                    case "bonus": if (newStars != null && newStars.Contains(2)) Advance(m, 1); break;
                }
            }
            if (won) { var wc = WeeklyClear(zone); if (wc != null) toasts.Add(wc); }
            toasts.AddRange(justDone); justDone.Clear();
            if (ChestReady) toasts.Add("DAILY OPS  //  ALL 3 DONE: OPEN THE CHEST IN OPS");
            toasts.AddRange(CheckAchievements());
            Progress.Save();
            return toasts;
        }
        // menu-side completions (upgrades, merges) wait here for the next toast
        public static List<string> TakeJustDone() { var l = new List<string>(justDone); justDone.Clear(); return l; }
    }
}
