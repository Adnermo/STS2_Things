"""Chromatic Slime (Large) — v4 Vanilla-STS2 Peristaltic Floor-Locked & Projectile Animations.

Strict invariants enforced across ALL 8 clips:
1. scaleX == 1.0, scaleY == 1.0 on EVERY bone at EVERY frame (maxBoneScaleDev == 0).
2. rotation == 0.0 deg on ALL gel body/lobe bones (body_base, puddle_*_foot, flank_l/r, belly_sag,
   inner_core, mid_torus, brow_crimson, hump_amber, lobe_azure, amber_goo_b, amber_goo_tip) — ZERO cheap tilt-rocking!
3. Bottom puddle sole stays 100% horizontal and flush on the floor (body_base dy == 0, puddle_*_foot dy == 0).
4. Motion comes from multi-wave peristaltic fluid shear, height-proportional vertical surge/crush,
   axial mineral extrusion, heavy boulder lag-inertia, and 3 real matte-gouache flying projectiles!
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from rigutil import TAU, Frame, bump, clamp, ease, ease_in, ease_out, hold, kick, seg

ANIMS = {
    "idle_loop": (4.0, True),
    "attack": (0.98, False),
    "cast": (1.20, False),
    "power_up": (1.08, False),
    "summon": (1.15, False),
    "hurt": (0.46, False),
    "die": (1.10, False),
    "revive": (0.85, False),
}

COLD_START = ("revive",)

EVENTS = {
    "attack": [("attack_contact", 0.44)],
    "cast": [("cast_release", 0.56)],
    "power_up": [("power_peak", 0.48)],
    "summon": [("summon_pulse", 0.52)],
    "hurt": [],
    "die": [("rupture_burst", 0.36)],
    "revive": [],
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


def hop_arc(t: float, t0: float, dur: float) -> float:
    """Ballistic parabolic hop in [t0, t0 + dur], peaking at 1.0 mid-flight and landing cleanly at 0.0."""
    if t <= t0 or t >= t0 + dur:
        return 0.0
    u = (t - t0) / dur
    return 4.0 * u * (1.0 - u)


def peristaltic_body(
    f: Frame,
    crown_dy: float,
    shear_mid_x: float = 0.0,
    shear_top_x: float = 0.0,
    flank_bulge_x: float | None = None,
    puddle_l_dx: float | None = None,
    puddle_c_dx: float = 0.0,
    puddle_r_dx: float | None = None,
) -> None:
    """Height-proportional zero-rotation fluid deformation with 100% flat horizontal floor sole (dy=0, r=0!).
    - crown_dy > 0: tall vertical surge (flanks & puddle draw inward automatically unless overridden).
    - crown_dy < 0: heavy vertical crush / squat (flanks & puddle spread outward along y=0 to conserve volume).
    - NEVER applies rotation to any body or lobe bone!"""
    auto_bulge = -0.44 * crown_dy if flank_bulge_x is None else flank_bulge_x
    pl_dx = (-0.65 * auto_bulge if puddle_l_dx is None else puddle_l_dx)
    pr_dx = (0.65 * auto_bulge if puddle_r_dx is None else puddle_r_dx)

    # Floor contact sole: strictly dy == 0.0, r == 0.0!
    f.P.add("puddle_l_foot", x=pl_dx)
    f.P.add("puddle_c_foot", x=puddle_c_dx)
    f.P.add("puddle_r_foot", x=pr_dx)

    # Lower tiers (height ~35% of crown): strictly r == 0.0!
    f.P.add("flank_l", x=-auto_bulge + 0.72 * shear_mid_x, y=0.34 * crown_dy)
    f.P.add("flank_r", x=auto_bulge + 0.58 * shear_mid_x, y=0.34 * crown_dy)
    f.P.add("belly_sag", x=shear_mid_x, y=0.35 * crown_dy)

    # Mid & upper tiers (accumulating additively on belly_sag / flank_r to reach 100% crown_dy at hump_amber): r == 0.0!
    f.P.add("mid_torus", x=0.55 * shear_top_x, y=0.32 * crown_dy)
    f.P.add("brow_crimson", x=-0.18 * auto_bulge + 0.45 * shear_top_x, y=0.28 * crown_dy)
    f.P.add("hump_amber", x=0.14 * auto_bulge + 0.45 * shear_top_x, y=0.33 * crown_dy)
    f.P.add("lobe_azure", x=0.22 * auto_bulge + 0.30 * shear_top_x, y=0.18 * crown_dy)


def fire_projectile(
    f: Frame,
    slot_name: str,
    bone_name: str,
    t: float,
    t0: float,
    t1: float,
    dx_end: float,
    dy_end: float,
    arc_h: float,
    start_offset: tuple[float, float] = (0.0, 0.0),
) -> None:
    """Ballistic parabolic flight in [t0, t1] with velocity-tangent nose alignment & clean slot_alpha fade."""
    if t <= t0 or t >= t1:
        return
    u = (t - t0) / (t1 - t0)
    sx, sy = start_offset
    dx = sx + dx_end * u
    dy = sy + dy_end * u + 4.0 * arc_h * u * (1.0 - u)
    # Instantaneous velocity tangent (nose points along (-1, 0) at r=0; M @ (-1,0) = (-cos r, -sin r))
    vx = dx_end
    vy = dy_end + 4.0 * arc_h * (1.0 - 2.0 * u)
    rot = -math.degrees(math.atan2(vy, max(-vx, 1.0)))
    f.P.add(bone_name, x=dx, y=dy, r=rot)

    fade_in = ease(clamp(u / 0.09))
    fade_out = 1.0 - ease(clamp((u - 0.80) / 0.20))
    f.extra.setdefault("slot_alpha", {})[slot_name] = fade_in * fade_out


def eyes_3(
    f: Frame,
    c_main: float = 0.0,
    c_up: float = 0.0,
    c_rt: float = 0.0,
    gx: float = 0.0,
    gy: float = 0.0,
) -> None:
    """Unscaled 3-eye hooded gouache blink & horizontal gaze (strictly sx=1, sy=1, r=0!)."""
    cm, cu, cr = clamp(c_main), clamp(c_up), clamp(c_rt)
    if cm > 1e-4:
        f.P.add("lid_main_b", y=-60.0 * cm)
    if cu > 1e-4:
        f.P.add("lid_up_b", y=-41.0 * cu)
    if cr > 1e-4:
        f.P.add("lid_rt_b", y=-43.0 * cr)
    if abs(gx) > 1e-4 or abs(gy) > 1e-4 or cm > 1e-4 or cu > 1e-4 or cr > 1e-4:
        f.P.add("iris_main_b", x=gx, y=gy - 22.0 * cm)
        f.P.add("iris_up_b", x=0.72 * gx, y=0.75 * gy - 15.0 * cu)
        f.P.add("iris_rt_b", x=0.82 * gx, y=0.80 * gy - 16.0 * cr)


def drips_bias(f: Frame, deg: float) -> None:
    f.bias["drip_1"] = deg
    f.bias["drip_2"] = 0.92 * deg
    f.bias["drip_3"] = 0.85 * deg
    f.bias["drip_4"] = 0.72 * deg


def settle_springs(f: Frame, t: float, dur: float, window: float = 0.18) -> None:
    if t > dur - window:
        f.extra["spring_mix"] = 1.0 - ease(seg(t, dur - window, dur))


def idle_loop(t: float) -> Frame:
    """Zero-tilt peristaltic breathing wave traveling upward from flat floor -> belly -> dorsal amber hump,
    percolating azure froth, staggered 3-eye wave blink, and watchful horizontal gaze (4.0s loop)."""
    f = Frame()
    ph = TAU * (t / 4.0)
    w0 = math.sin(ph)
    w1 = math.sin(ph - 0.65) - math.sin(-0.65)
    w2 = math.sin(ph - 1.25) - math.sin(-1.25)
    s2 = math.sin(2.0 * ph)
    s3 = math.sin(3.0 * ph)

    env = hold(t, 0.06, 0.38, 3.44, 3.84)

    # Upward-traveling peristaltic wave: floor rim spreads first -> belly rises -> dorsal hump crests with lag (r == 0!)
    peristaltic_body(
        f,
        crown_dy=18.0 * w0 * env,
        shear_mid_x=-6.0 * w1 * env,
        shear_top_x=-5.0 * w2 * env,
        flank_bulge_x=-10.0 * w0 * env,
        puddle_l_dx=8.5 * w0 * env,
        puddle_c_dx=-3.0 * w1 * env,
        puddle_r_dx=-8.5 * w0 * env,
    )
    f.P.add("hump_amber", y=7.0 * w2 * env)
    f.P.add("amber_goo_b", x=3.0 * w2 * env, y=4.5 * w2 * env)
    f.P.add("amber_goo_tip", x=2.5 * w2 * env, y=-4.5 * w0 * env)
    f.P.add("inner_core", x=-10.0 * w1 * env, y=10.0 * w1 * env)

    # Heavy dorsal sandstone boulders settle with delayed inertia on soft golden goo
    f.P.add("amber_rock_l_b", y=-5.0 * w2 * env, r=1.6 * w2 * env)
    f.P.add("amber_rock_main_b", y=-6.5 * w2 * env, r=-1.8 * w2 * env)
    f.P.add("amber_rock_r_b", y=-5.5 * w2 * env, r=2.0 * w2 * env)

    # Subtle axial float on crimson shards
    f.P.add("crimson_shard_l_b", x=-1.8 * w1 * env, y=2.5 * w1 * env)
    f.P.add("crimson_shard_tall_b", x=-1.0 * w2 * env, y=3.8 * w2 * env)
    f.P.add("crimson_shard_r_b", x=1.4 * w1 * env, y=3.0 * w1 * env)

    # Percolating seafoam-cyan froth & effervescing spores
    f.P.add("azure_froth_main_b", x=2.0 * w0 * env, y=6.5 * s2 * env)
    f.P.add("azure_froth_spores_b", x=3.5 * s2 * env, y=(9.5 * s2 + 3.5 * s3) * env, r=3.0 * s2 * env)

    # Sympathetic floor ripple on detached droplets
    trem = math.sin(4.0 * ph) * env
    f.P.add("drop_near_b", x=3.0 * w0 * env + 1.0 * trem, y=1.2 * abs(s2) * env)
    f.P.add("drop_far_b", x=2.2 * w1 * env - 0.8 * trem, y=1.0 * abs(s3) * env)

    drips_bias(f, 3.2 * w1 * env)

    # Creepy staggered 3-eye wave blink + watchful horizontal gaze
    b_main = plateau(t, 1.28, 1.35, 1.43, 1.56, p=2.6)
    b_up = plateau(t, 1.41, 1.48, 1.55, 1.67, p=2.6)
    b_rt = plateau(t, 1.53, 1.60, 1.67, 1.79, p=2.6)
    gaze_l = hold(t, 0.26, 0.44, 1.16, 1.36) - 0.45 * hold(t, 2.52, 2.74, 3.22, 3.48)
    eyes_3(f, b_main, b_up, b_rt, gx=-6.0 * gaze_l, gy=-0.6 * gaze_l)

    settle_springs(f, t, 4.0, window=0.22)
    return f


def attack(t: float) -> Frame:
    """Chromatic Slam / 异彩重砸 (0.98s, attack_contact @ 0.44s):
    Peristaltic Tidal Surge & Vertical Anvil Crush (ZERO body/lobe rotation!):
    - 0.00..0.24s (Gather): Front puddle lip draws inward (puddle_l_foot dx=+46, dy=0) while gel mass rears
      +58px tall vertically into a towering wall of ooze and retracts the 3 crimson shards slightly!
    - 0.24..0.44s (Tidal Surge & Vertical Crush): Leading floor lip shoots left along flat ground (puddle_l_foot
      dx=-168, dy=0; trailing puddle_r_foot dx=-66, dy=0) as the towering gel wall crashes -78px vertically down
      (+58 -> -20) into a 5-frame seismic Hit-Stop (0.43..0.51s), thrusting crimson shards axially, lag-slamming
      dorsal boulders vertically, bouncing floor droplets, and shooting low front splash globs!"""
    f = Frame()
    gather = plateau(t, 0.0, 0.19, 0.24, 0.39, p=2.1)
    # 5-frame seismic Hit-Stop locked across 0.43..0.51s!
    crush = plateau(t, 0.24, 0.43, 0.51, 0.84, p=3.5)
    # Trailing tail catches up then glides back in a smooth peristaltic wave
    tail_wave = plateau(t, 0.28, 0.48, 0.58, 0.92, p=2.6)
    rip_y = kick(t, 0.51, freq=4.2, damp=5.8) * (1.0 - ease(seg(t, 0.76, 0.96)))

    # +58px tall vertical wall at gather -> -20px crushed anvil impact = 78px vertical drop!
    crown_dy = 58.0 * gather - 20.0 * crush + 12.0 * rip_y
    pl_dx = 46.0 * gather - 168.0 * crush
    pc_dx = 20.0 * gather - 118.0 * crush
    pr_dx = -16.0 * gather - 66.0 * tail_wave

    peristaltic_body(
        f,
        crown_dy=crown_dy,
        shear_mid_x=26.0 * gather - 132.0 * crush,
        shear_top_x=18.0 * gather - 34.0 * crush,
        flank_bulge_x=-28.0 * gather + 22.0 * crush,
        puddle_l_dx=pl_dx,
        puddle_c_dx=pc_dx,
        puddle_r_dx=pr_dx,
    )
    f.P.add("shadow_b", x=18.0 * gather - 108.0 * crush)
    f.P.add("inner_core", x=22.0 * gather - 138.0 * crush, y=22.0 * gather - 14.0 * crush + 8.0 * rip_y)

    # 3 Crimson Ruby Shards: retract slightly into brow during gather -> thrust straight outward axially on impact!
    f.P.add("crimson_shard_l_b", x=7.0 * gather - 28.0 * crush, y=-4.0 * gather - 10.0 * crush, r=5.0 * crush)
    f.P.add("crimson_shard_tall_b", x=5.0 * gather - 26.0 * crush, y=-8.0 * gather + 8.0 * crush, r=7.0 * crush)
    f.P.add("crimson_shard_r_b", x=4.0 * gather - 22.0 * crush, y=-6.0 * gather + 6.0 * crush, r=5.0 * crush)

    # 3 Heavy Dorsal Sandstone Boulders: lag high during fast vertical drop (0.26..0.42s), then SLAM straight down (顿感)!
    rock_lag = bump(t, 0.25, 0.37, 0.45)
    rock_slam = plateau(t, 0.42, 0.47, 0.57, 0.86, p=2.9)
    f.P.add("amber_goo_b", x=10.0 * rock_lag - 16.0 * rock_slam, y=-9.0 * rock_slam)
    f.P.add("amber_goo_tip", x=8.0 * rock_lag - 24.0 * rock_slam, y=-12.0 * rock_slam)

    f.P.add("amber_rock_l_b", x=12.0 * rock_lag - 14.0 * rock_slam, y=22.0 * rock_lag - 16.0 * rock_slam, r=5.5 * rock_slam)
    f.P.add("amber_rock_main_b", x=16.0 * rock_lag - 18.0 * rock_slam, y=28.0 * rock_lag - 20.0 * rock_slam, r=6.5 * rock_slam)
    f.P.add("amber_rock_r_b", x=14.0 * rock_lag - 15.0 * rock_slam, y=22.0 * rock_lag - 16.0 * rock_slam, r=-5.0 * rock_slam)

    # Azure froth lags upward during drop then splashes down-left on anvil impact
    f.P.add("azure_froth_main_b", x=12.0 * rock_lag - 18.0 * rock_slam, y=16.0 * rock_lag - 10.0 * rock_slam)
    f.P.add("azure_froth_spores_b", x=18.0 * rock_lag - 28.0 * rock_slam, y=24.0 * rock_lag - 6.0 * rock_slam, r=8.0 * rock_slam)

    # Detached floor droplets bounce vertically off the shaking ground during Hit-Stop!
    hop1 = hop_arc(t, 0.43, 0.24)
    hop2 = hop_arc(t, 0.46, 0.26)
    skid = bump(t, 0.43, 0.56, 0.90)
    f.P.add("drop_near_b", x=14.0 * skid, y=34.0 * hop1, r=-8.0 * hop1)
    f.P.add("drop_far_b", x=18.0 * skid, y=26.0 * hop2, r=-10.0 * hop2)

    # Low front shockwave splash globs shooting fast across the impact zone from the crushed leading wave lip!
    fire_projectile(
        f, "proj_crimson", "proj_c_b", t, 0.42, 0.74,
        dx_end=-480.0, dy_end=-35.0, arc_h=58.0, start_offset=(-275.0, -320.0),
    )
    fire_projectile(
        f, "proj_azure", "proj_z_b", t, 0.44, 0.77,
        dx_end=-520.0, dy_end=-25.0, arc_h=72.0, start_offset=(-775.0, -165.0),
    )

    drips_bias(f, 5.5 * gather - 7.0 * crush)

    wince = 0.55 * bump(t, 0.42, 0.47, 0.60)
    look = hold(t, 0.04, 0.16, 0.70, 0.90)
    eyes_3(f, wince, wince, wince, gx=-7.0 * look, gy=-1.5 * crush)
    f.glow["prism_glow"] = 0.54 * hold(t, 0.24, 0.42, 0.53, 0.78)
    settle_springs(f, t, 0.98, window=0.16)
    return f


def cast(t: float) -> Frame:
    """Prism Splash / 棱光溅射 (1.20s, cast_release @ 0.56s):
    Planted Volcanic Boil & Real 3-Glob Parabolic Barrage (like vanilla leaf_slime_m_cast!):
    - Base stays 100% planted (body_base dx=0, dy=0, r=0)!
    - 0.00..0.46s: Wide horizontal belly swell -> flanks squeeze inward (dx = +/-34) pumping a tall +55px vertical
      geyser boil while crimson shards bristle, amber rocks rattle, and azure froth boils at 22Hz!
    - 0.48..0.96s (cast_release @ 0.56s): Upper dome convulses straight down in a vertical pumping squeeze while
      FIRING ALL 3 REAL MATTE-GOUACHE PROJECTILES (proj_crimson, proj_amber, proj_azure) in staggered fast parabolic
      arcs -580px to -680px left, followed by damped vertical jelly ripples!"""
    f = Frame()
    swell = bump(t, 0.0, 0.14, 0.28)
    geyser = plateau(t, 0.16, 0.36, 0.46, 0.56, p=2.6)
    pump_down = plateau(t, 0.46, 0.56, 0.65, 0.96, p=3.2)
    rip = kick(t, 0.57, freq=3.8, damp=4.8) * (1.0 - ease(seg(t, 0.92, 1.16)))

    # Planted horizontal squeeze -> tall +55px vertical geyser -> downward pumping squeeze -> vertical ripple!
    crown_dy = -22.0 * swell + 55.0 * geyser - 26.0 * pump_down + 14.0 * rip
    flank_bulge = 24.0 * swell - 34.0 * geyser + 24.0 * pump_down - 8.0 * rip
    pl_dx = -20.0 * swell + 22.0 * geyser - 18.0 * pump_down
    pr_dx = 20.0 * swell - 22.0 * geyser + 18.0 * pump_down

    peristaltic_body(
        f,
        crown_dy=crown_dy,
        shear_mid_x=-10.0 * pump_down,
        shear_top_x=-14.0 * pump_down,
        flank_bulge_x=flank_bulge,
        puddle_l_dx=pl_dx,
        puddle_c_dx=-4.0 * pump_down,
        puddle_r_dx=pr_dx,
    )
    f.P.add("inner_core", x=-12.0 * pump_down, y=-12.0 * swell + 30.0 * geyser - 14.0 * pump_down + 10.0 * rip)

    # Crimson shards bristle axially during geyser boil, then pulse on release
    vib_c = math.sin(TAU * 18.0 * t) * geyser
    f.P.add("crimson_shard_l_b", x=-12.0 * geyser - 6.0 * pump_down, y=9.0 * geyser + 3.0 * vib_c, r=5.0 * geyser)
    f.P.add("crimson_shard_tall_b", x=-4.0 * geyser, y=15.0 * geyser - 3.5 * vib_c, r=3.0 * vib_c)
    f.P.add("crimson_shard_r_b", x=9.0 * geyser, y=11.0 * geyser + 3.0 * vib_c, r=-5.0 * geyser)

    # Amber boulders rattle vertically atop golden goo geyser, then dip on convulsive squeeze
    vib_a = math.sin(TAU * 16.0 * t) * geyser
    f.P.add("amber_goo_b", y=10.0 * geyser - 8.0 * pump_down)
    f.P.add("amber_goo_tip", x=-8.0 * pump_down, y=12.0 * geyser - 10.0 * pump_down)
    f.P.add("amber_rock_l_b", y=10.0 * geyser - 8.0 * pump_down + 3.5 * vib_a, r=3.5 * vib_a)
    f.P.add("amber_rock_main_b", y=14.0 * geyser - 10.0 * pump_down - 4.0 * vib_a, r=-3.0 * vib_a)
    f.P.add("amber_rock_r_b", y=10.0 * geyser - 8.0 * pump_down + 3.5 * vib_a, r=3.5 * vib_a)

    # Azure seafoam froth boils rapidly at 22 Hz!
    boil_env = hold(t, 0.14, 0.28, 0.58, 0.82)
    boil_x = math.sin(TAU * 22.0 * t) * boil_env
    boil_y = math.cos(TAU * 22.0 * t) * boil_env
    f.P.add("azure_froth_main_b", x=6.5 * boil_x - 10.0 * pump_down, y=14.0 * geyser + 7.0 * boil_y)
    f.P.add("azure_froth_spores_b", x=-8.0 * boil_y - 16.0 * pump_down, y=24.0 * geyser + 10.0 * boil_x, r=10.0 * boil_x)

    # Floor droplets hop sympathetically on volcanic pump
    f.P.add("drop_near_b", x=8.0 * swell, y=15.0 * hop_arc(t, 0.53, 0.22))
    f.P.add("drop_far_b", x=10.0 * swell, y=12.0 * hop_arc(t, 0.56, 0.22))

    # STAGGERED 3-GLOB MATTE-GOUACHE PARABOLIC BARRAGE soaring cleanly into open air (-760px to -1060px left)!
    fire_projectile(
        f, "proj_crimson", "proj_c_b", t, 0.47, 0.87,
        dx_end=-760.0, dy_end=-140.0, arc_h=185.0, start_offset=(-15.0, 34.0),
    )
    fire_projectile(
        f, "proj_amber", "proj_a_b", t, 0.52, 0.93,
        dx_end=-960.0, dy_end=-185.0, arc_h=235.0, start_offset=(-45.0, 36.0),
    )
    fire_projectile(
        f, "proj_azure", "proj_z_b", t, 0.56, 0.98,
        dx_end=-1060.0, dy_end=25.0, arc_h=275.0, start_offset=(-85.0, 65.0),
    )

    drips_bias(f, -4.5 * geyser + 5.5 * pump_down)

    squint = 0.45 * swell + 0.55 * bump(t, 0.48, 0.56, 0.72)
    eyes_3(f, squint, squint, squint, gx=-6.5 * hold(t, 0.20, 0.36, 0.85, 1.08), gy=2.2 * geyser - 1.5 * pump_down)
    f.glow["prism_glow"] = 0.92 * hold(t, 0.16, 0.50, 0.68, 1.04)
    settle_springs(f, t, 1.20, window=0.18)
    return f


def power_up(t: float) -> Frame:
    """Refractive Gel / 折光凝胶 (1.08s, power_peak @ 0.48s):
    Symmetrical planted floor spread -> rises into dense vertical fortress dome (0.46..0.64s) as the 3 dorsal boulders
    slide down & clamp tightly over the hump and the 3 crimson horns extrude axially (+18px) + prism_glow + ground thud!"""
    f = Frame()
    squat = hold(t, 0.0, 0.16, 0.24, 0.35)
    fort = plateau(t, 0.24, 0.46, 0.64, 0.88, p=3.1)
    thud = bump(t, 0.80, 0.89, 1.03)
    shimmer = math.sin(TAU * 24.0 * t) * bump(t, 0.45, 0.50, 0.66)

    peristaltic_body(
        f,
        crown_dy=-30.0 * squat + 40.0 * fort - 16.0 * thud,
        flank_bulge_x=22.0 * squat - 24.0 * fort + 12.0 * thud,
        puddle_l_dx=-22.0 * squat + 18.0 * fort - 10.0 * thud,
        puddle_r_dx=22.0 * squat - 18.0 * fort + 10.0 * thud,
    )
    f.P.add("inner_core", y=-16.0 * squat + 24.0 * fort - 10.0 * thud)

    # 3 Dorsal sandstone boulders slide down & clamp tightly inward like interlocking armor plates!
    f.P.add("amber_goo_b", y=-7.0 * fort)
    f.P.add("amber_rock_l_b", x=12.0 * fort, y=-13.0 * fort - 4.0 * thud, r=-8.0 * fort + 1.8 * shimmer)
    f.P.add("amber_rock_main_b", y=-16.0 * fort - 5.0 * thud, r=2.0 * shimmer)
    f.P.add("amber_rock_r_b", x=-14.0 * fort, y=-13.0 * fort - 4.0 * thud, r=8.5 * fort - 1.8 * shimmer)

    # 3 Crimson ruby horns extrude +18px axially along their natural crystal vectors!
    f.P.add("crimson_shard_l_b", x=-14.0 * fort, y=11.0 * fort, r=4.0 * fort + 1.8 * shimmer)
    f.P.add("crimson_shard_tall_b", x=-5.0 * fort, y=18.0 * fort, r=-1.8 * shimmer)
    f.P.add("crimson_shard_r_b", x=11.0 * fort, y=14.0 * fort, r=-4.0 * fort + 1.8 * shimmer)

    f.P.add("azure_froth_main_b", x=-8.0 * fort, y=10.0 * fort - 5.0 * thud)
    f.P.add("azure_froth_spores_b", x=-12.0 * fort, y=15.0 * fort + 8.0 * thud)

    hop_t = hop_arc(t, 0.82, 0.18)
    f.P.add("drop_near_b", x=8.0 * squat, y=16.0 * hop_t)
    f.P.add("drop_far_b", x=10.0 * squat, y=13.0 * hop_t)

    drips_bias(f, -3.5 * fort + 4.5 * thud)

    squint = 0.35 * squat + 0.25 * fort
    eyes_3(f, squint, squint, squint, gx=-5.5 * fort, gy=1.5 * fort)
    f.glow["prism_glow"] = 0.95 * fort
    settle_springs(f, t, 1.08, window=0.16)
    return f


def summon(t: float) -> Frame:
    """Wells up vertically from a wide floor-locked puddle into full towering mound with heavy boulder settle (1.15s)."""
    f = Frame()
    flat = 1.0 - ease_out(seg(t, 0.0, 0.46))
    crest = bump(t, 0.34, 0.52, 0.78)
    boulder_settle = bump(t, 0.50, 0.64, 0.92)
    wob = kick(t, 0.52, freq=3.6, damp=5.2) * (1.0 - ease(seg(t, 0.88, 1.12)))

    peristaltic_body(
        f,
        crown_dy=-96.0 * flat + 34.0 * crest + 10.0 * wob,
        flank_bulge_x=54.0 * flat - 14.0 * crest,
        puddle_l_dx=-62.0 * flat + 10.0 * crest,
        puddle_r_dx=62.0 * flat - 10.0 * crest,
    )
    f.P.add("inner_core", y=-58.0 * flat + 22.0 * crest)

    f.P.add("amber_goo_b", y=-18.0 * flat + 10.0 * crest - 6.0 * boulder_settle)
    f.P.add("amber_rock_l_b", x=-12.0 * flat, y=-24.0 * flat + 14.0 * crest - 10.0 * boulder_settle, r=10.0 * flat)
    f.P.add("amber_rock_main_b", y=-30.0 * flat + 18.0 * crest - 13.0 * boulder_settle, r=-8.0 * flat)
    f.P.add("amber_rock_r_b", x=14.0 * flat, y=-24.0 * flat + 14.0 * crest - 10.0 * boulder_settle, r=-10.0 * flat)

    f.P.add("crimson_shard_l_b", x=10.0 * flat, y=-20.0 * flat + 10.0 * crest, r=-12.0 * flat)
    f.P.add("crimson_shard_tall_b", y=-26.0 * flat + 14.0 * crest, r=8.0 * flat)
    f.P.add("crimson_shard_r_b", x=-10.0 * flat, y=-22.0 * flat + 10.0 * crest, r=10.0 * flat)

    f.P.add("azure_froth_main_b", y=-24.0 * flat + 14.0 * crest)
    f.P.add("azure_froth_spores_b", x=-12.0 * flat, y=-30.0 * flat + 20.0 * crest)
    f.P.add("drop_near_b", x=-32.0 * flat, y=14.0 * hop_arc(t, 0.42, 0.24))
    f.P.add("drop_far_b", x=-48.0 * flat, y=11.0 * hop_arc(t, 0.46, 0.24))

    c_m = 1.0 - ease(seg(t, 0.28, 0.56))
    c_u = 1.0 - ease(seg(t, 0.34, 0.62))
    c_r = 1.0 - ease(seg(t, 0.40, 0.68))
    eyes_3(f, c_m, c_u, c_r, gx=-5.0 * crest)
    f.glow["prism_glow"] = 0.68 * crest
    settle_springs(f, t, 1.15, window=0.18)
    return f


def hurt(t: float) -> Frame:
    """Pure horizontal-base hydro-shock shear wave right (+52px crown shear, 0 deg body tilt!), 3 eyes snap shut (0.46s)."""
    f = Frame()
    snap = plateau(t, 0.0, 0.045, 0.11, 0.38, p=1.7)
    rip = kick(t, 0.05, freq=5.4, damp=7.2) * (1.0 - ease(seg(t, 0.28, 0.44)))
    rattle = math.sin(TAU * 19.0 * t) * bump(t, 0.02, 0.09, 0.32)

    peristaltic_body(
        f,
        crown_dy=-26.0 * snap + 14.0 * rip,
        shear_mid_x=38.0 * snap + 12.0 * rip,
        shear_top_x=24.0 * snap + 8.0 * rip,
        flank_bulge_x=18.0 * snap,
        puddle_l_dx=34.0 * snap + 6.0 * rip,
        puddle_c_dx=24.0 * snap + 4.0 * rip,
        puddle_r_dx=20.0 * snap + 4.0 * rip,
    )
    f.P.add("shadow_b", x=26.0 * snap)
    f.P.add("inner_core", x=42.0 * snap + 14.0 * rip, y=-10.0 * snap)

    f.P.add("crimson_shard_l_b", x=8.0 * snap, r=-9.0 * snap + 5.5 * rattle)
    f.P.add("crimson_shard_tall_b", x=12.0 * snap, y=5.0 * snap, r=-11.0 * snap - 6.5 * rattle)
    f.P.add("crimson_shard_r_b", x=9.0 * snap, r=-8.0 * snap + 5.0 * rattle)

    f.P.add("amber_rock_l_b", x=10.0 * snap, y=6.0 * snap, r=8.0 * snap - 5.5 * rattle)
    f.P.add("amber_rock_main_b", x=14.0 * snap, y=8.0 * snap, r=9.5 * snap + 6.0 * rattle)
    f.P.add("amber_rock_r_b", x=12.0 * snap, y=5.0 * snap, r=-8.0 * snap - 5.5 * rattle)

    f.P.add("azure_froth_main_b", x=16.0 * snap, y=6.0 * snap)
    f.P.add("azure_froth_spores_b", x=24.0 * snap + 6.0 * rattle, y=12.0 * snap)
    f.P.add("drop_near_b", x=16.0 * snap, y=14.0 * hop_arc(t, 0.03, 0.20))
    f.P.add("drop_far_b", x=13.0 * snap, y=10.0 * hop_arc(t, 0.05, 0.20))

    drips_bias(f, 6.5 * snap)
    eyes_3(f, 1.0 * snap, 1.0 * snap, 1.0 * snap, gx=4.0 * snap)
    settle_springs(f, t, 0.46, window=0.12)
    return f


def _fission_pose(
    f: Frame,
    melt: float,
    strain: float = 0.0,
    burst_arc: float = 0.0,
    shudder_x: float = 0.0,
) -> None:
    """Canonical zero-tilt, zero-scale Chromatic Fission pose (die(1.10) == revive(0.0) to 0.000px!)."""
    peristaltic_body(
        f,
        crown_dy=28.0 * strain - 100.0 * melt,
        shear_mid_x=shudder_x,
        shear_top_x=1.25 * shudder_x,
        flank_bulge_x=18.0 * strain + 62.0 * melt,
        puddle_l_dx=-16.0 * strain - 72.0 * melt,
        puddle_c_dx=0.4 * shudder_x,
        puddle_r_dx=16.0 * strain + 72.0 * melt,
    )
    f.P.add("inner_core", x=shudder_x, y=18.0 * strain - 62.0 * melt)

    # Three chromatic zones pull apart horizontally & vertically (r == 0.0 deg strictly!)
    f.P.add("brow_crimson", x=-20.0 * strain - 34.0 * melt, y=10.0 * strain - 12.0 * melt)
    f.P.add("hump_amber", x=8.0 * strain + 16.0 * melt, y=16.0 * strain - 16.0 * melt)
    f.P.add("lobe_azure", x=20.0 * strain + 36.0 * melt, y=-6.0 * strain - 14.0 * melt)
    f.P.add("amber_goo_b", x=14.0 * melt, y=-12.0 * melt)
    f.P.add("amber_goo_tip", x=24.0 * melt, y=-16.0 * melt)

    # 3 Crimson ruby shards fling left & downward onto the floor!
    f.P.add("crimson_shard_l_b", x=-16.0 * strain - 128.0 * melt, y=28.0 * burst_arc - 102.0 * melt, r=48.0 * melt)
    f.P.add("crimson_shard_tall_b", x=-10.0 * strain - 96.0 * melt, y=46.0 * burst_arc - 128.0 * melt, r=34.0 * melt)
    f.P.add("crimson_shard_r_b", x=-6.0 * strain - 58.0 * melt, y=34.0 * burst_arc - 108.0 * melt, r=-28.0 * melt)

    # 3 Heavy sandstone boulders launch upward-out on rupture_burst, then crash down!
    f.P.add("amber_rock_l_b", x=-52.0 * melt, y=50.0 * burst_arc - 132.0 * melt, r=32.0 * melt)
    f.P.add("amber_rock_main_b", x=38.0 * melt, y=64.0 * burst_arc - 150.0 * melt, r=-26.0 * melt)
    f.P.add("amber_rock_r_b", x=112.0 * melt, y=46.0 * burst_arc - 122.0 * melt, r=-38.0 * melt)

    # Seafoam froth & floor droplets scatter outward right!
    f.P.add("azure_froth_main_b", x=14.0 * strain + 84.0 * melt, y=28.0 * burst_arc - 52.0 * melt, r=-22.0 * melt)
    f.P.add("azure_froth_spores_b", x=22.0 * strain + 132.0 * melt, y=48.0 * burst_arc - 44.0 * melt, r=-36.0 * melt)
    f.P.add("drop_near_b", x=72.0 * melt, y=28.0 * burst_arc - 4.0 * melt, r=-18.0 * melt)
    f.P.add("drop_far_b", x=56.0 * melt, y=22.0 * burst_arc - 4.0 * melt, r=-22.0 * melt)


def die(t: float) -> Frame:
    """Chromatic Fission / 异彩裂变 (1.10s, rupture_burst @ 0.36s):
    Internal horizontal shear shudder (0 deg body tilt!) -> 3-way outward fission burst & flat puddle collapse."""
    f = Frame()
    strain = bump(t, 0.0, 0.30, 0.46)
    melt = ease_in(seg(t, 0.33, 0.90))
    burst_arc = hop_arc(t, 0.34, 0.36)
    shud_x = 14.0 * math.sin(TAU * 16.0 * t) * bump(t, 0.05, 0.31, 0.48)

    _fission_pose(f, melt=melt, strain=strain, burst_arc=burst_arc, shudder_x=shud_x)
    wince = clamp(0.45 * strain + ease(seg(t, 0.12, 0.44)))
    eyes_3(f, wince, wince, wince)

    alpha = 1.0 - ease(seg(t, 0.72, 1.04))
    f.tint = (1.0, 1.0, 1.0, alpha)
    f.glow["prism_glow"] = 0.95 * bump(t, 0.08, 0.36, 0.68) * alpha
    settle_springs(f, t, 1.10, window=0.20)
    return f


def revive(t: float) -> Frame:
    """Exact 0.00px cold-start match to die(1.10s), drawing droplets, froth, boulders & shards back into rest (0.85s)."""
    f = Frame()
    k = 1.0 - ease_out(seg(t, 0.0, 0.78))
    _fission_pose(f, melt=k)
    eyes_3(f, k, k, k)
    f.tint = (1.0, 1.0, 1.0, ease(seg(t, 0.02, 0.36)))
    f.glow["prism_glow"] = 0.60 * bump(t, 0.16, 0.46, 0.78)
    f.extra["spring_mix"] = ease(seg(t, 0.08, 0.34)) * (1.0 - ease(seg(t, 0.68, 0.84)))
    return f


FUNCS = {name: globals()[name] for name in ANIMS}
