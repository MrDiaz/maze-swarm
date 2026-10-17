using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // First-deploy polish captures (2026-10-15), muted, on a throwaway save: the hangar and its sheets (heroes, gear,
    // loadout), then the hero on Cryo Station floor 1 standing, firing (minigun) and close up, with the Meshy rig and the
    // Blender rig. Shots go to Design/Captures/polish-2026-10-15/<POLISH_TAG>_*.png (UiShot's capture: UI included).
    //   POLISH_TAG=before UISHOT_SIZE=1600x738 Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.PolishShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class PolishShot
    {
        const string Key = "Maze.PolishShot.Active";
        const string Out = "Design/Captures/polish-2026-10-15";
        static string Tag => Environment.GetEnvironmentVariable("POLISH_TAG") ?? "shot";
        static int step; static bool prepared, rigBefore; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static PolishShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void Shot(string n) { UiShot.Shot(Tag + "_" + n, new[] { Out }); Debug.Log("PolishShot: " + Tag + "_" + n); }
        static void Hold() { var g = G; var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; typeof(Game).GetField("toSpawn", f).SetValue(g, 0); typeof(Game).GetField("breakT", f).SetValue(g, 99f); }
        static void StartCryo()
        {
            CryoSoftEnvironment.Enabled = true; var def = SectorDefs.Get("cryo-station"); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false; g.holdFire = true; g.swarm.Clear();
        }
        static void Aim() { var p = G.player; p.aim = new Vector2(1, 0); p.facing = Mathf.PI / 2; }
        static List<Action> Build()
        {
            if (Environment.GetEnvironmentVariable("POLISH_CHIBI") == "1")
            {   // the Vanguard before / after the chibi pass: a plain-background turnaround (front, 3/4, side), a run-camera shot in
                // Cryo room 1 while firing, and editor fps with 20 enemies, chibi off then on
                const string OutC = "Design/Captures/hero-chibi-2026-10-16";
                var c = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; HeroChibi.On = false; rigBefore = Prefs.VanguardMeshy; Prefs.VanguardMeshy = true; UiShot.Prepare(); StartCryo(); G.player.RebuildHero(G.cfg); Later(7); } };
                bool modelBefore = HeroChibi.ModelOn;
                foreach (var mode in new[] { 0, 1, 2 })   // the current hero, the flat-colour pass, the Blender rebuild
                {
                    bool on = mode == 1; string t = mode == 0 ? "before" : mode == 1 ? "after" : "rebuild";
                    c.Add(() => { HeroChibi.On = on; if (mode == 2) { HeroChibi.ModelOn = true; G.player.RebuildHero(G.cfg); } Hold(); G.swarm.Clear(); Aim(); Later(mode == 0 ? 8 : 1.2); });   // (the first waits out the floor banner)
                    c.Add(() => { Turn(OutC, mode == 2 ? "rebuild_game" : t); var g = G; g.holdFire = false; for (int i = 0; i < 5; i++) { var e = g.DebugSpawn(0, g.player.p + new Vector2(6 + i * .6f, (i - 2) * .8f)); e.hp = e.maxHp = 1e6f; } Later(1.2); });
                    c.Add(() => { Aim(); UiShot.Shot(t + "_run", new[] { OutC }); Debug.Log("PolishShot: chibi " + t + "_run");
                        var g = G; g.swarm.Clear(); for (int i = 0; i < 20; i++) { var e = g.DebugSpawn(i % 3, g.player.p + new Vector2(7 + (i % 5) * .7f, (i / 5 - 1.5f) * 1.4f)); e.hp = e.maxHp = 1e6f; }
                        int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 4) return false;
                            Debug.Log($"PolishShot: chibi {t} editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0} with {G.swarm.enemies.Count} enemies"); QualitySettings.vSyncCount = 1; G.holdFire = true; G.swarm.Clear(); return true; }; });
                }
                c.Add(() => { HeroChibi.On = false; HeroChibi.ModelOn = modelBefore; Prefs.VanguardMeshy = rigBefore; G.player.RebuildHero(G.cfg); Later(.5); });
                return c;
            }
            if (Environment.GetEnvironmentVariable("POLISH_HANGAR") == "1")
            {   // the title screen and the hangar at phone resolution (two frames 1.5 s apart to show what moves), on a throwaway save
                string OutH = "Design/Captures/hangar-2026-10-16";
                void SH(string n) { UiShot.Shot(Tag + "_" + n, new[] { OutH }); Debug.Log("PolishShot: " + Tag + "_" + n); }
                return new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; HangarHero3D.On = Environment.GetEnvironmentVariable("HANGAR3D") == "1"; G.menu.Show(); Later(1.5); },
                    () => { UiShot.Prepare(); Later(1.5); },
                    () => { SH("title"); var tv = Object.FindAnyObjectByType<TitleScreenView>(); Debug.Log("PolishShot: title view " + (tv != null)); if (tv != null) tv.Play(); Later(2.5); },
                    () => { if (Environment.GetEnvironmentVariable("HANGAR_CLIP") == "1") { int i = 0; double t0 = EditorApplication.timeSinceStartup; wait = () => { if (EditorApplication.timeSinceStartup - t0 < i * .1) return false; UiShot.Shot($"clip_{i:000}", new[] { "Logs/HangarClip_" + Tag }); return ++i >= 80; }; } else Later(.05); },
                    () => { SH("hangar_a"); Later(1.5); },
                    () => { SH("hangar_b"); int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 3) return false;
                            int rend = 0; foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && r.gameObject.activeInHierarchy) rend++;
                            var names = new List<string>(); foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.parent == null && t.gameObject.activeInHierarchy) names.Add(t.name);
                            Debug.Log($"PolishShot: {Tag} hangar editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0}, renderers {rend}, roots: {string.Join(", ", names)}"); QualitySettings.vSyncCount = 1; return true; }; },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_VFX3") == "1")
            {   // VFX batch 3: one zone per effect at the game camera (6 s in, hazards cycling, no enemies), plus forced lightning / rain / wind
                string Out3 = "Design/Captures/vfx-batch3-2026-10-16";
                void S3(string n) { UiShot.Shot(Tag + "_" + n, new[] { Out3 }); Debug.Log("PolishShot: " + Tag + "_" + n); }
                bool kit3 = typeof(VfxKit).GetMethod("Strike") != null;
                var zones = new[] { ("meltdown", 1, "radiation_vents"), ("hive-core", 2, "creep"), ("drowned-city", 1, "water_grates"), ("kessler-refinery", 2, "steam_vents"), ("quarantine-ward", 1, "gas_vents"), ("acid-marsh", 1, "rain_zone"), ("relay-spire", 1, "wind_zone") };
                var v = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5, sectorsCleared = 30 }); Prefs.TestMode = false; UiShot.Prepare(); Later(1); } };
                foreach (var (id, fl, tag) in zones)
                {
                    var zid = id; int f = fl; var t = tag;
                    v.Add(() => { var def = SectorDefs.Get(zid); if (def == null) { Debug.Log("PolishShot: no zone " + zid); Later(.1); return; } var g = G; Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null; g.LoadMap(0); g.menu.Hide(); g.Restart(); if (f > 1) g.DebugFloor(f); g.godMode = true; g.holdFire = true; Later(2); });
                    v.Add(() => { Hold(); G.swarm.Clear(); Later(6); });
                    v.Add(() => { S3(t); Later(0.2); });
                    if (t == "rain_zone" || t == "wind_zone" || t == "water_grates")
                        v.Add(() => {   // forced weather / lightning for 1.2 s, then the shot
                            if (!kit3) { Later(.1); return; }
                            var g = G; var mi = typeof(VfxKit); double t0 = EditorApplication.timeSinceStartup; bool struck = false;
                            wait = () => {
                                var at = MazeMap.ToWorld(g.player.p, 0); float dt = Time.deltaTime;
                                if (t == "rain_zone") mi.GetMethod("Rain").Invoke(VfxKit.I, new object[] { at, dt, 1f });
                                if (t == "wind_zone") mi.GetMethod("Wind").Invoke(VfxKit.I, new object[] { at, new Vector3(1, 0, .3f).normalized, dt });
                                if (t == "water_grates" && !struck && EditorApplication.timeSinceStartup - t0 > .9) { mi.GetMethod("Strike").Invoke(VfxKit.I, new object[] { at + new Vector3(4, 0, 2) }); struck = true; }
                                if (EditorApplication.timeSinceStartup - t0 < 1.0) return false;
                                S3(t == "water_grates" ? "lightning" : t + "_forced"); return true; };
                        });
                    v.Add(() => { G.ToMenu(); Later(1); });
                }
                // fps: a vent bank erupting (meltdown) with rain at once
                v.Add(() => { var def = SectorDefs.Get("meltdown"); var g = G; Game.PendingTier = def.tier; Game.PendingSector = def; g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; Later(3); });
                v.Add(() => { Hold(); G.swarm.Clear(); int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    wait = () => { if (kit3) typeof(VfxKit).GetMethod("Rain").Invoke(VfxKit.I, new object[] { MazeMap.ToWorld(G.player.p, 0), Time.deltaTime, 1f });
                        if (EditorApplication.timeSinceStartup - t0 < 4) return false; Debug.Log($"PolishShot: {Tag} meltdown + rain: editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0}"); QualitySettings.vSyncCount = 1; return true; }; });
                v.Add(() => { G.ToMenu(); Later(1); });
                return v;
            }
            if (Environment.GetEnvironmentVariable("POLISH_PLAYGROUND") == "1")
            {   // the playground: picker open, a few types, a boss, every enemy / boss once, 30 enemies for fps, and the save untouched
                string save0 = null; bool tm0 = false; Enemy boss = null;
                string Out2 = "Design/Captures/playground-2026-10-16";
                void S(string n) { UiShot.Shot(n, new[] { Out2 }); Debug.Log("PolishShot: " + n); }
                return new List<Action>
                {
                    () => { tm0 = Prefs.TestMode; Prefs.TestMode = true; CryoSoftEnvironment.Enabled = true; save0 = PlayerPrefs.GetString("ms.progress", ""); G.menu.Show(); Later(1.5); },
                    () => { UiShot.Prepare(); Later(1); },
                    () => { S("01_menu_playground_button"); Debug.Log("PolishShot: playground enter " + PlaygroundMode.Enter(G)); Later(3); },
                    () => { UiShot.Prepare(); Later(0.5); },
                    () => { S("02_picker_open"); var g = G; foreach (var n in new[] { "grunt", "leaper", "spitter", "carapace", "shardback", "tick" }) { int ti = g.swarm.TypeByName(n); if (ti >= 0) { var e = g.DebugSpawn(ti, g.player.p + new Vector2((n.Length % 5) * 1.6f - 3f, 5.5f + (n.Length % 3))); e.spawnT = 1; e.frozen = 3; } } Later(1.2); },
                    () => { S("03_types_spawned"); var g = G; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); boss = BossBrain.Spawn(g, "Razorback", g.player.p + new Vector2(0, 7), false); Later(2.5); },
                    () => { S("04_boss"); var g = G; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); Later(0.5); },
                    () => { var g = G; var bad = PlaygroundMode.SpawnAllOnce(g, false); Debug.Log($"PolishShot: playground enemies spawned once: {g.swarm.enemies.Count} alive, failed: {(bad.Count == 0 ? "none" : string.Join(", ", bad))}"); Later(1.5); },
                    () => { var g = G; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); Later(0.5); },
                    () => { var g = G; var bad = PlaygroundMode.SpawnAllOnce(g, true); Debug.Log($"PolishShot: playground bosses spawned once: {g.swarm.enemies.Count} alive, failed: {(bad.Count == 0 ? "none" : string.Join(", ", bad))}"); Later(2); },
                    () => { S("05_all_bosses"); var g = G; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); Later(1); },
                    () => { var g = G; g.godMode = true; int ti = g.swarm.TypeByName("grunt"); for (int i = 0; i < 30; i++) { float a = i / 30f * Mathf.PI * 2; var e = g.DebugSpawn(ti, NearW(g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (6 + i % 4))); e.hp = e.maxHp = 1e5f; } Later(1); },
                    () => { S("06_thirty_enemies"); int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 3) return false; Debug.Log($"PolishShot: playground 30 enemies: editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0}, alive {G.swarm.enemies.Count}"); QualitySettings.vSyncCount = 1; return true; }; },
                    () => { PlaygroundMode.Leave(G); Later(2); },
                    () => { var s1 = PlayerPrefs.GetString("ms.progress", ""); Debug.Log($"PolishShot: playground save unchanged: {s1 == save0} ({save0.Length} chars)"); Prefs.TestMode = tm0; Later(0.2); },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_OVERVIEW") == "1")
            {   // every Cryo floor from above: an orthographic top view of the whole room and a high 3/4 view, no HUD, no enemies
                var r = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; Later(1); } };
                foreach (int fl in new[] { 1, 2, 3, 4, 5 })
                {
                    int f = fl;
                    r.Add(() => { StartCryo(); if (f > 1) G.DebugFloor(f); if (G.state == Game.State.Won) G.state = Game.State.Playing; Later(2.5); });
                    r.Add(() => { Hold(); G.swarm.Clear(); Overview("overview_F" + f + "_" + MazeMap.LayoutName(MazeMap.MapIndex).Replace("cryo-station/", "")); Later(0.3); });
                    r.Add(() => { G.ToMenu(); Later(1); });
                }
                return r;
            }
            if (Environment.GetEnvironmentVariable("POLISH_PROBE") == "1")
            {   // which renderers draw the floor at each odd-looking cell: per Cryo floor, every cell's ZoneCells char, and for a sample of
                // light-blue 'i'-like cells the renderers whose bounds hold that point (name, material, shader)
                var r = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; Later(1); } };
                foreach (int fl in new[] { 1, 2, 3, 4, 5 })
                {
                    int f = fl;
                    r.Add(() => { StartCryo(); if (f > 1) G.DebugFloor(f); Later(2.5); });
                    r.Add(() => { Hold(); var chars = new Dictionary<char, int>(); var sample = new Dictionary<char, Vector2>();
                        for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) { if (!MazeMap.Walkable(x, z)) continue; var c = MazeMap.Center(x, z); char ch = ZoneCells.At(c); chars.TryGetValue(ch, out int n); chars[ch] = n + 1; if (!sample.ContainsKey(ch)) sample[ch] = c; }
                        Debug.Log($"PolishShot probe F{f} {MazeMap.LayoutName(MazeMap.MapIndex)}: cell chars " + string.Join(", ", new List<char>(chars.Keys).ConvertAll(k => $"'{k}' {chars[k]}")));
                        foreach (var kv in sample)
                        {
                            var w = MazeMap.ToWorld(kv.Value, 0.02f); var hits = new List<string>();
                            foreach (var rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            {
                                if (!rr.enabled || !rr.gameObject.activeInHierarchy) continue; var b = rr.bounds;
                                if (w.x < b.min.x || w.x > b.max.x || w.z < b.min.z || w.z > b.max.z || b.min.y > 0.4f || b.size.x > 30 && b.size.z > 20 && !rr.name.StartsWith("SC_")) continue;
                                var m = rr.sharedMaterial; hits.Add($"{rr.name}<{(rr.transform.parent != null ? rr.transform.parent.name : "-")}> [{(m != null ? m.name + " / " + m.shader.name : "none")}] y {b.min.y:0.00}..{b.max.y:0.00}");
                            }
                            Debug.Log($"PolishShot probe F{f} cell '{kv.Key}' at {kv.Value}: " + string.Join(" | ", hits));
                        }
                        Later(0.2); });
                    r.Add(() => { G.ToMenu(); Later(1); });
                }
                return r;
            }
            // READ_OFF=Rings,HpBars,...: those ReadabilityFx toggles off for this capture ("all" = every one)
            var off = Environment.GetEnvironmentVariable("READ_OFF");
            if (!string.IsNullOrEmpty(off))
                foreach (var f in typeof(ReadabilityFx).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    if (f.FieldType == typeof(bool) && (off == "all" || ("," + off + ",").Contains("," + f.Name + ","))) f.SetValue(null, false);
            if (Environment.GetEnvironmentVariable("POLISH_EVENTS") == "1")
            {   // one shot per one-off effect on Cryo F1: a spawn rising, the Spitter's lob, a kill burst, a barrel-sized blast, a level-up
                Enemy spit = null;
                return new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); Later(1); },
                    () => { StartCryo(); Later(3); },
                    () => { Hold(); var g = G; g.swarm.Clear(); g.godMode = true; var e = g.DebugSpawn(0, NearW(g.player.p + new Vector2(4, 2))); e.spawnT = 0; Later(0.25); },
                    () => { Shot("event_spawn"); var g = G; int sp = Array.FindIndex(g.cfg.enemyTypes, t => t.name == "spitter"); spit = g.DebugSpawn(sp, NearW(g.player.p + new Vector2(-7, 3))); spit.spawnT = 1; spit.aiCd = 0; Later(0.5); },
                    () => { Shot("event_lob"); Later(0.15); },
                    () => { Shot("event_lob2"); var g = G; g.swarm.Clear(); for (int i = 0; i < 3; i++) { var e = g.DebugSpawn(0, NearW(g.player.p + new Vector2(3 + i, -2))); e.spawnT = 1; } Later(0.3); },
                    () => { var g = G; foreach (var e in g.swarm.enemies.ToArray()) g.KillEnemy(e); Later(0.08); },
                    () => { Shot("event_kill"); Fx.I.Boom(MazeMap.ToWorld(G.player.p + new Vector2(-4, -2), 0.5f), 3f); Later(0.15); },
                    () => { Shot("event_explosion"); Later(2.0); },
                    () => { Shot("event_scorch"); var g = G; typeof(Game).GetField("level").SetValue(g, g.level + 1); Later(0.15); },
                    () => { Shot("event_levelup"); G.swarm.Clear(); G.ToMenu(); Later(1); },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_READ") == "1")
            {   // readability pass: Cryo F1 and F5 at the same seeded moment (a pack round the hero, a spitter, firing), the game view with
                // HUD, then 3 s of uncapped editor fps and the scene's renderers / triangles
                var r = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); Later(1); } };
                foreach (int fl in new[] { 1, 5 })
                {
                    int f = fl;
                    r.Add(() => { StartCryo(); if (f > 1) G.DebugFloor(f); if (G.state == Game.State.Won) G.state = Game.State.Playing; Later(3); });
                    r.Add(() => { Hold(); var g = G; g.swarm.Clear(); UnityEngine.Random.InitState(42); g.godMode = true;
                        int sp = Array.FindIndex(g.cfg.enemyTypes, t => t.name == "spitter");
                        for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.PI * 2; var e = g.DebugSpawn(0, NearW(g.player.p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (4.5f + (i % 3) * 1.5f))); e.hp = e.maxHp = e.maxHp * 6; }
                        if (sp >= 0) { var s1 = g.DebugSpawn(sp, NearW(g.player.p + new Vector2(6, 4))); s1.hp = s1.maxHp = 1e5f; }
                        g.holdFire = false; Later(1.3); });
                    r.Add(() => { Shot("read_F" + f); Later(0.4); });
                    r.Add(() => { Shot("read_F" + f + "_b"); int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 3) return false;
                            int rend = 0, tris = 0; foreach (var rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) { if (!rr.enabled || !rr.gameObject.activeInHierarchy || rr is ParticleSystemRenderer) continue; rend++; var mf = rr.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) for (int k = 0; k < mf.sharedMesh.subMeshCount; k++) tris += (int)mf.sharedMesh.GetIndexCount(k) / 3; }
                            Debug.Log($"PolishShot: {Tag} F{f}: editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0}, renderers {rend}, static tris {tris}, camera distance {G.cfg.camera.distance}"); QualitySettings.vSyncCount = 1; return true; }; });
                    r.Add(() => { G.holdFire = true; G.swarm.Clear(); G.ToMenu(); Later(1); });
                }
                return r;
            }
            if (Environment.GetEnvironmentVariable("POLISH_RING") == "1")
            {   // auto-fire all round: the hero faces +x (walked that way), Drones behind (-x) and beside (+-y); HP lost per group after 4 s
                var groups = new List<(string, Enemy)>(); bool cone = Environment.GetEnvironmentVariable("POLISH_CONE") == "1";
                return new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); Game.AutoFireAllRound = !cone; Later(1); },
                    () => { StartCryo(); Later(3); },
                    () => { Hold(); var g = G; g.swarm.Clear(); var f = typeof(Game).GetField("faceDir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); f.SetValue(g, new Vector2(1, 0));
                            foreach (var (n, off) in new[] { ("behind", new Vector2(-5, 0)), ("behind", new Vector2(-5.5f, 1.2f)), ("left", new Vector2(0, 5)), ("right", new Vector2(0, -5)), ("front", new Vector2(6, 0)) })
                            { var e = g.DebugSpawn(0, NearW(g.player.p + off)); e.hp = e.maxHp = 1e6f; e.speed = 0; groups.Add((n, e)); }
                            g.holdFire = false; Later(1.2); },
                    () => { Shot((cone ? "cone" : "allround") + "_fire_1"); Later(1.4); },
                    () => { Shot((cone ? "cone" : "allround") + "_fire_2"); Later(1.4); },
                    () => { var sums = new Dictionary<string, float>(); foreach (var (n, e) in groups) { sums.TryGetValue(n, out float v); sums[n] = v + (1e6f - e.hp); }
                            Debug.Log($"PolishShot: {(cone ? "cone" : "all round")}: damage taken in 4 s by group: {string.Join(", ", new List<string>(sums.Keys).ConvertAll(k => k + " " + sums[k].ToString("0")))}");
                            G.holdFire = true; G.swarm.Clear(); Game.AutoFireAllRound = true; G.ToMenu(); Later(1); },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_FIRE") == "1")
            {   // continuous fire on Cryo F1: 10 tanky Drones in range, 8 s of auto-fire, ammo / reload state sampled every 0.5 s
                int n = 0, s0 = 0, reloads = 0; float minAmmo = 1e9f;
                var list = new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); Later(1); },
                    () => { StartCryo(); if (Environment.GetEnvironmentVariable("POLISH_RELOAD") == "1") G.SetReload(true); Later(3); },
                    () => { Hold(); var g = G; g.swarm.Clear(); for (int i = 0; i < 10; i++) { var e = g.DebugSpawn(0, NearW(g.player.p + new Vector2(-4 + i, 7 + (i % 3)))); e.hp = e.maxHp = 1e7f; }
                            g.holdFire = false; s0 = (int)typeof(Game).GetField("shotCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g); Later(0.5); },
                };
                for (int k = 0; k < 16; k++)
                {
                    int kk = k;
                    list.Add(() => { var g = G; var p = g.player; if (p.Reloading) reloads++; minAmmo = Mathf.Min(minAmmo, p.ammo[p.weapon]);
                        if (kk == 3 || kk == 9 || kk == 15) Shot("noreload_fire_" + kk); n++; Later(0.5); });
                }
                list.Add(() => { var g = G; int s1 = (int)typeof(Game).GetField("shotCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g);
                    Debug.Log($"PolishShot: fire test {n} samples over 8 s: shots {s1 - s0}, samples reloading {reloads}, min ammo {minAmmo}, magSize {g.cfg.weapons[g.player.weapon].magSize}, ReloadEnabled {Game.ReloadEnabled}"); g.holdFire = true; g.swarm.Clear(); g.ToMenu(); Later(1); });
                return list;
            }
            if (Environment.GetEnvironmentVariable("POLISH_ENEMY") is string en && en.Length > 0)
            {   // a new enemy type in a Cryo room: a few beside Drones walking at the hero, the game view and a close look
                Enemy one = null;
                return new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); Later(1); },
                    () => { StartCryo(); Later(3); },
                    () => { Hold(); var g = G; g.swarm.Clear(); int ti = Array.FindIndex(g.cfg.enemyTypes, t => t.name == en); Debug.Log($"PolishShot: {en} type index {ti}");
                            for (int i = 0; i < 3; i++) { var e = g.DebugSpawn(ti, NearW(g.player.p + new Vector2(-4 + 4 * i, 6))); e.hp = e.maxHp = 1e6f; if (i == 1) one = e; }
                            for (int i = 0; i < 3; i++) { var e = g.DebugSpawn(0, NearW(g.player.p + new Vector2(-3 + 3 * i, 9))); e.hp = e.maxHp = 1e6f; } Later(1.6); },
                    () => { Shot("enemy_" + en + "_game"); Later(0.6); },
                    () => { Shot("enemy_" + en + "_game2"); var p = MazeMap.ToWorld(one.p, 0.6f); CloseAt(p, "enemy_" + en + "_close"); Later(0.5); },
                    () => { G.swarm.Clear(); G.ToMenu(); Later(1); },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_RETURN") == "1")
            {   // the menu before a run and after coming back (MenuSceneryGate hides the menu's 3D worlds during the run)
                return new List<Action>
                {
                    () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; UiShot.Prepare(); G.menu.Show(); Later(2.5); },
                    () => { Shot("return_menu_before"); G.menu.OpenSheet("gear"); Later(1.5); },
                    () => { Shot("return_gear_before"); G.menu.CloseSheet(); Later(0.8); },
                    () => { StartCryo(); Later(3); },
                    () => { Debug.Log($"PolishShot: in run, menu worlds hidden {MenuSceneryGate.Hidden}"); G.ToMenu(); G.menu.Show(); Later(2.5); },
                    () => { Debug.Log($"PolishShot: back at the menu, menu worlds hidden {MenuSceneryGate.Hidden}"); Shot("return_menu_after"); G.menu.OpenSheet("gear"); Later(1.5); },
                    () => { Shot("return_gear_after"); G.menu.CloseSheet(); Later(0.5); },
                };
            }
            if (Environment.GetEnvironmentVariable("POLISH_HERO") == "1")
            {   // the hero's hands on the minigun: a close camera at 4 angles, idle and firing, Meshy rig (default) then Blender rig; IK distances logged
                var h = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; rigBefore = Prefs.VanguardMeshy; Later(1); } };
                foreach (var meshy in new[] { true, false })
                {
                    string r = meshy ? "meshyrig" : "blenderrig";
                    h.Add(() => { Prefs.VanguardMeshy = meshy; StartCryo(); G.player.RebuildHero(G.cfg); Later(2.5); });
                    h.Add(() => { Hold(); G.swarm.Clear(); Aim(); Player.IkDebug = true; Later(0.5); });
                    h.Add(() => { Player.IkDebug = false; Close(r + "_idle"); var g = G; g.holdFire = false; var e = g.DebugSpawn(0, g.player.p + new Vector2(9, 0)); e.hp = e.maxHp = 1e6f; e.v = Vector2.zero; Later(0.8); });
                    h.Add(() => { Aim(); Player.IkDebug = true; Later(0.05); });
                    h.Add(() => { Player.IkDebug = false; Close(r + "_fire"); G.holdFire = true; G.swarm.Clear(); G.ToMenu(); Later(1); });
                }
                return h;
            }
            var s = new List<Action>
            {
                () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; rigBefore = Prefs.VanguardMeshy; UiShot.Prepare(); G.menu.Show(); Later(2.5); },
                () => { Shot("menu_hangar"); G.menu.OpenSheet("squad"); Later(1.5); },
                () => { Shot("menu_heroes"); G.menu.OpenSheet("gear"); Later(1.5); },
                () => { Shot("menu_gear"); G.menu.OpenSheet("loadout"); Later(1.5); },
                () => { Shot("menu_loadout"); G.menu.CloseSheet(); Later(0.5); },
            };
            foreach (var meshy in new[] { true, false })
            {
                string r = meshy ? "meshyrig" : "blenderrig";
                s.Add(() => { Prefs.VanguardMeshy = meshy; StartCryo(); G.player.RebuildHero(G.cfg); Later(3); });
                s.Add(() => { Hold(); G.swarm.Clear(); Aim(); Later(0.6); });
                s.Add(() => { Shot("run_" + r + "_stand"); var g = G; g.holdFire = false; for (int i = 0; i < 5; i++) { var e = g.DebugSpawn(0, g.player.p + new Vector2(6 + i * .6f, (i - 2) * .8f)); e.hp = e.maxHp = 1e6f; } Later(1.2); });
                s.Add(() => { Aim(); Shot("run_" + r + "_fire"); var c = Camera.main; var cfg = G.cfg.camera; float d0 = cfg.distance; cfg.distance = d0 * .45f; wait = null; Later(0.8); s2 = () => cfg.distance = d0; });
                s.Add(() => { Aim(); Shot("run_" + r + "_close"); s2?.Invoke(); s2 = null; G.holdFire = true; G.swarm.Clear(); G.ToMenu(); Later(1.5); });
            }
            s.Add(() => { Prefs.VanguardMeshy = rigBefore; Later(0.2); });
            return s;
        }
        static Action s2;
        static void Overview(string n)
        {
            float w = MazeMap.W * MazeMap.C, h = MazeMap.H * MazeMap.C; var ctr = MazeMap.ToWorld(new Vector2(w / 2, h / 2), 0);
            var go = new GameObject("OverviewCam"); var cam = go.AddComponent<Camera>(); cam.nearClipPlane = .3f; cam.farClipPlane = 300;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.85f, .9f, .96f);
            int W = 1800, H = Mathf.RoundToInt(1800 * h / w) + 1; var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            cam.orthographic = true; cam.orthographicSize = h / 2 + 1; cam.aspect = (float)W / H; go.transform.position = ctr + Vector3.up * 80; go.transform.rotation = Quaternion.Euler(90, 0, 0); cam.Render();
            Save(rt, W, H, n + "_top");
            var rt2 = new RenderTexture(1800, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt2; cam.orthographic = false; cam.fieldOfView = 34; cam.aspect = 1.8f;
            go.transform.position = ctr + new Vector3(0, 46, -34); go.transform.LookAt(ctr + new Vector3(0, 0, 2)); cam.Render();
            Save(rt2, 1800, 1000, n + "_34");
            rt.Release(); rt2.Release(); Object.DestroyImmediate(go);
        }
        static void Save(RenderTexture rt, int W, int H, string n)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt; var t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply(); RenderTexture.active = prev;
            Directory.CreateDirectory("Design/Captures/cryodress-2026-10-16"); File.WriteAllBytes("Design/Captures/cryodress-2026-10-16/" + n + ".png", t.EncodeToPNG()); Object.DestroyImmediate(t);
        }
        static Vector2 NearW(Vector2 p)
        {
            var best = p; float bd = 1e9f;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); float d = (c - p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }
        static void CloseAt(Vector3 c, string n)
        {
            int T = 640; var go = new GameObject("PolishCloseCam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
            var rt = new RenderTexture(T, T, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            var sheet = new Texture2D(T * 2, T, TextureFormat.RGB24, false);
            var views = new[] { new Vector3(2.6f, 1.6f, -2.6f), new Vector3(-3.2f, 1.0f, 0.4f) };
            for (int i = 0; i < 2; i++)
            {
                go.transform.position = c + views[i]; go.transform.LookAt(c); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, T, T), i * T, 0); RenderTexture.active = prev;
            }
            sheet.Apply(); Directory.CreateDirectory(Out); File.WriteAllBytes(Path.Combine(Out, Tag + "_" + n + ".png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); rt.Release(); Object.DestroyImmediate(go); Debug.Log("PolishShot: " + Tag + "_" + n);
        }
        // a close render of the hero from 4 sides (front-right, right, front-left, above-front), 4 tiles in one PNG
        // the hero alone on a plain background: front, 3/4 and side (his own facing), side by side
        static void Turn(string dir, string n)
        {
            var vis = G.player.VisualRoot; var c = G.player.transform.position + Vector3.up * .95f; int Wd = 760, Ht = 800;
            var layers = new Dictionary<Transform, int>(); foreach (var tr in vis.GetComponentsInChildren<Transform>(true)) { layers[tr] = tr.gameObject.layer; tr.gameObject.layer = 31; }
            var go = new GameObject("PolishTurnCam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 26; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.80f, .84f, .89f); cam.cullingMask = 1 << 31;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            var sheet = new Texture2D(Wd * 3, Ht, TextureFormat.RGB24, false);
            var fwd = vis.forward; fwd.y = 0; fwd.Normalize(); var right = new Vector3(fwd.z, 0, -fwd.x);
            var views = new[] { fwd, (fwd + right).normalized, right };
            for (int i = 0; i < 3; i++)
            {
                go.transform.position = c + views[i] * 4.6f + Vector3.up * .5f; go.transform.LookAt(c); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, Wd, Ht), i * Wd, 0); RenderTexture.active = prev;
            }
            sheet.Apply(); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, n + "_turnaround.png"), sheet.EncodeToPNG());
            foreach (var kv in layers) if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
            Object.DestroyImmediate(sheet); rt.Release(); Object.DestroyImmediate(go); Debug.Log("PolishShot: chibi " + n + "_turnaround");
        }
        static void Close(string n)
        {
            var hero = G.player.transform; var c = hero.position + Vector3.up * 1.0f; int T = 640;
            var go = new GameObject("PolishCloseCam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.62f, .68f, .76f);
            var rt = new RenderTexture(T, T, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            var sheet = new Texture2D(T * 2, T * 2, TextureFormat.RGB24, false);
            var fwd = G.player.VisualRoot.forward; fwd.y = 0; fwd.Normalize(); var right = new Vector3(fwd.z, 0, -fwd.x);   // the hero's own facing
            var views = new[] { (fwd + right).normalized * 3.2f + Vector3.up * .5f, right * 3.2f + Vector3.up * .3f, (fwd - right).normalized * 3.2f + Vector3.up * .5f, fwd * 2.0f + Vector3.up * 2.6f };
            for (int i = 0; i < 4; i++)
            {
                go.transform.position = c + views[i]; go.transform.LookAt(c); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, T, T), (i % 2) * T, (1 - i / 2) * T); RenderTexture.active = prev;
            }
            sheet.Apply(); Directory.CreateDirectory(Out); File.WriteAllBytes(Path.Combine(Out, Tag + "_hero_" + n + ".png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); rt.Release(); Object.DestroyImmediate(go); Debug.Log("PolishShot: " + Tag + "_hero_" + n);
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 400) { Debug.LogError("PolishShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null || g.hud == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); Later(2); return; }
            if (g.run) Hold();
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"PolishShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            Prefs.VanguardMeshy = rigBefore; SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("PolishShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
