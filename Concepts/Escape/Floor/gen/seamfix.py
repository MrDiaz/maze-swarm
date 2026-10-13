"""Grid-aware seam fix: wrap seam = head dark run + tail dark run; trim the tail so the
wrapped seam matches the interior seam width, then resize to 1024 (power of two)."""
import sys, numpy as np
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGB"); a = np.asarray(im.convert("L")).astype(float) / 255
def trim(p):
    base = np.median(p); dark = p < base - 0.12
    n = len(p); h = 0
    while dark[h]: h += 1
    t = 0
    while dark[n - 1 - t]: t += 1
    # interior seam width: longest dark run in the middle half
    mid = dark[n // 4: 3 * n // 4]; best = run = 0
    for v in mid: run = run + 1 if v else 0; best = max(best, run)
    cut = max(0, min(t, h + t - best))
    return h, t, best, cut
hx, tx, mx, cx = trim(a.mean(0)); hy, ty, my, cy = trim(a.mean(1))
w, hgt = im.size
print(f"{src}: cols head {hx} tail {tx} mid {mx} -> trim {cx}; rows head {hy} tail {ty} mid {my} -> trim {cy}")
im.crop((0, 0, w - cx, hgt - cy)).resize((1024, 1024), Image.LANCZOS).save(dst)
