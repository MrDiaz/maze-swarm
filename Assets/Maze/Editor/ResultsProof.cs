#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    [InitializeOnLoad]
    public static class ResultsProof
    {
        const string Key = "Maze.ResultsProof.Active";
        static readonly BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Proof report; static List<Action> steps; static int step, readyFrame;
        static double started = -1, next; static bool prepared, done;
        static string originalSave, originalHero, savedHash, originalPendingPlanJson;
        static bool originalSim; static float originalVolume; static bool originalAudioPause;
        static int originalPendingTier; static SectorDef originalPendingSector; static Game.Goal[] originalPendingPlan;
        static Progress.Loot awardedLoot; static string rewardSnapshot, upgradeSnapshot; static int beforePower;
        static Game G => Object.FindAnyObjectByType<Game>();
        static RunResultsView View => Object.FindAnyObjectByType<RunResultsView>(FindObjectsInactive.Include);
        static string Root => Directory.GetParent(Application.dataPath).FullName;
        static string Out => "Logs/ResultsProof/" + (Environment.GetEnvironmentVariable("UISHOT_SIZE") ?? "2556x1179");
        static ResultsProof() { if (SessionState.GetBool(Key, false)) Hook(); }
        [MenuItem("Maze/Results/Capture and Verify")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start results proof outside Play mode.");
            if (TMPro.TMP_Settings.instance == null)
            {
                started = EditorApplication.timeSinceStartup;
                TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
                EditorApplication.update -= WaitForResources;
                EditorApplication.update += WaitForResources;
                return;
            }
            step = 0; next = 0; prepared = done = false; report = null;
            ResultsArtImport.Apply();
            SessionState.SetBool(Key, true); started = EditorApplication.timeSinceStartup; Hook();
            EditorSceneManager.OpenScene("Assets/Maze/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
        }
        static void WaitForResources()
        {
            if (EditorApplication.timeSinceStartup - started > 120)
            {
                EditorApplication.update -= WaitForResources;
                throw new TimeoutException("TMP resources did not finish importing.");
            }
            if (TMPro.TMP_Settings.instance == null) return;
            EditorApplication.update -= WaitForResources; Run();
        }
        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Logged; Application.logMessageReceived += Logged;
        }
        static void Logged(string message, string stack, LogType type)
        {
            if (done || !SessionState.GetBool(Key, false)) return;
            report ??= new Proof();
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var method = typeof(CryoKitProof).GetMethod("KnownEditorSearchStartup", All);
                bool known = method != null && (bool)method.Invoke(null, new object[] { message, stack, type });
                if (known) report.editorDiagnostics.Add(type + ": " + message + "\n" + stack);
                else report.errors.Add(message + "\n" + stack);
            }
            else if (type == LogType.Warning) report.warnings.Add(message);
        }
        static void Tick()
        {
            if (done) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 360) { Fail(new TimeoutException("Results proof timed out.")); return; }
            if (!EditorApplication.isPlaying || G == null || G.player == null || G.menu == null || View == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                if (report.errors.Count > 0) throw new InvalidOperationException("Runtime error logged.");
                if (G.state == Game.State.Choosing) { Require(G.offer.Count > 0, "Empty card offer."); G.Pick(0); Wait(.3); return; }
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < readyFrame) return;
                if (step == steps.Count) { Finish(); return; }
                steps[step++]();
            }
            catch (Exception e) { Fail(e); }
        }
        static void Prepare()
        {
            report ??= new Proof(); report.unityVersion = Application.unityVersion;
            report.resolution = Environment.GetEnvironmentVariable("UISHOT_SIZE") ?? "2556x1179";
            originalSave = JsonUtility.ToJson(Progress.D); originalSim = Progress.Sim; originalHero = Prefs.HeroOverride;
            originalVolume = AudioListener.volume; originalAudioPause = AudioListener.pause;
            originalPendingTier = Game.PendingTier; originalPendingSector = Game.PendingSector; originalPendingPlan = Game.PendingPlan;
            savedHash = Hash(PlayerPrefs.GetString("ms.progress", "")); report.savedProgressHashBefore = savedHash;
            prepared = true; UiShot.Prepare();
            if (report.resolution == "1847x851") SafeArea.Override = new Rect(0, 0, 1, 1);
            Directory.CreateDirectory(Path.Combine(Root, Out));
            steps = new List<Action>
            {
                () => { StartRun(); Wait(2); },
                () => { Die(); Wait(1.5); },
                () => { VerifyLiveBindings(); CheckInteractionTargets(); Shot("01_actual_death_results"); rewardSnapshot = Snapshot(); awardedLoot = View.DisplayLoot; },
                () => { G.HurtPlayer(999999); View.Show(false, G.floor, awardedLoot); Require(Snapshot() == rewardSnapshot, "Repeated death/display granted rewards again."); report.rewardIdempotencyPassed = true; Wait(.2); },
                () => { beforePower = Progress.Power(); int wallet = Progress.D.credits; Require(View.UpgradeMode == "upgrade", "Affordable fixture has no upgrade action."); View.UpgradeButton.onClick.Invoke(); Require(Progress.D.credits < wallet && Progress.Power() > beforePower, "Actual upgrade callback did not spend/gain power."); upgradeSnapshot = Snapshot(); View.UpgradeButton.onClick.Invoke(); Require(Snapshot() == upgradeSnapshot, "Immediate repeated upgrade purchased twice."); report.upgradePassed = report.upgradeDebouncePassed = true; Wait(.5); },
                () => { Shot("02_after_upgrade"); Progress.D.credits = 0; Progress.D.parts = 0; Progress.D.alloy = 0; View.Show(false, G.floor, new Progress.Loot()); Require(View.UpgradeMode == "armory", "Unaffordable upgrade did not fall back to Armory."); rewardSnapshot = Snapshot(); Wait(.5); },
                () => { Shot("03_no_currency_no_equipment"); View.UpgradeButton.onClick.Invoke(); Require(G.state == Game.State.Menu && G.menu.SheetOpen && !View.Visible, "Armory fallback failed."); Require(Snapshot() == rewardSnapshot, "Unaffordable action changed currencies or progression."); report.insufficientCurrencyPassed = true; Wait(.5); },
                () => { StartRun(); Wait(1); },
                () => { Die(); Wait(.8); },
                () => { string before = Snapshot(); View.RetryButton.onClick.Invoke(); Require(G.run && G.sector.id == "cryo-station" && !View.Visible && (G.state == Game.State.Playing || G.state == Game.State.Choosing), "Retry failed to restart same sector."); string restarted = Snapshot(); View.RetryButton.onClick.Invoke(); Require(Snapshot() == restarted, "Repeated Retry changed progression."); report.retryPassed = report.navigationDebouncePassed = true; Wait(1); },
                () => { Die(); Wait(.8); },
                () => { string before = Snapshot(); View.MenuButton.onClick.Invoke(); View.MenuButton.onClick.Invoke(); Require(G.state == Game.State.Menu && !View.Visible, "Menu action failed."); Require(Snapshot() == before, "Menu action changed rewards."); report.menuPassed = true; Wait(.5); },
                () => { StartRun(); Wait(1); },
                () => { Die(); Wait(.5); },
                () => { var loot = new Progress.Loot { gear = new List<GearItem>() }; for (int n = 0; n < 12; n++) loot.gear.Add(new GearItem { uid = 9000 + n, slot = n % 5, rarity = n % 5, level = 1 }); string before = Snapshot(); View.Show(false, G.floor, loot); Require(Snapshot() == before, "Displaying extra loot awarded fixture inventory."); Require(View.RenderedRewardRows >= 12, "Long reward list truncated."); View.SetRewardPage(11); Require(View.RewardPage == 11, "Variable reward pagination failed."); report.variableRewardsPassed = true; Wait(.5); },
                () => { Shot("04_variable_rewards_paginated"); ReferenceFixture(); Wait(.5); },
                () => { Shot("05_reference_comparison_fixture"); }

            };
            Wait(.3);
        }
        static void StartRun()
        {
            Progress.Sim = true; Progress.UseTemp(new SaveData { credits = 5000, parts = 35, alloy = 25, rank = 2, runs = 5 });
            Prefs.HeroOverride = "vanguard";
            var def = SectorDefs.Get("cryo-station"); Game.PendingTier = def.tier; Game.PendingSector = def; Game.PendingPlan = null;
            G.LoadMap(0); G.menu.Hide(); G.Restart();
            if (Game.TestRun) { G.TestEnd(); Progress.Sim = true; } // temporary proof uses production reward path; no preference writes
            G.DebugFloor(4); G.demo = false; G.godMode = true; G.holdFire = true;
            G.kills = 41; G.level = 7; G.RunScore = 3567; G.events.Tick(0);
        }
        static void Die()
        {
            G.state = Game.State.Playing; G.godMode = false; G.holdFire = true; G.player.dashT = 0;
            G.player.hp = Mathf.Min(G.player.hp, G.player.maxHp * .2f);
            G.HurtPlayer(999999); Require(G.state == Game.State.Dead && View.Visible, "Actual player death did not open results.");
        }
        static void ReferenceFixture()
        {
            // Display-only visual calibration fixture. Production UI never uses these values.
            G.level = 10; G.kills = 159; G.RunScore = 6335; G.XpGained = 58; Progress.D.rank = 3;
            G.OpsToasts.Clear(); G.RankUps.Clear(); G.StarTrack.Clear(); G.newStars.Clear(); G.ShardsGained = 0;
            View.Show(false, 4, new Progress.Loot { credits = 160, parts = 5, alloy = 4, gear = new List<GearItem> { new GearItem { slot = 4, rarity = 1 } } });
            ((TMPro.TMP_Text)typeof(RunResultsView).GetField("upgradeName", All).GetValue(View)).text = "MINIGUN LV 8";
            ((TMPro.TMP_Text)typeof(RunResultsView).GetField("upgradeDetail", All).GetValue(View)).text = "+33 POWER (386 CR)";
        }
        static void VerifyLiveBindings()
        {
            Require(G.run && G.sector.id == "cryo-station", "Wrong source run.");
            var texts = View.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First().GetParsedText());
            Require(texts["LEVELValue"] == G.level.ToString("N0") && texts["KILLSValue"] == G.kills.ToString("N0") && texts["SCOREValue"] == G.RunScore.ToString("N0"), "Run statistics are not live.");
            Require(texts["CurrencyValue0"] == $"{View.DisplayLoot.credits:N0} CR" && texts["CurrencyValue1"] == $"{View.DisplayLoot.parts:N0} PARTS" && texts["CurrencyValue2"] == $"{View.DisplayLoot.alloy:N0} ALLOY", "Reward values are not actual awarded Loot.");
            var picks = Meta.BestUpgrades(3);
            string expectedName = picks[0].name.ToUpperInvariant() + (picks.Count > 1 ? $" +{picks.Count - 1} MORE" : "");
            string expectedDetail = $"{(picks.Count > 1 ? "UP TO " : "")}+{picks.Sum(p => p.gain):N0} POWER ({picks.Sum(p => p.credits):N0} CR)";
            Require(texts["UpgradeName"] == expectedName && texts["UpgradeDetail"] == expectedDetail, "Upgrade bundle label/cost/gain do not match actual planned callbacks.");
            report.bundleSemanticsPassed = true;
            Require(!texts.Values.Any(t => t.IndexOf("a death pays", StringComparison.OrdinalIgnoreCase) >= 0), "Payout sentence remains.");
            Require(!View.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b => b.name == "Sector"), "Sector button remains.");
            report.liveBindingsPassed = true; report.level = G.level; report.kills = G.kills; report.score = G.RunScore;
            foreach (var image in View.GetComponentsInChildren<UnityEngine.UI.Image>()) report.imageEvidence.Add(image.name + " | " + image.sprite?.name + " | color=" + image.color + " | culled=" + image.canvasRenderer.cull + " | rect=" + image.rectTransform.rect.size + " | material=" + image.materialForRendering.name);
            foreach (var t in View.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.gameObject.activeInHierarchy && t.text.Length > 0) report.textEvidence.Add(t.name + " | " + t.text + " | verts=" + t.textInfo.meshInfo.Sum(m => m.vertexCount) + " | font=" + t.fontSize + " | rect=" + t.rectTransform.rect.size);
        }
        static void CheckInteractionTargets()
        {
            Require(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Expected exactly one EventSystem.");
            var canvas = View.GetComponentInParent<Canvas>(); Require(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null, "Results canvas lacks a raycaster.");
            var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            foreach (var button in new[] { View.MenuButton, View.UpgradeButton, View.RetryButton })
            {
                Vector3 point = button.transform.TransformPoint(((RectTransform)button.transform).rect.center);
                var screen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, point);
                var data = new PointerEventData(EventSystem.current) { position = screen };
                var hits = new List<RaycastResult>(); raycaster.Raycast(data, hits);
                Require(hits.Any(h => h.gameObject.transform.IsChildOf(button.transform)), "Button is not a valid raycast target: " + button.name);
            }
            report.raycastTargetsPassed = true;
        }
        static void Shot(string name)
        {
            UiShot.Shot(name, new[] { Out });
            string p = Path.Combine(Root, Out, name + ".png"); var bytes = File.ReadAllBytes(p);
            var tex = new Texture2D(2, 2); Require(tex.LoadImage(bytes), "Invalid rendered PNG.");
            Require(tex.width >= 1800 && tex.height >= 800, "Unexpected landscape resolution.");
            report.captures.Add(new Capture { path = p, bytes = bytes.Length, sha256 = Hash(bytes), width = tex.width, height = tex.height });
            Object.DestroyImmediate(tex); Save();
        }
        static string Snapshot()
        {
            var d = Progress.D;
            return string.Join("|", d.credits, d.parts, d.alloy, d.rank, d.cxp, d.cores, string.Join(",", d.weaponLv),
                string.Join(",", d.heroLv.OrderBy(x => x)), string.Join(",", d.research.OrderBy(x => x)),
                string.Join(",", d.gear.OrderBy(x => x.uid).Select(x => $"{x.uid}:{x.slot}:{x.rarity}:{x.level}")));
        }
        static void Wait(double s) { next = EditorApplication.timeSinceStartup + s; readyFrame = Time.frameCount + 3; }
        static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
        static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));
        static string Hash(byte[] b) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
        static void Save() => File.WriteAllText(Path.Combine(Root, Out, "proof-report.json"), JsonUtility.ToJson(report, true));
        static void Fail(Exception e) { report ??= new Proof(); report.errors.Add(e.ToString()); Complete(false); }
        static void Finish() => Complete(true);
        static void Complete(bool passed)
        {
            if (done) return; done = true;
            if (prepared)
            {
                G?.TestEnd(); Progress.UseTemp(JsonUtility.FromJson<SaveData>(originalSave)); Progress.Sim = originalSim;
                Prefs.HeroOverride = originalHero; Game.PendingTier = originalPendingTier; Game.PendingSector = originalPendingSector; Game.PendingPlan = originalPendingPlan;
                AudioListener.volume = originalVolume; AudioListener.pause = originalAudioPause; SafeArea.Override = null;
                report.savedProgressHashAfter = Hash(PlayerPrefs.GetString("ms.progress", ""));
                if (report.savedProgressHashAfter != savedHash) { report.errors.Add("Saved progression changed."); passed = false; }
            }
            report.passed = passed && report.errors.Count == 0; Directory.CreateDirectory(Path.Combine(Root, Out)); Save();
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Logged;
            Debug.Log("ResultsProof: " + (report.passed ? "PASS" : "FAIL") + "; " + Path.Combine(Root, Out, "proof-report.json"));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.ExitPlaymode();
        }
        [Serializable] class Capture { public string path, sha256; public int bytes, width, height; }
        [Serializable] class Proof
        {
            public bool passed, bundleSemanticsPassed, liveBindingsPassed, raycastTargetsPassed, rewardIdempotencyPassed, upgradePassed, upgradeDebouncePassed, insufficientCurrencyPassed, retryPassed, navigationDebouncePassed, menuPassed, variableRewardsPassed;
            public string unityVersion, resolution, savedProgressHashBefore, savedProgressHashAfter; public int level, kills, score;
            public List<string> errors = new(), warnings = new(), editorDiagnostics = new(), textEvidence = new(), imageEvidence = new(); public List<Capture> captures = new();
            public string[] limits = { "Actual Unity Editor Play-mode rendering; no phone install/performance claim.", "Death uses actual HurtPlayer/RunEnd with a temporary save and deliberately distinct test-run statistics.", "Extra/no-loot views use display-only fixtures to test variable content; they do not award those items.", "Upgrade verification invokes production callbacks; repeated same-frame clicks must be ignored.", "The source is flattened artwork; supplied scenery is a rectangular crop, and alpha/frame extraction is approximate." };
        }
    }
}
#endif
