# Slice a 2x2 white-background icon sheet into 256x256 transparent PNGs.
#   uv run --with pillow python slice.py <sheet.png> <outdir> name_tl name_tr name_bl name_br   ("-" skips a quadrant)
# White is keyed by flood fill from the quadrant border (so white parts inside the outlined icon stay opaque),
# with a soft alpha ramp on near-white edge pixels; then trimmed and padded to 256 with a 20 px margin like the approved set.
import sys
from collections import deque
from PIL import Image

sheet, out, names = sys.argv[1], sys.argv[2], sys.argv[3:7]
im = Image.open(sheet).convert("RGBA")
W, H = im.size

def key(q):
    w, h = q.size; px = q.load()
    def whiteness(p):  # 0 = clearly not background, 1 = pure white
        m = min(p[0], p[1], p[2]); spread = max(p[:3]) - m
        return 0.0 if spread > 40 else max(0.0, min(1.0, (m - 200) / 50))
    seen = bytearray(w * h); dq = deque()
    for x in range(w):
        dq.append((x, 0)); dq.append((x, h - 1))
    for y in range(h):
        dq.append((0, y)); dq.append((w - 1, y))
    while dq:
        x, y = dq.popleft(); i = y * w + x
        if seen[i]: continue
        seen[i] = 1
        p = px[x, y]; wv = whiteness(p)
        if wv <= 0: continue
        a = int(round(255 * (1 - wv)))
        if a: # partially white edge pixel: un-premultiply against white
            f = 255 / a
            px[x, y] = tuple(max(0, min(255, int(255 - (255 - c) * f))) for c in p[:3]) + (a,)
        else:
            px[x, y] = (0, 0, 0, 0)
        if wv > 0.5:
            for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                if 0 <= nx < w and 0 <= ny < h and not seen[ny*w+nx]: dq.append((nx, ny))
    return q

for idx, name in enumerate(names):
    if name == "-": continue
    cx, cy = idx % 2, idx // 2
    q = key(im.crop((cx * W // 2, cy * H // 2, (cx + 1) * W // 2, (cy + 1) * H // 2)))
    a = q.getchannel("A").point(lambda v: 255 if v > 12 else 0)
    q = q.crop(a.getbbox())
    s = 216 / max(q.size)
    q = q.resize((max(1, round(q.width * s)), max(1, round(q.height * s))), Image.LANCZOS)
    c = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    c.paste(q, ((256 - q.width) // 2, (256 - q.height) // 2), q)
    c.save(f"{out}/{name}.png")
    print("wrote", name)
