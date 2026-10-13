// Escape floor test (start room): the picked worn tile -> a x2 detail albedo + its normal map, for
// Resources/Textures/EscapeFloor. URP's MULX2 detail is 2 x the sampled value, so the detail is imported LINEAR with its
// mean at 128 (= 0.5, neutral); contrast x0.8 keeps the painted floor's brightness (variation about +-13%).
//   node escape_floor.mjs <Concepts/Escape/Floor> <Resources/Textures/EscapeFloor>
import sharp from 'sharp'; import fs from 'fs'; import path from 'path';
const [src, out] = process.argv.slice(2), K = 0.8;
const { data, info } = await sharp(path.join(src, 'floor_c2_worn_2048.png')).removeAlpha().raw().toBuffer({ resolveWithObject: true });
const st = await sharp(path.join(src, 'floor_c2_worn_2048.png')).stats(), mean = st.channels.slice(0, 3).map(c => c.mean);
const o = Buffer.alloc(data.length);
for (let i = 0; i < data.length; i++) o[i] = Math.max(0, Math.min(255, Math.round(128 + (data[i] - mean[i % 3]) * K)));
await sharp(o, { raw: info }).png({ compressionLevel: 9 }).toFile(path.join(out, 'floor_worn_detail.png'));
fs.copyFileSync(path.join(src, 'normal_floor_c2_worn_2048.png'), path.join(out, 'floor_worn_normal.png'));
console.log('detail mean', mean.map(m => m.toFixed(1)), '-> 128, contrast x' + K);
