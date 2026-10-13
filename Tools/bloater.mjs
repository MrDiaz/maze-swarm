// Bloater: Meshy GLB -> phone-ready rigged enemy, in one pass.
//   node bloater.mjs <meshy.glb> <out.glb> [tris=6000]
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
await MeshoptSimplifier.ready;
const [src, out] = process.argv.slice(2), target = +(process.argv[4] || 6000);
const doc = await io.read(src);
const root = doc.getRoot(), buf = root.listBuffers()[0];
const mesh = root.listMeshes()[0], prim = mesh.listPrimitives()[0], mat = prim.getMaterial();
const pos = prim.getAttribute('POSITION').getArray(), nrm = prim.getAttribute('NORMAL').getArray(), uv = prim.getAttribute('TEXCOORD_0').getArray();
const idx = prim.getIndices().getArray(), n = pos.length / 3;
const img = mat.getBaseColorTexture().getImage();
const { data: px, info } = await sharp(Buffer.from(img)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
const sample = (u, v) => { const x = Math.min(info.width - 1, Math.max(0, Math.floor((u % 1 + 1) % 1 * info.width))), y = Math.min(info.height - 1, Math.max(0, Math.floor((v % 1 + 1) % 1 * info.height))); const o = (y * info.width + x) * 4; return [px[o], px[o+1], px[o+2]]; };
const key = new Map(), weldOf = new Uint32Array(n), rep = [];
for (let i = 0; i < n; i++) { const k = `${pos[3*i].toFixed(5)},${pos[3*i+1].toFixed(5)},${pos[3*i+2].toFixed(5)}`; let w = key.get(k); if (w === undefined) { w = rep.length; key.set(k, w); rep.push(i); } weldOf[i] = w; }
const NW = rep.length, wcol = new Float32Array(NW * 3), wcnt = new Float32Array(NW), wn = new Float32Array(NW * 3);
for (let i = 0; i < n; i++) { const w = weldOf[i]; for (let j = 0; j < 3; j++) wn[3*w+j] += nrm[3*i+j]; }
for (let t = 0; t < idx.length; t += 3) {
  const [a, b, c] = [idx[t], idx[t+1], idx[t+2]];
  const col = sample((uv[2*a] + uv[2*b] + uv[2*c]) / 3, (uv[2*a+1] + uv[2*b+1] + uv[2*c+1]) / 3);
  for (const v of [a, b, c]) { const w = weldOf[v]; for (let j = 0; j < 3; j++) wcol[3*w+j] += col[j]; wcnt[w]++; }
}
const wpos = new Float32Array(NW * 3); rep.forEach((v, w) => wpos.set(pos.subarray(3*v, 3*v+3), 3*w));
const widx = new Uint32Array(idx.length); for (let i = 0; i < idx.length; i++) widx[i] = weldOf[idx[i]];
const [sidx, err] = MeshoptSimplifier.simplify(widx, wpos, 3, target * 3, 1.0, []);
const T = sidx.length / 3, S = Math.ceil(Math.sqrt(T));
const P = new Float32Array(T * 9), N2 = new Float32Array(T * 9), U2 = new Float32Array(T * 6), atlas = Buffer.alloc(S * S * 3), triCol = [];
for (let t = 0; t < T; t++) {
  const c = [0, 0, 0];
  for (let k = 0; k < 3; k++) { const w = sidx[3*t+k]; P.set(wpos.subarray(3*w, 3*w+3), 9*t+3*k);
    const nl = Math.hypot(wn[3*w], wn[3*w+1], wn[3*w+2]) || 1; N2.set([wn[3*w]/nl, wn[3*w+1]/nl, wn[3*w+2]/nl], 9*t+3*k);
    for (let j = 0; j < 3; j++) c[j] += wcol[3*w+j] / wcnt[w] / 3; }
  triCol.push(c);
  const cx = t % S, cy = Math.floor(t / S);
  atlas.set(c.map(Math.round), (cy * S + cx) * 3);
  for (let k = 0; k < 3; k++) U2.set([(cx + 0.5) / S, (cy + 0.5) / S], 6*t+2*k);
}
console.log('tris', T, 'error', err.toFixed(4), 'atlas', S);
const png = await sharp(atlas, { raw: { width: S, height: S, channels: 3 } }).png().toBuffer();
for (const sem of prim.listSemantics()) prim.setAttribute(sem, null);
const acc = (type, arr) => doc.createAccessor().setType(type).setArray(arr).setBuffer(buf);
prim.setAttribute('POSITION', acc('VEC3', P)).setAttribute('NORMAL', acc('VEC3', N2)).setAttribute('TEXCOORD_0', acc('VEC2', U2));
prim.setIndices(acc('SCALAR', new Uint16Array(T * 3).map((_, i) => i)));
mat.setName('BloaterMat').setBaseColorTexture(doc.createTexture('BloaterColors').setImage(new Uint8Array(png)).setMimeType('image/png'))
  .setNormalTexture(null).setMetallicRoughnessTexture(null).setOcclusionTexture(null).setEmissiveTexture(null).setMetallicFactor(0.1).setRoughnessFactor(0.4);
mat.getBaseColorTextureInfo().setMagFilter(9728).setMinFilter(9728);
const FEET = [[-0.68, 0.33], [0.32, 0.64], [0.69, -0.22], [-0.38, -0.61]];
const NAMES = ['LF', 'RF', 'RR', 'LR'], GROUP = [0, 1, 0, 1];
const FOOT_Y = -0.8, BODY = [0, -0.15, 0], HEAD = [-0.05, -0.42, 0.62], BELLY = [0, 0.05, -0.05];
const legs = FEET.map(([fx, fz], i) => {
  const hip = [fx * 0.5, -0.42, fz * 0.5], foot = [fx, FOOT_Y + 0.05, fz];
  const knee = [hip[0] + (foot[0] - hip[0]) * 0.6, -0.55, hip[2] + (foot[2] - hip[2]) * 0.6];
  return { name: NAMES[i], hip, knee, foot, group: GROUP[i] };
});
const sub = (a, b) => [a[0]-b[0], a[1]-b[1], a[2]-b[2]], dot = (a, b) => a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
function segDist(p, a, b) { const ab = sub(b, a), t = Math.max(0, Math.min(1, dot(sub(p, a), ab) / dot(ab, ab))); return [Math.hypot(...sub(p, [a[0]+ab[0]*t, a[1]+ab[1]*t, a[2]+ab[2]*t])), t]; }
const arm = doc.createNode('BloaterRig');
const body = doc.createNode('Body').setTranslation(BODY); arm.addChild(body);
const belly = doc.createNode('Belly').setTranslation(sub(BELLY, BODY)); body.addChild(belly);
const head = doc.createNode('Head').setTranslation(sub(HEAD, BODY)); body.addChild(head);
const joints = [body, belly, head], worldOf = [BODY, BELLY, HEAD];
for (const L of legs) {
  L.hipN = doc.createNode('Hip_' + L.name).setTranslation(sub(L.hip, BODY)); body.addChild(L.hipN);
  L.kneeN = doc.createNode('Knee_' + L.name).setTranslation(sub(L.knee, L.hip)); L.hipN.addChild(L.kneeN);
  joints.push(L.hipN, L.kneeN); worldOf.push(L.hip, L.knee);
}
const ibm = new Float32Array(16 * joints.length);
worldOf.forEach((w, j) => ibm.set([1,0,0,0, 0,1,0,0, 0,0,1,0, -w[0],-w[1],-w[2],1], 16 * j));
const skin = doc.createSkin('BloaterSkin').setSkeleton(body).setInverseBindMatrices(acc('MAT4', ibm));
joints.forEach(j => skin.addJoint(j));
const NV = T * 3, J = new Uint16Array(NV * 4), W = new Float32Array(NV * 4);
let nLeg = 0, nSac = 0, nHead = 0;
for (let v = 0; v < NV; v++) {
  const p = [P[3*v], P[3*v+1], P[3*v+2]], [r, g, b] = triCol[Math.floor(v / 3)];
  const green = Math.max(0, Math.min(1, (g - Math.max(r, b) - 25) / 60));
  const inHead = p[2] > 0.55 && p[1] < -0.2 && Math.abs(p[0]) < 0.4;
  let best = null, bd = 1e9;
  legs.forEach((L, li) => { const [d1, t1] = segDist(p, L.hip, L.knee), [d2, t2] = segDist(p, L.knee, L.foot);
    const d = Math.min(d1, d2), t = d1 < d2 ? t1 * 0.5 : 0.5 + t2 * 0.5; if (d < bd) { bd = d; best = { li, t }; } });
  const isLeg = !inHead && (p[1] < -0.62 || (p[1] < -0.4 && bd < 0.2 && Math.hypot(p[0], p[2]) > 0.42));
  if (isLeg) {
    nLeg++;
    const hipJ = 3 + 2 * best.li, kneeJ = hipJ + 1, t = best.t;
    if (t < 0.12) { const k = t / 0.12; J.set([0, hipJ], 4*v); W.set([1 - k, k], 4*v); }
    else if (t < 0.45) { J[4*v] = hipJ; W[4*v] = 1; }
    else if (t < 0.6) { const k = (t - 0.45) / 0.15; J.set([hipJ, kneeJ], 4*v); W.set([1 - k, k], 4*v); }
    else { J[4*v] = kneeJ; W[4*v] = 1; }
  } else if (inHead) { nHead++; const k = Math.min(1, (p[2] - 0.55) / 0.15); J.set([0, 2], 4*v); W.set([1 - k, k], 4*v); }
  else if (green > 0) { nSac++; J.set([0, 1], 4*v); W.set([1 - green, green], 4*v); }
  else { J[4*v] = 0; W[4*v] = 1; }
}
console.log('verts', NV, 'leg', nLeg, 'sac', nSac, 'head', nHead);
prim.setAttribute('JOINTS_0', acc('VEC4', J)).setAttribute('WEIGHTS_0', acc('VEC4', W));
const scene = root.listScenes()[0];
for (const c of scene.listChildren()) scene.removeChild(c);
const meshNode = doc.createNode('Bloater').setMesh(mesh).setSkin(skin);
scene.addChild(arm); scene.addChild(meshNode);
for (const a of root.listAnimations()) a.dispose();
const FRAMES = 30, DUR = 1.2, SWING = 0.6, LIFT = 0.8, KNEE = 0.9; // big enough to read from the top-down camera
const times = new Float32Array(FRAMES + 1).map((_, i) => i * DUR / FRAMES);
const quat = (ax, ang) => { const s = Math.sin(ang / 2); return [ax[0]*s, ax[1]*s, ax[2]*s, Math.cos(ang / 2)]; };
const qmul = (a, b) => [a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1], a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0], a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3], a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const walk = doc.createAnimation('Walk');
function track(node, path, vals, type) {
  const s = doc.createAnimationSampler().setInput(acc('SCALAR', times)).setOutput(acc(type, vals)).setInterpolation('LINEAR');
  walk.addSampler(s).addChannel(doc.createAnimationChannel().setTargetNode(node).setTargetPath(path).setSampler(s));
}
for (const L of legs) {
  const hipQ = new Float32Array((FRAMES + 1) * 4), kneeQ = new Float32Array((FRAMES + 1) * 4);
  const out = [L.foot[0] - L.hip[0], 0, L.foot[2] - L.hip[2]], ol = Math.hypot(out[0], out[2]);
  const liftAxis = [out[2] / ol, 0, -out[0] / ol];
  for (let f = 0; f <= FRAMES; f++) {
    const ph = (f / FRAMES + L.group * 0.5) % 1;
    let fwd, lift;
    if (ph < 0.55) { fwd = 1 - 2 * ph / 0.55; lift = 0; }
    else { const k = (ph - 0.55) / 0.45; fwd = -1 + 2 * k; lift = Math.sin(Math.PI * k); }
    const sw = quat([1, 0, 0], -fwd * SWING), up = quat(liftAxis, -lift * LIFT);
    hipQ.set(qmul(up, sw), 4*f);
    kneeQ.set(quat(liftAxis, lift * KNEE), 4*f);
  }
  track(L.hipN, 'rotation', hipQ, 'VEC4'); track(L.kneeN, 'rotation', kneeQ, 'VEC4');
}
const bT = new Float32Array((FRAMES + 1) * 3), bR = new Float32Array((FRAMES + 1) * 4), sS = new Float32Array((FRAMES + 1) * 3), hR = new Float32Array((FRAMES + 1) * 4);
for (let f = 0; f <= FRAMES; f++) {
  const a = f / FRAMES * Math.PI * 2;
  bT.set([BODY[0], BODY[1] - 0.045 * Math.cos(a * 2), BODY[2]], 3*f);
  // roll + pitch, and a yaw sway once per cycle so the gait shows from above
  bR.set(qmul(quat([0, 1, 0], 0.06 * Math.sin(a)), qmul(quat([0, 0, 1], 0.07 * Math.sin(a)), quat([1, 0, 0], 0.03 * Math.sin(a * 2)))), 4*f);
  const pulse = 1 + 0.07 * Math.max(0, Math.sin(a * 2)) ** 2;
  sS.set([pulse, pulse * 1.04, pulse], 3*f);
  hR.set(quat([0, 1, 0], 0.12 * Math.sin(a)), 4*f);
}
track(body, 'translation', bT, 'VEC3'); track(body, 'rotation', bR, 'VEC4');
track(belly, 'scale', sS, 'VEC3'); track(head, 'rotation', hR, 'VEC4');
await doc.transform(prune());
await io.write(out, doc);
console.log('wrote', out);
