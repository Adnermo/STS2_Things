"""Curves and small motion primitives for authoring the Scale Beetle actions."""
from __future__ import annotations

import math
from typing import Sequence


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


def ease_in(x: float) -> float:
    x = clamp(x)
    return x * x * x


def window(t: float, a: float, b: float, c: float, d: float) -> float:
    """0 before a, eases to 1 over a..b, holds, eases back to 0 over c..d."""
    return smoother(seg(t, a, b)) * (1 - smoother(seg(t, c, d)))


class Curve:
    """Cubic Hermite spline through ``(time, value[, mode])`` keys; values outside the keys are held.

    Modes: ``auto`` (limited Catmull-Rom, no overshoot), ``stop`` (zero velocity), ``hit``/``hit3``
    (accelerate into the key, leave it at rest), ``go`` (leave the key at speed) or ``lin``.
    """

    def __init__(self, keys: Sequence, period: float | None = None):
        rows = sorted(keys, key=lambda k: k[0])
        self.period = period
        if period:
            # Periodic: wrap two keys on each side so tangents at the seam are continuous.
            rows = ([(k[0] - period,) + tuple(k[1:]) for k in rows[-2:]] + rows +
                    [(k[0] + period,) + tuple(k[1:]) for k in rows[:2]])
        self.t = [float(k[0]) for k in rows]
        self.p = [float(k[1]) for k in rows]
        modes = [k[2] if len(k) > 2 else 'auto' for k in rows]
        n = len(rows)
        self.vin, self.vout = [], []
        for i in range(n):
            left = (self.p[i] - self.p[i - 1]) / (self.t[i] - self.t[i - 1]) if i > 0 else None
            right = (self.p[i + 1] - self.p[i]) / (self.t[i + 1] - self.t[i]) if i < n - 1 else None
            mode = modes[i]
            if mode == 'stop' or (mode == 'auto' and (left is None or right is None)):
                vin = vout = 0.0
            elif mode in ('hit', 'hit3'):
                vin, vout = (3.0 if mode == 'hit3' else 2.0) * (left or 0.0), 0.0
            elif mode in ('go', 'go3'):
                vin, vout = 0.0, (3.0 if mode == 'go3' else 2.0) * (right or 0.0)
            elif mode == 'lin':
                lo, hi = max(i - 1, 0), min(i + 1, n - 1)
                vin = vout = (self.p[hi] - self.p[lo]) / (self.t[hi] - self.t[lo])
            else:
                if left * right <= 0:
                    vin = vout = 0.0
                else:
                    hl, hr = self.t[i] - self.t[i - 1], self.t[i + 1] - self.t[i]
                    m = (left * hr + right * hl) / (hl + hr)
                    vin = vout = math.copysign(min(abs(m), 3 * min(abs(left), abs(right))), m)
            self.vin.append(vin)
            self.vout.append(vout)

    def __call__(self, t: float) -> float:
        if self.period:
            t %= self.period
        ts = self.t
        if t <= ts[0]:
            return self.p[0]
        if t >= ts[-1]:
            return self.p[-1]
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
        return ((2 * s3 - 3 * s2 + 1) * self.p[lo] + (s3 - 2 * s2 + s) * h * self.vout[lo]
                + (-2 * s3 + 3 * s2) * self.p[hi] + (s3 - s2) * h * self.vin[hi])


ZERO = Curve([(0.0, 0.0)])


def shake(t: float, t0: float, amp: float, freq: float = 10.0, decay: float = 6.0, phase: float = 0.0) -> float:
    """Damped ring of a rigid plate after an impact at ``t0`` (starts from zero displacement)."""
    if t < t0:
        return 0.0
    u = t - t0
    return amp * math.exp(-decay * u) * math.sin(2 * math.pi * freq * u + phase) * smooth(u / 0.025)


def wave(t: float, period: float, cycles: int = 1, phase: float = 0.0) -> float:
    """Loop-safe sine: zero at t=0 and periodic over ``period``."""
    return math.sin(2 * math.pi * cycles * t / period + phase) - math.sin(phase)


def breath(t: float, period: float, cycles: int = 1, peak: float = 0.58) -> float:
    """0..1 periodic heave, 0 at t=0: slow rise to 1 at ``peak`` of each cycle, quicker return."""
    phi = (cycles * t / period) % 1.0
    if phi < peak:
        return 0.5 - 0.5 * math.cos(math.pi * phi / peak)
    return 0.5 + 0.5 * math.cos(math.pi * (phi - peak) / (1 - peak))


def buzz(t: float, freq: float, seed: float = 0.0) -> float:
    """Irregular fast vibration in [-1, 1] (two incommensurate partials)."""
    return 0.68 * math.sin(2 * math.pi * freq * t + seed) + 0.32 * math.sin(2 * math.pi * freq * 1.618 * t + 2.1 * seed + 0.7)
