// Heavy's minigun: cut the barrel cluster (every triangle wholly at x < CUT, the -X muzzle end) out of the decimated
// mesh into its own node "Barrels", pivoted on the barrel axis, so the game can spin it while firing.
//   node minigun_split.mjs <minigun.glb> [cut x=-0.14]     (run after vanguard.mjs ... minigun.glb 18000 2048 MinigunMat)
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [file] = process.argv.slice(2), CUT = +(process.argv[3] ?? -0.14);
const doc = await io.read(file), root = doc.getRoot(), buf = root.listBuffers()[0];
const node = root.listNodes().find(n => n.getMesh()), prim = node.getMesh().listPrimitives()[0];
const pos = prim.getAttribute('POSITION').getArray(), idx = prim.getIndices().getArray();
const body = [], barrel = [];
for (let t = 0; t < idx.length; t += 3) {
  const inB = [0, 1, 2].every(k => pos[3 * idx[t + k]] < CUT);
  (inB ? barrel : body).push(idx[t], idx[t + 1], idx[t + 2]);
}
// barrel axis = centre of the cluster's cross-section
let y0 = 1e9, y1 = -1e9, z0 = 1e9, z1 = -1e9;
for (const i of barrel) { y0 = Math.min(y0, pos[3*i+1]); y1 = Math.max(y1, pos[3*i+1]); z0 = Math.min(z0, pos[3*i+2]); z1 = Math.max(z1, pos[3*i+2]); }
const cy = (y0 + y1) / 2, cz = (z0 + z1) / 2;
// rebuild: body keeps the original vertex arrays (unused ones get pruned by Unity anyway); barrels get a compacted copy
const remap = new Map(), sem = prim.listSemantics(), arrays = Object.fromEntries(sem.map(s => [s, []]));
const bIdx = barrel.map(i => { if (!remap.has(i)) { remap.set(i, remap.size);
  for (const s of sem) { const a = prim.getAttribute(s), n = a.getElementSize(), src = a.getArray();
    for (let k = 0; k < n; k++) arrays[s].push(src[i * n + k] - (s === 'POSITION' ? [0, cy, cz][k] : 0)); } } return remap.get(i); });
prim.setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(body)).setBuffer(buf));
const bp = doc.createPrimitive().setMaterial(prim.getMaterial()).setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(bIdx)).setBuffer(buf));
for (const s of sem) { const a = prim.getAttribute(s); bp.setAttribute(s, doc.createAccessor().setType(a.getType()).setArray(new (a.getArray().constructor)(arrays[s])).setBuffer(buf)); }
const bn = doc.createNode('Barrels').setMesh(doc.createMesh('Barrels').addPrimitive(bp)).setTranslation([0, cy, cz]);
node.addChild(bn);
await doc.transform(prune());
await io.write(file, doc);
console.log(`body ${body.length / 3} tris, barrels ${barrel.length / 3} tris, axis y ${cy.toFixed(3)} z ${cz.toFixed(3)}, cut x < ${CUT}`);
