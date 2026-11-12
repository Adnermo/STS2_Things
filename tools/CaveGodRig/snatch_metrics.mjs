// Card-snatch wrist metrics in screen pixels, measured like CaveGodCardSnatchRegression (120 Hz).
// node tools/CaveGodRig/snatch_metrics.mjs [SPJSON]
import {load, pose} from '../ScaleBeetleRig/runtime.mjs';

const here = p => decodeURIComponent(new URL(p, import.meta.url).pathname.replace(/^\/(\w:)/, '$1'));
const data = load(process.argv[2] ?? here('../../animations/monsters/cave_god/cave_god.spjson'),
  here('../../animations/monsters/cave_god/cave_god.atlas'));
const SCREEN = 1.3 * 0.75;  // rig scale x encounter background scale
for (const [clip, bone] of [['card_snatch', 'arm1_3'], ['card_snatch_right', 'arm2_3'], ['card_snatch_angry', 'arm1_3']]) {
  let prev, prevStep, start, worst = 0, at = 0, minLunge = Infinity, peakWind = 0, peakLunge = 0, top = Infinity;
  for (let i = 0; i <= 384; i++) {
    const t = i / 120, b = pose(data, clip, t).findBone(bone);
    const p = [b.worldX * SCREEN, -b.worldY * SCREEN];
    start ??= p;
    if (prev) {
      const step = [p[0] - prev[0], p[1] - prev[1]], len = Math.hypot(...step);
      if (t <= 1.0) { peakWind = Math.max(peakWind, len); top = Math.min(top, p[1]); }
      if (t > 1.0 && t < 1.36) peakLunge = Math.max(peakLunge, len);
      if (t > 1.09 && t < 1.27) minLunge = Math.min(minLunge, len);
      if (prevStep) {
        const d = Math.hypot(step[0] - prevStep[0], step[1] - prevStep[1]);
        if (d > worst) { worst = d; at = t; }
      }
      prevStep = step;
    }
    prev = p;
  }
  console.log(`${clip}: velocity change ${worst.toFixed(2)}@${at.toFixed(3)} (limit 3), windup ${(start[1] - top).toFixed(0)}px (min 300), ` +
    `wind/lunge peak ${peakWind.toFixed(1)}/${peakLunge.toFixed(1)} (x1.3), lunge min ${minLunge.toFixed(1)} (min 6)`);
}
