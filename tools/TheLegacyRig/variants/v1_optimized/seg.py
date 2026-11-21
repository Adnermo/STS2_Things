import numpy as np, cv2, json
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
from skimage.segmentation import watershed

im = np.array(Image.open('src.png').convert('RGBA'))
rgb = im[..., :3]
a = im[..., 3]
H, W = a.shape
hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
h = hsv[..., 0].astype(int) * 2
s = hsv[..., 1].astype(int)
v = hsv[..., 2].astype(int)
m = a > 8

green = (h >= 45) & (h <= 110) & m & (s > 50)
cyan = (h > 170) & (h <= 205) & m & (s > 40)
blue = (h > 205) & (h <= 235) & m & (s > 40)
purple = (h > 235) & (h <= 300) & m & (s > 30)
pink = ((h > 300) | (h < 20)) & m & (s > 40)
strongbody = (cyan | blue | purple) & (v > 80)
bodyish = (cyan | blue | purple) & (v > 35) & (s > 35)
barnacle_px = (s < 38) & (v > 100) & m

# ---- seaweed sprigs
g = cv2.morphologyEx(green.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
n, lab, st, cen = cv2.connectedComponentsWithStats(g, 8)

# Restrict weed_back_d to ONLY the tall dorsal sprig rising above/behind purple_mass (y < 275, components 14, 32)
SPRIGS = {
    'weed_top_left': [1],
    'weed_left': [22, 97],
    'weed_bottom_mid': [209],
    'weed_bottom_right': [214, 265, 295],
    'weed_right': [12],
    'weed_ground_left': [264, 248],
    'weed_back_a': [7],
    'weed_back_b': [3],
    'weed_back_c': [2],
    'weed_back_d': [14, 32],
    'weed_blue': [120],
    'weed_ground_right': [282],
    'weed_edge_right': [244],
    'weed_ground_bl': [328, 355],
}

cand = (~strongbody) & m & (~pink)

def grow(core, it=4, c=None):
    c = cand | core if c is None else c
    gm = core.copy()
    ker3 = np.ones((3, 3), np.uint8)
    for _ in range(it):
        gm = ((cv2.dilate(gm.astype(np.uint8), ker3) > 0) & c) | gm
    return gm

# ---- corals (avoid swallowing pitch-black cleft pockets between twigs!)
not_pitch_dark = v >= 40
box = np.zeros((H, W), bool)
box[330:520, 160:400] = True
coralp = pink & box & (v >= 36)
coralp = cv2.morphologyEx(coralp.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)) > 0
coralp = grow(coralp & box, 2, (cand | pink) & box & not_pitch_dark)
# Allow at most a 1px dark ink outline on lower coral base only (never grab dark cleft shadow under tube_b at y < 425!)
coralp = (((cv2.dilate(coralp.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) & m & ~strongbody & box & (np.arange(H)[:, None] >= 425) & (v < 40)) | coralp)

box2 = np.zeros((H, W), bool)
box2[455:689, 740:1000] = True
cpu = purple & box2 & (h < 285) & (v >= 34)
cpu = grow(cpu, 2, (cand | purple) & box2 & (v >= 36))
cpu = (((cv2.dilate(cpu.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) & m & ~strongbody & box2 & (v < 36)) | cpu)

corals = coralp | cpu

# ---- smooth anatomical body silhouette (body_true)
# Solidly cover behind coral_pink, coral_purple, and weed_bottom_mid with smooth rounded lobe contours
yy, xx = np.ogrid[:H, :W]
xx_f = np.broadcast_to(xx, (H, W)).astype(np.float32)
yy_f = np.broadcast_to(yy, (H, W)).astype(np.float32)

dx_right = np.maximum(0.0, xx_f - 875.0)
floor_y = np.where(
    xx_f <= 460.0, 669.0,
    np.where(
        xx_f <= 875.0,
        668.0 - (xx_f - 460.0) * (9.0 / 415.0),
        659.0 - (dx_right / 118.0) ** 1.4 * 34.0
    )
)

# Close foreground flora hulls inside the smooth lobe envelope so gaps between twigs/fronds are solid too
cpu_hull = cv2.morphologyEx(cpu.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))) > 0
wbm_hull = cv2.morphologyEx((lab == 209).astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))) > 0
coralp_hull = cv2.morphologyEx(coralp.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (29, 29))) > 0
wb_hull = cv2.morphologyEx((lab == 120).astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))) > 0
wbr_hull = cv2.morphologyEx(np.isin(lab, [214, 265, 295]).astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))) > 0
wgbl_hull = cv2.morphologyEx(np.isin(lab, [328, 355]).astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (27, 27))) > 0
wtl_pad = (cv2.dilate((lab == 1).astype(np.uint8), np.ones((15, 15), np.uint8)) > 0) & (
    yy_f >= 216.0 + np.maximum(0.0, 392.0 - xx_f) * 0.28
)

behind_front_body = (
    coralp_hull
    | (cpu_hull & (yy_f <= floor_y) & (xx_f <= 990.0))
    | (wbm_hull & (yy_f <= floor_y))
    | wb_hull
    | (wbr_hull & (xx_f <= 1158.0) & (yy_f <= 578.0) & (a >= 220))
    | (wgbl_hull & (yy_f <= 663.0))
    | wtl_pad
    | ((lab == 7) & (yy_f >= 216.0))
    | ((lab == 12) & (xx_f <= 1138.0) & (yy_f >= 255.0))
)

bt_vis = (strongbody | (pink & (v > 60)) | barnacle_px) & ~corals
bt_vis = cv2.morphologyEx(bt_vis.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)) > 0
bt = bt_vis | (behind_front_body & m)
bt = cv2.morphologyEx(bt.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (23, 23))) > 0
# Trim any closed spill below floor_y in the bottom foreground flora zone (x=450..995)
in_flora_bottom = (xx_f >= 455.0) & (xx_f <= 995.0) & (yy_f > floor_y) & ~bt_vis
bt &= ~in_flora_bottom
body_true = (ndi.binary_fill_holes(bt) & m) | (coralp_hull & (xx_f >= 258.0) & (yy_f >= 335.0) & (yy_f <= 372.0))
np.save('body_true.npy', body_true)

outside = m & ~body_true & ~corals
sprig_masks = {}
taken = corals.copy()

ker = np.ones((3, 3), np.uint8)
olive_leaf = (h >= 30) & (h <= 130) & (v >= 38) & (s >= 26)
cores = {name: np.isin(lab, ids) for name, ids in SPRIGS.items()}
grown = {k: v_mask.copy() for k, v_mask in cores.items()}
for it in range(14):
    for k in grown:
        d = cv2.dilate(grown[k].astype(np.uint8), ker) > 0
        # Only grow >2px into actual olive/green leaf pixels; limit dark ink outline growth to <=1px (it < 1)
        leaf_ok = olive_leaf | (it < 1) | ((a < 235) & (it < 4))
        allow = (((outside & ~bodyish & (it < 14)) | (cand & ~bodyish & (it < 6))) & leaf_ok) & ~barnacle_px
        if k == 'weed_back_d':
            allow = allow & (yy < 275)
        grown[k] |= d & allow

urch = ((xx - 296) ** 2 + (yy - 656) ** 2 < 29 ** 2)
for k in grown:
    g2 = grown[k] & ~taken & ~urch
    if k == 'weed_back_d':
        g2 &= (yy < 275)
    sprig_masks[k] = g2
    taken |= g2

np.save('masks_front.npy', {'sprigs': sprig_masks, 'coral_pink': coralp, 'coral_purple': cpu}, allow_pickle=True)

# ---- body watershed
body = m & ~taken
markers = np.zeros((H, W), np.int32)
PARTS = [
    'purple_top', 'purple_mass', 'purple_lr',
    'blue_main', 'blue_down', 'blue_bottom',
    'atrium', 'mid_lobe', 'bottom_lobe', 'bulb', 'right_lobe'
]

def rect(x0, y0, x1, y1):
    r = np.zeros((H, W), bool)
    r[y0:y1, x0:x1] = True
    return r

def disc(cx, cy, rr):
    return (xx - cx) ** 2 + (yy - cy) ** 2 < rr * rr

# Coronary pink veins sit on the cyan chambers — include (cyan | pink) in cyan chamber seeds
# and keep dorsal moss right of x=882 (y=240..320) on purple_mass rather than bulb.
cyan_wall = cyan | (pink & ~coralp)
seeds = {
    'purple_top': (purple, [rect(640, 60, 690, 110), rect(700, 25, 760, 80), rect(760, 120, 800, 180)]),
    'purple_mass': (purple | (~cyan & rect(884, 240, 960, 318)) | rect(654, 195, 696, 248), [
        rect(830, 140, 1000, 230), rect(700, 215, 800, 245), rect(1000, 250, 1080, 300), rect(886, 245, 950, 312), rect(656, 200, 694, 246)
    ]),
    'purple_lr': (purple, [rect(1260, 390, 1330, 440), rect(1170, 560, 1270, 600), rect(1100, 470, 1150, 520)]),
    'blue_main': (blue, [rect(920, 340, 1080, 380), rect(1190, 290, 1240, 340), rect(1110, 360, 1160, 390)]),
    'blue_down': (blue, [rect(1180, 440, 1280, 480)]),
    'blue_bottom': (blue, [rect(1010, 520, 1070, 560), rect(1000, 470, 1040, 500)]),
    'atrium': (cyan_wall, [rect(180, 240, 330, 285), rect(140, 350, 300, 385), rect(380, 190, 600, 240)]),
    'mid_lobe': (cyan_wall, [rect(100, 440, 160, 510), rect(400, 410, 600, 475)]),
    'bottom_lobe': (cyan_wall, [rect(180, 570, 400, 640), rect(440, 580, 650, 645), rect(730, 555, 830, 645)]),
    'bulb': (cyan_wall, [disc(770, 360, 80), rect(640, 440, 740, 505)]),
    'right_lobe': (cyan_wall, [disc(945, 470, 35), rect(885, 535, 980, 615)]),
}

for i, p in enumerate(PARTS, 1):
    cls, regs = seeds[p]
    for r in regs:
        markers[r & cls & body] = i

lum = cv2.GaussianBlur(v.astype(np.float32), (0, 0), 2.0)
elev = 255.0 - lum
# Treat coronary pink veins as part of the cyan chamber wall so watershed follows anatomical lobe creases,
# not vein borders!
hue_cls = np.zeros((H, W), np.uint8)
hue_cls[cyan_wall] = 1
hue_cls[blue] = 2
hue_cls[purple & ~corals] = 3
edge = np.zeros((H, W), np.float32)
for k in (1, 2, 3):
    mk = (hue_cls == k).astype(np.float32)
    mk = cv2.GaussianBlur(mk, (0, 0), 1.5)
    edge += np.hypot(cv2.Sobel(mk, cv2.CV_32F, 1, 0), cv2.Sobel(mk, cv2.CV_32F, 0, 1))
elev = elev + edge * 120.0
labels = watershed(elev, markers, mask=body)
np.save('labels_body.npy', labels)
json.dump(PARTS, open('parts_body.json', 'w'))

pal = np.random.default_rng(3).integers(40, 255, (40, 3))
vis = rgb.copy().astype(float) * 0.35
for i in range(1, len(PARTS) + 1):
    vis[labels == i] = vis[labels == i] + pal[i] * 0.65
for j, (nme, mk) in enumerate(sprig_masks.items()):
    vis[mk] = [0, 255, 0] if j % 2 == 0 else [180, 255, 80]
vis[coralp] = [255, 80, 160]
vis[cpu] = [200, 120, 255]
out = Image.fromarray(np.clip(vis, 0, 255).astype(np.uint8))
d = ImageDraw.Draw(out)
for i, p in enumerate(PARTS, 1):
    ys, xs = np.nonzero(labels == i)
    if len(xs):
        d.text((int(xs.mean()), int(ys.mean())), p, fill='white')
out.save('vis_seg.jpg', quality=90)
print('seg ok: weed_back_d px=', int(sprig_masks['weed_back_d'].sum()), 'body_true px=', int(body_true.sum()))
