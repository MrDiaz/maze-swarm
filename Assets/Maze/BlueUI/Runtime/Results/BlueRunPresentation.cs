using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Maze
{
    // Display-only labels derived from the authored route; no mock names or new objective rules.
    public static class BlueRunPresentation
    {
        public static string TitleCase(string value)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase((value ?? "").ToLowerInvariant());
        }

        public static ZoneFloor FloorDefinition(SectorDef sector, int floor)
        {
            return sector?.floors != null && floor > 0 && floor <= sector.floors.Length ? sector.floors[floor - 1] : null;
        }

        public static string FloorName(SectorDef sector, int floor)
        {
            if (CohesiveZone.For(sector)) return CohesiveMenuArt.FloorName(floor);
            var def = FloorDefinition(sector, floor);
            var layout = def?.layout;
            if (!string.IsNullOrEmpty(layout) && sector?.layoutNotes != null)
            {
                int slash = layout.LastIndexOf('/');
                string key = slash >= 0 ? layout.Substring(slash + 1) : layout;
                if (sector.layoutNotes.TryGetValue(key, out var note) && !string.IsNullOrWhiteSpace(note))
                {
                    int period = note.IndexOf('.'), colon = note.IndexOf(':');
                    int stop = period < 0 ? colon : colon < 0 ? period : Math.Min(period, colon);
                    return (stop > 0 ? note.Substring(0, stop) : note).Trim();
                }
            }
            if (def != null && !string.IsNullOrWhiteSpace(def.spec)) return TitleCase(def.spec);
            return "Floor " + floor;
        }

        public static string GoalName(Game.Goal goal) => TitleCase(Game.GoalNames[(int)goal]);

        public static string GoalArt(Game.Goal goal)
        {
            return goal switch
            {
                Game.Goal.Purge => "objective_purge", Game.Goal.Hunt => "objective_hunt",
                Game.Goal.Hold or Game.Goal.Defend => "objective_hold",
                Game.Goal.Survive => "objective_survive", Game.Goal.Boss or Game.Goal.Assassinate => "objective_boss",
                _ => null
            };
        }

        public static string ObjectiveText(SectorDef sector, int floor, Game.Goal goal)
        {
            var def = FloorDefinition(sector, floor);
            string param = def?.param ?? "";
            var count = Regex.Match(param, @"\d+");
            string n = count.Success ? count.Value : "";
            switch (goal)
            {
                case Game.Goal.Purge: return n == "3" ? "Survive three waves" : n.Length > 0 ? "Survive " + n + " waves" : "Clear every wave";
                case Game.Goal.Hunt: return n.Length > 0 ? "Destroy " + n + " nests" : "Destroy the nests";
                case Game.Goal.Hold: return n.Length > 0 ? "Hold the zone for " + n + " s" : "Hold the zone";
                case Game.Goal.Survive: return n.Length > 0 ? "Survive for " + n + " s" : "Survive the swarm";
                case Game.Goal.Boss: return "Defeat " + TitleCase(sector?.boss ?? "the boss");
                case Game.Goal.Escape: return "Hack the doors and reach the lift";
                default:
                    string spec = !string.IsNullOrEmpty(def?.spec) ? TitleCase(def.spec) : GoalName(goal);
                    return spec + (param.Length > 0 ? " · " + param : "");
            }
        }

        public static Game.Goal RouteGoal(Game game, int floor)
        {
            int index = game.Endless ? (Math.Max(1, floor) - 1) % Game.Floors : Math.Max(0, Math.Min(Game.Floors - 1, floor - 1));
            if (game.plan != null && index < game.plan.Length) return game.plan[index];
            return FloorDefinition(game.sector, floor)?.goal ?? Game.Goal.Purge;
        }
    }
}
