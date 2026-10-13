using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    // Cryo Station ambience: a few slow steam wisps where the swarm creep touches the ice (one pooled particle
    // system, ~4 puffs a second), and the creep spreading a little each wave (its alpha-clip threshold sinks).
    public class CryoAmbience : MonoBehaviour
    {
        ParticleSystem ps; List<Vector3> spots; Material creep; float emitT;

        public void Init(List<Vector3> edges, Material creepMat)
        {
            spots = edges; creep = creepMat;
            var go = new GameObject("CreepSteam"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = false; m.playOnAwake = false; m.maxParticles = 24; m.simulationSpace = ParticleSystemSimulationSpace.World; m.gravityModifier = -0.02f;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(1, 1.6f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(0, 1) }); col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Mats.ParticlesFade(Resources.Load<Texture2D>("Fx/puff"));
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            SetWave(1);
        }

        // creep grows a little each wave (and is back to its start on a new run)
        public void SetWave(int wave) { if (creep != null) creep.SetFloat("_Cutoff", Mathf.Lerp(0.42f, 0.16f, Mathf.Clamp01((wave - 1) / 10f))); }

        void Update()
        {
            if (spots == null || spots.Count == 0) return;
            emitT -= Time.deltaTime;
            if (emitT > 0) return;
            emitT = 0.25f;
            var ep = new ParticleSystem.EmitParams
            {
                position = spots[Random.Range(0, spots.Count)] + new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f)),
                velocity = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.25f, 0.45f), Random.Range(-0.1f, 0.1f)),
                startLifetime = Random.Range(2f, 3f), startSize = Random.Range(0.9f, 1.5f),
                startColor = new Color(0.9f, 0.95f, 1f, 0.32f), rotation = Random.value * 360,
            };
            ps.Emit(ep, 1);
        }
    }
}
