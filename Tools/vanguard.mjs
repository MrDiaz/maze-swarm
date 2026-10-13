// Iron Vanguard (Meshy texture export: one static ~1.8M-tri mesh, no rig yet) -> a phone preview model.
//   node vanguard.mjs <meshy.glb> <out.glb> [target tris=30000] [texture size=2048] [material name=VanguardMat]
// (also used for the Heavy's minigun: ... minigun.glb 18000 2048 MinigunMat)
// Decimated with meshoptimizer to about Sentinel density; the real maps are written NEXT to the glb as
// VanguardMat_base.png / _normal.png / _ms.png (Unity imports them at 2048, ASTC 4x4 on iOS; Mats.ConvertGltf picks
// them up), the glb only keeps 64 px placeholders. Swap for the rigged biped (sentinel.mjs pipeline) when it arrives.
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, weld, simplify, dedup } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
import fs from 'fs';
import path from 'path';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
await MeshoptSimplifier.ready;
const [src, out] = process.argv.slice(2), TARGET = +(process.argv[4] || 30000), TEX = +(process.argv[5] || 2048);
const doc = await io.read(src), root = doc.getRoot();
const tris = () => root.listMeshes().reduce((s, m) => s + m.listPrimitives().reduce((a, p) => a + (p.getIndices() ? p.getIndices().getCount() : p.getAttribute('POSITION').getCount()) / 3, 0), 0);
const t0 = tris();
await doc.transform(weld(), simplify({ simplifier: MeshoptSimplifier, ratio: TARGET / t0, error: 0.01 }));
console.log('tris', t0, '->', tris());
const name = process.argv[6] || 'VanguardMat', dirOut = path.dirname(out);
for (const mat of root.listMaterials()) {
  const full = async (tex, file) => { if (!tex) return;
    fs.writeFileSync(path.join(dirOut, file), await sharp(Buffer.from(tex.getImage())).resize(TEX, TEX, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toBuffer()); };
  await full(mat.getBaseColorTexture(), name + '_base.png');
  await full(mat.getNormalTexture(), name + '_normal.png');
  const mr = mat.getMetallicRoughnessTexture();
  if (mr) {
    const { data, info } = await sharp(Buffer.from(mr.getImage())).resize(TEX, TEX, { kernel: 'lanczos3' }).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    const ms = Buffer.alloc(info.width * info.height * 4), mf = mat.getMetallicFactor(), rf = mat.getRoughnessFactor();
    for (let i = 0; i < info.width * info.height; i++) { const m = Math.round(data[4*i+2] * mf); ms[4*i] = ms[4*i+1] = ms[4*i+2] = m; ms[4*i+3] = Math.round(255 - data[4*i+1] * rf); }
    await sharp(ms, { raw: { width: info.width, height: info.height, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, name + '_ms.png'));
  }
  for (const t of [mat.getBaseColorTexture(), mat.getNormalTexture(), mr]) if (t) t.setImage(new Uint8Array(await sharp(Buffer.from(t.getImage())).resize(64, 64).png().toBuffer())).setMimeType('image/png');
  mat.setName(name);
}
await doc.transform(dedup(), prune());
await io.write(out, doc);
const p = root.listMeshes()[0].listPrimitives()[0].getAttribute('POSITION'); console.log('bounds', p.getMin([]).map(x => x.toFixed(2)), p.getMax([]).map(x => x.toFixed(2)));
console.log('wrote', out, (fs.statSync(out).size / 1e6).toFixed(1), 'MB');
