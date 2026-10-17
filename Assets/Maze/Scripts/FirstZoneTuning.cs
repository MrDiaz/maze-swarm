namespace Maze
{
    // First-deploy balance (2026-10-15 polish): Cryo Station only, the numbers the RunSim pass tuned. Every other zone keeps
    // the shared defaults in GameRun (Defaults = the values Cryo had before this pass).
    public static class FirstZoneTuning
    {
        public static bool On(Game g) => g != null && g.run && g.sector != null && g.sector.id == "cryo-station";
        // HUNT (Cryo F2): seconds between a nest's drones (default 2.6 - 0.1 x tier = 2.5) and the nest-drone cap (default 20 + 2 x floor = 24)
        public const float HuntNestEvery = 2.8f; public const int HuntNestCap = 22;
        // HOLD (Cryo F3): alive cap (default 18) and the spawn gap inside the ring (default 0.42 s)
        public const int HoldAlive = 26; public const float HoldGapInside = 0.26f;
        // SURVIVE (Cryo F4): alive cap 14 + 22 x progress by default
        public const float SurviveAlive0 = 24f, SurviveAliveRamp = 32f;
        // and its spawn gap, from the start to the end of the 45 s (default 0.55 -> 0.22 s)
        public const float SurviveGap0 = 0.36f, SurviveGap1 = 0.14f;
        // BOSS (Cryo F5): the gap between the boss floor's drones (default 0.9 s)
        public const float BossGap = 0.55f;
        // (17:59, no reload: the minigun never pauses, ~23% more sustained fire) per floor F1..F5: enemy HP and contact damage multipliers
        // PURGE (Cryo F1): enemies per wave x this (default (9 + 3 x floor + 3 x wave))
        public const float PurgeCountMul = 1.4f;
        public static readonly float[] HpMul = { 1.72f, 1.72f, 1.72f, 1.72f, 1.55f }, DmgMul = { 1.30f, 1.30f, 1.35f, 1.35f, 1.30f };
        public static readonly float[] SpeedMul = { 1.12f, 1.12f, 1.12f, 1.12f, 1.10f };   // enemy move speed
        public static float Speed(int floor) => SpeedMul[UnityEngine.Mathf.Clamp(floor - 1, 0, 4)];
        public static float Hp(int floor) => HpMul[UnityEngine.Mathf.Clamp(floor - 1, 0, 4)];
        public static float Dmg(int floor) => DmgMul[UnityEngine.Mathf.Clamp(floor - 1, 0, 4)];
    }
}
