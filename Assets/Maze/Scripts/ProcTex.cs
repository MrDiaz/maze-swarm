using UnityEngine;

namespace Maze
{
    // Procedural surface textures, painted into pixel buffers at startup (ported from the
    // web build's canvas painters). Buffers use image coordinates: (0,0) = top-left.
    public class PixBuf
    {
        public readonly int w, h;
        public readonly Color[] px;
        public PixBuf(int w, int h, Color fill) { this.w = w; this.h = h; px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = fill; }

        public void Blend(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || a <= 0f) return;
            int i = y * w + x;
            px[i] = Color.Lerp(px[i], c, Mathf.Clamp01(a));
        }
        public void Rect(float x, float y, float rw, float rh, Color c, float a)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(x)), y0 = Mathf.Max(0, Mathf.FloorToInt(y));
            int x1 = Mathf.Min(w, Mathf.CeilToInt(x + rw)), y1 = Mathf.Min(h, Mathf.CeilToInt(y + rh));
            for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) Blend(xx, yy, c, a);
        }
        public void Blotch(float cx, float cy, float r, Color c, float a)
        {
            int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r), y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                    if (d < 1f) Blend(x, y, c, a * (1f - d));
                }
        }
        public void Grain(float amount, System.Random rng)
        {
            for (int i = 0; i < px.Length; i++)
            {
                float n = ((float)rng.NextDouble() - 0.5f) * amount;
                var c = px[i]; px[i] = new Color(c.r + n, c.g + n, c.b + n, 1f);
            }
        }
        // dirt that collects at the foot of a wall (image bottom = floor level)
        public void Grime(float pxHeight, float a)
        {
            var dirt = new Color(0.28f, 0.27f, 0.25f);
            for (int y = 0; y < pxHeight; y++) Rect(0, h - 1 - y, w, 1, dirt, a * (1f - y / pxHeight));
        }
        public void Streaks(int n, float a, System.Random rng)
        {
            var c = new Color(0.35f, 0.37f, 0.42f);
            for (int i = 0; i < n; i++)
            {
                float x = (float)rng.NextDouble() * w, y = (float)rng.NextDouble() * h * 0.5f, len = h * (0.15f + (float)rng.NextDouble() * 0.5f);
                float aa = a * (float)rng.NextDouble();
                for (int k = 0; k < len; k++) Blend((int)x, (int)(y + k), c, aa * (1f - k / len));
            }
        }
        public Texture2D ToTexture(bool repeat = true, bool mips = true, bool readable = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips, false);
            var flipped = new Color[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            t.SetPixels(flipped);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(mips, !readable); // non-readable frees the CPU copy (the escape floor reads it once for its grime)
            return t;
        }
    }

    public static class ProcTex
    {
        static readonly System.Random rng = new System.Random(7);
        static float R => (float)rng.NextDouble();
        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        static readonly Color White = Color.white, Ink = new Color(0.27f, 0.31f, 0.39f);

        // corrugated metal siding (tile = 4 x 4 units)
        public static Texture2D Siding()
        {
            int S = 256; float u = S / 4f;
            var b = new PixBuf(S, S, Hex(0xeef0f3));
            for (float x = 0; x < S; x += u / 4) { b.Rect(x, 0, 2, S, White, 0.55f); b.Rect(x + 2, 0, 2, S, Ink, 0.1f); }
            b.Rect(0, 0, 2, S, Ink, 0.22f); b.Rect(S / 2f, 0, 2, S, Ink, 0.22f);
            b.Rect(0, S - u * 2.2f, S, 1, Ink, 0.1f);
            b.Streaks(30, 0.08f, rng);
            for (int i = 0; i < 6; i++) b.Blotch(R * S, R * S, 10 + R * 30, Hex(0x6e737d), 0.05f);
            b.Grime(u * 0.9f, 0.28f);
            b.Grain(0.04f, rng);
            return b.ToTexture();
        }

        // roof membrane with welded seams (tile = 4 x 4 units)
        public static Texture2D Roof()
        {
            int S = 256; float u = S / 4f;
            var b = new PixBuf(S, S, Hex(0xe6e9ee));
            for (int i = 0; i < 16; i++) b.Blotch(R * S, R * S, 10 + R * 40, Hex(0x787d87), 0.08f);
            for (float y = 0; y < S; y += u) { b.Rect(0, y, S, 2, White, 0.6f); b.Rect(0, y + 2, S, 1, Ink, 0.14f); }
            for (int i = 0; i < 2; i++) b.Blotch(30 + R * (S - 60), 30 + R * (S - 60), 5, Hex(0x8a8f99), 0.9f);
            b.Grain(0.05f, rng);
            return b.ToTexture();
        }

        // tilt-up concrete panels for the outer walls (tile = 4 x 4 units)
        public static Texture2D Concrete()
        {
            int S = 256; float u = S / 4f;
            var b = new PixBuf(S, S, Hex(0xe4e6ea));
            for (int i = 0; i < 20; i++) b.Blotch(R * S, R * S, 8 + R * 40, Hex(0x787c84), 0.07f);
            b.Rect(0, 0, 2, S, Hex(0x373c4b), 0.3f); b.Rect(2, 0, 1, S, White, 0.5f);
            for (float x = u * 0.5f; x < S; x += u) for (float y = u * 0.6f; y < S; y += u * 0.9f) b.Blotch(x, y, 2.5f, Hex(0x464b5a), 0.6f);
            b.Streaks(20, 0.1f, rng);
            b.Grime(u * 1.1f, 0.3f);
            b.Grain(0.06f, rng);
            return b.ToTexture();
        }

        // plywood shipping crate (tile = 2 wide x 1.4 tall)
        public static Texture2D Crate()
        {
            int S = 128;
            var b = new PixBuf(S, S, Hex(0xd9c7a0));
            for (int i = 0; i < 60; i++) b.Rect(0, R * S, S, 1 + R, Hex(0x8a6432), R * 0.12f);
            var frame = Hex(0x6e5028);
            b.Rect(0, 0, S, 8, frame, 0.55f); b.Rect(0, S - 8, S, 8, frame, 0.55f);
            b.Rect(0, 0, 8, S, frame, 0.55f); b.Rect(S - 8, 0, 8, S, frame, 0.55f);
            for (int k = 8; k < S - 8; k++) b.Rect(k - 3, S - k - 3, 7, 7, frame, 0.25f); // diagonal brace
            b.Grime(20, 0.25f);
            b.Grain(0.05f, rng);
            return b.ToTexture();
        }

        // painted steel kick plate (tile = 2 x 1 units; trim covers the bottom 0.3)
        public static Texture2D Trim()
        {
            int S = 128;
            var b = new PixBuf(S, S, Hex(0x2346b8));
            for (int i = 0; i < 80; i++)
            {
                float x = R * S, y = S * 0.7f + R * S * 0.3f, l = 2 + R * 9;
                b.Rect(x, y, l, 1, Hex(0xc8d2eb), R * 0.35f);
            }
            for (int i = 0; i < 12; i++) b.Rect(R * S, S * 0.72f + R * S * 0.28f, 1 + R * 2, 1 + R, Hex(0xbec3cd), 0.6f);
            b.Rect(0, S * 0.7f - 1, S, 2, White, 0.35f);
            b.Grime(15, 0.35f);
            b.Grain(0.04f, rng);
            return b.ToTexture();
        }

        // blue roll-up dock door (face UVs)
        public static Texture2D Door()
        {
            int S = 128;
            var b = new PixBuf(S, S, Hex(0x2a4bb5));
            for (int y = 0; y < S; y += 7) { b.Rect(0, y, S, 1, Hex(0x0a143c), 0.35f); b.Rect(0, y + 1, S, 1, White, 0.12f); }
            b.Rect(0, S - 9, S, 9, Hex(0x1b2f78), 1f);
            b.Rect(S / 2f - 9, S - 17, 18, 4, Hex(0xc9ced8), 1f);
            b.Grime(25, 0.3f);
            b.Grain(0.04f, rng);
            return b.ToTexture(false);
        }

        // louvered metal for rooftop HVAC units (tile = 1 x 1)
        public static Texture2D Hvac()
        {
            int S = 64;
            var b = new PixBuf(S, S, Hex(0xcdd2da));
            for (int y = 5; y < S - 5; y += 5) b.Rect(5, y, S - 10, 2, Hex(0x283246), 0.35f);
            b.Rect(0, 0, S, 2, Ink, 0.4f); b.Rect(0, S - 2, S, 2, Ink, 0.4f); b.Rect(0, 0, 2, S, Ink, 0.4f); b.Rect(S - 2, 0, 2, S, Ink, 0.4f);
            b.Grain(0.04f, rng);
            return b.ToTexture();
        }

        // building number plate
        public static Texture2D Sign()
        {
            int S = 64;
            var b = new PixBuf(S, S, Hex(0x1d2f80));
            b.Rect(4, 4, S - 8, 2, White, 0.85f); b.Rect(4, S - 6, S - 8, 2, White, 0.85f);
            b.Rect(4, 4, 2, S - 8, White, 0.85f); b.Rect(S - 6, 4, 2, S - 8, White, 0.85f);
            b.Rect(14, 28, S - 28, 8, White, 0.9f);
            return b.ToTexture(false);
        }

        public static Texture2D Ground()
        {
            int S = 128;
            var b = new PixBuf(S, S, Hex(0xdcdfe5));
            for (int i = 0; i < 8; i++) b.Blotch(R * S, R * S, 10 + R * 30, Hex(0x6e737d), 0.06f);
            b.Grain(0.05f, rng);
            return b.ToTexture();
        }

        public static Texture2D Scorch()
        {
            int S = 128;
            var b = new PixBuf(S, S, new Color(0.1f, 0.09f, 0.09f, 0f));
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - S / 2f) * (x - S / 2f) + (y - S / 2f) * (y - S / 2f)) / (S / 2f);
                    float ang = Mathf.Atan2(y - S / 2f, x - S / 2f);
                    float rays = 0.85f + 0.15f * Mathf.Sin(ang * 11f) * Mathf.Sin(ang * 5f + 1f);
                    b.px[y * S + x] = new Color(0.1f, 0.09f, 0.09f, Mathf.Clamp01((1f - d / rays) * 0.85f));
                }
            return b.ToTexture(false);
        }

        // goo splat: lobed puddle with flung droplets, darker rim and a wet highlight (white, tinted by the particle color)
        public static Texture2D Goo()
        {
            int S = 128;
            var rng = new System.Random(7);
            var b = new PixBuf(S, S, new Color(1, 1, 1, 0));
            var drops = new Vector3[9];
            for (int i = 0; i < drops.Length; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, d = 0.5f + (float)rng.NextDouble() * 0.38f;
                drops[i] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.04f + (float)rng.NextDouble() * 0.07f);
            }
            float p1 = (float)rng.NextDouble() * 6, p2 = (float)rng.NextDouble() * 6;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1;
                    float d = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                    float edge = 0.46f + 0.08f * Mathf.Sin(ang * 5 + p1) + 0.05f * Mathf.Sin(ang * 9 + p2) + 0.03f * Mathf.Sin(ang * 14);
                    float f = (edge - d) / edge; // > 0 inside
                    foreach (var dr in drops)
                    {
                        float dd = Mathf.Sqrt((u - dr.x) * (u - dr.x) + (v - dr.y) * (v - dr.y));
                        f = Mathf.Max(f, (dr.z - dd) / dr.z * 0.6f);
                    }
                    float a2 = Mathf.Clamp01(f * S * 0.08f);
                    float shade = 0.7f + 0.3f * Mathf.Clamp01(f * 2.5f); // darker rim
                    float hu = u + 0.14f, hv = v - 0.16f;
                    float hl = Mathf.Clamp01(1 - Mathf.Sqrt(hu * hu + hv * hv * 2.2f) / 0.13f);
                    float c = Mathf.Min(1, shade + hl * 0.9f);
                    b.px[y * S + x] = new Color(c, c, c, a2 * 0.92f);
                }
            return b.ToTexture(false);
        }

        public static Sprite Circle(int size = 128, float ring = 0f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01(r - d);
                    if (ring > 0f) a *= Mathf.Clamp01(d - (r - ring));
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // The floor: one large non-repeating texture covering the whole map, with blue zones,
        // hazard stripes, dock markings, tyre tracks, slab joints and baked contact shadows (AO).
        // cryo: the "Frosted lab" Cryo Station (pale frosted deck, frost crust, half-frosted hazard stripes; the cryo pads
        // are separate glowing meshes on top, see LevelBuilder.CryoDressing)
        public static Texture2D Floor(int ppu, bool cryo = false, bool readable = false)
        {
            int W = MazeMap.W, H = MazeMap.H;
            float C = MazeMap.C, cs = C * ppu;
            int FW = Mathf.RoundToInt(W * cs), FH = Mathf.RoundToInt(H * cs);
            var b = new PixBuf(FW, FH, cryo ? Hex(0xeef3f7) : Hex(0xe8eaee));
            for (int z = 0; z < H; z += 2)
                for (int x = 0; x < W; x += 2)
                {
                    float v = R - 0.5f;
                    b.Rect(x * cs, z * cs, cs * 2, cs * 2, v > 0 ? White : cryo ? Hex(0x9aa9bd) : Hex(0x505869), v > 0 ? v * 0.35f : -v * (cryo ? 0.1f : 0.12f));
                }
            if (cryo)
            {   // frost crust: soft white drifts and faint cool-grey wear
                for (int i = 0; i < 160; i++) b.Blotch(R * FW, R * FH, ppu * (1f + R * 6), White, 0.22f * R);
                for (int i = 0; i < 60; i++) b.Blotch(R * FW, R * FH, ppu * (1.5f + R * 6), Hex(0x8d9bb0), 0.05f * R);
            }
            else
            {
                for (int i = 0; i < 90; i++) b.Blotch(R * FW, R * FH, ppu * (1.5f + R * 8), Hex(0x5f6473), 0.07f * R);
                for (int i = 0; i < 30; i++) b.Blotch(R * FW, R * FH, ppu * (0.5f + R * 2), Hex(0x37322d), 0.13f * R);
            }
            foreach (var r in MazeMap.Rects(ch => ch == '~'))
            {
                b.Rect(r.x * cs, r.y * cs, r.width * cs, r.height * cs, cryo ? Hex(0x6fb8d6) : Hex(0x2445aa), 1f);
                for (int i = 0; i < r.width * r.height; i++) b.Blotch((r.x + R * r.width) * cs, (r.y + R * r.height) * cs, ppu * (0.5f + R * 2), Hex(0xbec8e1), 0.1f * R);
                float t = 3;
                b.Rect(r.x * cs + 2, r.y * cs + 2, r.width * cs - 4, t, Hex(0x0f1946), 0.35f);
                b.Rect(r.x * cs + 2, (r.y + r.height) * cs - 2 - t, r.width * cs - 4, t, Hex(0x0f1946), 0.35f);
                b.Rect(r.x * cs + 2, r.y * cs + 2, t, r.height * cs - 4, Hex(0x0f1946), 0.35f);
                b.Rect((r.x + r.width) * cs - 2 - t, r.y * cs + 2, t, r.height * cs - 4, Hex(0x0f1946), 0.35f);
            }
            // escape maps: the extraction pad (a cyan-green square with a light border), door cells striped like 'y'
            foreach (var r in MazeMap.Rects(ch => ch == 'E'))
            {
                b.Rect(r.x * cs, r.y * cs, r.width * cs, r.height * cs, Hex(0x3fd6a0), 1f);
                float lw = ppu * 0.25f;
                b.Rect(r.x * cs, r.y * cs, r.width * cs, lw, White, 0.8f); b.Rect(r.x * cs, (r.y + r.height) * cs - lw, r.width * cs, lw, White, 0.8f);
                b.Rect(r.x * cs, r.y * cs, lw, r.height * cs, White, 0.8f); b.Rect((r.x + r.width) * cs - lw, r.y * cs, lw, r.height * cs, White, 0.8f);
            }
            foreach (var r in MazeMap.Rects(ch => ch == 'y' || ch == 'D' || ch == 'd'))
            {
                float sw = ppu * 0.45f;
                for (int y = (int)(r.y * cs); y < (r.y + r.height) * cs; y++)
                    for (int x = (int)(r.x * cs); x < (r.x + r.width) * cs; x++)
                    {
                        var c = ((int)((x + y) / sw) % 2 == 0) ? Hex(0xe9c241) : Hex(0xe4e6ea);
                        if (cryo)
                        {   // worn and half under frost
                            float f = 0.5f + 0.25f * Mathf.Sin(x * 0.05f + Mathf.Sin(y * 0.03f) * 3) + 0.25f * Mathf.Sin(y * 0.071f + x * 0.013f);
                            c = Color.Lerp(c, Hex(0xf1f5f8), Mathf.SmoothStep(0.35f, 0.75f, f) * 0.85f + 0.1f);
                        }
                        b.px[y * FW + x] = c;
                    }
            }
            // dock squares at every enemy entrance
            foreach (var s in MazeMap.spawnPoints)
            {
                float x0 = (s.x - 1.6f) * ppu, y0 = (s.y - 1.6f) * ppu, sz = 3.2f * ppu, lw = ppu * 0.12f;
                b.Rect(x0, y0, sz, lw, White, 0.6f); b.Rect(x0, y0 + sz - lw, sz, lw, White, 0.6f);
                b.Rect(x0, y0, lw, sz, White, 0.6f); b.Rect(x0 + sz - lw, y0, lw, sz, White, 0.6f);
            }
            // forklift tyre tracks
            for (int i = 0; i < (cryo ? 0 : 20); i++)
            {
                float x = R * FW, y = R * FH, a = R * 6.28f, l = ppu * (6 + R * 14), bend = (R - 0.5f) * 0.08f;
                float alpha = 0.04f + R * 0.05f;
                for (int k = 0; k < 2; k++)
                {
                    float ox = -Mathf.Sin(a) * ppu * 0.9f * k, oy = Mathf.Cos(a) * ppu * 0.9f * k, aa = a;
                    float px = x + ox, py = y + oy;
                    for (float t = 0; t < l; t += 1f) { aa += bend * 0.05f; px += Mathf.Cos(aa); py += Mathf.Sin(aa); b.Rect(px, py, ppu * 0.2f, ppu * 0.2f, Hex(0x232328), alpha * 0.4f); }
                }
            }
            // saw-cut joints between cells, deeper joints between slabs
            for (int x = 0; x <= W; x++) { bool slab = x % 2 == 0; b.Rect(x * cs - 1, 0, slab ? 2 : 1, FH, Hex(0x464e62), slab ? 0.2f : 0.08f); if (slab) b.Rect(x * cs + 1, 0, 1, FH, White, 0.35f); }
            for (int z = 0; z <= H; z++) { bool slab = z % 2 == 0; b.Rect(0, z * cs - 1, FW, slab ? 2 : 1, Hex(0x464e62), slab ? 0.2f : 0.08f); if (slab) b.Rect(0, z * cs + 1, FW, 1, White, 0.35f); }
            b.Grain(0.03f, rng);

            // baked ambient occlusion: blur a low-res mask of the solid cells and multiply it in
            int A = 4, AW = Mathf.RoundToInt(W * C * A), AH = Mathf.RoundToInt(H * C * A);
            var mask = new float[AW * AH];
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    char ch = MazeMap.grid[z, x];
                    float v = ch == 'B' || ch == '#' ? 1f : ch == 'C' ? 0.75f : ch == 'r' ? 0.25f : 0f;
                    if (v == 0f) continue;
                    for (int j = 0; j < C * A; j++) for (int i = 0; i < C * A; i++) mask[(int)((z * C * A + j) * AW + x * C * A + i)] = v;
                }
            var tmp = new float[AW * AH];
            const int rad = 3;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < AH; y++)
                    for (int x = 0; x < AW; x++)
                    {
                        float s = 0; int n = 0;
                        for (int k = -rad; k <= rad; k++) { int xx = x + k; if (xx >= 0 && xx < AW) { s += mask[y * AW + xx]; n++; } }
                        tmp[y * AW + x] = s / n;
                    }
                for (int y = 0; y < AH; y++)
                    for (int x = 0; x < AW; x++)
                    {
                        float s = 0; int n = 0;
                        for (int k = -rad; k <= rad; k++) { int yy = y + k; if (yy >= 0 && yy < AH) { s += tmp[yy * AW + x]; n++; } }
                        mask[y * AW + x] = s / n;
                    }
            }
            float scale = (float)A / ppu;
            for (int y = 0; y < FH; y++)
                for (int x = 0; x < FW; x++)
                {
                    // bilinear sample of the mask
                    float fx = x * scale - 0.5f, fy = y * scale - 0.5f;
                    int ix = Mathf.Clamp((int)fx, 0, AW - 2), iy = Mathf.Clamp((int)fy, 0, AH - 2);
                    float tx = Mathf.Clamp01(fx - ix), ty = Mathf.Clamp01(fy - iy);
                    float m = Mathf.Lerp(Mathf.Lerp(mask[iy * AW + ix], mask[iy * AW + ix + 1], tx), Mathf.Lerp(mask[(iy + 1) * AW + ix], mask[(iy + 1) * AW + ix + 1], tx), ty);
                    float k = 1f - 0.55f * m;
                    var c = b.px[y * FW + x];
                    b.px[y * FW + x] = cryo ? new Color(c.r * k * 0.94f, c.g * k * 0.97f, c.b * Mathf.Lerp(1f, k, 0.8f), 1f) : new Color(c.r * k * 0.97f, c.g * k * 0.98f, c.b * k, 1f);
                }
            return b.ToTexture(false, true, readable);
        }
    }
}
