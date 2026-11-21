#!/usr/bin/env python3
"""Lossless Spine 4.2 `.spjson` animation timeline optimizer & 60 Hz pose verifier.

Optimizes ONLY `animations` timelines (`bones` numeric timelines and `slots` `rgba`
timelines) while keeping `skeleton`, `bones`, `slots`, `ik`, `transform`, `path`,
`physics`, `skins`, `events`, and non-numeric animation sections bit-for-bit intact.

Strict guarantees (verified at 60 Hz across every animation & every bone before write):
  - Max translate error <= 0.05 px (internal simplification target <= 0.03 px)
  - Max rotate error    <= 0.05 deg (internal simplification target <= 0.03 deg)
  - Max scale error     <= 0.002    (internal simplification target <= 0.001)
  - Max shear error     <= 0.05 deg (internal simplification target <= 0.03 deg)
  - Max slot rgba error <= 1.5 / 255 per channel
  - Exact animation names, durations (<= 1e-3 s), events, and setup/skins data.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import pathlib
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from typing import Any

# (fields, defaults, key_tol, verify60_tol, bezier_cy_digits)
BONE_TL_SPEC: dict[str, tuple[tuple[str, ...], tuple[float, ...], float, float, int]] = {
    "translate": (("x", "y"), (0.0, 0.0), 0.03, 0.04, 2),
    "shear": (("x", "y"), (0.0, 0.0), 0.03, 0.04, 2),
    "scale": (("x", "y"), (1.0, 1.0), 0.001, 0.0015, 4),
    "rotate": (("value",), (0.0,), 0.03, 0.04, 2),
    "translatex": (("value",), (0.0,), 0.03, 0.04, 2),
    "translatey": (("value",), (0.0,), 0.03, 0.04, 2),
    "shearx": (("value",), (0.0,), 0.03, 0.04, 2),
    "sheary": (("value",), (0.0,), 0.03, 0.04, 2),
    "scalex": (("value",), (1.0,), 0.001, 0.0015, 4),
    "scaley": (("value",), (1.0,), 0.001, 0.0015, 4),
}

SPLIT_MAP: dict[str, tuple[str, str]] = {
    "translate": ("translatex", "translatey"),
    "scale": ("scalex", "scaley"),
    "shear": ("shearx", "sheary"),
}

# Candidate relative time positions for cubic Bezier handles (all strictly inside (0, 1)).
BEZIER_HANDLE_CANDIDATES: tuple[tuple[float, float], ...] = (
    (1.0 / 3.0, 2.0 / 3.0),
    (0.2, 0.8),
    (0.15, 0.55),
    (0.45, 0.85),
)

VERIFY_LIMITS = {
    "translate": 0.05,
    "rotate": 0.05,
    "scale": 0.002,
    "shear": 0.05,
    "rgba": 1.5,  # in 0..255 channel units
    "duration": 1e-3,
}

RGBA_TOL_255 = 0.9


def clean_num(value: float, ndigits: int = 4) -> int | float:
    """Round float cleanly, normalize -0.0 to 0, and convert exact whole numbers to int."""
    r = round(float(value), ndigits)
    if r == 0.0:
        return 0
    if r == int(r):
        return int(r)
    return r


def is_linear_key(key: dict[str, Any]) -> bool:
    c = key.get("curve")
    return c is None or c == "linear"


def animation_duration(animation: Any) -> float:
    """Exact match for verify_project.py _spine_clip_length / animation_duration."""
    length = 0.0
    stack = [animation]
    while stack:
        node = stack.pop()
        if isinstance(node, dict):
            t = node.get("time")
            if isinstance(t, (int, float)):
                ft = float(t)
                if ft > length:
                    length = ft
            stack.extend(node.values())
        elif isinstance(node, list):
            stack.extend(node)
    return length


def count_bone_keyframes(data: dict[str, Any]) -> int:
    total = 0
    for anim in data.get("animations", {}).values():
        if not isinstance(anim, dict):
            continue
        for btls in anim.get("bones", {}).values():
            if not isinstance(btls, dict):
                continue
            for keys in btls.values():
                if isinstance(keys, list):
                    total += len(keys)
    return total


def count_slot_keyframes(data: dict[str, Any]) -> int:
    total = 0
    for anim in data.get("animations", {}).values():
        if not isinstance(anim, dict):
            continue
        for stls in anim.get("slots", {}).values():
            if not isinstance(stls, dict):
                continue
            for keys in stls.values():
                if isinstance(keys, list):
                    total += len(keys)
    return total


# ---------------------------------------------------------------------------
# Exact Spine 4.2 18-segment cubic Bezier evaluator (CurveTimeline.setBezier)
# ---------------------------------------------------------------------------

def build_spine_bezier_18(
    t0: float,
    v0: float,
    cx1: float,
    cy1: float,
    cx2: float,
    cy2: float,
    t1: float,
    v1: float,
) -> tuple[list[float], list[float]]:
    xs = [t0]
    ys = [v0]
    for k in range(1, 19):
        u = k / 18.0
        om = 1.0 - u
        b0 = om * om * om
        b1 = 3.0 * om * om * u
        b2 = 3.0 * om * u * u
        b3 = u * u * u
        xs.append(b0 * t0 + b1 * cx1 + b2 * cx2 + b3 * t1)
        ys.append(b0 * v0 + b1 * cy1 + b2 * cy2 + b3 * v1)
    return xs, ys


def eval_spine_bezier_18(xs: list[float], ys: list[float], t: float) -> float:
    if t <= xs[0]:
        return ys[0]
    if t >= xs[-1]:
        return ys[-1]
    for k in range(1, 19):
        if xs[k] >= t:
            dx = xs[k] - xs[k - 1]
            if dx <= 1e-12:
                return ys[k]
            return ys[k - 1] + (ys[k] - ys[k - 1]) * (t - xs[k - 1]) / dx
    return ys[-1]


def solve_u_for_t(t: float, t0: float, cx1: float, cx2: float, t1: float) -> float:
    """Invert monotonic cubic x(u) = t using 5 Newton iterations."""
    dt = t1 - t0
    if dt <= 1e-12:
        return 0.0
    u = (t - t0) / dt
    if u < 0.0:
        u = 0.0
    elif u > 1.0:
        u = 1.0
    for _ in range(5):
        om = 1.0 - u
        x = om * om * om * t0 + 3.0 * om * om * u * cx1 + 3.0 * om * u * u * cx2 + u * u * u * t1
        dx = 3.0 * om * om * (cx1 - t0) + 6.0 * om * u * (cx2 - cx1) + 3.0 * u * u * (t1 - cx2)
        if abs(dx) < 1e-12:
            break
        u -= (x - t) / dx
        if u < 0.0:
            u = 0.0
        elif u > 1.0:
            u = 1.0
    return u


def fit_cubic_1d(
    ts: list[float],
    vs: list[float],
    t0: float,
    v0: float,
    cx1: float,
    cx2: float,
    t1: float,
    v1: float,
    uniform: bool,
) -> tuple[float, float] | None:
    dt = t1 - t0
    if dt <= 1e-12:
        return None
    s11 = s12 = s22 = r1 = r2 = 0.0
    for t, v in zip(ts, vs):
        u = (t - t0) / dt if uniform else solve_u_for_t(t, t0, cx1, cx2, t1)
        om = 1.0 - u
        a = 3.0 * om * om * u
        b = 3.0 * om * u * u
        y = v - (om * om * om * v0 + u * u * u * v1)
        s11 += a * a
        s12 += a * b
        s22 += b * b
        r1 += a * y
        r2 += b * y
    det = s11 * s22 - s12 * s12
    if abs(det) < 1e-14:
        return (v0 + (v1 - v0) / 3.0, v0 + 2.0 * (v1 - v0) / 3.0)
    return ((r1 * s22 - r2 * s12) / det, (s11 * r2 - s12 * r1) / det)


def eval_pwl_1d(pts_t: list[float], pts_v: list[float], a: int, b: int, t: float) -> float:
    lo, hi = a, b
    while lo + 1 < hi:
        mid = (lo + hi) >> 1
        if pts_t[mid] <= t:
            lo = mid
        else:
            hi = mid
    dt = pts_t[hi] - pts_t[lo]
    if dt <= 1e-12:
        return pts_v[lo]
    return pts_v[lo] + (pts_v[hi] - pts_v[lo]) * (t - pts_t[lo]) / dt


# ---------------------------------------------------------------------------
# Timestamp precision helper (prevents fast-slope rounding drift)
# ---------------------------------------------------------------------------

def choose_time_digits(pts_t: list[float], pts_vals: list[list[float]], idx: int, tol: float) -> int:
    """Return 4 unless rounding timestamp to 4 decimals would shift pose by > 0.25*tol."""
    t = pts_t[idx]
    dt_err = abs(round(t, 4) - t)
    if dt_err <= 1e-12:
        return 4
    max_slope = 0.0
    n = len(pts_t)
    for nb in (idx - 1, idx + 1):
        if 0 <= nb < n:
            seg_dt = abs(pts_t[nb] - t)
            if seg_dt > 1e-9:
                for vals in pts_vals:
                    slope = abs(vals[nb] - vals[idx]) / seg_dt
                    if slope > max_slope:
                        max_slope = slope
    if max_slope * dt_err > tol * 0.25:
        return 6
    return 4


def round_time_list(pts_t: list[float], pts_vals: list[list[float]], tol: float) -> list[float]:
    n = len(pts_t)
    out = [0.0] * n
    for i in range(n):
        d = choose_time_digits(pts_t, pts_vals, i, tol)
        rt = round(pts_t[i], d)
        if i > 0 and rt <= out[i - 1]:
            rt = round(pts_t[i], 6)
            if rt <= out[i - 1]:
                rt = pts_t[i]
        out[i] = rt
    return out


# ---------------------------------------------------------------------------
# Linear + Spine 4.2 Cubic Bezier Run Simplifiers
# ---------------------------------------------------------------------------

def test_linear_1d(
    pts_t: list[float],
    pts_rt: list[float],
    pts_v: list[float],
    a: int,
    b: int,
    tol: float,
) -> bool:
    ta, va = pts_rt[a], round(pts_v[a], 4)
    tb, vb = pts_rt[b], round(pts_v[b], 4)
    dt = tb - ta
    for i in range(a + 1, b):
        ti = pts_t[i]
        u = (ti - ta) / dt if dt > 1e-12 else 0.0
        if abs(pts_v[i] - (va + (vb - va) * u)) > tol:
            return False
    return True


def test_bezier_1d(
    pts_t: list[float],
    pts_rt: list[float],
    pts_v: list[float],
    a: int,
    b: int,
    tol: float,
    tol60: float,
    cy_digits: int,
) -> list[int | float] | None:
    ta, va = pts_rt[a], round(pts_v[a], 4)
    tb, vb = pts_rt[b], round(pts_v[b], 4)
    dt = tb - ta
    if dt <= 1e-12:
        return None
    ts_in = pts_t[a + 1 : b]
    vs_in = pts_v[a + 1 : b]
    k0 = int(math.floor(ta * 60.0)) + 1
    k1 = int(math.ceil(tb * 60.0)) - 1
    for h_idx, (a1, a2) in enumerate(BEZIER_HANDLE_CANDIDATES):
        cx1 = round(ta + a1 * dt, 4)
        cx2 = round(ta + a2 * dt, 4)
        if not (ta < cx1 < tb and ta < cx2 < tb):
            cx1 = round(ta + a1 * dt, 6)
            cx2 = round(ta + a2 * dt, 6)
        fit = fit_cubic_1d(ts_in, vs_in, ta, va, cx1, cx2, tb, vb, uniform=(h_idx == 0))
        if fit is None:
            continue
        cy1 = round(fit[0], cy_digits)
        cy2 = round(fit[1], cy_digits)
        xs, ys = build_spine_bezier_18(ta, va, cx1, cy1, cx2, cy2, tb, vb)
        ok = True
        for i in range(a + 1, b):
            if abs(eval_spine_bezier_18(xs, ys, pts_t[i]) - pts_v[i]) > tol:
                ok = False
                break
        if not ok:
            continue
        for k in range(k0, k1 + 1):
            t = k / 60.0
            if t <= ta or t >= tb:
                continue
            if abs(eval_spine_bezier_18(xs, ys, t) - eval_pwl_1d(pts_t, pts_v, a, b, t)) > tol60:
                ok = False
                break
        if ok:
            return [
                clean_num(cx1, 6 if abs(cx1 - round(cx1, 4)) > 1e-9 else 4),
                clean_num(cy1, cy_digits),
                clean_num(cx2, 6 if abs(cx2 - round(cx2, 4)) > 1e-9 else 4),
                clean_num(cy2, cy_digits),
            ]
    return None


def simplify_run_1d(
    pts_t: list[float],
    pts_rt: list[float],
    pts_v: list[float],
    tol: float,
    tol60: float,
    cy_digits: int,
    allow_bezier: bool,
) -> list[tuple[int, list[int | float] | None]]:
    n = len(pts_t)
    if n <= 2:
        return [(i, None) for i in range(n)]
    out: list[tuple[int, list[int | float] | None]] = []
    a = 0
    while a < n - 1:
        best_lin = a + 1
        for b in range(a + 2, n):
            if test_linear_1d(pts_t, pts_rt, pts_v, a, b, tol):
                best_lin = b
            else:
                break
        best_bez = -1
        best_cv: list[int | float] | None = None
        if allow_bezier and n - a >= 4:
            fails = 0
            for b in range(max(best_lin + 1, a + 3), n):
                cv = test_bezier_1d(pts_t, pts_rt, pts_v, a, b, tol, tol60, cy_digits)
                if cv is not None:
                    best_bez = b
                    best_cv = cv
                    fails = 0
                else:
                    fails += 1
                    if fails >= 4:
                        break
        if best_bez >= best_lin + 2 and best_cv is not None:
            out.append((a, best_cv))
            a = best_bez
        else:
            out.append((a, None))
            a = best_lin
    out.append((n - 1, None))
    return out


def simplify_run_2d(
    pts_t: list[float],
    pts_rt: list[float],
    pts_x: list[float],
    pts_y: list[float],
    tol: float,
    tol60: float,
    cy_digits: int,
    allow_bezier: bool,
) -> list[tuple[int, list[int | float] | None]]:
    n = len(pts_t)
    if n <= 2:
        return [(i, None) for i in range(n)]
    out: list[tuple[int, list[int | float] | None]] = []
    a = 0
    while a < n - 1:
        best_lin = a + 1
        for b in range(a + 2, n):
            if test_linear_1d(pts_t, pts_rt, pts_x, a, b, tol) and test_linear_1d(
                pts_t, pts_rt, pts_y, a, b, tol
            ):
                best_lin = b
            else:
                break
        best_bez = -1
        best_cv: list[int | float] | None = None
        if allow_bezier and n - a >= 4:
            fails = 0
            for b in range(max(best_lin + 1, a + 3), n):
                cx = test_bezier_1d(pts_t, pts_rt, pts_x, a, b, tol, tol60, cy_digits)
                if cx is not None:
                    cy = test_bezier_1d(pts_t, pts_rt, pts_y, a, b, tol, tol60, cy_digits)
                    if cy is not None:
                        best_bez = b
                        best_cv = cx + cy
                        fails = 0
                        continue
                fails += 1
                if fails >= 4:
                    break
        # 2D bezier array adds ~56-68 bytes vs ~32-38 bytes per linear key; require >= 2 extra keys skipped
        if best_bez >= best_lin + 2 and best_cv is not None:
            out.append((a, best_cv))
            a = best_bez
        else:
            out.append((a, None))
            a = best_lin
    out.append((n - 1, None))
    return out


# ---------------------------------------------------------------------------
# Bone Timeline Optimizers
# ---------------------------------------------------------------------------

def is_identity_1d(keys: list[dict[str, Any]], default: float) -> bool:
    for k in keys:
        if abs(float(k.get("value", default)) - default) > 1e-4:
            return False
        c = k.get("curve")
        if isinstance(c, list):
            # 1D bezier: [cx1, cy1, cx2, cy2]
            if len(c) >= 4 and (abs(float(c[1]) - default) > 1e-4 or abs(float(c[3]) - default) > 1e-4):
                return False
    return True


def is_identity_2d(keys: list[dict[str, Any]], d0: float, d1: float) -> bool:
    for k in keys:
        if abs(float(k.get("x", d0)) - d0) > 1e-4 or abs(float(k.get("y", d1)) - d1) > 1e-4:
            return False
        c = k.get("curve")
        if isinstance(c, list):
            # 2D bezier: [cx1x, cy1x, cx2x, cy2x, cx1y, cy1y, cx2y, cy2y]
            if len(c) >= 8 and (
                abs(float(c[1]) - d0) > 1e-4
                or abs(float(c[3]) - d0) > 1e-4
                or abs(float(c[5]) - d1) > 1e-4
                or abs(float(c[7]) - d1) > 1e-4
            ):
                return False
    return True


def _exact_num(value: float) -> int | float:
    """Keep full original float precision while normalizing -0.0 and exact integers."""
    f = float(value)
    if f == 0.0:
        return 0
    if f == int(f):
        return int(f)
    return f


def format_1d_items(
    items: list[tuple[float, float, Any, bool]], default: float
) -> list[dict[str, Any]] | None:
    """Format list of (t, v, curve, preserve_exact) into compact Spine 4.2 1D keys."""
    # Pop redundant trailing constant keys (never pop across a Bezier curve).
    while len(items) >= 2:
        _pt, pv, pcv, p_exact = items[-2]
        _lt, lv, _lcv, l_exact = items[-1]
        if isinstance(pcv, list) or p_exact or l_exact:
            break
        if abs(pv - lv) <= 1e-4:
            items.pop()
            t_prev, v_prev, _c_prev, ex_prev = items[-1]
            items[-1] = (t_prev, v_prev, None, ex_prev)
        else:
            break

    if all(
        abs(v - default) <= 1e-4 and not isinstance(cv, list)
        for _, v, cv, _ in items
    ):
        return None

    res: list[dict[str, Any]] = []
    for t, v, cv, exact in items:
        nk: dict[str, Any] = {}
        ct = _exact_num(t) if exact else clean_num(t, 6 if abs(t - round(t, 4)) > 1e-9 else 4)
        if ct != 0:
            nk["time"] = ct
        cv_val = _exact_num(v) if exact else clean_num(v, 4)
        if cv_val != default and abs(cv_val - default) > 1e-9:
            nk["value"] = cv_val
        if cv is not None and cv != "linear":
            if isinstance(cv, list):
                nk["curve"] = [_exact_num(x) for x in cv] if exact else cv
            else:
                nk["curve"] = cv
        res.append(nk)
    return res


def format_2d_items(
    items: list[tuple[float, float, float, Any, bool]], d0: float, d1: float
) -> list[dict[str, Any]] | None:
    """Format list of (t, x, y, curve, preserve_exact) into compact Spine 4.2 2D keys."""
    while len(items) >= 2:
        _pt, px, py, pcv, p_exact = items[-2]
        _lt, lx, ly, _lcv, l_exact = items[-1]
        if isinstance(pcv, list) or p_exact or l_exact:
            break
        if abs(px - lx) <= 1e-4 and abs(py - ly) <= 1e-4:
            items.pop()
            t_prev, x_prev, y_prev, _c_prev, ex_prev = items[-1]
            items[-1] = (t_prev, x_prev, y_prev, None, ex_prev)
        else:
            break

    if all(
        abs(x - d0) <= 1e-4 and abs(y - d1) <= 1e-4 and not isinstance(cv, list)
        for _, x, y, cv, _ in items
    ):
        return None

    res: list[dict[str, Any]] = []
    for t, x, y, cv, exact in items:
        nk: dict[str, Any] = {}
        ct = _exact_num(t) if exact else clean_num(t, 6 if abs(t - round(t, 4)) > 1e-9 else 4)
        if ct != 0:
            nk["time"] = ct
        cx = _exact_num(x) if exact else clean_num(x, 4)
        cy = _exact_num(y) if exact else clean_num(y, 4)
        if cx != d0 and abs(cx - d0) > 1e-9:
            nk["x"] = cx
        if cy != d1 and abs(cy - d1) > 1e-9:
            nk["y"] = cy
        if cv is not None and cv != "linear":
            if isinstance(cv, list):
                nk["curve"] = [_exact_num(q) for q in cv] if exact else cv
            else:
                nk["curve"] = cv
        res.append(nk)
    return res


def _is_strictly_increasing_times(keys: list[dict[str, Any]]) -> bool:
    for i in range(len(keys) - 1):
        if float(keys[i + 1].get("time", 0.0)) <= float(keys[i].get("time", 0.0)) + 1e-12:
            return False
    return True


def optimize_bone_tl_1d(
    keys: list[dict[str, Any]],
    default: float,
    tol: float,
    tol60: float,
    cy_digits: int,
    allow_bezier: bool,
) -> list[dict[str, Any]] | None:
    if not keys:
        return None
    if not _is_strictly_increasing_times(keys):
        return copy.deepcopy(keys)
    if is_identity_1d(keys, default):
        return None

    n = len(keys)
    # Mark indices adjacent to existing non-linear curves so their exact precision is preserved.
    exact_idx = [False] * n
    for i in range(n - 1):
        if not is_linear_key(keys[i]):
            exact_idx[i] = True
            exact_idx[i + 1] = True

    items: list[tuple[float, float, Any, bool]] = []
    i = 0
    while i < n:
        if i == n - 1 or not is_linear_key(keys[i]):
            k = keys[i]
            items.append((float(k.get("time", 0.0)), float(k.get("value", default)), k.get("curve"), exact_idx[i]))
            i += 1
            continue
        j = i
        while j < n - 1 and is_linear_key(keys[j]):
            j += 1
        pts_t = [float(keys[idx].get("time", 0.0)) for idx in range(i, j + 1)]
        pts_v = [float(keys[idx].get("value", default)) for idx in range(i, j + 1)]
        pts_rt = round_time_list(pts_t, [pts_v], tol)
        if exact_idx[i]:
            pts_rt[0] = pts_t[0]
        if exact_idx[j]:
            pts_rt[-1] = pts_t[-1]
        kept = simplify_run_1d(pts_t, pts_rt, pts_v, tol, tol60, cy_digits, allow_bezier)
        for r, cv in kept[:-1]:
            items.append((pts_rt[r], pts_v[r], cv, exact_idx[i + r]))
        if j == n - 1:
            items.append((pts_rt[-1], pts_v[-1], None, exact_idx[j]))
            break
        i = j
    return format_1d_items(items, default)


def optimize_bone_tl_2d(
    tname: str,
    keys: list[dict[str, Any]],
    allow_bezier: bool,
    allow_split: bool,
) -> dict[str, list[dict[str, Any]]]:
    if not keys:
        return {}
    if not _is_strictly_increasing_times(keys):
        return {tname: copy.deepcopy(keys)}
    _fields, defaults, tol, tol60, cy_digits = BONE_TL_SPEC[tname]
    d0, d1 = defaults
    if is_identity_2d(keys, d0, d1):
        return {}

    n = len(keys)
    exact_idx = [False] * n
    has_nonlinear = False
    for i in range(n - 1):
        if not is_linear_key(keys[i]):
            has_nonlinear = True
            exact_idx[i] = True
            exact_idx[i + 1] = True

    items: list[tuple[float, float, float, Any, bool]] = []
    i = 0
    while i < n:
        if i == n - 1 or not is_linear_key(keys[i]):
            k = keys[i]
            items.append(
                (
                    float(k.get("time", 0.0)),
                    float(k.get("x", d0)),
                    float(k.get("y", d1)),
                    k.get("curve"),
                    exact_idx[i],
                )
            )
            i += 1
            continue
        j = i
        while j < n - 1 and is_linear_key(keys[j]):
            j += 1
        pts_t = [float(keys[idx].get("time", 0.0)) for idx in range(i, j + 1)]
        pts_x = [float(keys[idx].get("x", d0)) for idx in range(i, j + 1)]
        pts_y = [float(keys[idx].get("y", d1)) for idx in range(i, j + 1)]
        pts_rt = round_time_list(pts_t, [pts_x, pts_y], tol)
        if exact_idx[i]:
            pts_rt[0] = pts_t[0]
        if exact_idx[j]:
            pts_rt[-1] = pts_t[-1]
        kept = simplify_run_2d(pts_t, pts_rt, pts_x, pts_y, tol, tol60, cy_digits, allow_bezier)
        for r, cv in kept[:-1]:
            items.append((pts_rt[r], pts_x[r], pts_y[r], cv, exact_idx[i + r]))
        if j == n - 1:
            items.append((pts_rt[-1], pts_x[-1], pts_y[-1], None, exact_idx[j]))
            break
        i = j

    res2d = format_2d_items(items, d0, d1)
    if res2d is None:
        return {}

    if not allow_split or has_nonlinear or tname not in SPLIT_MAP:
        return {tname: res2d}

    # Opportunistic 2D -> 1D axis split: ONLY accepted when BOTH X and Y remain non-identity
    # (preserving exact Spine 4.2 propertyIds for multi-track safety) AND serialized JSON is smaller.
    pts_t = [float(k.get("time", 0.0)) for k in keys]
    pts_x = [float(k.get("x", d0)) for k in keys]
    pts_y = [float(k.get("y", d1)) for k in keys]
    if all(abs(x - d0) <= 1e-4 for x in pts_x) or all(abs(y - d1) <= 1e-4 for y in pts_y):
        # One axis is pure identity -> 2D timeline with default field omitted is already smaller & safer.
        return {tname: res2d}

    rt_x = round_time_list(pts_t, [pts_x], tol)
    rt_y = round_time_list(pts_t, [pts_y], tol)
    kx = simplify_run_1d(pts_t, rt_x, pts_x, tol, tol60, cy_digits, allow_bezier)
    ky = simplify_run_1d(pts_t, rt_y, pts_y, tol, tol60, cy_digits, allow_bezier)
    rx = format_1d_items([(rt_x[r], pts_x[r], cv, False) for r, cv in kx], d0)
    ry = format_1d_items([(rt_y[r], pts_y[r], cv, False) for r, cv in ky], d1)
    if rx is None or ry is None:
        return {tname: res2d}

    tx_name, ty_name = SPLIT_MAP[tname]
    split_dict = {tx_name: rx, ty_name: ry}
    b_2d = len(json.dumps({tname: res2d}, separators=(",", ":")))
    b_sp = len(json.dumps(split_dict, separators=(",", ":")))
    if b_sp < b_2d:
        return split_dict
    return {tname: res2d}


# ---------------------------------------------------------------------------
# Slot RGBA Timeline Optimizer (strictly linear segments)
# ---------------------------------------------------------------------------

def parse_rgba_hex(color: str) -> tuple[int, int, int, int]:
    s = color.strip()
    if len(s) == 6:
        return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255)
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), int(s[6:8], 16))


def simplify_slot_rgba(keys: list[dict[str, Any]]) -> list[dict[str, Any]]:
    n = len(keys)
    if n == 0:
        return keys
    if not _is_strictly_increasing_times(keys):
        return copy.deepcopy(keys)
    exact_idx = [False] * n
    for i in range(n - 1):
        if not is_linear_key(keys[i]):
            exact_idx[i] = True
            exact_idx[i + 1] = True

    out: list[tuple[dict[str, Any], float, bool]] = []
    i = 0
    while i < n:
        if i == n - 1 or not is_linear_key(keys[i]) or "color" not in keys[i]:
            out.append((keys[i], float(keys[i].get("time", 0.0)), exact_idx[i]))
            i += 1
            continue
        j = i
        while j < n - 1 and is_linear_key(keys[j]) and "color" in keys[j]:
            j += 1
        pts_t = [float(keys[idx].get("time", 0.0)) for idx in range(i, j + 1)]
        pts_c = [parse_rgba_hex(str(keys[idx].get("color", "ffffffff"))) for idx in range(i, j + 1)]
        pts_rt = round_time_list(
            pts_t,
            [[float(c[ch]) for c in pts_c] for ch in range(4)],
            RGBA_TOL_255,
        )
        if exact_idx[i]:
            pts_rt[0] = pts_t[0]
        if exact_idx[j]:
            pts_rt[-1] = pts_t[-1]
        a = 0
        m = len(pts_t)
        while a < m - 1:
            best = a + 1
            ta = pts_rt[a]
            ca = pts_c[a]
            for b in range(a + 2, m):
                tb = pts_rt[b]
                cb = pts_c[b]
                dt = tb - ta
                ok = True
                for idx in range(a + 1, b):
                    u = (pts_t[idx] - ta) / dt if dt > 1e-12 else 0.0
                    ci = pts_c[idx]
                    for ch in range(4):
                        if abs(ci[ch] - (ca[ch] + (cb[ch] - ca[ch]) * u)) > RGBA_TOL_255:
                            ok = False
                            break
                    if not ok:
                        break
                if ok:
                    best = b
                else:
                    break
            out.append((keys[i + a], pts_rt[a], exact_idx[i + a]))
            a = best
        if j == n - 1:
            out.append((keys[j], pts_rt[-1], exact_idx[j]))
            break
        i = j

    # Collapse trailing identical linear color keys
    while len(out) >= 2:
        pk, _pt, p_ex = out[-2]
        lk, _lt, l_ex = out[-1]
        if not is_linear_key(pk) or p_ex or l_ex:
            break
        if str(pk.get("color", "ffffffff")).lower() == str(lk.get("color", "ffffffff")).lower():
            out.pop()
        else:
            break

    res: list[dict[str, Any]] = []
    for k, rt, exact in out:
        nk = dict(k)
        ct = _exact_num(rt) if exact else clean_num(rt, 6 if abs(rt - round(rt, 4)) > 1e-9 else 4)
        if ct == 0:
            nk.pop("time", None)
        else:
            nk["time"] = ct
        if nk.get("curve") == "linear":
            nk.pop("curve", None)
        res.append(nk)
    return res


# ---------------------------------------------------------------------------
# Full Skeleton Optimizer
# ---------------------------------------------------------------------------

def _append_duration_anchor(anim: dict[str, Any], orig_duration: float, fallback_bone: str) -> None:
    """Append a zero-drift duration anchor so animation_duration(anim) == orig_duration."""
    anchor_t = clean_num(orig_duration, 6 if abs(orig_duration - round(orig_duration, 4)) > 1e-4 else 4)
    bones = anim.get("bones")
    if isinstance(bones, dict) and bones:
        # Prefer appending a duplicate of an existing bone timeline's final key (0 pose change, 0 track bleed).
        for preferred_tl in ("rotate", "translatex", "translatey", "translate", "scale", "scalex", "scaley", "shear"):
            for bname, btls in bones.items():
                if not isinstance(btls, dict) or preferred_tl not in btls:
                    continue
                keys = btls[preferred_tl]
                if not isinstance(keys, list) or not keys:
                    continue
                last_k = keys[-1]
                if isinstance(last_k, dict) and is_linear_key(last_k):
                    dup = {k: v for k, v in last_k.items() if k != "curve"}
                    dup["time"] = anchor_t
                    keys.append(dup)
                    return
    # Fallback: add a minimal identity translate anchor on fallback_bone.
    if not isinstance(bones, dict):
        bones = {}
        anim["bones"] = bones
    btls = bones.setdefault(fallback_bone, {})
    if "translate" not in btls:
        btls["translate"] = [{"time": anchor_t}] if anchor_t != 0 else [{}]
    else:
        keys = btls["translate"]
        dup = {k: v for k, v in keys[-1].items() if k != "curve"}
        dup["time"] = anchor_t
        keys.append(dup)


def optimize_skeleton(
    orig_data: dict[str, Any],
    allow_bezier: bool = True,
    allow_split: bool = True,
) -> dict[str, Any]:
    """Return a newly optimized Spine 4.2 JSON object without mutating orig_data."""
    opt_data: dict[str, Any] = {}
    for top_key, top_val in orig_data.items():
        if top_key != "animations":
            opt_data[top_key] = copy.deepcopy(top_val)

    skel_bones = [b["name"] for b in orig_data.get("bones", []) if isinstance(b, dict) and "name" in b]
    default_root = next((b for b in skel_bones if b.lower() == "root"), skel_bones[0] if skel_bones else "root")

    orig_anims = orig_data.get("animations", {})
    opt_anims: dict[str, Any] = {}

    for aname, orig_anim in orig_anims.items():
        if not isinstance(orig_anim, dict):
            opt_anims[aname] = copy.deepcopy(orig_anim)
            continue

        orig_dur = animation_duration(orig_anim)
        orig_bones = orig_anim.get("bones")
        had_bones = isinstance(orig_bones, dict) and bool(orig_bones)
        if had_bones:
            first_orig_bone = (
                default_root if default_root in orig_bones else next(iter(orig_bones.keys()))
            )
        else:
            first_orig_bone = default_root

        new_anim: dict[str, Any] = {}
        for sec_name, sec_val in orig_anim.items():
            if sec_name == "bones" and isinstance(sec_val, dict):
                new_bones: dict[str, Any] = {}
                for bname, btls in sec_val.items():
                    if not isinstance(btls, dict):
                        new_bones[bname] = copy.deepcopy(btls)
                        continue
                    new_btls: dict[str, Any] = {}
                    for tname, keys in btls.items():
                        if not isinstance(keys, list):
                            new_btls[tname] = copy.deepcopy(keys)
                            continue
                        if tname in SPLIT_MAP:
                            tx, ty = SPLIT_MAP[tname]
                            can_split = allow_split and (tx not in btls and ty not in btls)
                            res_map = optimize_bone_tl_2d(
                                tname, keys, allow_bezier=allow_bezier, allow_split=can_split
                            )
                            new_btls.update(res_map)
                        elif tname in BONE_TL_SPEC:
                            _f, defaults, tol, tol60, cy_digits = BONE_TL_SPEC[tname]
                            res1 = optimize_bone_tl_1d(
                                keys, defaults[0], tol, tol60, cy_digits, allow_bezier=allow_bezier
                            )
                            if res1 is not None:
                                new_btls[tname] = res1
                        else:
                            new_btls[tname] = copy.deepcopy(keys)
                    if new_btls:
                        new_bones[bname] = new_btls
                if new_bones:
                    new_anim["bones"] = new_bones
            elif sec_name == "slots" and isinstance(sec_val, dict):
                new_slots: dict[str, Any] = {}
                for sname, stls in sec_val.items():
                    if not isinstance(stls, dict):
                        new_slots[sname] = copy.deepcopy(stls)
                        continue
                    new_stls: dict[str, Any] = {}
                    for tname, keys in stls.items():
                        if tname == "rgba" and isinstance(keys, list):
                            new_stls[tname] = simplify_slot_rgba(keys)
                        else:
                            new_stls[tname] = copy.deepcopy(keys)
                    if new_stls:
                        new_slots[sname] = new_stls
                if new_slots:
                    new_anim["slots"] = new_slots
            else:
                new_anim[sec_name] = copy.deepcopy(sec_val)

        # Preserve non-empty `bones` contract if the original animation had `bones`.
        if had_bones and not new_anim.get("bones"):
            anchor_t = clean_num(orig_dur, 6 if abs(orig_dur - round(orig_dur, 4)) > 1e-4 else 4)
            new_anim["bones"] = {
                first_orig_bone: {"translate": [{"time": anchor_t}] if anchor_t > 0 else [{}]}
            }

        # Step C: preserve EXACT animation duration.
        new_dur = animation_duration(new_anim)
        if orig_dur > 0.0 and new_dur < orig_dur - 1e-5:
            _append_duration_anchor(new_anim, orig_dur, first_orig_bone)

        opt_anims[aname] = new_anim

    opt_data["animations"] = opt_anims
    return opt_data


# ---------------------------------------------------------------------------
# Mandatory 60 Hz Verification Gate (Exact Spine 4.2 Evaluation)
# ---------------------------------------------------------------------------

class CompiledTimeline1D:
    """Pre-compiled Spine 4.2 1D timeline for fast exact 60 Hz sampling."""

    __slots__ = ("times", "vals", "curves", "beziers", "default")

    def __init__(self, keys: list[dict[str, Any]], field: str, default: float, curve_offset: int = 0):
        self.default = default
        n = len(keys)
        self.times = [float(k.get("time", 0.0)) for k in keys]
        self.vals = [float(k.get(field, default)) for k in keys]
        self.curves: list[str] = []
        self.beziers: list[tuple[list[float], list[float]] | None] = []
        for i in range(n):
            c = keys[i].get("curve") if i < n - 1 else None
            if c == "stepped":
                self.curves.append("stepped")
                self.beziers.append(None)
            elif isinstance(c, list) and len(c) >= curve_offset + 4 and i < n - 1:
                self.curves.append("bezier")
                cx1, cy1, cx2, cy2 = (
                    float(c[curve_offset]),
                    float(c[curve_offset + 1]),
                    float(c[curve_offset + 2]),
                    float(c[curve_offset + 3]),
                )
                self.beziers.append(
                    build_spine_bezier_18(
                        self.times[i], self.vals[i], cx1, cy1, cx2, cy2, self.times[i + 1], self.vals[i + 1]
                    )
                )
            else:
                self.curves.append("linear")
                self.beziers.append(None)

    def eval(self, t: float) -> float:
        times = self.times
        n = len(times)
        if n == 0 or t < times[0] - 1e-9:
            return self.default
        if t <= times[0] or n == 1:
            return self.vals[0]
        if t >= times[-1]:
            return self.vals[-1]
        lo, hi = 0, n - 1
        while lo + 1 < hi:
            mid = (lo + hi) >> 1
            if times[mid] <= t:
                lo = mid
            else:
                hi = mid
        mode = self.curves[lo]
        if mode == "stepped":
            return self.vals[lo]
        if mode == "bezier":
            bez = self.beziers[lo]
            assert bez is not None
            return eval_spine_bezier_18(bez[0], bez[1], t)
        dt = times[hi] - times[lo]
        if dt <= 1e-12:
            return self.vals[lo]
        return self.vals[lo] + (self.vals[hi] - self.vals[lo]) * (t - times[lo]) / dt


def _compile_bone_channels(btls: dict[str, Any]) -> dict[str, CompiledTimeline1D]:
    ch: dict[str, CompiledTimeline1D] = {}
    if "translate" in btls and btls["translate"]:
        ch["tx"] = CompiledTimeline1D(btls["translate"], "x", 0.0, 0)
        ch["ty"] = CompiledTimeline1D(btls["translate"], "y", 0.0, 4)
    if "translatex" in btls and btls["translatex"]:
        ch["tx"] = CompiledTimeline1D(btls["translatex"], "value", 0.0, 0)
    if "translatey" in btls and btls["translatey"]:
        ch["ty"] = CompiledTimeline1D(btls["translatey"], "value", 0.0, 0)
    if "rotate" in btls and btls["rotate"]:
        ch["rot"] = CompiledTimeline1D(btls["rotate"], "value", 0.0, 0)
    if "scale" in btls and btls["scale"]:
        ch["sx"] = CompiledTimeline1D(btls["scale"], "x", 1.0, 0)
        ch["sy"] = CompiledTimeline1D(btls["scale"], "y", 1.0, 4)
    if "scalex" in btls and btls["scalex"]:
        ch["sx"] = CompiledTimeline1D(btls["scalex"], "value", 1.0, 0)
    if "scaley" in btls and btls["scaley"]:
        ch["sy"] = CompiledTimeline1D(btls["scaley"], "value", 1.0, 0)
    if "shear" in btls and btls["shear"]:
        ch["shx"] = CompiledTimeline1D(btls["shear"], "x", 0.0, 0)
        ch["shy"] = CompiledTimeline1D(btls["shear"], "y", 0.0, 4)
    if "shearx" in btls and btls["shearx"]:
        ch["shx"] = CompiledTimeline1D(btls["shearx"], "value", 0.0, 0)
    if "sheary" in btls and btls["sheary"]:
        ch["shy"] = CompiledTimeline1D(btls["sheary"], "value", 0.0, 0)
    return ch


class CompiledRgbaTimeline:
    """Pre-compiled Spine 4.2 slot rgba timeline for fast 60 Hz verification."""

    __slots__ = ("times", "colors", "modes", "beziers")

    def __init__(self, keys: list[dict[str, Any]]):
        n = len(keys)
        self.times = [float(k.get("time", 0.0)) for k in keys]
        self.colors = [parse_rgba_hex(str(k.get("color", "ffffffff"))) for k in keys]
        self.modes: list[str] = []
        self.beziers: list[list[tuple[list[float], list[float]]] | None] = []
        for i in range(n):
            c = keys[i].get("curve") if i < n - 1 else None
            if c == "stepped":
                self.modes.append("stepped")
                self.beziers.append(None)
            elif isinstance(c, list) and len(c) >= 16 and i < n - 1:
                self.modes.append("bezier")
                c0, c1 = self.colors[i], self.colors[i + 1]
                ch_bez = []
                for ch in range(4):
                    off = ch * 4
                    ch_bez.append(
                        build_spine_bezier_18(
                            self.times[i],
                            c0[ch] / 255.0,
                            float(c[off]),
                            float(c[off + 1]),
                            float(c[off + 2]),
                            float(c[off + 3]),
                            self.times[i + 1],
                            c1[ch] / 255.0,
                        )
                    )
                self.beziers.append(ch_bez)
            else:
                self.modes.append("linear")
                self.beziers.append(None)

    def eval(self, t: float) -> tuple[float, float, float, float] | None:
        times = self.times
        n = len(times)
        if n == 0 or t < times[0] - 1e-9:
            return None
        if t <= times[0] or n == 1:
            c = self.colors[0]
            return (float(c[0]), float(c[1]), float(c[2]), float(c[3]))
        if t >= times[-1]:
            c = self.colors[-1]
            return (float(c[0]), float(c[1]), float(c[2]), float(c[3]))
        lo, hi = 0, n - 1
        while lo + 1 < hi:
            mid = (lo + hi) >> 1
            if times[mid] <= t:
                lo = mid
            else:
                hi = mid
        mode = self.modes[lo]
        c0 = self.colors[lo]
        if mode == "stepped":
            return (float(c0[0]), float(c0[1]), float(c0[2]), float(c0[3]))
        if mode == "bezier":
            bez = self.beziers[lo]
            assert bez is not None
            return (
                eval_spine_bezier_18(bez[0][0], bez[0][1], t) * 255.0,
                eval_spine_bezier_18(bez[1][0], bez[1][1], t) * 255.0,
                eval_spine_bezier_18(bez[2][0], bez[2][1], t) * 255.0,
                eval_spine_bezier_18(bez[3][0], bez[3][1], t) * 255.0,
            )
        c1 = self.colors[hi]
        dt = times[hi] - times[lo]
        u = (t - times[lo]) / dt if dt > 1e-12 else 0.0
        return (
            c0[0] + (c1[0] - c0[0]) * u,
            c0[1] + (c1[1] - c0[1]) * u,
            c0[2] + (c1[2] - c0[2]) * u,
            c0[3] + (c1[3] - c0[3]) * u,
        )


def verify_skeleton_pair(
    orig_data: dict[str, Any],
    opt_data: dict[str, Any],
    label: str = "",
) -> dict[str, float]:
    """Strictly verify lossless equivalence at 60 Hz across every animation & bone."""
    # 1. Exact non-animation top-level equality
    orig_top = {k for k in orig_data if k != "animations"}
    opt_top = {k for k in opt_data if k != "animations"}
    if orig_top != opt_top:
        raise AssertionError(f"{label}: top-level keys mismatch: {orig_top ^ opt_top}")
    for k in orig_top:
        if orig_data[k] != opt_data[k]:
            raise AssertionError(f"{label}: top-level section {k!r} was modified!")

    # 2. Exact animation names
    orig_anims = orig_data.get("animations", {})
    opt_anims = opt_data.get("animations", {})
    if set(orig_anims.keys()) != set(opt_anims.keys()):
        raise AssertionError(f"{label}: animation names mismatch: {set(orig_anims) ^ set(opt_anims)}")

    max_err = {
        "translate": 0.0,
        "rotate": 0.0,
        "scale": 0.0,
        "shear": 0.0,
        "rgba": 0.0,
        "duration": 0.0,
    }
    ch_kind = {
        "tx": ("translate", 0.0),
        "ty": ("translate", 0.0),
        "rot": ("rotate", 0.0),
        "sx": ("scale", 1.0),
        "sy": ("scale", 1.0),
        "shx": ("shear", 0.0),
        "shy": ("shear", 0.0),
    }

    for aname, o_anim in orig_anims.items():
        n_anim = opt_anims[aname]
        # Exact non-bone, non-slot sections (events, ik, transform, path, physics, drawOrder, attachments, deform)
        o_other = {k for k in o_anim if k not in ("bones", "slots")}
        n_other = {k for k in n_anim if k not in ("bones", "slots")}
        if o_other != n_other:
            raise AssertionError(f"{label}/{aname}: animation sections mismatch: {o_other ^ n_other}")
        for sec in o_other:
            if o_anim[sec] != n_anim[sec]:
                raise AssertionError(f"{label}/{aname}: section {sec!r} was modified!")

        if bool(o_anim.get("bones")) and not bool(n_anim.get("bones")):
            raise AssertionError(f"{label}/{aname}: non-empty bones became empty!")

        o_dur = animation_duration(o_anim)
        n_dur = animation_duration(n_anim)
        dur_diff = abs(o_dur - n_dur)
        if dur_diff > max_err["duration"]:
            max_err["duration"] = dur_diff
        if dur_diff > VERIFY_LIMITS["duration"]:
            raise AssertionError(
                f"{label}/{aname}: duration drift {o_dur:.6f} -> {n_dur:.6f} (diff={dur_diff:.6f})"
            )

        # Build 60 Hz sample timestamps covering [0, o_dur]
        steps = int(math.ceil(o_dur * 60.0))
        sample_ts = [k / 60.0 for k in range(steps + 1) if k / 60.0 <= o_dur]
        if not sample_ts or abs(sample_ts[-1] - o_dur) > 1e-6:
            sample_ts.append(o_dur)

        # Compare every bone at 60 Hz
        o_bones = o_anim.get("bones", {})
        n_bones = n_anim.get("bones", {})
        all_bones = set(o_bones.keys()) | set(n_bones.keys())
        for bname in all_bones:
            o_ch = _compile_bone_channels(o_bones.get(bname, {}))
            n_ch = _compile_bone_channels(n_bones.get(bname, {}))
            active_keys = set(o_ch.keys()) | set(n_ch.keys())
            for ck in active_keys:
                kind, default = ch_kind[ck]
                tl_o = o_ch.get(ck)
                tl_n = n_ch.get(ck)
                lim = VERIFY_LIMITS[kind]
                for t in sample_ts:
                    vo = tl_o.eval(t) if tl_o is not None else default
                    vn = tl_n.eval(t) if tl_n is not None else default
                    err = abs(vo - vn)
                    if err > max_err[kind]:
                        max_err[kind] = err
                    if err > lim:
                        raise AssertionError(
                            f"{label}/{aname}/{bname}.{ck} at t={t:.4f}s: "
                            f"orig={vo:.5f} vs opt={vn:.5f} (err={err:.5f} > {lim})"
                        )

        # Compare slot timelines
        o_slots = o_anim.get("slots", {})
        n_slots = n_anim.get("slots", {})
        if set(o_slots.keys()) != set(n_slots.keys()):
            raise AssertionError(f"{label}/{aname}: slot set changed!")
        for sname, o_stls in o_slots.items():
            n_stls = n_slots[sname]
            if set(o_stls.keys()) != set(n_stls.keys()):
                raise AssertionError(f"{label}/{aname}/slot {sname}: timeline types changed!")
            for tname, o_keys in o_stls.items():
                n_keys = n_stls[tname]
                if tname != "rgba":
                    if o_keys != n_keys:
                        raise AssertionError(f"{label}/{aname}/slot {sname}.{tname} was modified!")
                elif o_keys != n_keys:
                    tl_o = CompiledRgbaTimeline(o_keys)
                    tl_n = CompiledRgbaTimeline(n_keys)
                    for t in sample_ts:
                        co = tl_o.eval(t)
                        cn = tl_n.eval(t)
                        if (co is None) != (cn is None):
                            raise AssertionError(
                                f"{label}/{aname}/slot {sname}.rgba presence mismatch at t={t:.4f}s"
                            )
                        if co is not None and cn is not None:
                            for ch_i in range(4):
                                err = abs(co[ch_i] - cn[ch_i])
                                if err > max_err["rgba"]:
                                    max_err["rgba"] = err
                                if err > VERIFY_LIMITS["rgba"]:
                                    raise AssertionError(
                                        f"{label}/{aname}/slot {sname}.rgba ch{ch_i} at t={t:.4f}s: "
                                        f"orig={co[ch_i]:.2f} vs opt={cn[ch_i]:.2f} (err={err:.2f})"
                                    )

    return max_err


# ---------------------------------------------------------------------------
# File Processing & CLI
# ---------------------------------------------------------------------------

def default_target_files(project_root: pathlib.Path) -> list[pathlib.Path]:
    files = sorted((project_root / "STS2_Things" / "animations" / "monsters").glob("*/*.spjson"))
    cave_god = project_root / "animations" / "monsters" / "cave_god" / "cave_god.spjson"
    if cave_god.is_file():
        files.append(cave_god)
    return files


def process_one_file(
    args_tuple: tuple[str, bool, bool, bool, bool]
) -> dict[str, Any]:
    path_str, dry_run, verify, allow_bezier, allow_split = args_tuple
    path = pathlib.Path(path_str)
    t_start = time.perf_counter()
    raw_bytes = path.read_bytes()
    orig_data = json.loads(raw_bytes.decode("utf-8"))

    orig_bkf = count_bone_keyframes(orig_data)
    orig_skf = count_slot_keyframes(orig_data)

    opt_data = optimize_skeleton(orig_data, allow_bezier=allow_bezier, allow_split=allow_split)
    out_text = json.dumps(opt_data, separators=(",", ":"), ensure_ascii=False)
    out_bytes = out_text.encode("utf-8")

    # Parse back the exact serialized JSON before verification so round-trip floats are verified!
    reparsed_opt = json.loads(out_bytes.decode("utf-8"))
    max_err: dict[str, float] = {}
    if verify:
        max_err = verify_skeleton_pair(orig_data, reparsed_opt, label=path.name)

    opt_bkf = count_bone_keyframes(reparsed_opt)
    opt_skf = count_slot_keyframes(reparsed_opt)

    wrote = False
    if not dry_run and len(out_bytes) <= len(raw_bytes):
        tmp_path = path.with_suffix(".spjson.tmp")
        tmp_path.write_bytes(out_bytes)
        os.replace(tmp_path, path)
        wrote = True

    return {
        "path": path_str,
        "name": path.name,
        "orig_bytes": len(raw_bytes),
        "opt_bytes": len(out_bytes),
        "orig_bkf": orig_bkf,
        "opt_bkf": opt_bkf,
        "orig_skf": orig_skf,
        "opt_skf": opt_skf,
        "max_err": max_err,
        "wrote": wrote,
        "elapsed": time.perf_counter() - t_start,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Lossless Spine 4.2 .spjson timeline optimizer with 60 Hz pose verification."
    )
    parser.add_argument(
        "paths",
        nargs="*",
        help="Target .spjson files or directories (defaults to all 36 shipping monster .spjson files).",
    )
    parser.add_argument("--dry-run", action="store_true", help="Optimize & verify in memory without writing.")
    parser.add_argument(
        "--verify",
        action="store_true",
        default=True,
        help="Run mandatory 60 Hz pose verification gate (enabled by default).",
    )
    parser.add_argument("--no-bezier", action="store_true", help="Disable Spine 4.2 cubic Bezier fitting.")
    parser.add_argument("--no-split", action="store_true", help="Disable opportunistic 2D->1D axis splitting.")
    parser.add_argument(
        "--workers",
        type=int,
        default=min(8, os.cpu_count() or 1),
        help="Parallel worker processes (default: min(8, cpu_count)).",
    )
    parser.add_argument(
        "--compare",
        nargs=2,
        metavar=("ORIG", "OPT"),
        help="Verify two existing .spjson files against each other at 60 Hz and exit.",
    )
    args = parser.parse_args()

    if args.compare:
        orig_p, opt_p = pathlib.Path(args.compare[0]), pathlib.Path(args.compare[1])
        orig_d = json.loads(orig_p.read_text(encoding="utf-8"))
        opt_d = json.loads(opt_p.read_text(encoding="utf-8"))
        errs = verify_skeleton_pair(orig_d, opt_d, label=opt_p.name)
        print(f"PASS {opt_p.name}: {errs}")
        return 0

    project_root = pathlib.Path(__file__).resolve().parent.parent
    targets: list[pathlib.Path] = []
    if args.paths:
        for raw_p in args.paths:
            p = pathlib.Path(raw_p)
            if p.is_dir():
                targets.extend(sorted(p.rglob("*.spjson")))
            elif p.is_file():
                targets.append(p)
            else:
                print(f"ERROR: path not found: {p}", file=sys.stderr)
                return 2
    else:
        targets = default_target_files(project_root)

    if not targets:
        print("No .spjson files found.", file=sys.stderr)
        return 1

    # Process largest files first for optimal multiprocessing load balance.
    targets.sort(key=lambda p: p.stat().st_size, reverse=True)
    work = [
        (str(p), args.dry_run, args.verify, not args.no_bezier, not args.no_split)
        for p in targets
    ]

    t0 = time.perf_counter()
    results: list[dict[str, Any]] = []
    if args.workers > 1 and len(work) > 1:
        with ProcessPoolExecutor(max_workers=args.workers) as pool:
            for res in pool.map(process_one_file, work):
                results.append(res)
    else:
        for item in work:
            results.append(process_one_file(item))

    results.sort(key=lambda r: r["orig_bytes"] - r["opt_bytes"], reverse=True)

    tot_orig = sum(r["orig_bytes"] for r in results)
    tot_opt = sum(r["opt_bytes"] for r in results)
    tot_bkf_o = sum(r["orig_bkf"] for r in results)
    tot_bkf_n = sum(r["opt_bkf"] for r in results)
    tot_skf_o = sum(r["orig_skf"] for r in results)
    tot_skf_n = sum(r["opt_skf"] for r in results)

    worst = {k: 0.0 for k in VERIFY_LIMITS}
    for r in results:
        for k, v in r["max_err"].items():
            if v > worst[k]:
                worst[k] = v

    mode = "DRY-RUN" if args.dry_run else "OPTIMIZED"
    print(f"=== {mode} {len(results)} Spine 4.2 .spjson files in {time.perf_counter() - t0:.2f}s ===")
    print(
        f"TOTAL SIZE:      {tot_orig / 1048576:.2f} MiB -> {tot_opt / 1048576:.2f} MiB "
        f"(saved {(tot_orig - tot_opt) / 1048576:.2f} MiB, {100.0 * (tot_orig - tot_opt) / tot_orig:.1f}%)"
    )
    print(
        f"BONE KEYFRAMES:  {tot_bkf_o:,} -> {tot_bkf_n:,} "
        f"({100.0 * (tot_bkf_o - tot_bkf_n) / max(1, tot_bkf_o):.1f}% reduced)"
    )
    print(
        f"SLOT KEYFRAMES:  {tot_skf_o:,} -> {tot_skf_n:,} "
        f"({100.0 * (tot_skf_o - tot_skf_n) / max(1, tot_skf_o):.1f}% reduced)"
    )
    if args.verify:
        print(
            "WORST 60Hz ERR:  "
            f"translate={worst['translate']:.4f}px (<=0.05), "
            f"rotate={worst['rotate']:.4f}deg (<=0.05), "
            f"scale={worst['scale']:.5f} (<=0.002), "
            f"shear={worst['shear']:.4f}deg (<=0.05), "
            f"rgba={worst['rgba']:.2f}/255 (<=1.5), "
            f"dur={worst['duration']:.6f}s (<=0.001)"
        )
    print("-" * 108)
    for r in results:
        saved = r["orig_bytes"] - r["opt_bytes"]
        pct = 100.0 * saved / max(1, r["orig_bytes"])
        kf_o = r["orig_bkf"] + r["orig_skf"]
        kf_n = r["opt_bkf"] + r["opt_skf"]
        e = r["max_err"]
        err_str = (
            f"t={e.get('translate', 0):.3f} r={e.get('rotate', 0):.3f} s={e.get('scale', 0):.4f}"
            if e
            else ""
        )
        print(
            f"  {r['name']:34s} {r['orig_bytes']/1048576:6.2f}M -> {r['opt_bytes']/1048576:5.2f}M "
            f"(-{saved/1048576:5.2f}M, {pct:4.1f}%) | kf {kf_o:7,d} -> {kf_n:7,d} | {err_str}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
