using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The spec's new objectives (drop-zones-core-v2.json "objectives"), on the layout's markers:
    //   RELAY  - terminals R in reading order, 3 s each (a hit knocks 15% off); the last ends the floor
    //   SALVAGE- walk over 5 data cores $ (each triggers a 4-6 Drone ambush), then hold the lift L 3 s
    //   DETONATE - plant every charge ! (3 s each), then 25 s to reach the lift; the blast kills you if you're not on it
    //   UPLINK - a 3 m ring that hops between the Z spots every 10 s; 30 s inside in total
    //   ESCORT - the cart on Q follows the ':' track at 1.5 m/s while you're within 4 m and nothing touches it;
    //            400 HP, mended 5/s while rolling; lose if it dies
    //   BURN   - scorch 80% of the creep (dash through it, explosions); nests keep growing it back
    //   ASSASSINATE - a marked elite (4x HP) flees to the far side and calls escorts every 10 s; kill it to end the floor
    // OUTRUN plays as ESCAPE (to the lift) with the floor script's FRONT behind you.
    public partial class Game
    {
        readonly List<Vector2> objPts = new(); int objIdx, objNeed; float objT, objHold, objCart, objCartMax, objCountdown = -1;
        readonly List<Vector2> track = new(); float trackPos; Transform cart; Enemy marked;
        int creepStart, creepLeft; float burnCountT; public static int Scorched;

        Vector2 Cell(Vector2Int c) => MazeMap.Center(c.x, c.y);
        List<Vector2> MarksAt(char ch) { var l = new List<Vector2>(); foreach (var c in MazeMap.Marks(ch)) l.Add(Cell(c)); return l; }
        Vector2 LiftCenter()
        {   // the lift cell nearest the lift's middle (the average alone can fall off an L-shaped or split lift)
            if (MazeMap.exitCells.Count == 0) return MazeMap.playerSpawn;
            Vector2 s = Vector2.zero; foreach (var c in MazeMap.exitCells) s += c; s /= MazeMap.exitCells.Count;
            Vector2 b = MazeMap.exitCells[0]; float bd = 1e9f; foreach (var c in MazeMap.exitCells) { float d = (c - s).sqrMagnitude; if (d < bd) { bd = d; b = c; } }
            return b;
        }

        void StartObjective()
        {
            objPts.Clear(); objIdx = 0; objT = 0; objHold = 0; objCountdown = -1; marked = null;
            switch (goal)
            {
                case Goal.Relay: objPts.AddRange(MarksAt('R')); if (objPts.Count == 0) objPts.Add(MazeMap.playerSpawn); objNeed = objPts.Count; goalAt = objPts[0]; break;
                case Goal.Salvage: objPts.AddRange(MarksAt('$')); objNeed = Mathf.Min(5, objPts.Count); goalAt = NearestObj(); liftAt = LiftCenter(); break;
                case Goal.Detonate: objPts.AddRange(MarksAt('!')); objNeed = objPts.Count; goalAt = NearestObj(); liftAt = LiftCenter(); break;
                case Goal.Uplink:
                    foreach (var z in MazeMap.zoneSpots) objPts.Add(z); if (objPts.Count == 0) objPts.Add(MazeMap.playerSpawn);
                    goalNeed = ParamSeconds(30f); goalAt = objPts[0]; objT = 10f; break;
                case Goal.Escort: BuildTrack(); objCartMax = objCart = ZoneCells.Hp(this, 400); goalAt = track.Count > 0 ? track[0] : MazeMap.playerSpawn; break;
                case Goal.Burn:
                    swarm.NestEvery = 3f; swarm.NestCap = 18 + 2 * floor;
                    foreach (var at in MazeMap.nestSpots) { var nn = swarm.Spawn(at, wave, swarm.NestType); nn.p = at; nn.hp = nn.maxHp = ZoneCells.RH(this) * 30f; nests.Add(nn); }
                    creepStart = Mathf.Max(1, MazeMap.Marks('c').Count); Scorched = 0; goalAt = MazeMap.nestSpots.Count > 0 ? MazeMap.nestSpots[0] : MazeMap.playerSpawn; break;
                case Goal.Assassinate:
                {
                    // the target: a named mini-boss of the floor, else an elite of its featured type (4x HP, gold)
                    string mb = null; if (zoneFloor != null) foreach (var en in zoneFloor.enemies) if (EnemyRegistry.IsBoss(en.type)) mb = en.type;
                    var at = FarFrom(player.p);
                    if (mb != null) marked = SpawnMiniBoss(mb, at);
                    if (marked == null)
                    {
                        string ft = "stalker"; if (zoneFloor != null) foreach (var en in zoneFloor.enemies) if (en.type != "Drone" && en.type != "Tick") { ft = en.type; break; }
                        marked = swarm.Spawn(at, wave, swarm.TypeByName(ft)); marked.p = at; MakeElite(marked); marked.hp *= 4f; marked.maxHp = marked.hp;
                        var nm = zoneFloor != null ? System.Text.RegularExpressions.Regex.Match(zoneFloor.param ?? "", "'([^']+)'").Groups[1].Value : "";
                        hud.ShowBanner((nm.Length > 0 ? nm : "TARGET").ToUpperInvariant() + " MARKED");
                    }
                    objT = 10f; goalAt = marked.p; break;
                }
            }
            spawnT = 1f;
            MazeMap.FlowTo(goalAt, goalField);
        }

        Vector2 NearestObj() { Vector2 b = MazeMap.playerSpawn; float bd = 1e9f; foreach (var p in objPts) { float d = (p - player.p).sqrMagnitude; if (d < bd) { bd = d; b = p; } } return b; }
        Vector2 FarFrom(Vector2 from) { Vector2 b = from; float bd = -1; foreach (var s in MazeMap.spawnPoints) { float d = (s - from).sqrMagnitude; if (d > bd) { bd = d; b = s; } } return b; }

        // hold-to-interact: within 1.5 m fills, away pauses, a hit knocks 15% off (HurtPlayer calls OnHeroHit)
        bool Interact(Vector2 at, float need, float dt)
        {
            bool near = (player.p - at).magnitude < 1.5f + cfg.player.radius + 0.6f;
            if (near) objHold += dt;
            if (objHold >= need) { objHold = 0; Haptics.Light(); Sfx.Click(); return true; }
            return false;
        }
        public void OnHeroHit() { if (run && objHold > 0) objHold = Mathf.Max(0, objHold - 0.45f); }

        void UpdateObjective(float dt, int alive)
        {
            switch (goal)
            {
                case Goal.Relay:
                    if (Interact(goalAt, 3f, dt))
                    {
                        objIdx++; events.Done("relay"); events.Done("terminal"); hud.ShowBanner($"RELAY {objIdx}/{objNeed} ONLINE");
                        if (objIdx >= objNeed) { GoalDone("ALL RELAYS ONLINE"); return; }
                        goalAt = objPts[objIdx]; MazeMap.FlowTo(goalAt, goalField); ShowGoalMarker();
                    }
                    Trickle(dt, alive, 16, 0.55f); break;
                case Goal.Salvage:
                    if (objIdx < objNeed)
                    {
                        for (int i = objPts.Count - 1; i >= 0; i--)
                            if ((objPts[i] - player.p).magnitude < 1.2f)
                            {
                                objPts.RemoveAt(i); objIdx++; events.Done("pickup"); hud.ShowBanner($"CORE {objIdx}/{objNeed}"); Haptics.Light();
                                var s = NearestSpawn(player.p); int n = Random.Range(4, 7); int dt0 = swarm.TypeByName("drone");
                                for (int k = 0; k < n; k++) { var e = swarm.Spawn(s + Random.insideUnitCircle, wave, dt0); e.p = s + Random.insideUnitCircle; }
                                if (objIdx >= objNeed) { goalAt = liftAt; hud.ShowBanner("CORES SECURED  //  TO THE LIFT"); } else goalAt = NearestObj();
                                MazeMap.FlowTo(goalAt, goalField); ShowGoalMarker(); break;
                            }
                    }
                    else if (OnLift(dt)) { GoalDone("EXTRACTED"); return; }
                    Trickle(dt, alive, 14, 0.7f); break;
                case Goal.Detonate:
                    if (objIdx < objNeed)
                    {
                        if (Interact(goalAt, 3f, dt))
                        {
                            objPts.Remove(goalAt); objIdx++; events.Done("charge"); hud.ShowBanner($"CHARGE {objIdx}/{objNeed} SET");
                            if (objIdx >= objNeed) { objCountdown = 25f; goalAt = liftAt; hud.ShowBanner("25 S  //  GET TO THE LIFT"); } else goalAt = NearestObj();
                            MazeMap.FlowTo(goalAt, goalField); ShowGoalMarker();
                        }
                    }
                    else
                    {
                        objCountdown -= dt;
                        if (OnLift(dt)) { GoalDone("DETONATED"); return; }
                        if (objCountdown <= 0) { hud.ShowBanner("CAUGHT IN THE BLAST"); player.hp = 0; GameOver(); return; }
                    }
                    Trickle(dt, alive, objCountdown > 0 ? 20 : 16, objCountdown > 0 ? 0.4f : 0.6f); break;
                case Goal.Uplink:
                {
                    bool inside = (player.p - goalAt).magnitude < 3f;
                    if (inside) goalT += dt;
                    objT -= dt;
                    if (objT <= 2f && objT + dt > 2f && objPts.Count > 1) hud.ShowBanner("UPLINK MOVING");
                    if (objT <= 0 && objPts.Count > 1) { objT = 10f; objIdx = (objIdx + 1) % objPts.Count; goalAt = objPts[objIdx]; MazeMap.FlowTo(goalAt, goalField); ShowGoalMarker(); }
                    if (goalT >= goalNeed) { GoalDone("UPLINK COMPLETE"); return; }
                    Trickle(dt, alive, 18, inside ? 0.42f : 0.6f); break;
                }
                case Goal.Escort: TickCart(dt); if (objCart <= 0) { hud.ShowBanner("CART DESTROYED"); GameOver(); return; } if (trackPos >= TrackLen) { GoalDone("DELIVERED"); return; } Trickle(dt, alive, 16, 0.55f); break;
                case Goal.Burn:
                {
                    // dashing scorches the creep under (and next to) the hero
                    if (player.dashT > 0) ScorchAt(player.p, 1);
                    if ((burnCountT -= dt) <= 0) { burnCountT = 0.5f; creepLeft = 0; for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.tag[z, x] == 'c' && MazeMap.flow[z * MazeMap.W + x] >= 0) creepLeft++; }   // only creep the hero can reach
                    float frac = Scorched / (float)Mathf.Max(1, Scorched + creepLeft);   // of all the creep there is (it grows)
                    goalCount = Mathf.FloorToInt(100 * frac);
                    if (frac >= 0.8f) { GoalDone("CREEP BURNED"); return; }
                    var n = NearestNest(); goalAt = n != null ? n.p : goalAt;
                    Trickle(dt, alive, 14, 0.7f); break;
                }
                case Goal.Assassinate:
                    if (marked == null || marked.dead) { GoalDone("TARGET DOWN"); return; }
                    objT -= dt; goalAt = marked.p;
                    if ((goalFieldT -= dt) <= 0) { goalFieldT = 2f; var far = FarFrom(player.p); MazeMap.FlowTo(far, goalField); marked.field = goalField; marked.fieldAt = far; }
                    if (objT <= 0) { objT = 10f; int ft = marked.typeIndex; for (int k = 0; k < 2; k++) { var e = swarm.Spawn(marked.p + Random.insideUnitCircle * 2f, wave, ft); e.p = marked.p + Random.insideUnitCircle * 2f; } }
                    Trickle(dt, alive, 12, 0.8f); break;
            }
        }

        bool OnLift(float dt)
        {
            if (!MazeMap.NearExit(player.p)) { goalT = 0; return false; }
            goalT += dt; int n = Mathf.CeilToInt(3f - goalT);
            if (n != goalCount && n > 0) { goalCount = n; hud.ShowBanner("EXTRACTING " + n); Sfx.Click(); }
            return goalT >= 3f;
        }
        void Trickle(float dt, int alive, int cap, float every) { if ((spawnT -= dt) <= 0 && alive < cap) { SpawnOne(); spawnT = every; } }
        Vector2 NearestSpawn(Vector2 at) { Vector2 b = at; float bd = 1e9f; foreach (var s in MazeMap.spawnPoints) { float d = (s - at).sqrMagnitude; if (d < bd && d > 16) { bd = d; b = s; } } return b; }

        // ---- ESCORT: the track is the ':' cells walked from Q
        float TrackLen => track.Count > 1 ? (track.Count - 1) * MazeMap.C : 0;
        void BuildTrack()
        {
            track.Clear(); trackPos = 0;
            var q = MazeMap.Marks('Q'); if (q.Count == 0) return;
            var cur = q[0]; var seen = new HashSet<Vector2Int> { cur }; track.Add(Cell(cur));
            for (int guard = 0; guard < 400; guard++)
            {
                bool moved = false;
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var n = cur + d; if (n.x < 0 || n.y < 0 || n.x >= MazeMap.W || n.y >= MazeMap.H || seen.Contains(n) || MazeMap.tag[n.y, n.x] != ':') continue;
                    seen.Add(n); track.Add(Cell(n)); cur = n; moved = true; break;
                }
                if (!moved) break;
            }
            if (!SimRun)
            {
                if (cart != null) Destroy(cart.gameObject);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(go.GetComponent<Collider>());
                go.name = "Cart"; go.transform.localScale = new Vector3(1.6f, 1.2f, 2.2f);
                go.GetComponent<MeshRenderer>().sharedMaterial = Mats.Lit(new Color(0.12f, 0.48f, 0.48f), null, null, 0.3f); cart = go.transform;
            }
        }
        Vector2 TrackAt(float s)
        {
            if (track.Count == 0) return MazeMap.playerSpawn;
            float f = s / MazeMap.C; int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, track.Count - 1), j = Mathf.Min(i + 1, track.Count - 1);
            return Vector2.Lerp(track[i], track[j], f - i);
        }
        void TickCart(float dt)
        {
            var p = TrackAt(trackPos); bool touched = false;
            foreach (var e in swarm.enemies) if (!e.dead && (e.p - p).sqrMagnitude < (e.r + 1.1f) * (e.r + 1.1f)) { touched = true; objCart -= e.damage * dt / Mathf.Max(0.1f, cfg.enemy.attackCooldown); }
            bool moving = (player.p - p).magnitude < 4f && trackPos < TrackLen;
            if (moving) { trackPos += (touched ? 0.5f : 1.5f) * dt; /* touched: a third of the speed (closest version) */ objCart = Mathf.Min(objCartMax, objCart + ZoneCells.Hp(this, 5) * dt); }
            goalAt = TrackAt(trackPos);
            if (cart != null)
            {
                var nxt = TrackAt(trackPos + 0.5f) - goalAt;
                cart.position = MazeMap.ToWorld(goalAt, 0.6f);
                if (nxt.sqrMagnitude > 1e-4f) cart.rotation = Quaternion.Euler(0, MazeMap.YawDeg(Mathf.Atan2(nxt.x, nxt.y)), 0);
            }
            if (goalRing != null && goalRing.gameObject.activeSelf) goalRing.position = MazeMap.ToWorld(goalAt, 0.07f);
        }
        void EndObjective() { if (cart != null) { Destroy(cart.gameObject); cart = null; } marked = null; StopFront(); }

        // ---- BURN / SPREAD: scorch creep cells within r cells of a point (dash, explosions)
        public void ScorchAt(Vector2 at, int r)
        {
            if (!MazeMap.V2) return;
            int cx = Mathf.FloorToInt(at.x / MazeMap.C), cz = Mathf.FloorToInt(at.y / MazeMap.C);
            for (int z = cz - r; z <= cz + r; z++) for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || z < 0 || x >= MazeMap.W || z >= MazeMap.H || MazeMap.tag[z, x] != 'c') continue;
                MazeMap.tag[z, x] = 's'; Scorched++; CellSpread.Dirty = true;
                if (!SimRun && Random.value < 0.4f) Fx.I.Sparks(MazeMap.ToWorld(MazeMap.Center(x, z), 0.2f), 3, new Color(1f, 0.6f, 0.2f), 2, 0.3f, 0.06f, 1);
            }
        }

        public string ObjLine() => goal switch
        {
            Goal.Relay => $"RELAY  //  TERMINAL {Mathf.Min(objIdx + 1, objNeed)}/{objNeed}  //  {Mathf.FloorToInt(100 * objHold / 3f)}%",
            Goal.Salvage => objIdx < objNeed ? $"SALVAGE  //  CORES {objIdx}/{objNeed}" : "SALVAGE  //  HOLD THE LIFT",
            Goal.Detonate => objIdx < objNeed ? $"DETONATE  //  CHARGES {objIdx}/{objNeed}  //  {Mathf.FloorToInt(100 * objHold / 3f)}%" : $"DETONATE  //  LIFT  //  {Mathf.Max(0, objCountdown):F0} S",
            Goal.Uplink => $"UPLINK  //  {Mathf.FloorToInt(100 * goalT / Mathf.Max(1, goalNeed))}%  //  MOVES IN {Mathf.Max(0, objT):F0}",
            Goal.Escort => $"ESCORT  //  {Mathf.FloorToInt(100 * trackPos / Mathf.Max(1, TrackLen))}%  //  CART {Mathf.CeilToInt(100 * objCart / Mathf.Max(1, objCartMax))}%",
            Goal.Burn => $"BURN THE CREEP  //  {goalCount}% / 80%",
            Goal.Assassinate => $"ASSASSINATE  //  TARGET {(marked != null ? Mathf.CeilToInt(100 * marked.hp / Mathf.Max(1, marked.maxHp)) : 0)}%",
            _ => "",
        };
        public float ObjProgress() => goal switch
        {
            Goal.Relay => (objIdx + objHold / 3f) / Mathf.Max(1, objNeed),
            Goal.Salvage => objIdx / (float)Mathf.Max(1, objNeed),
            Goal.Detonate => objIdx / (float)Mathf.Max(1, objNeed),
            Goal.Uplink => goalT / Mathf.Max(1, goalNeed),
            Goal.Escort => trackPos / Mathf.Max(1, TrackLen),
            Goal.Burn => goalCount / 80f,
            Goal.Assassinate => marked != null ? 1 - marked.hp / Mathf.Max(1, marked.maxHp) : 1,
            _ => 0,
        };
    }
}

namespace Maze
{
    // FRONT: a wall of doom sweeping one way across the floor (flood / fire / creep / collapse skins). 5 s countdown,
    // then 1.0-1.6 m/s; everything behind it costs the hero 25 HP/s. Terminals (R) it hasn't swept pause it 4 s once.
    public partial class Game
    {
        bool frontOn; float frontS, frontSpeed, frontWait, frontStopAt = 1e9f; Vector2 frontDir; Transform frontQuad; Material frontMat; float frontTick;
        readonly HashSet<Vector2Int> usedTerminals = new();

        public void StartFront(string dir, float speed, float countdown, string skin, float stopAt = 1e9f)
        {
            frontDir = dir == "N" ? Vector2.down : dir == "S" ? Vector2.up : dir == "W" ? Vector2.left : Vector2.right;   // plane: +y is down the map
            frontSpeed = speed; frontWait = countdown; frontOn = true; frontStopAt = stopAt; usedTerminals.Clear();
            // start just behind the far edge it comes from
            frontS = 0;
            if (!SimRun)
            {
                if (frontQuad == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(go.GetComponent<Collider>()); go.name = "Front";
                    frontQuad = go.transform; frontMat = Mats.Fade(Color.white); go.GetComponent<MeshRenderer>().sharedMaterial = frontMat;
                }
                frontQuad.gameObject.SetActive(true);
                frontMat.SetColor("_BaseColor", skin == "fire" ? new Color(1f, 0.4f, 0.1f, 0.55f) : skin == "creep" ? new Color(0.35f, 0.1f, 0.45f, 0.7f) : skin == "collapse" ? new Color(0.05f, 0.05f, 0.06f, 0.9f) : new Color(0.16f, 0.36f, 0.48f, 0.65f));
            }
            hud.ShowBanner("IT'S COMING  //  " + Mathf.CeilToInt(countdown));
        }
        public void PauseFront(float s) { if (frontOn) frontWait = Mathf.Max(frontWait, s); }
        void StopFront() { frontOn = false; if (frontQuad != null) frontQuad.gameObject.SetActive(false); }

        // how far along its axis a point is (0 = the starting edge)
        float FrontAxis(Vector2 p)
        {
            float w = MazeMap.W * MazeMap.C, h = MazeMap.H * MazeMap.C;
            return frontDir.x > 0 ? p.x : frontDir.x < 0 ? w - p.x : frontDir.y > 0 ? p.y : h - p.y;
        }

        void TickFront(float dt)
        {
            if (!frontOn) return;
            if (frontWait > 0) frontWait -= dt; else frontS = Mathf.Min(frontStopAt, frontS + frontSpeed * dt);
            // terminals on its path: completing one pauses it 4 s (each once)
            foreach (var c in MazeMap.Marks('R'))
            {
                if (usedTerminals.Contains(c)) continue; var p = MazeMap.Center(c.x, c.y);
                if (FrontAxis(p) < frontS) { usedTerminals.Add(c); continue; }   // swept: lost
                if ((player.p - p).magnitude < 2f) { objHold += dt; if (objHold >= 3f) { objHold = 0; usedTerminals.Add(c); PauseFront(4f); events.Done("terminal"); hud.ShowBanner("BULKHEAD SEALED  //  +4 S"); } }
            }
            bool behind = FrontAxis(player.p) < frontS;
            if (behind && (frontTick -= dt) <= 0) { frontTick = 0.25f; HurtPlayer(ZoneCells.Hp(this, 25) * 0.25f); }
            if (frontQuad != null)
            {
                float w = MazeMap.W * MazeMap.C, h = MazeMap.H * MazeMap.C; float s = Mathf.Max(0.01f, frontS);
                Vector2 c; Vector2 size;
                if (frontDir.x != 0) { size = new Vector2(s, h); c = new Vector2(frontDir.x > 0 ? s * 0.5f : w - s * 0.5f, h * 0.5f); }
                else { size = new Vector2(w, s); c = new Vector2(w * 0.5f, frontDir.y > 0 ? s * 0.5f : h - s * 0.5f); }
                frontQuad.position = MazeMap.ToWorld(c, 0.35f); frontQuad.rotation = Quaternion.Euler(90, 0, 0); frontQuad.localScale = new Vector3(size.x, size.y, 1);
            }
        }
    }
}
