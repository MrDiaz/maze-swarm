// Meshy props (the escape blast door, the rigged Leaper) -> phone-ready glbs in Resources/Models:
//   node door_prep.mjs <meshy.glb> <out.glb> <target tris> <material name> [lens]
// Decimated with meshoptimizer; real maps written next to the glb as <material>_base/_normal/_ms.png (2048, ASTC in
// Unity, as for the hero), the glb keeps 64 px placeholders. With "lens", the frame's glowing status-light lens
// (the green texels) is cut into its own node "Lens" so the game drives it as an emissive red/amber/green light.
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, weld, simplify, dedup } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
import fs from 'fs';
import path from 'path';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
await MeshoptSimplifier.ready;
const [src, out, target, name, lensFlag] = process.argv.slice(2);
const doc = await io.read(src), root = doc.getRoot(), buf = root.listBuffers()[0];
const count = () => root.listMeshes().reduce((s, m) => s + m.listPrimitives().reduce((a, p) => a + p.getIndices().getCount() / 3, 0), 0);
const t0 = count();
if (+target < t0) await doc.transform(weld(), simplify({ simplifier: MeshoptSimplifier, ratio: +target / t0, error: +(process.env.SIMPLIFY_ERR || 0.004) })); // (a skinned model at full density skips this)
console.log('tris', t0, '->', count());
const mat = root.listMaterials()[0];
if (lensFlag === 'lens') {
  const { data: px, info } = await sharp(Buffer.from(mat.getBaseColorTexture().getImage())).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const green = (u, v) => { const x = Math.min(info.width - 1, Math.max(0, Math.floor(u * info.width))), y = Math.min(info.height - 1, Math.max(0, Math.floor(v * info.height)));
    const o = (y * info.width + x) * 3, r = px[o], g = px[o + 1], b = px[o + 2]; return g > 110 && g > r + 40 && g > b + 25; };
  const node = root.listNodes().find(n => n.getMesh()), prim = node.getMesh().listPrimitives()[0];
  const uv = prim.getAttribute('TEXCOORD_0').getArray(), idx = prim.getIndices().getArray();
  const keep = [], lens = [];
  for (let t = 0; t < idx.length; t += 3) {
    let u = 0, v = 0; for (let k = 0; k < 3; k++) { u += uv[2 * idx[t + k]] / 3; v += uv[2 * idx[t + k] + 1] / 3; }
    (green(u, v) ? lens : keep).push(idx[t], idx[t + 1], idx[t + 2]);
  }
  prim.setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(keep)).setBuffer(buf));
  const lp = doc.createPrimitive().setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(lens)).setBuffer(buf))
    .setMaterial(doc.createMaterial('DoorLensMat').setBaseColorFactor([1, 1, 1, 1]));
  for (const s of prim.listSemantics()) lp.setAttribute(s, prim.getAttribute(s));
  node.addChild(doc.createNode('Lens').setMesh(doc.createMesh('Lens').addPrimitive(lp)));
  console.log('lens tris', lens.length / 3);
}
const dirOut = path.dirname(out);
const full = async (tex, file) => { if (tex) fs.writeFileSync(path.join(dirOut, file), await sharp(Buffer.from(tex.getImage())).resize(2048, 2048, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toBuffer()); };
await full(mat.getBaseColorTexture(), name + '_base.png');
await full(mat.getNormalTexture(), name + '_normal.png');
await full(mat.getEmissiveTexture(), name + '_emit.png');
const mr = mat.getMetallicRoughnessTexture();
if (mr) {
  const { data, info } = await sharp(Buffer.from(mr.getImage())).resize(2048, 2048).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const ms = Buffer.alloc(info.width * info.height * 4), mf = mat.getMetallicFactor(), rf = mat.getRoughnessFactor();
  for (let i = 0; i < info.width * info.height; i++) { const m = Math.round(data[4*i+2] * mf); ms[4*i] = ms[4*i+1] = ms[4*i+2] = m; ms[4*i+3] = Math.round(255 - data[4*i+1] * rf); }
  await sharp(ms, { raw: { width: info.width, height: info.height, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, name + '_ms.png'));
}
for (const t of [mat.getBaseColorTexture(), mat.getNormalTexture(), mr, mat.getEmissiveTexture()]) if (t) t.setImage(new Uint8Array(await sharp(Buffer.from(t.getImage())).resize(64, 64).png().toBuffer())).setMimeType('image/png');
mat.setName(name);
await doc.transform(dedup(), prune());
await io.write(out, doc);
console.log('wrote', out, (fs.statSync(out).size / 1e6).toFixed(2), 'MB');
