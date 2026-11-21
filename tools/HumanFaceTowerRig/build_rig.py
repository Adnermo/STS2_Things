#!/usr/bin/env python3
"""Build Spine 4.2.43 concave cylindrical tower rig, 9x7 3D relief face lattices & heavy stone physics for 人面塔."""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PARTS_DIR = ROOT / "source_assets" / "monsters" / "human_face_tower" / "parts"
OUT_DIR = ROOT / "STS2_Things" / "animations" / "monsters" / "human_face_tower"
OUT_DIR.mkdir(parents=True, exist_ok=True)

KEY = "human_face_tower"
RES_DIR = f"res://STS2_Things/animations/monsters/{KEY}"

# =============================================================================
# TRUE CONCAVE CYLINDRICAL GEOMETRY MATCHED TO ARENA_MASTER.JPG (1920x1080)
# =============================================================================
# Spine origin at screen center (960, 540), +X right, +Y up.
R_CYL = 1720.0
WALL_STRIPS = 24  # per 90° sector -> 96 strips around 360° (3.75° per strip)
WALL_ROWS = 2  # Top & bottom endpoints per column linearly fan all 7 brick courses & 3-point tilt!
FACE_NAMES = ("wrath", "lament", "bastion", "mockery")

SEC_W, SEC_H = 1600, 360
FACE_W, FACE_H = 540, 360

# High-resolution 3D Cylindrical Relief Lattice per Sovereign Face:
#   9 cols x 7 rows = 63 carrier bones (rows at y540 = 0, 60, 120[brows], 180[eyes/nose], 240[upper lip], 300[lower lip/chin], 360)
#   Outer ring (28 border nodes) is 100% welded to pilasters/cornice/sill; 35 interior nodes articulate expressions cleanly!
LAT_NX, LAT_NY = 8, 6
MESH_NX, MESH_NY = 16, 12
FACE_HALF_DEG = 7.7625  # Exact [662..938] central masonry bay span (276/1600 * 90° = 15.525°) between pilasters!
FACE_STAGE_W = 466.0
FACE_STAGE_H = 360.0


def smoothstep(t: float | np.ndarray) -> float | np.ndarray:
    x = np.clip(t, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def wrap_pi(angle: float) -> float:
    return (angle + math.pi) % (2.0 * math.pi) - math.pi


def pt540(img_x: float, img_y: float, sigma_x: float, sigma_y: float | None = None) -> tuple[float, float, float, float]:
    """Convert measured 540x360 image pixel (img_x, img_y) to centered (+X right, +Y up) anchor with anisotropic sigmas."""
    return (img_x - 270.0, 180.0 - img_y, sigma_x, sigma_y if sigma_y is not None else sigma_x)


def wall_seam_y(screen_dx: float) -> tuple[float, float]:
    """Exact measured cornice shadow lip y_top and sunlit floor sill joint y_bot at |x - 960| = screen_dx."""
    dx_top = min(1.32, abs(screen_dx) / 860.0)
    dx_bot = min(1.32, abs(screen_dx) / 800.0)
    y_top = 385.0 - 242.0 * (dx_top**2.06)
    y_bot = 690.0 + 32.0 * (dx_bot**2.00)
    return y_top, y_bot


def concave_wall_point(
    theta: float,
    v_norm: float,
    *,
    relief_px: float = 0.0,
    wall_tuck: bool = False,
) -> tuple[float, float]:
    """Project cylindrical point (theta, v_norm, inward relief_px) to Spine stage (bx, by)."""
    clamped = max(-0.46 * math.pi, min(0.46 * math.pi, theta))
    sin_t = math.sin(clamped)
    cos_t = math.cos(clamped)

    v_eff = (-0.11 + 1.18 * v_norm) if wall_tuck else v_norm
    x_tilt = (v_eff - 0.45) * 38.0 * sin_t
    wall_bx = R_CYL * sin_t + x_tilt
    bx = (R_CYL - relief_px) * sin_t + x_tilt

    sy_top, sy_bot = wall_seam_y(wall_bx)
    sy = sy_top + v_eff * (sy_bot - sy_top)
    sy -= relief_px * 0.14 * (1.0 - cos_t)
    by = 540.0 - sy
    return bx, by


def face_relief_profile(u: float, v: float) -> float:
    """Sculpted 3D relief depth (px): strictly 0.0 on all 4 bay edges -> 40px inward protrusion at brow/nose."""
    if u <= 1e-5 or u >= 1.0 - 1e-5 or v <= 1e-5 or v >= 1.0 - 1e-5:
        return 0.0
    hu = math.sin(math.pi * u) ** 0.85
    hv = math.sin(math.pi * v) ** 0.80
    brow_nose_boost = 1.0 + 0.16 * math.exp(-0.5 * ((v - 0.44) / 0.22) ** 2)
    return 40.0 * hu * hv * brow_nose_boost


def face_lattice_pose(face_center_theta: float, ix: int, iy: int) -> tuple[float, float, float, float]:
    """Return (bx, by, slope_deg, foreshorten_sx) for lattice node (ix, iy) welded flush to the central masonry bay."""
    u = ix / LAT_NX
    v = iy / LAT_NY
    half_rad = math.radians(FACE_HALF_DEG)
    theta_pt = wrap_pi(face_center_theta + (u - 0.5) * 2.0 * half_rad)
    rel = face_relief_profile(u, v)
    bx, by = concave_wall_point(theta_pt, v, relief_px=rel, wall_tuck=True)

    clamped = max(-0.46 * math.pi, min(0.46 * math.pi, theta_pt))
    sx = max(0.24, math.cos(clamped))
    eps = 0.018
    if abs(clamped) < 0.43 * math.pi:
        x1, y1 = concave_wall_point(clamped - eps, v, relief_px=rel, wall_tuck=True)
        x2, y2 = concave_wall_point(clamped + eps, v, relief_px=rel, wall_tuck=True)
        slope_deg = math.degrees(math.atan2(y2 - y1, max(1.0, x2 - x1))) * 0.75
    else:
        slope_deg = 0.0
    return bx, by, slope_deg, sx


def eval_lattice_stage_point(face_center_theta: float, x540: float, y540: float) -> tuple[float, float]:
    """Evaluate exact bilinear Spine stage position (bx, by) of any (x540, y540) point on the 9x7 lattice."""
    gx = min(float(LAT_NX) - 1e-6, max(0.0, (x540 / FACE_W) * LAT_NX))
    gy = min(float(LAT_NY) - 1e-6, max(0.0, (y540 / FACE_H) * LAT_NY))
    i0 = int(math.floor(gx))
    j0 = int(math.floor(gy))
    i1 = min(LAT_NX, i0 + 1)
    j1 = min(LAT_NY, j0 + 1)
    fx = gx - i0
    fy = gy - j0
    p00 = face_lattice_pose(face_center_theta, i0, j0)[:2]
    p10 = face_lattice_pose(face_center_theta, i1, j0)[:2]
    p01 = face_lattice_pose(face_center_theta, i0, j1)[:2]
    p11 = face_lattice_pose(face_center_theta, i1, j1)[:2]
    bx = (
        p00[0] * (1.0 - fx) * (1.0 - fy)
        + p10[0] * fx * (1.0 - fy)
        + p01[0] * (1.0 - fx) * fy
        + p11[0] * fx * fy
    )
    by = (
        p00[1] * (1.0 - fx) * (1.0 - fy)
        + p10[1] * fx * (1.0 - fy)
        + p01[1] * (1.0 - fx) * fy
        + p11[1] * fx * fy
    )
    return bx, by


def wall_shade_and_vis(theta: float) -> tuple[float, float]:
    """Unified concave-chamber lighting & off-screen-only rear-hemisphere culling (|theta| > 51.5°, >300px off-screen)."""
    clamped = max(-0.46 * math.pi, min(0.46 * math.pi, theta))
    shade = 0.38 + 0.62 * (max(0.0, math.cos(clamped)) ** 1.68)
    c_cut = math.cos(math.radians(58.0))
    vis = float(smoothstep((math.cos(theta) - c_cut) / 0.095))
    return shade, vis


def face_shade_and_vis(face_center_theta: float) -> tuple[float, float]:
    """Identical lighting to flanking wall bays; 100% opaque across the entire screen until >240px off-screen!"""
    clamped = max(-0.46 * math.pi, min(0.46 * math.pi, face_center_theta))
    shade = 0.38 + 0.62 * (max(0.0, math.cos(clamped)) ** 1.68)
    c_cut = math.cos(math.radians(58.0))
    vis = float(smoothstep((math.cos(face_center_theta) - c_cut) / 0.095))
    return shade, vis


def rgba_hex(r: float, g: float, b: float, a: float) -> str:
    ri = max(0, min(255, round(r * 255)))
    gi = max(0, min(255, round(g * 255)))
    bi = max(0, min(255, round(b * 255)))
    ai = max(0, min(255, round(a * 255)))
    return f"{ri:02x}{gi:02x}{bi:02x}{ai:02x}"


def write_godot_png_import(png_path: Path) -> None:
    rel_res = "res://" + png_path.relative_to(ROOT).as_posix()
    digest = hashlib.md5(rel_res.encode("utf-8")).hexdigest()
    uid_seed = hashlib.sha1(rel_res.encode("utf-8")).hexdigest()[:12]
    ctex = f"res://.godot/imported/{png_path.name}-{digest}.ctex"
    png_path.with_suffix(".png.import").write_text(
        f"""[remap]

importer="texture"
type="CompressedTexture2D"
uid="uid://hft{uid_seed}"
path="{ctex}"
metadata={{
"vram_texture": false
}}

[deps]

source_file="{rel_res}"
dest_files=["{ctex}"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
""",
        encoding="utf-8",
    )


def pack_atlas(parts: dict[str, Image.Image]) -> tuple[Image.Image, dict[str, tuple[int, int, int, int]], str]:
    """Skyline/shelf-pack RGBA parts into a 2048x2048 atlas with 2px extruded borders."""
    aw, ah = 2048, 2048
    atlas_img = Image.new("RGBA", (aw, ah), (0, 0, 0, 0))
    rects: dict[str, tuple[int, int, int, int]] = {}
    x, y, shelf_h = 4, 4, 0
    for name, im in sorted(parts.items(), key=lambda kv: (-kv[1].height, -kv[1].width, kv[0])):
        w, h = im.size
        if x + w + 4 > aw:
            x = 4
            y += shelf_h + 6
            shelf_h = 0
        if y + h + 4 > ah:
            raise RuntimeError(f"Atlas overflow packing {name} ({w}x{h}) at y={y}")
        atlas_img.alpha_composite(im, (x, y))
        atlas_img.paste(im.crop((0, 0, 1, h)).resize((2, h)), (x - 2, y))
        atlas_img.paste(im.crop((w - 1, 0, w, h)).resize((2, h)), (x + w, y))
        atlas_img.paste(im.crop((0, 0, w, 1)).resize((w, 2)), (x, y - 2))
        atlas_img.paste(im.crop((0, h - 1, w, h)).resize((w, 2)), (x, y + h))
        rects[name] = (x, y, w, h)
        x += w + 6
        shelf_h = max(shelf_h, h)

    lines = [
        f"{KEY}.png",
        f"size: {aw},{ah}",
        "format: RGBA8888",
        "filter: Linear,Linear",
        "repeat: none",
    ]
    for name, (rx, ry, rw, rh) in rects.items():
        lines += [
            name,
            "  rotate: false",
            f"  xy: {rx}, {ry}",
            f"  size: {rw}, {rh}",
            f"  orig: {rw}, {rh}",
            "  offset: 0, 0",
            "  index: -1",
        ]
    return atlas_img, rects, "\n".join(lines) + "\n"


def eval_hermite_track(keys: list[tuple[float, float]], t: float) -> float:
    if t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            u = (t - t0) / max(1e-6, t1 - t0)
            s = float(smoothstep(u))
            return v0 + (v1 - v0) * s
    return keys[-1][1]


def sample_curve(keys: list[tuple[float, float]], fps: int = 30) -> list[dict[str, float]]:
    dur = keys[-1][0]
    n = max(2, round(dur * fps) + 1)
    ts = np.linspace(0.0, dur, n)
    return [{"time": round(float(t), 4), "value": round(eval_hermite_track(keys, float(t)), 4)} for t in ts]


def sample_xy_curve(
    x_keys: list[tuple[float, float]],
    y_keys: list[tuple[float, float]],
    fps: int = 30,
) -> list[dict[str, float]]:
    dur = max(x_keys[-1][0], y_keys[-1][0])
    n = max(2, round(dur * fps) + 1)
    ts = np.linspace(0.0, dur, n)
    return [
        {
            "time": round(float(t), 4),
            "x": round(eval_hermite_track(x_keys, float(t)), 4),
            "y": round(eval_hermite_track(y_keys, float(t)), 4),
        }
        for t in ts
    ]


def build_cyl_lattice_mesh(
    part_name: str,
    bone_ids: dict[str, int],
    prefix: str,
    *,
    width: int = FACE_W,
    height: int = FACE_H,
    sub_box: tuple[float, float, float, float] | None = None,
    nx: int = MESH_NX,
    ny: int = MESH_NY,
) -> dict:
    """Build a 3D cylindrical-lattice weighted Spine mesh with pure bilinear zero-offset bone weights."""
    uvs: list[float] = []
    vertices: list[float] = []
    triangles: list[int] = []

    x0, y0, x1, y1 = sub_box if sub_box is not None else (0.0, 0.0, float(FACE_W), float(FACE_H))

    for iy in range(ny + 1):
        tv = iy / ny
        u_v = (y0 + tv * (y1 - y0)) / FACE_H
        gy = min(float(LAT_NY) - 1e-6, max(0.0, u_v * LAT_NY))
        j0 = int(math.floor(gy))
        j1 = min(LAT_NY, j0 + 1)
        fy = gy - j0

        for ix in range(nx + 1):
            tu = ix / nx
            u_u = (x0 + tu * (x1 - x0)) / FACE_W
            gx = min(float(LAT_NX) - 1e-6, max(0.0, u_u * LAT_NX))
            i0 = int(math.floor(gx))
            i1 = min(LAT_NX, i0 + 1)
            fx = gx - i0

            uvs += [round(tu, 5), round(tv, 5)]
            influences = [
                (bone_ids[f"{prefix}_{i0}_{j0}"], (1.0 - fx) * (1.0 - fy)),
                (bone_ids[f"{prefix}_{i1}_{j0}"], fx * (1.0 - fy)),
                (bone_ids[f"{prefix}_{i0}_{j1}"], (1.0 - fx) * fy),
                (bone_ids[f"{prefix}_{i1}_{j1}"], fx * fy),
            ]
            active = [(bid, w) for bid, w in influences if w > 1e-4]
            w_sum = sum(w for _, w in active)
            vertices.append(len(active))
            for bid, w in active:
                vertices += [bid, 0.0, 0.0, round(w / w_sum, 5)]

    stride = nx + 1
    for iy in range(ny):
        for ix in range(nx):
            v0 = iy * stride + ix
            v1 = v0 + 1
            v2 = v0 + stride
            v3 = v2 + 1
            triangles += [v0, v1, v2, v1, v3, v2]

    return {
        "type": "mesh",
        "path": part_name,
        "uvs": uvs,
        "vertices": vertices,
        "triangles": triangles,
        "hull": 4,
        "width": width,
        "height": height,
    }


def build_sub_mesh(
    part_name: str,
    part_layout: dict[str, dict[str, object]],
    bone_ids: dict[str, int],
    prefix: str,
) -> dict:
    info = part_layout[part_name]
    bx0, by0, bx1, by1 = [float(v) for v in info["sub_box"]]
    w, h = [int(v) for v in info["size"]]
    nx = max(4, round((bx1 - bx0) / 28.0))
    ny = max(3, round((by1 - by0) / 24.0))
    return build_cyl_lattice_mesh(
        part_name,
        bone_ids,
        prefix,
        width=w,
        height=h,
        sub_box=(bx0, by0, bx1, by1),
        nx=nx,
        ny=ny,
    )


def bake_face_deform_bones(
    face_idx: int,
    duration: float,
    anchor_defs: dict[str, tuple[float, float, float, float]],
    *,
    core_tx: list[tuple[float, float]] | None = None,
    core_ty: list[tuple[float, float]] | None = None,
    core_sx: list[tuple[float, float]] | None = None,
    core_sy: list[tuple[float, float]] | None = None,
    core_rot: list[tuple[float, float]] | None = None,
    features: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] | None = None,
    tup_tx: list[tuple[float, float]] | None = None,
    tup_ty: list[tuple[float, float]] | None = None,
    tlo_tx: list[tuple[float, float]] | None = None,
    tlo_ty: list[tuple[float, float]] | None = None,
    clamp_scale: list[tuple[float, float]] | None = None,
    pupil_tx: list[tuple[float, float]] | None = None,
    pupil_ty: list[tuple[float, float]] | None = None,
    fps: int = 30,
) -> dict[str, dict]:
    """Bake expressive anatomical Track-2 motion onto the 9x7 interior child deform lattice bones.

    - Outer masonry border nodes (ix in {0,8} or iy in {0,6}) stay strictly at setup (0, 0) (100% welded to wall!).
    - Face 0 (`Wrath`) drives dual-row teeth (`f0_tup_def`, `f0_tlo_def`) clamped so `tlo_dy <= tup_dy` (zero crossover!).
    - Face 2 (`Bastion`) drives `f2_clamp_def` (intact clamp + facet-locked keystone glow).
    - Face 3 (`Mockery`) drives `f3_pupil_def` (independent stone pupil gaze behind `f3_def` eyelids!).
    """
    n = max(2, round(duration * fps) + 1)
    ts = np.linspace(0.0, duration, n)
    out_bones: dict[str, dict] = {}

    ctx = core_tx or [(0.0, 0.0), (duration, 0.0)]
    cty = core_ty or [(0.0, 0.0), (duration, 0.0)]
    csx = core_sx or [(0.0, 1.0), (duration, 1.0)]
    csy = core_sy or [(0.0, 1.0), (duration, 1.0)]
    crot = core_rot or [(0.0, 0.0), (duration, 0.0)]
    feats = features or {}

    kx = FACE_STAGE_W / FACE_W
    ky = FACE_STAGE_H / FACE_H

    for iy in range(1, LAT_NY):
        v = iy / LAT_NY
        py540 = (0.5 - v) * FACE_H
        py_stage = py540 * ky
        # Smooth perimeter rolloff near top cornice (iy=1) & outer edges so stone extrudes naturally without pinching!
        edge_soft = math.sin(math.pi * v) ** 0.35
        for ix in range(1, LAT_NX):
            u = ix / LAT_NX
            px540 = (u - 0.5) * FACE_W
            px_stage = px540 * kx
            collar_w = (math.sin(math.pi * u) ** 0.35) * edge_soft

            tr_keys = []
            tup_keys = []
            tlo_keys = []
            clamp_keys = []
            pup_keys = []

            for t in ts:
                tf = float(t)
                tx = eval_hermite_track(ctx, tf)
                ty = eval_hermite_track(cty, tf)
                sx = eval_hermite_track(csx, tf)
                sy = eval_hermite_track(csy, tf)
                rot_rad = math.radians(eval_hermite_track(crot, tf))
                cr, sr = math.cos(rot_rad), math.sin(rot_rad)

                rx = px_stage * sx
                ry = py_stage * sy
                rot_x = rx * cr - ry * sr
                rot_y = rx * sr + ry * cr
                rigid_dx = ((rot_x - px_stage) + tx * kx) * collar_w
                rigid_dy = ((rot_y - py_stage) + ty * ky) * collar_w

                dx = rigid_dx
                dy = rigid_dy
                for fname, (fx_k, fy_k) in feats.items():
                    ax, ay, sig_x, sig_y = anchor_defs[fname]
                    w_f = math.exp(-0.5 * (((px540 - ax) / sig_x) ** 2 + ((py540 - ay) / sig_y) ** 2)) * collar_w
                    if w_f > 0.015:
                        dx += eval_hermite_track(fx_k, tf) * kx * w_f
                        dy += eval_hermite_track(fy_k, tf) * ky * w_f

                tr_keys.append({"time": round(tf, 4), "x": round(dx, 3), "y": round(dy, 3)})

                if face_idx == 0:
                    ux540 = eval_hermite_track(tup_tx, tf) if tup_tx else 0.0
                    uy540 = eval_hermite_track(tup_ty, tf) if tup_ty else 0.0
                    lx540 = eval_hermite_track(tlo_tx, tf) if tlo_tx else 0.0
                    ly540 = eval_hermite_track(tlo_ty, tf) if tlo_ty else 0.0
                    # Hard physical bite stop: lower teeth can never cross above upper teeth!
                    ly540 = min(ly540, uy540)
                    # Horizontal smile/snarl curvature tapers gently toward outer fangs
                    mouth_w = math.exp(-0.5 * ((px540 / 135.0) ** 2))
                    tup_keys.append(
                        {
                            "time": round(tf, 4),
                            "x": round(rigid_dx + ux540 * kx * mouth_w, 3),
                            "y": round(rigid_dy + uy540 * ky * mouth_w, 3),
                        }
                    )
                    tlo_keys.append(
                        {
                            "time": round(tf, 4),
                            "x": round(rigid_dx + lx540 * kx * mouth_w, 3),
                            "y": round(rigid_dy + ly540 * ky * mouth_w, 3),
                        }
                    )

                elif face_idx == 2:
                    cs = eval_hermite_track(clamp_scale, tf) if clamp_scale else 1.0
                    sm = (cs - 1.0) * collar_w
                    cdx = rigid_dx + (px_stage - 0.0) * sm
                    cdy = rigid_dy + (py_stage - (-70.0)) * sm
                    clamp_keys.append({"time": round(tf, 4), "x": round(cdx, 3), "y": round(cdy, 3)})

                elif face_idx == 3:
                    # Clamp pupil travel strictly within sclera socket bounds [-19..+5, -5..+6] in 540p space!
                    ptx540 = max(-19.0, min(5.0, eval_hermite_track(pupil_tx, tf) if pupil_tx else 0.0))
                    pty540 = max(-5.0, min(6.0, eval_hermite_track(pupil_ty, tf) if pupil_ty else 0.0))
                    pup_keys.append(
                        {
                            "time": round(tf, 4),
                            "x": round(rigid_dx + ptx540 * kx, 3),
                            "y": round(rigid_dy + pty540 * ky, 3),
                        }
                    )

            out_bones[f"f{face_idx}_def_{ix}_{iy}"] = {"translate": tr_keys}
            if face_idx == 0:
                out_bones[f"f0_tup_def_{ix}_{iy}"] = {"translate": tup_keys}
                out_bones[f"f0_tlo_def_{ix}_{iy}"] = {"translate": tlo_keys}
            elif face_idx == 2:
                out_bones[f"f2_clamp_def_{ix}_{iy}"] = {"translate": clamp_keys}
            elif face_idx == 3:
                out_bones[f"f3_pupil_def_{ix}_{iy}"] = {"translate": pup_keys}

    return out_bones


def main() -> None:
    part_layout: dict[str, dict[str, object]] = json.loads((PARTS_DIR / "part_layout.json").read_text(encoding="utf-8"))
    part_names = [
        "wall_sector_intact",
        "wall_sector_shattered",
        "wrath_throat_bg",
        "wrath_teeth_upper",
        "wrath_teeth_lower",
        "wrath_upper",
        "wrath_crack_glow",
        "lament_throat_bg",
        "lament_face",
        "lament_tear_glow",
        "bastion_throat_bg",
        "bastion_unbolted_face",
        "bastion_clamp_intact",
        "bastion_clamp_split_l",
        "bastion_clamp_split_r",
        "bastion_keystone_glow",
        "mockery_throat_bg",
        "mockery_eye_sclera",
        "mockery_pupils",
        "mockery_face",
        "mockery_spark_glow",
        "dust_puff",
        *[f"rubble_chip_{i}" for i in range(10)],
    ]
    parts = {name: Image.open(PARTS_DIR / f"{name}.png").convert("RGBA") for name in part_names}
    atlas_img, _, atlas_text = pack_atlas(parts)

    png_out = OUT_DIR / f"{KEY}.png"
    atlas_img.save(png_out)
    write_godot_png_import(png_out)
    (OUT_DIR / f"{KEY}.atlas").write_text(atlas_text, encoding="utf-8")
    (OUT_DIR / f"{KEY}.spatlas").write_text(
        json.dumps(
            {
                "source_path": f"{RES_DIR}/{KEY}.atlas",
                "atlas_data": atlas_text,
                "normal_texture_prefix": "n",
                "specular_texture_prefix": "s",
            }
        ),
        encoding="utf-8",
    )

    # =========================================================================
    # DEFINE SKELETON BONES
    # =========================================================================
    bones: list[dict] = [
        {"name": "root"},
        {"name": "camera_shake", "parent": "root"},
        {"name": "turntable_shake", "parent": "root"},
        {"name": "impact_shake", "parent": "turntable_shake"},
    ]

    # 1. Concave Cylindrical Wall Mesh Control Bones: 4 sectors x (WALL_STRIPS+1) cols x WALL_ROWS rows
    for s in range(4):
        for col in range(WALL_STRIPS + 1):
            for row in range(WALL_ROWS):
                theta0 = wrap_pi(s * 0.5 * math.pi + (col / WALL_STRIPS - 0.5) * 0.5 * math.pi)
                bx, by = concave_wall_point(theta0, row / (WALL_ROWS - 1), wall_tuck=True)
                bones.append(
                    {
                        "name": f"w_s{s}_c{col}_r{row}",
                        "parent": "impact_shake",
                        "x": round(bx, 3),
                        "y": round(by, 3),
                    }
                )

    # 2. 9x7 3D Cylindrical Relief Lattices & Measured Anatomical Anchors for All 4 Faces
    face_anchor_defs: list[dict[str, tuple[float, float, float, float]]] = [
        # Face 0: Wrath (嗔怒面)
        {
            "brow_L": pt540(176.0, 108.0, 66.0, 34.0),
            "brow_R": pt540(364.0, 108.0, 66.0, 34.0),
            "lid_L": pt540(164.0, 134.0, 52.0, 20.0),
            "lid_R": pt540(374.0, 134.0, 52.0, 20.0),
            "nose": pt540(270.0, 184.0, 54.0, 28.0),
            "upper_lip": pt540(270.0, 218.0, 108.0, 28.0),
            "lower_lip": pt540(270.0, 292.0, 112.0, 32.0),
            "corner_L": pt540(152.0, 256.0, 44.0, 32.0),
            "corner_R": pt540(388.0, 256.0, 44.0, 32.0),
        },
        # Face 1: Lament (悲叹面)
        {
            "brow_L": pt540(176.0, 106.0, 68.0, 34.0),
            "brow_R": pt540(364.0, 106.0, 68.0, 34.0),
            "lid_L": pt540(158.0, 136.0, 56.0, 20.0),
            "lid_R": pt540(382.0, 136.0, 56.0, 20.0),
            "upper_lip": pt540(269.0, 228.0, 84.0, 26.0),
            "lower_lip": pt540(269.0, 282.0, 92.0, 32.0),
            "cheek_L": pt540(138.0, 214.0, 50.0, 46.0),
            "cheek_R": pt540(406.0, 214.0, 50.0, 46.0),
        },
        # Face 2: Bastion (缄默面)
        {
            "brow_L": pt540(172.0, 108.0, 68.0, 32.0),
            "brow_R": pt540(368.0, 108.0, 68.0, 32.0),
            "lid_L": pt540(160.0, 136.0, 54.0, 18.0),
            "lid_R": pt540(380.0, 136.0, 54.0, 18.0),
            "upper_lip": pt540(269.8, 233.0, 88.0, 20.0),
            "lower_lip": pt540(269.8, 258.0, 90.0, 26.0),
        },
        # Face 3: Mockery (诡笑面)
        {
            "brow_L": pt540(168.0, 108.0, 64.0, 30.0),
            "brow_R": pt540(386.0, 102.0, 66.0, 30.0),
            "lid_L": pt540(174.0, 144.0, 52.0, 17.0),
            "lid_R": pt540(390.0, 140.0, 50.0, 17.0),
            "smirk_L": pt540(166.0, 210.0, 52.0, 28.0),
            "smirk_R": pt540(408.0, 204.0, 58.0, 30.0),
            "upper_lip": pt540(290.0, 220.0, 94.0, 22.0),
            "lower_lip": pt540(290.0, 258.0, 96.0, 28.0),
        },
    ]

    for k in range(4):
        theta0 = wrap_pi(k * 0.5 * math.pi)
        for iy in range(LAT_NY + 1):
            for ix in range(LAT_NX + 1):
                lx, ly, lrot, lsx = face_lattice_pose(theta0, ix, iy)
                lat_name = f"f{k}_lat_{ix}_{iy}"
                bones.append(
                    {
                        "name": lat_name,
                        "parent": "impact_shake",
                        "x": round(lx, 3),
                        "y": round(ly, 3),
                        "rotation": round(lrot, 3),
                        "scaleX": round(lsx, 4),
                    }
                )
                bones.append({"name": f"f{k}_def_{ix}_{iy}", "parent": lat_name})
                if k == 0:
                    bones.append({"name": f"f0_tup_def_{ix}_{iy}", "parent": lat_name})
                    bones.append({"name": f"f0_tlo_def_{ix}_{iy}", "parent": lat_name})
                elif k == 2:
                    bones.append({"name": f"f2_clamp_def_{ix}_{iy}", "parent": lat_name})
                elif k == 3:
                    bones.append({"name": f"f3_pupil_def_{ix}_{iy}", "parent": lat_name})

    # Calibrate Bastion's split clamp bones to exact stage coordinates of bastion_clamp_intact when Face 2 is centered (theta=0)!
    split_stage_specs: dict[str, tuple[float, float, float, float]] = {}
    for split_key, bname in (("bastion_clamp_split_l", "f2_clamp_split_l"), ("bastion_clamp_split_r", "f2_clamp_split_r")):
        sx0, sy0, sx1, sy1 = [float(v) for v in part_layout[split_key]["sub_box"]]
        cx_st, cy_st = eval_lattice_stage_point(0.0, 0.5 * (sx0 + sx1), 0.5 * (sy0 + sy1))
        lx_st, _ = eval_lattice_stage_point(0.0, sx0, 0.5 * (sy0 + sy1))
        rx_st, _ = eval_lattice_stage_point(0.0, sx1, 0.5 * (sy0 + sy1))
        _, ty_st = eval_lattice_stage_point(0.0, 0.5 * (sx0 + sx1), sy0)
        _, by_st = eval_lattice_stage_point(0.0, 0.5 * (sx0 + sx1), sy1)
        w_st = abs(rx_st - lx_st)
        h_st = abs(ty_st - by_st)
        split_stage_specs[split_key] = (cx_st, cy_st, w_st, h_st)
        bones.append({"name": bname, "parent": "impact_shake", "x": round(cx_st, 3), "y": round(cy_st, 3)})

    # 3. 8 Sill Track Dust Cloud Bones + 6 Sill Grinding Gravel Bones
    dust_x_anchors = [-610.0, -445.0, -280.0, -110.0, 110.0, 280.0, 445.0, 610.0]
    for i, dx_a in enumerate(dust_x_anchors):
        _, sy_b = wall_seam_y(dx_a)
        dy_a = 540.0 - (sy_b - 16.0)
        bones.append({"name": f"dust_{i}", "parent": "root", "x": round(dx_a, 2), "y": round(dy_a, 2)})

    grit_x_anchors = [-520.0, -330.0, -145.0, 145.0, 330.0, 520.0]
    for i, gx_a in enumerate(grit_x_anchors):
        _, sy_b = wall_seam_y(gx_a)
        gy_a = 540.0 - (sy_b - 8.0)
        bones.append({"name": f"grit_{i}", "parent": "root", "x": round(gx_a, 2), "y": round(gy_a, 2)})

    # 4. 10 Stone Rubble Explosion Bones
    chip_origins = [
        (-165.0, 88.0),
        (-95.0, 102.0),
        (0.0, 112.0),
        (98.0, 96.0),
        (168.0, 82.0),
        (-172.0, -24.0),
        (-86.0, -48.0),
        (12.0, -56.0),
        (96.0, -44.0),
        (174.0, -18.0),
    ]
    for i, (ox, oy) in enumerate(chip_origins):
        bones.append({"name": f"rubble_{i}", "parent": "root", "x": ox, "y": oy})

    bone_ids = {b["name"]: idx for idx, b in enumerate(bones)}

    # =========================================================================
    # DEFINE SLOTS & ATTACHMENTS
    # =========================================================================
    slots: list[dict] = []
    attachments: dict[str, dict] = {}

    # 1. Concave Cylindrical Wall Ring Strips
    for s in range(4):
        for strip in range(WALL_STRIPS):
            sname = f"wall_s{s}_{strip}"
            att_intact = f"intact_s{s}_{strip}"
            att_shattered = f"shattered_s{s}_{strip}"
            theta_mid = wrap_pi(s * 0.5 * math.pi + ((strip + 0.5) / WALL_STRIPS - 0.5) * 0.5 * math.pi)
            shade, vis = wall_shade_and_vis(theta_mid)
            slots.append(
                {
                    "name": sname,
                    "bone": "impact_shake",
                    "attachment": att_intact,
                    "color": rgba_hex(shade, shade, shade, vis),
                }
            )
            verts: list[float] = []
            uvs: list[float] = []
            tris: list[int] = []
            for row in range(WALL_ROWS):
                for col in (strip, strip + 1):
                    verts += [1, bone_ids[f"w_s{s}_c{col}_r{row}"], 0.0, 0.0, 1.0]
                    uvs += [round(col / WALL_STRIPS, 5), round(row / (WALL_ROWS - 1), 5)]
            for row in range(WALL_ROWS - 1):
                base = row * 2
                tris += [base, base + 1, base + 2, base + 1, base + 3, base + 2]
            mesh_def_intact = {
                "type": "mesh",
                "path": "wall_sector_intact",
                "uvs": uvs,
                "vertices": verts,
                "triangles": tris,
                "hull": WALL_ROWS * 2,
                "width": SEC_W,
                "height": SEC_H,
            }
            mesh_def_shat = dict(mesh_def_intact, path="wall_sector_shattered", vertices=list(verts))
            attachments[sname] = {att_intact: mesh_def_intact, att_shattered: mesh_def_shat}

    # 2. 4 Multi-Layer Articulated 3D Cylindrical-Relief Sovereign Stone Faces
    for k in range(4):
        theta0 = wrap_pi(k * 0.5 * math.pi)
        shade0, vis0 = face_shade_and_vis(theta0)
        base_col = rgba_hex(shade0, shade0, shade0, vis0)
        hidden_col = rgba_hex(1.0, 1.0, 1.0, 0.0)

        if k == 0:
            # Draw order: gullet -> lower teeth -> upper teeth -> continuous stone face/cheeks/lips -> forehead crack glow
            slots.append({"name": "f0_throat", "bone": "impact_shake", "attachment": "f0_throat_mesh", "color": base_col})
            attachments["f0_throat"] = {"f0_throat_mesh": build_sub_mesh("wrath_throat_bg", part_layout, bone_ids, "f0_def")}

            slots.append({"name": "f0_teeth_lo_slot", "bone": "impact_shake", "attachment": "f0_teeth_lo_mesh", "color": base_col})
            attachments["f0_teeth_lo_slot"] = {
                "f0_teeth_lo_mesh": build_sub_mesh("wrath_teeth_lower", part_layout, bone_ids, "f0_tlo_def")
            }

            slots.append({"name": "f0_teeth_up_slot", "bone": "impact_shake", "attachment": "f0_teeth_up_mesh", "color": base_col})
            attachments["f0_teeth_up_slot"] = {
                "f0_teeth_up_mesh": build_sub_mesh("wrath_teeth_upper", part_layout, bone_ids, "f0_tup_def")
            }

            slots.append({"name": "f0_face", "bone": "impact_shake", "attachment": "f0_mesh", "color": base_col})
            attachments["f0_face"] = {"f0_mesh": build_cyl_lattice_mesh("wrath_upper", bone_ids, "f0_def")}

            slots.append(
                {
                    "name": "f0_glow_slot",
                    "bone": "impact_shake",
                    "attachment": "f0_glow_mesh",
                    "color": hidden_col,
                    "blend": "additive",
                }
            )
            attachments["f0_glow_slot"] = {"f0_glow_mesh": build_sub_mesh("wrath_crack_glow", part_layout, bone_ids, "f0_def")}

        elif k == 1:
            slots.append({"name": "f1_throat", "bone": "impact_shake", "attachment": "f1_throat_mesh", "color": base_col})
            attachments["f1_throat"] = {"f1_throat_mesh": build_sub_mesh("lament_throat_bg", part_layout, bone_ids, "f1_def")}

            slots.append({"name": "f1_face", "bone": "impact_shake", "attachment": "f1_mesh", "color": base_col})
            attachments["f1_face"] = {"f1_mesh": build_cyl_lattice_mesh("lament_face", bone_ids, "f1_def")}

            slots.append(
                {
                    "name": "f1_glow_slot",
                    "bone": "impact_shake",
                    "attachment": "f1_glow_mesh",
                    "color": hidden_col,
                    "blend": "additive",
                }
            )
            attachments["f1_glow_slot"] = {"f1_glow_mesh": build_sub_mesh("lament_tear_glow", part_layout, bone_ids, "f1_def")}

        elif k == 2:
            slots.append({"name": "f2_throat", "bone": "impact_shake", "attachment": "f2_throat_mesh", "color": base_col})
            attachments["f2_throat"] = {"f2_throat_mesh": build_sub_mesh("bastion_throat_bg", part_layout, bone_ids, "f2_def")}

            slots.append({"name": "f2_face", "bone": "impact_shake", "attachment": "f2_mesh", "color": base_col})
            attachments["f2_face"] = {"f2_mesh": build_cyl_lattice_mesh("bastion_unbolted_face", bone_ids, "f2_def")}

            slots.append({"name": "f2_clamp_slot", "bone": "impact_shake", "attachment": "f2_clamp_mesh", "color": base_col})
            attachments["f2_clamp_slot"] = {
                "f2_clamp_mesh": build_sub_mesh("bastion_clamp_intact", part_layout, bone_ids, "f2_clamp_def")
            }

            _, _, slw_st, slh_st = split_stage_specs["bastion_clamp_split_l"]
            _, _, srw_st, srh_st = split_stage_specs["bastion_clamp_split_r"]
            slots.append(
                {"name": "f2_split_l_slot", "bone": "f2_clamp_split_l", "attachment": "bastion_clamp_split_l", "color": hidden_col}
            )
            attachments["f2_split_l_slot"] = {
                "bastion_clamp_split_l": {
                    "type": "region",
                    "path": "bastion_clamp_split_l",
                    "width": round(slw_st, 2),
                    "height": round(slh_st, 2),
                }
            }
            slots.append(
                {"name": "f2_split_r_slot", "bone": "f2_clamp_split_r", "attachment": "bastion_clamp_split_r", "color": hidden_col}
            )
            attachments["f2_split_r_slot"] = {
                "bastion_clamp_split_r": {
                    "type": "region",
                    "path": "bastion_clamp_split_r",
                    "width": round(srw_st, 2),
                    "height": round(srh_st, 2),
                }
            }

            slots.append(
                {
                    "name": "f2_glow_slot",
                    "bone": "impact_shake",
                    "attachment": "f2_glow_mesh",
                    "color": hidden_col,
                    "blend": "additive",
                }
            )
            attachments["f2_glow_slot"] = {
                "f2_glow_mesh": build_sub_mesh("bastion_keystone_glow", part_layout, bone_ids, "f2_clamp_def")
            }

        elif k == 3:
            # Draw order: grin throat -> sclera -> moving stone pupils -> stone face/eyelids -> amethyst scar glow
            slots.append({"name": "f3_throat", "bone": "impact_shake", "attachment": "f3_throat_mesh", "color": base_col})
            attachments["f3_throat"] = {"f3_throat_mesh": build_sub_mesh("mockery_throat_bg", part_layout, bone_ids, "f3_def")}

            slots.append({"name": "f3_sclera_slot", "bone": "impact_shake", "attachment": "f3_sclera_mesh", "color": base_col})
            attachments["f3_sclera_slot"] = {
                "f3_sclera_mesh": build_sub_mesh("mockery_eye_sclera", part_layout, bone_ids, "f3_def")
            }

            slots.append({"name": "f3_pupil_slot", "bone": "impact_shake", "attachment": "f3_pupil_mesh", "color": base_col})
            attachments["f3_pupil_slot"] = {
                "f3_pupil_mesh": build_sub_mesh("mockery_pupils", part_layout, bone_ids, "f3_pupil_def")
            }

            slots.append({"name": "f3_face", "bone": "impact_shake", "attachment": "f3_mesh", "color": base_col})
            attachments["f3_face"] = {"f3_mesh": build_cyl_lattice_mesh("mockery_face", bone_ids, "f3_def")}

            slots.append(
                {
                    "name": "f3_glow_slot",
                    "bone": "impact_shake",
                    "attachment": "f3_glow_mesh",
                    "color": hidden_col,
                    "blend": "additive",
                }
            )
            attachments["f3_glow_slot"] = {"f3_glow_mesh": build_sub_mesh("mockery_spark_glow", part_layout, bone_ids, "f3_def")}

    # 3. Sill Track Grinding Gravel (6 slots) + Billowing Sill Stone Dust Clouds (8 slots)
    for i in range(6):
        chip_idx = (i * 2) % 10
        rw, rh = parts[f"rubble_chip_{chip_idx}"].size
        sname = f"grit_slot_{i}"
        slots.append({"name": sname, "bone": f"grit_{i}", "attachment": f"grit_chip_{i}", "color": "ffffff00"})
        attachments[sname] = {
            f"grit_chip_{i}": {
                "type": "region",
                "path": f"rubble_chip_{chip_idx}",
                "width": round(rw * 0.42),
                "height": round(rh * 0.42),
            }
        }

    dw, dh = parts["dust_puff"].size
    for i in range(8):
        sname = f"dust_slot_{i}"
        slots.append({"name": sname, "bone": f"dust_{i}", "attachment": "dust_puff", "color": "ffffff00"})
        attachments[sname] = {"dust_puff": {"type": "region", "path": "dust_puff", "width": dw, "height": dh}}

    # 4. 10 Stone Rubble Fracture Chunks
    for i in range(10):
        rw, rh = parts[f"rubble_chip_{i}"].size
        sname = f"rubble_slot_{i}"
        slots.append({"name": sname, "bone": f"rubble_{i}", "attachment": f"rubble_chip_{i}", "color": "ffffff00"})
        attachments[sname] = {f"rubble_chip_{i}": {"type": "region", "path": f"rubble_chip_{i}", "width": rw, "height": rh}}

    # =========================================================================
    # AUTHOR ANIMATIONS
    # =========================================================================
    animations: dict[str, dict] = {}

    face_opaque_slots = {
        0: ["f0_throat", "f0_teeth_lo_slot", "f0_teeth_up_slot", "f0_face"],
        1: ["f1_throat", "f1_face"],
        2: ["f2_throat", "f2_face", "f2_clamp_slot"],
        3: ["f3_throat", "f3_sclera_slot", "f3_pupil_slot", "f3_face"],
    }

    def add_sill_impact_dust(
        bone_tracks: dict[str, dict],
        slot_tracks: dict[str, dict],
        t_hit: float,
        duration: float,
        *,
        intensity: float = 0.72,
    ) -> None:
        """Emit outward stone dust puffs along central floor sill (dust_2..5) when a massive stone slam hits."""
        for i in (2, 3, 4, 5):
            outward = -1.0 if i < 4 else 1.0
            span = max(0.28, min(0.58, duration - t_hit))
            t_end = min(duration, round(t_hit + span, 4))
            t_peak = round(t_hit + 0.06, 4)
            dx_end = outward * (42.0 + 14.0 * (i in (2, 5)))
            dy_end = 24.0 + 8.0 * (i in (3, 4))
            bone_tracks[f"dust_{i}"] = {
                "translate": sample_xy_curve(
                    [(0.0, 0.0), (t_hit, 0.0), (t_end, dx_end), (duration, 0.0)],
                    [(0.0, 0.0), (t_hit, 0.0), (t_end, dy_end), (duration, 0.0)],
                ),
                "scale": sample_xy_curve(
                    [(0.0, 0.65), (t_hit, 0.65), (t_end, 1.42), (duration, 1.0)],
                    [(0.0, 0.60), (t_hit, 0.60), (t_end, 1.24), (duration, 1.0)],
                ),
            }
            slot_tracks[f"dust_slot_{i}"] = {
                "rgba": [
                    {"time": 0.0, "color": "ffffff00"},
                    {"time": round(max(0.0, t_hit - 0.01), 4), "color": "ffffff00"},
                    {"time": t_peak, "color": rgba_hex(0.95, 0.96, 0.92, intensity)},
                    {"time": t_end, "color": "ffffff00"},
                    {"time": round(duration, 4), "color": "ffffff00"},
                ]
            }

    def build_turntable_clip(phi_start: float, phi_end: float, duration: float, *, fast_combo: bool = False) -> dict:
        """Build 10,000-ton stone cylinder rotation clip with 4-phase inertia, chamber shake & sill dust/gravel."""
        if abs(phi_end - phi_start) < 1e-6:
            ts = np.array([0.0])
        else:
            fps = 30 if fast_combo else 20
            n_frames = max(2, round(duration * fps) + 1)
            ts = np.linspace(0.0, duration, n_frames)
        delta_phi = phi_end - phi_start
        move_dir = -1.0 if delta_phi <= 0.0 else 1.0

        def heavy_motion_at(t: float) -> tuple[float, float, float, float, float]:
            if abs(delta_phi) < 1e-6:
                return phi_start, 0.0, 0.0, 0.0, 0.0
            u = t / duration
            if fast_combo:
                if u < 0.13:
                    w = u / 0.13
                    prog = -0.016 * math.sin(w * math.pi * 0.85)
                    ty = -5.5 * float(smoothstep(w))
                    tx = 4.0 * math.sin(w * math.pi * 3.0)
                    cx = 4.5 * math.sin(w * math.pi * 2.5) * (1.0 - 0.3 * w)
                    cy = -5.5 * math.sin(w * math.pi * 2.0) * (1.0 - 0.3 * w)
                elif u < 0.74:
                    w = (u - 0.13) / 0.61
                    prog = 1.028 * (0.32 * w + 0.68 * (w**1.42))
                    ty = -5.5 + 2.0 * math.sin(w * math.pi * 10.0)
                    tx = 2.8 * math.sin(w * math.pi * 14.0)
                    cx = 2.6 * math.sin(w * math.pi * 11.0)
                    cy = 2.2 * math.cos(w * math.pi * 13.0)
                else:
                    w = (u - 0.74) / 0.26
                    decay = (1.0 - w) ** 2.0
                    prog = 1.000 + 0.028 * math.cos(w * math.pi * 2.35) * ((1.0 - w) ** 1.8)
                    ty = -5.5 * (1.0 - float(smoothstep(min(1.0, w * 1.8)))) - 6.0 * math.sin(w * math.pi * 3.0) * decay
                    tx = -7.0 * move_dir * math.sin(w * math.pi * 3.5) * decay
                    cx = -13.0 * move_dir * math.cos(w * math.pi * 3.2) * decay
                    cy = -11.0 * math.cos(w * math.pi * 2.8) * decay
            else:
                if u < 0.14:
                    w = u / 0.14
                    prog = -0.018 * math.sin(w * math.pi * 0.85)
                    ty = -6.5 * float(smoothstep(w))
                    tx = 3.0 * math.sin(w * math.pi * 3.0) * (1.0 - 0.35 * w)
                    cx = 4.0 * math.sin(w * math.pi * 2.5) * ((1.0 - w) ** 1.2)
                    cy = -6.0 * math.sin(w * math.pi * 1.8) * ((1.0 - w) ** 1.1)
                elif u < 0.31:
                    w = (u - 0.14) / 0.17
                    stick_slip = 0.115 * (w + 0.21 * math.sin(w * 2.0 * math.pi))
                    prog = -0.018 * math.sin(0.85 * math.pi) * (1.0 - float(smoothstep(min(1.0, w / 0.35)))) + stick_slip
                    ty = -6.5 + 2.0 * abs(math.sin(w * math.pi * 5.0))
                    tx = 3.4 * math.sin(w * math.pi * 7.0)
                    cx = 2.8 * math.sin(w * math.pi * 6.0)
                    cy = -2.4 * math.cos(w * math.pi * 5.0)
                elif u < 0.78:
                    w = (u - 0.31) / 0.47
                    prog = 0.115 + (1.024 - 0.115) * (0.38 * w + 0.62 * (w**1.46))
                    ty = -5.8 + 1.8 * math.sin(w * math.pi * 16.0) + 0.9 * math.cos(w * math.pi * 27.0)
                    tx = 2.2 * math.sin(w * math.pi * 21.0)
                    cx = 2.0 * math.sin(w * math.pi * 15.0)
                    cy = 1.8 * math.cos(w * math.pi * 19.0)
                else:
                    w = (u - 0.78) / 0.22
                    decay = (1.0 - w) ** 2.05
                    prog = 1.000 + 0.024 * math.cos(w * math.pi * 2.35) * ((1.0 - w) ** 1.85)
                    ty = -5.8 * (1.0 - float(smoothstep(min(1.0, w * 1.7)))) + 5.5 * math.sin(w * math.pi * 3.0) * decay
                    tx = -6.5 * move_dir * math.cos(w * math.pi * 3.8) * decay
                    cx = -12.0 * move_dir * math.cos(w * math.pi * 3.4) * decay
                    cy = -10.5 * math.cos(w * math.pi * 3.0) * decay
            return phi_start + delta_phi * prog, tx, ty, cx, cy

        bone_tracks: dict[str, dict] = {}
        slot_tracks: dict[str, dict] = {}

        shake_keys = []
        cam_keys = []
        phis = []
        for t in ts:
            phi_t, shx, shy, cx, cy = heavy_motion_at(float(t))
            phis.append(phi_t)
            shake_keys.append({"time": round(float(t), 4), "x": round(shx, 3), "y": round(shy, 3)})
            cam_keys.append({"time": round(float(t), 4), "x": round(cx, 3), "y": round(cy, 3)})
        bone_tracks["turntable_shake"] = {"translate": shake_keys}
        bone_tracks["camera_shake"] = {"translate": cam_keys}

        # 1. Concave Cylindrical Wall Mesh Control Bones + Atmospheric Shadow Falloff
        last_idx = len(ts) - 1
        for s in range(4):
            for col in range(WALL_STRIPS + 1):
                theta_setup = wrap_pi(s * 0.5 * math.pi + (col / WALL_STRIPS - 0.5) * 0.5 * math.pi)
                col_vis = [
                    math.cos(wrap_pi(phis[idx_t] + s * 0.5 * math.pi + (col / WALL_STRIPS - 0.5) * 0.5 * math.pi)) > 0.34
                    for idx_t in range(len(ts))
                ]
                for row in range(WALL_ROWS):
                    bname = f"w_s{s}_c{col}_r{row}"
                    v_n = row / (WALL_ROWS - 1)
                    bx0, by0 = concave_wall_point(theta_setup, v_n, wall_tuck=True)
                    tr = []
                    for idx_t, t in enumerate(ts):
                        near_vis = (
                            idx_t in (0, last_idx)
                            or col_vis[idx_t]
                            or (idx_t > 0 and col_vis[idx_t - 1])
                            or (idx_t < last_idx and col_vis[idx_t + 1])
                        )
                        if not near_vis:
                            continue
                        theta = wrap_pi(phis[idx_t] + s * 0.5 * math.pi + (col / WALL_STRIPS - 0.5) * 0.5 * math.pi)
                        px, py = concave_wall_point(theta, v_n, wall_tuck=True)
                        tr.append({"time": round(float(t), 4), "x": round(px - bx0, 2), "y": round(py - by0, 2)})
                    bone_tracks[bname] = {"translate": tr}

            for strip in range(WALL_STRIPS):
                rgba_keys = []
                prev_hex = None
                for idx_t, t in enumerate(ts):
                    theta = wrap_pi(
                        phis[idx_t] + s * 0.5 * math.pi + ((strip + 0.5) / WALL_STRIPS - 0.5) * 0.5 * math.pi
                    )
                    shade, vis = wall_shade_and_vis(theta)
                    chex = rgba_hex(shade, shade, shade, vis)
                    if idx_t in (0, last_idx) or chex != prev_hex:
                        if (
                            prev_hex is not None
                            and chex != prev_hex
                            and len(rgba_keys) > 0
                            and rgba_keys[-1]["time"] != round(float(ts[idx_t - 1]), 4)
                        ):
                            rgba_keys.append({"time": round(float(ts[idx_t - 1]), 4), "color": prev_hex})
                        rgba_keys.append({"time": round(float(t), 4), "color": chex})
                    prev_hex = chex
                slot_tracks[f"wall_s{s}_{strip}"] = {"rgba": rgba_keys}

        # 2. 9x7 3D Cylindrical Relief Lattices for All 4 Faces (culling hidden rear-hemisphere frames cleanly!)
        for k in range(4):
            theta_setup = wrap_pi(k * 0.5 * math.pi)
            face_vis_flags = [math.cos(wrap_pi(phis[idx_t] + k * 0.5 * math.pi)) > 0.36 for idx_t in range(len(ts))]
            col_k = []
            prev_hex = None
            for idx_t, t in enumerate(ts):
                theta_f = wrap_pi(phis[idx_t] + k * 0.5 * math.pi)
                shade_f, vis_f = face_shade_and_vis(theta_f)
                chex = rgba_hex(shade_f, shade_f, shade_f, vis_f)
                if idx_t in (0, last_idx) or chex != prev_hex:
                    if (
                        prev_hex is not None
                        and chex != prev_hex
                        and len(col_k) > 0
                        and col_k[-1]["time"] != round(float(ts[idx_t - 1]), 4)
                    ):
                        col_k.append({"time": round(float(ts[idx_t - 1]), 4), "color": prev_hex})
                    col_k.append({"time": round(float(t), 4), "color": chex})
                prev_hex = chex
            for sname in face_opaque_slots[k]:
                slot_tracks[sname] = {"rgba": col_k}

            for iy in range(LAT_NY + 1):
                for ix in range(LAT_NX + 1):
                    lat_name = f"f{k}_lat_{ix}_{iy}"
                    lx0, ly0, lrot0, lsx0 = face_lattice_pose(theta_setup, ix, iy)
                    tr_l, rot_l, sc_l = [], [], []
                    for idx_t, t in enumerate(ts):
                        near_vis = (
                            idx_t in (0, last_idx)
                            or face_vis_flags[idx_t]
                            or (idx_t > 0 and face_vis_flags[idx_t - 1])
                            or (idx_t < last_idx and face_vis_flags[idx_t + 1])
                        )
                        if not near_vis:
                            continue
                        theta_f = wrap_pi(phis[idx_t] + k * 0.5 * math.pi)
                        lx, ly, lrot, lsx = face_lattice_pose(theta_f, ix, iy)
                        tr_l.append({"time": round(float(t), 4), "x": round(lx - lx0, 2), "y": round(ly - ly0, 2)})
                        rot_l.append({"time": round(float(t), 4), "value": round(lrot - lrot0, 2)})
                        sc_l.append({"time": round(float(t), 4), "x": round(lsx / max(1e-4, lsx0), 3), "y": 1.0})
                    bone_tracks[lat_name] = {"translate": tr_l, "rotate": rot_l, "scale": sc_l}

        # 3. Sill Track Rolling Stone Dust Plumes & Skittering Gravel
        if abs(delta_phi) > 1e-6:
            stop_u = 0.74 if fast_combo else 0.78
            dust_size_mul = [0.72, 1.08, 0.86, 1.28, 1.18, 0.90, 1.12, 0.76]
            dust_flip = [1.0, -1.0, 1.0, -1.0, 1.0, -1.0, -1.0, 1.0]
            for i in range(8):
                phase_off = (i * 0.29 + (0.11 if i % 2 else 0.0)) % 1.0
                sz_m = dust_size_mul[i]
                fl_x = dust_flip[i]
                d_tr, d_sc, d_rot, d_col = [], [], [], []
                for t in ts:
                    tf = float(t)
                    u = tf / duration
                    if u < 0.14:
                        w = u / 0.14
                        dx = move_dir * (-14.0 * w)
                        dy = (8.0 + (i % 3) * 4.0) * w
                        sc = (0.58 + 0.42 * w) * sz_m
                        rot_v = move_dir * 6.0 * w
                        alpha = (0.56 if i in (2, 3, 4, 5) else 0.24) * math.sin(w * math.pi)
                    elif u < stop_u:
                        w = (u - 0.14) / (stop_u - 0.14)
                        wave = (w * (1.85 + 0.12 * (i % 3)) + phase_off) % 1.0
                        dx = move_dir * ((75.0 + 18.0 * (i % 3)) * wave)
                        dy = (16.0 + 9.0 * (i % 2)) * (wave**0.72)
                        sc = (0.54 + 0.76 * wave) * sz_m
                        rot_v = move_dir * (14.0 * wave - 5.0)
                        env = float(smoothstep(min(1.0, w / 0.15)))
                        center_boost = 1.0 if i in (2, 3, 4, 5) else 0.68
                        alpha = 0.70 * center_boost * math.sin(wave * math.pi) * env
                    else:
                        w = (u - stop_u) / (1.0 - stop_u)
                        outward = -1.0 if i < 4 else 1.0
                        dx = outward * ((36.0 + 16.0 * (i % 3)) * (w**0.65)) + move_dir * 22.0 * (1.0 - w)
                        dy = (26.0 + 12.0 * (i in (2, 3, 4, 5))) * (w**0.58)
                        sc = (0.78 + 0.96 * (w**0.68)) * sz_m
                        rot_v = outward * 16.0 * w
                        alpha = 0.88 * math.sin(min(1.0, w * 1.05) * math.pi) * ((1.0 - w) ** 0.45)
                    d_tr.append({"time": round(tf, 4), "x": round(dx, 2), "y": round(dy, 2)})
                    d_sc.append({"time": round(tf, 4), "x": round(fl_x * sc, 3), "y": round(sc * 0.90, 3)})
                    d_rot.append({"time": round(tf, 4), "value": round(rot_v, 2)})
                    d_col.append({"time": round(tf, 4), "color": rgba_hex(0.95, 0.96, 0.92, max(0.0, alpha))})
                bone_tracks[f"dust_{i}"] = {"translate": d_tr, "scale": d_sc, "rotate": d_rot}
                slot_tracks[f"dust_slot_{i}"] = {"rgba": d_col}

            for i in range(6):
                g_off = (i * 0.23) % 1.0
                g_tr, g_rot, g_col = [], [], []
                for t in ts:
                    tf = float(t)
                    u = tf / duration
                    if 0.14 <= u <= stop_u + 0.14:
                        w = (u - 0.14) / stop_u
                        cyc = (w * 2.4 + g_off) % 1.0
                        gx = move_dir * (115.0 * cyc)
                        gy = 24.0 * abs(math.sin(cyc * math.pi * 2.0)) * (1.0 - 0.45 * cyc)
                        grot = move_dir * 320.0 * cyc
                        ga = 0.90 * math.sin(cyc * math.pi)
                    else:
                        gx, gy, grot, ga = 0.0, 0.0, 0.0, 0.0
                    g_tr.append({"time": round(tf, 4), "x": round(gx, 2), "y": round(gy, 2)})
                    g_rot.append({"time": round(tf, 4), "value": round(grot, 2)})
                    g_col.append({"time": round(tf, 4), "color": rgba_hex(0.92, 0.94, 0.90, max(0.0, ga))})
                bone_tracks[f"grit_{i}"] = {"translate": g_tr, "rotate": g_rot}
                slot_tracks[f"grit_slot_{i}"] = {"rgba": g_col}
        else:
            for i in range(8):
                slot_tracks[f"dust_slot_{i}"] = {"rgba": [{"time": 0.0, "color": "ffffff00"}]}
            for i in range(6):
                slot_tracks[f"grit_slot_{i}"] = {"rgba": [{"time": 0.0, "color": "ffffff00"}]}

        return {"bones": bone_tracks, "slots": slot_tracks}

    # Track 0: Turntable holds & 90°/180°/270° monumental + combo whip rotations
    for slot_i in range(4):
        phi_hold = -slot_i * 0.5 * math.pi
        animations[f"hold_slot_{slot_i}"] = build_turntable_clip(phi_hold, phi_hold, 0.20)
        for step in (1, 2, 3):
            next_slot = (slot_i + step) % 4
            phi_next = phi_hold - step * 0.5 * math.pi
            dur_norm = 2.20 + 0.34 * (step - 1)
            dur_fast = 0.78 + 0.14 * (step - 1)
            animations[f"turn_{slot_i}_to_{next_slot}"] = build_turntable_clip(
                phi_hold, phi_next, dur_norm, fast_combo=False
            )
            animations[f"combo_turn_{slot_i}_to_{next_slot}"] = build_turntable_clip(
                phi_hold, phi_next, dur_fast, fast_combo=True
            )

    # Track 1 (`BastionSealTrack = 1`): Persistent Bastion clamp & keystone glow attachment state!
    # Ensures hurt, stun, colossus drop & climax roar NEVER pop a broken clamp back on or flash glow over empty lips!
    animations["state_bastion_bolted"] = {
        "slots": {
            "f2_clamp_slot": {"attachment": [{"time": 0.0, "name": "f2_clamp_mesh"}]},
            "f2_glow_slot": {"attachment": [{"time": 0.0, "name": "f2_glow_mesh"}]},
        }
    }
    animations["state_bastion_unbolted"] = {
        "slots": {
            "f2_clamp_slot": {"attachment": [{"time": 0.0, "name": None}]},
            "f2_glow_slot": {"attachment": [{"time": 0.0, "name": None}]},
        }
    }

    # =========================================================================
    # TRACK-2 ARTICULATED FACIAL CLIPS (9x7 LATTICE + MULTI-LAYER TEETH/EYES)
    # =========================================================================

    # --- FACE 0: WRATH (嗔怒面) ---
    animations["wrath_idle"] = {
        "bones": bake_face_deform_bones(
            0,
            2.80,
            face_anchor_defs[0],
            core_ty=[(0, 0), (1.4, 4.5), (2.8, 0)],
            core_sx=[(0, 1.0), (1.4, 1.015), (2.8, 1.0)],
            core_sy=[(0, 1.0), (1.4, 1.012), (2.8, 1.0)],
            features={
                "brow_L": ([(0, 0), (1.4, 4.5), (2.8, 0)], [(0, 0), (1.4, -7.5), (2.8, 0)]),
                "brow_R": ([(0, 0), (1.4, -4.5), (2.8, 0)], [(0, 0), (1.4, -7.5), (2.8, 0)]),
                "upper_lip": ([(0, 0), (2.8, 0)], [(0, 0), (1.4, 3.0), (2.8, 0)]),
                "lower_lip": ([(0, 0), (0.9, -1.5), (1.9, 1.5), (2.8, 0)], [(0, 0), (1.4, -6.5), (2.8, 0)]),
            },
            tup_ty=[(0, 0), (1.4, 1.8), (2.8, 0)],
            tlo_tx=[(0, 0), (0.9, -1.5), (1.9, 1.5), (2.8, 0)],
            tlo_ty=[(0, 0), (1.4, -5.0), (2.8, 0)],
        ),
        "slots": {
            "f0_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "ff9a4414"},
                    {"time": 1.4, "color": "ffa44c52"},
                    {"time": 2.8, "color": "ff9a4414"},
                ]
            }
        },
    }

    # Wrath Heavy Mallet (`HeavyMalletMoveAsync` hit=0.44s, `ComboFollowUp` hit=0.38s, `ComboStarter` hit=0.40s)
    wrath_heavy_bones = bake_face_deform_bones(
        0,
        1.12,
        face_anchor_defs[0],
        core_ty=[(0, 0), (0.22, 15.0), (0.36, 7.0), (0.43, -13.0), (0.68, 4.0), (1.12, 0)],
        core_sx=[(0, 1.0), (0.22, 0.94), (0.38, 1.15), (0.46, 1.17), (0.74, 0.98), (1.12, 1.0)],
        core_sy=[(0, 1.0), (0.22, 0.94), (0.38, 1.12), (0.46, 1.10), (0.74, 1.02), (1.12, 1.0)],
        core_rot=[(0, 0), (0.20, 2.2), (0.42, -2.6), (0.54, 1.5), (1.12, 0)],
        features={
            "brow_L": ([(0, 0), (0.24, 9.0), (0.44, 14.0), (0.80, 3.0), (1.12, 0)], [(0, 0), (0.24, -13.0), (0.44, -19.0), (0.80, -4.0), (1.12, 0)]),
            "brow_R": ([(0, 0), (0.24, -9.0), (0.44, -14.0), (0.80, -3.0), (1.12, 0)], [(0, 0), (0.24, -13.0), (0.44, -19.0), (0.80, -4.0), (1.12, 0)]),
            "lid_L": ([(0, 0), (1.12, 0)], [(0, 0), (0.42, -4.5), (1.12, 0)]),
            "lid_R": ([(0, 0), (1.12, 0)], [(0, 0), (0.42, -4.5), (1.12, 0)]),
            "upper_lip": ([(0, 0), (1.12, 0)], [(0, 0), (0.14, -1.0), (0.28, 9.5), (0.37, 8.0), (0.43, 2.0), (0.58, 4.5), (0.78, 0), (1.12, 0)]),
            "lower_lip": (
                [(0, 0), (0.43, 0), (0.49, 3.0), (0.57, -2.0), (1.12, 0)],
                [(0, 0), (0.12, 1.5), (0.28, -26.0), (0.37, -22.0), (0.43, 2.0), (0.58, -6.5), (0.78, 0), (1.12, 0)],
            ),
            "corner_L": ([(0, 0), (0.28, -5.0), (0.43, 2.0), (1.12, 0)], [(0, 0), (0.28, -8.0), (0.43, 1.5), (1.12, 0)]),
            "corner_R": ([(0, 0), (0.28, 5.0), (0.43, -2.0), (1.12, 0)], [(0, 0), (0.28, -8.0), (0.43, 1.5), (1.12, 0)]),
        },
        tup_ty=[(0, 0), (0.14, 0.0), (0.28, 5.5), (0.37, 4.8), (0.43, 2.2), (0.58, 2.0), (0.78, 0), (1.12, 0)],
        tlo_tx=[(0, 0), (0.43, 0), (0.49, 2.8), (0.57, -2.0), (1.12, 0)],
        tlo_ty=[(0, 0), (0.12, 0.0), (0.28, -20.0), (0.37, -17.0), (0.43, 2.2), (0.58, -5.0), (0.78, 0), (1.12, 0)],
    )
    wrath_heavy_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.41, 0), (0.46, -9.5), (0.52, 8.0), (0.60, -4.0), (0.74, 0), (1.12, 0)],
            [(0, 0), (0.41, 0), (0.46, -11.5), (0.53, 6.0), (0.62, -3.0), (0.76, 0), (1.12, 0)],
        )
    }
    wrath_heavy_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.42, 0), (0.46, -8.5), (0.53, 7.0), (0.62, -3.5), (0.76, 0), (1.12, 0)],
            [(0, 0), (0.42, 0), (0.46, -9.5), (0.54, 5.5), (0.64, -2.5), (0.78, 0), (1.12, 0)],
        )
    }
    wrath_heavy_slots = {
        "f0_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "ff9a4414"},
                {"time": 0.22, "color": "ff9a44ee"},
                {"time": 0.46, "color": "ffbe62ff"},
                {"time": 0.94, "color": "ff9a4420"},
                {"time": 1.12, "color": "ff9a4414"},
            ]
        }
    }
    add_sill_impact_dust(wrath_heavy_bones, wrath_heavy_slots, 0.42, 1.12, intensity=0.68)
    animations["wrath_attack_heavy"] = {"bones": wrath_heavy_bones, "slots": wrath_heavy_slots}

    # Wrath Double Jaw (`DoubleJawMoveAsync` multi-hit initial delay=0.36s, 2 distinct diagonal chomps at 0.35s & 0.68s)
    wrath_double_bones = bake_face_deform_bones(
        0,
        1.16,
        face_anchor_defs[0],
        core_tx=[(0, 0), (0.16, -15.0), (0.35, 22.0), (0.50, 12.0), (0.68, -24.0), (1.16, 0)],
        core_ty=[(0, 0), (0.16, 12.0), (0.35, -10.0), (0.50, 14.0), (0.68, -13.0), (1.16, 0)],
        core_sx=[(0, 1.0), (0.16, 0.95), (0.35, 1.13), (0.50, 0.96), (0.68, 1.16), (1.16, 1.0)],
        core_sy=[(0, 1.0), (0.16, 0.95), (0.35, 1.11), (0.50, 0.96), (0.68, 1.14), (1.16, 1.0)],
        core_rot=[(0, 0), (0.16, 4.5), (0.35, -5.0), (0.50, -4.0), (0.68, 5.5), (1.16, 0)],
        features={
            "brow_L": ([(0, 0), (0.35, 12.0), (0.68, 14.0), (1.16, 0)], [(0, 0), (0.35, -15.0), (0.68, -18.0), (1.16, 0)]),
            "brow_R": ([(0, 0), (0.35, -12.0), (0.68, -14.0), (1.16, 0)], [(0, 0), (0.35, -15.0), (0.68, -18.0), (1.16, 0)]),
            "upper_lip": ([(0, 0), (1.16, 0)], [(0, 0), (0.21, 8.5), (0.35, 2.0), (0.53, 9.5), (0.68, 2.5), (0.88, 0), (1.16, 0)]),
            "lower_lip": ([(0, 0), (1.16, 0)], [(0, 0), (0.21, -23.0), (0.35, 2.0), (0.53, -26.0), (0.68, 2.5), (0.88, 0), (1.16, 0)]),
            "corner_L": ([(0, 0), (0.21, -4.0), (0.35, 2.0), (0.53, -5.0), (0.68, 2.0), (1.16, 0)], [(0, 0), (0.21, -7.0), (0.35, 1.0), (0.53, -8.0), (0.68, 1.0), (1.16, 0)]),
            "corner_R": ([(0, 0), (0.21, 4.0), (0.35, -2.0), (0.53, 5.0), (0.68, -2.0), (1.16, 0)], [(0, 0), (0.21, -7.0), (0.35, 1.0), (0.53, -8.0), (0.68, 1.0), (1.16, 0)]),
        },
        tup_ty=[(0, 0), (0.21, 5.0), (0.35, 2.0), (0.53, 5.8), (0.68, 2.5), (0.88, 0), (1.16, 0)],
        tlo_ty=[(0, 0), (0.21, -18.0), (0.35, 2.0), (0.53, -20.5), (0.68, 2.5), (0.88, 0), (1.16, 0)],
    )
    wrath_double_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.33, 0), (0.37, 7.5), (0.45, 0), (0.66, 0), (0.71, -9.5), (0.84, 0), (1.16, 0)],
            [(0, 0), (0.33, 0), (0.37, -7.5), (0.45, 0), (0.66, 0), (0.71, -9.5), (0.84, 0), (1.16, 0)],
        )
    }
    wrath_double_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.34, 0), (0.38, 6.5), (0.46, 0), (0.67, 0), (0.72, -8.0), (0.86, 0), (1.16, 0)],
            [(0, 0), (0.34, 0), (0.38, -6.5), (0.46, 0), (0.67, 0), (0.72, -8.5), (0.86, 0), (1.16, 0)],
        )
    }
    wrath_double_slots = {
        "f0_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "ff9a4414"},
                {"time": 0.35, "color": "ffa44cee"},
                {"time": 0.50, "color": "ff9a4455"},
                {"time": 0.69, "color": "ffbe62ff"},
                {"time": 1.16, "color": "ff9a4414"},
            ]
        }
    }
    add_sill_impact_dust(wrath_double_bones, wrath_double_slots, 0.68, 1.16, intensity=0.62)
    animations["wrath_attack_double"] = {"bones": wrath_double_bones, "slots": wrath_double_slots}

    # --- FACE 1: LAMENT (悲叹面) ---
    animations["lament_idle"] = {
        "bones": bake_face_deform_bones(
            1,
            3.80,
            face_anchor_defs[1],
            core_ty=[(0, 0), (1.5, 5.5), (2.5, -4.5), (3.8, 0)],
            core_rot=[(0, 0), (1.9, -1.8), (3.8, 0)],
            features={
                "brow_L": ([(0, 0), (1.9, -3.5), (3.8, 0)], [(0, 0), (1.9, 8.0), (3.8, 0)]),
                "brow_R": ([(0, 0), (1.9, 3.5), (3.8, 0)], [(0, 0), (1.9, 8.0), (3.8, 0)]),
                # Slow mournful heavy stone eye-squeeze around t=2.1..2.6s!
                "lid_L": ([(0, 0), (3.8, 0)], [(0, 0), (1.7, -1.5), (2.15, -8.5), (2.65, -2.0), (3.8, 0)]),
                "lid_R": ([(0, 0), (3.8, 0)], [(0, 0), (1.7, -1.5), (2.15, -8.5), (2.65, -2.0), (3.8, 0)]),
                "upper_lip": ([(0, 0), (3.8, 0)], [(0, 0), (1.5, 2.0), (2.5, 1.0), (3.8, 0)]),
                "lower_lip": ([(0, 0), (3.8, 0)], [(0, 0), (1.5, 2.5), (2.5, -11.0), (3.8, 0)]),
            },
        ),
        "slots": {
            "f1_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "77eedd18"},
                    {"time": 2.3, "color": "77eedd58"},
                    {"time": 3.8, "color": "77eedd18"},
                ]
            }
        },
    }

    # Lament Weathered Wail (`WeatheredWailMoveAsync` hit=0.46s, `ComboFollowUp` hit=0.42s)
    wail_bones = bake_face_deform_bones(
        1,
        1.42,
        face_anchor_defs[1],
        core_tx=[(0, 0), (0.28, 0), (0.48, -6.5), (0.62, 6.5), (0.76, -6.0), (0.90, 5.5), (1.04, -4.0), (1.42, 0)],
        core_ty=[(0, 0), (0.28, 15.0), (0.46, -6.0), (1.02, -3.0), (1.42, 0)],
        core_sx=[(0, 1.0), (0.28, 0.96), (0.46, 1.10), (1.02, 1.06), (1.42, 1.0)],
        core_sy=[(0, 1.0), (0.28, 1.02), (0.46, 1.11), (1.02, 1.07), (1.42, 1.0)],
        core_rot=[(0, 0), (0.28, 2.8), (0.52, -3.0), (0.78, 2.6), (1.02, -2.0), (1.42, 0)],
        features={
            "lid_L": ([(0, 0), (1.42, 0)], [(0, 0), (0.24, -9.5), (0.46, -3.0), (1.02, -5.0), (1.42, 0)]),
            "lid_R": ([(0, 0), (1.42, 0)], [(0, 0), (0.24, -9.5), (0.46, -3.0), (1.02, -5.0), (1.42, 0)]),
            "upper_lip": ([(0, 0), (1.42, 0)], [(0, 0), (0.24, -2.0), (0.46, 8.0), (1.02, 6.5), (1.42, 0)]),
            "lower_lip": (
                [(0, 0), (1.42, 0)],
                [(0, 0), (0.24, 4.0), (0.46, -24.0), (0.60, -19.0), (0.74, -24.0), (0.88, -18.5), (1.02, -22.0), (1.42, 0)],
            ),
            "brow_L": ([(0, 0), (0.48, -5.5), (1.42, 0)], [(0, 0), (0.48, 15.0), (1.02, 12.0), (1.42, 0)]),
            "brow_R": ([(0, 0), (0.48, 5.5), (1.42, 0)], [(0, 0), (0.48, 15.0), (1.02, 12.0), (1.42, 0)]),
        },
    )
    wail_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.42, 0), (0.48, -5.0), (0.58, 4.5), (0.72, -4.0), (0.86, 3.5), (1.05, 0), (1.42, 0)],
            [(0, 0), (0.42, 0), (0.48, -5.5), (0.60, 4.0), (0.76, -3.5), (1.05, 0), (1.42, 0)],
        )
    }
    animations["lament_cast_wail"] = {
        "bones": wail_bones,
        "slots": {
            "f1_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "77eedd18"},
                    {"time": 0.44, "color": "77eeddee"},
                    {"time": 0.74, "color": "99ffeeff"},
                    {"time": 1.05, "color": "77eedddd"},
                    {"time": 1.42, "color": "77eedd18"},
                ]
            }
        },
    }

    # Lament Blood Rain (`BloodRainMoveAsync` 3 hits starting at 0.36s -> 3 escalating asymmetric sob-slams!)
    lament_rain_bones = bake_face_deform_bones(
        1,
        1.30,
        face_anchor_defs[1],
        core_tx=[(0, 0), (0.18, 5.0), (0.35, -13.0), (0.50, -4.0), (0.63, 14.0), (0.78, 3.0), (0.92, 0), (1.30, 0)],
        core_ty=[(0, 0), (0.18, 10.0), (0.35, -10.0), (0.49, 9.0), (0.63, -12.0), (0.77, 12.0), (0.92, -16.0), (1.30, 0)],
        core_sx=[(0, 1.0), (0.35, 1.09), (0.49, 0.97), (0.63, 1.11), (0.77, 0.96), (0.92, 1.15), (1.30, 1.0)],
        core_sy=[(0, 1.0), (0.35, 1.09), (0.49, 0.97), (0.63, 1.11), (0.77, 0.96), (0.92, 1.14), (1.30, 1.0)],
        core_rot=[(0, 0), (0.35, 3.8), (0.63, -4.0), (0.92, 0.0), (1.30, 0)],
        features={
            "lid_L": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, -8.0), (0.49, -2.0), (0.63, -5.0), (0.92, -9.0), (1.30, 0)]),
            "lid_R": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, -5.0), (0.49, -2.0), (0.63, -8.5), (0.92, -9.0), (1.30, 0)]),
            "upper_lip": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, 5.0), (0.49, 1.0), (0.63, 6.0), (0.77, 1.0), (0.92, 7.5), (1.30, 0)]),
            "lower_lip": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, -16.0), (0.49, -4.0), (0.63, -19.0), (0.77, -4.0), (0.92, -23.0), (1.30, 0)]),
            "brow_L": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, 11.0), (0.63, 9.0), (0.92, 14.0), (1.30, 0)]),
            "brow_R": ([(0, 0), (1.30, 0)], [(0, 0), (0.35, 9.0), (0.63, 11.0), (0.92, 14.0), (1.30, 0)]),
        },
    )
    lament_rain_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.33, 0), (0.37, -5.5), (0.44, 0), (0.61, 0), (0.65, 6.0), (0.72, 0), (0.90, 0), (0.95, -7.5), (1.06, 0), (1.30, 0)],
            [(0, 0), (0.33, 0), (0.37, -6.0), (0.44, 0), (0.61, 0), (0.65, -6.5), (0.72, 0), (0.90, 0), (0.95, -9.0), (1.06, 0), (1.30, 0)],
        )
    }
    lament_rain_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.34, 0), (0.38, -4.5), (0.45, 0), (0.62, 0), (0.66, 5.0), (0.73, 0), (0.91, 0), (0.96, -6.5), (1.08, 0), (1.30, 0)],
            [(0, 0), (0.34, 0), (0.38, -5.0), (0.45, 0), (0.62, 0), (0.66, -5.5), (0.73, 0), (0.91, 0), (0.96, -7.5), (1.08, 0), (1.30, 0)],
        )
    }
    lament_rain_slots = {
        "f1_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "77eedd18"},
                {"time": 0.35, "color": "77eeddd0"},
                {"time": 0.63, "color": "88ffeedd"},
                {"time": 0.93, "color": "aaffeeff"},
                {"time": 1.30, "color": "77eedd18"},
            ]
        }
    }
    add_sill_impact_dust(lament_rain_bones, lament_rain_slots, 0.91, 1.30, intensity=0.58)
    animations["lament_attack_rain"] = {"bones": lament_rain_bones, "slots": lament_rain_slots}

    # --- FACE 2: BASTION (缄默面) ---
    animations["bastion_idle_bolted"] = {
        "bones": bake_face_deform_bones(
            2,
            3.20,
            face_anchor_defs[2],
            core_ty=[(0, 0), (1.6, 3.5), (3.2, 0)],
            core_sx=[(0, 1.0), (1.6, 1.012), (3.2, 1.0)],
            core_sy=[(0, 1.0), (1.6, 1.010), (3.2, 1.0)],
            features={
                "brow_L": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -4.5), (3.2, 0)]),
                "brow_R": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -4.5), (3.2, 0)]),
            },
            clamp_scale=[(0, 1.0), (1.6, 1.012), (3.2, 1.0)],
        ),
        "slots": {
            "f2_split_l_slot": {"rgba": [{"time": 0.0, "color": "ffffff00"}, {"time": 3.2, "color": "ffffff00"}]},
            "f2_split_r_slot": {"rgba": [{"time": 0.0, "color": "ffffff00"}, {"time": 3.2, "color": "ffffff00"}]},
            "f2_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "ffe06612"},
                    {"time": 1.6, "color": "ffe06648"},
                    {"time": 3.2, "color": "ffe06612"},
                ]
            },
        },
    }

    # Bastion Unbolt (`bastion_unbolt`: Track 1 flips to state_bastion_unbolted; Track 2 holds intact clamp for 0.00..0.12s,
    # flashes facet-locked keystone glow strictly BEFORE fracture, then blasts exact-sized split halves outward!)
    unbolt_bones = bake_face_deform_bones(
        2,
        0.90,
        face_anchor_defs[2],
        core_tx=[(0, 0), (0.06, -10.0), (0.12, 12.0), (0.22, -7.0), (0.38, 3.5), (0.90, 0)],
        core_ty=[(0, 0), (0.12, 6.0), (0.28, -7.0), (0.90, 0)],
        core_sx=[(0, 1.0), (0.12, 0.95), (0.26, 1.06), (0.90, 1.0)],
        core_sy=[(0, 1.0), (0.12, 0.95), (0.26, 1.05), (0.90, 1.0)],
        features={
            "brow_L": ([(0, 0), (0.90, 0)], [(0, 0), (0.20, 11.0), (0.90, 6.0)]),
            "brow_R": ([(0, 0), (0.90, 0)], [(0, 0), (0.20, 11.0), (0.90, 6.0)]),
            "upper_lip": ([(0, 0), (0.90, 0)], [(0, 0), (0.12, 0.0), (0.26, 4.5), (0.90, 1.5)]),
            "lower_lip": ([(0, 0), (0.90, 0)], [(0, 0), (0.12, 0.0), (0.26, -11.0), (0.90, -4.5)]),
        },
        clamp_scale=[(0, 1.0), (0.10, 1.08), (0.12, 1.0), (0.90, 1.0)],
    )
    unbolt_bones["f2_clamp_split_l"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.12, 0), (0.44, -96.0), (0.90, -168.0)],
            [(0, 0), (0.12, 0), (0.28, 18.0), (0.90, -210.0)],
        ),
        "rotate": sample_curve([(0, 0), (0.12, 0), (0.90, 74.0)]),
    }
    unbolt_bones["f2_clamp_split_r"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.12, 0), (0.44, 96.0), (0.90, 168.0)],
            [(0, 0), (0.12, 0), (0.28, 18.0), (0.90, -210.0)],
        ),
        "rotate": sample_curve([(0, 0), (0.12, 0), (0.90, -74.0)]),
    }
    unbolt_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.11, 0), (0.15, -7.5), (0.22, 6.0), (0.34, 0), (0.90, 0)],
            [(0, 0), (0.11, 0), (0.15, -6.5), (0.22, 4.5), (0.34, 0), (0.90, 0)],
        )
    }
    unbolt_slots = {
        "f2_clamp_slot": {
            "attachment": [
                {"time": 0.0, "name": "f2_clamp_mesh"},
                {"time": 0.12, "name": None},
            ]
        },
        "f2_glow_slot": {
            "attachment": [
                {"time": 0.0, "name": "f2_glow_mesh"},
                {"time": 0.16, "name": None},
            ],
            "rgba": [
                {"time": 0.0, "color": "ffe06640"},
                {"time": 0.11, "color": "fff088ff"},
                {"time": 0.16, "color": "ffffff00"},
                {"time": 0.90, "color": "ffffff00"},
            ],
        },
        "f2_split_l_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffffff00"},
                {"time": 0.11, "color": "ffffff00"},
                {"time": 0.12, "color": "ffffffff"},
                {"time": 0.66, "color": "ffffffcc"},
                {"time": 0.90, "color": "ffffff00"},
            ]
        },
        "f2_split_r_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffffff00"},
                {"time": 0.11, "color": "ffffff00"},
                {"time": 0.12, "color": "ffffffff"},
                {"time": 0.66, "color": "ffffffcc"},
                {"time": 0.90, "color": "ffffff00"},
            ]
        },
    }
    add_sill_impact_dust(unbolt_bones, unbolt_slots, 0.12, 0.90, intensity=0.52)
    animations["bastion_unbolt"] = {"bones": unbolt_bones, "slots": unbolt_slots}

    animations["bastion_idle_unbolted"] = {
        "bones": bake_face_deform_bones(
            2,
            2.60,
            face_anchor_defs[2],
            core_ty=[(0, 0), (1.3, -3.5), (2.6, 0)],
            features={
                "brow_L": ([(0, 0), (2.6, 0)], [(0, 6.0), (1.3, 9.5), (2.6, 6.0)]),
                "brow_R": ([(0, 0), (2.6, 0)], [(0, 6.0), (1.3, 9.5), (2.6, 6.0)]),
                "upper_lip": ([(0, 0), (2.6, 0)], [(0, 1.5), (1.3, 3.0), (2.6, 1.5)]),
                "lower_lip": ([(0, 0), (2.6, 0)], [(0, -4.5), (1.3, -9.5), (2.6, -4.5)]),
            },
        ),
        "slots": {
            "f2_split_l_slot": {"rgba": [{"time": 0.0, "color": "ffffff00"}, {"time": 2.6, "color": "ffffff00"}]},
            "f2_split_r_slot": {"rgba": [{"time": 0.0, "color": "ffffff00"}, {"time": 2.6, "color": "ffffff00"}]},
        },
    }

    # Bastion Fortify (when ALREADY bolted — clenches clamp & executes Bulwark Press slam at 0.42s)
    fortify_bones = bake_face_deform_bones(
        2,
        0.96,
        face_anchor_defs[2],
        core_ty=[(0, 0), (0.24, 13.0), (0.42, -14.0), (0.96, 0)],
        core_sx=[(0, 1.0), (0.24, 0.95), (0.42, 1.13), (0.96, 1.0)],
        core_sy=[(0, 1.0), (0.24, 0.95), (0.42, 1.11), (0.96, 1.0)],
        features={
            "brow_L": ([(0, 0), (0.96, 0)], [(0, 0), (0.42, -14.0), (0.96, 0)]),
            "brow_R": ([(0, 0), (0.96, 0)], [(0, 0), (0.42, -14.0), (0.96, 0)]),
        },
        clamp_scale=[(0, 1.0), (0.22, 0.95), (0.42, 1.05), (0.96, 1.0)],
    )
    fortify_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.40, 0), (0.45, -7.0), (0.53, 5.5), (0.66, 0), (0.96, 0)],
            [(0, 0), (0.40, 0), (0.45, -9.0), (0.54, 5.0), (0.68, 0), (0.96, 0)],
        )
    }
    fortify_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.40, 0), (0.45, -6.5), (0.54, 5.0), (0.68, 0), (0.96, 0)],
            [(0, 0), (0.40, 0), (0.45, -8.0), (0.55, 4.5), (0.70, 0), (0.96, 0)],
        )
    }
    fortify_slots = {
        "f2_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffe06612"},
                {"time": 0.24, "color": "ffe066cc"},
                {"time": 0.44, "color": "fff088ff"},
                {"time": 0.86, "color": "ffe06620"},
                {"time": 0.96, "color": "ffe06612"},
            ]
        }
    }
    add_sill_impact_dust(fortify_bones, fortify_slots, 0.42, 0.96, intensity=0.58)
    animations["bastion_fortify"] = {"bones": fortify_bones, "slots": fortify_slots}

    # Bastion Re-Bolt + Bulwark Press (`bastion_rebolt_attack`: when UNBOLTED -> slams 2 clamp halves inward at 0.20s -> hits at 0.42s!)
    rebolt_bones = bake_face_deform_bones(
        2,
        0.98,
        face_anchor_defs[2],
        core_ty=[(0, 0), (0.18, 4.0), (0.26, 12.0), (0.42, -14.0), (0.98, 0)],
        core_sx=[(0, 1.0), (0.20, 0.96), (0.42, 1.14), (0.98, 1.0)],
        core_sy=[(0, 1.0), (0.20, 0.96), (0.42, 1.12), (0.98, 1.0)],
        features={
            "brow_L": ([(0, 0), (0.98, 0)], [(0, 6.0), (0.20, -6.0), (0.42, -15.0), (0.98, 0)]),
            "brow_R": ([(0, 0), (0.98, 0)], [(0, 6.0), (0.20, -6.0), (0.42, -15.0), (0.98, 0)]),
            "lower_lip": ([(0, 0), (0.98, 0)], [(0, -4.5), (0.18, 0.0), (0.98, 0)]),
        },
        clamp_scale=[(0, 1.0), (0.20, 1.04), (0.30, 0.97), (0.42, 1.05), (0.98, 1.0)],
    )
    rebolt_bones["f2_clamp_split_l"] = {
        "translate": sample_xy_curve(
            [(0, -138.0), (0.20, 0.0), (0.98, 0.0)],
            [(0, 10.0), (0.20, 0.0), (0.98, 0.0)],
        ),
        "rotate": sample_curve([(0, 22.0), (0.20, 0.0), (0.98, 0.0)]),
    }
    rebolt_bones["f2_clamp_split_r"] = {
        "translate": sample_xy_curve(
            [(0, 138.0), (0.20, 0.0), (0.98, 0.0)],
            [(0, 10.0), (0.20, 0.0), (0.98, 0.0)],
        ),
        "rotate": sample_curve([(0, -22.0), (0.20, 0.0), (0.98, 0.0)]),
    }
    rebolt_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.19, 0), (0.22, 5.5), (0.30, 0), (0.40, 0), (0.45, -8.0), (0.54, 6.0), (0.68, 0), (0.98, 0)],
            [(0, 0), (0.19, 0), (0.22, -4.5), (0.30, 0), (0.40, 0), (0.45, -9.5), (0.55, 5.0), (0.70, 0), (0.98, 0)],
        )
    }
    rebolt_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.19, 0), (0.23, 4.5), (0.30, 0), (0.41, 0), (0.46, -7.0), (0.55, 5.0), (0.70, 0), (0.98, 0)],
            [(0, 0), (0.19, 0), (0.23, -4.0), (0.30, 0), (0.41, 0), (0.46, -8.5), (0.56, 4.5), (0.72, 0), (0.98, 0)],
        )
    }
    rebolt_slots = {
        "f2_clamp_slot": {
            "attachment": [
                {"time": 0.0, "name": None},
                {"time": 0.20, "name": "f2_clamp_mesh"},
            ]
        },
        "f2_glow_slot": {
            "attachment": [
                {"time": 0.0, "name": None},
                {"time": 0.20, "name": "f2_glow_mesh"},
            ],
            "rgba": [
                {"time": 0.0, "color": "ffffff00"},
                {"time": 0.19, "color": "ffffff00"},
                {"time": 0.21, "color": "fff299ff"},
                {"time": 0.44, "color": "ffe477ff"},
                {"time": 0.90, "color": "ffe06618"},
                {"time": 0.98, "color": "ffe06612"},
            ],
        },
        "f2_split_l_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffffff00"},
                {"time": 0.04, "color": "ffffffff"},
                {"time": 0.19, "color": "ffffffff"},
                {"time": 0.20, "color": "ffffff00"},
                {"time": 0.98, "color": "ffffff00"},
            ]
        },
        "f2_split_r_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffffff00"},
                {"time": 0.04, "color": "ffffffff"},
                {"time": 0.19, "color": "ffffffff"},
                {"time": 0.20, "color": "ffffff00"},
                {"time": 0.98, "color": "ffffff00"},
            ]
        },
    }
    add_sill_impact_dust(rebolt_bones, rebolt_slots, 0.42, 0.98, intensity=0.62)
    animations["bastion_rebolt_attack"] = {"bones": rebolt_bones, "slots": rebolt_slots}

    # Bastion Colossus Drop (`ColossusDropMoveAsync` hit=0.48s; respects Track 1 bolted/unbolted automatically!)
    colossus_bones = bake_face_deform_bones(
        2,
        1.24,
        face_anchor_defs[2],
        core_ty=[(0, 0), (0.34, 23.0), (0.48, -17.0), (0.64, -11.0), (0.92, 4.5), (1.24, 0)],
        core_sx=[(0, 1.0), (0.34, 0.92), (0.48, 1.18), (0.64, 1.14), (1.24, 1.0)],
        core_sy=[(0, 1.0), (0.34, 0.93), (0.48, 1.15), (0.64, 1.11), (1.24, 1.0)],
        features={
            "brow_L": ([(0, 0), (1.24, 0)], [(0, 0), (0.48, -16.0), (1.24, 0)]),
            "brow_R": ([(0, 0), (1.24, 0)], [(0, 0), (0.48, -16.0), (1.24, 0)]),
        },
        clamp_scale=[(0, 1.0), (0.34, 0.96), (0.48, 1.06), (1.24, 1.0)],
    )
    colossus_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.46, 0), (0.51, -11.5), (0.58, 10.0), (0.66, -6.0), (0.82, 0), (1.24, 0)],
            [(0, 0), (0.46, 0), (0.51, -14.5), (0.59, 8.0), (0.68, -4.0), (0.84, 0), (1.24, 0)],
        )
    }
    colossus_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.46, 0), (0.51, -10.5), (0.59, 9.0), (0.68, -5.0), (0.84, 0), (1.24, 0)],
            [(0, 0), (0.46, 0), (0.51, -12.5), (0.60, 7.5), (0.70, -3.5), (0.86, 0), (1.24, 0)],
        )
    }
    colossus_slots = {
        "f2_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "ffe06612"},
                {"time": 0.32, "color": "ffd855ee"},
                {"time": 0.52, "color": "fff088ff"},
                {"time": 1.08, "color": "ffe06618"},
                {"time": 1.24, "color": "ffe06612"},
            ]
        }
    }
    add_sill_impact_dust(colossus_bones, colossus_slots, 0.48, 1.24, intensity=0.80)
    animations["bastion_attack_colossus"] = {"bones": colossus_bones, "slots": colossus_slots}

    # --- FACE 3: MOCKERY (诡笑面) ---
    animations["mockery_idle"] = {
        "bones": bake_face_deform_bones(
            3,
            3.20,
            face_anchor_defs[3],
            core_tx=[(0, 0), (1.6, 3.5), (3.2, 0)],
            core_ty=[(0, 0), (1.6, 4.0), (3.2, 0)],
            core_rot=[(0, 0), (1.6, -2.4), (3.2, 0)],
            features={
                "brow_L": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -5.5), (3.2, 0)]),
                "brow_R": ([(0, 0), (3.2, 0)], [(0, 0), (1.2, 9.5), (2.2, 8.0), (3.2, 0)]),
                "lid_L": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -2.5), (3.2, 0)]),
                "lid_R": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -2.0), (3.2, 0)]),
                "smirk_R": ([(0, 0), (1.6, 6.5), (3.2, 0)], [(0, 0), (1.6, 9.0), (3.2, 0)]),
                "lower_lip": ([(0, 0), (3.2, 0)], [(0, 0), (1.6, -2.5), (3.2, 0)]),
            },
            # Sly side-glance -> darts left to lock eyes with the player -> holds smirk -> slides back!
            pupil_tx=[(0, 0.0), (0.75, 0.0), (1.05, -14.5), (2.15, -13.5), (2.55, 0.0), (3.20, 0.0)],
            pupil_ty=[(0, 0.0), (0.75, 0.0), (1.05, -1.5), (2.15, -1.0), (2.55, 0.0), (3.20, 0.0)],
        ),
        "slots": {
            "f3_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "c977ff10"},
                    {"time": 1.5, "color": "d888ff45"},
                    {"time": 3.2, "color": "c977ff10"},
                ]
            }
        },
    }

    # Mockery Sneer Stab (`SneerStabMoveAsync` 2 hits starting at 0.35s)
    twin_bones = bake_face_deform_bones(
        3,
        1.06,
        face_anchor_defs[3],
        core_tx=[(0, 0), (0.16, 16.0), (0.34, -25.0), (0.48, -11.0), (0.63, 27.0), (1.06, 0)],
        core_ty=[(0, 0), (0.16, 10.0), (0.34, -11.0), (0.48, 8.0), (0.63, -13.0), (1.06, 0)],
        core_sx=[(0, 1.0), (0.16, 0.96), (0.34, 1.12), (0.48, 0.98), (0.63, 1.15), (1.06, 1.0)],
        core_sy=[(0, 1.0), (0.16, 0.96), (0.34, 1.11), (0.48, 0.98), (0.63, 1.13), (1.06, 1.0)],
        core_rot=[(0, 0), (0.16, -5.5), (0.34, 6.8), (0.48, 3.8), (0.63, -7.8), (1.06, 0)],
        features={
            "lid_L": ([(0, 0), (1.06, 0)], [(0, 0), (0.34, -4.0), (0.63, -4.5), (1.06, 0)]),
            "lid_R": ([(0, 0), (1.06, 0)], [(0, 0), (0.34, -4.0), (0.63, -4.5), (1.06, 0)]),
            "smirk_R": ([(0, 0), (0.34, 11.0), (0.63, 15.0), (1.06, 0)], [(0, 0), (0.34, 13.0), (0.63, 17.0), (1.06, 0)]),
            "lower_lip": ([(0, 0), (1.06, 0)], [(0, 0), (0.34, -8.0), (0.48, -3.0), (0.63, -10.5), (1.06, 0)]),
            "brow_R": ([(0, 0), (1.06, 0)], [(0, 0), (0.34, 13.0), (0.63, 16.0), (1.06, 0)]),
        },
        pupil_tx=[(0, 0.0), (0.16, -4.0), (0.34, -17.0), (0.63, -9.0), (0.88, 0.0), (1.06, 0.0)],
        pupil_ty=[(0, 0.0), (0.34, -2.5), (0.63, -2.5), (1.06, 0.0)],
    )
    twin_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.32, 0), (0.36, -7.5), (0.44, 0), (0.61, 0), (0.66, 8.5), (0.78, 0), (1.06, 0)],
            [(0, 0), (0.32, 0), (0.36, -6.5), (0.44, 0), (0.61, 0), (0.66, -7.5), (0.78, 0), (1.06, 0)],
        )
    }
    twin_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.33, 0), (0.37, -6.0), (0.45, 0), (0.62, 0), (0.67, 7.0), (0.80, 0), (1.06, 0)],
            [(0, 0), (0.33, 0), (0.37, -5.0), (0.45, 0), (0.62, 0), (0.67, -6.0), (0.80, 0), (1.06, 0)],
        )
    }
    animations["mockery_attack_twin"] = {
        "bones": twin_bones,
        "slots": {
            "f3_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "c977ff10"},
                    {"time": 0.34, "color": "d484ffee"},
                    {"time": 0.48, "color": "c977ff66"},
                    {"time": 0.64, "color": "e699ffff"},
                    {"time": 1.06, "color": "c977ff10"},
                ]
            }
        },
    }

    # Mockery Whirling Fang (`WhirlingFangMoveAsync` single heavy strike at 0.42s — distinct coil & center fang lunge!)
    fang_bones = bake_face_deform_bones(
        3,
        1.10,
        face_anchor_defs[3],
        core_tx=[(0, 0), (0.26, 18.0), (0.42, -8.0), (0.62, 3.0), (1.10, 0)],
        core_ty=[(0, 0), (0.26, 15.0), (0.42, -15.0), (0.66, 4.0), (1.10, 0)],
        core_sx=[(0, 1.0), (0.26, 0.93), (0.42, 1.17), (0.62, 1.03), (1.10, 1.0)],
        core_sy=[(0, 1.0), (0.26, 0.94), (0.42, 1.14), (0.62, 1.02), (1.10, 1.0)],
        core_rot=[(0, 0), (0.26, -8.5), (0.42, 5.5), (0.64, -1.5), (1.10, 0)],
        features={
            "lid_L": ([(0, 0), (1.10, 0)], [(0, 0), (0.26, -5.5), (0.42, -2.0), (1.10, 0)]),
            "lid_R": ([(0, 0), (1.10, 0)], [(0, 0), (0.26, -5.5), (0.42, -2.0), (1.10, 0)]),
            "smirk_L": ([(0, 0), (0.42, -9.0), (1.10, 0)], [(0, 0), (0.42, 10.0), (1.10, 0)]),
            "smirk_R": ([(0, 0), (0.42, 14.0), (1.10, 0)], [(0, 0), (0.42, 18.0), (1.10, 0)]),
            "upper_lip": ([(0, 0), (1.10, 0)], [(0, 0), (0.26, 2.0), (0.42, 5.5), (1.10, 0)]),
            "lower_lip": ([(0, 0), (1.10, 0)], [(0, 0), (0.26, -3.0), (0.42, -13.5), (0.72, -2.0), (1.10, 0)]),
            "brow_L": ([(0, 0), (1.10, 0)], [(0, 0), (0.42, -10.0), (1.10, 0)]),
            "brow_R": ([(0, 0), (1.10, 0)], [(0, 0), (0.42, 16.0), (1.10, 0)]),
        },
        pupil_tx=[(0, 0.0), (0.26, 3.0), (0.40, -15.5), (0.72, -14.0), (1.10, 0.0)],
        pupil_ty=[(0, 0.0), (0.26, 2.0), (0.42, -3.0), (1.10, 0.0)],
    )
    fang_bones["impact_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.40, 0), (0.45, -9.0), (0.52, 7.5), (0.64, -3.5), (0.78, 0), (1.10, 0)],
            [(0, 0), (0.40, 0), (0.45, -10.5), (0.53, 6.0), (0.66, -2.5), (0.80, 0), (1.10, 0)],
        )
    }
    fang_bones["camera_shake"] = {
        "translate": sample_xy_curve(
            [(0, 0), (0.41, 0), (0.46, -8.0), (0.54, 6.5), (0.68, 0), (1.10, 0)],
            [(0, 0), (0.41, 0), (0.46, -9.0), (0.55, 5.0), (0.70, 0), (1.10, 0)],
        )
    }
    fang_slots = {
        "f3_glow_slot": {
            "rgba": [
                {"time": 0.0, "color": "c977ff10"},
                {"time": 0.26, "color": "d484ffcc"},
                {"time": 0.44, "color": "eb99ffff"},
                {"time": 0.92, "color": "c977ff22"},
                {"time": 1.10, "color": "c977ff10"},
            ]
        }
    }
    add_sill_impact_dust(fang_bones, fang_slots, 0.42, 1.10, intensity=0.60)
    animations["mockery_attack_fang"] = {"bones": fang_bones, "slots": fang_slots}

    # Mockery Taunt (`ThingsMockeryEchoPower`: smug eye-roll + 2-beat stone chuckle + amethyst scar glint!)
    animations["mockery_taunt"] = {
        "bones": bake_face_deform_bones(
            3,
            0.80,
            face_anchor_defs[3],
            core_ty=[(0, 0), (0.18, 5.0), (0.30, -2.0), (0.44, 4.5), (0.58, -1.5), (0.80, 0)],
            core_rot=[(0, 0), (0.20, -6.5), (0.46, 3.8), (0.80, 0)],
            core_sx=[(0, 1.0), (0.24, 1.07), (0.80, 1.0)],
            core_sy=[(0, 1.0), (0.24, 1.07), (0.80, 1.0)],
            features={
                "lid_L": ([(0, 0), (0.80, 0)], [(0, 0), (0.22, -4.5), (0.52, -3.5), (0.80, 0)]),
                "lid_R": ([(0, 0), (0.80, 0)], [(0, 0), (0.22, -4.5), (0.52, -3.5), (0.80, 0)]),
                "brow_R": ([(0, 0), (0.80, 0)], [(0, 0), (0.22, 17.0), (0.52, 13.0), (0.80, 0)]),
                "smirk_R": ([(0, 0), (0.24, 13.0), (0.48, 14.0), (0.80, 0)], [(0, 0), (0.24, 16.0), (0.48, 17.0), (0.80, 0)]),
                "lower_lip": ([(0, 0), (0.80, 0)], [(0, 0), (0.20, -9.0), (0.32, -3.0), (0.46, -9.5), (0.80, 0)]),
            },
            pupil_tx=[(0, 0.0), (0.16, -17.0), (0.48, -13.5), (0.80, 0.0)],
            pupil_ty=[(0, 0.0), (0.16, 3.5), (0.36, -1.5), (0.80, 0.0)],
        ),
        "slots": {
            "f3_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "c977ff10"},
                    {"time": 0.22, "color": "eb99ffff"},
                    {"time": 0.70, "color": "c977ff18"},
                    {"time": 0.80, "color": "c977ff10"},
                ]
            }
        },
    }

    # --- INDIVIDUALIZED PER-FACE HURT, STUN, CLIMAX ROAR & SHATTER CLIPS ---
    for k in range(4):
        hurt_feats: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
            "lid_L": ([(0, 0), (0.38, 0)], [(0, 0), (0.08, -6.5), (0.38, 0)]),
            "lid_R": ([(0, 0), (0.38, 0)], [(0, 0), (0.08, -6.5), (0.38, 0)]),
            "lower_lip": ([(0, 0), (0.38, 0)], [(0, 0), (0.09, -8.5), (0.38, 0)]),
        }
        animations[f"face_hurt_{k}"] = {
            "bones": bake_face_deform_bones(
                k,
                0.38,
                face_anchor_defs[k],
                core_tx=[(0, 0), (0.06, 14.0), (0.14, -10.0), (0.24, 4.0), (0.38, 0)],
                core_ty=[(0, 0), (0.07, 10.0), (0.18, -3.0), (0.38, 0)],
                core_sx=[(0, 1.0), (0.07, 0.92), (0.20, 1.03), (0.38, 1.0)],
                core_sy=[(0, 1.0), (0.07, 0.93), (0.20, 1.03), (0.38, 1.0)],
                features=hurt_feats,
                tlo_ty=[(0, 0), (0.09, -6.5), (0.38, 0)] if k == 0 else None,
                pupil_tx=[(0, 0), (0.08, -12.0), (0.38, 0)] if k == 3 else None,
            )
        }

        stun_feats: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
            "lid_L": ([(0, 0), (1.20, 0)], [(0, -6.0), (0.60, -7.5), (1.20, -6.0)]),
            "lid_R": ([(0, 0), (1.20, 0)], [(0, -6.0), (0.60, -7.5), (1.20, -6.0)]),
            "lower_lip": ([(0, 0), (1.20, 0)], [(0, -10.0), (0.60, -13.0), (1.20, -10.0)]),
        }
        animations[f"face_stun_{k}"] = {
            "bones": bake_face_deform_bones(
                k,
                1.20,
                face_anchor_defs[k],
                core_tx=[(0, -7.0), (0.60, 7.0), (1.20, -7.0)],
                core_ty=[(0, -5.0), (0.60, -2.0), (1.20, -5.0)],
                core_rot=[(0, -4.0), (0.60, 4.0), (1.20, -4.0)],
                features=stun_feats,
                tlo_ty=[(0, -8.5), (0.60, -11.0), (1.20, -8.5)] if k == 0 else None,
                pupil_tx=[(0, -16.0), (0.60, 3.0), (1.20, -16.0)] if k == 3 else None,
                pupil_ty=[(0, 3.0), (0.60, 1.5), (1.20, 3.0)] if k == 3 else None,
            )
        }

        # Full-Cycle 8-Turn Climax Roar per active face (`climax_cycle_roar_{k}`, hit=0.52s)
        roar_feats: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
            "upper_lip": ([(0, 0), (1.35, 0)], [(0, 0), (0.30, -2.0), (0.50, 10.5), (0.92, 6.5), (1.35, 0)]),
            "lower_lip": ([(0, 0), (1.35, 0)], [(0, 0), (0.30, 2.0), (0.50, -21.0), (0.92, -14.5), (1.35, 0)]),
            "brow_L": ([(0, 0), (1.35, 0)], [(0, 0), (0.50, -14.0 if k != 1 else 15.0), (1.35, 0)]),
            "brow_R": ([(0, 0), (1.35, 0)], [(0, 0), (0.50, -14.0 if k != 1 else 15.0), (1.35, 0)]),
        }
        roar_bones = bake_face_deform_bones(
            k,
            1.35,
            face_anchor_defs[k],
            core_ty=[(0, 0), (0.30, 18.0), (0.50, -9.0), (0.92, -4.5), (1.35, 0)],
            core_sx=[(0, 1.0), (0.30, 0.93), (0.50, 1.16), (0.92, 1.10), (1.35, 1.0)],
            core_sy=[(0, 1.0), (0.30, 0.94), (0.50, 1.13), (0.92, 1.08), (1.35, 1.0)],
            features=roar_feats,
            tup_ty=[(0, 0), (0.30, 0.0), (0.50, 6.5), (0.92, 4.2), (1.35, 0)] if k == 0 else None,
            tlo_ty=[(0, 0), (0.30, 0.0), (0.50, -18.5), (0.92, -12.5), (1.35, 0)] if k == 0 else None,
            clamp_scale=[(0, 1.0), (0.30, 0.95), (0.50, 1.08), (1.35, 1.0)] if k == 2 else None,
            pupil_tx=[(0, 0), (0.48, -14.5), (0.95, -14.5), (1.35, 0)] if k == 3 else None,
        )
        roar_bones["turntable_shake"] = {
            "translate": sample_xy_curve(
                [(0, 0), (0.15, -9), (0.30, 11), (0.48, -14), (0.62, 14), (0.78, -9), (0.96, 5), (1.35, 0)],
                [(0, 0), (0.25, 7), (0.52, -15), (0.84, 5), (1.35, 0)],
            )
        }
        roar_bones["camera_shake"] = {
            "translate": sample_xy_curve(
                [(0, 0), (0.15, -10), (0.30, 12), (0.48, -15), (0.62, 15), (0.78, -10), (0.96, 6), (1.35, 0)],
                [(0, 0), (0.22, -10), (0.52, 14), (0.82, -7), (1.35, 0)],
            )
        }
        glow_peak_colors = ["ffbe62ff", "99ffeeff", "fff088ff", "eb99ffff"]
        roar_slots = {
            f"f{k}_glow_slot": {
                "rgba": [
                    {"time": 0.0, "color": "ffffff00"},
                    {"time": 0.48, "color": glow_peak_colors[k]},
                    {"time": 0.96, "color": glow_peak_colors[k]},
                    {"time": 1.35, "color": "ffffff00"},
                ]
            }
        }
        add_sill_impact_dust(roar_bones, roar_slots, 0.50, 1.35, intensity=0.82)
        animations[f"climax_cycle_roar_{k}"] = {"bones": roar_bones, "slots": roar_slots}

        # Death Shatter (`face_shatter_{k}`, Track 3+k — sits above all lower tracks!)
        shatter_bones = bake_face_deform_bones(
            k,
            1.05,
            face_anchor_defs[k],
            core_tx=[(0, 0), (0.05, -16.0), (0.10, 18.0), (0.15, -16.0), (0.21, 14.0), (0.22, 0), (1.05, 0)],
            core_ty=[(0, 0), (0.11, 6.0), (0.21, -4.0), (0.22, 0), (1.05, 0)],
            core_sx=[(0, 1.0), (0.18, 1.07), (0.21, 1.09), (0.22, 1.0), (1.05, 1.0)],
            core_sy=[(0, 1.0), (0.18, 1.07), (0.21, 1.09), (0.22, 1.0), (1.05, 1.0)],
        )
        shatter_bones["impact_shake"] = {
            "translate": sample_xy_curve(
                [(0, 0), (0.20, 0), (0.24, -14.0), (0.30, 12.0), (0.38, -8.0), (0.50, 4.0), (0.68, 0), (1.05, 0)],
                [(0, 0), (0.20, 0), (0.24, -12.0), (0.31, 9.0), (0.40, -5.0), (0.68, 0), (1.05, 0)],
            )
        }
        shatter_bones["camera_shake"] = {
            "translate": sample_xy_curve(
                [(0, 0), (0.20, 0), (0.24, -14.0), (0.31, 11.5), (0.39, -7.5), (0.52, 3.5), (0.72, 0), (1.05, 0)],
                [(0, 0), (0.20, 0), (0.24, -13.0), (0.32, 9.5), (0.42, -5.0), (0.72, 0), (1.05, 0)],
            )
        }
        shatter_slots: dict[str, dict] = {}
        extra_hide = [f"f{k}_glow_slot"] + (["f2_split_l_slot", "f2_split_r_slot"] if k == 2 else [])
        for sname in face_opaque_slots[k] + extra_hide:
            shatter_slots[sname] = {"attachment": [{"time": 0.22, "name": None}]}
        for strip in range(WALL_STRIPS):
            shatter_slots[f"wall_s{k}_{strip}"] = {
                "attachment": [
                    {"time": 0.0, "name": f"intact_s{k}_{strip}"},
                    {"time": 0.22, "name": f"shattered_s{k}_{strip}"},
                ]
            }
        for i, (ox, _) in enumerate(chip_origins):
            vx = (ox * 1.55) + (-42.0 if i % 2 == 0 else 42.0)
            vy_peak = 82.0 + (i % 3) * 28.0
            shatter_bones[f"rubble_{i}"] = {
                "translate": sample_xy_curve(
                    [(0, 0), (0.21, 0), (0.52, vx * 0.58), (1.05, vx)],
                    [(0, 0), (0.21, 0), (0.42, vy_peak), (1.05, -280.0 - (i % 3) * 32.0)],
                ),
                "rotate": sample_curve([(0, 0), (0.21, 0), (1.05, (-1 if i % 2 else 1) * (105.0 + i * 20.0))]),
            }
            shatter_slots[f"rubble_slot_{i}"] = {
                "rgba": [
                    {"time": 0.0, "color": "ffffff00"},
                    {"time": 0.20, "color": "ffffff00"},
                    {"time": 0.22, "color": "ffffffff"},
                    {"time": 0.78, "color": "ffffffcc"},
                    {"time": 1.05, "color": "ffffff00"},
                ]
            }
        for i in (2, 3, 4, 5):
            outward = -1.0 if i < 4 else 1.0
            shatter_bones[f"dust_{i}"] = {
                "translate": sample_xy_curve([(0, 0), (0.21, 0), (1.05, outward * 65.0)], [(0, 0), (0.21, 0), (1.05, 42.0)]),
                "scale": sample_xy_curve([(0, 0.7), (0.21, 0.7), (1.05, 1.8)], [(0, 0.7), (0.21, 0.7), (1.05, 1.65)]),
            }
            shatter_slots[f"dust_slot_{i}"] = {
                "rgba": [
                    {"time": 0.0, "color": "ffffff00"},
                    {"time": 0.21, "color": "ffffff00"},
                    {"time": 0.26, "color": "f2f5ebdd"},
                    {"time": 1.05, "color": "ffffff00"},
                ]
            }
        animations[f"face_shatter_{k}"] = {"bones": shatter_bones, "slots": shatter_slots}

        # Persistent shattered-sector attachment clip
        st_slots: dict[str, dict] = {}
        for sname in face_opaque_slots[k] + extra_hide:
            st_slots[sname] = {"attachment": [{"time": 0.0, "name": None}]}
        for strip in range(WALL_STRIPS):
            st_slots[f"wall_s{k}_{strip}"] = {"attachment": [{"time": 0.0, "name": f"shattered_s{k}_{strip}"}]}
        animations[f"state_shattered_{k}"] = {"slots": st_slots}

    # Fallback alias for standalone/legacy callers
    animations["climax_cycle_roar"] = animations["climax_cycle_roar_0"]

    skeleton = {
        "skeleton": {
            "hash": "human-face-tower-v3",
            "spine": "4.2.43",
            "x": -960,
            "y": -540,
            "width": 1920,
            "height": 1080,
        },
        "bones": bones,
        "slots": slots,
        "skins": [{"name": "default", "attachments": attachments}],
        "animations": animations,
    }

    spjson_path = OUT_DIR / f"{KEY}.spjson"
    spjson_path.write_text(json.dumps(skeleton, separators=(",", ":")), encoding="utf-8")

    tres_path = OUT_DIR / f"{KEY}_skel_data.tres"
    tres_path.write_text(
        f"""[gd_resource type="SpineSkeletonDataResource" load_steps=3 format=3]
[ext_resource type="SpineAtlasResource" path="{RES_DIR}/{KEY}.spatlas" id="1"]
[ext_resource type="SpineSkeletonFileResource" path="{RES_DIR}/{KEY}.spjson" id="2"]
[resource]
atlas_res = ExtResource("1")
skeleton_file_res = ExtResource("2")
default_mix = 0.12
""",
        encoding="utf-8",
    )

    # =========================================================================
    # AUTOMATED MATHEMATICAL & ANATOMICAL RIG AUDIT (60 HZ + SEAM + TOPOLOGY)
    # =========================================================================
    # 1. Verify looping clips close with 0.000px jump & one-shot clips return cleanly to (0, 0)
    loop_clips = ["wrath_idle", "lament_idle", "bastion_idle_bolted", "bastion_idle_unbolted", "mockery_idle", *[f"face_stun_{k}" for k in range(4)]]
    for cname in loop_clips:
        for bname, bdata in animations[cname].get("bones", {}).items():
            if "translate" in bdata:
                t0, t1 = bdata["translate"][0], bdata["translate"][-1]
                jump = math.hypot(t1["x"] - t0["x"], t1["y"] - t0["y"])
                assert jump < 1e-3, f"Loop closure jump in {cname}/{bname}: {jump:.4f}px"

    oneshot_face_clips = [
        "wrath_attack_heavy",
        "wrath_attack_double",
        "lament_cast_wail",
        "lament_attack_rain",
        "bastion_fortify",
        "bastion_rebolt_attack",
        "bastion_attack_colossus",
        "mockery_attack_twin",
        "mockery_attack_fang",
        "mockery_taunt",
        *[f"face_hurt_{k}" for k in range(4)],
        *[f"climax_cycle_roar_{k}" for k in range(4)],
    ]
    for cname in oneshot_face_clips:
        for bname, bdata in animations[cname].get("bones", {}).items():
            if bname.startswith("dust_"):
                continue
            if "translate" in bdata:
                t_end = bdata["translate"][-1]
                rem = math.hypot(t_end["x"], t_end["y"])
                assert rem < 1e-3, f"One-shot {cname}/{bname} did not return to setup: ({t_end['x']}, {t_end['y']})"

    # 2. Verify Wrath's upper & lower teeth never cross over (tlo_dy <= tup_dy) across ALL Face-0 clips
    for cname in ("wrath_idle", "wrath_attack_heavy", "wrath_attack_double", "face_hurt_0", "face_stun_0", "climax_cycle_roar_0"):
        bmap = animations[cname]["bones"]
        if "f0_tup_def_4_4" in bmap and "f0_tlo_def_4_4" in bmap:
            for ku, kl in zip(bmap["f0_tup_def_4_4"]["translate"], bmap["f0_tlo_def_4_4"]["translate"]):
                assert kl["y"] <= ku["y"] + 1e-3, f"Wrath teeth crossed over in {cname} at t={ku['time']}: lo={kl['y']} > up={ku['y']}"

    # 3. Verify zero 9x7 lattice cell Jacobian foldover / inversion across all facial clips when centered
    base_grid = {
        k: {(ix, iy): face_lattice_pose(0.0, ix, iy)[:2] for iy in range(LAT_NY + 1) for ix in range(LAT_NX + 1)}
        for k in range(4)
    }
    min_cell_area_ratio = 1.0
    for cname in loop_clips + oneshot_face_clips + ["bastion_unbolt"]:
        bmap = animations[cname].get("bones", {})
        face_k = next((k for k in range(4) if f"f{k}_def_4_3" in bmap), None)
        if face_k is None:
            continue
        n_frames = len(bmap[f"f{face_k}_def_4_3"]["translate"])
        for fi in range(n_frames):
            pts: dict[tuple[int, int], tuple[float, float]] = {}
            for iy in range(LAT_NY + 1):
                for ix in range(LAT_NX + 1):
                    bx0, by0 = base_grid[face_k][(ix, iy)]
                    dname = f"f{face_k}_def_{ix}_{iy}"
                    if dname in bmap:
                        tk = bmap[dname]["translate"][fi]
                        # Child bone local delta rotated/scaled by centered carrier bone
                        _, _, lrot, lsx = face_lattice_pose(0.0, ix, iy)
                        rad = math.radians(lrot)
                        dx_w = (tk["x"] * lsx) * math.cos(rad) - tk["y"] * math.sin(rad)
                        dy_w = (tk["x"] * lsx) * math.sin(rad) + tk["y"] * math.cos(rad)
                        pts[(ix, iy)] = (bx0 + dx_w, by0 + dy_w)
                    else:
                        pts[(ix, iy)] = (bx0, by0)
            for iy in range(LAT_NY):
                for ix in range(LAT_NX):
                    p00, p10 = pts[(ix, iy)], pts[(ix + 1, iy)]
                    p01 = pts[(ix, iy + 1)]
                    b00, b10, b01 = base_grid[face_k][(ix, iy)], base_grid[face_k][(ix + 1, iy)], base_grid[face_k][(ix, iy + 1)]
                    rest_cross = (b10[0] - b00[0]) * (b01[1] - b00[1]) - (b10[1] - b00[1]) * (b01[0] - b00[0])
                    cur_cross = (p10[0] - p00[0]) * (p01[1] - p00[1]) - (p10[1] - p00[1]) * (p01[0] - p00[0])
                    ratio = cur_cross / rest_cross
                    assert ratio > 0.22, f"Lattice pinch/inversion in {cname} frame {fi} cell ({ix},{iy}): ratio={ratio:.3f}"
                    min_cell_area_ratio = min(min_cell_area_ratio, ratio)

    # 4. Single-Face & Zero-Onscreen-Fade 360° Turntable Audit
    vp_half_x = 960.0 / 1.032
    visibility_report: dict[str, object] = {}
    for slot_i in range(4):
        phi = -slot_i * 0.5 * math.pi
        on_screen = []
        for k in range(4):
            theta = wrap_pi(phi + k * 0.5 * math.pi)
            _, vis = face_shade_and_vis(theta)
            lx_lo = min(face_lattice_pose(theta, 0, r)[0] for r in range(LAT_NY + 1))
            lx_hi = max(face_lattice_pose(theta, LAT_NX, r)[0] for r in range(LAT_NY + 1))
            if (vis > 0.01) and (lx_hi > -vp_half_x) and (lx_lo < vp_half_x):
                on_screen.append(FACE_NAMES[k])
        assert len(on_screen) == 1, f"Slot {slot_i} expected 1 visible face, got {on_screen}"
        visibility_report[f"slot_{slot_i}"] = on_screen

    max_simultaneous_faces = 0
    min_onscreen_alpha = 1.0
    for deg_half in range(720):
        phi = math.radians(-0.5 * deg_half)
        cnt = 0
        for k in range(4):
            theta = wrap_pi(phi + k * 0.5 * math.pi)
            _, vis = face_shade_and_vis(theta)
            lx_lo = min(face_lattice_pose(theta, 0, r)[0] for r in range(LAT_NY + 1))
            lx_hi = max(face_lattice_pose(theta, LAT_NX, r)[0] for r in range(LAT_NY + 1))
            in_vp = (lx_hi > -vp_half_x) and (lx_lo < vp_half_x)
            if in_vp:
                min_onscreen_alpha = min(min_onscreen_alpha, vis)
                assert vis >= 0.999, f"On-screen fade for {FACE_NAMES[k]} at phi={math.degrees(phi):.1f}°: vis={vis:.4f}"
            if (vis > 0.01) and in_vp:
                cnt += 1
        assert cnt <= 1, f"Two faces simultaneously visible at phi={math.degrees(phi):.1f} deg!"
        max_simultaneous_faces = max(max_simultaneous_faces, cnt)

    visibility_report["max_simultaneous_across_360deg"] = max_simultaneous_faces
    visibility_report["min_onscreen_alpha"] = round(min_onscreen_alpha, 4)
    visibility_report["min_lattice_jacobian_ratio"] = round(min_cell_area_ratio, 3)

    print(
        json.dumps(
            {
                "status": "OK",
                "bones": len(bones),
                "slots": len(slots),
                "animations": len(animations),
                "single_face_audit": visibility_report,
                "spjson_mb": round(spjson_path.stat().st_size / (1024 * 1024), 2),
                "spjson": str(spjson_path),
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
