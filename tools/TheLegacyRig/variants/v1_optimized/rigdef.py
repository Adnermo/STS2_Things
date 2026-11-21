"""Rig definition for The Legacy (腐化之遗) — Version A Deep-Polished (v1_optimized). All positions in source-image pixels."""
ORIGIN = (679.0, 660.0)          # skeleton origin (image px) -> spine (0,0)

def S(p):
    return (round(p[0] - ORIGIN[0], 2), round(ORIGIN[1] - p[1], 2))

# name, parent, image position
BONES = [
    ('root', None, (679, 660)),
    ('weed_l_anchor', 'root', (102, 456)),
    ('body', 'root', (679, 640)),
    ('heart', 'body', (650, 480)),
    ('bulb_base', 'heart', (640, 520)),
    ('bulb', 'bulb_base', (770, 390)),
    ('atrium', 'heart', (560, 320)),
    ('tube_a', 'atrium', (335, 262)),
    ('tube_b', 'atrium', (305, 372)),
    ('mid_lobe', 'heart', (520, 480)),
    ('mid_tip', 'mid_lobe', (260, 468)),
    ('bottom_lobe', 'heart', (650, 620)),
    ('bottom_tip', 'bottom_lobe', (360, 602)),
    ('right_lobe', 'heart', (940, 600)),
    ('back', 'body', (950, 320)),
    ('purple_mass', 'back', (900, 300)),
    ('purple_top', 'purple_mass', (800, 215)),
    ('ptube_l', 'purple_top', (742, 132)),
    ('ptube_r', 'purple_top', (768, 112)),
    ('purple_lr', 'back', (1100, 480)),
    ('plr_a', 'purple_lr', (1185, 450)),
    ('plr_b', 'purple_lr', (1185, 525)),
    ('blue', 'body', (950, 420)),
    ('blue_main', 'blue', (900, 385)),
    ('blue_up', 'blue_main', (1195, 365)),
    ('blue_down', 'blue', (1150, 432)),
    ('blue_bottom', 'blue', (1000, 470)),
]

# per body part: list of (bone, segment start, segment end) used for skin weights
BODY_SKIN = {
    'purple_top': [('purple_top', (805, 215), (775, 150)), ('ptube_l', (742, 132), (660, 82)), ('ptube_r', (768, 112), (725, 42))],
    'purple_mass': [('purple_mass', (820, 250), (1000, 250))],
    'purple_lr': [('purple_lr', (1080, 470), (1170, 480)), ('plr_a', (1185, 450), (1335, 425)), ('plr_b', (1185, 525), (1240, 592))],
    'blue_bottom': [('blue_bottom', (990, 470), (1050, 545))],
    'blue_main': [('blue_main', (900, 385), (1140, 410)), ('blue_up', (1195, 365), (1215, 280))],
    'blue_down': [('blue_down', (1150, 432), (1275, 500))],
    'right_lobe': [('right_lobe', (930, 420), (940, 600))],
    'mid_lobe': [('mid_lobe', (620, 470), (380, 480)), ('mid_tip', (260, 468), (95, 470))],
    'bottom_lobe': [('bottom_lobe', (900, 600), (480, 620)), ('bottom_tip', (360, 602), (160, 585))],
    'atrium': [('atrium', (640, 280), (420, 300)), ('tube_a', (335, 262), (165, 255)), ('tube_b', (305, 372), (120, 375))],
    'bulb': [('bulb_base', (560, 570), (680, 480)), ('bulb', (740, 420), (800, 330))],
}

# Shared skin-weight pools for cross-seam continuity
CYAN_CHAMBERS = ('bulb', 'bottom_lobe', 'mid_lobe', 'right_lobe', 'atrium')
PURPLE_UPPER = ('purple_top', 'purple_mass')
BLUE_CHAMBERS = ('blue_main', 'blue_down', 'blue_bottom')
GROUND_PINNED_PARTS = ('bottom_lobe', 'right_lobe', 'purple_lr', 'blue_bottom', 'mid_lobe', 'bulb', 'atrium')

GLOW_OF = {
    'atrium_glow': 'atrium',
    'bulb_glow': 'bulb',
    'mid_lobe_glow': 'mid_lobe',
    'bottom_lobe_glow': 'bottom_lobe',
}

# chain parts (weeds / corals): base -> tip overrides
CHAIN_OVERRIDE = {
    'weed_left':         ((102, 456), (72, 162)),
    'weed_ground_left':  ((152, 566), (12, 520)),
    'weed_ground_right': ((1254, 544), (1348, 534)),
    'weed_edge_right':   ((1310, 495), (1355, 474)),
    'weed_ground_bl':    ((306, 664), (438, 614)),
    'weed_bottom_mid':   ((545, 675), (535, 416)),
    'weed_bottom_right': ((1095, 622), (1187, 419)),
    'weed_blue':         ((1056, 382), (1012, 294)),
}

# Ground-floor flora parented to 'root' so their stone-floor roots stay 0px planted;
# weed_left sits 204px above the floor behind mid_lobe/tube_b on dedicated 'weed_l_anchor';
# dorsal/vessel flora parented to their host chambers.
CHAIN_PARENT = {
    'coral_pink':        'mid_lobe',
    'coral_purple':      'root',
    'weed_top_left':     'tube_a',
    'weed_left':         'weed_l_anchor',
    'weed_bottom_mid':   'root',
    'weed_bottom_right': 'root',
    'weed_right':        'purple_mass',
    'weed_ground_left':  'root',
    'weed_back_a':       'purple_mass',
    'weed_back_b':       'purple_top',
    'weed_back_c':       'purple_mass',
    'weed_back_d':       'purple_mass',
    'weed_blue':         'blue_main',
    'weed_ground_right': 'root',
    'weed_edge_right':   'root',
    'weed_ground_bl':    'root',
}

# Adjacent chamber coupling: drives realistic shockwave & contact-push whip on non-chamber-parented flora!
CHAMBER_COUPLE = {
    'weed_left':         ('mid_tip',     1.35),
    'weed_ground_left':  ('mid_tip',     1.20),
    'weed_ground_bl':    ('bottom_tip',  1.05),
    'weed_bottom_mid':   ('bulb',        1.10),
    'coral_purple':      ('right_lobe',  0.60),
    'weed_bottom_right': ('blue_bottom', 1.15),
    'weed_ground_right': ('plr_b',       1.05),
    'weed_edge_right':   ('blue_down',   1.05),
}

GROUND_FLORA = tuple(k for k, v in CHAIN_PARENT.items() if v == 'root')
