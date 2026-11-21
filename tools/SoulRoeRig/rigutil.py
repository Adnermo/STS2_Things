"""rigutil — math, curve, and pose helpers for SoulRoeRig (zero external rig state)."""
from __future__ import annotations

import math
from functools import lru_cache

TAU = 2.0 * math.pi


def clamp(x: float, a: float = 0.0, b: float = 1.0) -> float:
    return max(a, min(b, x))


def ease(x: float) -> float:
    x = clamp(x)
    return x * x * (3.0 - 2.0 * x)


def ease_out(x: float) -> float:
    x = clamp(x)
    return 1.0 - (1.0 - x) ** 3


def ease_in(x: float) -> float:
    x = clamp(x)
    return x ** 3


def ease_in_quad(x: float) -> float:
    x = clamp(x)
    return x * x


def seg(t: float, t0: float, t1: float) -> float:
    return clamp((t - t0) / (t1 - t0)) if t1 > t0 else float(t >= t0)


def bump(t: float, t0: float, t1: float, t2: float) -> float:
    return ease(seg(t, t0, t1)) * (1.0 - ease(seg(t, t1, t2)))


def hold(t: float, t0: float, t1: float, t2: float, t3: float) -> float:
    return ease(seg(t, t0, t1)) * (1.0 - ease(seg(t, t2, t3)))


def plateau(t: float, t0: float, t1: float, t2: float, t3: float, p: float = 2.6) -> float:
    if t <= t0 or t >= t3:
        return 0.0
    if t < t1:
        return clamp(seg(t, t0, t1)) ** p
    if t <= t2:
        return 1.0
    return 1.0 - ease(seg(t, t2, t3))


def kick(t: float, t0: float, freq: float = 4.0, damp: float = 6.0) -> float:
    if t < t0:
        return 0.0
    x = t - t0
    return math.exp(-damp * x) * math.sin(TAU * freq * x)


@lru_cache(maxsize=None)
def _curve(points: tuple[tuple[float, float], ...]) -> list[float]:
    slopes = [0.0] * len(points)
    for i in range(1, len(points) - 1):
        h0 = points[i][0] - points[i - 1][0]
        h1 = points[i + 1][0] - points[i][0]
        a = (points[i][1] - points[i - 1][1]) / h0
        b = (points[i + 1][1] - points[i][1]) / h1
        if a * b > 0:
            w0, w1 = 2.0 * h1 + h0, h1 + 2.0 * h0
            slopes[i] = (w0 + w1) / (w0 / a + w1 / b)
    return slopes


def key(t: float, *points: tuple[float, float]) -> float:
    """Shape-preserving monotone Hermite curve."""
    if t <= points[0][0]:
        return points[0][1]
    slopes = _curve(points)
    for i in range(len(points) - 1):
        if t <= points[i + 1][0]:
            h = points[i + 1][0] - points[i][0]
            u = (t - points[i][0]) / h
            return (
                (2 * u**3 - 3 * u * u + 1) * points[i][1]
                + (u**3 - 2 * u * u + u) * h * slopes[i]
                + (-2 * u**3 + 3 * u * u) * points[i + 1][1]
                + (u**3 - u * u) * h * slopes[i + 1]
            )
    return points[-1][1]


class Pose(dict):
    """Bone pose map: bone_name -> (r_deg, dx, dy, sx, sy). Strictly sx == sy == 1.0."""

    def add(self, b: str, r: float = 0.0, x: float = 0.0, y: float = 0.0) -> None:
        rr, xx, yy, sx, sy = self.get(b, (0.0, 0.0, 0.0, 1.0, 1.0))
        self[b] = (rr + r, xx + x, yy + y, sx, sy)


class Frame:
    def __init__(self) -> None:
        self.P = Pose()
        self.tint = (1.0, 1.0, 1.0, 1.0)
        self.slot_rgba: dict[str, tuple[float, float, float, float]] = {}
        self.slot_alpha: dict[str, float] = {}
        self.draw_order: list[str] | None = None
