# Design/zones-*.json (the drop-zone specs) -> Assets/Maze/Scripts/ZoneData.cs: one SectorDef per zone (theme colours,
# authored floors, enemy counts, mechanics, boss, stars, par, power, unlocks) plus every ASCII layout in the v2 legend
# (Design/drop-zones-core.json). Rerun after a spec changes:
#   python3 -I Tools/zones_gen.py
import json, os, re, sys, glob

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGN = os.path.join(ROOT, 'Design')
OUT = os.path.join(ROOT, 'Assets', 'Maze', 'Scripts', 'ZoneData.cs')

zones = []
for f in sorted(glob.glob(os.path.join(DESIGN, 'zones-*.json'))):
    zones += json.load(open(f, encoding='utf-8'))
# the patched per-floor event scripts (and their layouts, which supersede the zone files' ones)
scripts = {}
for f in sorted(glob.glob(os.path.join(DESIGN, 'zone-scripts-*.json'))):
    if f.endswith('-rev2.json'): continue
    for zs in json.load(open(f, encoding='utf-8')): scripts[zs['id']] = zs
for z in zones:
    zs = scripts.get(z['id'])
    if not zs: continue
    z['layouts'].update(zs.get('layouts', {}))
    for fl, sf in zip(z['floors'], zs.get('floors', [])):
        fl['script'] = sf.get('script', [])
        if sf.get('layout'): fl['layout'] = sf['layout']
# revision 2: full replacement scripts for single floors (zone + floor number; layout ids unchanged)
rev2 = os.path.join(DESIGN, 'zone-scripts-rev2.json')
if os.path.exists(rev2):
    by = {z['id']: z for z in zones}
    for r in json.load(open(rev2, encoding='utf-8'))['rev2_full_scripts']:
        z = by.get(r['zone'])
        if not z: print('rev2: unknown zone', r['zone']); continue
        fl = next((f for f in z['floors'] if f.get('n') == r['floor']), None) or (z['floors'][r['floor'] - 1] if r['floor'] - 1 < len(z['floors']) else None)
        if not fl: print('rev2: no floor', r['zone'], r['floor']); continue
        if fl.get('layout') != r['layout']: print('rev2: layout differs', r['zone'], r['floor'], fl.get('layout'), r['layout'])
        fl['script'] = r['script']
# revision 3: full floor replacements (objective, params, enemies, script, layout) + layout replacements; after rev2
rev3 = os.path.join(DESIGN, 'zone-rev3.json')
if os.path.exists(rev3):
    r3 = json.load(open(rev3, encoding='utf-8')); by = {z['id']: z for z in zones}
    for r in r3['rev3_floors']:
        z = by.get(r['zone'])
        if not z: print('rev3: unknown zone', r['zone']); continue
        fl = next((f for f in z['floors'] if f.get('n') == r['floor']), None)
        if not fl: print('rev3: no floor', r['zone'], r['floor']); continue
        for k in ('objective', 'params', 'enemies', 'script', 'layout'):
            if k in r: fl[k] = r[k]
        if r['layout'] not in z['layouts']: print('rev3: unknown layout', r['zone'], r['layout'])
    for k, v in r3.items():
        if k.startswith('layout_'):
            lid = k[len('layout_'):].replace('_', '-')
            for z in zones:
                if lid in z['layouts']: z['layouts'][lid] = v; print('rev3: layout', z['id'], lid)
# rev3 trims: value-only edits on single events (match by action + params; optional new 'every' seconds)
trims = os.path.join(DESIGN, 'zone-rev3-trims.json')
if os.path.exists(trims):
    by = {z['id']: z for z in zones}
    for t in json.load(open(trims, encoding='utf-8'))['trims']:
        fl = next((f for f in by[t['zone']]['floors'] if f.get('n') == t['floor']), None); hit = 0
        for ev in (fl or {}).get('script', []):
            pr = ev.get('params', {}); w = ev.get('when', {})
            if ev.get('do') != t['do'] or any(pr.get(k) != v for k, v in t['match'].items()): continue
            if 'when_from' in t and (w.get('every') or {}).get('from') != t['when_from']: continue
            pr.update(t['set']); hit += 1
            if 'when_every' in t and 'every' in w: w['every']['s'] = t['when_every']
        print('trim', t['zone'], t['floor'], t['do'], 'events changed:', hit)
zones.sort(key=lambda z: z.get('num', 99) or 99)   # Swarm Gate (num 0, the endless mode) last

def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
def hexes(s): return re.findall(r'#([0-9a-fA-F]{6})', s or '')
def col(h): return f'C(0x{h.lower()})'
def num_after(s, word, default):
    m = re.search(word + r'\s*([0-9.]+)', s or '')
    return float(m.group(1)) if m else default

# spec objective -> the game's Goal (new objectives map to the closest one until they exist)
GOALS = {'PURGE': 'Purge', 'SURVIVE': 'Survive', 'ESCAPE': 'Escape', 'HOLD': 'Hold', 'HUNT': 'Hunt', 'BOSS': 'Boss',
         'UPLINK': 'Uplink', 'ESCORT': 'Escort', 'SALVAGE': 'Salvage', 'DEFEND': 'Defend', 'DETONATE': 'Detonate',
         'OUTRUN': 'Outrun', 'RELAY': 'Relay', 'BURN': 'Burn', 'ASSASSINATE': 'Assassinate'}

def particles(t):
    s = ' '.join(str(v) for v in t.values()).lower()
    for key, p in (('snow', 'Snow'), ('rain', 'Rain'), ('ember', 'Embers'), ('spore', 'Spores'), ('pollen', 'Spores'), ('ash', 'Ash'), ('dust', 'Ash'), ('spray', 'Rain')):
        if key in s: return p
    return 'None'

def fog(t):
    s = t.get('fog', '')
    m = re.search(r'linear\s*([0-9.]+)\s*-\s*([0-9.]+)', s)
    if m: return float(m.group(1)), float(m.group(2))
    d = num_after(s, 'exponential', 0)
    if d > 0: return 6.0, round(1.8 / d, 1)
    return 40.0, 120.0

def secs(par):
    m = re.match(r'(\d+):(\d+)', par or '')
    return int(m.group(1)) * 60 + int(m.group(2)) if m else 300

lines = []
W = lines.append
W('// GENERATED by Tools/zones_gen.py from Design/zones-*.json. Do not edit by hand: change the spec and rerun.')
W('using UnityEngine;')
W('using System.Collections.Generic;')
W('')
W('namespace Maze')
W('{')
W('    public static partial class ZoneData')
W('    {')
W('        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);')
W('        public static readonly Dictionary<string, string[]> Layouts = new()')
W('        {')
for z in zones:
    for lid, rows in z['layouts'].items():
        W(f'            {{ {cs(z["id"] + "/" + lid)}, new[] {{')   # keyed by zone: two zones both have a 'spire-a'
        for r in rows: W(f'                {cs(r)},')
        W('            } },')
W('        };')
W('')
W('        public static List<SectorDef> Build() => new()')
W('        {')
for z in zones:
    t = z.get('theme', {})
    fh, wh, lh, gh = hexes(t.get('floor')), hexes(t.get('walls')), hexes(t.get('lighting')), hexes(t.get('fog'))
    floorTint = fh[0] if fh else 'ffffff'
    wallTint = wh[0] if wh else 'ffffff'
    accent = (wh[1] if len(wh) > 1 else (fh[1] if len(fh) > 1 else '7fd6ff'))
    sun = lh[0] if lh else 'fff6ea'
    m = re.search(r'ambient[^#]*#([0-9a-fA-F]{6})', t.get('lighting', ''))   # only an explicit 'ambient #..' (not alarm lamps)
    amb = m.group(1) if m else 'a0a8b8'
    fogc = gh[0] if gh else 'c8ccd4'
    sunI = num_after(t.get('lighting'), 'intensity', None) or num_after(t.get('lighting'), r'#[0-9a-fA-F]{6}\s*', 1.1)
    if not (0.1 <= sunI <= 2.5): sunI = 1.1
    f0, f1 = fog(t)
    goals = ', '.join('Game.Goal.' + GOALS.get(f['objective'], 'Purge') for f in z['floors'])
    floors = []
    for f in z['floors']:
        en = ', '.join(f'new EnemyEntry({cs(e["enemy"])}, {e["count"]})' for e in f.get('enemies', []))
        floors.append(f'new ZoneFloor {{ goal = Game.Goal.{GOALS.get(f["objective"], "Purge")}, spec = {cs(f["objective"])}, param = {cs(f.get("params", ""))}, layout = {cs(z["id"] + "/" + f["layout"])}, enemies = new EnemyEntry[] {{ {en} }}, events = {cs(f.get("events", ""))}, script = {cs(json.dumps(f.get("script", []), separators=(",", ":")))} }}')
    st = z.get('stars', {})
    boss = z.get('boss', {})
    unlock = z.get('unlock', {})
    req = unlock.get('requires_any', [])
    pitch = z.get('pitch', '')
    blurb = pitch.split(':')[0].split('.')[0]
    key = (z['id'] + ' ' + z['name'] + ' ' + str(t)).lower()
    art = next((a for k, a in (('cryo', 'cryo'), ('ice', 'cryo'), ('quarantine', 'hive'), ('hive', 'hive'), ('brood', 'hive'), ('marsh', 'verdant'), ('verdant', 'verdant'), ('fung', 'verdant'),
                ('drown', 'drowned'), ('port', 'drowned'), ('vault', 'drowned'), ('spire', 'drowned'), ('neon', 'drowned')) if k in key), 'kessler')
    i = max(0, (z.get('num', 1) or 1) - 1); n = 20
    import math
    lat = round(math.degrees(math.asin(1 - 2 * (i + 0.5) / n)) * 0.85, 1); lon = round((i * 137.508 + 17) % 360 - 180, 1)
    power = z.get('power', 900); tier = sum(1 for r in (900, 1300, 1900, 2750, 4000, 5800) if power >= r) or 1
    endless = 'true' if z.get('num', 0) == 0 else 'false'
    if endless == 'true': lat, lon = -30.0, 160.0   # clear of the Escape Test pin
    W(f'            new SectorDef {{ id = {cs(z["id"])}, name = {cs(z["name"].upper())}, num = {z.get("num", 0)}, act = {cs(z.get("act", ""))}, power = {power}, tier = {tier}, art = {cs(art)}, lat = {lat}f, lon = {lon}f, endless = {endless},')
    W(f'                blurb = {cs(blurb)}, pitch = {cs(pitch)}, signature = {cs(z.get("signature", ""))},')
    W(f'                unlockAny = new string[] {{ {", ".join(cs(r) for r in req)} }}, unlockAfter = {cs(req[0]) if req else "null"},')
    W(f'                floorTint = {col(floorTint)}, wallTint = {col(wallTint)}, accent = {col(accent)}, sun = {col(sun)}, sunIntensity = {sunI}f, ambient = {col(amb)}, fog = {col(fogc)}, fogStart = {f0}f, fogEnd = {f1}f, particles = Particles.{particles(t)},')
    W(f'                order = new[] {{ {goals} }},')
    W(f'                floors = new ZoneFloor[] {{')
    for fl in floors: W(f'                    {fl},')
    W('                },')
    W(f'                mechanics = new[] {{ {", ".join(cs(m) for m in z.get("mechanics_all", z.get("mechanics", [])))} }},')
    W(f'                boss = {cs(boss.get("name", "Gravid Bloater").split(" (")[0])}, bossBody = {cs(boss.get("body", ""))}, bossLook = {cs(boss.get("look", ""))},')
    W(f'                modifiers = new[] {{ {", ".join(cs(m) for m in z.get("modifiers", []))} }},')
    W(f'                stars = new[] {{ {cs(st.get("1", "Clear the zone."))}, {cs(st.get("2", "Under par."))}, {cs(st.get("3", ""))} }}, starBonus = {cs(st.get("bonus", ""))}, par = {secs(z.get("par") or st.get("par"))},')
    W(f'                layoutNotes = new Dictionary<string, string> {{ {", ".join("{" + cs(k) + ", " + cs(v) + "}" for k, v in z.get("layout_notes", {}).items())} }} }},')
W('        };')
W('    }')
W('}')
open(OUT, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print(f'{len(zones)} zones, {sum(len(z["layouts"]) for z in zones)} layouts -> {OUT}')
