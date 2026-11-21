import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { S, load, pose, worldVertices } from './runtime.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const pkgRoot = process.argv[2] ? path.resolve(process.argv[2]) : path.join(__dirname, 'pkg');

const variants = ['soul_roe_1', 'soul_roe_2', 'soul_roe_3'];
const expectedAnims = [
  'idle_loop',
  'attack',
  'cast',
  'power_up',
  'summon',
  'hurt',
  'die',
  'revive',
];
const expectedEvents = {
  attack: ['attack_contact', 0.42],
  cast: ['cast_release', 0.50],
  power_up: ['power_peak', 0.52],
  summon: ['summon_appear', 0.36],
  die: ['rupture_burst', 0.28],
};
const silentEventAnims = ['idle_loop', 'hurt', 'revive'];

const mainOrbZeroRotBones = [
  'root',
  'shadow_b',
  'main_center',
  'rim_n',
  'rim_s',
  'rim_w',
  'rim_e',
  'yolk_b',
  'halo_n',
  'halo_s',
  'halo_w',
  'halo_e',
  'impact_b',
];

const solidSlots = [
  'shadow',
  'rear_glow',
  'cytoplasm',
  'nucleus',
  'veins_highlight',
  'membrane_ring',
];

function wrapDeg(d) {
  const r = (((d + 180) % 360) + 360) % 360 - 180;
  return Math.abs(r);
}

function triArea(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((k) => k * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}

const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

let globalMaxBoneScaleDev = 0;
let globalMaxMainOrbRotDeg = 0;
let globalInvertedTriangles = 0;
let globalLoopErr = 0;
let globalReviveErr = 0;

const variantReports = {};
const idleDriftSignatures = [];

for (const vName of variants) {
  const vDir = path.join(pkgRoot, vName);
  for (const ext of [
    `${vName}.png`,
    `${vName}.atlas`,
    `${vName}.spatlas`,
    `${vName}.spjson`,
    `${vName}_skel_data.tres`,
  ]) {
    assert(fs.existsSync(path.join(vDir, ext)), `Missing packaged file: ${vName}/${ext}`);
  }

  const tresText = fs.readFileSync(path.join(vDir, `${vName}_skel_data.tres`), 'utf8');
  assert(
    tresText.includes(`res://STS2_Things/animations/monsters/${vName}/${vName}.spatlas`),
    `Bad .spatlas path in ${vName}_skel_data.tres`
  );
  assert(
    tresText.includes(`res://STS2_Things/animations/monsters/${vName}/${vName}.spjson`),
    `Bad .spjson path in ${vName}_skel_data.tres`
  );

  const spjsonPath = path.join(vDir, `${vName}.spjson`);
  const atlasPath = path.join(vDir, `${vName}.atlas`);
  const spjsonRawText = fs.readFileSync(spjsonPath, 'utf8');
  const atlasRawText = fs.readFileSync(atlasPath, 'utf8');
  const raw = JSON.parse(spjsonRawText);
  const data = load(spjsonPath, atlasPath);

  // STRICT SINGLE-ROE ASSERTION: ZERO `sat_` bones, slots, or attachments anywhere!
  assert(!spjsonRawText.includes('sat_'), `${vName}: forbidden 'sat_' reference found in .spjson!`);
  assert(!atlasRawText.includes('sat_'), `${vName}: forbidden 'sat_' region found in .atlas!`);
  for (const b of data.bones) {
    assert(!b.name.startsWith('sat_'), `${vName}: forbidden satellite bone ${b.name}`);
  }
  for (const s of data.slots) {
    assert(!s.name.startsWith('sat_'), `${vName}: forbidden satellite slot ${s.name}`);
  }

  assert.equal(data.bones.length, 14, `${vName}: expected 14 bones, got ${data.bones.length}`);
  assert.equal(data.slots.length, 12, `${vName}: expected 12 slots, got ${data.slots.length}`);
  assert.deepEqual(
    data.animations.map((a) => a.name).sort(),
    expectedAnims.toSorted(),
    `${vName}: animation list mismatch`
  );

  // Event verification
  for (const [animName, [evName, evTime]] of Object.entries(expectedEvents)) {
    const evs = raw.animations[animName]?.events;
    assert(evs && evs.length === 1, `${vName}/${animName}: expected 1 event`);
    assert.equal(evs[0].name, evName, `${vName}/${animName}: wrong event name ${evs[0].name}`);
    assert(Math.abs(evs[0].time - evTime) < 1e-4, `${vName}/${animName}: wrong event time ${evs[0].time}`);
  }
  for (const animName of silentEventAnims) {
    const evs = raw.animations[animName]?.events || [];
    assert.equal(evs.length, 0, `${vName}/${animName}: expected 0 events, got ${evs.length}`);
  }

  // Setup pose assertions (`SLOT_DEFAULT_ALPHA == 0` on all dynamic VFX/glow layers)
  const setupSk = pose(data, null, 0);
  for (const glowSlot of [
    'body_trail',
    'core_beckon_glow',
    'core_intangible_glow',
    'beckon_halo_ring',
    'intangible_shimmer',
    'impact_flare',
  ]) {
    const sl = setupSk.findSlot(glowSlot);
    assert(sl, `${vName}: missing slot ${glowSlot}`);
    assert.equal(sl.color.a, 0, `${vName}: setup ${glowSlot} alpha must be 0, got ${sl.color.a}`);
  }

  // Distinct idle drift signature per variant
  const idleMid = pose(data, 'idle_loop', 0.70);
  idleDriftSignatures.push(
    `${Math.round(idleMid.findBone('main_center').worldX * 10)},${Math.round(idleMid.findBone('main_center').worldY * 10)}`
  );

  // Seam checks (`idle_loop` 0 vs 2.80s, `die` 0.78s vs `revive` 0.0s, `revive` 0.68s vs setup)
  let loopErr = 0;
  let reviveErr = 0;
  let reviveEndSetupErr = 0;
  for (const s of solidSlots) {
    const vSetup = worldVertices(pose(data, null, 0).findSlot(s));
    const vIdle0 = worldVertices(pose(data, 'idle_loop', 0).findSlot(s));
    const vIdleEnd = worldVertices(pose(data, 'idle_loop', 2.80).findSlot(s));
    const vDieEnd = worldVertices(pose(data, 'die', 0.78).findSlot(s));
    const vRev0 = worldVertices(pose(data, 'revive', 0.0).findSlot(s));
    const vRevEnd = worldVertices(pose(data, 'revive', 0.68).findSlot(s));

    loopErr = Math.max(loopErr, maxDiff(vIdle0, vIdleEnd));
    reviveErr = Math.max(reviveErr, maxDiff(vDieEnd, vRev0));
    reviveEndSetupErr = Math.max(reviveEndSetupErr, maxDiff(vRevEnd, vSetup));
  }
  assert(loopErr < 0.02, `${vName}: loopErr >= 0.02 (${loopErr})`);
  assert(reviveErr < 0.05, `${vName}: reviveErr >= 0.05 (${reviveErr})`);
  assert(reviveEndSetupErr < 0.05, `${vName}: revive(0.68) != setup (${reviveEndSetupErr})`);

  // 60 FPS sweep: zero bone scaling, zero main orb rotation, zero triangle inversions
  let maxBoneScaleDev = 0;
  let maxMainOrbRotDeg = 0;
  let invertedTriangles = 0;

  const allSlots = data.slots.map((s) => s.name);
  const setupVerts = Object.fromEntries(allSlots.map((s) => [s, worldVertices(setupSk.findSlot(s))]));
  const defaultSkin = data.findSkin('default');

  for (const anim of data.animations) {
    const steps = Math.round(anim.duration * 60);
    for (let step = 0; step <= steps; step++) {
      const t = Math.min(step / 60, anim.duration);
      const sk = pose(data, anim.name, t);

      for (const b of sk.bones) {
        maxBoneScaleDev = Math.max(maxBoneScaleDev, Math.abs(b.scaleX - 1.0), Math.abs(b.scaleY - 1.0));
      }
      for (const bName of mainOrbZeroRotBones) {
        const b = sk.findBone(bName);
        assert(b, `${vName}: missing bone ${bName}`);
        maxMainOrbRotDeg = Math.max(maxMainOrbRotDeg, wrapDeg(b.rotation), wrapDeg(b.getWorldRotationX()));
      }

      for (let sIdx = 0; sIdx < allSlots.length; sIdx++) {
        const sName = allSlots[sIdx];
        const att = defaultSkin.getAttachment(sIdx, sName);
        if (!(att instanceof S.MeshAttachment)) continue;
        const tris = att.triangles;
        const vCur = worldVertices(sk.findSlot(sName), att);
        const vBase = setupVerts[sName];
        for (let i = 0; i < tris.length; i += 3) {
          if (triArea(vCur, i, tris) * triArea(vBase, i, tris) < -0.01) {
            invertedTriangles++;
          }
        }
      }
    }
  }

  assert.equal(maxBoneScaleDev, 0, `${vName}: non-zero bone scale dev: ${maxBoneScaleDev}`);
  assert.equal(maxMainOrbRotDeg, 0, `${vName}: non-zero main orb rotation: ${maxMainOrbRotDeg}`);
  assert.equal(invertedTriangles, 0, `${vName}: inverted triangles: ${invertedTriangles}`);

  // `attack` FULL-BODY SELF-FLING COMET STRIKE verification:
  // - Wind-up `main_center` draws >= +70px right
  // - At `t = 0.42s` (`attack_contact`), `main_center` travels <= -470px left with `body_trail` alpha > 0.90 & `impact_flare` alpha > 0.85!
  // - Vaulted rebound arc rises >= +55px above setup Y around `t ~ 0.68s`, settling cleanly to (0,0) with VFX alphas = 0!
  const atkAnim = data.findAnimation('attack');
  const setupMainX = setupSk.findBone('main_center').worldX;
  const setupMainY = setupSk.findBone('main_center').worldY;

  let atkMaxWindupDx = -Infinity;
  let atkMinStrikeDx = Infinity;
  let atkMaxReboundDy = -Infinity;
  for (let step = 0; step <= Math.round(atkAnim.duration * 60); step++) {
    const t = Math.min(step / 60, atkAnim.duration);
    const sk = pose(data, 'attack', t);
    const dx = sk.findBone('main_center').worldX - setupMainX;
    const dy = sk.findBone('main_center').worldY - setupMainY;
    atkMaxWindupDx = Math.max(atkMaxWindupDx, dx);
    atkMinStrikeDx = Math.min(atkMinStrikeDx, dx);
    if (t > 0.48) atkMaxReboundDy = Math.max(atkMaxReboundDy, dy);
  }

  const atkHit = pose(data, 'attack', 0.42);
  const hitDx = atkHit.findBone('main_center').worldX - setupMainX;
  const hitTrailAlpha = atkHit.findSlot('body_trail').color.a;
  const hitImpactAlpha = atkHit.findSlot('impact_flare').color.a;
  const hitRimWDx = atkHit.findBone('rim_w').x - atkHit.findBone('rim_w').data.x;
  const hitYolkDx = atkHit.findBone('yolk_b').x - atkHit.findBone('yolk_b').data.x;
  const atkEnd = pose(data, 'attack', atkAnim.duration);

  assert(atkMaxWindupDx >= 72.0, `${vName}: attack wind-up coil dx too small (${atkMaxWindupDx})`);
  assert(hitDx <= -470.0, `${vName}: attack main_center dx at t=0.42s expected <= -470, got ${hitDx}`);
  assert(hitTrailAlpha > 0.90, `${vName}: attack body_trail alpha at t=0.42s expected > 0.90, got ${hitTrailAlpha}`);
  assert(hitImpactAlpha > 0.85, `${vName}: attack impact_flare alpha at t=0.42s expected > 0.85, got ${hitImpactAlpha}`);
  assert(hitRimWDx >= 15.0, `${vName}: attack front rim_w pancake compression missing (${hitRimWDx})`);
  assert(hitYolkDx <= -12.0, `${vName}: attack yolk_b forward slam inertia missing (${hitYolkDx})`);
  assert(atkMaxReboundDy >= 58.0, `${vName}: attack vaulted rebound arc too low (${atkMaxReboundDy})`);
  assert(atkEnd.findSlot('body_trail').color.a < 1e-4, `${vName}: attack body_trail end alpha != 0`);
  assert(atkEnd.findSlot('impact_flare').color.a < 1e-4, `${vName}: attack impact_flare end alpha != 0`);

  // `cast` (Beckon golden brightening) verification
  const castAnim = data.findAnimation('cast');
  let maxBeckonGlow = 0;
  let maxHaloGlow = 0;
  for (let step = 0; step <= Math.round(castAnim.duration * 60); step++) {
    const t = Math.min(step / 60, castAnim.duration);
    const sk = pose(data, 'cast', t);
    maxBeckonGlow = Math.max(maxBeckonGlow, sk.findSlot('core_beckon_glow').color.a);
    maxHaloGlow = Math.max(maxHaloGlow, sk.findSlot('beckon_halo_ring').color.a);
  }
  const castEnd = pose(data, 'cast', castAnim.duration);
  assert(maxBeckonGlow > 0.90, `${vName}: cast core_beckon_glow peak ${maxBeckonGlow} <= 0.90`);
  assert(maxHaloGlow > 0.85, `${vName}: cast beckon_halo_ring peak ${maxHaloGlow} <= 0.85`);
  assert(castEnd.findSlot('core_beckon_glow').color.a < 1e-4, `${vName}: cast core_beckon_glow end alpha != 0`);
  assert(castEnd.findSlot('beckon_halo_ring').color.a < 1e-4, `${vName}: cast beckon_halo_ring end alpha != 0`);

  // `power_up` (Intangible pearlescent brightening + `intangible_shimmer` + ethereal cytoplasm dip) verification
  const pwrAnim = data.findAnimation('power_up');
  let maxIntangGlow = 0;
  let maxIntangShimmer = 0;
  let minCytoAlpha = 1.0;
  for (let step = 0; step <= Math.round(pwrAnim.duration * 60); step++) {
    const t = Math.min(step / 60, pwrAnim.duration);
    const sk = pose(data, 'power_up', t);
    maxIntangGlow = Math.max(maxIntangGlow, sk.findSlot('core_intangible_glow').color.a);
    maxIntangShimmer = Math.max(maxIntangShimmer, sk.findSlot('intangible_shimmer').color.a);
    minCytoAlpha = Math.min(minCytoAlpha, sk.findSlot('cytoplasm').color.a);
  }
  const pwrEnd = pose(data, 'power_up', pwrAnim.duration);
  assert(maxIntangGlow > 0.90, `${vName}: power_up core_intangible_glow peak ${maxIntangGlow} <= 0.90`);
  assert(maxIntangShimmer > 0.90, `${vName}: power_up intangible_shimmer peak ${maxIntangShimmer} <= 0.90`);
  assert(minCytoAlpha <= 0.65, `${vName}: power_up cytoplasm did not become ethereal (${minCytoAlpha})`);
  assert(pwrEnd.findSlot('core_intangible_glow').color.a < 1e-4, `${vName}: power_up core_intangible_glow end alpha != 0`);
  assert(pwrEnd.findSlot('intangible_shimmer').color.a < 1e-4, `${vName}: power_up intangible_shimmer end alpha != 0`);
  assert(Math.abs(pwrEnd.findSlot('cytoplasm').color.a - 1.0) < 1e-3, `${vName}: power_up cytoplasm end alpha != 1`);

  // `hurt` hydro-shock (+42px right + -16px liquid yolk lag) verification
  const hurtPeak = pose(data, 'hurt', 0.06);
  const hurtDx = hurtPeak.findBone('main_center').worldX - setupMainX;
  const yolkLocalDx = hurtPeak.findBone('yolk_b').x - hurtPeak.findBone('yolk_b').data.x;
  assert(hurtDx >= 39.0, `${vName}: hurt right jolt too weak (${hurtDx})`);
  assert(yolkLocalDx <= -14.0, `${vName}: hurt yolk liquid lag missing (${yolkLocalDx})`);

  globalMaxBoneScaleDev = Math.max(globalMaxBoneScaleDev, maxBoneScaleDev);
  globalMaxMainOrbRotDeg = Math.max(globalMaxMainOrbRotDeg, maxMainOrbRotDeg);
  globalInvertedTriangles += invertedTriangles;
  globalLoopErr = Math.max(globalLoopErr, loopErr);
  globalReviveErr = Math.max(globalReviveErr, reviveErr);

  variantReports[vName] = {
    bones: data.bones.length,
    slots: data.slots.length,
    animations: data.animations.length,
    zeroSatellitesConfirmed: true,
    loopErr: Math.round(loopErr * 1e5) / 1e5,
    reviveErr: Math.round(reviveErr * 1e5) / 1e5,
    maxBoneScaleDev,
    maxMainOrbRotDeg,
    invertedTriangles,
    attackWindupMaxDx: Math.round(atkMaxWindupDx * 10) / 10,
    attackHitDxAt042: Math.round(hitDx * 10) / 10,
    attackMinStrikeDx: Math.round(atkMinStrikeDx * 10) / 10,
    attackBodyTrailAt042: Math.round(hitTrailAlpha * 100) / 100,
    attackImpactFlareAt042: Math.round(hitImpactAlpha * 100) / 100,
    attackReboundApexDy: Math.round(atkMaxReboundDy * 10) / 10,
    castBeckonGlowPeak: Math.round(maxBeckonGlow * 100) / 100,
    castHaloPeak: Math.round(maxHaloGlow * 100) / 100,
    powerUpIntangibleGlowPeak: Math.round(maxIntangGlow * 100) / 100,
    powerUpIntangibleShimmerPeak: Math.round(maxIntangShimmer * 100) / 100,
    powerUpMinCytoplasmAlpha: Math.round(minCytoAlpha * 100) / 100,
    hurtMainDx: Math.round(hurtDx * 10) / 10,
    hurtYolkLagDx: Math.round(yolkLocalDx * 10) / 10,
  };
}

assert.equal(new Set(idleDriftSignatures).size, 3, 'All 3 variants must have distinct idle buoyancy paths!');

const report = {
  result: 'PASS',
  rig: 'ThingsSoulRoe',
  architecture: 'pure_single_roe_self_fling',
  variants,
  fps: 60,
  zeroSatellites: true,
  maxBoneScaleDev: globalMaxBoneScaleDev,
  maxMainOrbRotDeg: globalMaxMainOrbRotDeg,
  invertedTriangles: globalInvertedTriangles,
  loopErr: Math.round(globalLoopErr * 1e5) / 1e5,
  reviveErr: Math.round(globalReviveErr * 1e5) / 1e5,
  perVariant: variantReports,
};

fs.writeFileSync(path.join(pkgRoot, 'verification.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
