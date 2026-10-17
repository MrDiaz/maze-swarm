using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze
{
    public class Barrel
    {
        public Vector2 p;
        public float hp, fuse = -1, flash, grow = 1, smokeT, yaw;
        public bool alive = true;
        public Transform tr;
        public Renderer[] rends;
    }

    // Explosive barrels: placed with 'X' in the map. Shoot one and it blows after a short fuse:
    // area damage + knockback to enemies, hurts the player too, and sets off nearby barrels.
    public class Barrels : MonoBehaviour
    {
        public readonly List<Barrel> list = new();
        Game game;
        MaterialPropertyBlock mpb;

        public void Init(Game g)
        {
            game = g;
            mpb = new MaterialPropertyBlock();
            var B = g.cfg.barrels;
            var prefab = CohesiveZone.For(g) ? Resources.Load<GameObject>("CohesiveZone/Models/cargo-tank") : CryoSoftEnvironment.For(g.run, g.sector, g.floor) ? Resources.Load<GameObject>("LivingEnvironments/CryoExplosiveBarrel") : null;
            if (prefab == null) prefab = Resources.Load<GameObject>("Models/barrel");
            Material fallback = null;
            if (prefab == null) { Debug.LogWarning("Maze: Resources/Models/barrel not found, using stand-in barrels."); fallback = Mats.Lit(new Color(0.7f, 0.15f, 0.12f), null, null, 0.5f, 0.3f); }
            var rnd = new System.Random(4242);
            foreach (var s in MazeMap.barrelSpots)
            {
                int n = B.perSpot; float axis = (float)rnd.NextDouble() * Mathf.PI;
                for (int i = 0; i < n; i++)
                {
                    float off = n > 1 ? (i / (n - 1f) - 0.5f) : 0f;
                    var b = new Barrel
                    {
                        p = s + new Vector2(Mathf.Cos(axis) * off, Mathf.Sin(axis) * off) + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.15f,
                        yaw = (float)rnd.NextDouble() * 360f, hp = B.hp,
                    };
                    GameObject go;
                    if (prefab != null) { go = Instantiate(prefab); Mats.ConvertGltf(go); go = Normalize(go, B.height); }
                    else
                    {
                        go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Destroy(go.GetComponent<Collider>());
                        var inner = go.transform; var holder = new GameObject("Barrel");
                        inner.SetParent(holder.transform, false);
                        inner.localScale = new Vector3(0.88f, B.height / 2, 0.88f); inner.localPosition = Vector3.up * B.height / 2;
                        go.GetComponent<Renderer>().sharedMaterial = fallback;
                        go = holder;
                    }
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(MazeMap.ToWorld(b.p), Quaternion.Euler(0, b.yaw, 0));
                    b.tr = go.transform;
                    b.rends = go.GetComponentsInChildren<Renderer>();
                    foreach (var r in b.rends) r.shadowCastingMode = ShadowCastingMode.On;
                    if (CohesiveZone.For(g)) CohesiveExplosiveMarker.Attach(b.tr, B.height);
                    list.Add(b);
                }
            }
            MarkCells();
        }

        static GameObject Normalize(GameObject model, float height)
        {
            var holder = new GameObject("Barrel");
            model.transform.SetParent(holder.transform, false);
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float s = height / Mathf.Max(0.01f, b.size.y);
                model.transform.localScale *= s;
                model.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            }
            return holder;
        }

        public void MarkCells()
        {
            System.Array.Clear(MazeMap.barrelCells, 0, MazeMap.barrelCells.Length);
            foreach (var b in list)
            {
                if (!b.alive) continue;
                int i = MazeMap.CellIndex(b.p);
                if (i >= 0) MazeMap.barrelCells[i]++;
            }
            MazeMap.flowCell = -1; // force a fresh flow field
        }

        public Barrel At(Vector2 p, float pad = 0f)
        {
            float rr = game.cfg.barrels.collide + pad; rr *= rr;
            foreach (var b in list) if (b.alive && (b.p - p).sqrMagnitude < rr) return b;
            return null;
        }

        public void Collide(ref Vector2 p, float r)
        {
            float rr = r + game.cfg.barrels.collide;
            foreach (var b in list)
            {
                if (!b.alive) continue;
                var d = p - b.p; float d2 = d.sqrMagnitude;
                if (d2 < rr * rr && d2 > 1e-8f) { float dl = Mathf.Sqrt(d2); p += d / dl * (rr - dl); }
            }
        }

        public void Damage(Barrel b, float dmg)
        {
            if (!b.alive || b.fuse >= 0) return;
            b.hp -= dmg; b.flash = 1;
            Fx.I.Debris(MazeMap.ToWorld(b.p, 0.9f), 3, new Color(0.84f, 0.2f, 0.16f), 3, 0.6f, 0.08f, 3);
            if (b.hp <= 0) b.fuse = game.cfg.barrels.fuse * Random.Range(0.8f, 1.3f);
        }

        void Explode(Barrel b)
        {
            var B = game.cfg.barrels;
            b.alive = false; b.fuse = -1; b.tr.gameObject.SetActive(false);
            MarkCells();
            if (game.run) { game.ScorchAt(b.p, 2); if (CellHiveWalls.Instance != null) for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) CellHiveWalls.Instance.Hit(b.p + new Vector2(dx, dz) * MazeMap.C, ZoneCells.RH(game) * 30f); }   // explosions burn creep, smash hive walls (x3)
            foreach (var e in game.swarm.enemies)
            {
                if (e.dead) continue;
                var dv = e.p - b.p; float d = Mathf.Max(dv.magnitude, 1e-3f);
                if (d > B.radius + e.r) continue;
                float f = 1 - Mathf.Min(1, d / B.radius) * 0.7f;
                e.hp -= B.enemyDamage * f; e.flash = 1;
                e.k += dv / d * (B.knockback * f / Mathf.Sqrt(e.type.hp));
                if (e.hp <= 0) game.KillEnemy(e);
            }
            if (game.state == Game.State.Playing)
            {
                var pl = game.player;
                var dv = pl.p - b.p; float d = Mathf.Max(dv.magnitude, 1e-3f);
                if (d < B.radius)
                {
                    float f = 1 - d / B.radius * 0.7f;
                    game.HurtPlayer(B.playerDamage * f);
                    pl.vel += dv / d * B.knockback * 0.7f * f;
                }
            }
            foreach (var o in list)
            {
                if (o == b || !o.alive) continue;
                float d = (o.p - b.p).magnitude;
                if (d < B.chainRadius)
                {
                    float t = B.chainDelay * (0.6f + d / B.chainRadius);
                    o.fuse = o.fuse >= 0 ? Mathf.Min(o.fuse, t) : t;
                }
            }
            game.AddShake(B.shake);
            Fx.I.Boom(MazeMap.ToWorld(b.p), B.radius);
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), GltfBase = Shader.PropertyToID("baseColorFactor");

        public void Tick(float dt, float time, bool paused)
        {
            var B = game.cfg.barrels;
            foreach (var b in list)
            {
                if (!b.alive) continue;
                b.flash = Mathf.Max(0, b.flash - dt * 8);
                b.grow = Mathf.Min(1, b.grow + dt * 3);
                Color tint = Color.white * (1 + b.flash * 1.5f);
                Vector2 jit = Vector2.zero;
                if (b.fuse >= 0)
                {   // lit: shake, glow hot, then blow
                    b.fuse -= dt;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(time * 60 + b.p.x);
                    tint = new Color(1.6f + pulse * 1.6f, 1 + pulse * 0.5f, 0.8f);
                    jit = new Vector2(Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f));
                    if (b.fuse < 0) { Explode(b); continue; }
                }
                else if (b.hp < B.hp && !paused)
                {   // damaged: trickle of smoke
                    b.smokeT -= dt;
                    if (b.smokeT <= 0) { b.smokeT = 0.25f; Fx.I.Smoke(MazeMap.ToWorld(b.p, 1.4f), 1, new Color(0.54f, 0.55f, 0.58f), 0.4f, 1.4f, 0.22f, 1); }
                }
                tint.a = 1;
                foreach (var r in b.rends)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(BaseColor, tint); mpb.SetColor(GltfBase, tint);
                    r.SetPropertyBlock(mpb);
                }
                b.tr.position = MazeMap.ToWorld(b.p + jit);
                float s = 1 - Mathf.Pow(1 - b.grow, 3);
                b.tr.localScale = Vector3.one * Mathf.Max(0.01f, s);
            }
        }

        public void Respawn(Vector2 playerPos)
        {
            foreach (var b in list)
            {
                if (b.alive || b.fuse >= 0) continue;
                if ((playerPos - b.p).magnitude < 2.5f) continue;
                b.alive = true; b.hp = game.cfg.barrels.hp; b.fuse = -1; b.flash = 0; b.grow = 0;
                b.tr.gameObject.SetActive(true);
            }
            MarkCells();
        }

        // a new map: new barrel spots
        public void Rebuild()
        {
            foreach (var b in list) if (b.tr != null) Destroy(b.tr.gameObject);
            list.Clear();
            Init(game);
        }

        public void ResetAll()
        {
            foreach (var b in list) { b.alive = true; b.hp = game.cfg.barrels.hp; b.fuse = -1; b.flash = 0; b.grow = 1; b.tr.gameObject.SetActive(true); }
            MarkCells();
        }

        // demo autopilot: shoot a barrel when the swarm bunches up around it
        public Barrel BestTarget(Vector2 from)
        {
            var B = game.cfg.barrels; Barrel best = null; int bestN = 3;
            foreach (var b in list)
            {
                if (!b.alive || b.fuse >= 0) continue;
                float pd = (b.p - from).magnitude;
                if (pd < B.radius + 1.5f || pd > 24) continue;
                int n = 0;
                foreach (var e in game.swarm.enemies) if ((e.p - b.p).magnitude < B.radius * 0.8f) n++;
                if (n > bestN) { bestN = n; best = b; }
            }
            return best;
        }
    }
}
