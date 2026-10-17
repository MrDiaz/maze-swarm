using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Every Cryo Station floor, the original look then the CryoSoft kit room, HUD off at the game camera (HDR target as the
    // device renders, the room's glow included), muted. Per floor and look: layout, walkable cells, the level's triangles
    // and renderer-material pairs (an upper bound on draw calls before batching), editor fps over 3 s with a small pack.
    // CRYO_ALL_FLOORS=1,2: only those floors. CRYO_ALL_ONLY=after: only the kit look.  Output Design/Captures/cryo-all-2026-10-14
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.CryoAllShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class CryoAllShot
    {
        const string Key = "Maze.CryoAllShot.Active";
        // CRYO_ALL_OBJ=1 (2026-10-15): before = the kit rooms with the kit's machines, after = the same rooms with the Meshy objects
        static bool Obj => Environment.GetEnvironmentVariable("CRYO_ALL_OBJ") == "1";
        static string Out => Environment.GetEnvironmentVariable("CRYO_ALL_OUT") ?? (Obj ? "Design/Captures/cryo-objects-2026-10-15" : "Design/Captures/cryo-all-2026-10-14");
        static int step; static bool prepared, wasSoft, pack; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static CryoAllShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        static void Hold() { var g = G; typeof(Game).GetField("toSpawn", F).SetValue(g, 0); typeof(Game).GetField("breakT", F).SetValue(g, 99f); }
        static Texture2D Frame(int w, int h)
        {
            var cam = Camera.main;
            var hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear) { antiAliasing = 4 };
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var pt = cam.targetTexture; cam.targetTexture = hdr; cam.Render(); cam.targetTexture = pt; Graphics.Blit(hdr, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev; hdr.Release(); rt.Release();
            return tex;
        }
        static Vector2 NearestWalk(Vector2 p)
        {
            var best = p; float bd = 1e9f;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); float d = (c - p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }
        static void Teleport(Vector2 p)
        {   // the nearest walkable cell centre to p (the camera follows the hero)
            var best = p; float bd = 1e9f;
            for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) { var c = MazeMap.Center(x, z); float d = (c - p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            G.player.p = best;
        }
        static void Shot(string n) { n = (Environment.GetEnvironmentVariable("CRYO_ALL_PREFIX") ?? "") + n; Directory.CreateDirectory(Out); G.hud.gameObject.SetActive(false); var t = Frame(1600, 738); File.WriteAllBytes(Path.Combine(Out, n + ".png"), t.EncodeToPNG()); Object.DestroyImmediate(t); G.hud.gameObject.SetActive(true); }
        static void Start(int floor, bool kit)
        {
            CryoSoftEnvironment.Enabled = kit || Obj; CryoSoftEnvironment.Objects = Obj && kit; CryoPropMotion.DebugOpen = false;
            var def = SectorDefs.Get("cryo-station"); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); if (floor > 1) g.DebugFloor(floor);
            g.godMode = true; g.demo = false; g.holdFire = true; g.swarm.Clear(); pack = false;
        }
        static string Stats(bool kit)
        {
            var g = G; var level = g.transform.Find("Level");
            int tris = 0, pairs = 0, rend = 0;
            if (level != null) foreach (var r in level.GetComponentsInChildren<Renderer>()) { if (!r.enabled || !r.gameObject.activeInHierarchy) continue; rend++; pairs += r.sharedMaterials.Length; var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) tris += (int)mf.sharedMesh.GetIndexCount(0) / 3 * Mathf.Max(1, mf.sharedMesh.subMeshCount); }
            int walk = 0; for (int z = 0; z < MazeMap.H; z++) for (int x = 0; x < MazeMap.W; x++) if (MazeMap.Walkable(x, z)) walk++;
            var sun = RenderSettings.sun; var vols = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);
            Debug.Log($"CryoAllShot: env: fog {RenderSettings.fog} {RenderSettings.fogStartDistance:0}-{RenderSettings.fogEndDistance:0} col {RenderSettings.fogColor}, ambient {RenderSettings.ambientSkyColor}, sun {(sun != null ? sun.name + " " + sun.intensity + " " + sun.color + " rot " + sun.transform.eulerAngles : "none")}, volumes {vols.Length}, cryoAmbience {Object.FindObjectsByType<CryoAmbience>(FindObjectsSortMode.None).Length}");
            {   // (2026-10-15 polish) the scene's costs: shadow casters and their triangles, transparent renderers, lights, particle systems, props
                var casters = new List<KeyValuePair<string, int>>(); int sc = 0, sct = 0, tr = 0, all = 0, allT = 0, props = 0, propT = 0; var byName = new Dictionary<string, int>();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue; all++;
                    var mf = r.GetComponent<MeshFilter>(); int t = 0; if (mf != null && mf.sharedMesh != null) for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) t += (int)mf.sharedMesh.GetIndexCount(i) / 3;
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) t = smr.sharedMesh.triangles.Length / 3;
                    allT += t;
                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) { sc++; sct += t; casters.Add(new KeyValuePair<string, int>(r.name + (r.transform.parent != null ? "<" + r.transform.parent.name : ""), t)); }
                    foreach (var m in r.sharedMaterials) if (m != null && m.renderQueue > 2500) { tr++; break; }
                    var root = r.transform; while (root.parent != null && root.parent.name != "Cryo objects") root = root.parent;
                    if (root.parent != null) { props++; propT += t; byName.TryGetValue(root.name, out int c); byName[root.name] = c + t; }
                }
                int ps = 0; foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (p.gameObject.activeInHierarchy && p.particleCount > 0) ps++;
                int lights = 0; foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.enabled && l.gameObject.activeInHierarchy) lights++;
                casters.Sort((x, y) => y.Value.CompareTo(x.Value)); var top = new List<KeyValuePair<string, int>>(byName); top.Sort((x, y) => y.Value.CompareTo(x.Value));
                Debug.Log($"CryoAllShot: cost F{G.floor}: scene renderers {all} ({allT} tris), shadow casters {sc} ({sct} tris), transparent renderers {tr}, live particle systems {ps}, lights {lights}, prop renderers {props} ({propT} tris); heaviest props: {string.Join(", ", top.GetRange(0, Mathf.Min(5, top.Count)).ConvertAll(x => x.Key + " " + x.Value))}; heaviest shadow casters: {string.Join(", ", casters.GetRange(0, Mathf.Min(8, casters.Count)).ConvertAll(x => x.Key + " " + x.Value))}");
            }
            return $"layout {MazeMap.LayoutName(MazeMap.MapIndex)}, goal {g.goal}, kit room {CryoSoftEnvironment.Active}, walkable cells {walk}, level renderers {rend}, renderer-material pairs {pairs}, level tris {tris}";
        }
        static List<Action> Floor(int f, bool kit)
        {
            string tag = $"F{f}_{(kit ? "after" : "before")}";
            return new List<Action>
            {
                () => { Start(f, kit); WaitReal(4.5); },
                () => { Hold(); G.swarm.Clear(); WaitReal(0.3); },
                () => { Shot(tag); Debug.Log($"CryoAllShot: {tag}: {Stats(kit)}"); Later(0.1); },
                () => { var g = G; for (int i = 0; i < 6; i++) { var at = g.player.p + new Vector2(UnityEngine.Random.Range(-7f, 7f), UnityEngine.Random.Range(4f, 7f)); if (MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) { var e = g.DebugSpawn(0, at); e.hp = e.maxHp = 1e6f; } }
                        g.holdFire = false; WaitReal(1.2); },
                () => { if (Obj && kit) { var d = GameObject.Find("blast_door"); if (d != null) Teleport(MazeMap.ToPlane(d.transform.position) + new Vector2(0, 5.5f)); G.swarm.Clear(); WaitReal(0.8); } else Later(0.05); },
                () => { if (Obj && kit) { Shot(tag + "_door_closed"); CryoPropMotion.DebugOpen = true; WaitReal(1.8); } else Later(0.05); },
                () => { if (Obj && kit) { Shot(tag + "_door_open"); CryoPropMotion.DebugOpen = false; } Later(0.05); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 0) { var t = CryoHeroTurret.Live[0]; if (G.state == Game.State.Won) G.state = Game.State.Playing; /* the boss floor: the opening swarm clear took the boss */ t.owned = 25f; t.hp = 1e5f; var c = new Vector2(MazeMap.W * MazeMap.C / 2f, MazeMap.H * MazeMap.C / 2f); var toC = (c - t.At).normalized; var side = new Vector2(-toC.y, toC.x);
                        Teleport(t.At + toC * 2.4f + side * 2.2f); var g = G;
                        for (int i = 0; i < 4; i++) { var at = t.At + toC * (t.flame ? 4.2f : 7.5f) + side * UnityEngine.Random.Range(-1.6f, 1.6f); var e = g.DebugSpawn(0, NearestWalk(at)); e.hp = e.maxHp = 1e6f; }
                        int s0 = CryoHeroTurret.Shots, f0 = CryoHeroTurret.FlameTicks; wait = null; Time.captureFramerate = 60; WaitReal(0.9); } else Later(0.05); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 0 && Environment.GetEnvironmentVariable("CRYO_TURRET_BURST") == "1") { int i = 0; wait = () => { Shot(tag + "_turret0_burst_" + i); return ++i >= 8; }; } else Later(0.01); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 0) { var t = CryoHeroTurret.Live[0]; Shot(tag + "_turret0_" + (t.flame ? "flame" : "gun")); Debug.Log($"CryoAllShot: {tag} turret0 {(t.flame ? "flame" : "gun")}: gun shots {CryoHeroTurret.Shots}, flame ticks {CryoHeroTurret.FlameTicks} (running totals), state {G.state}, owned {t.owned:0.0}, cool {t.cool:0.0}"); Time.captureFramerate = 0; t.owned = 0; G.swarm.Clear(); } Later(0.05); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 1) { var t = CryoHeroTurret.Live[1]; if (G.state == Game.State.Won) G.state = Game.State.Playing; /* the boss floor: the opening swarm clear took the boss */ t.owned = 25f; t.hp = 1e5f; var c = new Vector2(MazeMap.W * MazeMap.C / 2f, MazeMap.H * MazeMap.C / 2f); var toC = (c - t.At).normalized; var side = new Vector2(-toC.y, toC.x);
                        Teleport(t.At + toC * 2.4f + side * 2.2f); var g = G;
                        for (int i = 0; i < 4; i++) { var at = t.At + toC * (t.flame ? 4.2f : 7.5f) + side * UnityEngine.Random.Range(-1.6f, 1.6f); var e = g.DebugSpawn(0, NearestWalk(at)); e.hp = e.maxHp = 1e6f; }
                        int s0 = CryoHeroTurret.Shots, f0 = CryoHeroTurret.FlameTicks; wait = null; Time.captureFramerate = 60; WaitReal(0.9); } else Later(0.05); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 1 && Environment.GetEnvironmentVariable("CRYO_TURRET_BURST") == "1") { int i = 0; wait = () => { Shot(tag + "_turret1_burst_" + i); return ++i >= 8; }; } else Later(0.01); },
                () => { if (Obj && kit && CryoHeroTurret.Live.Count > 1) { var t = CryoHeroTurret.Live[1]; Shot(tag + "_turret1_" + (t.flame ? "flame" : "gun")); Debug.Log($"CryoAllShot: {tag} turret1 {(t.flame ? "flame" : "gun")}: gun shots {CryoHeroTurret.Shots}, flame ticks {CryoHeroTurret.FlameTicks} (running totals), state {G.state}, owned {t.owned:0.0}, cool {t.cool:0.0}"); Time.captureFramerate = 0; t.owned = 0; G.swarm.Clear(); } Later(0.05); },
                () => { Shot(tag + "_action"); int f0 = Time.frameCount; double t0 = EditorApplication.timeSinceStartup; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                        wait = () => { if (EditorApplication.timeSinceStartup - t0 < 3) return false; Debug.Log($"CryoAllShot: {tag}: editor fps {(Time.frameCount - f0) / (EditorApplication.timeSinceStartup - t0):0} (uncapped, 6 enemies, firing)"); QualitySettings.vSyncCount = 1; return true; }; },
            };
        }
        static List<Action> Build()
        {
            var off = Environment.GetEnvironmentVariable("READ_OFF");   // (readability toggles off for this capture, as in PolishShot)
            if (!string.IsNullOrEmpty(off)) foreach (var f in typeof(ReadabilityFx).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) if (f.FieldType == typeof(bool) && (off == "all" || ("," + off + ",").Contains("," + f.Name + ","))) f.SetValue(null, false);
            var s = new List<Action> { () => { Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; wasSoft = CryoSoftEnvironment.Enabled; Later(0.5); } };
            if (Environment.GetEnvironmentVariable("CRYO_GROWTH") == "1")
            {   // no-growth: three passes over floors 1-5 with the objects on, each floor's turrets brought online and firing; the
                // scene's objects, materials and meshes counted on every floor (pass 2 and 3 should match pass 1)
                s.Add(() => { Start(1, true); WaitReal(1); });
                for (int pass = 1; pass <= 3; pass++) for (int f = 1; f <= 5; f++)
                {
                    int ff = f, pp = pass;
                    s.Add(() => { var g = G; if (ff > 1 || pp > 1) g.DebugFloor(ff); g.swarm.Clear(); if (g.state == Game.State.Won) g.state = Game.State.Playing; WaitReal(0.6); });
                    s.Add(() => { var g = G; foreach (var t in CryoHeroTurret.Live) { t.owned = 25f; t.hp = 1e5f; for (int i = 0; i < 3; i++) { var e = g.DebugSpawn(0, NearestWalk(t.At + new Vector2(UnityEngine.Random.Range(-4f, 4f), UnityEngine.Random.Range(-4f, 4f)))); e.hp = e.maxHp = 1e6f; } } WaitReal(1.5); });
                    s.Add(() => { var g = G; g.swarm.Clear(); foreach (var t in CryoHeroTurret.Live) t.owned = 0; Later(0.5); });
                    s.Add(() => {
                        int gos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, mats = Resources.FindObjectsOfTypeAll<Material>().Length, meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length, tex = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                        int fx = 0; foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (tr.name.EndsWith(" muzzle fx")) fx++;
                        { var names = new List<string>(); foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) names.Add(m.name); names.Sort(); Directory.CreateDirectory("Logs/CryoGrowth"); File.WriteAllLines($"Logs/CryoGrowth/mats_p{pp}_f{ff}.txt", names); }
                        Debug.Log($"CryoGrowth: state {G.state}, pass {pp} floor {ff} layout {MazeMap.LayoutName(MazeMap.MapIndex)}: game objects {gos}, materials {mats}, meshes {meshes}, textures {tex}, live turrets {CryoHeroTurret.Live.Count}, muzzle fx anchors {fx}, gun shots {CryoHeroTurret.Shots}, flame ticks {CryoHeroTurret.FlameTicks}");
                        Later(0.05); });
                }
                s.Add(() => { G.ToMenu(); Later(0.5); });
                return s;
            }
            var only = Environment.GetEnvironmentVariable("CRYO_ALL_FLOORS"); var mode = Environment.GetEnvironmentVariable("CRYO_ALL_ONLY");
            for (int f = 1; f <= 5; f++)
            {
                if (only != null && !("," + only + ",").Contains("," + f + ",")) continue;
                if (mode != "after") s.AddRange(Floor(f, false));
                if (mode != "before") s.AddRange(Floor(f, true));
            }
            s.Add(() => { G.ToMenu(); Later(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 420) { Debug.LogError("CryoAllShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); Later(2); return; }
            if (g.run) Hold();
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"CryoAllShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            CryoSoftEnvironment.Enabled = wasSoft;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("CryoAllShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
