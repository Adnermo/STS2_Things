// Verify packaged Spine 4.2 skeleton & all 8 clips at 60 Hz using official @esotericsoftware/spine-core
import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as S from '../../../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';

const spjsonPath = process.argv[2] ?? 'pkg/the_legacy.spjson';
const atlasPath  = process.argv[3] ?? 'pkg/the_legacy.atlas';

const raw = JSON.parse(fs.readFileSync(spjsonPath, 'utf8').replace(/^\uFEFF/, ''));
assert(raw.skeleton.spine.startsWith('4.2'), `Expected Spine 4.2 header, got ${raw.skeleton.spine}`);

const atlasText = fs.readFileSync(atlasPath, 'utf8');
const atlas = new S.TextureAtlas(atlasText);
for (const page of atlas.pages) {
  page.setTexture({
    setFilters() {}, setWraps() {}, dispose() {},
    getImage() { return { width: page.width, height: page.height }; },
  });
}
const data = new S.SkeletonJson(new S.AtlasAttachmentLoader(atlas)).readSkeletonData(raw);

const EXPECTED = {
  idle_loop: 3.00,
  attack:    1.25,
  cast:      1.40,
  hurt:      0.65,
  die:       2.60,
  summon:    1.30,
  power_up:  1.50,
  revive:    1.80,
};

const animNames = data.animations.map(a => a.name).sort();
assert.deepEqual(animNames, Object.keys(EXPECTED).sort(), 'Animation list mismatch');
for (const [name, dur] of Object.entries(EXPECTED)) {
  const anim = data.findAnimation(name);
  assert(anim, `Missing animation ${name}`);
  assert(Math.abs(anim.duration - dur) < 1e-3, `${name} duration ${anim.duration} != ${dur}`);
}
for (const bone of data.bones) {
  if (bone.parent) {
    assert(data.bones.indexOf(bone.parent) < data.bones.indexOf(bone), `Bone parent order invalid for ${bone.name}`);
  }
}

function pose(animName = null, time = 0) {
  const sk = new S.Skeleton(data);
  sk.setToSetupPose();
  if (animName) {
    data.findAnimation(animName).apply(sk, 0, time, false, [], 1, S.MixBlend.setup, S.MixDirection.mixIn);
  }
  sk.updateWorldTransform(S.Physics.none);
  return sk;
}

function slotWorldVerts(slot) {
  const att = slot.attachment;
  if (!att) return [];
  if (att instanceof S.MeshAttachment) {
    const out = new Float32Array(att.worldVerticesLength);
    att.computeWorldVertices(slot, 0, out.length, out, 0, 2);
    return Array.from(out);
  }
  if (att instanceof S.RegionAttachment) {
    const out = new Float32Array(8);
    att.computeWorldVertices(slot, out, 0, 2);
    return Array.from(out);
  }
  return [];
}

function allVertices(sk) {
  return Object.fromEntries(sk.slots.map(s => [s.data.name, slotWorldVerts(s)]));
}

function maxVertDiff(a, b) {
  let mx = 0;
  for (const [k, va] of Object.entries(a)) {
    const vb = b[k];
    for (let i = 0; i < va.length; i++) {
      mx = Math.max(mx, Math.abs(va[i] - vb[i]));
    }
  }
  return mx;
}

// 1. Glow topology & skin weight lock check
const GLOW_MAP = {
  atrium_glow: 'atrium',
  bulb_glow: 'bulb',
  mid_lobe_glow: 'mid_lobe',
  bottom_lobe_glow: 'bottom_lobe',
};
for (const [glow, host] of Object.entries(GLOW_MAP)) {
  assert(raw.skins[0].attachments[glow]?.[glow], `Missing glow attachment ${glow}`);
  const gAtt = raw.skins[0].attachments[glow][glow];
  const hAtt = raw.skins[0].attachments[host][host];
  for (const field of ['uvs', 'triangles', 'vertices']) {
    assert.deepEqual(gAtt[field], hAtt[field], `${glow} ${field} diverges from ${host}`);
  }
}

// 2. Endpoint & loop seam continuity checks
const setupVerts = allVertices(pose());
const idle0      = allVertices(pose('idle_loop', 0));
const idleEnd    = allVertices(pose('idle_loop', EXPECTED.idle_loop));
const loopErr    = maxVertDiff(idle0, idleEnd);
assert(loopErr < 0.3, `idle_loop(0) vs idle_loop(3.0) gap ${loopErr.toFixed(4)}px >= 0.3px`);

const oneShotErrors = {};
for (const name of ['attack', 'cast', 'hurt', 'summon', 'power_up']) {
  const endV = allVertices(pose(name, EXPECTED[name]));
  const startV = allVertices(pose(name, 0));
  const errEnd = maxVertDiff(idle0, endV);
  const errStart = maxVertDiff(idle0, startV);
  oneShotErrors[name] = { startErr: +errStart.toFixed(4), endErr: +errEnd.toFixed(4) };
  assert(errEnd < 1.0, `${name} end gap to idle_loop(0) is ${errEnd.toFixed(4)}px >= 1.0px`);
}

const dieEnd     = allVertices(pose('die', EXPECTED.die));
const revive0    = allVertices(pose('revive', 0));
const reviveEnd  = allVertices(pose('revive', EXPECTED.revive));
const dieRevErr  = maxVertDiff(dieEnd, revive0);
const revIdleErr = maxVertDiff(reviveEnd, idle0);
assert(dieRevErr < 0.3, `die(2.6) vs revive(0) gap ${dieRevErr.toFixed(4)}px >= 0.3px`);
assert(revIdleErr < 1.0, `revive(1.8) vs idle_loop(0) gap ${revIdleErr.toFixed(4)}px >= 1.0px`);

// Also check tint & glow continuity across die(2.6) -> revive(0)
const skDieEnd = pose('die', EXPECTED.die);
const skRev0   = pose('revive', 0);
for (let i = 0; i < skDieEnd.slots.length; i++) {
  const c1 = skDieEnd.slots[i].color, c2 = skRev0.slots[i].color;
  const cd = Math.max(Math.abs(c1.r - c2.r), Math.abs(c1.g - c2.g), Math.abs(c1.b - c2.b), Math.abs(c1.a - c2.a));
  assert(cd < 0.02, `Slot ${skDieEnd.slots[i].data.name} color jump across die->revive: ${cd}`);
}

// 3. Identify bottom-most 5% of body vertices & ground-weed base vertices in setup pose
const BODY_PARTS = [
  'purple_top', 'purple_mass', 'purple_lr',
  'blue_bottom', 'blue_main', 'blue_down',
  'right_lobe', 'mid_lobe', 'bottom_lobe', 'atrium', 'bulb',
];
const GROUND_WEEDS = [
  'weed_ground_left', 'weed_ground_bl', 'weed_ground_right', 'weed_edge_right',
  'coral_purple', 'weed_bottom_mid', 'weed_bottom_right',
];
const ACTIVE_FLORA = [
  'weed_left', 'weed_ground_left', 'weed_top_left', 'weed_bottom_mid', 'weed_bottom_right', 'weed_ground_bl', 'weed_right',
];

// Spine Y is up (y_spine = 660 - y_img), so "bottom-most 5% of body vertices" = lowest 5% setup y_spine!
const allBodySetupY = [];
for (const part of BODY_PARTS) {
  const v = setupVerts[part];
  for (let i = 0; i < v.length; i += 2) {
    allBodySetupY.push({ part, idx: i, x0: v[i], y0: v[i + 1] });
  }
}
allBodySetupY.sort((a, b) => a.y0 - b.y0);
const bottom5Count = Math.max(1, Math.ceil(allBodySetupY.length * 0.05));
const bottom5Body = allBodySetupY.slice(0, bottom5Count);

// Ground-weed bases: lowest 8% y_spine vertices of each ground-rooted flora part (plus chain _0 bone world position!)
const groundWeedBases = [];
for (const gw of GROUND_WEEDS) {
  const v = setupVerts[gw];
  const pts = [];
  for (let i = 0; i < v.length; i += 2) pts.push({ part: gw, idx: i, x0: v[i], y0: v[i + 1] });
  pts.sort((a, b) => a.y0 - b.y0);
  const k = Math.max(1, Math.ceil(pts.length * 0.08));
  groundWeedBases.push(...pts.slice(0, k));
}
const setupSk = pose();
const groundBoneBases = GROUND_WEEDS.map(gw => {
  const b = setupSk.findBone(`${gw}_0`);
  assert(b.parent.data.name === 'root', `${gw}_0 must be parented to root, got ${b.parent.data.name}`);
  return { name: `${gw}_0`, x0: b.worldX, y0: b.worldY };
});
// Verify back-flora draw order: weed_left & weed_ground_left & weed_ground_right must sit behind body chambers!
const slotNames = data.slots.map(s => s.name);
for (const bf of ['weed_left', 'weed_ground_left', 'weed_ground_right', 'weed_edge_right']) {
  assert(slotNames.indexOf(bf) < slotNames.indexOf('mid_lobe'), `${bf} must draw behind mid_lobe!`);
}
// Track tip vertex of each active flora part to assert lively non-frozen motion!
const floraTipIdx = {};
for (const fn of ACTIVE_FLORA) {
  const v = setupVerts[fn];
  let best = 0, bestScore = -Infinity;
  for (let i = 0; i < v.length; i += 2) {
    const score = (fn === 'weed_ground_left') ? -v[i] : (fn === 'weed_ground_bl' ? v[i] + v[i + 1] : v[i + 1]);
    if (score > bestScore) { bestScore = score; best = i; }
  }
  floraTipIdx[fn] = best;
}

// Setup triangle orientation signs per opaque mesh slot
const setupTriSign = {};
for (const slot of setupSk.slots) {
  const name = slot.data.name;
  if (GLOW_MAP[name]) continue;
  const att = slot.attachment;
  if (!(att instanceof S.MeshAttachment)) continue;
  const v = setupVerts[name];
  const tris = att.triangles;
  const signs = [];
  for (let t = 0; t < tris.length; t += 3) {
    const i0 = tris[t] * 2, i1 = tris[t + 1] * 2, i2 = tris[t + 2] * 2;
    const area2 = (v[i1] - v[i0]) * (v[i2 + 1] - v[i0 + 1]) - (v[i1 + 1] - v[i0 + 1]) * (v[i2] - v[i0]);
    signs.push(Math.sign(area2));
  }
  setupTriSign[name] = { tris, signs };
}

// 4. Sample ALL 8 clips at 60 Hz!
let globalMinBottomDY = 0;
let globalMaxBottomDY = 0;
let globalMaxWeedBaseMove = 0;
let globalMaxWeedBoneMove = 0;
let globalMaxInvertedTris = 0;
let globalMaxInvertedArea = 0;
const clipStats = {};

for (const [name, dur] of Object.entries(EXPECTED)) {
  const steps = Math.ceil(dur * 60);
  let minDY = Infinity, maxDY = -Infinity;
  let maxWeedMove = 0;
  let maxInvCount = 0, maxInvArea = 0;
  let minTubeAX = Infinity, maxBulbScale = -Infinity, minBulbY = Infinity, maxHurtHeartX = -Infinity;
  const tipBounds = Object.fromEntries(ACTIVE_FLORA.map(fn => [fn, { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity }]));

  for (let s = 0; s <= steps; s++) {
    const t = Math.min(s / 60, dur);
    const sk = pose(name, t);
    const verts = allVertices(sk);

    for (const fn of ACTIVE_FLORA) {
      const idx = floraTipIdx[fn];
      const tx = verts[fn][idx], ty = verts[fn][idx + 1];
      tipBounds[fn].minX = Math.min(tipBounds[fn].minX, tx);
      tipBounds[fn].maxX = Math.max(tipBounds[fn].maxX, tx);
      tipBounds[fn].minY = Math.min(tipBounds[fn].minY, ty);
      tipBounds[fn].maxY = Math.max(tipBounds[fn].maxY, ty);
    }

    for (const bone of sk.bones) {
      assert(
        [bone.worldX, bone.worldY, bone.a, bone.b, bone.c, bone.d].every(Number.isFinite),
        `Non-finite bone ${bone.data.name} in ${name} at t=${t}`
      );
    }

    // Track key motion landmarks
    minTubeAX = Math.min(minTubeAX, sk.findBone('tube_a').worldX - setupSk.findBone('tube_a').worldX);
    maxBulbScale = Math.max(maxBulbScale, sk.findBone('bulb').scaleY);
    minBulbY = Math.min(minBulbY, sk.findBone('bulb').worldY - setupSk.findBone('bulb').worldY);
    maxHurtHeartX = Math.max(maxHurtHeartX, sk.findBone('heart').worldX - setupSk.findBone('heart').worldX);

    // Check bottom-most 5% of body vertices stay within [-4.5, +4.5] px of setup Y!
    for (const item of bottom5Body) {
      const dy = verts[item.part][item.idx + 1] - item.y0;
      minDY = Math.min(minDY, dy);
      maxDY = Math.max(maxDY, dy);
    }

    // Check ground-weed bases move <= 2.0px!
    for (const item of groundWeedBases) {
      const v = verts[item.part];
      const d = Math.hypot(v[item.idx] - item.x0, v[item.idx + 1] - item.y0);
      maxWeedMove = Math.max(maxWeedMove, d);
    }
    for (const gb of groundBoneBases) {
      const b = sk.findBone(gb.name);
      globalMaxWeedBoneMove = Math.max(globalMaxWeedBoneMove, Math.hypot(b.worldX - gb.x0, b.worldY - gb.y0));
    }

    // Check triangle inversions at 60 Hz!
    let frameInv = 0, frameInvArea = 0;
    for (const [slotName, info] of Object.entries(setupTriSign)) {
      const v = verts[slotName];
      const { tris, signs } = info;
      for (let k = 0, ti = 0; ti < tris.length; k++, ti += 3) {
        if (signs[k] === 0) continue;
        const i0 = tris[ti] * 2, i1 = tris[ti + 1] * 2, i2 = tris[ti + 2] * 2;
        const area2 = (v[i1] - v[i0]) * (v[i2 + 1] - v[i0 + 1]) - (v[i1 + 1] - v[i0 + 1]) * (v[i2] - v[i0]);
        if (area2 * signs[k] < -1e-4) {
          frameInv++;
          frameInvArea += 0.5 * Math.abs(area2);
        }
      }
    }
    maxInvCount = Math.max(maxInvCount, frameInv);
    maxInvArea = Math.max(maxInvArea, frameInvArea);
  }

  globalMinBottomDY = Math.min(globalMinBottomDY, minDY);
  globalMaxBottomDY = Math.max(globalMaxBottomDY, maxDY);
  globalMaxWeedBaseMove = Math.max(globalMaxWeedBaseMove, maxWeedMove);
  globalMaxInvertedTris = Math.max(globalMaxInvertedTris, maxInvCount);
  globalMaxInvertedArea = Math.max(globalMaxInvertedArea, maxInvArea);

  const floraTipSpans = {};
  for (const fn of ACTIVE_FLORA) {
    const b = tipBounds[fn];
    floraTipSpans[fn] = +Math.hypot(b.maxX - b.minX, b.maxY - b.minY).toFixed(2);
  }

  clipStats[name] = {
    bottom5_dy_range: [+minDY.toFixed(3), +maxDY.toFixed(3)],
    ground_weed_base_max_px: +maxWeedMove.toFixed(3),
    max_inverted_tris: maxInvCount,
    max_inverted_area_px2: +maxInvArea.toFixed(3),
    tube_a_left_thrust_px: +minTubeAX.toFixed(2),
    bulb_peak_sy: +maxBulbScale.toFixed(4),
    bulb_min_dy_px: +minBulbY.toFixed(2),
    heart_max_dx_px: +maxHurtHeartX.toFixed(2),
    flora_tip_span_px: floraTipSpans,
  };
}

const summary = {
  spine_version: raw.skeleton.spine,
  bones: data.bones.length,
  slots: data.slots.length,
  glow_slots: Object.keys(GLOW_MAP),
  idle_loop_gap_px: +loopErr.toFixed(5),
  die_to_revive_gap_px: +dieRevErr.toFixed(5),
  revive_to_idle_gap_px: +revIdleErr.toFixed(5),
  one_shot_seam_errors_px: oneShotErrors,
  global_bottom5_body_dy_range_px: [+globalMinBottomDY.toFixed(3), +globalMaxBottomDY.toFixed(3)],
  global_ground_weed_base_max_px: +globalMaxWeedBaseMove.toFixed(4),
  global_ground_weed_bone0_max_px: +globalMaxWeedBoneMove.toFixed(5),
  global_max_inverted_tris: globalMaxInvertedTris,
  global_max_inverted_area_px2: +globalMaxInvertedArea.toFixed(4),
  clips: clipStats,
};

fs.writeFileSync('out/verification.json', JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));

for (const [name, st] of Object.entries(clipStats)) {
  assert(st.bottom5_dy_range[0] >= -4.5 && st.bottom5_dy_range[1] <= 4.5, `${name}: bottom 5% body Y drift [${st.bottom5_dy_range}] exceeds [-4.5, +4.5]px!`);
  assert(st.ground_weed_base_max_px <= 2.0, `${name}: ground weed base moved ${st.ground_weed_base_max_px}px > 2.0px!`);
  assert(st.max_inverted_tris === 0, `${name}: ${st.max_inverted_tris} inverted triangles (area ${st.max_inverted_area_px2} px^2)!`);
}
assert(clipStats.idle_loop.flora_tip_span_px.weed_left >= 28.0, `idle_loop weed_left too static (${clipStats.idle_loop.flora_tip_span_px.weed_left}px < 28px)!`);
assert(clipStats.idle_loop.flora_tip_span_px.weed_ground_left >= 18.0, `idle_loop weed_ground_left too static (${clipStats.idle_loop.flora_tip_span_px.weed_ground_left}px < 18px)!`);
assert(clipStats.attack.flora_tip_span_px.weed_left >= 40.0, `attack weed_left sweep too weak (${clipStats.attack.flora_tip_span_px.weed_left}px < 40px)!`);
assert(clipStats.attack.flora_tip_span_px.weed_ground_left >= 24.0, `attack weed_ground_left sweep too weak (${clipStats.attack.flora_tip_span_px.weed_ground_left}px < 24px)!`);
console.log('THE_LEGACY_V1_OPTIMIZED_VERIFY_PASS');
