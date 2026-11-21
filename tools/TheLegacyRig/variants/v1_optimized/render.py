import json, math, os, pickle, sys, cv2, numpy as np
from PIL import Image
from fk import Skel
from rigdef import GLOW_OF, S

os.makedirs('prev', exist_ok=True)
rig = json.load(open('out/rig_static.json'))
sk = Skel(rig)
meta = json.load(open('parts/meta.json'))

tex = {}
for n in meta:
    im = np.array(Image.open(f'parts/{n}.png').convert('RGBA')).astype(np.float32) / 255.0
    tex[n] = im

bpos_map = {bb['name']: bb['pos'] for bb in rig['bones']}
bindS = {}
for src, m in rig['mesh'].items():
    world = np.array(m['world'])
    binds = []
    for p, wl in zip(world, m['wts']):
        sp = np.array(S(p))
        binds.append([(b, sp - np.array(S(bpos_map[b])), w) for b, w in wl])
    bindS[src] = binds

def render(frame, scale=0.62, W=920, H=500, ox=460, oy=465, bg=(34, 36, 46), draw_ground=True):
    Wm = sk.world(frame['pose'])
    canvas = np.zeros((H, W, 3), np.float32)
    canvas[...] = np.array(bg, np.float32) / 255.0

    # Subtle background reference grid + stone floor line behind/under character
    if draw_ground:
        gy = int(round(oy))  # Spine Y=0 (y_img=660 ground contact plane)
        if 0 <= gy < H:
            canvas[gy:min(H, gy + 2), :] = np.array([75, 82, 98], np.float32) / 255.0

    for sl in rig['slots']:
        n = sl['name']
        src = GLOW_OF.get(n, n)
        m = rig['mesh'][src]
        alpha_mul = 1.0
        col = np.array([1.0, 1.0, 1.0], np.float32)
        if n in GLOW_OF:
            alpha_mul = 0.76 * frame['glow'].get(n, 0.0)
        else:
            col = np.array(frame['tint'], np.float32)
        if alpha_mul < 0.008:
            continue

        pts = np.zeros((len(bindS[src]), 2), np.float32)
        for idx_v, bl in enumerate(bindS[src]):
            vx = 0.0
            vy = 0.0
            for b, loc, w in bl:
                M = Wm[b]
                vx += w * (M[0, 0] * loc[0] + M[0, 1] * loc[1] + M[0, 2])
                vy += w * (M[1, 0] * loc[0] + M[1, 1] * loc[1] + M[1, 2])
            pts[idx_v, 0] = vx
            pts[idx_v, 1] = vy

        scr = np.stack([ox + pts[:, 0] * scale, oy - pts[:, 1] * scale], 1)
        sx0, sy0 = np.floor(scr.min(0)).astype(int)
        sx1, sy1 = np.ceil(scr.max(0)).astype(int) + 1
        sx0 = max(sx0, 0)
        sy0 = max(sy0, 0)
        sx1 = min(sx1, W)
        sy1 = min(sy1, H)
        if sx1 <= sx0 or sy1 <= sy0:
            continue

        t = tex[n]
        th, tw = t.shape[:2]
        uv = np.array(m['uvs'], np.float32) * np.array([tw, th], np.float32)
        tris = np.array(m['tris']).reshape(-1, 3)
        add = (n in GLOW_OF)

        # Accumulate all triangles of this slot into a per-slot layer before compositing onto canvas,
        # preventing double-counted shared interior edges (bright grid in additive / dark seam in alpha-over).
        sh_s, sw_s = sy1 - sy0, sx1 - sx0
        slot_cov = np.zeros((sh_s, sw_s), np.float32)
        slot_pm = np.zeros((sh_s, sw_s, 3), np.float32)
        slot_a = np.zeros((sh_s, sw_s), np.float32)

        for tri in tris:
            d = scr[tri].astype(np.float32)
            s = uv[tri].astype(np.float32)
            x0, y0 = np.floor(d.min(0)).astype(int)
            x1, y1 = np.ceil(d.max(0)).astype(int) + 1
            x0 = max(x0, sx0)
            y0 = max(y0, sy0)
            x1 = min(x1, sx1)
            y1 = min(y1, sy1)
            if x1 <= x0 or y1 <= y0:
                continue
            det = (s[1, 0] - s[0, 0]) * (s[2, 1] - s[0, 1]) - (s[1, 1] - s[0, 1]) * (s[2, 0] - s[0, 0])
            if abs(det) < 1e-4:
                continue
            Mt = cv2.getAffineTransform(s, d - np.array([x0, y0], np.float32))
            patch = cv2.warpAffine(t, Mt, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
            mask = np.zeros((y1 - y0, x1 - x0), np.float32)
            cv2.fillConvexPoly(mask, np.round((d - np.array([x0, y0])) * 8).astype(np.int32), 1.0, lineType=cv2.LINE_AA, shift=3)
            ly0, ly1 = y0 - sy0, y1 - sy0
            lx0, lx1 = x0 - sx0, x1 - sx0
            pa = patch[..., 3] * mask
            slot_cov[ly0:ly1, lx0:lx1] += mask
            slot_a[ly0:ly1, lx0:lx1] += pa
            slot_pm[ly0:ly1, lx0:lx1] += patch[..., :3] * pa[..., None]

        inv_cov = 1.0 / np.maximum(slot_cov, 1.0)
        fa = np.clip(slot_a * inv_cov, 0.0, 1.0) * alpha_mul
        frgb = slot_pm * (inv_cov * alpha_mul)[..., None]
        reg = canvas[sy0:sy1, sx0:sx1]
        if add:
            reg += frgb
        else:
            reg[...] = reg * (1.0 - fa[..., None]) + frgb * col

    out = (np.clip(canvas, 0.0, 1.0) * 255.0).astype(np.uint8)
    if draw_ground:
        gy = int(round(oy))
        # Draw dashed bright cyan-amber ground reference line & origin tick on top so ground lock is unmistakable
        for x in range(0, W, 18):
            cv2.line(out, (x, gy), (min(W - 1, x + 9), gy), (90, 210, 170), 1, cv2.LINE_AA)
        cv2.line(out, (int(ox), gy - 8), (int(ox), gy + 8), (240, 200, 90), 1, cv2.LINE_AA)
    return out

def frame_at_time(frames_list, target_t):
    return min(frames_list, key=lambda f: abs(f['t'] - target_t))

def make_sheet(items, cols, fn, scale=0.58, W=860, H=460, ox=435, oy=430):
    tiles = []
    for anim_name, fr, note in items:
        im = render(fr, scale=scale, W=W, H=H, ox=ox, oy=oy, draw_ground=True)
        label = f"{anim_name}  t={fr['t']:.2f}s  [{note}]"
        cv2.putText(im, label, (14, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (18, 20, 26), 4, cv2.LINE_AA)
        cv2.putText(im, label, (14, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (240, 244, 255), 1, cv2.LINE_AA)
        tiles.append(im)
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.zeros((rows * H, cols * W, 3), np.uint8)
    for k, im in enumerate(tiles):
        r, c = divmod(k, cols)
        sheet[r * H:(r + 1) * H, c * W:(c + 1) * W] = im
    Image.fromarray(sheet).save(fn, quality=90)
    print('saved', fn, sheet.shape)

def build_review_sheets(frames):
    # Main contact sheet: idle_loop, attack, cast, power_up (4 rows x 4 cols)
    main_items = [
        ('idle_loop', frame_at_time(frames['idle_loop'], 0.07), 'Lub atrium lead'),
        ('idle_loop', frame_at_time(frames['idle_loop'], 0.27), 'Dub bulb+coronary surge'),
        ('idle_loop', frame_at_time(frames['idle_loop'], 0.60), 'Gravity settle'),
        ('idle_loop', frame_at_time(frames['idle_loop'], 1.77), 'Beat 2 Dub surge'),

        ('attack', frame_at_time(frames['attack'], 0.24), 'Coil right + engorge'),
        ('attack', frame_at_time(frames['attack'], 0.37), 'Thrust left -38px HIT'),
        ('attack', frame_at_time(frames['attack'], 0.56), 'Meaty rebound'),
        ('attack', frame_at_time(frames['attack'], 1.25), 'Settle == idle(0)'),

        ('cast', frame_at_time(frames['cast'], 0.19), 'Accel beat 2'),
        ('cast', frame_at_time(frames['cast'], 0.30), 'Accel beat 3'),
        ('cast', frame_at_time(frames['cast'], 0.43), 'Systolic eruption + 8 tubes'),
        ('cast', frame_at_time(frames['cast'], 0.80), 'Heavy recovery'),

        ('power_up', frame_at_time(frames['power_up'], 0.27), 'Hardening lockdown'),
        ('power_up', frame_at_time(frames['power_up'], 0.50), 'Towering overdrive surge'),
        ('power_up', frame_at_time(frames['power_up'], 0.70), 'High-tension shudder'),
        ('power_up', frame_at_time(frames['power_up'], 1.10), 'Decompress'),
    ]
    make_sheet(main_items, 4, 'prev/sheet_main.jpg')

    # Sub contact sheet: summon, hurt, die, revive (4 rows x 4 cols)
    sub_items = [
        ('summon', frame_at_time(frames['summon'], 0.13), 'Wave R: plr+blue'),
        ('summon', frame_at_time(frames['summon'], 0.30), 'Wave M: purple+right'),
        ('summon', frame_at_time(frames['summon'], 0.43), 'Wave C: bulb+lobes'),
        ('summon', frame_at_time(frames['summon'], 0.53), 'Wave L: atrium+tubes eject'),

        ('hurt', frame_at_time(frames['hurt'], 0.00), 'Pre-hit rest'),
        ('hurt', frame_at_time(frames['hurt'], 0.07), 'Shear right + squash down'),
        ('hurt', frame_at_time(frames['hurt'], 0.23), 'Elastic rebound'),
        ('hurt', frame_at_time(frames['hurt'], 0.65), 'Settle == idle(0)'),

        ('die', frame_at_time(frames['die'], 0.23), 'Arrhythmic spasm'),
        ('die', frame_at_time(frames['die'], 0.63), 'Fibrillation flicker'),
        ('die', frame_at_time(frames['die'], 1.53), 'Exsanguination sag'),
        ('die', frame_at_time(frames['die'], 2.60), 'Collapsed dead pose'),

        ('revive', frame_at_time(frames['revive'], 0.00), 'Matches die(2.60)'),
        ('revive', frame_at_time(frames['revive'], 0.57), 'Defibrillation heave'),
        ('revive', frame_at_time(frames['revive'], 1.00), 'Re-inflation lock-in'),
        ('revive', frame_at_time(frames['revive'], 1.80), 'Matches idle(0)'),
    ]
    make_sheet(sub_items, 4, 'prev/sheet_sub.jpg')

    # Full-resolution peak frames (scale=1.0, 1460x740)
    peaks = [
        ('attack',   0.37, 'prev/peak_attack.jpg',   'attack peak forward-left thrust & 4-chamber flash'),
        ('power_up', 0.53, 'prev/peak_power_up.jpg', 'power_up towering overdrive surge'),
        ('die',      2.60, 'prev/peak_die.jpg',      'die exsanguinated collapse on stone floor'),
        ('cast',     0.43, 'prev/peak_cast.jpg',     'cast upward-outward systolic eruption'),
        ('hurt',     0.07, 'prev/peak_hurt.jpg',     'hurt right-shear & downward fleshy squash'),
    ]
    for anim_name, pt, pfn, note in peaks:
        fr = frame_at_time(frames[anim_name], pt)
        im = render(fr, scale=1.0, W=1460, H=740, ox=740, oy=695, draw_ground=True)
        label = f"{anim_name}  t={fr['t']:.2f}s  —  {note}"
        cv2.putText(im, label, (20, 38), cv2.FONT_HERSHEY_SIMPLEX, 0.85, (18, 20, 26), 4, cv2.LINE_AA)
        cv2.putText(im, label, (20, 38), cv2.FONT_HERSHEY_SIMPLEX, 0.85, (240, 244, 255), 2, cv2.LINE_AA)
        Image.fromarray(im).save(pfn, quality=92)
        print('saved', pfn, im.shape)

if __name__ == '__main__':
    frames = pickle.load(open('out/frames.pkl', 'rb'))
    if len(sys.argv) == 1 or sys.argv[1] == '--review':
        build_review_sheets(frames)
    else:
        name = sys.argv[1]
        idxs = [int(x) for x in sys.argv[2].split(',')]
        ims = [render(frames[name][i]) for i in idxs]
        cols = int(sys.argv[3]) if len(sys.argv) > 3 else 2
        h, w = ims[0].shape[:2]
        rows = (len(ims) + cols - 1) // cols
        sheet = np.zeros((rows * h, cols * w, 3), np.uint8)
        for k, im in enumerate(ims):
            cv2.putText(im, f'{name} f{idxs[k]} t={frames[name][idxs[k]]["t"]:.2f}', (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
            sheet[(k // cols) * h:(k // cols + 1) * h, (k % cols) * w:(k % cols + 1) * w] = im
        fn = f'prev/{name}_sheet.jpg'
        Image.fromarray(sheet).save(fn, quality=88)
        print(fn)
