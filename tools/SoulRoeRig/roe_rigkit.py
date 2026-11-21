"""roe_rigkit — Spine 4.2 builder, 6-pass RGB-dilated atlas packer, exporter, packager & renderer for SoulRoeRig."""
from __future__ import annotations

import json
import math
import pickle
import shutil
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from anims import ANIMS, EVENTS, make_anim_funcs
from rigdef import make_rig_spec
from rigutil import clamp

FPS = 60


def S_pt(p: tuple[float, float] | np.ndarray, origin: tuple[float, float]) -> tuple[float, float]:
    return (round(float(p[0]) - origin[0], 2), round(origin[1] - float(p[1]), 2))


def hexcol(r: float, g: float, b: float, a: float) -> str:
    return "".join(f"{int(round(clamp(v) * 255.0)):02x}" for v in (r, g, b, a))


class Skel:
    def __init__(self, bones: list[dict], origin: tuple[float, float]) -> None:
        self.bones = bones
        self.origin = origin
        self.idx = {b["name"]: i for i, b in enumerate(bones)}
        self.setup = {b["name"]: S_pt(b["pos"], origin) for b in bones}
        self.parent = {b["name"]: b["parent"] for b in bones}
        self.local: dict[str, tuple[float, float]] = {}
        for b in bones:
            p = b["parent"]
            sp = self.setup[b["name"]]
            self.local[b["name"]] = sp if p is None else (sp[0] - self.setup[p][0], sp[1] - self.setup[p][1])

    def world(self, pose: dict) -> dict[str, np.ndarray]:
        W: dict[str, np.ndarray] = {}
        for b in self.bones:
            n = b["name"]
            r, dx, dy, sx, sy = pose.get(n, (0.0, 0.0, 0.0, 1.0, 1.0))
            lx, ly = self.local[n]
            c, s = math.cos(math.radians(r)), math.sin(math.radians(r))
            M = np.array([[c * sx, -s * sy, lx + dx], [s * sx, c * sy, ly + dy], [0.0, 0.0, 1.0]])
            W[n] = M if b["parent"] is None else W[b["parent"]] @ M
        return W


def longest_increasing_subseq_indices(seq: list[int]) -> set[int]:
    """Return the set of VALUES in `seq` forming a longest strictly increasing subsequence."""
    if not seq:
        return set()
    n = len(seq)
    dp = [1] * n
    prev = [-1] * n
    best_end = 0
    for i in range(n):
        for j in range(i):
            if seq[j] < seq[i] and dp[j] + 1 > dp[i]:
                dp[i] = dp[j] + 1
                prev[i] = j
        if dp[i] > dp[best_end]:
            best_end = i
    chosen: set[int] = set()
    cur = best_end
    while cur != -1:
        chosen.add(seq[cur])
        cur = prev[cur]
    return chosen


def compute_spine_draw_order_offsets(setup_slots: list[str], target_order: list[str]) -> list[dict] | None:
    """Compute exact Spine 4.2 DrawOrderTimeline `offsets` array sorted by `slot.index` ascending.
    Returns `None` when `target_order == setup_slots` (Spine JSON resets drawOrder when `offsets` is omitted).
    """
    if target_order == setup_slots:
        return None
    s_idx = {s: i for i, s in enumerate(setup_slots)}
    t_idx = {s: i for i, s in enumerate(target_order)}
    seq = [s_idx[s] for s in target_order]
    unchanged_set = longest_increasing_subseq_indices(seq)
    offsets = []
    for orig_i, s_name in enumerate(setup_slots):
        if orig_i not in unchanged_set:
            offsets.append({"slot": s_name, "offset": t_idx[s_name] - orig_i})
    return offsets


def reduce_keys(times: list[float], vals: list[tuple[float, ...]], eps: tuple[float, ...]) -> list[int]:
    keep = [0]
    i = 0
    N = len(times)
    while i < N - 1:
        j = i + 2
        while j < N:
            ok = True
            t0, t1 = times[i], times[j]
            for m in range(i + 1, j):
                u = (times[m] - t0) / (t1 - t0)
                for q in range(len(vals[0])):
                    if abs(vals[i][q] + (vals[j][q] - vals[i][q]) * u - vals[m][q]) > eps[q]:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
            j += 1
        i = j - 1
        keep.append(i)
    return keep


def build_variant(variant: int) -> dict:
    spec = make_rig_spec(variant)
    name = spec["name"]
    origin = spec["origin"]
    parts_dir = HERE / "parts" / name
    out_dir = HERE / "out" / name
    out_dir.mkdir(parents=True, exist_ok=True)

    meta = json.loads((parts_dir / "meta.json").read_text(encoding="utf-8"))
    bones = [dict(name=n, parent=p, pos=list(pos)) for n, p, pos in spec["bones"]]
    bidx = {b["name"]: i for i, b in enumerate(bones)}
    bpos = {b["name"]: b["pos"] for b in bones}

    slot_names = spec["order"]
    imgs = {sn: Image.open(parts_dir / f"{sn}.png").convert("RGBA") for sn in slot_names}

    PAD = 3

    def pack(PW: int, PH: int) -> dict[str, tuple[int, int]] | None:
        rects: dict[str, tuple[int, int]] = {}
        x = y = shelf = 0
        for pk in sorted(imgs, key=lambda k: (-imgs[k].height, -imgs[k].width, k)):
            w, h = imgs[pk].width + 2 * PAD, imgs[pk].height + 2 * PAD
            if x + w > PW:
                x = 0
                y += shelf
                shelf = 0
            if y + h > PH:
                return None
            rects[pk] = (x + PAD, y + PAD)
            x += w
            shelf = max(shelf, h)
        return rects

    rects = None
    PW = PH = 1024
    for cand_w, cand_h in [(512, 512), (1024, 512), (512, 1024), (1024, 1024), (1024, 1536)]:
        rects = pack(cand_w, cand_h)
        if rects:
            PW, PH = cand_w, cand_h
            break
    if not rects:
        raise RuntimeError(f"Atlas packing failed for {name}")

    page = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
    for pk, (x, y) in rects.items():
        page.paste(imgs[pk], (x, y))

    # Mandatory 6-pass RGB edge dilation on transparent pixels so Linear filtering has ZERO dark halo!
    P = np.array(page).astype(np.float32)
    known = P[..., 3] > 0
    col = P[..., :3].copy()
    col[~known] = 0.0
    for _ in range(6):
        k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        s = cv2.blur(col * known[..., None].astype(np.float32), (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = k & ~known
        if np.any(new):
            col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = k
    P[..., :3] = np.clip(np.round(col), 0, 255)
    Image.fromarray(P.astype(np.uint8)).save(out_dir / f"{name}.png", optimize=True)

    atlas_lines = [
        f"{name}.png",
        f"size: {PW},{PH}",
        "format: RGBA8888",
        "filter: Linear,Linear",
        "repeat: none",
    ]
    for pk in slot_names:
        x, y = rects[pk]
        w, h = imgs[pk].size
        atlas_lines += [
            pk,
            "  rotate: false",
            f"  xy: {x}, {y}",
            f"  size: {w}, {h}",
            f"  orig: {w}, {h}",
            "  offset: 0, 0",
            "  index: -1",
        ]
    (out_dir / f"{name}.atlas").write_text("\n".join(atlas_lines) + "\n", encoding="utf-8", newline="\n")

    def grid_mesh(pk: str, spacing: int) -> tuple[np.ndarray, np.ndarray, list[int]]:
        al = np.array(imgs[pk])[..., 3]
        h, w = al.shape
        xs = sorted(set([int(v) for v in np.arange(0, w, spacing)] + [w]))
        ys = sorted(set([int(v) for v in np.arange(0, h, spacing)] + [h]))
        occ = cv2.dilate((al > 0).astype(np.uint8), np.ones((3, 3), np.uint8))
        vid: dict[tuple[int, int], int] = {}
        verts: list[tuple[int, int]] = []
        tris: list[int] = []

        def V(i: int, j: int) -> int:
            if (i, j) not in vid:
                vid[(i, j)] = len(verts)
                verts.append((xs[i], ys[j]))
            return vid[(i, j)]

        for j in range(len(ys) - 1):
            for i in range(len(xs) - 1):
                if occ[ys[j] : ys[j + 1], xs[i] : xs[i + 1]].any():
                    a, b, c, d = V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)
                    tris += [a, b, c, a, c, d]
        verts_arr = np.array(verts, dtype=float)
        uvs = np.stack([verts_arr[:, 0] / w, verts_arr[:, 1] / h], axis=1)
        return verts_arr + np.array([meta[pk]["x"], meta[pk]["y"]], dtype=float), uvs, tris

    def spine_vertices(world: np.ndarray, wts: list[list[tuple[str, float]]]) -> list[float]:
        out: list[float] = []
        for p, wl in zip(world, wts):
            sp = S_pt(p, origin)
            out.append(len(wl))
            for b, w in wl:
                bp = S_pt(bpos[b], origin)
                out += [bidx[b], round(sp[0] - bp[0], 2), round(sp[1] - bp[1], 2), round(w, 4)]
        return out

    slots_json = []
    def_alpha = spec["slot_default_alpha"]
    add_slots = spec["additive_slots"]
    for sn in slot_names:
        sl: dict = {"name": sn, "bone": spec["slot_bone"][sn], "attachment": sn}
        if sn in add_slots:
            sl["blend"] = "additive"
        a0 = def_alpha.get(sn, 1.0)
        if abs(a0 - 1.0) > 1e-4 or sn in add_slots:
            sl["color"] = hexcol(1.0, 1.0, 1.0, a0)
        slots_json.append(sl)

    attachments = {}
    meshes = {}
    for sn in slot_names:
        world, uvs, tris = grid_mesh(sn, spec["spacing"].get(sn, 24))
        wts = spec["weights"](sn, world)
        meshes[sn] = dict(world=world.tolist(), uvs=uvs.tolist(), tris=tris, wts=wts, part=sn)
        attachments[sn] = {
            sn: {
                "type": "mesh",
                "uvs": [round(float(v), 5) for v in uvs.flatten()],
                "triangles": tris,
                "vertices": spine_vertices(world, wts),
                "hull": 0,
                "width": meta[sn]["w"],
                "height": meta[sn]["h"],
            }
        }

    skins_payload = [{"name": "default", "attachments": attachments}]
    rig_static = dict(
        variant=variant,
        name=name,
        origin=list(origin),
        bones=bones,
        slots=slots_json,
        skins=skins_payload,
        mesh=meshes,
        page=[PW, PH],
    )
    (out_dir / "rig_static.json").write_text(json.dumps(rig_static), encoding="utf-8", newline="\n")
    print(
        f"[{name}] atlas={PW}x{PH} bones={len(bones)} slots={len(slots_json)} "
        f"verts={sum(len(m['world']) for m in meshes.values())} tris={sum(len(m['tris']) // 3 for m in meshes.values())}"
    )
    return rig_static


def sample_clip(variant: int, anim_name: str, dur: float, loop: bool, funcs: dict) -> list[dict]:
    spec = make_rig_spec(variant)
    n = int(round(dur * FPS))
    dt = dur / n
    fn = funcs[anim_name]
    frames: list[dict] = []
    for k in range(n + 1):
        tt = (k * dt) % dur if (loop and k == n) else min(k * dt, dur)
        f = fn(tt)
        # Strictly assert zero bone scale on every frame!
        clean_pose = {}
        for b_name, val in f.P.items():
            r, x, y, sx, sy = val
            assert abs(sx - 1.0) < 1e-9 and abs(sy - 1.0) < 1e-9, f"Illegal bone scale on {b_name}: {sx}, {sy}"
            clean_pose[b_name] = (float(r), float(x), float(y), 1.0, 1.0)
        frames.append(
            dict(
                t=round(k * dt, 5),
                pose=clean_pose,
                tint=tuple(float(v) for v in f.tint),
                slot_alpha={k_s: float(v_s) for k_s, v_s in f.slot_alpha.items()},
                slot_rgba={k_s: tuple(float(c) for c in v_s) for k_s, v_s in f.slot_rgba.items()},
                draw_order=list(f.draw_order) if f.draw_order else list(spec["order"]),
            )
        )
    if loop:
        frames[-1] = dict(frames[0], t=round(dur, 5))
    return frames


def build_anim_json(sk: Skel, spec: dict, frames: list[dict]) -> dict:
    times = [f["t"] for f in frames]
    bones_out = {}
    for b in sk.idx:
        raw_rot = [f["pose"].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[0] for f in frames]
        # Unwrap continuous angle sequence so linear interpolation never hits a +-180 wrap!
        unwrapped = [raw_rot[0]]
        for r_val in raw_rot[1:]:
            prev = unwrapped[-1]
            unwrapped.append(prev + ((r_val - prev + 180.0) % 360.0 - 180.0))
        rot = [(v,) for v in unwrapped]
        tr = [tuple(f["pose"].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[1:3]) for f in frames]

        tl: dict = {}
        if any(abs(v[0]) > 1e-4 for v in rot):
            ks = reduce_keys(times, rot, (0.04,))
            tl["rotate"] = [{"time": round(times[k], 4), "value": round(rot[k][0], 3)} for k in ks]
        if any(abs(v[0]) > 1e-3 or abs(v[1]) > 1e-3 for v in tr):
            ks = reduce_keys(times, tr, (0.03, 0.03))
            tl["translate"] = [{"time": round(times[k], 4), "x": round(tr[k][0], 2), "y": round(tr[k][1], 2)} for k in ks]
        if tl:
            for arr in tl.values():
                if arr[0]["time"] == 0:
                    del arr[0]["time"]
            bones_out[b] = tl

    slots_out = {}
    setup_slots = spec["order"]
    def_alpha = spec["slot_default_alpha"]
    add_slots = spec["additive_slots"]

    for sn in setup_slots:
        da = def_alpha.get(sn, 1.0)
        vals: list[tuple[float, float, float, float]] = []
        diff = False
        is_add = sn in add_slots
        for f in frames:
            tr0, tg0, tb0, ta0 = f["tint"]
            if sn in f["slot_rgba"]:
                sr, sg, sb, sa = f["slot_rgba"][sn]
                # Additive glows carry their own RGB light color, modulated by global fade (`ta0`)
                rgba = (sr, sg, sb, sa * ta0) if is_add else (sr * tr0, sg * tg0, sb * tb0, sa * ta0)
            else:
                sa = f["slot_alpha"].get(sn, da)
                rgba = (1.0, 1.0, 1.0, sa * ta0) if is_add else (tr0, tg0, tb0, sa * ta0)
            if (
                abs(rgba[0] - 1.0) > 1e-3
                or abs(rgba[1] - 1.0) > 1e-3
                or abs(rgba[2] - 1.0) > 1e-3
                or abs(rgba[3] - da) > 1e-3
            ):
                diff = True
            vals.append(rgba)
        if diff:
            ks = reduce_keys(times, vals, (0.004, 0.004, 0.004, 0.004))
            arr = [{"time": round(times[k], 4), "color": hexcol(*vals[k])} for k in ks]
            if arr and arr[0].get("time") == 0:
                del arr[0]["time"]
            slots_out[sn] = {"rgba": arr}

    out_anim: dict = {"bones": bones_out, "slots": slots_out}

    # Emit exact Spine 4.2 `drawOrder` timeline whenever satellite 2.5D depth order differs from setup!
    if any(f["draw_order"] != setup_slots for f in frames):
        do_keys = []
        prev_sig = None
        for f in frames:
            sig = tuple(f["draw_order"])
            if sig == prev_sig:
                continue
            prev_sig = sig
            offs = compute_spine_draw_order_offsets(setup_slots, f["draw_order"])
            entry: dict = {"time": round(f["t"], 4)}
            if offs:
                entry["offsets"] = offs
            if entry["time"] == 0:
                del entry["time"]
            do_keys.append(entry)
        if do_keys:
            out_anim["drawOrder"] = do_keys

    return out_anim


def export_and_package_variant(variant: int) -> None:
    spec = make_rig_spec(variant)
    name = spec["name"]
    origin = tuple(spec["origin"])
    out_dir = HERE / "out" / name
    pkg_dir = HERE / "pkg" / name
    pkg_dir.mkdir(parents=True, exist_ok=True)

    rig = json.loads((out_dir / "rig_static.json").read_text(encoding="utf-8"))
    sk = Skel(rig["bones"], origin)
    funcs = make_anim_funcs(variant)

    anim_json = {}
    all_frames = {}
    for anim_name, (dur, loop) in ANIMS.items():
        fr = sample_clip(variant, anim_name, dur, loop, funcs)
        all_frames[anim_name] = fr
        anim_json[anim_name] = build_anim_json(sk, spec, fr)

    bones_json = []
    for b in rig["bones"]:
        e: dict = {"name": b["name"]}
        if b["parent"]:
            e["parent"] = b["parent"]
        lx, ly = sk.local[b["name"]]
        if abs(lx) > 1e-6:
            e["x"] = round(lx, 2)
        if abs(ly) > 1e-6:
            e["y"] = round(ly, 2)
        bones_json.append(e)

    all_w = np.array([S_pt(p, origin) for m in rig["mesh"].values() for p in m["world"]])
    x0, y0 = all_w.min(axis=0)
    x1, y1 = all_w.max(axis=0)

    skeleton = {
        "skeleton": {
            "hash": name.replace("_", "") + "v1",
            "spine": "4.2.0",
            "x": round(float(x0), 2),
            "y": round(float(y0), 2),
            "width": round(float(x1 - x0), 2),
            "height": round(float(y1 - y0), 2),
            "images": "./",
            "audio": "",
        },
        "bones": bones_json,
        "slots": rig["slots"],
        "skins": rig["skins"],
        "events": {ev_name: {} for ev_list in EVENTS.values() for ev_name, _ in ev_list},
        "animations": anim_json,
    }
    for anim_name, ev_list in EVENTS.items():
        if ev_list and anim_name in skeleton["animations"]:
            skeleton["animations"][anim_name]["events"] = [{"time": ev_t, "name": ev_n} for ev_n, ev_t in ev_list]

    (out_dir / f"{name}.json").write_text(json.dumps(skeleton, separators=(",", ":")), encoding="utf-8", newline="\n")
    with open(out_dir / "frames.pkl", "wb") as fp:
        pickle.dump(all_frames, fp)

    # Package `.png`, `.atlas`, `.spatlas`, `.spjson`, `_skel_data.tres` into `pkg/{name}/`
    res_dir = f"res://STS2_Things/animations/monsters/{name}"
    atlas_text = (out_dir / f"{name}.atlas").read_text(encoding="utf-8")
    shutil.copyfile(out_dir / f"{name}.png", pkg_dir / f"{name}.png")
    (pkg_dir / f"{name}.atlas").write_text(atlas_text, encoding="utf-8", newline="\n")
    (pkg_dir / f"{name}.spjson").write_text(json.dumps(skeleton, separators=(",", ":")), encoding="utf-8", newline="\n")
    (pkg_dir / f"{name}.spatlas").write_text(
        json.dumps(
            dict(
                source_path=f"{res_dir}/{name}.atlas",
                atlas_data=atlas_text,
                normal_texture_prefix="n",
                specular_texture_prefix="s",
            ),
            separators=(",", ":"),
        ),
        encoding="utf-8",
        newline="\n",
    )
    (pkg_dir / f"{name}_skel_data.tres").write_text(
        f"""[gd_resource type="SpineSkeletonDataResource" load_steps=3 format=3]

[ext_resource type="SpineAtlasResource" path="{res_dir}/{name}.spatlas" id="1_atlas"]
[ext_resource type="SpineSkeletonFileResource" path="{res_dir}/{name}.spjson" id="2_skeleton"]

[resource]
atlas_res = ExtResource("1_atlas")
skeleton_file_res = ExtResource("2_skeleton")
default_mix = 0.12
""",
        encoding="utf-8",
        newline="\n",
    )
    print(f"[{name}] packaged -> {pkg_dir} ({', '.join(f'{k}:{len(v)}f' for k, v in all_frames.items())})")


class VariantRenderer:
    """High-precision Python mesh + additive + drawOrder renderer for visual contact sheets."""

    def __init__(self, variant: int) -> None:
        self.spec = make_rig_spec(variant)
        self.name = self.spec["name"]
        self.origin = tuple(self.spec["origin"])
        out_dir = HERE / "out" / self.name
        parts_dir = HERE / "parts" / self.name

        self.rig = json.loads((out_dir / "rig_static.json").read_text(encoding="utf-8"))
        self.sk = Skel(self.rig["bones"], self.origin)
        meta = json.loads((parts_dir / "meta.json").read_text(encoding="utf-8"))
        self.tex = {
            sn: np.array(Image.open(parts_dir / f"{sn}.png").convert("RGBA")).astype(np.float32) / 255.0
            for sn in self.spec["order"]
            if sn in meta
        }
        bpos = {b["name"]: b["pos"] for b in self.rig["bones"]}
        self.mesh = self.rig["mesh"]
        self.binds = {
            n: [
                [(b, np.array(S_pt(p, self.origin)) - np.array(S_pt(bpos[b], self.origin)), w) for b, w in wl]
                for p, wl in zip(m["world"], m["wts"])
            ]
            for n, m in self.mesh.items()
        }

    def render(
        self,
        frame: dict,
        scale: float = 0.72,
        W: int = 360,
        H: int = 270,
        ox: int = 230,
        oy: int = 198,
        bg: tuple[int, int, int] = (24, 30, 40),
    ) -> np.ndarray:
        Wm = self.sk.world(frame["pose"])
        canvas = np.zeros((H, W, 3), dtype=np.float32)
        canvas[...] = np.array(bg, dtype=np.float32) / 255.0
        # Subtle sea-floor reference line at oy
        cv2.line(canvas, (0, oy), (W, oy), (0.18, 0.22, 0.29), 1)

        tr0, tg0, tb0, ta0 = frame["tint"]
        draw_list = frame.get("draw_order") or self.spec["order"]
        def_alpha = self.spec["slot_default_alpha"]
        add_slots = self.spec["additive_slots"]

        for n in draw_list:
            if n not in self.mesh:
                continue
            m = self.mesh[n]
            is_add = n in add_slots
            da = def_alpha.get(n, 1.0)
            if n in frame.get("slot_rgba", {}):
                sr, sg, sb, sa = frame["slot_rgba"][n]
                col = np.array([sr, sg, sb], dtype=np.float32) if is_add else np.array([sr * tr0, sg * tg0, sb * tb0], dtype=np.float32)
                amul = sa * ta0
            else:
                sa = frame.get("slot_alpha", {}).get(n, da)
                col = np.ones(3, dtype=np.float32) if is_add else np.array([tr0, tg0, tb0], dtype=np.float32)
                amul = sa * ta0

            if amul < 0.006:
                continue

            pts = np.array(
                [sum(w * (Wm[b] @ np.array([l[0], l[1], 1.0]))[:2] for b, l, w in bl) for bl in self.binds[n]],
                dtype=np.float64,
            )
            scr = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], axis=1)
            t_img = self.tex[m["part"]]
            th, tw = t_img.shape[:2]
            uv = np.array(m["uvs"], dtype=np.float64) * np.array([tw, th], dtype=np.float64)

            # Composite entire slot into a single per-slot buffer using exact half-space triangle ownership
            # so shared mesh edges NEVER double-accumulate alpha/additive seams!
            slot_rgba_buf = np.zeros((H, W, 4), dtype=np.float32)
            for tri in np.array(m["tris"]).reshape(-1, 3):
                d = scr[tri].astype(np.float32)
                s = uv[tri].astype(np.float32)
                x0_i, y0_i = np.floor(d.min(axis=0)).astype(int)
                x1_i, y1_i = np.ceil(d.max(axis=0)).astype(int) + 1
                x0_i, y0_i = max(x0_i, 0), max(y0_i, 0)
                x1_i, y1_i = min(x1_i, W), min(y1_i, H)
                if x1_i <= x0_i or y1_i <= y0_i:
                    continue
                Mt = cv2.getAffineTransform(s, d - np.array([x0_i, y0_i], dtype=np.float32))
                patch = cv2.warpAffine(t_img, Mt, (x1_i - x0_i, y1_i - y0_i), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)

                # Exact pixel-center barycentric coverage (zero double-drawn shared edges!)
                py, px = np.mgrid[y0_i:y1_i, x0_i:x1_i].astype(np.float32) + 0.5
                ax, ay = d[0]
                bx, by = d[1]
                cx_t, cy_t = d[2]
                den = (bx - ax) * (cy_t - ay) - (by - ay) * (cx_t - ax)
                if abs(den) < 1e-4:
                    continue
                sgn = 1.0 if den > 0 else -1.0
                e0 = ((cx_t - bx) * (py - by) - (cy_t - by) * (px - bx)) * sgn
                e1 = ((ax - cx_t) * (py - cy_t) - (ay - cy_t) * (px - cx_t)) * sgn
                e2 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) * sgn
                inside = (e0 >= -1e-4) & (e1 >= -1e-4) & (e2 >= -1e-4)
                slot_rgba_buf[y0_i:y1_i, x0_i:x1_i][inside] = patch[inside]

            a_full = slot_rgba_buf[..., 3] * amul
            if is_add:
                canvas += slot_rgba_buf[..., :3] * col[None, None, :] * a_full[..., None]
            else:
                canvas = canvas * (1.0 - a_full[..., None]) + slot_rgba_buf[..., :3] * col[None, None, :] * a_full[..., None]

        return (np.clip(canvas, 0.0, 1.0) * 255.0).astype(np.uint8)
