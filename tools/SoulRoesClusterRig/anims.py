"""10 Masterpiece Animations & C# Event Contracts for ThingsSoulRoes (灵魂异鱼卵 — 12-Orb Brood Cluster).

Strict Geometric Invariants across ALL 10 clips & ALL frames:
- scaleX == 1.0, scaleY == 1.0 on EVERY bone (maxBoneScaleDev == 0)!
- r == 0.0 deg on ALL 12 orb bones (brood_0..5, sat_0..5) & cluster_hub/root/ring_* (maxOrbRotDeg == 0)!
- trail_0_b..trail_5_b orient smoothly along flight velocity when visible and return to r == 0.0 deg at rest!
"""
import math

from rigdef import BROOD_NAMES, HUB_CANVAS, ORIGIN, POS, RING_SPAN, SAT_NAMES
from rigutil import TAU, Frame, bump, clamp, ease, ease_in, ease_out, hold, kick, seg


def _s(p):
    return (p[0] - ORIGIN[0], ORIGIN[1] - p[1])


HUB_S = _s(HUB_CANVAS)
# Rest offsets of brood_0..5 and sat_0..5 relative to cluster_hub in Spine (+X right, +Y up) coordinates
BROOD_REL = {
    b: (_s(POS[b])[0] - HUB_S[0], _s(POS[b])[1] - HUB_S[1])
    for b in BROOD_NAMES
}
SAT_REL = {
    s: (_s(POS[s])[0] - HUB_S[0], _s(POS[s])[1] - HUB_S[1])
    for s in SAT_NAMES
}
SAT_SETUP = {s: _s(POS[s]) for s in SAT_NAMES}
SAT_THETA0 = [math.atan2(SAT_REL[f"sat_{k}"][1], SAT_REL[f"sat_{k}"][0]) for k in range(6)]
BROOD_THETA0 = [math.atan2(BROOD_REL[f"brood_{k}"][1], BROOD_REL[f"brood_{k}"][0]) for k in range(6)]

ANIMS = {
    "idle_loop": (3.60, True),
    "multi_bite_6": (1.76, False),
    "multi_bite_4_intangible": (1.76, False),
    "attack": (1.04, False),
    "cast": (1.28, False),
    "power_up": (1.16, False),
    "summon": (1.26, False),
    "hurt": (0.46, False),
    "die": (1.12, False),
    "revive": (0.86, False),
}

# Exact C# event contracts (sorted chronologically per clip)
EVENTS = {
    "idle_loop": [],
    "multi_bite_6": [
        ("bite6_contact", 0.34),
        ("bite6_ready_2", 0.42),
        ("bite6_contact", 0.54),
        ("bite6_ready_3", 0.62),
        ("bite6_contact", 0.74),
        ("bite6_ready_4", 0.82),
        ("bite6_contact", 0.94),
        ("bite6_ready_5", 1.02),
        ("bite6_contact", 1.14),
        ("bite6_ready_6", 1.22),
        ("bite6_contact", 1.34),
    ],
    "multi_bite_4_intangible": [
        ("bite4_contact", 0.34),
        ("bite4_ready_2", 0.44),
        ("bite4_contact", 0.58),
        ("bite4_ready_3", 0.68),
        ("bite4_contact", 0.82),
        ("bite4_ready_4", 0.92),
        ("bite4_contact", 1.06),
        ("intangible_peak", 1.38),
    ],
    "attack": [("attack_contact", 0.42)],
    "cast": [("cast_release", 0.58)],
    "power_up": [("power_peak", 0.52)],
    "summon": [("summon_release", 0.64)],
    "hurt": [],
    "die": [("rupture_burst", 0.42)],
    "revive": [],
}


def _set_ring_scale(P, scale_factor: float):
    """Exact Euclidean radial scale on beckon_ring & intangible_shimmer via 4 pure-translation bones (sx==sy==1)."""
    d = (scale_factor - 1.0) * (2.0 * RING_SPAN)
    if abs(d) > 1e-4:
        P.add("ring_e", x=d)
        P.add("ring_w", x=-d)
        P.add("ring_n", y=d)
        P.add("ring_s", y=-d)


def _vortex_or_fling(
    t: float,
    k: int,
    hub_dx: float,
    hub_dy: float,
    t_hit: float | None,
    dx_hit: float,
    dy_lane: float,
    t_orbit_end: float,
    clip_dur: float,
):
    """Compute (sat_dx, sat_dy, trail_rot, trail_alpha, glow_val) for satellite k in centrifugal slingshot clips.

    - Before launch (or if t_hit is None): fast counter-clockwise orbital vortex around cluster_hub!
    - At t_launch = t_hit - 0.13s: releases off the upper CCW tangent (-X velocity) into a blazing whip-fling,
      hitting exact dx_hit <= -555px left across a 2-frame plateau [t_hit - 0.018, t_hit + 0.018], then swooping
      along an underhand arc back into its perimeter slot!
    """
    sname = f"sat_{k}"
    rx0, ry0 = SAT_REL[sname]
    sx0, sy0 = SAT_SETUP[sname]
    th0 = SAT_THETA0[k]
    # Evenly spaced 6-slot carousel angles (150 deg + 60*k) so spinning vortex ring stays wide & distinct!
    carousel_base = math.radians(150.0 + 60.0 * k)
    Rx, Ry = 172.0, 142.0

    if t_hit is None:
        # Pure vortex satellite (keeps spinning around hub in evenly-spaced carousel, then parks smoothly)
        env = hold(t, 0.0, 0.18, t_orbit_end - 0.24, t_orbit_end)
        th = carousel_base + TAU * 1.85 * ease(seg(t, 0.0, t_orbit_end))
        ox = Rx * math.cos(th)
        oy = Ry * math.sin(th)
        rel_x = rx0 + env * (ox - rx0)
        rel_y = ry0 + env * (oy - ry0)
        wx = HUB_S[0] + hub_dx + rel_x
        wy = HUB_S[1] + hub_dy + rel_y
        return (wx - sx0, wy - sy0, 0.0, 0.0, 0.72 * env)

    t_launch = t_hit - 0.13
    t_plat0 = t_hit - 0.018
    t_plat1 = t_hit + 0.018
    t_ret = min(t_hit + 0.35, clip_dur - 0.04)

    # Shared carousel angular velocity (~520 deg/s CCW), smoothly phasing into upper-left tangent (108 deg) before launch
    omega = math.radians(520.0)
    th_shared = carousel_base + omega * t
    th_at_launch_shared = carousel_base + omega * t_launch
    # Nearest upper-left launch apex >= th_at_launch_shared
    target_th = math.radians(108.0)
    turns = math.ceil((th_at_launch_shared - target_th) / TAU)
    th_release = target_th + TAU * turns

    u_whip = ease(seg(t, max(0.0, t_launch - 0.12), t_launch))
    th_cur = th_shared + (th_release - th_at_launch_shared) * u_whip
    blend_ring = ease(seg(t, 0.0, min(0.15, t_launch * 0.82)))
    orb_rx = rx0 + blend_ring * (Rx * math.cos(th_cur) - rx0)
    orb_ry = ry0 + blend_ring * (Ry * math.sin(th_cur) - ry0)

    launch_wx = HUB_S[0] + hub_dx + Rx * math.cos(th_release)
    launch_wy = HUB_S[1] + hub_dy + Ry * math.sin(th_release)
    hit_wx = sx0 + dx_hit
    hit_wy = HUB_S[1] + dy_lane

    # Tail angle matching trajectory from launch -> impact (points +X/back toward launch point, smoothly in [-24, +24] deg)
    vx_f = hit_wx - launch_wx
    vy_f = hit_wy - launch_wy
    flight_tail_deg = math.degrees(math.atan2(-vy_f, -vx_f))

    if t <= t_launch:
        wx = HUB_S[0] + hub_dx + orb_rx
        wy = HUB_S[1] + hub_dy + orb_ry
        # Pre-align trail_k_b invisibly before trail fades in near launch apex
        t_rot = flight_tail_deg * ease(seg(t, max(0.0, t_launch - 0.10), t_launch - 0.02))
        t_al = 0.78 * ease(seg(t, t_launch - 0.025, t_launch))
        gl = 0.85 * ease(seg(t, 0.0, 0.18))
        return (wx - sx0, wy - sy0, t_rot, t_al, gl)

    if t <= t_plat0:
        u = seg(t, t_launch, t_plat0)
        # Fast whip acceleration with snappy arrival into impact plateau
        ux = 1.0 - (1.0 - u) ** 2.6
        uy = ease(u) + 0.16 * math.sin(math.pi * u)
        wx = launch_wx + (hit_wx - launch_wx) * ux
        wy = launch_wy + (hit_wy - launch_wy) * uy
        return (wx - sx0, wy - sy0, flight_tail_deg, 1.0, 1.0)

    if t <= t_plat1:
        # Crisp 2-frame hit-stop impact plateau right across exact C# contact event!
        return (hit_wx - sx0, hit_wy - sy0, flight_tail_deg, 1.0, 1.0)

    if t <= t_ret:
        u = seg(t, t_plat1, t_ret)
        # Sweeping underhand return arc back to live perimeter slot on cluster_hub
        dest_wx = HUB_S[0] + hub_dx + rx0
        dest_wy = HUB_S[1] + hub_dy + ry0
        ux = ease(u)
        uy = ease(u)
        under_arc = -54.0 * math.sin(math.pi * u)
        wx = hit_wx + (dest_wx - hit_wx) * ux
        wy = hit_wy + (dest_wy - hit_wy) * uy + under_arc
        # Trail fades cleanly right after impact turnaround, then bone eases back to r == 0 invisibly
        t_al = 1.0 - ease(seg(t, t_plat1, t_plat1 + 0.11))
        t_rot = flight_tail_deg * (1.0 - ease(seg(t, t_plat1 + 0.12, t_ret)))
        gl = 1.0 - 0.85 * ease(u)
        return (wx - sx0, wy - sy0, t_rot, t_al, gl)

    # Cushioned elastic settle attached to cluster_hub
    settle = 1.0 - ease(seg(t, t_ret, clip_dur))
    wx = HUB_S[0] + hub_dx + rx0
    wy = HUB_S[1] + hub_dy + ry0
    gl = 0.15 * settle
    return (wx - sx0, wy - sy0, 0.0, 0.0, gl)


def anim_idle_loop(t: float) -> Frame:
    """3.60s seamless underwater buoyancy float + surface-tension wave + figure-8 satellite bob + golden yolk wave."""
    f = Frame()
    P = f.P
    dur = 3.60
    th = TAU * t / dur

    # Cluster hub slow underwater buoyancy (±12px vertical float, exact 0 at t=0 & t=3.60 with continuous velocity!)
    hub_dx = 4.5 * math.sin(th) - 2.0 * math.sin(2.0 * th)
    hub_dy = 12.0 * math.sin(th)
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    # Organic radial surface-tension breathing across brood_0..5 (exact 0 at t=0 & t=3.60, C-inf periodic!)
    for i, bname in enumerate(BROOD_NAMES):
        phi = i * (TAU / 6.0) + 0.35
        ang = BROOD_THETA0[i]
        dr = 6.2 * (math.sin(th + phi) - math.sin(phi)) * 0.55 + 3.4 * math.sin(2.0 * th)
        dtan = 3.2 * (math.cos(th + phi) - math.cos(phi)) * 0.50
        bx = dr * math.cos(ang) - dtan * math.sin(ang)
        by = dr * math.sin(ang) + dtan * math.cos(ang)
        P.add(bname, x=bx, y=by)

    # Lazy floating figure-8 / micro-orbital bobbing of outer sat_0..5 (±14..20px phase-staggered, exact 0 at loop ends!)
    sat_amps = [16.5, 19.0, 15.0, 17.5, 20.0, 16.0]
    for k, sname in enumerate(SAT_NAMES):
        phi = k * (TAU / 6.0) + 0.52
        psi = k * 0.95 + 0.30
        amp = sat_amps[k]
        ang = SAT_THETA0[k]
        # Radial + figure-8 tangential Lissajous around perimeter slot (zero at th=0 & th=TAU, C-inf smooth wrap!)
        u_rad = 0.52 * (math.sin(th + phi) - math.sin(phi)) + 0.32 * math.sin(2.0 * th)
        u_tan = 0.50 * (math.cos(th + phi) - math.cos(phi)) + 0.36 * (math.sin(2.0 * th + psi) - math.sin(psi)) * 0.5
        sx = hub_dx + amp * (u_rad * math.cos(ang) - u_tan * math.sin(ang))
        sy = hub_dy + amp * (u_rad * math.sin(ang) + u_tan * math.cos(ang))
        P.add(sname, x=sx, y=sy)

    # Travelling golden bioluminescent heartbeat wave sweeping across all 12 yolks inside (0.18s .. 3.42s)
    for i in range(6):
        tc_b = 0.46 + i * 0.38
        f.glow[f"brood_glow_{i}"] = 0.44 * bump(t, tc_b - 0.38, tc_b, tc_b + 0.44)
        tc_s = 0.66 + i * 0.38
        f.glow[f"sat_glow_{i}"] = 0.28 * bump(t, tc_s - 0.36, tc_s, tc_s + 0.42)

    return f


def anim_multi_bite_6(t: float) -> Frame:
    """1.76s 6-hit centrifugal vortex spin-up + 6 sequential tangential whip-flings (-555..-640px left)."""
    f = Frame()
    P = f.P
    sa = {}
    dur = 1.76
    hit_times = [0.34, 0.54, 0.74, 0.94, 1.14, 1.34]
    dx_hits = [-575.0, -620.0, -555.0, -640.0, -590.0, -615.0]
    dy_lanes = [65.0, -45.0, 20.0, -70.0, 48.0, -15.0]

    # Cluster hub coils +28px right on spin-up + recoils +16px right on each of the 6 launches
    coil_env = hold(t, 0.0, 0.22, 1.38, 1.74)
    recoil = sum(16.0 * bump(t, th - 0.07, th + 0.01, th + 0.14) for th in hit_times)
    hub_dx = 28.0 * coil_env + recoil
    hub_dy = 10.0 * coil_env + sum(4.5 * ((-1) ** k) * bump(t, th - 0.05, th, th + 0.12) for k, th in enumerate(hit_times))
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    # Inner brood core tightens under centrifugal spin & shimmers gold on every launch pulse
    pulse = sum(bump(t, th - 0.06, th, th + 0.12) for th in hit_times)
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        tight = -0.10 * coil_env + 0.05 * min(1.0, pulse)
        P.add(bname, x=rx * tight, y=ry * tight)
        f.glow[f"brood_glow_{i}"] = clamp(0.32 * coil_env + 0.28 * pulse)
    f.glow["beckon_aura"] = clamp(0.24 * coil_env + 0.14 * pulse)

    for k in range(6):
        sdx, sdy, trot, tal, sgl = _vortex_or_fling(
            t, k, hub_dx, hub_dy, hit_times[k], dx_hits[k], dy_lanes[k], 1.56, dur
        )
        P.add(f"sat_{k}", x=sdx, y=sdy)
        if abs(trot) > 1e-4:
            P.add(f"trail_{k}_b", r=trot)
        sa[f"trail_{k}"] = tal
        f.glow[f"sat_glow_{k}"] = sgl

    f.extra["slot_alpha"] = sa
    return f


def anim_multi_bite_4_intangible(t: float) -> Frame:
    """1.76s 4 heavy whip-flings (0.34..1.06s) -> blinding pearlescent SoulFysh Intangible ascension @ 1.38s!"""
    f = Frame()
    P = f.P
    sa = {}
    dur = 1.76
    hit_times = [0.34, 0.58, 0.82, 1.06]
    dx_hits = [-580.0, -625.0, -565.0, -640.0]
    dy_lanes = [58.0, -48.0, 26.0, -64.0]

    # Phase 1 (0..1.16s): coil + 4 launch recoils; Phase 2 (1.14..1.76s): +26px weightless Intangible levitation!
    coil_env = hold(t, 0.0, 0.22, 1.04, 1.24)
    recoil = sum(17.0 * bump(t, th - 0.07, th + 0.01, th + 0.15) for th in hit_times)
    intang_env = hold(t, 1.12, 1.34, 1.44, 1.74)
    intang_peak = bump(t, 1.16, 1.38, 1.66)

    hub_dx = 26.0 * coil_env + recoil
    hub_dy = 8.0 * coil_env + 26.0 * intang_env
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    pulse = sum(bump(t, th - 0.06, th, th + 0.13) for th in hit_times)
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        rad_mod = -0.09 * coil_env + 0.09 * intang_env
        P.add(bname, x=rx * rad_mod, y=ry * rad_mod)
        f.glow[f"brood_glow_{i}"] = clamp(0.28 * coil_env + 0.24 * pulse + 0.96 * intang_env)
        sa[bname] = 1.0 - 0.16 * intang_peak

    for k in range(6):
        th_k = hit_times[k] if k < 4 else None
        dx_k = dx_hits[k] if k < 4 else 0.0
        dy_k = dy_lanes[k] if k < 4 else 0.0
        sdx, sdy, trot, tal, sgl = _vortex_or_fling(t, k, hub_dx, hub_dy, th_k, dx_k, dy_k, 1.22, dur)
        # Add buoyant radial separation during Intangible peak (1.14..1.74s)
        rx0, ry0 = SAT_REL[f"sat_{k}"]
        sdx += rx0 * (0.08 * intang_env)
        sdy += ry0 * (0.08 * intang_env)
        P.add(f"sat_{k}", x=sdx, y=sdy)
        if abs(trot) > 1e-4:
            P.add(f"trail_{k}_b", r=trot)
        sa[f"trail_{k}"] = tal
        f.glow[f"sat_glow_{k}"] = clamp(max(sgl, 1.0 * intang_env))
        # Dip outer orb alpha to 0.72 at 1.38s so luminous moonstone cores shine through!
        sa[f"sat_{k}"] = 1.0 - 0.28 * intang_peak

    # Blinding pearlescent aqua-white-gold SoulFysh spectral radiance + cool moonstone membrane shift!
    f.tint = (1.0 - 0.16 * intang_peak, 1.0, 1.0, 1.0)
    f.glow["intangible_aura"] = clamp(1.0 * intang_env)
    f.glow["intangible_shimmer"] = clamp(1.0 * intang_env)
    _set_ring_scale(P, 1.0 + (-0.14 * bump(t, 1.08, 1.18, 1.30) + 0.26 * ease(seg(t, 1.18, 1.62))) * hold(t, 1.08, 1.16, 1.60, 1.75))

    f.extra["slot_alpha"] = sa
    return f


def anim_attack(t: float) -> Frame:
    """1.04s single-beat 3-orb (sat_0, sat_2, sat_4) synchronized whip-fling volley (-560..-590px left @ 0.42s)."""
    f = Frame()
    P = f.P
    sa = {}
    dur = 1.04
    t_hit = 0.42

    coil_env = hold(t, 0.0, 0.22, 0.46, 0.96)
    recoil = 22.0 * bump(t, 0.34, 0.43, 0.62)
    hub_dx = 26.0 * coil_env + recoil
    hub_dy = 9.0 * coil_env
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    hit_pulse = bump(t, 0.34, 0.42, 0.58)
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        P.add(bname, x=rx * (-0.09 * coil_env), y=ry * (-0.09 * coil_env))
        f.glow[f"brood_glow_{i}"] = clamp(0.30 * coil_env + 0.35 * hit_pulse)

    volley_spec = {
        0: (-575.0, 62.0),
        2: (-560.0, -54.0),
        4: (-590.0, 8.0),
    }
    for k in range(6):
        if k in volley_spec:
            dx_k, dy_k = volley_spec[k]
            sdx, sdy, trot, tal, sgl = _vortex_or_fling(t, k, hub_dx, hub_dy, t_hit, dx_k, dy_k, 0.88, dur)
        else:
            sdx, sdy, trot, tal, sgl = _vortex_or_fling(t, k, hub_dx, hub_dy, None, 0.0, 0.0, 0.90, dur)
        P.add(f"sat_{k}", x=sdx, y=sdy)
        if abs(trot) > 1e-4:
            P.add(f"trail_{k}_b", r=trot)
        sa[f"trail_{k}"] = tal
        f.glow[f"sat_glow_{k}"] = sgl

    f.extra["slot_alpha"] = sa
    return f


def anim_cast(t: float) -> Frame:
    """1.28s Beckon + Strength ('召唤改成鱼籽变亮'): 0.84x inward gather -> cascading 12-egg sun-gold ignition @ 0.58s!"""
    f = Frame()
    P = f.P
    gather = bump(t, 0.0, 0.26, 0.46)
    swell = hold(t, 0.32, 0.56, 0.78, 1.24)
    peak = bump(t, 0.36, 0.58, 0.98)

    hub_dy = -9.0 * gather + 24.0 * swell
    P.add("cluster_hub", y=hub_dy)

    # Radial 0.84x gather (0.00..0.28s) -> 1.13x Strength swell at 0.58s!
    rad_scale = -0.16 * gather + 0.13 * swell

    # Cascading center-to-rim warm golden-amber bioluminescent ignition
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        P.add(bname, x=rx * rad_scale, y=ry * rad_scale)
        stagger = i * 0.024
        f.glow[f"brood_glow_{i}"] = clamp(hold(t, 0.24 + stagger, 0.48 + stagger, 0.78, 1.22))

    for k, sname in enumerate(SAT_NAMES):
        rx, ry = SAT_REL[sname]
        P.add(sname, x=rx * rad_scale, y=hub_dy + ry * rad_scale)
        stagger = k * 0.026
        f.glow[f"sat_glow_{k}"] = clamp(0.96 * hold(t, 0.30 + stagger, 0.54 + stagger, 0.78, 1.22))

    f.glow["beckon_aura"] = clamp(max(peak, hold(t, 0.32, 0.56, 0.72, 1.18)))
    ring_vis = bump(t, 0.28, 0.58, 1.06)
    f.glow["beckon_ring"] = clamp(0.98 * ring_vis)
    ring_s = 1.0 + (-0.22 + 0.64 * ease_out(seg(t, 0.28, 1.04))) * hold(t, 0.22, 0.28, 1.04, 1.24)
    _set_ring_scale(P, ring_s)

    return f


def anim_power_up(t: float) -> Frame:
    """1.16s Full 12-egg pearlescent white-gold Intangible illumination peaking @ 0.52s (+24px levitation)."""
    f = Frame()
    P = f.P
    sa = {}
    gather = bump(t, 0.0, 0.20, 0.36)
    lev = hold(t, 0.22, 0.50, 0.70, 1.12)
    peak = bump(t, 0.26, 0.52, 0.92)

    hub_dy = -6.0 * gather + 24.0 * lev
    P.add("cluster_hub", y=hub_dy)
    rad_scale = -0.10 * gather + 0.10 * lev

    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        P.add(bname, x=rx * rad_scale, y=ry * rad_scale)
        f.glow[f"brood_glow_{i}"] = clamp(0.96 * lev)
        sa[bname] = 1.0 - 0.15 * peak

    for k, sname in enumerate(SAT_NAMES):
        rx, ry = SAT_REL[sname]
        P.add(sname, x=rx * rad_scale, y=hub_dy + ry * rad_scale)
        f.glow[f"sat_glow_{k}"] = clamp(1.0 * lev)
        sa[sname] = 1.0 - 0.26 * peak

    f.tint = (1.0 - 0.16 * peak, 1.0, 1.0, 1.0)
    f.glow["intangible_aura"] = clamp(max(peak, lev))
    f.glow["intangible_shimmer"] = clamp(max(peak, lev))
    ring_s = 1.0 + (-0.16 + 0.46 * ease_out(seg(t, 0.22, 0.96))) * hold(t, 0.18, 0.24, 0.96, 1.14)
    _set_ring_scale(P, ring_s)

    f.extra["slot_alpha"] = sa
    return f


def anim_summon(t: float) -> Frame:
    """1.26s Mitotic brood division: pressurize -> sat_1 & sat_4 bud ±148px left/right @ 0.64s -> cohesive reform!"""
    f = Frame()
    P = f.P
    press = hold(t, 0.0, 0.28, 0.42, 0.68)
    bud = hold(t, 0.32, 0.62, 0.72, 1.20)
    flash = bump(t, 0.48, 0.64, 0.92)
    quiver = math.sin(TAU * 22.0 * t) * bump(t, 0.06, 0.30, 0.56)

    hub_dy = -8.0 * press + 14.0 * bud
    P.add("cluster_hub", x=3.5 * quiver, y=hub_dy)

    # Inner brood stretches horizontally during mitotic cleavage, then snaps elastically back into 12-orb core
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        mx = rx * (0.16 * press + 0.20 * bump(t, 0.34, 0.58, 0.88))
        my = ry * (-0.12 * press + 0.06 * bud)
        P.add(bname, x=mx, y=my)
        f.glow[f"brood_glow_{i}"] = clamp(0.65 * press + 0.85 * flash)

    for k, sname in enumerate(SAT_NAMES):
        rx, ry = SAT_REL[sname]
        if k == 1:
            # Daughter roe budding -148px LEFT!
            bx = -148.0 * bud - 12.0 * press + 4.0 * quiver
            by = hub_dy + 14.0 * bud
            P.add(sname, x=bx, y=by)
            f.glow[f"sat_glow_{k}"] = clamp(0.75 * press + 1.0 * bud)
        elif k == 4:
            # Daughter roe budding +148px RIGHT!
            bx = 148.0 * bud + 12.0 * press - 4.0 * quiver
            by = hub_dy + 14.0 * bud
            P.add(sname, x=bx, y=by)
            f.glow[f"sat_glow_{k}"] = clamp(0.75 * press + 1.0 * bud)
        else:
            P.add(sname, x=rx * (-0.10 * press + 0.08 * flash), y=hub_dy + ry * (-0.08 * press + 0.08 * flash))
            f.glow[f"sat_glow_{k}"] = clamp(0.45 * press + 0.72 * flash)

    f.glow["beckon_aura"] = clamp(0.68 * press + 0.92 * flash)
    f.glow["beckon_ring"] = clamp(0.95 * bump(t, 0.50, 0.64, 1.02))
    _set_ring_scale(P, 1.0 + (-0.14 + 0.50 * ease_out(seg(t, 0.48, 1.02))) * hold(t, 0.44, 0.50, 1.02, 1.22))

    return f


def anim_hurt(t: float) -> Frame:
    """0.46s +36px right hydro-cluster jolt + +22..34px staggered elastic surface-tension scatter & snap-back."""
    f = Frame()
    P = f.P
    dur = 0.46
    fade_end = 1.0 - ease(seg(t, 0.34, dur))

    jolt = bump(t, 0.0, 0.075, 0.32) + 0.28 * kick(t, 0.075, freq=5.2, damp=9.5) * fade_end
    hub_dx = 36.0 * jolt
    hub_dy = -8.0 * bump(t, 0.0, 0.07, 0.26)
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    shock_glow = bump(t, 0.0, 0.06, 0.24)
    brood_amp = [24.0, 27.0, 22.0, 28.0, 25.0, 26.0]
    for i, bname in enumerate(BROOD_NAMES):
        ang = BROOD_THETA0[i]
        t0 = 0.012 * (i % 3)
        spr = (bump(t, t0, t0 + 0.085, 0.28) + 0.36 * kick(t, t0 + 0.08, freq=6.4, damp=9.0)) * fade_end
        dr = brood_amp[i] * spr
        P.add(bname, x=dr * math.cos(ang), y=dr * math.sin(ang))
        f.glow[f"brood_glow_{i}"] = clamp(0.48 * shock_glow)

    sat_amp = [31.0, 34.0, 29.0, 32.0, 34.0, 30.0]
    for k, sname in enumerate(SAT_NAMES):
        ang = SAT_THETA0[k]
        t0 = 0.014 * (k % 3)
        spr = (bump(t, t0, t0 + 0.09, 0.30) + 0.40 * kick(t, t0 + 0.085, freq=5.8, damp=8.5)) * fade_end
        dr = sat_amp[k] * spr
        P.add(sname, x=hub_dx + dr * math.cos(ang), y=hub_dy + dr * math.sin(ang))
        f.glow[f"sat_glow_{k}"] = clamp(0.52 * shock_glow)

    return f


def anim_die(t: float) -> Frame:
    """1.12s Rupture Burst ('鱼子爆发'): 0..0.38s violent over-pressurization flare -> 0.42s 6-way radial starburst!"""
    f = Frame()
    P = f.P
    sa = {}

    # 0.00..0.38s: High-frequency over-pressurization + blinding golden-white core flare
    tremor_env = bump(t, 0.02, 0.34, 0.43)
    tremor_x = 9.0 * tremor_env * math.sin(TAU * 28.0 * t)
    tremor_y = 7.0 * tremor_env * math.cos(TAU * 33.0 * t)

    burst_u = ease_out(seg(t, 0.39, 1.12))
    swell_pre = -0.14 * bump(t, 0.0, 0.18, 0.32) + 0.18 * bump(t, 0.22, 0.39, 0.50)

    hub_dx = tremor_x
    hub_dy = tremor_y + 14.0 * bump(t, 0.16, 0.40, 0.78)
    P.add("cluster_hub", x=hub_dx, y=hub_dy)

    # Dissolve tint alpha cleanly to 0.0 by 1.12s as 6 newborn ThingsSoulRoe materialize!
    dissolve = 1.0 - ease_in(seg(t, 0.48, 1.12))
    f.tint = (1.0, 1.0, 1.0, dissolve)
    sa["shadow"] = 0.42 * (1.0 - ease(seg(t, 0.38, 0.90)))

    flare_env = bump(t, 0.06, 0.40, 0.96)
    f.glow["beckon_aura"] = clamp(1.0 * bump(t, 0.08, 0.39, 0.88))
    f.glow["intangible_aura"] = clamp(1.0 * bump(t, 0.14, 0.42, 0.92))
    f.glow["beckon_ring"] = clamp(1.0 * bump(t, 0.36, 0.44, 0.98))
    f.glow["intangible_shimmer"] = clamp(0.95 * bump(t, 0.36, 0.46, 1.00))
    _set_ring_scale(P, 1.0 - 0.18 * seg(t, 0.0, 0.36) + 0.66 * ease_out(seg(t, 0.38, 1.12)))

    brood_dist = [92.0, 108.0, 78.0, 116.0, 104.0, 96.0]
    for i, bname in enumerate(BROOD_NAMES):
        rx, ry = BROOD_REL[bname]
        ang = BROOD_THETA0[i] + 0.18 * ((-1) ** i)
        bx = rx * swell_pre + brood_dist[i] * burst_u * math.cos(ang)
        by = ry * swell_pre + brood_dist[i] * burst_u * math.sin(ang)
        P.add(bname, x=bx, y=by)
        f.glow[f"brood_glow_{i}"] = clamp(1.0 * flare_env)

    # 6-way radial starburst matching the surrounding encounter ring! (225..275px outward)
    ray_deg = [146.0, 194.0, 238.0, 310.0, 352.0, 44.0]
    ray_dist = [245.0, 275.0, 230.0, 238.0, 272.0, 248.0]
    for k, sname in enumerate(SAT_NAMES):
        rx, ry = SAT_REL[sname]
        ang = math.radians(ray_deg[k])
        # Tail streams inward opposite to outward ray_deg: wrap(ray_deg + 180) in [-180, +180]
        tail_deg = ((ray_deg[k] + 180.0 + 180.0) % 360.0) - 180.0
        sx = hub_dx + rx * swell_pre + ray_dist[k] * burst_u * math.cos(ang)
        sy = hub_dy + ry * swell_pre + ray_dist[k] * burst_u * math.sin(ang)
        P.add(sname, x=sx, y=sy)
        # Pre-orient trail_k_b invisibly during 0.14..0.35s while trail_k alpha == 0.0!
        P.add(f"trail_{k}_b", r=tail_deg * ease(seg(t, 0.14, 0.35)))
        sa[f"trail_{k}"] = 0.96 * bump(t, 0.38, 0.46, 1.04)
        f.glow[f"sat_glow_{k}"] = clamp(1.0 * bump(t, 0.08, 0.42, 1.02))

    f.extra["slot_alpha"] = sa
    return f


def anim_revive(t: float) -> Frame:
    """0.86s exact smooth time-reversal of die(1.12) -> die(0.00) (reviveErr == 0.0000)."""
    return anim_die(1.12 * (1.0 - ease(seg(t, 0.0, 0.86))))


FUNCS = {
    "idle_loop": anim_idle_loop,
    "multi_bite_6": anim_multi_bite_6,
    "multi_bite_4_intangible": anim_multi_bite_4_intangible,
    "attack": anim_attack,
    "cast": anim_cast,
    "power_up": anim_power_up,
    "summon": anim_summon,
    "hurt": anim_hurt,
    "die": anim_die,
    "revive": anim_revive,
}
