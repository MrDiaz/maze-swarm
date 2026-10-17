#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Editor-only end-to-end capture proof. All screen values are bound to a throwaway SaveData.
    // Only production callbacks grant rewards or mutate that fixture; preferences are restored on every exit.
    [InitializeOnLoad]
    public static partial class BlueUiProof
    {
        const string ActiveKey = "Maze.BlueUiProof.Active";
        const string PrefsKey = "Maze.BlueUiProof.Prefs";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static string Out => Environment.GetEnvironmentVariable("BLUE_UI_OUTPUT") ?? "Logs/BlueUiProof";
        static string Cases => (Environment.GetEnvironmentVariable("BLUE_UI_CASE") ?? "all").ToLowerInvariant();
        static Game G => Object.FindAnyObjectByType<Game>();
        static MenuPresentation P => G.menu.Presentation;
        static GearView Gear => G.menu.Gear;
        static TitleScreenView Title => G.menu.GetComponent<TitleScreenView>();
        static RunResultsView Results => Field<RunResultsView>(G.runUi, "resultsView");

        [Serializable] sealed class PrefValue { public string key, type, text; public int integer; public float number; public bool existed; }
        [Serializable] sealed class PrefSnapshot { public List<PrefValue> entries = new(); }
        [Serializable] sealed class Capture { public string screen, file, sha256, fixture; public int width, height; }
        [Serializable] sealed class BoundsRow { public string screen, name; public float x, y, width, height; }
        [Serializable] sealed class Report
        {
            public string utc, unityVersion, cases, savedBefore, savedAfter, preferenceHashBefore, preferenceHashAfter;
            public bool preferencesRestored, simulatedSave;
            public List<string> tests = new(), errors = new(), warnings = new(), editorDiagnostics = new();
            public List<Capture> captures = new();
            public List<BoundsRow> bounds = new();
        }
        static Report report;
        static PrefSnapshot prefs;
        static List<Action> steps;
        static int step, readyFrame, width, height, originalTier, originalDifficulty, originalFrameRate;
        static double started = -1, next;
        static bool prepared, done, originalSim, originalGameSim, originalAudioPause;
        static float originalAudioVolume, originalTime;
        static string originalSave, originalHeroOverride, transaction, selectedCardId;
        static SectorDef originalSector;
        static Game.Goal[] originalPlan;
        static Camera ui;
        static RenderTexture target;
        static Rect safePixels;
        static int creditBefore, alloyBefore, itemLevelBefore, itemCost, itemAlloy, itemUid, equippedBefore, modBefore, countBefore;
        static int qaVestOriginal;
        static bool pauseSoundBefore, pauseHapticsBefore;
        static Color[] pauseCornerPixels;
        static Color[] dialogCornerPixels;
        static float pauseHp;
        static int pauseFloor;
        static CellLight testLight;
        static Progress.Loot displayedLoot;

        static BlueUiProof() { if (SessionState.GetBool(ActiveKey, false)) Hook(); }

        // Daily Ops refreshes during menu startup, before the first Editor tick.
        // Keep those startup writes inside the proof's throwaway save too.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void ProtectProofStartup()
        {
            if (SessionState.GetBool(ActiveKey, false)) Progress.Sim = true;
        }


        public static void ImportAndRun()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start BlueUiProof outside Play mode.");
            BlueUiImporter.Import();
            TitleScreenImporter.Import();
            Run();
        }

        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start BlueUiProof outside Play mode.");
            Directory.CreateDirectory(Out);
            prefs = SnapshotPreferences();
            string json = JsonUtility.ToJson(prefs, true);
            SessionState.SetString(PrefsKey, json);
            // Local recovery evidence can contain saved progress. Do not publish this private backup.
            File.WriteAllText(Path.Combine(Out, "playerprefs-before.private.json"), json);
            SessionState.SetBool(ActiveKey + ".OriginalSim", Progress.Sim);
            SessionState.SetBool(ActiveKey, true);
            Hook();
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity");
            EditorApplication.EnterPlaymode();
        }

        // Fast executeMethod gate: Unity has already compiled this class before it can call this entry.
        public static void CompileGate()
        {
            var result = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, cases = "compile" };
            result.tests.Add("PASS BlueUiProof and current runtime sources compiled into the Editor domain");
            Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "compile-report.json"), JsonUtility.ToJson(result, true));
            Debug.Log("BlueUiProof compile gate passed");
            EditorApplication.Exit(0);
        }

        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged;
        }
        static void Logged(string message, string stack, LogType type)
        {
            if (done || !SessionState.GetBool(ActiveKey, false)) return;
            report ??= new Report();
            if (type == LogType.Warning) report.warnings.Add(message);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var known = typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup", Static);
                if (known != null && (bool)known.Invoke(null, new object[] { message, stack, type }))
                    report.editorDiagnostics.Add(message + "\n" + stack);
                else report.errors.Add(message + "\n" + stack);
            }
        }
        static void Tick()
        {
            if (done) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            double timeout = double.TryParse(Environment.GetEnvironmentVariable("BLUE_UI_TIMEOUT"), out var limit) ? limit : 900;
            if (EditorApplication.timeSinceStartup - started > timeout) { Finish("Timed out at step " + step); return; }
            if (!EditorApplication.isPlaying || G == null || G.menu == null || G.runUi == null || P == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                TickQualityRecording();
                G.hud.Refresh(1f / 60f, G);
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < readyFrame) return;
                if (step >= steps.Count) { Finish(); return; }
                steps[step++]();
            }
            catch (Exception e) { Finish("Step " + (step - 1) + ": " + e); }
        }
        static bool Has(string name) => Cases == "all" || Cases.Split(',').Select(x => x.Trim()).Contains(name);
        static void Add(Action action) => steps.Add(action);
        static void Wait(double seconds = .55) { next = EditorApplication.timeSinceStartup + seconds; readyFrame = Time.frameCount + 7; }
        static void Check(bool condition, string description)
        {
            report.tests.Add((condition ? "PASS " : "FAIL ") + description);
            if (!condition) report.errors.Add(description);
        }
        static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); }
        static T Field<T>(object source, string name)
        {
            Require(source != null, "Null object for field " + name);
            var field = source.GetType().GetField(name, Instance);
            Require(field != null, source.GetType().Name + "." + name + " field missing");
            return (T)field.GetValue(source);
        }
        static void Set(object source, string name, object value)
        {
            var field = source.GetType().GetField(name, Instance);
            Require(field != null, source.GetType().Name + "." + name + " field missing");
            field.SetValue(source, value);
        }
        static object Invoke(object source, string name, params object[] args)
        {
            var method = source.GetType().GetMethods(Instance).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            Require(method != null, source.GetType().Name + "." + name + " method missing");
            return method.Invoke(source, args);
        }
        static UnityEngine.UI.Button Button(Component source, string name)
        {
            var button = source.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(x =>
                x.name == name && x.gameObject.activeInHierarchy && x.GetComponentInParent<Canvas>()?.enabled == true);
            Require(button != null, "Visible button missing: " + source.name + "/" + name);
            return button;
        }
        static void Click(UnityEngine.UI.Button button)
        {
            Require(button != null && button.gameObject.activeInHierarchy && button.interactable, "Button is not actionable: " + button?.name);
            button.onClick.Invoke();
        }
        static string Snapshot() => JsonUtility.ToJson(Progress.D);
        static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text ?? ""));
        static string Hash(byte[] bytes) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        static void Prepare()
        {
            report ??= new Report();
            prefs = JsonUtility.FromJson<PrefSnapshot>(SessionState.GetString(PrefsKey, ""));
            Require(prefs != null, "Pre-play preference recovery snapshot missing");
            report.utc = DateTime.UtcNow.ToString("O"); report.unityVersion = Application.unityVersion; report.cases = Cases;
            report.savedBefore = Hash(prefs.entries.FirstOrDefault(x => x.key == "ms.progress")?.text ?? "");
            report.preferenceHashBefore = Hash(JsonUtility.ToJson(prefs));
            Check(report.savedBefore == Hash(PlayerPrefs.GetString("ms.progress", "")), "proof protects saved progress before menu startup");
            originalSave = Snapshot(); originalSim = SessionState.GetBool(ActiveKey + ".OriginalSim", false); originalGameSim = Game.SimRun; originalHeroOverride = Prefs.HeroOverride;
            originalTier = Game.PendingTier; originalSector = Game.PendingSector; originalPlan = Game.PendingPlan; originalDifficulty = Meta.Difficulty;
            originalAudioPause = AudioListener.pause; originalAudioVolume = AudioListener.volume; originalTime = Time.timeScale;
            originalFrameRate = Application.targetFrameRate;
            prepared = true; report.simulatedSave = true;
            Progress.Sim = true; Game.SimRun = false; AudioListener.pause = true; AudioListener.volume = 0; Time.timeScale = 1;
            Prefs.HeroOverride = "vanguard"; Prefs.TestMode = false; Prefs.TestFloor = 1; Meta.Difficulty = 0;
            // Apply after the play-mode domain reload resets the Editor-only art-review preference.
            if (Environment.GetEnvironmentVariable("BLUE_UI_PRODUCTION_CRYO") == "1") CryoSoftEnvironment.Enabled = true;
            Check(Meta.Heroes.SequenceEqual(new[] { "vanguard" }) && Meta.HeroId == "vanguard" && Prefs.Vanguard, "Heavy and Engineer share the Heavy gameplay and progression baseline");
            steps = new List<Action>();
            foreach (var size in Sizes())
            {
                var resolution = size;
                if (Has("menu") || Has("equipment") || Has("mission")) AddMenuSteps(resolution);
                if (Has("hud") || Has("choices") || Has("results")) AddRunSteps(resolution);
                if (Has("dialog")) AddDialogSteps(resolution);
                if (Has("living")) AddLivingSteps(resolution);
                if (Has("richhangar")) AddRichHangarSteps(resolution);
                if (Cases.Split(',').Any(x => x.Trim() == "quality")) AddQualitySteps(resolution);
            }
            Wait();
        }
        static IEnumerable<Vector2Int> Sizes()
        {
            foreach (var value in (Environment.GetEnvironmentVariable("BLUE_UI_SIZES") ?? "1672x941,2868x1320").Split(','))
            {
                var parts = value.Trim().ToLowerInvariant().Split('x');
                Require(parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _), "Invalid BLUE_UI_SIZES value " + value);
                yield return new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1]));
            }
        }
        static void Fixture()
        {
            Progress.Sim = true;
            var data = JsonUtility.FromJson<SaveData>(originalSave);
            data.credits = 5000; data.parts = 200; data.alloy = 200; data.runs = Mathf.Max(5, data.runs); data.rank = 10;
            data.gear = new List<GearItem>(); data.equipped = new int[5]; data.nextUid = 1;
            data.cleared = new List<string> { "cryo-station" }; data.research = new List<string>(); data.talents = new List<string>();
            data.relics = new List<string>(); data.tierCleared = new List<string>();
            for (int n = 0; n < 25; n++)
            {
                var item = new GearItem { uid = data.nextUid++, slot = n % 5, rarity = n / 5, level = 2, set = -1 };
                data.gear.Add(item);
                if (n < 5) data.equipped[item.slot] = item.uid;
            }
            for (int n = 0; n < 3; n++) data.gear.Add(new GearItem { uid = 1000 + n, slot = 2, rarity = 0, level = 2, set = -1 });
            data.nextUid = 1100; Progress.UseTemp(data);
            Prefs.HeroOverride = "vanguard"; Prefs.TestMode = false; Prefs.TestFloor = 1; Meta.Difficulty = 0;
            G.player.RebuildHero(G.cfg);
        }
        static void MenuTitle(Vector2Int size)
        {
            if (Game.TestRun) G.TestEnd();
            Progress.Sim = true; G.enabled = false; G.ToMenu(); Fixture();
            G.menu.Show(); G.menu.ReturnToTitle(); Resize(size.x, size.y);
        }
        static void StaticHangarState()
        {
            var root = P.GetComponentsInChildren<RectTransform>(false).First(x => x.name == "Hangar");
            var scene = root.GetComponentsInChildren<UnityEngine.UI.RawImage>(false).FirstOrDefault(x => x.name == "RichHangarScene");
            Check(scene != null && scene.texture == Resources.Load<Texture2D>("RichHangar/Art/scene-rich-extended"), "Hangar uses the approved rich scene adaptation with Heavy and no baked controls");
            Check(scene != null && !scene.raycastTarget && scene.GetComponent<RichHangarArtFit>() != null, "one continuous rich scene shares the safe UI frame and fills phone gutters without intercepting touches");
            Check(root.GetComponentsInChildren<LivingEnvironmentView>(true).Length == 0, "Hangar creates no duplicate 3D room or hero model");
            Check(Meta.Heroes.SequenceEqual(new[] { "vanguard" }) && Meta.HeroId == "vanguard" && Prefs.Vanguard, "rich Hangar retains the shared Heavy gameplay and progression baseline");
            Check(P.DeployButton.onClick.GetPersistentEventCount() == 0 && P.DeployButton.transform.Find("Label").GetComponent<TMP_Text>().text == "DEPLOY", "Deploy remains a real UI Button with independent live text");
            Check(P.HeroesButton != null && P.GearButton != null && P.SettingsButton != null, "approved navigation has implemented Heroes, Gear and Settings routes");
            Check(root.GetComponentsInChildren<UnityEngine.UI.Button>(false).Length == 4, "Hangar exposes only its four implemented controls");
            Check(((RectTransform)P.DeployButton.transform).anchoredPosition == new Vector2(377, -770) && ((RectTransform)P.DeployButton.transform).sizeDelta == new Vector2(314, 124), "Deploy matches approved navigation coordinates with expanded touch target");
        }
        static void AddMenuSteps(Vector2Int size)
        {
            Add(() => { MenuTitle(size); Wait(.9); });
            Add(() =>
            {
                Check(Title.Visible && Title.Logo.childCount == 9, "title has nine independent letter transforms");
                Check(Title.PlayRect != null && !Title.PlayRect.IsChildOf(Title.Logo), "Play has an independently movable transform");
                Check(Title.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(Visible).All(x => x == Title.PlayButton), "title has only Play");
                if (Has("menu")) CaptureScreen("title");
                Click(Title.PlayButton); Wait(.9);
            });
            Add(() =>
            {
                Check(P.Visible && !Title.Visible, "real Play callback opens Hangar");
                StaticHangarState();
                Check(P.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(Visible).All(x => new[] { "Deploy", "Heroes", "Equipment", "Settings" }.Contains(x.name)), "Hangar exposes only approved implemented navigation");
                if (Has("menu")) CaptureScreen("hangar");
                if (Has("equipment")) { G.menu.OpenSheet("gear"); Wait(); }
            });
            if (Has("equipment")) AddEquipmentSteps();
            if (Has("mission"))
            {
                Add(() => { CloseGear(); P.EnterHangar(); Click(P.DeployButton); Wait(); });
                Add(() => { Check(MapSelect.I.Open, "Hangar Deploy opens Deployment"); CaptureScreen("deployment"); ZoneChecks(); MapSelect.I.PickSector(0); Invoke(MapSelect.I, "RefreshStyledScreens"); Click(MapSelect.I.StyledBriefButton); Wait(); });
                Add(() =>
                {
                    var cryo = SectorDefs.Get("cryo-station");
                    Check(Game.PendingPlan.SequenceEqual(cryo.order) && Game.PendingPlan.SequenceEqual(new[] { Game.Goal.Purge, Game.Goal.Hunt, Game.Goal.Hold, Game.Goal.Survive, Game.Goal.Boss }), "Cryo route matches the five authored objectives");
                    Check(MapSelect.I.StyledNextFloorNumber == 1 && MapSelect.I.StyledNextFloorText.Contains("Cold Storage") && MapSelect.I.StyledNextObjectiveText == "Survive three waves", "briefing binds the actual opening floor and objective");
                    CaptureScreen("briefing"); Click(MapSelect.I.StyledDifficultyButton); Wait();
                });
                Add(() =>
                {
                    var modal = Field<RectTransform>(MapSelect.I, "styleDiffModal");
                    Check(modal.gameObject.activeInHierarchy, "difficulty dialog opens");
                    Check(Button(MapSelect.I, "Tier1").interactable, "cleared Normal and rank10 unlock Hard");
                    Check(!Button(MapSelect.I, "Tier2").interactable, "uncleared Hard keeps Nightmare locked");
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Bounds((RectTransform)MapSelect.I.StyledDeployButton.transform).center }, hits);
                    Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(modal), "difficulty modal blocks the underlying Deploy target");
                    CaptureScreen("qa_difficulty");
                    Click(Button(MapSelect.I, "Tier1")); Check(Meta.Difficulty == 1, "actual difficulty callback selects Hard"); Wait();
                });
                Add(() =>
                {
                    Meta.Difficulty = 0; Prefs.TestMode = true;
                    for (int floor = 2; floor <= 5; floor++)
                    {
                        Prefs.TestFloor = floor; Invoke(MapSelect.I, "RefreshStyledScreens");
                        Check(MapSelect.I.StyledNextFloorNumber == floor && MapSelect.I.StyledNextFloorText.Contains(BlueRunPresentation.FloorName(SectorDefs.Get("cryo-station"), floor)), "test-floor briefing uses authored floor " + floor);
                        var routeCard = MapSelect.I.GetComponentsInChildren<RectTransform>(true).First(x => x.name == "Floor" + floor && x.parent.name == "RunRoutePanel");
                        Check(routeCard.Find("Surface").GetComponent<UnityEngine.UI.Image>().sprite == BlueUi.Art("route_active"), "test-floor " + floor + " receives the actual active-route backing");
                    }
                    Prefs.TestFloor = 1; MapSelect.I.StyledBack(); MapSelect.I.PickSector(1); MapSelect.I.OpenBriefing(); Wait();
                });
                Add(() =>
                {
                    Check(Game.PendingPlan.SequenceEqual(SectorDefs.Get("evac-deck").order), "Evac Deck retains Escape/Defend and its actual authored route");
                    CaptureScreen("qa_briefing_evac");
                    MapSelect.I.StyledBack(); MapSelect.I.PickSector(MapSelect.Sectors.Length - 1); MapSelect.I.OpenBriefing(); Wait();
                });
                Add(() =>
                {
                    Check(Game.PendingPlan == null && MapSelect.I.StyledNextFloorNumber == 1, "Escape Test keeps a single Escape floor");
                    Check(!MapSelect.I.StyledDifficultyButton.gameObject.activeSelf, "Escape Test has no fictional difficulty tiers");
                    CaptureScreen("qa_briefing_escape"); MapSelect.I.StyledBack(); Prefs.TestMode = false; Prefs.TestFloor = 1;
                    MapSelect.I.PickSector(0); MapSelect.I.OpenBriefing(); Wait();
                });
                Add(() => { Click(MapSelect.I.StyledDeployButton); G.enabled = true; Wait(6); });
                Add(() =>
                {
                    G.enabled = false;
                    Check(G.run && G.sector?.id == "cryo-station" && G.floor == 1 && G.state == Game.State.Playing, "real Deployment and briefing callbacks launch Cryo floor1");
                    G.ToMenu(); Progress.Sim = true; Wait();
                });
            }
        }
        static void CloseGear()
        {
            if (Gear == null || !Gear.Visible) return;
            Gear.Close(); G.menu.CloseSheet();
        }
        static void AddDialogSteps(Vector2Int size)
        {
            Add(() => { MenuTitle(size); Wait(.9); });
            Add(() =>
            {
                Check(Title.Visible && Title.Logo.childCount == 9 && !Title.PlayRect.IsChildOf(Title.Logo), "dialog patch retains nine independent title glyphs and movable Play");
                var canvas = Field<Canvas>(Title, "canvas");
                var scene = Field<UnityEngine.UI.RawImage>(Title, "background").texture;
                Check(canvas.GetComponentsInChildren<UnityEngine.UI.RawImage>(false).Count(x => x.texture == scene) == 1, "title renders the cover scene once without duplicate Heavy feet");
                CaptureScreen("dialog_title"); Click(Title.PlayButton); Wait(.8);
            });
            Add(() =>
            {
                // These are ordinary production GearItem objects, differing only by saved UID.
                var gloves = Progress.Item(8); gloves.level = 1; gloves.set = 0;
                Progress.D.gear.Add(new GearItem { uid = 1200, slot = gloves.slot, rarity = gloves.rarity, level = gloves.level, set = gloves.set });
                Progress.D.nextUid = 1300; G.menu.OpenSheet("gear"); Wait();
            });
            Add(() =>
            {
                Gear.ChooseFilter(2); Gear.SelectItem(8, true); Gear.OpenDetail(); Click(Gear.EquipButton);
                Check(Progress.D.equipped[2] == 8, "dialog fixture equips actual Rare Frostline Gloves through the existing callback"); Wait();
            });
            Add(() =>
            {
                Gear.CloseDetail(); Wait();
            });
            Add(() =>
            {
                CaptureScreen("dialog_equipment_baseline"); dialogCornerPixels = DialogCornerPixels();
                transaction = Snapshot(); Gear.SelectItem(1200, true); Gear.OpenDetail(); Wait();
            });
            Add(() =>
            {
                var selected = Progress.Item(Gear.SelectedUid); var equipped = Progress.Equipped(2);
                Check(selected.uid != equipped.uid && selected.slot == 2 && equipped.slot == 2 && Mathf.Approximately(Progress.Stat(selected), Progress.Stat(equipped)), "zero comparison uses distinct production glove items with genuinely equal stats");
                Check(Snapshot() == transaction, "opening an equal-stat comparison changes no progression");
                var heading = Field<TMP_Text>(Gear, "compareTitle"); heading.ForceMeshUpdate();
                Check(heading.GetParsedText() == "Compare Gloves" && !heading.text.Contains("Glovess"), "comparison keeps the correct plural Gloves");
                VerifyNeutralZero();
                InspectDialog(Field<RectTransform>(Gear, "comparisonRoot"), "equal Gloves comparison");
                GearDetailRarityChecks("equal Gloves comparison"); CaptureScreen("dialog_compare_gloves_zero");
                creditBefore = Progress.D.credits; alloyBefore = Progress.D.alloy; countBefore = Progress.D.gear.Count;
                Click(Gear.EquipButton);
                Check(Progress.D.equipped[2] == 1200 && Progress.D.credits == creditBefore && Progress.D.alloy == alloyBefore && Progress.D.gear.Count == countBefore, "equal-stat Equip changes the real selected UID without inventing a cost or reward"); Wait();
            });
            Add(() =>
            {
                InspectDialog(Field<RectTransform>(Gear, "detailRoot"), "equipped Gloves detail"); CaptureScreen("dialog_equipped_gloves");
                Gear.CloseDetail(); Gear.OpenManage(); Wait();
            });
            Add(() =>
            {
                InspectDialog(Field<RectTransform>(Gear, "manageRoot"), "Gloves management"); CaptureScreen("dialog_manage_gloves");
                Gear.CloseManage(); Gear.SelectItem(9, true); Gear.OpenDetail(); Wait();
            });
            Add(() =>
            {
                var heading = Field<TMP_Text>(Gear, "compareTitle"); heading.ForceMeshUpdate();
                Check(heading.GetParsedText() == "Compare Boots" && !heading.text.Contains("Bootss"), "comparison keeps the correct plural Boots");
                InspectDialog(Field<RectTransform>(Gear, "comparisonRoot"), "Boots comparison"); CaptureScreen("dialog_compare_boots");
                Gear.CloseDetail(); CloseGear(); Wait();
            });
        }
        static void VerifyNeutralZero()
        {
            var text = Field<TMP_Text>(Gear, "compareValues"); text.ForceMeshUpdate();
            string hex = ColorUtility.ToHtmlStringRGB(BlueUi.Muted), parsed = text.GetParsedText();
            string span = "<color=#" + hex + ">0 pp</color>";
            Check(parsed.Contains("0 pp") && text.text.IndexOf(span, StringComparison.OrdinalIgnoreCase) >= 0,
                "displayed zero delta uses the exact neutral Muted hex instead of gain green");
            int start = parsed.IndexOf("0 pp", StringComparison.Ordinal);
            Color32 expected = BlueUi.Muted;
            bool neutral = start >= 0;
            for (int i = start; neutral && i < start + 4; i++)
            {
                if (i >= text.textInfo.characterCount) { neutral = false; break; }
                var character = text.textInfo.characterInfo[i];
                if (!character.isVisible) continue;
                Color32 color = character.color;
                neutral &= color.r == expected.r && color.g == expected.g && color.b == expected.b;
            }
            Check(neutral, "rendered zero-delta TMP glyph vertices use the neutral color");
        }
        static Vector3 WorldCenter(RectTransform transform) => transform.TransformPoint(transform.rect.center);
        static void InspectDialog(RectTransform overlay, string context)
        {
            Require(overlay != null && overlay.gameObject.activeInHierarchy, context + " overlay missing");
            Canvas.ForceUpdateCanvases();
            var scrim = overlay.Find("Dim")?.GetComponent<UnityEngine.UI.Image>();
            Require(scrim != null, context + " Dim scrim missing");
            var root = Gear.GetComponentInParent<Canvas>().rootCanvas;
            var canvasRect = (RectTransform)root.transform;
            var scrimBounds = Bounds(scrim.rectTransform); var canvasBounds = Bounds(canvasRect);
            Check(scrimBounds.xMin <= canvasBounds.xMin + 1 && scrimBounds.xMax >= canvasBounds.xMax - 1 &&
                scrimBounds.yMin <= canvasBounds.yMin + 1 && scrimBounds.yMax >= canvasBounds.yMax - 1,
                context + " scrim world corners cover the root Canvas including unsafe margins");
            Check(scrim.raycastTarget && scrim.color.a > .1f, context + " full-screen scrim remains visible and input-blocking");
            foreach (var button in overlay.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                var face = button.transform.Find("Surface") as RectTransform;
                var icon = button.transform.Find("Icon") as RectTransform;
                var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (face != null && icon != null && (label == null || string.IsNullOrEmpty(label.text)))
                {
                    Vector2 faceCenter = RectTransformUtility.WorldToScreenPoint(ui, WorldCenter(face));
                    Vector2 iconCenter = RectTransformUtility.WorldToScreenPoint(ui, WorldCenter(icon));
                    Check(Vector2.Distance(faceCenter, iconCenter) <= 1f, context + " " + button.name + " icon world center matches its actual face within one pixel");
                }
                var rect = Bounds((RectTransform)button.transform);
                Check(rect.xMin >= safePixels.xMin - 2 && rect.xMax <= safePixels.xMax + 2 && rect.yMin >= safePixels.yMin - 2 && rect.yMax <= safePixels.yMax + 2,
                    context + " " + button.name + " content target stays inside the safe frame");
            }
            var after = DialogCornerPixels();
            Require(dialogCornerPixels != null && dialogCornerPixels.Length == after.Length, "Dialog pixel baseline missing");
            for (int i = 0; i < after.Length; i++)
                Check(Luminance(dialogCornerPixels[i]) - Luminance(after[i]) >= .04f,
                    context + " full-screen corner " + i + " darkens beyond the native modal frame");
        }
        static float Luminance(Color color) => .2126f * color.r + .7152f * color.g + .0722f * color.b;
        static Color[] DialogCornerPixels()
        {
            var points = new List<Vector2> { new(5, 5), new(width - 6, 5), new(5, height - 6), new(width - 6, height - 6) };
            if (width / (float)height > 2)
            {
                points.Add(new Vector2(safePixels.xMin * .5f, height * .5f));
                points.Add(new Vector2((safePixels.xMax + width) * .5f, height * .5f));
            }
            return points.Select(TargetPixel).ToArray();
        }
        static void AddEquipmentSteps()
        {
            Add(() => { Gear.ChooseFilter(-1); Check(Gear.Visible && Gear.FilteredCount == Progress.D.gear.Count, "equipment inventory represents all actual fixture items"); Check(Gear.VisibleCount == 20, "equipment page contains twenty visible item cells"); GearRarityChecks("inventory page0"); CaptureScreen("equipment"); });
            Add(() =>
            {
                var seen = new HashSet<int>(Gear.VisibleUids); Gear.SetPage(1); foreach (var uid in Gear.VisibleUids) seen.Add(uid);
                GearRarityChecks("inventory page1");
                Check(Progress.D.gear.Select(x => x.rarity).Distinct().OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 5)), "temporary fixture covers real Common/Rare/Epic/Legendary/Mythic rarities");
                Check(seen.SetEquals(Progress.D.gear.Select(x => x.uid)), "inventory pagination preserves all item identities");
                for (int slot = 0; slot < 5; slot++) { Gear.ChooseFilter(slot); Check(Gear.VisibleUids.All(uid => Progress.Item(uid).slot == slot), "inventory filter " + slot + " binds the real slot"); }
                Gear.ChooseFilter(-1); Gear.SelectItem(1, true); Gear.OpenDetail(); Wait();
            });
            Add(() =>
            {
                Check(Gear.DetailOpen && Progress.D.equipped[0] == 1, "equipped slot opens its real detail");
                GearDetailRarityChecks("equipped detail");
                CaptureScreen("equipped-modal");
                itemUid = Gear.SelectedUid; var item = Progress.Item(itemUid); itemLevelBefore = item.level;
                itemCost = Meta.GearCredits(item); itemAlloy = Meta.GearAlloy(item); creditBefore = Progress.D.credits; alloyBefore = Progress.D.alloy;
                Click(Gear.UpgradeButton);
                Check(item.level == itemLevelBefore + 1 && Progress.D.credits == creditBefore - itemCost && Progress.D.alloy == alloyBefore - itemAlloy, "Upgrade spends the actual cost and increases the actual item level");
                transaction = Snapshot(); Gear.UpgradeButton.onClick.Invoke(); Check(Snapshot() == transaction, "immediate repeated Upgrade is debounced"); Wait();
            });
            Add(() =>
            {
                Click(Gear.UnequipButton); Check(Progress.D.equipped[0] == 0, "Unequip clears the actual equipped slot"); Wait();
            });
            Add(() =>
            {
                Gear.CloseDetail(); Gear.SelectItem(1, true); Gear.OpenDetail(); Click(Gear.EquipButton);
                Check(Progress.D.equipped[0] == 1, "Equip restores a selected item to its actual slot"); Wait();
            });
            Add(() => { Gear.CloseDetail(); Gear.SelectItem(6, true); Gear.OpenDetail(); Wait(); });
            Add(() =>
            {
                Check(Gear.DetailOpen && Progress.D.equipped[0] != Gear.SelectedUid, "unequipped selection opens comparison");
                var comparison = Field<TMP_Text>(Gear, "compareValues").GetParsedText();
                float delta = (Progress.Stat(Progress.Item(6)) - Progress.Stat(Progress.Equipped(0))) * 100;
                Check(comparison.Contains(delta.ToString("+0.#;-0.#;0") + " pp"), "comparison shows the accurate signed stat delta");
                GearDetailRarityChecks("equipment comparison");
                CaptureScreen("comparison"); Click(Gear.EquipButton); Check(Progress.D.equipped[0] == 6, "comparison Equip applies the selected actual item"); Wait();
            });
            Add(() =>
            {
                Gear.CloseDetail(); Gear.SelectItem(1, true); Gear.OpenDetail();
                var weaker = Field<TMP_Text>(Gear, "compareValues"); weaker.ForceMeshUpdate();
                Check(weaker.GetParsedText().Contains("-"), "weaker equipment retains a signed negative comparison");
                Gear.CloseDetail(); Gear.SelectItem(1000, true); Gear.OpenManage(); Wait();
            });
            Add(() =>
            {
                countBefore = Progress.D.gear.Count; Click(Gear.MergeButton);
                Check(Gear.ModalOpen, "existing merge remains accessible through item management");
                transaction = Snapshot(); Click(Gear.CancelButton); Check(Snapshot() == transaction, "cancelling merge preserves inventory"); Wait();
            });
            Add(() => { Click(Gear.MergeButton); Wait(); });
            Add(() =>
            {
                Click(Gear.ConfirmButton); Check(Progress.D.gear.Count == countBefore - 2, "merge confirmation consumes exactly three real matching pieces into one");
                transaction = Snapshot(); Gear.ConfirmButton.onClick.Invoke(); Check(Snapshot() == transaction, "repeated merge confirmation grants no duplicate item"); Wait();
            });
            Add(() =>
            {
                Gear.CloseManage(); qaVestOriginal = Progress.D.equipped[1];
                Gear.SelectItem(7, true); Gear.OpenDetail(); Click(Gear.EquipButton);
                Check(Progress.D.equipped[1] == 7, "Rare Vest QA uses an actual equipped Rare item"); Wait();
            });
            Add(() =>
            {
                GearDetailRarityChecks("Rare Vest detail"); CaptureScreen("qa_equipped_rare_vest");
                Gear.CloseDetail(); Gear.SelectItem(12, true); Gear.OpenDetail(); Wait();
            });
            Add(() =>
            {
                VerifyComparisonDelta("higher Vest comparison", true); GearDetailRarityChecks("higher Vest comparison");
                CaptureScreen("qa_comparison_higher_vest"); Gear.CloseDetail(); Gear.SelectItem(2, true); Gear.OpenDetail(); Wait();
            });
            Add(() =>
            {
                VerifyComparisonDelta("lower Vest comparison", false); GearDetailRarityChecks("lower Vest comparison");
                CaptureScreen("qa_comparison_lower_vest"); Click(Gear.EquipButton);
                Check(Progress.D.equipped[1] == qaVestOriginal, "Vest comparison QA restores its original equipped item through the actual Equip callback"); Wait();
            });
            Add(() => { Gear.CloseDetail(); CloseGear(); P.EnterHangar(); Wait(); });
        }
        static void ZoneChecks()
        {
            var map = MapSelect.I; var seen = new HashSet<int>();
            while (map.StyledZonePage > 0) Click(Button(map, "Previous"));
            for (int page = 0; page < map.StyledZonePageCount; page++)
            {
                for (int card = 0; card < 3; card++) { int index = map.StyledVisibleZoneIndex(card); if (index >= 0) Check(seen.Add(index), "zone index " + index + " occurs once in pagination"); }
                if (page + 1 < map.StyledZonePageCount) Click(Button(map, "Next"));
            }
            Check(seen.SetEquals(Enumerable.Range(0, MapSelect.Sectors.Length)), "all twenty actual zones remain reachable");
            for (int index = 0; index < MapSelect.Sectors.Length; index++)
            {
                map.PickSector(index); Invoke(map, "RefreshStyledScreens");
                Check(map.StyledBriefButton.interactable == !MapSelect.Sectors[index].locked, "zone " + index + " Deploy gate matches progression");
            }
            var locked = Enumerable.Range(0, MapSelect.Sectors.Length).FirstOrDefault(i => MapSelect.Sectors[i].locked);
            map.PickSector(locked); Invoke(map, "RefreshStyledScreens");
            if (MapSelect.Sectors[locked].locked) Check(!map.StyledBriefButton.interactable, "locked zone cannot launch from Deployment");
        }

        static bool SameColor(Color actual, Color expected) => Mathf.Abs(actual.r - expected.r) < .0001f &&
            Mathf.Abs(actual.g - expected.g) < .0001f && Mathf.Abs(actual.b - expected.b) < .0001f && Mathf.Abs(actual.a - expected.a) < .0001f;
        static void GearRarityChecks(string context)
        {
            foreach (var cell in Field<System.Collections.IEnumerable>(Gear, "cells"))
            {
                if (!Field<RectTransform>(cell, "rt").gameObject.activeSelf) continue;
                int uid = Field<int>(cell, "uid"); var item = Progress.Item(uid);
                Require(item != null, context + " visible item identity missing");
                var backing = Field<UnityEngine.UI.Image>(cell, "backing");
                Check(backing.sprite != null && SameColor(backing.color, Progress.RarityColors[item.rarity]),
                    context + " " + Progress.Rarities[item.rarity] + " item " + uid + " backing equals its actual rarity color");
            }
            int slot = 0;
            foreach (var equipped in Field<System.Collections.IEnumerable>(Gear, "slots"))
            {
                var item = Progress.Equipped(slot++);
                var backing = Field<UnityEngine.UI.Image>(equipped, "backing");
                Check(item == null ? !backing.gameObject.activeSelf : SameColor(backing.color, Progress.RarityColors[item.rarity]),
                    context + " equipped slot " + (slot - 1) + " backing follows its actual item rarity");
            }
        }
        static void GearDetailRarityChecks(string context)
        {
            var selected = Progress.Item(Gear.SelectedUid);
            Require(selected != null, context + " selected item missing");
            var actual = Progress.Equipped(selected.slot);
            Check(SameColor(Field<UnityEngine.UI.Image>(Gear, "detailBacking").color, Progress.RarityColors[selected.rarity]),
                context + " detail backing follows selected actual rarity");
            Check(SameColor(Field<UnityEngine.UI.Image>(Gear, "rightBacking").color, Progress.RarityColors[selected.rarity]),
                context + " comparison selected backing follows actual rarity");
            var left = Field<UnityEngine.UI.Image>(Gear, "leftBacking");
            Check(actual == null ? !left.gameObject.activeSelf : SameColor(left.color, Progress.RarityColors[actual.rarity]),
                context + " comparison equipped backing follows actual rarity");
        }
        static void VerifyComparisonDelta(string context, bool positive)
        {
            var item = Progress.Item(Gear.SelectedUid); var equipped = Progress.Equipped(item.slot);
            float delta = (Progress.Stat(item) - (equipped != null ? Progress.Stat(equipped) : 0)) * 100;
            var text = Field<TMP_Text>(Gear, "compareValues"); text.ForceMeshUpdate();
            Check((positive ? delta > 0 : delta < 0) && text.GetParsedText().Contains(delta.ToString("+0.#;-0.#;0") + " pp"),
                context + " shows the exact signed production stat difference");
        }

        static void StartRun(Vector2Int size)
        {
            if (Game.TestRun) G.TestEnd();
            Progress.Sim = true; G.enabled = false; G.ToMenu(); Fixture(); Resize(size.x, size.y);
            G.menu.ReturnToTitle(); Click(Title.PlayButton);
            var def = SectorDefs.Get("cryo-station");
            G.LoadMap(0); Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = Game.NewPlan(def);
            G.menu.Hide(); G.Restart(); G.enabled = false; G.demo = false; G.godMode = true; G.holdFire = true;
            G.events.Tick(0); G.Tick(0); Ready();
            Check(G.run && G.floor == 1 && G.sector?.id == "cryo-station", "started actual Heavy Cryo run in temporary progress");
        }
        static void Ready()
        {
            EnemyAI.Tele.Clear(); Abilities.Cd = 0; Abilities.BarrageT = 0; Abilities.DecoyT = 0;
            G.player.dashCd = G.player.dashT = 0; Hud.SkillOn = true; G.hud.Refresh(.1f, G);
        }
        static void AddRunSteps(Vector2Int size)
        {
            Add(() => { StartRun(size); Wait(3.1); });
            if (Has("hud"))
            {
                Add(() =>
                {
                    Ready(); G.level = 3; G.xp = G.XpNeed / 2; G.hud.Refresh(.1f, G); Wait();
                });
                Add(() =>
                {
                    var hp = Field<TMP_Text>(G.hud, "hpText");
                    Check(hp.text == $"{Mathf.CeilToInt(G.player.hp):N0} / {Mathf.CeilToInt(G.player.maxHp):N0}", "HUD TMP health values come from the live player");
                    Check(Field<TMP_Text>(G.runUi, "lvText").text == "3", "HUD TMP level badge comes from the real run");
                    Check(Mathf.Abs(Field<UnityEngine.UI.Image>(G.runUi, "xpFill").fillAmount - G.xp / (float)G.XpNeed) < .03f, "HUD XP fill uses real XP");
                    Check(Field<RoundedHudButton>(G.hud, "skillVisual").IconName == "barrage", "Heavy skill keeps the real Barrage glyph");
                    Check(!G.hud.GetComponentsInChildren<TMP_Text>(false).Any(x => x.name == "AbilityName" || x.name == "WeaponPicker"), "HUD abilities remain icon-only with no in-map weapon selector");
                    HudHitChecks(); CaptureScreen("hud");
                    Set(G, "botDash", true); G.demo = true; G.Tick(.001f); G.demo = false;
                    Check(G.player.dashT > 0 && G.player.dashCd > 0, "actual dash action starts lifetime and cooldown"); Wait(.1);
                });
                Add(() =>
                {
                    Check(Field<RoundedHudButton>(G.hud, "dashVisual").Active, "HUD dash active state follows its real lifetime");
                    G.Tick(G.player.dashT + .02f); G.hud.Refresh(.1f, G);
                    Check(!Field<RoundedHudButton>(G.hud, "dashVisual").Active && G.player.dashCd > 0, "dash active ends into its real cooldown");
                    var input = new InputFrame();
                    Check(TryHudAction(Bounds((RectTransform)Field<HoloButton>(G.hud, "skillBtn").transform).center, ref input) && input.skill, "Barrage control routes the actual skill input");
                    Abilities.Cd = 0; Abilities.Tick(G, 0, input.skill); G.hud.Refresh(.1f, G);
                    Check(Abilities.BarrageT > 0 && Abilities.Cd > 0 && Field<RoundedHudButton>(G.hud, "skillVisual").Active, "actual Barrage lifetime/cooldown reaches the HUD");
                    CaptureScreen("qa_hud_barrage");
                    Abilities.Tick(G, Abilities.BarrageT + .1f, false); G.hud.Refresh(.1f, G);
                    Check(!Field<RoundedHudButton>(G.hud, "skillVisual").Active && Field<RoundedHudButton>(G.hud, "skillVisual").Counter.Length > 0, "Barrage ends into a visible cooldown counter");
                    G.player.maxHp = 12345; G.player.hp = 8765; G.level = Game.MaxLevel; G.xp = 0; Wait();
                });
                Add(() =>
                {
                    Check(Field<TMP_Text>(G.hud, "hpText").text == "8,765 / 12,345", "HUD formats large real HP values");
                    Check(Field<TMP_Text>(G.runUi, "lvText").text == "MAX 10", "HUD maximum-level badge is truthful");
                    Check(Field<UnityEngine.UI.Image>(G.runUi, "xpFill").fillAmount < .02f, "XP stays empty at level cap");
                    CaptureScreen("qa_hud_large_values"); G.player.maxHp = G.cfg.player.hp * Progress.HpMul; G.player.hp = G.player.maxHp; G.level = 3; G.xp = 0;
                    PowerUps.EndRun(); testLight = CellLight.I; if (testLight == null) { testLight = new CellLight(); testLight.Begin(G); }
                    testLight.Level = 0; CellLight.Flares = 3; Wait();
                });
                Add(() =>
                {
                    G.hud.Refresh(.1f, G); var flare = Field<HoloButton>(G.hud, "flareBtn");
                    Check(flare.gameObject.activeSelf, "Flare appears only for actual darkness mechanics");
                    var input = new InputFrame(); Check(TryHudAction(Bounds((RectTransform)flare.transform).center, ref input) && input.flare, "Flare control routes actual flare input");
                    testLight.ThrowFlare(); Check(CellLight.Flares == 2, "actual Flare consumes one charge");
                    CellLight.Flares = 0; testLight.ThrowFlare(); Check(CellLight.Flares == 0, "empty Flare never goes negative");
                    testLight.Level = 1; Wait();
                });
                Add(() =>
                {
                    Check(!Field<HoloButton>(G.hud, "flareBtn").gameObject.activeSelf, "Flare hides again in a lit map");
                    pauseHp = G.player.hp; pauseFloor = G.floor;
                    var pause = Field<HoloButton>(G.hud, "pauseBtn");
                    ExecuteEvents.Execute(pause.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                    Wait(.8);
                });
                Add(() =>
                {
                    Check(G.state == Game.State.Paused && G.hud.OverlayOpen, "real pause pointer callback enters paused state");
                    var input = new InputFrame(); Check(!TryHudAction(Bounds((RectTransform)Field<HoloButton>(G.hud, "dashBtn").transform).center, ref input), "pause blocks in-map dash input");
                    PauseToggleChecks("initial"); CaptureScreen("pause");
                    pauseCornerPixels = PauseToggleCornerPixels();
                    pauseSoundBefore = Prefs.Sound; pauseHapticsBefore = Prefs.HapticsOn;
                    Click(Button(G.hud, "Sound")); Check(Prefs.Sound != pauseSoundBefore, "pause Sound toggles its actual preference");
                    Click(Button(G.hud, "Haptics")); Check(Prefs.HapticsOn != pauseHapticsBefore, "pause Haptics toggles its actual preference"); Wait();
                });
                Add(() =>
                {
                    PauseToggleChecks("inverse"); CaptureScreen("qa_pause_toggles_inverse"); CheckPauseToggleCorners("inverse");
                    Click(Button(G.hud, "Sound")); Click(Button(G.hud, "Haptics"));
                    Check(Prefs.Sound == pauseSoundBefore && Prefs.HapticsOn == pauseHapticsBefore, "toggling both pause controls again restores their initial real settings"); Wait();
                });
                Add(() =>
                {
                    PauseToggleChecks("restored"); CaptureScreen("qa_pause_toggles_restored"); CheckPauseToggleCorners("restored");
                    Click(Button(G.hud, "Hangar")); Wait();
                });
                Add(() =>
                {
                    var confirmation = Field<RectTransform>(G.hud, "blueLeaveConfirm");
                    Check(confirmation.gameObject.activeSelf && G.state == Game.State.Paused, "Hangar exit retains confirmation before ending a run");
                    CaptureScreen("qa_pause_confirmation"); Click(Button(G.hud, "Stay"));
                    Check(!confirmation.gameObject.activeSelf && G.state == Game.State.Paused && G.player.hp == pauseHp && G.floor == pauseFloor, "Stay preserves the paused run"); Click(Button(G.hud, "Resume")); Wait();
                });
                Add(() =>
                {
                    Check(G.state == Game.State.Playing && G.floor == pauseFloor && G.player.hp == pauseHp, "Resume returns to the same run without reward/state changes");
                    G.TogglePause(); Click(Button(G.hud, "Hangar")); Click(Button(G.hud, "Leave"));
                    Check(G.state == Game.State.Menu, "confirmed Hangar exit invokes the existing menu state transition"); Progress.Sim = true; Wait(1.1);
                });
                Add(() => { Check(P.Visible && G.state == Game.State.Menu, "confirmed Hangar exit settles on the existing menu"); });
            }
            if (Has("choices"))
            {
                Add(() => { StartRun(size); Wait(3); });
                Add(() => { G.DebugLevelUp(); Check(G.state == Game.State.Choosing && G.offer.Count == 3, "actual level-up generates three offers"); SetOffer("rate", "hp", "rico"); Wait(); });
                Add(() =>
                {
                    Check(Field<RectTransform>(G.runUi, "choiceRt").gameObject.activeSelf, "upgrade chooser is visible");
                    CaptureScreen("upgrade"); selectedCardId = G.offer[0].id; modBefore = G.mods[selectedCardId];
                    var choose = ChoiceButton(0); Click(choose); choose.onClick.Invoke();
                    Check(G.mods[selectedCardId] == modBefore + 1 && G.choosing == Game.Choice.None, "real Choose callback applies one upgrade once"); Wait();
                });
                Add(() =>
                {
                    Progress.D.research.Add("ta4:1"); G.level = 4; G.DebugLevelUp();
                    Check(G.offer.Count == 4, "actual Commander's Pick generates four offers");
                    SetOffer("rate", "hp", "rico", "hunters_mark");
                    Check(G.offer[3].desc.Length > 65, "four-card proof uses an actual long production description"); Wait();
                });
                Add(() =>
                {
                    CaptureScreen("qa_upgrade_four_long"); selectedCardId = G.offer[3].id; modBefore = G.mods[selectedCardId];
                    Click(ChoiceButton(3)); Check(G.mods[selectedCardId] == modBefore + 1, "fourth real upgrade remains interactive"); Wait();
                });
            }
            if (Has("results"))
            {
                Add(() => { StartRun(size); Wait(3); });
                Add(() =>
                {
                    G.DebugCache(); transaction = Snapshot(); countBefore = G.bonusCredits; equippedBefore = G.mods.Total; Wait();
                });
                Add(() =>
                {
                    var clear = Field<BlueFloorClearView>(G.runUi, "blueFloorClear");
                    Check(clear != null && clear.Visible && G.choosing == Game.Choice.Cache, "actual cache flow presents Floor cleared");
                    CaptureScreen("floor-cleared");
                    var ids = G.offer.Select(x => x.id).ToArray(); int floor = G.floor;
                    Click(clear.ContinueButton); clear.ContinueButton.onClick.Invoke();
                    Check(Snapshot() == transaction && G.bonusCredits == countBefore && G.mods.Total == equippedBefore && G.floor == floor, "Floor cleared Continue grants nothing and starts no floor");
                    Check(G.offer.Select(x => x.id).SequenceEqual(ids) && G.choosing == Game.Choice.Cache, "Continue reveals the existing prepared offer exactly once"); Wait();
                });
                Add(() =>
                {
                    Check(Field<RectTransform>(G.runUi, "choiceRt").gameObject.activeSelf, "Continue reveals the actual cache chooser");
                    int index = G.offer.FindIndex(x => x.id == "cache_credits"); int payout = G.offer[index].max; int before = G.bonusCredits;
                    var button = ChoiceButton(index); Click(button); button.onClick.Invoke();
                    Check(G.bonusCredits == before + payout && G.floor == 2, "only the actual cache selection applies its credit promise and advances one floor"); Wait(3);
                });
                Add(() =>
                {
                    G.DebugFloor(4); G.kills = 41; G.level = 7; G.RunScore = 3567; Set(G, "runT", 132.6f); KillPlayer(); Wait(1);
                });
                Add(() =>
                {
                    Check(G.state == Game.State.Dead && Results.Visible, "actual player death opens Run ended");
                    VerifyResultBindings(false); CaptureScreen("death"); displayedLoot = Results.DisplayLoot; transaction = Snapshot();
                    G.HurtPlayer(999999); Results.Show(false, G.floor, displayedLoot);
                    Check(Snapshot() == transaction, "repeated death/display does not award loot twice");
                    Results.OpenDetails(); Wait();
                });
                Add(() =>
                {
                    Check(Field<RectTransform>(Results, "details").gameObject.activeSelf, "Run details drawer is accessible");
                    CaptureScreen("qa_run_details");
                    int beforePower = Progress.Power(), beforeCredits = Progress.D.credits;
                    if (Results.UpgradeMode == "upgrade")
                    {
                        Click(Results.UpgradeButton);
                        Check(Progress.D.credits < beforeCredits && Progress.Power() > beforePower, "Run details upgrade spends currency and changes real power");
                        transaction = Snapshot(); Results.UpgradeButton.onClick.Invoke(); Check(Snapshot() == transaction, "immediate repeated result upgrade is debounced");
                    }
                    else Check(false, "affordable result fixture should expose an upgrade");
                    Wait();
                });
                Add(() =>
                {
                    var loot = new Progress.Loot { gear = new List<GearItem>() };
                    for (int i = 0; i < 12; i++) loot.gear.Add(new GearItem { uid = 9000 + i, slot = i % 5, rarity = i % 4, level = 1 });
                    transaction = Snapshot(); Results.Show(false, G.floor, loot); Results.OpenDetails(); Results.SetRewardPage(11);
                    Check(Results.RenderedRewardRows >= 12 && Results.RewardPage == 11, "Run details retains variable reward pagination");
                    Check(Snapshot() == transaction, "display-only reward drawer fixture grants no items");
                    Click(Results.RetryButton); transaction = Snapshot(); Results.RetryButton.onClick.Invoke();
                    Check(G.run && G.sector?.id == "cryo-station" && G.floor == 1 && !Results.Visible, "Retry restarts the same real sector");
                    Check(Snapshot() == transaction, "repeated Retry grants no extra progression"); G.enabled = false; Wait(3);
                });
                Add(() => { KillPlayer(); Wait(); });
                Add(() =>
                {
                    transaction = Snapshot(); Click(Results.MenuButton); Results.MenuButton.onClick.Invoke();
                    Check(G.state == Game.State.Menu && !Results.Visible, "death Hangar invokes the existing menu transition");
                    Check(Snapshot() == transaction, "repeated death Hangar action grants no rewards"); Progress.Sim = true; Wait(1.1);
                });
                Add(() => { Check(P.Visible && G.state == Game.State.Menu, "death Hangar settles on the existing menu"); });
                Add(() => { StartRun(size); G.DebugFloor(5); G.kills = 159; G.level = 8; Set(G, "runT", 209f); G.DebugWin(); Wait(.9); });
                Add(() =>
                {
                    Check(G.state == Game.State.Won && Results.Visible, "actual win opens Victory");
                    VerifyResultBindings(true); CaptureScreen("victory"); transaction = Snapshot(); displayedLoot = Results.DisplayLoot;
                    Results.Show(true, Game.Floors, displayedLoot); Check(Snapshot() == transaction, "repeated Victory display grants no duplicate rewards");
                    Click(Results.MenuButton); Check(G.state == Game.State.Menu, "Victory Return to Hangar invokes the existing menu transition"); Wait(1.1);
                });
                Add(() => { Check(P.Visible && G.state == Game.State.Menu, "Victory Return to Hangar settles on the existing menu"); });
            }
        }
        static void KillPlayer()
        {
            G.state = Game.State.Playing; G.godMode = false; G.holdFire = true; G.player.dashT = 0;
            G.player.hp = G.player.maxHp * .2f; Set(G, "lastBreathT", 0f); Set(G, "secondWindUsed", true);
            Set(G, "vanRevive", true); Set(G, "tagsUsed", true); G.lastBreathUsed = true;
            PowerUps.EndRun(); G.HurtPlayer(999999);
        }
        static void SetOffer(params string[] ids)
        {
            // Production card objects and production mutations; deterministic ordering only for visual comparison.
            var source = Game.Cards.Concat(Game.Cards2).ToArray();
            G.offer.Clear();
            foreach (var id in ids) G.offer.Add(source.First(x => x.id == id));
            G.runUi.ShowChoice(Game.Choice.LevelUp, G.offer);
        }
        static UnityEngine.UI.Button ChoiceButton(int index)
        {
            var card = G.runUi.GetComponentsInChildren<RectTransform>(false).FirstOrDefault(x => x.name == "Card" + index && x.parent.name == "ChoiceLayout");
            Require(card != null, "Choice card " + index + " missing");
            return card.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(x => x.name == "Choose");
        }
        static bool TryHudAction(Vector2 point, ref InputFrame input)
        {
            var args = new object[] { point, input }; bool hit = (bool)Invoke(G.hud, "TryHudAction", args); input = (InputFrame)args[1]; return hit;
        }
        static void HudHitChecks()
        {
            foreach (var name in new[] { "dashBtn", "skillBtn" })
            {
                var button = Field<HoloButton>(G.hud, name); var bounds = Bounds((RectTransform)button.transform); var input = new InputFrame();
                Check(TryHudAction(bounds.center, ref input), name + " centre is interactive");
                Check(name == "dashBtn" ? input.dash && !input.skill && !input.flare : input.skill && !input.dash && !input.flare, name + " input remains semantically distinct");
                input = new InputFrame(); Check(!TryHudAction(new Vector2(bounds.xMin - 6, bounds.yMax + 6), ref input), name + " rejects a point outside its target");
                Check(bounds.width / height * 375 >= 44 && bounds.height / height * 375 >= 44, name + " has a mobile touch target");
            }
        }
        static void PauseToggleChecks(string context)
        {
            foreach (var name in new[] { "Sound", "Haptics" })
            {
                var button = Button(G.hud, name);
                var touch = button.transform.Find("TouchTarget")?.GetComponent<UnityEngine.UI.Image>();
                Check(touch != null && Mathf.Abs(touch.color.a) < .00001f && touch.raycastTarget,
                    context + " pause " + name + " hit target remains exactly transparent and raycastable");
                var surface = button.targetGraphic as UnityEngine.UI.Image;
                Check(surface != null && surface.sprite != null && surface.color.a > .99f,
                    context + " pause " + name + " has a visible original button face");
                bool on = name == "Sound" ? Prefs.Sound : Prefs.HapticsOn;
                Color tint = on ? Color.white : new Color(.68f, .78f, .9f, 1);
                Check(surface != null && SameColor(surface.color, tint), context + " pause " + name + " face tint matches its real on/off state");
                var icon = button.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
                if (icon != null) Check(icon.sprite != null && icon.color.a > .99f && SameColor(icon.color, tint),
                    context + " pause " + name + " icon remains valid and tinted");
                var label = button.transform.Find("State")?.GetComponent<TMP_Text>();
                string expected = name == "Sound" ? on ? "Sound on" : "Sound off" : on ? "On" : "Off";
                Check(label != null && label.text == expected, context + " pause " + name + " label reflects its actual preference");
            }
        }
        static Color[] PauseToggleCornerPixels()
        {
            var samples = new List<Color>();
            foreach (var name in new[] { "Sound", "Haptics" })
            {
                var rect = Bounds((RectTransform)Button(G.hud, name).transform);
                samples.Add(TargetPixel(new Vector2(rect.xMin + 3, rect.yMax - 3)));
                samples.Add(TargetPixel(new Vector2(rect.xMax - 3, rect.yMax - 3)));
            }
            return samples.ToArray();
        }
        static Color TargetPixel(Vector2 position)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(position.x), 0, width - 1), y = Mathf.Clamp(Mathf.RoundToInt(position.y), 0, height - 1);
            var old = RenderTexture.active; RenderTexture.active = target;
            var sample = new Texture2D(1, 1, TextureFormat.RGB24, false);
            sample.ReadPixels(new Rect(x, y, 1, 1), 0, 0); sample.Apply(); var color = sample.GetPixel(0, 0);
            Object.DestroyImmediate(sample); RenderTexture.active = old; return color;
        }
        static void CheckPauseToggleCorners(string context)
        {
            var after = PauseToggleCornerPixels();
            Require(pauseCornerPixels != null && pauseCornerPixels.Length == after.Length, "Pause corner baseline missing");
            for (int i = 0; i < after.Length; i++)
            {
                Color before = pauseCornerPixels[i], now = after[i];
                float delta = Mathf.Max(Mathf.Abs(before.r - now.r), Mathf.Max(Mathf.Abs(before.g - now.g), Mathf.Abs(before.b - now.b)));
                Check(delta <= .035f, context + " pause hit-target corner " + i + " retains the same panel background instead of an opaque square");
            }
        }
        static void VerifyResultBindings(bool victory)
        {
            var root = Field<RectTransform>(Results, victory ? "victoryLayout" : "deathLayout");
            var values = root.GetComponentsInChildren<TMP_Text>(false).Where(x => x.name.StartsWith("CurrencyValue")).OrderBy(x => x.name).ToArray();
            int[] expected = { Results.DisplayLoot.credits, Results.DisplayLoot.parts, Results.DisplayLoot.alloy };
            Check(values.Length == 3 && values.Select(x => x.GetParsedText()).SequenceEqual(expected.Select(x => "+" + x.ToString("N0"))), "result currency labels equal the actual awarded Loot");
            var allText = Results.GetComponentsInChildren<TMP_Text>(true).Select(x => x.GetParsedText()).ToArray();
            Check(!allText.Any(x => x.IndexOf("death pays", StringComparison.OrdinalIgnoreCase) >= 0), "results have no obsolete death payout sentence");
            Check(!Results.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(x => x.name == "Sector"), "results have no Sector action");
            Check(Results.DisplayLoot.gear == null || Results.DisplayLoot.gear.All(x => Progress.Item(x.uid) != null), "rendered actual loot item identities exist in temporary progress");
        }

        static bool Visible(UnityEngine.UI.Button button) => button.gameObject.activeInHierarchy && button.GetComponentInParent<Canvas>()?.enabled == true;
        static void Resize(int w, int h)
        {
            width = w; height = h;
            if (target != null)
            {
                Camera.main.targetTexture = null; if (ui != null) ui.targetTexture = null;
                target.Release(); Object.DestroyImmediate(target);
            }
            target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "BlueUiProofTarget" };
            target.Create(); Camera.main.targetTexture = target;
            if (ui == null)
            {
                ui = new GameObject("BlueUiProofUiCamera").AddComponent<Camera>();
                ui.cullingMask = 1 << 5; ui.orthographic = true; ui.nearClipPlane = .1f; ui.farClipPlane = 200;
                ui.transform.position = new Vector3(0, -2000, 0);
                ui.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
                Camera.main.GetUniversalAdditionalCameraData().cameraStack.Add(ui);
            }
            ui.targetTexture = target;
            float left = w / (float)h > 2 ? Mathf.Round(199 * w / 2868f) : 0;
            float right = w / (float)h > 2 ? Mathf.Round(199 * w / 2868f) : 0;
            float bottom = w / (float)h > 2 ? Mathf.Round(71 * h / 1320f) : 0;
            safePixels = new Rect(left, bottom, w - left - right, h - bottom);
            SafeArea.Override = new Rect(left / w, bottom / h, 1 - (left + right) / w, 1 - bottom / h);
            ConvertCanvases();
        }
        static void ConvertCanvases()
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                int order = canvas.sortingOrder;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 50; canvas.sortingOrder = order;
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null && canvas.name != "TitleScreenCanvas")
                {
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = height / 1080f;
                }
            }
        }
        static Rect Bounds(RectTransform transform)
        {
            var corners = new Vector3[4]; transform.GetWorldCorners(corners);
            var bottom = RectTransformUtility.WorldToScreenPoint(ui, corners[0]);
            var top = RectTransformUtility.WorldToScreenPoint(ui, corners[2]);
            return Rect.MinMaxRect(bottom.x, bottom.y, top.x, top.y);
        }
        static void Inspect(string screen)
        {
            ConvertCanvases(); Canvas.ForceUpdateCanvases();
            Check(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, screen + " has one EventSystem");
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(x => x.enabled && x.gameObject.activeInHierarchy).ToArray();
            foreach (var canvas in canvases)
            {
                if (canvas.isRootCanvas) Check(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null, screen + " " + canvas.name + " has a GraphicRaycaster");
                foreach (var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(Visible))
                {
                    var rect = Bounds((RectTransform)button.transform);
                    report.bounds.Add(new BoundsRow { screen = screen, name = button.name, x = rect.x, y = rect.y, width = rect.width, height = rect.height });
                    Check(rect.xMin >= safePixels.xMin - 2 && rect.xMax <= safePixels.xMax + 2 && rect.yMin >= safePixels.yMin - 2 && rect.yMax <= safePixels.yMax + 2,
                        screen + " " + button.name + " target stays inside safe area");
                    bool primary = new[] { "Play", "Back", "Deploy", "Briefing", "Equip", "Upgrade", "Unequip", "Resume", "Hangar", "Retry", "Continue", "Choose" }.Contains(button.name);
                    if (primary) Check(rect.width / height * 375 >= 44 && rect.height / height * 375 >= 44, screen + " " + button.name + " has a mobile touch target");
                }
                foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(false).Where(x => x.GetComponentInParent<Canvas>()?.enabled == true))
                {
                    if (string.IsNullOrWhiteSpace(text.text) || text.rectTransform.rect.width <= 2 || text.rectTransform.rect.height <= 2) continue;
                    text.ForceMeshUpdate();
                    Check(text.font != null && text.textInfo.characterCount > 0, screen + " " + text.name + " generates real TMP glyphs");
                    Check(!text.isTextOverflowing, screen + " " + text.name + " text fits its assigned area");
                }
            }
            foreach (var field in new[] { "hpText", "waveText" }) Field<TMP_Text>(G.hud, field).ForceMeshUpdate();
        }
        static void CaptureScreen(string screen)
        {
            Inspect(screen);
            var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); RenderTexture.active = old;
            byte[] bytes = texture.EncodeToPNG(); Object.DestroyImmediate(texture);
            string file = Path.Combine(Out, screen + "_" + width + "x" + height + ".png");
            File.WriteAllBytes(file, bytes);
            report.captures.Add(new Capture { screen = screen, file = file, width = width, height = height, sha256 = Hash(bytes),
                fixture = "Temporary SaveData and production callbacks; no baked UI values. Optional deterministic card ordering uses actual production cards." });
            Debug.Log("BlueUiProof capture " + file);
            SaveReport();
        }
        static void SaveReport()
        {
            Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "report.json"), JsonUtility.ToJson(report, true));
        }

        static PrefSnapshot SnapshotPreferences()
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "Maze"), "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(source, "\"(ms\\.[^\"]+)\""))
                    if (match.Groups[1].Value.Length < 150 && !match.Groups[1].Value.Contains("{")) types.TryAdd(match.Groups[1].Value, "Int");
                foreach (Match match in Regex.Matches(source, "PlayerPrefs\\.(?:Get|Set)(Int|String|Float)\\s*\\(\\s*\"([^\"]+)\""))
                    types[match.Groups[2].Value] = match.Groups[1].Value;
                foreach (Match match in Regex.Matches(source, "\\bHint\\s*\\(\\s*\"([^\"]+)\""))
                    types["ms.hint." + match.Groups[1].Value] = "Int";
            }
            foreach (var sector in SectorDefs.All) { types["ms.seen." + sector.id] = "Int"; types["ms.grade." + sector.id] = "Int"; }
            foreach (string key in new[] { "ms.progress", "ms.hero", "ms.weapon", "ms.heavyRoster.archive.v1" }) types[key] = "String";
            types["ms.escape.best"] = "Float";
            var snapshot = new PrefSnapshot();
            foreach (var pair in types.OrderBy(x => x.Key, StringComparer.Ordinal)) snapshot.entries.Add(ReadPref(pair.Key, pair.Value));
            return snapshot;
        }
        static PrefValue ReadPref(string key, string type)
        {
            var value = new PrefValue { key = key, type = type, existed = PlayerPrefs.HasKey(key) };
            if (type == "String") value.text = PlayerPrefs.GetString(key, "");
            else if (type == "Float") value.number = PlayerPrefs.GetFloat(key, 0);
            else value.integer = PlayerPrefs.GetInt(key, 0);
            return value;
        }
        static void Restore(PrefSnapshot snapshot)
        {
            foreach (var value in snapshot.entries)
            {
                if (!value.existed) PlayerPrefs.DeleteKey(value.key);
                else if (value.type == "String") PlayerPrefs.SetString(value.key, value.text);
                else if (value.type == "Float") PlayerPrefs.SetFloat(value.key, value.number);
                else PlayerPrefs.SetInt(value.key, value.integer);
            }
            PlayerPrefs.Save();
        }
        public static void RestorePreferences()
        {
            string json = SessionState.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) json = File.ReadAllText(Path.Combine(Out, "playerprefs-before.private.json"));
            var recovery = JsonUtility.FromJson<PrefSnapshot>(json);
            Require(recovery != null, "Recovery snapshot missing"); Restore(recovery);
            SessionState.SetBool(ActiveKey, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("BlueUiProof restored pre-play known preferences without opening the game");
            EditorApplication.Exit(0);
        }
        static void Finish(string error = null)
        {
            if (done) return; done = true; report ??= new Report();
            if (error != null) report.errors.Add(error);
            try
            {
                StopQualityVideo();
                prefs ??= JsonUtility.FromJson<PrefSnapshot>(SessionState.GetString(PrefsKey, ""));
                if (prepared)
                {
                    Progress.Sim = true;
                    if (G != null) { G.enabled = false; G.ToMenu(); Progress.Sim = true; }
                    Check(report.savedBefore == Hash(PlayerPrefs.GetString("ms.progress", "")), "proof mutations never write the saved progress key");
                    Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave)); Progress.Sim = originalSim; Game.SimRun = originalGameSim; Prefs.HeroOverride = originalHeroOverride;
                    Game.PendingTier = originalTier; Game.PendingSector = originalSector; Game.PendingPlan = originalPlan; Meta.Difficulty = originalDifficulty;
                    SafeArea.Override = null; AudioListener.pause = originalAudioPause; AudioListener.volume = originalAudioVolume; Time.timeScale = originalTime; Application.targetFrameRate = originalFrameRate;
                }
                if (prefs != null)
                {
                    Restore(prefs);
                    report.savedAfter = Hash(PlayerPrefs.GetString("ms.progress", ""));
                    var current = new PrefSnapshot { entries = prefs.entries.Select(x => ReadPref(x.key, x.type)).ToList() };
                    report.preferenceHashAfter = Hash(JsonUtility.ToJson(current));
                    report.preferencesRestored = report.preferenceHashBefore == report.preferenceHashAfter;
                    Check(report.preferencesRestored, "all captured local PlayerPrefs values and key presence restored");
                    Check(report.savedBefore == report.savedAfter, "saved progress restored byte-for-byte");
                }
                SaveReport();
            }
            catch (Exception restoreError)
            {
                report.errors.Add("Recovery error: " + restoreError);
                try { SaveReport(); } catch { }
                Debug.LogError("BlueUiProof recovery error: " + restoreError);
            }
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("BlueUiProof complete: " + report.tests.Count + " checks, " + report.errors.Count + " errors, " + report.captures.Count + " captures");
            EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
        }
    }
}
#endif
