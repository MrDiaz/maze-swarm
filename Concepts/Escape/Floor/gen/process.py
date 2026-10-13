"""Floor tile post-processing: seamless fix, 3x3 check, height->normal/roughness, tilted preview.
usage: python process.py <name> [<name> ...]   (names without .png, files in ../)
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

FLOOR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SMALL = os.path.join(FLOOR, "small")


def load(name):
    im = Image.open(os.path.join(FLOOR, name + ".png")).convert("RGB")
    w, h = im.size
    s = min(w, h)
    if w != h:  # center-crop to square
        im = im.crop(((w - s) // 2, (h - s) // 2, (w - s) // 2 + s, (h - s) // 2 + s))
    return np.asarray(im).astype(np.float32) / 255.0


def edge_mismatch(a):
    """Mean abs difference across the wrap seam, relative to typical neighbour difference."""
    wrap = (np.abs(a[:, 0] - a[:, -1]).mean() + np.abs(a[0] - a[-1]).mean()) / 2
    inner = (np.abs(np.diff(a, axis=1)).mean() + np.abs(np.diff(a, axis=0)).mean()) / 2
    return wrap / max(inner, 1e-6), wrap


def make_seamless(a, band=0.12):
    """Blend a half-offset copy over the edges; centre keeps the original."""
    n = a.shape[0]
    rolled = np.roll(a, (n // 2, n // 2), axis=(0, 1))
    t = np.linspace(0, 1, n)
    d = np.minimum(t, 1 - t)  # 0 at edges, 0.5 at centre
    w1 = np.clip(d / band, 0, 1)
    w1 = w1 * w1 * (3 - 2 * w1)
    w = np.minimum(w1[:, None], w1[None, :])[..., None]
    return a * w + rolled * (1 - w)


def gauss_wrap(x, sigma):
    """Gaussian blur with wrap-around (FFT), so derived maps stay tileable."""
    n, m = x.shape
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(m)[None, :]
    g = np.exp(-2 * (math.pi ** 2) * (sigma ** 2) * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(x) * g))


def lum(a):
    return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722


def save(arr, path, mode="RGB"):
    Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(path)


def small_jpg(path):
    im = Image.open(path).convert("RGB")
    im.thumbnail((1024, 1024), Image.LANCZOS)
    im.save(os.path.join(SMALL, os.path.splitext(os.path.basename(path))[0] + ".jpg"), quality=80)


def process(name):
    a = load(name)
    n = a.shape[0]
    ratio, raw = edge_mismatch(a)
    fixed = ratio > 1.5 and "--blend" in sys.argv
    if fixed:
        os.makedirs(os.path.join(FLOOR, "gen", "orig"), exist_ok=True)
        save(a, os.path.join(FLOOR, "gen", "orig", name + "_orig.png"))
        a = make_seamless(a)
        save(a, os.path.join(FLOOR, name + ".png"))
    ratio2, raw2 = edge_mismatch(a)
    L = lum(a)
    print(f"{name}: {n}px  edge mismatch {ratio:.2f}x (abs {raw:.3f}) -> "
          f"{'blended' if fixed else 'kept'} -> {ratio2:.2f}x;  luminance mean {L.mean():.2f} "
          f"p5 {np.percentile(L,5):.2f} p95 {np.percentile(L,95):.2f}")

    # 3x3 seam check
    tiled = np.tile(a, (3, 3, 1))
    check = Image.fromarray((tiled * 255).astype(np.uint8)).resize((1536, 1536), Image.LANCZOS)
    p = os.path.join(FLOOR, f"tile_check_{name}.png"); check.save(p); small_jpg(p)

    # height from blurred luminance; normal (OpenGL, +Y up = toward image top)
    s = n / 1024.0
    h = gauss_wrap(L, 3.0 * s)
    h = (h - h.min()) / max(h.max() - h.min(), 1e-6)
    strength = 1.2 * s  # gentle
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2  # +row = down
    nx, ny, nz = -dx * strength * 10, dy * strength * 10, np.ones_like(h)
    ln = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    nrm = np.stack([nx / ln, ny / ln, nz / ln], -1)
    save(nrm * 0.5 + 0.5, os.path.join(FLOOR, f"normal_{name}.png"))

    # roughness: panels ~0.55, seams/grime (darker than local surroundings) rougher
    local = gauss_wrap(L, 24 * s)
    cavity = np.clip((local - L) / 0.25, 0, 1)
    rough = 0.55 + 0.35 * cavity + (gauss_wrap(np.random.default_rng(1).random(L.shape), 3 * s) - 0.5) * 0.04
    save(rough, os.path.join(FLOOR, f"roughness_{name}.png"), "L")
    print(f"   normal tilt mean {np.degrees(np.arccos(nrm[...,2])).mean():.1f} deg, roughness mean {rough.mean():.2f}")

    # preview: lit 3x3 at 1536, squashed for ~57 deg camera, dark drone blobs
    tn = np.tile(nrm, (3, 3, 1))
    ld = np.array([-0.45, 0.55, 0.70]); ld /= np.linalg.norm(ld)
    diff = np.clip((tn * ld).sum(-1), 0, 1)
    lit = tiled * (0.35 + 0.75 * diff)[..., None]
    pim = Image.fromarray((np.clip(lit, 0, 1) * 255).astype(np.uint8)).resize((1536, 1536), Image.LANCZOS)
    pim = pim.resize((1536, int(1536 * math.sin(math.radians(57)))), Image.LANCZOS)
    W, H = pim.size
    shadow = Image.new("L", pim.size, 0)
    sd = ImageDraw.Draw(shadow)
    rng = np.random.default_rng(7)
    drones = [(rng.uniform(0.1, 0.9) * W, rng.uniform(0.1, 0.9) * H, rng.uniform(16, 24)) for _ in range(9)]
    for x, y, r in drones:
        sd.ellipse([x - r * 1.2 + 6, y - r * 0.7 + 8, x + r * 1.2 + 6, y + r * 0.7 + 8], fill=110)
    shadow = shadow.filter(ImageFilter.GaussianBlur(6))
    pim = Image.composite(Image.new("RGB", pim.size, (20, 20, 30)), pim, shadow)
    d = ImageDraw.Draw(pim)
    for x, y, r in drones:
        for k in (-1, 0, 1):  # legs
            for sgn in (-1, 1):
                d.line([x, y + k * r * 0.4, x + sgn * r * 1.5, y + k * r * 0.7], fill=(30, 18, 40), width=3)
        d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8], fill=(38, 22, 52))
        d.ellipse([x - r * 0.35, y - r * 0.5, x + r * 0.1, y - r * 0.2], fill=(70, 150, 90))
    p = os.path.join(FLOOR, f"preview_{name}.png"); pim.save(p); small_jpg(p)
    small_jpg(os.path.join(FLOOR, name + ".png"))


if __name__ == "__main__":
    os.makedirs(SMALL, exist_ok=True)
    for nm in [x for x in sys.argv[1:] if not x.startswith("--")]:
        process(nm)
