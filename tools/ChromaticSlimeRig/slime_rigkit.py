"""slime_rigkit — shared multi-skin Spine 4.2 toolkit for Chromatic Slime rigs."""
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

sys.path.insert(0, os.getcwd())
sys.path.insert(0, str(Path(__file__).resolve().parent))
R = importlib.import_module("rigdef")
NAME = R.NAME
ORIGIN = R.ORIGIN
GLOW_OF = getattr(R, "GLOW_OF", {})
CHAINS = getattr(R, "CHAINS", {})
SKINS = getattr(R, "SKINS", None)  # e.g. ["crimson", "amber", "azure"]
SHARED_SLOTS = set(getattr(R, "SHARED_SLOTS", {"shadow"}))
FPS = 60


def S(p):
    return (round(p[0] - ORIGIN[0], 2), round(ORIGIN[1] - p[1], 2))


from rigutil import *  # noqa: F401,F403


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
            r, dx, dy, sx, sy = pose.get(n, (0, 0, 0, 1, 1))
            lx, ly = self.local[n]
            c, s = math.cos(math.radians(r)), math.sin(math.radians(r))
            M = np.array([[c * sx, -s * sy, lx + dx], [s * sx, c * sy, ly + dy], [0, 0, 1]])
            W[n] = M if b["parent"] is None else W[b["parent"]] @ M
        return W


def ang(v):
    return math.atan2(v[1], v[0])


def wrap(x):
    return (x + 180) % 360 - 180


def seg_dist(p, a, b):
    a = np.array(a, float)
    b = np.array(b, float)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-6), 0, 1)
    return np.hypot(*(p - (a + t[:, None] * ab)).T), t


def _part_key(slot: str, skin: str | None) -> str:
    if skin is None or slot in SHARED_SLOTS:
        return slot
    return f"{skin}_{slot}"


def build():
    os.makedirs("out", exist_ok=True)
    meta = json.load(open("parts/meta.json"))
    bones = bone_list()
    bidx = {b["name"]: i for i, b in enumerate(bones)}
    bpos = {b["name"]: b["pos"] for b in bones}
    GS = getattr(R, "GLOW_SCALE", 0.5)
    CLIPS = getattr(R, "CLIPS", {})

    slot_list = []
    for n in R.ORDER:
        slot_list.append(n)
        for g, base in GLOW_OF.items():
            if base == n:
                slot_list.append(g)
    slot_list += [g for g in GLOW_OF if g not in slot_list]

    skin_names = list(SKINS) if SKINS else ["default"]
    imgs = {}
    for sk_name in skin_names:
        for n in slot_list:
            if n in CLIPS:
                continue
            pk = _part_key(n, None if sk_name == "default" else sk_name)
            if pk not in meta or pk in imgs:
                continue
            im = Image.open(f"parts/{pk}.png").convert("RGBA")
            if n in GLOW_OF and n not in getattr(R, "NOSCALE", ()):
                im = im.resize((max(1, round(im.width * GS)), max(1, round(im.height * GS))), Image.LANCZOS)
            imgs[pk] = im

    PAD = 2

    def pack(PW, PH):
        rects = {}
        x = y = shelf = 0
        for pk in sorted(imgs, key=lambda k: -imgs[k].height):
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
    for PW, PH in [(1024, 1024), (1024, 1536), (2048, 1024), (2048, 1536), (2048, 2048)]:
        rects = pack(PW, PH)
        if rects:
            break
    if not rects:
        raise RuntimeError("Atlas packing failed")

    page = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
    for pk, (x, y) in rects.items():
        page.paste(imgs[pk], (x, y))
    P = np.array(page).astype(np.float32)
    known = P[..., 3] > 0
    col = P[..., :3].copy()
    for _ in range(6):
        k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        s = cv2.blur(col * known[..., None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = k & ~known
        col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = k
    P[..., :3] = col
    Image.fromarray(np.clip(P, 0, 255).astype(np.uint8)).save(f"out/{NAME}.png", optimize=True)

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
    open(f"out/{NAME}.atlas", "w", newline="\n").write("\n".join(atlas) + "\n")

    SIG = getattr(R, "SIGMA", {})
    SPC = getattr(R, "SPACING", {})
    INHERIT = getattr(R, "INHERIT", {})

    def grid_mesh(pk: str, spacing: int):
        al = np.array(Image.open(f"parts/{pk}.png"))[..., 3]
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
                if occ[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].any():
                    a, b, c, d = V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)
                    tris += [a, b, c, a, c, d]
        verts = np.array(verts, float)
        uvs = np.stack([verts[:, 0] / w, verts[:, 1] / h], 1)
        return verts + np.array([meta[pk]["x"], meta[pk]["y"]]), uvs, tris

    def weights(slot_name, world):
        if hasattr(R, "weights"):
            return R.weights(slot_name, world)
        if slot_name in INHERIT:
            host, remap = INHERIT[slot_name]
            out = []
            for wl in weights(host, world):
                acc = {}
                for b, w in wl:
                    b = remap.get(b, b)
                    acc[b] = acc.get(b, 0) + w
                out.append(list(acc.items()))
            return out
        segs = R.SKIN[slot_name]
        if len(segs) == 1:
            return [[(segs[0][0], 1.0)] for _ in world]
        D = np.stack([seg_dist(world, a, b)[0] for _, a, b in segs], 1)
        Wt = np.exp(-(D / SIG.get(slot_name, 60.0)) ** 2) + 1e-9
        Wt[np.arange(len(world)), D.argmin(1)] += 0.35
        Wt /= Wt.sum(1, keepdims=True)
        out = []
        for i in range(len(world)):
            acc = {}
            for k in range(len(segs)):
                if Wt[i, k] > 0.02:
                    acc[segs[k][0]] = acc.get(segs[k][0], 0) + Wt[i, k]
            s = sum(acc.values())
            out.append([(b, w / s) for b, w in acc.items()])
        return out

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
        if n in CLIPS:
            host = CLIPS[n].get("host", "body")
            sl = {"name": n, "bone": R.SKIN[host][0][0], "attachment": n}
            slots.append(sl)
            continue
        base = GLOW_OF.get(n, n)
        skin_slot = getattr(R, "GLOW_SKIN", {}).get(n, base)
        sl = {"name": n, "bone": R.SKIN[skin_slot][0][0], "attachment": n}
        if n in GLOW_OF:
            sl["blend"] = "additive"
            sl["color"] = "ffffff00"
        elif n in SLOT_DEFAULT_ALPHA:
            sl["color"] = hexcol(1, 1, 1, SLOT_DEFAULT_ALPHA[n])
        slots.append(sl)

    attachments_by_skin = {}
    meshes_by_skin = {}
    for sk_name in skin_names:
        atts = {}
        ms = {}
        for n in slot_list:
            if n in CLIPS:
                cdef = CLIPS[n]
                poly = cdef["poly_by_skin"][sk_name] if "poly_by_skin" in cdef else cdef["poly"]
                if not poly:
                    continue
                poly_arr = np.asarray(poly, dtype=float)
                wts = weights(cdef.get("host", "body"), poly_arr)
                atts[n] = {
                    n: {
                        "type": "clipping",
                        "end": cdef["end"],
                        "vertexCount": len(poly),
                        "vertices": spine_vertices(poly_arr, wts),
                    }
                }
                continue
            base = GLOW_OF.get(n, n)
            pk = _part_key(n, None if sk_name == "default" else sk_name)
            if pk not in meta:
                continue
            world, uvs, tris = grid_mesh(pk, SPC.get(n, SPC.get(base, 24)))
            wts = weights(getattr(R, "GLOW_SKIN", {}).get(n, base), world)
            ms[n] = dict(world=world.tolist(), uvs=uvs.tolist(), tris=tris, wts=wts, part=pk)
            att = {
                "type": "mesh",
                "uvs": [round(float(v), 5) for v in uvs.flatten()],
                "triangles": tris,
                "vertices": spine_vertices(world, wts),
                "hull": 0,
                "width": meta[pk]["w"],
                "height": meta[pk]["h"],
            }
            if pk != n:
                att["path"] = pk
                att["name"] = pk
            atts[n] = {n: att}
        attachments_by_skin[sk_name] = atts
        meshes_by_skin[sk_name] = ms

    if SKINS:
        # 'default' carries skin[0] shared slots so Spine default fallback still renders cleanly, plus each named skin!
        skins_payload = [{"name": "default", "attachments": attachments_by_skin[skin_names[0]]}]
        for sk_name in skin_names:
            skins_payload.append({"name": sk_name, "attachments": attachments_by_skin[sk_name]})
        primary_mesh = meshes_by_skin[skin_names[0]]
    else:
        skins_payload = [{"name": "default", "attachments": attachments_by_skin["default"]}]
        primary_mesh = meshes_by_skin["default"]

    json.dump(
        dict(bones=bones, slots=slots, skins=skins_payload, meshes=meshes_by_skin, page=[PW, PH], mesh=primary_mesh),
        open("out/rig_static.json", "w"),
    )
    print(
        "page", PW, PH,
        "bones", len(bones),
        "slots", len(slots),
        "skins", skin_names,
        "verts", sum(len(m["world"]) for m in primary_mesh.values()),
        "tris", sum(len(m["tris"]) // 3 for m in primary_mesh.values()),
    )


def load_rig():
    rig = json.load(open("out/rig_static.json"))
    return rig, Skel(rig["bones"])


def simulate(sk, A, name, dur, loop, init=None):
    n = int(math.ceil(dur * FPS - 1e-9))
    sub = 4
    dt = 1 / (FPS * sub)
    warm = 2 if loop else 0
    total = (warm + 1) * n * sub if loop else n * sub
    cb = [b for c in CHAINS.values() for b in c["bones"]]
    state = {b: (list(init[b]) if init and b in init else [0.0, 0.0]) for b in cb}
    prev = {}
    out = []
    post = getattr(R, "post", None)
    for k in range(total + 1):
        tt = (k * dt) % dur if loop else min(k * dt, dur)
        f = A.FUNCS[name](tt)
        P = Pose(f.P)
        if CHAINS:
            W = sk.world(P)
            for cname, c in CHAINS.items():
                b0 = c["bones"][0]
                host = sk.parent[b0]
                M = W[host]
                base = (M @ np.array([*sk.local[b0], 1.0]))[:2]
                hang = math.degrees(ang(M[:2, 0]))
                d = M[:2, :2] @ np.array(sk.local[c["bones"][1]] if len(c["bones"]) > 1 else (1.0, 0.0))
                d = d / (np.linalg.norm(d) + 1e-9)
                if cname in prev:
                    pb, pv, ph, pw = prev[cname]
                    vel = (base - pb) / dt
                    acc = (vel - pv) / dt
                    om_h = wrap(hang - ph) / dt
                    al_h = (om_h - pw) / dt
                else:
                    vel = np.zeros(2)
                    acc = np.zeros(2)
                    om_h = 0.0
                    al_h = 0.0
                if k == 0:
                    acc = np.zeros(2)
                    al_h = 0.0
                prev[cname] = (base, vel, hang, om_h)
                cross = d[0] * acc[1] - d[1] * acc[0]
                for j, b in enumerate(c["bones"]):
                    w0 = TAU * c["freq"] * (1 - c.get("taper", 0.06) * j)
                    th, om = state[b]
                    tgt = f.bias.get(b, 0.0)
                    a = (
                        -w0 * w0 * (th - tgt)
                        - 2 * c["zeta"] * w0 * om
                        - c["gain"] * (1 + 0.6 * j) * cross
                        - c.get("rgain", 0.0) * (1 + 0.4 * j) * al_h
                    )
                    om += a * dt
                    th += om * dt
                    th = max(-c["clamp"], min(c["clamp"], th))
                    state[b] = [th, om]
        if k % sub == 0 and (not loop or k >= warm * n * sub):
            for b in cb:
                r0 = P.get(b, (0, 0, 0, 1, 1))
                P[b] = (r0[0] + state[b][0] * f.extra.get("spring_mix", 1.0),) + tuple(r0[1:])
            if post:
                post(P, sk.world(P), f, sk)
            t_out = tt if not loop else (k - warm * n * sub) * dt
            out.append(
                {
                    "t": round(t_out, 5),
                    "pose": dict(P),
                    "glow": {g: clamp(f.glow.get(g, 0.0)) for g in GLOW_OF},
                    "tint": tuple(f.tint),
                    "slot_alpha": dict(f.extra.get("slot_alpha", {})),
                }
            )
    out = out[: n + 1]
    if loop:
        out[-1] = dict(out[0], t=round(dur, 5))
    return out, {b: tuple(state[b]) for b in state}


def reduce_keys(times, vals, eps):
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


def hexcol(r, g, b, a):
    return "".join(f"{int(round(clamp(v) * 255)):02x}" for v in (r, g, b, a))


def build_anim(sk, rig, frames, glow_alpha):
    times = [f["t"] for f in frames]
    bones = {}
    for b in sk.idx:
        rot = [(f["pose"].get(b, (0, 0, 0, 1, 1))[0],) for f in frames]
        tr = [tuple(f["pose"].get(b, (0, 0, 0, 1, 1))[1:3]) for f in frames]
        sc = [tuple(f["pose"].get(b, (0, 0, 0, 1, 1))[3:5]) for f in frames]
        tl = {}
        if any(abs(v[0]) > 1e-3 for v in rot):
            ks = reduce_keys(times, rot, (0.05,))
            tl["rotate"] = [{"time": round(times[k], 4), "value": round(rot[k][0], 3)} for k in ks]
        if any(abs(v[0]) > 1e-3 or abs(v[1]) > 1e-3 for v in tr):
            ks = reduce_keys(times, tr, (0.06, 0.06))
            tl["translate"] = [{"time": round(times[k], 4), "x": round(tr[k][0], 2), "y": round(tr[k][1], 2)} for k in ks]
        if any(abs(v[0] - 1) > 1e-4 or abs(v[1] - 1) > 1e-4 for v in sc):
            ks = reduce_keys(times, sc, (0.0006, 0.0006))
            tl["scale"] = [{"time": round(times[k], 4), "x": round(sc[k][0], 4), "y": round(sc[k][1], 4)} for k in ks]
        if tl:
            for arr in tl.values():
                if arr[0]["time"] == 0:
                    del arr[0]["time"]
            bones[b] = tl
    slots = {}
    CLIPS = getattr(R, "CLIPS", {})
    SLOT_DEFAULT_ALPHA = getattr(R, "SLOT_DEFAULT_ALPHA", {})
    for g in GLOW_OF:
        vals = [(glow_alpha * f["glow"][g],) for f in frames]
        ks = reduce_keys(times, vals, (0.01,))
        slots[g] = {"rgba": [{"time": round(times[k], 4), "color": hexcol(1, 1, 1, vals[k][0])} for k in ks]}
    has_tint = any(tuple(f["tint"]) != (1.0, 1.0, 1.0, 1.0) for f in frames)
    has_sa = any(bool(f.get("slot_alpha")) for f in frames)
    if has_tint or has_sa:
        for s in rig["slots"]:
            sn = s["name"]
            if sn in GLOW_OF or sn in CLIPS:
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
                ks = reduce_keys(times, vals, (0.004,) * 4)
                slots[sn] = {"rgba": [{"time": round(times[k], 4), "color": hexcol(*vals[k])} for k in ks]}
    for sl in slots.values():
        for arr in sl.values():
            if arr and arr[0].get("time") == 0:
                del arr[0]["time"]
    return {"bones": bones, "slots": slots}


def export():
    rig, sk = load_rig()
    A = importlib.import_module("anims")
    ga = getattr(R, "GLOW_ALPHA", 0.85)
    anim_json = {}
    allframes = {}
    idle, idle_state = simulate(sk, A, "idle_loop", *A.ANIMS["idle_loop"])
    allframes["idle_loop"] = idle
    anim_json["idle_loop"] = build_anim(sk, rig, idle, ga)
    for name, (dur, loop) in A.ANIMS.items():
        if name == "idle_loop":
            continue
        cold = getattr(A, "COLD_START", ())
        fr, _ = simulate(sk, A, name, dur, loop, init=idle_state if name not in cold else None)
        allframes[name] = fr
        anim_json[name] = build_anim(sk, rig, fr, ga)
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
    json.dump(skel, open(f"out/{NAME}.json", "w"), separators=(",", ":"))
    pickle.dump(allframes, open("out/frames.pkl", "wb"))
    print("ok", {k: len(v) for k, v in allframes.items()})


class Renderer:
    def __init__(self, skin=None):
        self.rig, self.sk = load_rig()
        meta = json.load(open("parts/meta.json"))
        self.tex = {n: np.array(Image.open(f"parts/{n}.png").convert("RGBA")).astype(np.float32) / 255 for n in meta}
        bpos = {b["name"]: b["pos"] for b in self.rig["bones"]}
        use_skin = skin or (SKINS[0] if SKINS else "default")
        self.mesh = self.rig["meshes"].get(use_skin, self.rig["mesh"])
        self.binds = {
            n: [[(b, np.array(S(p)) - np.array(S(bpos[b])), w) for b, w in wl] for p, wl in zip(m["world"], m["wts"])]
            for n, m in self.mesh.items()
        }
        self.ga = getattr(R, "GLOW_ALPHA", 0.85)

    def render(self, frame, scale=0.5, W=800, H=600, ox=400, oy=560, bg=(32, 36, 44)):
        Wm = self.sk.world(frame["pose"])
        canvas = np.zeros((H, W, 3), np.float32)
        canvas[...] = np.array(bg) / 255
        cv2.line(canvas, (0, oy), (W, oy), (0.3, 0.3, 0.36), 1)
        tint = frame["tint"] if len(frame["tint"]) == 4 else tuple(frame["tint"]) + (1.0,)
        for sl in self.rig["slots"]:
            n = sl["name"]
            if n not in self.mesh:
                continue
            m = self.mesh[n]
            add = n in GLOW_OF
            def_a = getattr(R, "SLOT_DEFAULT_ALPHA", {}).get(n, 1.0)
            sa = frame.get("slot_alpha", {}).get(n, def_a)
            amul = self.ga * frame["glow"].get(n, 0) if add else tint[3] * sa
            col = np.ones(3, np.float32) if add else np.array(tint[:3], np.float32)
            if amul < 0.01:
                continue
            pts = np.array([sum(w * (Wm[b] @ np.array([l[0], l[1], 1]))[:2] for b, l, w in bl) for bl in self.binds[n]])
            scr = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], 1)
            t = self.tex[m["part"]]
            th, tw = t.shape[:2]
            uv = np.array(m["uvs"]) * np.array([tw, th])
            for tri in np.array(m["tris"]).reshape(-1, 3):
                d = scr[tri].astype(np.float32)
                s = uv[tri].astype(np.float32)
                x0, y0 = np.floor(d.min(0)).astype(int)
                x1, y1 = np.ceil(d.max(0)).astype(int) + 1
                x0 = max(x0, 0)
                y0 = max(y0, 0)
                x1 = min(x1, W)
                y1 = min(y1, H)
                if x1 <= x0 or y1 <= y0:
                    continue
                Mt = cv2.getAffineTransform(s, d - np.array([x0, y0], np.float32))
                patch = cv2.warpAffine(t, Mt, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
                mask = np.zeros((y1 - y0, x1 - x0), np.float32)
                cv2.fillConvexPoly(mask, np.round(d - np.array([x0, y0])).astype(np.int32), 1.0, lineType=cv2.LINE_8)
                a = patch[..., 3] * mask * amul
                reg = canvas[y0:y1, x0:x1]
                if add:
                    reg += patch[..., :3] * a[..., None]
                else:
                    reg[...] = reg * (1 - a[..., None]) + patch[..., :3] * col * a[..., None]
        return (np.clip(canvas, 0, 1) * 255).astype(np.uint8)


def render_cli(args):
    name = args[0]
    mode = args[1]
    tag = args[2] if len(args) > 2 else "x"
    skin = args[3] if len(args) > 3 else None
    rr = Renderer(skin=skin)
    frames = pickle.load(open("out/frames.pkl", "rb"))
    V = getattr(R, "VIEW", dict(scale=0.5, W=800, H=600, ox=400, oy=560))
    os.makedirs("prev", exist_ok=True)
    suffix = f"_{skin}" if skin else ""
    if mode == "gif":
        g = dict(V)
        g.update(scale=V["scale"] * 0.7, W=int(V["W"] * 0.7), H=int(V["H"] * 0.7), ox=int(V["ox"] * 0.7), oy=int(V["oy"] * 0.7))
        ims = [Image.fromarray(rr.render(f, **g)).quantize(128) for f in frames[name][::2]]
        ims[0].save(f"prev/{name}_{tag}{suffix}.gif", save_all=True, append_images=ims[1:], duration=67, loop=0)
        print(f"prev/{name}_{tag}{suffix}.gif")
        return
    fr = frames[name]
    idxs = [int(x) for x in mode.split(",")] if mode != "auto" else [round(k * (len(fr) - 1) / 5) for k in range(6)]
    ims = [rr.render(fr[i], **V) for i in idxs]
    cols = 3
    h, w = ims[0].shape[:2]
    rows = (len(ims) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 3), np.uint8)
    for k, im in enumerate(ims):
        cv2.putText(im, f"{name} f{idxs[k]} t={fr[idxs[k]]['t']:.2f}", (10, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        sheet[(k // cols) * h : (k // cols + 1) * h, (k % cols) * w : (k % cols + 1) * w] = im
    fn = f"prev/{name}_{tag}{suffix}.jpg"
    Image.fromarray(sheet).save(fn, quality=85)
    print(fn)


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "build":
        build()
    elif cmd == "export":
        export()
    elif cmd == "render":
        render_cli(sys.argv[2:])
