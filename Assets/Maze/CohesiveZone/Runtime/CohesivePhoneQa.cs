using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Maze
{
    // Explicit launch-only QA. Inert without COHESIVE_QA=1. No Editor dependencies.
    // This drives production UI handlers and the existing demo movement brain, not OS touches.
    [Preserve, DefaultExecutionOrder(-32000)]
    public sealed class CohesivePhoneQa : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static bool SaveProtected => Active && Progress.Sim;
        static bool SelectionOnly => Environment.GetEnvironmentVariable("COHESIVE_QA_MODE") == "hero-select";
        public static bool ExternalReview => Active && Application.isEditor && Environment.GetEnvironmentVariable("COHESIVE_QA_MODE") == "editor-review";
        static bool originalSim, originalGameSim;
        static string originalData, originalProgressString, bootError;
        static Preferences preferences;
        static CohesivePhoneQa instance;

        [Serializable] public sealed class Preference
        { public string key, type, text; public bool present; public int integer; public float number; }
        [Serializable] public sealed class Preferences { public List<Preference> entries = new(); }
        [Serializable] public sealed class Event
        { public string stage, utc, state, sector, note; public float realtime, runTime, x, y; public int floor, wave, kills, level, enemies; }
        [Serializable] public sealed class FrameReport
        {
            public int samples, targetFps, peakEnemies, width, height, screenshotCountDuringRun;
            public float warmupPlayingSeconds, measuredSeconds, meanFps, medianMs, p95Ms, p99Ms, worstMs, distanceMoved;
            public bool captureFramerateZero, includesScreenshotOverhead;
        }
        [Serializable] public sealed class Report
        {
            public string schema = "cohesive-phone-qa-v1", utc, unityVersion, platform, deviceModel, outputDirectory;
            public string requestedZone, requestedHero, selectedHero, gameplayHeroResource, progressBeforeSha256, progressAfterSha256, preferenceSnapshotSha256;
            public bool selectionPersisted, unselectedPreferencesRestored;
            public string conditions = "Opt-in runtime proof; original SaveData deep-cloned before startup; Progress.Sim guard; production UI pointer handlers/raycasts; existing demo movement; god mode; natural enemies/upgrades; no forced wins, kills, floors, cooldowns or capture clock; not physical touch input.";
            public bool completed, passed, guardInstalledBeforeScene, preferencesRestored, progressStringUnchanged, returnedToHangar;
            public bool naturalResultSeen, naturalVictory, capturesEnabled, originalTestMode;
            public int choicesClicked, maximumFloor, maximumWave, totalKillsBeforeExit, blockedCenterSamples, naturalFloorTransitions;
            public float goalProgressBeforeExit, gameplaySecondsBeforeExit;
            public FrameReport performance = new();
            public List<string> checks = new(), errors = new(), warnings = new(), captures = new();
            public List<Event> events = new();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ProtectBeforeStartup()
        {
            Active = Environment.GetEnvironmentVariable("COHESIVE_QA") == "1";
            instance = null; bootError = null;
            if (!Active) return;
            originalSim = Progress.Sim;
            originalGameSim = Game.SimRun;
            Progress.Sim = true; // Before HeavyRoster.BeforeSceneLoad and Game/menu startup.
            try
            {
                preferences = SnapshotPreferences();
                originalProgressString = PlayerPrefs.GetString("ms.progress", "");
                originalData = JsonUtility.ToJson(Progress.D);
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalData));
                Game.SimRun = false; // Keep production visuals/audio and normal real-time gameplay.
                // Game.TestEnd would otherwise clear Progress.Sim when a stored test run starts/ends.
                PlayerPrefs.SetInt("ms.test.mode", 0);
                PlayerPrefs.Save();
                foreach (string name in new[] { "MAZE_PERF", "MAZE_MAPSELECT", "MAZE_SANDBOX" })
                    if (Environment.GetEnvironmentVariable(name) == "1")
                        throw new InvalidOperationException("Conflicting launch variable " + name + "; launch this proof with only COHESIVE_QA variables.");
            }
            catch (Exception error) { bootError = error.ToString(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!Active || instance != null) return;
            var root = new GameObject("CohesivePhoneQa (launch opt-in)");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<CohesivePhoneQa>();
        }

        Game game;
        Report report;
        string output, stage = "startup";
        bool finishing, finished, gameplay, oldDemo, oldGod, capturedWave;
        bool samplePosition, capturesEnabled;
        Vector2 lastPosition;
        int lastFloor, framesSinceCollisionSample, choices, capturesDuringRun;
        float warmup, measuredSeconds, gameplaySeconds, moved, nextCheckpoint, choiceSince = -1;
        readonly List<float> frameSeconds = new(20000);
        readonly List<int> frameEnemyCounts = new(20000);
        readonly List<int> frameFloors = new(20000);
        int peakEnemies;
        float duration;

        void Awake()
        {
            if (!Active) { Destroy(gameObject); return; }
            capturesEnabled = Environment.GetEnvironmentVariable("COHESIVE_QA_CAPTURES") != "0";
            duration = 75;
            if (float.TryParse(Environment.GetEnvironmentVariable("COHESIVE_QA_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                duration = Mathf.Clamp(seconds, 15, capturesEnabled ? 600 : 180);
            output = Path.Combine(Application.persistentDataPath, "CohesiveQA", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), deviceModel = SystemInfo.deviceModel,
                outputDirectory = output, capturesEnabled = capturesEnabled,
                requestedZone = Environment.GetEnvironmentVariable("COHESIVE_QA_ZONE") ?? "cryo-station",
                requestedHero = Environment.GetEnvironmentVariable("COHESIVE_QA_HERO") ?? Prefs.Hero,
                progressBeforeSha256 = Hash(originalProgressString ?? ""),
                guardInstalledBeforeScene = Progress.Sim && originalData != null,
                originalTestMode = preferences != null && preferences.entries.Any(p => p.key == "ms.test.mode" && p.integer == 1)
            };
            if (preferences != null)
            {
                string snapshot = JsonUtility.ToJson(preferences, true);
                File.WriteAllText(Path.Combine(output, "playerprefs-before.private.json"), snapshot);
                report.preferenceSnapshotSha256 = Hash(snapshot);
            }
            Application.logMessageReceived += Logged;
            Mark("startup", "Early temporary-save guard installed; no user progression will be written by Progress.Save.");
            if (!ExternalReview) StartCoroutine(Guarded(Drive()));
        }

        // The Editor wrapper owns its own fixtures/captures but shares the same earliest save guard.
        public static void FinishExternalReview(bool success, string error = null)
        {
            if (!ExternalReview || instance == null) throw new InvalidOperationException("No protected external Editor review is active.");
            instance.game = Object.FindAnyObjectByType<Game>();
            if (!success) instance.report.errors.Add(error ?? "External Editor review failed.");
            instance.report.returnedToHangar = instance.game != null && instance.game.state == Game.State.Menu;
            instance.Finish();
        }

        IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                object current = null; bool hasNext = false; Exception failed = null;
                try { hasNext = stack.Peek().MoveNext(); if (hasNext) current = stack.Peek().Current; }
                catch (Exception error) { failed = error; }
                if (failed != null) { Fail(failed.ToString()); yield break; }
                if (!hasNext) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator child) { stack.Push(child); continue; }
                yield return current;
            }
        }

        IEnumerator Drive()
        {
            Require(bootError == null, "Early startup succeeded: " + bootError);
            Require(report.guardInstalledBeforeScene, "Temporary save protected before scene startup");
            Require(Time.captureFramerate == 0 && Time.captureDeltaTime == 0, "No artificial capture clock");
            yield return Until(() => (game = Object.FindAnyObjectByType<Game>()) != null && game.menu != null && game.hud != null && game.runUi != null, 45, "Game/menu boot");
            oldDemo = game.demo; oldGod = game.godMode;
            var title = game.menu.GetComponent<TitleScreenView>();
            Require(title != null, "Production title component exists");
            yield return Until(() => title.Visible && !game.menu.Busy, 15, "Title visible");
            yield return Settle();
            yield return Capture("01-title");
            Click(title.PlayButton, "Title Play");
            yield return Until(() => game.menu.Presentation != null && game.menu.Presentation.Visible && !title.Visible, 12, "Hangar visible after Play");
            yield return Settle();
            Require(game.menu.Presentation.Current == MenuPresentation.Screen.Home, "Title Play reaches Hangar Home");
            Require(Prefs.IsPlayableHero(report.requestedHero), "Requested selectable hero exists");
            if (Environment.GetEnvironmentVariable("COHESIVE_QA_MODE") == "hero-reload")
            {
                Require(PlayerPrefs.GetString("ms.hero", "") == report.requestedHero && Prefs.Hero == report.requestedHero && game.player.HeroId == report.requestedHero && game.player.HeroAnimationReady, "New process loads the persisted selected hero before any card selection");
                report.selectedHero = Prefs.Hero; report.gameplayHeroResource = CohesiveCharacterPolicy.HeroResource;
                report.returnedToHangar = true;
                Mark("hero-reload", "New process read the saved hero and built its actual model; no selection callback or preference write preceded this check.");
                yield return Capture("02-hero-reloaded-" + report.selectedHero);
                Finish(); yield break;
            }
            // The guarded clone makes the existing Heroes route available without writing a first run.
            Progress.D.runs = Mathf.Max(1, Progress.D.runs);
            Click(game.menu.Presentation.HeroesButton, "Open Heroes");
            yield return Settle();
            Click(FindButton(game.menu.Presentation, "Hero_" + report.requestedHero), "Select " + report.requestedHero);
            yield return Settle();
            Require(Prefs.Hero == report.requestedHero && game.player.HeroId == report.requestedHero && game.player.HeroAnimationReady, "Card selects the actual ready gameplay model");
            Require(Meta.HeroId == "vanguard" && Prefs.Vanguard, "Existing Heavy combat/progression baseline retained");
            report.selectedHero = Prefs.Hero;
            report.gameplayHeroResource = CohesiveCharacterPolicy.HeroResource;
            yield return Capture("02a-heroes-" + report.selectedHero);
            Click(FindButton(game.menu.Presentation, "Back"), "Return from Heroes");
            yield return Settle();
            Mark("hangar", "Reached through production title Play and hero card pointer handlers.");
            yield return Capture("02-hangar");
            if (SelectionOnly)
            {
                report.returnedToHangar = game.menu.Presentation.Current == MenuPresentation.Screen.Home;
                Mark("hero-selection", "Intentional saved hero choice only; original progression/other preferences are restored; process relaunch is verified separately by host.");
                Finish(); yield break;
            }
            Click(game.menu.Presentation.DeployButton, "Hangar Deploy");
            yield return Until(() => MapSelect.I != null && MapSelect.I.Open && !MapSelect.I.Dropping && MapSelect.I.StyledBriefButton.IsActive(), 12, "Drop selector");
            yield return Settle();
            var map = MapSelect.I;
            int target = Array.FindIndex(MapSelect.Sectors, s => s.def != null && s.def.id == report.requestedZone);
            Require(target >= 0, "Requested actual zone exists: " + report.requestedZone);
            while (map.StyledZonePage > 0) { Click(FindButton(map, "Previous"), "Previous zone page"); yield return Settle(.25f); }
            int card = -1;
            for (int page = 0; page < map.StyledZonePageCount; page++)
            {
                for (int i = 0; i < 3; i++) if (map.StyledVisibleZoneIndex(i) == target) card = i;
                if (card >= 0) break;
                Require(page + 1 < map.StyledZonePageCount, "Requested zone reachable through actual pagination");
                Click(FindButton(map, "Next"), "Next zone page"); yield return Settle(.25f);
            }
            Click(FindButton(map, "ZoneCard" + card), "Select requested zone card");
            yield return Settle();
            Require(MapSelect.Selected == target, "Actual zone card callback selects requested zone");
            Require(!MapSelect.Sectors[target].locked, "Requested zone unlocked by preserved player progression");
            Mark("selector", MapSelect.Sectors[target].name);
            yield return Capture("03-zone-selected");
            Click(map.StyledBriefButton, "Open mission briefing");
            yield return Until(() => map.StyledDeployButton.IsActive(), 10, "Briefing visible");
            yield return Settle();
            Mark("briefing", "Actual selector Deploy callback opened briefing.");
            yield return Capture("04-briefing");
            Click(map.StyledDeployButton, "Briefing Deploy");
            yield return Until(() => game.state == Game.State.Playing && game.run && !game.menu.Busy && !MapSelect.I.Open, 25, "Drop transition reaches gameplay");
            Require(game.floor == 1 && game.sector != null && game.sector.id == report.requestedZone, "Production drop launches requested zone floor one");
            Require(!Game.TestRun && Progress.Sim, "Test-run cleanup cannot clear QA save protection");
            InspectLiveMaterials();
            game.godMode = true; game.demo = true;
            gameplay = true; lastFloor = game.floor;
            Mark("run-start", "Existing movement/combat demo brain enabled; god mode; no state or floor injection.");
            yield return Settle(1);
            yield return Capture("05-run-start");
            float deadline = Time.realtimeSinceStartup + duration * 3 + 90;
            while (measuredSeconds < duration)
            {
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("Bounded gameplay measurement did not complete.");
                if (!Progress.Sim) throw new InvalidOperationException("Progress.Sim cleared during gameplay.");
                if (game.state == Game.State.Won || game.state == Game.State.Dead)
                {
                    report.naturalResultSeen = true; report.naturalVictory = game.state == Game.State.Won;
                    Mark("natural-result", "Reached by normal game logic; not forced by QA.");
                    yield return Settle(1.2f); yield return Capture("08-natural-result"); break;
                }
                if (game.state == Game.State.Choosing)
                {
                    game.demo = false; // Stop AutoPick's .35s direct choice; use the real visible UI instead.
                    if (choiceSince < 0)
                    {
                        choiceSince = Time.realtimeSinceStartup;
                        Mark("choice", "Natural offer: " + string.Join(",", game.offer.Select(c => c.id)));
                    }
                    if (Time.realtimeSinceStartup - choiceSince >= .8f)
                    {
                        if (choices < 2) yield return Capture("06-natural-choice-" + choices);
                        var clear = game.runUi.GetComponentsInChildren<BlueFloorClearView>(false).FirstOrDefault(v => v.Visible);
                        if (clear != null)
                        {
                            Click(clear.ContinueButton, "Natural floor clear Continue"); yield return Settle(.4f);
                        }
                        Require(game.offer.Count > 0, "Natural choice has at least one real offer");
                        int best = BestOffer();
                        var cardRoot = game.runUi.GetComponentsInChildren<Transform>(false).First(t => t.name == "Card" + best && t.GetComponent<Button>() != null);
                        Click(FindButton(cardRoot, "Choose"), "Natural choice " + game.offer[best].id);
                        choices++; report.choicesClicked = choices; choiceSince = -1;
                        game.demo = true;
                    }
                }
                else { choiceSince = -1; game.demo = true; }
                if (!capturedWave && peakEnemies > 0 && gameplaySeconds >= 5)
                {
                    capturedWave = true; Mark("wave-active", "Naturally spawned enemies with real movement/combat.");
                    yield return Capture("07-active-wave");
                }
                yield return null;
            }
            gameplay = false;
            report.totalKillsBeforeExit = game.kills;
            report.gameplaySecondsBeforeExit = game.RunTime;
            report.goalProgressBeforeExit = game.GoalProgress;
            Require(moved >= 3, "Production movement brain moved player at least three metres");
            Require(peakEnemies > 0, "Natural gameplay spawned live enemies");
            Require(game.kills > 0, "Natural production combat killed enemies");
            if (choices == 0) report.warnings.Add("No natural upgrade reached within this bounded run; upgrade interaction remains unverified.");
            if (!report.naturalResultSeen) report.warnings.Add("Bounded run ended through normal confirmed Hangar exit; a complete zone victory was not established.");
            Require(report.blockedCenterSamples == 0, "Sampled hero centre remained on walkable grid cells");
            game.demo = false;
            if (report.naturalResultSeen)
            {
                var results = game.runUi.GetComponentsInChildren<RunResultsView>(false).FirstOrDefault(v => v.Visible);
                Require(results != null, "Natural result has production results UI");
                Click(results.MenuButton, "Results Hangar");
            }
            else
            {
                ClickHolo(game.hud.GetComponentsInChildren<HoloButton>(false).First(b => b.name == "Pause" && b.enabled), "Gameplay Pause");
                yield return Until(() => game.state == Game.State.Paused, 3, "Paused");
                yield return Settle();
                Click(FindButton(game.hud, "Hangar"), "Pause Hangar");
                yield return Settle();
                yield return Capture("09-exit-confirmation");
                Click(FindButton(game.hud, "Leave"), "Confirm Leave to Hangar");
            }
            yield return Until(() => game.state == Game.State.Menu && !game.menu.Busy && game.menu.Presentation.Visible, 15, "Return to Hangar");
            yield return Settle();
            report.returnedToHangar = game.menu.Presentation.Current == MenuPresentation.Screen.Home;
            Require(report.returnedToHangar, "Confirmed exit settles on production Hangar Home");
            Mark("returned-hangar", "Actual exit callbacks finished; restoring original in-memory data and preferences.");
            yield return Capture("10-returned-hangar");
            Finish();
        }

        void LateUpdate()
        {
            if (finished || report == null) return;
            if (!Progress.Sim) { Fail("Progress.Sim was unexpectedly cleared; stopping QA."); return; }
            if (gameplay && game != null && game.player != null)
            {
                report.maximumFloor = Mathf.Max(report.maximumFloor, game.floor);
                report.maximumWave = Mathf.Max(report.maximumWave, game.wave);
                if (game.floor != lastFloor)
                {
                    report.naturalFloorTransitions++; lastFloor = game.floor; samplePosition = false;
                    Mark("floor-transition", "Reached naturally; movement distance excludes floor-spawn relocation.");
                }
                if (game.state == Game.State.Choosing) game.demo = false;
                if (game.state == Game.State.Playing)
                {
                    float dt = Time.unscaledDeltaTime; gameplaySeconds += dt;
                    int enemies = game.swarm.enemies.Count(e => !e.dead); peakEnemies = Mathf.Max(peakEnemies, enemies);
                    if (samplePosition) moved += Vector2.Distance(lastPosition, game.player.p);
                    lastPosition = game.player.p; samplePosition = true;
                    if (++framesSinceCollisionSample >= 30)
                    {
                        framesSinceCollisionSample = 0;
                        if (!MazeMap.Walkable(Mathf.FloorToInt(game.player.p.x / MazeMap.C), Mathf.FloorToInt(game.player.p.y / MazeMap.C))) report.blockedCenterSamples++;
                    }
                    if (warmup < 6) warmup += dt;
                    else { frameSeconds.Add(dt); frameEnemyCounts.Add(enemies); frameFloors.Add(game.floor); measuredSeconds += dt; }
                }
                if (Time.realtimeSinceStartup >= nextCheckpoint)
                {
                    nextCheckpoint = Time.realtimeSinceStartup + 10;
                    Mark("gameplay-checkpoint", "Measurement seconds=" + measuredSeconds.ToString("F1", CultureInfo.InvariantCulture));
                }
            }
        }

        int BestOffer()
        {
            int best = 0; float value = -1;
            for (int i = 0; i < game.offer.Count; i++)
            {
                string id = game.offer[i].id;
                float score = game.offer[i].evo ? 100 : id == "rate" || id == "multi" ? 9 : id == "storm" || id == "heavy" || id == "pierce" ? 8 : id == "cache_card" ? 20 : id == "cache_credits" ? 10 : 4;
                if (score > value) { best = i; value = score; }
            }
            return best;
        }

        IEnumerator Until(Func<bool> condition, float limit, string description)
        {
            float deadline = Time.realtimeSinceStartup + limit;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), description + " within " + limit + "s");
        }
        static IEnumerator Settle(float seconds = .7f) { yield return new WaitForSecondsRealtime(seconds); yield return new WaitForEndOfFrame(); }
        IEnumerator Capture(string name)
        {
            if (!capturesEnabled) yield break;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(output, name + ".png");
            // The project does not enable Unity's ScreenCapture module. At EndOfFrame,
            // the native backbuffer includes ScreenSpaceOverlay UI as well as the 3D view.
            // Explicitly select that backbuffer, then restore any previous RT binding.
            var previousTarget = RenderTexture.active;
            Texture2D capture = null;
            try
            {
                RenderTexture.active = null;
                capture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
            }
            finally
            {
                if (capture != null) Object.Destroy(capture);
                RenderTexture.active = previousTarget;
            }
            yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 0, 10, "Screenshot saved: " + name);
            report.captures.Add(path); if (gameplay) capturesDuringRun++;
            SaveReport();
        }
        static Button FindButton(Component owner, string name)
        {
            var found = owner.GetComponentsInChildren<Button>(false).Where(b => b.name == name && b.IsActive() && b.IsInteractable()).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Expected one active button " + name + "; found " + found.Length);
            return found[0];
        }
        void Click(Button button, string label)
        {
            Require(button != null && button.IsActive() && button.IsInteractable(), label + " active and interactable");
            var pointer = Pointer(button, label);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Mark("ui", label + " through actual pointer handler");
        }
        void ClickHolo(HoloButton button, string label)
        {
            Require(button != null && button.isActiveAndEnabled && button.interactable && !button.manual, label + " active Holo callback");
            var pointer = Pointer(button, label);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Mark("ui", label + " through actual Holo pointer handler");
        }
        PointerEventData Pointer(Component button, string label)
        {
            Canvas.ForceUpdateCanvases();
            var rt = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Rect bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var safe = Screen.safeArea;
            Require(bounds.xMin >= safe.xMin - 2 && bounds.xMax <= safe.xMax + 2 && bounds.yMin >= safe.yMin - 2 && bounds.yMax <= safe.yMax + 2, label + " target contained in actual safe area");
            Require(EventSystem.current != null, label + " EventSystem exists");
            var pointer = new PointerEventData(EventSystem.current) { position = bounds.center, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            var first = hits.FirstOrDefault(h => ExecuteEvents.GetEventHandler<IPointerClickHandler>(h.gameObject) != null);
            Require(first.gameObject != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(first.gameObject) == button.gameObject, label + " centre raycast reaches intended production button");
            return pointer;
        }
        void InspectLiveMaterials()
        {
            int meshes = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                meshes++;
                foreach (var material in renderer.sharedMaterials)
                    Require(material != null && material.shader != null && material.shader.isSupported, "Supported material on " + renderer.name);
            }
            Require(meshes > 0, "Gameplay has active 3D renderers");
            Mark("materials", meshes + " enabled scene renderers inspected");
        }
        void Require(bool condition, string message)
        {
            report.checks.Add((condition ? "PASS " : "FAIL ") + message);
            if (!condition) throw new InvalidOperationException(message);
        }
        void Mark(string name, string note)
        {
            stage = name;
            var e = new Event { stage = name, note = note, utc = DateTime.UtcNow.ToString("O"), realtime = Time.realtimeSinceStartup };
            if (game != null)
            {
                e.state = game.state.ToString(); e.sector = game.sector?.id; e.floor = game.floor; e.wave = game.wave;
                e.kills = game.kills; e.level = game.level; e.runTime = game.RunTime;
                if (game.player != null) { e.x = game.player.p.x; e.y = game.player.p.y; }
                if (game.swarm != null) e.enemies = game.swarm.enemies.Count(x => !x.dead);
            }
            report.events.Add(e); SaveReport();
            Debug.Log("CohesivePhoneQa: " + name + " — " + note);
        }
        void Logged(string message, string stack, LogType type)
        {
            if (finished || report == null) return;
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 50)
                report.errors.Add(stage + ": " + message + "\n" + stack);
        }
        void Fail(string error)
        {
            if (finished || finishing) return;
            if (report != null) report.errors.Add(error);
            Finish();
        }
        void Finish()
        {
            if (finished || finishing) return;
            finishing = true; gameplay = false;
            try
            {
                Progress.Sim = true;
                try
                {
                    if (game != null)
                    {
                    game.demo = false;
                    if (game.state != Game.State.Menu) game.ToMenu();
                    Progress.Sim = true; // TestEnd is not expected, but cannot leak the guard during cleanup.
                    game.godMode = oldGod; game.demo = oldDemo;
                    }
                }
                catch (Exception error) { report.errors.Add("Game cleanup: " + error); }
                RestorePreferences(preferences);
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalData));
                // Refresh bound menu wallets while still guarded; restore the clone again if Ops refreshed it.
                try { if (game != null && game.menu != null) game.menu.Show(); }
                catch (Exception error) { report.errors.Add("Menu cleanup: " + error); }
                Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalData));
                RestorePreferences(preferences);
                report.progressAfterSha256 = Hash(PlayerPrefs.GetString("ms.progress", ""));
                report.progressStringUnchanged = PlayerPrefs.GetString("ms.progress", "") == originalProgressString;
                if (SelectionOnly && report.errors.Count == 0)
                {
                    // This explicit mode is the user-authorized persistent selection test. It
                    // changes only ms.hero; ordinary QA continues to restore every preference.
                    Prefs.Hero = report.requestedHero;
                    var expected = JsonUtility.FromJson<Preferences>(JsonUtility.ToJson(preferences));
                    var selected = expected.entries.Single(p => p.key == "ms.hero");
                    selected.present = true; selected.text = report.requestedHero;
                    report.selectionPersisted = PlayerPrefs.GetString("ms.hero", "") == report.requestedHero;
                    report.unselectedPreferencesRestored = SamePreferences(expected, SnapshotPreferences());
                }
                if (game != null && game.player != null && game.player.HeroId != Prefs.Hero) game.player.RebuildHero(game.cfg);
                report.preferencesRestored = SamePreferences(preferences, SnapshotPreferences());
                if (!report.progressStringUnchanged) report.errors.Add("Persisted progression changed: preserve external backup; do not silently restore or claim preservation.");
                if (!report.preferencesRestored && !(report.selectionPersisted && report.unselectedPreferencesRestored)) report.errors.Add("Tracked preferences changed outside the explicitly requested saved hero choice.");
                Game.SimRun = originalGameSim;
                Progress.Sim = report.progressStringUnchanged && (report.preferencesRestored || (report.selectionPersisted && report.unselectedPreferencesRestored)) ? originalSim : true;
                report.performance = Performance();
                WriteFrames();
                report.completed = true;
                report.passed = report.errors.Count == 0 && report.returnedToHangar && report.progressStringUnchanged && (report.preferencesRestored || (report.selectionPersisted && report.unselectedPreferencesRestored));
                if (capturesEnabled) report.warnings.Add("Screenshot overhead is included; rerun with COHESIVE_QA_CAPTURES=0 for an uncaptured frame-time measurement.");
                report.warnings.Add("Existing demo movement skips the intermediate floor-clear splash; natural upgrades use actual visible Choose buttons. Physical device touches, complete radius collision and visual/art consistency still need separate review.");
            }
            catch (Exception error)
            {
                report.errors.Add("Cleanup failed: " + error);
                // Fail closed. Never permit subsequent saves from a still-temporary session.
                Progress.Sim = true; report.passed = false;
            }
            finished = true; finishing = false;
            Application.logMessageReceived -= Logged;
            SaveReport();
            Debug.Log("CohesivePhoneQa complete: " + (report.passed ? "PASS" : "FAIL") + "; " + output);
        }
        FrameReport Performance()
        {
            var sorted = frameSeconds.OrderBy(x => x).ToArray();
            return new FrameReport
            {
                samples = sorted.Length, warmupPlayingSeconds = warmup, measuredSeconds = measuredSeconds,
                meanFps = measuredSeconds > 0 ? sorted.Length / measuredSeconds : 0,
                medianMs = Percentile(sorted, .5f), p95Ms = Percentile(sorted, .95f), p99Ms = Percentile(sorted, .99f),
                worstMs = sorted.Length == 0 ? 0 : sorted[sorted.Length - 1] * 1000,
                targetFps = Application.targetFrameRate, peakEnemies = peakEnemies, distanceMoved = moved,
                captureFramerateZero = Time.captureFramerate == 0 && Time.captureDeltaTime == 0,
                width = Screen.width, height = Screen.height, includesScreenshotOverhead = capturesEnabled,
                screenshotCountDuringRun = capturesDuringRun
            };
        }
        static float Percentile(float[] sorted, float percentile) => sorted.Length == 0 ? 0 : sorted[Mathf.Clamp(Mathf.CeilToInt(percentile * sorted.Length) - 1, 0, sorted.Length - 1)] * 1000;
        void WriteFrames()
        {
            var rows = new StringBuilder("sample,unscaled_seconds,live_enemies,floor\n");
            for (int i = 0; i < frameSeconds.Count; i++) rows.Append(i).Append(',').Append(frameSeconds[i].ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(frameEnemyCounts[i]).Append(',').Append(frameFloors[i]).Append('\n');
            File.WriteAllText(Path.Combine(output, "frame-times.csv"), rows.ToString());
        }
        void SaveReport()
        {
            if (report == null || output == null) return;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "CohesiveQA", "latest.txt"), output);
        }
        void OnApplicationQuit() { if (Active && !finished && report != null) Fail("Application quit before QA completed."); }
        void OnDestroy() { Application.logMessageReceived -= Logged; if (Active && !finished && report != null) Fail("QA component destroyed before completion."); }

        static Preferences SnapshotPreferences()
        {
            var types = new SortedDictionary<string, string>();
            foreach (string key in new[] { "ms.progress", "ms.hero", "ms.weapon", "ms.heavyRoster.archive.v1" }) types[key] = "String";
            foreach (string key in new[] { "ms.sound", "ms.haptics", "ms.fps120", "ms.camclose", "ms.camframe.v2", "ms.showfps", "ms.quality", "ms.test.mode", "ms.test.power", "ms.test.floor", "ms.vgrig", "ms.best", "ms.gate.best", "ms.heavyRoster.v1", "ms.sector", "ms.dev.weekly", "ms.tut.armory", "ms.feel.hints", "ms.feel.shake", "ms.feel.flash", "ms.feel.callouts", "ms.herochibi", "ms.herochibi.model", "ms.hangargyro", "ms.hangarhero3d", "ms.framelog" }) types[key] = "Int";
            types["ms.escape.best"] = "Float";
            foreach (var zone in SectorDefs.All) { types["ms.seen." + zone.id] = "Int"; types["ms.grade." + zone.id] = "Int"; }
            foreach (string hint in new[] { "move", "skillon", "hold", "bio", "auto", "dash", "skill", "leaper", "bloater", "over", "med" }) types["ms.hint." + hint] = "Int";
            var snapshot = new Preferences();
            foreach (var item in types)
                snapshot.entries.Add(new Preference { key = item.Key, type = item.Value, present = PlayerPrefs.HasKey(item.Key), text = item.Value == "String" ? PlayerPrefs.GetString(item.Key, "") : "", integer = item.Value == "Int" ? PlayerPrefs.GetInt(item.Key, 0) : 0, number = item.Value == "Float" ? PlayerPrefs.GetFloat(item.Key, 0) : 0 });
            return snapshot;
        }
        static void RestorePreferences(Preferences snapshot)
        {
            if (snapshot == null) throw new InvalidOperationException("Missing initial preference snapshot");
            foreach (var p in snapshot.entries)
            {
                // Progress.Save must never have changed this key; a discrepancy is an error, not an automatic overwrite.
                if (p.key == "ms.progress") continue;
                if (!p.present) PlayerPrefs.DeleteKey(p.key);
                else if (p.type == "String") PlayerPrefs.SetString(p.key, p.text);
                else if (p.type == "Float") PlayerPrefs.SetFloat(p.key, p.number);
                else PlayerPrefs.SetInt(p.key, p.integer);
            }
            PlayerPrefs.Save();
        }
        static bool SamePreferences(Preferences a, Preferences b) => a != null && b != null && JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        static string Hash(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
