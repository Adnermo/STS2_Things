import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as S from '../../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';
import { load, worldVertices } from '../runtime.mjs';

const dir = process.argv[2] || 'pkg';
const data = load(`${dir}/chromatic_slime_small.spjson`, `${dir}/chromatic_slime_small.atlas`);
const raw = JSON.parse(fs.readFileSync(`${dir}/chromatic_slime_small.spjson`, 'utf8'));

assert.equal(data.bones.length, 17, `Expected 17 bones, got ${data.bones.length}`);
assert.equal(data.slots.length, 11, `Expected 11 slots, got ${data.slots.length}`);

const expectedAnims = [
  'idle_loop',
  'spawn',
  'attack',
  'cast',
  'power_up',
  'hurt',
  'die',
  'revive',
  'attack_crimson',
  'attack_amber',
  'cast_azure',
  'attack_azure',
];
assert.deepEqual(data.animations.map((a) => a.name).sort(), expectedAnims.toSorted());

const skins = ['crimson', 'amber', 'azure'];
for (const sk of ['default', ...skins]) {
  assert(data.findSkin(sk), `Missing skin: ${sk}`);
}

const gelBodyBones = [
  'root',
  'shadow_b',
  'body_base',
  'foot_l',
  'foot_c',
  'foot_r',
  'flank_l',
  'flank_r',
  'belly_mid',
  'crown',
  'inner_core',
];
assert.equal(gelBodyBones.length, 11);

const meshSlots = ['shadow', 'body', 'aura', 'inner_blob', 'iris', 'eyelid', 'crest_l', 'crest_c', 'crest_r', 'spit_glob'];
const solidMeshSlots = ['shadow', 'body', 'inner_blob', 'iris', 'eyelid', 'crest_l', 'crest_c', 'crest_r', 'spit_glob'];

// Verify hardware `eye_clip` ClippingAttachment across all 3 skins + default
const clipSlotIdx = data.slots.findIndex((s) => s.name === 'eye_clip');
assert(clipSlotIdx >= 0, 'Missing eye_clip slot');
for (const sk of ['default', ...skins]) {
  const att = data.findSkin(sk).getAttachment(clipSlotIdx, 'eye_clip');
  assert(att instanceof S.ClippingAttachment, `eye_clip is not ClippingAttachment in skin ${sk}`);
  assert.equal(att.endSlot.name, 'eyelid', `eye_clip endSlot must be eyelid in skin ${sk}`);
  assert(att.worldVerticesLength >= 40, `eye_clip polygon too coarse in skin ${sk}`);
}

function poseSkin(skinName, animName, t) {
  const sk = new S.Skeleton(data);
  sk.setSkinByName(skinName);
  sk.setToSetupPose();
  if (animName) {
    data.findAnimation(animName).apply(sk, 0, t, false, [], 1, S.MixBlend.setup, S.MixDirection.mixIn);
  }
  sk.updateWorldTransform(S.Physics.none);
  return sk;
}

const verts = (skin, slot, anim, t) => worldVertices(poseSkin(skin, anim, t).findSlot(slot));
const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

// 1. Seam checks across ALL 3 skins (`idle_loop` 0 vs 2.80s, `die` 0.80s vs `revive` 0.0s)
let loopErr = 0;
let reviveErr = 0;
for (const skin of skins) {
  for (const s of solidMeshSlots) {
    loopErr = Math.max(loopErr, maxDiff(verts(skin, s, 'idle_loop', 0), verts(skin, s, 'idle_loop', 2.80)));
    reviveErr = Math.max(reviveErr, maxDiff(verts(skin, s, 'die', 0.80), verts(skin, s, 'revive', 0)));
  }
}
assert(loopErr < 0.05, `Idle loop seam exceeded 0.05: ${loopErr}`);
assert(reviveErr < 0.08, `Die/revive seam exceeded 0.08: ${reviveErr}`);

// 2. STRICT ZERO bone scaling (all 17 bones) & STRICT ZERO body rotation (all 11 gel body bones) at 60 FPS
let maxBoneScaleDev = 0;
let maxBodyRotDeg = 0;

function wrapDeg(d) {
  let r = ((d + 180) % 360 + 360) % 360 - 180;
  return Math.abs(r);
}

for (const skin of skins) {
  for (const anim of data.animations) {
    for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
      const sk = poseSkin(skin, anim.name, Math.min(t, anim.duration));
      for (const b of sk.bones) {
        maxBoneScaleDev = Math.max(maxBoneScaleDev, Math.abs(b.scaleX - 1.0), Math.abs(b.scaleY - 1.0));
      }
      for (const bName of gelBodyBones) {
        const gb = sk.findBone(bName);
        assert(gb, `Missing gel body bone: ${bName}`);
        maxBodyRotDeg = Math.max(maxBodyRotDeg, wrapDeg(gb.rotation), wrapDeg(gb.getWorldRotationX()));
      }
    }
  }
}
assert.equal(maxBoneScaleDev, 0, `Non-zero bone scale detected: ${maxBoneScaleDev}`);
assert(maxBodyRotDeg < 0.01, `Non-zero gel body rotation detected: ${maxBodyRotDeg}`);

// 3. `spit_glob` setup alpha, idle silence, projectile launch/travel (<= -300px), and clean end alpha=0
const basePose = poseSkin('crimson', null, 0);
assert.equal(basePose.findSlot('spit_glob').color.a, 0, `Expected setup spit_glob alpha=0, got ${basePose.findSlot('spit_glob').color.a}`);

const idleAnim = data.findAnimation('idle_loop');
let maxIdleSpitAlpha = 0;
for (let t = 0; t <= idleAnim.duration + 1e-6; t += 1 / 60) {
  const sk = poseSkin('azure', 'idle_loop', Math.min(t, idleAnim.duration));
  maxIdleSpitAlpha = Math.max(maxIdleSpitAlpha, sk.findSlot('spit_glob').color.a);
}
assert.equal(maxIdleSpitAlpha, 0, `spit_glob must stay alpha=0 in idle_loop, got ${maxIdleSpitAlpha}`);

const baseSpitX = basePose.findBone('spit_glob_b').worldX;
const baseSpitY = basePose.findBone('spit_glob_b').worldY;

const spitStats = {};
for (const [skin, animName, minLeftTravel] of [
  ['azure', 'cast_azure', -300],
  ['crimson', 'cast', -300],
  ['crimson', 'attack_crimson', -300],
  ['amber', 'attack_amber', -160],
  ['azure', 'attack_azure', -210],
]) {
  const anim = data.findAnimation(animName);
  let maxAlpha = 0;
  let minDxVisible = Infinity;
  let minDxBright = Infinity;
  let maxDyVisible = -Infinity;
  for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
    const sk = poseSkin(skin, animName, Math.min(t, anim.duration));
    const a = sk.findSlot('spit_glob').color.a;
    const dx = sk.findBone('spit_glob_b').worldX - baseSpitX;
    const dy = sk.findBone('spit_glob_b').worldY - baseSpitY;
    maxAlpha = Math.max(maxAlpha, a);
    if (a > 0.05) {
      minDxVisible = Math.min(minDxVisible, dx);
      maxDyVisible = Math.max(maxDyVisible, dy);
    }
    if (a > 0.85) {
      minDxBright = Math.min(minDxBright, dx);
    }
  }
  const endAlpha = poseSkin(skin, animName, anim.duration).findSlot('spit_glob').color.a;
  assert(maxAlpha > 0.85, `${animName}: spit_glob peak alpha too low (${maxAlpha})`);
  assert(minDxVisible <= minLeftTravel, `${animName}: spit_glob left travel ${minDxVisible} > ${minLeftTravel}`);
  if (minLeftTravel <= -300) {
    assert(minDxBright <= -300, `${animName}: spit_glob bright (a>0.85) left travel ${minDxBright} > -300`);
  }
  assert(endAlpha < 1e-4, `${animName}: spit_glob end alpha not 0 (${endAlpha})`);
  spitStats[animName] = {
    maxAlpha: Math.round(maxAlpha * 100) / 100,
    minDx: Math.round(minDxVisible * 10) / 10,
    maxDy: Math.round(maxDyVisible * 10) / 10,
    endAlpha: Math.round(endAlpha * 1e4) / 1e4,
  };
}

// 4. Planted-base & bespoke choreography invariants
const baseBBX = basePose.findBone('body_base').worldX;
const baseBBY = basePose.findBone('body_base').worldY;
const dxAt = (skin, anim, t) => poseSkin(skin, anim, t).findBone('body_base').worldX - baseBBX;
const dyAt = (skin, anim, t) => poseSkin(skin, anim, t).findBone('body_base').worldY - baseBBY;

// Verify flat-floor base (body_base.y === 0 and foot_*.y === 0) across horizontal attacks & planted casts
for (const animName of ['attack', 'cast', 'power_up', 'hurt', 'attack_crimson', 'attack_amber', 'cast_azure', 'attack_azure']) {
  const anim = data.findAnimation(animName);
  for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
    const sk = poseSkin('crimson', animName, Math.min(t, anim.duration));
    assert(Math.abs(sk.findBone('body_base').worldY - baseBBY) < 0.01, `${animName}: body_base lifted off floor at t=${t}`);
    for (const fn of ['foot_l', 'foot_c', 'foot_r']) {
      const fb = sk.findBone(fn);
      assert(Math.abs(fb.y - fb.data.y) < 0.01, `${animName}: ${fn} lifted off floor at t=${t}`);
    }
    if (animName === 'cast' || animName === 'cast_azure') {
      assert(Math.abs(sk.findBone('body_base').worldX - baseBBX) < 0.01, `${animName}: body_base shifted horizontally at t=${t}`);
    }
  }
}

const localDx = (sk, name) => {
  const b = sk.findBone(name);
  return b.x - b.data.x;
};
const localDy = (sk, name) => {
  const b = sk.findBone(name);
  return b.y - b.data.y;
};

// Spawn: vertical drop onto y=0 @ 0.34s, raindrop narrow airborne -> wide splat @ 0.34s
assert(dyAt('azure', 'spawn', 0.0) > 95, `spawn initial drop height too low: ${dyAt('azure', 'spawn', 0.0)}`);
assert(Math.abs(dyAt('azure', 'spawn', 0.34)) < 0.05, `spawn not landed at 0.34s: ${dyAt('azure', 'spawn', 0.34)}`);
assert(localDx(poseSkin('azure', 'spawn', 0.05), 'foot_l') > 5, 'spawn airborne foot_l did not narrow inward');
assert(localDx(poseSkin('azure', 'spawn', 0.35), 'foot_l') < -12, 'spawn landing foot_l did not splat wide');

// Standard attack: pull back +22 -> foot_l leads -96px left into 4f hit-stop
assert(dxAt('crimson', 'attack', 0.15) > 18, `Standard attack coil weak: ${dxAt('crimson', 'attack', 0.15)}`);
const atkFootLDx = poseSkin('crimson', 'attack', 0.34).findBone('foot_l').worldX - basePose.findBone('foot_l').worldX;
assert(atkFootLDx <= -94, `Standard attack foot_l lead expected <= -94, got ${atkFootLDx}`);

// Bespoke 1: attack_crimson (-76px body_base + -26px crown shear + +20..+24px axial harpoon shoot + 4f hit-stop)
assert(Math.abs(dxAt('crimson', 'attack_crimson', 0.34) - (-76)) < 1.5, `attack_crimson body_base dx: ${dxAt('crimson', 'attack_crimson', 0.34)}`);
assert(Math.abs(dxAt('crimson', 'attack_crimson', 0.34) - dxAt('crimson', 'attack_crimson', 0.39)) < 1.0, 'attack_crimson missing 4f hit-stop');
assert(Math.abs(localDx(poseSkin('crimson', 'attack_crimson', 0.34), 'crown') - (-26)) < 1.5, 'attack_crimson crown -26px shear missing');
for (const cb of ['crest_l_b', 'crest_c_b', 'crest_r_b']) {
  const skHit = poseSkin('crimson', 'attack_crimson', 0.34);
  const dist = Math.hypot(localDx(skHit, cb), localDy(skHit, cb));
  assert(dist >= 18.5 && dist <= 26.5, `attack_crimson ${cb} axial shoot expected ~20..24px, got ${dist}`);
}

// Bespoke 2: attack_amber (-92px flat skid + 5f hit-stop + -16px stone anvil visor drop + Block crouch)
assert(Math.abs(dxAt('amber', 'attack_amber', 0.34) - (-92)) < 1.5, `attack_amber skid dx: ${dxAt('amber', 'attack_amber', 0.34)}`);
assert(Math.abs(dxAt('amber', 'attack_amber', 0.34) - dxAt('amber', 'attack_amber', 0.41)) < 1.0, 'attack_amber missing 5f hit-stop');
const amberVisorDrop = localDy(poseSkin('amber', 'attack_amber', 0.36), 'crest_c_b');
assert(amberVisorDrop <= -14.5, `attack_amber stone visor drop weak: ${amberVisorDrop}`);
const amberBraceY = localDy(poseSkin('amber', 'attack_amber', 0.58), 'crest_c_b');
assert(amberBraceY <= -9.5, `attack_amber Block crouch visor missing: ${amberBraceY}`);

// Bespoke 3: cast_azure (+32px vertical crown pump -> -14px nozzle pop + +95px high parabolic arc)
const azurePumpY = poseSkin('azure', 'cast_azure', 0.38).findBone('crown').worldY - basePose.findBone('crown').worldY;
assert(azurePumpY >= 29, `cast_azure vertical crown pump weak: ${azurePumpY}`);
assert(spitStats.cast_azure.maxDy >= 88, `cast_azure parabolic apex too low: ${spitStats.cast_azure.maxDy}`);

// Bespoke 4: attack_azure (foot_l -108px, foot_r -44px = 64px stretch along y=0 + -24px pancake splat)
const azFootL = poseSkin('azure', 'attack_azure', 0.35).findBone('foot_l').worldX - basePose.findBone('foot_l').worldX;
const azFootR = poseSkin('azure', 'attack_azure', 0.35).findBone('foot_r').worldX - basePose.findBone('foot_r').worldX;
assert(Math.abs(azFootL - (-108)) < 2.0, `attack_azure foot_l expected -108px, got ${azFootL}`);
assert(Math.abs(azFootR - (-44)) < 2.0, `attack_azure foot_r expected -44px, got ${azFootR}`);
assert(Math.abs((azFootR - azFootL) - 64) < 2.0, `attack_azure base stretch expected 64px, got ${azFootR - azFootL}`);

// 5. Exhaustive triangle inversion & hardware clipping sweep across all 3 skins & all 12 clips at 60 FPS
function area(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((k) => k * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}

let flipped = 0;
let maxDisp = 0;
const clipper = new S.SkeletonClipping();

for (const skin of skins) {
  const skinObj = data.findSkin(skin);
  const setup = Object.fromEntries(meshSlots.map((s) => [s, verts(skin, s, null, 0)]));

  for (const slot of meshSlots) {
    const sIdx = data.slots.findIndex((s) => s.name === slot);
    const att = skinObj.getAttachment(sIdx, slot);
    assert(att, `Missing attachment ${slot} in skin ${skin}`);
    const tris = att.triangles;
    const base = Array.from({ length: tris.length / 3 }, (_, i) => area(setup[slot], i * 3, tris));

    for (const anim of data.animations) {
      for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
        const v = verts(skin, slot, anim.name, Math.min(t, anim.duration));
        for (let i = 0; i < base.length; i++) {
          if (area(v, i * 3, tris) * base[i] < -0.01) flipped++;
        }
        if (slot === 'body') {
          for (let i = 0; i < v.length; i++) {
            maxDisp = Math.max(maxDisp, Math.abs(v[i] - setup.body[i]));
          }
        }
      }
    }
  }

  // Exercise real Spine SkeletonClipping on eye_clip -> iris -> eyelid at key poses
  const cWhite = new S.Color(1, 1, 1, 1);
  const cBlack = new S.Color(0, 0, 0, 0);
  for (const [animName, t, expectClosed] of [
    ['idle_loop', 0, false],
    ['idle_loop', 1.27, true],
    ['attack_crimson', 0.34, false],
    ['attack_amber', 0.36, false],
    ['die', 0.65, true],
  ]) {
    const sk = poseSkin(skin, animName, t);
    const clipSlot = sk.findSlot('eye_clip');
    assert(clipper.clipStart(clipSlot, clipSlot.attachment) > 0, `clipStart failed for ${skin}/${animName}`);
    for (const childName of ['iris', 'eyelid']) {
      const childSlot = sk.findSlot(childName);
      const childAtt = childSlot.attachment;
      const wv = worldVertices(childSlot, childAtt);
      clipper.clipTriangles(wv, wv.length, childAtt.triangles, childAtt.triangles.length, childAtt.uvs, cWhite, cBlack, false);
      if (childName === 'iris') {
        assert(clipper.clippedTriangles.length > 0, `iris clipped to 0 triangles in ${skin}/${animName}@${t}`);
      }
      if (childName === 'eyelid' && expectClosed) {
        assert(clipper.clippedTriangles.length > 0, `eyelid clipped to 0 triangles in ${skin}/${animName}@${t}`);
      }
      clipper.clipEndWithSlot(childSlot);
    }
    assert.equal(clipper.isClipping(), false, `eye_clip did not terminate after eyelid in ${skin}`);
  }
}

assert.equal(flipped, 0, `Inverted triangles in Small: ${flipped}`);
assert(maxDisp > 90, `Small displacement too subtle: ${maxDisp}`);

// 6. Event assertions across standard + bespoke clips
const expectedEvents = {
  spawn: ['spawn_land', 0.34],
  attack: ['attack_contact', 0.34],
  cast: ['cast_release', 0.44],
  power_up: ['power_peak', 0.40],
  die: ['rupture_burst', 0.26],
  attack_crimson: ['attack_contact', 0.34],
  attack_amber: ['attack_contact', 0.34],
  cast_azure: ['cast_release', 0.44],
  attack_azure: ['attack_contact', 0.34],
};
for (const [animName, [evName, evTime]] of Object.entries(expectedEvents)) {
  const evs = raw.animations[animName]?.events;
  assert(evs && evs.length === 1, `Missing event on ${animName}`);
  assert.equal(evs[0].name, evName, `Wrong event name on ${animName}`);
  assert(Math.abs(evs[0].time - evTime) < 1e-4, `Wrong event time on ${animName}: ${evs[0].time}`);
}

const report = {
  result: 'PASS',
  skeleton: 'chromatic_slime_small',
  bones: data.bones.length,
  slots: data.slots.length,
  skins,
  animations: data.animations.length,
  loopErr: Math.round(loopErr * 1e5) / 1e5,
  reviveErr: Math.round(reviveErr * 1e5) / 1e5,
  maxBoneScaleDev: Math.round(maxBoneScaleDev * 1e6) / 1e6,
  maxBodyRotDeg: Math.round(maxBodyRotDeg * 1e6) / 1e6,
  invertedTriangles: flipped,
  maxDisplacementPx: Math.round(maxDisp * 10) / 10,
  spitStats,
};
fs.writeFileSync(`${dir}/verification.json`, JSON.stringify(report, null, 2));
console.log(report);
