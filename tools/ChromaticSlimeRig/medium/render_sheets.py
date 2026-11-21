"""Hardware-CLIPS-accurate Spine 4.2 contact-sheet renderer for Medium Chromatic Slime (medium/prev/*.jpg)."""
from __future__ import annotations

import json
import pickle
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import rigdef as R
import slime_rigkit as K


class ClippedRenderer(K.Renderer):
    """Extends slime_rigkit.Renderer with exact Spine 4.2 ClippingAttachment polygon rasterization."""

    def __init__(self, skin: str):
        super().__init__(skin=skin)
        self.skin_name = skin
        bpos = {b["name"]: b["pos"] for b in self.rig["bones"]}
        self.clips_def = getattr(R, "CLIPS", {})
        self.clip_binds = {}
        for cname, cdef in self.clips_def.items():
            poly = np.asarray(cdef["poly_by_skin"][skin], dtype=np.float64)
            wts = R.weights(cdef.get("host", "body"), poly)
            self.clip_binds[cname] = [
                [(b, np.array(K.S(p)) - np.array(K.S(bpos[b])), w) for b, w in wl]
                for p, wl in zip(poly, wts)
            ]

    def render(self, frame, scale=0.92, W=760, H=540, ox=380, oy=470, bg=(32, 36, 44)):
        Wm = self.sk.world(frame["pose"])
        canvas = np.zeros((H, W, 3), np.float32)
        canvas[...] = np.array(bg) / 255.0
        cv2.line(canvas, (0, oy), (W, oy), (0.30, 0.30, 0.36), 1)
        tint = frame["tint"] if len(frame["tint"]) == 4 else tuple(frame["tint"]) + (1.0,)

        active_clip_mask: np.ndarray | None = None
        active_clip_end: str | None = None

        for sl in self.rig["slots"]:
            n = sl["name"]
            if n in self.clip_binds:
                cpts = np.array(
                    [sum(w * (Wm[b] @ np.array([l[0], l[1], 1.0]))[:2] for b, l, w in bl) for bl in self.clip_binds[n]]
                )
                cscr = np.stack([ox + cpts[:, 0] * scale, oy - cpts[:, 1] * scale], 1)
                cm = np.zeros((H, W), np.float32)
                cv2.fillPoly(cm, [np.round(cscr).astype(np.int32)], 1.0, lineType=cv2.LINE_AA)
                active_clip_mask = cm
                active_clip_end = self.clips_def[n]["end"]
                continue

            if n in self.mesh:
                m = self.mesh[n]
                add = n in R.GLOW_OF
                def_a = getattr(R, "SLOT_DEFAULT_ALPHA", {}).get(n, 1.0)
                sa = frame.get("slot_alpha", {}).get(n, def_a)
                amul = self.ga * frame["glow"].get(n, 0.0) if add else tint[3] * sa
                col = np.ones(3, np.float32) if add else np.array(tint[:3], np.float32)
                if amul >= 0.01:
                    pts = np.array(
                        [sum(w * (Wm[b] @ np.array([l[0], l[1], 1.0]))[:2] for b, l, w in bl) for bl in self.binds[n]]
                    )
                    scr = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], 1)
                    t = self.tex[m["part"]]
                    th, tw = t.shape[:2]
                    uv = np.array(m["uvs"]) * np.array([tw, th])
                    slot_rgb = np.zeros((H, W, 3), np.float32)
                    slot_cov = np.zeros((H, W), np.float32)
                    slot_ta = np.zeros((H, W), np.float32)
                    for tri in np.array(m["tris"]).reshape(-1, 3):
                        d = scr[tri].astype(np.float32)
                        s = uv[tri].astype(np.float32)
                        x0, y0 = np.floor(d.min(0)).astype(int)
                        x1, y1 = np.ceil(d.max(0)).astype(int) + 1
                        x0, y0 = max(x0, 0), max(y0, 0)
                        x1, y1 = min(x1, W), min(y1, H)
                        if x1 <= x0 or y1 <= y0:
                            continue
                        Mt = cv2.getAffineTransform(s, d - np.array([x0, y0], np.float32))
                        patch = cv2.warpAffine(
                            t, Mt, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT
                        )
                        mask = np.zeros((y1 - y0, x1 - x0), np.float32)
                        cv2.fillConvexPoly(mask, np.round(d - np.array([x0, y0])).astype(np.int32), 1.0, lineType=cv2.LINE_8)
                        write = mask > slot_cov[y0:y1, x0:x1]
                        slot_cov[y0:y1, x0:x1][write] = mask[write]
                        slot_rgb[y0:y1, x0:x1][write] = patch[..., :3][write]
                        slot_ta[y0:y1, x0:x1][write] = patch[..., 3][write]
                    if active_clip_mask is not None:
                        slot_cov *= active_clip_mask
                    a = slot_ta * slot_cov * amul
                    if add:
                        canvas += slot_rgb * a[..., None]
                    else:
                        canvas[:] = canvas * (1.0 - a[..., None]) + slot_rgb * col * a[..., None]

            if active_clip_end == n:
                active_clip_mask = None
                active_clip_end = None

        return (np.clip(canvas, 0.0, 1.0) * 255.0).astype(np.uint8)


def save_sheet(frames_all: dict, anim: str, skin: str, times_s: list[float], out_name: str) -> None:
    rr = ClippedRenderer(skin)
    fr = frames_all[anim]
    # Wide arena framing so both planted slime on right AND -440px flying projectiles on left are 100% visible!
    V = dict(scale=0.72, W=760, H=440, ox=535, oy=385)
    idxs = [min(len(fr) - 1, max(0, int(round(t * 60)))) for t in times_s]
    ims = []
    for idx in idxs:
        im = rr.render(fr[idx], **V)
        cv2.putText(
            im,
            f"{skin} | {anim} t={fr[idx]['t']:.2f}s (f{idx})",
            (12, 28),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.62,
            (245, 245, 250),
            2,
        )
        ims.append(im)
    cols = 3
    rows = (len(ims) + cols - 1) // cols
    h, w = ims[0].shape[:2]
    sheet = np.full((rows * h, cols * w, 3), 24, dtype=np.uint8)
    for k, im in enumerate(ims):
        r0, c0 = divmod(k, cols)
        sheet[r0 * h : (r0 + 1) * h, c0 * w : (c0 + 1) * w] = im
    out_p = HERE / "prev" / out_name
    out_p.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(sheet).save(out_p, quality=90)
    print("Saved", out_p)


def main() -> None:
    frames_all = pickle.load(open(HERE / "out" / "frames.pkl", "rb"))
    # 1. Crimson bespoke clips (showing compress -> Hit 1 harpoon+proj_1 -> reload -> Hit 2 5-shard burst+proj_2)
    save_sheet(frames_all, "attack_crimson", "crimson", [0.00, 0.16, 0.35, 0.45, 0.58, 0.65], "sheet_attack_crimson.jpg")
    save_sheet(frames_all, "power_crimson", "crimson", [0.00, 0.16, 0.34, 0.45, 0.56, 0.82], "sheet_power_crimson.jpg")
    # 2. Amber bespoke clips (showing rear+stone-lift -> -104px skid + anvil slam + shock-chips)
    save_sheet(frames_all, "attack_amber", "amber", [0.00, 0.20, 0.36, 0.44, 0.54, 0.85], "sheet_attack_amber.jpg")
    save_sheet(frames_all, "power_amber", "amber", [0.00, 0.12, 0.42, 0.52, 0.62, 0.88], "sheet_power_amber.jpg")
    # 3. Azure bespoke clips (showing planted +38px vertical geyser + dual parabolic globs, and -120/-50px tidal splat!)
    save_sheet(frames_all, "cast_azure", "azure", [0.00, 0.16, 0.44, 0.54, 0.68, 0.82], "sheet_cast_azure.jpg")
    save_sheet(frames_all, "attack_azure", "azure", [0.00, 0.18, 0.28, 0.36, 0.48, 0.80], "sheet_attack_azure.jpg")
    # 4. Shared 0-deg clips sanity check (spawn vertical teardrop -> flat splat, hurt horizontal hydro-shear)
    save_sheet(frames_all, "spawn", "crimson", [0.05, 0.22, 0.36, 0.42, 0.53, 0.65], "sheet_spawn_crimson.jpg")
    save_sheet(frames_all, "hurt", "amber", [0.00, 0.05, 0.10, 0.20, 0.30, 0.42], "sheet_hurt_amber.jpg")


if __name__ == "__main__":
    main()
