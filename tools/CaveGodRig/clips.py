"""Choreography of every in-game 活体巨岩 clip.

Torso, head and shoulder channels are deltas from the rest pose (old idle_front t=0, kept so hand
anchors and UI positions do not move). Arms are world-space wrist paths (Spine skeleton space, Y up)
solved by IK in build.py. The hand is authored as an inward wrist bend relative to the forearm
(0 = rest), which keeps it inside the range the weighted forearm/hand meshes tolerate.
Hit frames, clip lengths and events are fixed by gameplay code.

Pose rule for this front view: a raised fist must rise on the inner side of its shoulder so the
elbow drops down and out; wrists outside the shoulder stay low or the arm stays nearly straight.
"""
from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field
from typing import Callable

from motion import Curve, breath, const, seg, smooth, smoother, tremor, wave, window

REST_WRIST = {1: (-421.96, -35.67), 2: (422.36, -34.85)}
GROUND = (-0.65, -247.0)          # player_grab setup: feet of the centre player
GRIP_DROP = 62.0                  # captive feet below the fist centre while held (old rig: 61)
PALM_REACH = 77.0                 # fist/palm centre along the hand bone

# Channels: body1 x/y/rot, body2/body3 rot and stretch along the spine, head translate in the body3
# frame (x up, y left), head rot / scaleX, beard rot / scaleX, horn scaleX, shoulders (x up,
# o outward, r rot), finger curl (negative tightens), lower trunk sway.
CHANNELS = ("b1x", "b1y", "b1r", "b2r", "b2t", "b3r", "b3t", "hx", "hy", "hr", "hsx", "brd", "bsx", "horn",
            "s1x", "s1o", "s1r", "s2x", "s2o", "s2r", "f1", "f2", "bb")
MULTIPLY = {"hsx", "bsx", "horn"}

ANGRY = {"b1y": -10.0, "b2t": -3.0, "hx": -11.0, "s1x": 7.0, "s1o": -4.0, "s2x": 7.0, "s2o": -4.0,
         "horn": 1.07, "f1": -4.0, "f2": -4.0}

# Exposed core after the LEFT arm is knocked down (mirrored for the right arm).
WEAK_L = {"b1y": -122.0, "b1x": 8.0, "b1r": -7.0, "b2r": 3.5, "b3r": 2.5, "b2t": -4.0, "hx": -24.0, "hr": -13.0,
          "hy": -5.0, "s1x": -18.0, "s1o": -8.0, "s2x": 10.0, "s2o": 2.0, "f1": -7.0}
WEAK_L_ARMS = {1: ((-150.0, -58.0), 10.0), 2: ((392.0, -42.0), 0.0)}

# Prey held up beside the face (left arm). Shared by grab_player's plateau, grab_hold and grab_slam.
HOLD_L = {"b1y": 14.0, "b1x": 6.0, "b1r": -2.0, "s1x": 9.0, "s1o": 3.0, "hx": -6.0, "hr": -4.0, "hy": 7.0,
          "hsx": 0.97, "f2": -3.0}
HOLD_L_ARM = ((-322.0, 212.0), 12.0)


@dataclass
class Arm:
    wrist: Callable[[float], tuple]
    bend: Callable[[float], float]
    scale: Callable[[float], float]
    attach: list = field(default_factory=list)   # (time, "open" | "fist")


@dataclass
class Clip:
    name: str
    duration: float
    channels: dict
    arms: dict
    grab: Callable | None = None                 # (t, palm_xy) -> player_grab position
    loop: bool = False
    secondary: bool = True
    gate: Callable | None = None
    impacts: list = field(default_factory=list)  # (time, strength)
    overlay: bool = False
    exact: list = field(default_factory=list)    # times that must be sampled exactly
    posture: Callable | None = None              # weight of the angry posture over time
    source: str | None = None                    # clip whose slots/events/draw order are kept


def channels(duration: float, beats: list, start: dict | None = None, end: dict | None = None) -> dict:
    """Pose-to-pose keys. beats: (time, {channel: value | (value, mode)}). Missing endpoints use start/end."""
    start, end = start or {}, end or {}
    keys = defaultdict(list)
    for t, pose in beats:
        for ch, v in pose.items():
            keys[ch].append((t, *v) if isinstance(v, tuple) else (t, v))
    for ch in set(keys) | set(start) | set(end):
        rows = keys[ch]
        neutral = 1.0 if ch in MULTIPLY else 0.0
        if not any(abs(r[0]) < 1e-9 for r in rows):
            rows.insert(0, (0.0, start.get(ch, neutral), "stop"))
        if not any(abs(r[0] - duration) < 1e-9 for r in rows):
            rows.append((duration, end.get(ch, neutral), "stop"))
    return {ch: Curve(rows) for ch, rows in keys.items()}


def arm(beats: list, attach: list | None = None) -> Arm:
    """beats: (time, (x, y) | None, bend | None, scale | None, mode)."""
    pos, bend, scl = [], [], []
    for row in beats:
        t, p, b, s = row[:4]
        mode = row[4] if len(row) > 4 else "auto"
        if p is not None:
            pos.append((t, p, mode))
        if b is not None:
            bend.append((t, b, "stop" if mode in ("hit", "hit3", "stop") else "auto"))
        if s is not None:
            scl.append((t, s, "stop" if mode == "stop" else "auto"))
    return Arm(Curve(pos), Curve(bend), Curve(scl), list(attach or []))


def planted(side: int, wrist=None, bend=0.0, attach=None) -> Arm:
    w = wrist or REST_WRIST[side]
    return Arm(const(w), const(bend), const(1.0), list(attach or [(0, "open")]))


def mix(a: tuple, b: tuple, w: float) -> tuple:
    return tuple(p + (q - p) * w for p, q in zip(a, b))


def mirror_point(p: tuple, inverse: bool = False) -> tuple:
    """Left/right mirror that maps the two (slightly asymmetric) rest wrists exactly onto each other."""
    dy = REST_WRIST[2][1] - REST_WRIST[1][1]
    return REST_WRIST[1][0] + REST_WRIST[2][0] - p[0], p[1] + (-dy if inverse else dy)


def mirror_path(path: list) -> list:
    return [(row[0], mirror_point(row[1]) if row[1] is not None else None, *row[2:]) for row in path]


def shift_beats(path: list, after: float, by: float) -> list:
    """Delay the later part of a mirrored path so the two hands settle one after the other."""
    return [(row[0] + by if row[0] > after else row[0], *row[1:]) for row in path]


def held(t: float, palm: tuple, lifted: float) -> tuple:
    return mix(GROUND, (palm[0], palm[1] - GRIP_DROP), lifted)


# ---------------------------------------------------------------------------- loops

def idle(angry: bool) -> Clip:
    p = 4.0

    def b(t, d=0.0, cycles=1):
        peak = 0.5 if angry else 0.56
        return breath((t - d) % p, p, cycles, peak) - breath((-d) % p, p, cycles, peak)

    if not angry:
        ch = {
            "b1y": lambda t: 9.0 * b(t),
            "b1x": lambda t: 6.0 * wave(t, p, 1, 0.7),
            "b1r": lambda t: 0.9 * wave(t, p, 1, 1.4),
            "b2r": lambda t: -0.5 * wave(t, p, 1, 2.0),
            "b3r": lambda t: 0.7 * wave(t, p, 1, 2.6),
            "b2t": lambda t: 1.6 * b(t, 0.25),
            "s1x": lambda t: 5.0 * b(t, 0.15) + 1.2 * wave(t, p, 1, 0.9),
            "s2x": lambda t: 5.0 * b(t, 0.15) - 1.2 * wave(t, p, 1, 0.9),
            "s1o": lambda t: 2.5 * b(t, 0.2),
            "s2o": lambda t: 2.5 * b(t, 0.2),
            "hx": lambda t: -3.5 * b(t, 0.45) + 1.2 * wave(t, p, 2, 0.4),
            "hr": lambda t: 1.7 * wave(t, p, 1, 2.2),
            "hy": lambda t: 3.2 * wave(t, p, 1, 1.9),
            "brd": lambda t: 2.6 * wave(t, p, 1, 3.0) + 0.8 * wave(t, p, 2, 1.0),
            "f1": lambda t: 2.4 * wave(t, p, 1, 0.4),
            "f2": lambda t: 2.4 * wave(t, p, 1, 2.4),
        }
    else:
        ch = {
            "b1y": lambda t: 12.0 * b(t, 0, 2) + tremor(t, p, 0.9, 1),
            "b1x": lambda t: 4.5 * wave(t, p, 1, 0.5) + tremor(t, p, 0.7, 4),
            "b1r": lambda t: 0.8 * wave(t, p, 1, 1.2),
            "b2r": lambda t: -0.5 * wave(t, p, 1, 1.8),
            "b3r": lambda t: 0.6 * wave(t, p, 2, 1.9) + tremor(t, p, 0.28, 2),
            "b2t": lambda t: 2.0 * b(t, 0.18, 2),
            "s1x": lambda t: 7.0 * b(t, 0.12, 2) + tremor(t, p, 0.8, 5),
            "s2x": lambda t: 7.0 * b(t, 0.12, 2) + tremor(t, p, 0.8, 6),
            "s1o": lambda t: -2.5 * b(t, 0.2, 2),
            "s2o": lambda t: -2.5 * b(t, 0.2, 2),
            "hx": lambda t: -4.5 * b(t, 0.3, 2) + tremor(t, p, 1.0, 7),
            "hr": lambda t: 1.3 * wave(t, p, 1, 2.0) + tremor(t, p, 0.35, 3),
            "hy": lambda t: 2.6 * wave(t, p, 1, 1.7),
            "brd": lambda t: 3.0 * wave(t, p, 2, 2.6) + 1.0 * wave(t, p, 1, 0.3),
            "f1": lambda t: -3.0 * b(t, 0.05, 2),
            "f2": lambda t: -3.0 * b(t, 0.25, 2),
        }
    return Clip("idle_front_angry" if angry else "idle_front", p, ch, {1: planted(1), 2: planted(2)},
                loop=True, secondary=False, source="idle_front")


def grab_hold(angry: bool) -> Clip:
    """Loop while the captive struggles in the raised fist (replaces the frozen grab_player frame)."""
    p = 3.0

    def b(t, d=0.0):
        return breath((t - d) % p, p, 1, 0.55) - breath((-d) % p, p, 1, 0.55)

    base = dict(HOLD_L)
    ch = {
        "b1y": lambda t: base["b1y"] + 7.0 * b(t) + (tremor(t, p, 0.7, 2) if angry else 0.0),
        "b1x": lambda t: base["b1x"] + 4.0 * wave(t, p, 1, 0.8),
        "b1r": lambda t: base["b1r"] + 0.8 * wave(t, p, 1, 1.5),
        "b3r": lambda t: 0.7 * wave(t, p, 1, 2.3),
        "b2t": lambda t: 1.5 * b(t, 0.2),
        "s1x": lambda t: base["s1x"] + 4.0 * b(t, 0.1) + 1.5 * wave(t, p, 2, 0.6),
        "s1o": lambda t: base["s1o"],
        "s2x": lambda t: 4.0 * b(t, 0.15),
        "hx": lambda t: base["hx"] - 3.0 * b(t, 0.4) + 1.5 * wave(t, p, 2, 1.1),
        "hr": lambda t: base["hr"] + 2.4 * wave(t, p, 1, 2.3),
        "hy": lambda t: base["hy"] + 3.0 * wave(t, p, 1, 1.5),
        "hsx": lambda t: base["hsx"],
        "brd": lambda t: 3.0 * wave(t, p, 1, 2.8) + 1.2 * wave(t, p, 2, 0.9),
        "f2": lambda t: base["f2"] + 2.0 * wave(t, p, 1, 0.3),
    }
    (wx, wy), wb = HOLD_L_ARM

    def wrist(t):
        squeeze = wave(t, p, 2, 0.4)
        return (wx + 4.0 * squeeze + 2.0 * wave(t, p, 5, 1.1), wy + 5.0 * wave(t, p, 1, 0.9) + 2.5 * wave(t, p, 3, 2.0))

    hold = Arm(wrist, lambda t: wb + 3.0 * wave(t, p, 2, 1.7) + 1.2 * wave(t, p, 5, 0.2), const(1.0), [(0, "fist")])
    return Clip("grab_hold_angry" if angry else "grab_hold", p, ch, {1: hold, 2: planted(2)},
                grab=lambda t, palm: held(t, palm, 1.0), loop=True, secondary=False, source="grab_player")


def weak_idle(angry: bool) -> Clip:
    p = 3.2

    def b(t, d=0.0):
        return breath((t - d) % p, p, 1, 0.62) - breath((-d) % p, p, 1, 0.62)

    def twitch(t):
        return window(t, 2.05, 2.14, 2.22, 2.55)

    ch = {k: const(v) for k, v in WEAK_L.items()}
    ch.update({
        "b1y": lambda t: WEAK_L["b1y"] + 5.5 * b(t) + 1.4 * wave(t, p, 3, 0.5),
        "b1x": lambda t: WEAK_L["b1x"] + 2.0 * wave(t, p, 1, 0.9),
        "b1r": lambda t: WEAK_L["b1r"] + 0.6 * wave(t, p, 1, 1.4),
        "b2t": lambda t: WEAK_L["b2t"] + 1.5 * b(t, 0.25),
        "hx": lambda t: WEAK_L["hx"] - 2.5 * b(t, 0.5),
        "hr": lambda t: WEAK_L["hr"] + 1.6 * wave(t, p, 1, 2.1),
        "hy": lambda t: WEAK_L["hy"] + 2.5 * wave(t, p, 1, 1.2),
        "brd": lambda t: 2.2 * wave(t, p, 1, 2.7),
        "s2x": lambda t: WEAK_L["s2x"] + 3.0 * b(t, 0.1) + tremor(t, p, 0.9 if angry else 0.6, 3),
        "s2o": lambda t: WEAK_L["s2o"] + tremor(t, p, 0.6, 8),
        "s1x": lambda t: WEAK_L["s1x"] + 2.0 * b(t, 0.2),
        "f1": lambda t: WEAK_L["f1"] - 5.0 * twitch(t),
    })
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    broken = Arm(lambda t: (w1[0] + 2.0 * twitch(t), w1[1] + 4.0 * twitch(t)), lambda t: b1 + 4.0 * twitch(t),
                 const(1.0), [(0, "fist")])
    return Clip("weak_idle_angry" if angry else "weak_idle", p, ch, {1: broken, 2: planted(2, w2, b2)},
                loop=True, secondary=False, source="weak_idle")


# ---------------------------------------------------------------------------- overlays

def hit_recoil(angry: bool) -> Clip:
    """Additive flinch on the reaction track (deltas from rest; shoulders stay put)."""
    d = 0.8
    gain = 1.2 if angry else 1.0  # the enraged core flinches harder but recovers just as fast
    names = ("b1y", "b3r", "b2r", "hx", "hr", "hy", "hsx", "b2t", "b1x")
    beats = [
        (0.06, {k: (1.0 if k == "hsx" else 0.0, "stop") for k in names}),
        (0.13, {"b1y": (-11.0, "stop"), "b3r": 2.2, "b2r": -1.3, "hx": (12.0, "stop"), "hr": (-5.0, "stop"),
                "hy": -3.0, "hsx": 0.95, "b2t": -3.5, "b1x": 2.5}),
        (0.30, {"b1y": 4.0, "b3r": -1.1, "b2r": 0.6, "hx": -4.5, "hr": 2.4, "hy": 1.2, "hsx": 1.015, "b2t": 1.3, "b1x": -1.0}),
        (0.48, {"b1y": -1.3, "b3r": 0.4, "b2r": -0.2, "hx": 1.6, "hr": -0.8, "hy": -0.4, "hsx": 1.0, "b2t": -0.4}),
        (0.74, {k: (1.0 if k == "hsx" else 0.0, "stop") for k in names}),
    ]

    def scaled(ch, v):
        value, *mode = v if isinstance(v, tuple) else (v,)
        value = 1 + (value - 1) * gain if ch in MULTIPLY else value * gain
        return (value, *mode) if mode else value

    beats = [(t, {ch: scaled(ch, v) for ch, v in pose.items()}) for t, pose in beats]
    return Clip("hit_recoil_angry" if angry else "hit_recoil", d, channels(d, beats), {}, overlay=True,
                impacts=[(0.07, 0.6 * gain)], source="hit_recoil")


# ---------------------------------------------------------------------------- body attacks

def alternating_jabs() -> Clip:
    """Left jab, right jab, then a bigger left haymaker with the whole body behind it."""
    d = 3.5
    left = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.12, (-432, -26), -3, 1.00),
        (0.31, (-262, 178), -5, 0.95, "stop"),
        (0.64, (-170, -122), 18, 1.12, "hit3"),
        (0.72, (-178, -110), 15, 1.11, "stop"),
        (0.98, (-300, 40), 5, 1.00),
        (1.20, (-306, 52), 5, 1.00, "stop"),
        (1.58, (-298, 140), 0, 0.98),
        (1.93, (-212, 352), -5, 0.93, "stop"),
        (2.32, (-182, -118), 22, 1.15, "hit3"),
        (2.42, (-187, -108), 18, 1.14, "stop"),
        (2.64, (-206, -128), 15, 1.12, "stop"),
        (2.96, (-386, 42), 5, 1.02),
        (3.18, (-424, -28), 0, 1.00),
        (3.32, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (0.16, "fist"), (3.12, "open")])
    right = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.74, REST_WRIST[2], 0, 1.00, "stop"),
        (0.86, (440, -12), -3, 1.00),
        (1.04, (262, 178), -5, 0.95, "stop"),
        (1.34, (168, -132), 18, 1.12, "hit3"),
        (1.42, (177, -121), 15, 1.11, "stop"),
        (1.70, (402, 52), 5, 1.00),
        (1.92, (424, -26), 0, 1.00),
        (2.02, REST_WRIST[2], 0, 1.00, "stop"),
    ], [(0, "open"), (0.86, "fist"), (1.90, "open")])
    beats = [
        (0.12, {"b1x": 4, "b1y": -7, "b1r": -0.5, "s1x": -3, "s2x": -2, "f1": -4, "f2": -4, "hx": -2}),
        (0.32, {"b1x": 18, "b1y": 7, "b1r": -3.0, "hx": 4, "hr": -2, "hy": -3, "s1x": 11, "s1o": 7, "s2x": -4, "s2o": -2, "f1": 0, "f2": -6}),
        (0.64, {"b1x": (-12, "hit3"), "b1y": (-15, "hit3"), "b1r": (2.6, "hit3"), "hx": (-15, "hit3"), "hr": 3.5, "hy": 4,
                "s1x": (-9, "hit3"), "s1o": (-11, "hit3"), "s2x": 6, "s2o": 2, "f2": -4}),
        (0.80, {"b1x": -9, "b1y": -11, "b1r": 1.9, "hx": -10, "hr": 2.2, "hy": 3, "s1x": -4, "s1o": -6, "s2x": 4, "s2o": 0, "f2": -3}),
        (1.04, {"b1x": -22, "b1y": 5, "b1r": 3.2, "hx": 2, "hr": -2.5, "hy": -2, "s1x": 2, "s1o": 2, "s2x": 11, "s2o": 7, "f2": 0}),
        (1.34, {"b1x": (12, "hit3"), "b1y": (-15, "hit3"), "b1r": (-2.6, "hit3"), "hx": (-15, "hit3"), "hr": -3.5, "hy": -4,
                "s1x": 6, "s1o": 2, "s2x": (-9, "hit3"), "s2o": (-11, "hit3")}),
        (1.50, {"b1x": 10, "b1y": -10, "b1r": -1.8, "hx": -10, "hr": -2.0, "hy": -3, "s1x": 4, "s1o": 0, "s2x": -4, "s2o": -6}),
        (1.93, {"b1x": 30, "b1y": 15, "b1r": -5.0, "b2t": 3, "hx": 9, "hr": -3, "hy": -5, "hsx": 0.96, "s1x": 15, "s1o": 11,
                "s2x": -4, "s2o": -2, "f2": -6}),
        (2.32, {"b1x": (-16, "hit3"), "b1y": (-29, "hit3"), "b1r": (4.6, "hit3"), "b2t": -5, "hx": (-21, "hit3"), "hr": 5,
                "hy": 6, "hsx": 1.0, "s1x": (-13, "hit3"), "s1o": (-15, "hit3"), "s2x": 8, "s2o": 4}),
        (2.42, {"b1x": -18, "b1y": -33, "b1r": 4.9, "b2t": -6, "hx": -23, "hr": 5.2, "hy": 6, "s1x": -14, "s1o": -16}),
        (2.66, {"b1x": -12, "b1y": -25, "b1r": 3.4, "b2t": -4, "hx": -17, "hr": 3, "hy": 4, "s1x": -9, "s1o": -10, "s2x": 5, "s2o": 2}),
        (3.00, {"b1x": 2, "b1y": 7, "b1r": -0.6, "b2t": 1.5, "hx": 4, "hr": -1, "hy": -1, "s1x": 2, "s1o": 1, "s2x": 1, "s2o": 0, "f2": -2}),
        (3.32, {"b1x": (0, "stop"), "b1y": (0, "stop"), "b1r": (0, "stop"), "hx": (0, "stop"), "hr": (0, "stop"),
                "hy": (0, "stop"), "f2": (0, "stop")}),
    ]
    return Clip("alternating_jabs", d, channels(d, beats), {1: left, 2: right},
                impacts=[(0.64, 0.55), (1.34, 0.55), (2.32, 1.0)], exact=[0.64, 1.34, 2.32], source="alternating_jabs")


def central_slam() -> Clip:
    """Rear up with both open hands raised beside the crown, then slap the platform flat."""
    d = 3.75
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.30, (-430, -46), -3, 1.00, "stop"),
        (0.52, (-420, 70), 0, 0.99),
        (0.92, (-322, 226), -5, 0.96),
        (1.28, (-205, 350), -5, 0.93, "stop"),
        (1.52, (-200, 362), -5, 0.93, "stop"),
        (1.72, (-262, 182), 10, 1.02),
        (1.88, (-158, -150), 28, 1.12, "hit"),
        (2.08, (-163, -160), 25, 1.12, "stop"),
        (2.36, (-202, -150), 18, 1.08, "stop"),
        (2.66, (-338, -6), 8, 1.02),
        (2.94, (-425, -28), 0, 1.00),
        (3.06, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open")])
    right = arm(mirror_path(shift_beats(path, after=2.3, by=0.2)), [(0, "open")])
    beats = [
        (0.30, {"b1y": -13, "b2t": -3, "hx": -7, "s1x": -6, "s1o": -3, "s2x": -6, "s2o": -3, "f1": -5, "f2": -5}),
        (0.56, {"b1y": 10, "b2t": 1, "hx": 4, "s1x": 6, "s1o": 4, "s2x": 6, "s2o": 4, "f1": 0, "f2": 0}),
        (1.28, {"b1y": 30, "b2t": 4, "hx": 15, "hsx": 0.91, "hr": -1.5, "s1x": 20, "s1o": 10, "s2x": 20, "s2o": 10,
                "bsx": 1.04, "horn": 1.06}),
        (1.52, {"b1y": 34, "b2t": 4.5, "hx": 17, "hsx": 0.90, "hr": -1.0, "s1x": 22, "s1o": 11, "s2x": 22, "s2o": 11,
                "bsx": 1.05, "horn": 1.07}),
        (1.88, {"b1y": (-36, "hit3"), "b2t": (-6, "hit3"), "hx": (-20, "hit3"), "hsx": 1.0, "hr": 0.5,
                "s1x": (-14, "hit3"), "s1o": (-6, "hit3"), "s2x": (-14, "hit3"), "s2o": (-6, "hit3"), "bsx": 1.0, "horn": 1.0}),
        (2.00, {"b1y": -47, "b2t": -9, "hx": -26, "hsx": 1.02, "s1x": -18, "s1o": -8, "s2x": -18, "s2o": -8, "bsx": 0.98}),
        (2.36, {"b1y": -30, "b2t": -4, "hx": -16, "hsx": 1.0, "s1x": -8, "s1o": -4, "s2x": -8, "s2o": -4, "bsx": 1.0}),
        (2.92, {"b1y": 8, "b2t": 2, "hx": 4, "s1x": 4, "s1o": 2, "s2x": 4, "s2o": 2}),
        (3.40, {"b1y": (0, "stop"), "b2t": (0, "stop"), "hx": (0, "stop"), "s1x": (0, "stop"), "s1o": (0, "stop"),
                "s2x": (0, "stop"), "s2o": (0, "stop")}),
    ]
    return Clip("central_slam", d, channels(d, beats), {1: left, 2: right},
                impacts=[(1.88, 1.25)], exact=[1.88], source="central_slam")


def double_fist_crush() -> Clip:
    """Clasp both fists above the head, crush straight down onto the centre, pound once more."""
    d = 3.35
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.22, (-432, -42), -3, 1.00, "stop"),
        (0.44, (-400, 110), 0, 0.98),
        (0.78, (-95, 395), -5, 0.95, "stop"),
        (0.98, (-90, 410), -5, 0.95, "stop"),
        (1.25, (-108, -96), 20, 1.12, "hit"),
        (1.38, (-98, -103), 22, 1.13, "stop"),
        (1.47, (-106, -97), 18, 1.12),
        (1.56, (-100, -104), 22, 1.13, "stop"),
        (1.78, (-262, -40), 10, 1.06),
        (2.10, (-418, 24), 2, 1.01),
        (2.34, (-425, -28), 0, 1.00),
        (2.46, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open"), (0.30, "fist"), (2.30, "open")])
    right = arm(mirror_path(shift_beats(path, after=1.7, by=0.12)), [(0, "open"), (0.30, "fist"), (2.42, "open")])
    beats = [
        (0.22, {"b1y": -9, "hx": -5, "s1x": -3, "s1o": -5, "s2x": -3, "s2o": -5, "b2t": -2}),
        (0.78, {"b1y": 23, "hx": 11, "hsx": 0.95, "s1x": 12, "s1o": 15, "s2x": 12, "s2o": 15, "b2t": 3, "horn": 1.04}),
        (0.98, {"b1y": 27, "hx": 14, "hsx": 0.94, "s1x": 14, "s1o": 16, "s2x": 14, "s2o": 16, "b2t": 3.5, "horn": 1.05}),
        (1.25, {"b1y": (-25, "hit3"), "hx": (-18, "hit3"), "hsx": 1.0, "s1x": (-10, "hit3"), "s1o": (-15, "hit3"),
                "s2x": (-10, "hit3"), "s2o": (-15, "hit3"), "b2t": -4, "horn": 1.0}),
        (1.38, {"b1y": -33, "hx": -23, "s1o": -18, "s2o": -18, "b2t": -7}),
        (1.47, {"b1y": -27, "hx": -19}),
        (1.56, {"b1y": -32, "hx": -22, "s1o": -17, "s2o": -17}),
        (1.80, {"b1y": -20, "hx": -14, "s1x": -6, "s1o": -8, "s2x": -6, "s2o": -8, "b2t": -3}),
        (2.42, {"b1y": 6, "hx": 3, "s1x": 2, "s1o": 1, "s2x": 2, "s2o": 1, "b2t": 1}),
        (2.95, {"b1y": (0, "stop"), "hx": (0, "stop"), "s1x": (0, "stop"), "s1o": (0, "stop"), "s2x": (0, "stop"),
                "s2o": (0, "stop"), "b2t": (0, "stop")}),
    ]
    return Clip("double_fist_crush", d, channels(d, beats), {1: left, 2: right},
                impacts=[(1.25, 0.95), (1.47, 0.35)], exact=[1.25], source="double_fist_crush")


# ---------------------------------------------------------------------------- arm attacks (left versions)

def front_sweep() -> Clip:
    """Wide backhand: the arm swings out low to the side, then rakes across the platform."""
    d = 3.45
    sweeper = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.20, (-432, -30), -3, 1.00),
        (0.46, (-620, 20), 8, 0.97),
        (0.76, (-800, 30), 12, 0.95, "stop"),
        (1.10, (-436, -172), 0, 1.06),
        (1.35, (-172, -238), -5, 1.12, "lin"),
        (1.60, (124, -150), -5, 1.08),
        (1.84, (160, -116), 0, 1.05, "stop"),
        (2.40, (-196, 44), 5, 1.00),
        (2.90, (-424, -28), 0, 1.00),
        (3.05, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (0.40, "fist"), (2.86, "open")])
    beats = [
        (0.20, {"b1y": -6, "f2": -4}),
        (0.76, {"b1x": -16, "b1y": 8, "b1r": 3.6, "s1x": 9, "s1o": 9, "s2x": -3, "hy": 4, "hr": -2.5, "hx": 3, "f2": -6}),
        (1.35, {"b1x": 10, "b1y": -12, "b1r": -2.4, "s1x": -8, "s1o": -12, "s2x": 5, "hy": -3, "hr": 2.5, "hx": -12}),
        (1.62, {"b1x": 22, "b1y": -10, "b1r": -4.2, "s1x": -6, "s1o": -14, "s2x": 7, "s2o": 3, "hy": -6, "hr": 3.6, "hx": -10}),
        (1.90, {"b1x": 18, "b1y": -6, "b1r": -3.2, "hy": -5, "hr": 2.6, "hx": -6}),
        (2.60, {"b1x": -3, "b1y": 5, "b1r": 0.6, "s1x": 2, "s1o": 0, "s2x": 0, "s2o": 0, "hy": 1, "hr": -0.6, "hx": 2, "f2": -1}),
        (3.20, {k: (0, "stop") for k in ("b1x", "b1y", "b1r", "s1x", "s1o", "s2x", "s2o", "hy", "hr", "hx", "f2")}),
    ]
    return Clip("front_sweep", d, channels(d, beats), {1: sweeper, 2: planted(2)},
                impacts=[(1.35, 0.45)], exact=[1.35], source="front_sweep")


def card_snatch() -> Clip:
    """Open hand cocked high beside the head, lunge, close on the cards, cradle them back."""
    d = 3.2
    thief = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.14, (-433, -50), -3, 1.00, "stop"),
        (0.52, (-300, 200), -5, 1.02),
        (0.90, (-214, 354), -5, 1.05, "stop"),
        (1.00, (-210, 360), -5, 1.06, "stop"),
        # A snatch, not a punch: the lunge eases in and out so the palm settles onto the cards
        # (the card-snatch probe bounds the per-frame wrist velocity change).
        (1.36, (-80, -114), 15, 1.14, "stop"),
        (1.60, (-66, -122), 20, 1.14, "stop"),
        (1.68, (-70, -116), 18, 1.13, "stop"),
        (2.00, (-206, 72), 5, 1.04),
        (2.32, (-214, 196), 0, 0.98, "stop"),
        (2.54, (-228, 184), 0, 0.98),
        (2.96, (-425, -28), 0, 1.00),
        (3.08, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (1.60, "fist")])
    beats = [
        (0.14, {"b1y": -6, "b1x": 3, "s1x": -3, "f2": -3}),
        (0.55, {"b1y": 10, "b1x": 12, "b1r": -2.5, "s1x": 10, "s1o": 8, "hx": 2, "hr": -2, "hy": -4, "f2": -5}),
        (1.00, {"b1y": 16, "b1x": 16, "b1r": -4.0, "s1x": 14, "s1o": 10, "hx": 4, "hr": -3, "hy": -6, "s2x": -4,
                "hsx": 0.97, "f2": -6}),
        (1.36, {"b1y": (-16, "hit3"), "b1x": (-16, "hit3"), "b1r": (5.0, "hit3"), "s1x": (-10, "hit3"), "s1o": (-12, "hit3"),
                "hx": (-16, "hit3"), "hr": 3.5, "hy": 6, "s2x": 6, "hsx": 1.0}),
        (1.60, {"b1y": -14, "b1x": -14, "b1r": 4.6, "hx": -14, "hr": 3.2, "s1x": -9, "s1o": -11}),
        (1.70, {"b1y": -12, "b1x": -12, "b1r": 4.0, "hx": -12}),
        (2.32, {"b1y": 9, "b1x": 4, "b1r": 0.6, "s1x": 6, "s1o": -4, "s2x": 2, "hx": -11, "hr": 4.2, "hy": 7, "f2": -3}),
        (2.56, {"b1y": 7, "hr": 3.6, "hx": -10, "hy": 6}),
        (3.08, {k: (0, "stop") for k in ("b1y", "b1x", "b1r", "s1x", "s1o", "s2x", "hx", "hr", "hy", "f2")}),
    ]
    return Clip("card_snatch", d, channels(d, beats), {1: thief, 2: planted(2)},
                impacts=[(1.36, 0.35)], exact=[1.36, 1.60], source="card_snatch")


def grab_player() -> Clip:
    d = 5.0
    (hx_, hy_), hb = HOLD_L_ARM
    grabber = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.18, (-431, -47), -3, 1.00, "stop"),
        (0.46, (-300, 200), -5, 1.02),
        (0.70, (-230, 300), -5, 1.06, "stop"),
        (1.10, (-62, -141), 15, 1.14, "hit3"),
        (1.28, (-60, -147), 15, 1.14, "stop"),
        (1.56, (-186, 58), 10, 1.06),
        (1.90, (hx_, hy_), hb, 1.00, "stop"),
        (3.00, (hx_, hy_), hb, 1.00, "stop"),
        (3.30, (-190, 70), 10, 1.06),
        (3.62, (-62, -141), 15, 1.12, "stop"),
        (3.80, (-70, -130), 15, 1.10, "stop"),
        (4.32, (-382, 42), 5, 1.02),
        (4.72, (-424, -29), 0, 1.00),
        (4.86, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (1.08, "fist"), (3.65, "open")])
    hold = dict(HOLD_L)
    beats = [
        (0.18, {"b1y": -6, "f2": -3}),
        (0.70, {"b1y": 10, "b1x": -8, "b1r": 2.5, "s1x": 10, "s1o": 6, "hx": -8, "hr": 3, "hy": 3, "f2": -5}),
        (1.10, {"b1y": (-18, "hit3"), "b1x": (-16, "hit3"), "b1r": (4.5, "hit3"), "s1x": (-10, "hit3"), "s1o": (-10, "hit3"),
                "hx": (-18, "hit3"), "hr": 4, "hy": 6}),
        (1.28, {"b1y": -21, "b1x": -16, "b1r": 4.6, "hx": -19}),
        (1.90, {**{k: (v, "stop") for k, v in hold.items()}, "s2x": (0, "stop")}),
        (2.25, {k: (v, "stop") for k, v in hold.items()}),
        (3.00, {k: (v, "stop") for k, v in hold.items()}),
        (3.62, {"b1y": -14, "b1x": -12, "b1r": 3.5, "hx": -12, "hr": 2.5, "hy": 4, "s1x": -6, "s1o": -6, "hsx": 1.0, "f2": -3}),
        (4.40, {"b1y": 4, "b1x": 1, "b1r": -0.4, "hx": 2, "hr": -0.5, "hy": 0, "s1x": 1, "s1o": 0, "f2": 0}),
        (4.86, {k: (0, "stop") for k in ("b1y", "b1x", "b1r", "hx", "hr", "hy", "s1x", "s1o", "f2")}),
    ]

    def grab(t, palm):
        return held(t, palm, smoother(seg(t, 1.28, 1.70)) * (1 - smoother(seg(t, 3.24, 3.62))))

    return Clip("grab_player", d, channels(d, beats), {1: grabber, 2: planted(2)}, grab=grab,
                gate=lambda t: 1 - window(t, 2.0, 2.3, 3.0, 3.25), impacts=[(1.10, 0.5)], exact=[1.10, 2.6],
                source="grab_player")


def grab_slam() -> Clip:
    d = 1.7
    (hx_, hy_), hb = HOLD_L_ARM
    smasher = arm([
        (0.00, (hx_, hy_), hb, 1.00, "stop"),
        (0.40, (-215, 350), -5, 0.94),
        (0.52, (-210, 362), -5, 0.93, "stop"),
        (0.75, (-62, -148), 20, 1.14, "hit"),
        (0.83, (-64, -153), 20, 1.14, "stop"),
        (0.96, (-92, -118), 15, 1.10),
        (1.30, (-362, 22), 5, 1.02),
        (1.52, (-424, -29), 0, 1.00),
        (1.62, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "fist"), (0.95, "open")])
    hold = dict(HOLD_L)
    beats = [
        (0.0, {k: (v, "stop") for k, v in hold.items()}),
        (0.40, {"b1y": 30, "b1x": 0, "b1r": -3.5, "s1x": 18, "s1o": 6, "hx": 10, "hr": -2, "hy": 4, "hsx": 0.92, "f2": -5}),
        (0.52, {"b1y": 32, "b1r": -3.8, "hx": 11, "s1x": 19}),
        (0.75, {"b1y": (-34, "hit3"), "b1x": (-14, "hit3"), "b1r": (5.5, "hit3"), "s1x": (-14, "hit3"), "s1o": (-12, "hit3"),
                "hx": (-22, "hit3"), "hr": 4, "hy": 6, "hsx": 1.0, "f2": -6}),
        (0.83, {"b1y": -42, "b1x": -15, "b1r": 6.0, "hx": -25}),
        (1.10, {"b1y": -26, "b1x": -10, "b1r": 4.0, "hx": -16, "s1x": -8, "s1o": -7, "hr": 2.5, "hy": 3}),
        (1.48, {"b1y": 4, "b1x": 0, "b1r": -0.3, "hx": 2, "s1x": 1, "s1o": 0, "hr": -0.4, "hy": 0, "f2": -1}),
    ]
    return Clip("grab_slam", d, channels(d, beats, start=hold), {1: smasher, 2: planted(2)},
                grab=lambda t, palm: held(t, palm, 1 - smoother(seg(t, 0.58, 0.75))),
                impacts=[(0.75, 1.3)], exact=[0.75], source="grab_slam")


# ---------------------------------------------------------------------------- exposed core (left arm broken)

def weak_enter() -> Clip:
    d = 2.2
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    broken = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.10, (-446, 14), -3, 1.00),
        (0.32, (-382, -22), 5, 1.00),
        (0.62, (-206, -70), b1, 1.00),
        (0.85, (w1[0], w1[1] - 4), b1, 1.00, "hit"),
        (1.10, (w1[0] - 4, w1[1] + 2), b1 - 2, 1.00),
        (1.50, w1, b1, 1.00, "stop"),
    ], [(0, "open"), (0.12, "fist")])
    brace = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.40, (432, -30), 0, 1.00),
        (0.85, (395, -46), 2, 1.00, "stop"),
        (1.40, w2, b2, 1.00, "stop"),
    ], [(0, "open")])
    beats = [
        (0.10, {"b1y": 9, "b1x": -6, "b1r": 2.0, "hx": 11, "hr": -4, "hsx": 0.95, "s1x": 8}),
        (0.36, {"b1y": -32, "b1x": -2, "b1r": 3.2, "hx": -4, "hr": -6, "hsx": 1.0, "s1x": -6, "s1o": -4}),
        (0.85, {"b1y": (-130, "hit3"), "b1x": (9, "hit3"), "b1r": (-8.0, "hit3"), "b2r": 4.5, "b3r": 3.5, "b2t": -6,
                "hx": (-31, "hit3"), "hr": (-18, "hit3"), "hy": -7, "s1x": -22, "s1o": -10, "s2x": 13, "s2o": 3, "f1": -9}),
        (1.06, {"b1y": -112, "hx": -18, "hr": -11, "b2t": -2}),
        (1.38, {"b1y": -125, "hx": -26, "hr": -14, "b2t": -5}),
    ]
    return Clip("weak_enter", d, channels(d, beats, end=WEAK_L), {1: broken, 2: brace},
                impacts=[(0.85, 1.0)], exact=[0.85], source="weak_enter")


def weak_attack() -> Clip:
    """From the prone stance the healthy arm cocks beside the face and swats down."""
    d = 2.6
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    striker = arm([
        (0.00, w2, b2, 1.00, "stop"),
        (0.20, (406, -30), -3, 1.00),
        (0.60, (262, 210), -5, 0.96, "stop"),
        (1.00, (250, 222), -5, 1.00),
        (1.20, (131, -147), 18, 1.13, "hit3"),
        (1.35, (126, -153), 15, 1.12, "stop"),
        (1.72, (300, 2), 5, 1.04),
        (2.18, (395, -38), 0, 1.00),
        (2.34, w2, b2, 1.00, "stop"),
    ], [(0, "open"), (0.20, "fist"), (2.16, "open")])
    lying = arm([
        (0.00, w1, b1, 1.00, "stop"),
        (0.60, (w1[0] - 8, w1[1] + 3), b1 + 2, 1.00),
        (1.20, (w1[0] + 6, w1[1] - 2), b1 - 2, 1.00),
        (1.90, w1, b1, 1.00, "stop"),
    ], [(0, "fist")])
    w = WEAK_L
    beats = [
        (0.60, {"b1y": w["b1y"] + 22, "b1x": w["b1x"] - 16, "b1r": w["b1r"] + 5.0, "s2x": w["s2x"] + 12, "s2o": w["s2o"] + 9,
                "hx": w["hx"] + 12, "hr": w["hr"] + 5, "hy": w["hy"] + 9, "hsx": 0.96}),
        (1.20, {"b1y": (w["b1y"] - 12, "hit3"), "b1x": (w["b1x"] + 6, "hit3"), "b1r": (w["b1r"] - 3.5, "hit3"),
                "s2x": (w["s2x"] - 12, "hit3"), "s2o": (w["s2o"] - 10, "hit3"), "hx": (w["hx"] - 8, "hit3"),
                "hr": w["hr"] - 6, "hy": w["hy"] - 4, "hsx": 1.0}),
        (1.36, {"b1y": w["b1y"] - 16, "hx": w["hx"] - 10}),
        (1.85, {"b1y": w["b1y"] + 3, "b1x": w["b1x"] - 2, "b1r": w["b1r"] + 0.8, "s2x": w["s2x"] + 2, "s2o": w["s2o"],
                "hx": w["hx"] + 2, "hr": w["hr"] + 1, "hy": w["hy"] + 1}),
    ]
    return Clip("weak_attack", d, channels(d, beats, start=WEAK_L, end=WEAK_L), {1: lying, 2: striker},
                impacts=[(1.20, 0.8)], exact=[1.20], source="weak_attack")


def weak_recover() -> Clip:
    d = 2.6
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    healed = arm([
        (0.00, w1, b1, 1.00, "stop"),
        (0.45, (w1[0] - 20, w1[1] + 8), b1, 1.00, "stop"),
        (0.90, (-320, 40), 0, 1.00),
        (1.20, (-360, 80), -3, 0.98, "stop"),
        (1.60, (-428, -24), 0, 1.00, "hit"),
        (1.80, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "fist"), (1.56, "open")])
    support = arm([
        (0.00, w2, b2, 1.00, "stop"),
        (0.45, (398, -52), 2, 1.00, "stop"),
        (1.00, (432, -20), 0, 1.00),
        (1.40, REST_WRIST[2], 0, 1.00, "stop"),
    ], [(0, "open")])
    beats = [
        (0.45, {"b1y": -94, "b1x": 4, "b1r": -5.0, "hx": -10, "hr": -8, "hy": -3, "s2x": 17, "s2o": 3, "s1x": -12,
                "s1o": -6, "f1": -10, "b2t": -2}),
        (1.20, {"b1y": 27, "b1x": 0, "b1r": 1.0, "b2r": 0, "b3r": 0, "b2t": 3, "hx": 15, "hr": 1, "hy": 0, "hsx": 0.91,
                "horn": 1.06, "s1x": 13, "s1o": 8, "s2x": 13, "s2o": 8, "f1": 0, "bsx": 1.06}),
        (1.62, {"b1y": -9, "b2t": -2, "hx": -5, "hsx": 1.0, "horn": 1.0, "s1x": -3, "s1o": -2, "s2x": -3, "s2o": -2, "bsx": 1.0}),
        (2.12, {"b1y": 3, "b2t": 0.8, "hx": 1.5}),
    ]
    return Clip("weak_recover", d, channels(d, beats, start=WEAK_L), {1: healed, 2: support},
                impacts=[(1.60, 0.4)], source="weak_recover")


# ---------------------------------------------------------------------------- phase change and death

def main_angry() -> Clip:
    """Brooding build-up, eyes ignite (4.60), reared-up flexing roar (5.33), clasped double hammer (7.00)."""
    d = 11.2
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (4.60, (-426, -40), -2, 1.00, "stop"),
        (4.78, (-440, 40), 0, 1.00),
        (5.05, (-330, 90), -3, 0.98),
        (5.33, (-235, 120), -5, 0.96, "stop"),
        (6.10, (-240, 128), -5, 0.96),
        (6.40, (-150, 330), -5, 0.95),
        (6.58, (-95, 400), -5, 0.94, "stop"),
        (7.00, (-200, -110), 25, 1.13, "hit"),
        (7.18, (-205, -117), 22, 1.12, "stop"),
        (7.80, (-240, -80), 15, 1.08, "stop"),
        (8.40, (-424, -26), 2, 1.00),
        (8.67, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open"), (6.35, "fist"), (8.32, "open")])
    right = arm(mirror_path(shift_beats(path, after=7.5, by=0.14)), [(0, "open"), (6.35, "fist"), (8.46, "open")])
    beats = [
        (1.50, {"b1y": 12, "s1x": 6, "s2x": 6, "hx": -8, "hr": 1, "b2t": 2}),
        (3.00, {"b1y": -18, "s1x": 10, "s1o": -6, "s2x": 10, "s2o": -6, "hx": -18, "f1": -8, "f2": -8, "b2t": -4, "hr": -1}),
        (4.40, {"b1y": -34, "s1x": 14, "s1o": -9, "s2x": 14, "s2o": -9, "hx": -25, "f1": -11, "f2": -11, "b2t": -6, "hr": -2}),
        (4.60, {"b1y": (-37, "stop"), "hx": (-27, "stop")}),
        (4.74, {"b1y": -24, "hx": -4, "hr": 2.5, "hsx": 0.96}),
        (5.33, {"b1y": 44, "s1x": 26, "s1o": 14, "s2x": 26, "s2o": 14, "hx": 28, "hr": 0, "hsx": 0.84, "bsx": 1.12,
                "horn": 1.22, "b2t": 5, "f1": 0, "f2": 0}),
        (6.10, {"b1y": 48, "hx": 30, "hsx": 0.83, "horn": 1.24, "bsx": 1.14, "s1x": 27, "s2x": 27}),
        (6.58, {"b1y": 42, "hx": 22, "hsx": 0.86, "horn": 1.2, "bsx": 1.1, "s1x": 24, "s1o": 12, "s2x": 24, "s2o": 12}),
        (7.00, {"b1y": (-40, "hit3"), "s1x": (-16, "hit3"), "s1o": (-10, "hit3"), "s2x": (-16, "hit3"), "s2o": (-10, "hit3"),
                "hx": (-24, "hit3"), "hsx": 1.0, "horn": 1.1, "bsx": 1.0, "b2t": -6}),
        (7.16, {"b1y": -49, "hx": -29, "b2t": -9}),
        (7.62, {"b1y": -30, "hx": -20, "b2t": -4, "s1x": -8, "s1o": -6, "s2x": -8, "s2o": -6}),
        (8.12, {"b1y": -6, "hx": -6, "s1x": 6, "s1o": -2, "s2x": 6, "s2o": -2, "b2t": 1}),
        (8.67, {"b1y": -20, "hx": -15, "s1x": -2, "s2x": -2, "b2t": -3}),
        (9.40, {"b1y": 1, "hx": -6, "s1x": 4, "s2x": 4, "b2t": 0.5}),
        (10.20, {"b1y": -3, "hx": -2, "s1x": 1, "s2x": 1}),
        (10.90, {"b1y": (0, "stop"), "hx": (0, "stop"), "s1x": (0, "stop"), "s2x": (0, "stop"), "s1o": (0, "stop"),
                 "s2o": (0, "stop"), "b2t": (0, "stop"), "horn": (1.0, "stop"), "hr": (0, "stop")}),
    ]
    ch = channels(d, beats)
    shaken = dict(ch)

    def env(t):
        build = smooth(seg(t, 1.6, 4.5)) * 0.65
        roar = window(t, 5.25, 5.45, 6.30, 6.55) * 2.4
        return build * (1 - smooth(seg(t, 4.55, 4.75))) + roar

    for name, amp, seed in (("b3r", 0.45, 1), ("hr", 0.6, 2), ("hx", 1.6, 3), ("b1x", 1.3, 4), ("s1x", 1.0, 5), ("s2x", 1.0, 6)):
        curve = ch.get(name, const(0.0))
        shaken[name] = (lambda c, a, s: lambda t: c(t) + env(t) * tremor(t, 11.2, a, s))(curve, amp, seed)
    return Clip("main_angry", d, shaken, {1: left, 2: right}, impacts=[(4.60, 0.45), (7.00, 1.6)], exact=[4.6, 5.33, 7.0],
                posture=lambda t: smoother(seg(t, 7.4, 10.6)), source="main_angry")


def hide() -> Clip:
    """Death: the core recoils clutching its chest, collapses onto the platform edge, clings, slips under."""
    d = 7.2
    left = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.22, (-360, 60), 0, 0.98),
        (0.55, (-245, 115), -5, 0.95, "stop"),
        (1.20, (-520, 40), 5, 0.98),
        (1.67, (-470, -60), 15, 1.00, "hit3"),
        (2.20, (-470, -60), 15, 1.00, "stop"),
        (2.70, (-471, -63), 15, 1.00, "stop"),
        (3.17, (-472, -72), 12, 1.00, "stop"),
        (3.42, (-462, -300), 8, 1.00),
        (3.85, (-442, -720), 5, 1.00, "stop"),
        (7.20, (-440, -1080), 5, 1.00, "stop"),
    ], [(0, "open"), (1.62, "fist"), (3.20, "open")])
    right = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.24, (360, 60), 0, 0.98),
        (0.58, (245, 115), -5, 0.95, "stop"),
        (1.20, (520, 40), 5, 0.98),
        (1.67, (470, -60), 15, 1.00, "hit3"),
        (3.40, (470, -60), 15, 1.00, "stop"),
        (4.30, (470, -62), 15, 1.00, "stop"),
        (4.67, (472, -76), 12, 1.00, "stop"),
        (4.92, (462, -350), 8, 1.00),
        (5.40, (442, -760), 5, 1.00, "stop"),
        (7.20, (440, -1100), 5, 1.00, "stop"),
    ], [(0, "open"), (1.62, "fist"), (4.70, "open")])
    beats = [
        (0.20, {"b1y": 18, "hx": 18, "hsx": 0.9, "horn": 1.1, "s1x": 14, "s2x": 14, "bsx": 1.06}),
        (0.55, {"b1y": 25, "hx": 22, "hsx": 0.86, "horn": 1.15, "s1x": 18, "s2x": 18, "bsx": 1.1}),
        (1.20, {"b1y": -20, "hx": -14, "hsx": 1.0, "horn": 1.02, "s1x": 2, "s2x": 2, "bsx": 1.0}),
        (1.67, {"b1y": (-72, "hit3"), "hx": (-30, "hit3"), "s1x": -10, "s2x": -10, "horn": 1.0}),
        (2.20, {"b1y": -165, "b1r": 2.0, "hx": -20}),
        (2.45, {"b1y": -122, "b1r": -1.0, "hx": -8}),
        (2.72, {"b1y": -195, "b1r": -3.0, "hx": -24}),
        (2.96, {"b1y": -160, "b1r": 0.5, "hx": -12}),
        (3.17, {"b1y": (-245, "hit"), "b1r": 6.0, "b1x": -20, "hx": -26}),
        (3.85, {"b1y": -425, "b1r": 10.0, "b1x": -42, "hx": -16}),
        (4.30, {"b1y": -472, "b1r": 8.0, "b1x": -38, "hx": -12}),
        (4.67, {"b1y": (-560, "hit"), "b1r": -4.0, "b1x": 0, "hx": -20}),
        (5.40, {"b1y": -770, "b1r": -2.0, "hx": -10}),
        (6.35, {"b1y": -880, "b1r": 0.0}),
        (6.57, {"b1y": (-945, "hit"), "b1r": 1.5}),
        (7.20, {"b1y": (-1150, "stop"), "b1r": (0, "stop"), "b1x": (0, "stop"), "hx": (-10, "stop")}),
    ]
    return Clip("hide", d, channels(d, beats), {1: left, 2: right},
                impacts=[(1.67, 1.1), (3.17, 0.6), (4.67, 0.6), (6.57, 0.4)], source="hide")


# ---------------------------------------------------------------------------- variants

ATTACK_BUILDERS = (alternating_jabs, central_slam, double_fist_crush)
SIDED_BUILDERS = (front_sweep, card_snatch, grab_player, grab_slam, weak_enter, weak_attack, weak_recover)
