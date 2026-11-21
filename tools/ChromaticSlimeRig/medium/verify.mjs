import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as S from '../../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';
import { load, worldVertices } from '../runtime.mjs';

const dir = process.argv[2] || 'pkg';
const data = load(`${dir}/chromatic_slime_medium.spjson`, `${dir}/chromatic_slime_medium.atlas`);
const raw = JSON.parse(fs.readFileSync(`${dir}/chromatic_slime_medium.spjson`, 'utf8'));

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
  'power_crimson',
  'attack_amber',
  'power_amber',
  'cast_azure',
  'attack_azure',
];
assert.deepEqual(data.animations.map((a) => a.name).sort(), expectedAnims.toSorted());

const skins = ['crimson', 'amber', 'azure'];
for (const sk of ['default', ...skins]) {
  assert(data.findSkin(sk), `Missing skin: ${sk}`);
}

const commonMeshSlots = [
  'shadow',
  'body',
  'aura',
  'inner_blob',
  'iris_main',
  'lid_main',
  'iris_sec',
  'lid_sec',
  'proj_1',
  'proj_2',
];
const strainExclusiveSlots = {
  crimson: ['c_shard_back', 'c_shard_tall_l', 'c_shard_tall_r', 'c_shard_low_l', 'c_shard_low_r', 'c_collars'],
  amber: ['a_stone_top', 'a_stone_flank_r', 'a_stone_l', 'a_stone_cheek'],
  azure: ['z_froth_crown', 'z_froth_left', 'z_froth_puddle'],
};

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

// 1. Verify strict per-strain slot isolation, default-hidden projectiles & hardware ClippingAttachments
for (const skName of skins) {
  const sk = poseSkin(skName, null, 0);
  for (const s of [...commonMeshSlots, ...strainExclusiveSlots[skName]]) {
    assert(sk.findSlot(s).getAttachment(), `Expected active attachment ${s} on skin ${skName}`);
  }
  assert.equal(sk.findSlot('proj_1').color.a, 0, `proj_1 should have alpha 0 in setup pose on ${skName}`);
  assert.equal(sk.findSlot('proj_2').color.a, 0, `proj_2 should have alpha 0 in setup pose on ${skName}`);

  for (const other of skins) {
    if (other === skName) continue;
    for (const s of strainExclusiveSlots[other]) {
      assert.equal(
        sk.findSlot(s).getAttachment(),
        null,
        `Cross-skin bleed! Slot ${s} (${other}) leaked onto skin ${skName}`
      );
    }
  }

  const clipMain = sk.findSlot('eye_clip_main').getAttachment();
  const clipSec = sk.findSlot('eye_clip_sec').getAttachment();
  assert(clipMain instanceof S.ClippingAttachment, `Missing ClippingAttachment eye_clip_main on ${skName}`);
  assert(clipSec instanceof S.ClippingAttachment, `Missing ClippingAttachment eye_clip_sec on ${skName}`);
  assert.equal(clipMain.endSlot.name, 'lid_main');
  assert.equal(clipSec.endSlot.name, 'lid_sec');

  const clipper = new S.SkeletonClipping();
  for (const [clipSlotName, irisSlotName] of [
    ['eye_clip_main', 'iris_main'],
    ['eye_clip_sec', 'iris_sec'],
  ]) {
    const cSlot = sk.findSlot(clipSlotName);
    const nPolys = clipper.clipStart(cSlot, cSlot.getAttachment());
    assert(nPolys > 0, `Clipping polygon failed to triangulate for ${clipSlotName} on ${skName}`);
    const iSlot = sk.findSlot(irisSlotName);
    const iAtt = iSlot.getAttachment();
    const wv = worldVertices(iSlot, iAtt);
    clipper.clipTriangles(wv, wv.length, iAtt.triangles, iAtt.triangles.length);
    assert(clipper.clippedTriangles.length >= 3, `Iris ${irisSlotName} clipped to empty on ${skName}`);
    clipper.clipEnd();
  }
}

const verts = (skin, slot, anim, t) => worldVertices(poseSkin(skin, anim, t).findSlot(slot));
const maxDiff = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

// 2. Verify loopErr < 0.05 and reviveErr < 0.08 across ALL 3 skins
let maxLoopErr = 0;
let maxReviveErr = 0;
for (const skName of skins) {
  const activeSlots = [...commonMeshSlots, ...strainExclusiveSlots[skName]].filter(
    (s) => s !== 'aura' && s !== 'proj_1' && s !== 'proj_2'
  );
  const lErr = Math.max(...activeSlots.map((s) => maxDiff(verts(skName, s, 'idle_loop', 0), verts(skName, s, 'idle_loop', 3.2))));
  const rErr = Math.max(...activeSlots.map((s) => maxDiff(verts(skName, s, 'die', 0.95), verts(skName, s, 'revive', 0))));
  maxLoopErr = Math.max(maxLoopErr, lErr);
  maxReviveErr = Math.max(maxReviveErr, rErr);
}
assert(maxLoopErr < 0.05, `Idle loop seam exceeded 0.05: ${maxLoopErr}`);
assert(maxReviveErr < 0.08, `Die/revive seam exceeded 0.08: ${maxReviveErr}`);

// 3. Verify STRICT ZERO bone scale-stretching AND STRICT ZERO gel body/base rotation across ALL 14 animations!
let maxBoneScaleDev = 0;
let maxBodyRotDeg = 0;
const setupSkel = poseSkin('crimson', null, 0);
const setupRot = Object.fromEntries(gelBodyBones.map((n) => [n, setupSkel.findBone(n).rotation]));

for (const anim of data.animations) {
  for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
    const sk = poseSkin('crimson', anim.name, Math.min(t, anim.duration));
    for (const b of sk.bones) {
      maxBoneScaleDev = Math.max(maxBoneScaleDev, Math.abs(b.scaleX - 1.0), Math.abs(b.scaleY - 1.0));
    }
    for (const bn of gelBodyBones) {
      const b = sk.findBone(bn);
      const dev = Math.abs(b.rotation - setupRot[bn]);
      maxBodyRotDeg = Math.max(maxBodyRotDeg, dev);
      assert(dev < 0.01, `Forbidden body/base rotation ${dev} deg on bone ${bn} in ${anim.name} @ t=${t.toFixed(3)}`);
    }
  }
}
assert.equal(maxBoneScaleDev, 0, `Non-zero bone scale detected: ${maxBoneScaleDev}`);
assert.equal(maxBodyRotDeg, 0, `Non-zero gel body rotation detected: ${maxBodyRotDeg}`);

// 4. Verify Planted-Base Fluid Shear, Organ Harpoon Extrusion & Real Projectiles!
const boneWorld = (sk, anim, t, bname) => {
  const b = poseSkin(sk, anim, t).findBone(bname);
  const b0 = poseSkin(sk, null, 0).findBone(bname);
  return { dx: b.worldX - b0.worldX, dy: b.worldY - b0.worldY, x: b.x - b0.x, y: b.y - b0.y };
};

// 4a. Crimson 2-hit 绯晶双撕 (attack_crimson): planted horizontal shear + 32px harpoons + 2 projectiles!
for (let t = 0; t <= 0.94; t += 1 / 60) {
  for (const fn of ['foot_l', 'foot_c', 'foot_r']) {
    assert(Math.abs(boneWorld('crimson', 'attack_crimson', t, fn).dy) < 0.05, `Crimson ${fn} lifted off floor @ ${t}`);
  }
}
const cHit1Crown = boneWorld('crimson', 'attack_crimson', 0.35, 'crown');
const cHit1LowL = boneWorld('crimson', 'attack_crimson', 0.35, 'c_shard_low_l_b');
const cHit1TallL = boneWorld('crimson', 'attack_crimson', 0.35, 'c_shard_tall_l_b');
assert(cHit1Crown.dx < -55, `Crimson Hit 1 upper fluid shear weak: ${cHit1Crown.dx}`);
assert(Math.hypot(cHit1LowL.x, cHit1LowL.y) > 28, `Crimson Hit 1 low_l harpoon thrust weak`);
assert(Math.hypot(cHit1TallL.x, cHit1TallL.y) > 28, `Crimson Hit 1 tall_l harpoon thrust weak`);
assert(poseSkin('crimson', 'attack_crimson', 0.35).findSlot('proj_1').color.a > 0.8, `Crimson proj_1 not visible @ Hit 1`);

const cReload = boneWorld('crimson', 'attack_crimson', 0.45, 'crown');
assert(cReload.dx > cHit1Crown.dx + 40, `Crimson peristaltic reload missing between Hit 1 & Hit 2`);

const cHit2Base = boneWorld('crimson', 'attack_crimson', 0.58, 'body_base');
const cHit2Crown = boneWorld('crimson', 'attack_crimson', 0.58, 'crown');
const cHit2TallR = boneWorld('crimson', 'attack_crimson', 0.58, 'c_shard_tall_r_b');
const cHit2Back = boneWorld('crimson', 'attack_crimson', 0.58, 'c_shard_back_b');
assert(cHit2Base.dx < -80, `Crimson Hit 2 horizontal surge weak: ${cHit2Base.dx}`);
assert(cHit2Crown.dy > 24, `Crimson Hit 2 vertical stretch weak: ${cHit2Crown.dy}`);
assert(Math.hypot(cHit2TallR.x, cHit2TallR.y) > 30, `Crimson Hit 2 tall_r harpoon thrust weak`);
assert(Math.hypot(cHit2Back.x, cHit2Back.y) > 30, `Crimson Hit 2 back harpoon thrust weak`);
assert(poseSkin('crimson', 'attack_crimson', 0.58).findSlot('proj_2').color.a > 0.8, `Crimson proj_2 not visible @ Hit 2`);

// 4b. Amber 琥晶撞击 (attack_amber): horizontal floor skid (-104px, dy==0) + vertical anvil stone drop!
assert(boneWorld('amber', 'attack_amber', 0.20, 'body_base').dx > 28, `Amber pull-back weak`);
assert(boneWorld('amber', 'attack_amber', 0.20, 'a_stone_top_b').y > 14, `Amber anvil stone lift weak`);
assert(boneWorld('amber', 'attack_amber', 0.36, 'body_base').dx < -100, `Amber horizontal skid weak`);
assert(Math.abs(boneWorld('amber', 'attack_amber', 0.36, 'body_base').dy) < 0.05, `Amber base lifted off y=0!`);
assert(
  Math.abs(boneWorld('amber', 'attack_amber', 0.35, 'body_base').dx - boneWorld('amber', 'attack_amber', 0.42, 'body_base').dx) < 0.8,
  `Amber 5-frame seismic hit-stop drifted`
);
assert(boneWorld('amber', 'attack_amber', 0.38, 'a_stone_top_b').y < -10, `Amber anvil stone drop weak`);
assert(poseSkin('amber', 'attack_amber', 0.42).findSlot('proj_1').color.a > 0.7, `Amber shock-chip proj_1 missing`);

// 4c. Amber 岩壳固守 (power_amber): blast-door lock + flat horizontal shove
assert(boneWorld('amber', 'power_amber', 0.45, 'a_stone_top_b').x < -9, `Amber top slab did not slide inward`);
assert(boneWorld('amber', 'power_amber', 0.45, 'a_stone_top_b').y < -10, `Amber top slab did not slide downward`);
assert(boneWorld('amber', 'power_amber', 0.62, 'body_base').dx < -44, `Amber flat shield-shove weak`);

// 4d. Azure 蚀液喷淋 (cast_azure): 100% planted vertical geyser (+38px) + dual parabolic globs!
for (let t = 0; t <= 1.05; t += 1 / 60) {
  const b = boneWorld('azure', 'cast_azure', t, 'body_base');
  assert(Math.abs(b.dx) < 0.05 && Math.abs(b.dy) < 0.05, `Azure cast_azure base drifted off planted origin @ ${t}`);
}
assert(boneWorld('azure', 'cast_azure', 0.45, 'crown').dy > 34, `Azure vertical geyser weak`);
assert(boneWorld('azure', 'cast_azure', 0.45, 'flank_l').dx > 18, `Azure left flank squeeze weak`);
assert(boneWorld('azure', 'cast_azure', 0.45, 'flank_r').dx < -18, `Azure right flank squeeze weak`);
assert(poseSkin('azure', 'cast_azure', 0.64).findSlot('proj_1').color.a > 0.8, `Azure parabolic proj_1 missing`);
assert(boneWorld('azure', 'cast_azure', 0.80, 'proj_1_b').dx < -300, `Azure proj_1 did not fly far left`);
assert(poseSkin('azure', 'cast_azure', 0.72).findSlot('proj_2').color.a > 0.8, `Azure parabolic proj_2 missing`);
assert(boneWorld('azure', 'cast_azure', 0.88, 'proj_2_b').dx < -350, `Azure proj_2 did not fly far left`);

// 4e. Azure 酸雾涌潮 (attack_azure): differential horizontal foot stretch (-120px vs -50px) & pancake splat!
const azFootL = boneWorld('azure', 'attack_azure', 0.36, 'foot_l');
const azFootR = boneWorld('azure', 'attack_azure', 0.36, 'foot_r');
const azCrown = boneWorld('azure', 'attack_azure', 0.36, 'crown');
assert(azFootL.dx < -114 && Math.abs(azFootL.dy) < 0.05, `Azure leading foot_l stretch wrong: ${JSON.stringify(azFootL)}`);
assert(azFootR.dx > -56 && azFootR.dx < -42 && Math.abs(azFootR.dy) < 0.05, `Azure trailing foot_r stretch wrong: ${JSON.stringify(azFootR)}`);
assert(azCrown.dy < -24, `Azure pancake splat too high: ${azCrown.dy}`);

// 5. Dense 60 FPS triangle inversion scan across ALL 3 skins and ALL 14 animations!
function area(v, i, tris) {
  const [a, b, c] = tris.slice(i, i + 3).map((k) => k * 2);
  return (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
}

let flipped = 0;
let maxDisp = 0;
for (const skin of skins) {
  const skinObj = data.findSkin(skin);
  const activeMeshSlots = [...commonMeshSlots.filter((s) => s !== 'aura'), ...strainExclusiveSlots[skin]];
  const setup = Object.fromEntries(activeMeshSlots.map((s) => [s, verts(skin, s, null, 0)]));
  for (const slot of activeMeshSlots) {
    const sIdx = data.slots.findIndex((s) => s.name === slot);
    const att = skinObj.getAttachment(sIdx, slot);
    const tris = att.triangles;
    const base = Array.from({ length: tris.length / 3 }, (_, i) => area(setup[slot], i * 3, tris));
    for (const anim of data.animations) {
      for (let t = 0; t <= anim.duration + 1e-6; t += 1 / 60) {
        const v = verts(skin, slot, anim.name, Math.min(t, anim.duration));
        for (let i = 0; i < base.length; i++) {
          if (area(v, i * 3, tris) * base[i] < -0.01) flipped++;
        }
        if (slot === 'body') {
          for (let i = 0; i < v.length; i++) maxDisp = Math.max(maxDisp, Math.abs(v[i] - setup.body[i]));
        }
      }
    }
  }
}
assert.equal(flipped, 0, `Inverted triangles in Medium: ${flipped}`);
assert(maxDisp > 110, `Medium displacement too subtle: ${maxDisp}`);

const report = {
  result: 'PASS',
  skeleton: 'chromatic_slime_medium',
  bones: data.bones.length,
  slots: data.slots.length,
  animations: data.animations.length,
  skins,
  maxBoneScaleDev,
  maxBodyRotDeg,
  invertedTriangles: flipped,
  loopErr: Math.round(maxLoopErr * 1e5) / 1e5,
  reviveErr: Math.round(maxReviveErr * 1e5) / 1e5,
  maxDisplacementPx: Math.round(maxDisp * 10) / 10,
  crimsonHit1ShearDx: Math.round(cHit1Crown.dx * 10) / 10,
  crimsonHit2BaseDx: Math.round(cHit2Base.dx * 10) / 10,
  amberSkidDx: Math.round(boneWorld('amber', 'attack_amber', 0.36, 'body_base').dx * 10) / 10,
  azureGeyserDy: Math.round(boneWorld('azure', 'cast_azure', 0.45, 'crown').dy * 10) / 10,
  azureFootLeadDx: Math.round(azFootL.dx * 10) / 10,
  azureFootTrailDx: Math.round(azFootR.dx * 10) / 10,
};
fs.writeFileSync(`${dir}/verification.json`, JSON.stringify(report, null, 2));
console.log(report);
