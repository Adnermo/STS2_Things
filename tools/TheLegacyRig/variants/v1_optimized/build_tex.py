import numpy as np, cv2, json, os
from PIL import Image
from scipy import ndimage as ndi

im = np.array(Image.open('src.png').convert('RGBA')).astype(np.float32)
H, W = im.shape[:2]
rgb = im[..., :3]
a = im[..., 3]
L = np.load('layers_body.npy', allow_pickle=True).item()
Fm = np.load('front_masks.npy', allow_pickle=True).item()
O = json.load(open('order.json'))
bt = np.load('body_true.npy')
os.makedirs('parts', exist_ok=True)

hsv = cv2.cvtColor(im[..., :3].astype(np.uint8), cv2.COLOR_RGB2HSV)
h = hsv[..., 0].astype(int) * 2
s = hsv[..., 1].astype(int)
R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]

# Detect crimson/pink coronary trunks AND brightly lit pink vein branches over cyan skin while rejecting grey barnacles:
# Pink/crimson absorbs green (0.5*(R+B) - G > 11), whereas cyan has G >> R and grey barnacles have R ~ G ~ B!
green_dip = 0.5 * (R + B) - G
barnacle = (s < 36) & (np.abs(R - G) < 12.0) & (G > 130.0)
pinkm = (
    ((green_dip > 11.0) & (R - G > -6.0) & ((h > 250) | (h < 30)) & (s > 22))
    & ~barnacle
    & (a > 64)
)

front_any = np.zeros((H, W), bool)
for mk in Fm.values():
    front_any |= mk
front_guard = cv2.dilate(front_any.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0

# Exclude coral_pink bbox [155:410, 330:545] (image x=[155:410], y=[330:545]), dorsal barnacles, and purple-arch rim
coral_pink_box = np.zeros((H, W), bool)
coral_pink_box[330:545, 155:410] = True
coral_guard = coral_pink_box | (
    cv2.dilate((Fm['coral_pink'] | Fm['coral_purple']).astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
)
atrium_rim_guard = np.zeros((H, W), bool)
atrium_rim_guard[158:188, 425:482] = True
atrium_rim_guard[192:232, 608:676] = True

yy, xx = np.ogrid[:H, :W]
coralp_bridge = cv2.morphologyEx(
    Fm['coral_pink'].astype(np.uint8),
    cv2.MORPH_CLOSE,
    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))
) > 0
tube_b_bridge = coralp_bridge & (xx >= 215) & (yy >= 332) & (yy <= 372)
dark_twig_pocket = tube_b_bridge & ((a < 235) | (rgb.max(-1) < 42.0))

def pushpull(col, wt):
    """Fill colour where wt==0 using weighted push-pull pyramid."""
    levels = [(col * wt[..., None], wt)]
    c, w = levels[0]
    while min(w.shape) > 2:
        c = cv2.pyrDown(c)
        w = cv2.pyrDown(w)
        levels.append((c, w))
    cc, ww = levels[-1]
    est = cc / np.maximum(ww, 1e-6)[..., None]
    for c, w in reversed(levels[:-1]):
        up = cv2.resize(est, (w.shape[1], w.shape[0]), interpolation=cv2.INTER_LINEAR)
        here = c / np.maximum(w, 1e-6)[..., None]
        k = np.clip(w * 4, 0, 1)[..., None]
        est = here * k + up * (1 - k)
    return est

meta = {}
def save(name, rgbimg, alpha, bbox_mask):
    ys, xs = np.nonzero(bbox_mask)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    out = np.dstack([rgbimg, alpha])[y0:y1, x0:x1]
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(f'parts/{name}.png')
    meta[name] = dict(x=int(x0), y=int(y0), w=int(x1 - x0), h=int(y1 - y0))
    return x0, y0, x1, y1

GLOW_CHAMBERS = ('atrium', 'bulb', 'mid_lobe', 'bottom_lobe')

for p in O['order']:
    vis = L[p]['vis']
    full = L[p]['full']

    other = ~vis
    outer_empty = cv2.dilate((a == 0).astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    band = cv2.dilate(other.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    src_mask = vis & ~(band & ~outer_empty) & (a > 250) & ~dark_twig_pocket
    src_clean = src_mask & ~front_guard
    if src_clean.sum() >= 200:
        src_mask = src_clean

    fill = pushpull(rgb, src_mask.astype(np.float32))
    fill = cv2.GaussianBlur(fill, (0, 0), 1.2)
    use_vis_rgb = (vis & ~dark_twig_pocket) if p in ('atrium', 'mid_lobe') else vis
    col = np.where(use_vis_rgb[..., None], rgb, fill)

    alpha = np.where(full, a, 0.0).astype(np.float32)
    alpha = np.where(full & bt & ((~vis & (a >= 250)) | tube_b_bridge), 255.0, alpha)

    soft = cv2.GaussianBlur(full.astype(np.float32), (0, 0), 0.7) * 255.0
    alpha = np.where(vis & ~dark_twig_pocket, alpha, np.minimum(alpha, soft))
    # Smoothly feather front-over-back weed-pad extensions (e.g. bulb over bottom_lobe behind weed_bottom_mid)
    infront_mask = L[p].get('infront', np.zeros((H, W), bool))
    over_pad = full & ~vis & ~infront_mask & ~tube_b_bridge
    if over_pad.any():
        dist_v = ndi.distance_transform_edt(~vis)
        feather = np.clip(1.0 - (dist_v - 1.0) / 8.5, 0.0, 1.0) * 255.0
        alpha = np.where(over_pad, np.minimum(alpha, feather), alpha)
    alpha = np.where(full & ~vis & ~bt & (a < 250), 0.0, alpha)
    save(p, col, alpha, full)

    # Complete Coronary Vein Glow across atrium, bulb, mid_lobe, and bottom_lobe
    if p in GLOW_CHAMBERS:
        if p in ('mid_lobe', 'bottom_lobe'):
            vm_raw = pinkm & vis & ~coral_guard
        elif p == 'atrium':
            vm_raw = pinkm & vis & ~coral_guard & ~atrium_rim_guard
        else:
            vm_raw = pinkm & vis & ~(cv2.dilate(Fm['coral_pink'].astype(np.uint8), np.ones((7, 7), np.uint8)) > 0)
        # Remove isolated 1-2px noise specks while preserving fine veins
        vm_raw = cv2.morphologyEx(vm_raw.astype(np.uint8), cv2.MORPH_OPEN, np.ones((2, 2), np.uint8)) > 0
        # Bridge narrow foreground weed stems crossing coronary veins
        vm_bridged = cv2.morphologyEx(vm_raw.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))) > 0
        vm_bin = np.where(front_any & full & ~coral_guard & (~atrium_rim_guard if p == 'atrium' else True), vm_bridged, vm_raw).astype(np.uint8)
        # Reject tiny isolated surface specks (< 28px) while keeping all branching coronary vessels
        nc, clab, cst, _ = cv2.connectedComponentsWithStats(vm_bin, 8)
        keep = np.zeros(nc, bool)
        for ci in range(1, nc):
            if cst[ci, 4] >= 28:
                keep[ci] = True
        vm = keep[clab].astype(np.float32)
        print(f'glow {p}_glow: vm_px={int(vm.sum())}')
        if vm.sum() > 50:
            g1 = cv2.GaussianBlur(vm, (0, 0), 1.0)
            g2 = cv2.GaussianBlur(vm, (0, 0), 5.0)
            ga = np.clip(g1 * 1.3 + g2 * 1.6, 0.0, 1.0) * 255.0
            gcol = np.zeros_like(rgb)
            gcol[...] = (255, 70, 110)
            ga = np.where(full & (alpha > 16.0), ga, 0.0)
            save(p + '_glow', gcol, ga, full)

back_flora_set = set(O.get('back_flora', []))
any_body_opaque = np.zeros((H, W), bool)
for p_body in O['order']:
    any_body_opaque |= (L[p_body]['vis'] & (a >= 245)) | (L[p_body]['full'] & bt & (a >= 245))
outer_safe = cv2.erode((a >= 180).astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
body_opaque_core = any_body_opaque & outer_safe

for k, mk in Fm.items():
    if mk.sum() < 50:
        continue
    if k in back_flora_set:
        # Extend only true opaque leaf/stem cores (not faint 1px edge anti-aliasing) 36px inward underneath occluding body chambers
        mk_core = cv2.morphologyEx((mk & (a >= 160)).astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)) > 0
        if mk_core.sum() < 20:
            mk_core = mk & (a >= 80)
        pad_dil = cv2.dilate(mk_core.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (73, 73))) > 0
        near_body_core = cv2.dilate(body_opaque_core.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
        pad_zone = body_opaque_core | (mk & (a < 230) & near_body_core)
        pad = pad_dil & pad_zone & ~(mk & (a >= 230))
        full_k = mk | pad
        src_wt = (mk_core & (a > 210)).astype(np.float32)
        if src_wt.sum() < 20:
            src_wt = mk_core.astype(np.float32)
        fill_k = cv2.GaussianBlur(pushpull(rgb, src_wt), (0, 0), 1.1)
        col_k = np.where((mk & (a >= 200))[..., None], rgb, fill_k)
        dist_mk = ndi.distance_transform_edt(~mk_core)
        pad_alpha = np.clip((36.0 - np.maximum(0.0, dist_mk - 20.0) * (36.0 / 16.0)) / 36.0, 0.0, 1.0) * 255.0
        base_mk_a = np.where(mk, a, 0.0)
        alpha_k = np.where(pad, np.maximum(base_mk_a, pad_alpha), base_mk_a).astype(np.float32)
        save(k, col_k, alpha_k, full_k)
    else:
        alpha = np.where(mk, a, 0.0).astype(np.float32)
        save(k, rgb, alpha, mk)

json.dump(meta, open('parts/meta.json', 'w'), indent=1)
print('parts count:', len(meta), 'area:', sum(v['w'] * v['h'] for v in meta.values()))
