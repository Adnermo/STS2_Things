"""Curves and light dynamics for authoring the 活体巨岩 motions."""
from __future__ import annotations

import math
from typing import Callable, Sequence


def clamp(x: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return lo if x < lo else hi if x > hi else x


def seg(t: float, a: float, b: float) -> float:
    return clamp((t - a) / (b - a)) if b > a else float(t >= a)


def smooth(x: float) -> float:
    x = clamp(x)
    return x * x * (3 - 2 * x)


def smoother(x: float) -> float:
    x = clamp(x)
    return x * x * x * (x * (6 * x - 15) + 10)


def window(t: float, a: float, b: float, c: float, d: float) -> float:
    """0 before a, eases to 1 over a..b, holds, eases back to 0 over c..d."""
    return smoother(seg(t, a, b)) * (1 - smoother(seg(t, c, d)))


def _vec(value) -> tuple[float, ...]:
    return tuple(float(v) for v in value) if isinstance(value, (tuple, list)) else (float(value),)


class Curve:
    """Cubic Hermite spline through keys.

    Keys are ``(time, value)`` or ``(time, value, mode)``; ``value`` is a number or a tuple.
    Modes: ``auto`` (smooth Catmull-Rom, limited to avoid overshoot), ``stop`` (zero velocity),
    ``hit``/``hit3`` (accelerate into the key, stop dead after it), ``go`` (leave the key at speed),
    ``lin`` (straight line through the neighbours), or an explicit ``(vin, vout)`` velocity pair.
    Values before the first key and after the last key are held.
    """

    def __init__(self, keys: Sequence):
        rows = sorted(keys, key=lambda k: k[0])
        self.t = [float(k[0]) for k in rows]
        self.p = [_vec(k[1]) for k in rows]
        self.scalar = not isinstance(rows[0][1], (tuple, list))
        n, dim = len(rows), len(self.p[0])
        modes = [k[2] if len(k) > 2 else "auto" for k in rows]
        self.vin: list[tuple[float, ...]] = []
        self.vout: list[tuple[float, ...]] = []
        zero = (0.0,) * dim
        for i in range(n):
            left = [(self.p[i][d] - self.p[i - 1][d]) / (self.t[i] - self.t[i - 1]) for d in range(dim)] if i > 0 else None
            right = [(self.p[i + 1][d] - self.p[i][d]) / (self.t[i + 1] - self.t[i]) for d in range(dim)] if i < n - 1 else None
            mode = modes[i]
            if isinstance(mode, tuple):
                vin, vout = _vec(mode[0]) if mode[0] is not None else zero, _vec(mode[1]) if mode[1] is not None else zero
            elif mode == "stop" or (mode == "auto" and (left is None or right is None)):
                vin = vout = zero
            elif mode in ("hit", "hit3"):
                k = 3.0 if mode == "hit3" else 2.0
                vin = tuple(k * v for v in left) if left else zero
                vout = zero
            elif mode in ("go", "go3"):
                k = 3.0 if mode == "go3" else 2.0
                vin = zero
                vout = tuple(k * v for v in right) if right else zero
            elif mode == "lin":
                span = self.t[min(i + 1, n - 1)] - self.t[max(i - 1, 0)]
                v = tuple((self.p[min(i + 1, n - 1)][d] - self.p[max(i - 1, 0)][d]) / span for d in range(dim))
                vin = vout = v
            else:  # auto with Fritsch-Carlson style limiting per component
                hl, hr = self.t[i] - self.t[i - 1], self.t[i + 1] - self.t[i]
                v = []
                for d in range(dim):
                    a, b = left[d], right[d]
                    if a * b <= 0:
                        v.append(0.0)
                        continue
                    m = (a * hr + b * hl) / (hl + hr)
                    limit = 3 * min(abs(a), abs(b))
                    v.append(math.copysign(min(abs(m), limit), m))
                vin = vout = tuple(v)
            self.vin.append(vin)
            self.vout.append(vout)

    def __call__(self, t: float):
        ts = self.t
        if t <= ts[0]:
            out = self.p[0]
        elif t >= ts[-1]:
            out = self.p[-1]
        else:
            lo, hi = 0, len(ts) - 1
            while hi - lo > 1:
                mid = (lo + hi) // 2
                if ts[mid] <= t:
                    lo = mid
                else:
                    hi = mid
            h = ts[hi] - ts[lo]
            s = (t - ts[lo]) / h
            s2, s3 = s * s, s * s * s
            h00, h10, h01, h11 = 2 * s3 - 3 * s2 + 1, s3 - 2 * s2 + s, -2 * s3 + 3 * s2, s3 - s2
            p0, p1, m0, m1 = self.p[lo], self.p[hi], self.vout[lo], self.vin[hi]
            out = tuple(h00 * p0[d] + h10 * h * m0[d] + h01 * p1[d] + h11 * h * m1[d] for d in range(len(p0)))
        return out[0] if self.scalar else out


def const(value) -> Callable[[float], object]:
    return lambda _t: value


def shake(t: float, t0: float, amp: float, freq: float = 9.0, decay: float = 6.0, phase: float = 0.0) -> float:
    """Damped stone resonance after an impact."""
    if t < t0:
        return 0.0
    u = t - t0
    return amp * math.exp(-decay * u) * math.sin(2 * math.pi * freq * u + phase) * smooth(u / 0.02)


def wave(t: float, period: float, cycles: int = 1, phase: float = 0.0) -> float:
    """Loop-safe sine that is exactly zero at t=0 and periodic over ``period``."""
    return math.sin(2 * math.pi * cycles * t / period + phase) - math.sin(phase)


def breath(t: float, period: float, cycles: int = 1, peak: float = 0.58) -> float:
    """0..1 breathing shape, 0 at t=0: slow inhale up to ``peak`` of the cycle, quicker exhale."""
    phi = (cycles * t / period) % 1.0
    if phi < peak:
        return 0.5 - 0.5 * math.cos(math.pi * phi / peak)
    return 0.5 + 0.5 * math.cos(math.pi * (phi - peak) / (1 - peak))


def tremor(t: float, period: float, amp: float, seed: int = 0) -> float:
    """Loop-safe irregular shiver built from incommensurate integer harmonics."""
    harmonics = ((11, 0.55), (13, 0.35), (17, 0.25), (23, 0.15))
    total = 0.0
    for i, (n, weight) in enumerate(harmonics):
        total += weight * wave(t, period, n + seed % 3, phase=1.3 * (i + 1) + 0.7 * seed)
    return amp * total / 1.3


def follow(signal: Sequence[float], dt: float, freq: float, zeta: float) -> list[float]:
    """Second-order follower (lag + overshoot) of a sampled signal, starting at rest on its first value."""
    w = 2 * math.pi * freq
    y, v = signal[0], 0.0
    out = []
    sub = max(1, int(math.ceil(dt * w / 0.08)))
    h = dt / sub
    for x in signal:
        for _ in range(sub):
            a = w * w * (x - y) - 2 * zeta * w * v
            v += a * h
            y += v * h
        out.append(y)
    return out


def pendulum(root_acc: Sequence[tuple[float, float]], rigid_angle: Sequence[float], dt: float,
             freq: float, zeta: float, length: float, gain: float) -> list[float]:
    """Angular deviation (degrees) of a hanging appendage from its rigidly attached direction."""
    w = 2 * math.pi * freq
    dev, vel = 0.0, 0.0
    prev_rigid = rigid_angle[0]
    out = []
    sub = max(1, int(math.ceil(dt * w / 0.08)))
    h = dt / sub
    for (ax, ay), rigid in zip(root_acc, rigid_angle):
        # The appendage keeps its world direction when the parent turns quickly.
        turn = math.radians(rigid - prev_rigid)
        prev_rigid = rigid
        dev -= turn
        direction = math.radians(rigid) + dev
        # Inertial force perpendicular to the appendage (CCW positive).
        perp = -math.sin(direction) * ax + math.cos(direction) * ay
        for _ in range(sub):
            acc = -w * w * dev - 2 * zeta * w * vel - gain * perp / length
            vel += acc * h
            dev += vel * h
        out.append(math.degrees(dev))
    return out
