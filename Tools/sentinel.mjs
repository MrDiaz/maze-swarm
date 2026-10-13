// Arctic Sentinel (Meshy biped, 4 single-clip GLBs) -> one hero.glb: the Run_and_Shoot file as the base, the other
// clips copied onto its Mixamo nodes by name, 8 weights per vertex folded to the top 4, textures resized for the phone.
//   node sentinel.mjs <unzipped folder> <out.glb> [base color size=2048] [normal/MR size=2048]
// The full-resolution maps are written NEXT to the glb as <material>_base.png (lossless), _normal.png and _ms.png
// (URP metallic/smoothness); Unity imports those (2048, ASTC 4x4 on iOS) and Mats.ConvertGltf uses them. The glb
// itself only carries 64 px placeholders, so the textures aren't loaded twice.
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import sharp from 'sharp';
import fs from 'fs';
import path from 'path';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [dir, out] = process.argv.slice(2), BASE = +(process.argv[4] || 2048), OTHER = +(process.argv[5] || 2048);
const files = fs.readdirSync(dir).filter(f => /_Animation_(.+)_withSkin\.glb$/.test(f)).sort();
const clipOf = f => f.match(/_Animation_(.+)_withSkin\.glb$/)[1];
const baseFile = files.find(f => clipOf(f) === 'Run_and_Shoot') || files[0];
const doc = await io.read(path.join(dir, baseFile));
const root = doc.getRoot(), buf = root.listBuffers()[0];
const byName = new Map(root.listNodes().map(n => [n.getName(), n]));
for (const f of files) {
  if (f === baseFile) continue;
  const src = await io.read(path.join(dir, f));
  for (const anim of src.getRoot().listAnimations()) {
    const outA = doc.createAnimation(clipOf(f)), sm = new Map();
    for (const ch of anim.listChannels()) {
      const tgt = byName.get(ch.getTargetNode().getName());
      if (!tgt) { console.log('missing node', ch.getTargetNode().getName()); continue; }
      const s = ch.getSampler(); let ns = sm.get(s);
      if (!ns) { const cp = a => doc.createAccessor().setType(a.getType()).setArray(a.getArray().slice()).setBuffer(buf);
        ns = doc.createAnimationSampler().setInput(cp(s.getInput())).setOutput(cp(s.getOutput())).setInterpolation(s.getInterpolation());
        outA.addSampler(ns); sm.set(s, ns); }
      outA.addChannel(doc.createAnimationChannel().setTargetNode(tgt).setTargetPath(ch.getTargetPath()).setSampler(ns));
    }
  }
}
for (const mesh of root.listMeshes()) for (const p of mesh.listPrimitives()) {
  const j0 = p.getAttribute('JOINTS_0'), w0 = p.getAttribute('WEIGHTS_0'), j1 = p.getAttribute('JOINTS_1'), w1 = p.getAttribute('WEIGHTS_1');
  if (!j1) continue;
  const n = j0.getCount(), J = new Uint16Array(n * 4), W = new Float32Array(n * 4), a = [], b = [], c = [], d = [];
  for (let i = 0; i < n; i++) {
    j0.getElement(i, a); w0.getElement(i, b); j1.getElement(i, c); w1.getElement(i, d);
    const all = [0, 1, 2, 3].map(k => [a[k], b[k]]).concat([0, 1, 2, 3].map(k => [c[k], d[k]])).sort((x, y) => y[1] - x[1]).slice(0, 4);
    const sum = all.reduce((s, x) => s + x[1], 0) || 1;
    all.forEach(([jj, ww], k) => { J[4*i+k] = jj; W[4*i+k] = ww / sum; });
  }
  p.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(J).setBuffer(buf));
  p.setAttribute('WEIGHTS_0', doc.createAccessor().setType('VEC4').setArray(W).setBuffer(buf));
  p.setAttribute('JOINTS_1', null); p.setAttribute('WEIGHTS_1', null);
}
for (const mat of root.listMaterials()) {
  const dirOut = path.dirname(out), name = 'SentinelMat';
  const full = async (tex, size, file) => { if (!tex) return null;
    const buf = await sharp(Buffer.from(tex.getImage())).resize(size, size, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toBuffer();
    if (file) fs.writeFileSync(path.join(dirOut, file), buf); return buf; };
  await full(mat.getBaseColorTexture(), BASE, name + '_base.png');
  await full(mat.getNormalTexture(), OTHER, name + '_normal.png');
  // URP layout of metallic-roughness (R metal, A smoothness), from the 4096 original down to OTHER
  const mr = mat.getMetallicRoughnessTexture();
  if (mr) {
    const { data, info } = await sharp(Buffer.from(mr.getImage())).resize(OTHER, OTHER, { kernel: 'lanczos3' }).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    const ms = Buffer.alloc(info.width * info.height * 4), mf = mat.getMetallicFactor(), rf = mat.getRoughnessFactor();
    for (let i = 0; i < info.width * info.height; i++) { const m = Math.round(data[4*i+2] * mf); ms[4*i] = ms[4*i+1] = ms[4*i+2] = m; ms[4*i+3] = Math.round(255 - data[4*i+1] * rf); }
    await sharp(ms, { raw: { width: info.width, height: info.height, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, name + '_ms.png'));
  }
  // tiny placeholders inside the glb
  for (const t of [mat.getBaseColorTexture(), mat.getNormalTexture(), mr]) if (t) t.setImage(new Uint8Array(await sharp(Buffer.from(t.getImage())).resize(64, 64).png().toBuffer())).setMimeType('image/png');
  mat.setName(name);
}
await doc.transform(prune());
for (const a of root.listAnimations()) { let d = 0; for (const s of a.listSamplers()) { const t = s.getInput().getArray(); d = Math.max(d, t[t.length - 1]); } console.log('clip', a.getName(), d.toFixed(2) + 's'); }
console.log('joints', root.listSkins()[0].listJoints().length, 'tris', root.listMeshes()[0].listPrimitives()[0].getIndices().getCount() / 3);
await io.write(out, doc);
console.log('wrote', out, (fs.statSync(out).size / 1e6).toFixed(1) + ' MB');
