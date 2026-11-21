import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as S from '../../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';
import { load, pose, worldVertices } from '../runtime.mjs';

const dir = process.argv[2] || 'pkg';
const data = load(`${dir}/chromatic_slime_large.spjson`, `${dir}/chromatic_slime_large.atlas`);
const raw = JSON.parse(fs.readFileSync(`${dir}/chromatic_slime_large.spjson`, 'utf8'));

const names = ['idle_loop', 'attack', 'cast', 'power_up', 'summon', 'hurt', 'die', 'revive'];
assert.deepEqual(data.animations.map((a) => a.name).sort(), names.toSorted());
assert(data.bones.length >= 37, `Expected >= 37 bones, got ${data.bones.length}`);

// Separate hardware clipping slots from mesh slots dynamically
const clipSlots = [];
const meshSlots = [];
for (let i = 0; i < data.slots.length; i++) {
  const sName = data.slots[i].name;
  const att = data.defaultSkin.getAttachment(i, sName);
  assert(att, `Missing default attachment for slot: ${sName}`);
  if (att instanceof S.ClippingAttachment) {
    clipSlots.push(sName);
  } else if (att instanceof S.MeshAttachment) {
    meshSlots.push(sName);
  }
}

assert.deepEqual(clipSlots, ['eye_clip_main', 'eye_clip_up', 'eye_clip_rt']);
for (const ps of ['proj_crimson', 'proj_amber', 'proj_azure']) {
  assert(meshSlots.includes(ps), `Missing projectile mesh slot: ${ps}`);
}

// Verify hardware eye clipping end-slots & runtime SkeletonClipping triangulation across frames
const expectedClipEnd = {
  eye_clip_main: 'lid_main',
  eye_clip_up: 'lid_up',
  eye_clip_rt: 'lid_rt',
};
const clipper = new S.SkeletonClipping();
for (const cs of clipSlots) {
  const sIdx = data.slots.findIndex((s) => s.name === cs);
  const clipAtt = data.defaultSkin.getAttachment(sIdx, cs);
  assert.equal(clipAtt.endSlot.name, expectedClipEnd[cs], `Wrong endSlot on ${cs}`);
}
for (const anim of data.animations) {
  for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 20) {
    const sk = pose(data, anim.name, Math.min(t, anim.duration));
    for (const cs of clipSlots) {
      const sl = sk.findSlot(cs);
      const count = clipper.clipStart(sl, sl.attachment);
      assert(count > 0, `SkeletonClipping failed on ${cs} in ${anim.name} @ t=${t.toFixed(2)}`);
      clipper.clipEnd();
    }
  }
}

const setupSk = pose(data, null, 0);
const vertices = (slot, animation, t) => worldVertices(pose(data, animation, t).findSlot(slot));
const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

const loopErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'idle_loop', 0), vertices(s, 'idle_loop', 4.0))));
const reviveErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'die', 1.10), vertices(s, 'revive', 0))));
assert(loopErr < 0.05, `Idle loop seam: ${loopErr}`);
assert(reviveErr < 0.05, `Die/revive seam: ${reviveErr}`);

const setup = Object.fromEntries(meshSlots.map((s) => [s, vertices(s, null, 0)]));
for (const anim of data.animations) {
  if (anim.name === 'die') continue;
  const err = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, anim.name, anim.duration), setup[s])));
  assert(err < 0.2, `${anim.name} return-to-rest error: ${err}`);
}

// Verify STRICT ZERO bone scale-stretching AND ZERO body/lobe tilt-rocking + horizontal floor lock!
const noTiltBodyBones = [
  'body_base',
  'mid_torus',
  'belly_sag',
  'puddle_l_foot',
  'puddle_c_foot',
  'puddle_r_foot',
  'flank_l',
  'flank_r',
  'brow_crimson',
  'hump_amber',
  'lobe_azure',
  'amber_goo_b',
  'amber_goo_tip',
];
const floorLockedBones = ['body_base', 'puddle_l_foot', 'puddle_c_foot', 'puddle_r_foot'];
const setupRot = Object.fromEntries(noTiltBodyBones.map((bn) => [bn, setupSk.findBone(bn).rotation]));
const setupFloorY = Object.fromEntries(floorLockedBones.map((bn) => [bn, setupSk.findBone(bn).worldY]));

let maxBoneScaleDev = 0;
let maxBodyRotDeg = 0;
let maxFloorLiftPx = 0;
for (const anim of data.animations) {
  for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
    const sk = pose(data, anim.name, Math.min(t, anim.duration));
    for (const b of sk.bones) {
      maxBoneScaleDev = Math.max(maxBoneScaleDev, Math.abs(b.scaleX - 1.0), Math.abs(b.scaleY - 1.0));
    }
    for (const bn of noTiltBodyBones) {
      const rotDev = Math.abs(sk.findBone(bn).rotation - setupRot[bn]);
      assert(rotDev < 0.01, `Body tilt detected on ${bn} in ${anim.name} @ t=${t.toFixed(2)}: ${rotDev}`);
      maxBodyRotDeg = Math.max(maxBodyRotDeg, rotDev);
    }
    for (const bn of floorLockedBones) {
      const yDev = Math.abs(sk.findBone(bn).worldY - setupFloorY[bn]);
      assert(yDev < 0.05, `Floor sole lifted on ${bn} in ${anim.name} @ t=${t.toFixed(2)}: ${yDev}`);
      maxFloorLiftPx = Math.max(maxFloorLiftPx, yDev);
    }
  }
}
assert.equal(maxBoneScaleDev, 0, `Bone scale-stretching detected: ${maxBoneScaleDev}`);

// Verify Attack: Peristaltic Gather -> Floor-Hugging Tidal Surge & Vertical Anvil Crush -> 5-Frame Hit-Stop!
const dWorldX = (anim, bn, t) => pose(data, anim, t).findBone(bn).worldX - setupSk.findBone(bn).worldX;
const dWorldY = (anim, bn, t) => pose(data, anim, t).findBone(bn).worldY - setupSk.findBone(bn).worldY;

assert(dWorldX('attack', 'puddle_l_foot', 0.21) >= 42, `Attack front gather too weak: ${dWorldX('attack', 'puddle_l_foot', 0.21)}`);
assert(dWorldY('attack', 'hump_amber', 0.21) >= 54, `Attack vertical gather wall too low: ${dWorldY('attack', 'hump_amber', 0.21)}`);
assert(dWorldX('attack', 'puddle_l_foot', 0.45) <= -160, `Attack leading tidal surge too weak: ${dWorldX('attack', 'puddle_l_foot', 0.45)}`);
assert(dWorldX('attack', 'puddle_r_foot', 0.50) <= -60, `Attack trailing puddle wave too weak: ${dWorldX('attack', 'puddle_r_foot', 0.50)}`);
const anvilDrop = dWorldY('attack', 'hump_amber', 0.21) - dWorldY('attack', 'hump_amber', 0.45);
assert(anvilDrop >= 74, `Attack vertical anvil crush too small: ${anvilDrop}`);
assert(
  Math.abs(dWorldX('attack', 'puddle_l_foot', 0.50) - dWorldX('attack', 'puddle_l_foot', 0.44)) < 1.5,
  'Attack lacks 5-frame seismic Hit-Stop plateau on leading foot',
);
assert(dWorldY('attack', 'drop_near_b', 0.54) > 22, `Floor droplet did not bounce on attack hit-stop`);

// Verify Cast: 100% Planted Base + Tall Volcanic Boil + Real 3-Glob Parabolic Barrage!
for (let t = 0; t <= 1.20 + 1e-6; t += 1 / 60) {
  assert(Math.abs(dWorldX('cast', 'body_base', Math.min(t, 1.20))) < 0.01, 'Cast body_base must stay planted');
}
assert(dWorldY('cast', 'hump_amber', 0.40) >= 50, `Cast volcanic geyser boil too low: ${dWorldY('cast', 'hump_amber', 0.40)}`);

for (const [slotName, boneName] of [
  ['proj_crimson', 'proj_c_b'],
  ['proj_amber', 'proj_a_b'],
  ['proj_azure', 'proj_z_b'],
]) {
  assert.equal(setupSk.findSlot(slotName).color.a, 0, `${slotName} must have setup alpha == 0`);
  assert.equal(pose(data, 'idle_loop', 1.0).findSlot(slotName).color.a, 0, `${slotName} must stay invisible in idle`);
  assert(pose(data, 'cast', 1.20).findSlot(slotName).color.a < 0.005, `${slotName} must fade out by cast end`);
  let minDx = 0;
  let peakAlpha = 0;
  for (let t = 0.46; t <= 0.98; t += 1 / 60) {
    const sk = pose(data, 'cast', t);
    const a = sk.findSlot(slotName).color.a;
    peakAlpha = Math.max(peakAlpha, a);
    if (a > 0.05) {
      minDx = Math.min(minDx, sk.findBone(boneName).worldX - setupSk.findBone(boneName).worldX);
    }
  }
  assert(peakAlpha > 0.9, `${slotName} did not appear in cast (peakAlpha=${peakAlpha})`);
  assert(minDx <= -550, `${slotName} parabolic flight too short in cast (minDx=${minDx})`);
}

// Full triangle non-inversion audit across ALL mesh slots!
function area(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((k) => k * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}
let flipped = 0;
let maxDisp = 0;
for (const slot of meshSlots) {
  const sIdx = data.slots.findIndex((s) => s.name === slot);
  const tris = data.defaultSkin.getAttachment(sIdx, slot).triangles;
  const base = Array.from({ length: tris.length / 3 }, (_, i) => area(setup[slot], i * 3, tris));
  for (const anim of data.animations) {
    for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
      const v = vertices(slot, anim.name, Math.min(t, anim.duration));
      for (let i = 0; i < base.length; i++) {
        if (area(v, i * 3, tris) * base[i] < -0.01) flipped++;
      }
      if (slot === 'body_dome') {
        for (let i = 0; i < v.length; i++) maxDisp = Math.max(maxDisp, Math.abs(v[i] - setup.body_dome[i]));
      }
    }
  }
}
assert.equal(flipped, 0, `Inverted triangles in Large: ${flipped}`);
assert(maxDisp > 150, `Displacement too small: ${maxDisp}`);

// Verify exact C# event contracts
assert.equal(raw.animations.attack.events[0].name, 'attack_contact');
assert.equal(raw.animations.attack.events[0].time, 0.44);
assert.equal(raw.animations.cast.events[0].name, 'cast_release');
assert.equal(raw.animations.cast.events[0].time, 0.56);
assert.equal(raw.animations.power_up.events[0].name, 'power_peak');
assert.equal(raw.animations.power_up.events[0].time, 0.48);
assert.equal(raw.animations.summon.events[0].name, 'summon_pulse');
assert.equal(raw.animations.summon.events[0].time, 0.52);
assert.equal(raw.animations.die.events[0].name, 'rupture_burst');
assert.equal(raw.animations.die.events[0].time, 0.36);

const report = {
  result: 'PASS',
  skeleton: 'chromatic_slime_large',
  bones: data.bones.length,
  slots: data.slots.length,
  clipSlots: clipSlots.length,
  meshSlots: meshSlots.length,
  loopErrPx: Math.round(loopErr * 1e5) / 1e5,
  reviveErrPx: Math.round(reviveErr * 1e5) / 1e5,
  maxBoneScaleDev: Math.round(maxBoneScaleDev * 1e6) / 1e6,
  maxBodyRotDeg: Math.round(maxBodyRotDeg * 1e6) / 1e6,
  maxFloorLiftPx: Math.round(maxFloorLiftPx * 1e5) / 1e5,
  invertedTriangles: flipped,
  maxDisplacementPx: Math.round(maxDisp * 10) / 10,
};
fs.writeFileSync(`${dir}/verification.json`, JSON.stringify(report, null, 2));
console.log(report);
