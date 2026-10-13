#!/usr/bin/env python3
"""Bake Maze Swarm's gunfire textures (standard library only).

    python3 Tools/fx_textures.py   -> Assets/Maze/Resources/Fx/*.png

All soft-edged RGBA (colour in RGB, coverage in A, fading to 0 at every cell edge so mipmaps don't
bleed), for alpha-blended particles: the maze floor is near white, where additive light vanishes, so
the hottest colour is a pale yellow rather than white and the colour itself carries the flash:
  flash_star.png   1536x1536  3x3 cells of 512: rows = AK (three prongs), SMG (small, tight), shotgun (wide)
  flash_cone.png   1536x768   3x3 cells of 512x256: the side-on flame along the barrel, same rows
  streaks.png      256x256    4 columns of 64x256: tracer streaks for RIFLE / SHOTGUN / SMG, then a spark
  puff.png         128x128    soft smoke wisp (white; tinted grey by the particle colour)
  ring.png         256x256    soft shockwave ring (shotgun blast)
Columns of a flash row are three random variants; the game picks one per shot.
"""
import math
import os
import random
import struct
import zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Maze", "Resources", "Fx")


def write_png(path, w, h, px):
    """px: bytearray of RGBA rows"""
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += px[y * w * 4:(y + 1) * w * 4]
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def fire(i):
    """intensity -> (r, g, b, a): deep orange edges, amber body, pale-yellow hot core"""
    a = min(1.0, i * 1.6)  # opaque body: an alpha-blended flash has to cover a bright floor
    t = min(1.0, i / 1.25)
    keys = [(0.0, (0.95, 0.28, 0.02)), (0.45, (1.0, 0.55, 0.1)), (0.75, (1.0, 0.8, 0.3)), (1.0, (1.0, 0.95, 0.66))]
    for k in range(len(keys) - 1):
        (t0, c0), (t1, c1) = keys[k], keys[k + 1]
        if t <= t1:
            f = (t - t0) / (t1 - t0)
            return tuple(c0[j] + (c1[j] - c0[j]) * f for j in range(3)) + (a,)
    return keys[-1][1] + (a,)


def put(px, w, x, y, c):
    o = (y * w + x) * 4
    px[o] = int(c[0] * 255 + 0.5); px[o + 1] = int(c[1] * 255 + 0.5); px[o + 2] = int(c[2] * 255 + 0.5); px[o + 3] = int(c[3] * 255 + 0.5)


# ---------------------------------------------------------------- muzzle star (front view)
STAR = {  # prongs, length, base width (rad), core radius, bloom, length jitter
    0: dict(n=3, L=0.95, w=0.40, core=0.13, bloom=0.30, jit=0.10),   # AK: big three-prong
    1: dict(n=5, L=0.50, w=0.30, core=0.12, bloom=0.22, jit=0.15),   # SMG: small and tight
    2: dict(n=9, L=0.80, w=0.20, core=0.17, bloom=0.42, jit=0.35),   # shotgun: wide burst
}


def star_cell(px, W, ox, oy, S, style, rng):
    p = STAR[style]
    rot = rng.uniform(0, 2 * math.pi)
    petals = [(rot + 2 * math.pi * k / p["n"] + rng.uniform(-0.12, 0.12), p["L"] * rng.uniform(1 - p["jit"], 1.0),
               p["w"] * rng.uniform(0.8, 1.15)) for k in range(p["n"])]
    ripple = rng.uniform(0, 6.28)
    for y in range(S):
        fy = (y + 0.5) / S * 2 - 1
        for x in range(S):
            fx = (x + 0.5) / S * 2 - 1
            r = math.hypot(fx, fy)
            if r >= 1:
                put(px, W, ox + x, oy + y, (1, 0.33, 0.04, 0)); continue
            th = math.atan2(fy, fx)
            best = 0.0
            for a, L, w in petals:
                if r >= L: continue
                d = (th - a + math.pi) % (2 * math.pi) - math.pi
                width = w * (1 - r / L) ** 0.75 + 0.015
                v = math.exp(-(d / width) ** 2) * (1 - r / L) ** 1.1
                if v > best: best = v
            i = 1.25 * math.exp(-(r / p["core"]) ** 2) + 0.95 * best + p["bloom"] * math.exp(-(r / 0.45) ** 2)
            i *= 0.88 + 0.12 * math.cos(th * 11 + ripple)
            i *= 1 - smooth(0.82, 1.0, r)
            put(px, W, ox + x, oy + y, fire(i))


# ---------------------------------------------------------------- muzzle cone (side view, muzzle on the left)
CONE = {  # length, half width, side tongues (angle, length, width), spark lines
    0: dict(L=0.95, w=0.42, tongues=[(0.0, 1.0, 1.0), (0.42, 0.75, 0.55), (-0.42, 0.75, 0.55)], lines=0),
    1: dict(L=0.55, w=0.30, tongues=[(0.0, 1.0, 1.0)], lines=0),
    2: dict(L=0.62, w=0.5, tongues=[(0.0, 1.0, 1.0), (0.33, 0.8, 0.6), (-0.33, 0.8, 0.6)], lines=6),
}


def cone_cell(px, W, ox, oy, CW, CH, style, rng):
    p = CONE[style]
    tongues = [(a + rng.uniform(-0.06, 0.06), l * rng.uniform(0.85, 1.05), w * rng.uniform(0.85, 1.1)) for a, l, w in p["tongues"]]
    lines = [(rng.uniform(-0.55, 0.55), rng.uniform(0.6, 0.95)) for _ in range(p["lines"])]
    wob = rng.uniform(0, 6.28)
    for y in range(CH):
        v = (y + 0.5) / CH * 2 - 1            # across, -1..1
        for x in range(CW):
            u = (x + 0.5) / CW                # along, 0 (muzzle) .. 1
            best = 0.0
            for a, l, w in tongues:
                L = p["L"] * l
                if u >= L: continue
                vv = v - math.tan(a) * u * 1.6  # side tongues lean out
                t = u / L
                hw = p["w"] * w * math.sin(math.pi * min(1.0, t ** 0.55 * 0.92 + 0.08)) ** 0.9 + 0.02
                f = math.exp(-(vv / hw) ** 2) * (1 - t) ** 0.7
                f += 0.7 * math.exp(-(vv / (hw * 0.25)) ** 2) * (1 - t) ** 1.6  # hot core along the axis
                if f > best: best = f
            for a, L in lines:  # shotgun: thin spark streaks
                if u < L:
                    d = v - math.tan(a) * u * 1.6
                    best = max(best, 0.7 * math.exp(-(d / 0.025) ** 2) * (u / L) ** 0.5 * (1 - u / L) ** 0.3)
            i = best * (0.9 + 0.1 * math.sin(u * 40 + wob))
            i *= smooth(0.0, 0.05, u) * (1 - smooth(0.9, 0.99, u)) * (1 - smooth(0.88, 0.99, abs(v)))
            put(px, W, ox + x, oy + y, fire(i * 1.1))


# ---------------------------------------------------------------- tracer streaks + spark
TINTS = [(0.96, 0.77, 0.0), (0.23, 0.66, 1.0), (1.0, 0.42, 0.24), (1.0, 0.55, 0.15)]


def streaks():
    CW, H = 64, 256
    W = CW * 4
    px = bytearray(W * H * 4)
    for col, tint in enumerate(TINTS):
        for y in range(H):
            t = (y + 0.5) / H          # 0 = tail (top row of the PNG = v 1 = head, flipped below)
            along = smooth(0.0, 0.85, t) ** 1.4 * (1 - smooth(0.92, 1.0, t))
            for x in range(CW):
                fx = (x + 0.5) / CW * 2 - 1
                core = math.exp(-(fx / 0.13) ** 2)
                glow = 0.55 * math.exp(-(fx / 0.42) ** 2)
                i = (core + glow) * along * (1 - smooth(0.85, 1.0, abs(fx)))
                if col == 1: i *= 0.6  # shotgun pellets: dimmer than the rifle / SMG tracers
                k = core / (core + glow + 1e-6)
                c = tuple(tint[j] + (1 - tint[j]) * k * 0.55 for j in range(3))  # light core, not white
                put(px, W, col * CW + x, H - 1 - y, c + (min(1.0, i),))
    write_png(os.path.join(OUT, "streaks.png"), W, H, px)


def puff():
    S = 128
    px = bytearray(S * S * 4)
    rng = random.Random(7)
    ph = [rng.uniform(0, 6.28) for _ in range(3)]
    for y in range(S):
        fy = (y + 0.5) / S * 2 - 1
        for x in range(S):
            fx = (x + 0.5) / S * 2 - 1
            r = math.hypot(fx, fy); th = math.atan2(fy, fx)
            lump = 1 + 0.12 * math.cos(3 * th + ph[0]) + 0.08 * math.cos(5 * th + ph[1]) + 0.05 * math.cos(9 * th + ph[2])
            i = math.exp(-(r / (0.5 * lump)) ** 2) * (1 - smooth(0.85, 1.0, r))
            put(px, S, x, y, (1, 1, 1, i))
    write_png(os.path.join(OUT, "puff.png"), S, S, px)


def ring():
    S = 256
    px = bytearray(S * S * 4)
    for y in range(S):
        fy = (y + 0.5) / S * 2 - 1
        for x in range(S):
            fx = (x + 0.5) / S * 2 - 1
            r = math.hypot(fx, fy)
            i = math.exp(-((r - 0.82) / 0.07) ** 2) + 0.25 * math.exp(-((r - 0.7) / 0.18) ** 2)
            i *= 1 - smooth(0.93, 1.0, r)
            put(px, S, x, y, (1, 0.93, 0.8, min(1.0, i)))
    write_png(os.path.join(OUT, "ring.png"), S, S, px)


def main():
    os.makedirs(OUT, exist_ok=True)
    S = 512
    px = bytearray(S * 3 * S * 3 * 4)
    for row in range(3):
        for col in range(3):
            star_cell(px, S * 3, col * S, row * S, S, row, random.Random(row * 10 + col + 1))
    write_png(os.path.join(OUT, "flash_star.png"), S * 3, S * 3, px)
    print("flash_star.png")
    CW, CH = 512, 256
    px = bytearray(CW * 3 * CH * 3 * 4)
    for row in range(3):
        for col in range(3):
            cone_cell(px, CW * 3, col * CW, row * CH, CW, CH, row, random.Random(100 + row * 10 + col))
    write_png(os.path.join(OUT, "flash_cone.png"), CW * 3, CH * 3, px)
    print("flash_cone.png")
    streaks(); print("streaks.png")
    puff(); print("puff.png")
    ring(); print("ring.png")


if __name__ == "__main__":
    main()
