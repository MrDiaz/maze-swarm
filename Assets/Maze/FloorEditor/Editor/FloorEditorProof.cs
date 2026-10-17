#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.FloorEditing.Editor
{
    // Execute explicitly with no -quit; this proof owns and closes its temporary Play session.
    // No floor is saved under Assets. Runtime consumption uses the production MazeMap and transient override.
    [InitializeOnLoad]
    public static class FloorEditorProof
    {
        const string Key = "Maze.FloorEditor.Proof.Active";
        const string Sentinel = "floor-editor.qa.arbitrary-original";
        const string Introduced = "floor-editor.qa.created-during-play";
        static readonly string Out = Environment.GetEnvironmentVariable("MAZE_FLOOR_PROOF_OUTPUT") ?? "Library/MazeFloorEditor/QA/Latest";
        static double enteredAt, deadline;
        static bool observedPlay, requestedStop;
        static int runtimeTicks;
        [Serializable] sealed class Report
        {
            public string utc, unityVersion, preferencesBefore, preferencesAfter, fixture, savedDataFolder;
            public bool preferencesRestored, authoredFilesUnchanged, sceneSetupRestored, baselineFixture;
            public List<string> tests = new(), errors = new();
        }
        [Serializable] sealed class FileHash { public string path, hash; }
        [Serializable] sealed class FileSet { public List<FileHash> files = new(); }
        [Serializable] sealed class SceneRecord { public string path; public bool loaded, active; }
        [Serializable] sealed class Scenes { public SceneRecord[] entries; }
        static Report report;
        static FloorPreferenceSnapshot outerPrefs, playtestPrefs;
        static FileSet authoredBefore;

        static FloorEditorProof()
        {
            if (SessionState.GetBool(Key, false))
            {
                LoadState(); Hook();
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += FinishAfterPlay;
            }
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || FloorPlaytest.Active) throw new InvalidOperationException("Start the floor proof outside Play mode.");
            Directory.CreateDirectory(Out);
            report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            outerPrefs = FloorPreferenceProtection.Capture();
            report.preferencesBefore = FloorPreferenceProtection.Digest(outerPrefs);
            File.WriteAllText(Path.Combine(Out, "preferences-before.private.json"), JsonUtility.ToJson(outerPrefs, true));
            authoredBefore = SnapshotAuthored();
            File.WriteAllText(Path.Combine(Out, "authored-before.json"), JsonUtility.ToJson(authoredBefore, true));
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + ".stage", 0);
            SessionState.SetBool(Key + ".OriginalSim", Progress.Sim);
            SessionState.SetBool(Key + ".OriginalCryoEnabled", CryoSoftEnvironment.Enabled);
            SessionState.SetString(Key + ".OriginalMemory", JsonUtility.ToJson(Progress.D));
            try
            {
                DataTests();
                EditorUndoTests();
                if (Environment.GetEnvironmentVariable("MAZE_FLOOR_PROOF_BASELINE") == "1") BaselineFixture();
                // A batch-owned untitled startup scene is not a user scene. Interactive runs fail closed.
                if (Application.isBatchMode && string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
                    EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity", OpenSceneMode.Single);
                string scenes = SnapshotScenes();
                File.WriteAllText(Path.Combine(Out, "scenes-before.private.json"), scenes);
                PlayerPrefs.SetString(Sentinel, "exact before value ✓"); PlayerPrefs.Save();
                playtestPrefs = FloorPreferenceProtection.Capture();
                Check(playtestPrefs.entries.Any(x => x.key == Sentinel && x.text == "exact before value ✓"), "complete native capture includes a freshly written arbitrary preference immediately");
                File.WriteAllText(Path.Combine(Out, "playtest-preferences.private.json"), JsonUtility.ToJson(playtestPrefs, true));
                SaveReport();
                deadline = EditorApplication.timeSinceStartup + 100; enteredAt = 0; runtimeTicks = 0; observedPlay = false; requestedStop = false;
                SessionState.SetFloat(Key + ".deadline", (float)deadline);
                Hook();
                Check(FloorPlaytest.Begin(LoadFixture(), "cryo-station", 1), "protected playtest accepts the valid fixture: " + FloorPlaytest.Status);
            }
            catch (Exception error) { report.errors.Add(error.ToString()); CleanupAndFinish(); }
        }

        public static void RunDataOnly()
        {
            Directory.CreateDirectory(Out);
            report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            outerPrefs = FloorPreferenceProtection.Capture(); authoredBefore = SnapshotAuthored();
            report.preferencesBefore = FloorPreferenceProtection.Digest(outerPrefs);
            SessionState.SetBool(Key + ".OriginalSim", Progress.Sim);
            SessionState.SetBool(Key + ".OriginalCryoEnabled", CryoSoftEnvironment.Enabled);
            SessionState.SetString(Key + ".OriginalMemory", JsonUtility.ToJson(Progress.D));
            try { DataTests(); EditorUndoTests(); }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            CleanupAndFinish();
        }

        static void DataTests()
        {
            string fixtureDir = Path.GetFullPath(Path.Combine("Library/MazeFloorEditor/QA/Fixtures", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixtureDir); report.savedDataFolder = fixtureDir;
            var original = FloorDocuments.Create("cryo-station/cryo-a", "cryo-station", 1);
            original.objects = Array.Empty<PlacedObject>();
            Check(FloorDocuments.Validate(original).IsValid, "canonical Cryo floor 1 is a valid source for a temporary fixture");
            var legacy = new PlacedObject { x = 10, z = -10, yaw = 90, scale = 1, footprintWidth = 8, footprintDepth = 2, legacyFit = true };
            Check(FloorDocuments.FootprintCells(legacy).Contains(new Vector2Int(3, 4)) && !FloorDocuments.FootprintCells(legacy).Contains(new Vector2Int(4, 3)), "legacy rotated fits retain world-axis collision footprint");
            var legacyCells = FloorDocuments.FootprintCells(legacy); legacy.preserveBehavior = true;
            Check(legacyCells.SequenceEqual(FloorDocuments.FootprintCells(legacy)), "asset behavior toggle cannot change collision fitting");
            legacy.legacyFit = false;
            Check(!FloorDocuments.FootprintCells(legacy).Contains(new Vector2Int(3, 4)) && FloorDocuments.FootprintCells(legacy).Contains(new Vector2Int(4, 3)), "new prop footprints rotate with their models");
            var draft = FloorDocuments.Clone(original);
            draft.title = "QA TEMPORARY — never saved to production Resources";
            var site = FindPlain(draft, null);
            draft.objects = new[] { new PlacedObject {
                id = "qa-solid-crate", name = "QA crate", assetId = "cryo-supply-crate",
                x = (site.x + .5f) * MazeMap.C, z = -(site.y + .5f) * MazeMap.C,
                footprintWidth = 1.6f, footprintDepth = 1.6f, maxHeight = 1.6f, scale = 1, yaw = 90, collision = "solid"
            } };
            var spawn = FindMarker(draft, 'S');
            var spawnTarget = FindPlain(draft, new HashSet<Vector2Int> { site });
            SetCell(draft, spawn, '.'); SetCell(draft, spawnTarget, 'S');
            var zoneTarget = FindPlain(draft, new HashSet<Vector2Int> { site, spawnTarget });
            SetCell(draft, zoneTarget, 'Z');
            Check(FloorDocuments.Validate(draft).IsValid, "sample move/place/spawn/marker edit validates using the production schema");
            string path = Path.Combine(fixtureDir, "qa-cryo-floor-1.json");
            Check(FloorDocuments.TrySaveAtomic(path, original, out var firstError), "initial explicit atomic save succeeds: " + firstError);
            byte[] first = File.ReadAllBytes(path);
            Check(FloorDocuments.TrySaveAtomic(path, draft, out var secondError), "edited explicit atomic save succeeds: " + secondError);
            var backups = Directory.GetFiles(fixtureDir, "*.bak");
            Check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(first), "atomic replace retains the complete prior document in a backup");
            Check(FloorDocuments.TryParse(File.ReadAllText(path), out var reloaded, out var reloadReport), "saved sample reloads through strict parser: " + reloadReport);
            Check(FloorDocuments.ToJson(reloaded, false) == FloorDocuments.ToJson(draft, false), "save/reload retains transforms, asset identity, spawn cells and objective markers");
            report.fixture = path;
            string json = FloorDocuments.ToJson(draft, false);
            Check(!FloorDocuments.TryParse("{\"unknown\":1," + json.Substring(1), out _, out _), "unknown schema properties are rejected");
            Check(!FloorDocuments.TryParse("{\"schemaVersion\":1," + json.Substring(1), out _, out _), "duplicate schema properties are rejected");
            Check(!FloorDocuments.TryParse("{broken", out _, out _), "malformed JSON is rejected");
            var invalid = FloorDocuments.Clone(draft); invalid.schemaVersion = 999;
            Check(!FloorDocuments.TrySaveAtomic(path, invalid, out _), "unsupported schema cannot replace a saved floor");
            Check(File.ReadAllText(path) == json + "\n" || FloorDocuments.TryParse(File.ReadAllText(path), out var intact, out _) && FloorDocuments.ToJson(intact, false) == json, "rejected save leaves the previous floor bytes/data intact");
            string blocker = Path.Combine(fixtureDir, "not-a-directory"); File.WriteAllText(blocker, "preserve me");
            Check(!FloorDocuments.TrySaveAtomic(Path.Combine(blocker, "floor.json"), draft, out var saveError) && !string.IsNullOrWhiteSpace(saveError), "write failure is reported without a false success");
            Check(File.ReadAllText(blocker) == "preserve me", "failed save does not overwrite an existing unrelated file");
            Check(Directory.GetFiles(fixtureDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "failed or completed saves leave no temporary candidate files");
            invalid = FloorDocuments.Clone(draft); invalid.objects[0].scale = float.NaN;
            Check(!FloorDocuments.Validate(invalid).IsValid, "nonfinite transforms fail schema validation");
            invalid = FloorDocuments.Clone(draft); SetCell(invalid, FindMarker(invalid, 'P'), '.');
            Check(!FloorDocuments.Validate(invalid).IsValid, "missing player spawn cannot be saved");
            invalid = FloorDocuments.Clone(draft); var playerCell = FindMarker(invalid, 'P');
            invalid.objects[0].x = (playerCell.x + .5f) * MazeMap.C; invalid.objects[0].z = -(playerCell.y + .5f) * MazeMap.C;
            Check(FloorDocuments.Validate(invalid).issues.Any(x => x.code == "object.marker"), "solid placements cannot overwrite protected player/objective markers");
            invalid = FloorDocuments.Clone(draft); var doorCell = FindPlain(invalid, new HashSet<Vector2Int> { site }); SetCell(invalid, doorCell, 'D');
            Check(FloorDocuments.Validate(invalid).issues.Any(x => x.code.StartsWith("door.", StringComparison.Ordinal)), "unsupported free-floating doors fail wall-anchor or passage validation");
            var oldContext = FloorDocuments.RuntimeSectorId; int oldFloor = FloorDocuments.RuntimeFloorNumber, oldMap = MazeMap.MapIndex;
            try
            {
                FloorDocuments.SetRuntimeContext("cryo-station", 1);
                FloorDocuments.SetPlaytestOverride(reloaded);
                MazeMap.MapIndex = MazeMap.ZoneLayoutIndex(reloaded.layoutId); MazeMap.Init();
                Check(MazeMap.Rows.SequenceEqual(FloorDocuments.BuildRows(reloaded)), "production MazeMap consumes reloaded draft rows through its runtime hook");
                Check(MazeMap.grid[site.y, site.x] == 'C' && !MazeMap.Walkable(site.x, site.y), "placed solid object's authored footprint becomes real runtime collision");
                Check(MazeMap.spawnPoints.Contains(MazeMap.Center(spawnTarget.x, spawnTarget.y)), "moved enemy spawn reaches the real runtime spawn collection");
                Check(MazeMap.zoneSpots.Contains(MazeMap.Center(zoneTarget.x, zoneTarget.y)), "authored objective marker reaches the real runtime objective collection");
            }
            finally
            {
                FloorDocuments.ClearPlaytestOverride();
                if (string.IsNullOrEmpty(oldContext)) FloorDocuments.ClearRuntimeContext(); else FloorDocuments.SetRuntimeContext(oldContext, oldFloor);
                MazeMap.MapIndex = oldMap; MazeMap.Init();
            }
            Check(!FloorDocuments.PlaytestActive, "temporary runtime override is removed after data consumption checks");
        }

        static void EditorUndoTests()
        {
            // Create an independent window instance; never reuse or modify a user's open draft.
            var window = ScriptableObject.CreateInstance<FloorEditorWindow>();
            try
            {
                window.CreateGUI(); window.Load("cryo-station", 1);
                string initial = FloorDocuments.ToJson(window.Document, false);
                window.Edit(d => { d.title = "QA undo test"; if (d.objects.Length > 0) { d.objects[0].x += .5f; d.objects[0].yaw += 15f; } });
                string edited = FloorDocuments.ToJson(window.Document, false);
                Check(edited != initial && window.Dirty, "actual editor document edit changes the draft and marks it dirty");
                window.UndoEdit();
                Check(FloorDocuments.ToJson(window.Document, false) == initial, "actual editor Undo restores prior title and object transforms");
                window.RedoEdit();
                Check(FloorDocuments.ToJson(window.Document, false) == edited, "actual editor Redo restores the complete edit");
                window.UndoEdit();
                window.Edit(d => d.title = "QA undo branch");
                string branch = FloorDocuments.ToJson(window.Document, false); window.RedoEdit();
                Check(FloorDocuments.ToJson(window.Document, false) == branch, "new edits after Undo discard the obsolete redo branch");
                window.UndoEdit();
                Check(FloorDocuments.ToJson(window.Document, false) == initial, "temporary QA editor draft is restored before disposal");
            }
            finally { Object.DestroyImmediate(window); }
        }

        static void BaselineFixture()
        {
            var baseline = FloorDocuments.Create("cryo-station/cryo-a", "cryo-station", 1);
            baseline.title = "QA TEMPORARY BASELINE — original approved room and preserved object behaviors";
            Check(FloorDocuments.Validate(baseline).IsValid && !FloorDocuments.RowsDifferFromOriginal(baseline), "unchanged-layout fixture preserves the approved authored geometry");
            Check(baseline.objects.Length > 0 && baseline.objects.All(x => x.preserveBehavior), "baseline fixture retains original integrated props and their behavior ownership");
            string path = Path.Combine(report.savedDataFolder, "qa-cryo-floor-1-original-geometry.json");
            Check(FloorDocuments.TrySaveAtomic(path, baseline, out var error), "temporary baseline fixture saves outside production Resources: " + error);
            report.fixture = path; report.baselineFixture = true;
        }

        static void Hook() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false)) return;
            try
            {
                if (deadline <= 0) deadline = SessionState.GetFloat(Key + ".deadline", 0);
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Protected playtest proof timed out: " + FloorPlaytest.Status);
                if (!EditorApplication.isPlaying) return;
                var game = Object.FindAnyObjectByType<Game>();
                if (game == null || !game.run || game.floor != 1 || !Game.TestRun) return;
                if (!observedPlay)
                {
                    observedPlay = true; enteredAt = EditorApplication.timeSinceStartup;
                    Check(FloorPlaytest.Active && FloorDocuments.PlaytestActive && Progress.Sim, "safe playtest enters the selected authored floor using a transient draft");
                    Check(MazeMap.Rows.SequenceEqual(FloorDocuments.BuildRows(LoadFixture())), "actual playing Game consumes the current unsaved draft");
                    if (report.baselineFixture)
                    {
                        Check(CryoSoftSceneInfo.VisualsActive, "unchanged-layout playtest uses the approved rounded Cryo room");
                        int expected = LoadFixture().objects.Count(x => x.preserveBehavior && (x.assetId == "cryo-gun-turret" || x.assetId == "cryo-flamethrower-turret"));
                        int actual = CryoHeroTurret.Live.Count(x => x != null && x.isActiveAndEnabled);
                        Check(expected > 0 && actual == expected, "approved-room playtest preserves exactly " + expected + " original ally turrets with no duplicates");
                    }
                    string saved = PlayerPrefs.GetString("ms.progress", "");
                    Progress.D.credits += 123456; Progress.D.cleared.Add("qa-never-persist"); Progress.Save();
                    Check(PlayerPrefs.GetString("ms.progress", "") == saved, "playtest progress edits never write the real progress key");
                    PlayerPrefs.SetString(Sentinel, "changed in play"); PlayerPrefs.SetFloat(Introduced, 3.75f); PlayerPrefs.Save();
                    Check(PlayerPrefs.GetString(Sentinel) != "exact before value ✓" && PlayerPrefs.HasKey(Introduced), "fixture exercises changed and newly created arbitrary preference keys");
                    SaveReport();
                }
                runtimeTicks++;
                if (!requestedStop && runtimeTicks >= 10 && EditorApplication.timeSinceStartup - enteredAt > 1.5)
                {
                    Check(game.sector.id == "cryo-station" && game.floor == 1, "runtime remains on the selected floor while normal gameplay updates run");
                    requestedStop = true; SessionState.SetInt(Key + ".stage", 1); SaveReport();
                    // Exercise the same manual Stop action available to an author.
                    FloorPlaytest.Stop();
                    EditorApplication.delayCall += FinishAfterPlay;
                }
            }
            catch (Exception error)
            {
                report.errors.Add(error.ToString()); SaveReport();
                if (EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.SetInt(Key + ".stage", 1); FloorPlaytest.Stop(); EditorApplication.delayCall += FinishAfterPlay; }
                else CleanupAndFinish();
            }
        }

        static void FinishAfterPlay()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += FinishAfterPlay; return; }
            if (FloorPlaytest.Active)
            {
                // A failed recovery retains the private backup. Report and terminate the batch
                // instead of spinning forever and hiding the recovery problem from the owner.
                LoadState();
                report.errors.Add("Protected playtest recovery failed: " + FloorPlaytest.Status);
                SessionState.SetBool(Key, false); EditorApplication.update -= Tick; SaveReport();
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            try
            {
                LoadState();
                Check(SessionState.GetInt(Key + ".stage", 0) == 1, "playtest reached runtime checks and its manual stop path");
                Check(FloorPreferenceProtection.Digest(FloorPreferenceProtection.Capture()) == FloorPreferenceProtection.Digest(playtestPrefs), "complete arbitrary-key preference snapshot is restored after manual Stop");
                Check(PlayerPrefs.GetString(Sentinel) == "exact before value ✓" && !PlayerPrefs.HasKey(Introduced), "changed key values and newly introduced key presence are restored");
                var expected = File.ReadAllText(Path.Combine(Out, "scenes-before.private.json"));
                report.sceneSetupRestored = expected == SnapshotScenes();
                Check(report.sceneSetupRestored, "original loaded/active scene setup is restored after playtest");
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            CleanupAndFinish();
        }

        static void LoadState()
        {
            if (report == null && File.Exists(Path.Combine(Out, "report.json"))) report = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Out, "report.json")));
            if (outerPrefs == null && File.Exists(Path.Combine(Out, "preferences-before.private.json"))) outerPrefs = JsonUtility.FromJson<FloorPreferenceSnapshot>(File.ReadAllText(Path.Combine(Out, "preferences-before.private.json")));
            if (playtestPrefs == null && File.Exists(Path.Combine(Out, "playtest-preferences.private.json"))) playtestPrefs = JsonUtility.FromJson<FloorPreferenceSnapshot>(File.ReadAllText(Path.Combine(Out, "playtest-preferences.private.json")));
            if (authoredBefore == null && File.Exists(Path.Combine(Out, "authored-before.json"))) authoredBefore = JsonUtility.FromJson<FileSet>(File.ReadAllText(Path.Combine(Out, "authored-before.json")));
        }
        static FloorDocument LoadFixture()
        {
            LoadState();
            if (!FloorDocuments.TryParse(File.ReadAllText(report.fixture), out var fixture, out var validation)) throw new InvalidDataException("Fixture rejected: " + validation);
            return fixture;
        }
        static void CleanupAndFinish()
        {
            try
            {
                FloorDocuments.ClearPlaytestOverride();
                if (outerPrefs != null)
                {
                    FloorPreferenceProtection.Restore(outerPrefs);
                    report.preferencesAfter = FloorPreferenceProtection.Digest(FloorPreferenceProtection.Capture());
                    report.preferencesRestored = report.preferencesAfter == FloorPreferenceProtection.Digest(outerPrefs);
                    Check(report.preferencesRestored, "all original game preferences and progress are restored after the complete proof");
                }
                report.authoredFilesUnchanged = authoredBefore == null || JsonUtility.ToJson(authoredBefore) == JsonUtility.ToJson(SnapshotAuthored());
                Check(report.authoredFilesUnchanged, "production authored Resources files remain byte-for-byte unchanged");
                Check(CryoSoftEnvironment.Enabled == SessionState.GetBool(Key + ".OriginalCryoEnabled", false), "original Editor Cryo environment toggle is restored after playtest");
                var memory = SessionState.GetString(Key + ".OriginalMemory", "");
                if (!string.IsNullOrEmpty(memory)) Progress.UseTemp(JsonUtility.FromJson<SaveData>(memory));
                Progress.Sim = SessionState.GetBool(Key + ".OriginalSim", false);
            }
            catch (Exception error) { report.errors.Add("Final cleanup: " + error); }
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; SaveReport();
            Debug.Log("FloorEditorProof: " + report.tests.Count + " checks; " + report.errors.Count + " errors. " + Path.GetFullPath(Path.Combine(Out, "report.json")));
            if (Application.isBatchMode) EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
        }
        static FileSet SnapshotAuthored()
        {
            var set = new FileSet(); string root = "Assets/Maze/FloorEditor/Resources";
            if (!Directory.Exists(root)) return set;
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                using var sha = SHA256.Create();
                set.files.Add(new FileHash { path = path, hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() });
            }
            return set;
        }
        static string SnapshotScenes() => JsonUtility.ToJson(new Scenes { entries = EditorSceneManager.GetSceneManagerSetup().Select(x => new SceneRecord { path = x.path, loaded = x.isLoaded, active = x.isActive }).ToArray() });
        static Vector2Int FindMarker(FloorDocument doc, char marker)
        {
            for (int y = 0; y < doc.rows.Length; y++) { int x = doc.rows[y].IndexOf(marker); if (x >= 0) return new Vector2Int(x, y); }
            throw new InvalidDataException("Missing test marker " + marker);
        }
        static Vector2Int FindPlain(FloorDocument doc, HashSet<Vector2Int> excluded)
        {
            for (int y = 2; y < doc.rows.Length - 2; y++) for (int x = 2; x < doc.rows[y].Length - 2; x++)
            {
                var at = new Vector2Int(x, y);
                if (doc.rows[y][x] == '.' && (excluded == null || !excluded.Contains(at))) return at;
            }
            throw new InvalidDataException("No plain fixture cell is available.");
        }
        static void SetCell(FloorDocument doc, Vector2Int cell, char value) { var chars = doc.rows[cell.y].ToCharArray(); chars[cell.x] = value; doc.rows[cell.y] = new string(chars); }
        static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); report.tests.Add("PASS " + message); }
        static void SaveReport() { Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "report.json"), JsonUtility.ToJson(report, true)); }
    }
}
#endif
