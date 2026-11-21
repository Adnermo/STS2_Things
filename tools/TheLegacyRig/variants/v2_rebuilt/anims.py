"""Visceral Hydrostatic Cardiac Biomechanics for The Legacy (腐化之遗) — v2_rebuilt.

Coordinate & Rotation Sign Cheat-Sheet in Spine Space S(p) = (x - 679, 660 - y):
  - X: dx < 0 = surge LEFT toward player; dx > 0 = coil/recoil RIGHT.
  - Y: dy > 0 = swell UPWARD away from stone dock; dy < 0 = slam/deflate DOWNWARD toward stone dock.
  - Rotation r (CCW positive):
      * LEFT-pointing bones (cannon_upper, cannon_lower, apex_upper, apex_lower):
          r < 0 pitches/curls UPWARD; r > 0 aims/droops DOWNWARD.
      * RIGHT-pointing bones (vena_up, vena_down, drain_a, drain_b):
          r > 0 arches UPWARD; r < 0 droops DOWNWARD.
      * UP-LEFT dorsal pipes (aorta_crown, pipe_pl, pipe_pr):
          r < 0 rears UPWARD/RIGHT; r > 0 whips/droops LEFTWARD.
"""
import math

TAU = 2.0 * math.pi

ANIMS = {
    'idle_loop': (3.00, True),
    'attack': (1.25, False),
    'cast': (1.40, False),
    'hurt': (0.65, False),
    'die': (2.60, False),
    'summon': (1.30, False),
    'power_up': (1.50, False),
    'revive': (1.80, False),
}

LEFT_ORIFICES = ('orifice_ta', 'orifice_tb')
TOP_ORIFICES = ('orifice_pl', 'orifice_pr')
RIGHT_ORIFICES = ('orifice_pa', 'orifice_pb', 'orifice_bu', 'orifice_bd', 'orifice_bb', 'orifice_bm')
ALL_ORIFICES = LEFT_ORIFICES + TOP_ORIFICES + RIGHT_ORIFICES


def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def seg(t, t0, t1):
    return clamp((t - t0) / (t1 - t0)) if t1 > t0 else float(t >= t0)


def ease(x):
    x = clamp(x)
    return x * x * (3.0 - 2.0 * x)


def ease_out(x):
    x = clamp(x)
    return 1.0 - (1.0 - x) ** 3


def ease_in(x):
    x = clamp(x)
    return x ** 3


def bump(t, t0, t1, t2):
    """Smooth C1 bell pulse: 0 at t<=t0, 1 at t=t1, 0 at t>=t2."""
    if t <= t0 or t >= t2:
        return 0.0
    if t < t1:
        return ease((t - t0) / (t1 - t0))
    return 1.0 - ease((t - t1) / (t2 - t1))


def hold(t, t0, t1, t2, t3):
    """Smooth plateau: 0->1 over [t0,t1], 1 over [t1,t2], 1->0 over [t2,t3]."""
    if t <= t0 or t >= t3:
        return 0.0
    return ease(seg(t, t0, t1)) * (1.0 - ease(seg(t, t2, t3)))


def kick(t, t0, freq=2.6, damp=4.5, end_t=None):
    """Windowed damped sine starting cleanly at 0 at t=t0."""
    if t <= t0:
        return 0.0
    x = t - t0
    val = math.exp(-damp * x) * math.sin(TAU * freq * x)
    if end_t is not None:
        val *= 1.0 - ease(seg(t, end_t - 0.16, end_t))
    return val


def pbeat(t, period, center, rise=0.10, fall=0.30):
    """Strictly periodic cardiac pulse centered at `center` with period `period`."""
    u = (t - center) % period
    if u > period - rise:
        return ease((u - (period - rise)) / rise)
    if u < fall:
        return 1.0 - ease(u / fall)
    return 0.0


class Pose(dict):
    def add(self, b, r=0.0, x=0.0, y=0.0, sx=0.0, sy=0.0, s=0.0):
        R, X, Y, SX, SY = self.get(b, (0.0, 0.0, 0.0, 1.0, 1.0))
        self[b] = (R + r, X + x, Y + y, SX * (1.0 + sx + s), SY * (1.0 + sy + s))

    def blend_from(self, other, w):
        if w <= 1e-6:
            return
        for b, (r, x, y, sx, sy) in other.items():
            self.add(b, r=r * w, x=x * w, y=y * w, sx=(sx - 1.0) * w, sy=(sy - 1.0) * w)


class Frame:
    def __init__(self):
        self.P = Pose()
        self.glow = {'glow_sa_root': 0.03, 'glow_av_bulb': 0.03, 'glow_purkinje_apex': 0.03}
        self.tint = (1.0, 1.0, 1.0, 1.0)
        self.bias = {}      # chain_name -> per-link angle bias in degrees
        self.collapse = 0.0


def set_orifices(P: Pose, names, scale_delta=0.0, sx=0.0, sy=0.0, r=0.0, x=0.0, y=0.0):
    for ob in names:
        P.add(ob, r=r, x=x, y=y, sx=sx, sy=sy, s=scale_delta)


# ======================================================================
# 1. IDLE_LOOP (3.00s — Two 1.50s Visceral Lub-Dub Cardiac Cycles)
# ======================================================================
def idle_loop(t: float, amp: float = 1.0) -> Frame:
    f = Frame()
    P = f.P
    ph = TAU * t / 3.0
    # Slight organic breath modulation between beat 1 and beat 2, strictly 3.0s periodic
    swell_mod = 1.0 + 0.10 * math.sin(ph - 0.6)
    ocean = math.sin(ph)

    # Three-phase cardiac cycle (period = 1.50s):
    #   1) Venous suction & dorsal engorgement (center 0.12s)
    #   2) Atrial snap "LUB" (center 0.25s) -> SA glow
    #   3) Ventricular wring "DUB" (+0.17s -> center 0.42s) -> AV & Purkinje glow + arterial flare!
    suck = pbeat(t, 1.50, 0.12, rise=0.11, fall=0.22) * amp * swell_mod
    lub = pbeat(t, 1.50, 0.25, rise=0.08, fall=0.20) * amp * swell_mod
    dub = pbeat(t, 1.50, 0.42, rise=0.08, fall=0.30) * amp * swell_mod
    purk = pbeat(t, 1.50, 0.51, rise=0.09, fall=0.34) * amp * swell_mod
    slump = pbeat(t, 1.50, 0.72, rise=0.16, fall=0.42) * amp * swell_mod

    # 1) Right venous & dorsal intake suction -> dorsal arch engorgement
    P.add('venous_sinus', x=6.0 * suck - 7.0 * dub, y=9.0 * suck - 6.0 * dub + 2.0 * ocean,
          sx=0.045 * suck - 0.030 * dub, sy=0.050 * suck - 0.035 * dub, r=1.4 * suck - 1.8 * dub)
    P.add('vena_up', r=4.2 * suck - 5.0 * dub + 1.5 * math.sin(ph + 0.8), y=3.5 * suck - 2.5 * dub)
    P.add('vena_down', r=3.6 * suck - 4.2 * dub + 1.3 * math.sin(ph + 1.7), x=3.0 * suck - 2.0 * dub)
    set_orifices(P, RIGHT_ORIFICES, scale_delta=-0.11 * suck + 0.07 * slump)

    P.add('dorsal_arch', x=4.0 * suck - 6.0 * dub, y=11.0 * (0.65 * suck + 0.55 * lub) - 10.0 * dub + 2.5 * ocean,
          sx=0.035 * suck + 0.030 * dub, sy=0.055 * (0.6 * suck + 0.5 * lub) - 0.055 * dub, r=-1.8 * suck + 2.2 * dub)
    P.add('aorta_crown', y=5.0 * lub - 4.0 * dub, r=-3.2 * lub + 4.8 * dub + 1.4 * math.sin(ph + 1.2))
    P.add('pipe_pl', r=-3.5 * lub + 6.5 * purk + 2.0 * math.sin(ph + 2.0))
    P.add('pipe_pr', r=3.0 * lub - 6.0 * purk - 1.8 * math.sin(ph + 2.7))
    P.add('dorsal_drain', x=5.0 * suck - 4.0 * dub, y=5.0 * suck - 4.0 * dub, s=0.035 * suck - 0.02 * dub)
    P.add('drain_a', r=3.5 * suck - 4.2 * dub + 1.4 * math.sin(ph + 0.4))
    P.add('drain_b', r=2.8 * suck - 3.2 * dub - 1.2 * math.sin(ph + 1.5))

    # 2) Atrial Snap ("LUB") -> pushes blood into central vent_dome!
    P.add('conus_atrium',
          x=9.0 * lub - 15.0 * dub + 4.0 * slump,
          y=-9.0 * lub + 12.0 * dub - 5.0 * slump + 2.0 * ocean,
          sx=-0.050 * lub + 0.065 * dub,
          sy=-0.055 * lub + 0.050 * dub - 0.025 * slump,
          r=2.5 * lub - 3.8 * dub)

    # 3) Ventricular Wring ("DUB"): vent_dome twists (-4.0 deg) & squeezes down while lower lobes bulge wide!
    P.add('vent_dome',
          x=4.0 * lub - 11.0 * dub + 3.5 * slump,
          y=13.0 * lub - 16.0 * dub - 4.0 * slump + 2.5 * ocean,
          sx=0.045 * lub - 0.060 * dub + 0.030 * slump,
          sy=0.065 * lub - 0.085 * dub - 0.020 * slump,
          r=-4.0 * dub + 1.4 * lub)

    # Hydrostatic lower-wall expansion onto the locked stone dock + organic upward apex curl (r < 0 = UP!)
    P.add('vent_mid',
          x=-14.0 * dub + 4.5 * lub + 3.0 * slump,
          y=-5.0 * dub + 3.0 * lub,
          sx=0.065 * dub - 0.020 * lub,
          sy=-0.040 * dub + 0.025 * lub)
    P.add('apex_upper',
          x=-9.0 * purk + 3.0 * lub,
          y=5.0 * purk - 2.0 * slump,
          r=-5.8 * purk + 2.2 * slump + 1.0 * math.sin(ph + 1.9),
          s=0.035 * purk)

    P.add('vent_low',
          x=-8.0 * purk + 2.5 * lub,
          y=-3.5 * dub,
          sx=0.060 * dub + 0.025 * purk,
          sy=-0.045 * dub)
    P.add('apex_lower',
          x=-8.0 * purk + 2.5 * lub,
          y=3.5 * purk,
          r=-4.8 * purk + 1.8 * slump + 0.8 * math.sin(ph + 2.5),
          s=0.030 * purk)

    P.add('vent_right',
          x=11.0 * dub - 3.5 * suck,
          y=-4.0 * dub + 3.0 * lub,
          sx=0.060 * dub,
          sy=-0.035 * dub + 0.025 * lub)

    # Anterior & dorsal cannon ejection flare (1.17x) + pressure whip!
    P.add('cannon_upper',
          x=-10.0 * dub + 4.0 * lub + 3.0 * slump,
          r=-6.2 * purk + 2.6 * lub + 1.3 * math.sin(ph + 0.5),
          sx=0.04 * dub, sy=0.03 * purk)
    P.add('cannon_lower',
          x=-9.0 * dub + 3.5 * lub + 2.5 * slump,
          r=5.0 * purk - 2.2 * lub + 1.1 * math.sin(ph + 1.4),
          sx=0.04 * dub, sy=0.03 * purk)
    set_orifices(P, LEFT_ORIFICES, scale_delta=-0.08 * lub + 0.175 * purk)
    set_orifices(P, TOP_ORIFICES, scale_delta=-0.06 * suck + 0.160 * purk)

    # 3-Zone Traveling Coronary Electrical Impulse (SA -> AV -> Purkinje!)
    g_sa = pbeat(t, 1.50, 0.21, rise=0.06, fall=0.19) * amp
    g_av = pbeat(t, 1.50, 0.35, rise=0.07, fall=0.22) * amp
    g_pk = pbeat(t, 1.50, 0.48, rise=0.08, fall=0.26) * amp
    f.glow = {
        'glow_sa_root': clamp(0.03 + 0.94 * g_sa + 0.08 * g_av),
        'glow_av_bulb': clamp(0.03 + 0.95 * g_av + 0.10 * g_sa),
        'glow_purkinje_apex': clamp(0.03 + 0.97 * g_pk + 0.10 * g_av),
    }

    # Periodic underwater current target for flora chains
    f.bias['__ocean_t'] = t
    return f


# Baseline pose at idle_loop(0.0) so all one-shots start & end cleanly at idle_loop(0.0)!
IDLE0 = idle_loop(0.0)


def _envelope_idle0(f: Frame, t: float, dur: float, fade_in: float = 0.06, fade_out: float = 0.22) -> Frame:
    """Blend smoothly out of IDLE0 at t=0 and back into exact IDLE0 at t=dur."""
    w_act = ease(seg(t, 0.0, fade_in)) * (1.0 - ease(seg(t, dur - fade_out, dur)))
    out = Frame()
    for b, (r0, x0, y0, sx0, sy0) in IDLE0.P.items():
        out.P[b] = (r0, x0, y0, sx0, sy0)
    for b, (r, x, y, sx, sy) in f.P.items():
        out.P.add(b, r=r * w_act, x=x * w_act, y=y * w_act, sx=(sx - 1.0) * w_act, sy=(sy - 1.0) * w_act)
    for gk in IDLE0.glow:
        out.glow[gk] = clamp(IDLE0.glow[gk] * (1.0 - w_act) + f.glow.get(gk, 0.03) * w_act)
    out.tint = tuple(IDLE0.tint[i] * (1.0 - w_act) + f.tint[i] * w_act for i in range(4))
    for bk, bv in f.bias.items():
        out.bias[bk] = bv * w_act if isinstance(bv, (int, float)) else bv
    out.bias['__ocean_t'] = 0.0
    return out


# ======================================================================
# 2. ATTACK (1.25s — Grounded Peristaltic Coil & Dual-Cannon Arterial Blast)
# ======================================================================
def attack(t: float) -> Frame:
    f = Frame()
    P = f.P

    # Phase 1: 0.00..0.29s Vacuum Aneurysm Coil (dorsal/dome swell high, anterior coils right, cannons purse 0.85x)
    coil = bump(t, 0.00, 0.25, 0.36)
    # Phase 2: 0.29..0.41s Peristaltic Slam & Dual-Cannon Detonation (hit peak at t=0.39s!)
    slam_dorsal = bump(t, 0.27, 0.36, 0.66)
    blast = bump(t, 0.29, 0.39, 0.72)
    flare = bump(t, 0.31, 0.39, 0.58)
    # Phase 3: 0.41..1.25s Wet Hydrostatic Slosh & Venous Refill Gasp
    slosh = kick(t, 0.41, freq=1.85, damp=4.2, end_t=1.22)
    refill = bump(t, 0.48, 0.66, 1.05)

    # Dorsal & central chambers swell tall (+14% dome height) on coil, then SLAM DOWN to drive blood left!
    P.add('dorsal_arch',
          x=22.0 * coil - 26.0 * slam_dorsal + 8.0 * slosh,
          y=26.0 * coil - 24.0 * slam_dorsal - 5.0 * slosh,
          sx=0.06 * coil + 0.05 * slam_dorsal,
          sy=0.13 * coil - 0.11 * slam_dorsal + 0.03 * slosh,
          r=-5.5 * coil + 6.5 * slam_dorsal)
    P.add('aorta_crown', y=12.0 * coil - 10.0 * slam_dorsal, r=-7.0 * coil + 10.0 * blast - 5.0 * slosh)
    P.add('pipe_pl', r=-6.0 * coil + 11.0 * blast - 7.0 * slosh)
    P.add('pipe_pr', r=5.5 * coil - 9.5 * blast + 6.0 * slosh)
    set_orifices(P, TOP_ORIFICES, scale_delta=-0.12 * coil + 0.18 * flare)

    P.add('venous_sinus',
          x=18.0 * coil - 16.0 * slam_dorsal + 8.0 * refill,
          y=16.0 * coil - 14.0 * slam_dorsal + 9.0 * refill,
          sx=0.08 * coil - 0.05 * slam_dorsal + 0.05 * refill,
          sy=0.09 * coil - 0.07 * slam_dorsal + 0.05 * refill)
    P.add('vena_up', r=7.0 * coil - 9.0 * slam_dorsal + 6.0 * refill)
    P.add('vena_down', r=6.0 * coil - 8.0 * slam_dorsal + 5.0 * refill)
    P.add('dorsal_drain', x=12.0 * coil - 10.0 * slam_dorsal + 6.0 * refill, y=8.0 * coil - 8.0 * slam_dorsal)
    P.add('drain_a', r=5.0 * coil - 7.0 * slam_dorsal + 4.5 * refill)
    P.add('drain_b', r=4.0 * coil - 5.5 * slam_dorsal + 3.5 * refill)
    set_orifices(P, RIGHT_ORIFICES, scale_delta=-0.14 * coil + 0.16 * refill)

    P.add('vent_dome',
          x=24.0 * coil - 32.0 * slam_dorsal + 11.0 * slosh,
          y=28.0 * coil - 26.0 * slam_dorsal - 6.0 * slosh,
          sx=0.05 * coil + 0.09 * slam_dorsal - 0.03 * slosh,
          sy=0.14 * coil - 0.13 * slam_dorsal + 0.04 * slosh,
          r=-5.0 * coil + 7.2 * slam_dorsal - 2.5 * slosh)
    P.add('vent_right',
          x=16.0 * coil - 10.0 * slam_dorsal + 9.0 * refill,
          y=8.0 * coil - 10.0 * slam_dorsal,
          sx=0.05 * coil + 0.06 * slam_dorsal,
          sy=0.05 * coil - 0.06 * slam_dorsal)

    # Anterior left chambers coil back-right (+25px) along dock, then WHIP forward-left (-50px at cannon tips!)
    P.add('conus_atrium',
          x=25.0 * coil - 34.0 * blast + 12.0 * slosh,
          y=-10.0 * coil + 14.0 * blast - 5.0 * slosh,
          sx=-0.05 * coil + 0.09 * blast,
          sy=-0.06 * coil + 0.06 * blast,
          r=4.5 * coil - 5.5 * blast + 2.2 * slosh)
    P.add('cannon_upper',
          x=8.0 * coil - 18.0 * blast + 6.0 * slosh,
          y=-3.0 * coil + 4.0 * blast,
          r=6.5 * coil - 9.5 * blast + 6.0 * kick(t, 0.40, 2.5, 4.8, 1.20),
          sx=0.06 * blast, sy=0.05 * flare)
    P.add('cannon_lower',
          x=8.0 * coil - 18.0 * blast + 6.0 * slosh,
          y=2.0 * coil - 3.0 * blast,
          r=-5.5 * coil + 8.0 * blast - 5.5 * kick(t, 0.41, 2.5, 4.8, 1.20),
          sx=0.06 * blast, sy=0.05 * flare)
    # Dual-cannon mouths purse narrow (0.85x) on coil, FLARE WIDE OPEN (1.25x) at t=0.39s!
    set_orifices(P, LEFT_ORIFICES,
                 scale_delta=-0.15 * coil + 0.255 * flare,
                 x=-4.0 * flare)

    P.add('vent_mid',
          x=24.0 * coil - 28.0 * blast + 10.0 * slosh,
          y=-8.0 * coil + 4.0 * blast,
          sx=-0.04 * coil + 0.09 * blast,
          sy=-0.05 * coil - 0.03 * blast)
    P.add('apex_upper',
          x=6.0 * coil - 12.0 * blast + 5.0 * slosh,
          y=-3.0 * coil + 8.0 * blast,
          r=5.0 * coil - 8.5 * blast + 4.5 * slosh,
          s=0.05 * blast)
    P.add('vent_low',
          x=14.0 * coil - 18.0 * blast + 6.0 * slosh,
          y=-3.0 * coil - 3.0 * blast,
          sx=0.03 * coil + 0.08 * blast,
          sy=-0.04 * coil - 0.04 * blast)
    P.add('apex_lower',
          x=8.0 * coil - 14.0 * blast + 5.0 * slosh,
          y=4.0 * blast,
          r=3.5 * coil - 6.0 * blast + 3.0 * slosh,
          s=0.04 * blast)

    # Traveling intense crimson charge -> Purkinje cannon detonation at t=0.39s!
    f.glow = {
        'glow_sa_root': clamp(0.08 + 0.92 * bump(t, 0.04, 0.22, 0.46)),
        'glow_av_bulb': clamp(0.08 + 0.95 * bump(t, 0.14, 0.31, 0.54)),
        'glow_purkinje_apex': clamp(0.08 + 0.35 * coil + 1.00 * bump(t, 0.28, 0.39, 0.68)),
    }
    f.bias['__shock'] = -9.0 * coil + 14.0 * blast
    return _envelope_idle0(f, t, 1.25, fade_in=0.05, fade_out=0.20)


# ======================================================================
# 3. CAST (1.40s — Tachycardia Fibrillation & All-Orifice Miasma Geyser)
# ======================================================================
def cast(t: float) -> Frame:
    f = Frame()
    P = f.P

    # Three accelerating asymmetric fibrillating spasms (0.06s, 0.18s, 0.29s)
    sp1 = bump(t, 0.01, 0.065, 0.14)
    sp2 = bump(t, 0.11, 0.185, 0.26)
    sp3 = bump(t, 0.22, 0.295, 0.37)
    fib_lr = sp1 - 1.15 * sp2 + 1.25 * sp3
    fib_env = sp1 + sp2 + sp3

    # 0.36..0.64s Belly flattens wide onto dock while ALL vessel pipes arch skyward & flare 1.23x!
    geyser = hold(t, 0.33, 0.41, 0.50, 0.80)
    pop = bump(t, 0.35, 0.42, 0.68)
    slump = bump(t, 0.72, 0.92, 1.26)

    # Lower belly flattens wide & heavy onto stone floor to pressurize upward geyser
    P.add('vent_low',
          x=12.0 * fib_lr,
          y=-6.0 * geyser - 3.0 * slump,
          sx=0.10 * geyser + 0.04 * fib_env,
          sy=-0.08 * geyser)
    P.add('apex_lower', x=-12.0 * geyser + 8.0 * fib_lr, r=-5.0 * geyser + 4.0 * fib_lr)
    P.add('vent_mid',
          x=-16.0 * geyser + 16.0 * fib_lr,
          y=-8.0 * geyser + 6.0 * sp2 - 4.0 * slump,
          sx=0.09 * geyser,
          sy=-0.05 * geyser)
    P.add('apex_upper', x=-12.0 * geyser + 10.0 * fib_lr, y=4.0 * geyser, r=-7.0 * geyser + 6.0 * fib_lr)
    P.add('vent_right',
          x=16.0 * geyser + 12.0 * fib_lr,
          y=-7.0 * geyser + 6.0 * sp1,
          sx=0.09 * geyser,
          sy=-0.05 * geyser)

    # Upper chambers & ALL 8+ vessel chimneys arch upward and spew miasma simultaneously!
    P.add('vent_dome',
          x=14.0 * fib_lr,
          y=14.0 * sp1 - 10.0 * sp2 + 16.0 * sp3 + 20.0 * geyser - 12.0 * slump,
          sx=-0.04 * geyser + 0.05 * sp2,
          sy=0.11 * geyser + 0.06 * sp3 - 0.06 * slump,
          r=4.5 * fib_lr - 3.0 * geyser)

    P.add('conus_atrium',
          x=-8.0 * geyser - 14.0 * fib_lr,
          y=18.0 * geyser + 10.0 * sp2 - 10.0 * slump,
          sx=0.04 * geyser, sy=0.08 * geyser,
          r=-6.5 * geyser + 4.0 * fib_lr)
    # Left-pointing cannons: r < 0 pitches UPWARD like chimneys!
    P.add('cannon_upper', y=8.0 * geyser, r=-15.0 * geyser + 6.0 * fib_lr + 4.0 * slump, sx=0.04 * pop)
    P.add('cannon_lower', y=6.0 * geyser, r=-12.0 * geyser - 5.0 * fib_lr + 3.5 * slump, sx=0.04 * pop)

    P.add('dorsal_arch',
          x=10.0 * fib_lr,
          y=22.0 * geyser + 10.0 * sp1 - 11.0 * slump,
          sx=0.04 * geyser, sy=0.10 * geyser - 0.05 * slump,
          r=-4.0 * geyser)
    P.add('aorta_crown', y=12.0 * geyser - 5.0 * slump, r=-8.5 * geyser + 4.0 * fib_lr)
    P.add('pipe_pl', r=-11.0 * geyser + 5.0 * fib_lr + 4.0 * slump)
    P.add('pipe_pr', r=10.0 * geyser - 5.0 * fib_lr - 4.0 * slump)

    # Right-pointing venous & dorsal pipes: r > 0 arches UPWARD!
    P.add('venous_sinus', x=6.0 * geyser + 10.0 * fib_lr, y=16.0 * geyser - 8.0 * slump, sy=0.08 * geyser)
    P.add('vena_up', y=8.0 * geyser, r=14.0 * geyser - 5.0 * slump)
    P.add('vena_down', y=6.0 * geyser, r=12.0 * geyser - 4.5 * slump)
    P.add('dorsal_drain', y=10.0 * geyser - 5.0 * slump, sy=0.06 * geyser)
    P.add('drain_a', r=13.0 * geyser - 4.5 * slump)
    P.add('drain_b', r=10.0 * geyser - 3.5 * slump)

    # All orifices pinch during fibrillation spasms, then FLARE 1.23x together on geyser eruption!
    set_orifices(P, ALL_ORIFICES, scale_delta=-0.12 * sp3 + 0.235 * pop - 0.05 * slump)

    # Rapid 3-wave traveling vein flash across tachycardia -> full blaze on geyser!
    w_sa = bump(t, 0.02, 0.06, 0.13) + bump(t, 0.14, 0.18, 0.25) + bump(t, 0.25, 0.29, 0.36) + bump(t, 0.34, 0.40, 0.68)
    w_av = bump(t, 0.05, 0.09, 0.16) + bump(t, 0.17, 0.21, 0.28) + bump(t, 0.28, 0.32, 0.39) + bump(t, 0.37, 0.43, 0.72)
    w_pk = bump(t, 0.08, 0.12, 0.19) + bump(t, 0.20, 0.24, 0.31) + bump(t, 0.31, 0.35, 0.42) + bump(t, 0.40, 0.46, 0.76)
    f.glow = {
        'glow_sa_root': clamp(0.08 + 0.92 * w_sa),
        'glow_av_bulb': clamp(0.08 + 0.95 * w_av),
        'glow_purkinje_apex': clamp(0.08 + 0.95 * w_pk),
    }
    f.bias['__outward'] = 12.0 * pop - 4.0 * sp3
    return _envelope_idle0(f, t, 1.40, fade_in=0.04, fade_out=0.22)


# ======================================================================
# 4. POWER_UP (1.50s — Calcified Clamp & Twin Systolic Overdrive Surge)
# ======================================================================
def power_up(t: float) -> Frame:
    f = Frame()
    P = f.P

    # 0.00..0.38s Hardened Shell Clampdown (whole heart clamps flat, wide, and taut onto stone dock)
    clamp_dn = hold(t, 0.00, 0.16, 0.28, 0.42)
    # 0.40..0.88s Twin Massive Systolic Overdrive Surges (0.46s & 0.69s) + high-tension tremor!
    s1 = bump(t, 0.37, 0.46, 0.61)
    s2 = bump(t, 0.56, 0.69, 0.94)
    surge = 0.85 * s1 + 1.12 * s2
    trem_env = hold(t, 0.40, 0.45, 0.76, 0.92)
    trem = math.sin(TAU * 15.0 * t) * trem_env

    P.add('vent_dome',
          x=6.0 * clamp_dn - 8.0 * surge + 2.2 * trem,
          y=-22.0 * clamp_dn + 34.0 * surge + 2.5 * trem,
          sx=0.10 * clamp_dn - 0.03 * surge,
          sy=-0.12 * clamp_dn + 0.15 * surge,
          r=3.0 * clamp_dn - 5.2 * surge)
    P.add('dorsal_arch',
          y=-18.0 * clamp_dn + 32.0 * surge + 2.0 * trem,
          sx=0.08 * clamp_dn + 0.03 * surge,
          sy=-0.10 * clamp_dn + 0.14 * surge,
          r=-3.5 * surge)
    P.add('aorta_crown', y=-8.0 * clamp_dn + 15.0 * surge, r=5.0 * clamp_dn - 9.0 * surge)
    P.add('pipe_pl', r=6.0 * clamp_dn - 10.0 * surge + 2.5 * trem)
    P.add('pipe_pr', r=-6.0 * clamp_dn + 9.5 * surge - 2.5 * trem)

    P.add('conus_atrium',
          x=12.0 * clamp_dn - 18.0 * surge - 2.0 * trem,
          y=-18.0 * clamp_dn + 25.0 * surge + 2.0 * trem,
          sx=0.08 * clamp_dn + 0.04 * surge,
          sy=-0.10 * clamp_dn + 0.12 * surge,
          r=4.0 * clamp_dn - 5.5 * surge)
    P.add('cannon_upper', x=6.0 * clamp_dn - 10.0 * surge, r=6.0 * clamp_dn - 10.0 * surge)
    P.add('cannon_lower', x=6.0 * clamp_dn - 10.0 * surge, r=5.0 * clamp_dn - 8.0 * surge)

    # Lower lobes grip wide on clampdown, then brace taut on twin overdrive surges
    P.add('vent_mid',
          x=-16.0 * clamp_dn - 10.0 * surge,
          y=-9.0 * clamp_dn + 4.0 * surge,
          sx=0.09 * clamp_dn + 0.05 * surge,
          sy=-0.07 * clamp_dn + 0.03 * surge)
    P.add('apex_upper', x=-8.0 * clamp_dn - 8.0 * surge, r=4.0 * clamp_dn - 7.5 * surge)
    P.add('vent_low',
          y=-5.0 * clamp_dn,
          sx=0.09 * clamp_dn + 0.06 * surge,
          sy=-0.08 * clamp_dn)
    P.add('apex_lower', x=-8.0 * clamp_dn - 6.0 * surge, r=3.0 * clamp_dn - 5.5 * surge)
    P.add('vent_right',
          x=16.0 * clamp_dn + 12.0 * surge,
          y=-8.0 * clamp_dn + 5.0 * surge,
          sx=0.09 * clamp_dn + 0.06 * surge,
          sy=-0.07 * clamp_dn + 0.04 * surge)

    P.add('venous_sinus', x=10.0 * clamp_dn + 6.0 * surge, y=-12.0 * clamp_dn + 18.0 * surge, sy=-0.08 * clamp_dn + 0.10 * surge)
    P.add('vena_up', r=-6.0 * clamp_dn + 11.0 * surge)
    P.add('vena_down', r=-5.0 * clamp_dn + 9.0 * surge)
    P.add('dorsal_drain', x=8.0 * clamp_dn + 6.0 * surge, y=-8.0 * clamp_dn + 10.0 * surge)
    P.add('drain_a', r=-5.0 * clamp_dn + 9.0 * surge)
    P.add('drain_b', r=-4.0 * clamp_dn + 7.0 * surge)

    set_orifices(P, ALL_ORIFICES, scale_delta=-0.15 * clamp_dn + 0.22 * (s1 + 1.05 * s2))

    f.glow = {
        'glow_sa_root': clamp(0.08 + 0.25 * clamp_dn + 0.95 * (bump(t, 0.36, 0.44, 0.60) + bump(t, 0.55, 0.66, 0.96))),
        'glow_av_bulb': clamp(0.08 + 0.20 * clamp_dn + 0.98 * (bump(t, 0.39, 0.47, 0.64) + bump(t, 0.58, 0.70, 1.00))),
        'glow_purkinje_apex': clamp(0.08 + 0.18 * clamp_dn + 0.98 * (bump(t, 0.42, 0.50, 0.68) + bump(t, 0.61, 0.73, 1.04))),
    }
    f.bias['__outward'] = -6.0 * clamp_dn + 14.0 * surge
    return _envelope_idle0(f, t, 1.50, fade_in=0.05, fade_out=0.24)


# ======================================================================
# 5. SUMMON (1.30s — Deep Right-to-Left Traveling Peristaltic Wave)
# ======================================================================
def summon(t: float) -> Frame:
    f = Frame()
    P = f.P

    # 5-stage smooth traveling wave from x=1325 (far-right intake) -> x=120 (far-left cannons)!
    w1 = bump(t, 0.02, 0.12, 0.30)  # Far-right orifices & pipes (pa, pb, bd, bu)
    w2 = bump(t, 0.10, 0.22, 0.42)  # Venous sinus & dorsal drain
    w3 = bump(t, 0.18, 0.32, 0.54)  # Dorsal arch, aorta crown, vent_right
    w4 = bump(t, 0.27, 0.42, 0.66)  # Central vent_dome & vent_low
    w5 = bump(t, 0.37, 0.53, 0.80)  # Anterior vent_mid, conus_atrium, cannons & orifice_ta/tb burst!
    recoil = kick(t, 0.55, freq=1.9, damp=4.5, end_t=1.26)

    # Stage 1 & 2: Far-right intake gulps & squeezes leftward
    P.add('drain_a', x=10.0 * w1 - 6.0 * w2, y=10.0 * w1, r=12.0 * w1 - 6.0 * w2)
    P.add('drain_b', x=8.0 * w1 - 5.0 * w2, y=6.0 * w1, r=9.0 * w1 - 5.0 * w2)
    P.add('vena_up', x=8.0 * w1 - 6.0 * w2, y=12.0 * w1, r=13.0 * w1 - 6.0 * w2)
    P.add('vena_down', x=10.0 * w1 - 6.0 * w2, y=8.0 * w1, r=11.0 * w1 - 5.0 * w2)
    set_orifices(P, ('orifice_pa', 'orifice_pb', 'orifice_bu', 'orifice_bd'), scale_delta=0.24 * w1 - 0.12 * w2)

    P.add('dorsal_drain', x=8.0 * w1 - 12.0 * w3, y=18.0 * w2 - 6.0 * w4, s=0.11 * w2 - 0.04 * w3)
    P.add('venous_sinus', x=8.0 * w1 - 14.0 * w3, y=24.0 * w2 - 8.0 * w4, sx=0.09 * w2, sy=0.13 * w2 - 0.05 * w3)
    set_orifices(P, ('orifice_bb', 'orifice_bm'), scale_delta=0.22 * w2 - 0.10 * w3)

    # Stage 3: Dorsal arch & right cushion crest
    P.add('dorsal_arch', x=8.0 * w2 - 16.0 * w4, y=28.0 * w3 - 10.0 * w5, sx=0.08 * w3, sy=0.14 * w3 - 0.05 * w4, r=-4.0 * w3 + 3.0 * w4)
    P.add('aorta_crown', y=14.0 * w3 - 5.0 * w5, r=-9.0 * w3 + 6.0 * w4)
    P.add('pipe_pl', r=-10.0 * w3 + 7.0 * w4)
    P.add('pipe_pr', r=9.0 * w3 - 6.0 * w4)
    set_orifices(P, TOP_ORIFICES, scale_delta=0.23 * w3 - 0.09 * w4)
    P.add('vent_right', x=14.0 * w3 - 10.0 * w4, y=14.0 * w3 - 6.0 * w4, sx=0.09 * w3, sy=0.08 * w3)

    # Stage 4: Central dome & lower belly roll forward
    P.add('vent_dome',
          x=12.0 * w3 - 22.0 * w5 + 7.0 * recoil,
          y=32.0 * w4 - 14.0 * w5 - 4.0 * recoil,
          sx=0.09 * w4, sy=0.15 * w4 - 0.07 * w5,
          r=-4.5 * w4 + 4.5 * w5)
    P.add('vent_low', x=8.0 * w3 - 16.0 * w5, y=-4.0 * w4, sx=0.09 * w4, sy=-0.04 * w4)
    P.add('apex_lower', x=-16.0 * w5 + 5.0 * recoil, y=5.0 * w5, r=-6.5 * w5)

    # Stage 5: Anterior atrium & dual cannons surge left & burst open at t=0.53s!
    P.add('vent_mid', x=10.0 * w4 - 26.0 * w5 + 8.0 * recoil, y=12.0 * w5 - 4.0 * w4, sx=0.10 * w5, sy=0.06 * w5)
    P.add('apex_upper', x=-14.0 * w5 + 5.0 * recoil, y=8.0 * w5, r=-8.0 * w5 + 3.5 * recoil, s=0.05 * w5)
    P.add('conus_atrium', x=10.0 * w4 - 30.0 * w5 + 9.0 * recoil, y=18.0 * w5 - 6.0 * w4, sx=0.09 * w5, sy=0.09 * w5, r=-5.5 * w5)
    P.add('cannon_upper', x=-16.0 * w5 + 6.0 * recoil, r=-9.5 * w5 + 5.0 * recoil, s=0.06 * w5)
    P.add('cannon_lower', x=-16.0 * w5 + 6.0 * recoil, r=7.5 * w5 - 4.5 * recoil, s=0.06 * w5)
    set_orifices(P, LEFT_ORIFICES, scale_delta=-0.10 * w4 + 0.26 * w5)

    f.glow = {
        'glow_sa_root': clamp(0.08 + 0.94 * bump(t, 0.16, 0.32, 0.56)),
        'glow_av_bulb': clamp(0.08 + 0.96 * bump(t, 0.26, 0.42, 0.68)),
        'glow_purkinje_apex': clamp(0.08 + 0.98 * bump(t, 0.36, 0.53, 0.82)),
    }
    f.bias['__wave_t'] = t
    return _envelope_idle0(f, t, 1.30, fade_in=0.04, fade_out=0.22)


# ======================================================================
# 6. HURT (0.65s — Hydrostatic Volume-Conserving Impact Slosh)
# ======================================================================
def hurt(t: float) -> Frame:
    f = Frame()
    P = f.P

    # Struck on anterior left at t=0.035s -> caves right (+34..46px) & squashes down!
    hit_l = bump(t, 0.00, 0.042, 0.19)
    # 0.05s later (t=0.09s): displaced blood visibly surges into right/dorsal chambers!
    surge_r = bump(t, 0.035, 0.095, 0.29)
    # Viscous damped slosh back-and-forth on locked stone dock
    slosh = kick(t, 0.16, freq=2.8, damp=6.2, end_t=0.63)

    # Left chambers cave rightward & compress
    P.add('conus_atrium',
          x=34.0 * hit_l - 12.0 * slosh,
          y=-14.0 * hit_l + 6.0 * slosh,
          sx=-0.09 * hit_l + 0.04 * slosh,
          sy=-0.06 * hit_l,
          r=6.5 * hit_l - 3.0 * slosh)
    P.add('cannon_upper', x=14.0 * hit_l - 5.0 * slosh, r=-13.0 * hit_l + 7.0 * slosh)
    P.add('cannon_lower', x=14.0 * hit_l - 5.0 * slosh, r=11.0 * hit_l - 6.0 * slosh)
    set_orifices(P, LEFT_ORIFICES, scale_delta=-0.16 * hit_l + 0.12 * surge_r)

    P.add('vent_mid',
          x=32.0 * hit_l - 11.0 * slosh,
          y=-12.0 * hit_l + 4.0 * slosh,
          sx=-0.08 * hit_l + 0.04 * slosh,
          sy=-0.05 * hit_l)
    P.add('apex_upper', x=14.0 * hit_l - 5.0 * slosh, y=-4.0 * hit_l, r=10.0 * hit_l - 5.0 * slosh)
    P.add('vent_low', x=18.0 * hit_l - 6.0 * slosh, y=-4.0 * hit_l, sx=-0.04 * hit_l + 0.05 * surge_r, sy=-0.04 * hit_l)
    P.add('apex_lower', x=12.0 * hit_l - 4.0 * slosh, r=7.0 * hit_l - 3.5 * slosh)

    # Displaced fluid surges into vent_dome, vent_right, venous_sinus, dorsal_arch (+0.05s delayed peak!)
    P.add('vent_dome',
          x=24.0 * surge_r - 9.0 * slosh,
          y=20.0 * surge_r - 7.0 * slosh,
          sx=0.08 * surge_r - 0.03 * slosh,
          sy=0.11 * surge_r - 0.04 * slosh,
          r=-6.0 * surge_r + 2.5 * slosh)
    P.add('vent_right',
          x=24.0 * surge_r - 8.0 * slosh,
          y=12.0 * surge_r - 4.0 * slosh,
          sx=0.11 * surge_r, sy=0.07 * surge_r)
    P.add('venous_sinus',
          x=26.0 * surge_r - 9.0 * slosh,
          y=18.0 * surge_r - 6.0 * slosh,
          sx=0.10 * surge_r, sy=0.10 * surge_r,
          r=-4.5 * surge_r)
    P.add('vena_up', x=8.0 * surge_r, r=-10.0 * surge_r + 5.0 * slosh)
    P.add('vena_down', x=8.0 * surge_r, r=-9.0 * surge_r + 4.5 * slosh)

    P.add('dorsal_arch',
          x=22.0 * surge_r - 8.0 * slosh,
          y=18.0 * surge_r - 6.0 * slosh,
          sx=0.07 * surge_r, sy=0.09 * surge_r,
          r=-5.5 * surge_r + 2.5 * slosh)
    P.add('aorta_crown', x=6.0 * surge_r, r=-8.0 * surge_r + 4.5 * slosh)
    P.add('pipe_pl', r=-9.0 * surge_r + 5.5 * slosh)
    P.add('pipe_pr', r=-8.0 * surge_r + 5.0 * slosh)
    P.add('dorsal_drain', x=18.0 * surge_r - 6.0 * slosh, y=10.0 * surge_r)
    P.add('drain_a', r=-9.0 * surge_r + 4.5 * slosh)
    P.add('drain_b', r=-7.0 * surge_r + 3.5 * slosh)
    set_orifices(P, TOP_ORIFICES + RIGHT_ORIFICES, scale_delta=0.20 * surge_r)

    f.glow = {
        'glow_sa_root': clamp(0.08 + 0.85 * surge_r),
        'glow_av_bulb': clamp(0.08 + 0.90 * bump(t, 0.01, 0.065, 0.24)),
        'glow_purkinje_apex': clamp(0.08 + 0.85 * hit_l),
    }
    f.bias['__shock'] = -14.0 * hit_l + 6.0 * slosh
    return _envelope_idle0(f, t, 0.65, fade_in=0.02, fade_out=0.15)


# ======================================================================
# SHARED DEATH PANCAKE COLLAPSE POSE (0px underground clipping, det(J) > 0.85)
# ======================================================================
def apply_collapse_pose(P: Pose, c: float):
    if c <= 1e-6:
        return
    # Continuum vertical deflation proportional to height above stone dock + lateral pancake spread!
    P.add('dorsal_arch', x=14.0 * c, y=-42.0 * c, sx=0.08 * c, sy=-0.14 * c, r=-5.0 * c)
    P.add('aorta_crown', x=-4.0 * c, y=-16.0 * c, r=10.0 * c)
    P.add('pipe_pl', y=-6.0 * c, r=14.0 * c)
    P.add('pipe_pr', y=-6.0 * c, r=-16.0 * c)

    P.add('conus_atrium', x=-14.0 * c, y=-38.0 * c, sx=0.09 * c, sy=-0.14 * c, r=6.0 * c)
    # Left-pointing cannons: r > 0 droops limp DOWNWARD!
    P.add('cannon_upper', x=-6.0 * c, y=-10.0 * c, r=12.5 * c)
    P.add('cannon_lower', x=-6.0 * c, y=-10.0 * c, r=7.5 * c)

    P.add('vent_dome', x=6.0 * c, y=-34.0 * c, sx=0.11 * c, sy=-0.16 * c, r=4.0 * c)
    P.add('vent_mid', x=-16.0 * c, y=-18.0 * c, sx=0.10 * c, sy=-0.11 * c)
    P.add('apex_upper', x=-10.0 * c, y=-6.0 * c, r=8.0 * c)

    P.add('vent_low', y=-6.0 * c, sx=0.10 * c, sy=-0.09 * c)
    P.add('apex_lower', x=-10.0 * c, y=-2.0 * c, r=4.5 * c)
    P.add('vent_right', x=16.0 * c, y=-14.0 * c, sx=0.10 * c, sy=-0.10 * c)

    # Right-pointing pipes: r < 0 droops limp DOWNWARD!
    P.add('venous_sinus', x=16.0 * c, y=-26.0 * c, sx=0.09 * c, sy=-0.13 * c, r=-4.0 * c)
    P.add('vena_up', y=-10.0 * c, r=-18.0 * c)
    P.add('vena_down', y=-6.0 * c, r=-11.0 * c)

    P.add('dorsal_drain', x=14.0 * c, y=-18.0 * c, sx=0.08 * c, sy=-0.10 * c)
    P.add('drain_a', y=-4.0 * c, r=-12.0 * c)
    P.add('drain_b', y=-2.0 * c, r=-6.0 * c)

    # All 10 orifices pinch/flatten limp
    set_orifices(P, ALL_ORIFICES, sx=-0.14 * c, sy=-0.20 * c)


def necrotic_tint(dark: float):
    # Cold necrotic slate-purple tint
    return (1.0 - 0.46 * dark, 1.0 - 0.44 * dark, 1.0 - 0.31 * dark, 1.0)


# ======================================================================
# 7. DIE (2.60s — Ventricular Fibrillation -> Agonal Heave -> Pancake Exsanguination)
# ======================================================================
def die(t: float) -> Frame:
    f = Frame()
    # Fade smoothly out of IDLE0 over first 0.06s so transition from idle has zero pop
    w_idle = 1.0 - ease(seg(t, 0.0, 0.08))
    f.P.blend_from(IDLE0.P, w_idle)

    P = f.P
    # 0.00..0.95s: Violent erratic ventricular fibrillation (0.05s, 0.21s, 0.36s), desperate heave (0.60s), weak flutter (1.02s)
    vf1 = bump(t, 0.01, 0.055, 0.16)
    vf2 = bump(t, 0.13, 0.21, 0.32)
    vf3 = bump(t, 0.28, 0.36, 0.48)
    heave = bump(t, 0.46, 0.60, 0.88)
    flut = bump(t, 0.92, 1.03, 1.22)
    # Post-mortem nerve twitch at t=1.85s (vanishes well before 2.40s flatline!)
    twitch = bump(t, 1.79, 1.85, 1.98)

    # Total pancake exsanguination collapse factor c: 0 -> 1 over [0.88, 2.34], flatline 1.0 for t >= 2.34s!
    c = 0.35 * ease_in(seg(t, 0.88, 2.34)) + 0.65 * ease(seg(t, 0.88, 2.34))
    f.collapse = c

    # Arrhythmic misfires + desperate arching heave at 0.60s
    P.add('conus_atrium',
          x=-16.0 * vf1 + 14.0 * vf2 - 12.0 * heave,
          y=14.0 * vf1 - 10.0 * vf2 + 22.0 * heave + 6.0 * flut + 3.5 * twitch,
          s=0.07 * vf1 + 0.09 * heave,
          r=-5.0 * vf1 + 5.0 * vf2 - 6.0 * heave)
    P.add('cannon_upper', r=-11.0 * vf1 + 9.0 * vf2 - 13.0 * heave - 4.0 * twitch)
    P.add('cannon_lower', r=9.0 * vf1 - 10.0 * vf3 - 10.0 * heave + 3.0 * twitch)

    P.add('vent_dome',
          x=14.0 * vf2 - 12.0 * vf3,
          y=-12.0 * vf1 + 18.0 * vf2 + 26.0 * heave + 8.0 * flut + 4.5 * twitch,
          sx=0.07 * vf2 + 0.06 * heave,
          sy=0.10 * vf2 + 0.13 * heave + 0.04 * flut,
          r=-6.0 * vf2 + 5.0 * vf3 - 4.5 * heave)
    P.add('vent_mid', x=-14.0 * vf3 + 12.0 * vf1 - 10.0 * heave, y=8.0 * vf3 + 8.0 * heave, s=0.06 * vf3)
    P.add('vent_right', x=14.0 * vf2 + 10.0 * heave, y=8.0 * heave, s=0.06 * vf2)
    P.add('dorsal_arch', y=14.0 * vf3 + 22.0 * heave + 5.0 * flut, sy=0.10 * heave)
    P.add('aorta_crown', r=-8.0 * vf3 - 10.0 * heave)
    P.add('venous_sinus', y=14.0 * vf1 + 16.0 * heave, s=0.07 * vf1 + 0.08 * heave)

    set_orifices(P, ALL_ORIFICES, scale_delta=0.16 * vf2 - 0.12 * vf3 + 0.22 * heave + 0.08 * flut + 0.06 * twitch)

    # Apply grounded pancake exsanguination
    apply_collapse_pose(P, c)

    # Desynchronized flickering coronary veins -> die to 0.0, tiny spark on 1.85s nerve twitch
    g_sa = 0.90 * vf1 + 0.55 * vf3 + 0.95 * heave + 0.35 * flut + 0.22 * twitch
    g_av = 0.85 * vf2 + 0.90 * heave + 0.28 * flut + 0.10 * twitch
    g_pk = 0.80 * vf3 + 0.85 * heave + 0.18 * flut
    alive_glow = 1.0 - ease(seg(t, 1.05, 1.65))
    f.glow = {
        'glow_sa_root': clamp(IDLE0.glow['glow_sa_root'] * w_idle + g_sa * alive_glow + 0.22 * twitch),
        'glow_av_bulb': clamp(IDLE0.glow['glow_av_bulb'] * w_idle + g_av * alive_glow + 0.09 * twitch),
        'glow_purkinje_apex': clamp(IDLE0.glow['glow_purkinje_apex'] * w_idle + g_pk * alive_glow),
    }

    dark = ease(seg(t, 0.92, 2.36))
    f.tint = necrotic_tint(dark)
    f.bias['__collapse'] = c
    return f


# ======================================================================
# 8. REVIVE (1.80s — Defibrillation Spark & Twin Re-Pressurization Pumps -> IDLE0)
# ======================================================================
def revive(t: float) -> Frame:
    f = Frame()
    P = f.P

    # Exact match to die(2.60) at t=0.0 (c=1.0 -> 0.0), exact match to idle_loop(0.0) at t=1.80!
    rise_1 = ease(seg(t, 0.42, 0.78))
    rise_2 = ease_out(seg(t, 0.82, 1.38))
    c = 1.0 - (0.42 * rise_1 + 0.58 * rise_2)
    f.collapse = c
    apply_collapse_pose(P, c)

    # 1) SA-node spark at t=0.18s (tiny electrical jolt)
    spark = bump(t, 0.11, 0.18, 0.32)
    # 2) Twin defibrillation pumps at t=0.56s & t=0.96s re-pressurizing dorsal -> dome -> anterior!
    p1_dorsal = bump(t, 0.42, 0.52, 0.74)
    p1_dome = bump(t, 0.46, 0.56, 0.78)
    p1_ant = bump(t, 0.50, 0.61, 0.82)

    p2_dorsal = bump(t, 0.82, 0.92, 1.20)
    p2_dome = bump(t, 0.86, 0.96, 1.26)
    p2_ant = bump(t, 0.91, 1.02, 1.32)

    P.add('dorsal_arch', y=6.0 * spark + 18.0 * p1_dorsal + 24.0 * p2_dorsal, sy=0.08 * p1_dorsal + 0.11 * p2_dorsal)
    P.add('aorta_crown', r=-4.0 * spark - 8.0 * p1_dorsal - 10.0 * p2_dorsal)
    P.add('venous_sinus', y=14.0 * p1_dorsal + 18.0 * p2_dorsal, s=0.07 * p1_dorsal + 0.09 * p2_dorsal)
    P.add('vena_up', r=8.0 * p1_dorsal + 11.0 * p2_dorsal)

    P.add('vent_dome',
          y=8.0 * spark + 22.0 * p1_dome + 28.0 * p2_dome,
          sx=0.05 * p1_dome + 0.07 * p2_dome,
          sy=0.10 * p1_dome + 0.13 * p2_dome,
          r=-3.5 * p1_dome - 4.5 * p2_dome)
    P.add('conus_atrium',
          x=-10.0 * p1_ant - 16.0 * p2_ant,
          y=16.0 * p1_ant + 22.0 * p2_ant,
          s=0.07 * p1_ant + 0.09 * p2_ant,
          r=-4.0 * p1_ant - 5.5 * p2_ant)
    P.add('cannon_upper', x=-6.0 * p1_ant - 10.0 * p2_ant, r=-8.0 * p1_ant - 10.0 * p2_ant)
    P.add('cannon_lower', x=-6.0 * p1_ant - 10.0 * p2_ant, r=-6.0 * p1_ant - 8.0 * p2_ant)
    P.add('vent_mid', x=-8.0 * p1_ant - 14.0 * p2_ant, sx=0.06 * p2_ant)
    P.add('apex_upper', r=-5.0 * p1_ant - 7.0 * p2_ant)

    set_orifices(P, RIGHT_ORIFICES + TOP_ORIFICES, scale_delta=0.18 * p1_dorsal + 0.22 * p2_dorsal)
    set_orifices(P, LEFT_ORIFICES, scale_delta=0.18 * p1_ant + 0.24 * p2_ant)

    # Blend smoothly into exact IDLE0 over [1.32, 1.80]
    w_end = ease(seg(t, 1.32, 1.80))
    P.blend_from(IDLE0.P, w_end)

    f.glow = {
        'glow_sa_root': clamp(0.92 * spark + 0.90 * p1_dorsal + 0.96 * p2_dorsal + IDLE0.glow['glow_sa_root'] * w_end),
        'glow_av_bulb': clamp(0.25 * spark + 0.92 * p1_dome + 0.98 * p2_dome + IDLE0.glow['glow_av_bulb'] * w_end),
        'glow_purkinje_apex': clamp(0.88 * p1_ant + 0.98 * p2_ant + IDLE0.glow['glow_purkinje_apex'] * w_end),
    }

    dark = 1.0 - (0.35 * ease(seg(t, 0.20, 0.72)) + 0.65 * ease(seg(t, 0.72, 1.36)))
    f.tint = necrotic_tint(dark)
    f.bias['__collapse'] = c
    return f


FUNCS = {
    'idle_loop': idle_loop,
    'attack': attack,
    'cast': cast,
    'hurt': hurt,
    'die': die,
    'summon': summon,
    'power_up': power_up,
    'revive': revive,
}
