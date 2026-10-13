using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // Elites and enemy modifiers (Design/enemies.json "elites" / "modifiers" / spawn.threatTiers). An elite: x3 HP,
    // x1.25 size, x2 biomass, always drops a power-up, 1 modifier (2 from floor 4, 3 in tier 6+). Regular enemies roll
    // a modifier by the tier's modChance. Ticks and Spore Pods are never elites.
    public static class Elites
    {
        [System.Flags] public enum Mod { None = 0, Armoured = 1, Volatile = 2, Swift = 4, HiveLinked = 8, Regenerating = 16, Shielded = 32, Vampiric = 64, Splitting = 128, Frenzied = 256, Cloaked = 512, Toxic = 1024, Magnetic = 2048 }
        static readonly (Mod m, int minFloor, int minTier, bool sustain)[] Table =
        {
            (Mod.Armoured, 2, 1, false), (Mod.Volatile, 1, 1, false), (Mod.Swift, 1, 1, false), (Mod.HiveLinked, 2, 2, false),
            (Mod.Regenerating, 2, 2, true), (Mod.Shielded, 3, 2, false), (Mod.Vampiric, 3, 3, true), (Mod.Splitting, 3, 2, false),
            (Mod.Frenzied, 2, 1, false), (Mod.Cloaked, 4, 3, false), (Mod.Toxic, 2, 2, false), (Mod.Magnetic, 4, 3, false),
        };
        // per threat tier (1-6): elite chance, one-mod chance, two-mod chance, elite mod count
        static readonly float[] EliteChance = { 0.02f, 0.03f, 0.04f, 0.05f, 0.06f, 0.08f };
        static readonly float[] OneMod = { 0f, 0.05f, 0.1f, 0.15f, 0.2f, 0.25f };
        static readonly float[] TwoMod = { 0f, 0f, 0f, 0f, 0.05f, 0.1f };

        public static float EliteChanceFor(int tier) => EliteChance[Mathf.Clamp(tier, 1, 6) - 1];

        // a fresh spawn: maybe an elite, maybe a modifier or two
        public static void Roll(Game g, Enemy e, int tier, int floor, bool forceElite = false)
        {
            if (e.type.nest || e.type.ai == "tick" || e.type.ai == "spore_pod" || e.boss) return;
            int t = Mathf.Clamp(tier, 1, 6);
            float diff = Meta.Difficulty == 2 ? 3f : Meta.Difficulty == 1 && floor >= 2 ? 2.5f : 1f;   // Hard: elites from floor 2; Nightmare: more of everything
            bool elite = forceElite || Random.value < EliteChance[t - 1] * (1 + 0.25f * (floor - 1)) * diff;
            int mods = 0;
            if (elite) mods = t >= 6 ? 3 : floor >= 4 ? 2 : 1;
            else { float r = Random.value; mods = r < TwoMod[t - 1] ? 2 : r < TwoMod[t - 1] + OneMod[t - 1] ? 1 : 0; }
            if (elite) MakeElite(e);
            for (int i = 0; i < mods; i++) AddMod(e, tier, floor);
        }

        public static void MakeElite(Enemy e)
        {
            if (e.elite) return;
            e.elite = true; e.scale *= 1.25f; e.r *= 1.25f; e.hp *= 3f; e.maxHp = e.hp; e.damage *= 1.35f;
        }

        static void AddMod(Enemy e, int tier, int floor)
        {
            var pool = new List<Mod>();
            bool hasSustain = (e.mods & (Mod.Regenerating | Mod.Vampiric)) != 0;
            foreach (var (m, mf, mt, sus) in Table)
            {
                if ((e.mods & m) != 0 || floor < mf || tier < mt || (sus && hasSustain)) continue;
                if (m == Mod.Splitting && e.type.name.StartsWith("splitter")) continue;
                if (m == Mod.Cloaked && e.type.ai == "stalker") continue;
                if (m == Mod.Magnetic && floor <= 3 && (e.type.bloater || e.type.name.StartsWith("splitter"))) continue;
                pool.Add(m);
            }
            if (pool.Count == 0) return;
            var pick = pool[Random.Range(0, pool.Count)]; e.mods |= pick;
            switch (pick)
            {
                case Mod.Swift: e.speed *= 1.5f; e.hp *= 0.75f; e.maxHp = e.hp; break;
                case Mod.Frenzied: e.speed *= 1.4f; break;
                case Mod.Shielded: e.shieldHits = 5; break;
                case Mod.Armoured: e.armourHp = e.maxHp * 0.7f; break;
            }
        }

        // the damage a hit really does (armour halves it until 30% HP is gone; a front shield eats 5 hits)
        public static float Damage(Enemy e, float dmg, Vector2 bulletDir)
        {
            if (e.mods == Mod.None) return dmg;
            if ((e.mods & Mod.Shielded) != 0 && e.shieldHits > 0)
            {
                var fwd = new Vector2(Mathf.Sin(e.yaw), Mathf.Cos(e.yaw));
                if (Vector2.Dot(fwd, -bulletDir) > 0.5f) { e.shieldHits--; e.shieldRegrow = 6f; return 0; }
            }
            if ((e.mods & Mod.Armoured) != 0 && e.hp > e.armourHp) dmg *= 0.5f;
            e.noDamageT = 0;
            return dmg;
        }

        public static void Tick(Game g, Enemy e, float dt)
        {
            if (e.mods == Mod.None || e.dead) return;
            e.noDamageT += dt;
            if ((e.mods & Mod.Regenerating) != 0 && e.noDamageT > 2f && e.burnT <= 0 && e.frozen <= 0) e.hp = Mathf.Min(e.maxHp, e.hp + e.maxHp * 0.08f * dt);
            if ((e.mods & Mod.Shielded) != 0 && e.shieldHits < 5 && (e.shieldRegrow -= dt) <= 0) e.shieldHits = 5;
            if ((e.mods & Mod.Cloaked) != 0) { e.cloaked = (e.p - g.player.p).sqrMagnitude > 25f && e.revealT <= 0; e.revealT -= dt; }
            if ((e.mods & Mod.Toxic) != 0 && (e.toxicT -= dt) <= 0) { e.toxicT = 0.6f; EnemyAI.AcidPuddle(e.p, 1f, 3f, 6f); }
            if ((e.mods & Mod.Magnetic) != 0) { var d = e.p - g.player.p; if (d.sqrMagnitude < 36f && d.sqrMagnitude > 0.5f) g.player.p += d.normalized * 0.8f * dt; }
        }

        // a contact hit landed on the hero
        public static void OnHitHero(Enemy e, float dmg) { if ((e.mods & Mod.Vampiric) != 0) { e.hp = Mathf.Min(e.maxHp * 1.5f, e.hp + dmg); e.scale *= 1.02f; } }

        public static void OnDeath(Game g, Enemy e)
        {
            if (e.mods == Mod.None) return;
            if ((e.mods & Mod.Volatile) != 0)
            {   // 1 s fuse, then 12 HP in 2.5 m (hits enemies too)
                var at = e.p;
                EnemyAI.Tele.Circle(at, 2.5f, 1f, new Color(1f, 0.55f, 0.15f, 0.85f), () =>
                {
                    if ((g.player.p - at).magnitude < 2.5f + g.cfg.player.radius && g.player.dashT <= 0) g.HurtPlayer(ZoneCells.Hp(g, 12));
                    foreach (var o in g.swarm.enemies) if (!o.dead && (o.p - at).magnitude < 2.5f) { o.hp -= ZoneCells.RH(g) * 4f; if (o.hp <= 0) g.KillEnemy(o); }
                    if (!Game.SimRun) Fx.I.Boom(MazeMap.ToWorld(at, 0.3f), 2.5f);
                }, ring: true);
            }
            if ((e.mods & Mod.Splitting) != 0) EnemyAI.QueueCopies(e, 2, 0.3f, 0.25f);
        }

        // a gold ring under every elite (placeholder for the gold rim / rising particle)
        static readonly Dictionary<Enemy, Transform> rings = new(); static Material gold;
        public static void TickRings(Game g)
        {
            if (Game.SimRun) return;
            foreach (var e in g.swarm.enemies)
            {
                if (!e.elite || e.dead || rings.ContainsKey(e)) continue;
                gold ??= Mats.Glow(new Color(1f, 0.78f, 0.2f, 0.85f));
                var go = new GameObject("EliteRing"); go.AddComponent<MeshFilter>().sharedMesh = Fx.Ring;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = gold; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rings[e] = go.transform;
            }
            var dead = new List<Enemy>();
            foreach (var kv in rings)
            {
                if (kv.Key.dead || !g.swarm.enemies.Contains(kv.Key)) { dead.Add(kv.Key); continue; }
                kv.Value.position = MazeMap.ToWorld(kv.Key.p, 0.06f); kv.Value.localScale = Vector3.one * (kv.Key.r * 1.5f + 0.2f * Mathf.Sin(Time.time * 4));
                kv.Value.gameObject.SetActive(!kv.Key.Hidden);
            }
            foreach (var e in dead) { Object.Destroy(rings[e].gameObject); rings.Remove(e); }
        }
        public static void ClearRings() { foreach (var t in rings.Values) if (t != null) Object.Destroy(t.gameObject); rings.Clear(); }
        public static string Tag(Enemy e)
        {
            if (!e.elite && e.mods == Mod.None) return null;
            var s = e.elite ? "ELITE " : "";
            foreach (var (m, _, _, _) in Table) if ((e.mods & m) != 0) s += m.ToString().ToUpperInvariant() + " ";
            return s.Trim();
        }
    }
}
