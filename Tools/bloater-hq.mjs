// Bloater HQ: Meshy GLB -> rigged enemy keeping the full Meshy mesh and its real textures (resized for the phone).
//   node bloater-hq.mjs <meshy.glb> <out.glb> _ [texture size=1024]
// (bloater.mjs is the older 6k-tri flat-colour version.)
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
await MeshoptSimplifier.ready;
const [src, out] = process.argv.slice(2);
const doc = await io.read(src);
const root = doc.getRoot(), buf = root.listBuffers()[0];
const mesh = root.listMeshes()[0], prim = mesh.listPrimitives()[0], mat = prim.getMaterial();
const pos = prim.getAttribute('POSITION').getArray(), nrm = prim.getAttribute('NORMAL').getArray(), uv = prim.getAttribute('TEXCOORD_0').getArray();
const idx = prim.getIndices().getArray(), n = pos.length / 3;
const img = mat.getBaseColorTexture().getImage();
const { data: px, info } = await sharp(Buffer.from(img)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
const sample = (u, v) => { const x = Math.min(info.width - 1, Math.max(0, Math.floor((u % 1 + 1) % 1 * info.width))), y = Math.min(info.height - 1, Math.max(0, Math.floor((v % 1 + 1) % 1 * info.height))); const o = (y * info.width + x) * 4; return [px[o], px[o+1], px[o+2]]; };
// ---- 1. keep Meshy's full mesh and real textures (resized for the phone); per-vertex color for rigging only
const TEX = +(process.argv[5] || 1024);
const P = pos, NV0 = n;
const vcol = new Array(NV0); for (let v = 0; v < NV0; v++) vcol[v] = sample(uv[2*v], uv[2*v+1]);
async function shrink(tex, fmt) { if (!tex) return; const im = sharp(Buffer.from(tex.getImage())).resize(TEX, TEX);
  const out = fmt === 'jpeg' ? await im.jpeg({ quality: 85 }).toBuffer() : await im.png({ compressionLevel: 9 }).toBuffer();
  tex.setImage(new Uint8Array(out)).setMimeType('image/' + fmt); }
await shrink(mat.getBaseColorTexture(), 'jpeg');
await shrink(mat.getNormalTexture(), 'png');
await shrink(mat.getMetallicRoughnessTexture(), 'jpeg');
// URP wants metallic in R and smoothness in A (glTF: roughness in G, metal in B): write that layout next to the
// model as <material>_ms.png; Mats.ConvertGltf picks it up as _MetallicGlossMap
{
  const mr = mat.getMetallicRoughnessTexture();
  if (mr) {
    const { data, info } = await sharp(Buffer.from(mr.getImage())).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    const ms = Buffer.alloc(info.width * info.height * 4), mf = mat.getMetallicFactor(), rf = mat.getRoughnessFactor();
    for (let i = 0; i < info.width * info.height; i++) {
      const metal = Math.round(data[4*i+2] * mf), smooth = Math.round(255 - data[4*i+1] * rf);
      ms[4*i] = metal; ms[4*i+1] = metal; ms[4*i+2] = metal; ms[4*i+3] = smooth;
    }
    const path = out.replace(/[^/\\]+$/, '') + 'BloaterMat_ms.png';
    await sharp(ms, { raw: { width: info.width, height: info.height, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path);
    console.log('wrote', path);
  }
}
mat.setName('BloaterMat');
console.log('verts', NV0, 'tris', idx.length / 3, 'textures', TEX);
const acc = (type, arr) => doc.createAccessor().setType(type).setArray(arr).setBuffer(buf);
// ---- 2. skeleton
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
const NV = NV0, J = new Uint16Array(NV * 4), W = new Float32Array(NV * 4);
let nLeg = 0, nSac = 0, nHead = 0;
for (let v = 0; v < NV; v++) {
  const p = [P[3*v], P[3*v+1], P[3*v+2]], [r, g, b] = vcol[v];
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
