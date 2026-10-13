using UnityEngine;

namespace Maze
{
    // First-run onboarding (Design/game-feel-brief.md section 4, condensed): the first run is the tutorial.
    //   hints   - never pause, top band, 2.4 s, at most 2 times each (PlayerPrefs), forced on for runs 1-2
    //   run 1   - enemy damage x0.75 (run 2 x0.85); floor 1: no spawns closer than 14 m until the hero has moved 3 m,
    //             at most 6 enemies alive within 8 m; a Med Gel 6 m away once if HP < 30% on floors 1-2
    //   deaths  - on floor 2+ before the first win: 250 Credits of salvage and a pointer to the Armory
    public partial class Game
    {
        readonly System.Collections.Generic.Queue<string> hintQ = new();   // one hint at a time, in order
        int obRun, obFloor; float obMoved, obT, obCrowdT; Vector2 obLast; bool obSeen, obOrb, obMed, obSkillOn, obOver, obFirstPick; public bool ObRecommend;
        public static bool HintsOn => Progress.D.runs < 3 || PlayerPrefs.GetInt("ms.feel.hints", 0) == 1;
        void OnboardStart()
        {
            var d = Progress.D; bool fresh = d.cleared.Count == 0;
            obRun = SimRun || Endless ? 0 : fresh && d.runs == 0 ? 1 : fresh && d.runs == 1 ? 2 : 0;
            obMoved = obT = obCrowdT = 0; obFloor = 0; hintQ.Clear(); obLast = player.p; obSeen = obOrb = obMed = obSkillOn = obOver = obFirstPick = ObRecommend = false;
        }
        public float OnboardDmgMul => obRun == 1 ? 0.75f : obRun == 2 ? 0.85f : 1f;
        public bool Hint(string key, string text)
        {
            if (SimRun || !HintsOn) return false;
            int n = PlayerPrefs.GetInt("ms.hint." + key, 0); if (n >= 2) return false;
            if (hintQ.Contains(text)) return false;
            PlayerPrefs.SetInt("ms.hint." + key, n + 1); hintQ.Enqueue(text); return true;
        }
        // floor 1 of run 1: spawns keep their distance until the hero moves, and a calm first wave
        bool OnboardBlocksSpawn(Vector2 s)
        {
            if (obRun != 1 || floor != 1) return false;
            if (obMoved < 3f && (s - player.p).sqrMagnitude < 196f) return true;
            int near = 0; foreach (var e in swarm.enemies) if (!e.dead && (e.p - player.p).sqrMagnitude < 64f) near++;
            return near >= 6;
        }
        void OnboardFloor()
        {
            obT = 0;
            if (floor == 1) Hint("move", "DRAG TO MOVE");
            if (floor == 2 && !obSkillOn) { obSkillOn = true; Hint("skillon", Abilities.Name == "FREEZE" ? "FLASH FREEZE ONLINE" : Abilities.Name + " ONLINE"); }
            if (goal == Goal.Hold) Hint("hold", "STAY IN THE RING");
        }
        public void OnboardOrb() { if (!obOrb && run && floorT > 2f) { obOrb = true; Hint("bio", "BIOMASS FILLS THE BAR"); } }
        void OnboardTick(float dt)
        {
            if (!run || SimRun) return;
            if (hintQ.Count > 0 && !hud.HintBusy) { var h = hintQ.Dequeue(); hud.ShowHint(h); if (Application.isEditor) Debug.Log($"Maze hint: {h} (floor {floor}, t {floorT:0.0})"); }
            if (floor != obFloor && floorT > 0.5f) { obFloor = floor; OnboardFloor(); }
            if (floorT < 2f) return;   // the floor's own hint first
            obT += dt; obMoved += (player.p - obLast).magnitude; obLast = player.p;
            if (floor == 1 && obT > 12f && obMoved < 1f) { obT = -100f; Hint("move", "DRAG TO MOVE"); }
            int near3 = 0, near6 = 0; Enemy bloater = null, leaper = null;
            foreach (var e in swarm.enemies)
            {
                if (e.dead) continue; float d = (e.p - player.p).sqrMagnitude;
                if (d < 9f) near3++; if (d < 36f) near6++;
                if (!obSeen && d < 100f) { obSeen = true; Hint("auto", "YOUR WEAPON FIRES AUTOMATICALLY. KEEP MOVING."); }
                if (e.type.bloater && d < 144f) bloater = e;
                if (e.type.leaper && e.leap == 1 && d < 64f) leaper = e;
            }
            obCrowdT = near3 >= 3 ? obCrowdT + dt : 0;
            if (obCrowdT > 1f && player.dashCd <= 0) { obCrowdT = -100f; Hint("dash", "TAP DASH TO ESCAPE"); }
            if (floor >= 2 && near6 >= 6 && Abilities.Cd <= 0) Hint("skill", Abilities.Name == "FREEZE" ? "TAP TO FREEZE THEM" : "TAP " + Abilities.Name);
            if (leaper != null) Hint("leaper", "RED LINE = POUNCE. STEP OFF OR DASH.");
            if (bloater != null) Hint("bloater", "BLOATERS BURST. KILL IT FAR AWAY.");
            if (obRun == 1 && !obOver && floor == 1 && floorT > 20f)
            {   // a guaranteed Overcharge 6-12 m away under a light beam
                for (int k = 0; k < 20; k++)
                {
                    var at = player.p + Random.insideUnitCircle.normalized * Random.Range(6f, 12f);
                    if (!MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) continue;
                    obOver = true; PowerUps.Drop(PowerUps.Get("overcharge"), at); Hint("over", "WALK INTO IT");
                    if (!SimRun)
                    {
                        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(beam.GetComponent<Collider>()); beam.name = "OverchargeBeam";
                        beam.transform.position = MazeMap.ToWorld(at, 6f); beam.transform.localScale = new Vector3(0.6f, 6f, 0.6f);
                        var mr = beam.GetComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Fade(new Color(1f, 0.85f, 0.3f, 0.35f)); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        Object.Destroy(beam, 12f);
                    }
                    break;
                }
            }
            if (obRun > 0 && !obMed && floor <= 2 && player.hp > 0 && player.hp < player.maxHp * 0.3f)
            {   // a Med Gel 6 m away, once
                obMed = true;
                for (int k = 0; k < 12; k++)
                {
                    var at = player.p + Random.insideUnitCircle.normalized * 6f;
                    if (MazeMap.Walkable(Mathf.FloorToInt(at.x / MazeMap.C), Mathf.FloorToInt(at.y / MazeMap.C))) { PowerUps.Drop(PowerUps.Get("med_gel"), at); Hint("med", "GRAB THE MED GEL"); break; }
                }
            }
        }
        // run 1's first level-up: scripted cards (damage, fire rate, survival), the first RECOMMENDED
        bool OnboardOffer()
        {
            if (obRun != 1 || obFirstPick) return false;
            obFirstPick = true; offer.Clear();
            foreach (var id in new[] { "multi", "rate", "hp" }) { var k = System.Array.Find(Cards, c => c.id == id); if (k != null) offer.Add(k); }
            if (offer.Count < 3) { offer.Clear(); return false; }
            ObRecommend = true; return true;
        }
        // a death on floor 2+ before the first win: salvage and a pointer
        string OnboardDeath(bool won, int reached)
        {
            var d = Progress.D;
            if (won || SimRun || Endless || reached < 2 || d.cleared.Count > 0 || Ops.Stat("salvage") > 0) return null;
            Ops.AddStat("salvage", 1); d.credits += 250; Progress.Save();
            return "SALVAGE  //  +250 CREDITS: LEVEL UP YOUR RIFLE IN THE ARMORY";
        }
    }
}
