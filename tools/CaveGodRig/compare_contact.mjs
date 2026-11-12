// The contact build converts active arms to IK + world wrist control; without runtime aim offsets
// it must reproduce the authored FK motion. node compare_contact.mjs BASE FINAL
import {load, pose} from '../ScaleBeetleRig/runtime.mjs';

const [,, basePath, finalPath] = process.argv;
const atlas = decodeURIComponent(new URL('../../animations/monsters/cave_god/cave_god.atlas', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'));
const base = load(basePath, atlas), final = load(finalPath, atlas);
const bones = ['body1', 'body3', 'head', 'shoulder1', 'shoulder2', 'arm1_1', 'arm1_2', 'arm1_3', 'fist1', 'arm2_1', 'arm2_2', 'arm2_3', 'fist2', 'player_grab'];
let worstAll = 0;
for (const anim of base.animations) {
  if (/^(arrive|earthquake|main_01|main_happy|walk|leftpunch|rightpunch)/.test(anim.name)) continue;
  let worst = 0, at = 0, bone = '', turn = 0;
  for (let i = 0; i <= Math.ceil(anim.duration * 120); i++) {
    const t = Math.min(anim.duration, i / 120);
    const a = pose(base, anim.name, t), b = pose(final, anim.name, t);
    for (const n of bones) {
      const x = a.findBone(n), y = b.findBone(n);
      const d = Math.hypot(x.worldX - y.worldX, x.worldY - y.worldY);
      const r = Math.abs(((Math.atan2(x.c, x.a) - Math.atan2(y.c, y.a)) * 180 / Math.PI + 540) % 360 - 180);
      if (d > worst) { worst = d; at = t; bone = n; }
      turn = Math.max(turn, r);
    }
  }
  worstAll = Math.max(worstAll, worst);
  if (worst > 0.5 || turn > 0.5) console.log(`${anim.name.padEnd(28)} max ${worst.toFixed(3)}px @${at.toFixed(3)} (${bone}) turn ${turn.toFixed(3)}°`);
}
console.log(`worst position gap ${worstAll.toFixed(3)}px`);
