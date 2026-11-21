"""SoulRoeRig — 8 Masterpiece Single-Body 60-FPS Animations (`soul_roe_1`, `soul_roe_2`, `soul_roe_3`).

ZERO orbiting micro-roes: the roe attacks directly with its OWN body (`main_center` rocketing -498px LEFT)!

Clips & Event Contracts:
  - `idle_loop` (2.80s, loop=True,  events=[]): Pure single-roe underwater buoyancy + variant-distinct Lissajous drift, +-4px rim tension, liquid yolk lag
  - `attack`    (0.96s, loop=False, `attack_contact` @ 0.42s): +76px curved wind-up coil -> -498px supersonic self-fling comet strike (`body_trail` 0.96) -> 3-frame hydro-elastic pancake hit-stop (`impact_flare` 0.96, `yolk_b` -14.5px slam) -> +65px vaulted rebound arc!
  - `cast`      (1.12s, loop=False, `cast_release` @ 0.50s): Beckon warm sun-gold lantern ignition (`core_beckon_glow` 1.0) + expanding zero-scale `beckon_halo_ring`
  - `power_up`  (1.14s, loop=False, `power_peak` @ 0.52s): Intangible SoulFysh pearlescent cyan-white-gold glow (`core_intangible_glow` 1.0, `intangible_shimmer` 0.96) + crystalline-ethereal `cytoplasm` (alpha -> 0.62)
  - `summon`    (0.76s, loop=False, `summon_appear` @ 0.36s): Single-roe golden flash condensation & elastic droplet pop-in
  - `hurt`      (0.42s, loop=False, events=[]): +42px right hydro-shock jolt + -16px delayed `yolk_b` liquid inertia lag + surface-tension rim oscillation
  - `die`       (0.78s, loop=False, `rupture_burst` @ 0.28s): 18Hz tremble + blinding core flash -> radial membrane rupture & clean dissolve to alpha=0
  - `revive`    (0.68s, loop=False, events=[]): Exact smooth time-reversal `die(0.78 * (1 - ease(seg(t, 0, 0.68))))` -> reviveErr == 0!
"""
from __future__ import annotations

import math

from rigutil import TAU, Frame, bump, clamp, ease, ease_out, hold, key, kick, plateau, seg

ANIMS = {
    "idle_loop": (2.80, True),
    "attack": (0.96, False),
    "cast": (1.12, False),
    "power_up": (1.14, False),
    "summon": (0.76, False),
    "hurt": (0.42, False),
    "die": (0.78, False),
    "revive": (0.68, False),
}

EVENTS = {
    "idle_loop": [],
    "attack": [("attack_contact", 0.42)],
    "cast": [("cast_release", 0.50)],
    "power_up": [("power_peak", 0.52)],
    "summon": [("summon_appear", 0.36)],
    "hurt": [],
    "die": [("rupture_burst", 0.28)],
    "revive": [],
}


def rim_cage(f: Frame, stretch_y: float = 0.0, squash_x: float = 0.0, shear_x: float = 0.0) -> None:
    """Zero-rotation, zero-scale 5-bone organic droplet surface-tension deformation.
    Positive `stretch_y` elongates vertically and narrows horizontally (volume-conserving!).
    """
    rx = -0.78 * stretch_y + squash_x
    f.P.add("rim_n", x=0.42 * shear_x, y=stretch_y)
    f.P.add("rim_s", x=-0.20 * shear_x, y=-stretch_y)
    f.P.add("rim_w", x=-rx + 0.32 * shear_x, y=0.14 * stretch_y)
    f.P.add("rim_e", x=rx + 0.32 * shear_x, y=-0.14 * stretch_y)


def halo_expand(f: Frame, delta_r: float) -> None:
    """Zero-rotation, zero-scale radial expansion of `beckon_halo_ring` & `intangible_shimmer` via 4 halo bones."""
    if abs(delta_r) > 1e-4:
        f.P.add("halo_n", y=delta_r)
        f.P.add("halo_s", y=-delta_r)
        f.P.add("halo_w", x=-delta_r)
        f.P.add("halo_e", x=delta_r)


def make_anim_funcs(variant: int) -> dict:
    # ------------------------------------------------------------------
    # 1. `idle_loop` (2.80s, looping — pure single-roe Lissajous buoyancy!)
    # ------------------------------------------------------------------
    def idle_loop(t: float) -> Frame:
        f = Frame()
        wt = TAU * (t / 2.80)

        # Subtle variant-distinct underwater Lissajous paths (pos(0)==pos(2.80)==(0,0) & vel(0)==vel(2.80)!)
        if variant == 1:
            sway_x = 5.2 * math.sin(2.0 * wt)
            bob_y = 10.5 * math.sin(wt)
            ph_rim = -0.38
        elif variant == 2:
            sway_x = -5.6 * (math.cos(wt) - 1.0)
            bob_y = 10.0 * (math.sin(wt + 0.65) - math.sin(0.65))
            ph_rim = 0.25
        else:
            sway_x = 4.8 * math.sin(wt)
            bob_y = 10.8 * (math.sin(wt - 0.58) - math.sin(-0.58))
            ph_rim = -0.95

        f.P.add("main_center", x=sway_x, y=bob_y)
        f.P.add("shadow_b", x=0.88 * sway_x)

        # Gentle surface-tension rim breathing (+-4.2px) + liquid-lagged golden yolk float
        rim_cage(
            f,
            stretch_y=4.2 * math.sin(wt + ph_rim) + 1.1 * math.sin(2.0 * wt),
            shear_x=2.0 * math.sin(wt + ph_rim - 0.5),
        )
        f.P.add(
            "yolk_b",
            x=3.0 * (math.sin(wt + ph_rim - 0.55) - math.sin(ph_rim - 0.55)),
            y=-5.6 * (math.sin(wt + ph_rim - 0.42) - math.sin(ph_rim - 0.42)),
        )

        # Gentle periodic core bioluminescence heartbeat
        pulse = 0.5 * (1.0 - math.cos(2.0 * wt))
        f.slot_alpha["core_beckon_glow"] = 0.16 * pulse
        f.slot_alpha["rear_glow"] = 0.78 + 0.14 * pulse
        f.slot_alpha["shadow"] = clamp(1.0 - 0.012 * bob_y)
        return f

    # ------------------------------------------------------------------
    # 2. `attack` (`啃咬`, 0.96s, `attack_contact` @ 0.42s — FULL-BODY SELF-FLING COMET RAM!)
    # ------------------------------------------------------------------
    def attack(t: float) -> Frame:
        f = Frame()

        # 0.00s..0.27s: Curved wind-up arc (+76px right, +24px rise -> -14px crouched launch line)
        # 0.27s..0.415s: Supersonic self-fling comet strike to -498px LEFT!
        # 0.415s..0.470s: Crisp 3-frame hydro-elastic hit-stop @ -498px (`attack_contact` @ 0.42s)!
        # 0.470s..0.96s: Buoyant vaulted rebound arc (+65px apex @ ~0.68s) settling smoothly to (0, 0)!
        if t <= 0.27:
            u_w = seg(t, 0.00, 0.27)
            mdx = key(t, (0.00, 0.0), (0.15, 54.0), (0.25, 76.0), (0.27, 74.0))
            mdy = key(t, (0.00, 0.0), (0.13, 24.0), (0.255, -14.0), (0.27, -12.0))
        elif t <= 0.410:
            u_s = seg(t, 0.27, 0.410)
            # Fast explosive acceleration into crisp arrival at -498px
            prog = 0.30 * ease_out(u_s) + 0.70 * (u_s ** 0.80)
            mdx = 74.0 + (-498.0 - 74.0) * prog
            mdy = -12.0 + 16.0 * ease(u_s)  # Rises smoothly from -12px crouch to +4px strike line
        elif t <= 0.470:
            # 3+ frame hydro-elastic hit-stop locked at -498px..-502px (`t = 0.42s` right inside!)
            u_h = seg(t, 0.410, 0.470)
            mdx = -498.0 - 4.0 * math.sin(math.pi * u_h)
            mdy = 4.0
        else:
            # Buoyant vaulted rebound arc (+65px apex @ ~0.68s -> smooth settle to (0, 0) at 0.96s)
            u_r = seg(t, 0.470, 0.960)
            mdx = -498.0 * (1.0 - ease(u_r))
            vault = 65.0 * math.sin(math.pi * (u_r ** 0.86)) * (1.0 - 0.18 * u_r)
            mdy = 4.0 * (1.0 - ease(u_r)) + vault

        f.P.add("main_center", x=mdx, y=mdy)
        f.P.add("shadow_b", x=mdx)
        f.slot_alpha["shadow"] = clamp(1.0 - 0.007 * max(0.0, mdy))

        # Phase envelopes for zero-scale surface-tension droplet deformation & yolk inertia
        coil_env = hold(t, 0.00, 0.16, 0.24, 0.285)
        flight_env = hold(t, 0.27, 0.30, 0.390, 0.412)
        impact_env = hold(t, 0.396, 0.410, 0.468, 0.535)
        rebound_wobble = kick(t, 0.475, freq=5.2, damp=6.8) * (1.0 - ease(seg(t, 0.76, 0.95)))

        # 1) Coil (0.00..0.27s): taut bead compression (all 4 rims pull inward -7px)
        # 2) Flight (0.27..0.415s): aerodynamic liquid-teardrop bullet (`rim_e` trails +18px, `rim_n/s` narrow -9.5px)
        # 3) Impact (0.415..0.47s): front `rim_w` pancakes +18px inward, `rim_n/s` splash +-15px vertically!
        # 4) Rebound (0.47..0.96s): damped elastic droplet oscillation
        f.P.add(
            "rim_w",
            x=7.0 * coil_env - 6.5 * flight_env + 18.0 * impact_env - 6.5 * rebound_wobble,
        )
        f.P.add(
            "rim_e",
            x=-7.0 * coil_env + 18.5 * flight_env - 6.0 * impact_env + 6.5 * rebound_wobble,
        )
        f.P.add(
            "rim_n",
            x=-3.5 * flight_env + 4.5 * impact_env,
            y=-7.0 * coil_env - 9.5 * flight_env + 15.5 * impact_env + 7.5 * rebound_wobble,
        )
        f.P.add(
            "rim_s",
            x=-3.5 * flight_env + 4.5 * impact_env,
            y=7.0 * coil_env + 9.5 * flight_env - 15.5 * impact_env - 7.5 * rebound_wobble,
        )

        # Golden yolk liquid inertia (`yolk_b`, r == 0.0 deg):
        # - Leans eagerly forward (-10px) during coil
        # - Trails slightly back (+9px) under supersonic launch acceleration
        # - Slams -14.5px forward against front membrane during 3-frame hit-stop (`顿感`!)
        f.P.add(
            "yolk_b",
            x=-10.0 * coil_env + 9.5 * flight_env - 14.5 * impact_env + 5.5 * rebound_wobble,
            y=-2.5 * coil_env + 1.5 * impact_env + 3.0 * rebound_wobble,
        )

        # Core charge-up on wind-up (0.70) + impact flash, keeping body golden & readable
        core_charge = 0.70 * coil_env + 0.45 * flight_env + 0.65 * bump(t, 0.405, 0.425, 0.50)
        f.slot_alpha["core_beckon_glow"] = clamp(core_charge)
        f.slot_alpha["rear_glow"] = clamp(0.78 + 0.22 * (coil_env + impact_env))

        # `body_trail` blazes at 0.96 directly behind the roe across supersonic strike & 3f hit-stop (0.275s..0.45s -> 0 by 0.54s)!
        if t < 0.265 or t >= 0.545:
            bt_a = 0.0
        elif t < 0.305:
            bt_a = 0.96 * ease_out(seg(t, 0.265, 0.305))
        elif t <= 0.450:
            bt_a = 0.96
        else:
            bt_a = 0.96 * (1.0 - ease(seg(t, 0.450, 0.545)))
        f.slot_alpha["body_trail"] = clamp(bt_a)

        # Gently angle `body_trail_b` along flight tangent during launch (-3.5 deg), returning cleanly to 0 deg!
        if 0.26 <= t <= 0.56:
            f.P.add("body_trail_b", r=-3.2 * hold(t, 0.26, 0.30, 0.42, 0.55))

        # `impact_flare` flashes crisply (0.96 > 0.85!) at t=0.42s on the front-left contact edge!
        imp_a = 0.96 * plateau(t, 0.396, 0.410, 0.460, 0.530, p=1.6)
        f.slot_alpha["impact_flare"] = clamp(imp_a)
        # Follow pancaked front-left rim (+12px inward) during hit-stop
        f.P.add("impact_b", x=10.0 * impact_env)

        return f

    # ------------------------------------------------------------------
    # 3. `cast` (`召唤` / Beckon, 1.12s, `cast_release` @ 0.50s)
    # ------------------------------------------------------------------
    def cast(t: float) -> Frame:
        f = Frame()
        gather = hold(t, 0.00, 0.15, 0.20, 0.29)
        lev = plateau(t, 0.22, 0.46, 0.70, 1.08, p=2.2)
        pulse1 = bump(t, 0.24, 0.50, 0.61)
        pulse2 = bump(t, 0.53, 0.66, 1.04)
        lantern = clamp(max(pulse1, 0.98 * pulse2, 0.78 * hold(t, 0.34, 0.48, 0.68, 0.96)))

        mdy = -6.5 * gather + 22.0 * lev
        f.P.add("main_center", y=mdy)
        f.slot_alpha["shadow"] = clamp(1.0 - 0.30 * lev)

        f.tint = (1.0 - 0.08 * gather, 1.0 - 0.11 * gather, 1.0 - 0.16 * gather, 1.0)
        # Symmetrical breathing fullness during golden lantern pulses
        rad_full = -5.0 * gather + 5.5 * pulse1 + 4.5 * pulse2
        f.P.add("rim_n", y=rad_full)
        f.P.add("rim_s", y=-rad_full)
        f.P.add("rim_w", x=-rad_full)
        f.P.add("rim_e", x=rad_full)
        f.P.add("yolk_b", y=-3.0 * gather + 5.5 * lev + 2.5 * (pulse1 - 0.5 * pulse2))

        f.slot_alpha["core_beckon_glow"] = lantern
        f.slot_alpha["rear_glow"] = clamp(0.78 - 0.14 * gather + 0.22 * lantern)

        halo_a = clamp(0.95 * pulse1 + 0.88 * pulse2)
        f.slot_alpha["beckon_halo_ring"] = halo_a
        halo_r = -10.0 * gather + 30.0 * ease_out(seg(t, 0.26, 0.88)) * (1.0 - ease(seg(t, 0.88, 1.10)))
        halo_expand(f, halo_r)
        return f

    # ------------------------------------------------------------------
    # 4. `power_up` (`虚化` / Intangible, 1.14s, `power_peak` @ 0.52s)
    # ------------------------------------------------------------------
    def power_up(t: float) -> Frame:
        f = Frame()
        dip = bump(t, 0.00, 0.14, 0.26)
        rise = plateau(t, 0.18, 0.48, 0.64, 1.10, p=2.2)
        peak = bump(t, 0.36, 0.52, 0.72)
        shimmer_osc = 0.05 * math.sin(TAU * 12.0 * t) * hold(t, 0.34, 0.46, 0.66, 0.82)
        spectral = clamp(0.94 * rise + 0.14 * peak + shimmer_osc)

        mdy = -4.5 * dip + 26.0 * rise
        mdx = 4.5 * math.sin(TAU * (t / 1.14)) * rise
        f.P.add("main_center", x=mdx, y=mdy)
        f.P.add("shadow_b", x=0.82 * mdx)

        rim_cage(f, stretch_y=-3.5 * dip + 6.5 * rise + 1.8 * math.sin(TAU * 6.0 * t) * rise)
        f.P.add("yolk_b", x=-1.5 * mdx * 0.35, y=6.0 * rise)

        # Pearlescent SoulFysh spectral light (`core_intangible_glow` -> 1.0, `intangible_shimmer` -> 0.96 @ 0.52s)
        f.slot_alpha["core_intangible_glow"] = clamp(max(spectral, 1.0 * bump(t, 0.32, 0.52, 0.88)))
        f.slot_alpha["intangible_shimmer"] = clamp(max(0.92 * rise, 0.96 * bump(t, 0.30, 0.52, 0.92)))
        shimmer_r = -6.0 * dip + 24.0 * ease_out(seg(t, 0.22, 0.86)) * (1.0 - ease(seg(t, 0.86, 1.10)))
        halo_expand(f, shimmer_r)

        # Outer cytoplasm becomes crystalline-ethereal (`slot_alpha` dipping to 0.62 @ 0.52s -> 1.0 @ 1.14s)
        ethereal_dip = clamp(0.75 * rise + 0.25 * peak)
        f.slot_alpha["cytoplasm"] = round(1.0 - 0.38 * ethereal_dip, 4)
        f.slot_alpha["veins_highlight"] = clamp(0.94 + 0.06 * spectral)
        f.slot_alpha["shadow"] = clamp(1.0 - 0.72 * ethereal_dip)
        return f

    # ------------------------------------------------------------------
    # 5. `summon` (0.76s, `summon_appear` @ 0.36s)
    # ------------------------------------------------------------------
    def summon(t: float) -> Frame:
        f = Frame()
        fade_in = ease_out(seg(t, 0.00, 0.14))
        condense = 1.0 - ease_out(seg(t, 0.00, 0.36))
        overshoot = bump(t, 0.26, 0.36, 0.54)
        wobble = kick(t, 0.30, freq=5.8, damp=7.0) * (1.0 - ease(seg(t, 0.56, 0.75)))

        mdy = 22.0 * condense - 5.0 * overshoot
        f.P.add("main_center", y=mdy)
        f.tint = (1.0, 1.0, 1.0, fade_in)
        f.slot_alpha["shadow"] = fade_in * (1.0 - 0.50 * condense)

        # Newborn droplet pop-in: starts compressed (-15px inward), pops outward (+9px @ 0.36s), wobbles to rest!
        rad_pop = -15.0 * condense + 9.0 * overshoot
        f.P.add("rim_n", y=rad_pop + 6.0 * wobble)
        f.P.add("rim_s", y=-rad_pop - 6.0 * wobble)
        f.P.add("rim_w", x=-rad_pop + 5.0 * wobble)
        f.P.add("rim_e", x=rad_pop - 5.0 * wobble)
        f.P.add("yolk_b", y=7.0 * condense - 4.0 * overshoot + 3.5 * wobble)

        flare = (1.0 - ease(seg(t, 0.12, 0.56))) * fade_in
        f.slot_alpha["core_beckon_glow"] = clamp(0.94 * flare + 0.35 * overshoot)
        f.slot_alpha["core_intangible_glow"] = clamp(0.58 * flare)
        f.slot_alpha["beckon_halo_ring"] = clamp(0.82 * bump(t, 0.08, 0.32, 0.58))
        halo_expand(f, -18.0 * condense + 24.0 * ease_out(seg(t, 0.12, 0.56)) * (1.0 - ease(seg(t, 0.56, 0.75))))
        return f

    # ------------------------------------------------------------------
    # 6. `hurt` (0.42s, events=[])
    # ------------------------------------------------------------------
    def hurt(t: float) -> Frame:
        f = Frame()
        jolt = plateau(t, 0.00, 0.045, 0.105, 0.34, p=1.6)
        rip = kick(t, 0.055, freq=7.0, damp=9.5) * (1.0 - ease(seg(t, 0.26, 0.41)))
        rebound = bump(t, 0.14, 0.21, 0.33)

        # Crisp +42px right hydro-shock jolt
        mdx = 42.0 * jolt + 6.0 * rip
        mdy = 4.0 * jolt - 2.5 * rebound
        f.P.add("main_center", x=mdx, y=mdy)
        f.P.add("shadow_b", x=0.88 * mdx)

        # Left-rim hydro-dent + vertical splash oscillation
        f.P.add("rim_w", x=13.5 * jolt + 4.5 * rip)
        f.P.add("rim_e", x=6.5 * jolt - 4.0 * rip)
        f.P.add("rim_n", x=4.5 * jolt, y=10.5 * jolt - 4.5 * rebound + 4.0 * rip)
        f.P.add("rim_s", x=3.5 * jolt, y=-10.5 * jolt + 4.5 * rebound - 4.0 * rip)

        # -16.5px delayed `yolk_b` liquid suspension inertia lag, then +8px internal slosh rebound!
        f.P.add("yolk_b", x=-16.5 * jolt + 8.0 * rebound - 3.5 * rip, y=-2.5 * jolt)
        f.slot_alpha["core_intangible_glow"] = clamp(0.24 * bump(t, 0.00, 0.05, 0.18))
        return f

    # ------------------------------------------------------------------
    # 7 & 8. `die` (0.78s, `rupture_burst` @ 0.28s) & `revive` (0.68s)
    # ------------------------------------------------------------------
    def _die_eval(u: float, is_revive: bool = False) -> Frame:
        f = Frame()
        trem_env = bump(u, 0.02, 0.18, 0.28)
        freq = 6.0 if is_revive else 18.0
        trem_x = (2.8 if is_revive else 6.8) * math.sin(TAU * freq * u) * trem_env
        trem_y = (2.0 if is_revive else 4.2) * math.cos(TAU * freq * u) * trem_env

        pinch = bump(u, 0.02, 0.22, 0.29)
        burst = ease_out(seg(u, 0.26, 0.76))
        dissolve = ease(seg(u, 0.28, 0.68))
        end_fade = 1.0 - ease(seg(u, 0.56, 0.78))

        f.P.add("main_center", x=trem_x, y=trem_y + 20.0 * burst)

        # Radial membrane rupture (`rim_n/s/w/e` outward +24px as membrane dissolves!)
        rad_exp = -7.5 * pinch + 24.0 * burst
        f.P.add("rim_n", y=rad_exp)
        f.P.add("rim_s", y=-rad_exp)
        f.P.add("rim_w", x=-rad_exp)
        f.P.add("rim_e", x=rad_exp)
        f.P.add("yolk_b", x=-0.5 * trem_x, y=10.0 * burst)

        # Blinding core flash @ 0.26..0.30s + radial rupture rings & `impact_flare` burst!
        flash = hold(u, 0.04, 0.25, 0.31, 0.64) * end_fade
        f.slot_alpha["core_beckon_glow"] = clamp(1.00 * flash)
        f.slot_alpha["core_intangible_glow"] = clamp(1.00 * flash)
        f.slot_alpha["beckon_halo_ring"] = clamp(0.92 * bump(u, 0.22, 0.32, 0.68) * end_fade)
        f.slot_alpha["intangible_shimmer"] = clamp(0.95 * bump(u, 0.24, 0.36, 0.74) * end_fade)
        halo_expand(f, -9.0 * pinch + 40.0 * burst)

        # Shift `impact_b` +74px right so `impact_flare` bursts right from `main_center` @ 0.28s!
        rupture_flare = bump(u, 0.24, 0.29, 0.52) * end_fade
        f.P.add("impact_b", x=74.0 * ease_out(seg(u, 0.22, 0.28)))
        f.slot_alpha["impact_flare"] = clamp(0.94 * rupture_flare)

        keep_main = (1.0 - dissolve) * end_fade
        f.slot_alpha["cytoplasm"] = keep_main
        f.slot_alpha["membrane_ring"] = keep_main
        f.slot_alpha["nucleus"] = keep_main
        f.slot_alpha["veins_highlight"] = 0.94 * keep_main
        f.slot_alpha["rear_glow"] = 0.78 * keep_main
        f.slot_alpha["shadow"] = (1.0 - ease(seg(u, 0.24, 0.58))) * end_fade
        f.tint = (1.0, 1.0, 1.0, end_fade)
        return f

    def die(t: float) -> Frame:
        return _die_eval(clamp(t, 0.0, 0.78), is_revive=False)

    def revive(t: float) -> Frame:
        u = 0.78 * (1.0 - ease(seg(t, 0.0, 0.68)))
        return _die_eval(u, is_revive=True)

    return {
        "idle_loop": idle_loop,
        "attack": attack,
        "cast": cast,
        "power_up": power_up,
        "summon": summon,
        "hurt": hurt,
        "die": die,
        "revive": revive,
    }
