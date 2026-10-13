// Cryo Station PBR materials from the CC0 sets in Textures/Source (see Textures/CREDITS.txt).
//   node cryo_pbr.mjs <Maze Swarm project folder>
// Writes 2K maps for Unity to Assets/Maze/Resources/Cryo/pbr/ (iOS import caps them at 1K ASTC):
//   <mat>_color.png, <mat>_normal.png (OpenGL / Y+), <mat>_ms.png (R metallic, A smoothness = 1 - roughness),
//   <mat>_emit.png where it glows
// and 1K colour / normal / roughness (+ emit) to Concepts/CryoStation/pbr/ for the web build.
// Materials:
//   floor   MetalPlates016A, used as a DETAIL layer over the painted map floor: grey x2 albedo + detail normal
//           (the set itself is navy; the floor has to stay pale so the dark aliens pop)
//   wall    Metal016, white paint, pink rust cooled to a blue-grey
//   snow    Poly Haven snow_02, lifted toward white with a cold tint
//   ice     Ice004 tinted cyan; emission = the cryo panel light strips + a soft glow in the ice's bright cracks
//   crate   Metal027 dark powder-coat + our yellow frame / hazard / label / frost bands (Concepts tile crate.png)
//   creep   Lava004 recoloured purple-black, its hot cracks become green veins (emission), tiled 2x across the
//           decal, alpha = our growth field (creep_mask.png) for the alpha-clip spread
import sharp from 'sharp';
import fs from 'fs';
import path from 'path';

const P = process.argv[2];
const SRC = path.join(P, 'Textures/Source'), OUT = path.join(P, 'Assets/Maze/Resources/Cryo/pbr'), WEB = path.join(P, 'Concepts/CryoStation/pbr');
const TILES = path.join(P, 'Concepts/CryoStation/tiles');
fs.mkdirSync(OUT, { recursive: true }); fs.mkdirSync(WEB, { recursive: true });
const N = 2048;

async function load(file, channels = 3, size = N) {
  const { data } = await sharp(file).resize(size, size).removeAlpha().toColourspace(channels === 1 ? 'b-w' : 'srgb').raw().toBuffer({ resolveWithObject: true });
  return data;
}
async function save(name, buf, ch, webName) {
  await sharp(buf, { raw: { width: N, height: N, channels: ch } }).png({ compressionLevel: 6 }).toFile(path.join(OUT, name + '.png'));
  if (webName) await sharp(buf, { raw: { width: N, height: N, channels: ch } }).resize(1024, 1024).png({ compressionLevel: 9 }).toFile(path.join(WEB, webName + '.png'));
  console.log(name);
}
const acg = (id, map) => path.join(SRC, id, `${id}_2K-JPG_${map}.jpg`);
const clamp = v => v < 0 ? 0 : v > 255 ? 255 : v;

// metal/smoothness pack (+ the plain roughness map for the web)
async function ms(name, roughFile, metalFile, metalConst, smoothScale = 1) {
  const r = await load(roughFile, 1), m = metalFile ? await load(metalFile, 1) : null;
  const out = Buffer.alloc(N * N * 4);
  for (let i = 0; i < N * N; i++) { const mv = m ? m[i] : metalConst * 255; out[4*i] = out[4*i+1] = out[4*i+2] = mv; out[4*i+3] = clamp((255 - r[i]) * smoothScale); }
  await save(name + '_ms', out, 4);
  await sharp(r, { raw: { width: N, height: N, channels: 1 } }).resize(1024, 1024).png().toFile(path.join(WEB, name + '_roughness.png'));
}
async function normal(name, file) {
  const n = await load(file, 3);
  await save(name + '_normal', n, 3, name + '_normal');
}
// recolour by a per-pixel function of (r,g,b) in 0..1
async function color(name, file, f) {
  const c = await load(file, 3), out = Buffer.alloc(N * N * 3);
  for (let i = 0; i < N * N; i++) { const o = f(c[3*i] / 255, c[3*i+1] / 255, c[3*i+2] / 255, i); out[3*i] = clamp(o[0] * 255); out[3*i+1] = clamp(o[1] * 255); out[3*i+2] = clamp(o[2] * 255); }
  await save(name + '_color', out, 3, name + '_color');
  return out;
}
const lum = (r, g, b) => 0.3 * r + 0.59 * g + 0.11 * b;
const mix = (a, b, t) => a + (b - a) * t;

// ---- floor: detail albedo (grey around 0.5, linear) + detail normal
{
  const c = await load(acg('MetalPlates016A', 'Color'), 3);
  let mean = 0; for (let i = 0; i < N * N; i++) mean += lum(c[3*i], c[3*i+1], c[3*i+2]); mean /= N * N;
  const out = Buffer.alloc(N * N * 3);
  for (let i = 0; i < N * N; i++) { const v = clamp(128 + (lum(c[3*i], c[3*i+1], c[3*i+2]) - mean) * 0.22); out[3*i] = out[3*i+1] = out[3*i+2] = v; }
  await save('floor_detail', out, 3, 'floor_detail_x2');
  await normal('floor', acg('MetalPlates016A', 'NormalGL'));
  await ms('floor', acg('MetalPlates016A', 'Roughness'), null, 0, 0.8);
}
// ---- wall: white paint, the rust cooled and faded
{
  // white painted panels: 1.5 m panels (the tile is 3 m) with recessed seams, rivets and a few vent slots
  const seam = (x, y) => {  // 0..1 groove depth at pixel x,y and its slope for the normal
    const P = N / 2, w = 7, dx = Math.min(x % P, P - x % P), dy = Math.min(y % P, P - y % P);
    const d = Math.min(dx, dy), g = d < w ? 1 - d / w : 0;
    const rx = (x % (P / 4)) - P / 8, ry = (y % P) - 26, rivet = Math.hypot(rx, ry) < 7 || Math.hypot(rx, (y % P) - (P - 26)) < 7;
    return { g, vx: dx < dy && dx < w ? Math.sign((x % P) < P / 2 ? 1 : -1) : 0, vy: dy <= dx && dy < w ? Math.sign((y % P) < P / 2 ? 1 : -1) : 0, rivet };
  };
  await color('wall', acg('Metal016', 'Color'), (r, g, b, i) => {
    const l = lum(r, g, b), sat = Math.max(r, g, b) - Math.min(r, g, b);
    let v = mix(l, 0.96, 0.55) * (1 - sat * 0.5);
    const x = i % N, y = Math.floor(i / N), q = seam(x, y);
    v *= 1 - 0.45 * q.g; if (q.rivet) v *= 0.82;
    return [v * 0.95, v * 0.98, v * 1.03];
  });
  {
    const n = await load(acg('Metal016', 'NormalGL'), 3), out = Buffer.alloc(N * N * 3);
    for (let i = 0; i < N * N; i++) {
      const x = i % N, y = Math.floor(i / N), q = seam(x, y);
      let nx = n[3*i] / 127.5 - 1, ny = n[3*i+1] / 127.5 - 1;
      nx += q.vx * q.g * 0.6; ny -= q.vy * q.g * 0.6;
      const nz = Math.sqrt(Math.max(0.05, 1 - nx * nx - ny * ny));
      const L = Math.hypot(nx, ny, nz); out[3*i] = clamp((nx / L + 1) * 127.5); out[3*i+1] = clamp((ny / L + 1) * 127.5); out[3*i+2] = clamp((nz / L + 1) * 127.5);
    }
    await save('wall_normal', out, 3, 'wall_normal');
  }
  await ms('wall', acg('Metal016', 'Roughness'), null, 0.15);
}
// ---- snow
{
  const p = f => path.join(SRC, 'snow_02', `snow_02_${f}_2k.jpg`);
  await color('snow', p('diff'), (r, g, b) => { const l = Math.max(lum(r, g, b), 0.56); const v = 0.84 + (l - 0.62) * 0.5; return [v * 0.97, v * 0.99, v * 1.04]; }); // dark debris specks lifted
  await normal('snow', p('nor_gl'));
  await ms('snow', p('rough'), null, 0, 0.6);
}
// ---- ice + emission (panel strips from the cryo_pad tile, plus a soft glow in the bright cracks)
{
  const strips = await load(path.join(TILES, 'cryo_pad_emit.png'), 3);
  const col = await color('ice', acg('Ice004', 'Color'), (r, g, b) => { const l = lum(r, g, b); return [mix(0.08, 0.6, l), mix(0.32, 0.86, l), mix(0.5, 0.97, l)]; }); // deep cyan so the panel glow reads
  const ic = await load(acg('Ice004', 'Color'), 3);
  const out = Buffer.alloc(N * N * 3);
  for (let i = 0; i < N * N; i++) {
    const crack = Math.max(0, lum(ic[3*i], ic[3*i+1], ic[3*i+2]) / 255 - 0.55) * 0.9;
    out[3*i] = clamp(strips[3*i] * 1.6 + crack * 50); out[3*i+1] = clamp(strips[3*i+1] * 1.6 + crack * 170); out[3*i+2] = clamp(strips[3*i+2] * 1.6 + crack * 200);
  }
  await save('ice_emit', out, 3, 'ice_emit');
  await normal('ice', acg('Ice004', 'NormalGL'));
  await ms('ice', acg('Ice004', 'Roughness'), null, 0, 1.0);
}
// ---- crate: Metal027 + our bands where the procedural crate face has colour
{
  const ov = await load(path.join(TILES, 'crate.png'), 3);
  await color('crate', acg('Metal027', 'Color'), (r, g, b, i) => {
    const o = [ov[3*i] / 255, ov[3*i+1] / 255, ov[3*i+2] / 255];
    const yellow = Math.max(0, Math.min(1, (o[0] - o[2]) * 3 - 0.6));          // frame, hazard
    const light = Math.max(0, Math.min(1, (Math.min(o[0], o[1], o[2]) - 0.5) * 3)); // label plate, frost
    let c = [r * 1.25 + 0.02, g * 1.25 + 0.025, b * 1.25 + 0.035];
    c = c.map((v, k) => mix(v, o[k] * (0.85 + 0.3 * lum(r, g, b)), yellow));
    return c.map((v, k) => mix(v, o[k], light));
  });
  await normal('crate', acg('Metal027', 'NormalGL'));
  await ms('crate', acg('Metal027', 'Roughness'), acg('Metal027', 'Metalness'), 0);
}
// ---- creep: lava tiled 2x2 across the decal, recoloured; alpha = growth field
{
  const H = N / 2;
  const tile2 = async (file, ch) => {
    const small = await sharp(file).resize(H, H).removeAlpha().toColourspace(ch === 1 ? 'b-w' : 'srgb').raw().toBuffer();
    const out = Buffer.alloc(N * N * ch);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) for (let k = 0; k < ch; k++) out[(y * N + x) * ch + k] = small[((y % H) * H + (x % H)) * ch + k];
    return out;
  };
  const c = await tile2(acg('Lava004', 'Color'), 3), e = await tile2(acg('Lava004', 'Emission'), 1);
  const nrm = await tile2(acg('Lava004', 'NormalGL'), 3), rough = await tile2(acg('Lava004', 'Roughness'), 1);
  const mask = await sharp(path.join(TILES, 'creep_mask.png')).resize(N, N).ensureAlpha().raw().toBuffer();
  const out = Buffer.alloc(N * N * 4), emit = Buffer.alloc(N * N * 3), msb = Buffer.alloc(N * N * 4);
  for (let i = 0; i < N * N; i++) {
    // the lava's colour sits around 0.3-0.55 and its emission around 0.2-0.6: stretch both; only the hottest
    // ~10% of the cracks become green veins
    const l = Math.max(0, Math.min(1, (lum(c[3*i], c[3*i+1], c[3*i+2]) / 255 - 0.25) / 0.35)), hot = e[i] / 255;
    const flesh = [mix(0.05, 0.36, l), mix(0.02, 0.1, l), mix(0.08, 0.44, l)];
    const t = Math.max(0, Math.min(1, (hot - 0.5) / 0.12)), vein = t * t * (3 - 2 * t);
    out[4*i] = clamp(mix(flesh[0], 0.45, vein) * 255); out[4*i+1] = clamp(mix(flesh[1], 0.95, vein) * 255); out[4*i+2] = clamp(mix(flesh[2], 0.2, vein) * 255);
    out[4*i+3] = mask[4*i+3];
    emit[3*i] = clamp(vein * 0.35 * 255); emit[3*i+1] = clamp(vein * 255); emit[3*i+2] = clamp(vein * 0.15 * 255);
    msb[4*i] = msb[4*i+1] = msb[4*i+2] = 0; msb[4*i+3] = clamp(255 - rough[i] * 0.55); // wet
  }
  await save('creep_color', out, 4, 'creep_color');
  await save('creep_emit', emit, 3, 'creep_emit');
  await save('creep_normal', nrm, 3, 'creep_normal');
  await save('creep_ms', msb, 4);
  await sharp(rough, { raw: { width: N, height: N, channels: 1 } }).resize(1024, 1024).png().toFile(path.join(WEB, 'creep_roughness.png'));
}
console.log('done');
