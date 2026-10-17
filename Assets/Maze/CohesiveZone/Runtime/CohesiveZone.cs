using UnityEngine;

namespace Maze
{
    // A presentation alias. The authored route, save keys, grid and rewards stay cryo-station.
    public static class CohesiveZone
    {
        public static bool Enabled = true;
        static Game game;
        public static bool For(SectorDef sector) => Enabled && sector?.id == "cryo-station";
        public static bool For(Game value) => value != null && value.run && For(value.sector);
        public static bool For(bool run, SectorDef sector, int floor) => run && For(sector);
        public static bool Active { get { if (game == null) game = Object.FindAnyObjectByType<Game>(); return For(game); } }
        public static string Title(SectorDef sector) => For(sector) ? "CARGO DEPOT" : sector?.name ?? "";
    }
}
