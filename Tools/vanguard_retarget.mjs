// Iron Vanguard (2026-10-14 Meshy biped, "smartrig:" skeleton, only a Dead clip) -> Resources/Models/hero_vanguard_meshy.glb
// with the game's Heavy clips retargeted onto it from the previous Heavy (Meshy "mixamorig:" rig: Running / Walking /
// run_fast_3_inplace), plus Dead (same rest pose, copied by bone name). Retarget per frame in world space: the old bone's
// rotation change from its rest, applied to the new bone after aligning the new rest bone direction onto the old one
// (so a different rest pose and bone roll still give the same posture); hips translation scaled by hip height.
// Full mesh kept; 2048 maps written next to the glb (VanguardMeshyMat_base / _normal / _ms), the glb keeps 64 px
// placeholders (as vanguard_meshy.mjs).
//   node vanguard_retarget.mjs <old_clips.glb> <new_char.glb> <new_dead.glb> <texdir> <out.glb>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
import sharp from 'sharp'; import fs from 'fs'; import path from 'path';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [oldF, newF, deadF, texDir, out] = process.argv.slice(2), SIZE = 2048, MAT = 'VanguardMeshyMat';

// ---- quaternions [x,y,z,w]
const qmul = (a, b) => [a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1], a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0], a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3], a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const qinv = q => [-q[0], -q[1], -q[2], q[3]];
const qnorm = q => { const l = Math.hypot(...q) || 1; return q.map(v => v / l); };
const qrot = (q, v) => { const p = qmul(qmul(q, [v[0], v[1], v[2], 0]), qinv(q)); return [p[0], p[1], p[2]]; };
const vsub = (a, b) => [a[0]-b[0], a[1]-b[1], a[2]-b[2]], vadd = (a, b) => [a[0]+b[0], a[1]+b[1], a[2]+b[2]], vscale = (a, s) => a.map(v => v * s);
const vnorm = v => { const l = Math.hypot(...v) || 1; return v.map(x => x / l); };
function qfromto(a, b) {
  a = vnorm(a); b = vnorm(b); const d = a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
  if (d < -0.9999) { const ax = Math.abs(a[0]) < 0.9 ? [1,0,0] : [0,1,0]; const c = vnorm([a[1]*ax[2]-a[2]*ax[1], a[2]*ax[0]-a[0]*ax[2], a[0]*ax[1]-a[1]*ax[0]]); return [c[0], c[1], c[2], 0]; }
  const c = [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; return qnorm([c[0], c[1], c[2], 1 + d]);
}
function slerp(a, b, t) {
  let d = a[0]*b[0]+a[1]*b[1]+a[2]*b[2]+a[3]*b[3]; if (d < 0) { b = b.map(v => -v); d = -d; }
  if (d > 0.9995) return qnorm(a.map((v, i) => v + (b[i] - v) * t));
  const th = Math.acos(d), s = Math.sin(th); return a.map((v, i) => (v * Math.sin((1 - t) * th) + b[i] * Math.sin(t * th)) / s);
}
const base = n => n.replace(/^.*:/, '');   // "mixamorig:LeftArm" / "smartrig:LeftArm" -> "LeftArm"

// ---- a skeleton: joints by base name, rest TRS, parents; channel sampling
function skeleton(doc) {
  const skin = doc.getRoot().listSkins()[0], joints = skin.listJoints(), set = new Set(joints);
  const J = new Map(); for (const j of joints) J.set(base(j.getName()), j);
  return { joints, set, J };
}
function sampler(ch) {
  const s = ch.getSampler(), inp = s.getInput().getArray(), outA = s.getOutput().getArray(), n = s.getOutput().getElementSize();
  return t => {
    let i = 0; while (i < inp.length - 1 && inp[i + 1] <= t) i++;
    const v = k => Array.from(outA.slice(k * n, k * n + n));
    if (i >= inp.length - 1 || t <= inp[0]) return v(t <= inp[0] ? 0 : inp.length - 1);
    const u = (t - inp[i]) / (inp[i + 1] - inp[i]);
    return n === 4 ? slerp(v(i), v(i + 1), u) : v(i).map((x, k) => x + (v(i + 1)[k] - x) * u);
  };
}
// world rotation + position of every node of a skeleton for given local overrides (Map node -> {r,t})
function worldPose(nodes, local) {
  const W = new Map();
  const get = n => {
    if (W.has(n)) return W.get(n);
    const L = local.get(n) || { r: n.getRotation(), t: n.getTranslation() };
    const p = n.getParentNode(); let res;
    if (!p) res = { r: L.r, p: L.t };
    else { const pw = get(p); res = { r: qnorm(qmul(pw.r, L.r)), p: vadd(pw.p, qrot(pw.r, L.t)) }; }
    W.set(n, res); return res;
  };
  for (const n of nodes) get(n);
  return W;
}

const oldDoc = await io.read(oldF), newDoc = await io.read(newF), deadDoc = await io.read(deadF);
const O = skeleton(oldDoc), N = skeleton(newDoc), D = skeleton(deadDoc);
const oRest = worldPose(O.joints, new Map()), nRest = worldPose(N.joints, new Map());
// the bone direction used to align rests: towards the main child
const mainChild = { Hips: 'Spine', Spine: 'Spine1', Spine1: 'Spine2', Spine2: 'Neck', Neck: 'Head', LeftShoulder: 'LeftArm', LeftArm: 'LeftForeArm', LeftForeArm: 'LeftHand', LeftHand: 'LeftHandMiddle1',
  RightShoulder: 'RightArm', RightArm: 'RightForeArm', RightForeArm: 'RightHand', RightHand: 'RightHandMiddle1', LeftUpLeg: 'LeftLeg', LeftLeg: 'LeftFoot', LeftFoot: 'LeftToeBase', RightUpLeg: 'RightLeg', RightLeg: 'RightFoot', RightFoot: 'RightToeBase' };
const align = new Map();
for (const [name, nj] of N.J) {
  const oj = O.J.get(name), c = mainChild[name] ?? (() => { const k = nj.listChildren().find(ch => O.J.has(base(ch.getName())) && N.set.has(ch)); return k && base(k.getName()); })();
  if (!oj || name === 'Hips' || !c || !N.J.get(c) || !O.J.get(c)) { align.set(name, [0, 0, 0, 1]); continue; }
  const dn = vsub(nRest.get(N.J.get(c)).p, nRest.get(nj).p), dO = vsub(oRest.get(O.J.get(c)).p, oRest.get(oj).p);
  align.set(name, qfromto(dn, dO));
}
const hipRatio = nRest.get(N.J.get('Hips')).p[1] / oRest.get(O.J.get('Hips')).p[1];
console.log('joints old', O.joints.length, 'new', N.joints.length, 'mapped', [...N.J.keys()].filter(k => O.J.has(k)).length, 'hip ratio', hipRatio.toFixed(3));

// ---- drop the new file's one-frame rest action; retarget the old clips
const root = newDoc.getRoot(), buf = root.listBuffers()[0];
for (const a of root.listAnimations()) a.dispose();
const newOrder = []; { const seen = new Set(); const walk = n => { if (seen.has(n)) return; const p = n.getParentNode(); if (p && N.set.has(p)) walk(p); seen.add(n); newOrder.push(n); }; N.joints.forEach(walk); }
for (const anim of oldDoc.getRoot().listAnimations()) {
  const chans = new Map(); let times = null;
  for (const ch of anim.listChannels()) {
    const node = ch.getTargetNode(), pathK = ch.getTargetPath(); if (!O.set.has(node)) continue;
    if (!chans.has(node)) chans.set(node, {}); chans.get(node)[pathK] = sampler(ch);
    const inp = ch.getSampler().getInput().getArray(); if (!times || inp.length > times.length) times = Array.from(inp);
  }
  const outR = new Map(N.joints.map(j => [j, []])), hipT = [];
  const nHips = N.J.get('Hips'), oHips = O.J.get('Hips');
  for (const t of times) {
    const local = new Map(); for (const [node, c] of chans) local.set(node, { r: c.rotation ? c.rotation(t) : node.getRotation(), t: c.translation ? c.translation(t) : node.getTranslation() });
    const oW = worldPose(O.joints, local);
    const nW = new Map();
    for (const nj of newOrder) {
      const name = base(nj.getName()), oj = O.J.get(name), p = nj.getParentNode(), pW = p && nW.has(p) ? nW.get(p) : (p ? nRest.get(p) ?? { r: [0,0,0,1] } : { r: [0,0,0,1] });
      let w;
      if (oj) { const dlt = qmul(oW.get(oj).r, qinv(oRest.get(oj).r)); w = qnorm(qmul(dlt, qmul(align.get(name), nRest.get(nj).r))); }
      else w = qnorm(qmul(pW.r, nj.getRotation()));
      nW.set(nj, { r: w });
      outR.get(nj).push(...qnorm(qmul(qinv(pW.r), w)));
    }
    const oT = local.get(oHips)?.t ?? oHips.getTranslation();
    hipT.push(...vadd(nHips.getTranslation(), vscale(vsub(oT, oHips.getTranslation()), hipRatio)));
  }
  const a = newDoc.createAnimation(anim.getName()), input = newDoc.createAccessor().setType('SCALAR').setArray(new Float32Array(times)).setBuffer(buf);
  for (const nj of N.joints) {
    const s = newDoc.createAnimationSampler().setInput(input).setOutput(newDoc.createAccessor().setType('VEC4').setArray(new Float32Array(outR.get(nj))).setBuffer(buf)).setInterpolation('LINEAR');
    a.addSampler(s).addChannel(newDoc.createAnimationChannel().setTargetNode(nj).setTargetPath('rotation').setSampler(s));
  }
  const hs = newDoc.createAnimationSampler().setInput(input).setOutput(newDoc.createAccessor().setType('VEC3').setArray(new Float32Array(hipT)).setBuffer(buf)).setInterpolation('LINEAR');
  a.addSampler(hs).addChannel(newDoc.createAnimationChannel().setTargetNode(nHips).setTargetPath('translation').setSampler(hs));
  console.log('retargeted', anim.getName(), times.length, 'keys', times[times.length - 1].toFixed(2), 's');
}
// ---- Dead: same rest pose as the new character, copy by bone name
for (const anim of deadDoc.getRoot().listAnimations()) {
  const a = newDoc.createAnimation('Dead'); let n = 0;
  for (const ch of anim.listChannels()) {
    const tn = ch.getTargetNode(); if (!D.set.has(tn)) continue; const nj = N.J.get(base(tn.getName())); if (!nj) continue;
    const s = ch.getSampler();
    const ns = newDoc.createAnimationSampler().setInterpolation(s.getInterpolation())
      .setInput(newDoc.createAccessor().setType('SCALAR').setArray(new Float32Array(s.getInput().getArray())).setBuffer(buf))
      .setOutput(newDoc.createAccessor().setType(s.getOutput().getType()).setArray(new Float32Array(s.getOutput().getArray())).setBuffer(buf));
    a.addSampler(ns).addChannel(newDoc.createAnimationChannel().setTargetNode(nj).setTargetPath(ch.getTargetPath()).setSampler(ns)); n++;
  }
  console.log('Dead:', n, 'channels');
}
// ---- material: 2048 maps beside the glb, 64 px placeholders inside
const dirOut = path.dirname(out), T = f => path.join(texDir, f);
const pre = 'Meshy_AI_Iron_Vanguard_biped_texture_0';
await sharp(T(pre + '.png')).resize(SIZE, SIZE, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, MAT + '_base.png'));
await sharp(T(pre + '_normal.png')).resize(SIZE, SIZE, { kernel: 'lanczos3' }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, MAT + '_normal.png'));
{
  const m = await sharp(T(pre + '_metallic.png')).resize(SIZE, SIZE).greyscale().raw().toBuffer(), r = await sharp(T(pre + '_roughness.png')).resize(SIZE, SIZE).greyscale().raw().toBuffer();
  const ms = Buffer.alloc(SIZE * SIZE * 4); for (let i = 0; i < SIZE * SIZE; i++) { ms[4*i] = ms[4*i+1] = ms[4*i+2] = m[i]; ms[4*i+3] = 255 - r[i]; }
  await sharp(ms, { raw: { width: SIZE, height: SIZE, channels: 4 } }).png({ compressionLevel: 9 }).toFile(path.join(dirOut, MAT + '_ms.png'));
}
for (const mat of root.listMaterials()) {
  mat.setName(MAT).setMetallicFactor(1).setRoughnessFactor(1);
  for (const t of [mat.getBaseColorTexture(), mat.getNormalTexture()]) if (t) t.setImage(new Uint8Array(await sharp(Buffer.from(t.getImage())).resize(64, 64).png().toBuffer())).setMimeType('image/png');
}
await newDoc.transform(prune());
await io.write(out, newDoc);
console.log('wrote', out, (fs.statSync(out).size / 1e6).toFixed(1), 'MB; clips', root.listAnimations().map(a => a.getName()).join(', '));
