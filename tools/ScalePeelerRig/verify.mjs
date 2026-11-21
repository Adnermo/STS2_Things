import assert from 'node:assert/strict';
import fs from 'node:fs';
import { load, pose, worldVertices } from './runtime.mjs';

const data = load('pkg/scale_peeler.spjson', 'pkg/scale_peeler.atlas');
const raw = JSON.parse(fs.readFileSync('pkg/scale_peeler.spjson', 'utf8'));
const names = ['idle_loop', 'attack', 'slam', 'cast', 'flurry', 'hurt', 'die', 'revive', 'summon', 'power_up'];
assert.deepEqual(data.animations.map((a) => a.name).sort(), names.toSorted());
assert.equal(data.bones.length, 25);

const meshSlots = ['body', 'jaw_upper', 'jaw_lower', 'mouth_interior', 'burrow_back', 'burrow_front', 'feeler_far', 'feeler_near', 'plate_0', 'plate_1', 'plate_2', 'plate_3'];
const vertices = (slot, animation, t) => worldVertices(pose(data, animation, t).findSlot(slot));
const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

const loopErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'idle_loop', 0), vertices(s, 'idle_loop', 4))));
const reviveErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'die', 1.45), vertices(s, 'revive', 0))));
assert(loopErr < 0.05, `Idle loop seam: ${loopErr}`);
assert(reviveErr < 0.05, `Death/revive seam: ${reviveErr}`);

const setup = Object.fromEntries(meshSlots.map((s) => [s, vertices(s, null, 0)]));
const returnErrors = {};
for (const anim of data.animations) {
  if (anim.name === 'die') continue;
  const err = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, anim.name, anim.duration), setup[s])));
  returnErrors[anim.name] = err;
  assert(err < 0.15, `${anim.name} does not settle to rest: ${err}`);
}

const burrowRest = pose(data, null).findBone('burrow');
let burrowDrift = 0;
for (const anim of data.animations) {
  for (let t = 0; t <= anim.duration; t += 1 / 60) {
    const b = pose(data, anim.name, t).findBone('burrow');
    burrowDrift = Math.max(burrowDrift, Math.hypot(b.worldX - burrowRest.worldX, b.worldY - burrowRest.worldY));
  }
}
assert(burrowDrift < 0.1, `Burrow slips: ${burrowDrift}`);

const headX = (t) => pose(data, 'attack', t).findBone('head').worldX;
assert(headX(0.22) > headX(0) + 10, `Attack coils back before biting: ${headX(0.22)} vs ${headX(0)}`);
assert(headX(0.46) < headX(0) - 55, `Attack lunges left on contact: ${headX(0.46)} vs ${headX(0)}`);

function area(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((k) => k * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}

let frames = 0;
let flipped = 0;
let maxDisplacement = 0;
const failures = {};
for (const slot of meshSlots) {
  const slotIdx = data.slots.findIndex((s) => s.name === slot);
  const tris = data.defaultSkin.getAttachment(slotIdx, slot).triangles;
  const base = Array.from({ length: tris.length / 3 }, (_, i) => area(setup[slot], i * 3, tris));
  for (const anim of data.animations) {
    for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
      const v = vertices(slot, anim.name, t);
      assert(v.every(Number.isFinite), `non-finite vertices in ${slot}:${anim.name}`);
      for (let i = 0; i < base.length; i++) {
        if (area(v, i * 3, tris) * base[i] < -0.01) {
          flipped++;
          failures[`${slot}:${anim.name}`] = (failures[`${slot}:${anim.name}`] ?? 0) + 1;
        }
      }
      if (slot === 'body') {
        for (let i = 0; i < v.length; i++) maxDisplacement = Math.max(maxDisplacement, Math.abs(v[i] - setup.body[i]));
      }
      frames++;
    }
  }
}
assert.equal(flipped, 0, JSON.stringify(failures));
assert(maxDisplacement > 140, `Readable deformation too subtle: ${maxDisplacement}`);
assert.equal(raw.animations.attack.events[0].name, 'bite_contact');
assert.equal(raw.animations.slam.events[0].name, 'slam_contact');
assert.equal(raw.animations.cast.events[1].name, 'siphon_peak');
assert.equal(raw.animations.flurry.events[1].name, 'flurry_hit_1');

const report = {
  result: 'PASS',
  runtime: 'official Spine 4.2.43',
  bones: data.bones.length,
  animations: names.length,
  meshSlots: meshSlots.length,
  frames,
  loopErr,
  reviveErr,
  returnErrors,
  burrowDrift,
  invertedTriangles: flipped,
  maxDisplacement,
};
fs.writeFileSync('pkg/verification.json', JSON.stringify(report, null, 2));
console.log(report);
