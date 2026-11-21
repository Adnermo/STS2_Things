import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as S from '../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';
import { load, pose, worldVertices } from './runtime.mjs';

const pkgDir = process.argv[2] || 'pkg/soul_roes';
const data = load(`${pkgDir}/soul_roes.spjson`, `${pkgDir}/soul_roes.atlas`);
const raw = JSON.parse(fs.readFileSync(`${pkgDir}/soul_roes.spjson`, 'utf8'));

// 1. Exact 10-clip contract
const expectedClips = [
  'idle_loop',
  'attack',
  'cast',
  'power_up',
  'summon',
  'hurt',
  'die',
  'revive',
  'multi_bite_6',
  'multi_bite_4_intangible',
];
assert.deepEqual(data.animations.map((a) => a.name).sort(), expectedClips.toSorted());

// 2. Verify all 35 mesh slots & default attachments
const meshSlots = [];
for (let i = 0; i < data.slots.length; i++) {
  const sName = data.slots[i].name;
  const att = data.defaultSkin.getAttachment(i, sName);
  assert(att, `Missing default attachment for slot: ${sName}`);
  if (att instanceof S.MeshAttachment) {
    meshSlots.push(sName);
  }
}
for (let i = 0; i < 6; i++) {
  assert(meshSlots.includes(`brood_${i}`), `Missing brood_${i}`);
  assert(meshSlots.includes(`sat_${i}`), `Missing sat_${i}`);
  assert(meshSlots.includes(`brood_glow_${i}`), `Missing brood_glow_${i}`);
  assert(meshSlots.includes(`sat_glow_${i}`), `Missing sat_glow_${i}`);
  assert(meshSlots.includes(`trail_${i}`), `Missing trail_${i}`);
}
for (const layer of ['beckon_aura', 'beckon_ring', 'intangible_aura', 'intangible_shimmer']) {
  assert(meshSlots.includes(layer), `Missing illumination layer: ${layer}`);
}

const setupSk = pose(data, null, 0);
for (const hiddenSlot of [
  'beckon_aura',
  'beckon_ring',
  'intangible_aura',
  'intangible_shimmer',
  ...Array.from({ length: 6 }, (_, i) => `trail_${i}`),
  ...Array.from({ length: 6 }, (_, i) => `sat_glow_${i}`),
]) {
  assert.equal(setupSk.findSlot(hiddenSlot).color.a, 0, `${hiddenSlot} setup alpha must be 0`);
}

// 3. Verify loop seam, revive seam, and return-to-rest across all non-die clips
const vertices = (slot, animation, t) => worldVertices(pose(data, animation, t).findSlot(slot));
const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

const loopErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'idle_loop', 0), vertices(s, 'idle_loop', 3.60))));
const reviveErr = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, 'die', 1.12), vertices(s, 'revive', 0))));
assert(loopErr < 0.02, `Idle loop seam too high: ${loopErr}`);
assert(reviveErr < 0.05, `Die/revive seam too high: ${reviveErr}`);

const setupVerts = Object.fromEntries(meshSlots.map((s) => [s, vertices(s, null, 0)]));
let maxReturnToRestErr = 0;
for (const anim of data.animations) {
  if (anim.name === 'die') continue;
  const err = Math.max(...meshSlots.map((s) => maxDiff(vertices(s, anim.name, anim.duration), setupVerts[s])));
  assert(err < 0.05, `${anim.name} return-to-rest error: ${err}`);
  maxReturnToRestErr = Math.max(maxReturnToRestErr, err);
}

// 4. Strict Geometric Invariants @ 60 FPS: maxBoneScaleDev === 0, maxOrbRotDeg === 0, invertedTriangles === 0!
const orbAndHubBones = [
  'cluster_hub',
  ...Array.from({ length: 6 }, (_, i) => `brood_${i}`),
  ...Array.from({ length: 6 }, (_, i) => `sat_${i}`),
];
const setupRot = Object.fromEntries(orbAndHubBones.map((bn) => [bn, setupSk.findBone(bn).rotation]));

let maxBoneScaleDev = 0;
let maxOrbRotDeg = 0;
for (const anim of data.animations) {
  const steps = Math.ceil(anim.duration * 60);
  for (let k = 0; k <= steps; k++) {
    const t = Math.min(k / 60, anim.duration);
    const sk = pose(data, anim.name, t);
    for (const b of sk.bones) {
      maxBoneScaleDev = Math.max(maxBoneScaleDev, Math.abs(b.scaleX - 1.0), Math.abs(b.scaleY - 1.0));
    }
    for (const bn of orbAndHubBones) {
      const rotDev = Math.abs(sk.findBone(bn).rotation - setupRot[bn]);
      maxOrbRotDeg = Math.max(maxOrbRotDeg, rotDev);
    }
  }
}
assert.equal(maxBoneScaleDev, 0, `Bone scale deviation detected: ${maxBoneScaleDev}`);
assert.equal(maxOrbRotDeg, 0, `Orb/hub rotation detected: ${maxOrbRotDeg}`);

function triArea(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((idx) => idx * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}
let invertedTriangles = 0;
for (const slot of meshSlots) {
  const sIdx = data.slots.findIndex((s) => s.name === slot);
  const tris = data.defaultSkin.getAttachment(sIdx, slot).triangles;
  const base = Array.from({ length: tris.length / 3 }, (_, i) => triArea(setupVerts[slot], i * 3, tris));
  for (const anim of data.animations) {
    const steps = Math.ceil(anim.duration * 60);
    for (let k = 0; k <= steps; k++) {
      const t = Math.min(k / 60, anim.duration);
      const v = vertices(slot, anim.name, t);
      for (let i = 0; i < base.length; i++) {
        if (triArea(v, i * 3, tris) * base[i] < -0.01) invertedTriangles++;
      }
    }
  }
}
assert.equal(invertedTriangles, 0, `Inverted triangles detected: ${invertedTriangles}`);

// 5. Verify Exact C# Event Names & Timestamps
const evMap = (animName) => raw.animations[animName].events || [];

const mb6Events = evMap('multi_bite_6');
const bite6Contacts = mb6Events.filter((e) => e.name === 'bite6_contact').map((e) => e.time);
assert.deepEqual(bite6Contacts, [0.34, 0.54, 0.74, 0.94, 1.14, 1.34]);
for (const [rName, rTime] of [
  ['bite6_ready_2', 0.42],
  ['bite6_ready_3', 0.62],
  ['bite6_ready_4', 0.82],
  ['bite6_ready_5', 1.02],
  ['bite6_ready_6', 1.22],
]) {
  assert(mb6Events.some((e) => e.name === rName && e.time === rTime), `Missing ${rName} @ ${rTime}`);
}

const mb4Events = evMap('multi_bite_4_intangible');
const bite4Contacts = mb4Events.filter((e) => e.name === 'bite4_contact').map((e) => e.time);
assert.deepEqual(bite4Contacts, [0.34, 0.58, 0.82, 1.06]);
for (const [rName, rTime] of [
  ['bite4_ready_2', 0.44],
  ['bite4_ready_3', 0.68],
  ['bite4_ready_4', 0.92],
  ['intangible_peak', 1.38],
]) {
  assert(mb4Events.some((e) => e.name === rName && e.time === rTime), `Missing ${rName} @ ${rTime}`);
}

assert.deepEqual(evMap('attack'), [{ time: 0.42, name: 'attack_contact' }]);
assert.deepEqual(evMap('cast'), [{ time: 0.58, name: 'cast_release' }]);
assert.deepEqual(evMap('power_up'), [{ time: 0.52, name: 'power_peak' }]);
assert.deepEqual(evMap('summon'), [{ time: 0.64, name: 'summon_release' }]);
assert.deepEqual(evMap('hurt'), []);
assert.deepEqual(evMap('die'), [{ time: 0.42, name: 'rupture_burst' }]);
assert.deepEqual(evMap('revive'), []);

// 6. Verify Biomechanical & Bioluminescent Assertions
const dWorldX = (anim, bn, t) => pose(data, anim, t).findBone(bn).worldX - setupSk.findBone(bn).worldX;
const dWorldY = (anim, bn, t) => pose(data, anim, t).findBone(bn).worldY - setupSk.findBone(bn).worldY;

// Assert each sat_0..5 travels <= -500px left right at its corresponding bite6_contact[k] timestamp in multi_bite_6!
const bite6HitDx = [];
for (let k = 0; k < 6; k++) {
  const tHit = bite6Contacts[k];
  const dx = dWorldX('multi_bite_6', `sat_${k}`, tHit);
  const trAlpha = pose(data, 'multi_bite_6', tHit).findSlot(`trail_${k}`).color.a;
  assert(dx <= -500, `sat_${k} did not travel <= -500px left @ t=${tHit} in multi_bite_6 (got ${dx})`);
  assert(trAlpha > 0.9, `trail_${k} not blazing @ t=${tHit} in multi_bite_6 (alpha=${trAlpha})`);
  bite6HitDx.push(Math.round(dx * 10) / 10);
}

// Assert sat_0..3 travel <= -500px left @ bite4_contact[k] in multi_bite_4_intangible
const bite4HitDx = [];
for (let k = 0; k < 4; k++) {
  const tHit = bite4Contacts[k];
  const dx = dWorldX('multi_bite_4_intangible', `sat_${k}`, tHit);
  assert(dx <= -500, `sat_${k} did not travel <= -500px left @ t=${tHit} in multi_bite_4_intangible (got ${dx})`);
  bite4HitDx.push(Math.round(dx * 10) / 10);
}

// Assert attack 3-orb volley <= -500px @ 0.42
for (const k of [0, 2, 4]) {
  const dx = dWorldX('attack', `sat_${k}`, 0.42);
  assert(dx <= -500, `sat_${k} did not travel <= -500px left @ 0.42 in attack (got ${dx})`);
}

// Assert summon mitotic budding (sat_1 left <= -140px, sat_4 right >= +140px @ 0.64s)
assert(dWorldX('summon', 'sat_1', 0.64) <= -140, `summon sat_1 left bud too small: ${dWorldX('summon', 'sat_1', 0.64)}`);
assert(dWorldX('summon', 'sat_4', 0.64) >= 140, `summon sat_4 right bud too small: ${dWorldX('summon', 'sat_4', 0.64)}`);

// Assert die 6-way starburst (>= 210px radial travel + alpha dissolve -> 0 at 1.12s)
for (let k = 0; k < 6; k++) {
  const rDist = Math.hypot(dWorldX('die', `sat_${k}`, 1.12), dWorldY('die', `sat_${k}`, 1.12));
  assert(rDist >= 210, `die sat_${k} radial burst too small: ${rDist}`);
}
assert(pose(data, 'die', 1.12).findSlot('brood_0').color.a < 0.005, 'die must dissolve alpha to 0 by 1.12s');

// Assert peak aura alpha > 0.90 in multi_bite_4_intangible, cast, and power_up!
const peakSlotAlpha = (animName, slotName) => {
  const anim = data.findAnimation(animName);
  let peak = 0;
  const steps = Math.ceil(anim.duration * 60);
  for (let k = 0; k <= steps; k++) {
    peak = Math.max(peak, pose(data, animName, Math.min(k / 60, anim.duration)).findSlot(slotName).color.a);
  }
  return Math.round(peak * 1000) / 1000;
};

const mb4IntangibleAuraPeak = peakSlotAlpha('multi_bite_4_intangible', 'intangible_aura');
const castBeckonAuraPeak = peakSlotAlpha('cast', 'beckon_aura');
const powerUpIntangibleAuraPeak = peakSlotAlpha('power_up', 'intangible_aura');

assert(mb4IntangibleAuraPeak > 0.90, `multi_bite_4_intangible intangible_aura peak <= 0.90: ${mb4IntangibleAuraPeak}`);
assert(castBeckonAuraPeak > 0.90, `cast beckon_aura peak <= 0.90: ${castBeckonAuraPeak}`);
assert(powerUpIntangibleAuraPeak > 0.90, `power_up intangible_aura peak <= 0.90: ${powerUpIntangibleAuraPeak}`);

const report = {
  result: 'PASS',
  skeleton: 'soul_roes',
  originCanvas: [320, 440],
  recommendedTscn: { position: [0, 0], scale: [0.36, 0.36] },
  bones: data.bones.length,
  slots: data.slots.length,
  meshSlots: meshSlots.length,
  animations: data.animations.length,
  loopErrPx: Math.round(loopErr * 1e5) / 1e5,
  reviveErrPx: Math.round(reviveErr * 1e5) / 1e5,
  maxReturnToRestErrPx: Math.round(maxReturnToRestErr * 1e5) / 1e5,
  maxBoneScaleDev,
  maxOrbRotDeg,
  invertedTriangles,
  bite6ContactDxPx: bite6HitDx,
  bite4ContactDxPx: bite4HitDx,
  auraPeaks: {
    multi_bite_4_intangible_aura: mb4IntangibleAuraPeak,
    cast_beckon_aura: castBeckonAuraPeak,
    power_up_intangible_aura: powerUpIntangibleAuraPeak,
  },
};

fs.mkdirSync('out', { recursive: true });
fs.writeFileSync('out/verification.json', JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
