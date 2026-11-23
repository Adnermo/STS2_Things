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
const names = data.animations.map(a => a.name).filter(n => !/^(arrive|earthquake|main_01|main_happy|walk)/.test(n));
for (const name of names) {
  const anim = data.findAnimation(name);
  const angry = name.endsWith('_angry') || name === 'main_angry';
  const right = name.includes('_right') || name.startsWith('right');
  const idle = angry ? 'idle_front_angry' : 'idle_front';
  const weakIdle = 'weak_idle' + (right ? '_right' : '') + (angry ? '_angry' : '');
  const entry = {duration: anim.duration};
  if (/^(idle_front|weak_idle|grab_hold|critical_tremor)/.test(name)) entry.loop = gap(at(name, 0), at(name, anim.duration));
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
  } else if (!/^(hit_recoil|critical_tremor|weak_idle|idle_front|grab_hold)/.test(name)) entry.start = gap(at(name, 0), at(idle, 0));
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

// ----------------------------------------------------------------------------
// Additive-overlay invariants & Spine 4.2 MixBlend.add drift verification
// ----------------------------------------------------------------------------
const ADDITIVE_OVERLAYS = ['hit_recoil', 'hit_recoil_angry', 'critical_tremor'];
const raw = JSON.parse(fs.readFileSync(json, 'utf8'));
const invariantErrors = [];
const SPINE_PROP_NAMES = ['rotate', 'x', 'y', 'scaleX', 'scaleY', 'shearX', 'shearY', 'inherit'];
const propLabel = pid => {
  const [prop, idx] = String(pid).split('|');
  const boneName = data.bones[Number(idx)]?.name ?? `bone#${idx}`;
  const propName = SPINE_PROP_NAMES[Number(prop)] ?? `prop#${prop}`;
  return `${boneName}.${propName}`;
};

const overlayPropIds = new Set();
for (const ovName of ADDITIVE_OVERLAYS) {
  const rawAnim = raw.animations?.[ovName];
  const spAnim = data.findAnimation(ovName);
  if (!rawAnim || !spAnim) {
    invariantErrors.push(`Missing additive overlay animation '${ovName}'.`);
    continue;
  }
  if (rawAnim.events && rawAnim.events.length > 0)
    invariantErrors.push(`${ovName}: additive overlay must have zero events, found ${rawAnim.events.length}.`);
  if (rawAnim.bones?.hand1_contact_rotation || rawAnim.bones?.hand2_contact_rotation)
    invariantErrors.push(`${ovName}: additive overlay must NOT key hand1_contact_rotation or hand2_contact_rotation.`);
  for (const forbidden of ['ik', 'transform', 'drawOrder']) {
    if (rawAnim[forbidden] && Object.keys(rawAnim[forbidden]).length > 0)
      invariantErrors.push(`${ovName}: additive overlay must NOT contain '${forbidden}'.`);
  }
  if (rawAnim.slots && Object.keys(rawAnim.slots).length > 0)
    invariantErrors.push(`${ovName}: additive overlay must NOT contain slot timelines.`);
  for (const tl of spAnim.timelines) {
    for (const pid of tl.getPropertyIds()) {
      if (String(pid).includes('|') && Number(String(pid).split('|')[0]) <= 7) overlayPropIds.add(pid);
    }
  }
  for (const [label, t] of [['start', 0], ['end', spAnim.duration]]) {
    const s = pose(data, ovName, t);
    for (const b of s.bones) {
      const dRot = Math.abs(b.rotation - b.data.rotation);
      const dX = Math.abs(b.x - b.data.x);
      const dY = Math.abs(b.y - b.data.y);
      const dSx = Math.abs(b.scaleX - b.data.scaleX);
      const dSy = Math.abs(b.scaleY - b.data.scaleY);
      if (dRot > 0.02 || dX > 0.05 || dY > 0.05 || dSx > 0.002 || dSy > 0.002) {
        invariantErrors.push(`${ovName} ${label} (${t}s) bone '${b.data.name}' not identity: dRot=${dRot.toFixed(4)} dX=${dX.toFixed(4)} dY=${dY.toFixed(4)} dS=(${dSx.toFixed(4)},${dSy.toFixed(4)})`);
      }
    }
  }
}

const overlaySet = new Set(ADDITIVE_OVERLAYS);
let checkedTrack0Clips = 0;
for (const anim of data.animations) {
  if (overlaySet.has(anim.name)) continue;
  checkedTrack0Clips++;
  const covered = new Map();
  for (const tl of anim.timelines) {
    const startT = tl.frames && tl.frames.length > 0 ? tl.frames[0] : 0;
    for (const pid of tl.getPropertyIds()) {
      const prev = covered.get(pid);
      if (prev === undefined || startT < prev) covered.set(pid, startT);
    }
  }
  const missing = [];
  const late = [];
  for (const pid of overlayPropIds) {
    if (!covered.has(pid)) missing.push(propLabel(pid));
    else if (covered.get(pid) > 1e-4) late.push(`${propLabel(pid)}@${covered.get(pid).toFixed(4)}s`);
  }
  if (missing.length) invariantErrors.push(`${anim.name}: missing overlay-protected channels: ${missing.join(', ')}`);
  if (late.length) invariantErrors.push(`${anim.name}: overlay-protected channels start after t=0: ${late.join(', ')}`);
}

// Multi-track MixBlend.add stability checks against Track-0-only reference.
const simScenarios = [
  {name: 'idle_angry+tremor+recoil', track0: 'idle_front_angry', loop0: true, tremor: true, recoil: 'hit_recoil_angry', frames: 300, recoilEvery: 30},
  {name: 'idle+recoil', track0: 'idle_front', loop0: true, tremor: false, recoil: 'hit_recoil', frames: 240, recoilEvery: 30},
  {name: 'central_slam_angry+tremor+recoil', track0: 'central_slam_angry', follow0: 'idle_front_angry', loop0: false, tremor: true, recoil: 'hit_recoil_angry', frames: 270, recoilAt: [40, 110, 175]},
  {name: 'double_fist_crush_angry+tremor+recoil', track0: 'double_fist_crush_angry', follow0: 'idle_front_angry', loop0: false, tremor: true, recoil: 'hit_recoil_angry', frames: 240, recoilAt: [35, 95, 155]},
];
const checkBones = ['shoulder1', 'shoulder2', 'body1', 'arm1_1', 'arm2_1', 'hand1_contact_rotation', 'hand2_contact_rotation'];
const simSummary = {};
for (const sc of simScenarios) {
  const sd = new S.AnimationStateData(data);
  sd.defaultMix = 0.05;
  const skRef = new S.Skeleton(data), stRef = new S.AnimationState(sd);
  const skAdd = new S.Skeleton(data), stAdd = new S.AnimationState(sd);
  stRef.setAnimation(0, sc.track0, sc.loop0);
  stAdd.setAnimation(0, sc.track0, sc.loop0);
  if (sc.follow0) {
    stRef.addAnimation(0, sc.follow0, true, 0);
    stAdd.addAnimation(0, sc.follow0, true, 0);
  }
  if (sc.tremor) {
    const t2 = stAdd.setAnimation(2, 'critical_tremor', true);
    t2.mixBlend = S.MixBlend.add;
    t2.alpha = 1;
  }
  let maxRotDelta = 0, maxHandRotDelta = 0, maxArmPosDelta = 0, settleRotDelta = 0, settleArmPosDelta = 0;
  const totalFrames = sc.frames + 60;
  for (let f = 0; f <= totalFrames; f++) {
    if (f < sc.frames && ((sc.recoilEvery && f % sc.recoilEvery === 10) || (sc.recoilAt && sc.recoilAt.includes(f)))) {
      const t1 = stAdd.setAnimation(1, sc.recoil, false);
      t1.mixBlend = S.MixBlend.add;
      t1.alpha = 1;
      t1.mixDuration = 0.15;
      stAdd.addEmptyAnimation(1, 0.12, 0);
    }
    if (f === sc.frames) {
      stAdd.setEmptyAnimation(1, 0.1);
      if (sc.tremor) stAdd.setEmptyAnimation(2, 0.2);
    }
    stRef.update(1 / 60); stRef.apply(skRef); skRef.updateWorldTransform(S.Physics.update);
    stAdd.update(1 / 60); stAdd.apply(skAdd); skAdd.updateWorldTransform(S.Physics.update);
    for (const bn of checkBones) {
      const a = skRef.findBone(bn), b = skAdd.findBone(bn);
      if (!a || !b) continue;
      const dRot = Math.abs(b.rotation - a.rotation);
      const dPos = Math.hypot(b.worldX - a.worldX, b.worldY - a.worldY);
      if (bn.endsWith('_contact_rotation')) maxHandRotDelta = Math.max(maxHandRotDelta, dRot);
      else if (bn === 'shoulder1' || bn === 'shoulder2' || bn === 'body1') {
        maxRotDelta = Math.max(maxRotDelta, dRot);
        if (f === totalFrames) settleRotDelta = Math.max(settleRotDelta, dRot);
      } else {
        maxArmPosDelta = Math.max(maxArmPosDelta, dPos);
        if (f === totalFrames) settleArmPosDelta = Math.max(settleArmPosDelta, dPos);
      }
    }
  }
  simSummary[sc.name] = {
    maxRotDelta: +maxRotDelta.toFixed(3),
    maxHandRotDelta: +maxHandRotDelta.toFixed(4),
    maxArmPosDelta: +maxArmPosDelta.toFixed(2),
    settleRotDelta: +settleRotDelta.toFixed(4),
    settleArmPosDelta: +settleArmPosDelta.toFixed(3),
  };
  if (maxHandRotDelta > 0.01) invariantErrors.push(`${sc.name}: hand contact rotation drifted by ${maxHandRotDelta.toFixed(3)}° (limit 0.01°).`);
  if (maxRotDelta > 6.0) invariantErrors.push(`${sc.name}: shoulder/body1 rotation delta reached ${maxRotDelta.toFixed(3)}° (limit 6.0°).`);
  if (maxArmPosDelta > 25.0) invariantErrors.push(`${sc.name}: arm1_1/arm2_1 world drift reached ${maxArmPosDelta.toFixed(2)}px (limit 25px).`);
  if (settleRotDelta > 0.05 || settleArmPosDelta > 0.5) invariantErrors.push(`${sc.name}: residual drift after clearing overlays: rot=${settleRotDelta.toFixed(4)}° pos=${settleArmPosDelta.toFixed(3)}px.`);
}

report._additiveInvariants = {
  overlayCount: ADDITIVE_OVERLAYS.length,
  protectedChannelCount: overlayPropIds.size,
  checkedTrack0Clips,
  simulations: simSummary,
  errors: invariantErrors,
};
if (out) fs.writeFileSync(out, JSON.stringify(report, null, 1));
if (invariantErrors.length) {
  for (const err of invariantErrors) console.error(`ADDITIVE_INVARIANT_FAIL: ${err}`);
  process.exit(1);
}
console.log(`ADDITIVE_OVERLAY_INVARIANTS: PASS (${overlayPropIds.size} protected channels across ${checkedTrack0Clips} non-additive clips, ${simScenarios.length} MixBlend.add stability sims)`);
for (const [k, v] of Object.entries(simSummary)) {
  console.log(`  sim ${k.padEnd(40)} maxRot=${v.maxRotDelta}° handRot=${v.maxHandRotDelta}° armPos=${v.maxArmPosDelta}px settle=(${v.settleRotDelta}°, ${v.settleArmPosDelta}px)`);
}
