"""soul_roes_rigkit — Spine 4.2 atlas/mesh builder, 60 FPS simulator, exporter, and preview renderer."""
import importlib
import json
import math
import os
import pickle
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

R = importlib.import_module("rigdef")
NAME = R.NAME
ORIGIN = R.ORIGIN
GLOW_OF = getattr(R, "GLOW_OF", {})
FPS = 60


def S(p):
    return (round(p[0] - ORIGIN[0], 2), round(ORIGIN[1] - p[1], 2))


from rigutil import *  # noqa: E402,F401,F403


def bone_list():
    return [dict(name=n, parent=p, pos=tuple(pos)) for n, p, pos in R.BONES]


class Skel:
    def __init__(self, bones):
        self.bones = bones
        self.idx = {b["name"]: i for i, b in enumerate(bones)}
        self.setup = {b["name"]: S(b["pos"]) for b in bones}
        self.parent = {b["name"]: b["parent"] for b in bones}
        self.local = {}
        for b in bones:
            p = b["parent"]
            sp = self.setup[b["name"]]
            self.local[b["name"]] = sp if p is None else (sp[0] - self.setup[p][0], sp[1] - self.setup[p][1])

    def world(self, pose):
        W = {}
        for b in self.bones:
            n = b["name"]
            r, dx, dy, sx, sy = pose.get(n, (0.0, 0.0, 0.0, 1.0, 1.0))
            lx, ly = self.local[n]
            c, s = math.cos(math.radians(r)), math.sin(math.radians(r))
            M = np.array([[c * sx, -s * sy, lx + dx], [s * sx, c * sy, ly + dy], [0.0, 0.0, 1.0]])
            W[n] = M if b["parent"] is None else W[b["parent"]] @ M
        return W


def hexcol(r, g, b, a):
    return "".join(f"{int(round(clamp(v) * 255)):02x}" for v in (r, g, b, a))


def build():
    out_dir = HERE / "out"
    out_dir.mkdir(exist_ok=True)
    meta = json.loads((HERE / "parts" / "meta.json").read_text(encoding="utf-8"))
    bones = bone_list()
    bidx = {b["name"]: i for i, b in enumerate(bones)}
    bpos = {b["name"]: b["pos"] for b in bones}
    GS = getattr(R, "GLOW_SCALE", 0.65)

    slot_list = []
    for n in R.ORDER:
        slot_list.append(n)
        for g, base in GLOW_OF.items():
            if base == n and g not in ("beckon_aura", "beckon_ring", "intangible_aura", "intangible_shimmer"):
                slot_list.append(g)
    for g in ("beckon_aura", "beckon_ring", "intangible_aura", "intangible_shimmer"):
        if g in GLOW_OF and g not in slot_list:
            slot_list.append(g)
    slot_list += [g for g in GLOW_OF if g not in slot_list]

    imgs = {}
    for n in slot_list:
        if n not in meta:
            continue
        im = Image.open(HERE / "parts" / f"{n}.png").convert("RGBA")
        if n in GLOW_OF:
            im = im.resize((max(1, round(im.width * GS)), max(1, round(im.height * GS))), Image.LANCZOS)
        imgs[n] = im

    PAD = 3

    def pack(PW, PH):
        rects = {}
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
    for PW, PH in [(1024, 1024), (1024, 1536), (1536, 1024), (2048, 1024), (2048, 1536), (2048, 2048)]:
        rects = pack(PW, PH)
        if rects:
            break
    if not rects:
        raise RuntimeError("Atlas packing failed")

    page = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
    for pk, (x, y) in rects.items():
        page.paste(imgs[pk], (x, y))

    # 6-pass RGB edge dilation on transparent pixels across entire packed atlas page
    P = np.array(page).astype(np.float32)
    known = P[..., 3] > 0
    col = P[..., :3].copy()
    k3 = np.ones((3, 3), np.uint8)
    for _ in range(6):
        k = cv2.dilate(known.astype(np.uint8), k3) > 0
        s = cv2.blur(col * known[..., None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = k & ~known
        col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = k
    P[..., :3] = col
    Image.fromarray(np.clip(P, 0, 255).astype(np.uint8)).save(out_dir / f"{NAME}.png", optimize=True)

    atlas = [f"{NAME}.png", f"size: {PW},{PH}", "format: RGBA8888", "filter: Linear,Linear", "repeat: none"]
    for pk, (x, y) in rects.items():
        w, h = imgs[pk].size
        atlas += [
            pk,
            "  rotate: false",
            f"  xy: {x}, {y}",
            f"  size: {w}, {h}",
            f"  orig: {w}, {h}",
            "  offset: 0, 0",
            "  index: -1",
        ]
    (out_dir / f"{NAME}.atlas").write_text("\n".join(atlas) + "\n", encoding="utf-8", newline="\n")

    SPC = getattr(R, "SPACING", {})

    def grid_mesh(pk: str, spacing: int):
        al = np.array(Image.open(HERE / "parts" / f"{pk}.png"))[..., 3]
        h, w = al.shape
        xs = sorted(set([int(v) for v in np.arange(0, w, spacing)] + [w]))
        ys = sorted(set([int(v) for v in np.arange(0, h, spacing)] + [h]))
        occ = cv2.dilate((al > 0).astype(np.uint8), np.ones((3, 3), np.uint8))
        vid = {}
        verts = []
        tris = []

        def V(i, j):
            if (i, j) not in vid:
                vid[(i, j)] = len(verts)
                verts.append((xs[i], ys[j]))
            return vid[(i, j)]

        for j in range(len(ys) - 1):
            for i in range(len(xs) - 1):
                if occ[ys[j] : ys[j + 1], xs[i] : xs[i + 1]].any():
                    a, b, c, d = V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)
                    tris += [a, b, c, a, c, d]
        verts = np.array(verts, float)
        uvs = np.stack([verts[:, 0] / w, verts[:, 1] / h], 1)
        return verts + np.array([meta[pk]["x"], meta[pk]["y"]]), uvs, tris

    def spine_vertices(world, wts):
        out = []
        for p, wl in zip(world, wts):
            sp = S(p)
            out.append(len(wl))
            for b, w in wl:
                bp = S(bpos[b])
                out += [bidx[b], round(sp[0] - bp[0], 2), round(sp[1] - bp[1], 2), round(w, 4)]
        return out

    slots = []
    SLOT_DEFAULT_ALPHA = getattr(R, "SLOT_DEFAULT_ALPHA", {})
    for n in slot_list:
        base = GLOW_OF.get(n, n)
        skin_slot = getattr(R, "GLOW_SKIN", {}).get(n, base)
        sl = {"name": n, "bone": R.SKIN[skin_slot][0][0], "attachment": n}
        if n in GLOW_OF:
            sl["blend"] = "additive"
            sl["color"] = "ffffff00"
        elif n in SLOT_DEFAULT_ALPHA:
            sl["color"] = hexcol(1.0, 1.0, 1.0, SLOT_DEFAULT_ALPHA[n])
        slots.append(sl)

    atts = {}
    ms = {}
    for n in slot_list:
        if n not in meta:
            continue
        base = GLOW_OF.get(n, n)
        skin_slot = getattr(R, "GLOW_SKIN", {}).get(n, base)
        world, uvs, tris = grid_mesh(n, SPC.get(n, SPC.get(base, 32)))
        wts = R.weights(skin_slot, world)
        ms[n] = dict(world=world.tolist(), uvs=uvs.tolist(), tris=tris, wts=wts, part=n)
        atts[n] = {
            n: {
                "type": "mesh",
                "uvs": [round(float(v), 5) for v in uvs.flatten()],
                "triangles": tris,
                "vertices": spine_vertices(world, wts),
                "hull": 0,
                "width": meta[n]["w"],
                "height": meta[n]["h"],
            }
        }

    skins_payload = [{"name": "default", "attachments": atts}]
    (out_dir / "rig_static.json").write_text(
        json.dumps(
            dict(bones=bones, slots=slots, skins=skins_payload, meshes={"default": ms}, page=[PW, PH], mesh=ms)
        ),
        encoding="utf-8",
    )
    print(
        f"Built atlas {PW}x{PH} | bones={len(bones)} | slots={len(slots)} | "
        f"verts={sum(len(m['world']) for m in ms.values())} | tris={sum(len(m['tris']) // 3 for m in ms.values())}"
    )


def load_rig():
    rig = json.loads((HERE / "out" / "rig_static.json").read_text(encoding="utf-8"))
    return rig, Skel(rig["bones"])


def sample_times(dur: float, event_times: list[float]) -> tuple[list[float], set[int]]:
    """Generate 60 FPS sample grid merged with exact event timestamps and exact clip end duration."""
    pts = set()
    k = 0
    while True:
        t = round(k / FPS, 5)
        if t >= dur - 1e-4:
            break
        pts.add(t)
        k += 1
    pts.add(0.0)
    pts.add(round(dur, 4))
    ev_rounded = {round(et, 4) for et in event_times if 0.0 <= et <= dur}
    # Remove any 60fps sample within 2ms of an exact event timestamp so timeline slopes stay ultra-clean
    cleaned = {t for t in pts if all(abs(t - et) > 0.0025 or abs(t - et) < 1e-6 for et in ev_rounded)}
    cleaned |= ev_rounded
    cleaned.add(0.0)
    cleaned.add(round(dur, 4))
    ts = sorted(cleaned)
    pinned = {0, len(ts) - 1}
    for i, t in enumerate(ts):
        if round(t, 4) in ev_rounded:
            pinned.add(i)
    return ts, pinned


def simulate(A, name: str, dur: float, loop: bool):
    ev_times = [et for _, et in getattr(A, "EVENTS", {}).get(name, [])]
    ts, pinned = sample_times(dur, ev_times)
    out = []
    for t in ts:
        f = A.FUNCS[name](t)
        out.append(
            {
                "t": round(t, 4),
                "pose": dict(f.P),
                "glow": {g: clamp(f.glow.get(g, 0.0)) for g in GLOW_OF},
                "tint": tuple(f.tint),
                "slot_alpha": dict(f.extra.get("slot_alpha", {})),
            }
        )
    if loop:
        out[-1] = dict(out[0], t=round(dur, 4))
    return out, pinned


def reduce_keys(times: list[float], vals: list[tuple], eps: tuple, pinned: set[int]) -> list[int]:
    keep = [0]
    i = 0
    N = len(times)
    while i < N - 1:
        j = i + 2
        while j < N:
            if (j - 1) in pinned:
                break
            ok = True
            t0, t1 = times[i], times[j]
            for m in range(i + 1, j):
                u = (times[m] - t0) / max(t1 - t0, 1e-9)
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


def build_anim(sk, rig, frames, pinned: set[int], glow_alpha: float):
    times = [f["t"] for f in frames]
    bones = {}
    for b in sk.idx:
        rot = [(f["pose"].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[0],) for f in frames]
        tr = [tuple(f["pose"].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[1:3]) for f in frames]
        sc = [tuple(f["pose"].get(b, (0.0, 0.0, 0.0, 1.0, 1.0))[3:5]) for f in frames]
        tl = {}
        if any(abs(v[0]) > 1e-4 for v in rot):
            ks = reduce_keys(times, rot, (0.04,), pinned)
            tl["rotate"] = [{"time": round(times[k], 4), "value": round(rot[k][0], 3)} for k in ks]
        if any(abs(v[0]) > 1e-3 or abs(v[1]) > 1e-3 for v in tr):
            ks = reduce_keys(times, tr, (0.05, 0.05), pinned)
            tl["translate"] = [{"time": round(times[k], 4), "x": round(tr[k][0], 2), "y": round(tr[k][1], 2)} for k in ks]
        if any(abs(v[0] - 1.0) > 1e-5 or abs(v[1] - 1.0) > 1e-5 for v in sc):
            ks = reduce_keys(times, sc, (0.0005, 0.0005), pinned)
            tl["scale"] = [{"time": round(times[k], 4), "x": round(sc[k][0], 4), "y": round(sc[k][1], 4)} for k in ks]
        if tl:
            for arr in tl.values():
                if arr[0]["time"] == 0:
                    del arr[0]["time"]
            bones[b] = tl

    slots = {}
    SLOT_DEFAULT_ALPHA = getattr(R, "SLOT_DEFAULT_ALPHA", {})
    for g in GLOW_OF:
        vals = [(glow_alpha * f["glow"][g],) for f in frames]
        if any(abs(v[0]) > 1e-3 for v in vals):
            ks = reduce_keys(times, vals, (0.008,), pinned)
            slots[g] = {"rgba": [{"time": round(times[k], 4), "color": hexcol(1.0, 1.0, 1.0, vals[k][0])} for k in ks]}

    has_tint = any(tuple(f["tint"]) != (1.0, 1.0, 1.0, 1.0) for f in frames)
    has_sa = any(bool(f.get("slot_alpha")) for f in frames)
    if has_tint or has_sa:
        for s in rig["slots"]:
            sn = s["name"]
            if sn in GLOW_OF:
                continue
            def_a = SLOT_DEFAULT_ALPHA.get(sn, 1.0)
            vals = []
            diff = False
            for f in frames:
                r0, g0, b0, a0 = f["tint"]
                sa = f.get("slot_alpha", {}).get(sn, def_a)
                fa = a0 * sa
                if abs(r0 - 1.0) > 1e-3 or abs(g0 - 1.0) > 1e-3 or abs(b0 - 1.0) > 1e-3 or abs(fa - def_a) > 1e-3:
                    diff = True
                vals.append((r0, g0, b0, fa))
            if diff:
                ks = reduce_keys(times, vals, (0.004,) * 4, pinned)
                slots[sn] = {"rgba": [{"time": round(times[k], 4), "color": hexcol(*vals[k])} for k in ks]}

    for sl in slots.values():
        for arr in sl.values():
            if arr and arr[0].get("time") == 0:
                del arr[0]["time"]
    return {"bones": bones, "slots": slots}


def export():
    out_dir = HERE / "out"
    rig, sk = load_rig()
    A = importlib.import_module("anims")
    ga = getattr(R, "GLOW_ALPHA", 1.0)
    anim_json = {}
    allframes = {}
    for name, (dur, loop) in A.ANIMS.items():
        fr, pinned = simulate(A, name, dur, loop)
        allframes[name] = fr
        anim_json[name] = build_anim(sk, rig, fr, pinned, ga)

    bones = []
    for b in rig["bones"]:
        e = {"name": b["name"]}
        if b["parent"]:
            e["parent"] = b["parent"]
        lx, ly = sk.local[b["name"]]
        if abs(lx) > 1e-6:
            e["x"] = round(lx, 2)
        if abs(ly) > 1e-6:
            e["y"] = round(ly, 2)
        bones.append(e)

    allw = np.array([S(p) for ms in rig["meshes"].values() for m in ms.values() for p in m["world"]])
    x0, y0 = allw.min(0)
    x1, y1 = allw.max(0)
    skel = {
        "skeleton": {
            "hash": NAME.replace("_", "") + "1",
            "spine": "4.2.0",
            "x": round(float(x0), 2),
            "y": round(float(y0), 2),
            "width": round(float(x1 - x0), 2),
            "height": round(float(y1 - y0), 2),
            "images": "./",
            "audio": "",
        },
        "bones": bones,
        "slots": rig["slots"],
        "skins": rig["skins"],
        "animations": anim_json,
    }
    (out_dir / f"{NAME}.json").write_text(json.dumps(skel, separators=(",", ":")), encoding="utf-8", newline="\n")
    with open(out_dir / "frames.pkl", "wb") as fp:
        pickle.dump(allframes, fp)
    print("Exported animations:", {k: len(v) for k, v in allframes.items()})


class Renderer:
    def __init__(self):
        self.rig, self.sk = load_rig()
        meta = json.loads((HERE / "parts" / "meta.json").read_text(encoding="utf-8"))
        self.tex = {
            n: np.array(Image.open(HERE / "parts" / f"{n}.png").convert("RGBA")).astype(np.float32) / 255.0
            for n in meta
        }
        bpos = {b["name"]: b["pos"] for b in self.rig["bones"]}
        self.mesh = self.rig["mesh"]
        self.binds = {
            n: [[(b, np.array(S(p)) - np.array(S(bpos[b])), w) for b, w in wl] for p, wl in zip(m["world"], m["wts"])]
            for n, m in self.mesh.items()
        }
        self.ga = getattr(R, "GLOW_ALPHA", 1.0)

    def render(self, frame, scale=0.42, W=620, H=320, ox=425, oy=248, bg=(22, 28, 36)):
        Wm = self.sk.world(frame["pose"])
        canvas = np.zeros((H, W, 3), np.float32)
        # Subtle deep underwater gradient matching Underdocks / dark STS2 combat stage
        yy = np.linspace(0.0, 1.0, H, dtype=np.float32)[:, None, None]
        top_c = np.array(bg, np.float32) / 255.0
        bot_c = np.clip(top_c * 0.72, 0.0, 1.0)
        canvas[...] = top_c * (1.0 - yy) + bot_c * yy
        cv2.line(canvas, (0, oy), (W, oy), (0.22, 0.27, 0.33), 1)
        cv2.line(canvas, (ox, max(0, oy - 14)), (ox, min(H - 1, oy + 14)), (0.28, 0.34, 0.40), 1)

        tint = frame["tint"] if len(frame["tint"]) == 4 else tuple(frame["tint"]) + (1.0,)
        for sl in self.rig["slots"]:
            n = sl["name"]
            if n not in self.mesh:
                continue
            m = self.mesh[n]
            add = n in GLOW_OF
            def_a = getattr(R, "SLOT_DEFAULT_ALPHA", {}).get(n, 1.0)
            sa = frame.get("slot_alpha", {}).get(n, def_a)
            amul = self.ga * frame["glow"].get(n, 0.0) if add else tint[3] * sa
            col = np.ones(3, np.float32) if add else np.array(tint[:3], np.float32)
            if amul < 0.008:
                continue
            pts = np.array(
                [sum(w * (Wm[b] @ np.array([l[0], l[1], 1.0]))[:2] for b, l, w in bl) for bl in self.binds[n]]
            )
            scr = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], 1)
            t = self.tex[m["part"]]
            th, tw = t.shape[:2]
            uv = np.array(m["uvs"]) * np.array([tw, th])

            # Single-pass per-slot compositing buffer so shared mesh triangle edges NEVER double-accumulate!
            bx0, by0 = np.floor(scr.min(0)).astype(int)
            bx1, by1 = np.ceil(scr.max(0)).astype(int) + 1
            bx0, by0 = max(bx0, 0), max(by0, 0)
            bx1, by1 = min(bx1, W), min(by1, H)
            if bx1 <= bx0 or by1 <= by0:
                continue
            bw, bh = bx1 - bx0, by1 - by0
            slot_rgba = np.zeros((bh, bw, 4), np.float32)
            slot_cov = np.zeros((bh, bw), np.uint8)

            for tri in np.array(m["tris"]).reshape(-1, 3):
                d = scr[tri].astype(np.float32)
                s = uv[tri].astype(np.float32)
                x0, y0 = np.floor(d.min(0)).astype(int)
                x1, y1 = np.ceil(d.max(0)).astype(int) + 1
                x0, y0 = max(x0, bx0), max(y0, by0)
                x1, y1 = min(x1, bx1), min(y1, by1)
                if x1 <= x0 or y1 <= y0:
                    continue
                Mt = cv2.getAffineTransform(s, d - np.array([x0, y0], np.float32))
                patch = cv2.warpAffine(t, Mt, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
                tmask = np.zeros((y1 - y0, x1 - x0), np.uint8)
                cv2.fillConvexPoly(tmask, np.round(d - np.array([x0, y0])).astype(np.int32), 1, lineType=cv2.LINE_8)
                cov_view = slot_cov[y0 - by0 : y1 - by0, x0 - bx0 : x1 - bx0]
                write = (tmask > 0) & (cov_view == 0)
                if write.any():
                    cov_view[write] = 1
                    slot_rgba[y0 - by0 : y1 - by0, x0 - bx0 : x1 - bx0][write] = patch[write]

            a = slot_rgba[..., 3] * amul
            reg = canvas[by0:by1, bx0:bx1]
            if add:
                reg += slot_rgba[..., :3] * a[..., None]
            else:
                reg[...] = reg * (1.0 - a[..., None]) + slot_rgba[..., :3] * col * a[..., None]
        return (np.clip(canvas, 0.0, 1.0) * 255.0).astype(np.uint8)


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "build":
        build()
    elif cmd == "export":
        export()
