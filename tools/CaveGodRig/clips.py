"""Choreography of every in-game 活体巨岩 clip — Heavy Monolithic Basalt Edition.

Torso, head and shoulder channels are deltas from the rest pose (old idle_front t=0, kept so hand
anchors and UI positions do not move). Arms are world-space wrist paths (Spine skeleton space, Y up)
solved by IK in build.py. The hand is authored as an inward wrist bend relative to the forearm
(0 = rest), strictly inside [-8°, +38°] so neither weighted forearm/hand mesh ever folds triangles.
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
PALM_REACH = 95.0                 # nominal reach along hand bone
# Bone-local grip centre of arm1_3 (thumb +Y) and arm2_3 (thumb -Y, bone origin 22u closer to knuckles)
PALM_LOCAL = {1: (70.0, 31.0), 2: (57.0, -46.0)}
GRIP_DROP = 34.0                  # captive boots stay enclosed inside fist; waist sits at thumb ridge; chest/head emerge above

# Channels: body1 x/y/rot, body2/body3 rot and stretch along the spine, head translate in the body3
# frame (x up, y left), head rot / scaleX, beard rot / scaleX, horn scaleX, shoulders (x up,
# o outward, r rot), finger curl (negative tightens), lower trunk sway.
CHANNELS = ("b1x", "b1y", "b1r", "b2r", "b2t", "b3r", "b3t", "hx", "hy", "hr", "hsx", "brd", "bsx", "horn",
            "s1x", "s1o", "s1r", "s2x", "s2o", "s2r", "f1", "f2", "bb")
MULTIPLY = {"hsx", "bsx", "horn"}

# Deeper predatory magma crouch, hunched shoulders, fiercer crystal horn flare, tighter clenched fists.
ANGRY = {"b1y": -14.0, "b2t": -4.5, "b3t": -1.5, "hx": -13.0, "s1x": 8.5, "s1o": -5.0, "s2x": 8.5, "s2o": -5.0,
         "horn": 1.10, "f1": -5.5, "f2": -5.5}

# Exposed core after the LEFT arm is knocked down (mirrored for the right arm).
WEAK_L = {"b1y": -124.0, "b1x": 9.0, "b1r": -7.5, "b2r": 3.8, "b3r": 2.8, "b2t": -4.5, "hx": -25.0, "hr": -13.5,
          "hy": -5.5, "s1x": -19.0, "s1o": -8.5, "s2x": 11.0, "s2o": 2.5, "f1": -7.5}
WEAK_L_ARMS = {1: ((-150.0, -58.0), 10.0), 2: ((392.0, -42.0), 0.0)}

# Prey held up beside the face (left arm). Shared identically by grab_player's plateau, grab_hold and grab_slam.
HOLD_L = {"b1y": 14.0, "b1x": 7.0, "b1r": -2.2, "b2t": 1.8, "s1x": 10.0, "s1o": 3.5, "hx": -6.5, "hr": -4.2,
          "hy": 7.0, "hsx": 1.0, "f2": -4.5}
# Upright inward bend (28° inside [-8°, +38°]) tilts curled fingers across lower-front & thumb-index opening upward.
HOLD_L_ARM = ((-306.0, 208.0), 28.0)


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
    lock_modes = ("hit", "hit3", "hit4", "slam", "stop", "drag")
    for row in beats:
        t, p, b, s = row[:4]
        mode = row[4] if len(row) > 4 else "auto"
        if p is not None:
            pos.append((t, p, mode))
        if b is not None:
            bend.append((t, b, "stop" if mode in lock_modes else "auto"))
        if s is not None:
            scl.append((t, s, "stop" if mode in lock_modes else "auto"))
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
    """Delay the later part of a mirrored path so the two stone hands unpeel and settle one after the other."""
    return [(row[0] + by if row[0] > after else row[0], *row[1:]) for row in path]


def held(t: float, palm: tuple, lifted: float) -> tuple:
    return mix(GROUND, (palm[0], palm[1] - GRIP_DROP), lifted)


# ---------------------------------------------------------------------------- loops

def idle(angry: bool) -> Clip:
    p = 4.0

    def b(t, d=0.0):
        peak = 0.58 if angry else 0.62
        return breath((t - d) % p, p, 1, peak) - breath((-d) % p, p, 1, peak)

    if not angry:
        # Anchored colossal stone monolith: slow 4s tectonic pressure heave, planted palms, zero lateral drift.
        ch = {
            "b1y": lambda t: 3.6 * b(t),
            "b1x": lambda t: 0.55 * wave(t, p, 1, 0.7),
            "b1r": lambda t: 0.16 * wave(t, p, 1, 1.4),
            "b2r": lambda t: -0.12 * wave(t, p, 1, 2.0),
            "b3r": lambda t: 0.16 * wave(t, p, 1, 2.6),
            "b2t": lambda t: 0.85 * b(t, 0.26),
            "b3t": lambda t: 0.45 * b(t, 0.36),
            "s1x": lambda t: 2.5 * b(t, 0.14),
            "s2x": lambda t: 2.5 * b(t, 0.22),
            "s1o": lambda t: 0.9 * b(t, 0.18),
            "s2o": lambda t: 0.9 * b(t, 0.26),
            "s1r": lambda t: 0.22 * wave(t, p, 1, 0.8),
            "s2r": lambda t: -0.22 * wave(t, p, 1, 1.6),
            "hx": lambda t: -1.6 * b(t, 0.42),
            "hr": lambda t: 0.32 * wave(t, p, 1, 2.2),
            "hy": lambda t: 0.55 * wave(t, p, 1, 1.9),
            "horn": lambda t: 1.0 + 0.010 * b(t, 0.32),
            "brd": lambda t: 0.60 * wave(t, p, 1, 2.9),
            "f1": const(0.0),
            "f2": const(0.0),
        }
    else:
        # Lowered volcanic monolith stance: deep pressurized 4s furnace breath, immovable footing, ZERO baked tremor
        # (trembling is layered strictly via critical_tremor when HP <= 20%).
        ch = {
            "b1y": lambda t: 4.6 * b(t),
            "b1x": lambda t: 0.70 * wave(t, p, 1, 0.5),
            "b1r": lambda t: 0.20 * wave(t, p, 1, 1.2),
            "b2r": lambda t: -0.15 * wave(t, p, 1, 1.8),
            "b3r": lambda t: 0.22 * wave(t, p, 1, 2.4),
            "b2t": lambda t: 1.1 * b(t, 0.20),
            "b3t": lambda t: 0.6 * b(t, 0.30),
            "s1x": lambda t: 3.4 * b(t, 0.12),
            "s2x": lambda t: 3.4 * b(t, 0.18),
            "s1o": lambda t: -1.1 * b(t, 0.16),
            "s2o": lambda t: -1.1 * b(t, 0.22),
            "hx": lambda t: -2.2 * b(t, 0.36),
            "hr": lambda t: 0.40 * wave(t, p, 1, 2.0),
            "hy": lambda t: 0.65 * wave(t, p, 1, 1.7),
            "horn": lambda t: 1.0 + 0.016 * b(t, 0.24),
            "brd": lambda t: 0.75 * wave(t, p, 1, 2.7),
            "f1": const(0.0),
            "f2": const(0.0),
        }
    return Clip("idle_front_angry" if angry else "idle_front", p, ch, {1: planted(1), 2: planted(2)},
                loop=True, secondary=False, source="idle_front")


def grab_hold(angry: bool) -> Clip:
    """Loop while the captive is locked inside an immovable stone vise."""
    p = 3.0

    def b(t, d=0.0):
        return breath((t - d) % p, p, 1, 0.58) - breath((-d) % p, p, 1, 0.58)

    base = dict(HOLD_L)
    ch = {
        "b1y": lambda t: base["b1y"] + 3.0 * b(t),
        "b1x": lambda t: base["b1x"] + 0.75 * wave(t, p, 1, 0.8),
        "b1r": lambda t: base["b1r"] + 0.22 * wave(t, p, 1, 1.5),
        "b3r": lambda t: 0.20 * wave(t, p, 1, 2.3),
        "b2t": lambda t: base["b2t"] + 0.75 * b(t, 0.22),
        "s1x": lambda t: base["s1x"] + 2.2 * b(t, 0.10),
        "s1o": lambda t: base["s1o"] + 0.6 * b(t, 0.16),
        "s2x": lambda t: 1.8 * b(t, 0.18),
        "hx": lambda t: base["hx"] - 1.4 * b(t, 0.38),
        "hr": lambda t: base["hr"] + 0.55 * wave(t, p, 1, 2.3),
        "hy": lambda t: base["hy"] + 0.75 * wave(t, p, 1, 1.5),
        "hsx": lambda t: base["hsx"],
        "brd": lambda t: 0.65 * wave(t, p, 1, 2.8),
        "f2": const(base["f2"]),
    }
    (wx, wy), wb = HOLD_L_ARM

    def wrist(t):
        return (wx + 1.5 * wave(t, p, 1, 0.4),
                wy + 2.0 * b(t, 0.12))

    hold = Arm(wrist, lambda t: wb + 1.0 * wave(t, p, 1, 1.6), const(1.0), [(0, "fist")])
    return Clip("grab_hold_angry" if angry else "grab_hold", p, ch, {1: hold, 2: planted(2)},
                grab=lambda t, palm: held(t, palm, 1.0), loop=True, secondary=False, source="grab_player")


def weak_idle(angry: bool) -> Clip:
    p = 3.2

    def b(t, d=0.0):
        return breath((t - d) % p, p, 1, 0.65) - breath((-d) % p, p, 1, 0.65)

    def twitch(t):
        return window(t, 2.04, 2.13, 2.22, 2.56)

    ch = {k: const(v) for k, v in WEAK_L.items()}
    ch.update({
        "b1y": lambda t: WEAK_L["b1y"] + 4.2 * b(t),
        "b1x": lambda t: WEAK_L["b1x"] + 0.8 * wave(t, p, 1, 0.9),
        "b1r": lambda t: WEAK_L["b1r"] + 0.25 * wave(t, p, 1, 1.4),
        "b2t": lambda t: WEAK_L["b2t"] + 1.0 * b(t, 0.25),
        "hx": lambda t: WEAK_L["hx"] - 1.8 * b(t, 0.50),
        "hr": lambda t: WEAK_L["hr"] + 0.55 * wave(t, p, 1, 2.1),
        "hy": lambda t: WEAK_L["hy"] + 0.8 * wave(t, p, 1, 1.2),
        "brd": lambda t: 0.75 * wave(t, p, 1, 2.7),
        "s2x": lambda t: WEAK_L["s2x"] + 2.2 * b(t, 0.10),
        "s2o": const(WEAK_L["s2o"]),
        "s1x": lambda t: WEAK_L["s1x"] + 1.4 * b(t, 0.22),
        "f1": lambda t: WEAK_L["f1"] - 4.5 * twitch(t),
    })
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    broken = Arm(lambda t: (w1[0] + 2.2 * twitch(t), w1[1] + 3.5 * twitch(t)), lambda t: b1 + 3.0 * twitch(t),
                 const(1.0), [(0, "fist")])
    return Clip("weak_idle_angry" if angry else "weak_idle", p, ch, {1: broken, 2: planted(2, w2, b2)},
                loop=True, secondary=False, source="weak_idle")


# ---------------------------------------------------------------------------- overlays

def critical_tremor() -> Clip:
    """Seamless additive fracture tremor on Track 2, active strictly when CaveGod HP <= 20% in Phase 2."""
    p = 2.0
    ch = {
        "b1x": lambda t: tremor(t, p, 2.2, 4),
        "b1y": lambda t: tremor(t, p, 1.5, 1),
        "b1r": lambda t: tremor(t, p, 0.34, 9),
        "b2r": lambda t: tremor(t, p, 0.28, 6),
        "b3r": lambda t: tremor(t, p, 0.52, 2),
        "hx": lambda t: tremor(t, p, 2.4, 7),
        "hr": lambda t: tremor(t, p, 0.72, 3),
        "horn": lambda t: 1.0 + 0.014 * tremor(t, p, 1.0, 8),
    }
    return Clip("critical_tremor", p, ch, {}, loop=True, secondary=False, overlay=True, source="hit_recoil")


def hit_recoil(angry: bool) -> Clip:
    """Compact high-mass basalt flinch on Track 1 (deltas from rest; shoulders locked; zero rubber bounce)."""
    d = 0.72
    gain = 1.22 if angry else 1.0
    names = ("b1y", "b3r", "b2r", "hx", "hr", "hy", "hsx", "horn", "b2t", "b1x")
    neutral = lambda k: 1.0 if k in MULTIPLY else 0.0  # noqa: E731
    beats = [
        (0.03, {k: (neutral(k), "stop") for k in names}),
        (0.09, {"b1y": (-7.5, "hit3"), "b3r": (1.4, "hit3"), "b2r": (-0.8, "hit3"), "hx": (6.8, "hit3"),
                "hr": (-2.6, "hit3"), "hy": -1.8, "hsx": 1.0, "horn": 1.012, "b2t": (-2.4, "hit3"), "b1x": 1.8}),
        (0.14, {"b1y": (-8.0, "stop"), "b3r": (1.5, "stop"), "b2r": (-0.85, "stop"), "hx": (7.2, "stop"),
                "hr": (-2.8, "stop"), "hy": -1.9, "hsx": 1.0, "horn": 1.012, "b2t": (-2.6, "stop"), "b1x": 1.9}),
        (0.34, {"b1y": -2.4, "b3r": 0.45, "b2r": -0.25, "hx": 2.2, "hr": -0.85, "hy": -0.6, "hsx": 1.0,
                "horn": 1.003, "b2t": -0.8, "b1x": 0.6}),
        (0.64, {k: (neutral(k), "stop") for k in names}),
    ]

    def scaled(ch, v):
        value, *mode = v if isinstance(v, tuple) else (v,)
        value = 1 + (value - 1) * gain if ch in MULTIPLY else value * gain
        return (value, *mode) if mode else value

    beats = [(t, {ch: scaled(ch, v) for ch, v in pose.items()}) for t, pose in beats]
    return Clip("hit_recoil_angry" if angry else "hit_recoil", d, channels(d, beats), {}, overlay=True,
                impacts=[(0.05, 0.45 * gain)], source="hit_recoil")


# ---------------------------------------------------------------------------- body & single-arm attacks

def alternating_jabs() -> Clip:
    """Left boulder jab (0.64), right boulder jab (1.34), then colossal full-body left haymaker (2.32)."""
    d = 3.5
    left = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.13, (-434, -46), -3, 1.00, "stop"),
        (0.32, (-246, 206), -5, 0.96, "stop"),
        (0.40, (-240, 218), -5, 0.96, "stop"),
        (0.64, (-170, -130), 22, 1.14, "slam"),
        (0.76, (-170, -130), 22, 1.14, "stop"),
        (1.00, (-302, 30), 6, 1.00, "drag"),
        (1.22, (-308, 42), 5, 1.00, "stop"),
        (1.56, (-320, 22), 2, 0.99, "stop"),
        (1.90, (-208, 358), -5, 0.94, "stop"),
        (2.02, (-200, 370), -5, 0.94, "stop"),
        (2.32, (-180, -128), 26, 1.17, "slam"),
        (2.52, (-180, -128), 26, 1.17, "stop"),
        (2.70, (-212, -116), 16, 1.11, "drag"),
        (3.00, (-392, 22), 4, 1.01),
        (3.20, (-424, -38), 0, 1.00, "stop"),
        (3.34, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (0.14, "fist"), (3.16, "open")])
    right = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.72, REST_WRIST[2], 0, 1.00, "stop"),
        (0.84, (438, -44), -3, 1.00, "stop"),
        (1.02, (246, 206), -5, 0.96, "stop"),
        (1.10, (240, 218), -5, 0.96, "stop"),
        (1.34, (168, -136), 22, 1.14, "slam"),
        (1.46, (168, -136), 22, 1.14, "stop"),
        (1.74, (396, 32), 4, 1.01, "drag"),
        (1.94, (424, -37), 0, 1.00, "stop"),
        (2.08, REST_WRIST[2], 0, 1.00, "stop"),
    ], [(0, "open"), (0.84, "fist"), (1.92, "open")])
    beats = [
        (0.13, {"b1x": 5, "b1y": -11, "b1r": -0.9, "b2t": -3.0, "s1x": -5, "s1o": -3, "s2x": -2, "f1": -6, "f2": -6, "hx": -5}),
        (0.38, {"b1x": 18, "b1y": 9, "b1r": -3.2, "b2t": 1.8, "hx": 5, "hr": -2.0, "hy": -3,
                "s1x": 13, "s1o": 8, "s2x": -4, "s2o": -2, "f1": 0, "f2": -7}),
        (0.64, {"b1x": (-15, "slam"), "b1y": (-24, "slam"), "b1r": (3.4, "slam"), "b2t": (-5.0, "slam"),
                "hx": (-18, "hit3"), "hr": (4.0, "hit3"), "hy": 4, "s1x": (-13, "slam"), "s1o": (-13, "slam"), "s2x": 6, "s2o": 2, "f2": -5}),
        (0.74, {"b1x": (-15, "stop"), "b1y": (-24, "stop"), "b1r": (3.4, "stop"), "b2t": (-5.0, "stop"),
                "hx": (-18, "stop"), "hr": (4.0, "stop"), "hy": (4, "stop"), "s1x": (-13, "stop"), "s1o": (-13, "stop")}),
        (0.86, {"b1x": -9, "b1y": -14, "b1r": 2.0, "b2t": -3.0, "hx": -10, "hr": 1.8, "hy": 2, "s1x": -4, "s1o": -5, "s2x": -4, "s2o": -2}),
        (1.08, {"b1x": -20, "b1y": 9, "b1r": 3.4, "b2t": 1.8, "hx": 4, "hr": -2.4, "hy": -3,
                "s1x": 3, "s1o": 2, "s2x": 13, "s2o": 8, "f2": 0}),
        (1.34, {"b1x": (15, "slam"), "b1y": (-25, "slam"), "b1r": (-3.4, "slam"), "b2t": (-5.0, "slam"),
                "hx": (-18, "hit3"), "hr": (-4.0, "hit3"), "hy": -4, "s1x": 6, "s1o": 2, "s2x": (-13, "slam"), "s2o": (-13, "slam")}),
        (1.44, {"b1x": (15, "stop"), "b1y": (-25, "stop"), "b1r": (-3.4, "stop"), "b2t": (-5.0, "stop"),
                "hx": (-18, "stop"), "hr": (-4.0, "stop"), "hy": (-4, "stop"), "s2x": (-13, "stop"), "s2o": (-13, "stop")}),
        (1.62, {"b1x": 10, "b1y": -15, "b1r": -1.8, "b2t": -3.5, "hx": -11, "hr": -2.0, "hy": -2, "s1x": -5, "s1o": -3, "s2x": -4, "s2o": -4}),
        (1.98, {"b1x": 28, "b1y": 19, "b1r": -5.2, "b2t": 3.8, "hx": 11, "hr": -3.2, "hy": -5, "hsx": 0.99, "horn": 1.04,
                "s1x": 18, "s1o": 11, "s2x": -5, "s2o": -3, "f2": -7}),
        (2.32, {"b1x": (-20, "slam"), "b1y": (-40, "slam"), "b1r": (5.4, "slam"), "b2t": (-8.0, "slam"),
                "hx": (-26, "hit3"), "hr": (5.6, "hit3"), "hy": 6, "hsx": 1.0, "horn": 1.0,
                "s1x": (-17, "slam"), "s1o": (-17, "slam"), "s2x": 9, "s2o": 4}),
        (2.50, {"b1x": (-20, "stop"), "b1y": (-40, "stop"), "b1r": (5.4, "stop"), "b2t": (-8.0, "stop"),
                "hx": (-26, "stop"), "hr": (5.6, "stop"), "hy": (6, "stop"), "hsx": (1.0, "stop"), "s1x": (-17, "stop"), "s1o": (-17, "stop")}),
        (2.78, {"b1x": -11, "b1y": -22, "b1r": 3.0, "b2t": -4.0, "hx": -14, "hr": 3.0, "hy": 3, "hsx": 1.0, "s1x": -8, "s1o": -9, "s2x": 4, "s2o": 2}),
        (3.12, {"b1x": -2, "b1y": -4, "b1r": 0.5, "b2t": -0.8, "hx": -2.5, "hr": 0.5, "hy": 0.5, "s1x": -1.5, "s1o": -1.5, "s2x": 0.5, "s2o": 0, "f2": -1}),
        (3.34, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1x", "b1y", "b1r", "b2t", "hx", "hr", "hy", "hsx", "horn", "s1x", "s1o", "s2x", "s2o", "f1", "f2")}),
    ]
    return Clip("alternating_jabs", d, channels(d, beats), {1: left, 2: right},
                impacts=[(0.64, 0.72), (1.34, 0.72), (2.32, 1.25)], exact=[0.64, 1.34, 2.32, 3.45], source="alternating_jabs")


def leftpunch() -> Clip:
    """Dedicated heavy single-hand boulder smash (duration 2.95s, central_hit @ 1.18s, attack_recover @ 2.85s)."""
    d = 2.95
    puncher = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.18, (-434, -48), -3, 1.00, "stop"),
        (0.50, (-312, 172), -4, 0.97),
        (0.82, (-206, 352), -5, 0.94, "stop"),
        (0.93, (-198, 366), -5, 0.94, "stop"),
        (1.18, (-68, -142), 25, 1.16, "slam"),
        (1.38, (-68, -142), 25, 1.16, "stop"),
        (1.62, (-104, -126), 16, 1.10, "drag"),
        (2.14, (-340, 22), 4, 1.01),
        (2.54, (-424, -38), 0, 1.00, "stop"),
        (2.76, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (0.16, "fist"), (2.50, "open")])
    beats = [
        (0.18, {"b1x": 7, "b1y": -14, "b1r": -1.3, "b2t": -3.5, "hx": -7, "s1x": -6, "s1o": -4, "s2x": -3, "f1": -6, "f2": -7}),
        (0.52, {"b1x": 19, "b1y": 12, "b1r": -3.5, "b2t": 1.8, "hx": 5, "hr": -2.2, "hy": -3, "s1x": 14, "s1o": 8, "s2x": -4, "s2o": -2, "f1": 0, "f2": -8}),
        (0.84, {"b1x": 27, "b1y": 23, "b1r": -5.0, "b2t": 3.6, "hx": 11, "hr": -3.0, "hy": -5, "hsx": 0.99, "horn": 1.04,
                "s1x": 18, "s1o": 11, "s2x": -5, "s2o": -3, "f2": -8}),
        (0.93, {"b1x": 29, "b1y": 25, "b1r": -5.3, "b2t": 4.0, "hx": 12, "hr": -3.2, "hy": -5, "hsx": 0.99, "horn": 1.05,
                "s1x": 20, "s1o": 12, "s2x": -5, "s2o": -3, "f2": -8}),
        (1.18, {"b1x": (-18, "slam"), "b1y": (-38, "slam"), "b1r": (5.2, "slam"), "b2t": (-7.5, "slam"),
                "hx": (-25, "hit3"), "hr": (5.2, "hit3"), "hy": 6, "hsx": 1.0, "horn": 1.0,
                "s1x": (-16, "slam"), "s1o": (-15, "slam"), "s2x": 8, "s2o": 3, "f2": -5}),
        (1.36, {"b1x": (-18, "stop"), "b1y": (-38, "stop"), "b1r": (5.2, "stop"), "b2t": (-7.5, "stop"),
                "hx": (-25, "stop"), "hr": (5.2, "stop"), "hy": (6, "stop"), "hsx": (1.0, "stop"), "s1x": (-16, "stop"), "s1o": (-15, "stop")}),
        (1.70, {"b1x": -10, "b1y": -21, "b1r": 3.0, "b2t": -4.0, "hx": -14, "hr": 2.8, "hy": 3.5, "hsx": 1.0, "s1x": -8, "s1o": -8, "s2x": 4, "s2o": 1.5}),
        (2.30, {"b1x": -2.5, "b1y": -5, "b1r": 0.7, "b2t": -1.0, "hx": -3, "hr": 0.6, "hy": 0.8, "s1x": -2, "s1o": -1.5, "s2x": 0.5, "s2o": 0, "f2": -1.5}),
        (2.76, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1x", "b1y", "b1r", "b2t", "hx", "hr", "hy", "hsx", "horn", "s1x", "s1o", "s2x", "s2o", "f1", "f2")}),
    ]
    return Clip("leftpunch", d, channels(d, beats), {1: puncher, 2: planted(2)},
                impacts=[(1.18, 1.15)], exact=[1.18, 2.85], source="idle_front")


def central_slam() -> Clip:
    """Deep tectonic coil, rear up with both open palms beside the crown, cataclysmic flat slap onto the platform."""
    d = 3.75
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.34, (-434, -52), -4, 1.00, "stop"),
        (0.60, (-414, 68), 0, 0.99),
        (0.96, (-316, 232), -5, 0.96),
        (1.30, (-202, 356), -5, 0.94, "stop"),
        (1.56, (-196, 370), -5, 0.94, "stop"),
        (1.88, (-158, -152), 30, 1.15, "slam"),
        (2.14, (-158, -152), 30, 1.15, "stop"),
        (2.42, (-200, -138), 18, 1.09, "drag"),
        (2.78, (-348, 0), 6, 1.02),
        (3.08, (-424, -39), 0, 1.00, "stop"),
        (3.30, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open")])
    right = arm(mirror_path(shift_beats(path, after=2.30, by=0.16)), [(0, "open")])
    beats = [
        (0.34, {"b1y": -18, "b2t": -4.5, "hx": -10, "s1x": -8, "s1o": -5, "s2x": -8, "s2o": -5, "f1": -7, "f2": -7}),
        (0.62, {"b1y": 12, "b2t": 1.8, "hx": 5, "s1x": 8, "s1o": 4, "s2x": 8, "s2o": 4, "f1": 0, "f2": 0}),
        (1.30, {"b1y": 30, "b2t": 4.4, "hx": 14, "hsx": 0.99, "hr": -1.2, "s1x": 20, "s1o": 11, "s2x": 20, "s2o": 11,
                "bsx": 1.02, "horn": 1.06}),
        (1.56, {"b1y": 34, "b2t": 5.0, "hx": 16, "hsx": 0.985, "hr": -0.8, "s1x": 22, "s1o": 12, "s2x": 22, "s2o": 12,
                "bsx": 1.03, "horn": 1.07}),
        (1.88, {"b1y": (-46, "slam"), "b2t": (-9.0, "slam"), "hx": (-26, "hit3"), "hsx": 1.0, "hr": 0.5,
                "s1x": (-19, "slam"), "s1o": (-9, "slam"), "s2x": (-19, "slam"), "s2o": (-9, "slam"), "bsx": 1.0, "horn": 1.0}),
        (2.12, {"b1y": (-46, "stop"), "b2t": (-9.0, "stop"), "hx": (-26, "stop"), "hsx": (1.0, "stop"),
                "s1x": (-19, "stop"), "s1o": (-9, "stop"), "s2x": (-19, "stop"), "s2o": (-9, "stop"), "bsx": (1.0, "stop")}),
        (2.50, {"b1y": -25, "b2t": -4.5, "hx": -14, "hsx": 1.0, "s1x": -9, "s1o": -4, "s2x": -9, "s2o": -4, "bsx": 1.0}),
        (3.06, {"b1y": -5, "b2t": -1.0, "hx": -3, "s1x": -2, "s1o": -1, "s2x": -2, "s2o": -1}),
        (3.48, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "b2t", "hx", "hsx", "hr", "s1x", "s1o", "s2x", "s2o", "bsx", "horn", "f1", "f2")}),
    ]
    return Clip("central_slam", d, channels(d, beats), {1: left, 2: right},
                impacts=[(1.88, 1.35)], exact=[1.88, 3.70], source="central_slam")


def double_fist_crush() -> Clip:
    """Deep brace, clasp both boulder-fists overhead, crush straight down (1.25), brutal aftershock pound (1.50)."""
    d = 3.35
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.24, (-434, -48), -3, 1.00, "stop"),
        (0.48, (-392, 118), 0, 0.98),
        (0.80, (-168, 366), -5, 0.95, "stop"),
        (0.98, (-160, 380), -5, 0.95, "stop"),
        (1.25, (-106, -102), 24, 1.15, "slam"),
        (1.36, (-106, -102), 24, 1.15, "stop"),
        (1.42, (-106, -74), 18, 1.12, "stop"),
        (1.50, (-102, -108), 25, 1.16, "slam"),
        (1.66, (-102, -108), 25, 1.16, "stop"),
        (1.88, (-258, -42), 12, 1.07, "drag"),
        (2.24, (-412, 14), 3, 1.01),
        (2.48, (-424, -38), 0, 1.00, "stop"),
        (2.70, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open"), (0.28, "fist"), (2.44, "open")])
    right = arm(mirror_path(shift_beats(path, after=1.74, by=0.14)), [(0, "open"), (0.28, "fist"), (2.58, "open")])
    beats = [
        (0.24, {"b1y": -14, "hx": -8, "s1x": -6, "s1o": -5, "s2x": -6, "s2o": -5, "b2t": -3.5, "f1": -6, "f2": -6}),
        (0.80, {"b1y": 26, "hx": 13, "hsx": 0.99, "s1x": 15, "s1o": 15, "s2x": 15, "s2o": 15, "b2t": 3.8, "horn": 1.05, "f1": 0, "f2": 0}),
        (0.98, {"b1y": 30, "hx": 15, "hsx": 0.985, "s1x": 17, "s1o": 16, "s2x": 17, "s2o": 16, "b2t": 4.4, "horn": 1.06}),
        (1.25, {"b1y": (-36, "slam"), "hx": (-23, "hit3"), "hsx": 1.0, "s1x": (-15, "slam"), "s1o": (-17, "slam"),
                "s2x": (-15, "slam"), "s2o": (-17, "slam"), "b2t": (-7.0, "slam"), "horn": 1.0}),
        (1.36, {"b1y": (-36, "stop"), "hx": (-23, "stop"), "hsx": (1.0, "stop"), "s1x": (-15, "stop"), "s2x": (-15, "stop"),
                "s1o": (-17, "stop"), "s2o": (-17, "stop"), "b2t": (-7.0, "stop")}),
        (1.42, {"b1y": (-26, "stop"), "hx": (-17, "stop"), "hsx": 1.0, "b2t": -4.8}),
        (1.50, {"b1y": (-42, "slam"), "hx": (-26, "hit3"), "s1x": (-17, "slam"), "s2x": (-17, "slam"),
                "s1o": (-18, "slam"), "s2o": (-18, "slam"), "b2t": (-8.5, "slam")}),
        (1.66, {"b1y": (-42, "stop"), "hx": (-26, "stop"), "s1x": (-17, "stop"), "s2x": (-17, "stop"),
                "s1o": (-18, "stop"), "s2o": (-18, "stop"), "b2t": (-8.5, "stop")}),
        (1.98, {"b1y": -21, "hx": -13, "s1x": -7, "s1o": -8, "s2x": -7, "s2o": -8, "b2t": -3.8}),
        (2.54, {"b1y": -4, "hx": -2.5, "s1x": -1.5, "s1o": -1, "s2x": -1.5, "s2o": -1, "b2t": -0.8}),
        (3.02, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "hx", "hsx", "s1x", "s1o", "s2x", "s2o", "b2t", "horn", "f1", "f2")}),
    ]
    return Clip("double_fist_crush", d, channels(d, beats), {1: left, 2: right},
                impacts=[(1.25, 1.12), (1.50, 0.55)], exact=[1.25, 3.30], source="double_fist_crush")


# ---------------------------------------------------------------------------- arm attacks (left versions)

def front_sweep() -> Clip:
    """Colossal low backhand: deep coil & counter-twist out to the side, violent tectonic rake across the stage."""
    d = 3.45
    sweeper = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.22, (-436, -46), -3, 1.00, "stop"),
        (0.50, (-635, 18), 9, 0.97),
        (0.76, (-806, 28), 14, 0.95, "stop"),
        (0.90, (-818, 32), 15, 0.95, "stop"),
        (1.12, (-460, -182), 2, 1.07),
        (1.35, (-172, -242), -5, 1.14, "lin"),
        (1.62, (148, -148), -5, 1.09),
        (1.84, (178, -114), 0, 1.05, "stop"),
        (2.02, (152, -108), 2, 1.04, "drag"),
        (2.48, (-204, 34), 5, 1.00),
        (2.90, (-424, -38), 0, 1.00, "stop"),
        (3.12, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (0.36, "fist"), (2.86, "open")])
    beats = [
        (0.22, {"b1y": -12, "b2t": -3.0, "s1x": -5, "s1o": -3, "hx": -6, "f1": -5, "f2": -7}),
        (0.76, {"b1x": -20, "b1y": 9, "b1r": 4.0, "b2r": 1.5, "b2t": 1.8, "s1x": 11, "s1o": 10, "s2x": -4,
                "hy": 5, "hr": -3.0, "hx": 4, "f1": 0, "f2": -8}),
        (0.90, {"b1x": -23, "b1y": 11, "b1r": 4.5, "b2r": 1.8, "b2t": 2.2, "s1x": 13, "s1o": 11, "s2x": -5,
                "hy": 6, "hr": -3.4, "hx": 5, "f2": -8}),
        (1.35, {"b1x": 15, "b1y": (-18, "hit3"), "b1r": -3.4, "b2r": -1.4, "b2t": -4.5, "s1x": -11, "s1o": -13,
                "s2x": 6, "s2o": 3, "hy": -4, "hr": 3.2, "hx": -14}),
        (1.66, {"b1x": 26, "b1y": (-18, "stop"), "b1r": -5.0, "b2r": -2.0, "b2t": -4.2, "s1x": -10, "s1o": -15,
                "s2x": 8, "s2o": 3.5, "hy": -7, "hr": 4.2, "hx": -13}),
        (1.98, {"b1x": 18, "b1y": -11, "b1r": -3.5, "b2r": -1.3, "b2t": -2.2, "hy": -5, "hr": 2.8, "hx": -8}),
        (2.64, {"b1x": 3, "b1y": -3, "b1r": -0.6, "b2r": -0.2, "b2t": -0.6, "s1x": -1.5, "s1o": -1.5, "s2x": 0.5, "s2o": 0,
                "hy": -0.8, "hr": 0.5, "hx": -2, "f2": -1}),
        (3.18, {k: (0, "stop") for k in ("b1x", "b1y", "b1r", "b2r", "b2t", "s1x", "s1o", "s2x", "s2o", "hy", "hr", "hx", "f1", "f2")}),
    ]
    return Clip("front_sweep", d, channels(d, beats), {1: sweeper, 2: planted(2)},
                impacts=[(1.35, 0.72)], exact=[1.35, 3.40], source="front_sweep")


def card_snatch() -> Clip:
    """Open stone hand cocked high beside crown, smooth ease-in/out lunge, crush shut on cards, haul prize back."""
    d = 3.2
    thief = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.14, (-434, -50), -3, 1.00, "stop"),
        (0.52, (-298, 204), -5, 1.02),
        (0.90, (-212, 356), -5, 1.05, "stop"),
        (1.00, (-208, 362), -5, 1.06, "stop"),
        # Smooth ease-in/ease-out wrist lunge required by CaveGodCardSnatchRegression (delta v <= 3 px/frame).
        (1.36, (-80, -114), 16, 1.14, "stop"),
        (1.60, (-66, -122), 22, 1.14, "stop"),
        (1.68, (-70, -116), 20, 1.13, "stop"),
        (2.02, (-208, 76), 6, 1.04),
        (2.32, (-214, 198), 2, 0.98, "stop"),
        (2.54, (-228, 186), 2, 0.98),
        (2.96, (-425, -30), 0, 1.00),
        (3.08, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (1.60, "fist")])
    beats = [
        (0.14, {"b1y": -11, "b1x": 5, "b2t": -2.8, "s1x": -5, "s1o": -3, "hx": -5, "f2": -6}),
        (0.55, {"b1y": 11, "b1x": 14, "b1r": -2.8, "b2t": 1.6, "s1x": 11, "s1o": 8, "hx": 3, "hr": -2.0, "hy": -4, "f2": -7}),
        (1.00, {"b1y": 20, "b1x": 20, "b1r": -4.4, "b2t": 3.0, "s1x": 16, "s1o": 10, "s2x": -5,
                "hx": 7, "hr": -3.2, "hy": -6, "hsx": 0.99, "horn": 1.04, "f2": -8}),
        (1.36, {"b1y": (-25, "hit3"), "b1x": (-20, "hit3"), "b1r": (5.4, "hit3"), "b2t": (-5.2, "hit3"),
                "s1x": (-14, "hit3"), "s1o": (-15, "hit3"), "s2x": 7, "hx": (-20, "hit3"), "hr": 4.2, "hy": 6,
                "hsx": 1.0, "horn": 1.0}),
        (1.48, {"b1y": (-25, "stop"), "b1x": (-20, "stop"), "b1r": (5.4, "stop"), "b2t": (-5.2, "stop"),
                "hx": (-20, "stop"), "hr": (4.2, "stop"), "s1x": (-14, "stop"), "s1o": (-15, "stop")}),
        (1.60, {"b1y": (-22, "hit"), "b1x": -18, "b1r": 5.0, "b2t": -4.5, "hx": -18, "hr": 3.8, "s1x": -11, "s1o": -16}),
        (1.72, {"b1y": -16, "b1x": -14, "b1r": 4.0, "b2t": -3.0, "hx": -13}),
        (2.32, {"b1y": 10, "b1x": 5, "b1r": 0.6, "b2t": 1.6, "s1x": 8, "s1o": -3, "s2x": 2,
                "hx": -11, "hr": 4.0, "hy": 6, "f2": -3}),
        (2.56, {"b1y": 7, "b2t": 1.0, "hr": 3.2, "hx": -9, "hy": 5}),
        (3.06, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "b1x", "b1r", "b2t", "s1x", "s1o", "s2x", "hx", "hr", "hy", "hsx", "horn", "f2")}),
    ]
    return Clip("card_snatch", d, channels(d, beats), {1: thief, 2: planted(2)},
                impacts=[(1.36, 0.55), (1.60, 0.30)], exact=[1.00, 1.36, 1.60, 1.68, 3.08], source="card_snatch")


def grab_player() -> Clip:
    d = 5.0
    (hx_, hy_), hb = HOLD_L_ARM
    grabber = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.20, (-434, -50), -3, 1.00, "stop"),
        (0.48, (-296, 206), -5, 1.02),
        (0.68, (-224, 316), -5, 1.05, "stop"),
        (0.78, (-218, 328), -5, 1.06, "stop"),
        (1.10, (-62, -142), 18, 1.15, "slam"),
        (1.30, (-62, -142), 18, 1.15, "stop"),
        (1.56, (-188, 52), 22, 1.06, "drag"),
        (1.90, (hx_, hy_), hb, 1.00, "stop"),
        (2.45, (hx_, hy_), hb, 1.00, "stop"),
        (3.00, (hx_, hy_), hb, 1.00, "stop"),
        (3.30, (-190, 68), 16, 1.06),
        (3.62, (-62, -142), 16, 1.13, "hit3"),
        (3.80, (-62, -142), 16, 1.13, "stop"),
        (4.32, (-388, 24), 4, 1.01, "drag"),
        (4.66, (-424, -38), 0, 1.00, "stop"),
        (4.84, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "open"), (1.08, "fist"), (3.65, "open")])
    hold_keys = ("b1y", "b1x", "b1r", "b2t", "s1x", "s1o", "s2x", "hx", "hr", "hy", "hsx", "horn", "f2")
    hold_pose = {k: (HOLD_L.get(k, 1.0 if k in MULTIPLY else 0.0), "stop") for k in hold_keys}
    beats = [
        (0.20, {"b1y": -12, "b2t": -3.0, "s1x": -5, "s1o": -3, "hx": -6, "f2": -6}),
        (0.74, {"b1y": 19, "b1x": 14, "b1r": -3.0, "b2t": 2.8, "s1x": 14, "s1o": 8, "s2x": -4,
                "hx": 7, "hr": -2.2, "hy": -3, "hsx": 0.99, "horn": 1.04, "f2": -8}),
        (1.10, {"b1y": (-28, "slam"), "b1x": (-18, "slam"), "b1r": (5.2, "slam"), "b2t": (-6.0, "slam"),
                "s1x": (-14, "slam"), "s1o": (-13, "slam"), "s2x": 7, "hx": (-22, "hit3"), "hr": (4.6, "hit3"), "hy": 6,
                "hsx": 1.0, "horn": 1.0}),
        (1.28, {"b1y": (-28, "stop"), "b1x": (-18, "stop"), "b1r": (5.2, "stop"), "b2t": (-6.0, "stop"),
                "hx": (-22, "stop"), "s1x": (-14, "stop"), "s1o": (-13, "stop")}),
        (1.90, dict(hold_pose)),
        (2.45, dict(hold_pose)),
        (3.00, dict(hold_pose)),
        (3.62, {"b1y": (-18, "hit"), "b1x": -14, "b1r": 4.0, "b2t": -3.5, "hx": -15, "hr": 3.0, "hy": 5,
                "s1x": -8, "s1o": -8, "s2x": 4, "hsx": 1.0, "f2": -4}),
        (3.78, {"b1y": (-18, "stop"), "b1x": (-14, "stop"), "b1r": (4.0, "stop"), "b2t": (-3.5, "stop"),
                "hx": (-15, "stop"), "s1x": (-8, "stop"), "s1o": (-8, "stop")}),
        (4.38, {"b1y": -4, "b1x": -2, "b1r": 0.6, "b2t": -0.8, "hx": -3, "hr": 0.5, "hy": 0.8, "s1x": -1.5, "s1o": -1.5, "s2x": 0.5, "f2": -1}),
        (4.84, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in hold_keys}),
    ]

    def grab(t, palm):
        return held(t, palm, smoother(seg(t, 1.30, 1.72)) * (1 - smoother(seg(t, 3.24, 3.62))))

    return Clip("grab_player", d, channels(d, beats), {1: grabber, 2: planted(2)}, grab=grab,
                gate=lambda t: 1 - window(t, 1.70, 1.88, 3.02, 3.22), impacts=[(1.10, 0.78), (3.62, 0.45)],
                exact=[1.10, 1.90, 2.20, 2.60, 3.00, 3.65, 4.95], source="grab_player")


def grab_slam() -> Clip:
    d = 1.7
    (hx_, hy_), hb = HOLD_L_ARM
    smasher = arm([
        (0.00, (hx_, hy_), hb, 1.00, "stop"),
        (0.40, (-210, 356), -4, 0.95),
        (0.52, (-202, 372), -5, 0.94, "stop"),
        (0.75, (-62, -150), 24, 1.16, "slam"),
        (0.90, (-62, -150), 24, 1.16, "stop"),
        (1.06, (-104, -120), 16, 1.10, "drag"),
        (1.34, (-372, 12), 4, 1.01),
        (1.48, (-424, -38), 0, 1.00, "stop"),
        (1.58, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "fist"), (0.95, "open")])
    hold = dict(HOLD_L)
    beats = [
        (0.00, {k: (v, "stop") for k, v in hold.items()}),
        (0.40, {"b1y": 30, "b1x": 4, "b1r": -3.8, "b2t": 4.0, "s1x": 18, "s1o": 8, "hx": 11, "hr": -2.2, "hy": 3.5,
                "hsx": 0.99, "horn": 1.04, "f2": -6}),
        (0.52, {"b1y": 33, "b1x": 5, "b1r": -4.2, "b2t": 4.6, "s1x": 20, "s1o": 9, "hx": 13, "hsx": 0.985, "horn": 1.05}),
        (0.75, {"b1y": (-44, "slam"), "b1x": (-17, "slam"), "b1r": (6.2, "slam"), "b2t": (-8.5, "slam"),
                "s1x": (-18, "slam"), "s1o": (-15, "slam"), "hx": (-27, "hit3"), "hr": (5.0, "hit3"), "hy": 6,
                "hsx": 1.0, "horn": 1.0, "f2": -7}),
        (0.88, {"b1y": (-44, "stop"), "b1x": (-17, "stop"), "b1r": (6.2, "stop"), "b2t": (-8.5, "stop"),
                "hx": (-27, "stop"), "hsx": (1.0, "stop"), "s1x": (-18, "stop"), "s1o": (-15, "stop")}),
        (1.16, {"b1y": -22, "b1x": -9, "b1r": 3.4, "b2t": -4.2, "hx": -14, "hsx": 1.0, "s1x": -8, "s1o": -7, "hr": 2.4, "hy": 2.5}),
        (1.42, {"b1y": -4, "b1x": -1.5, "b1r": 0.6, "b2t": -0.8, "hx": -2.5, "s1x": -1.5, "s1o": -1, "hr": 0.4, "hy": 0.4, "f2": -1}),
        (1.58, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "b1x", "b1r", "b2t", "s1x", "s1o", "hx", "hr", "hy", "hsx", "horn", "f2")}),
    ]
    return Clip("grab_slam", d, channels(d, beats, start=hold), {1: smasher, 2: planted(2)},
                grab=lambda t, palm: held(t, palm, 1 - smoother(seg(t, 0.54, 0.75))),
                impacts=[(0.75, 1.45)], exact=[0.75], source="grab_slam")


# ---------------------------------------------------------------------------- exposed core (left arm broken)

def weak_enter() -> Clip:
    """Catastrophic structural fracture & heavy monolithic collapse onto the basalt platform."""
    d = 2.2
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    broken = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.12, (-448, 22), -4, 1.00),
        (0.34, (-380, -14), 4, 1.00, "stop"),
        (0.62, (-214, -64), b1, 1.00),
        (0.85, (w1[0], w1[1] - 6), b1 + 2, 1.00, "slam"),
        (0.98, (w1[0], w1[1] - 6), b1 + 2, 1.00, "stop"),
        (1.28, (w1[0] - 3, w1[1] - 1), b1 + 1, 1.00, "drag"),
        (1.74, w1, b1, 1.00, "stop"),
    ], [(0, "open"), (0.12, "fist")])
    brace = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.36, (434, -28), 0, 1.00),
        (0.85, (394, -48), 3, 1.00, "slam"),
        (1.00, (394, -48), 3, 1.00, "stop"),
        (1.60, w2, b2, 1.00, "stop"),
    ], [(0, "open")])
    beats = [
        (0.14, {"b1y": 11, "b1x": -8, "b1r": 2.6, "hx": 12, "hr": -5, "hsx": 0.99, "horn": 1.04, "s1x": 10}),
        (0.36, {"b1y": -28, "b1x": -3, "b1r": 3.4, "hx": -2, "hr": -6.5, "hsx": 1.0, "horn": 1.0, "s1x": -10, "s1o": -5}),
        (0.85, {"b1y": (-136, "slam"), "b1x": (11, "slam"), "b1r": (-8.6, "slam"), "b2r": 4.6, "b3r": 3.6, "b2t": -7.5,
                "hx": (-33, "hit3"), "hr": (-18, "hit3"), "hy": -7, "s1x": (-23, "slam"), "s1o": -10,
                "s2x": 14, "s2o": 3.5, "f1": -10}),
        (0.98, {"b1y": (-136, "stop"), "b2t": (-7.5, "stop"), "hx": (-33, "stop"), "hr": (-18, "stop")}),
        (1.36, {"b1y": -128, "hx": -28, "hr": -15.0, "b2t": -5.5}),
    ]
    return Clip("weak_enter", d, channels(d, beats, end=WEAK_L), {1: broken, 2: brace},
                impacts=[(0.85, 1.25)], exact=[0.85], source="weak_enter")


def weak_attack() -> Clip:
    """Strained labored heave from prone, cocking the intact stone arm beside the face and slamming down."""
    d = 2.6
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    striker = arm([
        (0.00, w2, b2, 1.00, "stop"),
        (0.22, (408, -48), -3, 1.00, "stop"),
        (0.66, (256, 218), -5, 0.96, "stop"),
        (0.94, (246, 232), -5, 0.98, "stop"),
        (1.20, (131, -148), 22, 1.15, "slam"),
        (1.38, (131, -148), 22, 1.15, "stop"),
        (1.76, (296, -8), 6, 1.04, "drag"),
        (2.18, (394, -44), 0, 1.00, "stop"),
        (2.38, w2, b2, 1.00, "stop"),
    ], [(0, "open"), (0.20, "fist"), (2.16, "open")])
    lying = arm([
        (0.00, w1, b1, 1.00, "stop"),
        (0.22, (w1[0] + 3, w1[1] - 2), b1 + 1, 1.00),
        (0.70, (w1[0] - 8, w1[1] + 3), b1 + 2, 1.00),
        (1.20, (w1[0] + 6, w1[1] - 3), b1 - 2, 1.00, "hit"),
        (1.40, (w1[0] + 6, w1[1] - 3), b1 - 2, 1.00, "stop"),
        (2.04, w1, b1, 1.00, "stop"),
    ], [(0, "fist")])
    w = WEAK_L
    beats = [
        (0.22, {"b1y": w["b1y"] - 8, "b2t": w["b2t"] - 2.0, "hx": w["hx"] - 4, "s2x": w["s2x"] - 3}),
        (0.68, {"b1y": w["b1y"] + 22, "b1x": w["b1x"] - 15, "b1r": w["b1r"] + 5.0, "s2x": w["s2x"] + 13, "s2o": w["s2o"] + 9,
                "hx": w["hx"] + 12, "hr": w["hr"] + 5, "hy": w["hy"] + 8, "hsx": 0.99}),
        (0.94, {"b1y": w["b1y"] + 25, "b1x": w["b1x"] - 17, "b1r": w["b1r"] + 5.4, "s2x": w["s2x"] + 15, "s2o": w["s2o"] + 10,
                "hx": w["hx"] + 14, "hsx": 0.99}),
        (1.20, {"b1y": (w["b1y"] - 20, "slam"), "b1x": (w["b1x"] + 8, "slam"), "b1r": (w["b1r"] - 4.0, "slam"),
                "b2t": (w["b2t"] - 3.8, "slam"), "s2x": (w["s2x"] - 15, "slam"), "s2o": (w["s2o"] - 10, "slam"),
                "hx": (w["hx"] - 14, "hit3"), "hr": w["hr"] - 6, "hy": w["hy"] - 4, "hsx": 1.0}),
        (1.38, {"b1y": (w["b1y"] - 20, "stop"), "b1x": (w["b1x"] + 8, "stop"), "b1r": (w["b1r"] - 4.0, "stop"),
                "b2t": (w["b2t"] - 3.8, "stop"), "hx": (w["hx"] - 14, "stop"), "s2x": (w["s2x"] - 15, "stop")}),
        (1.92, {"b1y": w["b1y"] - 4, "b1x": w["b1x"] + 1.5, "b1r": w["b1r"] - 0.8, "b2t": w["b2t"] - 0.7,
                "s2x": w["s2x"] - 2.5, "s2o": w["s2o"], "hx": w["hx"] - 2.5, "hr": w["hr"] - 1, "hy": w["hy"] - 0.8}),
        (2.38, {k: (w[k], "stop") for k in w}),
    ]
    return Clip("weak_attack", d, channels(d, beats, start=WEAK_L, end=WEAK_L), {1: lying, 2: striker},
                impacts=[(1.20, 1.00)], exact=[1.20], source="weak_attack")


def weak_recover() -> Clip:
    """Grinding two-arm push off the ledge, colossal labored heave upright, heavy stone palm stomp at 1.60s."""
    d = 2.6
    (w1, b1), (w2, b2) = WEAK_L_ARMS[1], WEAK_L_ARMS[2]
    healed = arm([
        (0.00, w1, b1, 1.00, "stop"),
        (0.20, (w1[0] - 8, w1[1] - 4), b1 + 3, 1.00, "stop"),
        (0.48, (w1[0] - 24, w1[1] + 10), b1, 1.00, "drag"),
        (0.92, (-322, 46), 0, 1.00),
        (1.22, (-356, 98), -4, 0.98, "stop"),
        (1.60, (-424, -40), 2, 1.02, "slam"),
        (1.74, (-424, -40), 2, 1.02, "stop"),
        (2.12, REST_WRIST[1], 0, 1.00, "stop"),
    ], [(0, "fist"), (1.56, "open")])
    support = arm([
        (0.00, w2, b2, 1.00, "stop"),
        (0.48, (396, -54), 4, 1.00, "stop"),
        (1.04, (434, -18), 0, 1.00),
        (1.52, REST_WRIST[2], 0, 1.00, "stop"),
    ], [(0, "open")])
    beats = [
        (0.20, {"b1y": -132, "b1x": 10, "b1r": -8.0, "b2t": -6.0, "hx": -29, "s2x": 15, "f1": -12}),
        (0.48, {"b1y": -90, "b1x": 4, "b1r": -4.8, "hx": -9, "hr": -7.5, "hy": -3, "s2x": 20, "s2o": 4,
                "s1x": -11, "s1o": -5, "f1": -11, "b2t": -2.0}),
        (1.22, {"b1y": 24, "b1x": 0, "b1r": 1.0, "b2r": 0, "b3r": 0, "b2t": 3.5, "hx": 13, "hr": 1.0, "hy": 0,
                "hsx": 0.99, "horn": 1.05, "s1x": 14, "s1o": 8, "s2x": 14, "s2o": 8, "f1": 0, "bsx": 1.03}),
        (1.60, {"b1y": (-18, "slam"), "b2t": (-4.0, "slam"), "hx": (-10, "hit3"), "hsx": 1.0, "horn": 1.0,
                "s1x": (-7, "slam"), "s1o": -3, "s2x": (-6, "slam"), "s2o": -2, "bsx": 1.0}),
        (1.74, {"b1y": (-18, "stop"), "b2t": (-4.0, "stop"), "hx": (-10, "stop"), "s1x": (-7, "stop"), "s2x": (-6, "stop")}),
        (2.14, {"b1y": -4, "b2t": -0.9, "hx": -2.5, "s1x": -1.5, "s2x": -1.5}),
        (2.44, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "b1x", "b1r", "b2r", "b3r", "b2t", "hx", "hr", "hy", "hsx", "horn", "s1x", "s1o", "s2x", "s2o", "f1", "bsx")}),
    ]
    return Clip("weak_recover", d, channels(d, beats, start=WEAK_L), {1: healed, 2: support},
                impacts=[(1.60, 0.68)], exact=[1.60], source="weak_recover")


# ---------------------------------------------------------------------------- phase change and death

def main_angry() -> Clip:
    """Pressurized magma buildup (0-4.60), ignition jolt (4.60), skyward roar (5.33), clasped double-boulder smash (7.00)."""
    d = 11.2
    path = [
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (3.00, (-428, -42), -2, 1.00),
        (4.60, (-432, -48), -3, 1.00, "stop"),
        (4.76, (-444, 48), 0, 1.00),
        (5.04, (-326, 98), -3, 0.98),
        (5.33, (-228, 136), -5, 0.96, "stop"),
        (6.12, (-234, 146), -5, 0.96, "stop"),
        (6.40, (-192, 326), -5, 0.95),
        (6.56, (-166, 372), -5, 0.94, "stop"),
        (6.66, (-158, 384), -5, 0.94, "stop"),
        (7.00, (-196, -124), 28, 1.16, "slam"),
        (7.24, (-196, -124), 28, 1.16, "stop"),
        (7.76, (-238, -88), 16, 1.09, "drag"),
        (8.42, (-424, -39), 2, 1.00, "stop"),
        (8.78, REST_WRIST[1], 0, 1.00, "stop"),
    ]
    left = arm(path, [(0, "open"), (6.35, "fist"), (8.36, "open")])
    right = arm(mirror_path(shift_beats(path, after=7.50, by=0.16)), [(0, "open"), (6.35, "fist"), (8.52, "open")])
    beats = [
        (1.40, {"b1y": 8, "s1x": 5, "s2x": 5, "hx": -5, "hr": 0.8, "b2t": 1.5}),
        (3.00, {"b1y": -22, "s1x": 11, "s1o": -6, "s2x": 11, "s2o": -6, "hx": -18, "f1": -9, "f2": -9, "b2t": -4.5, "hr": -1.0}),
        (4.45, {"b1y": -40, "s1x": 15, "s1o": -10, "s2x": 15, "s2o": -10, "hx": -26, "f1": -12, "f2": -12, "b2t": -7.5, "hr": -1.8}),
        (4.60, {"b1y": (-42, "stop"), "hx": (-27, "stop"), "b2t": (-8.0, "stop")}),
        (4.72, {"b1y": (-12, "hit3"), "hx": (5, "hit3"), "hr": 2.8, "hsx": 0.99, "horn": 1.06, "b2t": 1.0}),
        (5.33, {"b1y": 44, "s1x": 26, "s1o": 14, "s2x": 26, "s2o": 14, "hx": 26, "hr": 0, "hsx": 0.97, "bsx": 1.06,
                "horn": 1.20, "b2t": 6.0, "b3t": 3.0, "f1": 0, "f2": 0}),
        (6.12, {"b1y": 48, "hx": 28, "hsx": 0.97, "horn": 1.22, "bsx": 1.07, "s1x": 27, "s2x": 27, "b2t": 6.5, "b3t": 3.5}),
        (6.66, {"b1y": 42, "hx": 22, "hsx": 0.98, "horn": 1.18, "bsx": 1.05, "s1x": 24, "s1o": 12, "s2x": 24, "s2o": 12,
                "b2t": 5.2, "b3t": 2.2}),
        (7.00, {"b1y": (-50, "slam"), "s1x": (-20, "slam"), "s1o": (-12, "slam"), "s2x": (-20, "slam"), "s2o": (-12, "slam"),
                "hx": (-30, "hit3"), "hsx": 1.0, "horn": 1.06, "bsx": 1.0, "b2t": (-10.0, "slam"), "b3t": -2.5}),
        (7.24, {"b1y": (-50, "stop"), "hx": (-30, "stop"), "hsx": (1.0, "stop"), "b2t": (-10.0, "stop"),
                "b3t": (-2.5, "stop"), "s1x": (-20, "stop"), "s2x": (-20, "stop")}),
        (7.80, {"b1y": -28, "hx": -18, "hsx": 1.0, "b2t": -4.5, "b3t": -1.0, "s1x": -8, "s1o": -6, "s2x": -8, "s2o": -6}),
        (8.60, {"b1y": -10, "hx": -7, "s1x": -2, "s1o": -2, "s2x": -2, "s2o": -2, "b2t": -1.8, "b3t": -0.4}),
        (9.60, {"b1y": -3, "hx": -2, "s1x": 0, "s2x": 0, "b2t": -0.5, "b3t": 0.0}),
        (10.80, {k: (1.0 if k in MULTIPLY else 0.0, "stop") for k in ("b1y", "hx", "hsx", "s1x", "s2x", "s1o", "s2o", "b2t", "b3t", "horn", "bsx", "hr", "f1", "f2")}),
    ]
    ch = channels(d, beats)
    shaken = dict(ch)

    def env(t):
        build = (seg(t, 1.0, 4.55) ** 1.6) * 0.85
        roar = window(t, 5.22, 5.42, 6.32, 6.62) * 2.4
        return build * (1 - smooth(seg(t, 4.56, 4.72))) + roar

    for name, amp, seed in (("b3r", 0.50, 1), ("hr", 0.65, 2), ("hx", 1.8, 3), ("b1x", 1.4, 4), ("s1x", 1.1, 5), ("s2x", 1.1, 6)):
        curve = ch.get(name, const(0.0))
        shaken[name] = (lambda c, a, s: lambda t: c(t) + env(t) * tremor(t, 11.2, a, s))(curve, amp, seed)
    return Clip("main_angry", d, shaken, {1: left, 2: right}, impacts=[(4.60, 0.70), (7.00, 1.65)], exact=[4.6, 5.33, 7.0],
                posture=lambda t: smoother(seg(t, 7.4, 10.6)), source="main_angry")


def hide() -> Clip:
    """Death: heavy staggering recoil clutching fractured core, crashing onto cliff edge, scraping & slipping into the abyss."""
    d = 7.2
    left = arm([
        (0.00, REST_WRIST[1], 0, 1.00, "stop"),
        (0.22, (-356, 68), 0, 0.98),
        (0.55, (-238, 122), -5, 0.95, "stop"),
        (0.76, (-248, 112), -5, 0.95, "stop"),
        (1.22, (-524, 46), 6, 0.98),
        (1.67, (-472, -62), 18, 1.00, "slam"),
        (2.25, (-472, -62), 18, 1.00, "stop"),
        (2.70, (-473, -65), 17, 1.00, "stop"),
        (3.08, (-474, -76), 14, 1.00, "stop"),
        (3.17, (-474, -84), 12, 1.00, "stop"),
        (3.45, (-460, -340), 8, 1.00),
        (3.88, (-442, -740), 5, 1.00, "hit"),
        (7.20, (-440, -1100), 5, 1.00, "stop"),
    ], [(0, "open"), (1.62, "fist"), (3.19, "open")])
    right = arm([
        (0.00, REST_WRIST[2], 0, 1.00, "stop"),
        (0.24, (356, 68), 0, 0.98),
        (0.58, (238, 122), -5, 0.95, "stop"),
        (0.78, (248, 112), -5, 0.95, "stop"),
        (1.22, (524, 46), 6, 0.98),
        (1.67, (472, -62), 18, 1.00, "slam"),
        (3.40, (472, -62), 18, 1.00, "stop"),
        (4.25, (472, -65), 17, 1.00, "stop"),
        (4.58, (474, -78), 14, 1.00, "stop"),
        (4.67, (474, -86), 12, 1.00, "stop"),
        (4.95, (460, -380), 8, 1.00),
        (5.42, (442, -780), 5, 1.00, "hit"),
        (7.20, (440, -1120), 5, 1.00, "stop"),
    ], [(0, "open"), (1.62, "fist"), (4.69, "open")])
    beats = [
        (0.20, {"b1y": 18, "hx": 16, "hsx": 0.98, "horn": 1.08, "s1x": 14, "s2x": 14, "bsx": 1.04}),
        (0.55, {"b1y": 25, "hx": 21, "hsx": 0.97, "horn": 1.12, "s1x": 18, "s2x": 18, "bsx": 1.06}),
        (0.78, {"b1y": 20, "hx": 16, "hsx": 0.98, "horn": 1.10, "s1x": 15, "s2x": 15}),
        (1.22, {"b1y": -24, "hx": -16, "hsx": 1.0, "horn": 1.02, "s1x": 2, "s2x": 2, "bsx": 1.0}),
        (1.67, {"b1y": (-88, "slam"), "hx": (-34, "hit3"), "b2t": -7.0, "s1x": -14, "s2x": -14, "horn": 1.0}),
        (2.20, {"b1y": -182, "b1r": 2.4, "b2t": -4.0, "hx": -24}),
        (2.46, {"b1y": -132, "b1r": -1.2, "b2t": -1.0, "hx": -10}),
        (2.74, {"b1y": -206, "b1r": -3.2, "b2t": -5.0, "hx": -26}),
        (2.98, {"b1y": -168, "b1r": 0.6, "hx": -14}),
        (3.17, {"b1y": (-268, "slam"), "b1r": 8.2, "b1x": -32, "hx": -30}),
        (3.86, {"b1y": -445, "b1r": 11.5, "b1x": -46, "hx": -18}),
        (4.32, {"b1y": -486, "b1r": 9.0, "b1x": -40, "hx": -14}),
        (4.67, {"b1y": (-586, "slam"), "b1r": -4.8, "b1x": 0, "hx": -24}),
        (5.42, {"b1y": -790, "b1r": -2.4, "hx": -12}),
        (6.35, {"b1y": -895, "b1r": 0.0}),
        (6.57, {"b1y": (-965, "slam"), "b1r": 1.8}),
        (7.20, {"b1y": (-1180, "stop"), "b1r": (0, "stop"), "b1x": (0, "stop"), "b2t": (0, "stop"), "hx": (-10, "stop")}),
    ]
    return Clip("hide", d, channels(d, beats), {1: left, 2: right},
                impacts=[(0.55, 0.55), (1.67, 1.35), (3.17, 0.85), (4.67, 0.85), (6.57, 0.55)],
                exact=[1.67, 3.17, 4.67, 6.57], source="hide")


# ---------------------------------------------------------------------------- variants

ATTACK_BUILDERS = (central_slam, double_fist_crush)
SIDED_BUILDERS = (alternating_jabs, front_sweep, card_snatch, grab_player, grab_slam, weak_enter, weak_attack, weak_recover)
PUNCH_BUILDER = leftpunch
