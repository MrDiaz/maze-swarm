using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.EditorTools
{
    // Balance check for the escape floor (map 1): the demo bot plays whole runs at a fixed 1/30 s step, locked to
    // each weapon and then free to switch, on 4 seeds each, and reports the room reached, escaped or not, the time
    // and the kills. A run with no new kill and no new room for 120 s counts as stuck.
    //   Unity -batchmode -projectPath . -executeMethod Maze.EditorTools.EscapeSim.Run -logFile Logs/escsim.log
    //   (no -quit; results in Logs/EscapeSim.txt)
    public static class EscapeSim
    {
        const string Key = "maze.escsim";
        const float Dt = 1f / 30f, MaxTime = 900f, StuckAfter = 120f;
        const int StepsPerUpdate = 400;
        static readonly int[] Seeds = { 1, 2, 3, 4 };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                SessionState.SetBool(Key, false);
                var sim = new Sim(); EditorApplication.update += sim.Update;
            };
        }

        class Sim
        {
            Game game; int scenario = -1, seedIdx, lastRoom, lastKills; float time, quietT;
            readonly StringBuilder log = new();
            static readonly (string name, int weapon)[] Scenarios = { ("bot RIFLE", 0), ("bot SHOTGUN", 1), ("bot SMG", 2), ("bot switching", -1) };
            readonly int[] escaped = new int[4], roomsSum = new int[4]; readonly float[] timeSum = new float[4];

            public void Update()
            {
                if (game == null)
                {
                    game = Object.FindAnyObjectByType<Game>();
                    if (game == null || game.player == null || game.menu == null || game.escape == null) { game = null; return; }
                    game.enabled = false;
                    game.LoadMap(1);
                    log.AppendLine($"escape floor: {MazeMap.RoomCount} rooms, {MazeMap.doors.Count} doors");
                    log.AppendLine("scenario        seed  room  result    kills   hp   time");
                    Next(); return;
                }
                for (int i = 0; i < StepsPerUpdate; i++)
                {
                    game.Tick(Dt); time += Dt;
                    if (game.escape.Cur != lastRoom || game.kills != lastKills) { lastRoom = game.escape.Cur; lastKills = game.kills; quietT = 0; } else quietT += Dt;
                    bool done = game.state == Game.State.Dead || game.state == Game.State.Won || time >= MaxTime || quietT >= StuckAfter;
                    if (done) { Record(); if (!Next()) { Finish(); return; } break; }
                }
            }

            bool Next()
            {
                if (scenario < 0) scenario = 0;
                else if (++seedIdx >= Seeds.Length) { seedIdx = 0; scenario++; }
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-simQuick") >= 0 && (scenario == 1 || scenario == 2)) scenario = 3; // RIFLE + switching only
                if (scenario >= Scenarios.Length) return false;
                Random.InitState(Seeds[seedIdx]);
                game.menu.Hide(); game.Restart();
                game.demo = true; game.demoWeapon = Scenarios[scenario].weapon;
                time = 0; quietT = 0; lastRoom = 0; lastKills = 0;
                return true;
            }

            void Record()
            {
                var sc = Scenarios[scenario];
                string res = game.state == Game.State.Won ? "ESCAPED" : game.state == Game.State.Dead ? "died" : quietT >= StuckAfter ? "STUCK" : "time cap";
                if (game.state == Game.State.Won) { escaped[scenario]++; timeSum[scenario] += game.escape.RunTime; }
                roomsSum[scenario] += game.escape.Cur;
                log.AppendLine($"{sc.name,-15} {Seeds[seedIdx],4} {game.escape.Cur,5}  {res,-9} {game.kills,5} {Mathf.Max(0, game.player.hp),4:F0} {time,5:F0}s");
                if (res == "STUCK")
                {
                    var sb = new StringBuilder($"    stuck: player {game.player.p} room {MazeMap.RoomAt(game.player.p)} cleared {game.escape.Cleared}, {game.swarm.enemies.Count} enemies:");
                    foreach (var e in game.swarm.enemies) sb.Append($" [{e.type.name} {e.p} room {MazeMap.RoomAt(e.p)} hp {e.hp:F0}]");
                    log.AppendLine(sb.ToString());
                }
            }

            void Finish()
            {
                EditorApplication.update -= Update;
                log.AppendLine();
                log.AppendLine($"summary ({Seeds.Length} runs each; room {MazeMap.RoomCount - 1} = the exit room)");
                for (int i = 0; i < Scenarios.Length; i++)
                    log.AppendLine($"{Scenarios[i].name,-15} escaped {escaped[i]}/{Seeds.Length}   mean room {roomsSum[i] / (float)Seeds.Length:F2}   mean escape time {(escaped[i] > 0 ? Hud.Clock(timeSum[i] / escaped[i]) : "-")}");
                var text = log.ToString();
                File.WriteAllText("Logs/EscapeSim.txt", text);
                Debug.Log("EscapeSim\n" + text);
                game.demoWeapon = -1;
                EditorApplication.isPlaying = false;
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }
    }
}
