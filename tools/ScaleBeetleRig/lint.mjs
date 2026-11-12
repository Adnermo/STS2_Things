// Non-stopping per-clip report of everything verify.mjs asserts, plus mesh folds: `node lint.mjs [spjson] [atlas]`.
import fs from 'node:fs';
import * as S from '@esotericsoftware/spine-core';
import {load, pose, worldVertices} from './runtime.mjs';

const path = process.argv[2] ?? 'pkg/scale_beetle.spjson';
const atlas = process.argv[3] ?? 'pkg/scale_beetle.atlas';
const data = load(path, atlas);
const targets = JSON.parse(fs.readFileSync('out/foot-targets.json', 'utf8'));
const legs = ['front', 'middle', 'rear', 'far_front', 'far_middle', 'far_rear'];
const floorParts = ['head', 'jaw_near', 'jaw_far', 'antenna_near', 'antenna_far'];
const area = (v, a, b, c) => (v[2 * b] - v[2 * a]) * (v[2 * c + 1] - v[2 * a + 1]) - (v[2 * c] - v[2 * a]) * (v[2 * b + 1] - v[2 * a + 1]);

const rest = pose(data);
const planted = Object.fromEntries(legs.map(n => [n, [rest.findBone(n + '_ankle').worldX, rest.findBone(n + '_ankle').worldY]]));
const meshes = [];
for (const {slotIndex, attachment} of data.defaultSkin.getAttachments()) {
  if (!(attachment instanceof S.MeshAttachment)) continue;
  const base = worldVertices(rest.slots[slotIndex], attachment), tris = attachment.triangles, sign = [];
  for (let i = 0; i < tris.length; i += 3) sign.push(area(base, tris[i], tris[i + 1], tris[i + 2]));
  meshes.push({slotIndex, attachment, sign, name: rest.slots[slotIndex].data.name});
}
const verts = s => Object.fromEntries(s.slots.map(slot => [slot.data.name, worldVertices(slot)]));
const gap = (a, b) => {let m = 0; for (const k in a) for (let i = 0; i < a[k].length; i++) m = Math.max(m, Math.abs(a[k][i] - b[k][i])); return m;};
const setup = verts(rest);

const problems = [];
const f1 = v => (Number.isFinite(v) ? v.toFixed(1) : '-');
console.log('clip       footErr slide cont bodyStep feel  bodyX  bodyR  headY  floorMin(part@t)        folds{part:[triangle samples at 60 Hz, most at once, largest flipped area px^2, its time]}');
for (const animation of data.animations) {
  const name = animation.name, duration = animation.duration;
  let footError = 0, footAt = 0, footLeg = '', slide = 0, slideAt = 0, slideLeg = '', contacts = 6, contactsAt = 0, bodyStep = 0, bodyAt = 0, feelStep = 0, feelAt = 0;
  let floor = Infinity, floorPart = '', floorAt = 0, previous;
  const ranges = {bodyX: [Infinity, -Infinity], bodyR: [Infinity, -Infinity], headY: [Infinity, -Infinity]};
  const last = {}, folds = {};
  const living = !['die', 'revive'].includes(name);
  for (let frame = 0; frame <= Math.ceil(duration * 120); frame++) {
    const t = Math.min(frame / 120, duration), s = pose(data, name, t);
    for (const [key, bone, field] of [['bodyX', 'body', 'worldX'], ['bodyR', 'body', 'rotation'], ['headY', 'head', 'worldY']]) {
      const v = s.findBone(bone)[field]; ranges[key] = [Math.min(ranges[key][0], v), Math.max(ranges[key][1], v)];
    }
    const pts = ['body', 'head', 'antenna_near_7', 'antenna_far_7'].map(n => s.findBone(n));
    if (previous) pts.forEach((b, i) => {
      const d = Math.hypot(b.worldX - previous[i][0], b.worldY - previous[i][1]);
      if (i < 2 && d > bodyStep) { bodyStep = d; bodyAt = t; }
      if (i >= 2 && d > feelStep) { feelStep = d; feelAt = t; }
    });
    previous = pts.map(b => [b.worldX, b.worldY]);
    if (living) {
      const list = targets[name], index = Math.min(Math.floor(t * 60 + 1e-6), list.length - 1);
      const a = list[index], b = list[Math.min(index + 1, list.length - 1)], u = Math.min(1, Math.max(0, t * 60 - index));
      let count = 0;
      for (const leg of legs) {
        const offset = a.feet[leg].map((v, i) => v * (1 - u) + b.feet[leg][i] * u), foot = s.findBone(leg + '_ankle');
        const e = Math.hypot(foot.worldX - planted[leg][0] - offset[0], foot.worldY - planted[leg][1] - offset[1]);
        if (e > footError) { footError = e; footAt = t; footLeg = leg; }
        if (offset[1] < .5) count++;
        const prev = last[leg];
        if (prev && prev.lift < .1 && offset[1] < .1 && Math.abs(foot.worldX - prev.x) > slide) { slide = Math.abs(foot.worldX - prev.x); slideAt = t; slideLeg = leg; }
        last[leg] = {x: foot.worldX, lift: offset[1]};
      }
      if (count < contacts) { contacts = count; contactsAt = t; }
      for (const part of floorParts) {
        const y = Math.min(...worldVertices(s.slots.find(x => x.data.name === part)).filter((_, i) => i % 2));
        if (y < floor) { floor = y; floorPart = part; floorAt = t; }
      }
    }
    if (frame % 2 === 0) for (const m of meshes) {
      const v = worldVertices(s.slots[m.slotIndex], m.attachment), tris = m.attachment.triangles;
      let now = 0, flipped = 0;
      for (let i = 0, k = 0; i < tris.length; i += 3, k++) {
        if (Math.abs(m.sign[k]) < 20) continue;
        const a = area(v, tris[i], tris[i + 1], tris[i + 2]);
        if (Math.sign(a) !== Math.sign(m.sign[k])) { now++; flipped = Math.max(flipped, Math.abs(a) / 2); }
      }
      if (now) {
        const row = folds[m.name] ??= [0, 0, 0, 0];
        row[0] += now; row[1] = Math.max(row[1], now);
        if (flipped > row[2]) { row[2] = Math.round(flipped); row[3] = +t.toFixed(2); }
      }
    }
  }
  const range = k => ranges[k][1] - ranges[k][0];
  console.log(name.padEnd(10), f1(footError).padStart(6), f1(slide).padStart(5), String(contacts).padStart(4), f1(bodyStep).padStart(8), f1(feelStep).padStart(5),
    f1(range('bodyX')).padStart(6), f1(range('bodyR')).padStart(6), f1(range('headY')).padStart(6), `${f1(floor)} ${floorPart}@${floorAt.toFixed(2)}`.padEnd(24), JSON.stringify(folds));
  if (living && footError >= 1.5) problems.push(`${name}: foot target unreachable ${footError.toFixed(1)} (${footLeg}@${footAt.toFixed(3)})`);
  if (living && slide >= .7) problems.push(`${name}: planted foot skating ${slide.toFixed(2)} (${slideLeg}@${slideAt.toFixed(3)})`);
  if (living && contacts < 3) problems.push(`${name}: only ${contacts} supporting feet @${contactsAt.toFixed(3)}`);
  if (bodyStep >= 16) problems.push(`${name}: body step ${bodyStep.toFixed(1)} @${bodyAt.toFixed(3)}`);
  if (feelStep >= 95) problems.push(`${name}: feeler step ${feelStep.toFixed(1)} @${feelAt.toFixed(3)}`);
  if (living && floor <= 0) problems.push(`${name}: ${floorPart} under the floor (${floor.toFixed(1)}) @${floorAt.toFixed(3)}`);
  if (name === 'attack' && !(range('bodyX') > 130 && range('headY') > 90)) problems.push(`attack amplitude bodyX ${range('bodyX').toFixed(1)} headY ${range('headY').toFixed(1)}`);
  if (name === 'whip' && !(range('bodyR') > 14 && range('bodyX') > 50)) problems.push(`whip amplitude bodyR ${range('bodyR').toFixed(1)} bodyX ${range('bodyX').toFixed(1)}`);
  if (['cast', 'molt', 'power_up', 'summon'].includes(name) && !(range('headY') > 70)) problems.push(`${name}: headY amplitude ${range('headY').toFixed(1)}`);
  if (!['idle_loop', 'die'].includes(name)) {
    const e = gap(setup, verts(pose(data, name, duration)));
    if (e >= .05) problems.push(`${name}: end vs setup ${e.toFixed(3)}`);
  }
}
const idleGap = gap(verts(pose(data, 'idle_loop', 0)), verts(pose(data, 'idle_loop', data.findAnimation('idle_loop').duration)));
if (idleGap >= .01) problems.push(`idle loop gap ${idleGap.toFixed(4)}`);
const reviveGap = gap(verts(pose(data, 'die', data.findAnimation('die').duration)), verts(pose(data, 'revive', 0)));
if (reviveGap >= .02) problems.push(`die->revive gap ${reviveGap.toFixed(4)}`);
for (const [side, t] of [['near', .60], ['far', 1.12], ['near', 1.68], ['far', 1.68]]) {
  const reach = rest.findBone(`antenna_${side}_7`).worldX - pose(data, 'whip', t).findBone(`antenna_${side}_7`).worldX;
  console.log(`whip reach ${side}@${t}: ${reach.toFixed(1)}`);
  if (reach <= 500) problems.push(`whip ${side}@${t} reach ${reach.toFixed(1)}`);
}
const jaw = pose(data, 'attack', .3).findBone('jaw_near').rotation - pose(data, 'attack', .68).findBone('jaw_near').rotation;
console.log(`bite jaw close: ${jaw.toFixed(1)}`);
if (jaw <= 40) problems.push(`bite jaw close ${jaw.toFixed(1)}`);
console.log(problems.length ? `\nPROBLEMS (${problems.length}):\n  ` + problems.join('\n  ') : '\nLINT CLEAN');
