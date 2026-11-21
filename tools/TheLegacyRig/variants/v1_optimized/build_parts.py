"""Stage B: every opaque pixel -> exactly one layer; smooth occlusion fill for back layers."""
import numpy as np, cv2, json
from PIL import Image
from scipy import ndimage as ndi

im = np.array(Image.open('src.png').convert('RGBA'))
H, W = im.shape[:2]
rgb = im[..., :3]
a = im[..., 3]
m = a > 0

yy, xx = np.ogrid[:H, :W]
F = np.load('masks_front.npy', allow_pickle=True).item()

# Bridge tiny 3-4px coral-twig cutouts along tube_b's cylindrical wall so tube_b never shows square notches when moving
coralp_bridge = cv2.morphologyEx(
    F['coral_pink'].astype(np.uint8),
    cv2.MORPH_CLOSE,
    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))
) > 0
tube_b_bridge = coralp_bridge & (xx >= 215) & (yy >= 332) & (yy <= 372)

# Fill tiny internal gaps inside the silhouette before eroding by 2px so coral/weed gaps never punch Swiss-cheese holes,
# while still keeping hidden extensions 2px inside the outer silhouette boundary for exact rest alpha!
outer_solid = ndi.binary_fill_holes((a >= 245) | tube_b_bridge)
interior_opaque = ((cv2.erode(outer_solid.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0) & (a >= 245)) | tube_b_bridge

labels = np.load('labels_body.npy')
PARTS = json.load(open('parts_body.json'))

front_names = ['coral_purple', 'coral_pink'] + list(F['sprigs'].keys())
front_masks = {'coral_purple': F['coral_purple'], 'coral_pink': F['coral_pink'], **F['sprigs']}
body_true0 = np.load('body_true.npy')

hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
h_deg = hsv[..., 0].astype(int) * 2
sat = hsv[..., 1].astype(int)
val = hsv[..., 2].astype(int)
near_body_rim = cv2.dilate(body_true0.astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
olive_hue = (h_deg >= 28) & (h_deg <= 135) & (sat >= 18)
weed_like = (olive_hue & (val >= 36)) | ((a < 235) & (olive_hue | ~near_body_rim))

known0 = labels > 0
# Only absorb outer stray pixels that are actual olive leaf / anti-aliased leaf fringe — never steal cyan chamber rims!
stray = ((known0 & ~body_true0 & weed_like) | ((labels > 0) & (a <= 24) & ~near_body_rim))
for k in list(F['sprigs'].keys()):
    mk = F['sprigs'][k]
    near = cv2.dilate(mk.astype(np.uint8), np.ones((15, 15), np.uint8)) > 0
    add = stray & near
    if k == 'weed_back_d':
        add &= (yy < 275)
    mk |= add
    stray &= ~add
    labels[add] = 0
    front_masks[k] = mk

fk = list(front_masks.keys())
U = np.zeros((H, W), np.int32)
kn = labels > 0
ii = ndi.distance_transform_edt(~kn, return_distances=False, return_indices=True)
U[m] = labels[ii[0], ii[1]][m]
for j, k in enumerate(fk):
    U[front_masks[k]] = 100 + j

faint = m & (a <= 24) & ~body_true0
solid = (U > 0) & ~faint
ii = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
U[faint] = U[ii[0], ii[1]][faint]

for j, k in enumerate(fk):
    mk = (U == 100 + j)
    if k == 'weed_back_d':
        mk &= (yy < 275)
    front_masks[k] = mk

labels = np.where(U < 100, U, 0)
front_any = np.zeros((H, W), bool)
for k in front_masks:
    front_any |= front_masks[k]

front_dil = cv2.dilate(front_any.astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
clean_known = (labels > 0) & ~front_dil
idx = ndi.distance_transform_edt(~clean_known, return_distances=False, return_indices=True)
smooth_under = labels[idx[0], idx[1]]
smooth_under[~m] = 0

body_lab = labels.copy()
body_lab[front_any] = 0
full_under = np.where(body_lab > 0, body_lab, smooth_under)
full_under[~m] = 0

body_sil = np.load('body_true.npy')
np.save('body_sil.npy', body_sil)

ORDER = [
    'purple_top', 'purple_mass', 'purple_lr',
    'blue_bottom', 'blue_main', 'blue_down',
    'right_lobe', 'mid_lobe', 'bottom_lobe', 'atrium', 'bulb'
]
rank = {p: i for i, p in enumerate(ORDER)}
pid = {p: i + 1 for i, p in enumerate(PARTS)}
layers = {}
EXT = 44

coralp_closed = cv2.morphologyEx(
    front_masks['coral_pink'].astype(np.uint8),
    cv2.MORPH_CLOSE,
    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))
) > 0
cpu_closed = cv2.morphologyEx(
    front_masks['coral_purple'].astype(np.uint8),
    cv2.MORPH_CLOSE,
    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))
) > 0

for p in ORDER:
    vis = (body_lab == pid[p])
    under = (full_under == pid[p]) & front_any & body_sil & interior_opaque
    own = vis | under

    infront_full = np.zeros((H, W), bool)
    for q in ORDER[rank[p] + 1:]:
        infront_full |= (full_under == pid[q]) & body_sil

    inbehind_vis = np.zeros((H, W), bool)
    for q in ORDER[:rank[p]]:
        inbehind_vis |= (body_lab == pid[q])

    grow_ext = cv2.dilate(
        (own & body_sil).astype(np.uint8),
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * EXT + 1, 2 * EXT + 1))
    ) > 0
    grow_front_pad = cv2.dilate(
        (own & body_sil).astype(np.uint8),
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (19, 19))
    ) > 0

    ext = ((grow_ext & infront_full) | (grow_front_pad & front_any & body_sil & ~inbehind_vis)) & interior_opaque
    if p == 'mid_lobe':
        # Guarantee mid_lobe forms an unbroken solid sheet behind coral_pink under atrium & bottom_lobe
        ext |= (coralp_closed & (infront_full | front_any) & body_sil & interior_opaque)
    if p == 'right_lobe':
        # Guarantee right_lobe forms an unbroken solid sheet behind coral_purple
        rl_hull = cv2.morphologyEx((own & body_sil).astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (45, 45))) > 0
        ext |= (rl_hull & cpu_closed & body_sil & interior_opaque)

    if p == 'atrium':
        ext |= (tube_b_bridge & body_sil)

    big = cv2.morphologyEx(
        (own & body_sil).astype(np.uint8),
        cv2.MORPH_CLOSE,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (81, 81))
    ) > 0
    big = ndi.binary_fill_holes(big)
    ext |= big & infront_full & interior_opaque

    valid_domain = m | tube_b_bridge
    full = (own | ext) & valid_domain
    # Fill any tiny internal holes inside body_sil while respecting back layers' visible pixels
    block_back = (inbehind_vis & ~tube_b_bridge) if p == 'atrium' else inbehind_vis
    closed_sil = ndi.binary_fill_holes(full & body_sil) & interior_opaque & ~block_back
    full = (full | closed_sil) & valid_domain
    if p == 'purple_mass':
        cleft_fill = (xx >= 654) & (xx <= 698) & (yy >= 198) & (yy <= 252) & body_sil & interior_opaque
        vis = vis | (cleft_fill & ~front_any)
        full = full | cleft_fill
    if p == 'blue_main':
        # Prevent hidden extension from growing upward into bulb's upper sky dome (x < 1050), avoiding shark-fin exposure in die
        ceil_y = 328.0 + np.maximum(0.0, 918.0 - xx) * 0.42
        full = vis | (full & ((xx >= 1050) | (yy >= ceil_y)))
    if p == 'atrium':
        # Keep dorsal cleft behind weed_back_a (x >= 656, y <= 246) on purple_mass so no cyan sliver peeks out
        cleft_guard = (xx >= 656) & (yy <= 246)
        vis = vis & ~cleft_guard
        full = full & ~cleft_guard
    layers[p] = dict(vis=vis, known=own, full=full, infront=infront_full)

BACK_FLORA = ['weed_left', 'weed_ground_left', 'weed_ground_right', 'weed_edge_right', 'weed_back_c']
front_only = [n for n in front_names if n not in BACK_FLORA]

np.save('layers_body.npy', {k: {kk: vv for kk, vv in v.items()} for k, v in layers.items()}, allow_pickle=True)
np.save('front_masks.npy', front_masks, allow_pickle=True)
json.dump({'order': ORDER, 'back_flora': BACK_FLORA, 'front': front_only}, open('order.json', 'w'))
print({p: int(layers[p]['full'].sum()) for p in ORDER})
