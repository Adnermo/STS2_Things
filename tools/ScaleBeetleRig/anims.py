"""Authored actions for the Scale Beetle (放缩巨甲虫), rebuilt 2026-11-09, plates locked 2026-11-11.

Heavy armour with insect detail. The carapace moves as one braced mass: it crouches,
rears, lunges and recoils, and rings as a whole after impacts. The painted plates hide
only a few pixels of underside where they overlap, so they never slide against each
other (`python seams.py check`): the shells, collar and underbody stay together, the
pronotum dips nose-down about its top seam with the shells, and the head lifts nose-up
inside the neck shield (`python slack.py`). The head, mandibles and beaded feelers move
in quick, staccato beats with follow-through, and the feelers lash as a travelling wave
so a whip reaches its tip at the hit. Six legs keep at least three feet planted. Feet
step, rise and stamp when the body travels; at rest the legs are close to full
extension, so every rise is paid for by a crouch, a lifted foreleg or a step in.

Event times and clip lengths are gameplay contracts shared with ThingsScaleBeetle and
NThingsScaleBeetleMotion (whip holds at the two ``whip_ready`` events).
"""
import math

from rigutil import Frame
from rigdef import BONES, GLOW_OF, LEGS, s as spine_point
from motion import ZERO, Curve, breath, buzz, seg, shake, smooth, smoother, wave, window

ANIMS = {
    'idle_loop': (4.8, True), 'attack': (1.95, False), 'whip': (2.75, False),
    'cast': (2.3, False), 'molt': (2.65, False), 'power_up': (2.2, False),
    'hurt': (0.85, False), 'die': (2.8, False), 'revive': (2.2, False), 'summon': (2.2, False),
}
EVENTS = {
    'attack': [('bite_contact', .68)],
    'whip': [('whip_contact', .60), ('whip_ready_second', .88),
             ('whip_contact', 1.12), ('whip_ready_third', 1.40), ('whip_contact', 1.68)],
    'cast': [('reconstruct_release', .76)],
    'molt': [('molt_release', .80)],
    'summon': [('landing', .88)], 'die': [('death_ground', 1.45)],
}
COLD_START = ('revive', 'summon')

_REST = {name: spine_point(position) for name, _, position in BONES}
# Source-pixel hinges that keep every hidden cut edge covered (slack.py): the neck shield and
# collar (carrying the head) dip about their top seam with the shells; the head lifts about a
# point inside the neck shield. Pitches saturate below the measured limits (16 and 12 degrees).
FRONT_HINGE, HEAD_HINGE = (460, 280), (310, 520)
FRONT_LIMIT, HEAD_LIMIT, BLEND = 15.0, 11.0, 4.0
# The far mandible opens about the front of its base; about its own origin it uncovers the head.
JAW_FAR_HINGE = (70, 660)
# Lying pose shared by the end of `die` and the start of `revive`.
SPLAY = {'front': -55.0, 'middle': 87.0, 'rear': 64.0, 'far_front': -24.0, 'far_middle': 37.0, 'far_rear': 40.0}


def C(*keys, period=None):
    return Curve(keys, period)


class F(Frame):
    def __init__(self, t, duration=None, fade=0.3, springs=1.0):
        super().__init__()
        self.t = t
        self.extra['feet'] = {name: [0.0, 0.0, 0.0] for name in LEGS}
        self.glow = {name: 0.0 for name in GLOW_OF}
        self.head_pitch = 0.0
        if duration:
            springs *= 1 - smooth(seg(t, duration - fade, duration))
        self.extra['spring_mix'] = springs

    def add(self, bone, r=0.0, x=0.0, y=0.0):
        self.P.add(bone, r=r, x=x, y=y)

    def pitch(self, degrees):
        """Head pitch against the carapace (+ nose down); done() resolves it onto the safe hinges."""
        self.head_pitch += degrees

    def shudder(self, t0, amp, freq=10.0, decay=6.0):
        """The whole carapace rings after an impact; plates never move against each other."""
        t = self.t
        self.add('body', y=shake(t, t0, amp, freq, decay), x=shake(t, t0, 0.4 * amp, freq * 1.13, decay, 0.9),
                 r=shake(t, t0, 0.15 * amp, freq * 0.91, decay, 1.7))
        self.pitch(shake(t, t0, 0.8 * amp, freq * 1.07, decay, 0.5))

    def foot(self, name, dx=0.0, lift=0.0, tilt=0.0):
        foot = self.extra['feet'][name]
        foot[0] += dx
        foot[1] += max(0.0, lift)
        foot[2] += tilt

    def step(self, name, start, duration, dx=0.0, lift=14.0, tilt=8.0):
        u = seg(self.t, start, start + duration)
        if u <= 0.0:
            return
        arc = math.sin(math.pi * u) ** 2 if u < 1.0 else 0.0
        self.foot(name, dx * smoother(u), lift * arc, tilt * arc)

    def path(self, name, path):
        dx, lift, tilt = path
        t = self.t
        self.foot(name, dx(t), lift(t), tilt(t))

    def feeler(self, side, base, curl=ZERO, delay=0.03, extra=None):
        """Segment i follows the base angle ``i*delay`` later, so swings travel to the tip."""
        t = self.t
        previous = base(t)
        self.add(f'antenna_{side}_0', r=previous + (extra(0) if extra else 0.0))
        for i in range(1, 7):
            angle = base(t - i * delay)
            r = angle - previous + curl(t - 0.5 * i * delay)
            previous = angle
            self.add(f'antenna_{side}_{i}', r=r + (extra(i) if extra else 0.0))

    def shine(self, names, level):
        for name in names:
            self.glow[name] += level

    def hinge(self, bone, r, point):
        """Rotate a bone about a fixed source-pixel point instead of its own origin."""
        hx, hy = spine_point(point) - _REST[bone]
        a = math.radians(r)
        c, s = math.cos(a), math.sin(a)
        self.P.add(bone, r=r, x=hx - (c * hx - s * hy), y=hy - (s * hx + c * hy))

    def done(self):
        # A nose-down pitch dips the whole front; a nose-up pitch lifts only the head. The two
        # share the motion near zero, so the head never jerks when a swing changes direction.
        theta = self.head_pitch
        w = smooth((theta + BLEND) / (2 * BLEND))
        front = FRONT_LIMIT * math.tanh(theta * w / FRONT_LIMIT)
        head = HEAD_LIMIT * math.tanh(theta * (1 - w) / HEAD_LIMIT)
        for bone in ('neck', 'neck_shield', 'collar'):
            self.hinge(bone, front, FRONT_HINGE)
        self.hinge('head', head, HEAD_HINGE)
        jaw = self.P.pop('jaw_far', (0.0, 0.0, 0.0, 1.0, 1.0))
        self.P['jaw_far'] = (0.0,) + tuple(jaw[1:])
        self.hinge('jaw_far', jaw[0], JAW_FAR_HINGE)
        return self


EYE = ('eye_glow',)
FEELERS = ('antenna_near_glow', 'antenna_far_glow')
SHELLS = ('shell_near_glow', 'shell_far_glow')
COLLAR = ('collar_glow',)
ALL_GLOW = EYE + FEELERS + SHELLS + COLLAR


def path(dx, lift, tilt=None):
    return Curve(dx), Curve(lift), Curve(tilt) if tilt else ZERO


def flash(t, t0, rise=0.04, fall=0.45):
    """Glow flash: quick rise at t0, exponential fade."""
    if t < t0 - rise:
        return 0.0
    if t < t0:
        return smooth((t - t0 + rise) / rise)
    return math.exp(-(t - t0) / fall)


# ----------------------------------------------------------------------------- idle
class Idle:
    """Weighted breathing, staccato head and feeler sensing, a mandible click and a foreleg tap."""

    def __init__(self):
        P = 4.8
        self.P = P
        self.head = C((0.00, 0.0), (0.30, -0.8), (0.46, -4.5, 'stop'), (1.18, -5.0), (1.42, -1.8), (1.95, -1.0),
                      (2.50, 1.6), (2.80, 4.0, 'stop'), (3.42, 4.6), (3.68, 1.4), (4.12, -1.0), (4.50, 0.3), period=P)
        # The feelers probe in short held beats. The outer (far) one always leads, or the two fuse into one strand.
        self.near = C((0.00, 0.0), (0.20, 0.5), (0.36, 2.5, 'stop'), (0.95, 3.5), (1.10, -5.0, 'stop'), (1.85, -6.5),
                      (2.18, -3.0), (2.62, 11.0, 'stop'), (3.36, 13.0), (3.56, 4.0, 'stop'), (4.05, -2.5), (4.35, -1.5),
                      period=P)
        self.near_curl = C((0.0, 0.0), (0.36, 1.2), (0.95, 1.6), (1.12, -0.6), (2.62, 2.0), (3.36, 2.4), (3.58, -0.8),
                           (4.0, 0.3), period=P)
        self.far = C((0.00, 0.0), (0.34, 1.0), (0.50, 8.0, 'stop'), (1.06, 9.5), (1.22, 3.0, 'stop'), (1.95, 4.0),
                     (2.30, 3.0), (2.74, 16.0, 'stop'), (3.44, 18.0), (3.64, 5.0, 'stop'), (4.12, 0.0), (4.42, 1.0),
                     period=P)
        self.far_curl = C((0.0, 0.0), (0.50, 1.0), (1.06, 1.4), (1.22, -0.4), (2.74, 2.0), (3.44, 2.4), (3.66, -0.6),
                          (4.2, 0.3), period=P)

    def __call__(self, t):
        P = self.P
        f = F(t)
        b = breath(t, P, 2)
        tap = window(t, 2.62, 2.86, 3.46, 3.78)
        shuffle = window(t, 1.12, 1.28, 1.80, 1.98)
        f.add('body', y=-8.5 * b, x=-3.0 * (0.5 - 0.5 * math.cos(2 * math.pi * t / P)) + 2.5 * tap - 1.5 * shuffle,
              r=0.6 * wave(t, P, 1, 0.9) - 0.5 * tap - 0.3 * breath(t - 0.25, P, 2))
        f.pitch(self.head(t) + 0.3 * self.head(t + 0.07) - 0.6 * breath(t - 0.12, P, 2))
        click = window(t, 1.56, 1.61, 1.63, 1.69) + 0.85 * window(t, 1.73, 1.78, 1.80, 1.87)
        chew = 0.45 * window(t, 3.02, 3.10, 3.14, 3.22) + 0.35 * window(t, 3.26, 3.33, 3.36, 3.44)
        f.add('jaw_near', r=1.4 * b + 10.0 * click + 9.0 * chew)
        f.add('jaw_far', r=-1.0 * b - 7.5 * click - 6.5 * chew)
        f.feeler('near', self.near, self.near_curl, 0.035)
        f.feeler('far', self.far, self.far_curl, 0.035)
        f.step('far_rear', 1.25, 0.62, 0.0, 7.0, -4.0)
        f.step('front', 2.86, 0.30, -8.0, 24.0, 12.0)
        f.step('front', 3.18, 0.30, 8.0, 15.0, 9.0)
        f.shine(EYE, 0.08 + 0.08 * b)
        f.shine(FEELERS, 0.05 + 0.10 * window(t, 0.34, 0.50, 1.00, 1.22) + 0.12 * window(t, 2.50, 2.70, 3.30, 3.60))
        f.shine(SHELLS, 0.05 * b)
        return f.done()


# ----------------------------------------------------------------------------- attack (bite)
class Attack:
    """Rear back with forelegs raised and mandibles gaping, pounce, snap shut at 0.68,
    worry the prey, then walk back in two groups of steps."""

    def __init__(self):
        self.x = C((0, 0), (0.08, -5), (0.18, 10), (0.32, 34, 'stop'), (0.40, 28), (0.68, -112, 'hit'), (0.75, -106),
                   (0.84, -111), (0.98, -108, 'stop'), (1.24, -72), (1.34, -64), (1.58, -18), (1.70, -4), (1.82, 3),
                   (1.95, 0, 'stop'))
        self.y = C((0, 0), (0.10, -5), (0.30, -24, 'stop'), (0.42, -21), (0.68, -30, 'hit'), (0.76, -24), (0.86, -29),
                   (1.00, -27), (1.30, -22), (1.62, -7), (1.80, 1.0), (1.95, 0, 'stop'))
        self.r = C((0, 0), (0.10, 1.2), (0.30, -10, 'stop'), (0.44, -8), (0.68, 3.5, 'hit'), (0.76, 2), (0.86, 3.2),
                   (1.00, 2.8), (1.30, 1.5), (1.65, -1.2), (1.82, 0.4), (1.95, 0, 'stop'))
        self.head = C((0, 0), (0.09, 4), (0.30, -16, 'stop'), (0.46, -13), (0.68, 9, 'hit'), (0.95, 8), (1.10, 5),
                      (1.45, -3), (1.70, 0.8), (1.95, 0, 'stop'))
        self.jaw = C((0, 0), (0.10, 6), (0.26, 50, 'stop'), (0.52, 45), (0.68, -7, 'hit3'), (0.74, -4.5), (0.96, -6),
                     (1.06, 13), (1.26, 4), (1.60, 0, 'stop'))
        # The rearing head carries the feelers back; positive curl keeps the line above the shell.
        self.feel = C((0, 0), (0.09, 12), (0.17, 9), (0.34, -2, 'stop'), (0.42, -1), (0.58, 30), (0.70, 38),
                      (0.82, 22), (0.96, 28), (1.20, 6), (1.45, -6), (1.62, 1.5), (1.72, 0, 'stop'))
        self.curl = C((0, 0), (0.09, 2), (0.34, 9), (0.46, 8), (0.64, 6), (0.74, 8), (0.90, 3), (1.20, -1),
                      (1.50, 0.5), (1.72, 0))
        self.front = path(((0.12, 0), (0.28, 30), (0.50, 24), (0.645, -100, 'hit')),
                          ((0.12, 0), (0.28, 80), (0.50, 72), (0.60, 26), (0.645, 0, 'hit')),
                          ((0.12, 0), (0.28, 26), (0.50, 22), (0.645, 0)))
        self.far_front = path(((0.15, 0), (0.31, 40), (0.52, 34), (0.675, -100, 'hit')),
                              ((0.15, 0), (0.31, 70), (0.52, 62), (0.63, 22), (0.675, 0, 'hit')),
                              ((0.15, 0), (0.31, 18), (0.52, 16), (0.675, 0)))

    def __call__(self, t):
        f = F(t, 1.95, 0.3, 0.7)
        worry = math.sin(2 * math.pi * 6.5 * (t - 0.70)) * window(t, 0.70, 0.73, 0.90, 1.00)
        f.add('body', x=self.x(t), y=self.y(t), r=self.r(t) + 0.8 * worry)
        f.pitch(self.head(t) + 3.5 * worry)
        tremble = 2.0 * buzz(t, 13.0) * window(t, 0.27, 0.31, 0.50, 0.56)
        jaw = self.jaw(t) + tremble
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.74 * jaw)
        f.shudder(0.645, 1.0, 9.0, 7.0)
        f.shudder(0.68, 2.2, 9.5, 5.5)
        f.feeler('near', self.feel, self.curl, 0.03)
        f.feeler('far', lambda u: self.feel(u - 0.03), lambda u: self.curl(u - 0.03), 0.03)
        f.path('front', self.front)
        f.path('far_front', self.far_front)
        f.step('middle', 0.42, 0.20, -85.0, 34.0, 6.0)
        f.step('front', 0.98, 0.24, 100.0, 26.0, 8.0)
        f.step('far_front', 1.02, 0.24, 100.0, 20.0, 6.0)
        f.step('middle', 1.36, 0.26, 85.0, 26.0, 6.0)
        f.shine(EYE, 0.12 + 0.62 * window(t, 0.10, 0.30, 0.72, 1.20))
        f.shine(FEELERS, 0.30 * window(t, 0.12, 0.34, 0.70, 1.10))
        return f.done()


# ----------------------------------------------------------------------------- whip (three hits)
class Whip:
    """Each lash is thrown by the body: rock back and cock the feeler, lurch forward with a
    head snap while the swing travels down the beads to the tip on the hit. The near feeler
    hits at 0.60, the far one at 1.12; both at 1.68 with a foreleg stamp. The cocked poses at
    0.88 and 1.40 may be held while damage resolves."""

    def __init__(self):
        self.x = C((0, 0), (0.10, -3), (0.34, 16, 'stop'), (0.42, 12), (0.60, -26, 'hit'), (0.70, -20), (0.88, 15, 'stop'),
                   (0.94, 11), (1.12, -30, 'hit'), (1.22, -24), (1.40, 22, 'stop'), (1.48, 18), (1.68, -48, 'hit'),
                   (1.80, -42), (1.95, -44), (2.20, -20), (2.45, 3), (2.62, -1), (2.75, 0, 'stop'))
        self.y = C((0, 0), (0.12, -8), (0.34, -22), (0.60, -26, 'hit'), (0.70, -22), (0.88, -21), (1.12, -27, 'hit'),
                   (1.22, -23), (1.40, -19), (1.68, -30, 'hit'), (1.78, -25), (1.95, -28), (2.30, -12), (2.55, 0.8),
                   (2.75, 0, 'stop'))
        # The carapace and head stop just before each contact and hand their speed to the feeler.
        self.r = C((0, 0), (0.12, 0.8), (0.34, -5, 'stop'), (0.42, -4), (0.55, 4, 'hit'), (0.70, 2), (0.88, -6, 'stop'),
                   (0.94, -5), (1.07, 4.5, 'hit'), (1.22, 2.5), (1.40, -10.5, 'stop'), (1.48, -9), (1.63, 5, 'hit'), (1.78, 3.5),
                   (1.95, 4), (2.30, 1), (2.55, -0.8), (2.75, 0, 'stop'))
        self.head = C((0, 0), (0.34, -12), (0.44, -10), (0.57, 7, 'hit'), (0.72, 4), (0.88, -13, 'stop'), (0.96, -11),
                      (1.09, 8, 'hit'), (1.24, 5), (1.40, -16, 'stop'), (1.48, -14), (1.65, 10, 'hit'), (1.85, 7),
                      (2.20, 1), (2.50, -1.5), (2.75, 0, 'stop'))
        self.jaw = C((0, 0), (0.34, 22), (0.60, 3, 'hit'), (0.88, 20), (1.12, 3, 'hit'), (1.40, 30), (1.68, 0, 'hit'),
                     (1.90, 8), (2.30, 0, 'stop'))
        # At the hit the feeler straightens toward the party: the root turns about +45 and the
        # per-joint curl unbends the arc, so the whole line points forward just above the floor.
        self.near = C((0, 0), (0.10, 6), (0.34, -30, 'stop'), (0.52, 38), (0.60, 46, 'stop'), (0.70, 34),
                      (0.82, 6), (0.96, 12), (1.20, -6), (1.40, -18, 'stop'), (1.60, 36), (1.68, 42, 'stop'),
                      (1.80, 30), (1.95, 14), (2.20, -6), (2.40, 3), (2.52, 0, 'stop'))
        self.near_curl = C((0, 0), (0.34, 5), (0.58, 14), (0.62, 18), (0.74, 8), (0.96, 2), (1.40, 6),
                           (1.64, 15), (1.70, 18), (1.82, 8), (2.10, -1.5), (2.40, 0.3), (2.55, 0))
        self.far = C((0, 0), (0.30, -8), (0.50, -4), (0.60, 8), (0.72, -12), (0.88, -28, 'stop'), (1.05, 33),
                     (1.12, 40, 'stop'), (1.22, 28), (1.30, 10), (1.40, -20, 'stop'), (1.60, 30),
                     (1.68, 36, 'stop'), (1.80, 26), (1.95, 12), (2.22, -8), (2.42, 3), (2.55, 0, 'stop'))
        self.far_curl = C((0, 0), (0.60, 1), (0.88, 5), (1.10, 14), (1.14, 18), (1.26, 8), (1.40, 6),
                          (1.64, 15), (1.70, 18), (1.84, 8), (2.12, -1.5), (2.42, 0.3), (2.55, 0))
        self.front_raise = path(((1.20, 0), (1.36, 8), (1.46, 4), (1.66, -28, 'hit')),
                                ((1.20, 0), (1.36, 52), (1.46, 48), (1.60, 16), (1.66, 0, 'hit')),
                                ((1.20, 0), (1.36, 18), (1.46, 16), (1.66, 0)))
        self.far_raise = path(((1.22, 0), (1.38, 10), (1.48, 6), (1.675, -30, 'hit')),
                              ((1.22, 0), (1.38, 40), (1.48, 36), (1.62, 12), (1.675, 0, 'hit')),
                              ((1.22, 0), (1.38, 14), (1.48, 12), (1.675, 0)))

    def __call__(self, t):
        f = F(t, 2.75, 0.3, 0.45)
        f.add('body', x=self.x(t), y=self.y(t), r=self.r(t))
        f.pitch(self.head(t))
        jaw = self.jaw(t)
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.75 * jaw)
        for hit, amp in ((0.60, 1.4), (1.12, 1.5), (1.68, 2.4)):
            f.shudder(hit, amp, 10.0, 6.5)
        f.feeler('near', self.near, self.near_curl, 0.022)
        f.feeler('far', self.far, self.far_curl, 0.022)
        f.step('front', 0.02, 0.20, 20.0, 18.0, 6.0)
        f.step('far_front', 0.12, 0.22, 26.0, 16.0, 5.0)
        f.path('front', self.front_raise)
        f.path('far_front', self.far_raise)
        f.step('front', 2.20, 0.24, 8.0, 14.0, 5.0)
        f.step('far_front', 2.28, 0.24, 4.0, 10.0, 4.0)
        lashes = sum(window(t, c - 0.16, c - 0.02, c + 0.04, c + 0.30) for c in (0.60, 1.12))
        f.shine(FEELERS, 0.12 + 0.55 * lashes + 0.9 * window(t, 1.50, 1.64, 1.72, 2.05))
        f.shine(EYE, 0.10 + 0.25 * lashes + 0.5 * window(t, 1.40, 1.60, 1.75, 2.10))
        return f.done()


# ----------------------------------------------------------------------------- cast (reconstruct)
class Cast:
    """Resonance: gather, rise with feelers spread and humming, dip, then thrust both feelers
    at the party on the release (0.76) with a glow burst; broadcast, then stand tall."""

    def __init__(self):
        self.x = C((0, 0), (0.22, 3), (0.62, 6), (0.72, 4), (0.76, -14, 'hit'), (0.86, -10), (1.10, -18), (1.45, -12),
                   (1.75, -3), (2.05, 1), (2.30, 0, 'stop'))
        self.y = C((0, 0), (0.20, -16, 'stop'), (0.55, -5), (0.62, -6), (0.72, -18, 'stop'), (0.76, -17), (0.84, -22),
                   (1.00, -19), (1.40, -16), (1.75, -3), (2.00, 0.5), (2.30, 0, 'stop'))
        self.r = C((0, 0), (0.20, 2.5), (0.58, -6, 'stop'), (0.66, -5), (0.72, -2), (0.76, 3.5, 'hit'), (0.86, 2.5),
                   (1.20, 3), (1.45, 2), (1.75, -1.5), (2.05, 0.3), (2.30, 0, 'stop'))
        # The pronotum dips about a seam far behind the head, so the nod lands just after the
        # release instead of snapping onto it.
        self.head = C((0, 0), (0.20, 6), (0.58, -15, 'stop'), (0.66, -13), (0.72, -6), (0.80, 6), (0.92, 5),
                      (1.40, 6), (1.70, -8), (2.00, -1), (2.30, 0, 'stop'))
        self.jaw = C((0, 0), (0.20, -2), (0.55, 26), (0.66, 28), (0.72, 2), (0.80, 32), (1.30, 26), (1.60, 3),
                     (2.00, 0, 'stop'))
        self.near = C((0, 0), (0.20, -18, 'stop'), (0.30, -14), (0.55, 30), (0.66, 32), (0.72, 20, 'stop'),
                      (0.80, 74, 'hit'), (1.40, 66), (1.75, -6), (1.95, 2), (2.08, 0, 'stop'))
        self.near_curl = C((0, 0), (0.20, -4), (0.55, 3), (0.72, 0), (0.80, 12), (1.40, 11), (1.75, -3), (1.95, 0.5),
                           (2.08, 0))
        self.far = C((0, 0), (0.20, -12, 'stop'), (0.30, -10), (0.55, -10), (0.66, -12), (0.72, -8, 'stop'),
                     (0.80, 48, 'hit'), (1.40, 42), (1.75, -2), (1.95, 2), (2.10, 0, 'stop'))
        self.far_curl = C((0, 0), (0.20, 0), (0.55, 4), (0.72, 3), (0.80, 12), (1.40, 10), (1.75, -4), (1.98, 0.5),
                          (2.10, 0))

    def __call__(self, t):
        f = F(t, 2.3, 0.3, 0.6)
        f.add('body', x=self.x(t), y=self.y(t), r=self.r(t))
        hum = 0.6 * buzz(t, 21.0) * window(t, 0.30, 0.62, 0.66, 0.72)
        f.pitch(self.head(t) + 1.2 * hum)
        jaw = self.jaw(t) + 3.0 * hum
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.72 * jaw)
        f.shudder(0.76, 2.0, 9.0, 4.5)
        swell = window(t, 1.45, 1.72, 1.88, 2.20)
        resonance = 3.2 * window(t, 0.30, 0.62, 0.66, 0.72) + 2.4 * window(t, 0.78, 0.82, 0.95, 1.45)

        def vibrate(phase):
            return lambda i: resonance * math.sin(2 * math.pi * 13.0 * t - 0.9 * i + phase)

        f.feeler('near', self.near, self.near_curl, 0.03, vibrate(0.0))
        f.feeler('far', self.far, self.far_curl, 0.03, vibrate(1.7))
        f.step('front', 0.02, 0.18, 18.0, 16.0, 6.0)
        f.step('far_front', 0.06, 0.18, 24.0, 14.0, 5.0)
        f.step('front', 1.95, 0.22, -18.0, 14.0, 5.0)
        f.step('far_front', 2.02, 0.22, -24.0, 12.0, 4.0)
        charge = smooth(seg(t, 0.20, 0.72))
        f.shine(EYE, 0.15 + 0.55 * smooth(seg(t, 0.15, 0.55)) * (1 - smooth(seg(t, 0.90, 1.70))) + 0.4 * flash(t, 0.76))
        f.shine(FEELERS, 0.65 * smooth(seg(t, 0.28, 0.66)) * (1 - smooth(seg(t, 0.80, 1.60))) + 0.7 * flash(t, 0.77, 0.04, 0.55))
        f.shine(COLLAR, 0.45 * smooth(seg(t, 0.40, 0.72)) * (1 - smooth(seg(t, 0.80, 1.40))) + 0.8 * flash(t, 0.78, 0.04, 0.4))
        f.shine(SHELLS, 0.30 * charge * (1 - smooth(seg(t, 0.80, 1.30))) + 0.9 * flash(t, 0.79, 0.05, 0.4) + 0.45 * swell)
        return f.done()


# ----------------------------------------------------------------------------- molt
class Molt:
    """Brace wide and low, heave twice while the armour strains and its plates light up, then
    clamp down on the release (0.80) with a flash and a shudder; rise and settle the stance."""

    def __init__(self):
        self.y = C((0, 0), (0.10, -8), (0.30, -40, 'stop'), (0.44, -22), (0.52, -34), (0.72, -14, 'stop'),
                   (0.80, -30, 'hit'), (0.86, -36), (0.95, -31), (1.10, -33), (1.55, -4), (1.75, -1), (1.95, -6),
                   (2.20, -2), (2.45, 0.5), (2.65, 0, 'stop'))
        self.x = C((0, 0), (0.30, 4), (0.72, -2), (0.80, 3), (1.10, 0), (2.65, 0))
        self.r = C((0, 0), (0.30, 1.5), (0.44, -1.5), (0.52, 1), (0.72, -3), (0.80, 2, 'hit'), (0.95, 0.5), (1.55, -2.5),
                   (1.90, 0.5), (2.30, -0.3), (2.65, 0, 'stop'))
        self.head = C((0, 0), (0.30, 10), (0.44, 4), (0.52, 8), (0.72, -4), (0.82, 8), (0.94, 6), (1.50, -12),
                      (1.75, -10), (2.10, 1), (2.40, -0.5), (2.65, 0, 'stop'))
        self.jaw = C((0, 0), (0.30, -3), (0.72, 22), (0.80, -4, 'hit'), (0.88, 10), (1.00, 2), (1.50, 16), (1.80, 2),
                     (2.20, 0, 'stop'))
        self.near = C((0, 0), (0.30, 16, 'stop'), (0.74, 18), (0.80, 20), (0.88, 6), (1.00, -6), (1.15, 2),
                      (1.50, 4), (1.80, 4), (2.15, -1), (2.32, 0.4), (2.42, 0, 'stop'))
        self.far = C((0, 0), (0.32, 26, 'stop'), (0.74, 28), (0.80, 30), (0.89, 16), (1.02, 2), (1.18, 10),
                     (1.50, 14), (1.80, 12), (2.15, 2), (2.32, -0.4), (2.42, 0, 'stop'))
        self.curl = C((0, 0), (0.30, 3), (0.74, 3), (0.88, 4), (1.10, -1), (1.50, 2), (2.10, 0), (2.40, 0))

    def __call__(self, t):
        f = F(t, 2.65, 0.3, 0.6)
        strain = smooth(seg(t, 0.30, 0.78)) * (1 - smooth(seg(t, 0.79, 0.81)))
        f.add('body', x=self.x(t) + 1.2 * strain * buzz(t, 19.0, 1.1), y=self.y(t) + 1.8 * strain * buzz(t, 17.0, 0.4),
              r=self.r(t) + 0.25 * strain * buzz(t, 15.0, 2.2))
        f.pitch(self.head(t) + 1.5 * strain * buzz(t, 16.0, 0.4))
        jaw = self.jaw(t)
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.74 * jaw)
        f.shudder(0.80, 3.0, 12.0, 4.5)
        f.feeler('near', self.near, self.curl, 0.032, lambda i: 1.6 * strain * math.sin(2 * math.pi * 15.0 * t - 0.9 * i))
        f.feeler('far', self.far, lambda u: self.curl(u - 0.02), 0.032,
                 lambda i: 1.6 * strain * math.sin(2 * math.pi * 14.0 * t - 0.9 * i + 1.3))
        # The stance widens while the body is low; the feet come back in before it rises again.
        for name, dx in (('front', -10.0), ('far_middle', -10.0), ('rear', 28.0)):
            f.step(name, 0.00, 0.18, dx, 16.0, 5.0)
            f.step(name, 1.26, 0.22, -dx, 14.0, 4.0)
        for name, dx in (('middle', 12.0), ('far_rear', 10.0)):
            f.step(name, 0.18, 0.18, dx, 14.0, 4.0)
            f.step(name, 1.46, 0.22, -dx, 12.0, 4.0)
        f.shine(SHELLS, 0.55 * smooth(seg(t, 0.30, 0.78)) * (1 - smooth(seg(t, 0.79, 0.82)))
                + flash(t, 0.80, 0.02, 0.45) + 0.45 * window(t, 1.30, 1.50, 1.60, 1.95))
        f.shine(COLLAR, 0.35 * smooth(seg(t, 0.40, 0.78)) * (1 - smooth(seg(t, 0.79, 0.82)))
                + 0.9 * flash(t, 0.81, 0.02, 0.4) + 0.35 * window(t, 1.30, 1.50, 1.60, 1.95))
        f.shine(EYE, 0.12 + 0.25 * smooth(seg(t, 0.30, 0.78)) + 0.5 * flash(t, 0.80) - 0.25 * smooth(seg(t, 1.0, 1.6)))
        f.shine(FEELERS, 0.35 * flash(t, 0.82, 0.03, 0.5))
        return f.done()


# ----------------------------------------------------------------------------- power_up
class PowerUp:
    """Head down and back arched, feelers forward like horns; the near legs stamp in a rolling
    cascade while the glow climbs from eye to tail; the head throws up at the peak, then a shake."""

    def __init__(self):
        self.y = C((0, 0), (0.28, -20, 'stop'), (0.95, -18), (1.12, -16), (1.25, -17), (1.60, -6), (1.90, 0.6),
                   (2.20, 0, 'stop'))
        self.r = C((0, 0), (0.28, 3.5, 'stop'), (0.95, 3), (1.12, -2.5, 'hit'), (1.25, -2), (1.60, 0.6), (1.90, -0.3),
                   (2.20, 0, 'stop'))
        self.head = C((0, 0), (0.28, 9, 'stop'), (0.95, 8), (1.12, -16, 'stop'), (1.25, -14), (1.60, -4), (1.90, 1),
                      (2.20, 0, 'stop'))
        self.jaw = C((0, 0), (0.28, 8), (0.45, 8), (0.50, -2), (0.60, 10), (0.70, -2), (0.95, 6), (1.12, 34, 'hit'),
                     (1.30, 30), (1.55, 4), (1.90, 0, 'stop'))
        self.near = C((0, 0), (0.28, 30, 'stop'), (0.86, 32), (1.12, 2, 'stop'), (1.26, 6), (1.44, 6), (1.60, -3),
                      (1.85, 1), (2.00, 0, 'stop'))
        self.far = C((0, 0), (0.30, 24, 'stop'), (0.86, 26), (1.14, -4, 'stop'), (1.28, 0), (1.46, 4), (1.62, -2),
                     (1.88, 0.5), (2.00, 0, 'stop'))
        self.curl = C((0, 0), (0.28, 9), (0.86, 10), (1.14, 10), (1.30, 6), (1.50, 1), (1.95, 0))

    def __call__(self, t):
        f = F(t, 2.2, 0.3, 0.6)
        stamps = (0.50, 0.68, 0.86)
        dip = sum(-4.5 * math.exp(-((t - c - 0.03) / 0.05) ** 2) for c in stamps)
        shudder = window(t, 1.40, 1.44, 1.54, 1.64)
        f.add('body', y=self.y(t) + dip, r=self.r(t), x=2.5 * shudder * buzz(t, 14.0))
        f.pitch(self.head(t) + 2.5 * shudder * buzz(t, 13.0, 1.1))
        jaw = self.jaw(t)
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.74 * jaw)
        for c in stamps:
            f.shudder(c, 1.2, 11.0, 7.0)
        f.shudder(1.42, 2.2, 13.0, 9.0)
        f.feeler('near', self.near, self.curl, 0.03, lambda i: 3.0 * shudder * math.sin(2 * math.pi * 12 * t - i))
        f.feeler('far', self.far, lambda u: self.curl(u - 0.02), 0.03,
                 lambda i: 3.0 * shudder * math.sin(2 * math.pi * 11 * t - i + 1.3))
        for name, c in zip(('front', 'middle', 'rear'), stamps):
            f.step(name, c - 0.18, 0.18, 0.0, 32.0 if name != 'middle' else 26.0, 10.0)
        for i, (names, peak) in enumerate(((EYE, 0.45), (COLLAR, 0.60), (('shell_near_glow',), 0.75),
                                            (('shell_far_glow',), 0.90))):
            f.shine(names, 0.5 * window(t, 0.30 + 0.12 * i, peak, peak + 0.2, 0.95 + 0.1 * i) + flash(t, 1.12, 0.06, 0.40))
        f.shine(FEELERS, 0.25 * window(t, 0.30, 0.50, 0.90, 1.05) + 0.8 * flash(t, 1.13, 0.06, 0.40))
        return f.done()


# ----------------------------------------------------------------------------- hurt
class Hurt:
    """Knocked back and up from the party's side; forelegs skid, the carapace rattles, then a
    weighted recovery with overshoot."""

    def __init__(self):
        self.x = C((0, 0, 'go'), (0.07, 21), (0.20, 26), (0.34, 14), (0.52, -4), (0.68, 1.5), (0.85, 0, 'stop'))
        self.y = C((0, 0), (0.07, -6), (0.22, -17), (0.40, -12), (0.60, 1.0), (0.72, -1), (0.85, 0, 'stop'))
        self.r = C((0, 0, 'go'), (0.06, -4), (0.20, -3), (0.36, 1.8), (0.55, -0.8), (0.85, 0, 'stop'))
        self.head = C((0, 0, 'go'), (0.06, -13), (0.16, -10), (0.34, 5), (0.52, -2), (0.70, 0.5), (0.85, 0, 'stop'))
        self.jaw = C((0, 0, 'go'), (0.06, 24), (0.20, 14), (0.36, -3), (0.55, 1), (0.75, 0, 'stop'))
        self.feel = C((0, 0), (0.05, 10), (0.16, -20), (0.34, 10), (0.50, -4), (0.58, 1), (0.62, 0, 'stop'))
        # The forelegs are nearly straight at rest, so they are dragged along with the carapace.
        self.front = path(((0, 0, 'go'), (0.07, 21), (0.20, 25)), ((0, 0), (0.03, 12), (0.07, 18), (0.14, 7), (0.20, 0)),
                          ((0, 0), (0.08, 10), (0.20, 0)))
        self.far_front = path(((0, 0, 'go'), (0.07, 21), (0.22, 26)), ((0, 0), (0.03, 16), (0.07, 26), (0.15, 8), (0.22, 0)),
                              ((0, 0), (0.10, 8), (0.22, 0)))

    def __call__(self, t):
        f = F(t, 0.85, 0.2, 0.8)
        f.add('body', x=self.x(t), y=self.y(t), r=self.r(t))
        f.pitch(self.head(t))
        jaw = self.jaw(t)
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.72 * jaw)
        f.shudder(0.02, 2.2, 14.0, 9.0)
        f.feeler('near', self.feel, ZERO, 0.03)
        f.feeler('far', lambda u: 0.85 * self.feel(u - 0.025), ZERO, 0.03)
        f.path('front', self.front)
        f.path('far_front', self.far_front)
        f.step('front', 0.38, 0.20, -25.0, 14.0, 6.0)
        f.step('far_front', 0.46, 0.20, -26.0, 12.0, 5.0)
        hit = flash(t, 0.03, 0.03, 0.10)
        f.shine(EYE, 0.6 * hit)
        f.tint = (1.0, 1.0 - 0.16 * hit, 1.0 - 0.20 * hit, 1.0)
        return f.done()


# ----------------------------------------------------------------------------- die / revive
def corpse(f, body=0.0, feet=0.0, head=0.0, feelers=0.0, dark=0.0):
    """Lying pose. Each factor runs 0 (standing) -> 1 (lying) so the collapse can be staged;
    ``feet`` may be a per-leg dict."""
    f.add('body', y=-74 * body, r=-1.0 * body)
    f.pitch(10.0 * head)
    f.add('jaw_near', r=15 * head)
    f.add('jaw_far', r=-9 * head)
    for name, amount in SPLAY.items():
        f.extra['feet'][name][0] += amount * (feet[name] if isinstance(feet, dict) else feet)
    for side, strength in (('near', 1.0), ('far', 0.85)):
        for i in range(7):
            angle = 115 if i == 0 else (12 if i < 5 else 8)
            f.add(f'antenna_{side}_{i}', r=angle * feelers * strength)
    f.extra['antenna_floor'] = True
    f.tint = (1 - 0.35 * dark, 1 - 0.36 * dark, 1 - 0.31 * dark, 1)


class Die:
    """Death blow, a buckling stagger and one last push, then the legs give way and the
    carapace hits the floor at 1.45; a bounce, two dying twitches and the glow goes out."""

    def __init__(self):
        self.fall = C((0, 0), (0.25, 0), (0.65, 0.20), (0.72, 0.20), (0.92, 0.12), (1.05, 0.15), (1.45, 1.0, 'hit'))
        self.feet = C((0, 0), (0.30, 0), (0.65, 0.16), (1.05, 0.22), (1.45, 1.0, 'hit'))
        self.head = C((0, 0), (0.25, -0.6), (0.65, 0.55), (0.80, 0.25), (0.95, 0.20), (1.05, 0.3), (1.38, 1.0, 'hit'))
        self.feelers = C((0, 0), (0.25, -0.12), (0.65, 0.30), (0.85, 0.22), (1.05, 0.30), (1.48, 1.0, 'hit'))
        self.x = C((0, 0, 'go'), (0.08, 20), (0.25, 22), (0.65, -6), (0.95, -2), (1.45, 0))
        self.r = C((0, 0, 'go'), (0.08, -4), (0.25, -3), (0.65, 6), (0.80, 4), (0.95, 2), (1.10, 3), (1.45, 0, 'hit'))
        # The straight forelegs are dragged by the blow, then stumble back under the body.
        self.front = path(((0, 0, 'go'), (0.08, 20), (0.25, 22), (0.34, 22), (0.56, 0)),
                          ((0, 0), (0.04, 16), (0.08, 20), (0.16, 8), (0.25, 0), (0.34, 0), (0.45, 14), (0.56, 0)))
        self.far_front = path(((0, 0, 'go'), (0.08, 20), (0.25, 22), (0.38, 22), (0.60, 0)),
                              ((0, 0), (0.04, 20), (0.08, 28), (0.16, 10), (0.25, 0), (0.38, 0), (0.49, 12), (0.60, 0)))

    def __call__(self, t):
        f = F(t)
        corpse(f, self.fall(t), self.feet(t), self.head(t), self.feelers(t), smooth(seg(t, 1.0, 2.45)))
        tremble = window(t, 0.30, 0.40, 0.95, 1.05)
        f.add('body', x=self.x(t) + 1.5 * tremble * buzz(t, 9.0), r=self.r(t))
        f.add('jaw_near', r=20 * window(t, 0.0, 0.06, 0.20, 0.60))
        f.add('jaw_far', r=-14 * window(t, 0.0, 0.06, 0.20, 0.60))
        bounce = shake(t, 1.45, 1.0, 3.2, 6.5) * (1 - smooth(seg(t, 2.05, 2.40)))
        f.add('body', y=7.0 * abs(bounce))
        f.pitch(-2.5 * abs(bounce))
        f.shudder(1.45, 2.0, 10.0, 6.0)
        f.shudder(0.02, 1.6, 14.0, 7.0)
        f.path('front', self.front)
        f.path('far_front', self.far_front)
        f.step('front', 1.98, 0.28, 0.0, 6.0, 9.0)
        f.step('middle', 2.20, 0.22, 0.0, 5.0, 6.0)
        # The life goes out of the feelers first: the beads sag and quiver while the body still fights.
        sag = window(t, 0.40, 0.70, 1.10, 1.42)
        for side, phase in (('near', 0.0), ('far', 1.1)):
            for i in range(1, 7):
                f.add(f'antenna_{side}_{i}', r=6.5 * sag + 1.2 * tremble * math.sin(2 * math.pi * 7.0 * t - 0.8 * i + phase))
        f.add('antenna_near_5', r=6 * window(t, 2.30, 2.36, 2.40, 2.48))
        f.add('antenna_near_6', r=9 * window(t, 2.30, 2.36, 2.40, 2.48))
        f.extra['spring_mix'] = 1 - smooth(seg(t, 1.8, 2.45))
        eye = 0.6 * flash(t, 0.04, 0.03, 0.12) + 0.35 * window(t, 0.62, 0.75, 0.90, 1.10) \
            + 0.15 * window(t, 1.95, 2.0, 2.05, 2.25)
        f.shine(EYE, eye * (1 - smooth(seg(t, 2.25, 2.45))))
        f.shine(FEELERS, 0.3 * flash(t, 0.04, 0.03, 0.15))
        f.tint = tuple(v * (1 - (0.14 if i == 1 else 0.18 if i == 2 else 0) * flash(t, 0.03, 0.03, 0.12))
                       for i, v in enumerate(f.tint))
        return f.done()


class Revive:
    """From the lying pose: the eye relights, legs draw in one by one, the carapace heaves up
    rear-first, then shakes itself out and settles."""

    # Each leg draws in with its own lifted step; the last two finish once the body is up.
    # The middle leg's short femur pinches if its foot comes in while the body is still down,
    # so it only takes part of the distance first and finishes during the rise.
    GATHER = (('front', 0.35, 0.8), ('far_front', 0.42, 0.8), ('rear', 0.50, 0.8), ('far_rear', 0.58, 0.8),
              ('middle', 0.66, 0.45), ('far_middle', 0.74, 0.8), ('middle', 1.02, 0.55), ('front', 1.12, 0.2),
              ('far_front', 1.18, 0.2), ('rear', 1.24, 0.2), ('far_rear', 1.30, 0.2), ('far_middle', 1.36, 0.2))

    def __init__(self):
        self.body = C((0, 1.0), (0.45, 1.0), (0.70, 0.82), (0.86, 0.86), (1.20, 0.20), (1.38, 0.04), (1.55, 0.0))
        self.head = C((0, 1.0), (0.85, 1.0), (1.30, -0.35), (1.55, 0.05), (1.75, 0.0))
        self.feelers = C((0, 1.0), (0.70, 1.0), (0.90, 0.82), (1.30, 0.06), (1.46, -0.10), (1.66, 0.03), (1.84, 0.0))
        self.pitch = C((0, 0), (0.45, 0), (0.70, 4), (0.86, 5), (1.10, 2), (1.30, -3), (1.50, 0.5), (1.70, 0))

    def __call__(self, t):
        f = F(t)
        feet = dict.fromkeys(SPLAY, 1.0)
        for name, start, share in self.GATHER:
            u = seg(t, start, start + 0.20)
            feet[name] -= share * smoother(u)
            f.foot(name, lift=(13.0 if share > 0.5 else 9.0) * (math.sin(math.pi * u) ** 2 if 0 < u < 1 else 0.0),
                   tilt=8.0 * (math.sin(math.pi * u) ** 2 if 0 < u < 1 else 0.0))
        corpse(f, self.body(t), feet, self.head(t), self.feelers(t), 1 - smooth(seg(t, 0.25, 1.50)))
        f.add('body', r=self.pitch(t))
        jolt = sum(math.exp(-((t - c) / 0.04) ** 2) for c in (0.45, 0.60, 0.74))
        f.add('body', x=-2.5 * jolt)
        shudder = window(t, 1.42, 1.48, 1.62, 1.76)
        f.add('body', x=3.0 * shudder * buzz(t, 13.0))
        f.pitch(3.0 * shudder * buzz(t, 12.0, 0.8))
        f.shudder(1.45, 2.0, 13.0, 9.0)
        f.add('antenna_near_6', r=10 * window(t, 0.22, 0.27, 0.30, 0.38))
        f.extra['spring_mix'] = smooth(seg(t, 0.9, 1.3)) * (1 - smooth(seg(t, 1.85, 2.2)))
        f.shine(EYE, 0.6 * window(t, 0.05, 0.40, 1.55, 2.0) + 0.3 * flash(t, 0.20, 0.03, 0.15))
        f.shine(FEELERS, 0.5 * window(t, 0.90, 1.30, 1.60, 2.0))
        f.shine(SHELLS + COLLAR, 0.45 * window(t, 1.10, 1.45, 1.60, 2.0))
        return f.done()


# ----------------------------------------------------------------------------- summon
class Summon:
    """Entrance: coil low, rear high with forelegs pawing and mandibles wide, slam down on the
    landing (0.88), shake the head like a bull, rise to stance."""

    def __init__(self):
        self.y = C((0, 0), (0.28, -30, 'stop'), (0.62, -14), (0.70, -12), (0.88, -30, 'hit'), (0.96, -24), (1.06, -27),
                   (1.30, -22), (1.75, -4), (2.00, 0.5), (2.20, 0, 'stop'))
        self.r = C((0, 0), (0.28, 4, 'stop'), (0.62, -12, 'stop'), (0.70, -11), (0.88, 3.5, 'hit'), (0.96, 2),
                   (1.06, 3), (1.30, 2), (1.75, -0.8), (2.00, 0.2), (2.20, 0, 'stop'))
        self.x = C((0, 0), (0.28, -4), (0.62, 12), (0.70, 10), (0.88, -16, 'hit'), (1.00, -12), (1.30, -14),
                   (1.75, -2), (2.05, 0.5), (2.20, 0, 'stop'))
        self.head = C((0, 0), (0.28, 10), (0.62, -16, 'stop'), (0.70, -14), (0.88, 8, 'hit'), (1.00, 6), (1.40, 3),
                      (1.80, -1.5), (2.20, 0, 'stop'))
        self.jaw = C((0, 0), (0.28, -2), (0.62, 34), (0.84, 30), (0.88, 2, 'hit'), (1.00, 14), (1.10, 2), (1.20, 12),
                     (1.32, 1), (1.70, 0, 'stop'))
        self.feel = C((0, 0), (0.28, -16), (0.62, -4, 'stop'), (0.72, -2), (0.86, 30), (0.94, 40), (1.06, 4),
                      (1.20, 14), (1.40, -4), (1.60, 2), (1.75, 0, 'stop'))
        self.curl = C((0, 0), (0.28, -3), (0.62, 9), (0.88, 8), (1.10, 1), (1.40, -1), (1.75, 0))
        # Forelegs (already drawn back 20/24 by the coil steps) rise, paw the air twice and stamp
        # down ahead of their rest spot on the landing.
        self.front = path(((0.30, 0), (0.46, 6), (0.66, 4), (0.88, -44, 'hit')),
                          ((0.30, 0), (0.44, 80), (0.50, 66), (0.56, 84), (0.62, 68), (0.68, 82), (0.76, 60),
                           (0.84, 22), (0.88, 0, 'hit')),
                          ((0.30, 0), (0.44, 24), (0.70, 20), (0.88, 0)))
        self.far_front = path(((0.32, 0), (0.48, 8), (0.68, 6), (0.88, -48, 'hit')),
                              ((0.32, 0), (0.47, 60), (0.53, 48), (0.59, 64), (0.65, 50), (0.71, 62), (0.78, 44),
                               (0.85, 16), (0.88, 0, 'hit')),
                              ((0.32, 0), (0.48, 16), (0.72, 14), (0.88, 0)))

    def __call__(self, t):
        f = F(t, 2.2, 0.3, 0.6)
        f.add('body', x=self.x(t), y=self.y(t), r=self.r(t))
        bull = math.sin(2 * math.pi * 5.0 * (t - 0.96)) * window(t, 0.94, 1.00, 1.24, 1.36)
        f.pitch(self.head(t) + 7.0 * bull)
        jaw = self.jaw(t)
        f.add('jaw_near', r=jaw)
        f.add('jaw_far', r=-0.74 * jaw)
        f.shudder(0.88, 3.0, 10.0, 6.0)
        f.feeler('near', self.feel, self.curl, 0.03)
        f.feeler('far', lambda u: self.feel(u - 0.03), lambda u: self.curl(u - 0.03), 0.03)
        f.step('front', 0.04, 0.18, 20.0, 16.0, 5.0)
        f.step('far_front', 0.08, 0.18, 24.0, 14.0, 5.0)
        f.path('front', self.front)
        f.path('far_front', self.far_front)
        f.step('front', 1.18, 0.24, 24.0, 16.0, 5.0)
        f.step('far_front', 1.28, 0.24, 24.0, 12.0, 4.0)
        f.shine(EYE, 0.5 * window(t, 0.20, 0.55, 0.95, 1.6) + 0.5 * flash(t, 0.88, 0.03, 0.35))
        f.shine(FEELERS, 0.35 * window(t, 0.30, 0.62, 0.90, 1.4))
        f.shine(SHELLS + COLLAR, 0.6 * flash(t, 0.89, 0.03, 0.35))
        return f.done()


FUNCS = {'idle_loop': Idle(), 'attack': Attack(), 'whip': Whip(), 'cast': Cast(), 'molt': Molt(),
         'power_up': PowerUp(), 'hurt': Hurt(), 'die': Die(), 'revive': Revive(), 'summon': Summon()}
