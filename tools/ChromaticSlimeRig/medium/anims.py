"""Chromatic Slime (Medium) — Official STS2 Style Planted-Base Fluid Shear, Harpoon Extrusion & Projectile Animations.

STRICT INVARIANTS:
- scaleX == 1.0, scaleY == 1.0 on EVERY bone across ALL 14 clips (maxBoneScaleDev == 0.0)
- rotation == 0.0 deg on ALL 11 gel body/base bones (root, shadow_b, body_base, foot_l/c/r, flank_l/r, belly_mid, crown, inner_core)
  across EVERY frame of ALL 14 clips (maxBodyRotDeg == 0.0 — flat puddle NEVER tilts off the horizontal floor!)
- True planted-base horizontal fluid shear, piston crystal harpoon extrusion, bulldozer anvil slam & parabolic flying projectiles!
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from rigutil import TAU, Frame, clamp, ease, ease_in, ease_out, hold, kick, seg

ANIMS = {
    # 8 Standard Clips
    "idle_loop": (3.2, True),
    "spawn": (0.65, False),
    "attack": (0.85, False),
    "cast": (1.05, False),
    "power_up": (0.95, False),
    "hurt": (0.42, False),
    "die": (0.95, False),
    "revive": (0.75, False),
    # 6 Bespoke Strain-Signature Clips
    "attack_crimson": (0.94, False),
    "power_crimson": (0.95, False),
    "attack_amber": (0.92, False),
    "power_amber": (0.98, False),
    "cast_azure": (1.05, False),
    "attack_azure": (0.88, False),
}

COLD_START = ("spawn", "revive")

EVENTS = {
    "spawn": [("spawn_land", 0.36)],
    "attack": [("attack_contact", 0.36)],
    "cast": [("cast_release", 0.48)],
    "power_up": [("power_peak", 0.45)],
    "hurt": [],
    "die": [("rupture_burst", 0.30)],
    "revive": [],
    # Bespoke Strain-Signature Events
    "attack_crimson": [("attack_contact", 0.35), ("attack_contact_2", 0.58)],
    "power_crimson": [("power_peak", 0.45)],
    "attack_amber": [("attack_contact", 0.36)],
    "power_amber": [("power_peak", 0.45)],
    "cast_azure": [("cast_release", 0.48)],
    "attack_azure": [("attack_contact", 0.36)],
}


def pow_in(u: float, p: float = 3.0) -> float:
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


def stops(t: float, pts: list[tuple[float, float, str]]) -> float:
    """Piecewise trajectory with exact hit-stop plateaus & smooth/whipping segments."""
    if t <= pts[0][0]:
        return pts[0][1]
    for i in range(1, len(pts)):
        t1, v1, mode = pts[i]
        if t <= t1:
            t0, v0, _ = pts[i - 1]
            if mode == "hold" or t1 <= t0:
                return v1
            u = seg(t, t0, t1)
            if mode == "whip":
                w = pow_in(u, 2.8)
            elif mode == "out":
                w = ease_out(u)
            else:
                w = ease(u)
            return v0 + (v1 - v0) * w
    return pts[-1][1]


def fluid_gel(
    f: Frame,
    rise_y: float = 0.0,
    shear_x: float = 0.0,
    puddle_spread: float = 0.0,
    foot_l_dx: float = 0.0,
    foot_c_dx: float = 0.0,
    foot_r_dx: float = 0.0,
) -> None:
    """STRICT ZERO-ROTATION, ZERO-SCALE planted-base volumetric gel deformation!
    - Foot bones NEVER lift off y=0 and NEVER rotate (r == 0.0 deg on every gel bone!).
    - shear_x shifts belly_mid & crown horizontally relative to the planted base (authentic TwigSlime/LeafSlime fluid shear!).
    """
    bulge_x = -0.56 * rise_y + puddle_spread
    # Note: crown is parented to belly_mid, so world_dx(crown) = belly_dx + crown_dx = 1.0 * shear_x,
    # and world_dy(crown) = belly_dy + crown_dy = 1.0 * rise_y!
    f.P.add("belly_mid", x=0.58 * shear_x, y=0.50 * rise_y)
    f.P.add("crown", x=0.42 * shear_x, y=0.50 * rise_y)
    f.P.add("flank_l", x=-bulge_x + 0.48 * shear_x, y=0.20 * rise_y)
    f.P.add("flank_r", x=bulge_x + 0.30 * shear_x, y=0.20 * rise_y)
    f.P.add("foot_l", x=-0.52 * bulge_x + foot_l_dx)
    f.P.add("foot_c", x=foot_c_dx)
    f.P.add("foot_r", x=0.52 * bulge_x + foot_r_dx)
    # Keep secondary upper-right eye tracking the mid-upper dome smoothly
    f.P.add("iris_sec_b", x=0.18 * shear_x, y=0.22 * rise_y)
    f.P.add("lid_sec_b", x=0.18 * shear_x, y=0.22 * rise_y)


def eyes_med(
    f: Frame,
    close_main: float = 0.0,
    close_sec: float | None = None,
    gx: float = 0.0,
    gy: float = 0.0,
) -> None:
    """Translate eyelids & irises rigidly inside hardware CLIPS (zero bone scaling!)."""
    if close_sec is None:
        close_sec = close_main
    cm = clamp(close_main)
    cs = clamp(close_sec)
    if cm > 1e-4:
        f.P.add("lid_main_b", y=-62.0 * cm)
    if cs > 1e-4:
        f.P.add("lid_sec_b", y=-46.0 * cs)
    if abs(gx) > 1e-4 or abs(gy) > 1e-4:
        f.P.add("iris_main_b", x=gx, y=gy)
        f.P.add("iris_sec_b", x=0.72 * gx, y=0.72 * gy)


def crimson_harpoons(
    f: Frame,
    tall_l: float = 0.0,
    low_l: float = 0.0,
    tall_r: float = 0.0,
    back: float = 0.0,
    low_r: float = 0.0,
    forward_aim: float = 0.0,
) -> None:
    """Piston-extrude or retract each of Crimson's 5 ruby crystals strictly along its exact socket bore vector (in px)!
    Positive values (+16..+34px) harpoon-shoot the crystal OUT of its stationary pale-rose meniscus socket;
    negative values (-8..-13px) retract the crystal DEEP INTO its socket!
    """
    # Socket bore unit vectors in Spine Y-up (+X right, +Y up):
    # tall_l: (-0.51, +0.86)
    f.P.add("c_shard_tall_l_b", x=-0.51 * tall_l, y=0.86 * tall_l, r=3.0 * forward_aim)
    # low_l:  (-0.98, +0.19) — pure horizontal-left harpoon spear!
    f.P.add("c_shard_low_l_b", x=-0.98 * low_l, y=0.19 * low_l, r=1.5 * forward_aim)
    # tall_r: (+0.57, +0.82) — pure axial piston extrusion out of upper-right collar!
    f.P.add("c_shard_tall_r_b", x=0.57 * tall_r, y=0.82 * tall_r, r=3.0 * forward_aim)
    # back:   (-0.24, +0.97) — behind body, free to lance forward-up!
    f.P.add("c_shard_back_b", x=-0.24 * back - 4.0 * forward_aim, y=0.97 * back, r=6.0 * forward_aim)
    # low_r:  (+0.95, +0.31) — pure axial piston extrusion out of right-flank collars!
    f.P.add("c_shard_low_r_b", x=0.95 * low_r, y=0.31 * low_r, r=2.5 * forward_aim)


def amber_slabs(
    f: Frame,
    clamp_in: float = 0.0,
    lift_y: float = 0.0,
    skid_lead: float = 0.0,
) -> None:
    """Slide Amber's 4 sandstone armor slabs vertically (anvil lift/drop) & horizontally (blast-door clamp)."""
    f.P.add(
        "a_stone_l_b",
        x=4.5 * clamp_in - 6.5 * skid_lead,
        y=-12.0 * clamp_in + lift_y,
        r=3.5 * clamp_in + 3.0 * skid_lead,
    )
    f.P.add(
        "a_stone_top_b",
        x=-11.0 * clamp_in - 5.5 * skid_lead,
        y=-13.0 * clamp_in + lift_y,
        r=3.0 * clamp_in + 2.5 * skid_lead,
    )
    f.P.add(
        "a_stone_flank_r_b",
        x=-12.0 * clamp_in - 5.0 * skid_lead,
        y=-6.5 * clamp_in + 0.85 * lift_y,
        r=4.5 * clamp_in + 2.5 * skid_lead,
    )
    f.P.add(
        "a_stone_cheek_b",
        x=-10.5 * clamp_in - 4.5 * skid_lead,
        y=9.0 * clamp_in + 0.55 * lift_y,
        r=5.5 * clamp_in + 2.5 * skid_lead,
    )


def azure_froth(f: Frame, boil: float = 0.0, surge_left: float = 0.0, hug_left: float = 0.0, ph: float = 0.0) -> None:
    """Drive Azure's 3 seafoam froth clusters with asynchronous boiling & contour-hugging surf motion."""
    b1 = math.sin(ph)
    b2 = math.sin(2.0 * ph + 1.1)
    b3 = math.sin(3.0 * ph + 2.3)
    f.P.add(
        "z_froth_crown_b",
        x=-5.5 * surge_left + 3.2 * boil * b1,
        y=8.0 * boil * (0.5 + 0.5 * b2) + 3.0 * surge_left,
        r=5.0 * surge_left + 3.5 * boil * b1,
    )
    f.P.add(
        "z_froth_left_b",
        x=6.5 * hug_left - 2.0 * surge_left - 2.0 * boil * b2,
        y=8.5 * boil * (0.4 + 0.6 * b3) - 2.0 * surge_left,
        r=3.5 * surge_left - 3.5 * boil * b2,
    )
    f.P.add(
        "z_froth_puddle_b",
        x=-4.5 * surge_left + 3.5 * boil * b3,
        y=6.0 * boil * (0.5 + 0.5 * b1),
        r=3.5 * surge_left + 3.2 * boil * b3,
    )


def fire_projectile(
    f: Frame,
    slot: str,
    t: float,
    t_start: float,
    t_end: float,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    arc_h: float = 0.0,
) -> None:
    """Fly a hand-painted projectile along a linear or parabolic arc (x0,y0)->(x1,y1) with tangent nose alignment!"""
    if t < t_start or t > t_end:
        return
    u = clamp((t - t_start) / max(t_end - t_start, 1e-5))
    fade_in = ease_out(clamp((t - t_start) / 0.025))
    fade_out = 1.0 - ease_in(clamp((t - (t_end - 0.065)) / 0.065))
    alpha = clamp(fade_in * fade_out)
    if alpha <= 1e-3:
        return

    # Slight acceleration on linear lances, steady ballistic horizontal velocity on parabolic arcs
    ux = u if arc_h > 25.0 else (0.35 * u + 0.65 * (u ** 1.25))
    dx = x0 + (x1 - x0) * ux
    dy = y0 + (y1 - y0) * u + 4.0 * arc_h * u * (1.0 - u)

    vx = x1 - x0
    vy = (y1 - y0) + 4.0 * arc_h * (1.0 - 2.0 * u)
    # At r=0 sprite points LEFT (-X); negative r tilts nose UP-LEFT, positive r tilts nose DOWN-LEFT!
    rot = -math.degrees(math.atan2(vy, max(-vx, 1.0)))

    f.P.add(f"{slot}_b", x=dx, y=dy, r=rot)
    sa = f.extra.setdefault("slot_alpha", {})
    sa[slot] = alpha


def settle_springs(f: Frame, t: float, dur: float, window: float = 0.16) -> None:
    if t > dur - window:
        f.extra["spring_mix"] = 1.0 - ease(seg(t, dur - window, dur))


# ============================================================================
# 8 STANDARD CLIPS (ALL STRICTLY r == 0.0 deg ON BODY/BASE!)
# ============================================================================

def idle_loop(t: float) -> Frame:
    """3.2s planted-base breathing: vertical gel pulse + subtle horizontal shear + strain life + staggered dual-eye blinks."""
    f = Frame()
    ph = TAU * (t / 3.2)
    s = math.sin(ph)
    s_lag = math.sin(ph - 0.52) - math.sin(-0.52)
    s_heavy = math.sin(ph - 0.72) - math.sin(-0.72)
    env = hold(t, 0.06, 0.30, 2.74, 3.04)

    fluid_gel(f, rise_y=10.5 * s * env, shear_x=-5.5 * s * env)
    f.P.add("inner_core", x=-8.5 * s_lag * env, y=7.0 * s_lag * env)

    # 1) Crimson: subtle axial crystal breathing + resonant glint
    trem_win = bump(t, 0.68, 0.96, 1.24) + 0.75 * bump(t, 2.10, 2.34, 2.60)
    ax_trem = 2.2 * math.sin(TAU * 15.0 * t) * trem_win * env
    ext = 3.2 * s_lag * env + ax_trem
    crimson_harpoons(f, tall_l=ext, low_l=0.7 * ext, tall_r=ext, back=0.9 * ext, low_r=0.7 * ext)

    # 2) Amber: heavy delayed vertical plate settle
    amber_slabs(f, clamp_in=0.14 * (-s_heavy) * env, lift_y=2.5 * s_heavy * env)

    # 3) Azure: asynchronous 3-cluster bubble boil
    f.P.add("z_froth_crown_b", x=2.6 * math.sin(ph + 0.4) * env, y=4.2 * math.sin(2.0 * ph) * env, r=2.5 * s * env)
    f.P.add("z_froth_left_b", x=-1.8 * math.sin(2.0 * ph + 1.1) * env, y=5.0 * math.sin(3.0 * ph + 0.7) * env)
    f.P.add("z_froth_puddle_b", x=2.8 * math.sin(2.0 * ph + 2.2) * env, y=3.6 * math.sin(3.0 * ph + 1.9) * env)

    blink_main = plateau(t, 1.28, 1.34, 1.43, 1.53, p=2.5)
    blink_sec = plateau(t, 1.37, 1.43, 1.52, 1.62, p=2.5) + 0.28 * bump(t, 2.38, 2.50, 2.66)
    gaze = hold(t, 0.24, 0.40, 1.06, 1.24) - 0.55 * hold(t, 1.85, 2.05, 2.55, 2.78)
    eyes_med(f, close_main=blink_main, close_sec=blink_sec, gx=-4.0 * gaze, gy=-0.8 * abs(gaze))

    settle_springs(f, t, 3.2, window=0.18)
    return f


def spawn(t: float) -> Frame:
    """0.65s 0-degree vertical teardrop drop -> flat horizontal pancake splat @ 0.36s + vertical rebound!"""
    f = Frame()
    air = 1.0 - ease_in(seg(t, 0.0, 0.35))
    drop_y = 92.0 * air
    splat = bump(t, 0.33, 0.38, 0.51)
    rebound = bump(t, 0.45, 0.53, 0.63)

    # Pure vertical drop (r == 0.0 deg!), lands dead flat on y=0 at 0.35s!
    f.P.add("body_base", y=drop_y)
    fluid_gel(
        f,
        rise_y=24.0 * air - 28.0 * splat + 14.0 * rebound,
        puddle_spread=-4.0 * air + 14.0 * splat,
    )
    f.P.add("inner_core", y=16.0 * air - 18.0 * splat + 9.0 * rebound)

    ext = -8.0 * air + 14.0 * splat - 4.0 * rebound
    crimson_harpoons(f, tall_l=ext, low_l=ext, tall_r=ext, back=ext, low_r=ext)
    amber_slabs(f, clamp_in=0.35 * splat, lift_y=10.0 * air - 6.0 * splat)
    azure_froth(f, boil=0.65 * (splat + rebound), hug_left=0.75 * splat, ph=TAU * 4.0 * t)

    eyes_med(f, close_main=0.85 * splat, close_sec=0.90 * splat, gx=-3.2 * rebound)
    f.tint = (1.0, 1.0, 1.0, ease_out(seg(t, 0.0, 0.14)))
    f.glow["aura"] = 0.75 * hold(t, 0.0, 0.14, 0.34, 0.56)
    f.extra["spring_mix"] = ease(seg(t, 0.12, 0.26)) * (1.0 - ease(seg(t, 0.48, 0.64)))
    return f


def attack(t: float) -> Frame:
    """0.85s flat-bottomed horizontal fluid shear & floor-skid tackle @ 0.36s (r == 0.0 deg!)."""
    f = Frame()
    coil = plateau(t, 0.0, 0.15, 0.20, 0.30, p=2.1)
    lunge = plateau(t, 0.19, 0.35, 0.42, 0.70, p=3.4)
    jiggle = kick(t, 0.36, freq=4.8, damp=6.8) * (1.0 - ease(seg(t, 0.62, 0.83)))

    dx = 26.0 * coil - 88.0 * lunge
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.92 * dx)

    fluid_gel(
        f,
        rise_y=-16.0 * coil - 18.0 * lunge + 10.0 * jiggle,
        shear_x=14.0 * coil - 34.0 * lunge - 10.0 * jiggle,
        puddle_spread=8.0 * lunge,
        foot_l_dx=-18.0 * lunge,
        foot_r_dx=22.0 * lunge,
    )
    f.P.add("inner_core", x=14.0 * coil - 28.0 * lunge + 10.0 * jiggle)

    ext = -10.0 * coil + 24.0 * lunge
    crimson_harpoons(f, tall_l=ext, low_l=ext, tall_r=0.7 * ext, back=0.8 * ext, low_r=0.6 * ext, forward_aim=0.6 * lunge)
    amber_slabs(f, clamp_in=0.35 * lunge, lift_y=12.0 * coil - 10.0 * lunge, skid_lead=0.8 * lunge)
    azure_froth(f, boil=0.45 * lunge, surge_left=0.85 * lunge - 0.35 * coil, hug_left=0.75 * lunge, ph=TAU * 3.0 * t)

    eyes_med(f, close_main=0.24 * hold(t, 0.06, 0.16, 0.50, 0.72), gx=-4.8 * hold(t, 0.05, 0.15, 0.58, 0.78))
    f.glow["aura"] = 0.55 * hold(t, 0.18, 0.34, 0.45, 0.68)
    settle_springs(f, t, 0.85, window=0.14)
    return f


def cast(t: float) -> Frame:
    """1.05s planted-base vertical geyser pump & single parabolic glob spit @ 0.48s (r == 0.0 deg!)."""
    f = Frame()
    swell = plateau(t, 0.0, 0.18, 0.26, 0.38, p=2.2)
    geyser = plateau(t, 0.22, 0.46, 0.56, 0.90, p=3.2)
    wave = kick(t, 0.48, freq=4.6, damp=5.2) * (1.0 - ease(seg(t, 0.80, 1.03)))

    fluid_gel(
        f,
        rise_y=-18.0 * swell + 34.0 * geyser + 10.0 * wave,
        shear_x=8.0 * swell - 12.0 * geyser,
        puddle_spread=12.0 * swell - 4.0 * geyser,
    )
    f.P.add("inner_core", x=-8.0 * geyser, y=-10.0 * swell + 22.0 * geyser + 8.0 * wave)

    crimson_harpoons(f, tall_l=14.0 * geyser, tall_r=14.0 * geyser, back=18.0 * geyser)
    amber_slabs(f, lift_y=12.0 * geyser - 6.0 * swell)
    azure_froth(f, boil=0.95 * (swell + geyser), surge_left=0.5 * geyser, ph=TAU * 5.0 * t)
    fire_projectile(f, "proj_1", t, 0.46, 0.86, x0=-10.0, y0=95.0, x1=-380.0, y1=8.0, arc_h=72.0)

    eyes_med(f, close_main=0.22 * swell, gx=-4.2 * geyser, gy=2.0 * geyser)
    f.glow["aura"] = 0.86 * hold(t, 0.16, 0.44, 0.58, 0.90)
    settle_springs(f, t, 1.05, window=0.16)
    return f


def power_up(t: float) -> Frame:
    """0.95s planted-base vertical pump & radial mineral extrusion @ 0.45s (r == 0.0 deg!)."""
    f = Frame()
    coil = hold(t, 0.0, 0.14, 0.22, 0.30)
    pump = plateau(t, 0.22, 0.43, 0.58, 0.84, p=3.1)
    thud = bump(t, 0.72, 0.81, 0.92)

    fluid_gel(f, rise_y=-18.0 * coil + 28.0 * pump - 8.0 * thud, puddle_spread=10.0 * coil)
    f.P.add("inner_core", y=-10.0 * coil + 18.0 * pump - 6.0 * thud)

    ext = -8.0 * coil + 18.0 * pump
    crimson_harpoons(f, tall_l=ext, low_l=ext, tall_r=ext, back=ext, low_r=ext)
    amber_slabs(f, clamp_in=0.75 * pump)
    azure_froth(f, boil=0.80 * pump, ph=TAU * 4.0 * t)

    eyes_med(f, close_main=0.22 * coil, gx=-3.8 * pump, gy=1.5 * pump)
    f.glow["aura"] = 0.88 * pump
    settle_springs(f, t, 0.95, window=0.14)
    return f


def hurt(t: float) -> Frame:
    """0.42s flat-bottomed horizontal hydro-shear wave right (r == 0.0 deg! Base glued to floor!)."""
    f = Frame()
    snap = plateau(t, 0.0, 0.04, 0.10, 0.35, p=1.7)
    rip = kick(t, 0.04, freq=5.8, damp=7.5) * (1.0 - ease(seg(t, 0.26, 0.41)))

    dx = 20.0 * snap + 5.0 * rip
    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.90 * dx)
    fluid_gel(
        f,
        rise_y=-16.0 * snap + 9.0 * rip,
        shear_x=28.0 * snap + 12.0 * rip,
        puddle_spread=8.0 * snap,
        foot_l_dx=-10.0 * snap,
    )
    f.P.add("inner_core", x=24.0 * snap + 8.0 * rip)

    crimson_harpoons(f, tall_l=-8.0 * snap, low_l=-8.0 * snap, tall_r=8.0 * snap, low_r=8.0 * snap)
    amber_slabs(f, clamp_in=0.25 * snap, skid_lead=-0.45 * snap)
    azure_froth(f, boil=0.60 * snap, surge_left=-0.55 * snap, ph=TAU * 6.0 * t)

    eyes_med(f, close_main=1.0 * snap, close_sec=1.0 * snap, gx=3.5 * snap)
    settle_springs(f, t, 0.42, window=0.11)
    return f


def _melted_med(f: Frame, melt: float, swell: float = 0.0, shud_x: float = 0.0) -> None:
    """Shared 0-degree flat-floor terminal puddle pose so die(0.95s) and revive(0.0s) match to 0.000px!"""
    fluid_gel(
        f,
        rise_y=18.0 * swell - 42.0 * melt,
        shear_x=shud_x,
        puddle_spread=22.0 * melt + 8.0 * swell,
    )
    f.P.add("inner_core", x=0.5 * shud_x, y=12.0 * swell - 32.0 * melt)

    # Crimson shards retract/sink cleanly into the melting puddle
    ret = 8.0 * swell - 12.0 * melt
    crimson_harpoons(f, tall_l=ret, low_l=ret, tall_r=ret, back=ret, low_r=ret)

    # Amber boulders settle outward & downward onto the flat floor
    f.P.add("a_stone_l_b", x=-22.0 * melt, y=-14.0 * melt + 5.0 * swell, r=10.0 * melt)
    f.P.add("a_stone_top_b", x=10.0 * melt, y=-16.0 * melt + 5.0 * swell, r=-8.0 * melt)
    f.P.add("a_stone_flank_r_b", x=6.0 * melt, y=-12.0 * melt, r=-7.0 * melt)
    f.P.add("a_stone_cheek_b", x=8.0 * melt, y=-10.0 * melt, r=-8.0 * melt)

    # Azure froth flattens & spreads into the puddle
    f.P.add("z_froth_crown_b", x=8.0 * melt, y=-14.0 * melt + 8.0 * swell, r=-8.0 * melt)
    f.P.add("z_froth_left_b", x=6.0 * melt, y=-12.0 * melt + 5.0 * swell, r=5.0 * melt)
    f.P.add("z_froth_puddle_b", x=6.0 * melt, y=-6.0 * melt, r=-5.0 * melt)


def die(t: float) -> Frame:
    """0.95s horizontal-shudder fission burst @ 0.30s -> flat puddle melt (r == 0.0 deg!)."""
    f = Frame()
    swell = bump(t, 0.0, 0.22, 0.38)
    melt = ease_in(seg(t, 0.26, 0.78))
    shud_x = 11.0 * math.sin(TAU * 15.0 * t) * bump(t, 0.10, 0.28, 0.48)

    _melted_med(f, melt=melt, swell=swell, shud_x=shud_x)
    close_v = clamp(0.32 * swell + ease(seg(t, 0.10, 0.36)))
    eyes_med(f, close_main=close_v, close_sec=close_v)

    iris_vis = 1.0 - ease(seg(t, 0.36, 0.48))
    f.extra["slot_alpha"] = {"iris_main": iris_vis, "iris_sec": iris_vis}
    alpha = 1.0 - ease(seg(t, 0.62, 0.90))
    f.tint = (1.0, 1.0, 1.0, alpha)
    f.glow["aura"] = 0.88 * swell * alpha
    settle_springs(f, t, 0.95, window=0.16)
    return f


def revive(t: float) -> Frame:
    """0.75s exact reform from die(0.95s) flat puddle pose (r == 0.0 deg!)."""
    f = Frame()
    k = 1.0 - ease_out(seg(t, 0.0, 0.68))
    _melted_med(f, melt=k)
    eyes_med(f, close_main=k, close_sec=k)
    iris_vis = ease(seg(t, 0.22, 0.38))
    f.extra["slot_alpha"] = {"iris_main": iris_vis, "iris_sec": iris_vis}
    f.tint = (1.0, 1.0, 1.0, ease(seg(t, 0.02, 0.34)))
    f.glow["aura"] = 0.55 * bump(t, 0.14, 0.40, 0.70)
    f.extra["spring_mix"] = ease(seg(t, 0.06, 0.28)) * (1.0 - ease(seg(t, 0.58, 0.74)))
    return f


# ============================================================================
# 6 BESPOKE STRAIN-SIGNATURE CLIPS (ALL STRICTLY r == 0.0 deg ON BODY/BASE!)
# ============================================================================

def attack_crimson(t: float) -> Frame:
    """0.94s 绯晶双撕 — TwigSlime-Style Planted Dual-Harpoon Extrusion & Criss-Cross Shard Barrage (ZERO base tilt!):
    - 0.00..0.20s (Compress): Pancakes wide onto flat floor while retracting all 5 ruby shards inward (-12px)!
    - Hit 1 (0.35s, 3f hit-stop 0.34..0.39s): Upper jelly shears horizontally left (belly_mid -44px, crown -58px world while
      foot_r anchors on floor!) as c_shard_tall_l & c_shard_low_l HARPOON-THRUST +32px OUTWARD + fires crimson_proj_1 (-340px left)!
    - 0.41..0.47s: Quick internal peristaltic wave reloads from right flank (r=0!) while Hit 1 shards retract halfway!
    - Hit 2 (0.58s, 4f hit-stop 0.57..0.64s): Whole slime surges horizontally along flat floor (dx=-86px, r=0!) while stretching
      tall (+28px) and SHOOTING ALL 5 RUBY SHARDS RADIALLY/FORWARD OUTWARD (+34px!) + firing crimson_proj_2 (-390px left)!
    """
    f = Frame()
    # Whole-base horizontal floor glide (y == 0, r == 0 deg ALWAYS!)
    base_dx = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.16, 12.0, "ease"),
            (0.20, 12.0, "hold"),
            (0.34, -18.0, "whip"),
            (0.39, -18.0, "hold"),
            (0.45, -10.0, "ease"),
            (0.47, -10.0, "hold"),
            (0.57, -86.0, "whip"),
            (0.64, -86.0, "hold"),
            (0.92, 0.0, "ease"),
        ],
    )
    # Upper-body horizontal fluid shear relative to base (Hit 1: belly_mid world dx = -18 + 0.58*(-44) = -43.5, crown world dx = -62!)
    shear_x = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.16, 16.0, "ease"),
            (0.20, 16.0, "hold"),
            (0.34, -44.0, "whip"),
            (0.39, -44.0, "hold"),
            (0.45, 14.0, "ease"),
            (0.47, 14.0, "hold"),
            (0.57, -26.0, "whip"),
            (0.64, -26.0, "hold"),
            (0.92, 0.0, "ease"),
        ],
    )
    rise = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.16, -20.0, "ease"),
            (0.20, -20.0, "hold"),
            (0.34, 6.0, "whip"),
            (0.39, 6.0, "hold"),
            (0.46, -16.0, "ease"),
            (0.57, 28.0, "whip"),
            (0.64, 28.0, "hold"),
            (0.92, 0.0, "ease"),
        ],
    )

    comp = plateau(t, 0.0, 0.14, 0.20, 0.28, p=2.0)
    hit1 = plateau(t, 0.21, 0.34, 0.39, 0.46, p=2.8)
    reload_w = bump(t, 0.39, 0.45, 0.52)
    hit2 = plateau(t, 0.47, 0.57, 0.64, 0.88, p=2.8)
    after_wave = kick(t, 0.64, freq=5.2, damp=7.2) * (1.0 - ease(seg(t, 0.78, 0.93)))

    f.P.add("body_base", x=base_dx)
    f.P.add("shadow_b", x=0.94 * base_dx)
    # Anchor foot_r rightward (+20px local) during Hit 1 so trailing right foot stays planted just like twig_slime_m_attack!
    fluid_gel(
        f,
        rise_y=rise + 7.0 * after_wave,
        shear_x=shear_x - 8.0 * after_wave,
        puddle_spread=12.0 * comp + 6.0 * reload_w,
        foot_l_dx=-16.0 * hit1 - 10.0 * hit2,
        foot_r_dx=18.0 * hit1 + 14.0 * hit2,
    )
    f.P.add("inner_core", x=0.65 * shear_x + 18.0 * reload_w, y=0.45 * rise)

    # Harpoon piston choreography:
    # - Compress (0.0..0.20s): all 5 shards retract -12px deep into their meniscus sockets!
    # - Hit 1 (0.35s): c_shard_tall_l & c_shard_low_l VIOLENTLY HARPOON-THRUST +32px outward!
    # - Reload (0.45s): Hit 1 shards pull halfway back (+12px) while rear shards cock (-10px)
    # - Hit 2 (0.58s): ALL 5 RUBY SHARDS SHOOT OUTWARD (+34px on tall_r/back/low_r, +26px on tall_l/low_l)!
    crimson_harpoons(
        f,
        tall_l=-12.0 * comp + 32.0 * hit1 + 12.0 * reload_w + 26.0 * hit2,
        low_l=-11.0 * comp + 33.0 * hit1 + 10.0 * reload_w + 25.0 * hit2,
        tall_r=-12.0 * comp - 6.0 * hit1 - 10.0 * reload_w + 34.0 * hit2,
        back=-12.0 * comp + 6.0 * hit1 - 8.0 * reload_w + 34.0 * hit2,
        low_r=-10.0 * comp - 5.0 * hit1 - 8.0 * reload_w + 29.0 * hit2,
        forward_aim=0.65 * hit1 + 1.0 * hit2,
    )

    # Real flying ruby-splinter lance projectiles!
    fire_projectile(f, "proj_1", t, 0.28, 0.47, x0=-55.0, y0=8.0, x1=-395.0, y1=-20.0, arc_h=8.0)
    fire_projectile(f, "proj_2", t, 0.51, 0.73, x0=-95.0, y0=28.0, x1=-485.0, y1=-16.0, arc_h=14.0)

    squint = 0.22 * hold(t, 0.04, 0.14, 0.64, 0.84) + 0.08 * (hit1 + hit2)
    eyes_med(
        f,
        close_main=squint,
        close_sec=squint + 0.04,
        gx=-5.2 * hold(t, 0.04, 0.14, 0.66, 0.88),
        gy=1.8 * hit2 - 1.0 * hit1,
    )
    f.glow["aura"] = 0.58 * hit1 + 0.86 * hit2
    settle_springs(f, t, 0.94, window=0.14)
    return f


def power_crimson(t: float) -> Frame:
    """0.95s 炽晶涌动: Planted horizontal puddle (r=0!), compresses flat then pumps tall (+32px) while axially
    extruding all 5 ruby crystals (+18px) with 22Hz axial crystalline vibration!
    """
    f = Frame()
    crouch = plateau(t, 0.0, 0.14, 0.21, 0.28, p=2.2)
    pump = plateau(t, 0.22, 0.42, 0.62, 0.86, p=3.0)
    thud = bump(t, 0.76, 0.84, 0.93)

    vib_env = hold(t, 0.26, 0.36, 0.62, 0.76)
    vib_ax = 3.6 * math.sin(TAU * 22.0 * t) * vib_env

    fluid_gel(
        f,
        rise_y=-22.0 * crouch + 32.0 * pump - 8.0 * thud,
        puddle_spread=12.0 * crouch,
    )
    f.P.add("inner_core", y=-14.0 * crouch + 22.0 * pump)

    ext = -11.0 * crouch + 19.0 * pump + vib_ax
    crimson_harpoons(f, tall_l=ext, low_l=ext, tall_r=ext, back=ext + 3.0 * pump, low_r=ext)

    eyes_med(
        f,
        close_main=0.30 * crouch + 0.12 * pump,
        close_sec=0.34 * crouch + 0.15 * pump,
        gx=-3.8 * pump,
        gy=2.2 * pump,
    )
    f.glow["aura"] = 0.94 * pump
    settle_springs(f, t, 0.95, window=0.14)
    return f


def attack_amber(t: float) -> Frame:
    """0.92s 琥晶撞击 — Horizontal Siege-Bulldozer Floor Skid & Vertical Anvil Rock-Drop (ZERO base tilt!):
    - Base 100% glued to y=0 (r=0 deg)! Pulls back horizontally (+32px) while lifting its 4 sandstone slabs +18px UP
    - Skids/bulldozes horizontally left (-104px, foot_* dy=0, r=0!) while SLAMMING all 4 sandstone slabs straight DOWN
      (-22px vertical anvil smash!) at 0.36s into a 5-frame seismic Hit-Stop (0.35..0.43s)
    - Kicks amber_proj_1 & amber_proj_2 shock-chips forward along the ground!
    """
    f = Frame()
    dx = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.18, 32.0, "ease"),
            (0.23, 32.0, "hold"),
            (0.35, -104.0, "whip"),
            (0.43, -104.0, "hold"),
            (0.88, 0.0, "ease"),
        ],
    )
    rear = plateau(t, 0.0, 0.16, 0.23, 0.31, p=2.0)
    skid = plateau(t, 0.23, 0.35, 0.43, 0.84, p=3.2)
    anvil = plateau(t, 0.26, 0.36, 0.46, 0.82, p=3.2)
    seismic = kick(t, 0.36, freq=7.5, damp=9.0) * (1.0 - ease(seg(t, 0.65, 0.88)))

    f.P.add("body_base", x=dx)
    f.P.add("shadow_b", x=0.95 * dx)

    fluid_gel(
        f,
        rise_y=8.0 * rear - 20.0 * skid + 6.0 * seismic,
        shear_x=16.0 * rear - 28.0 * skid,
        puddle_spread=-4.0 * skid,
        foot_l_dx=-10.0 * skid,
        foot_r_dx=14.0 * skid,
    )
    f.P.add("inner_core", x=14.0 * rear - 26.0 * skid, y=-10.0 * skid)

    # Lift 4 sandstone slabs +18px UP during rear-back, then slam -22px DOWN (net clamped low!) on impact!
    amber_slabs(
        f,
        clamp_in=0.55 * anvil,
        lift_y=18.0 * rear - 6.0 * anvil + 3.0 * seismic,
        skid_lead=0.95 * anvil - 0.35 * rear,
    )

    # Ground shockwave chips kicked forward at 0.35s!
    fire_projectile(f, "proj_1", t, 0.35, 0.66, x0=-130.0, y0=-18.0, x1=-415.0, y1=-42.0, arc_h=34.0)
    fire_projectile(f, "proj_2", t, 0.36, 0.64, x0=-118.0, y0=-52.0, x1=-355.0, y1=-60.0, arc_h=14.0)

    tuck = 0.34 * hold(t, 0.08, 0.20, 0.46, 0.72)
    eyes_med(f, close_main=tuck, close_sec=tuck + 0.14, gx=-4.8 * skid, gy=-1.2 * tuck)
    f.glow["aura"] = 0.64 * hold(t, 0.20, 0.35, 0.46, 0.72)
    settle_springs(f, t, 0.92, window=0.15)
    return f


def power_amber(t: float) -> Frame:
    """0.98s 岩壳固守 — Tectonic Blast-Door Lock + Flat Shield-Shove (ZERO base tilt!):
    - Planted flat (r=0!), squats into wide fortress dome as all 4 sandstone slabs slide inward & downward
      like interlocking blast doors (0.38..0.54s) + golden aura
    - Followed by a flat-bottomed horizontal shield-shove (-48px @ 0.62s, r=0!)!
    """
    f = Frame()
    unlatch = bump(t, 0.0, 0.12, 0.24)
    lock = plateau(t, 0.20, 0.38, 0.66, 0.92, p=3.0)
    shove_dx = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.20, 8.0, "ease"),
            (0.50, 8.0, "hold"),
            (0.61, -48.0, "whip"),
            (0.66, -48.0, "hold"),
            (0.94, 0.0, "ease"),
        ],
    )
    shove_pulse = bump(t, 0.52, 0.63, 0.84)
    clamp_thud = kick(t, 0.38, freq=8.0, damp=11.0) * (1.0 - ease(seg(t, 0.52, 0.70)))

    f.P.add("body_base", x=shove_dx)
    f.P.add("shadow_b", x=0.95 * shove_dx)

    fluid_gel(
        f,
        rise_y=10.0 * unlatch - 22.0 * lock + 3.0 * clamp_thud,
        shear_x=-16.0 * shove_pulse,
        puddle_spread=-5.5 * lock,
    )
    f.P.add("inner_core", x=-12.0 * shove_pulse, y=8.0 * unlatch - 15.0 * lock)

    amber_slabs(
        f,
        clamp_in=-0.22 * unlatch + 1.15 * lock,
        lift_y=10.0 * unlatch,
        skid_lead=0.45 * shove_pulse,
    )
    fire_projectile(f, "proj_2", t, 0.59, 0.80, x0=-75.0, y0=-52.0, x1=-225.0, y1=-60.0, arc_h=10.0)

    eyes_med(f, close_main=0.36 * lock, close_sec=0.48 * lock, gx=-4.2 * lock, gy=-1.0 * lock)
    f.glow["aura"] = 0.90 * hold(t, 0.28, 0.42, 0.64, 0.86)
    settle_springs(f, t, 0.98, window=0.15)
    return f


def cast_azure(t: float) -> Frame:
    """1.05s 蚀液喷淋 — Exact LeafSlime M Planted Vertical Geyser & Parabolic Dual-Glob Spit (ZERO base shift/tilt!):
    - Base stays 100% planted in place (body_base dx=0, dy=0, r=0 deg)!
    - Belly swells wide (0.0..0.20s) -> flanks squeeze inward (+24/-24px) pumping dome straight UP (+38px)
      as all 3 froth clusters boil rapidly -> convulses vertically @ 0.48s to FIRE azure_proj_1 & azure_proj_2
      in two staggered high parabolic arcs (-380px & -440px left!) with vertical peristaltic jelly waves!
    """
    f = Frame()
    swell = plateau(t, 0.0, 0.14, 0.20, 0.32, p=2.0)
    geyser = plateau(t, 0.20, 0.45, 0.56, 0.88, p=3.0)
    # Vertical peristaltic squeeze & recoil wave @ 0.48s (r == 0.0 deg!)
    spit_squeeze = bump(t, 0.44, 0.49, 0.58)
    v_wave = kick(t, 0.48, freq=5.2, damp=4.4) * (1.0 - ease(seg(t, 0.82, 1.03)))
    boil_env = hold(t, 0.02, 0.14, 0.66, 0.94)

    # 100% planted base: body_base dx == 0, dy == 0, r == 0!
    # At geyser=1: rise_y = +38, puddle_spread = -3 -> bulge_x = -24.3 -> flank_l pulls +24.3px INWARD, flank_r -24.3px INWARD!
    fluid_gel(
        f,
        rise_y=-18.0 * swell + 38.0 * geyser - 12.0 * spit_squeeze + 14.0 * v_wave,
        shear_x=-10.0 * geyser - 8.0 * spit_squeeze + 6.0 * v_wave,
        puddle_spread=14.0 * swell - 3.0 * geyser,
    )
    f.P.add("inner_core", x=-8.0 * geyser, y=-12.0 * swell + 26.0 * geyser + 10.0 * v_wave)

    azure_froth(
        f,
        boil=1.30 * boil_env,
        surge_left=0.55 * geyser + 0.45 * spit_squeeze,
        hug_left=0.45 * swell,
        ph=TAU * 6.5 * t,
    )

    # Staggered dual high-parabolic acid globs (just like leaf_slime_m_cast.jpg)!
    fire_projectile(f, "proj_1", t, 0.46, 0.86, x0=-8.0, y0=102.0, x1=-388.0, y1=6.0, arc_h=88.0)
    fire_projectile(f, "proj_2", t, 0.53, 0.94, x0=4.0, y0=108.0, x1=-436.0, y1=12.0, arc_h=96.0)

    eyes_med(
        f,
        close_main=0.20 * swell + 0.32 * spit_squeeze,
        close_sec=0.25 * swell + 0.38 * spit_squeeze,
        gx=-4.5 * geyser,
        gy=2.6 * geyser - 1.5 * spit_squeeze,
    )
    f.glow["aura"] = 0.92 * hold(t, 0.18, 0.45, 0.58, 0.90)
    settle_springs(f, t, 1.05, window=0.16)
    return f


def attack_azure(t: float) -> Frame:
    """0.88s 酸雾涌潮 — Exact LeafSlime M Horizontal Tidal-Stretch & Pancake Floor Splat (ZERO base tilt!):
    - Leading foot_l shoots -120px left along the flat floor (dy=0) while foot_r trails (-50px),
      shearing the upper dome into a cresting horizontal wave (r=0!)
    - Pancakes/splats wide onto the flat floor (rise_y=-28px, puddle_spread=+24px) at 0.36s (4f hit-stop 0.35..0.41s)
      while bursting azure_proj_1/2 splash droplets forward, then draws trailing tail smoothly home!
    """
    f = Frame()
    gather = plateau(t, 0.0, 0.15, 0.21, 0.31, p=2.1)
    # Leading foot_l shoots -120px first; trailing foot_r (-50px) catches up smoothly during recovery!
    lead_x = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.18, 16.0, "ease"),
            (0.34, -120.0, "whip"),
            (0.42, -120.0, "hold"),
            (0.84, 0.0, "ease"),
        ],
    )
    mid_x = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.18, 18.0, "ease"),
            (0.35, -86.0, "whip"),
            (0.42, -86.0, "hold"),
            (0.84, 0.0, "ease"),
        ],
    )
    trail_x = stops(
        t,
        [
            (0.00, 0.0, "hold"),
            (0.20, 20.0, "ease"),
            (0.36, -50.0, "whip"),
            (0.46, -56.0, "ease"),
            (0.84, 0.0, "ease"),
        ],
    )
    splat = plateau(t, 0.26, 0.35, 0.42, 0.78, p=3.2)
    wave_crest = bump(t, 0.16, 0.27, 0.36)
    surf_rip = kick(t, 0.36, freq=5.0, damp=5.8) * (1.0 - ease(seg(t, 0.66, 0.86)))

    f.P.add("body_base", x=mid_x)
    f.P.add("shadow_b", x=0.5 * (lead_x + trail_x))

    # Differential foot slide along flat floor: world_dx(foot_l) = -120px, world_dx(foot_r) = -50px at t=0.36s!
    splat_bulge_half = 0.52 * (-0.56 * (-28.0) + 24.0)  # +20.63px already applied by fluid_gel!
    fluid_gel(
        f,
        rise_y=26.0 * gather + 16.0 * wave_crest - 28.0 * splat + 10.0 * surf_rip,
        shear_x=-14.0 * gather - 30.0 * wave_crest - 18.0 * splat,
        puddle_spread=-4.0 * gather + 24.0 * splat,
        foot_l_dx=(lead_x - mid_x) + splat_bulge_half * splat,
        foot_r_dx=(trail_x - mid_x) - splat_bulge_half * splat,
    )
    f.P.add("inner_core", x=-22.0 * splat - 18.0 * wave_crest, y=18.0 * gather - 18.0 * splat)

    azure_froth(
        f,
        boil=0.85 * (gather + wave_crest) + 0.60 * splat,
        surge_left=0.65 * wave_crest + 1.05 * splat,
        hug_left=1.25 * splat,
        ph=TAU * 5.0 * t,
    )

    # Caustic surf droplets bursting forward off the crashing wave lip @ 0.35s!
    fire_projectile(f, "proj_1", t, 0.34, 0.64, x0=-115.0, y0=-6.0, x1=-365.0, y1=-38.0, arc_h=28.0)
    fire_projectile(f, "proj_2", t, 0.36, 0.62, x0=-105.0, y0=-24.0, x1=-310.0, y1=-52.0, arc_h=18.0)

    eyes_med(
        f,
        close_main=0.26 * bump(t, 0.33, 0.39, 0.54),
        close_sec=0.32 * bump(t, 0.33, 0.39, 0.54),
        gx=-4.8 * hold(t, 0.06, 0.18, 0.56, 0.78),
        gy=2.0 * gather - 1.2 * splat,
    )
    f.glow["aura"] = 0.72 * hold(t, 0.16, 0.34, 0.45, 0.70)
    settle_springs(f, t, 0.88, window=0.14)
    return f


FUNCS = {name: globals()[name] for name in ANIMS}
