using UnityEngine;

namespace Maze
{
    // Hero skills (Design/powerups.json "abilities"), one per hero on the SKILL button:
    //   Sentinel - FLASH FREEZE (14 s): a cryo grenade at the densest pack within 12 m (0.6 s flight): everything in
    //              4.5 m freezes 2 s (bosses slowed 60%), frozen enemies take +50%.
    //   Vanguard - BARRAGE (16 s): plant for 3 s (can turn, can't walk), minigun x3 fire rate, +2 pierce, every hit
    //              shoves 0.4 m; a stomp ring shoves the pack on start. The minigun stays at the hip.
    public static class Abilities
    {
        public static float Cd, BarrageT;
        public static bool Vanguard => Prefs.Vanguard;
        public static bool Scout => Prefs.Scout;
        public static float DecoyT; static Vector2 decoyAt;
        public static RunMods M;
        static int Rank => Meta.AbilityRank(Meta.HeroId);   // hero stars: 2 = rank 2, 4 = rank 3
        public static float Cooldown => (Vanguard ? (Rank >= 2 ? 13f : 16f) : Scout ? (Rank >= 2 ? 12f : 14f) : (Rank >= 2 ? 12f : 14f)) * Mathf.Pow(0.85f, M != null ? M["quick_recharge"] : 0) - (Game.TS(2, 2) ? 2f : 0f) - (Game.TV(7, 1) ? 3f : 0f);
        public static float BaseCooldown => (Vanguard ? (Rank >= 2 ? 13f : 16f) : Scout ? (Rank >= 2 ? 12f : 14f) : (Rank >= 2 ? 12f : 14f)) - (Game.TS(2, 2) ? 2f : 0f) - (Game.TV(7, 1) ? 3f : 0f);   // before a run's cards (the briefing)   // talents
        static float Area => 1f + 0.25f * (M != null ? M["wide_area"] : 0);
        public static string Name => Vanguard ? "BARRAGE" : Scout ? "DECOY" : "FREEZE";
        public static float RateMul => BarrageT > 0 ? 3f : 1f;
        public static int Pierce => BarrageT > 0 ? (Rank >= 3 ? 99 : 2) : 0;
        public static bool Rooted => BarrageT > 0;
        public static void Reset() { Cd = 4f; BarrageT = 0; DecoyT = 0; }

        public static void Tick(Game g, float dt, bool pressed)
        {
            M = g.mods;
            Cd -= dt; BarrageT -= dt;
            if (DecoyT > 0 && (DecoyT -= dt) <= 0 && Rank >= 3)
            {   // rank 3: the hologram explodes for 300% weapon damage (3 m)
                var w = g.cfg.weapons[g.player.weapon]; float dmg = 3f * w.damage * w.pellets * Progress.DamageMul(Progress.HeroWeapon);
                foreach (var e in g.swarm.enemies) if (!e.dead && (e.p - decoyAt).sqrMagnitude < 9f) { e.hp -= dmg; e.flash = 1; if (e.hp <= 0) g.KillEnemy(e); }
                if (!Game.SimRun) Fx.I.Sparks(MazeMap.ToWorld(decoyAt, 0.8f), 24, new Color(0.4f, 0.95f, 0.9f), 7, 0.6f, 0.12f, 4);
                g.AddShake(0.2f);
            }
            if (!pressed || Cd > 0) return;
            Cd = Cooldown;
            if (Vanguard) Barrage(g); else if (Scout) Decoy(g); else FlashFreeze(g);
        }

        static void FlashFreeze(Game g)
        {
            // the densest pack in reach (tap = auto aim), else 8 m ahead
            Vector2 at = g.player.p + g.player.aim * 8f; int best = 0;
            foreach (var e in g.swarm.enemies)
            {
                if (e.dead || e.Hidden || (e.p - g.player.p).sqrMagnitude > 144f) continue;
                int n = 0; foreach (var o in g.swarm.enemies) if (!o.dead && (o.p - e.p).sqrMagnitude < 20f) n++;
                if (n > best) { best = n; at = e.p; }
            }
            int throws = M["cryo_cascade"] > 0 ? 2 : 1; float R = (4.5f + (Rank >= 3 ? 1f : 0f) + (Game.TS(2, 1) ? 1f : 0f)) * Area, fz = 2f + (M["frost_core"] > 0 ? 1f : 0f);
            for (int k = 0; k < throws; k++)
            {
            var c = k == 0 ? at : at + Random.insideUnitCircle.normalized * R;
            EnemyAI.Tele.Circle(c, R, 0.6f, new Color(0.7f, 0.9f, 1f, 0.85f), () =>
            {
                foreach (var e in g.swarm.enemies)
                {
                    if (e.dead || (e.p - c).magnitude > R + e.r) continue;
                    if (e.boss) e.slowT = 2f; else if (!e.type.nest) e.frozen = fz;
                }
                if (!Game.SimRun) { if (FirstCryoFx.Active) FirstCryoFx.Get().FreezeBurst(MazeMap.ToWorld(c, .2f), R); else { Fx.I.Sparks(MazeMap.ToWorld(c, 0.6f), 30, new Color(0.75f, 0.92f, 1f), 7, 0.7f, 0.12f, 4); Fx.I.Smoke(MazeMap.ToWorld(c, 0.3f), 12, new Color(0.85f, 0.95f, 1f), 3, 0.9f, 0.6f, 1); } }
                g.AddShake(0.15f);
            }, ring: true);
            }
            g.hud.ShowBanner("FLASH FREEZE");
        }

        // the Scout: a hologram 2 m ahead pulls the swarm's aggro 3 s (rank 2: 4 s and 12 s cooldown; rank 3: it explodes)
        static void Decoy(Game g)
        {
            var at = g.player.p + g.player.aim.normalized * 2f;
            int x = Mathf.FloorToInt(at.x / MazeMap.C), z = Mathf.FloorToInt(at.y / MazeMap.C); if (!MazeMap.Walkable(x, z)) at = g.player.p;
            DecoyT = Rank >= 2 ? 4f : 3f; decoyAt = at;
            PowerUps.DecoyFor(at, DecoyT, boom: false);
            if (!Game.SimRun) Fx.I.Sparks(MazeMap.ToWorld(at, 1f), 16, new Color(0.4f, 0.95f, 0.9f), 4, 0.5f, 0.1f, 3);
            g.hud.ShowBanner("DECOY");
        }
        public static bool DecoyActive(out Vector2 at) { at = decoyAt; return DecoyT > 0; }

        static void Barrage(Game g)
        {
            BarrageT = (M["siege_protocol"] > 0 ? 5f : Rank >= 2 ? 4f : 3f) + (Game.TV(4, 2) ? 1f : 0f);
            if (Game.TV(7, 2)) g.player.hp = Mathf.Min(g.player.maxHp, g.player.hp + g.player.maxHp * 0.15f);   // talents
            foreach (var e in g.swarm.enemies)
            {   // the stomp ring
                var d = e.p - g.player.p; if (e.dead || d.magnitude > 3f * Area || e.boss) continue;
                e.k += d.normalized * 7f;
            }
            if (!Game.SimRun) Fx.I.Smoke(MazeMap.ToWorld(g.player.p, 0.1f), 14, new Color(0.75f, 0.7f, 0.62f), 4, 0.6f, 0.4f, 0.5f);
            g.AddShake(0.12f); Haptics.Heavy();
            g.hud.ShowBanner("BARRAGE");
        }
    }
}
