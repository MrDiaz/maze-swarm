using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // SWARM GATE (endless): every floor pulls a random authored floor from the zones you've cleared (its layout,
    // enemies and script) or one of the gate's own layouts, in 5-floor cycles: PURGE, a random objective, SURVIVE,
    // a random objective, BOSS (a boss floor from a cleared zone). Power climbs 6% a floor from 0.6x your frontier
    // zone's recommended power. Between floors: the usual cache. Ends only when you do.
    public partial class Game
    {
        ZoneFloor gateFloor; string gateBoss; int gateLayout = -1; public int GateBest => PlayerPrefs.GetInt("ms.gate.best", 0);
        public bool Endless => sector != null && sector.endless;

        // the source floors: every authored floor of every cleared zone (all zones if none is cleared yet)
        List<(SectorDef z, int f)> GatePool(bool boss)
        {
            var l = new List<(SectorDef, int)>();
            foreach (var z in SectorDefs.All)
            {
                if (z.endless || z.floors == null) continue;
                if (Progress.D.cleared.Count > 0 && !Progress.Cleared(z)) continue;
                for (int i = 0; i < z.floors.Length; i++) if ((z.floors[i].goal == Goal.Boss) == boss) l.Add((z, i));
            }
            return l;
        }

        // set up floor f of an endless run (called before StartFloor)
        void GatePlanFloor(int f)
        {
            int c = (f - 1) % 5, slot = c;
            gateBoss = null;
            if (f == 1 || (c != 4 && Random.value < 0.25f))
            {   // the gate's own layouts (the Antechamber opens every run)
                var gf = sector.floors[c]; gateFloor = gf; plan[slot] = gf.goal;
                planMap[slot] = MazeMap.ZoneLayoutIndex(gf.layout);
                return;
            }
            var pool = GatePool(c == 4); if (pool.Count == 0) pool = GatePool(false);
            var (z, i) = pool[Random.Range(0, pool.Count)];
            var src = z.floors[i];
            gateFloor = src;
            plan[slot] = c == 0 ? Goal.Purge : c == 2 ? Goal.Survive : src.goal;
            if (c == 4) { plan[slot] = Goal.Boss; gateBoss = z.boss; }
            planMap[slot] = MazeMap.ZoneLayoutIndex(src.layout);
        }

        float GatePowerScale(int f)
        {
            int frontier = 900; foreach (var z in SectorDefs.All) if (!z.endless && Progress.Unlocked(z)) frontier = Mathf.Max(frontier, z.power);
            return 0.6f * frontier / 900f * Mathf.Pow(1.06f, f - 1);
        }
    }
}
