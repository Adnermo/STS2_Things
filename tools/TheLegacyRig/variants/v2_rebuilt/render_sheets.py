"""CPU Contact Sheet & Full-Resolution Peak Frame Renderer for TheLegacy v2_rebuilt."""
import json
import os
import pickle
import cv2
import numpy as np
from PIL import Image
from export import Skel
from rigdef import GLOW_ALPHA, GLOW_OF, S


class CPURenderer:
    def __init__(self):
        self.rig = json.load(open('out/rig_static.json'))
        self.sk = Skel(self.rig['bones'])
        meta = json.load(open('parts/meta.json'))
        self.tex = {
            n: np.array(Image.open(f'parts/{n}.png').convert('RGBA')).astype(np.float32) / 255.0
            for n in meta
        }
        bpos = {b['name']: b['pos'] for b in self.rig['bones']}
        self.binds = {}
        for src, m in self.rig['mesh'].items():
            binds_src = []
            for p, wl in zip(m['world'], m['wts']):
                sp = np.array(S(p), dtype=np.float64)
                binds_src.append([(b, sp - np.array(S(bpos[b]), dtype=np.float64), w) for b, w in wl])
            self.binds[src] = binds_src

    def render(self, frame: dict, scale=0.52, W=760, H=420, ox=380, oy=392, bg=(30, 34, 44), label: str = None):
        Wm = self.sk.world(frame['pose'])
        canvas = np.empty((H, W, 3), dtype=np.float32)
        canvas[...] = np.array(bg, dtype=np.float32) / 255.0

        # Wet stone dock ground reference line (y_spine = 0 -> screen oy)
        cv2.line(canvas, (0, oy), (W, oy), (0.26, 0.52, 0.36), 1)

        tint = frame['tint']
        pts_cache = {}

        for sl in self.rig['slots']:
            n = sl['name']
            src = GLOW_OF.get(n, n)
            m = self.rig['mesh'][src]
            is_add = n in GLOW_OF
            amul = GLOW_ALPHA * frame['glow'].get(n, 0.0) if is_add else float(tint[3])
            if amul < 0.008:
                continue
            col = np.ones(3, dtype=np.float32) if is_add else np.array(tint[:3], dtype=np.float32)

            if src not in pts_cache:
                pts = np.array([
                    sum(w * (Wm[b] @ np.array([loc[0], loc[1], 1.0]))[:2] for b, loc, w in bl)
                    for bl in self.binds[src]
                ])
                pts_cache[src] = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], axis=1)

            scr = pts_cache[src]
            t_im = self.tex[n]
            th, tw = t_im.shape[:2]
            uv = np.array(m['uvs'], dtype=np.float32) * np.array([tw, th], dtype=np.float32)
            tris = np.array(m['tris'], dtype=np.int32).reshape(-1, 3)

            bx0 = max(0, int(np.floor(scr[:, 0].min())))
            by0 = max(0, int(np.floor(scr[:, 1].min())))
            bx1 = min(W, int(np.ceil(scr[:, 0].max())) + 1)
            by1 = min(H, int(np.ceil(scr[:, 1].max())) + 1)
            if bx1 <= bx0 or by1 <= by0:
                continue

            bh, bw = by1 - by0, bx1 - bx0
            slot_cov = np.zeros((bh, bw), dtype=np.float32)
            slot_premul = np.zeros((bh, bw, 3), dtype=np.float32)
            slot_alpha = np.zeros((bh, bw), dtype=np.float32)

            for tri in tris:
                d = scr[tri].astype(np.float32)
                s_uv = uv[tri].astype(np.float32)
                x0, y0 = np.floor(d.min(axis=0)).astype(int)
                x1, y1 = np.ceil(d.max(axis=0)).astype(int) + 1
                x0, y0 = max(x0, bx0), max(y0, by0)
                x1, y1 = min(x1, bx1), min(y1, by1)
                if x1 <= x0 or y1 <= y0:
                    continue
                area2 = abs((d[1, 0] - d[0, 0]) * (d[2, 1] - d[0, 1]) - (d[1, 1] - d[0, 1]) * (d[2, 0] - d[0, 0]))
                if area2 < 1e-3:
                    continue
                Mt = cv2.getAffineTransform(s_uv, d - np.array([x0, y0], dtype=np.float32))
                patch = cv2.warpAffine(t_im, Mt, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
                mask = np.zeros((y1 - y0, x1 - x0), dtype=np.float32)
                cv2.fillConvexPoly(mask, np.round((d - np.array([x0, y0])) * 16.0).astype(np.int32), 1.0, lineType=cv2.LINE_AA, shift=4)
                ry0, ry1 = y0 - by0, y1 - by0
                rx0, rx1 = x0 - bx0, x1 - bx0
                pa = patch[..., 3] * mask
                slot_cov[ry0:ry1, rx0:rx1] += mask
                slot_alpha[ry0:ry1, rx0:rx1] += pa
                slot_premul[ry0:ry1, rx0:rx1] += patch[..., :3] * pa[..., None]

            norm = np.where(slot_cov > 1.0, 1.0 / np.maximum(slot_cov, 1e-6), 1.0)
            a_fin = np.clip(slot_alpha * norm * amul, 0.0, 1.0)
            premul_fin = slot_premul * (norm * amul)[..., None]
            reg = canvas[by0:by1, bx0:bx1]
            if is_add:
                reg += premul_fin
            else:
                reg[...] = reg * (1.0 - a_fin[..., None]) + premul_fin * col

        # Re-draw crisp subtle dock reference tick marks on top so ground lock is unmistakable
        out_u8 = (np.clip(canvas, 0.0, 1.0) * 255.0).astype(np.uint8)
        cv2.line(out_u8, (0, oy), (W, oy), (80, 215, 120), 1)
        if label:
            cv2.putText(out_u8, label, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.62, (245, 248, 255), 2, cv2.LINE_AA)
        return out_u8


def nearest_frame(frames: list, target_t: float) -> dict:
    return min(frames, key=lambda fr: abs(fr['t'] - target_t))


def make_sheet(rr: CPURenderer, allframes: dict, rows_spec: list, out_path: str):
    tw, th = 540, 300
    cols = max(len(ts) for _, ts in rows_spec)
    rows = len(rows_spec)
    sheet = np.full((rows * th, cols * tw, 3), 24, dtype=np.uint8)
    for r_i, (anim_name, times) in enumerate(rows_spec):
        frs = allframes[anim_name]
        for c_i, t_val in enumerate(times):
            fr = nearest_frame(frs, t_val)
            im = rr.render(fr, scale=0.365, W=tw, H=th, ox=int(tw * 0.50), oy=th - 20, label=f"{anim_name}  t={fr['t']:.2f}s")
            sheet[r_i * th:(r_i + 1) * th, c_i * tw:(c_i + 1) * tw] = im
    Image.fromarray(sheet).save(out_path, quality=90)
    print(f'Saved {out_path} ({sheet.shape[1]}x{sheet.shape[0]})')


def main():
    os.makedirs('prev', exist_ok=True)
    rr = CPURenderer()
    allframes = pickle.load(open('out/frames.pkl', 'rb'))

    # 1. Main Contact Sheet: idle_loop, attack, cast, power_up (5 key poses each)
    main_spec = [
        ('idle_loop', [0.00, 0.12, 0.25, 0.42, 0.72]),
        ('attack', [0.00, 0.25, 0.39, 0.56, 1.25]),
        ('cast', [0.06, 0.19, 0.30, 0.42, 0.90]),
        ('power_up', [0.22, 0.46, 0.56, 0.69, 1.50]),
    ]
    make_sheet(rr, allframes, main_spec, 'prev/sheet_main.jpg')

    # 2. Sub Contact Sheet: summon, hurt, die, revive (5 key poses each)
    sub_spec = [
        ('summon', [0.12, 0.24, 0.38, 0.53, 1.30]),
        ('hurt', [0.00, 0.04, 0.10, 0.26, 0.65]),
        ('die', [0.06, 0.60, 1.25, 1.85, 2.60]),
        ('revive', [0.00, 0.18, 0.56, 0.96, 1.80]),
    ]
    make_sheet(rr, allframes, sub_spec, 'prev/sheet_sub.jpg')

    # 3. Full-Resolution Peak Frames (1460 x 780, scale=1.0)
    peaks = [
        ('idle_loop', 0.43, 'prev/peak_idle_loop.jpg'),
        ('attack', 0.25, 'prev/peak_attack_coil.jpg'),
        ('attack', 0.39, 'prev/peak_attack.jpg'),
        ('cast', 0.42, 'prev/peak_cast.jpg'),
        ('power_up', 0.69, 'prev/peak_power_up.jpg'),
        ('hurt', 0.09, 'prev/peak_hurt.jpg'),
        ('die', 2.60, 'prev/peak_die.jpg'),
    ]
    for anim_name, t_val, fn in peaks:
        fr = nearest_frame(allframes[anim_name], t_val)
        im = rr.render(fr, scale=0.95, W=1420, H=750, ox=710, oy=705, label=f"{anim_name} peak  t={fr['t']:.2f}s")
        Image.fromarray(im).save(fn, quality=92)
        print(f'Saved {fn}')


if __name__ == '__main__':
    main()
