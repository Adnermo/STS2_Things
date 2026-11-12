// Dump local and world bone poses through the official Spine 4.2 runtime.
// node pose_dump.mjs SPJSON OUT.json clip@time[,clip@time...]
import fs from 'node:fs';
import {load, pose} from '../ScaleBeetleRig/runtime.mjs';

const [,, json, out, specs] = process.argv;
const atlas = new URL('../../animations/monsters/cave_god/cave_god.atlas', import.meta.url);
const data = load(json, decodeURIComponent(atlas.pathname.replace(/^\/(\w:)/, '$1')));
const result = {};
for (const spec of specs.split(',')) {
  const [clip, time] = spec.split('@');
  const s = pose(data, clip, Number(time));
  const bones = {};
  for (const b of s.bones) {
    bones[b.data.name] = {
      local: [b.rotation, b.x, b.y, b.scaleX, b.scaleY, b.shearX, b.shearY],
      world: [b.worldX, b.worldY, Math.atan2(b.c, b.a) * 180 / Math.PI, b.a, b.b, b.c, b.d],
    };
  }
  const ik = Object.fromEntries(s.ikConstraints.map(c => [c.data.name, {mix: c.mix, bend: c.bendDirection}]));
  result[spec] = {bones, ik};
}
fs.writeFileSync(out, JSON.stringify(result));
console.log(`dumped ${Object.keys(result).length} poses to ${out}`);
