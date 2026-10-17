using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // The gunfire kit in real gameplay on the first Cryo floor (the CryoSoft room), muted, at the game camera:
    //   rifle (the Sentinel) and the Heavy's minigun under Barrage, a small pack kept alive to the hero's right so he
    //   auto-fires across the screen. Per variant: HUD-on stills (UiShot), a HUD-off clip (H.264 via MediaEncoder, 30 fps
    //   game time) and its PNG frames; then the checks: shots/s with the kit and with the old look, pool counts and peaks,
    //   expiry after firing stops, GameObject counts across 3 floor changes, editor fps.
    //   GUNFIRE_MODE=first: rifle stills + a short clip only.   GUNFIRE_OUT: output folder.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.GunfireShot.Run   (no -quit)
    [InitializeOnLoad]
    public static class GunfireShot
    {
        const string Key = "Maze.GunfireShot.Active";
        static int step; static bool prepared, wasSoft; static double next = -1, armed = -1;
        static Func<bool> wait; static List<Action> steps;
        static string Out => Environment.GetEnvironmentVariable("GUNFIRE_OUT") ?? "Design/Captures/gunfire-2026-10-14";
        static bool First => Environment.GetEnvironmentVariable("GUNFIRE_MODE") == "first";
        static string Sfx => Environment.GetEnvironmentVariable("GUNFIRE_SUFFIX") ?? "";   // e.g. _v2: new files beside the old
        static GunfireShot() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static Game G => Object.FindAnyObjectByType<Game>();
        static void Later(double s) { next = EditorApplication.timeSinceStartup + s; }
        static void WaitReal(double s) { double t0 = EditorApplication.timeSinceStartup; wait = () => EditorApplication.timeSinceStartup >= t0 + s; }
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        static int Bullets => ((System.Collections.ICollection)typeof(Game).GetField("bullets", F).GetValue(G)).Count;
        static int ShotCount => (int)typeof(Game).GetField("shotCount", F).GetValue(G);
        static int TracerPool => ((System.Collections.ICollection)typeof(Fx).GetField("tracerPool", F).GetValue(Fx.I)).Count;
        static int KitLive => GunFx.I.Kit != null ? GunFx.I.Kit.Live : -1;
        static void KitOn(bool on)
        {
            var f = typeof(GunFx).GetField("kit", F);
            if (!on) { if (savedKit == null) savedKit = f.GetValue(GunFx.I); f.SetValue(GunFx.I, null); }
            else if (savedKit != null) f.SetValue(GunFx.I, savedKit);
        }
        static object savedKit;

        // the run: the first Cryo floor, the chosen hero, a pack kept alive to his right
        static bool pack, topUp;
        static void Start(bool heavy)
        {
            
            var def = SectorDefs.Get("cryo-station"); var g = G;
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            g.LoadMap(0); g.menu.Hide(); g.Restart(); g.godMode = true; g.demo = false; g.holdFire = false;
            g.swarm.Clear(); pack = true;
            Debug.Log($"GunfireShot: start {(heavy ? "Heavy minigun" : "rifle")}; layout {MazeMap.LayoutName(MazeMap.MapIndex)}, CryoSoft {CryoSoftEnvironment.Active}, VgMinigun {g.VgMinigun}");
        }
        static float packT;
        static void KeepPack()
        {
            var g = G; if (!pack || g == null || g.player == null) return;
            typeof(Game).GetField("toSpawn", F).SetValue(g, 0); typeof(Game).GetField("breakT", F).SetValue(g, 99f);   // the floor's own waves held off
            foreach (var e in g.swarm.enemies)   // keep the pack at range (tanky enemies would otherwise pile onto the hero and hide the muzzle)
                if ((e.p - g.player.p).sqrMagnitude < 4.5f * 4.5f) { var to = g.player.p + new Vector2(UnityEngine.Random.Range(7f, 9.5f), UnityEngine.Random.Range(-1.8f, 1.8f)); if (MazeMap.Walkable(Mathf.FloorToInt(to.x / MazeMap.C), Mathf.FloorToInt(to.y / MazeMap.C))) e.p = to; }
            if (topUp) g.player.ammo[g.player.weapon] = Mathf.Max(g.player.ammo[g.player.weapon], 30);   // (capture only: no reload pauses in the clip)
            if (Time.time < packT) return; packT = Time.time + 0.25f;
            if (g.swarm.enemies.Count >= 4) return;
            for (int k = 0; k < 12; k++)
            {
                var at = g.player.p + new Vector2(UnityEngine.Random.Range(6.5f, 9.5f), UnityEngine.Random.Range(-1.8f, 1.8f));
                if (MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) { var e = g.DebugSpawn(0, at); e.hp = 1e6f; break; }   // tanky: the gun fires at its full rate
            }
        }

        // a HUD-off frame of the game camera (HDR target, as the device renders; the room's bloom included)
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
        static MediaEncoder enc; static int clipN, clipFrames; static string clipTag;
        static void ClipStart(string tag, int frames)
        {
            Directory.CreateDirectory(Path.Combine(Out, tag + "_frames"));
            var attr = new VideoTrackAttributes { frameRate = new MediaRational(60), width = 1600, height = 738, includeAlpha = false, bitRateMode = VideoBitrateMode.High };
            enc = new MediaEncoder(Path.Combine(Out, tag + ".mp4"), attr); clipN = 0; clipFrames = frames; clipTag = tag;
            Time.captureFramerate = 60;   // as a 60 Hz phone: the 0.05 s flashes render near full strength (at 30 they are mostly faded by their first frame)
            wait = () =>
            {
                var tex = Frame(1600, 738); enc.AddFrame(tex);
                if (First || clipN < 72 || clipN % 6 == 0) File.WriteAllBytes(Path.Combine(Out, clipTag + "_frames", $"{clipN:000}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return ++clipN >= clipFrames;
            };
        }
        static void ClipEnd() { enc.Dispose(); enc = null; Time.captureFramerate = 0; Debug.Log($"GunfireShot: clip {clipTag}.mp4, {clipFrames} frames at 60 fps; kit shots so far {GunfireKit.Shots}, impacts {GunfireKit.Impacts}, enemy hits {GunfireKit.EnemyHits}, game shots {ShotCount}"); }
        static void HudOff(bool off) { G.hud.gameObject.SetActive(!off); }
        static void Still(string n, bool hudOff)
        {
            Directory.CreateDirectory(Out);
            if (hudOff) { HudOff(true); var t = Frame(1600, 738); File.WriteAllBytes(Path.Combine(Out, n + ".png"), t.EncodeToPNG()); Object.DestroyImmediate(t); HudOff(false); }
            else UiShot.Shot(n, new[] { Out });
        }

        // pool / rate measurement over real time
        static int peakLive, peakBullets, s0, shotsN; static double m0; static int f0;
        static void Measure(string tag, double secs, bool barrage)
        {
            peakLive = 0; peakBullets = 0; s0 = ShotCount; m0 = EditorApplication.timeSinceStartup; f0 = Time.frameCount;
            int h0 = GunfireKit.TracersHidden, sh0 = GunfireKit.Shots;
            wait = () =>
            {
                if (barrage) Abilities.BarrageT = Mathf.Max(Abilities.BarrageT, 1f);
                peakLive = Mathf.Max(peakLive, KitLive); peakBullets = Mathf.Max(peakBullets, Bullets);
                if (EditorApplication.timeSinceStartup - m0 < secs) return false;
                double t = EditorApplication.timeSinceStartup - m0; shotsN = ShotCount - s0;
                Debug.Log($"GunfireShot: MEASURE {tag}: {shotsN} shots in {t:0.0} s = {shotsN / t:0.0} shots/s (game time scale {Time.timeScale}), kit shots {GunfireKit.Shots - sh0}, hidden tracers {GunfireKit.TracersHidden - h0}; peak kit particles {peakLive}, peak bullets/tracers in flight {peakBullets}, tracer pool idle {TracerPool}; editor fps {(Time.frameCount - f0) / t:0}");
                return true;
            };
        }

        static List<Action> Variant(bool heavy)
        {
            string v = (heavy ? "minigun" : "rifle"), vs = v + Sfx;
            var l = new List<Action>
            {
                () => { Start(heavy); WaitReal(4.5); },   // past the floor banner
                () => { if (heavy) Abilities.BarrageT = 30f; topUp = true; WaitReal(1.5); },   // firing
            };
            for (int i = 0; i < 3; i++) { int k = i; l.Add(() => { Still($"{vs}_hud_{k}", false); Later(0.13 + 0.05 * k); }); }
            l.Add(() => { Still($"{vs}_hudoff", true); Later(0.1); });
            l.Add(() => { HudOff(true); ClipStart($"{vs}_clip", First ? 240 : 420); });
            l.Add(() => { ClipEnd(); HudOff(false); topUp = false; Later(0.1); });
            if (First) return l;
            // checks (real time, uncapped)
            l.Add(() => { topUp = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; KitOn(true); Measure($"{v} kit{(heavy ? " + Barrage" : "")}", heavy ? 10 : 6, heavy); });
            l.Add(() => { KitOn(false); Measure($"{v} OLD look{(heavy ? " + Barrage" : "")}", heavy ? 10 : 6, heavy); });
            l.Add(() => { KitOn(true); topUp = false; pack = false; G.holdFire = true; G.swarm.Clear(); Abilities.BarrageT = 0; WaitReal(1.0); });
            l.Add(() => { Debug.Log($"GunfireShot: EXPIRY {v}: 1.0 s after firing stopped: kit particles {KitLive}, bullets in flight {Bullets}, tracer pool idle {TracerPool}"); G.holdFire = false; pack = true; Later(0.1); });
            return l;
        }
        static int Objects => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
        static List<Action> Build()
        {
            var s = new List<Action> { () => { UiShot.Prepare(); Progress.Sim = true; Progress.UseTemp(new SaveData { runs = 5 }); Prefs.TestMode = false; wasSoft = CryoSoftEnvironment.Enabled; CryoSoftEnvironment.Enabled = true; Debug.Log($"GunfireShot: kit loaded {(GunFx.I.Kit != null)}"); Later(0.5); } };
            bool heavyRun = Environment.GetEnvironmentVariable("MAZE_HERO") == "vanguard";   // the Heavy's model loads at startup: one hero per run
            s.AddRange(Variant(heavyRun));
            if (!First && heavyRun)
            {
                for (int f = 2; f <= 4; f++)
                {
                    int ff = f;
                    s.Add(() => { Abilities.BarrageT = 30f; G.DebugFloor(ff); G.godMode = true; G.holdFire = false; pack = true; WaitReal(4.0); });
                    s.Add(() => { Debug.Log($"GunfireShot: FLOOR {ff}: GameObjects {Objects}, kit particles {KitLive}, bullets {Bullets}, tracer pool idle {TracerPool}"); Later(0.1); });
                }
            }
            s.Add(() => { pack = false; Abilities.BarrageT = 0; G.ToMenu(); Later(0.5); });
            return s;
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (armed < 0) armed = now;
            if (now - armed > 420) { Debug.LogError("GunfireShot: timed out"); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            var g = G; if (g == null || g.menu == null || g.player == null) return;
            AudioListener.volume = 0;
            if (!prepared) { prepared = true; steps = Build(); Later(2); return; }
            KeepPack();
            if (g.state == Game.State.Choosing) { g.Pick(0); return; }
            if (now < next) return;
            if (wait != null) { if (!wait()) return; wait = null; }
            if (step >= steps.Count) { Finish(); return; }
            try { steps[step](); } catch (Exception e) { Debug.LogError($"GunfireShot: step {step} failed: {e}"); }
            step++;
        }
        static void Finish()
        {
            if (enc != null) { enc.Dispose(); enc = null; }
            Time.captureFramerate = 0; KitOn(true); CryoSoftEnvironment.Enabled = wasSoft; Abilities.BarrageT = 0;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Progress.Sim = false; Game.PendingTier = 0; Game.PendingSector = null;
            Debug.Log("GunfireShot: done");
            if (Application.isBatchMode) EditorApplication.Exit(0); else EditorApplication.ExitPlaymode();
        }
    }
}
