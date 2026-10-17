#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine.EventSystems;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    [InitializeOnLoad]
    public static class FirstCryoPolishProof
    {
        const string Key = "Maze.FirstCryoPolishProof.Active";
        static Game G => Object.FindAnyObjectByType<Game>();
        static string Out => Environment.GetEnvironmentVariable("CRYO_POLISH_OUT") ?? "Logs/FirstCryoPolish";
        static Report report;
        static List<Action> steps;
        static int step, earliestFrame, lastAudioFrame = -1, lastVideoFrame = -1, videoCount;
        static double started = -1, next;
        static Func<bool> wait;
        static bool prepared, done, audio, filming, originalSim, originalPause;
        static string originalSave, originalHero, pickedId;
        static int pickedBefore, cueBefore, tapBefore, originalSound; static bool soundHadKey, expectedMissing;
        static bool Detail => Environment.GetEnvironmentVariable("CRYO_DETAIL") == "1";
        static Enemy detailEnemy; static Vector2 detailAt;
        static bool Endurance => Environment.GetEnvironmentVariable("CRYO_ENDURANCE") == "1";
        static float naturalNext = 10, naturalSkill = 8; static int seenNaturalLevel;
        static float originalVolume; static AudioConfiguration originalAudioConfiguration;
        static readonly List<float> samples = new();
        static readonly List<float> frames = new();

        static FirstCryoPolishProof() { if (SessionState.GetBool(Key, false)) Hook(); }
        public static void Run()
        {
            SessionState.SetBool(Key, true); Hook();
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            EditorApplication.EnterPlaymode();
        }
        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged;
        }
        static void Logged(string message, string stack, LogType type)
        {
            if (done || !SessionState.GetBool(Key, false)) return;
            report ??= new Report();
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
            {
                if (message.Contains("Search") && stack.Contains("UnityEditor.Search")) report.editorDiagnostics.Add(message);
                else report.errors.Add(message + "\n" + stack);
            }
            else if (type == LogType.Warning) { if (expectedMissing && message.StartsWith("FirstCryoAudio: recorded cue missing")) report.tests.Add("Expected missing-cue warning"); else report.warnings.Add(message); }
        }
        static void Delay(double seconds = .3) { next = EditorApplication.timeSinceStartup + seconds; earliestFrame = Time.frameCount + 3; }
        static void WaitGame(float seconds) { float at = G.GameTime; wait = () => G.GameTime >= at + seconds; Delay(0); }
        static void Shot(string name) { UiShot.Shot(name, new[] { Out }); report.captures.Add(name + ".png"); }
        static string Hash(string value) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        static void Event(string name) { report.events.Add($"{Time.unscaledTime:0.000}s audio={samples.Count / (2f * Mathf.Max(1, AudioSettings.outputSampleRate)):0.000}s {name}"); }
        static void Start(bool bot)
        {
            UnityEngine.Random.InitState(int.TryParse(Environment.GetEnvironmentVariable("CRYO_SEED"), out var seed) ? seed : 47);
            Prefs.HeroOverride = "sentinel";
            Progress.UseTemp(new SaveData { runs = 5 });
            var def = SectorDefs.Get("cryo-station");
            Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            G.LoadMap(0); G.menu.Hide(); G.Restart(); G.godMode = true; G.demo = bot;
            report.sector = G.sector.id; report.floor = G.floor;
            report.conditions.Add($"sector={G.sector.id}; tier={G.tier}; floor={G.floor}; difficulty={Meta.Difficulty}; weaponDefinitions={string.Join(",", G.cfg.weapons.Select(w => w.name))}; hero={Prefs.Hero}; startingWeapon={G.cfg.weapons[G.player.weapon].name}; maxHP={G.player.maxHp}; godMode={G.godMode}; demo={G.demo}; temporaryProgression=new SaveData(runs=5); randomSeed={Environment.GetEnvironmentVariable("CRYO_SEED") ?? "47"}");
            report.conditions.Add($"captureFramerate={Time.captureFramerate}; captureDeltaTime={Time.captureDeltaTime}; timeScale={Time.timeScale}; initialInput={(bot ? "built-in demo bot moves/aims/fires, switches weapons and chooses upgrades" : "staged inspection with held fire and debug fixtures")}; mode={(Endurance ? "first-floor continuity; DebugSkill resets cooldown every 18 game seconds after t=8" : Detail ? "actual spitter/tick AI inspection" : "staged vents/blast/telegraph/freeze, direct status cleanup fixtures, UI clicks, mute/scope tests")}");
        }
        static Vector3 W(float x, float z, float y = .4f) => MazeMap.ToWorld(G.player.p + new Vector2(x, z), y);
        static void StagePack()
        {
            G.swarm.Clear();
            for (int i = 0; i < 6; i++) G.DebugSpawn(0, G.player.p + new Vector2(5.5f + i % 3 * 1.1f, -1.8f + i / 3 * 3.6f));
            G.demo = false; G.holdFire = true;
        }
        static void Check(bool ok, string name)
        {
            report.tests.Add((ok ? "PASS " : "FAIL ") + name);
            if (!ok) report.errors.Add(name);
        }
        static int Cues(string id) => report.events.Count(e => e.Contains("CRYO_UI " + id));
        static int Taps => report.events.Count(e => e.Contains("UI sfx_ui_tap"));
        static int ExpectedMarkers => G.swarm.enemies.Count(e => !e.dead && !e.Hidden && e.frozen > 0 && !e.type.nest);
        static void Markers(string name) => Check(FirstCryoFx.Get().FrozenMarkerCount == ExpectedMarkers, name + " " + ExpectedMarkers);
        static void PickButton()
        {
            pickedId = G.offer[0].id; pickedBefore = G.mods[pickedId]; cueBefore = Cues("first_cryo_upgrade_accept"); tapBefore = Taps;
            var b = G.runUi.GetComponentsInChildren<HoloButton>().First(x => x.name == "Card0");
            var rect = (RectTransform)b.transform; var canvas = b.GetComponentInParent<Canvas>().rootCanvas;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<HoloButton>() == b, "graphic raycaster reaches visible Card0");
            b.OnPointerClick(pointer);
            b.OnPointerClick(pointer); // A duplicate queued click must not apply this offer twice.
            Check(G.mods[pickedId] == pickedBefore + 1, "card value applied once under duplicate click: " + pickedId);
            Check(Cues("first_cryo_upgrade_accept") == cueBefore + 1 && Taps == tapBefore, "one confirmation and no generic tap");
        }
        static void CardLayout(int count)
        {
            Canvas.ForceUpdateCanvases();
            var buttons = G.runUi.GetComponentsInChildren<HoloButton>().Where(b => b.name.StartsWith("Card")).ToArray();
            Check(buttons.Length == count, "visible choice count " + count);
            var corners = new Vector3[4];
            foreach (var b in buttons)
            {
                ((RectTransform)b.transform).GetWorldCorners(corners);
                var canvas = b.GetComponentInParent<Canvas>().rootCanvas; var cam = canvas.worldCamera;
                var viewport = cam.pixelRect; var pixels = corners.Select(v => RectTransformUtility.WorldToScreenPoint(cam, v)).ToArray();
                report.tests.Add("Card bounds " + b.name + " " + pixels[0] + ".." + pixels[2] + " viewport=" + viewport);
                Check(pixels.All(v => v.x >= viewport.xMin && v.x <= viewport.xMax && v.y >= viewport.yMin && v.y <= viewport.yMax), "card within viewport " + b.name);
                var description = b.body.Find("Desc").GetComponent<UnityEngine.UI.Text>();
                Check(description.preferredHeight <= description.rectTransform.rect.height + 1, "description fits " + b.name);
            }
        }
        static void CheckAllCardText()
        {
            var button = G.runUi.GetComponentsInChildren<HoloButton>(true).First(b => b.name == "Card0");
            var title = button.body.Find("Name").GetComponent<UnityEngine.UI.Text>(); var description = button.body.Find("Desc").GetComponent<UnityEngine.UI.Text>();
            string oldTitle = title.text, oldDescription = description.text;
            foreach (var card in Game.Cards.Concat(Game.Cards2))
            {
                title.text = card.name; description.text = card.desc;
                Check(title.preferredHeight <= title.rectTransform.rect.height + 1, "title fits " + card.id);
                Check(description.preferredHeight <= description.rectTransform.rect.height + 1, "description fits " + card.id);
            }
            title.text = oldTitle; description.text = oldDescription;
        }
        static void Prepare()
        {
            report ??= new Report(); report.generatedUtc = DateTime.UtcNow.ToString("O");
            report.pass = Environment.GetEnvironmentVariable("CRYO_PASS") ?? "baseline";
            report.unityVersion = Application.unityVersion;
            originalSave = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim; originalHero = Prefs.HeroOverride;
            soundHadKey = PlayerPrefs.HasKey("ms.sound"); originalSound = PlayerPrefs.GetInt("ms.sound", 1);
            originalPause = AudioListener.pause; originalVolume = AudioListener.volume;
            report.savedBefore = Hash(PlayerPrefs.GetString("ms.progress", ""));
            Directory.CreateDirectory(Out); UiShot.Prepare();
            Progress.Sim = true; Game.SimRun = false;
            AudioListener.pause = false; AudioListener.volume = 1;
            Time.captureFramerate = 60;
            originalAudioConfiguration = AudioSettings.GetConfiguration(); var recordingConfiguration = originalAudioConfiguration; recordingConfiguration.dspBufferSize = 512; AudioSettings.Reset(recordingConfiguration);
            FirstCryoFx.Enabled = Environment.GetEnvironmentVariable("CRYO_ORIGINAL") != "1";
            audio = AudioRenderer.Start(); report.audioRendererStarted = audio;
            report.speakerMode = AudioSettings.speakerMode.ToString(); AudioSettings.GetDSPBufferSize(out report.dspBufferSamples, out report.dspBufferCount); report.audioListenerCount = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length; report.mixerAssetCount = AssetDatabase.FindAssets("t:AudioMixer", new[] { "Assets" }).Length;
            Sfx.Log = id => Event("UI " + id); HazardAudio.Log = id => Event("HAZARD " + id); RunMusic.Log = id => Event("MUSIC " + id); FirstCryoAudio.Log = id => Event("CRYO_UI " + id); CombatAudio.Log = id => Event("COMBAT " + id);
            foreach (string cue in new[] { "first_cryo_levelup_reveal", "first_cryo_upgrade_accept" }) { var path = "Assets/Maze/Resources/Audio/" + cue + ".wav"; var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path); var importer = AssetImporter.GetAtPath(path) as AudioImporter; if (clip != null && importer != null) report.clipInspection.Add(cue + ": channels=" + clip.channels + ", frequency=" + clip.frequency + ", seconds=" + clip.length + ", load=" + importer.defaultSampleSettings.loadType + ", codec=" + importer.defaultSampleSettings.compressionFormat); }
            var blastAtlas = Resources.Load<Texture2D>("Fx/FirstCryo/explosion_4x2");
            if (FirstCryoFx.Enabled && blastAtlas == null) throw new InvalidOperationException("Required Cryo explosion atlas missing");
            report.clipInspection.Add("blastAtlas=" + (blastAtlas == null ? "original" : blastAtlas.width + "x" + blastAtlas.height));
            steps = new List<Action>
            {
                () => { Start(true); WaitGame(4.5f); },
                () => { Shot("01_actual_gameplay"); report.hazards = Hazards.Live.Select(h => h.GetType().Name).ToList(); report.enemyTypes = G.swarm.enemies.Select(e => e.type.name).Distinct().ToList(); filming = true; frames.Clear(); WaitGame(10f); },
                () => { filming = false; Shot("02_gameplay_later"); report.gameplayFrames = frames.Count; if (frames.Count > 0) { frames.Sort(); report.editorFrameMeanMs = frames.Average(); report.editorFrameP95Ms = frames[(int)((frames.Count - 1) * .95f)]; } Start(false); WaitGame(4f); },
                () => { G.swarm.Clear(); G.holdFire = true; Shot("03_clean_floor"); filming = true; foreach (var h in Hazards.Live) if (h is CellVents v) v.EruptAll(3f, 2f); Event("FORCED VENT BURST"); WaitGame(.15f); },
                () => { Shot("04_vent_burst_early"); WaitGame(.5f); },
                () => { Shot("05_vent_burst_body"); WaitGame(.8f); },
                () => { Shot("06_vent_burst_tail"); Fx.I.ClearAll(); Fx.I.Boom(W(4, 0), 3.2f); Event("FORCED BARREL EXPLOSION"); WaitGame(.08f); },
                () => { Shot("07_explosion_attack"); WaitGame(.2f); },
                () => { Shot("08_explosion_body"); WaitGame(.6f); },
                () => { Shot("09_explosion_tail"); Fx.I.ClearAll(); EnemyAI.Tele.Circle(G.player.p + new Vector2(-4, 2), 2.2f, 2f, new Color(1, .25f, .2f, .8f), null); EnemyAI.Tele.Lane(G.player.p + new Vector2(-3, -3), Vector2.right, 7, 1.4f, 2f, new Color(1, .7f, .15f, .8f), null); WaitGame(.25f); },
                () => { Shot("10_enemy_telegraph_early"); WaitGame(1f); },
                () => { Shot("11_enemy_telegraph_late"); StagePack(); G.DebugSkill(); Event("FLASH FREEZE SKILL"); WaitGame(.2f); },
                () => { Shot("12_flash_freeze_windup"); WaitGame(.65f); },
                () => { report.frozenEnemiesAtImpact = G.swarm.enemies.Count(e => !e.dead && e.frozen > 0); report.frozenMarkersAtImpact = FirstCryoFx.Get().FrozenMarkerCount; Shot("13_flash_freeze_impact"); WaitGame(1f); },
                () => { Shot("14_flash_freeze_tail"); G.DebugLevelUp(); Event("LEVEL UP CHOICE"); Delay(.7); },
                () => { Shot("15_levelup_choice"); G.Pick(0); Event("UPGRADE CONFIRMED"); Delay(.6); },
                () => { G.demo = true; G.holdFire = false; WaitGame(3f); },
                () => { filming = false; Shot("16_resumed_gameplay"); report.camera = $"fov={Camera.main.fieldOfView},position={Camera.main.transform.position},rotation={Camera.main.transform.eulerAngles}"; Delay(.3); }
            };
            if (FirstCryoFx.Enabled && !Endurance && !Detail) steps.AddRange(new Action[]
            {
                () => { StagePack(); foreach (var e in G.swarm.enemies) e.frozen = .6f; WaitGame(.15f); },
                () => { Markers("frozen markers match"); Shot("17_frozen_status"); WaitGame(.75f); },
                () => { Check(FirstCryoFx.Get().FrozenMarkerCount == 0, "markers removed on thaw"); StagePack(); foreach (var e in G.swarm.enemies) e.frozen = 2f; WaitGame(.15f); },
                () => { Markers("markers regenerated"); G.KillEnemy(G.swarm.enemies.First(e => !e.dead)); WaitGame(.15f); },
                () => { Markers("dead enemy marker removed"); G.swarm.Clear(); WaitGame(.15f); },
                () => { Check(FirstCryoFx.Get().FrozenMarkerCount == 0, "clear removes markers"); StagePack(); WaitGame(.15f); },
                () => { Check(FirstCryoFx.Get().FrozenMarkerCount == 0, "reused enemy views have no stale markers"); G.swarm.Clear(); G.TogglePause(); Delay(.4); },
                () => { Check(G.state == Game.State.Paused, "pause entered"); Shot("18_paused"); G.TogglePause(); Check(G.state == Game.State.Playing, "resume entered"); G.DebugLevelUp(); Delay(.45); },
                () => { CardLayout(G.offer.Count); Shot("19_repeated_choice"); PickButton(); Delay(.45); },
                () => { G.DebugLevelUp(); Delay(.45); },
                () => { PickButton(); Delay(.45); },
                () => { G.DebugLevelUp(); Delay(.45); },
                () => { PickButton(); Delay(.45); },
                () => { cueBefore = Cues("first_cryo_levelup_reveal"); for (int i = 0; i < 12; i++) FirstCryoAudio.Reveal(G); Check(Cues("first_cryo_levelup_reveal") == cueBefore + 1, "rapid reveal throttled"); cueBefore = Cues("first_cryo_upgrade_accept"); for (int i = 0; i < 12; i++) FirstCryoAudio.Accept(G); Check(Cues("first_cryo_upgrade_accept") == cueBefore + 1, "rapid confirm throttled"); Check(Object.FindObjectsByType<FirstCryoAudio>(FindObjectsSortMode.None).Single().GetComponents<AudioSource>().Length == 2, "two UI audio sources only"); Check(!Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.name == "toggle"), "no procedural toggle clip allocated"); Delay(.3); },
                () => { PlayerPrefs.SetInt("ms.sound", 0); Prefs.ApplySound(); Event("MUTE START"); cueBefore = Cues("first_cryo_upgrade_accept"); FirstCryoAudio.Accept(G); FirstCryoAudio.Reveal(G); Delay(.2); },
                () => { Check(Cues("first_cryo_upgrade_accept") == cueBefore && !Object.FindObjectsByType<FirstCryoAudio>(FindObjectsSortMode.None).Single().GetComponents<AudioSource>().Any(x => x.isPlaying), "mute stops cues and creates no new voice"); Event("MUTE END"); PlayerPrefs.SetInt("ms.sound", 1); Prefs.ApplySound(); Delay(.4); },
                () => { FirstCryoAudio.Reveal(G); G.run = false; G.state = Game.State.Paused; Delay(.3); },
                () => { Check(!Object.FindObjectsByType<FirstCryoAudio>(FindObjectsSortMode.None).Single().GetComponents<AudioSource>().Any(x => x.isPlaying), "leaving first-floor run stops UI audio"); Check(Mathf.Approximately(FirstCryoAudio.CombatGain, 1), "combat mix gain is unchanged outside first Cryo floor"); Check(FirstCryoFx.Get().FrozenMarkerCount == 0 && FirstCryoFx.Get().LiveParticles == 0, "scope exit clears presentation"); G.run = true; G.state = Game.State.Playing; Delay(.4); },
                () => { var a = Object.FindObjectsByType<FirstCryoAudio>(FindObjectsSortMode.None).Single(); var f = typeof(FirstCryoAudio).GetField("revealClip", BindingFlags.Instance | BindingFlags.NonPublic); var clip = f.GetValue(a); f.SetValue(a, null); expectedMissing = true; cueBefore = Cues("first_cryo_levelup_reveal"); Check(FirstCryoAudio.Reveal(G) && Cues("first_cryo_levelup_reveal") == cueBefore, "missing optional clip handled silently without synth"); expectedMissing = false; f.SetValue(a, clip); Delay(.4); },
                () => { G.DebugLevelUp(); G.runUi.ShowChoice(Game.Choice.LevelUp, Game.Cards.Take(2).ToList()); Delay(.4); },
                () => { CardLayout(2); Shot("20_two_cards"); G.runUi.ShowChoice(Game.Choice.LevelUp, Game.Cards.Take(4).ToList()); Delay(.4); },
                () => { CardLayout(4); Shot("21_four_cards"); G.runUi.ShowChoice(Game.Choice.LevelUp, G.offer); G.Pick(0); G.swarm.Clear(); WaitGame(.2f); },
                () => { Check(FirstCryoFx.Get().FrozenMarkerPoolCount <= 24, "bounded freeze marker pool"); Shot("22_final_gameplay"); PowerUps.Clear(); G.demo = false; G.holdFire = true; PowerUps.Drop(PowerUps.Get("med_gel"), G.player.p + new Vector2(-3, 2)); PowerUps.Drop(PowerUps.Get("cryo_rounds"), G.player.p + new Vector2(0, 4)); PowerUps.Drop(PowerUps.Get("magnet"), G.player.p + new Vector2(3, 2)); WaitGame(.2f); },
                () => { Shot("23_pickup_identities"); Check(Object.FindObjectsByType<FirstCryoPickupView>(FindObjectsSortMode.None).Length == 3, "distinct first-floor pickup badges"); PowerUps.Clear(); Delay(.2); },
                () => { Check(Object.FindObjectsByType<FirstCryoPickupView>(FindObjectsSortMode.None).Length == 0, "pickup display cleanup"); CheckAllCardText(); cueBefore = Cues("first_cryo_dash_ready"); G.player.dashCd = .25f; WaitGame(.12f); },
                () => { G.player.dashCd = .01f; WaitGame(.15f); },
                () => { Check(Cues("first_cryo_dash_ready") == cueBefore + 1, "actual HUD dash-ready route uses recorded cue"); Delay(.3); }
            });
            if (Endurance) steps = new List<Action>
            {
                () => { Start(true); WaitGame(4.5f); },
                () => { Shot("01_natural_start"); filming = true; frames.Clear(); WaitGame(10f); },
                () => { filming = false; report.gameplayFrames = frames.Count; if (frames.Count > 0) { frames.Sort(); report.editorFrameMeanMs = frames.Average(); report.editorFrameP95Ms = frames[(int)((frames.Count - 1) * .95f)]; } Shot("02_natural_mid"); wait = () => G.choosing == Game.Choice.Cache || G.GameTime >= 180; Delay(0); },
                () => { G.demo = false; Check(G.floor == 1 && G.choosing == Game.Choice.Cache, "natural first floor completed to supply cache"); report.naturalCleared = G.choosing == Game.Choice.Cache; report.naturalKills = G.kills; report.naturalSeconds = G.GameTime; report.naturalLevel = G.level; Shot("03_natural_supply_cache"); CardLayout(G.offer.Count); Delay(.6); }
            };
            if (Detail) steps = new List<Action>
            {
                () => { Start(false); G.holdFire = true; WaitGame(3.5f); },
                () => { G.swarm.Clear(); Fx.I.ClearAll(); EnemyAI.Reset(); detailAt = G.player.p; var index = Array.FindIndex(G.cfg.enemyTypes, t => t.name == "spitter"); Check(index >= 0, "first-floor spitter fixture available"); detailEnemy = G.DebugSpawn(index, G.player.p + new Vector2(8, 1)); detailEnemy.aiCd = 0; filming = true; WaitGame(.3f); },
                () => { Shot("01_spitter_warning"); detailEnemy.aiCd = 100; WaitGame(.8f); },
                () => { Shot("02_acid_impact"); G.player.p += new Vector2(-3, 0); WaitGame(.7f); },
                () => { Shot("03_acid_body"); Check(GameObject.Find("CryoAcidBoundary") != null, "acid boundary uses real puddle lifetime"); WaitGame(3.6f); },
                () => { Shot("04_acid_tail"); WaitGame(1.3f); },
                () => { Shot("05_acid_expired"); Check(GameObject.Find("CryoAcidBoundary") == null, "acid boundary expires with puddle"); filming = false; EnemyAI.Reset(); G.swarm.Clear(); detailAt = G.player.p; var index = Array.FindIndex(G.cfg.enemyTypes, t => t.name == "tick"); Check(index >= 0, "first-floor tick fixture available"); detailEnemy = G.DebugSpawn(index, G.player.p + new Vector2(2.5f, .1f)); WaitGame(.2f); },
                () => { Shot("06_tick_hop"); Check(FirstCryoFx.Get().HopCueCount == 1, "hop warning follows existing tick windup"); WaitGame(.4f); },
                () => { Shot("07_tick_latch"); Check(FirstCryoFx.Get().HopCueCount == 0, "hop warning clears when windup ends"); Check(EnemyAI.Latched > 0, "tick latched through actual AI"); Check(G.runUi.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("DASH TO SHAKE")), "latch status shows dash remedy"); G.player.dashT = .2f; WaitGame(.4f); },
                () => { Shot("08_tick_dash_clear"); Check(EnemyAI.Latched == 0, "dash cleared latch through actual AI"); Check(!G.runUi.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("DASH TO SHAKE")), "latch status clears with actual latch"); G.swarm.Clear(); EnemyAI.Reset(); Delay(.2); }
            };
            prepared = true; Delay(1);
        }
        static void AudioFrame()
        {
            if (!audio || lastAudioFrame == Time.frameCount) return;
            report.audioPolls++;
            int count = AudioRenderer.GetSampleCountForCaptureFrame();
            report.maxCaptureFrames = Mathf.Max(report.maxCaptureFrames, count);
            if (count <= 0) return;
            using var buffer = new NativeArray<float>(count * 2, Allocator.Temp);
            if (AudioRenderer.Render(buffer)) { samples.AddRange(buffer.ToArray()); lastAudioFrame = Time.frameCount; } else report.audioRenderFailures++;
        }
        static void Tick()
        {
            if (done) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > (Endurance ? 900 : 420)) { Finish(false, "Timed out"); return; }
            if (!EditorApplication.isPlaying || G == null || G.menu == null || G.player == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                AudioFrame();
                report.minTimeScale = Mathf.Min(report.minTimeScale, Time.timeScale); report.maxTimeScale = Mathf.Max(report.maxTimeScale, Time.timeScale);
                report.minFeelScale = Mathf.Min(report.minFeelScale, Feel.Scale); report.maxFeelScale = Mathf.Max(report.maxFeelScale, Feel.Scale);
                if (filming && lastVideoFrame != Time.frameCount)
                {
                    lastVideoFrame = Time.frameCount; frames.Add(Time.unscaledDeltaTime * 1000);
                    if (Time.frameCount % 6 == 0) { report.videoTimes.Add(samples.Count / (2f * Mathf.Max(1, AudioSettings.outputSampleRate))); UiShot.Shot("frame_" + (videoCount++).ToString("D4"), new[] { Path.Combine(Out, "gameplay-frames") }); }
                }
                if (Endurance && G.run)
                {
                    if (G.floor != 1) { Finish(false, "Natural proof advanced outside floor one"); return; }
                    report.peakEnemies = Mathf.Max(report.peakEnemies, G.swarm.enemies.Count(e => !e.dead));
                    report.maxFirstCryoParticles = Mathf.Max(report.maxFirstCryoParticles, FirstCryoFx.Get().LiveParticles);
                    foreach (var e in G.swarm.enemies) if (!report.enemyTypes.Contains(e.type.name)) report.enemyTypes.Add(e.type.name);
                    if (G.GameTime >= naturalNext) { report.checkpoints.Add($"{G.GameTime:0.0}s wave={G.wave} kills={G.kills} level={G.level} state={G.state} enemies={G.swarm.enemies.Count} fx={FirstCryoFx.Get().LiveParticles}"); Shot("natural_" + ((int)naturalNext).ToString("D3")); naturalNext += 10; }
                    if (G.choosing == Game.Choice.LevelUp && G.level != seenNaturalLevel) { seenNaturalLevel = G.level; report.naturalChoices++; Shot("natural_choice_" + G.level); }
                    if (G.choosing == Game.Choice.Cache) G.demo = false;
                    if (G.state == Game.State.Playing && G.GameTime >= naturalSkill && G.swarm.enemies.Count >= 4) { G.DebugSkill(); naturalSkill += 18; Event("NATURAL FLASH FREEZE INPUT"); }
                }
                if (!Endurance && !Detail && G.state == Game.State.Choosing && step < 14) { if (filming) Shot("natural_levelup"); G.Pick(0); return; }
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < earliestFrame) return;
                if (wait != null) { if (!wait()) return; wait = null; }
                if (step >= steps.Count) { Finish(true); return; }
                steps[step++]();
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static void Finish(bool success, string error = null)
        {
            if (done) return; done = true; report ??= new Report();
            if (error != null) report.errors.Add(error);
            Sfx.Log = null; HazardAudio.Log = null; RunMusic.Log = null; FirstCryoAudio.Log = null; CombatAudio.Log = null;
            if (audio) AudioRenderer.Stop();
            Time.captureFramerate = 0;
            if (prepared) AudioSettings.Reset(originalAudioConfiguration);
            if (samples.Count > 0) WriteWav(Path.Combine(Out, "unity-output.wav"));
            report.audioSamples = samples.Count; report.audioSampleRate = AudioSettings.outputSampleRate;
            report.audioPeak = samples.Count > 0 ? samples.Max(v => Mathf.Abs(v)) : 0;
            report.videoFrames = videoCount;
            report.savedAfter = Hash(PlayerPrefs.GetString("ms.progress", ""));
            report.passed = success && report.errors.Count == 0 && report.savedBefore == report.savedAfter;
            report.completedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "proof-report.json"), JsonUtility.ToJson(report, true));
            if (prepared) { if (soundHadKey) PlayerPrefs.SetInt("ms.sound", originalSound); else PlayerPrefs.DeleteKey("ms.sound"); PlayerPrefs.Save(); Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave)); Progress.Sim = originalSim; Prefs.HeroOverride = originalHero; AudioListener.pause = originalPause; AudioListener.volume = originalVolume; }
            Game.PendingTier = 0; Game.PendingSector = null; SafeArea.Override = null;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("FirstCryoPolishProof: " + (report.passed ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.ExitPlaymode();
        }
        static void WriteWav(string path)
        {
            using var file = File.Create(path); using var w = new BinaryWriter(file);
            int bytes = samples.Count * 2, rate = AudioSettings.outputSampleRate;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float sample in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1, 1) * 32767));
        }
        [Serializable] class Report
        {
            public string generatedUtc, completedUtc, pass, unityVersion, savedBefore, savedAfter, sector, camera, speakerMode;
            public bool passed, audioRendererStarted, naturalCleared;
            public int naturalKills, naturalLevel, naturalChoices, peakEnemies, maxFirstCryoParticles; public float naturalSeconds;
            public List<string> checkpoints = new(), conditions = new();
            public float minTimeScale = 100, maxTimeScale, minFeelScale = 100, maxFeelScale;
            public int floor, gameplayFrames, videoFrames, audioSamples, audioSampleRate, frozenEnemiesAtImpact, frozenMarkersAtImpact, audioPolls, audioRenderFailures, maxCaptureFrames, dspBufferSamples, dspBufferCount, audioListenerCount, mixerAssetCount;
            public float editorFrameMeanMs, editorFrameP95Ms, audioPeak;
            public List<float> videoTimes = new();
            public List<string> tests = new();
            public List<string> hazards = new(), enemyTypes = new(), captures = new(), clipInspection = new(), events = new(), errors = new(), warnings = new(), editorDiagnostics = new();
        }
    }
}
#endif
