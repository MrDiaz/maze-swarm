// Iron Vanguard, Meshy's own rig (Meshy_AI_Iron_Vanguard_All_Animations.glb: Mixamo skeleton, clips Running /
// Walking / run_fast_3_inplace) -> Resources/Models/hero_vanguard_meshy.glb. Full mesh (~31k tris) kept; the up to 12
// weights per vertex folded to the top 4 (renormalised); real maps written next to it at 2048 (VanguardMeshyMat_*),
// the glb keeps 64 px placeholders (as sentinel.mjs).   node vanguard_meshy.mjs <in.glb> <out.glb>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import sharp from 'sharp'; import fs from 'fs'; import path from 'path';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [src, out, matName] = process.argv.slice(2), SIZE = 2048, name = matName || 'VanguardMeshyMat';   // also the Scout: ... hero_scout.glb ScoutMat
const doc = await io.read(src), root = doc.getRoot(), buf = root.listBuffers()[0];
for (const mesh of root.listMeshes()) for (const p of mesh.listPrimitives()) {
  const sets = []; for (let s = 0; p.getAttribute('JOINTS_' + s); s++) sets.push([p.getAttribute('JOINTS_' + s), p.getAttribute('WEIGHTS_' + s)]);
  if (sets.length < 2) continue;
  const n = sets[0][0].getCount(), J = new Uint16Array(n * 4), W = new Float32Array(n * 4), a = [], b = [];
  for (let i = 0; i < n; i++) {
    const all = [];
    for (const [j, w] of sets) { j.getElement(i, a); w.getElement(i, b); for (let k = 0; k < 4; k++) all.push([a[k], b[k]]); }
    const top = all.sort((x, y) => y[1] - x[1]).slice(0, 4), sum = top.reduce((s, x) => s + x[1], 0) || 1;
    top.forEach(([jj, ww], k) => { J[4*i+k] = jj; W[4*i+k] = ww / sum; });
  }
  p.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(J).setBuffer(buf));
  p.setAttribute('WEIGHTS_0', doc.createAccessor().setType('VEC4').setArray(W).setBuffer(buf));
  for (let s = 1; s < sets.length; s++) { p.setAttribute('JOINTS_' + s, null); p.setAttribute('WEIGHTS_' + s, null); }
}
for (const mat of root.listMaterials()) {
  const dirOut = path.dirname(out);
  const full = async (tex, file) => { if (tex) fs.writeFileSync(path.join(dirOut, file), await sharp(Buffer.from(tex.getImage())).resize(SIZE, SIZE, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toBuffer()); };
  await full(mat.getBaseColorTexture(), name + '_base.png');
  await full(mat.getNormalTexture(), name + '_normal.png');
  const mr = mat.getMetallicRoughnessTexture();
  if (mr) {
    const { data, info } = await sharp(Buffer.from(mr.getImage())).resize(SIZE, SIZE, { kernel: 'lanczos3' }).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    const ms = Buffer.alloc(info.width * info.height * 4), mf = mat.getMetallicFactor(), rf = mat.getRoughnessFactor();
    for (let i = 0; i < info.width * info.height; i++) { const m = Math.round(data[4*i+2] * mf); ms[4*i] = ms[4*i+1] = ms[4*i+2] = m; ms[4*i+3] = Math.round(255 - data[4*i+1] * rf); }
    await sharp(ms, { raw: { width: info.width, height: info.height, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, name + '_ms.png'));
  }
  for (const t of [mat.getBaseColorTexture(), mat.getNormalTexture(), mr]) if (t) t.setImage(new Uint8Array(await sharp(Buffer.from(t.getImage())).resize(64, 64).png().toBuffer())).setMimeType('image/png');
  mat.setName(name);
}
await doc.transform(prune());
await io.write(out, doc);
console.log('wrote', out, (fs.statSync(out).size / 1e6).toFixed(1), 'MB; clips', root.listAnimations().map(a => a.getName()).join(', '));
