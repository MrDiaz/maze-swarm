using System.Collections.Generic;

namespace Maze
{
    // Enemy names as the zone data writes them -> the config's enemy types. New enemies from the design specs land
    // here first as placeholders (an existing type stands in, with its look) until they get their own behaviour,
    // so zone tables can name them today.
    public static class EnemyRegistry
    {
        static readonly Dictionary<string, string> alias = new()
        {
            { "drone", "grunt" }, { "grunt", "grunt" }, { "mini drone", "mini" }, { "mini", "mini" }, { "minidrone", "mini" },
            { "leaper", "leaper" }, { "runner", "leaper" }, { "bloater", "bloater" }, { "brute", "brute" }, { "nest", "nest" },
            { "spore pod", "spore_pod" }, { "carapace", "carapace" }, { "spitter", "spitter" }, { "burrower", "burrower" }, { "shrieker", "shrieker" },
            { "tick", "tick" }, { "weaver", "weaver" }, { "lancer", "lancer" }, { "splitter", "splitter" }, { "mender", "mender" }, { "stalker", "stalker" },
        };
        public static string Resolve(string name)
        {
            var k = Key(name);
            return alias.TryGetValue(k, out var t) ? t : bosses.TryGetValue(k, out var bt) ? bt : k;
        }
        // the spec's bosses and mini-bosses: until each is built they fight as a giant elite of their base
        static readonly Dictionary<string, string> bosses = new()
        {
            { "gravid bloater", "bloater" }, { "razorback", "leaper" }, { "slagback", "bloater" }, { "mother bloom", "bloater" },
            { "subject zero", "leaper" }, { "tidecaller", "leaper" }, { "hullbreaker", "bloater" }, { "mire matriarch", "grunt" },
            { "warden 9", "brute" }, { "the maw", "bloater" }, { "the surgeon", "grunt" }, { "patient zero", "brute" },
            { "highwire", "grunt" }, { "the arc matron", "leaper" }, { "broodmother", "bloater" }, { "ravager prime", "bloater" },
            { "acid matriarch", "grunt" }, { "the overmind", "bloater" },
        };
        static string Key(string n) => n.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        public static bool IsBoss(string n) => bosses.ContainsKey(Key(n));
        public static void Alias(string name, string type) => alias[name.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ')] = type;
    }
}
