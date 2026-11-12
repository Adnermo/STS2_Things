// Official Spine 4.2 runtime checks for the redone 活体巨岩 clips.
// node tools/CaveGodRig/verify.mjs SPJSON [OUT.json]
import fs from 'node:fs';
import * as S from '../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';
import {load, pose, worldVertices} from '../ScaleBeetleRig/runtime.mjs';

const [,, json, out] = process.argv;
const atlas = decodeURIComponent(new URL('../../animations/monsters/cave_god/cave_god.atlas', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'));
const data = load(json, atlas);
const has = name => !!data.findAnimation(name);
const TRACKED = ['body1', 'body2', 'body3', 'head', 'beard', 'horn1', 'horn2', 'shoulder1', 'shoulder2', 'arm1_1', 'arm1_2',
  'arm1_3', 'fist1', 'arm2_1', 'arm2_2', 'arm2_3', 'fist2', 'player_grab'];
const HZ = 120;

const snap = s => Object.fromEntries(TRACKED.map(n => { const b = s.findBone(n); return [n, [b.worldX, b.worldY, Math.atan2(b.c, b.a)]]; }));
const gap = (a, b, skip = []) => {
  let worst = 0, at = '';
  for (const n of TRACKED) {
    if (skip.includes(n)) continue;
    const d = Math.hypot(a[n][0] - b[n][0], a[n][1] - b[n][1]);
    if (d > worst) { worst = d; at = n; }
  }
  return {px: +worst.toFixed(3), bone: at};
};
const at = (clip, t) => snap(pose(data, clip, t));

// Triangle orientation of every weighted/unweighted mesh, relative to the setup pose.
const setup = pose(data, null, 0);
const meshes = [];
for (const {slotIndex, name, attachment} of data.defaultSkin.getAttachments()) {
  if (!(attachment instanceof S.MeshAttachment)) continue;
  const base = worldVertices(setup.slots[slotIndex], attachment);
  const tris = attachment.triangles;
  const sign = [];
  for (let i = 0; i < tris.length; i += 3) sign.push(area(base, tris[i], tris[i + 1], tris[i + 2]));
  meshes.push({slotIndex, name, attachment, sign});
}
function area(v, a, b, c) {
  return ((v[2 * b] - v[2 * a]) * (v[2 * c + 1] - v[2 * a + 1]) - (v[2 * c] - v[2 * a]) * (v[2 * b + 1] - v[2 * a + 1])) / 2;
}
function meshHealth(s) {
  let flips = 0, crushed = 0, worst = '';
  const per = {};
  for (const m of meshes) {
    // Only the attachment actually shown in the slot can be seen.
    if (s.slots[m.slotIndex].getAttachment() !== m.attachment) continue;
    const v = worldVertices(s.slots[m.slotIndex], m.attachment);
    const tris = m.attachment.triangles;
    for (let i = 0, k = 0; i < tris.length; i += 3, k++) {
      const a0 = m.sign[k], a1 = area(v, tris[i], tris[i + 1], tris[i + 2]);
      if (Math.abs(a0) < 20) continue;
      if (Math.sign(a0) !== Math.sign(a1)) { flips++; worst = m.name; per[m.name] = (per[m.name] ?? 0) + 1; }
      else if (Math.abs(a1) < Math.abs(a0) * 0.25) { crushed++; worst = worst || m.name; }
    }
  }
  const bend = ['arm1_3', 'arm2_3'].map(n => s.findBone(n).rotation.toFixed(0)).join('/');
  return {flips, crushed, worst: Object.keys(per).length ? `${JSON.stringify(per)} wrist ${bend}` : worst};
}

const report = {};
const names = data.animations.map(a => a.name).filter(n => !/^(arrive|earthquake|main_01|main_happy|walk|leftpunch|rightpunch)/.test(n));
for (const name of names) {
  const anim = data.findAnimation(name);
  const angry = name.endsWith('_angry') || name === 'main_angry';
  const right = name.includes('_right');
  const idle = angry ? 'idle_front_angry' : 'idle_front';
  const weakIdle = 'weak_idle' + (right ? '_right' : '') + (angry ? '_angry' : '');
  const entry = {duration: anim.duration};
  if (/^(idle_front|weak_idle|grab_hold)/.test(name)) entry.loop = gap(at(name, 0), at(name, anim.duration));
  else if (/^weak_(enter|attack)/.test(name)) entry.end = gap(at(name, anim.duration), at(weakIdle, 0));
  else if (/^hit_recoil/.test(name)) entry.end = null;
  else if (/^hide/.test(name)) entry.end = null;
  else entry.end = gap(at(name, anim.duration), at(idle, 0));
  if (/^weak_(attack|recover)/.test(name)) entry.start = gap(at(name, 0), at(weakIdle, 0));
  else if (name === 'main_angry') entry.start = gap(at(name, 0), at('idle_front', 0));
  else if (/^grab_slam/.test(name)) {
    const grab = name.replace('grab_slam', 'grab_player'), hold = name.replace('grab_slam', 'grab_hold');
    entry.start = gap(at(name, 0), at(grab, 2.6));
    if (has(hold)) entry.hold = gap(at(hold, 0), at(grab, 2.6));
  } else if (!/^(hit_recoil|weak_idle|idle_front|grab_hold)/.test(name)) entry.start = gap(at(name, 0), at(idle, 0));
  // Frame-to-frame continuity and mesh health.
  let prev, prevStep = {}, maxStep = 0, maxJerk = 0, jerkAt = 0, jerkBone = '', flips = 0, crushed = 0, meshAt = '';
  const steps = Math.ceil(anim.duration * HZ);
  for (let i = 0; i <= steps; i++) {
    const t = Math.min(anim.duration, i / HZ), s = pose(data, name, t), cur = snap(s);
    if (prev) for (const n of TRACKED) {
      if (n === 'player_grab') continue;
      const dx = cur[n][0] - prev[n][0], dy = cur[n][1] - prev[n][1], step = Math.hypot(dx, dy);
      maxStep = Math.max(maxStep, step);
      if (prevStep[n]) {
        const j = Math.hypot(dx - prevStep[n][0], dy - prevStep[n][1]);
        if (j > maxJerk) { maxJerk = j; jerkAt = t; jerkBone = n; }
      }
      prevStep[n] = [dx, dy];
    }
    if (i % 4 === 0) {
      const h = meshHealth(s);
      if (h.flips > flips || (h.flips === flips && h.crushed > crushed)) { flips = h.flips; crushed = h.crushed; meshAt = `${h.worst}@${t.toFixed(2)}`; }
    }
    prev = cur;
  }
  Object.assign(entry, {maxStep: +maxStep.toFixed(2), maxJerk: +maxJerk.toFixed(2), jerkAt: +jerkAt.toFixed(3), jerkBone, flips, crushed, meshAt});
  report[name] = entry;
  const fmt = v => v ? `${v.px}px(${v.bone})` : '-';
  console.log(`${name.padEnd(28)} start ${fmt(entry.start).padEnd(22)} end ${fmt(entry.end || entry.loop).padEnd(22)} hold ${fmt(entry.hold).padEnd(16)} step ${entry.maxStep.toFixed(1).padStart(5)} jerk ${entry.maxJerk.toFixed(2).padStart(5)}@${entry.jerkAt}(${jerkBone}) mesh flips ${flips} crushed ${crushed} ${meshAt}`);
}
if (out) fs.writeFileSync(out, JSON.stringify(report, null, 1));
