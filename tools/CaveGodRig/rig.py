"""Spine 4.2 forward kinematics and arm IK for the 活体巨岩 rig (all bones use the normal inherit mode)."""
from __future__ import annotations

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "source_assets/monsters/cave_god_motion/contact_base.spjson"

# Local channel order used everywhere: rotation, x, y, scaleX, scaleY, shearX, shearY.
ROT, X, Y, SX, SY, SHX, SHY = range(7)

# arm1 is screen-left, arm2 screen-right. The elbow always bends outward; this matches the
# explicit IK directions written by scripts/build_cavegod_contact.mjs (arm1 positive, arm2 negative).
ARMS = {
    1: {"shoulder": "shoulder1", "upper": "arm1_1", "fore": "arm1_2", "hand": "arm1_3", "fist": "fist1", "bend": -1.0},
    2: {"shoulder": "shoulder2", "upper": "arm2_1", "fore": "arm2_2", "hand": "arm2_3", "fist": "fist2", "bend": 1.0},
}


def load_source(path: Path = SOURCE) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


class Rig:
    def __init__(self, data: dict):
        self.names: list[str] = []
        self.parent: dict[str, str | None] = {}
        self.setup: dict[str, list[float]] = {}
        self.length: dict[str, float] = {}
        for bone in data["bones"]:
            name = bone["name"]
            if bone.get("inherit", bone.get("transform", "normal")) != "normal":
                raise ValueError(f"{name}: only normal inheritance is supported")
            self.names.append(name)
            self.parent[name] = bone.get("parent")
            self.length[name] = bone.get("length", 0.0)
            self.setup[name] = [bone.get("rotation", 0.0), bone.get("x", 0.0), bone.get("y", 0.0),
                                bone.get("scaleX", 1.0), bone.get("scaleY", 1.0),
                                bone.get("shearX", 0.0), bone.get("shearY", 0.0)]

    def world(self, local: dict[str, list[float]], upto: str | None = None) -> dict[str, tuple]:
        """World matrices (a, b, c, d, worldX, worldY) in skeleton space, Y up."""
        out: dict[str, tuple] = {}
        for name in self.names:
            rot, x, y, sx, sy, shx, shy = local.get(name, self.setup[name])
            ra = math.radians(rot + shx)
            rb = math.radians(rot + 90.0 + shy)
            la, lb = math.cos(ra) * sx, math.cos(rb) * sy
            lc, ld = math.sin(ra) * sx, math.sin(rb) * sy
            parent = self.parent[name]
            if parent is None:
                out[name] = (la, lb, lc, ld, x, y)
            else:
                pa, pb, pc, pd, px, py = out[parent]
                out[name] = (pa * la + pb * lc, pa * lb + pb * ld, pc * la + pd * lc, pc * lb + pd * ld,
                             pa * x + pb * y + px, pc * x + pd * y + py)
            if name == upto:
                break
        return out

    @staticmethod
    def angle(m: tuple) -> float:
        return math.degrees(math.atan2(m[2], m[0]))

    @staticmethod
    def point(m: tuple, x: float, y: float) -> tuple[float, float]:
        return m[0] * x + m[1] * y + m[4], m[2] * x + m[3] * y + m[5]

    @staticmethod
    def to_local(m: tuple, wx: float, wy: float) -> tuple[float, float]:
        a, b, c, d, ox, oy = m
        det = a * d - b * c
        dx, dy = wx - ox, wy - oy
        return (d * dx - b * dy) / det, (-c * dx + a * dy) / det

    def set_world(self, local: dict, name: str, world_xy: tuple[float, float], world_angle: float,
                  parents: dict | None = None) -> None:
        """Pose an unscaled bone so its origin and x axis reach a world target (parent must be unsheared)."""
        parents = parents or self.world(local, upto=self.parent[name])
        pm = parents[self.parent[name]]
        lx, ly = self.to_local(pm, *world_xy)
        current = local[name]
        current[X], current[Y] = lx, ly
        current[ROT] = unwrap(world_angle - self.angle(pm) - current[SHX], current[ROT])

    def solve_arm(self, local: dict, side: int, wrist: tuple[float, float], hand_angle: float,
                  world: dict | None = None) -> float:
        """Two-bone IK onto the wrist (hand bone origin); returns the unreachable distance (0 when reached)."""
        arm = ARMS[side]
        world = world or self.world(local, upto=arm["shoulder"])
        sm = world[arm["shoulder"]]
        up, fore, hand = local[arm["upper"]], local[arm["fore"]], local[arm["hand"]]
        ux, uy = self.point(sm, up[X], up[Y])
        l1, d1 = math.hypot(fore[X], fore[Y]), math.atan2(fore[Y], fore[X])
        l2, d2 = math.hypot(hand[X], hand[Y]), math.atan2(hand[Y], hand[X])
        vx, vy = wrist[0] - ux, wrist[1] - uy
        dist = math.hypot(vx, vy)
        reach = min(max(dist, abs(l1 - l2) + 1e-3), l1 + l2 - 1e-3)
        cos_a = (l1 * l1 + reach * reach - l2 * l2) / (2 * l1 * reach)
        a = math.acos(max(-1.0, min(1.0, cos_a)))
        theta1 = math.atan2(vy, vx) + arm["bend"] * a
        ex, ey = ux + l1 * math.cos(theta1), uy + l1 * math.sin(theta1)
        tx, ty = (ux + vx * reach / dist, uy + vy * reach / dist) if dist > 1e-6 else wrist
        theta2 = math.atan2(ty - ey, tx - ex)
        shoulder_angle = self.angle(sm)
        phi1 = math.degrees(theta1 - d1)
        phi2 = math.degrees(theta2 - d2)
        up[ROT] = unwrap(phi1 - shoulder_angle - up[SHX], up[ROT])
        fore[ROT] = unwrap(phi2 - phi1 - fore[SHX], fore[ROT])
        hand[ROT] = unwrap(hand_angle - phi2 - hand[SHX], hand[ROT])
        return max(0.0, dist - reach)


def unwrap(value: float, near: float) -> float:
    return near + ((value - near + 180.0) % 360.0 - 180.0)


def copy_pose(pose: dict) -> dict:
    return {k: list(v) for k, v in pose.items()}


def sample_timeline_start(rig: Rig, clip: dict) -> dict:
    """Absolute local pose at t=0 of a source clip (the first key of every channel)."""
    pose = {name: list(values) for name, values in rig.setup.items()}
    for bone, timelines in clip.get("bones", {}).items():
        values = pose[bone]
        for channel, keys in timelines.items():
            if not keys:
                continue
            key = keys[0]
            if channel == "rotate":
                values[ROT] += key.get("value", 0.0)
            elif channel == "translate":
                values[X] += key.get("x", 0.0)
                values[Y] += key.get("y", 0.0)
            elif channel == "scale":
                values[SX] *= key.get("x", 1.0)
                values[SY] *= key.get("y", 1.0)
            elif channel == "shear":
                values[SHX] += key.get("x", 0.0)
                values[SHY] += key.get("y", 0.0)
    return pose
