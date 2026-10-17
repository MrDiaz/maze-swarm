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
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Bounded rendered review. This is an Editor fixture proof, never a phone/FPS proof.
    [InitializeOnLoad]
    public static partial class CohesiveEditorReview
    {
        const string Key = "Maze.CohesiveEditorReview.Active";
        static string Out => Environment.GetEnvironmentVariable("COHESIVE_REVIEW_OUT") ?? "Logs/CohesiveEditorReview";
        static Game G => Object.FindAnyObjectByType<Game>();
        static MenuPresentation P => G.menu.Presentation;
        static string Zone => Environment.GetEnvironmentVariable("COHESIVE_QA_ZONE") ?? "cryo-station";
        [Serializable] sealed class FloorRecord
        {
            public int width, height, floor, openCells, reachableCells, collisionProbes, frozenMarkers, rendererCount, skinnedBoneCount;
            public string layout, gridBefore, gridAfter, cameraConfig, cameraConfigSha256, renderedCamera, fixture;
            public float spitterMoved, spitterWalkBoneDegrees, spitterFrozenBoneDegrees, spitterFrozenPositionDelta;
            public float spitterFreezeInitialSpeed, spitterDriftWhileSettling, spitterSettleGameSeconds, spitterSettledSpeed;
        }
        [Serializable] sealed class Capture
        { public string file, stage, conditions, sha256; public int width, height, floor; }
        [Serializable] sealed class Report
        {
            public string utc, unityVersion, zone, savedBefore, savedAfter;
            public string conditions = "Actual Unity Editor runtime renders through a gameplay + overlay UI RenderTexture stack, inferred landscape safe-area fixtures; opt-in early temporary save. Navigation uses production pointer handlers. Five floors are forced with DebugFloor; selected enemies are forced spawns; direct freeze/death/pool fixtures. No natural victory, phone touch, phone performance or subjective audio claim.";
            public bool preferencesRestored, completed, passed;
            public List<string> tests = new(), errors = new(), warnings = new();
            public List<Capture> captures = new(); public List<FloorRecord> floors = new();
            public List<SpitterAttackEvidence> spitterAttacks = new();
            public List<HeroRecord> heroFixtures = new();
        }
        [Serializable] sealed class SpitterAttackEvidence
        {
            public string fixture = "FORCED one-time placement of the existing thawed Spitter on a clear LOS cell at9-13m; placement velocity cleared. Existing EnemyAI chooses every shot, cooldown, lead, damage and landing. Observe warm-up release, then one full natural repeat. No aiCd assignment or forced firing.";
            public int width, height, floor, fixtureFrame, windupFrame, releaseFrame, impactFrame, puddleFrame;
            public Vector2 originalPlane, fixturePlane, heroAtFixture, windupPlane, releasePlane, landingPlane;
            public Vector3 windupMuzzle, releaseMuzzle, lobFrom, lobTo, flightPosition, telegraphWorld, puddleWorld, boundaryExtents;
            public float fixtureAiCd, warmupAiCd, warmupGameTime, windupAiCd, windupGameTime, windupClipTime, releaseAiCd, releaseGameTime;
            public float releaseWindow, muzzleToLobMetres, telegraphRadius, telegraphLife, telegraphAgeAtRelease, lobLife, lobAgeAtCapture;
            public float impactGameTime, impactGameDelay, telegraphAgeAtImpact, puddleAgeAtCapture, puddleRemaining, puddleRadius, boundaryRadius, materialLife;
            public double windupEditorTime, releaseEditorTime, impactEditorTime;
            public bool windupCasting, windupPreparing, windupEligible, cooldownReset, activeLobChild, activeTelegraph, boundedPurplePuddle;
            public string lobObject, telegraphObject, puddleObject, boundaryObject;
            public Color puddleMid, boundaryColor;
        }

        sealed class CanvasState
        { public Canvas canvas; public RenderMode mode; public Camera camera; public float distance, scale; public CanvasScaler scaler; public CanvasScaler.ScaleMode scaleMode; }
        sealed class BoneState { public Transform bone; public Vector3 position; public Quaternion rotation; }
        static Report report;
        static List<Action> steps;
        static readonly List<CanvasState> canvases = new();
        static readonly List<(Canvas canvas, bool enabled)> previewCanvases = new();
        static int step, earliestFrame, width, height;
        static double started = -1, next, deadline;
        static bool prepared, finished;
        static Func<bool> waiting;
        static Camera ui, main;
        static RenderTexture target, originalTarget;
        static Rect? originalSafe;
        static Rect safePixels;
        static string originalCameraConfig, originalHeroOverride;
        static FloorRecord current;
        static Enemy fixture, spitter;
        static object previousView;
        static Vector2 spitterStart, frozenStart;
        static List<BoneState> walkPose, frozenPose;
        static Vector2 freezeAppliedPosition;
        static float freezeAppliedRunTime, freezeSampleRunTime;

        static SpitterAttackEvidence attackEvidence;
        static CohesiveSpitterPlayback attackMotion;
        static EnemyAI.Tele attackTele;
        static HashSet<object> attackOldPuddles;
        static VfxPool attackPuddle;
        static object attackPuddleRecord;
        static int attackPhase, attackObservedFrame;
        static float attackPreviousCd;

        static CohesiveEditorReview() { if (SessionState.GetBool(Key, false)) Hook(); }
        public static void Run()
        {
            if (Environment.GetEnvironmentVariable("COHESIVE_QA") != "1" || Environment.GetEnvironmentVariable("COHESIVE_QA_MODE") != "editor-review")
                throw new InvalidOperationException("Launch with COHESIVE_QA=1 COHESIVE_QA_MODE=editor-review for earliest save protection.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play mode.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene exists; preserve it before this review.");
            Directory.CreateDirectory(Out);
            SessionState.SetString(Key + ".Save", Hash(PlayerPrefs.GetString("ms.progress", "")));
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
            if (finished || !SessionState.GetBool(Key, false)) return;
            report ??= new Report();
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack);
            else if (type == LogType.Warning) report.warnings.Add(message);
        }
        static void Tick()
        {
            if (finished) return;
            if (started < 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 480) { Finish("Eight-minute review bound exceeded at step " + step); return; }
            if (!EditorApplication.isPlaying || G == null || G.menu?.Presentation == null || G.runUi == null || Camera.main == null) return;
            try
            {
                if (!prepared) { Prepare(); return; }
                if (heroFixtureFailure != null) throw new InvalidOperationException(heroFixtureFailure);
                if (!CohesivePhoneQa.SaveProtected) throw new InvalidOperationException("Early temporary-save guard was lost.");
                if (waiting != null)
                {
                    if (!waiting()) { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("State wait at step " + step); return; }
                    waiting = null;
                }
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < earliestFrame) return;
                if (step >= steps.Count) { Finish(); return; }
                steps[step++]();
            }
            catch (Exception error) { Finish("Step " + (step - 1) + ": " + error); }
        }
        static void Prepare()
        {
            report ??= new Report(); report.utc = DateTime.UtcNow.ToString("O"); report.unityVersion = Application.unityVersion; report.zone = Zone;
            report.savedBefore = SessionState.GetString(Key + ".Save", "");
            Check(CohesivePhoneQa.ExternalReview && CohesivePhoneQa.SaveProtected, "Runtime guard protected progression before startup");
            Check(report.savedBefore == Hash(PlayerPrefs.GetString("ms.progress", "")), "Persisted progression unchanged through menu startup");
            Check(Time.captureFramerate == 0 && Time.captureDeltaTime == 0, "No artificial capture clock");
            main = Camera.main; originalTarget = main.targetTexture; originalSafe = SafeArea.Override;
            originalCameraConfig = JsonUtility.ToJson(G.cfg.camera);
            steps = new List<Action>(); prepared = true;
            Progress.D.runs = Mathf.Max(1, Progress.D.runs);
            originalHeroOverride = Prefs.HeroOverride; Prefs.HeroOverride = null;
            G.player.RebuildHero(G.cfg);
            heroBasePower = Progress.Power();
            if (Environment.GetEnvironmentVariable("COHESIVE_REVIEW_MATERIAL") == "1") { AddMaterialReviewSteps(); return; }
            if (Environment.GetEnvironmentVariable("COHESIVE_REVIEW_GAIT") == "1") { AddGaitReviewSteps(); return; }
            foreach (var size in new[] { new Vector2Int(1334,750), new Vector2Int(1920,1080), new Vector2Int(2796,1290) })
            {
                var resolution = size;
                Add(() => { G.ToMenu(); Progress.Sim = true; G.menu.ReturnToTitle(); Resize(resolution.x, resolution.y); Delay(.8); });
                Add(() => { Check(Title.Visible, "Actual title visible " + Size); Shot("title", "Production title"); Click(Title.PlayButton); Delay(.8); });
                Add(() => { Check(P.Visible && P.Current == MenuPresentation.Screen.Home && !Title.Visible, "Play reaches Hangar " + Size); Shot("hangar", "Production Hangar after title callback"); });
                AddHeroMenuFixtures();
                Add(() => { Click(P.DeployButton); WaitFor(() => MapSelect.I.Open && MapSelect.I.StyledBriefButton.IsActive(), 12); });
                Add(() => { SelectActualZone(); Delay(.5); });
                Add(() => { Shot("zone-card", "Actual selected zone card and pagination callback"); Click(MapSelect.I.StyledBriefButton); Delay(.7); });
                Add(() => { Check(MapSelect.I.StyledDeployButton.IsActive(), "Actual briefing visible " + Size); Shot("briefing", "Production mission briefing"); Click(MapSelect.I.StyledDeployButton); WaitFor(() => G.state == Game.State.Playing && G.run && !G.menu.Busy && !MapSelect.I.Open, 25); });
                Add(() => { Check(G.floor == 1 && G.sector?.id == Zone, "Actual drop reaches requested floor1 " + Size); G.demo = false; G.godMode = true; G.holdFire = true; Delay(3.5); });
                Add(() => { ConvertCanvases(); Shot("run-live", "Production first floor, god mode and held fire for readable review"); Delay(.1); });
                for (int n = 1; n <= (Environment.GetEnvironmentVariable("COHESIVE_REVIEW_QUICK") == "1" ? 0 : 5); n++)
                {
                    int floor = n;
                    string fixtureRole = floor == 2 ? "leaper" : floor == 3 ? "carapace" : floor == 4 ? "bloater" : floor == 5 ? "tick" : "grunt";
                    Add(() =>
                    {
                        if (floor != 1) G.DebugFloor(floor);
                        G.demo = false; G.godMode = true; G.holdFire = true;
                        Delay(.6);
                    });
                    Add(() =>
                    {
                        if (floor != 1) Shot("floor" + floor + "-intro", "Actual production transition card and hint before their normal lifetime elapses; FORCED DebugFloor fixture");
                        Delay(floor == 1 ? .1 : 2.9);
                    });
                    Add(() =>
                    {
                        Check(G.floor == floor && G.sector?.id == Zone, "Forced floor fixture F" + floor + " " + Size);
                        Check(JsonUtility.ToJson(G.cfg.camera) == originalCameraConfig, "Camera configuration unchanged F" + floor + " " + Size);
                        current = InspectFloor(floor); report.floors.Add(current);
                        Shot("floor" + floor, "FORCED DebugFloor(" + floor + "); normal environment/materials/camera, held fire/god mode");
                        G.swarm.Clear(); fixture = Spawn(fixtureRole, 3);
                        fixture.frozen = .65f; Delay(.15);
                    });
                    Add(() =>
                    {
                        Check(!fixture.dead && fixture.frozen > 0, "Forced freeze active F" + floor + " " + Size);
                        Markers("Forced freeze");
                        Shot("floor" + floor + "-forced-freeze", "FORCED " + fixtureRole + ".frozen=.65s; status visuals use actual lifetime");
                        WaitFor(() => fixture.frozen <= 0, 8);
                    });
                    Add(() =>
                    {
                        Check(fixture.frozen <= 0, "Forced enemy naturally thawed F" + floor + " " + Size);
                        Markers("Natural thaw after forced status");
                        fixture.frozen = 2; previousView = fixture.view; G.KillEnemy(fixture); Delay(.2);
                    });
                    Add(() =>
                    {
                        Check(fixture.dead, "Forced death applied F" + floor + " " + Size); Markers("Frozen death cleanup");
                        G.swarm.Clear(); Delay(.2);
                    });
                    Add(() =>
                    {
                        fixture = Spawn(fixtureRole, 3);
                        Check(fixture.frozen <= 0 && !fixture.dead, "Fresh/reused enemy has clean status F" + floor + " " + Size);
                        report.tests.Add("INFO " + fixtureRole + " view reference reused=" + ReferenceEquals(previousView, fixture.view) + " F" + floor + " " + Size);
                        Delay(.15);
                    });
                    Add(() => { Markers("Pool respawn cleanup"); current.gridAfter = GridHash(); if (current.gridAfter != current.gridBefore) report.warnings.Add("Grid changed while live floor mechanics advanced F" + floor + " " + Size + "; inspect hashes/doors before attributing it to art."); Delay(.1); });
                    if (floor == 1)
                    {
                        Add(() => { G.swarm.Clear(); spitter = Spawn("spitter", 3); spitter.spawnT = 1; spitterStart = spitter.p; walkPose = Pose(spitter); Delay(1.3); });
                        Add(() =>
                        {
                            current.spitterMoved = Vector2.Distance(spitterStart, spitter.p);
                            current.spitterWalkBoneDegrees = PoseAngle(walkPose);
                            current.skinnedBoneCount = walkPose.Count;
                            Check(current.spitterMoved > .05f, "Actual spitter AI retreat moved forced spawn " + Size);
                            Check(current.skinnedBoneCount >= 4, "Spitter has an actual skinned rig " + Size);
                            Check(current.spitterWalkBoneDegrees > .2f, "Actual spitter walk changes bone pose " + Size);
                            Shot("spitter-motion", "FORCED spitter spawn at3m and mature spawnT; actual EnemyAI and authored playback for1.3s");
                            if (width == 1920) BeginScenePreview();
                            Delay(.35);
                        });
                        Add(() =>
                        {
                            if (width == 1920)
                            {
                                try { Shot("cargo-arena-scene-only", "Actual production camera/environment and live authored Spitter; forced fixture spawn; all canvases temporarily hidden for selector preview; no camera/layout edits"); }
                                finally { RestoreScenePreview(); }
                            }
                            current.spitterFreezeInitialSpeed = spitter.v.magnitude;
                            freezeAppliedPosition = spitter.p; freezeAppliedRunTime = G.RunTime;
                            spitter.frozen = 2f; // Forced status duration only; preserve the game's existing velocity decay/separation.
                            WaitFor(() => spitter.v.magnitude < .08f || spitter.frozen <= 0, 10);
                        });
                        Add(() =>
                        {
                            Check(spitter.frozen > .3f && spitter.v.magnitude < .08f, "Frozen spitter existing velocity settled before residual-drift sample " + Size);
                            current.spitterSettledSpeed = spitter.v.magnitude;
                            current.spitterDriftWhileSettling = Vector2.Distance(freezeAppliedPosition, spitter.p);
                            current.spitterSettleGameSeconds = G.RunTime - freezeAppliedRunTime;
                            frozenStart = spitter.p; frozenPose = Pose(spitter); freezeSampleRunTime = G.RunTime;
                            WaitFor(() => G.RunTime - freezeSampleRunTime >= .25f || spitter.frozen <= 0, 10);
                        });
                        Add(() =>
                        {
                            current.spitterFrozenPositionDelta = Vector2.Distance(frozenStart, spitter.p);
                            current.spitterFrozenBoneDegrees = PoseAngle(frozenPose);
                            Check(spitter.frozen > 0, "Residual-drift sample stayed inside actual frozen lifetime " + Size);
                            Check(current.spitterFrozenPositionDelta < .03f, "Settled frozen spitter residual drift below3cm; existing velocity decay retained " + Size);
                            Check(current.spitterFrozenBoneDegrees < 1, "Frozen spitter holds authored bone pose " + Size);
                            Markers("Forced spitter freeze"); Shot("spitter-frozen", "FORCED 2s freeze; residual drift/pose sampled after actual velocity settled below0.08m/s; existing knockback/separation unchanged; game-clock .25s window"); WaitFor(() => spitter.frozen <= 0, 10);
                        });
                        if (resolution.x == 1920)
                        {
                            Add(() => { Check(spitter.frozen <= 0, "Spitter thaws via normal countdown " + Size); Delay(.1); });
                            Add(BeginSpitterAttackEvidence);
                            Add(() => { G.swarm.Clear(); Delay(.1); });
                        }
                        else
                            Add(() => { Check(spitter.frozen <= 0, "Spitter thaws via normal countdown " + Size); G.swarm.Clear(); Delay(.1); });
                    }
                }
                Add(() => { G.ToMenu(); Progress.Sim = true; Delay(.8); });
                Add(() => { Check(P.Visible && P.Current == MenuPresentation.Screen.Home, "Regression returns to Hangar " + Size); Shot("returned-hangar", "Forced fixture regression ended; actual menu restore"); });
                if (resolution.x == 1920) AddHeroRuntimeFixtures();
            }
            Delay(.2);
        }
        static void BeginSpitterAttackEvidence()
        {
            Check(width == 1920 && G.floor == 1 && !spitter.dead && spitter.frozen <= 0, "Repeat attack starts after actual spitter thaw at1920");
            Check(CohesiveZone.For(G) && Meta.Difficulty != 2, "Cargo normal-duration attack fixture; Nightmare shortening is not selected");
            attackMotion = spitter.view?.model?.GetComponent<CohesiveSpitterPlayback>();
            Check(attackMotion != null && attackMotion.Ready, "Existing authored playback available without invoking an attack");
            var candidates = new List<Vector2>();
            for (int z = 1; z < MazeMap.H - 1; z++) for (int x = 1; x < MazeMap.W - 1; x++)
            {
                var p = MazeMap.Center(x, z); float distance = Vector2.Distance(p, G.player.p);
                if (distance < 9.5f || distance > 12 || !G.swarm.Clear(p, Mathf.Max(.6f, spitter.r)) || !Swarm.ClearLine(p, G.player.p)) continue;
                var screen = main.WorldToViewportPoint(MazeMap.ToWorld(p, .6f));
                if (screen.z > 0 && screen.x > .08f && screen.x < .92f && screen.y > .12f && screen.y < .9f) candidates.Add(p);
            }
            Check(candidates.Count > 0, "Deterministic visible safe LOS placement exists for natural repeat attack");
            var selected = candidates.OrderBy(p => Mathf.Abs(Vector2.Distance(p, G.player.p) - 10.5f)).ThenBy(p => p.y).ThenBy(p => p.x).First();
            attackEvidence = new SpitterAttackEvidence { width = width, height = height, floor = G.floor, fixtureFrame = Time.frameCount,
                originalPlane = spitter.p, fixturePlane = selected, heroAtFixture = G.player.p, fixtureAiCd = spitter.aiCd, releaseWindow = CohesiveSpitterPlayback.ReleaseSeconds };
            // The only new fixture mutation: one initial placement. Do not touch AI, health, spawnT, timers or damage.
            spitter.p = selected; spitter.v = Vector2.zero;
            attackPreviousCd = spitter.aiCd; attackObservedFrame = -1; attackPhase = 0;
            attackTele = null; attackPuddle = null; attackPuddleRecord = null;
            report.spitterAttacks.Add(attackEvidence);
            WaitFor(ObserveSpitterAttack, 35);
        }

        static bool ObserveSpitterAttack()
        {
            if (Time.frameCount == attackObservedFrame) return false;
            attackObservedFrame = Time.frameCount;
            if (spitter.dead || G.state != Game.State.Playing || G.floor != 1)
                throw new InvalidOperationException("Natural repeat fixture left the live first floor before evidence completed.");
            float cd = spitter.aiCd;
            bool reset = cd > attackPreviousCd + .5f;
            attackPreviousCd = cd;
            if (attackPhase == 0)
            {
                if (!reset) return false;
                // Observe a real release first, so the subsequent wind-up is a repeat through a full cooldown.
                Check(TryActualSpit(out _, out _), "Warm-up release reset has a corresponding live glob and telegraph");
                attackEvidence.warmupAiCd = cd; attackEvidence.warmupGameTime = G.RunTime;
                attackPhase = 1; return false;
            }
            if (attackPhase == 1)
            {
                bool preparing = Read<bool>(attackMotion, "preparing");
                if (!(cd > .08f && cd <= CohesiveSpitterPlayback.ReleaseSeconds * .75f && attackMotion.Casting && preparing)) return false;
                attackEvidence.windupFrame = Time.frameCount; attackEvidence.windupAiCd = cd; attackEvidence.windupGameTime = G.RunTime;
                attackEvidence.windupEditorTime = EditorApplication.timeSinceStartup; attackEvidence.windupCasting = attackMotion.Casting;
                attackEvidence.windupPreparing = preparing; attackEvidence.windupEligible = Vector2.Distance(spitter.p, G.player.p) <= 16 && spitter.spawnT >= 1 && Swarm.ClearLine(spitter.p, G.player.p);
                attackEvidence.windupPlane = spitter.p; attackEvidence.windupMuzzle = attackMotion.MuzzleWorld;
                attackEvidence.windupClipTime = Read<AnimationState>(attackMotion, "spit").time;
                Check(attackEvidence.windupEligible, "Observed BeforeShot preparing/Casting in the real positive final release window");
                Shot("spitter-windup", "FORCED initial LOS placement only; natural repeat cooldown=" + cd.ToString("F3") + "s within real ReleaseSeconds; existing BeforeShot preparing/Casting and authored sampled Spit pose");
                attackPhase = 2; return false;
            }
            if (attackPhase == 2)
            {
                if (!reset) return false;
                Check(cd >= 2.65f && cd <= 3.35f, "Repeat attack reset stays in the unchanged randomized3s cooldown range");
                Check(TryActualSpit(out var pooled, out attackTele), "Natural repeat reset corresponds to live ReadFx4 child and EnemyTele");
                var lob = Read<Transform>(pooled, "t");
                attackEvidence.releaseFrame = Time.frameCount; attackEvidence.releaseAiCd = cd; attackEvidence.cooldownReset = true;
                attackEvidence.releaseGameTime = G.RunTime; attackEvidence.releaseEditorTime = EditorApplication.timeSinceStartup;
                attackEvidence.releasePlane = spitter.p; attackEvidence.releaseMuzzle = attackMotion.MuzzleWorld;
                attackEvidence.lobFrom = Read<Vector3>(pooled, "a"); attackEvidence.lobTo = Read<Vector3>(pooled, "b");
                attackEvidence.flightPosition = lob.position; attackEvidence.lobAgeAtCapture = Read<float>(pooled, "age"); attackEvidence.lobLife = Read<float>(pooled, "life");
                attackEvidence.lobObject = lob.name; attackEvidence.activeLobChild = lob.gameObject.activeInHierarchy && lob.IsChildOf(ReadabilityFx.I.transform);
                attackEvidence.telegraphObject = attackTele.tr.name; attackEvidence.activeTelegraph = attackTele.tr.gameObject.activeInHierarchy;
                attackEvidence.telegraphWorld = attackTele.tr.position; attackEvidence.landingPlane = MazeMap.ToPlane(attackTele.tr.position);
                attackEvidence.telegraphRadius = attackTele.tr.localScale.x; attackEvidence.telegraphLife = attackTele.life; attackEvidence.telegraphAgeAtRelease = attackTele.t;
                attackEvidence.muzzleToLobMetres = Vector3.Distance(attackEvidence.releaseMuzzle, attackEvidence.lobFrom);
                Check(attackEvidence.activeLobChild && attackEvidence.activeTelegraph && Mathf.Abs(attackTele.life - .8f) < .001f && Mathf.Abs(attackEvidence.lobLife - .8f) < .001f, "Actual flight and target use unchanged0.8s lifetimes");
                Check(Vector2.Distance(MazeMap.ToPlane(attackEvidence.lobTo), attackEvidence.landingPlane) < .01f && attackEvidence.muzzleToLobMetres < .6f, "Actual glob endpoints correspond to authored muzzle and target ring");
                attackOldPuddles = new HashSet<object>(ExistingPuddles());
                Shot("spitter-flight", "Natural repeat EnemyAI cooldown reset; active actual ReadFx4 glob/trail and matching EnemyTele; no firing/timer injection; pool ages recorded separately from game clock");
                attackPhase = 3; return false;
            }
            if (attackPhase == 3)
            {
                foreach (var puddle in ExistingPuddles())
                {
                    if (attackOldPuddles.Contains(puddle) || Vector2.Distance(Read<Vector2>(puddle, "p"), attackEvidence.landingPlane) > .01f) continue;
                    var view = Read<VfxPool>(puddle, "v"); if (view?.tr == null) continue;
                    attackPuddleRecord = puddle; attackPuddle = view; break;
                }
                if (attackPuddle == null) return false;
                attackEvidence.impactFrame = Time.frameCount; attackEvidence.impactGameTime = G.RunTime;
                attackEvidence.impactGameDelay = G.RunTime - attackEvidence.releaseGameTime; attackEvidence.impactEditorTime = EditorApplication.timeSinceStartup;
                attackEvidence.telegraphAgeAtImpact = attackTele.t;
                Check(!attackTele.tr.gameObject.activeSelf && attackTele.t >= .8f && attackTele.t < .91f, "Existing telegraph callback lands after its normal0.8s game clock");
                Check(attackEvidence.impactGameDelay >= .74f && attackEvidence.impactGameDelay <= .91f, "Observed release-to-landing delay follows unchanged0.8s callback");
                attackPhase = 4; return false;
            }
            if (attackPhase == 4)
            {
                float age = Read<float>(attackPuddleRecord, "age");
                if (age < .35f) return false; // Actual normal growth-in; never set life/age or force a landing.
                var boundary = attackPuddle.tr.Find("CargoSpitBoundary");
                Check(boundary != null && boundary.gameObject.activeInHierarchy, "Natural landing creates the bounded CargoSpitBoundary");
                var renderer = boundary.GetComponent<MeshRenderer>();
                attackEvidence.puddleFrame = Time.frameCount; attackEvidence.puddleWorld = attackPuddle.tr.position;
                attackEvidence.puddleObject = attackPuddle.tr.name; attackEvidence.boundaryObject = boundary.name;
                attackEvidence.puddleAgeAtCapture = age; attackEvidence.puddleRemaining = Read<float>(attackPuddleRecord, "t");
                attackEvidence.puddleRadius = Read<float>(attackPuddleRecord, "r"); attackEvidence.boundaryRadius = boundary.localScale.x;
                attackEvidence.boundaryExtents = renderer.bounds.extents; attackEvidence.materialLife = attackPuddle.m.GetFloat("_Life");
                attackEvidence.puddleMid = attackPuddle.m.GetColor("_Mid"); attackEvidence.boundaryColor = renderer.sharedMaterial.GetColor("_BaseColor");
                attackEvidence.boundedPurplePuddle = attackEvidence.puddleMid.b > attackEvidence.puddleMid.r && attackEvidence.puddleMid.r > attackEvidence.puddleMid.g &&
                    Mathf.Abs(attackEvidence.puddleRadius - 1.4f) < .01f && Mathf.Abs(attackEvidence.boundaryRadius - 1.4f) < .01f &&
                    renderer.bounds.extents.x <= 1.45f && renderer.bounds.extents.z <= 1.45f && attackEvidence.materialLife > .9f;
                Check(attackEvidence.boundedPurplePuddle, "Actual new landing puddle is purple, grown and bounded to unchanged1.4m radius");
                Shot("spitter-landing", "Natural repeat callback after0.8 game seconds; corresponding new purple puddle after normal0.35s growth; actual1.4m boundary/material/positions recorded; god mode protects the review hero");
                attackPhase = 5; Save(); return true;
            }
            return attackPhase == 5;
        }

        static bool TryActualSpit(out object pooled, out EnemyAI.Tele tele)
        {
            pooled = null; tele = null;
            var source = Read<System.Collections.IEnumerable>(ReadabilityFx.I, "active");
            var telegraphs = ReadStatic<System.Collections.IEnumerable>(typeof(EnemyAI.Tele), "live");
            foreach (var candidate in source)
            {
                if (Read<int>(candidate, "kind") != 4) continue;
                var tr = Read<Transform>(candidate, "t");
                if (tr == null || tr.name != "ReadFx4" || !tr.gameObject.activeInHierarchy || !tr.IsChildOf(ReadabilityFx.I.transform)) continue;
                if (Vector3.Distance(Read<Vector3>(candidate, "a"), attackMotion.MuzzleWorld) > .6f) continue;
                foreach (EnemyAI.Tele target in telegraphs)
                {
                    if (target.tr == null || target.tr.name != "EnemyTele" || !target.tr.gameObject.activeInHierarchy || target.ring || Mathf.Abs(target.life - .8f) > .001f) continue;
                    if (Vector2.Distance(MazeMap.ToPlane(Read<Vector3>(candidate, "b")), MazeMap.ToPlane(target.tr.position)) > .01f || Mathf.Abs(target.tr.localScale.x - 1.4f) > .01f) continue;
                    pooled = candidate; tele = target; return true;
                }
            }
            return false;
        }
        static IEnumerable<object> ExistingPuddles()
        { foreach (var value in ReadStatic<System.Collections.IEnumerable>(typeof(EnemyAI), "puddles")) yield return value; }
        static T Read<T>(object instance, string name)
        {
            var field = instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return (T)field.GetValue(instance);
        }
        static T ReadStatic<T>(Type type, string name)
        {
            var field = type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return (T)field.GetValue(null);
        }

        static TitleScreenView Title => G.menu.GetComponent<TitleScreenView>();
        static string Size => width + "x" + height;
        static void Add(Action action) => steps.Add(action);
        static void Delay(double seconds = .35) { next = EditorApplication.timeSinceStartup + seconds; earliestFrame = Time.frameCount + 3; }
        static void WaitFor(Func<bool> predicate, double seconds) { waiting = predicate; deadline = EditorApplication.timeSinceStartup + seconds; Delay(.3); }
        static void Check(bool ok, string message) { report.tests.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
        static Button Button(Component root, string name) => root.GetComponentsInChildren<Button>(false).Single(b => b.name == name && b.IsActive() && b.IsInteractable());
        static void Click(Button button)
        {
            ConvertCanvases(); Canvas.ForceUpdateCanvases();
            Check(button != null && button.IsActive() && button.IsInteractable(), "Button active " + button?.name);
            var bounds = Bounds((RectTransform)button.transform);
            Check(bounds.xMin >= safePixels.xMin - 2 && bounds.xMax <= safePixels.xMax + 2 && bounds.yMin >= safePixels.yMin - 2 && bounds.yMax <= safePixels.yMax + 2, button.name + " safe-area contained " + Size);
            var data = new PointerEventData(EventSystem.current) { position = bounds.center, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            var hit = hits.FirstOrDefault(h => ExecuteEvents.GetEventHandler<IPointerClickHandler>(h.gameObject) != null);
            Check(hit.gameObject != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) == button.gameObject, "Centre raycast reaches " + button.name + " " + Size);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }
        static void SelectActualZone()
        {
            var map = MapSelect.I; int targetIndex = Array.FindIndex(MapSelect.Sectors, s => s.def?.id == Zone);
            Check(targetIndex >= 0, "Requested zone exists");
            while (map.StyledZonePage > 0) Click(Button(map, "Previous"));
            for (int p = 0; p < map.StyledZonePageCount; p++)
            {
                for (int card = 0; card < 3; card++)
                    if (map.StyledVisibleZoneIndex(card) == targetIndex)
                    { Click(Button(map, "ZoneCard" + card)); Check(MapSelect.Selected == targetIndex, "Actual zone card selected requested ID"); return; }
                if (p + 1 < map.StyledZonePageCount) Click(Button(map, "Next"));
            }
            throw new InvalidOperationException("Target zone not reachable by pagination");
        }
        static void Resize(int w, int h)
        {
            width = w; height = h;
            if (target != null) { main.targetTexture = null; if (ui != null) ui.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            target = new RenderTexture(w,h,24,RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "CohesiveEditorReviewTarget" }; target.Create(); main.targetTexture = target;
            if (ui == null)
            {
                ui = new GameObject("CohesiveEditorReviewUiCamera").AddComponent<Camera>(); ui.cullingMask = 1 << 5; ui.orthographic = true;
                ui.nearClipPlane = .1f; ui.farClipPlane = 200; ui.transform.position = new Vector3(0,-2000,0);
                ui.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay; main.GetUniversalAdditionalCameraData().cameraStack.Add(ui);
            }
            ui.targetTexture = target;
            float left = w / (float)h > 2 ? Mathf.Round(199 * w / 2868f) : 0;
            float bottom = w / (float)h > 2 ? Mathf.Round(71 * h / 1320f) : 0;
            safePixels = new Rect(left,bottom,w-2*left,h-bottom);
            SafeArea.Override = new Rect(left/w,bottom/h,1-2*left/w,1-bottom/h); ConvertCanvases();
        }
        static void ConvertCanvases()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (!c.isRootCanvas) continue;
                if (!canvases.Any(x => x.canvas == c))
                {
                    var sc = c.GetComponent<CanvasScaler>();
                    canvases.Add(new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, distance = c.planeDistance, scaler = sc, scale = sc != null ? sc.scaleFactor : 1, scaleMode = sc != null ? sc.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize });
                }
                int order = c.sortingOrder; c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ui; c.planeDistance = 50; c.sortingOrder = order;
                var scaler = c.GetComponent<CanvasScaler>();
                if (scaler != null && c.name != "TitleScreenCanvas") { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = height/1080f; }
            }
        }
        static Rect Bounds(RectTransform rt)
        {
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(ui,corners[0]); var b = RectTransformUtility.WorldToScreenPoint(ui,corners[2]);
            return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        static void BeginScenePreview()
        {
            RestoreScenePreview();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                previewCanvases.Add((canvas, canvas.enabled));
                canvas.enabled = false;
            }
        }
        static void RestoreScenePreview()
        {
            foreach (var entry in previewCanvases) if (entry.canvas != null) entry.canvas.enabled = entry.enabled;
            previewCanvases.Clear();
        }
        static void Shot(string label, string conditions)
        {
            ConvertCanvases(); Canvas.ForceUpdateCanvases();
            var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            RenderTexture.active = old; var bytes = image.EncodeToPNG(); Object.DestroyImmediate(image);
            string path = Path.Combine(Out,label+"_"+Size+".png"); File.WriteAllBytes(path,bytes);
            report.captures.Add(new Capture { file = path, stage = label, conditions = conditions, width = width, height = height, floor = G.floor, sha256 = Hash(bytes) }); Save();
        }
        static FloorRecord InspectFloor(int floor)
        {
            var row = new FloorRecord { width = width, height = height, floor = floor, layout = MazeMap.LayoutName(MazeMap.MapIndex), gridBefore = GridHash(), cameraConfig = JsonUtility.ToJson(G.cfg.camera), renderedCamera = "fov="+main.fieldOfView+" ortho="+main.orthographic+" position="+main.transform.position+" euler="+main.transform.eulerAngles, fixture = "FORCED DebugFloor; current production geometry/grid/collision" };
            row.cameraConfigSha256 = Hash(row.cameraConfig);
            var visited = new HashSet<int>(); var queue = new Queue<Vector2Int>();
            int sx = Mathf.FloorToInt(G.player.p.x/MazeMap.C), sz = Mathf.FloorToInt(G.player.p.y/MazeMap.C);
            Check(MazeMap.Walkable(sx,sz), "Hero spawn on walkable floor F"+floor+" "+Size);
            queue.Enqueue(new Vector2Int(sx,sz)); visited.Add(sz*MazeMap.W+sx);
            var dirs = new[] { Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down };
            while(queue.Count>0)
            {
                var at=queue.Dequeue();
                foreach(var d in dirs){var n=at+d;if(MazeMap.Walkable(n.x,n.y)&&visited.Add(n.y*MazeMap.W+n.x))queue.Enqueue(n);}
            }
            return CompleteFloor(row, visited);
        }
        static FloorRecord CompleteFloor(FloorRecord row, HashSet<int> visited)
        {
            row.reachableCells = visited.Count;
            var dirs = new[] { Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down };
            for(int z=0;z<MazeMap.H;z++) for(int x=0;x<MazeMap.W;x++)
            {
                if(!MazeMap.Walkable(x,z))continue; row.openCells++;
                foreach(var d in dirs)
                {
                    int nx=x+d.x,nz=z+d.y;
                    if(nx<0||nz<0||nx>=MazeMap.W||nz>=MazeMap.H||!MazeMap.IsSolidMove(MazeMap.grid[nz,nx]))continue;
                    var c=MazeMap.Center(x,z);var normal=new Vector2(d.x,d.y);var boundary=c+normal*(MazeMap.C*.5f);var probe=boundary+normal*.05f;
                    MazeMap.CollideGrid(ref probe,G.cfg.player.radius);
                    Check(Vector2.Dot(probe-boundary,normal)<=-G.cfg.player.radius+.015f,"Grid circle collision at "+x+","+z+" side"+d+" F"+row.floor+" "+Size);row.collisionProbes++;
                }
            }
            Check(row.collisionProbes>0,"Solid/open grid boundary probes exist F"+row.floor+" "+Size);
            if(row.reachableCells<row.openCells)report.warnings.Add("Current closed-door/isolated walkable cells F"+row.floor+" "+Size+": "+row.reachableCells+"/"+row.openCells+" reachable without opening doors.");
            foreach(var renderer in Object.FindObjectsByType<Renderer>())
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;row.rendererCount++;
                foreach(var m in renderer.sharedMaterials)Check(m!=null&&m.shader!=null&&m.shader.isSupported,"Valid material "+renderer.name+" F"+row.floor+" "+Size);
            }
            return row;
        }
        static Enemy Spawn(string type, float distance)
        {
            int index=G.swarm.TypeByName(type);Check(index>=0,"Enemy type exists: "+type);
            Check(G.swarm.OpenSpot(G.player.p,distance,distance+3,.5f,out var at),"Open forced enemy fixture spot: "+type);
            var e=G.swarm.Spawn(at,Mathf.Max(1,G.wave),index);Check(e!=null,"Forced enemy spawned: "+type);
            if (CohesiveZone.For(G) && e != null)
            {
                string role = CohesiveCharacterPolicy.RoleFor(e.type);
                if (!string.IsNullOrEmpty(role))
                {
                    var model = e.view?.model;
                    Check(model != null, "Cargo authored model exists for " + type + " " + Size);
                    if (model != null)
                    {
                        var source = CohesiveCharacterPolicy.AuthoredEnemyPrefab(e.type);
                        var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        var sourceMeshes = source != null ? source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray() : Array.Empty<Mesh>();
                        Check(sourceMeshes.Length > 0 && skins.Length == sourceMeshes.Length && skins.All(r => sourceMeshes.Contains(r.sharedMesh)),
                            "Production Cargo spawn uses exact authored " + role + " source meshes for " + type + " " + Size);
                        // Spitter binds its driver on the first normal SyncModel frame. Its
                        // later real walk/freeze/repeat-attack fixtures prove playback readiness.
                        if (role != "spitter")
                            Check(model.GetComponent<CohesiveFamilyPlayback>() != null && model.GetComponent<CohesiveFamilyPlayback>().Ready,
                                "Production Cargo spawn selects ready authored " + role + " driver for " + type + " " + Size);
                        Check(skins.Length > 0 && skins.All(r => r.quality == SkinQuality.Bone4), "Production Cargo " + role + " preserves four skin weights " + Size);
                    }
                }
            }
            return e;
        }
        static void Markers(string label)
        {
            if(!FirstCryoFx.Active){report.warnings.Add(label+": FirstCryoFx marker scope inactive F"+G.floor+" "+Size+"; inspect captured status representation.");return;}
            int cap=CohesiveZone.For(G)?64:24;
            int expected=Mathf.Min(cap,G.swarm.enemies.Count(e=>!e.dead&&e.frozen>0&&!e.type.nest&&!e.Hidden));
            int actual=FirstCryoFx.Get().FrozenMarkerCount;current.frozenMarkers=Mathf.Max(current.frozenMarkers,actual);
            Check(actual==expected,label+" markers follow actual frozen/dead/view lifetime F"+G.floor+" "+Size);
            Check(FirstCryoFx.Get().FrozenMarkerPoolCount<=cap,"Freeze marker pool bounded F"+G.floor+" "+Size);
        }
        static List<BoneState> Pose(Enemy e)
        {
            return e.view.model==null?new List<BoneState>():e.view.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.bones).Where(t=>t!=null).Distinct().Select(t=>new BoneState{bone=t,position=t.localPosition,rotation=t.localRotation}).ToList();
        }
        static float PoseAngle(List<BoneState> pose)=>pose.Count==0?0:pose.Max(p=>p.bone==null?0:Quaternion.Angle(p.rotation,p.bone.localRotation));
        static string GridHash()
        {
            var rows=new StringBuilder();for(int z=0;z<MazeMap.H;z++){for(int x=0;x<MazeMap.W;x++)rows.Append(MazeMap.grid[z,x]);rows.Append('\n');}return Hash(rows.ToString());
        }
        static void Save()=>File.WriteAllText(Path.Combine(Out,"report.json"),JsonUtility.ToJson(report,true));
        static void Finish(string error=null)
        {
            if(finished)return;finished=true;report??=new Report();if(error!=null)report.errors.Add(error);
            void Cleanup(Action action, string label) { try { action(); } catch (Exception e) { report.errors.Add(label + ": " + e); } }
            if(prepared)
            {
                Cleanup(EndMaterialProbe, "Material fixture cleanup");
                Cleanup(EndGaitReview, "Gait fixture cleanup");
                Cleanup(EndHeroFixture, "Hero fixture cleanup");
                Cleanup(RestoreScenePreview, "Preview cleanup");
                Cleanup(() => { if(G!=null)G.ToMenu();Progress.Sim=true; }, "Menu cleanup");
                foreach(var c in canvases) Cleanup(() => { if(c.canvas!=null){c.canvas.renderMode=c.mode;c.canvas.worldCamera=c.camera;c.canvas.planeDistance=c.distance;if(c.scaler!=null){c.scaler.uiScaleMode=c.scaleMode;c.scaler.scaleFactor=c.scale;}} }, "Canvas cleanup");
                Cleanup(() => { if(main!=null){if(ui!=null)main.GetUniversalAdditionalCameraData().cameraStack.Remove(ui);main.targetTexture=originalTarget;} }, "Camera cleanup");
                Cleanup(() => { if(ui!=null)Object.DestroyImmediate(ui.gameObject);if(target!=null){target.Release();Object.DestroyImmediate(target);}SafeArea.Override=originalSafe;Prefs.HeroOverride=originalHeroOverride; }, "Target cleanup");
                // Guard restoration is attempted independently even if scene cleanup failed.
                Cleanup(() =>
                {
                    CohesivePhoneQa.FinishExternalReview(report.errors.Count==0,error);
                    report.savedAfter=Hash(PlayerPrefs.GetString("ms.progress",""));
                    string guardOutput=File.ReadAllText(Path.Combine(Application.persistentDataPath,"CohesiveQA","latest.txt"));
                    var guardReport=JsonUtility.FromJson<CohesivePhoneQa.Report>(File.ReadAllText(Path.Combine(guardOutput,"report.json")));
                    report.preferencesRestored=guardReport.preferencesRestored&&guardReport.progressStringUnchanged&&report.savedBefore==report.savedAfter;
                    if(!report.preferencesRestored)report.errors.Add("Preferences/progress did not restore exactly after protected review; inspect runtime guard report.");
                }, "Unconditional preference restoration");
            }
            report.completed=true;report.passed=report.errors.Count==0&&report.preferencesRestored;Directory.CreateDirectory(Out);Save();
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Logged;
            Debug.Log("CohesiveEditorReview complete: "+report.tests.Count+" checks, "+report.errors.Count+" errors; "+Out);
            EditorApplication.Exit(report.passed?0:1);
        }
        static string Hash(string s)=>Hash(Encoding.UTF8.GetBytes(s));
        static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
}
#endif
