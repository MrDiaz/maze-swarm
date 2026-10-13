#!/usr/bin/env python3
"""The sector-select planet's spreading infestation overlay (standard library only).

    python3 Tools/planet_infestation.py <planet 1024x512 png> Assets/Maze/Resources/UI/planet_infest.png

Finds the dark purple hive growth and green nodes in the planet texture and turns them into a growth field:
alpha 1 at the hive cores, falling off outward through blurred, noisy fringes. MapSelect alpha-clips this
layer with a cutoff that sinks slowly, so the infestation visibly creeps out from the cores.
"""
import math, random, struct, sys, zlib
sys.path.insert(0, __import__("os").path.dirname(__file__))
import fx_textures as fx


def read_png(p):
    d = open(p, 'rb').read(); i = 8; idat = b''
    while i < len(d):
        n = struct.unpack('>I', d[i:i+4])[0]; t = d[i+4:i+8]; c = d[i+8:i+8+n]
        if t == b'IHDR': w, h, bd, ct = struct.unpack('>IIBB', c[:10])
        if t == b'IDAT': idat += c
        i += 12 + n
    bpp = 4 if ct == 6 else 3
    raw = zlib.decompress(idat); s = w * bpp; out = bytearray(w * h * bpp); prev = bytearray(s)
    for y in range(h):
        f = raw[y*(s+1)]; line = bytearray(raw[y*(s+1)+1:(y+1)*(s+1)])
        for x in range(s):
            a = line[x-bpp] if x >= bpp else 0; b = prev[x]; c = prev[x-bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p_ = a + b - c; pa, pb, pc = abs(p_-a), abs(p_-b), abs(p_-c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out[y*s:(y+1)*s] = line; prev = line
    return w, h, bpp, out


def blur(f, w, h, r):
    """box blur, wrapping in x (longitude), clamped in y"""
    out = [0.0] * (w * h)
    for y in range(h):
        row = f[y*w:(y+1)*w]; acc = sum(row[x % w] for x in range(-r, r + 1))
        for x in range(w):
            out[y*w + x] = acc / (2*r + 1)
            acc += row[(x + r + 1) % w] - row[(x - r) % w]
    res = [0.0] * (w * h)
    for x in range(w):
        acc = sum(out[min(h-1, max(0, y))*w + x] for y in range(-r, r + 1))
        for y in range(h):
            res[y*w + x] = acc / (2*r + 1)
            acc += out[min(h-1, y + r + 1)*w + x] - out[min(h-1, max(0, y - r))*w + x]
    return res


def main():
    w, h, bpp, px = read_png(sys.argv[1])
    hive = [0.0] * (w * h)
    for i in range(w * h):
        r, g, b = px[i*bpp], px[i*bpp+1], px[i*bpp+2]
        purple = (r > g + 8 and b > g + 8 and max(r, b) < 170)
        green_node = g > 170 and g > r + 40 and g > b + 60
        hive[i] = 1.0 if purple or green_node else 0.0
    core = blur(hive, w, h, 2)
    field = blur(core, w, h, 9)
    wide = blur(field, w, h, 14)
    rnd = random.Random(3)
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    out = bytearray(w * h * 4)
    for y in range(h):
        for x in range(w):
            i = y*w + x
            u, v = x / w * 6.283, y / h * 3.1416
            n = 0.5 + 0.25*math.sin(u*7 + ph[0] + math.sin(v*5 + ph[1])) + 0.25*math.sin(v*11 + ph[2] + math.cos(u*9 + ph[3]))
            a = max(core[i] * 1.4, field[i] * 2.2 * (0.6 + 0.6*n), wide[i] * 2.6 * n)
            a = min(1.0, a)
            # growth colour: dark violet flesh with green glow at the densest parts
            k = min(1.0, core[i] * 1.5)
            c = (0.32 + 0.1*k, 0.12 + 0.55*k*k*(n > 0.6), 0.42 + 0.05*k)
            fx.put(out, w, x, y, c + (a,))
    fx.write_png(sys.argv[2], w, h, out)
    print("wrote", sys.argv[2])


if __name__ == "__main__":
    main()
