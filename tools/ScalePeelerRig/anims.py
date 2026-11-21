"""Expressive multi-segment animations for Scale-Peeler Wyrm."""
import math
from rigutil import Frame, TAU, key, hold, bump, ease, seg, kick

ANIMS = {
    'idle_loop': (4.0, True),
    'attack': (1.06, False),
    'slam': (1.28, False),
    'cast': (1.40, False),
    'flurry': (1.32, False),
    'hurt': (0.58, False),
    'die': (1.45, False),
    'revive': (1.10, False),
    'summon': (0.95, False),
    'power_up': (0.95, False),
}

EVENTS = {
    'attack': [('bite_contact', 0.46)],
    'slam': [('slam_contact', 0.58)],
    'cast': [('siphon_start', 0.30), ('siphon_peak', 0.72)],
    'flurry': [('flurry_hit_0', 0.36), ('flurry_hit_1', 0.72)],
    'die': [('collapse', 1.08)],
}

COLD_START = ('summon', 'revive')


def wave(t, phase=0.0, period=4.0):
    return math.sin(TAU * t / period - phase) - math.sin(-phase)


def blink(f, amt):
    # Scale resting upper-lid crescent down smoothly to cover eyeballs cleanly with zero invert
    f.P.add('eye_far_lid', sy=2.5 * amt, y=-5.5 * amt)
    f.P.add('eye_near_lid', sy=2.7 * amt, y=-7.5 * amt)


def sway_plates(f, val, sx=0.0, sy=0.0):
    for i, sgn in enumerate([1.0, 0.85, 0.7, 0.55]):
        f.P.add(f'plate_{i}', r=val * sgn, sx=sx, sy=sy)


def idle(t):
    f = Frame()
    w = wave(t)
    # Slow heavy breathing depth inside burrow + lively peristalsis wave rising to neck & head
    f.P.add('trunk_base', y=5.5 * w, sy=0.022 * w, sx=-0.014 * w)
    chain = ['seg_0', 'seg_1', 'seg_2', 'seg_3', 'seg_4', 'seg_5', 'neck']
    for i, b in enumerate(chain):
        hw = wave(t, 0.34 * i)
        f.P.add(b, r=(2.1 if i < 3 else -2.3) * hw, sx=0.015 * hw, sy=0.010 * hw)
    f.P.add('head', r=3.8 * wave(t, 1.85), y=4.6 * wave(t, 1.4))

    # Lazy chisel-beak scraping nibble every 2s
    chew = wave(t, 0.25, 2.0)
    f.P.add('jaw_upper', r=-5.2 * max(0.0, chew) + 3.2 * max(0.0, -chew))
    f.P.add('jaw_lower', r=6.4 * max(0.0, chew) - 3.6 * max(0.0, -chew))
    f.P.add('mouth_core', sx=0.04 * chew, sy=0.05 * chew)

    # Dorsal armor shard gentle rhythmic gleam/rocking
    for i in range(4):
        pw = wave(t, 0.6 + 0.25 * i)
        f.P.add(f'plate_{i}', r=3.4 * pw)

    # Double sleepy blink (around t=1.15s and double flicker near t=2.85s)
    b1 = bump(t, 1.02, 1.14, 1.28)
    b2 = bump(t, 2.75, 2.86, 2.98) + 0.75 * bump(t, 3.00, 3.09, 3.20)
    blink(f, max(b1, b2))
    return f


def attack(t):
    """Chisel Bite: coil back with gaping scrapers, whip lunge LEFT and clamp jaws violently shut."""
    f = Frame()
    coil = hold(t, 0.0, 0.22, 0.28, 0.42)
    lunge = key(t, (0.0, 0.0), (0.24, -0.14), (0.34, 0.38), (0.46, 1.0), (0.56, 0.88), (0.78, -0.12), (0.94, 0.03), (1.06, 0.0))

    f.P.add('trunk_base', x=10 * coil - 18 * lunge, y=-8 * coil + 10 * lunge, r=-3.5 * coil + 8.0 * lunge, sy=-0.02 * coil + 0.04 * lunge)
    for i, b in enumerate(['seg_0', 'seg_1', 'seg_2', 'seg_3']):
        f.P.add(b, r=-3.0 * coil + (3.6 + i * 0.3) * lunge)
    f.P.add('seg_4', r=-3.6 * coil + 2.4 * lunge)
    f.P.add('seg_5', r=-3.2 * coil + 2.0 * lunge)
    f.P.add('neck', r=-4.5 * coil + 4.5 * lunge, x=-10 * lunge)
    f.P.add('head', r=-8.0 * coil + 9.5 * lunge, x=-24 * lunge, y=-8 * lunge)

    # Open wide during windup/lunge, snap SHUT hard on bite_contact (0.46s), nibble bounce out
    open_w = hold(t, 0.06, 0.26, 0.37, 0.45)
    clamp = key(t, (0.0, 0.0), (0.38, 0.0), (0.46, 1.0), (0.58, 0.82), (0.78, 0.12), (1.06, 0.0))
    f.P.add('jaw_upper', r=-20.0 * open_w + 15.5 * clamp)
    f.P.add('jaw_lower', r=24.0 * open_w - 5.5 * clamp)
    f.P.add('mouth_core', sx=0.14 * open_w - 0.10 * clamp, sy=0.12 * open_w - 0.08 * clamp)

    # Plates fan back on windup and whip forward on impact
    bite_rattle = 5.5 * kick(t, 0.46, 4.5, 6.0) * (1.0 - ease(seg(t, 0.78, 1.00)))
    sway_plates(f, 9.0 * coil - 10.0 * lunge + bite_rattle)
    blink(f, 0.85 * bump(t, 0.40, 0.48, 0.66))
    return f


def slam(t):
    """Plated Slam: rear HIGH out of burrow flexing dorsal crystals, then whip-smash forward DOWN onto enemy."""
    f = Frame()
    rear = key(t, (0.0, 0.0), (0.18, 0.72), (0.34, 1.0), (0.42, 0.85), (0.56, 0.0), (1.28, 0.0))
    smash = key(t, (0.0, 0.0), (0.38, 0.0), (0.50, 0.58), (0.58, 1.0), (0.70, 0.84), (0.96, -0.08), (1.16, 0.02), (1.28, 0.0))
    tremor = kick(t, 0.58, 5.5, 5.5) * hold(t, 0.58, 0.60, 0.90, 1.15)

    f.P.add('trunk_base', x=10 * rear - 18 * smash, y=10 * rear - 26 * smash + 4 * tremor, r=-6.5 * rear + 18.0 * smash + 2.0 * tremor, sy=0.05 * rear - 0.06 * smash)
    for i, b in enumerate(['seg_0', 'seg_1', 'seg_2']):
        f.P.add(b, r=-5.0 * rear + 4.2 * smash + 1.5 * tremor, sy=0.04 * rear)
    f.P.add('seg_3', r=-5.0 * rear + 2.8 * smash + 1.5 * tremor)
    f.P.add('seg_4', r=-4.5 * rear + 1.8 * smash)
    f.P.add('seg_5', r=-4.0 * rear + 1.8 * smash)
    f.P.add('neck', r=-3.0 * rear + 5.0 * smash)
    f.P.add('head', r=-8.5 * rear + 12.5 * smash, y=-12 * smash + 5 * tremor, x=-16 * smash)

    # Jaws clamp grimly tight during rear/smash
    clench = hold(t, 0.14, 0.34, 0.72, 1.05)
    f.P.add('jaw_upper', r=13.5 * clench)
    f.P.add('jaw_lower', r=-7.5 * clench)
    f.P.add('mouth_core', sx=-0.08 * clench, sy=-0.08 * clench)

    # Dorsal armor crystals flare dramatically upright on rear & rattle violently on slam_contact!
    sway_plates(f, -13.0 * rear + 14.0 * smash + 8.0 * tremor, sx=0.18 * rear + 0.12 * smash, sy=0.22 * rear + 0.12 * smash)
    blink(f, 0.95 * bump(t, 0.52, 0.60, 0.84))
    return f


def cast(t):
    """Armor Siphon: gape jaws ultra-wide with cyan vacuum vortex core, gulp pulse down segments, pop dorsal shards."""
    f = Frame()
    inhale = hold(t, 0.10, 0.32, 0.80, 1.05)
    gulp = bump(t, 0.72, 0.88, 1.18)

    f.P.add('trunk_base', y=-10 * inhale + 12 * gulp, sy=-0.025 * inhale + 0.03 * gulp)
    for i, b in enumerate(['seg_0', 'seg_1', 'seg_2', 'seg_3', 'seg_4', 'seg_5']):
        pulse = bump(t, 0.30 + (5 - i) * 0.055, 0.52 + (5 - i) * 0.055, 0.85 + (5 - i) * 0.055)
        gulp_wave = bump(t, 0.66 + (5 - i) * 0.045, 0.80 + (5 - i) * 0.045, 1.08 + (5 - i) * 0.045)
        f.P.add(b, r=-2.8 * inhale + 2.2 * gulp_wave, sx=0.055 * pulse + 0.065 * gulp_wave, sy=0.04 * pulse)

    f.P.add('neck', r=-5.0 * inhale + 4.0 * gulp, x=-10 * inhale)
    f.P.add('head', r=-8.0 * inhale + 6.0 * gulp, y=8 * inhale - 6 * gulp, x=-14 * inhale)

    # Wide vacuum suction gape then crisp swallow bite shut around t=0.90s!
    open_v = hold(t, 0.14, 0.30, 0.72, 0.86)
    swallow_shut = bump(t, 0.80, 0.92, 1.15)
    # Vibrating inhale tremble at peak suction
    flutter = 2.0 * math.sin(TAU * 9.0 * t) * hold(t, 0.30, 0.34, 0.70, 0.76)
    f.P.add('jaw_upper', r=-21.0 * open_v + 13.5 * swallow_shut + flutter)
    f.P.add('jaw_lower', r=25.0 * open_v - 7.5 * swallow_shut - flutter)
    f.P.add('mouth_core', sx=0.28 * open_v - 0.08 * swallow_shut, sy=0.28 * open_v - 0.08 * swallow_shut)

    # Cyan siphon core glows inside gullet from siphon_start through peak!
    f.glow['mouth_glow'] = hold(t, 0.22, 0.36, 0.76, 0.96)

    # 4 dorsal crystal shards swell & flare out sequentially as stolen Block arrives!
    for i in range(4):
        pop = bump(t, 0.44 + i * 0.08, 0.68 + i * 0.08, 1.05 + i * 0.07)
        f.P.add(f'plate_{i}', sx=0.20 * pop, sy=0.24 * pop, r=-7.0 * pop)
    return f


def flurry(t):
    """Burrow Scrape: rapid twin low-crouch double chisel strike!"""
    f = Frame()
    duck = hold(t, 0.0, 0.14, 0.92, 1.18)
    s1 = key(t, (0.0, 0.0), (0.14, -0.14), (0.28, 0.45), (0.36, 1.0), (0.46, 0.10), (0.56, -0.18), (0.64, 0.40), (0.72, 1.08), (0.85, 0.25), (1.08, -0.06), (1.32, 0.0))

    f.P.add('trunk_base', y=-12 * duck + 8 * s1, x=8 * duck - 18 * s1, r=6.5 * s1)
    for b in ['seg_0', 'seg_1', 'seg_2', 'seg_3']:
        f.P.add(b, r=-2.5 * duck + 3.6 * s1)
    f.P.add('seg_4', r=-3.0 * duck + 2.8 * s1)
    f.P.add('seg_5', r=-2.0 * duck + 2.4 * s1)
    f.P.add('neck', r=-3.5 * duck + 4.5 * s1)
    f.P.add('head', r=-4.5 * duck + 7.5 * s1, x=-22 * s1)

    # Snap #1 at t=0.36, open again 0.48..0.60, Snap #2 at t=0.72!
    open1 = bump(t, 0.10, 0.24, 0.35)
    snap1 = bump(t, 0.30, 0.36, 0.48)
    open2 = bump(t, 0.45, 0.56, 0.70)
    snap2 = bump(t, 0.65, 0.72, 0.90)
    f.P.add('jaw_upper', r=-18 * (open1 + open2) + 14.5 * (snap1 + snap2))
    f.P.add('jaw_lower', r=22 * (open1 + open2) - 7.5 * (snap1 + snap2))
    sway_plates(f, 5.0 * duck - 8.5 * s1)
    return f


def hurt(t):
    f = Frame()
    h = key(t, (0.0, 0.0), (0.09, 1.0), (0.20, 0.55), (0.34, -0.16), (0.48, 0.05), (0.58, 0.0))
    f.P.add('trunk_base', x=22 * h, y=-10 * h, sy=-0.03 * h, sx=0.025 * h)
    for i, b in enumerate(['seg_0', 'seg_1', 'seg_2', 'seg_3', 'seg_4']):
        f.P.add(b, r=-3.5 * h)
    f.P.add('neck', r=-4.5 * h)
    f.P.add('head', r=-6.0 * h, x=8 * h)
    f.P.add('jaw_upper', r=-10.0 * max(0.0, h))
    f.P.add('jaw_lower', r=12.0 * max(0.0, h))
    sway_plates(f, 9.0 * h)
    blink(f, 1.05 * bump(t, 0.0, 0.08, 0.34))
    return f


def _death_pose(u):
    f = Frame()
    h = ease(u)
    # Limp collapse pitching forward from burrow base while receding cleanly INSIDE rubble rim (no bottom poke-out!)
    f.P.add('trunk_base', x=-26 * h, y=-40 * h, r=28.0 * h, sx=0.05 * h, sy=-0.20 * h)
    f.P.add('seg_0', r=4.5 * h, sy=-0.06 * h)
    f.P.add('seg_1', r=4.5 * h, sy=-0.06 * h)
    f.P.add('seg_2', r=3.5 * h)
    f.P.add('seg_3', r=1.0 * h)
    f.P.add('seg_4', r=0.5 * h)
    f.P.add('seg_5', r=1.0 * h)
    f.P.add('neck', r=4.5 * h)
    f.P.add('head', r=8.5 * h, y=-4 * h)
    f.P.add('jaw_upper', r=12.0 * h)
    f.P.add('jaw_lower', r=-7.0 * h)
    sway_plates(f, 8.0 * h, sx=-0.06 * h, sy=-0.08 * h)
    blink(f, 1.05 * h)
    f.tint = (1.0 - 0.24 * h, 1.0 - 0.25 * h, 1.0 - 0.20 * h, 1.0)
    return f


def die(t):
    f = _death_pose(seg(t, 0.14, 1.24))
    spasm = bump(t, 0.0, 0.10, 0.26)
    f.P.add('trunk_base', x=16 * spasm)
    f.P.add('jaw_upper', r=-12 * spasm)
    f.P.add('jaw_lower', r=15 * spasm)
    f.P.add('head', r=-8 * spasm)
    return f


def revive(t):
    return _death_pose(1.0 - ease(seg(t, 0.0, 1.05)))


def summon(t):
    f = Frame()
    arrive = 1.0 - ease(seg(t, 0.0, 0.72))
    pop = bump(t, 0.35, 0.62, 0.95)
    f.P.add('trunk_base', y=-135 * arrive + 12 * pop, sx=-0.08 * arrive, sy=-0.14 * arrive)
    for b in ['seg_1', 'seg_2', 'seg_3']:
        f.P.add(b, r=6.0 * arrive - 3.0 * pop)
    f.P.add('head', r=8.0 * arrive - 4.0 * pop)
    f.P.add('jaw_upper', r=10 * arrive - 12 * pop)
    f.P.add('jaw_lower', r=-9 * arrive + 14 * pop)
    sway_plates(f, -6.0 * pop, sx=0.06 * pop, sy=0.06 * pop)
    return f


def power_up(t):
    f = Frame()
    gather = bump(t, 0.0, 0.26, 0.56)
    swell = bump(t, 0.22, 0.52, 0.95)
    f.P.add('trunk_base', y=-12 * gather + 22 * swell, sy=-0.03 * gather + 0.04 * swell)
    for i, b in enumerate(['seg_0', 'seg_1', 'seg_2', 'seg_3', 'seg_4']):
        f.P.add(b, r=-3.0 * gather + 2.5 * swell, sx=0.05 * swell)
    f.P.add('head', r=-7.0 * swell, y=6 * swell)
    f.P.add('jaw_upper', r=-13.0 * swell)
    f.P.add('jaw_lower', r=16.0 * swell)
    f.glow['mouth_glow'] = 0.55 * swell
    sway_plates(f, -10.0 * swell, sx=0.16 * swell, sy=0.18 * swell)
    return f


FUNCS = {
    'idle_loop': idle,
    'attack': attack,
    'slam': slam,
    'cast': cast,
    'flurry': flurry,
    'hurt': hurt,
    'die': die,
    'revive': revive,
    'summon': summon,
    'power_up': power_up,
}
