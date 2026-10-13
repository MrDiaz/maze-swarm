#!/usr/bin/env python3
"""Cryo Station "Frosted lab" textures (standard library only).

    python3 Tools/cryo_tiles.py

Input: the six tiles cropped from Concepts/CryoStation/cryo_d_texture_sheet.png into
Concepts/CryoStation/tiles/src/*.png (512x512; see the crop commands in the README there).
Output (512x512 PNG, tileable) into Concepts/CryoStation/tiles/ (for the web build) and
Assets/Maze/Resources/Cryo/ (the phone build):
  floor_plate, frost_wall, cryo_pad, snow, ice, creep   the sheet's tiles made seamless
  floor_detail      floor plate as a grey detail map (0.5 = neutral, URP Lit "detail x2")
  cryo_pad_emit     the pad's cyan light strips only (emission map)
  creep_mask        organic creep decal: creep colour, alpha = growth field (alpha-clip spreads it)
  drift             snow drift strip: alpha fades from the wall (v=0) out onto the floor (v=1)
  crate             dark grey / yellow industrial crate face (procedural)
"""
import math, os, random, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fx_textures as fx
from planet_infestation import read_png

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Concepts", "CryoStation", "tiles", "src")
OUT = os.path.join(ROOT, "Concepts", "CryoStation", "tiles")
RES = os.path.join(ROOT, "Assets", "Maze", "Resources", "Cryo")
N = 512


def load(name):
    w, h, bpp, px = read_png(os.path.join(SRC, name + ".png"))
    assert w == N and h == N, (name, w, h)
    return [[(px[(y*w + x)*bpp] / 255, px[(y*w + x)*bpp+1] / 255, px[(y*w + x)*bpp+2] / 255) for x in range(w)] for y in range(h)]


def seamless(img, band):
    """blend in a half-rolled copy toward the edges, so the result wraps (the rolled copy is continuous across the seam)"""
    out = [[None]*N for _ in range(N)]
    for y in range(N):
        wy = min(y, N - 1 - y) / (band * N)
        for x in range(N):
            wx = min(x, N - 1 - x) / (band * N)
            w = min(1.0, wx, wy); w = w * w * (3 - 2 * w)
            a = img[y][x]; b = img[(y + N//2) % N][(x + N//2) % N]
            out[y][x] = tuple(a[i] * w + b[i] * (1 - w) for i in range(3))
    return out


def save(name, img, alpha=None):
    px = bytearray(N * N * 4)
    for y in range(N):
        for x in range(N):
            c = img[y][x]; a = 1.0 if alpha is None else alpha[y][x]
            fx.put(px, N, x, y, (min(1, max(0, c[0])), min(1, max(0, c[1])), min(1, max(0, c[2])), min(1, max(0, a))))
    for d in (OUT, RES):
        os.makedirs(d, exist_ok=True)
        fx.write_png(os.path.join(d, name + ".png"), N, N, px)
    print(name)


def lum(c): return 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]


def noise(rnd, k):
    ph = [rnd.uniform(0, 6.28) for _ in range(8)]
    def f(x, y):  # tileable (integer frequencies over the tile)
        u, v = x / N * 6.2832, y / N * 6.2832
        s = 0.0
        for i, fq in enumerate((k, k * 2, k * 3, k * 5)):
            s += math.sin(u * fq + ph[i] + math.sin(v * fq * 0.5 + ph[i+4]) * 1.3) * math.cos(v * fq + ph[(i+2) % 8]) / (i + 1)
        return 0.5 + 0.35 * s
    return f


def main():
    tiles = {}
    for name, band in (("floor_plate", 0.08), ("frost_wall", 0.08), ("cryo_pad", 0.06), ("snow", 0.3), ("ice", 0.3), ("creep", 0.3)):
        tiles[name] = seamless(load(name), band)
        save(name, tiles[name])

    # floor detail: luminance around 0.5 (x2 multiply), softened so it reads as plating under the painted floor
    fp = tiles["floor_plate"]; m = sum(lum(fp[y][x]) for y in range(0, N, 4) for x in range(0, N, 4)) / (N * N / 16)
    det = [[(0.5 + (lum(fp[y][x]) - m) * 0.38,) * 3 for x in range(N)] for y in range(N)]
    save("floor_detail", det)

    # pad emission: the saturated bright cyan strips only
    pad = tiles["cryo_pad"]
    emit = []
    for y in range(N):
        row = []
        for x in range(N):
            r, g, b = pad[y][x]
            k = max(0.0, min(1.0, (b - r - 0.3) * 4.5)) * max(0.0, min(1.0, (g - 0.72) * 5))
            row.append((0.35 * k, 0.95 * k, 1.0 * k))
        emit.append(row)
    save("cryo_pad_emit", emit)

    # creep decal: creep colours; alpha = organic growth field (blobs + veins), high in the middle
    rnd = random.Random(11); n1, n2 = noise(rnd, 2), noise(rnd, 5)
    cr = tiles["creep"]
    blobs = [(rnd.uniform(0.3, 0.7) * N, rnd.uniform(0.3, 0.7) * N, rnd.uniform(0.12, 0.25) * N) for _ in range(5)]
    alpha, col = [], []
    for y in range(N):
        ar, cr_row = [], []
        for x in range(N):
            d = min(1.0, math.hypot(x - N/2, y - N/2) / (N * 0.48))
            b = max(math.exp(-((x - bx)**2 + (y - by)**2) / (br * br)) for bx, by, br in blobs)
            a = (1 - d) ** 1.2 * 0.8 + 0.5 * b
            a *= 0.55 + 0.7 * n1(x, y) + 0.25 * (n2(x, y) - 0.5)
            a *= 1 - max(0.0, (d - 0.86) / 0.14)  # zero at the decal edge
            ar.append(max(0.0, min(1.0, a)))
            c = cr[y][x]
            dark = 0.65 + 0.35 * (1 - a)  # denser growth darker
            cr_row.append((c[0] * dark, c[1] * dark, c[2] * dark))
        alpha.append(ar); col.append(cr_row)
    save("creep_mask", col, alpha)

    # snow drift strip: wraps along u, fades out across v (v = 0 at the wall)
    sn = tiles["snow"]; n3 = noise(random.Random(4), 3)
    da = [[max(0.0, min(1.0, (1 - y / N) ** 0.8 * 1.25 - 0.35 * (1 - n3(x, y)) * (y / N) * 1.6)) for x in range(N)] for y in range(N)]
    save("drift", [[tuple(min(1, v * 1.04) for v in sn[y][x]) for x in range(N)] for y in range(N)], da)

    # industrial crate face: dark grey panels, yellow edge bands and hazard corner, frost on the top edge
    rnd = random.Random(8); n4 = noise(rnd, 6)
    img = []
    for y in range(N):
        row = []
        for x in range(N):
            u, v = x / N, y / N
            base = 0.26 + 0.05 * n4(x, y)
            c = [base, base * 1.04, base * 1.1]
            edge = min(u, 1 - u, v, 1 - v)
            if edge < 0.06: c = [0.86, 0.66, 0.12]                                   # yellow frame
            elif edge < 0.075: c = [0.12, 0.13, 0.15]
            if 0.2 < v < 0.8 and abs(u - 0.5) < 0.012: c = [0.14, 0.15, 0.17]       # centre seam
            if 0.12 < u < 0.4 and 0.12 < v < 0.24 and int((x + y) / 14) % 2 == 0: c = [0.9, 0.7, 0.14]  # hazard patch
            if 0.6 < u < 0.86 and 0.14 < v < 0.22: c = [0.62, 0.66, 0.7]             # label plate
            frost = max(0.0, 1 - v / 0.16) * (0.5 + 0.5 * n4(x * 2 % N, y))           # rime along the top
            c = [ci * (1 - frost) + 0.93 * frost for ci in c]
            row.append(tuple(c))
        img.append(row)
    save("crate", img)


if __name__ == "__main__":
    main()
