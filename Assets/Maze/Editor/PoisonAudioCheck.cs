#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.EditorTools
{
    // Editor-only routing/overlap checks; runs through the existing save-preserving proof driver.
    [InitializeOnLoad]
    public static class PoisonAudioCheck
    {
        static readonly Type Driver = typeof(FirstCryoPolishProof);
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static object[] keys;
        static int scenario;
        static bool injected;
        static float maximumPoisonGain;
        static Game G => Object.FindAnyObjectByType<Game>();
        static PoisonAudioCheck() { if (Environment.GetEnvironmentVariable("CRYO_POISON_AUDIO") == "1") EditorApplication.update += Tick; }
        public static void Run() => FirstCryoPolishProof.Run();
        static object Field(string id) => Driver.GetField(id, Flags).GetValue(null);
        static void Set(string id, object value) => Driver.GetField(id, Flags).SetValue(null, value);
        static void Call(string id, params object[] args) => Driver.GetMethod(id, Flags).Invoke(null, args);
        static void Check(bool ok, string id) => Call("Check", ok, id);
        static void Delay(double seconds) => Call("Delay", seconds);
        static void Wait(float seconds) { float end = Time.time + seconds; Set("wait", new Func<bool>(() => Time.time >= end)); Delay(0); }
        static bool Poison(string id) => id == "amb_acid_pool" || id == "amb_toxic_gas";
        static AudioSource[] Sources => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.GetComponentInParent<HazardAudio>() != null && s.isPlaying && s.clip != null).ToArray();
        static AudioSource[] Splashes => Sources.Where(s => s.clip.name == "sfx_acid_splash").ToArray();
        static void Tick()
        {
            if (!EditorApplication.isPlaying || G == null || !(bool)(Field("prepared") ?? false)) return;
            if (!injected)
            {
                injected = true; keys = Enumerable.Range(0, 40).Select(i => new object()).ToArray();
                Set("steps", new List<Action>
                {
                    () => { Call("Start", false); G.run = false; G.holdFire = true; foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) s.mute = true; HazardAudio.ClearAll(); scenario = 1; Delay(.7); },
                    () => { Check(Sources.Count(s => s.clip.name == "amb_acid_pool") == 2, "acid owners capped at two"); Check(Sources.Count(s => s.clip.name == "amb_toxic_gas") == 2, "toxic gas owners capped at two"); Check(Sources.Where(s => Poison(s.clip.name)).Sum(s => s.volume) <= .12001f, "combined poison ambience gain at most 0.12"); Check(Sources.Where(s => Poison(s.clip.name)).All(s => s.spatialBlend == 1 && s.rolloffMode == AudioRolloffMode.Linear && s.minDistance == 2 && s.maxDistance == 14 && s.outputAudioMixerGroup == null), "poison retains original 3D routing/rolloff"); scenario = 2; Delay(.7); },
                    () => { Check(maximumPoisonGain <= .12001f, "owner handoff/fading voices stay within poison budget " + maximumPoisonGain); scenario = 3; Delay(.7); },
                    () => { var fire = Sources.Where(s => s.clip.name == "amb_fire").ToArray(); Check(fire.Length == 4, "non-poison retains four nearest owners"); Check(fire.All(s => Mathf.Abs(s.volume - .45f) < .0001f), "fire gain unchanged at 0.45"); scenario = 0; Delay(.7); },
                    () => { Check(!Sources.Any(s => s.loop), "untouched loops fade and stop"); HazardAudio.ClearAll(); for (int i = 0; i < 12; i++) HazardAudio.Shot("sfx_acid_splash", HazardAudio.HeroFloor, .6f); Check(Splashes.Length == 1, "rapid splash batch emits one voice"); Check(Splashes.All(s => Mathf.Abs(s.volume - .24f) < .0001f), "splash effective gain 0.6 to 0.24"); Check(Splashes.All(s => s.spatialBlend == .7f && s.rolloffMode == AudioRolloffMode.Linear && s.minDistance == 2 && s.maxDistance == 14), "splash original spatial routing retained"); Wait(.2f); },
                    () => { HazardAudio.Shot("sfx_acid_splash", HazardAudio.HeroFloor, .6f); Check(Splashes.Length == 2, "second splash allowed after cooldown"); Wait(.19f); },
                    () => { for (int i = 0; i < 12; i++) HazardAudio.Shot("sfx_acid_splash", HazardAudio.HeroFloor, .6f); Check(Splashes.Length <= 2, "active splash voices never exceed two"); for (int i = 0; i < 3; i++) HazardAudio.Shot("sfx_laser_fence_toggle", HazardAudio.HeroFloor, .5f); var normal = Sources.Where(s => s.clip.name == "sfx_laser_fence_toggle").ToArray(); Check(normal.Length == 3 && normal.All(s => Mathf.Abs(s.volume - .5f) < .0001f), "other shots keep gain and overlap behavior"); HazardAudio.ClearAll(); Check(Sources.Length == 0, "clear stops all hazard sources"); Delay(.7); },
                    () => { HazardAudio.Shot("sfx_acid_splash", HazardAudio.HeroFloor, .6f, 1); Wait(.2f); },
                    () => { HazardAudio.Shot("sfx_acid_splash", HazardAudio.HeroFloor, .6f, 1); Check(Splashes.Length == 1, "caller stricter one-voice cap retained"); HazardAudio.ClearAll(); Delay(.3); }
                });
                var report = Field("report"); var conditions = (List<string>)report.GetType().GetField("conditions").GetValue(report);
                conditions.Add("Focused poison audio source/routing/overlap harness; game.run=false; actual Unity AudioSources; no phone operations or subjective audio audition.");
            }
            var at = HazardAudio.HeroFloor;
            if (scenario == 1 || scenario == 2)
                for (int i = 0; i < 10; i++)
                {
                    int start = scenario == 1 ? 0 : 20;
                    HazardAudio.Loop("amb_acid_pool", keys[start + i], at + new Vector3(i * .1f, 0, 0));
                    HazardAudio.Loop("amb_toxic_gas", keys[start + 10 + i], at + new Vector3(0, 0, i * .1f));
                }
            if (scenario == 3) for (int i = 0; i < 4; i++) HazardAudio.Loop("amb_fire", keys[i], at + new Vector3(i * .1f, 0, 0));
            maximumPoisonGain = Mathf.Max(maximumPoisonGain, Sources.Where(s => Poison(s.clip.name)).Sum(s => s.volume));
            foreach (var source in Sources) source.mute = true; // numerical inspection; no sound played aloud.
        }
    }
}
#endif
