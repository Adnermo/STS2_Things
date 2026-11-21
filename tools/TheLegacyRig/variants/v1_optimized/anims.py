"""Heavy, non-jelly myocardial choreography & responsive coastal flora for The Legacy (腐化之遗) — Version A Deep-Polished (v1_optimized)."""
import math

FPS = 30
TAU = 2.0 * math.pi
GLOW_NAMES = ('atrium_glow', 'bulb_glow', 'mid_lobe_glow', 'bottom_lobe_glow')

def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))

def ease(x):
    x = clamp(x)
    return x * x * (3.0 - 2.0 * x)

def ease_out(x):
    x = clamp(x)
    return 1.0 - (1.0 - x) ** 3

def ease_in(x):
    x = clamp(x)
    return x ** 3

def seg(t, t0, t1):
    return clamp((t - t0) / (t1 - t0)) if t1 > t0 else float(t >= t0)

_PN_CACHE = {}
def pulse(x, rise=0.035, dec=0.16):
    if x <= 0.0:
        return 0.0
    key = (round(rise, 5), round(dec, 5))
    norm = _PN_CACHE.get(key)
    if norm is None:
        u_peak = math.log(1.0 + dec / rise) * rise
        norm = (1.0 - math.exp(-u_peak / rise)) * math.exp(-u_peak / dec)
        _PN_CACHE[key] = max(norm, 1e-9)
    val = (1.0 - math.exp(-x / rise)) * math.exp(-x / dec) / norm
    return val if val > 1e-6 else 0.0

def systole(x, rise=0.034, hold=0.055, relax=0.22):
    """Asymmetric heavy myocardial contraction: fast snap -> brief isometric peak lock -> viscous overdamped sag (ZERO jelly bounce)."""
    if x <= 0.0:
        return 0.0
    if x < rise:
        u = x / rise
        return 1.0 - (1.0 - u) ** 3
    if x < rise + hold:
        # Isometric muscular lock at peak internal blood pressure
        u = (x - rise) / max(hold, 1e-6)
        return 1.0 - 0.03 * (u * u)
    # Viscous overdamped diastolic relaxation (monotone, non-oscillatory)
    xr = x - (rise + hold)
    val = 0.97 * (1.0 + 0.55 * (xr / relax)) * math.exp(-xr / relax)
    return val if val > 1e-6 else 0.0

def heavy_recoil(t, t0, rise=0.048, settle=0.26):
    """Overdamped single-lobe heavy flesh absorption — absorbs momentum into wet stone with ZERO rubbery sine ringing."""
    return pulse(t - t0, rise=rise, dec=settle)

def kick(t, t0, freq=2.8, damp=6.2):
    """High-damping follow-through reserved ONLY for hollow vessel lips & slender fronds (never heavy muscle mass)."""
    if t <= t0:
        return 0.0
    x = t - t0
    return math.exp(-damp * x) * math.sin(TAU * freq * x)

class Pose(dict):
    def add(self, b, r=0.0, x=0.0, y=0.0, sx=0.0, sy=0.0, s=0.0):
        R, X, Y, SX, SY = self.get(b, (0.0, 0.0, 0.0, 1.0, 1.0))
        self[b] = (R + r, X + x, Y + y, SX * (1.0 + sx + s), SY * (1.0 + sy + s))

    def lerp_to(self, target, u):
        if u <= 0.0:
            return self
        if u >= 1.0:
            self.clear()
            self.update(target)
            return self
        keys = set(self.keys()) | set(target.keys())
        for k in keys:
            r0, x0, y0, sx0, sy0 = self.get(k, (0.0, 0.0, 0.0, 1.0, 1.0))
            r1, x1, y1, sx1, sy1 = target.get(k, (0.0, 0.0, 0.0, 1.0, 1.0))
            self[k] = (
                r0 + (r1 - r0) * u,
                x0 + (x1 - x0) * u,
                y0 + (y1 - y0) * u,
                sx0 + (sx1 - sx0) * u,
                sy0 + (sy1 - sy0) * u,
            )
        return self

def lerp_glow(g0, g1, u):
    u = clamp(u)
    return {k: g0.get(k, 0.0) * (1.0 - u) + g1.get(k, 0.0) * u for k in GLOW_NAMES}

def beats_sys(t, period, offset, rise=0.034, hold=0.055, relax=0.22):
    tw = t % period
    return sum(systole(tw - offset - k * period, rise=rise, hold=hold, relax=relax) for k in range(-4, 3))

def beats(t, period, offset, **kw):
    tw = t % period
    return sum(pulse(tw - offset - k * period, **kw) for k in range(-4, 3))

def pin_floor(P, heart_dy=0.0, heart_dsy=0.0):
    """Counter-compensate bottom_lobe & right_lobe vertical drift when 'heart' moves/scales,
    keeping the heavy lower belly planted flat on the wet stone floor."""
    comp = -0.88 * heart_dy + 125.0 * heart_dsy
    if abs(comp) > 1e-5:
        P.add('bottom_lobe', y=comp)
        P.add('right_lobe', y=comp * 0.78)

def add_atrium_swell(P, dsx=0.0, dsy=0.0):
    """Swell atrium chamber dome while keeping child tube_a/tube_b anchors from rubber-stretching (-225px lever)."""
    P.add('atrium', sx=dsx, sy=dsy)
    if abs(dsx) > 1e-5:
        P.add('tube_a', x=145.0 * dsx)
        P.add('tube_b', x=165.0 * dsx)

def add_mid_swell(P, dsx=0.0, dsy=0.0):
    """Swell mid_lobe dome while keeping mid_tip (-260px lever) organically proportioned."""
    P.add('mid_lobe', sx=dsx, sy=dsy)
    if abs(dsx) > 1e-5:
        P.add('mid_tip', x=160.0 * dsx)

# ---------------------------------------------------------------- idle_loop (3.0s, two 1.5s Lub-Dub heartbeats)
def idle_base(t, amp=1.0, T=3.0):
    """Grounded, dense, asymmetric Lub-Dub myocardial cycle (two 1.5s beats per 3.0s loop, zero body levitation, zero jelly)."""
    P = Pose()
    tw = t % T
    ph = TAU * tw / T

    # Asymmetric physiological sequence:
    # Slow venous engorgement -> Crisp Atrial 'Lub' -> Isometric Ventricular 'Dub' (+0.17s) -> Arterial jet -> Viscous gravity sag
    Pven = beats(tw, 1.5, 1.26, rise=0.10, dec=0.22) * amp
    Pa   = beats_sys(tw, 1.5, 0.02, rise=0.030, hold=0.042, relax=0.15) * amp   # "Lub" (atrium snaps taut)
    Pv   = beats_sys(tw, 1.5, 0.19, rise=0.034, hold=0.060, relax=0.19) * amp   # "Dub" (bulb isometric squeeze)
    Pm   = beats_sys(tw, 1.5, 0.23, rise=0.038, hold=0.050, relax=0.20) * amp   # coronary mid/bottom lobe compression
    Pr   = beats(tw, 1.5, 0.28, rise=0.030, dec=0.20) * amp                     # arterial mouth jet flare
    Pset = beats(tw, 1.5, 0.48, rise=0.085, dec=0.28) * amp                     # heavy viscous diastolic settle

    br = math.sin(ph)

    h_dy = 1.4 * Pv - 2.1 * Pset + 0.55 * br
    h_dsy = 0.014 * Pv - 0.007 * Pa - 0.010 * Pset + 0.003 * br
    P.add('heart', y=h_dy, sx=0.013 * Pv - 0.004 * Pset, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    # Subtle contact breathing on left-weed anchor right behind mid_lobe's nose
    P.add('weed_l_anchor', x=-1.4 * Pm + 0.4 * br, y=0.7 * Pv - 0.8 * Pset)

    # Venous reservoirs (right/dorsal purple & blue): slow dark-blood engorgement then emptying into atrium/bulb
    P.add('purple_mass', sx=0.038 * Pven - 0.018 * Pa + 0.020 * Pm, sy=0.034 * Pven - 0.016 * Pa + 0.020 * Pm,
          y=1.4 * Pven - 1.6 * Pset, r=0.45 * math.sin(ph + 0.7))
    P.add('purple_top', y=1.4 * Pr - 1.8 * Pset, r=-2.5 * Pr + 0.65 * math.sin(ph + 1.4))
    P.add('ptube_l', r=5.8 * Pr + 0.6 * math.sin(ph + 2.0), s=0.054 * Pr)
    P.add('ptube_r', r=-5.4 * Pr - 0.6 * math.sin(ph + 2.6), s=0.054 * Pr)

    P.add('purple_lr', s=0.030 * Pven + 0.018 * Pr)
    P.add('plr_a', r=4.8 * Pr - 0.5 * math.sin(ph + 0.3), s=0.048 * Pr)
    P.add('plr_b', r=4.4 * Pr + 0.45 * math.sin(ph + 1.1), s=0.045 * Pr)

    P.add('blue_main', s=0.032 * Pven - 0.014 * Pa + 0.024 * Pm, r=0.45 * math.sin(ph + 0.4))
    P.add('blue_up', r=-6.0 * Pr - 0.6 * math.sin(ph + 1.3), s=0.055 * Pr)
    P.add('blue_down', r=5.2 * Pr + 0.5 * math.sin(ph + 2.1), s=0.050 * Pr)
    P.add('blue_bottom', s=0.022 * Pm, r=1.4 * Pr - 0.35 * math.sin(ph + 0.2))

    # Left/central cyan chambers: authoritative isometric Lub-Dub squeeze with slight muscular torsion!
    add_atrium_swell(P, dsx=0.074 * Pa - 0.024 * Pv, dsy=0.072 * Pa - 0.022 * Pv)
    P.add('atrium', y=2.2 * Pa - 1.9 * Pset, x=-3.0 * Pa, r=1.5 * Pa - 0.9 * Pv)
    P.add('tube_a', r=-6.2 * Pr + 0.5 * math.sin(ph + 0.4), s=0.056 * Pr + 0.018 * Pa, x=-2.6 * Pr)
    P.add('tube_b', r=5.6 * Pr + 0.45 * math.sin(ph + 1.2), s=0.054 * Pr + 0.018 * Pa, x=-2.4 * Pr)

    P.add('bulb_base', s=0.040 * Pv - 0.010 * Pa)
    P.add('bulb', sx=0.088 * Pv - 0.018 * Pa, sy=0.092 * Pv - 0.020 * Pa - 0.014 * Pset,
          y=2.8 * Pv - 2.2 * Pset, r=-2.0 * Pv + 0.6 * Pset)

    add_mid_swell(P, dsx=0.070 * Pm, dsy=0.060 * Pm - 0.012 * Pset)
    P.add('mid_lobe', x=-4.2 * Pv)
    P.add('mid_tip', r=4.4 * Pr + 0.45 * math.sin(ph + 1.9), s=0.046 * Pr, x=-2.2 * Pm)

    P.add('bottom_lobe', sx=0.060 * Pm, sy=0.042 * Pm - 0.009 * Pset, x=-2.2 * Pm)
    P.add('bottom_tip', r=-1.3 * Pr + 0.25 * math.sin(ph + 2.4), sx=0.042 * Pr)

    P.add('right_lobe', sx=0.065 * Pm, sy=0.054 * Pm - 0.010 * Pset)

    # Traveling coronary bioluminescence wave: atrium -> mid_lobe -> bulb -> bottom_lobe
    glow = {
        'atrium_glow':      clamp(0.09 + 0.80 * Pa + 0.16 * Pv),
        'mid_lobe_glow':    clamp(0.08 + 0.50 * Pa + 0.48 * Pm),
        'bulb_glow':        clamp(0.09 + 0.14 * Pa + 0.88 * Pv),
        'bottom_lobe_glow': clamp(0.08 + 0.24 * Pv + 0.72 * Pm),
    }
    return P, glow

IDLE0_POSE, IDLE0_GLOW = idle_base(0.0)

def blend_one_shot(P, g, t, dur, blend_in=0.08, blend_out=0.22):
    if t < blend_in:
        u = 1.0 - ease(t / blend_in)
        P.lerp_to(IDLE0_POSE, u)
        g = lerp_glow(g, IDLE0_GLOW, u)
    if t > dur - blend_out:
        u = ease((t - (dur - blend_out)) / blend_out)
        P.lerp_to(IDLE0_POSE, u)
        g = lerp_glow(g, IDLE0_GLOW, u)
    return P, g

def weed_target_idle(name, i, t, base_x, is_ground=False, T=3.0):
    """Poised two-harmonic coastal draft wave (progressive phase lag base->tip) + physical heartbeat contact push!"""
    tw = t % T
    ph1 = TAU * tw / T - (base_x / 1358.0) * 1.65
    ph2 = 2.0 * TAU * tw / T - (base_x / 1358.0) * 2.10
    side = 1.0 if base_x < 679.0 else -1.0

    # Calibrated for 3-4 serial chain segments so cumulative tip excursion is lush (~38-50px on weed_left, ~24-32px on ground weeds) without frantic whipping!
    if name == 'weed_left':
        w1 = (1.15 + 0.65 * i) * math.sin(ph1 - 0.62 * i)
        w2 = (0.55 + 0.35 * i) * math.sin(ph2 - 0.75 * i + 0.5)
        beat_push = (1.35 + 0.65 * i) * beats_sys(tw, 1.5, 0.22 + 0.032 * i, rise=0.04, hold=0.04, relax=0.22)
        return w1 + w2 + beat_push
    if name == 'weed_ground_left':
        w1 = (1.85 + 1.15 * i) * math.sin(ph1 - 0.58 * i - 0.35)
        w2 = (0.80 + 0.55 * i) * math.sin(ph2 - 0.70 * i + 0.2)
        beat_push = (1.80 + 0.95 * i) * beats_sys(tw, 1.5, 0.23 + 0.035 * i, rise=0.04, hold=0.04, relax=0.22)
        return w1 + w2 + beat_push
    if is_ground:
        w1 = (1.05 + 0.68 * i) * math.sin(ph1 - 0.58 * i)
        w2 = (0.45 + 0.32 * i) * math.sin(ph2 - 0.68 * i + 0.4)
        dist_delay = abs(base_x - 650.0) / 2200.0 + 0.030 * i
        beat_push = side * (1.15 + 0.62 * i) * beats_sys(tw, 1.5, 0.21 + dist_delay, rise=0.04, hold=0.035, relax=0.20)
        return w1 + w2 + beat_push

    # Chamber-parented dorsal/top weeds
    w1 = (0.75 + 0.45 * i) * math.sin(ph1 - 0.55 * i)
    dist_delay = abs(base_x - 679.0) / 2600.0 + 0.028 * i
    dt_beat = (tw - 0.19 - dist_delay) % 1.5
    sh1 = kick(dt_beat, 0.0, freq=3.6, damp=7.2)
    return w1 + side * (0.85 + 0.42 * i) * sh1

# ---------------------------------------------------------------- attack (1.25s)
def attack(t):
    dur = 1.25
    P, g = idle_base(t * 0.35, amp=0.30)
    # 0.00-0.27s: Dense muscular brace downward (-4.5px) & back-right (+16px shear), gorging bulb & purple_mass.
    # 0.28-0.39s: Explosive systolic arterial heave forward-left (-39px tube_a/b, -27px mid_tip, +15% mouth flare),
    #             physically sweeping weed_left & weed_ground_left aside!
    # 0.40-1.25s: Overdamped heavy flesh recovery (ZERO rubbery jelly bounce on muscle!).
    coil = ease(seg(t, 0.0, 0.27)) * (1.0 - ease_out(seg(t, 0.27, 0.35)))
    thrust = ease_out(seg(t, 0.28, 0.38)) * (1.0 - ease(seg(t, 0.43, 0.96)))
    settle = heavy_recoil(t, 0.43, rise=0.065, settle=0.25) * (1.0 - ease(seg(t, 0.96, dur)))

    h_dx = 15.5 * coil - 12.0 * thrust + 3.2 * settle
    h_dy = -4.2 * coil + 4.0 * thrust - 2.2 * settle
    h_dsy = -0.040 * coil + 0.028 * thrust - 0.014 * settle
    P.add('heart', x=h_dx, y=h_dy, sx=0.018 * coil + 0.016 * thrust, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    # Left-weed base braces slightly with mid_lobe
    P.add('weed_l_anchor', x=2.0 * coil - 4.5 * thrust + 1.0 * settle, y=-1.5 * coil + 1.2 * thrust)

    ej = systole(t - 0.285, rise=0.032, hold=0.045, relax=0.17)
    hit = systole(t - 0.320, rise=0.030, hold=0.050, relax=0.18)
    P.add('bulb_base', s=0.052 * coil + 0.058 * ej, x=3.2 * coil - 5.2 * thrust)
    P.add('bulb', sx=0.108 * coil + 0.122 * ej, sy=0.096 * coil + 0.138 * ej - 0.020 * settle,
          x=4.2 * coil - 7.2 * thrust, y=4.0 * ej - 1.5 * settle, r=-3.8 * coil + 6.2 * thrust)

    add_atrium_swell(P, dsx=-0.032 * coil + 0.108 * hit, dsy=-0.032 * coil + 0.112 * hit)
    P.add('atrium', x=3.8 * coil - 9.5 * thrust + 2.0 * settle, y=-2.4 * coil + 4.2 * thrust - 1.5 * settle, r=-3.6 * coil + 4.8 * thrust)
    for b, sg in (('tube_a', -1.0), ('tube_b', 1.0)):
        P.add(b,
              x=2.2 * coil - 4.4 * thrust,
              r=sg * (-7.8 * coil + 10.0 * thrust) + sg * 3.8 * kick(t, 0.42, 2.4, 6.5),
              s=-0.032 * coil + 0.150 * hit)

    add_mid_swell(P, dsx=0.032 * coil + 0.078 * hit, dsy=0.052 * hit - 0.012 * settle)
    P.add('mid_lobe', x=2.8 * coil - 6.5 * thrust + 1.5 * settle)
    P.add('mid_tip', x=-2.8 * thrust, r=5.8 * coil - 8.5 * thrust + 2.8 * kick(t, 0.44, 2.2, 6.4), s=0.092 * hit)

    P.add('bottom_lobe', x=2.2 * coil - 4.4 * thrust, sx=0.036 * coil + 0.052 * hit, sy=0.028 * hit)
    P.add('bottom_tip', r=-1.3 * coil + 1.8 * thrust, sx=0.058 * hit)
    P.add('right_lobe', s=0.050 * coil + 0.062 * hit)

    # Heavy dorsal venous recoil (viscous, non-bouncy)
    P.add('back', x=9.8 * coil - 10.2 * thrust + 2.4 * settle, y=1.8 * coil - 2.6 * thrust)
    P.add('purple_mass', s=0.088 * coil + 0.052 * ej, x=3.6 * coil - 4.2 * thrust, r=-2.2 * coil + 2.6 * thrust)
    P.add('purple_top', r=-4.2 * coil + 6.8 * thrust - 1.8 * settle)
    P.add('ptube_l', r=6.8 * coil - 9.2 * thrust + 3.8 * kick(t, 0.45, 2.1, 6.0), s=0.072 * hit)
    P.add('ptube_r', r=-5.8 * coil + 8.8 * thrust - 3.6 * kick(t, 0.46, 2.1, 6.0), s=0.072 * hit)
    P.add('blue', x=7.8 * coil - 8.2 * thrust + 1.8 * settle)
    P.add('blue_up', r=-6.2 * coil + 8.8 * thrust - 3.0 * kick(t, 0.45, 2.2, 6.0), s=0.062 * hit)
    P.add('blue_down', r=5.2 * coil - 7.8 * thrust, s=0.062 * hit)
    P.add('plr_a', r=-4.6 * coil + 6.8 * thrust, s=0.052 * hit)
    P.add('plr_b', r=4.2 * coil - 5.8 * thrust, s=0.052 * hit)

    for k in GLOW_NAMES:
        g[k] = clamp(g[k] + 0.46 * coil + 0.98 * hit)
    return blend_one_shot(P, g, t, dur, blend_in=0.06, blend_out=0.22)

# ---------------------------------------------------------------- cast (1.40s)
def cast(t):
    dur = 1.40
    P, g = idle_base(t * 0.32, amp=0.28)
    # Three accelerating grounded squeezes (0.04s, 0.17s, 0.28s) -> 0.41s heavy isometric dorsal eruption -> viscous sag!
    b1 = 0.62 * pulse(t - 0.04, 0.022, 0.072)
    b2 = 0.88 * pulse(t - 0.17, 0.020, 0.068)
    b3 = 1.14 * pulse(t - 0.28, 0.018, 0.062)
    qb = b1 + b2 + b3

    gather = ease(seg(t, 0.0, 0.35)) * (1.0 - ease_out(seg(t, 0.37, 0.50)))
    erupt = systole(t - 0.365, rise=0.034, hold=0.065, relax=0.24)
    hold = ease_out(seg(t, 0.36, 0.45)) * (1.0 - ease(seg(t, 0.54, 1.16)))
    settle = heavy_recoil(t, 0.58, rise=0.08, settle=0.28)

    h_dy = -3.0 * gather + 10.5 * erupt + 3.0 * hold - 2.2 * settle
    h_dsy = -0.025 * gather + 0.056 * erupt + 0.020 * hold - 0.012 * settle
    P.add('heart', y=h_dy, sx=0.016 * qb + 0.026 * erupt, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    P.add('weed_l_anchor', x=-2.2 * erupt, y=1.5 * erupt - 1.0 * settle)

    P.add('bulb_base', s=0.046 * qb + 0.076 * erupt)
    P.add('bulb', sx=0.082 * qb + 0.168 * erupt, sy=0.090 * qb + 0.186 * erupt - 0.020 * settle, y=7.2 * erupt + 2.8 * hold - 1.8 * settle)
    add_atrium_swell(P, dsx=0.066 * qb + 0.132 * erupt, dsy=0.066 * qb + 0.146 * erupt)
    P.add('atrium', y=6.8 * erupt + 2.4 * hold - 1.5 * settle, r=-2.6 * erupt)
    add_mid_swell(P, dsx=0.046 * qb + 0.086 * erupt, dsy=0.046 * qb + 0.095 * erupt)
    P.add('bottom_lobe', sx=0.036 * qb + 0.056 * erupt, sy=0.030 * qb + 0.044 * erupt)
    P.add('right_lobe', sx=0.046 * qb + 0.086 * erupt, sy=0.046 * qb + 0.086 * erupt)

    P.add('back', y=5.8 * erupt + 1.8 * hold - 1.4 * settle)
    P.add('purple_mass', s=0.052 * qb + 0.116 * erupt, y=5.2 * erupt - 1.4 * settle)
    P.add('purple_top', y=6.2 * erupt + 1.8 * hold, r=-3.6 * hold)
    P.add('blue', y=4.2 * erupt - 1.0 * settle)
    P.add('blue_main', s=0.042 * qb + 0.092 * erupt)

    fl = 0.25 * qb + 1.0 * erupt + 0.34 * hold
    P.add('tube_a',    r=-13.4 * fl + 3.4 * kick(t, 0.50, 2.1, 6.0), s=0.122 * erupt)
    P.add('tube_b',    r=11.8 * fl - 3.0 * kick(t, 0.51, 2.1, 6.0),  s=0.122 * erupt)
    P.add('ptube_l',   r=13.0 * fl - 3.4 * kick(t, 0.52, 2.0, 5.8),  s=0.112 * erupt)
    P.add('ptube_r',   r=-13.6 * fl + 3.4 * kick(t, 0.53, 2.0, 5.8), s=0.112 * erupt)
    P.add('blue_up',   r=-12.8 * fl + 3.2 * kick(t, 0.51, 2.0, 5.8), s=0.112 * erupt)
    P.add('blue_down', r=11.6 * fl - 2.8 * kick(t, 0.52, 2.0, 5.8),  s=0.102 * erupt)
    P.add('plr_a',     r=11.4 * fl - 2.5 * kick(t, 0.53, 2.0, 5.8),  s=0.102 * erupt)
    P.add('plr_b',     r=10.8 * fl - 2.5 * kick(t, 0.54, 2.0, 5.8),  s=0.102 * erupt)
    P.add('mid_tip',   r=-6.6 * fl, s=0.072 * erupt)
    P.add('bottom_tip', r=-1.6 * fl, sx=0.056 * erupt)

    glow_lvl = clamp(0.14 + 0.54 * qb + 1.0 * erupt + 0.36 * hold)
    for k in GLOW_NAMES:
        g[k] = clamp(max(g[k], glow_lvl))
    return blend_one_shot(P, g, t, dur, blend_in=0.05, blend_out=0.24)

# ---------------------------------------------------------------- power_up (1.50s)
def power_up(t):
    dur = 1.50
    P, g = idle_base(t * 0.28, amp=0.24)
    # 0.00-0.36s: Iron-hard isometric ground clamp (muscles press dense & wide onto stone)
    # 0.40-0.84s: High-pressure cardiac overdrive with restrained fine isometric fiber tension (NOT loose jelly wobble!)
    lock = ease(seg(t, 0.0, 0.30)) * (1.0 - ease_out(seg(t, 0.36, 0.46)))
    surge_in = ease_out(seg(t, 0.38, 0.52))
    surge_out = 1.0 - ease(seg(t, 0.82, 1.34))
    overdrive = surge_in * surge_out
    burst = systole(t - 0.40, rise=0.036, hold=0.070, relax=0.26)
    # Restrained high-frequency isometric muscle-fiber tension quiver (0.6% tautness, zero sloshy wobble)
    tremor = math.sin(TAU * 11.5 * t) * overdrive
    tremor2 = math.cos(TAU * 13.0 * t) * overdrive

    h_dy = -6.8 * lock + 11.0 * overdrive + 3.0 * burst
    h_dsy = -0.050 * lock + 0.062 * overdrive + 0.018 * burst
    P.add('heart', y=h_dy, sx=0.038 * lock + 0.028 * overdrive + 0.005 * tremor, sy=h_dsy + 0.005 * tremor2)
    pin_floor(P, h_dy, h_dsy)

    P.add('weed_l_anchor', x=1.2 * lock - 2.5 * overdrive, y=-1.5 * lock + 1.6 * overdrive)

    P.add('bulb_base', sx=0.048 * lock + 0.065 * overdrive, sy=-0.038 * lock + 0.075 * overdrive)
    P.add('bulb', sx=0.062 * lock + 0.175 * overdrive + 0.032 * burst + 0.010 * tremor,
          sy=-0.062 * lock + 0.202 * overdrive + 0.032 * burst - 0.009 * tremor,
          y=-3.6 * lock + 10.2 * overdrive, r=2.0 * lock - 3.0 * overdrive + 0.7 * tremor)

    add_atrium_swell(P, dsx=0.052 * lock + 0.135 * overdrive + 0.009 * tremor2,
                     dsy=-0.052 * lock + 0.152 * overdrive - 0.009 * tremor2)
    P.add('atrium', y=-3.6 * lock + 8.8 * overdrive)

    add_mid_swell(P, dsx=0.052 * lock + 0.090 * overdrive, dsy=-0.036 * lock + 0.088 * overdrive)
    P.add('mid_lobe', x=0.7 * tremor)
    P.add('bottom_lobe', sx=0.052 * lock + 0.060 * overdrive, sy=-0.022 * lock + 0.046 * overdrive)
    P.add('right_lobe', sx=0.052 * lock + 0.090 * overdrive, sy=-0.032 * lock + 0.088 * overdrive)

    P.add('back', y=-3.6 * lock + 7.8 * overdrive, sx=0.032 * lock + 0.040 * overdrive)
    P.add('purple_mass', sx=0.052 * lock + 0.130 * overdrive + 0.009 * tremor,
          sy=-0.052 * lock + 0.142 * overdrive, y=-3.0 * lock + 7.8 * overdrive)
    P.add('purple_top', y=-3.0 * lock + 8.2 * overdrive, r=3.0 * lock - 4.5 * overdrive + 0.9 * tremor)
    P.add('blue', y=-2.5 * lock + 5.8 * overdrive)
    P.add('blue_main', sx=0.042 * lock + 0.100 * overdrive, sy=-0.042 * lock + 0.102 * overdrive)

    P.add('tube_a',    r=5.5 * lock - 12.6 * overdrive + 1.2 * tremor,  s=-0.04 * lock + 0.132 * overdrive)
    P.add('tube_b',    r=-4.5 * lock + 11.2 * overdrive - 1.2 * tremor, s=-0.04 * lock + 0.132 * overdrive)
    P.add('ptube_l',   r=-5.0 * lock + 12.2 * overdrive + 1.3 * tremor2, s=0.122 * overdrive)
    P.add('ptube_r',   r=5.0 * lock - 13.2 * overdrive - 1.3 * tremor2,  s=0.122 * overdrive)
    P.add('blue_up',   r=4.5 * lock - 12.2 * overdrive + 1.2 * tremor,   s=0.112 * overdrive)
    P.add('blue_down', r=-4.0 * lock + 10.6 * overdrive - 1.1 * tremor,  s=0.102 * overdrive)
    P.add('plr_a',     r=-4.0 * lock + 10.2 * overdrive + 1.0 * tremor2, s=0.102 * overdrive)
    P.add('plr_b',     r=-3.5 * lock + 9.6 * overdrive - 1.0 * tremor2,  s=0.102 * overdrive)

    shimmer = 0.08 * (0.5 + 0.5 * math.sin(TAU * 10.5 * t)) * overdrive
    g['atrium_glow']      = clamp(0.14 + 0.40 * lock + 0.88 * overdrive + 0.25 * burst + shimmer)
    g['bulb_glow']        = clamp(0.14 + 0.44 * lock + 0.92 * overdrive + 0.25 * burst + shimmer)
    g['mid_lobe_glow']    = clamp(0.12 + 0.36 * lock + 0.86 * overdrive + 0.25 * burst + shimmer)
    g['bottom_lobe_glow'] = clamp(0.12 + 0.36 * lock + 0.86 * overdrive + 0.25 * burst + shimmer)
    return blend_one_shot(P, g, t, dur, blend_in=0.06, blend_out=0.24)

# ---------------------------------------------------------------- summon (1.30s)
def summon(t):
    dur = 1.30
    P, g = idle_base(t * 0.32, amp=0.28)
    # Sequential deep peristaltic expulsion wave rolling right-to-left peaking ~0.50s
    w_plr   = systole(t - 0.05, rise=0.035, hold=0.035, relax=0.15)
    w_blue  = systole(t - 0.13, rise=0.035, hold=0.035, relax=0.15)
    w_purp  = systole(t - 0.21, rise=0.036, hold=0.040, relax=0.16)
    w_rlobe = systole(t - 0.29, rise=0.036, hold=0.040, relax=0.16)
    w_bulb  = systole(t - 0.37, rise=0.036, hold=0.045, relax=0.17)
    w_mid   = systole(t - 0.43, rise=0.036, hold=0.045, relax=0.17)
    w_front = systole(t - 0.47, rise=0.034, hold=0.050, relax=0.19)

    h_dy = 3.0 * w_bulb + 2.0 * w_front
    h_dsy = 0.020 * w_bulb + 0.016 * w_front
    P.add('heart', x=4.2 * w_purp - 7.5 * w_front, y=h_dy, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    P.add('weed_l_anchor', x=-3.5 * w_front, y=1.0 * w_front)

    P.add('purple_lr', s=0.112 * w_plr, x=-4.0 * w_plr)
    P.add('plr_a', r=12.2 * w_plr, s=0.142 * w_plr)
    P.add('plr_b', r=11.2 * w_plr, s=0.142 * w_plr)

    P.add('blue', x=-5.5 * w_blue, y=3.4 * w_blue)
    P.add('blue_main', s=0.112 * w_blue)
    P.add('blue_bottom', s=0.082 * w_blue)
    P.add('blue_up', r=-12.6 * w_blue, s=0.142 * w_blue)
    P.add('blue_down', r=11.6 * w_blue, s=0.132 * w_blue)

    P.add('back', x=-6.0 * w_purp, y=3.8 * w_purp)
    P.add('purple_mass', s=0.122 * w_purp)
    P.add('purple_top', y=4.8 * w_purp, r=-4.5 * w_purp)
    P.add('ptube_l', r=12.2 * w_purp, s=0.132 * w_purp)
    P.add('ptube_r', r=-12.6 * w_purp, s=0.132 * w_purp)

    P.add('right_lobe', s=0.116 * w_rlobe, x=-4.5 * w_rlobe)
    P.add('bulb_base', s=0.076 * w_bulb, x=-4.5 * w_bulb)
    P.add('bulb', sx=0.146 * w_bulb, sy=0.156 * w_bulb, x=-6.0 * w_bulb, y=5.2 * w_bulb, r=3.5 * w_bulb)

    add_mid_swell(P, dsx=0.102 * w_mid, dsy=0.090 * w_mid)
    P.add('mid_lobe', x=-6.5 * w_mid)
    P.add('mid_tip', r=-8.2 * w_mid, s=0.112 * w_mid, x=-3.6 * w_mid)
    P.add('bottom_lobe', sx=0.076 * w_mid, sy=0.048 * w_mid, x=-5.0 * w_mid)
    P.add('bottom_tip', r=-1.6 * w_mid, sx=0.082 * w_mid)

    add_atrium_swell(P, dsx=0.132 * w_front, dsy=0.132 * w_front)
    P.add('atrium', x=-9.2 * w_front, y=3.8 * w_front, r=3.6 * w_front)
    P.add('tube_a', r=-13.8 * w_front + 3.2 * kick(t, 0.58, 2.2, 6.2), s=0.162 * w_front, x=-5.2 * w_front)
    P.add('tube_b', r=12.2 * w_front - 3.0 * kick(t, 0.59, 2.2, 6.2), s=0.158 * w_front, x=-4.6 * w_front)

    g['bulb_glow']        = clamp(0.12 + 0.92 * w_bulb + 0.30 * w_rlobe)
    g['bottom_lobe_glow'] = clamp(0.10 + 0.88 * w_mid + 0.35 * w_bulb)
    g['mid_lobe_glow']    = clamp(0.10 + 0.88 * w_mid + 0.40 * w_front)
    g['atrium_glow']      = clamp(0.12 + 0.96 * w_front + 0.30 * w_mid)
    return blend_one_shot(P, g, t, dur, blend_in=0.05, blend_out=0.22)

# ---------------------------------------------------------------- hurt (0.65s)
def hurt(t):
    dur = 0.65
    P, g = idle_base(t * 0.28, amp=0.22)
    # Heavy blunt-force trauma from left: upper mass shears right (+24px) & compresses DOWN onto wet stone,
    # followed by viscous overdamped recovery (ZERO jelly bounce!).
    j_front = pulse(t - 0.005, 0.016, 0.115)
    j_mid   = pulse(t - 0.025, 0.020, 0.130)
    j_back  = pulse(t - 0.050, 0.024, 0.145)
    settle  = heavy_recoil(t, 0.12, rise=0.045, settle=0.18)

    h_dx = 18.0 * j_mid - 3.2 * settle
    h_dy = -6.0 * j_mid + 0.8 * settle
    h_dsy = -0.058 * j_mid + 0.010 * settle
    P.add('heart', x=h_dx, y=h_dy, sx=0.038 * j_mid - 0.008 * settle, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    P.add('weed_l_anchor', x=5.5 * j_front - 1.0 * settle, y=-2.0 * j_front)

    add_atrium_swell(P, dsx=0.046 * j_front, dsy=-0.068 * j_front)
    P.add('atrium', x=7.8 * j_front - 1.5 * settle, y=-4.2 * j_front, r=-5.2 * j_front)
    P.add('tube_a', r=11.5 * j_front - 3.8 * kick(t, 0.09, 2.9, 6.8), x=3.6 * j_front)
    P.add('tube_b', r=-9.8 * j_front + 3.5 * kick(t, 0.10, 2.9, 6.8), x=3.6 * j_front)

    add_mid_swell(P, dsx=0.046 * j_front, dsy=-0.058 * j_front)
    P.add('mid_lobe', x=7.2 * j_front - 1.4 * settle)
    P.add('mid_tip', r=9.2 * j_front - 3.2 * kick(t, 0.10, 2.7, 6.6))
    P.add('bottom_lobe', x=4.6 * j_mid, sx=0.042 * j_mid, sy=-0.025 * j_mid)
    P.add('bottom_tip', r=1.6 * j_front)

    P.add('bulb', x=6.8 * j_mid - 1.4 * settle, y=-4.8 * j_mid, sx=0.070 * j_mid, sy=-0.080 * j_mid + 0.015 * settle, r=-3.8 * j_mid)
    P.add('right_lobe', x=5.2 * j_back, sx=0.046 * j_back, sy=-0.044 * j_back)

    P.add('back', x=14.8 * j_back - 2.4 * settle, y=-3.6 * j_back)
    P.add('purple_mass', x=3.2 * j_back, y=-2.6 * j_back, sx=0.052 * j_back, sy=-0.052 * j_back, r=-2.6 * j_back)
    P.add('ptube_l', r=-8.8 * j_back + 3.2 * kick(t, 0.13, 2.5, 6.2))
    P.add('ptube_r', r=-9.8 * j_back + 3.2 * kick(t, 0.14, 2.5, 6.2))
    P.add('blue', x=12.8 * j_back - 2.0 * settle, y=-3.2 * j_back)
    P.add('blue_up', r=-9.2 * j_back + 3.2 * kick(t, 0.13, 2.6, 6.2))
    P.add('blue_down', r=-7.2 * j_back)
    P.add('plr_a', r=-7.6 * j_back)
    P.add('plr_b', r=-6.2 * j_back)

    g['atrium_glow']      = clamp(g['atrium_glow'] + 0.82 * j_front)
    g['mid_lobe_glow']    = clamp(g['mid_lobe_glow'] + 0.80 * j_front)
    g['bulb_glow']        = clamp(g['bulb_glow'] + 0.84 * j_mid)
    g['bottom_lobe_glow'] = clamp(g['bottom_lobe_glow'] + 0.78 * j_mid)
    return blend_one_shot(P, g, t, dur, blend_in=0.02, blend_out=0.16)

# ---------------------------------------------------------------- die (2.60s) & revive (1.80s)
DIE_END = 2.60

def collapse_pose(P, c):
    """Harmonized exsanguinated collapse pose: upper arches sag downward in lockstep toward the wet stone
    while ground-contact parts stay strictly on the floor (zero seam exposure & zero triangle inversions!)."""
    h_dy = -10.0 * c
    h_dsy = -0.085 * c
    P.add('heart', y=h_dy, sx=0.036 * c, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)
    P.add('bottom_lobe', sx=0.042 * c, sy=-0.020 * c)
    P.add('right_lobe', sx=0.042 * c, sy=-0.042 * c)

    # Settle weed_l_anchor down-and-right alongside atrium/mid_lobe so weed_left slumps naturally onto the collapsed heart
    P.add('weed_l_anchor', x=13.5 * c, y=-15.5 * c)

    # Tall upper cyan chambers deflate & sag downward harmoniously
    P.add('bulb_base', y=-4.8 * c, sx=0.035 * c, sy=-0.062 * c)
    P.add('bulb', y=-15.5 * c, sx=-0.038 * c, sy=-0.145 * c, r=-5.2 * c)
    add_atrium_swell(P, dsx=-0.030 * c, dsy=-0.108 * c)
    P.add('atrium', y=-14.2 * c, r=5.8 * c)
    add_mid_swell(P, dsx=0.036 * c, dsy=-0.065 * c)
    P.add('mid_lobe', y=-5.5 * c)

    # Dorsal purple & blue trunks sag in harmony with bulb so no shoulder step ever opens
    P.add('back', y=-6.2 * c)
    P.add('purple_mass', y=-16.8 * c, sx=0.050 * c, sy=-0.128 * c, r=-3.0 * c)
    P.add('purple_top', y=-12.8 * c, r=-11.5 * c)
    P.add('purple_lr', y=4.2 * c, sx=0.025 * c, sy=-0.035 * c)

    P.add('blue', y=-8.2 * c)
    P.add('blue_main', y=-11.2 * c, sx=0.040 * c, sy=-0.095 * c, r=-2.8 * c)
    P.add('blue_bottom', y=6.5 * c, r=3.5 * c)

    # Limp drooping tubes (14-22 deg downward!)
    P.add('tube_a',    r=18.5 * c, y=-5.5 * c, s=-0.05 * c)
    P.add('tube_b',    r=15.5 * c, y=-4.0 * c, s=-0.05 * c)
    P.add('mid_tip',   r=14.0 * c, y=-2.5 * c)
    P.add('bottom_tip', r=1.5 * c)
    P.add('ptube_l',   r=19.0 * c, s=-0.05 * c)
    P.add('ptube_r',   r=-20.5 * c, s=-0.05 * c)
    P.add('blue_up',   r=-21.5 * c, s=-0.05 * c)
    P.add('blue_down', r=-14.5 * c, y=-2.5 * c, s=-0.04 * c)
    P.add('plr_a',     r=-16.0 * c, s=-0.04 * c)
    P.add('plr_b',     r=-10.0 * c, s=-0.03 * c)

def tint_of(dark):
    return (1.0 - 0.46 * dark, 1.0 - 0.43 * dark, 1.0 - 0.35 * dark)

def die(t):
    P = Pose()
    u_start = 1.0 - ease(seg(t, 0.0, 0.09))
    if u_start > 0.0:
        P.lerp_to(IDLE0_POSE, u_start)

    sp_a = 0.95 * pulse(t - 0.04, 0.024, 0.105) + 0.65 * pulse(t - 0.41, 0.022, 0.10) + 0.30 * pulse(t - 0.76, 0.025, 0.11)
    sp_b = 0.85 * pulse(t - 0.22, 0.024, 0.105) + 0.70 * pulse(t - 0.60, 0.024, 0.105) + 0.22 * pulse(t - 1.05, 0.030, 0.12)
    fib_env = (1.0 - ease(seg(t, 0.85, 1.62))) * ease(seg(t, 0.0, 0.05))
    fib = math.sin(TAU * 11.5 * t) * fib_env

    c = 0.35 * ease_in(seg(t, 0.88, 2.30)) + 0.65 * ease(seg(t, 0.88, 2.30))

    h_dy = 1.8 * sp_b
    h_dsy = 0.020 * sp_b
    P.add('heart', x=3.8 * fib, y=h_dy, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)
    add_atrium_swell(P, dsx=0.070 * sp_a, dsy=0.070 * sp_a)
    P.add('atrium', x=-3.0 * sp_a + 2.0 * fib)
    P.add('bulb', s=0.095 * sp_b, x=-2.2 * fib)
    add_mid_swell(P, dsx=0.050 * sp_a, dsy=0.050 * sp_a)
    P.add('purple_mass', s=0.060 * sp_b, x=2.6 * fib)
    P.add('tube_a', r=-7.0 * sp_a)
    P.add('tube_b', r=6.5 * sp_a)
    P.add('ptube_l', r=6.5 * sp_b)
    P.add('ptube_r', r=-6.5 * sp_b)

    collapse_pose(P, c)

    flicker = 0.55 + 0.45 * math.sin(TAU * 9.5 * t) * math.cos(TAU * 3.7 * t)
    alive_glow = 1.0 - ease(seg(t, 1.45, 2.22))
    g = {
        'atrium_glow':      clamp((IDLE0_GLOW['atrium_glow'] * u_start + (0.16 + 0.72 * sp_a) * flicker) * alive_glow),
        'mid_lobe_glow':    clamp((IDLE0_GLOW['mid_lobe_glow'] * u_start + (0.14 + 0.62 * sp_a) * flicker) * alive_glow),
        'bulb_glow':        clamp((IDLE0_GLOW['bulb_glow'] * u_start + (0.16 + 0.80 * sp_b) * (1.0 - 0.45 * flicker)) * alive_glow),
        'bottom_lobe_glow': clamp((IDLE0_GLOW['bottom_lobe_glow'] * u_start + (0.14 + 0.65 * sp_b) * (1.0 - 0.45 * flicker)) * alive_glow),
    }
    if t >= 2.32:
        g = {k: 0.0 for k in GLOW_NAMES}
    dark = ease(seg(t, 0.88, 2.32))
    return P, g, tint_of(dark), c

def revive(t):
    dur = 1.80
    P = Pose()
    rise_u = ease_out(seg(t, 0.28, 1.18))
    c = 1.0 - rise_u
    collapse_pose(P, c)

    b1 = systole(t - 0.10, rise=0.030, hold=0.035, relax=0.15)
    b2 = systole(t - 0.50, rise=0.035, hold=0.055, relax=0.22)
    b3 = systole(t - 0.94, rise=0.035, hold=0.045, relax=0.20)
    settle = heavy_recoil(t, 1.05, rise=0.07, settle=0.26) * (1.0 - ease(seg(t, 1.42, dur)))

    h_dy = 3.0 * b1 + 8.0 * b2 + 4.0 * b3 - 2.0 * settle
    h_dsy = 0.025 * b1 + 0.058 * b2 + 0.032 * b3 - 0.012 * settle
    P.add('heart', y=h_dy, sx=0.025 * b2, sy=h_dsy)
    pin_floor(P, h_dy, h_dsy)

    P.add('bulb', s=0.07 * b1 + 0.155 * b2 + 0.09 * b3, y=5.0 * b2 - 1.5 * settle)
    add_atrium_swell(P, dsx=0.055 * b1 + 0.115 * b2 + 0.075 * b3, dsy=0.055 * b1 + 0.115 * b2 + 0.075 * b3)
    P.add('atrium', y=4.0 * b2 - 1.2 * settle)
    add_mid_swell(P, dsx=0.04 * b1 + 0.085 * b2 + 0.055 * b3, dsy=0.04 * b1 + 0.085 * b2 + 0.055 * b3)
    P.add('bottom_lobe', sx=0.055 * b2 + 0.035 * b3, sy=0.035 * b2)
    P.add('right_lobe', s=0.065 * b2 + 0.045 * b3)
    P.add('purple_mass', s=0.08 * b2 + 0.05 * b3, y=4.0 * b2 - 1.2 * settle)
    P.add('tube_a', r=-8.0 * b2 - 5.0 * b3, s=0.10 * b2)
    P.add('tube_b', r=7.0 * b2 + 4.5 * b3, s=0.10 * b2)
    P.add('ptube_l', r=8.0 * b2, s=0.08 * b2)
    P.add('ptube_r', r=-8.5 * b2, s=0.08 * b2)
    P.add('blue_up', r=-8.0 * b2, s=0.08 * b2)

    e_idle = ease(seg(t, 1.32, dur))
    if e_idle > 0.0:
        P.lerp_to(IDLE0_POSE, e_idle)

    g_raw = {
        'atrium_glow':      clamp(0.55 * b1 + 0.95 * b2 + 0.72 * b3 + 0.12 * rise_u),
        'mid_lobe_glow':    clamp(0.45 * b1 + 0.88 * b2 + 0.65 * b3 + 0.10 * rise_u),
        'bulb_glow':        clamp(0.60 * b1 + 1.00 * b2 + 0.78 * b3 + 0.12 * rise_u),
        'bottom_lobe_glow': clamp(0.45 * b1 + 0.88 * b2 + 0.68 * b3 + 0.10 * rise_u),
    }
    g = lerp_glow(g_raw, IDLE0_GLOW, e_idle)
    dark = 1.0 - ease(seg(t, 0.18, 1.22))
    return P, g, tint_of(dark), c

ANIMS = {
    'idle_loop': (3.00, True),
    'attack':    (1.25, False),
    'cast':      (1.40, False),
    'hurt':      (0.65, False),
    'die':       (DIE_END, False),
    'summon':    (1.30, False),
    'power_up':  (1.50, False),
    'revive':    (1.80, False),
}
