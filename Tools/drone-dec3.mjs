// Decimate the Meshy Drone for a phone swarm. Its UV unwrap is in tiny islands, so a decimated mesh can't keep
// the texture. Instead: simplify on welded positions, then give every triangle its own texel in a small
// color atlas (the average of the original texture at its three corners). Low-poly, correct colors.
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
await MeshoptSimplifier.ready;
const [src, out, target] = [process.argv[2], process.argv[3], +process.argv[4]];
const doc = await io.read(src);
const root = doc.getRoot(), buf = root.listBuffers()[0];
const prim = root.listMeshes()[0].listPrimitives()[0];
const mat = prim.getMaterial();
const pos = prim.getAttribute('POSITION').getArray(), nrm = prim.getAttribute('NORMAL').getArray(), uv = prim.getAttribute('TEXCOORD_0').getArray();
const idx = prim.getIndices().getArray(), n = pos.length / 3;
const img = mat.getBaseColorTexture().getImage();
const { data: px, info } = await sharp(Buffer.from(img)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
const sample = (u, v) => { const x = Math.min(info.width - 1, Math.max(0, Math.floor((u % 1 + 1) % 1 * info.width))), y = Math.min(info.height - 1, Math.max(0, Math.floor((v % 1 + 1) % 1 * info.height))); const o = (y * info.width + x) * 4; return [px[o], px[o+1], px[o+2]]; };
const key = new Map(), weldOf = new Uint32Array(n), rep = [];
for (let i = 0; i < n; i++) { const k = `${pos[3*i].toFixed(5)},${pos[3*i+1].toFixed(5)},${pos[3*i+2].toFixed(5)}`; let w = key.get(k); if (w === undefined) { w = rep.length; key.set(k, w); rep.push(i); } weldOf[i] = w; }
const W = rep.length, wcol = new Float32Array(W * 3), wcnt = new Float32Array(W), wn = new Float32Array(W * 3);
for (let i = 0; i < n; i++) { const w = weldOf[i]; wn[3*w] += nrm[3*i]; wn[3*w+1] += nrm[3*i+1]; wn[3*w+2] += nrm[3*i+2]; }
for (let t = 0; t < idx.length; t += 3) {
  const [a, b, c] = [idx[t], idx[t+1], idx[t+2]];
  const col = sample((uv[2*a] + uv[2*b] + uv[2*c]) / 3, (uv[2*a+1] + uv[2*b+1] + uv[2*c+1]) / 3);
  for (const v of [a, b, c]) { const w = weldOf[v]; wcol[3*w] += col[0]; wcol[3*w+1] += col[1]; wcol[3*w+2] += col[2]; wcnt[w]++; }
}
const wpos = new Float32Array(W * 3); rep.forEach((v, w) => wpos.set(pos.subarray(3*v, 3*v+3), 3*w));
const widx = new Uint32Array(idx.length); for (let i = 0; i < idx.length; i++) widx[i] = weldOf[idx[i]];
const [sidx, err] = MeshoptSimplifier.simplify(widx, wpos, 3, target * 3, 1.0, []);
const T = sidx.length / 3, S = Math.ceil(Math.sqrt(T));
console.log('tris', T, 'error', err.toFixed(4), 'atlas', S);
const P2 = new Float32Array(T * 9), N2 = new Float32Array(T * 9), U2 = new Float32Array(T * 6), atlas = Buffer.alloc(S * S * 3);
for (let t = 0; t < T; t++) {
  let c = [0, 0, 0];
  for (let k = 0; k < 3; k++) { const w = sidx[3*t+k]; P2.set(wpos.subarray(3*w, 3*w+3), 9*t+3*k);
    const nl = Math.hypot(wn[3*w], wn[3*w+1], wn[3*w+2]) || 1; N2.set([wn[3*w]/nl, wn[3*w+1]/nl, wn[3*w+2]/nl], 9*t+3*k);
    for (let j = 0; j < 3; j++) c[j] += wcol[3*w+j] / wcnt[w] / 3; }
  const cx = t % S, cy = Math.floor(t / S);
  atlas.set(c.map(Math.round), (cy * S + cx) * 3);
  for (let k = 0; k < 3; k++) U2.set([(cx + 0.5) / S, (cy + 0.5) / S], 6*t+2*k);
}
const png = await sharp(atlas, { raw: { width: S, height: S, channels: 3 } }).png().toBuffer();
for (const sem of prim.listSemantics()) prim.setAttribute(sem, null);
prim.setAttribute('POSITION', doc.createAccessor().setType('VEC3').setArray(P2).setBuffer(buf));
prim.setAttribute('NORMAL', doc.createAccessor().setType('VEC3').setArray(N2).setBuffer(buf));
prim.setAttribute('TEXCOORD_0', doc.createAccessor().setType('VEC2').setArray(U2).setBuffer(buf));
prim.setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint16Array(T * 3).map((_, i) => i)).setBuffer(buf));
const tex = doc.createTexture('DroneColors').setImage(new Uint8Array(png)).setMimeType('image/png');
mat.setBaseColorTexture(tex).setNormalTexture(null).setMetallicRoughnessTexture(null).setMetallicFactor(0.15).setRoughnessFactor(0.55);
mat.getBaseColorTextureInfo().setMagFilter(9728).setMinFilter(9728);
await doc.transform(prune());
await io.write(out, doc);
