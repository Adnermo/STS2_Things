// Rigid carapace bone transforms for every clip, from the official 4.2 runtime.
// node seam_dump.mjs SPJSON ATLAS OUT.json [HZ]   (input for seams.py)
import fs from 'node:fs';
import {load, pose} from './runtime.mjs';

const [path, atlas, out, hz = '30'] = process.argv.slice(2);
const rate = Number(hz);
const data = load(path, atlas);
const bones = ['body', 'shell_far', 'head', 'neck_shield', 'collar', 'shell_near', 'eye', 'jaw_far', 'jaw_near'];
const matrices = skeleton => Object.fromEntries(bones.map(name => {
  const b = skeleton.findBone(name);
  return [name, [b.a, b.b, b.c, b.d, b.worldX, b.worldY]];
}));
const clips = {};
for (const animation of data.animations) {
  const frames = [];
  for (let i = 0; i <= Math.ceil(animation.duration * rate - 1e-9); i++) {
    const t = Math.min(i / rate, animation.duration);
    frames.push({t, m: matrices(pose(data, animation.name, t))});
  }
  clips[animation.name] = frames;
}
fs.writeFileSync(out, JSON.stringify({rest: matrices(pose(data)), clips}));
console.log('SEAM_DUMP', path, Object.keys(clips).length, 'clips');
