"""Print shoulder/elbow/wrist world positions of authored clips (skeleton space, Y up; screen top is y≈565).

python tools/CaveGodRig/inspect_pose.py central_slam@0.3,1.28 grab_hold@0
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import build  # noqa: E402
import clips as C  # noqa: E402
from rig import ARMS, Rig, copy_pose, load_source  # noqa: E402

SCREEN_TOP = 565.0


def main(specs: list[str]) -> None:
    baker = build.Baker(load_source())
    named = {c.name: c for c in build.all_clips()}
    for spec in specs:
        name, times = spec.split("@")
        clip = named[name]
        for t in (float(v) for v in times.split(",")):
            vals = baker.channel_arrays(clip, [t])
            pose = copy_pose(baker.rest)
            baker.apply(pose, {ch: vals[ch][0] for ch in C.CHANNELS})
            world = baker.rig.world(pose)
            parts = []
            for side, arm in ARMS.items():
                intent = clip.arms.get(side)
                if intent is None:
                    continue
                baker.rig.solve_arm(pose, side, intent.wrist(t), 0.0, world)
                hand = pose[arm["hand"]]
                hand[0] = baker.rest[arm["hand"]][0] + build.BEND_SIGN[side] * build.soft_band(intent.bend(t), *build.BEND_BAND)
                w = baker.rig.world(pose)
                s, e, h = w[arm["upper"]], w[arm["fore"]], w[arm["hand"]]
                flag = " OFFSCREEN" if e[5] > SCREEN_TOP - 25 else ""
                flag += " HANGING" if h[5] > s[5] - 80 and e[5] > h[5] + 40 else ""
                parts.append(f"arm{side} S({s[4]:5.0f},{s[5]:4.0f}) E({e[4]:5.0f},{e[5]:4.0f}) W({h[4]:5.0f},{h[5]:4.0f})"
                             f" hand {Rig.angle(h):5.0f}°{flag}")
            print(f"{name}@{t:.2f}  " + "  |  ".join(parts))


if __name__ == "__main__":
    main(sys.argv[1:])
