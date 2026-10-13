// Re-rig the Meshy Drone: replace its auto-rig with Body + 6 legs (hip, knee) and bake a tripod-gait Walk clip.
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [src, out] = process.argv.slice(2);
const doc = await io.read(src);
const root = doc.getRoot(), buf = root.listBuffers()[0];
const mesh = root.listMeshes()[0], prim = mesh.listPrimitives()[0];
const P = prim.getAttribute('POSITION').getArray(), NV = P.length / 3;
const FEET = [[-0.75, 0.84], [-0.94, -0.13], [-0.39, -0.85], [0.60, 0.85], [0.94, 0.22], [0.90, -0.49]];
const NAMES = ['LF', 'LM', 'LR', 'RF', 'RM', 'RR'];
const BODY = [0, 0.95, 0.05];
const legs = FEET.map(([fx, fz], i) => {
  const s = Math.sign(fx);
  const hip = [s * 0.30, 0.82, fz * 0.42];
  const foot = [fx, 0.02, fz];
  const knee = [hip[0] + (foot[0] - hip[0]) * 0.55, 0.62, hip[2] + (foot[2] - hip[2]) * 0.55];
  return { name: NAMES[i], hip, knee, foot, side: s, group: (i === 0 || i === 4 || i === 2) ? 0 : 1 };
});
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
function segDist(p, a, b) { const ab = sub(b, a), t = Math.max(0, Math.min(1, dot(sub(p, a), ab) / dot(ab, ab))); const q = [a[0] + ab[0] * t, a[1] + ab[1] * t, a[2] + ab[2] * t]; return [Math.hypot(...sub(p, q)), t]; }
const arm = doc.createNode('DroneRig');
const body = doc.createNode('Body').setTranslation(BODY); arm.addChild(body);
const joints = [body];
for (const L of legs) {
  L.hipN = doc.createNode('Hip_' + L.name).setTranslation(sub(L.hip, BODY)); body.addChild(L.hipN);
  L.kneeN = doc.createNode('Knee_' + L.name).setTranslation(sub(L.knee, L.hip)); L.hipN.addChild(L.kneeN);
  joints.push(L.hipN, L.kneeN);
}
const worldOf = [BODY, ...legs.flatMap(L => [L.hip, L.knee])];
const ibm = new Float32Array(16 * joints.length);
worldOf.forEach((w, j) => { ibm.set([1,0,0,0, 0,1,0,0, 0,0,1,0, -w[0],-w[1],-w[2],1], 16 * j); });
const skin = doc.createSkin('DroneSkin').setSkeleton(body)
  .setInverseBindMatrices(doc.createAccessor().setType('MAT4').setArray(ibm).setBuffer(buf));
joints.forEach(j => skin.addJoint(j));
const J = new Uint16Array(NV * 4), W = new Float32Array(NV * 4);
let legCount = 0;
for (let v = 0; v < NV; v++) {
  const p = [P[3 * v], P[3 * v + 1], P[3 * v + 2]];
  let best = null, bd = 1e9;
  legs.forEach((L, li) => {
    const [d1, t1] = segDist(p, L.hip, L.knee), [d2, t2] = segDist(p, L.knee, L.foot);
    const d = Math.min(d1, d2), t = d1 < d2 ? t1 * 0.5 : 0.5 + t2 * 0.5;
    if (d < bd) { bd = d; best = { li, t }; }
  });
  const isLeg = p[1] < 0.6 || (bd < 0.2 && Math.abs(p[0]) > 0.28);
  if (!isLeg) { J[4 * v] = 0; W[4 * v] = 1; continue; }
  legCount++;
  const hipJ = 1 + 2 * best.li, kneeJ = hipJ + 1, t = best.t;
  if (t < 0.12) { const k = t / 0.12; J.set([0, hipJ], 4 * v); W.set([1 - k, k], 4 * v); }
  else if (t < 0.4) { J[4 * v] = hipJ; W[4 * v] = 1; }
  else if (t < 0.6) { const k = (t - 0.4) / 0.2; J.set([hipJ, kneeJ], 4 * v); W.set([1 - k, k], 4 * v); }
  else { J[4 * v] = kneeJ; W[4 * v] = 1; }
}
console.log('leg verts', legCount, 'of', NV);
prim.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(J).setBuffer(buf));
prim.setAttribute('WEIGHTS_0', doc.createAccessor().setType('VEC4').setArray(W).setBuffer(buf));
const scene = root.listScenes()[0];
for (const n of scene.listChildren()) scene.removeChild(n);
const meshNode = doc.createNode('Drone').setMesh(mesh).setSkin(skin);
scene.addChild(arm); scene.addChild(meshNode);
for (const n of root.listNodes()) if (n.getSkin() && n !== meshNode) n.setSkin(null);
for (const s of root.listSkins()) if (s !== skin) s.dispose();
for (const a of root.listAnimations()) a.dispose();
const FRAMES = 24, DUR = 1.0, SWING = 0.45, LIFT = 0.5, KNEE = 0.55;
const times = new Float32Array(FRAMES + 1).map((_, i) => i * DUR / FRAMES);
const quat = (ax, ang) => { const s = Math.sin(ang / 2); return [ax[0] * s, ax[1] * s, ax[2] * s, Math.cos(ang / 2)]; };
const qmul = (a, b) => [a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1], a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0], a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3], a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const walk = doc.createAnimation('Walk');
function track(node, path, vals, type) {
  const s = doc.createAnimationSampler().setInput(doc.createAccessor().setArray(times).setBuffer(buf))
    .setOutput(doc.createAccessor().setType(type).setArray(vals).setBuffer(buf)).setInterpolation('LINEAR');
  walk.addSampler(s).addChannel(doc.createAnimationChannel().setTargetNode(node).setTargetPath(path).setSampler(s));
}
for (const L of legs) {
  const out = [L.foot[0] - L.hip[0], 0, L.foot[2] - L.hip[2]], ol = Math.hypot(out[0], out[2]);
  const outward = [out[0] / ol, 0, out[2] / ol];
  const liftAxis = [outward[2], 0, -outward[0]];
  const hipQ = new Float32Array((FRAMES + 1) * 4), kneeQ = new Float32Array((FRAMES + 1) * 4);
  for (let f = 0; f <= FRAMES; f++) {
    let ph = (f / FRAMES + L.group * 0.5) % 1;
    let fwd, lift;
    if (ph < 0.5) { fwd = 1 - 4 * ph; lift = 0; }
    else { const k = (ph - 0.5) / 0.5; fwd = -1 + 2 * k; lift = Math.sin(Math.PI * k); }
    const yaw = quat([0, 1, 0], fwd * SWING * -L.side);
    const up = quat(liftAxis, -lift * LIFT);
    hipQ.set(qmul(up, yaw), 4 * f);
    kneeQ.set(quat(liftAxis, lift * KNEE), 4 * f);
  }
  track(L.hipN, 'rotation', hipQ, 'VEC4');
  track(L.kneeN, 'rotation', kneeQ, 'VEC4');
}
const bodyT = new Float32Array((FRAMES + 1) * 3), bodyR = new Float32Array((FRAMES + 1) * 4);
for (let f = 0; f <= FRAMES; f++) {
  const a = f / FRAMES * Math.PI * 2;
  bodyT.set([BODY[0], BODY[1] + 0.03 * Math.cos(a * 2), BODY[2]], 3 * f);
  bodyR.set(quat([0, 0, 1], 0.05 * Math.sin(a)), 4 * f);
}
track(body, 'translation', bodyT, 'VEC3');
track(body, 'rotation', bodyR, 'VEC4');
await doc.transform(prune());
await io.write(out, doc);
console.log('wrote', out);
