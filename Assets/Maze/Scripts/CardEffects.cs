using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The 22 level-up cards and 4 evolutions of Design/powerups.json, on top of the first set (GameRun.Cards).
    // Effects hang off a few hooks: a bullet hit, a kill, a dash, a power-up pickup, the skill, and a per-frame tick.
    public partial class Game
    {
        public static readonly Card[] Cards2 =
        {
            new Card { id = "armour_piercer", name = "ARMOUR PIERCER", desc = "+25% damage through armoured fronts", icon = "pierce", group = 0, max = 2 },
            new Card { id = "hunters_mark", name = "HUNTER'S MARK", desc = "Auto-fire picks Shriekers, Menders, Lancers, Spitters first; +15% vs them", icon = "crit", group = 0, max = 1 },
            new Card { id = "adrenaline_dash", name = "ADRENALINE DASH", desc = "+35% fire rate for 2.5 s after a dash", icon = "dashcd", group = 2, max = 2 },
            new Card { id = "scavenger", name = "SCAVENGER", desc = "Power-ups last 30% longer, drop 50% more", icon = "bioheal", group = 1, max = 2 },
            new Card { id = "thick_hide", name = "THICK HIDE", desc = "Half the drain from latched Ticks, half the knockback", icon = "shield", group = 1, max = 1 },
            new Card { id = "acid_walker", name = "ACID WALKER", desc = "Immune to acid and web slow", icon = "shield", group = 1, max = 1 },
            new Card { id = "overkill", name = "OVERKILL", desc = "Damage past a kill carries to an enemy within 4 m", icon = "heavy", group = 0, max = 2 },
            new Card { id = "chain_lightning", name = "CHAIN LIGHTNING", desc = "Every 6th hit chains to 3 enemies", icon = "storm", group = 0, max = 2 },
            new Card { id = "afterimage", name = "AFTERIMAGE", desc = "Dashing leaves a decoy for 1.5 s", icon = "trail", group = 2, max = 1 },
            new Card { id = "frost_core", name = "FROST CORE", desc = "Hits slow 10%; freezes come faster; Flash Freeze +1 s", icon = "range", group = 0, max = 1 },
            new Card { id = "pyromaniac", name = "PYROMANIAC", desc = "Burning enemies explode on death", icon = "grenade", group = 0, max = 1 },
            new Card { id = "executioner", name = "EXECUTIONER", desc = "Executes enemies under 15% HP; +20% on bosses under 30%", icon = "crit", group = 0, max = 1 },
            new Card { id = "elite_hunter", name = "ELITE HUNTER", desc = "+30% damage to elites and bosses", icon = "marksman", group = 0, max = 2 },
            new Card { id = "phase_sight", name = "PHASE SIGHT", desc = "See Stalkers and Burrowers; +25% crit vs them", icon = "range", group = 0, max = 1 },
            new Card { id = "second_wind", name = "SECOND WIND", desc = "Killing an elite or Shrieker resets dash, skill -4 s", icon = "dashcd", group = 2, max = 1 },
            new Card { id = "orbital_uplink", name = "ORBITAL UPLINK", desc = "A free Orbital Strike every 18 s", icon = "grenade", group = 4, max = 1 },
            new Card { id = "hive_breaker", name = "HIVE BREAKER", desc = "Kills strip Frenzy from the pack around", icon = "heavy", group = 4, max = 1 },
            new Card { id = "power_surge", name = "POWER SURGE", desc = "Power-up pickups blast a 4 m shockwave", icon = "storm", group = 4, max = 1 },
            new Card { id = "cryo_cascade", name = "CRYO CASCADE", desc = "Flash Freeze throws 2; frozen kills shatter", icon = "range", group = 4, max = 1, hero = "sentinel" },
            new Card { id = "siege_protocol", name = "SIEGE PROTOCOL", desc = "Barrage lasts 5 s", icon = "storm", group = 4, max = 1, hero = "vanguard" },
            new Card { id = "quick_recharge", name = "QUICK RECHARGE", desc = "-15% skill cooldown", icon = "dashcd", group = 2, max = 3 },
            new Card { id = "wide_area", name = "WIDE AREA", desc = "+25% skill area", icon = "range", group = 2, max = 2 },
            // evolutions (gold): offered once their recipe is met
            new Card { id = "cryo_lance", name = "CRYO LANCE", desc = "RIFLE + FROST CORE + PIERCE: every 4th shot freezes what it pierces", icon = "railgun", evo = true, max = 1 },
            new Card { id = "tesla_gatling", name = "TESLA GATLING", desc = "MINIGUN + CHAIN LIGHTNING: every 3rd bullet chains", icon = "inferno", evo = true, max = 1 },
            new Card { id = "dragons_breath", name = "DRAGON'S BREATH", desc = "SHOTGUN + PYROMANIAC: pellets ignite", icon = "breacher", evo = true, max = 1 },
            new Card { id = "swarm_breaker", name = "SWARM BREAKER", desc = "SMG + OVERKILL: kills burst into 4 bullets", icon = "arcsmg", evo = true, max = 1 },
            new Card { id = "hornet_swarm", name = "HORNET SWARM", desc = "NEEDLERS + RICOCHET MAX: pops seek 3 new targets", icon = "arcsmg", evo = true, max = 1 },
            new Card { id = "venom_needles", name = "VENOM NEEDLES", desc = "NEEDLERS MK III + CRIT + PIERCE MAX: pops poison, it spreads on death", icon = "arcsmg", evo = true, max = 1 },
        };

        bool CanOffer2(Card k)
        {
            if (k.group == 4 && floor < 3) return false;   // epics from floor 3
            switch (k.id)
            {
                case "cryo_lance": return WeaponName == "RIFLE" && mods["frost_core"] > 0 && mods["pierce"] > 0;
                case "tesla_gatling": return WeaponName == "MINIGUN" && mods["chain_lightning"] > 0;
                case "dragons_breath": return WeaponName == "SHOTGUN" && mods["pyromaniac"] > 0;
                case "swarm_breaker": return WeaponName == "SMG" && mods["overkill"] > 0;
                case "hornet_swarm": return WeaponName == "NEEDLER" && mods["rico"] >= 3 - EvoRush;
                case "venom_needles": return WeaponName == "NEEDLER" && Meta.WeaponTier(8) >= 2 && mods["crit"] >= 3 - EvoRush && mods["pierce"] >= 3 - EvoRush;
            }
            return !k.evo;
        }

        int hitCount, shotCount; float adrenalT, uplinkT = 18f;
        public float CardRateMul => (adrenalT > 0 ? 1f + 0.35f * mods["adrenaline_dash"] : 1f)
            * (1 + Meta.GloveRate) * (Meta.Relic(14) ? 1f + Mathf.Min(Meta.RelicV, 0.01f * (floorBio / 10)) : 1f) * (Meta.Set4On(2) && sinceDash < 3f ? 1.4f : 1f) * (Meta.Set4On(6) && stillT >= 1f ? 1.25f : 1f)
            * (TS(5, 2) && player.hp < player.maxHp * 0.3f ? 1.3f : 1f) * (TV(0, 1) ? 1.15f : 1f);   // talents   // gloves, OVERCLOCK 4, JUGGERNAUT 4
        float sinceDash = 99, stillT, regenT; int voltShots; public int floorBio;   // REACTOR CORE: biomass this floor
        public float GearDamageTakenMul => Meta.Set2On(6) && stillT >= 1f ? 0.8f : 1f;   // JUGGERNAUT 2
        public float GearMoveMul => (1f + Meta.BootsSpeed) * (TS(0, 2) ? 1.08f : 1f) * (TV(5, 2) ? 0.95f : 1f);

        // ---- a bullet hits e for dmg (before it is applied): the multiplier
        float CardHitMul(Enemy e)
        {
            float m = 1f;
            if (mods["hunters_mark"] > 0 && IsPriority(e)) m *= 1.15f;
            if (mods["elite_hunter"] > 0 && (e.elite || e.boss)) m *= 1f + 0.3f * mods["elite_hunter"];
            if (mods["executioner"] > 0 && e.boss && e.hp < e.maxHp * 0.3f) m *= 1.2f;
            if (mods["phase_sight"] > 0 && (e.type.ai == "stalker" || e.type.ai == "burrower") && Random.value < 0.25f) m *= 2f;
            if (Meta.Set2On(0) && (e.slowT > 0 || e.frozen > 0)) m *= 1.1f;                                  // FROSTLINE 2
            if (ZoneMods.On[ZoneMods.Glass]) m *= 1.5f;   // GLASS CANNON
            if (cfg.weapons[player.weapon].kind == "lance")
            {   // CRYO LANCE: ramps to x2 after 2 s on one target
                if (e != lanceTarget) { lanceTarget = e; lanceSince = runT; }
                m *= 1f + Mathf.Min(1f, (runT - lanceSince) / 2f);
            }
            if (RunTalHero != "")
            {   // talents
                if (TS(0, 1) && stillT >= 0.5f) m *= 1.15f;
                if (TS(6, 2) && evolvedAny) m *= 1.15f;
                if (TS(7, 1) && e.boss) m *= 1.2f;
                if (TS(7, 2)) m *= 1f + Mathf.Min(0.3f, 0.1f * (crowdN / 5));
                if (TV(2, 2) && fireStreak >= 3f) m *= 1.25f;
                if (TV(5, 1) && mods["shield"] > 0) m *= 1.1f;
                if (TV(8, 2) && player.hp < player.maxHp * 0.25f) m *= 1.2f;
            }
            if (Meta.EquippedRelic >= 0)
            {   // relics: DARK LENS (far), BURROWER FANG (near), ACID GLAND (marked)
                float d = (e.p - player.p).magnitude;
                if (Meta.Relic(4) && d > 8f || Meta.Relic(7) && runT < e.acidUntil) m *= 1f + Meta.RelicV;
            }
            if (Meta.Set2On(1) && (e.elite || e.boss || e.type.nest)) m *= Meta.Set4On(1) && (e.boss || e.type.nest) ? 1.4f : 1.15f;   // HIVEBREAKER
            return m;
        }
        static bool IsPriority(Enemy e) => e.type.ai == "shrieker" || e.type.ai == "mender" || e.type.ai == "lancer" || e.type.ai == "spitter";

        void CardAfterHit(Enemy e, Bullet b, float dealt)
        {
            if (mods["frost_core"] > 0) e.slowT = Mathf.Max(e.slowT, 1f);
            if (b.w.kind == "lance") e.slowT = Mathf.Max(e.slowT, 0.6f);   // CRYO LANCE: slows
            if (b.w.kind == "thermal") e.burnT = Mathf.Max(e.burnT, 3f);   // THERMAL PROJECTOR: sets them burning
            if (TV(6, 1) && e.burnT > 0) e.burnT = Mathf.Max(e.burnT, 1f);                                         // talent: burns last longer
            if (Meta.Set4On(0)) e.slowT = Mathf.Max(e.slowT, 1.5f);                                              // FROSTLINE 4
            if (Meta.Set4On(5) && ++voltShots % 8 == 0) ChainHit(e, 4, 4f, dealt * 0.6f);                        // VOLT 4
            if (Meta.Relic(7)) e.acidUntil = runT + 3f;                                                            // ACID GLAND
            if (Meta.Relic(11) && ++relayHits % Mathf.RoundToInt(Meta.RelicV) == 0)
            {   // RELAY COIL: a lightning strike on the target, arcing to 2 more
                e.hp -= 4 * ZoneCells.RH(this); e.flash = 1; ChainHit(e, 2, 3f, 2 * ZoneCells.RH(this));
                if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 1.2f), 12, new Color(0.5f, 0.85f, 1f), 6, 0.4f, 0.08f, 3);
            }
            if (mods["executioner"] > 0 && !e.boss && e.hp > 0 && e.hp < e.maxHp * 0.15f) e.hp = 0;
            hitCount++;
            if (mods["chain_lightning"] > 0 && hitCount % 6 == 0) ChainHit(e, 3, 4f, ZoneCells.RH(this));
            if (Evolved2("tesla_gatling") && hitCount % 3 == 0) ChainHit(e, 2, 4f, ZoneCells.RH(this));
            if (Evolved2("dragons_breath") && b.w.pellets > 1) e.burnT = 3f;
            if (Evolved2("cryo_lance") && b.pierce > 0 && shotCount % 4 == 0 && !e.boss) e.frozen = Mathf.Max(e.frozen, 1f);
            if (mods["overkill"] > 0 && e.hp < 0)
            {   // what's left over jumps to the nearest enemy within 4 m
                float over = -e.hp * (0.5f * mods["overkill"]); Enemy n = null; float bd = 16f;
                foreach (var o in swarm.enemies) { if (o == e || o.dead) continue; float d = (o.p - e.p).sqrMagnitude; if (d < bd) { bd = d; n = o; } }
                if (n != null) { n.hp -= over; n.flash = 1; if (n.hp <= 0) KillEnemy(n); }
            }
        }
        bool Evolved2(string id) => mods.Has(id);

        void ChainHit(Enemy from, int n, float r, float dmg)
        {
            var hit = new List<Enemy> { from }; var cur = from;
            for (int k = 0; k < n; k++)
            {
                Enemy nx = null; float bd = r * r;
                foreach (var o in swarm.enemies) { if (o.dead || hit.Contains(o)) continue; float d = (o.p - cur.p).sqrSqr(); if (d < bd) { bd = d; nx = o; } }
                if (nx == null) break;
                hit.Add(nx); nx.hp -= dmg; nx.flash = 1;
                if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(nx.p, 0.9f), 4, new Color(0.4f, 0.8f, 1f), 3, 0.2f, 0.06f, 2);
                if (nx.hp <= 0) KillEnemy(nx);
                cur = nx;
            }
        }

        // ---- a kill
        void CardOnKill(Enemy e)
        {
            float rh = ZoneCells.RH(this);
            if (mods["pyromaniac"] > 0 && e.burnT > 0)
            {
                foreach (var o in swarm.enemies) if (!o.dead && o != e && (o.p - e.p).sqrMagnitude < 4f) { o.hp -= 3 * rh; o.burnT = Mathf.Max(o.burnT, 2f); if (o.hp <= 0) KillEnemy(o); }
                if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.6f), 10, new Color(1f, 0.5f, 0.15f), 4, 0.4f, 0.1f, 3);
            }
            if (mods["second_wind"] > 0 && (e.elite || e.type.ai == "shrieker")) { player.dashCd = 0; Abilities.Cd -= 4f; }
            if (Meta.Relic(2) && Random.value < Meta.RelicV)
            {   // SLAG FURNACE: a burst that sets everything within 2 m burning
                foreach (var o in swarm.enemies) if (!o.dead && o != e && (o.p - e.p).sqrMagnitude < 4f) o.burnT = Mathf.Max(o.burnT, 3f);
                if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(e.p, 0.4f), 10, new Color(1f, 0.45f, 0.1f), 4, 0.5f, 0.1f, 3);
            }
            if (e == huntElite) { huntElite = e; pendingLevels++; hud.ShowBanner("ELITE HUNTED: FREE CARD"); if (choosing == Choice.None && state == State.Playing) OpenChoice(Choice.LevelUp); }   // ELITE HUNT
            if (ZoneMods.On[ZoneMods.Volatile] && !e.boss && volT <= 0 && (e.p - player.p).sqrMagnitude < 2.25f) { volT = 0.4f; HurtPlayer(ZoneCells.Hp(this, 4)); }   // VOLATILE
            if (Meta.Relic(16) && e.elite) player.hp = Mathf.Min(player.maxHp, player.hp + player.maxHp * Meta.RelicV);   // HIVE PULSE
            if (Meta.Set4On(1) && e.elite) player.hp = Mathf.Min(player.maxHp, player.hp + ZoneCells.Hp(this, 10));   // HIVEBREAKER 4
            if (mods["hive_breaker"] > 0 && e.frenzy > 0) foreach (var o in swarm.enemies) if (!o.dead && (o.p - e.p).sqrMagnitude < 36f) { o.frenzy = 0; o.frozen = Mathf.Max(o.frozen, 1f); }
            if (mods["cryo_cascade"] > 0 && e.frozen > 0) ChainHit(e, 4, 4f, 2 * rh);
            if (Evolved2("swarm_breaker")) ChainHit(e, 4, 5f, rh);
            if (Evolved2("venom_needles") && e.burnT > 0) foreach (var o in swarm.enemies) if (!o.dead && o != e && (o.p - e.p).sqrMagnitude < 9f) o.burnT = Mathf.Max(o.burnT, 3f);   // poison spreads on death
        }

        // ---- per frame
        void CardTick(float dt)
        {
            adrenalT -= dt; sinceDash += dt;
            stillT = player.vel.sqrMagnitude < 0.25f ? stillT + dt : 0;
            if (Meta.Leg(1) && (regenT += dt) >= 4f / Meta.AffixMul(1)) { regenT = 0; player.hp = Mathf.Min(player.maxHp, player.hp + 1); }   // vest: +1 HP every 4 s
            if (mods["orbital_uplink"] > 0 && (uplinkT -= dt) <= 0) { uplinkT = 18f; PowerUps.OrbitalAt(this); }
            if (Meta.EquippedRelic >= 0) RelicTick(dt);
            ZoneMods.Tick(this, dt);   // OVERTIME / HARD TILT
            volT -= dt;
            if (ZoneMods.On[ZoneMods.AcidRain] && stillT >= 2f && (acidT += dt) >= 0.5f) { acidT = 0; HurtPlayer(ZoneCells.Hp(this, 1)); }   // ACID RAIN: 2 HP/s standing still
            if (ZoneMods.On[ZoneMods.Regen]) foreach (var o in swarm.enemies) if (!o.dead && (o.boss || o.type.nest) && o.hp < o.maxHp) o.hp = Mathf.Min(o.maxHp, o.hp + o.maxHp * 0.01f * dt);   // REGENERATING HIVE
            if (ZoneMods.On[ZoneMods.Storm] && (stormT -= dt) <= 0)
            {   // LIGHTNING STORM: a strike near the hero every 5 s (2 m ring, 1 s warning)
                stormT = 5f; var c = player.p + Random.insideUnitCircle * 6f;
                EnemyAI.Tele.Circle(c, 2f, 1f, new Color(0.6f, 0.8f, 1f, 0.85f), () =>
                {
                    float rh = ZoneCells.RH(this);
                    foreach (var o in swarm.enemies) if (!o.dead && (o.p - c).sqrMagnitude < 4f) { o.hp -= 4 * rh; o.flash = 1; if (o.hp <= 0) KillEnemy(o); }
                    if ((player.p - c).sqrMagnitude < 4f) HurtPlayer(ZoneCells.Hp(this, 12));
                    if (!SimRun) { Fx.I.Sparks(MazeMap.ToWorld(c, 1f), 18, new Color(0.6f, 0.85f, 1f), 7, 0.5f, 0.1f, 4); HazardAudio.Shot("sfx_lightning_strike", MazeMap.ToWorld(c, 0.5f), 0.9f); }
                    Feel.Shake(0.1f, 0.15f);
                });
            }
            if (TS(7, 2) && (crowdT -= dt) <= 0)
            {   // talent: the crowd within 5 m
                crowdT = 0.25f; crowdN = 0; foreach (var o in swarm.enemies) if (!o.dead && (o.p - player.p).sqrMagnitude < 25f) crowdN++;
            }
            if (TV(3, 2) && (reflectT += dt) >= 5f)
            {   // talent: stored damage returns as a shockwave
                reflectT = 0;
                if (reflect > 0)
                {
                    float rh = ZoneCells.RH(this) * (2f + reflect / Mathf.Max(1f, player.maxHp) * 20f); reflect = 0;
                    foreach (var o in swarm.enemies) { var d = o.p - player.p; if (o.dead || d.sqrMagnitude > 16f) continue; o.hp -= rh; o.flash = 1; if (!o.boss) o.k += d.normalized * 6f; if (o.hp <= 0) KillEnemy(o); }
                    Feel.Shake(0.12f, 0.2f); if (!SimRun) Fx.I.Smoke(MazeMap.ToWorld(player.p, 0.1f), 12, new Color(0.9f, 0.7f, 0.4f), 4, 0.5f, 0.4f, 0.5f);
                }
            }
        }
        // talents on taking damage (before it lands); returns the damage
        float TalentHurt(float dmg) { dmg *= OnboardDmgMul; if (ZoneMods.On[ZoneMods.Glass]) dmg *= 1.5f; if (TV(1, 1)) dmg *= 0.9f; if (TV(3, 2)) reflect += dmg * 0.3f; return dmg; }
        void TalentFire() { fireStreak = runT - lastFireT < 0.4f ? fireStreak + (runT - lastFireT) : 0; lastFireT = runT; }
        float relicT; int relayHits, helixN; public bool sealUsed, tagsUsed;
        // the run hero's talents, cached at run start (Meta.Talents rows)
        public static readonly int[] RunTal = new int[10]; public static string RunTalHero = "";
        public static bool TS(int r, int c) => RunTalHero == "sentinel" && RunTal[r] == c;
        public static bool TV(int r, int c) => RunTalHero == "vanguard" && RunTal[r] == c;
        public static void CacheTalents() { RunTalHero = Meta.HeroId; for (int r = 0; r < 10; r++) RunTal[r] = Meta.Talent(RunTalHero, r); }
        Enemy huntElite, lanceTarget; float lanceSince; float volT, acidT, stormT = 5f; bool talentRare, lowHealUsed, vanRevive, evolvedAny; int crowdN, levelUpsSeen; float crowdT, reflect, reflectT, fireStreak, lastFireT;
        void RelicTick(float dt)
        {
            float rh = ZoneCells.RH(this);
            if (Meta.Relic(9) && floorT >= 2f && floorT - dt < 2f)
            {   // SEISMIC CHARGE: the floor's opening crowd is cleared
                float r2 = Meta.RelicV * Meta.RelicV;
                foreach (var o in swarm.enemies) if (!o.dead && !o.boss && !o.type.nest && !o.egg && (o.p - player.p).sqrMagnitude < r2) { o.hp -= 20 * rh; if (o.hp <= 0) KillEnemy(o); }
                Feel.Shake(0.15f, 0.3f); if (!SimRun) Fx.I.Debris(MazeMap.ToWorld(player.p, 0.2f), 20, new Color(0.7f, 0.6f, 0.45f), 6, 0.6f, 0.15f);
            }
            if (Meta.Relic(17)) BroodTick(dt);
            if (!(Meta.Relic(3) || Meta.Relic(5))) return;
            if ((relicT += dt) < Meta.RelicV) return;
            relicT = 0;
            foreach (var o in swarm.enemies)
            {
                if (o.dead) continue; var d = o.p - player.p; float m = d.magnitude;
                if (Meta.Relic(3) && m < 3f) o.burnT = Mathf.Max(o.burnT, 3f);                                    // SPORE LANTERN (poison as burn)
                if (Meta.Relic(5) && m < 4f && !o.boss) o.k += d.normalized * 8f;                                   // TIDE BELL
            }
            if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(player.p, 0.5f), 14, Meta.Relic(3) ? new Color(0.5f, 1f, 0.3f) : new Color(0.4f, 0.8f, 1f), 5, 0.4f, 0.1f, 3);
        }
        // BROOD CROWN: mini-Drone allies orbit the hero and zap the nearest enemy within 7 m
        readonly List<Transform> brood = new(); float broodT;
        void BroodTick(float dt)
        {
            int n = Mathf.RoundToInt(Meta.RelicV);
            if (!SimRun)
            {
                while (brood.Count < n)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(go.GetComponent<Collider>()); go.name = "BroodDrone";
                    go.transform.SetParent(transform, false); go.transform.localScale = Vector3.one * 0.35f;
                    go.GetComponent<Renderer>().sharedMaterial = Mats.Unlit(new Color(0.7f, 0.35f, 1f)); brood.Add(go.transform);
                }
                for (int i = 0; i < brood.Count; i++)
                {
                    brood[i].gameObject.SetActive(i < n); float a = runT * 2.2f + i * Mathf.PI * 2f / n;
                    brood[i].position = MazeMap.ToWorld(player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.4f, 1.6f);
                }
            }
            if ((broodT -= dt) > 0) return;
            broodT = 0.6f; float rh = ZoneCells.RH(this);
            for (int i = 0; i < n; i++)
            {
                Enemy best = null; float bd = 49f;
                foreach (var e in swarm.enemies) { if (e.dead || e.Hidden || e.spawnT < 0.5f) continue; float d = (e.p - player.p).sqrMagnitude; if (d < bd && (i == 0 || Random.value < 0.6f)) { bd = d; best = e; } }
                if (best == null) break;
                best.hp -= 1.2f * rh; best.flash = 1;
                if (!SimRun) Fx.I.Sparks(MazeMap.ToWorld(best.p, 0.9f), 4, new Color(0.75f, 0.4f, 1f), 3, 0.2f, 0.06f, 2);
                if (best.hp <= 0) KillEnemy(best);
            }
        }
        public void HideBrood() { foreach (var t in brood) if (t != null) t.gameObject.SetActive(false); }
        bool HelixTick() => Meta.Relic(8) && ++helixN % Mathf.RoundToInt(Meta.RelicV) == 0;
        public void CardOnDash()
        {
            sinceDash = 0;
            if (Meta.Leg(3)) foreach (var e in swarm.enemies) { var d = e.p - player.p; if (!e.dead && !e.boss && d.magnitude < 2.5f) e.k += d.normalized * 7f; }   // boots: dash shoves
            if (TS(3, 1)) foreach (var e in swarm.enemies) if (!e.dead && (e.p - player.p).sqrMagnitude < 9f) e.slowT = Mathf.Max(e.slowT, 2f);   // talent: frost trail
            if (TS(3, 2)) lastBreathT = Mathf.Max(lastBreathT, 0.5f);                                                                         // talent: dash invulnerable
            if (TV(1, 2)) foreach (var e in swarm.enemies) { var d = e.p - player.p; if (!e.dead && !e.boss && d.magnitude < 2.5f) e.k += d.normalized * 8f; }   // talent: dash shoves
            if (Meta.Relic(0)) foreach (var e in swarm.enemies) if (!e.dead && !e.boss && (e.p - player.p).sqrMagnitude < 6.25f) e.frozen = Mathf.Max(e.frozen, Meta.RelicV);   // CRYO HEART
            if (Meta.Relic(6))
            {   // CRANE HOOK: the nearest elite within 8 m is pulled 3 m and stunned
                Enemy n = null; float bd = 64f;
                foreach (var e in swarm.enemies) { if (e.dead || !e.elite) continue; float d = (e.p - player.p).sqrMagnitude; if (d < bd) { bd = d; n = e; } }
                if (n != null) { var dir = (player.p - n.p).normalized; float pull = Mathf.Min(3f, Mathf.Sqrt(bd) - 1f); if (pull > 0) n.p += dir * pull; n.frozen = Mathf.Max(n.frozen, Meta.RelicV); }
            }
            if (mods["adrenaline_dash"] > 0) adrenalT = 2.5f;
            if (mods["afterimage"] > 0) PowerUps.DecoyFor(player.p, 1.5f);
        }
    }

    static class V2Ext { public static float sqrSqr(this Vector2 v) => v.sqrMagnitude; }
}
