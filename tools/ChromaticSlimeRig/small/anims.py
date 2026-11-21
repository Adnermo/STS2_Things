"""Chromatic Slime (Small) v5 — Planted-Base Zero-Rotation Vanilla STS2 Fluid Physics + Spit Projectiles.
- STRICT ZERO BONE SCALING (`scaleX == 1.0, scaleY == 1.0` across all 17 bones & all 12 clips)
- STRICT ZERO GEL BODY ROTATION (`r == 0.0 deg` across all 11 gel body bones & all 12 clips!)
- 8 Standard Clips: `idle_loop` (2.80s), `spawn` (0.58s), `attack` (0.78s), `cast` (0.92s),
  `power_up` (0.85s), `hurt` (0.38s), `die` (0.80s), `revive` (0.68s)
- 4 Bespoke Strain-Signature Clips:
  - `attack_crimson` (0.84s): Flat-floor shear (-76px + -26px crown), +22px axial harpoon shoot, ruby shard streak (-320px+), 24Hz shimmer
  - `attack_amber`   (0.86s): Flat curling-stone skid (-92px), 5f seismic hit-stop, -16px stone anvil visor drop, ground pebble bounce, Block crouch
  - `cast_azure`     (0.95s): Planted base (dx=0, dy=0), 20Hz froth boil, +32px pump -> -14px nozzle pop, high parabolic acid glob (-360px+, +95px apex)
  - `attack_azure`   (0.80s): Differential puddle tidal wave (foot_l -108px, foot_r -44px = 64px stretch), -24px pancake splat + acid splash (-210px+)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from rigutil import TAU, Frame, clamp, ease, ease_in, ease_out, hold, kick, seg

ANIMS = {
    "idle_loop": (2.80, True),
    "spawn": (0.58, False),
    "attack": (0.78, False),
    "cast": (0.92, False),
    "power_up": (0.85, False),
    "hurt": (0.38, False),
    "die": (0.80, False),
    "revive": (0.68, False),
    "attack_crimson": (0.84, False),
    "attack_amber": (0.86, False),
    "cast_azure": (0.95, False),
    "attack_azure": (0.80, False),
}

COLD_START = ("spawn", "revive")

EVENTS = {
    "spawn": [("spawn_land", 0.34)],
    "attack": [("attack_contact", 0.34)],
    "cast": [("cast_release", 0.44)],
    "power_up": [("power_peak", 0.40)],
    "hurt": [],
    "die": [("rupture_burst", 0.26)],
    "revive": [],
    "attack_crimson": [("attack_contact", 0.34)],
    "attack_amber": [("attack_contact", 0.34)],
    "cast_azure": [("cast_release", 0.44)],
    "attack_azure": [("attack_contact", 0.34)],
}


def pow_in(u: float, p: float = 3.2) -> float:
    return clamp(u) ** p


def plateau(t: float, t0: float, t1: float, t2: float, t3: float, p: float = 3.0) -> float:
    if t <= t0 or t >= t3:
        return 0.0
    if t < t1:
        return pow_in(seg(t, t0, t1), p)
    if t <= t2:
        return 1.0
    return 1.0 - ease(seg(t, t2, t3))


def bump(t: float, t0: float, t1: float, t2: float) -> float:
    return ease(seg(t, t0, t1)) * (1.0 - ease(seg(t, t1, t2)))


def fluid_volume(
    f: Frame,
    rise_y: float = 0.0,
    slosh_x: float = 0.0,
    puddle_spread: float = 0.0,
    foot_spread_scale: float = 0.55,
) -> None:
    """Volume-conserving ZERO-ROTATION (0.00 deg), ZERO-SCALE planted-base gel deformation."""
    bulge_x = -0.58 * rise_y + puddle_spread
    f.P.add("belly_mid", x=0.52 * slosh_x, y=0.52 * rise_y)
    f.P.add("crown", x=0.48 * slosh_x, y=0.48 * rise_y)
    f.P.add("flank_l", x=-bulge_x + 0.30 * slosh_x, y=0.22 * rise_y)
    f.P.add("flank_r", x=bulge_x + 0.30 * slosh_x, y=0.22 * rise_y)
    if abs(foot_spread_scale) > 1e-5:
        f.P.add("foot_l", x=-foot_spread_scale * bulge_x)
        f.P.add("foot_r", x=foot_spread_scale * bulge_x)


def eye_ctrl(f: Frame, gaze_x: float = 0.0, gaze_y: float = 0.0, close: float = 0.0) -> None:
    """Unscaled, unrotated 3-layer cyclops eye control inside per-skin hardware `eye_clip`."""
    if abs(gaze_x) > 1e-4 or abs(gaze_y) > 1e-4:
        f.P.add("iris_b", x=gaze_x, y=gaze_y)
    if close > 1e-4:
        # Wide (+40px each side), 86px-tall eyelid sweeps cleanly across entire socket with zero side/bottom gap!
        f.P.add("eyelid_b", y=-68.0 * clamp(close))


def crown_trio(
    f: Frame,
    lift: float = 0.0,
    flare: float = 0.0,
    pitch: float = 0.0,
    shiver: float = 0.0,
) -> None:
    """Drive ONLY the 3 rigid crown pieces (`crest_l_b`, `crest_c_b`, `crest_r_b`)."""
    f.P.add("crest_l_b", x=-0.55 * flare, y=0.85 * lift, r=pitch + 0.85 * flare + shiver)
    f.P.add("crest_c_b", x=0.0, y=1.05 * lift, r=pitch - 0.70 * shiver)
    f.P.add("crest_r_b", x=0.60 * flare, y=0.80 * lift, r=pitch - 0.85 * flare + 0.80 * shiver)


def set_spit(
    f: Frame,
    t: float,
    t_appear: float,
    t_full_end: float,
    t_fade_end: float,
    dur: float,
    gx: float,
    gy: float,
    gr: float = 0.0,
) -> None:
    """Animate `spit_glob` alpha & pose cleanly without any visible flyback ghosting."""
    if t < t_appear - 0.015:
        f.extra["slot_alpha"] = {"spit_glob": 0.0}
        return
    if t <= t_appear:
        alpha = ease_out(seg(t, t_appear - 0.015, t_appear))
    elif t <= t_full_end:
        alpha = 1.0
    elif t < t_fade_end:
        alpha = 1.0 - ease(seg(t, t_full_end, t_fade_end))
    else:
        alpha = 0.0

    f.extra["slot_alpha"] = {"spit_glob": float(clamp(alpha))}

    # Hold trajectory 0.03s past alpha==0 before easing invisibly back to setup (0, 0, 0)
    t_reset = min(dur - 0.01, t_fade_end + 0.03)
    if t <= t_reset:
        f.P.add("spit_glob_b", x=gx, y=gy, r=gr)
    else:
        ret = 1.0 - ease(seg(t, t_reset, dur))
        f.P.add("spit_glob_b", x=gx * ret, y=gy * ret, r=gr * ret)


def settle_springs(f: Frame, t: float, dur: float, window: float = 0.14) -> None:
    if t > dur - window:
        f.extra["spring_mix"] = 1.0 - ease(seg(t, dur - window, dur))


# ==============================================================================
# 8 STANDARD CLIPS (ALL GEL BODY BONES r == 0.00 deg!)
# ==============================================================================


def idle_loop(t: float) -> Frame:
    """Pure vertical droplet breathing + peristaltic horizontal slosh, darting gaze & blink (0.0 deg body rot)."""
    f = Frame()
    ph = TAU * (t / 2.80)
    s = math.sin(ph)
    s_lag = math.sin(ph - 0.52) - math.sin(-0.52)
    s_lag2 = math.sin(ph - 0.96) - math.sin(-0.96)
    env = hold(t, 0.04, 0.24, 2.42, 2.72)

    fluid_volume(f, rise_y=11.5 * s * env, slosh_x=-8.5 * s_lag * env)
    f.P.add("inner_core", x=-9.0 * s_lag * env, y=6.5 * s_lag * env)

    # Phase-staggered 3-crest float
    f.P.add("crest_l_b", x=-1.6 * s_lag * env, y=3.4 * s_lag * env, r=2.6 * s * env)
    f.P.add("crest_c_b", x=-0.6 * s_lag * env, y=4.4 * s_lag * env, r=2.2 * s_lag * env)
    f.P.add("crest_r_b", x=1.7 * s_lag2 * env, y=3.1 * s_lag2 * env, r=-2.5 * s_lag2 * env)

    # Suspicious darting cyclops gaze + crisp grumpy blink
    gaze_left = plateau(t, 0.42, 0.54, 1.05, 1.22, p=2.4)
    gaze_right = plateau(t, 1.62, 1.76, 2.18, 2.42, p=2.4)
    gaze_x = -8.5 * gaze_left + 6.5 * gaze_right
    gaze_y = -2.6 * gaze_left + 1.8 * gaze_right
    blink = plateau(t, 1.18, 1.24, 1.31, 1.41, p=2.6)
    squint = 0.16 * gaze_left * (1.0 - blink)
    eye_ctrl(f, gaze_x=gaze_x, gaze_y=gaze_y, close=clamp(blink + squint))

    settle_springs(f, t, 2.80, window=0.16)
    return f


def spawn(t: float) -> Frame:
    """Vertical teardrop drop landing flat onto y=0 @ 0.34s -> wide floor splat & rebound (0.0 deg body rot)."""
    f = Frame()
    air = 1.0 - ease_in(seg(t, 0.0, 0.33))
    drop_y = 104.0 * air
    splat = bump(t, 0.31, 0.355, 0.46)
    rebound = bump(t, 0.43, 0.50, 0.57)

    # Straight vertical fall onto y=0 with r == 0.0 deg!
    f.P.add("body_base", y=drop_y)
    # Airborne: rise_y > 0 & negative puddle_spread narrows feet inward (foot_l.x > 0, foot_r.x < 0) into raindrop!
    # Impact @ 0.34s: body_base.y == 0 and feet spread wide into flat horizontal floor splat!
    fluid_volume(
        f,
        rise_y=22.0 * air - 26.0 * splat + 11.5 * rebound,
        puddle_spread=-5.0 * air + 16.0 * splat,
    )
    f.P.add("inner_core", y=14.0 * air - 17.0 * splat + 8.0 * rebound)
    crown_trio(
        f,
        lift=6.5 * air - 9.0 * splat + 6.0 * rebound,
        flare=-4.5 * air + 8.5 * splat,
        pitch=4.5 * splat,
    )
    eye_ctrl(f, gaze_y=-4.0 * splat, close=0.84 * splat)
    f.tint = (1.0, 1.0, 1.0, ease_out(seg(t, 0.0, 0.12)))
    f.glow["aura"] = 0.65 * hold(t, 0.0, 0.12, 0.32, 0.50)
    f.extra["spring_mix"] = ease(seg(t, 0.10, 0.24)) * (1.0 - ease(seg(t, 0.44, 0.57)))
    return f


def attack(t: float) -> Frame:
    """Flat-floor horizontal fluid whip-slide (`attack_contact` @ 0.34s, body_base.y=0, foot_*.y=0, r=0 deg)."""
    f = Frame()
    coil = plateau(t, 0.0, 0.12, 0.17, 0.25, p=2.1)
    lunge = plateau(t, 0.17, 0.33, 0.40, 0.65, p=3.4)  # 4-frame hit-stop @ 0.333s..0.40s
    jiggle = kick(t, 0.40, freq=5.4, damp=7.5) * (1.0 - ease(seg(t, 0.58, 0.76)))

    dx = 22.0 * coil - 80.0 * lunge
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.92 * dx)

    fluid_volume(
        f,
        rise_y=-13.0 * coil - 12.0 * lunge + 8.5 * jiggle,
        slosh_x=14.0 * coil - 26.0 * lunge + 10.0 * jiggle,
        puddle_spread=5.0 * coil + 8.0 * lunge,
    )
    # Leading left foot reaches to -96px total (-80 - 8.1 - 7.9 = -96px) along y=0!
    f.P.add("foot_l", x=-8.0 * lunge)
    f.P.add("inner_core", x=12.0 * coil - 22.0 * lunge + 8.0 * jiggle)
    crown_trio(
        f,
        lift=-3.0 * coil + 4.5 * lunge,
        flare=4.5 * lunge - 2.5 * coil,
        pitch=8.0 * lunge - 4.5 * coil,
    )

    eye_ctrl(
        f,
        gaze_x=-9.5 * lunge + 4.0 * coil,
        gaze_y=-2.0 * lunge,
        close=0.26 * coil + 0.42 * bump(t, 0.32, 0.36, 0.44),
    )
    f.glow["aura"] = 0.50 * hold(t, 0.16, 0.32, 0.42, 0.64)
    settle_springs(f, t, 0.78, window=0.13)
    return f


def cast(t: float) -> Frame:
    """100% planted base (dx=0, dy=0, r=0): +28px vertical pump -> parabolic spit_glob (-330px+, +75px apex)."""
    f = Frame()
    gather = plateau(t, 0.0, 0.12, 0.17, 0.24, p=2.0)
    pump = plateau(t, 0.17, 0.37, 0.425, 0.46, p=2.6)
    recoil = bump(t, 0.425, 0.465, 0.60)
    slosh = kick(t, 0.45, freq=4.8, damp=6.0) * (1.0 - ease(seg(t, 0.70, 0.90)))

    # body_base stays 100% planted at dx=0, dy=0, r=0!
    # Flanks squeeze inward while crown pumps +28px vertically upward, then pops at 0.44s!
    fluid_volume(
        f,
        rise_y=-12.0 * gather + 28.0 * pump - 18.0 * recoil + 7.5 * slosh,
        slosh_x=6.0 * gather - 12.0 * recoil + 6.0 * slosh,
        puddle_spread=5.0 * gather + 7.0 * recoil,
    )
    f.P.add("inner_core", x=-8.0 * recoil, y=16.0 * pump - 10.0 * recoil + 5.0 * slosh)
    crown_trio(
        f,
        lift=-3.5 * gather + 10.0 * pump - 5.0 * recoil,
        flare=-4.0 * pump + 8.5 * recoil,
        pitch=6.0 * recoil - 3.0 * pump,
    )
    eye_ctrl(
        f,
        gaze_x=-9.0 * (pump + 0.6 * recoil),
        gaze_y=3.5 * pump - 2.0 * gather,
        close=0.36 * gather + 0.28 * recoil,
    )

    # Parabolic spit_glob: -330px+ left, +75px apex, alpha=1.0 from 0.43s..0.72s fading to 0.0 by 0.82s
    u = seg(t, 0.43, 0.82)
    u_x = 0.26 * ease_out(u) + 0.74 * u
    gx = -384.0 * u_x
    gy = 10.0 * (1.0 - u) + 4.0 * 73.0 * u * (1.0 - u)
    vy = -10.0 + 292.0 * (1.0 - 2.0 * u)
    gr = -math.degrees(math.atan2(vy, 384.0))
    set_spit(f, t, 0.43, 0.72, 0.82, 0.92, gx, gy, gr)

    f.glow["aura"] = 0.70 * hold(t, 0.18, 0.40, 0.52, 0.78)
    settle_springs(f, t, 0.92, window=0.14)
    return f


def power_up(t: float) -> Frame:
    """Symmetrical planted spread -> tall pressurized vertical surge (+26px, r=0 deg) flaring 3 crown crests."""
    f = Frame()
    coil = hold(t, 0.0, 0.12, 0.19, 0.27)
    lock = plateau(t, 0.19, 0.39, 0.51, 0.76, p=3.1)
    thud = bump(t, 0.64, 0.73, 0.82)
    vib = math.sin(TAU * 18.0 * t) * hold(t, 0.34, 0.39, 0.51, 0.59)

    fluid_volume(
        f,
        rise_y=-15.0 * coil + 26.0 * lock - 7.5 * thud,
        puddle_spread=8.0 * coil + 4.5 * thud,
    )
    f.P.add("inner_core", y=16.0 * lock - 5.0 * thud)
    crown_trio(
        f,
        lift=-4.0 * coil + 12.0 * lock - 3.0 * thud,
        flare=10.5 * lock - 3.0 * coil,
        pitch=0.0,
        shiver=3.5 * vib,
    )
    eye_ctrl(f, gaze_x=-5.0 * lock, gaze_y=2.5 * lock, close=0.34 * coil + 0.18 * thud)
    f.glow["aura"] = 0.78 * lock
    settle_springs(f, t, 0.85, window=0.13)
    return f


def hurt(t: float) -> Frame:
    """Flat-bottomed horizontal hydro-shock knockback (+42px right along y=0, r=0 deg), pancake wince."""
    f = Frame()
    snap = plateau(t, 0.0, 0.04, 0.10, 0.32, p=1.7)
    rip = kick(t, 0.05, freq=6.0, damp=8.0) * (1.0 - ease(seg(t, 0.24, 0.37)))

    dx = 42.0 * snap + 6.0 * rip
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.90 * dx)
    fluid_volume(
        f,
        rise_y=-20.0 * snap + 8.0 * rip,
        slosh_x=16.0 * snap + 5.0 * rip,
        puddle_spread=11.0 * snap,
    )
    f.P.add("inner_core", x=18.0 * snap, y=-8.0 * snap)
    crown_trio(f, lift=-3.0 * snap, flare=8.0 * snap, pitch=-8.0 * snap)
    eye_ctrl(f, gaze_x=5.0 * snap, close=0.98 * snap)
    settle_springs(f, t, 0.38, window=0.10)
    return f


def _melted_sml(f: Frame, melt: float, swell: float = 0.0, boil_x: float = 0.0) -> None:
    """Shared zero-rotation flat-floor puddle collapse geometry (`die(0.80)` == `revive(0.0)`)."""
    # Crown world dy = -22 (belly_mid) + -24 (crown) = -46px at melt=1.0; feet spread flat along y=0!
    f.P.add("belly_mid", x=0.52 * boil_x, y=10.0 * swell - 22.0 * melt)
    f.P.add("crown", x=0.48 * boil_x, y=10.0 * swell - 24.0 * melt)
    spread = -9.0 * swell + 34.0 * melt
    f.P.add("flank_l", x=-spread + 0.30 * boil_x, y=4.5 * swell - 10.0 * melt)
    f.P.add("flank_r", x=spread + 0.30 * boil_x, y=4.5 * swell - 10.0 * melt)
    f.P.add("foot_l", x=5.0 * swell - 28.0 * melt)
    f.P.add("foot_r", x=-5.0 * swell + 28.0 * melt)
    f.P.add("inner_core", x=0.60 * boil_x, y=12.0 * swell - 30.0 * melt)
    crown_trio(
        f,
        lift=8.0 * swell - 8.0 * melt,
        flare=4.0 * swell + 14.0 * melt,
        pitch=8.0 * melt,
    )


def die(t: float) -> Frame:
    """Pressurized vertical swell @ 0.24s -> rupture @ 0.26s -> flat-floor puddle collapse (r=0 deg)."""
    f = Frame()
    swell = bump(t, 0.0, 0.23, 0.33)
    melt = ease_in(seg(t, 0.23, 0.66))
    boil_x = 7.5 * math.sin(TAU * 16.0 * t) * bump(t, 0.06, 0.22, 0.34)

    _melted_sml(f, melt=melt, swell=swell, boil_x=boil_x)
    eye_ctrl(f, gaze_y=-4.0 * melt, close=clamp(0.35 * swell + ease(seg(t, 0.06, 0.30))))
    alpha = 1.0 - ease(seg(t, 0.52, 0.77))
    f.tint = (1.0, 1.0, 1.0, alpha)
    f.glow["aura"] = 0.75 * swell * alpha
    settle_springs(f, t, 0.80, window=0.14)
    return f


def revive(t: float) -> Frame:
    """Coalesce from flat-floor puddle collapse back into setup pose (r=0 deg, 0.68s total)."""
    f = Frame()
    k = 1.0 - ease_out(seg(t, 0.0, 0.60))
    _melted_sml(f, melt=k)
    eye_ctrl(f, gaze_y=-4.0 * k, close=k)
    f.tint = (1.0, 1.0, 1.0, ease(seg(t, 0.02, 0.28)))
    f.glow["aura"] = 0.48 * bump(t, 0.12, 0.35, 0.62)
    f.extra["spring_mix"] = ease(seg(t, 0.05, 0.24)) * (1.0 - ease(seg(t, 0.52, 0.67)))
    return f


# ==============================================================================
# 4 BESPOKE STRAIN-SIGNATURE CLIPS (ALL GEL BODY BONES r == 0.00 deg!)
# ==============================================================================


def attack_crimson(t: float) -> Frame:
    """`赤刺突击` (0.84s, `attack_contact` @ 0.34s):
    - Flat-floor glide (body_base.y=0, foot_*.y=0, r=0): pull back +24px & retract horns -6px inward
    - Explosive shear (body_base.x=-76px, crown.x=-26px additional shear) + violent +20..+24px axial harpoon
      shoot of `crest_l_b/c_b/r_b` into 4-frame hit-stop (0.34s..0.40s)
    - Fast low ruby shard-spark streak (`spit_glob` <= -320px left across 0.33s..0.58s)
    - Post-hit 24Hz high-frequency axial horn shimmer (0.44s..0.68s) for +1 Strength!
    """
    f = Frame()
    coil = plateau(t, 0.0, 0.11, 0.17, 0.24, p=2.2)
    spear = plateau(t, 0.16, 0.33, 0.40, 0.53, p=3.6)  # 4-frame hit-stop 0.333s..0.40s!
    bristle = hold(t, 0.44, 0.48, 0.62, 0.68)
    shim = math.sin(TAU * 24.0 * max(0.0, t - 0.44)) * bristle

    dx = 24.0 * coil - 76.0 * spear + 3.0 * bristle
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.92 * dx)

    # Planted-base horizontal shear with crown.x = -26px additional forward shear during spear!
    rise = -11.0 * coil - 8.0 * spear + 12.0 * bristle
    bulge = -0.58 * rise + 4.0 * coil + 6.0 * spear
    f.P.add("belly_mid", x=10.0 * coil - 14.0 * spear, y=0.52 * rise)
    f.P.add("crown", x=8.0 * coil - 26.0 * spear, y=0.48 * rise)
    f.P.add("flank_l", x=-bulge + 6.0 * coil - 16.0 * spear, y=0.22 * rise)
    f.P.add("flank_r", x=bulge + 6.0 * coil - 8.0 * spear, y=0.22 * rise)
    f.P.add("foot_l", x=-0.55 * bulge - 10.0 * spear)
    f.P.add("foot_r", x=0.55 * bulge)
    f.P.add("inner_core", x=12.0 * coil - 22.0 * spear, y=7.0 * bristle)

    # Axial harpoon shoot along each crystal's exact outward axis (retract -6px -> shoot +21..+24px -> 24Hz shimmer)
    ext_l = -6.0 * coil + 22.0 * spear + (9.5 + 4.5 * shim) * bristle
    ext_c = -6.0 * coil + 24.0 * spear + (11.0 - 4.8 * shim) * bristle
    ext_r = -6.0 * coil + 20.5 * spear + (9.0 + 4.2 * shim) * bristle
    f.P.add("crest_l_b", x=-0.38 * ext_l, y=0.925 * ext_l, r=3.5 * shim)
    f.P.add("crest_c_b", x=-0.14 * ext_c, y=0.990 * ext_c, r=-3.0 * shim)
    f.P.add("crest_r_b", x=0.48 * ext_r, y=0.877 * ext_r, r=3.2 * shim)

    eye_ctrl(
        f,
        gaze_x=-10.5 * spear - 6.0 * coil - 6.5 * bristle,
        gaze_y=-2.0 * spear + 1.5 * bristle,
        close=0.42 * coil + 0.24 * spear + 0.36 * bristle,
    )

    # Fast low ruby shard-spark streak (-320px+ left across 0.33s..0.58s)
    u = seg(t, 0.325, 0.58)
    u_x = 0.32 * ease_out(u) + 0.68 * u
    gx = -54.0 - 318.0 * u_x
    gy = 10.0 * (1.0 - u) + 14.0 * math.sin(math.pi * u) - 16.0 * u
    gr = -3.0 + 8.5 * u
    set_spit(f, t, 0.33, 0.51, 0.58, 0.84, gx, gy, gr)

    f.glow["aura"] = 0.55 * spear + 0.72 * bristle
    settle_springs(f, t, 0.84, window=0.12)
    return f


def attack_amber(t: float) -> Frame:
    """`岩帽冲撞` (0.86s, `attack_contact` @ 0.34s):
    - Heavy flat-bottomed curling-stone floor skid (body_base.y=0, foot_*.y=0, r=0): haul back +26px while
      lifting 3 sandstone rocks +12px -> slam -92px horizontally into 5-frame seismic hit-stop (0.34s..0.42s)
    - Drop all 3 sandstone rocks -16px straight DOWN like an anvil visor (`顿感`)
    - Low pebble chip kick (`spit_glob` bouncing -160px+ left along ground 0.34s..0.58s)
    - Flat braced crouch (0.46s..0.68s) for Block gain!
    """
    f = Frame()
    coil = plateau(t, 0.0, 0.13, 0.18, 0.25, p=2.0)
    ram = plateau(t, 0.17, 0.33, 0.42, 0.51, p=3.5)  # 5-frame seismic hit-stop 0.333s..0.42s!
    brace = plateau(t, 0.46, 0.51, 0.64, 0.73, p=2.3)
    thud = kick(t, 0.34, freq=4.2, damp=8.5) * (1.0 - ease(seg(t, 0.46, 0.60)))

    dx = 26.0 * coil - 92.0 * ram - 6.0 * brace
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.94 * dx)

    fluid_volume(
        f,
        rise_y=-12.0 * coil - 18.0 * ram - 15.0 * brace + 4.5 * thud,
        slosh_x=12.0 * coil - 20.0 * ram - 4.0 * brace,
        puddle_spread=5.0 * coil + 11.0 * ram + 10.0 * brace,
    )
    f.P.add("inner_core", x=11.0 * coil - 20.0 * ram, y=-6.0 * ram - 6.0 * brace)

    # Lift +12px on haul-back -> slam -16px straight DOWN like an anvil visor -> hold -11px in Block brace!
    rock_y = 12.0 * coil - 16.0 * ram - 11.0 * brace
    visor_lock = ram + 0.90 * brace
    f.P.add("crest_l_b", x=5.0 * visor_lock, y=rock_y, r=3.0 * visor_lock)
    f.P.add("crest_c_b", x=-2.0 * ram, y=rock_y, r=2.5 * ram)
    f.P.add("crest_r_b", x=-6.0 * visor_lock, y=rock_y, r=-3.0 * visor_lock)

    eye_ctrl(
        f,
        gaze_x=-9.5 * ram - 5.0 * coil - 6.0 * brace,
        gaze_y=-3.5 * visor_lock,
        close=0.44 * coil + 0.54 * ram + 0.50 * brace,
    )

    # Low pebble chip kick bouncing -160px+ left along ground (0.34s..0.58s)
    u = seg(t, 0.335, 0.58)
    gx = -64.0 - 170.0 * u
    gy = -108.0 + 36.0 * abs(math.sin(math.pi * 2.0 * u)) * (1.0 - 0.30 * u)
    gr = 240.0 * u
    set_spit(f, t, 0.34, 0.50, 0.58, 0.86, gx, gy, gr)

    f.glow["aura"] = 0.50 * ram + 0.64 * brace
    settle_springs(f, t, 0.86, window=0.12)
    return f


def cast_azure(t: float) -> Frame:
    """`毒泡溅射` (0.95s, `cast_release` @ 0.44s):
    - Exact vanilla LeafSlime planted vertical spit (body_base dx=0, dy=0, r=0 throughout!)
    - Rapid 20Hz 3-bubble froth boil (0.06s..0.38s) while flanks squeeze inward (+/-16px) and crown pumps +32px up
    - @ 0.44s sharp downward nozzle pop (crown.y = -14px) launching `spit_glob` in high parabolic arc
      (-360px+ left, +95px apex, slot_alpha = 1.0 from 0.43s..0.76s, fading cleanly to 0.0 by 0.86s)!
    """
    f = Frame()
    churn = hold(t, 0.05, 0.09, 0.33, 0.39)
    pump = plateau(t, 0.08, 0.35, 0.41, 0.44, p=2.3)
    pop = bump(t, 0.415, 0.45, 0.58)
    wave1 = kick(t, 0.45, freq=4.8, damp=5.6) * (1.0 - ease(seg(t, 0.72, 0.93)))
    wave2 = kick(t, 0.49, freq=4.8, damp=5.8) * (1.0 - ease(seg(t, 0.74, 0.94)))

    # Rapid 20Hz out-of-phase 3-bubble froth boil
    b1 = math.sin(TAU * 20.0 * t) * churn
    b2 = math.sin(TAU * 20.0 * t + 2.09) * churn
    b3 = math.sin(TAU * 20.0 * t + 4.19) * churn

    # Planted base (body_base dx=0, dy=0, r=0): flanks squeeze +/-16px & crown pumps +32px up -> -14px nozzle pop!
    f.P.add("belly_mid", x=4.0 * b1 - 8.0 * pop + 5.0 * wave1, y=16.0 * pump - 10.0 * pop + 5.5 * wave1)
    f.P.add("crown", x=3.0 * b2 - 7.0 * pop + 4.5 * wave2, y=16.0 * pump - 14.0 * pop + 5.0 * wave2)
    squeeze = 16.0 * pump - 12.0 * pop + 4.5 * wave1
    f.P.add("flank_l", x=squeeze + 2.5 * b1, y=6.5 * pump - 5.0 * pop)
    f.P.add("flank_r", x=-squeeze + 2.5 * b3, y=6.5 * pump - 5.0 * pop)
    f.P.add("foot_l", x=6.0 * pump - 8.0 * pop)
    f.P.add("foot_r", x=-6.0 * pump + 8.0 * pop)
    f.P.add("inner_core", x=5.0 * b2 - 10.0 * pop, y=18.0 * pump - 10.0 * pop + 6.0 * wave2)

    # Froth bubbles boil at 20Hz, rise on pump, and pop @ 0.44s!
    f.P.add("crest_l_b", x=4.2 * b1 - 7.0 * pop, y=5.5 * b2 + 8.0 * pump + 10.0 * pop + 4.0 * wave2, r=7.0 * b1 + 8.0 * pop)
    f.P.add("crest_c_b", x=3.8 * b2 - 5.0 * pop, y=6.0 * b3 + 10.0 * pump + 12.0 * pop + 5.0 * wave1, r=6.5 * b2 + 6.0 * pop)
    f.P.add("crest_r_b", x=4.2 * b3 - 3.0 * pop, y=5.2 * b1 + 7.0 * pump + 8.0 * pop + 4.0 * wave2, r=7.0 * b3 - 7.0 * pop)

    eye_ctrl(
        f,
        gaze_x=-10.5 * (0.6 * pump + pop) + 2.5 * b1,
        gaze_y=4.5 * pump - 2.0 * pop,
        close=0.38 * churn + 0.34 * pop,
    )

    # High parabolic LeafSlime acid glob: -360px+ left, +95px apex, alpha=1.0 from 0.43s..0.76s -> 0.0 by 0.86s
    u = seg(t, 0.43, 0.86)
    u_x = 0.22 * ease_out(u) + 0.78 * u
    gx = -394.0 * u_x
    gy = 18.0 * (1.0 - u) + 4.0 * 88.0 * u * (1.0 - u)
    vy = -18.0 + 352.0 * (1.0 - 2.0 * u)
    gr = -math.degrees(math.atan2(vy, 394.0))
    set_spit(f, t, 0.43, 0.76, 0.86, 0.95, gx, gy, gr)

    f.glow["aura"] = 0.38 * churn + 0.78 * hold(t, 0.30, 0.43, 0.52, 0.80)
    settle_springs(f, t, 0.95, window=0.14)
    return f


def attack_azure(t: float) -> Frame:
    """`酸液飞滴` (0.80s, `attack_contact` @ 0.34s):
    - Zero-rotation fluid tidal stretch & pancake splat (body_base.r=0, crown.r=0, y=0):
      gather right (+22px) -> differential horizontal puddle wave (`foot_l` shoots -108px along y=0 while
      `foot_r` trails -44px along y=0, stretching base 64px wide!) with teardrop dome surge that pancakes
      -24px downward @ 0.34s + forward splash glob (`spit_glob` -210px+ left across 0.33s..0.56s)!
    """
    f = Frame()
    coil = plateau(t, 0.0, 0.11, 0.15, 0.22, p=2.1)
    lunge = plateau(t, 0.15, 0.33, 0.40, 0.64, p=3.3)
    surge = bump(t, 0.15, 0.24, 0.33)
    splat = plateau(t, 0.30, 0.34, 0.40, 0.54, p=2.6)
    ripple = kick(t, 0.40, freq=5.5, damp=7.0) * (1.0 - ease(seg(t, 0.58, 0.78)))

    # Midpoint of foot_l (-108) and foot_r (-44) is -76.0; +/-32px local foot stretch = exact 64px base stretch!
    dx = 22.0 * coil - 76.0 * lunge
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.92 * dx)

    rise = -14.0 * coil + 20.0 * surge - 24.0 * splat + 8.0 * ripple
    slosh = 12.0 * coil - 22.0 * surge - 14.0 * splat + 7.0 * ripple
    fluid_volume(
        f,
        rise_y=rise,
        slosh_x=slosh,
        puddle_spread=5.0 * coil + 14.0 * splat,
        foot_spread_scale=0.0,  # Explicit differential tidal wave on foot_l / foot_r below!
    )
    f.P.add("foot_l", x=4.0 * coil - 32.0 * lunge)   # world dx = -76 - 32 = -108px along y=0!
    f.P.add("foot_r", x=-4.0 * coil + 32.0 * lunge)  # world dx = -76 + 32 = -44px along y=0!
    f.P.add("inner_core", x=10.0 * coil - 20.0 * lunge, y=12.0 * surge - 15.0 * splat)

    crown_trio(
        f,
        lift=-4.0 * coil + 8.0 * surge - 9.0 * splat,
        flare=-4.0 * surge + 11.5 * splat,
        pitch=-5.0 * surge + 8.5 * splat,
    )
    eye_ctrl(
        f,
        gaze_x=-10.0 * lunge + 4.0 * coil,
        gaze_y=3.0 * surge - 4.0 * splat,
        close=0.32 * coil + 0.56 * splat,
    )

    # Forward acid splash glob (-210px+ left across 0.33s..0.56s)
    u = seg(t, 0.325, 0.56)
    gx = -54.0 - 196.0 * u
    gy = -18.0 + 34.0 * math.sin(math.pi * u) - 38.0 * u
    vy = 34.0 * math.pi * math.cos(math.pi * u) - 38.0
    gr = -math.degrees(math.atan2(vy, 196.0))
    set_spit(f, t, 0.33, 0.49, 0.56, 0.80, gx, gy, gr)

    f.glow["aura"] = 0.60 * hold(t, 0.16, 0.32, 0.42, 0.64)
    settle_springs(f, t, 0.80, window=0.13)
    return f


FUNCS = {name: globals()[name] for name in ANIMS}
