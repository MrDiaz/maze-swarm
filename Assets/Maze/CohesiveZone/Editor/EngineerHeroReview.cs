#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace Maze.EditorTools
{
    public static partial class CohesiveEditorReview
    {
        [Serializable] sealed class HeroRecord
        {
            public string hero, phase, condition;
            public float rightPalmError, leftPalmError, muzzleForwardDot, facingForwardDot, height;
            public int finiteSkinVertices;
            public string rightArm, leftArm, skinScales;
        }
        static bool heroFixtureActive;
        static Vector2 heroFixtureVelocity, heroFixtureOriginalPosition;
        static bool heroFixtureDead, heroFixtureFiring;
        static Quaternion[] heroLiveBones, heroDeathBones;
        static float heroBasePower;
        static EngineerHeroFixtureDriver heroFixtureDriver;
        static Animation heroFixtureAnimation;
        static bool fixtureOldGameEnabled, fixtureOldPlayerEnabled, fixtureOldAnimationEnabled;
        static int heroFixtureSampleFrame;
        static string heroFixtureFailure;
        static readonly MethodInfo HeroLateUpdate = typeof(Player).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        // Runs once per real player frame, after ordinary LateUpdate ordering and before rendering.
        // The fixture disables only the owned Game/Player/Animation updates; it samples the real
        // selected clips, calls the real Player pose/IK path, then leaves that pose for capture.
        public static void AdvanceHeroFixture()
        {
            if (!heroFixtureActive || G == null) return;
            try
            {
                float dt = Environment.GetEnvironmentVariable("COHESIVE_REVIEW_MATERIAL") == "1" ? 0 : Mathf.Min(.05f, Time.unscaledDeltaTime);
                G.player.shootT = heroFixtureFiring ? .5f : 0;
                G.player.hasTarget = heroFixtureFiring;
                if (Prefs.Engineer && !heroFixtureDead) G.player.p += heroFixtureVelocity * dt;
                G.player.Visuals(dt, G.cfg, heroFixtureVelocity, heroFixtureDead);
                if (!Prefs.Engineer)
                {
                    string name = Read<string>(G.player, "currentClip");
                    var state = heroFixtureAnimation[name];
                    float time = state.time + dt * state.speed;
                    foreach (AnimationState other in heroFixtureAnimation) { other.enabled = false; other.weight = 0; }
                    state.enabled = true; state.weight = 1;
                    state.time = heroFixtureDead ? Mathf.Min(state.length, time) : Mathf.Repeat(time, state.length);
                    heroFixtureAnimation.Sample();
                }
                HeroLateUpdate.Invoke(G.player, null);
                heroFixtureSampleFrame = Time.frameCount;
            }
            catch (Exception error) { heroFixtureFailure = error.ToString(); }
        }
        static void BeginHeroFixture()
        {
            heroFixtureOriginalPosition = G.player.p;
            fixtureOldGameEnabled = G.enabled; fixtureOldPlayerEnabled = G.player.enabled;
            heroFixtureAnimation = Read<Animation>(G.player, "anim");
            fixtureOldAnimationEnabled = heroFixtureAnimation.enabled;
            G.enabled = false; G.player.enabled = false; heroFixtureAnimation.enabled = false;
            heroFixtureActive = true; heroFixtureVelocity = Vector2.zero; heroFixtureDead = heroFixtureFiring = false;
            heroFixtureFailure = null;
            heroFixtureDriver = new GameObject("OwnedHeroPresentationFixture").AddComponent<EngineerHeroFixtureDriver>();
        }
        static void EndHeroFixture()
        {
            if (!heroFixtureActive) return;
            heroFixtureActive = false;
            if (heroFixtureDriver != null) UnityEngine.Object.DestroyImmediate(heroFixtureDriver.gameObject);
            if (heroFixtureAnimation != null) heroFixtureAnimation.enabled = fixtureOldAnimationEnabled;
            if (G != null) { G.enabled = fixtureOldGameEnabled; if (G.player != null) { G.player.p = heroFixtureOriginalPosition; G.player.ResetEngineerMotion(); G.player.enabled = fixtureOldPlayerEnabled; } }
        }
        static bool HeroDeathComplete()
        {
            if (Prefs.Engineer)
            {
                var playback = Read<EngineerHeroPlayback>(G.player, "engineerPlayback");
                var state = heroFixtureAnimation.Cast<AnimationState>().Single(s => s.name == "Death" || s.name.EndsWith("|Death"));
                return playback.Dead && playback.DeathTime >= state.length - .001f;
            }
            var falling = heroFixtureAnimation[Read<string>(G.player, "clipDead")];
            return Read<bool>(G.player, "deathPose") && falling.time >= falling.length - .001f;
        }
        static void AddHeroMenuFixtures()
        {
            foreach (string value in new[] { "engineer", "vanguard" })
            {
                string id = value;
                Add(() => { Click(P.HeroesButton); Delay(.3); });
                Add(() =>
                {
                    Check(P.Current == MenuPresentation.Screen.Heroes, "Production Heroes screen opened " + Size);
                    Click(Button(P, "Hero_" + id)); Delay(.6);
                });
                Add(() =>
                {
                    Check(Prefs.Hero == id && PlayerPrefs.GetString("ms.hero") == id, "Actual card saved " + id + " " + Size);
                    PlayerPrefs.Save();
                    typeof(HeavyRoster).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                    Check(Prefs.Hero == id, "Completed migration re-entry preserves saved selection: " + id);
                    Check(Meta.HeroId == "vanguard" && Prefs.Vanguard && Meta.Heroes.SequenceEqual(new[] { "vanguard" }), "Shared Heavy combat/progression; no new cohesion source");
                    Check(Mathf.Approximately(heroBasePower, Progress.Power()), "Changing hero retains combat power " + id);
                    var cards = P.GetComponentsInChildren<UnityEngine.UI.Button>().Where(b => b.name.StartsWith("Hero_")).ToArray();
                    Check(cards.Length == 2, "Exactly two selectable hero cards " + Size);
                    foreach (var card in cards)
                        Check(card.transform.Find("Selection").gameObject.activeSelf == (card.name == "Hero_" + id), "Selection outline follows " + id);
                    Check(Prefs.HeroPortrait("engineer") != null && Prefs.HeroPortrait("engineer") != Prefs.HeroPortrait("vanguard"), "Engineer has its own actual-model portrait");
                    var playback = Read<EngineerHeroPlayback>(G.player, "engineerPlayback");
                    Check(id == "engineer" ? playback != null && playback.Ready : playback == null, "Selected gameplay model matches " + id);
                    Shot("heroes-" + id, "Actual production hero card pointer/raycast; temporary save; shared minigun/Barrage balance");
                    Click(Button(P, "Back")); Delay(.6);
                });
                Add(() =>
                {
                    var hangar = UnityEngine.Object.FindAnyObjectByType<HangarHero3D>();
                    Check(hangar != null && Read<string>(hangar, "selectedId") == id, "Hangar live model refreshes to " + id);
                    Shot("hangar-" + id, "Actual selected live model refreshed after returning from Heroes; temporary selection");
                });
            }
        }
        static void AddHeroRuntimeFixtures()
        {
            foreach (string value in new[] { "vanguard", "engineer" })
            {
                string id = value;
                Add(() => { Click(P.HeroesButton); Delay(.2); });
                Add(() => { Click(Button(P, "Hero_" + id)); Delay(.3); });
                Add(() => { Click(Button(P, "Back")); Delay(.3); });
                Add(() => { Click(P.DeployButton); WaitFor(() => MapSelect.I.Open && MapSelect.I.StyledBriefButton.IsActive(), 12); });
                Add(() => { SelectActualZone(); Click(MapSelect.I.StyledBriefButton); Delay(.5); });
                Add(() => { Click(MapSelect.I.StyledDeployButton); WaitFor(() => G.state == Game.State.Playing && G.run && !G.menu.Busy && !MapSelect.I.Open, 25); });
                Add(() =>
                {
                    G.demo = false; G.godMode = true; G.holdFire = true;
                    BeginHeroFixture(); Delay(.5);
                });
                foreach (var direction in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
                {
                    Vector2 d = direction;
                    Add(() => { heroFixtureVelocity = d * G.cfg.player.speed; Delay(.55); });
                    Add(() => { InspectHero(id, "run-" + d, true); });
                }
                Add(() => { heroFixtureVelocity = Vector2.up * G.cfg.player.speed * .35f; Delay(.5); });
                Add(() => { InspectHero(id, "walking", true); Shot("hero-" + id + "-walking", "FORCED presentation pose on live Cargo scene; game movement paused by fixture, no camera/collision changes"); });
                Add(() => { heroFixtureVelocity = Vector2.zero; heroFixtureFiring = true; Delay(.5); });
                Add(() =>
                {
                    InspectHero(id, "attack", false);
                    if (id == "engineer") Check(Read<EngineerHeroPlayback>(G.player, "engineerPlayback").AttackWeight > .95f, "Engineer upper-body Attack active over Idle");
                    Shot("hero-" + id + "-attack", "FORCED shootT presentation fixture; actual shared minigun, hand IK and muzzle; no gameplay shot injected");
                    heroLiveBones = HeroBonePose(); heroFixtureDead = true; heroFixtureFiring = false; WaitFor(HeroDeathComplete, 15);
                });
                Add(() =>
                {
                    InspectHero(id, "death", false);
                    Check(HeroPoseDelta(heroLiveBones) > 5, "Actual full-body death deforms " + id);
                    Check(G.player.gun.parent == G.player.Hand, "Falling weapon follows hand without live IK " + id);
                    Shot("hero-" + id + "-death", "FORCED visual dead argument; authored full-body death clamps; game/reward state unchanged");
                    heroDeathBones = HeroBonePose(); Delay(.4);
                });
                Add(() =>
                {
                    Check(HeroPoseDelta(heroDeathBones) < .1f, "Death holds its final pose " + id);
                    heroFixtureDead = false; Delay(.4);
                });
                Add(() =>
                {
                    InspectHero(id, "revived", false);
                    Check(G.player.gun.parent == G.player.VisualRoot, "Restart restores hip weapon ownership " + id);
                    EndHeroFixture(); G.ToMenu(); Progress.Sim = true; Delay(.5);
                });
            }
        }
        static Quaternion[] HeroBonePose() => G.player.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(s => s.bones).Distinct().Select(b => b.localRotation).ToArray();
        static float HeroPoseDelta(Quaternion[] before)
        {
            var after = HeroBonePose();
            return before.Length == after.Length ? before.Select((q, i) => Quaternion.Angle(q, after[i])).DefaultIfEmpty(0).Max() : float.PositiveInfinity;
        }
        static Vector3 HeroPalm(Transform hand)
        {
            if (hand.childCount == 0) return hand.position;
            var child = hand.GetChild(0);
            if(child.name=="RightPalm"||child.name=="LeftPalm") return child.position;
            if(CohesiveCharacterPolicy.UseRebuiltVanguard) return hand.TransformPoint(new Vector3(0,hand.name.EndsWith("RightHand")?.05753228f:.05346125f,0));
            return (hand.position+child.position)*.5f;
        }
        static void InspectHero(string id, string phase, bool checkFacing)
        {
            var player = G.player;
            Check(heroFixtureSampleFrame > 0 && Time.frameCount - heroFixtureSampleFrame <= 2, "Fixture measured after actual clip and LateUpdate/IK sampling");
            Check(Prefs.Hero == id && player.Hand != null && player.HandL != null && player.gun != null, "Runtime hero/weapon/hand ownership " + id + "/" + phase);
            var row = new HeroRecord { hero = id, phase = phase, condition = "FORCED visual fixture; real model/skin/IK/muzzle, frozen game simulation" };
            foreach (var skin in player.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (var material in skin.sharedMaterials)
                    Check(material != null && Mathf.Abs(material.GetFloat("_Smoothness") - (id == "engineer" ? .15f : .30f)) < .001f, "Selected body finish " + id + "/" + phase);
            foreach (var renderer in player.gun.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    Check(material != null && Mathf.Abs(material.GetFloat("_Smoothness") - .30f) < .001f, "Shared gun finish retained " + id + "/" + phase);
            var grip = Read<Vector3>(player, "gunGrip");
            var support = Read<Vector3>(player, "GunHandguard");
            row.rightPalmError = Vector3.Distance(HeroPalm(player.Hand), player.gun.TransformPoint(grip));
            row.leftPalmError = Vector3.Distance(HeroPalm(player.HandL), player.gun.TransformPoint(support));
            row.rightArm = ArmDiagnostic(player.Hand, player.gun.TransformPoint(grip));
            row.leftArm = ArmDiagnostic(player.HandL, player.gun.TransformPoint(support));
            report.heroFixtures.Add(row);
            row.muzzleForwardDot = Vector3.Dot((player.MuzzleWorld - player.gun.TransformPoint(grip)).normalized, player.VisualRoot.forward);
            if (phase != "death")
            {
                if (row.rightPalmError >= .085f || row.leftPalmError >= .085f)
                    HeroDetailShot("hero-" + id + "-grip-diagnostic", "Actual sampled runtime pose; isolated review camera, gameplay camera unchanged");
                Check(row.rightPalmError < .085f && row.leftPalmError < .085f, "Both palms within8.5cm of actual weapon grips " + id + "/" + phase + " " + row.rightPalmError + "/" + row.leftPalmError);
                Check(row.muzzleForwardDot > .97f, "Actual muzzle points forward " + id + "/" + phase);
            }
            if (checkFacing)
            {
                var forward = (MazeMap.ToWorld(player.p + heroFixtureVelocity.normalized) - MazeMap.ToWorld(player.p)).normalized;
                row.facingForwardDot = Vector3.Dot(player.VisualRoot.forward, forward);
                Check(row.facingForwardDot > .97f, "Locomotion turns forward " + id + "/" + phase);
            }
            var baked = new Mesh(); Bounds bounds = default; bool first = true, finite = true;
            try
            {
                foreach (var skin in player.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Unity6000.6 BakeMesh(true) compensates renderer scale; TransformPoint applies it once.
                    skin.BakeMesh(baked, true);
                    row.skinScales += skin.name+" lossy="+skin.transform.lossyScale.ToString("F4")+" baked="+baked.bounds.size.ToString("F4")+" renderer="+skin.bounds.size.ToString("F4")+"; ";
                    foreach (var vertex in baked.vertices)
                    {
                        var world = skin.transform.TransformPoint(vertex);
                        finite &= !float.IsNaN(world.sqrMagnitude) && !float.IsInfinity(world.sqrMagnitude);
                        if (first) { bounds = new Bounds(world, Vector3.zero); first = false; } else bounds.Encapsulate(world);
                        row.finiteSkinVertices++;
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            Check(finite, "Every baked skin vertex is finite " + id + "/" + phase);
            row.height = bounds.size.y;
            if (row.height <= .1f || row.height >= 2.6f)
                HeroDetailShot("hero-"+id+"-bounds-diagnostic", "Actual sampled skin with isolated review camera; gameplay camera unchanged");
            Check(row.finiteSkinVertices > 100 && row.height > .1f && row.height < 2.6f, "Bounded actual skinned deformation " + id + "/" + phase);
            if (phase == "walking" || phase == "attack" || phase == "death")
                HeroDetailShot("hero-" + id + "-" + phase + "-detail", "Actual sampled runtime pose; isolated review camera, gameplay camera unchanged");
        }
        static string ArmDiagnostic(Transform h, Vector3 target)
        {
            var a=h.parent.parent; var b=h.parent;
            var palm=HeroPalm(h); var wristTarget=target-(palm-h.position);
            return "shoulder="+a.position.ToString("F5")+" elbow="+b.position.ToString("F5")+" wrist="+h.position.ToString("F5")+" palm="+palm.ToString("F5")+" target="+target.ToString("F5")+" reach="+(Vector3.Distance(a.position,b.position)+Vector3.Distance(b.position,h.position)).ToString("F5")+" required="+Vector3.Distance(a.position,wristTarget).ToString("F5");
        }
        static void HeroDetailShot(string label, string condition)
        {
            var renderers=G.player.VisualRoot.GetComponentsInChildren<Renderer>();
            var layers=renderers.Select(r=>r.gameObject.layer).ToArray();
            var go=new GameObject("OwnedHeroDetailCamera"); var camera=go.AddComponent<Camera>();
            var texture=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active; Texture2D image=null;
            try
            {
                for(int i=0;i<renderers.Length;i++) renderers[i].gameObject.layer=27;
                camera.CopyFrom(main); camera.cullingMask=1<<27; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.14f,.20f);
                camera.targetTexture=texture; camera.orthographic=true; camera.orthographicSize=1.22f; camera.aspect=1; camera.nearClipPlane=.05f; camera.farClipPlane=15;
                var centre=G.player.VisualRoot.position+Vector3.up*.85f;
                if(heroFixtureDead && renderers.Length>0)
                {
                    var all=renderers[0].bounds; foreach(var r in renderers) all.Encapsulate(r.bounds);
                    centre=all.center; camera.orthographicSize=Mathf.Max(1.22f,all.extents.magnitude*1.1f);
                }
                camera.transform.position=centre+G.player.VisualRoot.TransformDirection(new Vector3(3,1,4)); camera.transform.LookAt(centre);
                camera.Render(); RenderTexture.active=texture;
                image=new Texture2D(1024,1024,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1024,1024),0,0); image.Apply();
                var bytes=image.EncodeToPNG(); var path=System.IO.Path.Combine(Out,label+".png"); System.IO.File.WriteAllBytes(path,bytes);
                report.captures.Add(new Capture {file=path,stage=label,conditions=condition,width=1024,height=1024,floor=G.floor,sha256=Hash(bytes)}); Save();
            }
            finally
            {
                for(int i=0;i<renderers.Length;i++) renderers[i].gameObject.layer=layers[i];
                RenderTexture.active=previous; camera.targetTexture=null;
                if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
    [DefaultExecutionOrder(10000)]
    public sealed class EngineerHeroFixtureDriver : MonoBehaviour
    {
        void LateUpdate() => CohesiveEditorReview.AdvanceHeroFixture();
    }
}
#endif
