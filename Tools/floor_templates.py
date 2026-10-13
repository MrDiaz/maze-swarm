# Sector-run floor layouts (compact, the facility look) -> C# string arrays for MazeMap.FloorTemplates.
# Arena templates (PURGE / SURVIVE / HOLD / HUNT / BOSS): P start, V vents, Z hold-zone centre, N nest spots (3+),
# X barrels, C crates (2x2), B blocks, ~ blue zones, y stripes, r railings. Escape templates: rooms chained by 3-wide
# blast doors (yDy / yyy-DDD-yyy in 3-thick walls), E the 3x3 lift pad in the last room.
# Every template is checked: one connected walkable area (doors open), markers on floor, P away from vents.
#   python3 -I floor_templates.py > floors.cs
import sys
def grid(w, h):
    g = [['.'] * w for _ in range(h)]
    for x in range(w): g[0][x] = g[h-1][x] = '#'
    for z in range(h): g[z][0] = g[z][w-1] = '#'
    return g
def rect(g, x, z, w, h, ch):
    for zz in range(z, z+h):
        for xx in range(x, x+w): g[zz][xx] = ch
def put(g, pts, ch):
    for x, z in pts: g[z][x] = ch

T = {}
# 1 PLAZA: a central block, crate pairs, blue corners
g = grid(28, 18)
rect(g, 12, 7, 4, 4, 'B'); rect(g, 2, 2, 4, 3, '~'); rect(g, 22, 13, 4, 3, '~')
for x, z in ((6, 4), (20, 4), (6, 12), (20, 12)): rect(g, x, z, 2, 2, 'C')
rect(g, 11, 13, 6, 1, 'y')
put(g, [(13, 15)], 'P'); put(g, [(1+1, 8), (26-1, 8), (13, 1+1), (3, 15), (24, 2)], 'V')
put(g, [(13, 4)], 'Z'); put(g, [(3, 6), (24, 6), (9, 15)], 'N'); put(g, [(9, 8), (18, 9)], 'X')
T['PLAZA'] = g
# 2 CROSSROADS: four blocks, a plus of corridors
g = grid(30, 18)
for x, z in ((3, 3), (18, 3), (3, 11), (18, 11)): rect(g, x, z, 9, 4, 'B')
rect(g, 13, 7, 4, 4, '~')
put(g, [(14, 15)], 'P'); put(g, [(2, 1+1), (27, 2), (2, 15), (27, 15), (14, 1+1)], 'V')
put(g, [(15, 9)], 'Z'); put(g, [(2, 9), (27, 9), (14, 2+1)], 'N'); put(g, [(8, 9), (22, 8)], 'X')
rect(g, 13, 13, 4, 1, 'y')
T['CROSSROADS'] = g
# 3 PILLARS: a grid of 2x2 pillars
g = grid(28, 20)
for x in (5, 11, 17, 23):
    for z in (4, 9, 14):
        if not (x == 11 and z == 9) and not (x == 17 and z == 9): rect(g, x - 1, z - 1, 2, 2, 'B')
put(g, [(14, 17)], 'P'); put(g, [(2, 2), (25, 2), (2, 17), (25, 17), (14, 1+1)], 'V')
put(g, [(14, 9)], 'Z'); put(g, [(8, 2), (20, 2), (14, 12)], 'N'); put(g, [(8, 11), (20, 6)], 'X')
rect(g, 12, 15, 5, 1, 'y')
T['PILLARS'] = g
# 4 YARD: wide, rows of crates, railings
g = grid(32, 16)
for x in (5, 11, 19, 25): rect(g, x, 3, 2, 2, 'C'); rect(g, x, 10, 2, 2, 'C')
rect(g, 9, 7, 6, 1, 'r'); rect(g, 18, 7, 6, 1, 'r'); rect(g, 2, 12, 3, 2, '~'); rect(g, 27, 2, 3, 2, '~')
put(g, [(16, 13)], 'P'); put(g, [(2, 2), (29, 13), (16, 1+1), (2, 8), (29, 8)], 'V')
put(g, [(16, 4)], 'Z'); put(g, [(4, 7), (28, 6), (16, 9)], 'N'); put(g, [(8, 12), (24, 4)], 'X')
T['YARD'] = g
# 5 LOOP: a big central building, a corridor loop around it
g = grid(26, 20)
rect(g, 7, 6, 12, 8, 'B'); rect(g, 7, 6, 2, 2, '.')
rect(g, 3, 3, 2, 2, 'C'); rect(g, 21, 15, 2, 2, 'C')
put(g, [(13, 17)], 'P'); put(g, [(2, 2), (23, 2), (2, 17), (23, 17), (13, 1+1)], 'V')
put(g, [(13, 3)], 'Z'); put(g, [(3, 10), (22, 10), (19, 16)], 'N'); put(g, [(5, 15), (20, 4)], 'X')
rect(g, 10, 15, 6, 1, 'y')
T['LOOP'] = g
# 6 SPLIT: a dividing wall with two gaps
g = grid(30, 18)
rect(g, 14, 1, 2, 5, '#'); rect(g, 14, 9, 2, 3, '#'); rect(g, 14, 15, 2, 2, '#')
rect(g, 5, 7, 3, 3, 'B'); rect(g, 22, 7, 3, 3, 'B'); rect(g, 9, 13, 2, 2, 'C'); rect(g, 19, 3, 2, 2, 'C')
rect(g, 2, 2, 4, 2, '~')
put(g, [(7, 15)], 'P'); put(g, [(27, 2), (27, 15), (22, 1+1), (2, 9), (20, 15)], 'V')
put(g, [(22, 12)], 'Z'); put(g, [(26, 6), (11, 3), (24, 15)], 'N'); put(g, [(11, 10), (19, 12)], 'X')
T['SPLIT'] = g

def vdoor(g, x, z):   # a door in a vertical 3-thick wall at columns x..x+2, rows z..z+2
    for zz in range(z, z+3): g[zz][x] = 'y'; g[zz][x+1] = 'D'; g[zz][x+2] = 'y'
def hdoor(g, x, z):   # in a horizontal 3-thick wall at rows z..z+2, columns x..x+2
    for xx in range(x, x+3): g[z][xx] = 'y'; g[z+1][xx] = 'D'; g[z+2][xx] = 'y'
# 7 ESCAPE RUN: three rooms left to right
g = grid(54, 14)
rect(g, 17, 1, 3, 12, '#'); rect(g, 35, 1, 3, 12, '#')
vdoor(g, 17, 5); vdoor(g, 35, 8)
rect(g, 7, 3, 2, 2, 'C'); rect(g, 11, 9, 2, 2, 'C'); rect(g, 24, 3, 3, 3, 'B'); rect(g, 28, 9, 2, 2, 'C')
rect(g, 46, 5, 3, 3, 'E'); rect(g, 44, 4, 7, 1, 'y'); rect(g, 44, 8, 7, 1, 'y')
put(g, [(3, 7)], 'P'); put(g, [(14, 2), (14, 11), (22, 2), (32, 11), (31, 2), (40, 11), (51, 2), (40, 2)], 'V')
put(g, [(9, 11), (30, 6)], 'X')
T['ESC_LINE'] = g
# 8 ESCAPE BEND: an L of three rooms, down then right
g = grid(40, 26)
rect(g, 1, 9, 22, 3, '#'); rect(g, 20, 12, 3, 13, '#'); rect(g, 23, 1, 16, 11, '#'); rect(g, 13, 1, 10, 8, '#')
hdoor(g, 6, 9); vdoor(g, 20, 17)
rect(g, 4, 3, 2, 2, 'C'); rect(g, 8, 15, 3, 3, 'B'); rect(g, 15, 20, 2, 2, 'C'); rect(g, 27, 15, 2, 2, 'C')
rect(g, 32, 19, 3, 3, 'E'); rect(g, 31, 18, 5, 1, 'y'); rect(g, 31, 22, 5, 1, 'y')
put(g, [(3, 5)], 'P'); put(g, [(10, 2), (11, 7), (2, 14), (17, 14), (3, 23), (25, 14), (37, 13), (37, 23)], 'V')
put(g, [(13, 13), (29, 21)], 'X')
T['ESC_BEND'] = g

def check(name, g):
    h, w = len(g), len(g[0]); solid = set('#BCr')
    cells = [(x, z) for z in range(h) for x in range(w) if g[z][x] not in solid]
    p = [(x, z) for x, z in cells if g[z][x] == 'P']
    assert len(p) == 1, (name, 'P')
    seen = {p[0]}; st = [p[0]]
    while st:
        x, z = st.pop()
        for dx, dz in ((1,0),(-1,0),(0,1),(0,-1)):
            n = (x+dx, z+dz)
            if 0 <= n[0] < w and 0 <= n[1] < h and g[n[1]][n[0]] not in solid and n not in seen: seen.add(n); st.append(n)
    assert len(seen) == len(cells), (name, 'unreachable', len(cells) - len(seen))
    for x, z in cells:
        if g[z][x] == 'V': assert abs(x - p[0][0]) + abs(z - p[0][1]) >= 7, (name, 'vent near P', x, z)
    esc = name.startswith('ESC')
    if esc: assert any(g[z][x] == 'E' for x, z in cells), name
    else:
        assert sum(g[z][x] == 'N' for x, z in cells) >= 3 and any(g[z][x] == 'Z' for x, z in cells), name
    return len(cells), w, h

out = []
for name, g in T.items():
    n, w, h = check(name, g)
    print(f"// {name}: {w}x{h}, {n} open cells", file=sys.stderr)
    out.append(f"            // {name} ({w}x{h})\n            new[]\n            {{\n" + "".join(f"                \"{''.join(r)}\",\n" for r in g) + "            },")
print("\n".join(out))
print("NAMES " + ", ".join(f'"{k}"' for k in T), file=sys.stderr)
