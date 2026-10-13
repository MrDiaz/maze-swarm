using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Headless balance check: plays whole runs in Play mode at a fixed 1/30 s step, as fast as the
    // editor can, and reports the wave each run reached. Scenarios: an idle player (no input, auto-fire
    // only) with each weapon, and the demo bot. Every scenario runs with bottomless magazines (the
    // old game) and with the configured magazines, on the same seeds.
    //
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.MazeSim.Run -logFile Logs/sim.log
    //   (no -quit: it enters Play mode and exits itself; results in Logs/MazeSim.txt)
    //   -simMags 30,8,60 / -simReload 1.8,2.4,1.2 try other magazine sizes / reload times (weapon order)
    //   without editing MazeConfig
    public static class MazeSim
    {
        const string Key = "maze.sim";
        const float Dt = 1f / 30f, MaxTime = 900f;
        const int StepsPerUpdate = 400;
        static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            var args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, "-simMags");
            SessionState.SetString(Key + ".mags", at >= 0 && at + 1 < args.Length ? args[at + 1] : "");
            at = System.Array.IndexOf(args, "-simReload");
            SessionState.SetString(Key + ".reload", at >= 0 && at + 1 < args.Length ? args[at + 1] : "");
            SessionState.SetBool(Key + ".quick", System.Array.IndexOf(args, "-simQuick") >= 0); // demo bot, magazines only
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                SessionState.SetBool(Key, false);
                var sim = new Sim();
                EditorApplication.update += sim.Update;
            };
        }

        class Sim
        {
            Game game;
            int scenario = -1, seedIdx, mags; // mags 0 = bottomless, 1 = configured
            int[] magSizes;
            float time;
            readonly StringBuilder log = new();
            readonly System.Collections.Generic.Dictionary<string, float> sums = new();

            static readonly (string name, bool demo, int weapon)[] Scenarios =
                { ("idle RIFLE", false, 0), ("idle SHOTGUN", false, 1), ("idle SMG", false, 2), ("demo bot", true, 0) };

            public void Update()
            {
                if (game == null)
                {
                    game = Object.FindAnyObjectByType<Game>();
                    if (game == null || game.player == null || game.menu == null) { game = null; return; }
                    game.enabled = false; // driven from here
                    magSizes = new int[game.cfg.weapons.Length];
                    for (int i = 0; i < magSizes.Length; i++) magSizes[i] = game.cfg.weapons[i].magSize;
                    var over = SessionState.GetString(Key + ".mags", "");
                    if (over != "") { var parts = over.Split(','); for (int i = 0; i < parts.Length && i < magSizes.Length; i++) magSizes[i] = int.Parse(parts[i]); }
                    var rl = SessionState.GetString(Key + ".reload", "");
                    if (rl != "") { var parts = rl.Split(','); for (int i = 0; i < parts.Length && i < magSizes.Length; i++) game.cfg.weapons[i].reloadTime = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture); }
                    log.AppendLine("magazines: " + string.Join(", ", System.Linq.Enumerable.Select(game.cfg.weapons, (w, i) => $"{w.name} {magSizes[i]} / {w.reloadTime}s")));
                    log.AppendLine("scenario        seed  mags        wave  kills  time");
                    Next();
                    return;
                }
                for (int i = 0; i < StepsPerUpdate; i++)
                {
                    game.Tick(Dt); time += Dt;
                    if (game.state == Game.State.Dead || time >= MaxTime) { Record(); if (!Next()) { Finish(); return; } break; }
                }
            }

            bool Next()
            {
                bool quick = SessionState.GetBool(Key + ".quick", false);
                if (scenario < 0) { scenario = quick ? 3 : 0; if (quick) mags = 1; }
                else if (quick) { if (++seedIdx >= Seeds.Length) { seedIdx = 0; scenario++; } }
                else if (++mags > 1) { mags = 0; if (++seedIdx >= Seeds.Length) { seedIdx = 0; scenario++; } }
                if (scenario >= Scenarios.Length) return false;
                for (int i = 0; i < magSizes.Length; i++) game.cfg.weapons[i].magSize = mags == 0 ? 0 : magSizes[i];
                Random.InitState(Seeds[seedIdx]);
                game.menu.Hide();
                game.Restart();
                var sc = Scenarios[scenario];
                game.demo = sc.demo;
                game.player.weapon = sc.weapon;
                time = 0;
                return true;
            }

            void Record()
            {
                var sc = Scenarios[scenario];
                string m = mags == 0 ? "bottomless" : "magazines";
                log.AppendLine($"{sc.name,-15} {Seeds[seedIdx],4}  {m,-10} {game.wave,5} {game.kills,6} {time,5:F0}s{(time >= MaxTime ? " (time cap)" : "")}");
                string k = sc.name + "|" + m;
                sums[k] = (sums.TryGetValue(k, out var v) ? v : 0) + game.wave;
            }

            void Finish()
            {
                EditorApplication.update -= Update;
                log.AppendLine();
                log.AppendLine("mean wave reached        bottomless  magazines");
                foreach (var sc in Scenarios)
                {
                    string Mean(string m) => sums.TryGetValue(sc.name + "|" + m, out var v) ? (v / Seeds.Length).ToString("F2") : "-";
                    log.AppendLine($"{sc.name,-24} {Mean("bottomless"),10} {Mean("magazines"),10}");
                }
                var text = log.ToString();
                File.WriteAllText("Logs/MazeSim.txt", text);
                SessionState.EraseString(Key + ".mags"); SessionState.EraseString(Key + ".reload");
                Debug.Log("MazeSim\n" + text);
                EditorApplication.isPlaying = false;
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }
    }
}
