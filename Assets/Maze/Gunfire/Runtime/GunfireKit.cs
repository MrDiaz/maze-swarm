using UnityEngine;

namespace Maze
{
    // Gunfire in the approved chibi direction (2026-10-14): compact warm star flashes, short dashed tracers and small spark +
    // puff impacts, for the rifle and the Heavy's minigun (Barrage: every other round draws a tracer). Assets come from
    // Resources/Gunfire (baked by GunfireBuilder). Every emitter is one pre-made particle system fed by Emit with a hard
    // particle cap (a full system drops new particles: nothing is instantiated while firing); tracers ride Fx's tracer pool.
    // GunFx falls back to the old look when the kit is missing.
    public class GunfireKit : MonoBehaviour
    {
        ParticleSystem rStar, rFlame, mStar, mFlame, iStar, iSparks, iPuff, glow;
        Material tracerMat; Mesh tracerRifle, tracerMinigun;
        int tracerN; float lastEnemyHit = -1;
        public static int Shots, Impacts, EnemyHits, TracersHidden;   // (checks)

        public static GunfireKit Load(Transform parent, Transform anchor)
        {
            var mat = Resources.Load<Material>("Gunfire/Materials/GF_Tracer");
            var rm = Resources.Load<Mesh>("Gunfire/Meshes/GF_TracerRifle"); var mm = Resources.Load<Mesh>("Gunfire/Meshes/GF_TracerMinigun");
            var go = new GameObject("GunfireKit"); go.transform.SetParent(parent, false);
            var k = go.AddComponent<GunfireKit>(); k.tracerMat = mat; k.tracerRifle = rm; k.tracerMinigun = mm;
            k.rStar = k.Make("GF_RifleStar", anchor); k.rFlame = k.Make("GF_RifleFlame", anchor);
            k.mStar = k.Make("GF_MinigunStar", anchor); k.mFlame = k.Make("GF_MinigunFlame", anchor);
            k.glow = k.Make("GF_FlashGlow", anchor);
            k.iStar = k.Make("GF_ImpactStar", null); k.iSparks = k.Make("GF_ImpactSparks", null); k.iPuff = k.Make("GF_ImpactPuff", null);
            if (mat == null || rm == null || mm == null || k.rStar == null || k.rFlame == null || k.mStar == null || k.mFlame == null || k.iStar == null || k.iSparks == null || k.iPuff == null || k.glow == null)
            {
                Debug.LogWarning("Maze: Resources/Gunfire kit incomplete (Maze > Gunfire > Rebuild gunfire kit); using the old gunfire look");
                Destroy(go); return null;
            }
            return k;
        }
        ParticleSystem Make(string prefab, Transform anchor)
        {
            var p = Resources.Load<ParticleSystem>("Gunfire/Prefabs/" + prefab); if (p == null) return null;
            var ps = Instantiate(p, transform); ps.name = prefab;
            if (anchor != null) { var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.Custom; m.customSimulationSpace = anchor; }
            ps.Play();
            return ps;
        }

        // a shot from the muzzle (the flash particles live in the muzzle anchor's space, so they ride the gun)
        public void Shot(bool heavy, bool barrage, Vector3 dir)
        {
            Shots++;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            ep.position = dir * 0.1f + Vector3.up * 0.06f; ep.velocity = Vector3.zero;
            ep.startLifetime = heavy ? 0.045f : 0.055f;
            ep.startSize = (heavy ? (barrage ? 0.78f : 0.7f) : 1.0f) * Random.Range(0.88f, 1.1f);
            ep.rotation = Random.value * 360f; ep.startColor = Color.white;
            (heavy ? mStar : rStar).Emit(ep, 1);
            // v2: a small soft glow behind the star (white-hot centre, warm falloff; alpha-blended, never above 1)
            ep.startSize = (heavy ? 1.15f : 1.55f) * Random.Range(0.92f, 1.06f); ep.startColor = heavy ? new Color(1f, 0.9f, 0.75f, 0.85f) : new Color(1, 1, 1, 0.9f);
            glow.Emit(ep, 1);
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            float len = (heavy ? (barrage ? 1.1f : 1.0f) : 1.45f) * Random.Range(0.88f, 1.08f);
            float a = yaw * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            ep.position = d * (len * 0.5f - 0.04f); ep.startSize3D = new Vector3(len, len * 0.5f, 1); ep.rotation = yaw - 90f;
            ep.startLifetime = heavy ? 0.045f : 0.05f; ep.startColor = Color.white;
            (heavy ? mFlame : rFlame).Emit(ep, 1);
        }

        // the tracer quad for a bullet (Fx pool object); Barrage: every other round has no visible tracer
        public void SetupTracer(MeshFilter mf, MeshRenderer mr, Transform t, bool heavy, bool barrage)
        {
            mf.sharedMesh = heavy ? tracerMinigun : tracerRifle; mr.sharedMaterial = tracerMat;
            t.localScale = heavy ? new Vector3(0.28f, 1, 1.7f) : new Vector3(0.34f, 1, 2.5f);   // v2: wider quads carry the head glow; the cores stay ~0.1 m   // sized against the hero at the gameplay camera (reference: a dash ~1-1.5 hero heights)
            bool hide = heavy && barrage && (tracerN++ & 1) == 1;
            mr.enabled = !hide; if (hide) TracersHidden++;
        }

        // wall / barrel: a tiny star, a few short spark streaks back off the surface and a small pale puff;
        // enemy: a smaller star and two sparks (throttled), no puff
        public void Impact(Vector3 p, Vector3 back, bool enemy, int n)
        {
            if (enemy) { if (Time.time - lastEnemyHit < 0.03f) return; lastEnemyHit = Time.time; EnemyHits++; } else Impacts++;
            var ep = new ParticleSystem.EmitParams { position = p, startLifetime = 0.06f, startSize = enemy ? 0.32f : 0.46f, rotation = Random.value * 90f, startColor = Color.white };
            iStar.Emit(ep, 1);
            int sparks = enemy ? 2 : Mathf.Clamp(n - 1, 2, 4);
            for (int i = 0; i < sparks; i++)
            {
                var v = back + Random.insideUnitSphere * 0.85f; v.y = Mathf.Abs(v.y) * 0.7f + 0.25f;
                var sp = new ParticleSystem.EmitParams { position = p, velocity = v.normalized * Random.Range(3f, 6f), startLifetime = Random.Range(0.1f, 0.18f), startSize = Random.Range(0.08f, 0.11f), startColor = Color.white };
                iSparks.Emit(sp, 1);
            }
            if (!enemy)
            {
                var pp = new ParticleSystem.EmitParams { position = p + back * 0.08f, velocity = back * 0.6f + Vector3.up * 0.3f, startLifetime = 0.22f, startSize = 0.58f * Random.Range(0.9f, 1.1f), rotation = Random.value * 360f, startColor = new Color(1, 1, 1, 0.85f) };
                iPuff.Emit(pp, 1);
            }
        }

        public int Live => glow.particleCount + rStar.particleCount + rFlame.particleCount + mStar.particleCount + mFlame.particleCount + iStar.particleCount + iSparks.particleCount + iPuff.particleCount;
        public void ClearAll()
        {
            glow.Clear(); rStar.Clear(); rFlame.Clear(); mStar.Clear(); mFlame.Clear(); iStar.Clear(); iSparks.Clear(); iPuff.Clear();
            tracerN = 0;
        }
    }
}
