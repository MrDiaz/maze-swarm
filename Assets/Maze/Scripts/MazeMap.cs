using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maze
{
    // The level as ASCII, plus grid queries, the enemy flow field and circle-vs-grid collision.
    //
    // Gameplay runs on a 2D "plane" (x = column axis, y = row axis, both in world units), the same
    // coordinates as the three.js prototype, so its logic ports 1:1. ToWorld() maps plane -> Unity:
    // (x, h, -y), so row 0 is the far (top-of-screen) edge.
    public static class MazeMap
    {
        public const float C = 2f; // world units per cell

        // '#' outer wall, 'B' building, 'C' crate, 'r' railing (blocks walking, not bullets),
        // '.' floor, '~' blue zone, 'y' hazard stripes, 'P' player spawn, 'S' enemy spawn, 'X' explosive barrels.
        // Escape maps also use 'D' door cells (solid while closed), 'V' spawn vents inside a room and 'E' the
        // extraction pad.
        public static string[][] Maps { get { var m = new List<string[]> { CryoRows, EscapeRows }; m.AddRange(FloorTemplates); return m.ToArray(); } }
        public static int MapIndex;              // 0 Cryo Station (waves), 1 Escape Test (rooms), 2+ a sector run's floor templates
        public static bool Escape => MapIndex == 1;
        public static bool Facility => MapIndex >= 1;  // the escape look: worn floor, cargo crates, blast doors
        public const int FirstFloorMap = 2;
        public static string[] Rows => MapIndex >= ZoneBase ? FloorEditing.FloorDocuments.GetRowsOrOriginal(LayoutName(MapIndex), ZoneLayouts[MapIndex - ZoneBase]) : MapIndex == 0 ? CryoRows : MapIndex == 1 ? EscapeRows : FloorTemplates[MapIndex - FirstFloorMap];

        // the drop-zone specs' authored layouts (ZoneData.Layouts), map indices ZoneBase+; they use the v2 legend
        // (Design/drop-zones-core.json): mechanic letters are kept in `tag` (the hazards read them), the grid gets the
        // base cell ('.' for floor-like marks, '_' pit, 'm' hive wall as a block, 'L' -> 'E' lift, 'n' nest spots)
        public const int ZoneBase = 1000;
        public static readonly List<string[]> ZoneLayouts = new(); public static readonly List<string> ZoneLayoutIds = new();
        public static bool V2 => MapIndex >= ZoneBase;
        public static int ZoneLayoutIndex(string id)
        {
            if (ZoneLayouts.Count == 0) foreach (var kv in ZoneData.Layouts) { ZoneLayoutIds.Add(kv.Key); ZoneLayouts.Add(kv.Value); }
            int i = ZoneLayoutIds.IndexOf(id); return i < 0 ? -1 : ZoneBase + i;
        }
        public static string LayoutName(int index) => index >= ZoneBase ? ZoneLayoutIds[index - ZoneBase] : index >= FirstFloorMap ? FloorNames[index - FirstFloorMap] : index == 0 ? "CRYO" : "ESCAPE";
        public static char[,] tag;                                    // the layout's own letter per cell (v2 mechanics)
        public static readonly Dictionary<char, List<Vector2Int>> marks = new();   // v2 mechanic letter -> its cells
        public static Vector2 bossSpot; public static bool hasBossSpot;
        public static List<Vector2Int> Marks(char c) => marks.TryGetValue(c, out var l) ? l : NoMarks;
        static readonly List<Vector2Int> NoMarks = new();

        // Sector-run floors (Tools/floor_templates.py, checked for connectivity): compact layouts, ~1/3-1/2 of Cryo.
        // Arenas carry 'Z' (the HOLD zone centre) and 'N' (HUNT nest spots); the ESC_ ones chain rooms behind doors to a lift.
        public static readonly string[] FloorNames = { "PLAZA", "CROSSROADS", "PILLARS", "YARD", "LOOP", "SPLIT", "ESC_LINE", "ESC_BEND" };
        public const int ArenaCount = 6;         // the first 6 are arenas, the rest escape chains
        public static readonly string[][] FloorTemplates =
        {
            // PLAZA (28x18)
            new[]
            {
                "############################",
                "#..........................#",
                "#.~~~~.......V..........V..#",
                "#.~~~~.....................#",
                "#.~~~~CC.....Z......CC.....#",
                "#.....CC............CC.....#",
                "#..N....................N..#",
                "#...........BBBB...........#",
                "#.V......X..BBBB.........V.#",
                "#...........BBBB..X........#",
                "#...........BBBB...........#",
                "#..........................#",
                "#.....CC............CC.....#",
                "#.....CC...yyyyyy...CC~~~~.#",
                "#.....................~~~~.#",
                "#..V.....N...P........~~~~.#",
                "#..........................#",
                "############################",
            },
            // CROSSROADS (30x18)
            new[]
            {
                "##############################",
                "#............................#",
                "#.V...........V............V.#",
                "#..BBBBBBBBB..N...BBBBBBBBB..#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#............~~~~............#",
                "#............~~~~.....X......#",
                "#.N.....X....~~Z~..........N.#",
                "#............~~~~............#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#..BBBBBBBBB.yyyy.BBBBBBBBB..#",
                "#..BBBBBBBBB......BBBBBBBBB..#",
                "#.V...........P............V.#",
                "#............................#",
                "##############################",
            },
            // PILLARS (28x20)
            new[]
            {
                "############################",
                "#..........................#",
                "#.V.....N.....V.....N....V.#",
                "#...BB....BB....BB....BB...#",
                "#...BB....BB....BB....BB...#",
                "#..........................#",
                "#...................X......#",
                "#..........................#",
                "#...BB................BB...#",
                "#...BB........Z.......BB...#",
                "#..........................#",
                "#.......X..................#",
                "#.............N............#",
                "#...BB....BB....BB....BB...#",
                "#...BB....BB....BB....BB...#",
                "#...........yyyyy..........#",
                "#..........................#",
                "#.V...........P..........V.#",
                "#..........................#",
                "############################",
            },
            // YARD (32x16)
            new[]
            {
                "################################",
                "#..............................#",
                "#.V.............V..........~~~.#",
                "#....CC....CC......CC....CC~~~.#",
                "#....CC....CC...Z..CC...XCC....#",
                "#..............................#",
                "#...........................N..#",
                "#...N....rrrrrr...rrrrrr.......#",
                "#.V..........................V.#",
                "#...............N..............#",
                "#....CC....CC......CC....CC....#",
                "#....CC....CC......CC....CC....#",
                "#.~~~...X......................#",
                "#.~~~...........P............V.#",
                "#..............................#",
                "################################",
            },
            // LOOP (26x20)
            new[]
            {
                "##########################",
                "#........................#",
                "#.V..........V.........V.#",
                "#..CC........Z...........#",
                "#..CC...............X....#",
                "#........................#",
                "#........BBBBBBBBBB......#",
                "#........BBBBBBBBBB......#",
                "#......BBBBBBBBBBBB......#",
                "#......BBBBBBBBBBBB......#",
                "#..N...BBBBBBBBBBBB...N..#",
                "#......BBBBBBBBBBBB......#",
                "#......BBBBBBBBBBBB......#",
                "#......BBBBBBBBBBBB......#",
                "#........................#",
                "#....X....yyyyyy.....CC..#",
                "#..................N.CC..#",
                "#.V..........P.........V.#",
                "#........................#",
                "##########################",
            },
            // SPLIT (30x18)
            new[]
            {
                "##############################",
                "#.............##.............#",
                "#.~~~~........##......V....V.#",
                "#.~~~~.....N..##...CC........#",
                "#.............##...CC........#",
                "#.............##.............#",
                "#.........................N..#",
                "#....BBB..............BBB....#",
                "#....BBB..............BBB....#",
                "#.V..BBB......##......BBB....#",
                "#..........X..##.............#",
                "#.............##.............#",
                "#..................X..Z......#",
                "#........CC..................#",
                "#........CC..................#",
                "#......P......##....V...N..V.#",
                "#.............##.............#",
                "##############################",
            },
            // ESC_LINE (54x14)
            new[]
            {
                "######################################################",
                "#................###...............###...............#",
                "#.............V..###..V........V...###..V..........V.#",
                "#......CC........###....BBB........###...............#",
                "#......CC........###....BBB........###......yyyyyyy..#",
                "#................yDy....BBB........###........EEE....#",
                "#................yDy..........X....###........EEE....#",
                "#..P.............yDy...............###........EEE....#",
                "#................###...............yDy......yyyyyyy..#",
                "#..........CC....###........CC.....yDy...............#",
                "#..........CC....###........CC.....yDy...............#",
                "#........X....V..###............V..###..V............#",
                "#................###...............###...............#",
                "######################################################",
            },
            // ESC_BEND (40x26)
            new[]
            {
                "########################################",
                "#............###########################",
                "#.........V..###########################",
                "#...CC.......###########################",
                "#...CC.......###########################",
                "#..P.........###########################",
                "#............###########################",
                "#..........V.###########################",
                "#............###########################",
                "######yyy###############################",
                "######DDD###############################",
                "######yyy###############################",
                "#...................###................#",
                "#............X......###..............V.#",
                "#.V..............V..###..V.............#",
                "#.......BBB.........###....CC..........#",
                "#.......BBB.........###....CC..........#",
                "#.......BBB.........yDy................#",
                "#...................yDy........yyyyy...#",
                "#...................yDy.........EEE....#",
                "#..............CC...###.........EEE....#",
                "#..............CC...###......X..EEE....#",
                "#...................###........yyyyy...#",
                "#..V................###..............V.#",
                "#...................###................#",
                "########################################",
            },

        };
        public static readonly List<Vector2> zoneSpots = new(), nestSpots = new();

        public static readonly string[] CryoRows =
        {
            "##############################################",
            "#~~~~~~.........#~~~~~~#.........~~~~~~~~~~~~#",
            "#~~S~~~.........#~~~S~~#.........~~~~~~~~S~~~#",
            "#~~~~~~.........#~~~~~~#.........~~~~~~~~~~~~#",
            "#......XX..BBB...........BBBBB...BBBB........#",
            "#..CC......BBB...........BBBBB...BBBB....BBB.#",
            "#..CC......BBB.....BBBB..BBBBB...........BBB.#",
            "#..................BBBB..................BBB.#",
            "#..................BBBB.......CC.........~~~.#",
            "#....BBBBBB...................CC.X.......~~~.#",
            "#....BBBBBB..........XX..................~~~.#",
            "#...........rrrrrrrrB.......BBBBB............#",
            "#BBBBBB.............B.......BBBBB.......BBBBB#",
            "#BBBBBB.............B..................BBBBBB#",
            "#yyy~~~~~~~...................P........~~~~~~#",
            "#yyy~~~~~~~..........~~~~~~~...........~~~S~~#",
            "#yyy~~~~~~~..CCC.....~~~~~~~...CC......~~~~~~#",
            "#~~~~~~~~~~..CCC.....~~~~~~~...CC...XX.......#",
            "#~~~S~~~~~~..........~~~~~~~.......BBBBBB....#",
            "#~~~~~~~~~~...BBBBBB...............BBBBBB....#",
            "#.............BBBBBB.......BB......BBBBBB....#",
            "#.......B.....BBBBBB.......BB................#",
            "#.......B...XX.............BB....rrrrrr......#",
            "#.......B.....~~~~~~.....................CC..#",
            "#..BBBBBB.....~~~~~~.....BBBBBBBB........CC..#",
            "#..BBBBBB.....~~~~~~.....BBBBBBBB............#",
            "#.............~~~~~~.....BBBBBBBB....BBBBBBB.#",
            "#....CC..........................XX......BBB.#",
            "#....CC.....BBBBB.....~~~~~~~~.............B.#",
            "#~~~~~~..X..BBBBB.....~~~~~~~~.......~~~~~~~~#",
            "#~~S~~~...............~~~~S~~~.......~~~~S~~~#",
            "#~~~~~~...............~~~~~~~~.......~~~~~~~~#",
            "##############################################",
        };

        // Escape Test: six rooms in a winding chain, start (P) top-left to the extraction pad (E) at the bottom;
        // each 3-wide door (D) sits in a short hazard-striped corridor. Generated and checked for connectivity by
        // a script (rooms are separate with the doors shut, all reachable with them open).
        public static readonly string[] EscapeRows =
        {
            "##################################################",
            "#.CC.......#######################################",
            "#.CC.......###.....CC......CC......CC.........####",
            "#..........yDy.....CC......CC......CC.......V.####",
            "#....P.....yDy................V...............####",
            "#..........yDy...........X....................####",
            "#..........###.........CC......CC.......CC..V.####",
            "#.......X..###.........CC......CC.......CC....####",
            "#..........############################yyy########",
            "#######################################DDD########",
            "#######################################yyy########",
            "##################################..............##",
            "##################################............V.##",
            "##################################..............##",
            "##################################...CC....CC...##",
            "######.........................###...CC....CC...##",
            "######.V............V..........###..............##",
            "######..................BBB....yDy.X....CC......##",
            "######....BBBB..........BBB....yDy......CC......##",
            "######....BBBB..........BBB....yDy..............##",
            "######...........X.............###...CC....CC...##",
            "######.........................###...CC....CC...##",
            "######............BBBB.........###..............##",
            "######............BBBB.........###.V..........V.##",
            "######........BB............X..###..............##",
            "######.V......BB...............###################",
            "######.........................###################",
            "########yyy#######################################",
            "########DDD#######################################",
            "########yyy#######################################",
            "##................################################",
            "##.~~~~~...CC...V.####..........##################",
            "##.~~~~~...CC.....####.y.....y..##################",
            "##.~~~~~..........yyDy.y.EEE.y..##################",
            "##................yyDy.y.EEE.y..##################",
            "##.......X..~~~~~.yyDy.y.EEE.y..##################",
            "##..........~~~~~.####.y.....y..##################",
            "##.V........~~~~~.####..........##################",
            "##................####..........##################",
            "##################################################",
        };

        public static int W, H;
        public static char[,] grid; // [row, col]
        public static Vector2 playerSpawn;
        public static readonly List<Vector2> spawnPoints = new List<Vector2>();
        public static readonly List<Vector2> barrelSpots = new List<Vector2>();

        public static short[] flow;
        public static byte[] barrelCells; // live barrels per cell: the swarm paths around them
        public static int flowCell = -1;
        static int[] queue;

        public static void Init()
        {
            H = Rows.Length;
            W = 0;
            foreach (var r in Rows) W = Mathf.Max(W, r.Length);
            grid = new char[H, W]; tag = new char[H, W]; marks.Clear(); hasBossSpot = false;
            spawnPoints.Clear(); barrelSpots.Clear(); zoneSpots.Clear(); nestSpots.Clear();
            playerSpawn = new Vector2(W * C / 2, H * C / 2);
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    char ch = x < Rows[z].Length ? Rows[z][x] : '.';
                    if (z == 0 || z == H - 1 || x == 0 || x == W - 1) ch = '#';
                    var c = Center(x, z);
                    tag[z, x] = ch;
                    if (V2) ch = V2Cell(ch, x, z, c);
                    if (ch == 'P') { playerSpawn = c; ch = '.'; }
                    else if (ch == 'S') { spawnPoints.Add(c); ch = '~'; }
                    else if (ch == 'V') { spawnPoints.Add(c); ch = '.'; }
                    else if (ch == 'X') { barrelSpots.Add(c); ch = '.'; }
                    else if (ch == 'Z') { zoneSpots.Add(c); ch = '.'; }
                    else if (ch == 'N') { nestSpots.Add(c); ch = '.'; }
                    grid[z, x] = ch;
                }
            if (V2) DropSealedSpawns();
            flow = new short[W * H];
            barrelCells = new byte[W * H];
            queue = new int[W * H];
            flowCell = -1;
            BuildRooms();
        }

        // ---------------------------------------------------------------- rooms and doors (Escape maps)
        public class Door { public RectInt r; public int from, to; public bool open; public Vector2 center; }
        public static int[] roomOf;                   // per cell: room id (-1 solid or door)
        public static readonly List<Door> doors = new();
        public static readonly List<Vector2> roomCenter = new();
        public static readonly List<List<Vector2>> roomVents = new();
        public static int RoomCount => roomCenter.Count;
        public static readonly List<Vector2> exitCells = new();

        static void BuildRooms()
        {
            roomOf = new int[W * H]; for (int i = 0; i < roomOf.Length; i++) roomOf[i] = -1;
            doors.Clear(); roomCenter.Clear(); roomVents.Clear(); exitCells.Clear();
            if (!Facility) return;
            // flood rooms from the player outward, numbering them in the order the doors lead
            var doorRects = Rects(ch => ch == 'D');
            int start = CellIndex(playerSpawn), next = 0;
            var order = new List<int> { start };
            for (int k = 0; k < order.Count; k++)
            {
                int seed = order[k]; if (roomOf[seed] >= 0) continue;
                int id = next++;
                var cells = new List<int>(); var st = new Stack<int>(); st.Push(seed); roomOf[seed] = id;
                while (st.Count > 0)
                {
                    int i = st.Pop(), x = i % W, z = i / W; cells.Add(i);
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, nz = z + dz; if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                        int ni = nz * W + nx; char c = grid[nz, nx];
                        if (roomOf[ni] >= 0 || IsSolidMove(c) || c == 'D') continue;
                        roomOf[ni] = id; st.Push(ni);
                    }
                }
                Vector2 sum = Vector2.zero; foreach (var i in cells) sum += Center(i % W, i / W);
                var mid = sum / cells.Count; int best = cells[0]; float bd = float.MaxValue;
                foreach (var i in cells) { float d = (Center(i % W, i / W) - mid).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                roomCenter.Add(Center(best % W, best / W));
                // doors touching this room lead to the next rooms
                foreach (var r in doorRects)
                {
                    if (doors.Exists(d => d.r.Equals(r))) continue;
                    bool touches = false; int beyond = -1;
                    for (int z = r.y - 1; z <= r.y + r.height; z++)
                        for (int x = r.x - 1; x <= r.x + r.width; x++)
                        {
                            if (x < 0 || z < 0 || x >= W || z >= H || (x >= r.x && x < r.x + r.width && z >= r.y && z < r.y + r.height)) continue;
                            int ni = z * W + x; if (IsSolidMove(grid[z, x])) continue;
                            if (roomOf[ni] == id) touches = true; else if (roomOf[ni] < 0) beyond = ni;
                        }
                    if (!touches) continue;
                    doors.Add(new Door { r = r, from = id, to = id + 1, center = new Vector2((r.x + r.width / 2f) * C, (r.y + r.height / 2f) * C) });
                    if (beyond >= 0) order.Add(beyond);
                }
            }
            for (int i = 0; i < next; i++) roomVents.Add(new List<Vector2>());
            foreach (var v in spawnPoints) { int id = roomOf[CellIndex(v)]; if (id >= 0) roomVents[id].Add(v); }
            for (int z = 0; z < H; z++) for (int x = 0; x < W; x++) if (grid[z, x] == 'E') exitCells.Add(Center(x, z));
            ResetDoors();
        }

        // every door shut (the game opens the start room's straight away)
        public static void ResetDoors() { foreach (var d in doors) SetDoor(d, false); }

        public static void SetDoor(Door d, bool open)
        {
            d.open = open;
            for (int z = d.r.y; z < d.r.y + d.r.height; z++) for (int x = d.r.x; x < d.r.x + d.r.width; x++) grid[z, x] = open ? 'd' : 'D';
            flowCell = -1; // re-path
        }

        // a door's opening runs along its wall: a run of cells says it by its shape; a single cell asks its neighbours
        // (walls left and right: the wall runs along X; above and below: along Z). Before this a 1x1 door always
        // faced X, so doors in north-south walls stood across their passage instead of filling the opening.
        public static bool DoorAlongX(Door d)
        {
            if (d.r.width != d.r.height) return d.r.width > d.r.height;
            int x = d.r.x, z = d.r.y;
            bool Solid(int cx, int cz) => IsWallish(cx, cz);
            int xs = (Solid(x - 1, z) ? 1 : 0) + (Solid(x + d.r.width, z) ? 1 : 0), zs = (Solid(x, z - 1) ? 1 : 0) + (Solid(x, z + d.r.height) ? 1 : 0);
            return xs >= zs;
        }
        static bool IsWallish(int x, int z) => x < 0 || z < 0 || x >= W || z >= H || grid[z, x] == '#' || grid[z, x] == 'B' || grid[z, x] == 'C' || grid[z, x] == 'r' || grid[z, x] == 'm';

        public static int RoomAt(Vector2 p) { int i = CellIndex(p); return i < 0 ? -1 : roomOf[i]; }
        // on a lift cell, or within 0.6 m of one (a hero pressed against its edge still counts)
        public static bool NearExit(Vector2 p) => OnExit(p) || OnExit(p + new Vector2(0.6f, 0)) || OnExit(p - new Vector2(0.6f, 0)) || OnExit(p + new Vector2(0, 0.6f)) || OnExit(p - new Vector2(0, 0.6f));
        public static bool OnExit(Vector2 p) { int x = Mathf.FloorToInt(p.x / C), z = Mathf.FloorToInt(p.y / C); return x >= 0 && z >= 0 && x < W && z < H && grid[z, x] == 'E'; }

        // BFS distance field to a target point (the demo bot and the guide arrow walk it downhill)
        public static void FlowTo(Vector2 target, short[] field)
        {
            for (int i = 0; i < field.Length; i++) field[i] = -1;
            int sx = Mathf.FloorToInt(target.x / C), sz = Mathf.FloorToInt(target.y / C);
            if (!Walkable(sx, sz)) return;
            var q = new int[W * H]; int head = 0, tail = 0, s = sz * W + sx;
            field[s] = 0; q[tail++] = s;
            while (head < tail)
            {
                int i = q[head++], x = i % W, z = i / W; short f = (short)(field[i] + 1);
                if (x > 0 && field[i - 1] < 0 && FlowOpen(x - 1, z)) { field[i - 1] = f; q[tail++] = i - 1; }
                if (x < W - 1 && field[i + 1] < 0 && FlowOpen(x + 1, z)) { field[i + 1] = f; q[tail++] = i + 1; }
                if (z > 0 && field[i - W] < 0 && FlowOpen(x, z - 1)) { field[i - W] = f; q[tail++] = i - W; }
                if (z < H - 1 && field[i + W] < 0 && FlowOpen(x, z + 1)) { field[i + W] = f; q[tail++] = i + W; }
            }
        }

        // the next step from p along a FlowTo field: direction to the lowest neighbouring cell (diagonals allowed when
        // both side cells are open), or straight at the target in its own cell
        public static Vector2 StepAlong(short[] field, Vector2 p, Vector2 target)
        {
            int x = Mathf.FloorToInt(p.x / C), z = Mathf.FloorToInt(p.y / C);
            if (x < 0 || z < 0 || x >= W || z >= H) return Vector2.zero;
            int cur = field[z * W + x];
            if (cur == 0) return (target - p).normalized;
            int best = cur < 0 ? int.MaxValue : cur, bx = -1, bz = -1;
            for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oz == 0) continue;
                    int nx = x + ox, nz = z + oz;
                    if (!FlowOpen(nx, nz)) continue;
                    if (ox != 0 && oz != 0 && (!FlowOpen(x + ox, z) || !FlowOpen(x, z + oz))) continue;
                    int f = field[nz * W + nx]; if (f >= 0 && f < best) { best = f; bx = nx; bz = nz; }
                }
            return bx < 0 ? Vector2.zero : (Center(bx, bz) - p).normalized;
        }

        public static Vector2 Center(int x, int z) => new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
        public static Vector3 ToWorld(Vector2 p, float h = 0f) => new Vector3(p.x, h, -p.y);
        public static Vector2 ToPlane(Vector3 w) => new Vector2(w.x, -w.z);
        // plane-space facing angle (dir = (sin a, cos a)) -> Unity Y rotation in degrees
        public static float YawDeg(float a) => 180f - a * Mathf.Rad2Deg;

        public static bool IsSolidMove(char c) => c == '#' || c == 'B' || c == 'C' || c == 'r' || c == 'D' || c == '_' || c == '|' || c == 'm'; // '_' pit, '|' live fence, 'm' hive wall (v2) // D: closed door ('d' open)
        public static bool IsSolidShot(char c) => c == '#' || c == 'B' || c == 'C' || c == 'D' || c == '|' || c == 'm';   // '|' a live laser fence (v2)

        public static bool Walkable(int x, int z) => x >= 0 && z >= 0 && x < W && z < H && !IsSolidMove(grid[z, x]);
        public static string Dump()
        {   // (sims) the grid as text, top row first: # solid, o barrel, * slag, . open
            var sb = new System.Text.StringBuilder("\n");
            for (int z = H - 1; z >= 0; z--) { for (int x = 0; x < W; x++) sb.Append(!Walkable(x, z) ? grid[z, x] : barrelCells[z * W + x] != 0 ? 'o' : tag != null && tag[z, x] == '*' ? '*' : tag != null && tag[z, x] != '\0' && tag[z, x] != '.' ? tag[z, x] : '.'); sb.Append('\n'); }
            return sb.ToString();
        }
        public static bool FlowOpen(int x, int z) => Walkable(x, z) && barrelCells[z * W + x] == 0 && (tag == null || tag[z, x] != '*');   // the swarm paths around slag

        // v2 legend -> the base grid cell (and the marker lists); returns the char the v1 parser should see
        static char V2Cell(char ch, int x, int z, Vector2 c)
        {
            switch (ch)
            {
                case '#': case 'B': case 'C': case 'r': case '.': case '~': case 'y': case 'P': case 'S': case 'X': case 'D': case 'Z': return ch;
                case 'L': return 'E';
                case 'n': nestSpots.Add(c); Mark(ch, x, z); return '.';
                case '@': bossSpot = c; hasBossSpot = true; return '.';
                case '_': Mark(ch, x, z); return '_';
                case 'm': Mark(ch, x, z); return 'm';   // a hive wall: solid until shot down (CellHiveWalls draws it)
                case 'G': Mark(ch, x, z); zoneSpots.Add(c); return '.';
                default: Mark(ch, x, z); return '.';
            }
        }
        // spawns walled off from the hero (hive walls, pits) never reach him: keep only the connected ones (doors count as open)
        static void DropSealedSpawns()
        {
            var seen = new bool[W * H]; var st = new Stack<int>(); int s0 = CellIndex(playerSpawn); if (s0 < 0) return;
            st.Push(s0); seen[s0] = true;
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % W, z = i / W;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz; if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                    int ni = nz * W + nx; char c = grid[nz, nx];
                    if (seen[ni] || (IsSolidMove(c) && c != 'D')) continue;
                    seen[ni] = true; st.Push(ni);
                }
            }
            int before = spawnPoints.Count;
            spawnPoints.RemoveAll(p => { int i = CellIndex(p); return i < 0 || !seen[i]; });
            if (spawnPoints.Count == 0) { Debug.LogWarning("Maze: every spawn is sealed off, spawning at the edges"); spawnPoints.Add(Center(1, 1)); }
            else if (spawnPoints.Count < before) Debug.Log($"Maze: {before - spawnPoints.Count} sealed spawn(s) skipped");
        }
        static void Mark(char ch, int x, int z) { if (!marks.TryGetValue(ch, out var l)) marks[ch] = l = new List<Vector2Int>(); l.Add(new Vector2Int(x, z)); }

        public static bool ShotSolidAt(float px, float pz)
        {
            int x = Mathf.FloorToInt(px / C), z = Mathf.FloorToInt(pz / C);
            if (x < 0 || z < 0 || x >= W || z >= H) return true;
            return IsSolidShot(grid[z, x]);
        }

        public static int CellIndex(Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x / C), z = Mathf.FloorToInt(p.y / C);
            if (x < 0 || z < 0 || x >= W || z >= H) return -1;
            return z * W + x;
        }

        // BFS from the player's cell; enemies walk downhill
        public static void ComputeFlow(Vector2 p)
        {
            int sx = Mathf.FloorToInt(p.x / C), sz = Mathf.FloorToInt(p.y / C);
            if (!Walkable(sx, sz)) return;
            for (int i = 0; i < flow.Length; i++) flow[i] = -1;
            int head = 0, tail = 0, s = sz * W + sx;
            flow[s] = 0; queue[tail++] = s;
            while (head < tail)
            {
                int i = queue[head++], x = i % W, z = i / W;
                short f = (short)(flow[i] + 1);
                if (x > 0 && flow[i - 1] < 0 && FlowOpen(x - 1, z)) { flow[i - 1] = f; queue[tail++] = i - 1; }
                if (x < W - 1 && flow[i + 1] < 0 && FlowOpen(x + 1, z)) { flow[i + 1] = f; queue[tail++] = i + 1; }
                if (z > 0 && flow[i - W] < 0 && FlowOpen(x, z - 1)) { flow[i - W] = f; queue[tail++] = i - W; }
                if (z < H - 1 && flow[i + W] < 0 && FlowOpen(x, z + 1)) { flow[i + W] = f; queue[tail++] = i + W; }
            }
            flowCell = s;
        }

        // circle vs solid-cell collision (slides along walls)
        public static void CollideGrid(ref Vector2 p, float r)
        {
            for (int it = 0; it < 2; it++)
            {
                int x0 = Mathf.FloorToInt((p.x - r) / C), x1 = Mathf.FloorToInt((p.x + r) / C);
                int z0 = Mathf.FloorToInt((p.y - r) / C), z1 = Mathf.FloorToInt((p.y + r) / C);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (Walkable(x, z)) continue;
                        float minX = x * C, maxX = minX + C, minZ = z * C, maxZ = minZ + C;
                        float cx = Mathf.Clamp(p.x, minX, maxX), cz = Mathf.Clamp(p.y, minZ, maxZ);
                        float dx = p.x - cx, dz = p.y - cz, d2 = dx * dx + dz * dz;
                        if (d2 >= r * r) continue;
                        if (d2 > 1e-9f)
                        {
                            float d = Mathf.Sqrt(d2), push = r - d;
                            p.x += dx / d * push; p.y += dz / d * push;
                        }
                        else
                        {   // centre inside the cell: leave through the nearest side that opens onto a walkable cell
                            // (pushing through any side could shove a crowded hero deeper into a thick wall)
                            float l = p.x - minX, rr = maxX - p.x, t = p.y - minZ, b = maxZ - p.y, m = float.MaxValue; int side = -1;
                            if (Walkable(x - 1, z) && l < m) { m = l; side = 0; }
                            if (Walkable(x + 1, z) && rr < m) { m = rr; side = 1; }
                            if (Walkable(x, z - 1) && t < m) { m = t; side = 2; }
                            if (Walkable(x, z + 1) && b < m) { m = b; side = 3; }
                            if (side < 0) { p = NearestOpen(x, z, p); continue; }
                            if (side == 0) p.x = minX - r; else if (side == 1) p.x = maxX + r; else if (side == 2) p.y = minZ - r; else p.y = maxZ + r;
                        }
                    }
            }
        }

        // the centre of the nearest walkable cell (rings out from x, z); p itself when there's none nearby
        static Vector2 NearestOpen(int x, int z, Vector2 p)
        {
            for (int ring = 1; ring < 6; ring++)
            {
                Vector2 best = p; float bd = float.MaxValue;
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring || !Walkable(x + dx, z + dz)) continue;
                        var c = Center(x + dx, z + dz); float d = (c - p).sqrMagnitude;
                        if (d < bd) { bd = d; best = c; }
                    }
                if (bd < float.MaxValue) return best;
            }
            return p;
        }

        // merge same-type cells into rectangles (fewer meshes, cleaner shadows)
        public static List<RectInt> Rects(Func<char, bool> test)
        {
            var used = new bool[H, W];
            var outList = new List<RectInt>();
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    if (used[z, x] || !test(grid[z, x])) continue;
                    int w = 1;
                    while (x + w < W && !used[z, x + w] && test(grid[z, x + w])) w++;
                    int h = 1;
                    bool ok = true;
                    while (ok && z + h < H)
                    {
                        for (int i = 0; i < w; i++)
                            if (used[z + h, x + i] || !test(grid[z + h, x + i])) { ok = false; break; }
                        if (ok) h++;
                    }
                    for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) used[z + j, x + i] = true;
                    outList.Add(new RectInt(x, z, w, h));
                }
            return outList;
        }
    }
}
