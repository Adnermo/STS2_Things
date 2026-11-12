"""Bake the 活体巨岩 motion set into a Spine base for scripts/build_cavegod_contact.mjs.

python tools/CaveGodRig/build.py [--only clip,clip] [--out build/cavegod_motion/motion_base.spjson]
"""
from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from dataclasses import replace
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import clips as C  # noqa: E402
from motion import clamp, follow, pendulum, seg, shake, smoother, tremor, window  # noqa: E402
from rig import ARMS, ROOT, ROT, SX, SY, X, Y, Rig, copy_pose, load_source, sample_timeline_start, unwrap  # noqa: E402

DT = 1.0 / 240.0
SCREEN_TOP = 565.0   # skeleton-space y of the top screen edge in the encounter framing
OUT_HZ = 120  # matches the 120 Hz probes; 60 Hz linear keys doubled per-frame velocity jumps
ROOT_ORIGIN = (-0.65, 0.0)
KEEP_BONES = ("root", "arm1_IK", "arm2_IK", "body1", "tail_IK", "body2", "body_bottom", "body3", "body_bottom2", "head",
              "shoulder1", "shoulder2", "arm1_1", "arm2_1", "beard", "horn1", "horn2", "arm1_2", "arm2_2", "arm1_2_side",
              "arm1_3", "arm2_2_side", "arm2_3", "fist1", "fist2", "player_grab", "impact_center")
OVERLAY_BONES = ("body1", "body2", "body3", "head", "beard", "horn1", "horn2", "shoulder1", "shoulder2")
TOLERANCE = {"rotate": 0.025, "translate": 0.06, "scale": 0.0004}
ATTACH = {1: {"open": "arm1_3_2", "fist": "arm1_3"}, 2: {"open": "arm2_3_2", "fist": "arm2_3"}}
# Inward wrist bend (degrees from the rest wrist) that both weighted forearm/hand meshes tolerate
# without folding triangles (local sweep: arm1_3 -30..8, arm2_3 -45..43 around rests -27 / +28).
BEND_BAND = (-8.0, 38.0)
BEND_SIGN = {1: 1.0, 2: -1.0}


def soft_band(value: float, lo: float, hi: float) -> float:
    centre, half = (lo + hi) / 2, (hi - lo) / 2
    knee = 0.7 * half
    off = value - centre
    if abs(off) <= knee:
        return value
    excess = (abs(off) - knee) / (half - knee)
    return centre + math.copysign(knee + (half - knee) * math.tanh(excess), off)


# ---------------------------------------------------------------------------- variants

def mirrored(clip: C.Clip) -> C.Clip:
    flip = {"b1x", "b1r", "b2r", "b3r", "hy", "hr", "brd", "bb"}
    swap = {"s1x": "s2x", "s1o": "s2o", "s1r": "s2r", "f1": "f2", "s2x": "s1x", "s2o": "s1o", "s2r": "s1r", "f2": "f1"}
    ch = {}
    for name, fn in clip.channels.items():
        target = swap.get(name, name)
        negate = name in flip or name in ("s1r", "s2r")
        ch[target] = (lambda f: (lambda t: -f(t)))(fn) if negate else fn
    arms = {}
    for side, a in clip.arms.items():
        # The wrist bend is defined inward for both arms, so it carries over unchanged.
        arms[3 - side] = C.Arm((lambda f, inv: (lambda t: C.mirror_point(f(t), inv)))(a.wrist, side == 2),
                               a.bend, a.scale, list(a.attach))
    # grab(t, palm) works from the solved palm of whichever arm holds the captive.
    return replace(clip, name=clip.name + "_right", channels=ch, arms=arms)


def mirror_channels(values: dict) -> dict:
    out = {}
    for name, v in values.items():
        if name in ("b1x", "b1r", "b2r", "b3r", "hy", "hr", "brd", "bb"):
            out[name] = -v
        elif name[:1] in ("s", "f") and name[1:2] in ("1", "2"):
            other = name[0] + ("2" if name[1] == "1" else "1") + name[2:]
            out[other] = -v if name.endswith("r") else v
        else:
            out[name] = v
    return out


def angry(clip: C.Clip) -> C.Clip:
    return replace(clip, name=clip.name + "_angry", posture=clip.posture or (lambda _t: 1.0))


def all_clips() -> list[C.Clip]:
    always = lambda _t: 1.0  # noqa: E731
    out = [C.idle(False), replace(C.idle(True), posture=always), C.hit_recoil(False), C.hit_recoil(True)]
    for build in C.ATTACK_BUILDERS:
        base = build()
        out += [base, angry(base)]
    for build in C.SIDED_BUILDERS:
        base = build()
        right = mirrored(base)
        out += [base, angry(base), right, angry(right)]
    for is_angry in (False, True):
        hold = C.grab_hold(is_angry)
        hold = replace(hold, posture=(lambda _t: 1.0) if is_angry else None)
        right = mirrored(hold)
        out += [hold, right]
        weak = C.weak_idle(is_angry)
        weak = replace(weak, posture=(lambda _t: 1.0) if is_angry else None)
        weak_right = mirrored(weak)
        out += [weak, weak_right]
    out.append(C.main_angry())
    death = C.hide()
    out += [death, angry(death)]
    # angry() appends the suffix after _right; restore the project naming (base[_right][_angry]).
    for clip in out:
        clip.name = clip.name.replace("_angry_right", "_right_angry")
    return out


# ---------------------------------------------------------------------------- baking

class Baker:
    def __init__(self, data: dict):
        self.data = data
        self.rig = Rig(data)
        self.rest = sample_timeline_start(self.rig, data["animations"]["idle_front"])
        world = self.rig.world(self.rest)
        for side, arm in ARMS.items():
            m = world[arm["hand"]]
            assert math.hypot(m[4] - C.REST_WRIST[side][0], m[5] - C.REST_WRIST[side][1]) < 0.05, side
        self.report: dict[str, dict] = {}

    def apply(self, pose: dict, v: dict) -> None:
        p = pose
        p["body1"][X] += v["b1x"]; p["body1"][Y] += v["b1y"]; p["body1"][ROT] += v["b1r"]
        p["body2"][ROT] += v["b2r"]; p["body2"][X] += v["b2t"]
        p["body3"][ROT] += v["b3r"]; p["body3"][X] += v["b3t"]
        p["head"][X] += v["hx"]; p["head"][Y] += v["hy"]; p["head"][ROT] += v["hr"]; p["head"][SX] *= v["hsx"]
        p["beard"][ROT] += v["brd"]; p["beard"][SX] *= v["bsx"]
        p["horn1"][SX] *= v["horn"]; p["horn2"][SX] *= v["horn"]
        p["shoulder1"][X] += v["s1x"]; p["shoulder1"][Y] += v["s1o"]; p["shoulder1"][ROT] += v["s1r"]
        p["shoulder2"][X] += v["s2x"]; p["shoulder2"][Y] -= v["s2o"]; p["shoulder2"][ROT] += v["s2r"]
        p["fist1"][X] += v["f1"]; p["fist2"][X] += v["f2"]
        p["body_bottom"][ROT] += v["bb"]
        # The hidden lower trunk anchor travels with the body so body_bottom never folds (deep sinks).
        p["tail_IK"][X] += v["b1x"]; p["tail_IK"][Y] += v["b1y"]

    def channel_arrays(self, clip: C.Clip, times: list[float]) -> dict[str, list[float]]:
        vals = {}
        for ch in C.CHANNELS:
            neutral = 1.0 if ch in C.MULTIPLY else 0.0
            fn = clip.channels.get(ch)
            vals[ch] = [fn(t) for t in times] if fn else [neutral] * len(times)
        if clip.posture:
            for i, t in enumerate(times):
                w = clip.posture(t)
                for ch, off in C.ANGRY.items():
                    if ch in C.MULTIPLY:
                        vals[ch][i] *= 1 + (off - 1) * w
                    else:
                        vals[ch][i] += off * w
        return vals

    def secondary(self, clip: C.Clip, times: list[float], vals: dict) -> dict[str, list[float]]:
        n = len(times)
        sec = {ch: [0.0] * n for ch in C.CHANNELS}
        boost = 1.3 if clip.name.endswith("_angry") else 1.0
        for t0, s in clip.impacts:
            s *= boost
            for i, t in enumerate(times):
                sec["b3r"][i] += shake(t, t0, 0.9 * s, 8.5, 6.5)
                sec["b1y"][i] += shake(t, t0, 3.6 * s, 7.0, 7.5, 1.2)
                sec["b2t"][i] += shake(t, t0, 2.0 * s, 9.5, 8.0, 0.4)
                sec["hr"][i] += shake(t, t0, 1.5 * s, 6.0, 5.0, 2.0)
                sec["hx"][i] += shake(t, t0, 3.0 * s, 5.5, 5.5, 0.6)
                sec["s1x"][i] += shake(t, t0, 2.4 * s, 9.0, 7.0, 0.3)
                sec["s2x"][i] += shake(t, t0, 2.4 * s, 9.0, 7.0, 1.1)
                sec["horn"][i] += shake(t, t0, 0.010 * s, 12.0, 8.0, 0.8)
        if clip.name.endswith("_angry") and not clip.loop and not clip.overlay:
            # Enraged variants shiver with held fury in the half second before every blow.
            for k, (t0, s) in enumerate(clip.impacts):
                for i, t in enumerate(times):
                    e = window(t, t0 - 0.62, t0 - 0.38, t0 - 0.08, t0 + 0.02) * min(1.0, s + 0.3)
                    if e <= 0:
                        continue
                    for ch, amp, seed in (("b3r", 0.55, 1), ("hx", 1.8, 2), ("hr", 0.7, 3), ("b1x", 1.4, 4),
                                          ("s1x", 1.3, 5), ("s2x", 1.3, 6)):
                        sec[ch][i] += e * tremor(t, clip.duration, amp, seed + 7 * k)
                    sec["horn"][i] += e * 0.012 * tremor(t, clip.duration, 1.0, 11 + k)
        if clip.secondary and not clip.loop:
            lean, x, y = vals["b1r"], vals["b1x"], vals["b1y"]
            f2, f3 = follow(lean, DT, 3.0, 0.55), follow(lean, DT, 2.2, 0.5)
            fx, fy = follow(x, DT, 2.4, 0.55), follow(y, DT, 3.2, 0.42)
            for i in range(n):
                sec["b2r"][i] += 0.35 * (f2[i] - lean[i]) - 0.03 * (fx[i] - x[i])
                sec["b3r"][i] += 0.50 * (f3[i] - lean[i]) - 0.06 * (fx[i] - x[i])
                sec["b2t"][i] += clamp(0.16 * (fy[i] - y[i]), -10, 10)
                sec["b3t"][i] += clamp(0.12 * (fy[i] - y[i]), -8, 8)
            # Head inertia: the face hangs from the crown and trails body moves.
            pts, body_angles, head_angles = [], [], []
            for i in range(n):
                pose = copy_pose(self.rest)
                self.apply(pose, {ch: vals[ch][i] + sec[ch][i] for ch in C.CHANNELS})
                w = self.rig.world(pose, upto="head")
                pts.append((w["head"][4], w["head"][5]))
                body_angles.append(Rig.angle(w["body3"]))
                head_angles.append(Rig.angle(w["head"]))
            px = follow([p[0] for p in pts], DT, 2.8, 0.45)
            py = follow([p[1] for p in pts], DT, 2.8, 0.45)
            ha = follow(head_angles, DT, 2.4, 0.5)
            for i in range(n):
                a = math.radians(body_angles[i])
                lx, ly = px[i] - pts[i][0], py[i] - pts[i][1]
                along, side = lx * math.cos(a) + ly * math.sin(a), -lx * math.sin(a) + ly * math.cos(a)
                sec["hx"][i] += clamp(0.55 * along, -18, 18)
                sec["hy"][i] += clamp(0.55 * side, -18, 18)
                sec["hr"][i] += clamp(0.6 * (ha[i] - head_angles[i]), -6, 6)
        if clip.secondary:
            # Beard: hanging pendulum driven by the moving chin.
            roots, rigid = [], []
            for i in range(n):
                pose = copy_pose(self.rest)
                self.apply(pose, {ch: vals[ch][i] + sec[ch][i] for ch in C.CHANNELS})
                w = self.rig.world(pose, upto="beard")
                roots.append((w["beard"][4], w["beard"][5]))
                rigid.append(Rig.angle(w["beard"]))
            acc = []
            for i in range(n):
                a, b, c = roots[max(i - 1, 0)], roots[i], roots[min(i + 1, n - 1)]
                acc.append(((a[0] - 2 * b[0] + c[0]) / DT ** 2, (a[1] - 2 * b[1] + c[1]) / DT ** 2))
            smoothed = []
            for i in range(n):
                window_ = acc[max(0, i - 4): i + 5]
                smoothed.append((sum(v[0] for v in window_) / len(window_), sum(v[1] for v in window_) / len(window_)))
            unwrapped = [rigid[0]]
            for value in rigid[1:]:
                unwrapped.append(unwrap(value, unwrapped[-1]))
            dev = pendulum(smoothed, unwrapped, DT, 1.7, 0.2, 170.0, 1.6)
            for i in range(n):
                sec["brd"][i] += clamp(dev[i], -24, 24)
        return sec

    def bake(self, clip: C.Clip) -> dict:
        d = clip.duration
        n = int(round(d / DT))
        times = [i * DT for i in range(n)] + [d]
        vals = self.channel_arrays(clip, times)
        sec = self.secondary(clip, times, vals)
        final = {}
        for ch in C.CHANNELS:
            out = []
            for i, t in enumerate(times):
                g = clip.gate(t) if clip.gate else 1.0
                if not clip.loop:
                    g *= 1 - smoother(seg(t, d - 0.34, d - 0.02))
                out.append(vals[ch][i] + g * sec[ch][i])
            final[ch] = out

        attach_times = [t for a in clip.arms.values() for t, _ in a.attach]
        sample = sorted({round(i / OUT_HZ, 6) for i in range(int(math.floor(d * OUT_HZ)) + 1)} | {d} |
                        set(clip.exact) | set(attach_times))
        sample = [t for t in sample if 0 <= t <= d + 1e-9]
        frames, misses = [], {1: 0.0, 2: 0.0}
        base_world = None
        for t in sample:
            f = min(t / DT, n)
            i0 = int(math.floor(f))
            i1 = min(i0 + 1, n)
            u = f - i0
            v = {ch: final[ch][i0] + (final[ch][i1] - final[ch][i0]) * u for ch in C.CHANNELS}
            pose = copy_pose(self.rest)
            self.apply(pose, v)
            if clip.overlay:
                # Additive overlay (track 1, MixBlend.add): shoulders keep their world placement so
                # planted or IK-held hands never slide while the torso and head flinch.
                base_world = base_world or self.rig.world(self.rest)
                parents = self.rig.world(pose)
                for name in ("shoulder1", "shoulder2"):
                    m = base_world[name]
                    self.rig.set_world(pose, name, (m[4], m[5]), Rig.angle(m), parents)
            else:
                world = self.rig.world(pose)
                for side, arm in ARMS.items():
                    intent = clip.arms[side]
                    wrist, bend, scale_ = intent.wrist(t), intent.bend(t), intent.scale(t)
                    misses[side] = max(misses[side], self.rig.solve_arm(pose, side, wrist, 0.0, world))
                    hand = pose[arm["hand"]]
                    hand[ROT] = self.rest[arm["hand"]][ROT] + BEND_SIGN[side] * soft_band(bend, *BEND_BAND)
                    hand[SX] = self.rest[arm["hand"]][SX] * scale_
                    hand[SY] = self.rest[arm["hand"]][SY] * scale_
                    target = "arm1_IK" if side == 1 else "arm2_IK"
                    pose[target][X] = wrist[0] - ROOT_ORIGIN[0]
                    pose[target][Y] = wrist[1] - ROOT_ORIGIN[1]
                if clip.grab:
                    holder = ARMS[2 if "_right" in clip.name else 1]["hand"]
                    # The fist mesh scales with the hand bone, so its centre is a fixed local point.
                    gx, gy = clip.grab(t, Rig.point(self.rig.world(pose)[holder], C.PALM_REACH, 0.0))
                    pose["player_grab"][X] = gx - ROOT_ORIGIN[0]
                    pose["player_grab"][Y] = gy - ROOT_ORIGIN[1]
            frames.append((t, pose))
        # Raised hands must keep the elbow below them; a forearm hanging from an off-screen
        # elbow reads as a broken arm in this front view.
        hanging = {1: (0.0, 0.0, 0.0), 2: (0.0, 0.0, 0.0)}
        for t, pose in frames:
            if clip.overlay or clip.name.startswith("hide"):
                break
            world = self.rig.world(pose)
            for side, arm in ARMS.items():
                upper, elbow, wrist = world[arm["upper"]], world[arm["fore"]], world[arm["hand"]]
                off_top = elbow[5] - (SCREEN_TOP - 25)
                dangling = elbow[5] - wrist[5] if wrist[5] > upper[5] - 80 else 0.0
                score = max(off_top + 100 if off_top > 0 else 0.0, dangling - 40)
                if score > hanging[side][1]:
                    hanging[side] = (t, score, elbow[5])
        self.report[clip.name] = {"ik_miss": misses, "hanging": hanging}
        return self.timelines(clip, frames)

    def timelines(self, clip: C.Clip, frames: list) -> dict:
        setup = self.rig.setup
        keep = set(clip.exact) | {t for a in clip.arms.values() for t, _ in a.attach}
        bones = {}
        names = OVERLAY_BONES if clip.overlay else KEEP_BONES
        # Overlays store pure deltas from the rest pose (applied with MixBlend.add); clips store setup-relative values.
        ref = self.rest if clip.overlay else setup
        for name in names:
            rows = {"rotate": [], "translate": [], "scale": []}
            last = None
            for t, pose in frames:
                rot = pose[name][ROT] - ref[name][ROT]
                last = rot if last is None else unwrap(rot, last)
                rows["rotate"].append((t, (last,)))
                rows["translate"].append((t, (pose[name][X] - ref[name][X], pose[name][Y] - ref[name][Y])))
                if clip.overlay:
                    rows["scale"].append((t, (1 + (pose[name][SX] - ref[name][SX]) / setup[name][SX],
                                              1 + (pose[name][SY] - ref[name][SY]) / setup[name][SY])))
                else:
                    rows["scale"].append((t, (pose[name][SX] / setup[name][SX], pose[name][SY] / setup[name][SY])))
            entry = {}
            for channel, series in rows.items():
                keys = reduce_keys(series, TOLERANCE[channel], keep)
                if channel == "rotate":
                    entry[channel] = [{"time": r(t), "value": r(v[0])} for t, v in keys]
                else:
                    entry[channel] = [{"time": r(t), "x": r(v[0]), "y": r(v[1])} for t, v in keys]
            bones[name] = entry
        return {"bones": bones}


def reduce_keys(series: list, tol: float, keep: set) -> list:
    """Drop keys that linear interpolation reproduces within ``tol`` (always keeps ends and ``keep``)."""
    if all(max(abs(a - b) for a, b in zip(v, series[0][1])) <= tol * 0.25 for _, v in series):
        return [series[0]]
    n = len(series)
    flags = [False] * n
    flags[0] = flags[-1] = True
    for i, (t, _) in enumerate(series):
        if any(abs(t - k) < 1e-6 for k in keep):
            flags[i] = True
    anchors = [i for i in range(n) if flags[i]]
    stack = list(zip(anchors, anchors[1:]))
    while stack:
        a, b = stack.pop()
        if b - a < 2:
            continue
        ta, va = series[a]
        tb, vb = series[b]
        worst, at = 0.0, -1
        for i in range(a + 1, b):
            t, v = series[i]
            u = (t - ta) / (tb - ta)
            err = max(abs(v[k] - (va[k] + (vb[k] - va[k]) * u)) for k in range(len(v)))
            if err > worst:
                worst, at = err, i
        if worst > tol:
            flags[at] = True
            stack += [(a, at), (at, b)]
    return [series[i] for i in range(n) if flags[i]]


def r(v: float) -> float:
    return round(v, 5) + 0.0


def find_source(animations: dict, clip: C.Clip) -> dict:
    base = clip.source or clip.name
    right = "_right" in clip.name
    is_angry = clip.name.endswith("_angry")
    candidates = [base + ("_right" if right else "") + ("_angry" if is_angry else ""), base + ("_angry" if is_angry else ""), base]
    for name in candidates:
        if name in animations:
            return animations[name]
    raise KeyError(clip.name)


def assemble(data: dict, baker: Baker, built: list[C.Clip]) -> dict:
    out = copy.deepcopy(data)
    animations = out["animations"]
    for clip in built:
        source = find_source(data["animations"], clip)
        anim = baker.bake(clip)
        # An additive overlay must not touch slot colours, attachments or draw order of the base track.
        slots = {} if clip.overlay else copy.deepcopy(source.get("slots", {}))
        for side in (1, 2):
            slot = f"arm{side}_3"
            if clip.overlay:
                continue
            attach = clip.arms[side].attach or [(0.0, "open")]
            slots.setdefault(slot, {})["attachment"] = [{"time": r(t), "name": ATTACH[side][kind]} for t, kind in attach]
        anim["slots"] = slots
        if not clip.overlay:
            anim["ik"] = {"arm1_IK": [{"time": 0, "mix": 0, "bendPositive": False}],
                          "arm2_IK": [{"time": 0, "mix": 0, "bendPositive": True}]}
        if "drawOrder" in source and not clip.overlay:
            anim["drawOrder"] = copy.deepcopy(source["drawOrder"])
        if "events" in source and clip.source and not clip.name.startswith("grab_hold"):
            anim["events"] = copy.deepcopy(source["events"])
        animations[clip.name] = anim
    return out


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only")
    parser.add_argument("--out", type=Path, default=ROOT / "build/cavegod_motion/motion_base.spjson")
    args = parser.parse_args()
    data = load_source()
    baker = Baker(data)
    built = all_clips()
    if args.only:
        wanted = set(args.only.split(","))
        built = [c for c in built if c.name in wanted]
    result = assemble(data, baker, built)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n", encoding="utf-8")
    report = args.out.with_suffix(".report.json")
    report.write_text(json.dumps(baker.report, indent=1), encoding="utf-8")
    worst = max((max(v["ik_miss"].values()) for v in baker.report.values()), default=0.0)
    print(f"baked {len(built)} clips -> {args.out} (worst IK miss {worst:.2f})")
    for name, v in baker.report.items():
        if max(v["ik_miss"].values()) > 0.5:
            print(f"  {name}: unreachable wrist by {v['ik_miss']}")
        for side, (t, score, top) in v["hanging"].items():
            if score > 0:
                print(f"  {name}: arm{side} elbow problem {score:.0f} at {t:.2f}s (elbow y {top:.0f})")


if __name__ == "__main__":
    main()
