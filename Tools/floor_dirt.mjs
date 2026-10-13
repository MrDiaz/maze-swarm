// Escape floor grime: a 2x2 atlas of soft dirt blotches (white RGB, the shape in alpha; tinted in the game),
// fractal value noise inside a ragged falloff, each 512 px.   node floor_dirt.mjs <out.png>
import sharp from 'sharp';
const out = process.argv[2], S = 512, N = 2 * S;
let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
function noise(f) { const g = Array.from({ length: (f + 1) * (f + 1) }, rnd);
  return (x, y) => { const fx = x * f, fy = y * f, ix = Math.floor(fx), iy = Math.floor(fy), tx = fx - ix, ty = fy - iy;
    const s = t => t * t * (3 - 2 * t), a = g[iy * (f + 1) + ix], b = g[iy * (f + 1) + ix + 1], c = g[(iy + 1) * (f + 1) + ix], d = g[(iy + 1) * (f + 1) + ix + 1];
    return (a + (b - a) * s(tx)) * (1 - s(ty)) + (c + (d - c) * s(tx)) * s(ty); }; }
const px = Buffer.alloc(N * N * 4, 255);
for (let q = 0; q < 4; q++) {
  const oct = [noise(4), noise(9), noise(21), noise(47)], warp = noise(3), ox = (q % 2) * S, oy = Math.floor(q / 2) * S;
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const u = x / S, v = y / S, dx = u - 0.5, dy = v - 0.5;
    const n = oct[0](u, v) * 0.5 + oct[1](u, v) * 0.27 + oct[2](u, v) * 0.15 + oct[3](u, v) * 0.08;
    const r = Math.sqrt(dx * dx + dy * dy) * 2 + (warp(u, v) - 0.5) * 0.55;          // ragged edge
    const fall = Math.max(0, Math.min(1, (1 - r) / 0.45));
    const a = Math.max(0, Math.min(1, (n - 0.38) * 2.2)) * fall * fall;
    px[((oy + y) * N + ox + x) * 4 + 3] = Math.round(a * 255);
  }
}
await sharp(px, { raw: { width: N, height: N, channels: 4 } }).png({ compressionLevel: 9 }).toFile(out);
console.log('wrote', out);
