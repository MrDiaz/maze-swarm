#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    // Editor-only transaction. The draft is an in-memory override and is never saved to Resources.
    // Start and exit both survive domain reload. Private recovery remains if any restore step fails.
    [InitializeOnLoad]
    public static class FloorPlaytest
    {
        const string Key = "Maze.FloorEditor.Playtest.Active";
        const string MainScene = "Assets/Maze/Scenes/Main.unity";
        static readonly string Recovery = Path.GetFullPath("Library/MazeFloorEditor/PlaytestRecovery/active.private.json");
        static bool recovering, started;
        static double deadline;
        static string status = "Ready";

        [Serializable] sealed class SceneEntry { public string path; public bool loaded, active; }
        [Serializable] sealed class Transaction
        {
            public int version = 1;
            public string project, utc, sectorId, draft, progressMemory, heroOverride, pendingSector, preferenceHash, runtimeSector;
            public int floor, pendingTier, difficulty, mapIndex, frameRate, vSync, runtimeFloor;
            public bool progressSim, gameSim, audioPaused, hasSceneView, cryoEnabled;
            public float audioVolume, timeScale, viewSize;
            public Vector3 viewPivot;
            public Quaternion viewRotation;
            public bool viewOrtho;
            public SceneEntry[] scenes;
            public string[] selection;
            public Game.Goal[] pendingPlan;
            public FloorPreferenceSnapshot preferences;
            public UnityEngine.Random.State random;
        }

        public static bool Active => SessionState.GetBool(Key, false);
        public static string Status => status;
        public static event Action StatusChanged;

        static FloorPlaytest()
        {
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.quitting += Quitting;
            if (Active) { Progress.Sim = true; Hook(); }
            if (File.Exists(Recovery) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => Recover("Recovered an interrupted floor playtest.");
        }

        public static bool Begin(FloorDocument draft, string sectorId, int floor)
        {
            try { BeginCore(draft, sectorId, floor); return true; }
            catch (Exception error) { SetStatus("Playtest blocked: " + error.Message); return false; }
        }

        static void BeginCore(FloorDocument draft, string sectorId, int floor)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Active)
                throw new InvalidOperationException("Stop the current Play session before starting a floor playtest.");
            if (File.Exists(Recovery)) throw new InvalidOperationException("A prior playtest recovery is pending. Recover it before starting another test.");
            if (!FloorDocuments.TryParse(JsonUtility.ToJson(draft), out var validated, out var report))
                throw new ArgumentException("The floor draft is invalid: " + JsonUtility.ToJson(report, true));
            var sector = SectorDefs.Get(sectorId);
            if (sector?.floors == null || floor < 1 || floor > sector.floors.Length || sector.floors[floor - 1].layout != validated.layoutId)
                throw new ArgumentException("The selected zone and floor do not use this draft's layout. Choose its authored floor before playtesting.");
            validated.sectorId = sectorId; validated.floorNumber = floor;
            if (!FloorDocuments.Validate(validated).IsValid) throw new ArgumentException("The draft identity does not match this authored floor.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("Save or close unsaved scene changes before playtesting. No scene was changed.");
            }
            if (!File.Exists(MainScene)) throw new FileNotFoundException("The game's Main scene is unavailable.", MainScene);
            var prefs = FloorPreferenceProtection.Capture();
            var view = SceneView.lastActiveSceneView;
            var transaction = new Transaction
            {
                project = Path.GetFullPath("."), utc = DateTime.UtcNow.ToString("O"), sectorId = sectorId, floor = floor,
                draft = JsonUtility.ToJson(validated), preferences = prefs, preferenceHash = FloorPreferenceProtection.Digest(prefs),
                progressMemory = JsonUtility.ToJson(Progress.D), progressSim = Progress.Sim, gameSim = Game.SimRun,
                cryoEnabled = CryoSoftEnvironment.Enabled,
                pendingTier = Game.PendingTier, pendingSector = Game.PendingSector?.id, pendingPlan = Game.PendingPlan,
                difficulty = Meta.Difficulty, heroOverride = Prefs.HeroOverride, mapIndex = MazeMap.MapIndex,
                runtimeSector = FloorDocuments.RuntimeSectorId, runtimeFloor = FloorDocuments.RuntimeFloorNumber,
                audioVolume = AudioListener.volume, audioPaused = AudioListener.pause, timeScale = Time.timeScale,
                frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount, random = UnityEngine.Random.state,
                scenes = setup.Select(x => new SceneEntry { path = x.path, loaded = x.isLoaded, active = x.isActive }).ToArray(),
                selection = Selection.objects.Select(x => GlobalObjectId.GetGlobalObjectIdSlow(x).ToString()).ToArray(),
                hasSceneView = view != null,
            };
            if (view != null) { transaction.viewPivot = view.pivot; transaction.viewRotation = view.rotation; transaction.viewSize = view.size; transaction.viewOrtho = view.orthographic; }
            Directory.CreateDirectory(Path.GetDirectoryName(Recovery));
            File.WriteAllText(Recovery + ".tmp", JsonUtility.ToJson(transaction, true));
            File.Move(Recovery + ".tmp", Recovery);
            if (File.Exists(FloorPreferenceProtection.NativePath))
                File.Copy(FloorPreferenceProtection.NativePath, Path.Combine(Path.GetDirectoryName(Recovery), "native-before.private.plist"), true);
            SessionState.SetBool(Key, true);
            started = false;
            deadline = EditorApplication.timeSinceStartup + 90;
            Hook();
            SetStatus("Opening " + sector.name + " · Floor " + floor + " in a protected playtest");
            try
            {
                Progress.Sim = true;
                EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch { Recover("Playtest failed to start; original state restored."); throw; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void ProtectStartup()
        {
            if (!Active) return;
            try
            {
                var transaction = ReadTransaction();
                Progress.Sim = true;
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(transaction.progressMemory));
                Game.SimRun = false;
                CryoSoftEnvironment.Enabled = true;
                if (!FloorDocuments.TryParse(transaction.draft, out var draft, out var report))
                    throw new InvalidDataException("The playtest draft failed validation after reload: " + JsonUtility.ToJson(report));
                FloorDocuments.SetPlaytestOverride(draft);
                FloorDocuments.SetRuntimeContext(transaction.sectorId, transaction.floor);
                // These are existing game routes. No sectors or progress are unlocked.
                Prefs.TestMode = true; Prefs.TestPower = false; Prefs.TestFloor = transaction.floor;
                Prefs.HeroOverride = "vanguard";
                var sector = SectorDefs.Get(transaction.sectorId);
                Game.PendingSector = sector; Game.PendingTier = sector.tier; Game.PendingPlan = Game.NewPlan(sector);
                started = false;
                deadline = EditorApplication.timeSinceStartup + 90;
            }
            catch (Exception error)
            {
                SetStatus("Playtest startup stopped: " + error.Message);
                Debug.LogException(error);
                EditorApplication.delayCall += Stop;
            }
        }

        static void Hook() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

        static void Tick()
        {
            if (!Active || recovering) return;
            Progress.Sim = true;
            if (!EditorApplication.isPlaying) return;
            if (started) return;
            if (deadline <= 0) deadline = EditorApplication.timeSinceStartup + 90;
            if (EditorApplication.timeSinceStartup > deadline) { SetStatus("Playtest timed out while loading. Restoring original state."); Stop(); return; }
            var game = Object.FindAnyObjectByType<Game>();
            if (game?.menu == null || game.player == null || game.swarm == null) return;
            try
            {
                var transaction = ReadTransaction();
                var sector = SectorDefs.Get(transaction.sectorId);
                Game.PendingSector = sector; Game.PendingTier = sector.tier; Game.PendingPlan = Game.NewPlan(sector);
                game.LoadMap(0); game.menu.Hide(); game.Restart();
                Progress.Sim = true;
                if (!game.run || !Game.TestRun || game.floor != transaction.floor || game.zoneFloor?.layout != sector.floors[transaction.floor - 1].layout)
                    throw new InvalidOperationException("The runtime did not enter the selected authored floor.");
                if (!MazeMap.Rows.SequenceEqual(FloorDocuments.BuildRows(JsonUtility.FromJson<FloorDocument>(transaction.draft))))
                    throw new InvalidOperationException("The runtime did not consume the current draft rows.");
                started = true;
                SetStatus("Playtesting " + sector.name + " · Floor " + transaction.floor + " · progress is temporary");
            }
            catch (Exception error) { Debug.LogException(error); SetStatus("Playtest stopped: " + error.Message); Stop(); }
        }

        public static void Stop()
        {
            if (!Active && !File.Exists(Recovery)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Recover("Playtest finished. Preferences, progress and scenes restored.");
        }

        [MenuItem("Maze/Recover Floor Playtest", priority = 215)]
        public static void RecoverInterrupted() => Stop();

        static void PlayModeChanged(PlayModeStateChange state)
        {
            if (!Active) return;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                var game = Object.FindAnyObjectByType<Game>();
                if (game != null) { game.enabled = false; game.TestEnd(); }
                Progress.Sim = true;
            }
            if (state == PlayModeStateChange.EnteredEditMode) Recover("Playtest finished. Preferences, progress and scenes restored.");
        }

        static Transaction ReadTransaction()
        {
            var transaction = JsonUtility.FromJson<Transaction>(File.ReadAllText(Recovery));
            if (transaction == null || transaction.version != 1 || transaction.project != Path.GetFullPath("."))
                throw new InvalidDataException("The playtest recovery does not belong to this project.");
            return transaction;
        }

        static void Quitting()
        {
            if (!Active || !File.Exists(Recovery)) return;
            // The scene setup will be recovered on next open; do not attempt scene mutation while quitting.
            try { FloorPreferenceProtection.Restore(ReadTransaction().preferences); }
            catch (Exception error) { Debug.LogError("Floor playtest recovery remains available: " + error.Message); }
        }

        static void Recover(string success)
        {
            if (recovering || !File.Exists(Recovery)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Stop(); return; }
            recovering = true;
            try
            {
                var transaction = ReadTransaction();
                FloorDocuments.ClearPlaytestOverride();
                if (string.IsNullOrEmpty(transaction.runtimeSector)) FloorDocuments.ClearRuntimeContext();
                else FloorDocuments.SetRuntimeContext(transaction.runtimeSector, transaction.runtimeFloor);
                FloorPreferenceProtection.Restore(transaction.preferences);
                if (FloorPreferenceProtection.Digest(FloorPreferenceProtection.Capture()) != transaction.preferenceHash)
                    throw new InvalidDataException("The complete original game preference set did not match after recovery.");
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(transaction.progressMemory));
                Progress.Sim = transaction.progressSim; Game.SimRun = transaction.gameSim;
                CryoSoftEnvironment.Enabled = transaction.cryoEnabled;
                Game.TestRun = false;
                Game.PendingTier = transaction.pendingTier; Game.PendingSector = string.IsNullOrEmpty(transaction.pendingSector) ? null : SectorDefs.Get(transaction.pendingSector); Game.PendingPlan = transaction.pendingPlan;
                Meta.Difficulty = transaction.difficulty; Prefs.HeroOverride = transaction.heroOverride;
                MazeMap.MapIndex = transaction.mapIndex; MazeMap.Init();
                AudioListener.volume = transaction.audioVolume; AudioListener.pause = transaction.audioPaused; Time.timeScale = transaction.timeScale;
                Application.targetFrameRate = transaction.frameRate; QualitySettings.vSyncCount = transaction.vSync; UnityEngine.Random.state = transaction.random;
                EditorSceneManager.RestoreSceneManagerSetup(transaction.scenes.Select(x => new SceneSetup { path = x.path, isLoaded = x.loaded, isActive = x.active }).ToArray());
                Selection.objects = transaction.selection.Select(x => GlobalObjectId.TryParse(x, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null).Where(x => x != null).ToArray();
                if (transaction.hasSceneView && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.LookAt(transaction.viewPivot, transaction.viewRotation, transaction.viewSize, transaction.viewOrtho, true);
                File.Copy(Recovery, Path.Combine(Path.GetDirectoryName(Recovery), "last-completed.private.json"), true);
                File.Delete(Recovery);
                SessionState.SetBool(Key, false); EditorApplication.update -= Tick; started = false;
                SetStatus(success);
            }
            catch (Exception error)
            {
                SetStatus("Recovery needs attention: " + error.Message + " Private backup: " + Recovery);
                Debug.LogError(Status);
            }
            finally { recovering = false; }
        }

        static void SetStatus(string value) { status = value; StatusChanged?.Invoke(); }
    }
}
#endif
