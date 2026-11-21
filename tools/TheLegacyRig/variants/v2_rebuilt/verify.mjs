// Official Spine 4.2 Runtime Verification for TheLegacy v2_rebuilt (60 Hz across all 8 clips)
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {load, pose, worldVertices} from '../../../ScaleBeetleRig/runtime.mjs';

const spjsonPath = 'pkg/the_legacy.spjson';
const atlasPath = 'pkg/the_legacy.atlas';
const raw = JSON.parse(fs.readFileSync(spjsonPath, 'utf8'));
const rigStatic = JSON.parse(fs.readFileSync('out/rig_static.json', 'utf8'));
const data = load(spjsonPath, atlasPath);

const EXPECTED_CLIPS = {
  idle_loop: 3.0,
  attack: 1.25,
  cast: 1.4,
  hurt: 0.65,
  die: 2.6,
  summon: 1.3,
  power_up: 1.5,
  revive: 1.8,
};

// 1. Validate Spine 4.2 load, 8 required clips, durations, and bone hierarchy ordering
assert.deepEqual(
  data.animations.map(a => a.name).sort(),
  Object.keys(EXPECTED_CLIPS).sort(),
  'Animation clip names mismatch'
);
for (const [name, dur] of Object.entries(EXPECTED_CLIPS)) {
  const anim = data.findAnimation(name);
  assert(Math.abs(anim.duration - dur) < 1e-4, `${name} duration ${anim.duration} != ${dur}`);
}
for (const bone of data.bones) {
  if (bone.parent) {
    assert(data.bones.indexOf(bone.parent) < data.bones.indexOf(bone), `Bone parent order broken: ${bone.name}`);
  }
}

// 2. Verify 0.000px mesh/weight identity between myocardium and all 3 coronary glow slots
const myoRaw = raw.skins[0].attachments.myocardium.myocardium;
for (const glowName of ['glow_sa_root', 'glow_av_bulb', 'glow_purkinje_apex']) {
  const glowRaw = raw.skins[0].attachments[glowName][glowName];
  assert.deepEqual(glowRaw.uvs, myoRaw.uvs, `${glowName} UVs differ from myocardium`);
  assert.deepEqual(glowRaw.triangles, myoRaw.triangles, `${glowName} triangles differ from myocardium`);
  assert.deepEqual(glowRaw.vertices, myoRaw.vertices, `${glowName} vertices/weights differ from myocardium`);
}

function allSlotVertices(sk) {
  return Object.fromEntries(sk.slots.map(slot => [slot.data.name, worldVertices(slot)]));
}

function maxDiff(va, vb) {
  let m = 0;
  for (const [k, arr] of Object.entries(va)) {
    const bArr = vb[k];
    for (let i = 0; i < arr.length; i++) {
      m = Math.max(m, Math.abs(arr[i] - bArr[i]));
    }
  }
  return m;
}

const setupSkel = pose(data, null, 0);
const setupVerts = allSlotVertices(setupSkel);

// 3. Continuity & Seam Checks
const idle0 = allSlotVertices(pose(data, 'idle_loop', 0));
const idleEnd = allSlotVertices(pose(data, 'idle_loop', 3.0));
const idleLoopErr = maxDiff(idle0, idleEnd);
assert(idleLoopErr < 0.3, `idle_loop(0) vs idle_loop(3.0) error ${idleLoopErr} >= 0.3px`);

const dieEnd = allSlotVertices(pose(data, 'die', 2.6));
const revStart = allSlotVertices(pose(data, 'revive', 0.0));
const dieReviveErr = maxDiff(dieEnd, revStart);
assert(dieReviveErr < 0.3, `die(2.6) vs revive(0) error ${dieReviveErr} >= 0.3px`);

const revEnd = allSlotVertices(pose(data, 'revive', 1.8));
const revToIdleErr = maxDiff(revEnd, idle0);
assert(revToIdleErr < 1.0, `revive(1.8) vs idle_loop(0) error ${revToIdleErr} >= 1.0px`);

const oneShotReturnErrors = {};
for (const name of ['attack', 'cast', 'hurt', 'summon', 'power_up']) {
  const endV = allSlotVertices(pose(data, name, EXPECTED_CLIPS[name]));
  const err = maxDiff(endV, idle0);
  oneShotReturnErrors[name] = Number(err.toFixed(5));
  assert(err < 1.0, `${name} end vs idle_loop(0) error ${err} >= 1.0px`);
}

// 4. Identify Bottom-most 5% of Organ Vertices & Ground-Weed Base Vertices
const ORGAN_SLOTS = ['dorsal_purple', 'venous_blue', 'myocardium'];
const organAllY = [];
for (const sName of ORGAN_SLOTS) {
  const v = setupVerts[sName];
  for (let i = 1; i < v.length; i += 2) organAllY.push(v[i]);
}
organAllY.sort((a, b) => a - b);
const yBottom5PctCutoff = organAllY[Math.floor(organAllY.length * 0.05)];

const bottomOrganIndices = {};
for (const sName of ORGAN_SLOTS) {
  const v = setupVerts[sName];
  const idxs = [];
  for (let i = 0; i < v.length / 2; i++) {
    if (v[2 * i + 1] <= yBottom5PctCutoff) idxs.push(i);
  }
  bottomOrganIndices[sName] = idxs;
}

const GROUND_WEEDS = [
  'weed_ground_left',
  'weed_ground_bl',
  'weed_ground_right',
  'weed_edge_right',
  'coral_purple',
  'weed_bottom_mid',
  'weed_bottom_right',
  'weed_left',
];
const groundWeedBaseIndices = {};
for (const wn of GROUND_WEEDS) {
  const ch = rigStatic.chains[wn];
  const bx = ch.base[0] - 679.0, by = 660.0 - ch.base[1];
  const tx = ch.tip[0] - 679.0, ty = 660.0 - ch.tip[1];
  const dx = tx - bx, dy = ty - by;
  const L2 = dx * dx + dy * dy;
  const v = setupVerts[wn];
  const idxs = [];
  for (let i = 0; i < v.length / 2; i++) {
    const px = v[2 * i], py = v[2 * i + 1];
    const tProj = ((px - bx) * dx + (py - by) * dy) / L2;
    if (tProj <= 0.16) idxs.push(i);
  }
  assert(idxs.length > 0, `No base vertices found for ${wn}`);
  groundWeedBaseIndices[wn] = idxs;
}

// Precompute setup signed 2x triangle areas for every mesh slot
const slotTriangles = {};
const setupSigned2Area = {};
for (const slot of setupSkel.slots) {
  const name = slot.data.name;
  if (name.startsWith('glow_')) continue;
  const att = data.defaultSkin.getAttachment(slot.data.index, name);
  const tris = Array.from(att.triangles);
  slotTriangles[name] = tris;
  const v = setupVerts[name];
  const areas = [];
  for (let t = 0; t < tris.length; t += 3) {
    const a = tris[t] * 2, b = tris[t + 1] * 2, c = tris[t + 2] * 2;
    const s2 = (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
    areas.push(s2);
  }
  setupSigned2Area[name] = areas;
}

// 5. Dense 60 Hz Sweep Across All 8 Clips
let totalFrames = 0;
let maxBottomOrganDeltaY = 0;
let minBottomOrganDeltaY = 0;
let maxGroundWeedBaseMove = 0;
let foldedTrianglesOver15px2 = 0;
let anyInvertedTriangles = 0;
let maxFoldAreaPx2 = 0;
const clipPeakDisp = {};

for (const [animName, dur] of Object.entries(EXPECTED_CLIPS)) {
  const steps = Math.round(dur * 60);
  let peakDisp = 0;
  for (let s = 0; s <= steps; s++) {
    const t = Math.min(s / 60, dur);
    const sk = pose(data, animName, t);
    const verts = allSlotVertices(sk);
    totalFrames++;

    // Check bottom 5% of organ vertices stay within [-4.5, +4.5] px of setup Y
    for (const sName of ORGAN_SLOTS) {
      const v = verts[sName], sv = setupVerts[sName];
      for (const idx of bottomOrganIndices[sName]) {
        const dy = v[2 * idx + 1] - sv[2 * idx + 1];
        maxBottomOrganDeltaY = Math.max(maxBottomOrganDeltaY, dy);
        minBottomOrganDeltaY = Math.min(minBottomOrganDeltaY, dy);
        assert(
          dy >= -4.5 && dy <= 4.5,
          `${animName} t=${t.toFixed(3)}: ${sName} bottom vertex ${idx} dy=${dy.toFixed(2)} outside [-4.5, +4.5]`
        );
      }
    }

    // Check ground-weed bases move <= 2.0px
    for (const wn of GROUND_WEEDS) {
      const v = verts[wn], sv = setupVerts[wn];
      for (const idx of groundWeedBaseIndices[wn]) {
        const d = Math.hypot(v[2 * idx] - sv[2 * idx], v[2 * idx + 1] - sv[2 * idx + 1]);
        maxGroundWeedBaseMove = Math.max(maxGroundWeedBaseMove, d);
        assert(d <= 2.0, `${animName} t=${t.toFixed(3)}: ${wn} base moved ${d.toFixed(2)}px > 2.0px`);
      }
    }

    // Check signed triangle areas across all meshes + peak displacement
    for (const [sName, tris] of Object.entries(slotTriangles)) {
      const v = verts[sName], sv = setupVerts[sName];
      for (let i = 0; i < v.length; i += 2) {
        peakDisp = Math.max(peakDisp, Math.hypot(v[i] - sv[i], v[i + 1] - sv[i + 1]));
      }
      const baseAreas = setupSigned2Area[sName];
      for (let k = 0; k < baseAreas.length; k++) {
        const a = tris[3 * k] * 2, b = tris[3 * k + 1] * 2, c = tris[3 * k + 2] * 2;
        const s2 = (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
        if (s2 * baseAreas[k] < -1e-4) {
          anyInvertedTriangles++;
          const foldArea = 0.5 * Math.abs(s2);
          maxFoldAreaPx2 = Math.max(maxFoldAreaPx2, foldArea);
          if (foldArea > 15.0) foldedTrianglesOver15px2++;
        }
      }
    }
  }
  clipPeakDisp[animName] = Number(peakDisp.toFixed(2));
}

assert.equal(foldedTrianglesOver15px2, 0, `Detected ${foldedTrianglesOver15px2} folded triangles > 15 px^2 (max=${maxFoldAreaPx2.toFixed(2)})`);

// 6. Biomechanical Choreography Assertions
const cannonCoilX = pose(data, 'attack', 0.25).findBone('orifice_ta').worldX;
const cannonSetupX = setupSkel.findBone('orifice_ta').worldX;
const cannonBlastX = pose(data, 'attack', 0.39).findBone('orifice_ta').worldX;
assert(cannonCoilX - cannonSetupX > 20, `attack coil too weak: +${(cannonCoilX - cannonSetupX).toFixed(1)}px`);
assert(cannonSetupX - cannonBlastX > 42, `attack cannon blast reach too weak: ${(cannonBlastX - cannonSetupX).toFixed(1)}px`);

const hurtLeftDent = pose(data, 'hurt', 0.045).findBone('conus_atrium').worldX - setupSkel.findBone('conus_atrium').worldX;
const hurtRightSurge = pose(data, 'hurt', 0.095).findBone('venous_sinus').worldX - setupSkel.findBone('venous_sinus').worldX;
assert(hurtLeftDent > 24 && hurtRightSurge > 18, `hurt hydrostatic slosh missing: dent=${hurtLeftDent.toFixed(1)}, surge=${hurtRightSurge.toFixed(1)}`);

const report = {
  status: 'PASS',
  runtime: '@esotericsoftware/spine-core 4.2',
  bones: data.bones.length,
  slots: data.slots.length,
  animations: EXPECTED_CLIPS,
  framesChecked60Hz: totalFrames,
  idleLoopErrorPx: Number(idleLoopErr.toFixed(5)),
  dieReviveJoinErrorPx: Number(dieReviveErr.toFixed(5)),
  reviveToIdleErrorPx: Number(revToIdleErr.toFixed(5)),
  oneShotReturnErrorsPx: oneShotReturnErrors,
  bottom5PctOrganDeltaYRangePx: [Number(minBottomOrganDeltaY.toFixed(3)), Number(maxBottomOrganDeltaY.toFixed(3))],
  maxGroundWeedBaseMovePx: Number(maxGroundWeedBaseMove.toFixed(5)),
  foldedTrianglesOver15Px2: foldedTrianglesOver15px2,
  anyInvertedTrianglesTotal: anyInvertedTriangles,
  maxFoldAreaPx2: Number(maxFoldAreaPx2.toFixed(3)),
  attackCoilRightPx: Number((cannonCoilX - cannonSetupX).toFixed(2)),
  attackBlastLeftPx: Number((cannonBlastX - cannonSetupX).toFixed(2)),
  clipPeakDisplacementPx: clipPeakDisp,
};

fs.writeFileSync('pkg/verification.json', JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
console.log('THE_LEGACY_V2_REBUILT_PASS');
