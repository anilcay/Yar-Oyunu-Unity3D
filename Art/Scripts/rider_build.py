"""SK_Rider: builds the rider skeleton, body/head/helmet variants, skin weights, all animation clips,
saves Art/Blender/Rider_Main.blend and exports SK_Rider.fbx.

Run:  blender.exe -b -P Art/Scripts/rider_build.py
Renders are made separately by rider_render.py (opens the saved .blend).

Rest pose = T-pose standing on the origin (Unity Humanoid friendly). The rider faces -Y (Unity +Z).
Every animation clip is seated: the Hips bone sits on the armature origin, i.e. place the SK_Rider
prefab on the sled's `Seat` empty (see Art/ANIMATION.md).
"""
import hashlib
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rider_common as rc  # noqa: E402

# --------------------------------------------------------------------------------------------
# Skeleton (T-pose). name: (head, tail, parent, connected)
# --------------------------------------------------------------------------------------------
CENTER_BONES = [
    ("Hips", (0, 0, 0.96), (0, 0, 1.05), None, False),
    ("Spine", (0, 0, 1.05), (0, 0, 1.16), "Hips", True),
    ("Chest", (0, 0, 1.16), (0, 0, 1.28), "Spine", True),
    ("UpperChest", (0, 0, 1.28), (0, 0, 1.43), "Chest", True),
    ("Neck", (0, 0, 1.43), (0, 0.0, 1.53), "UpperChest", True),
    ("Head", (0, 0, 1.53), (0, 0, 1.73), "Neck", True),
]
SIDE_BONES = [  # left side (+X); right is mirrored
    ("Shoulder", (0.025, 0.0, 1.40), (0.165, 0.01, 1.405), "UpperChest", False),
    ("UpperArm", (0.165, 0.01, 1.405), (0.44, 0.01, 1.405), "Shoulder", True),
    ("LowerArm", (0.44, 0.01, 1.405), (0.69, 0.01, 1.405), "UpperArm", True),
    ("Hand", (0.69, 0.01, 1.405), (0.78, 0.0, 1.40), "LowerArm", True),
    ("Fingers1", (0.78, 0.0, 1.40), (0.825, 0.0, 1.40), "Hand", True),
    ("Fingers2", (0.825, 0.0, 1.40), (0.865, 0.0, 1.40), "Fingers1", True),
    ("Thumb1", (0.705, -0.028, 1.394), (0.74, -0.058, 1.388), "Hand", False),
    ("Thumb2", (0.74, -0.058, 1.388), (0.765, -0.08, 1.385), "Thumb1", True),
    ("UpperLeg", (0.092, 0.0, 0.91), (0.092, -0.004, 0.49), "Hips", False),
    ("LowerLeg", (0.092, -0.004, 0.49), (0.092, 0.0, 0.085), "UpperLeg", True),
    ("Foot", (0.092, 0.0, 0.085), (0.095, -0.125, 0.025), "LowerLeg", True),
    ("Toes", (0.095, -0.125, 0.025), (0.095, -0.19, 0.022), "Foot", True),
]


def mirror(v, s):
    return (v[0] * s, v[1], v[2])


def build_armature():
    arm_data = bpy.data.armatures.new("Rider")
    arm_data.display_type = 'STICK'
    ob = bpy.data.objects.new("Rider", arm_data)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones
    specs = list(CENTER_BONES)
    for side, s in (("L", 1), ("R", -1)):
        for name, h, t, par, con in SIDE_BONES:
            pname = par if par in ("UpperChest", "Hips") else f"{par}.{side}"
            specs.append((f"{name}.{side}", mirror(h, s), mirror(t, s), pname, con))
    for name, h, t, par, con in specs:
        b = eb.new(name)
        b.head, b.tail = Vector(h), Vector(t)
        if par:
            b.parent = eb[par]
            b.use_connect = con
    # Rolls: Z axis forward (-Y) for torso/legs, up (+Z) for arms/hands
    for b in eb:
        if any(k in b.name for k in ("Shoulder", "Arm", "Hand", "Fingers", "Thumb")):
            b.align_roll(Vector((0, 0, 1)))
        else:
            b.align_roll(Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in ob.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return ob


# --------------------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------------------

def clamp01(x):
    return max(0.0, min(1.0, x))


def hsh(*a):
    return int(hashlib.md5(repr(a).encode()).hexdigest()[:6], 16) % 100


def lerp_w(val, table):
    """table: sorted [(key, {bone: w})]; linear blend of weight dicts."""
    if val <= table[0][0]:
        return dict(table[0][1])
    for (k0, w0), (k1, w1) in zip(table, table[1:]):
        if val <= k1:
            t = (val - k0) / (k1 - k0) if k1 > k0 else 0
            out = {}
            for b in set(w0) | set(w1):
                out[b] = w0.get(b, 0) * (1 - t) + w1.get(b, 0) * t
            return out
    return dict(table[-1][1])


SPINE_TABLE = [(0.90, {"Hips": 1}), (1.02, {"Hips": 1}), (1.08, {"Spine": 1}), (1.13, {"Spine": 1}),
               (1.19, {"Chest": 1}), (1.25, {"Chest": 1}), (1.31, {"UpperChest": 1})]


def torso_w(p):
    w = lerp_w(p.z, SPINE_TABLE)
    if p.z < 0.95:
        side = "L" if p.x > 0 else "R"
        lf = clamp01((0.95 - p.z) / 0.10) * 0.55 * clamp01(abs(p.x) / 0.07)
        w = {k: v * (1 - lf) for k, v in w.items()}
        w[f"UpperLeg.{side}"] = w.get(f"UpperLeg.{side}", 0) + lf
    if p.z > 1.33:  # shoulder top follows clavicles a little
        side = "L" if p.x > 0 else "R"
        sf = clamp01((abs(p.x) - 0.08) / 0.1) * 0.45
        w = {k: v * (1 - sf) for k, v in w.items()}
        w[f"Shoulder.{side}"] = sf
    return w


class Tone:
    """4 tone palette ramp: light, base, dark, deep."""

    def __init__(self, light, base, dark, deep=None):
        self.light, self.base, self.dark, self.deep = light, base, dark, deep or dark


RAMPS = {
    "jeans": Tone("jeans_light", "jeans_base", "jeans_dark", "jeans_deep"),
    "jeans_dark": Tone("jeans_base", "jeans_dark", "jeans_deep", "jeans_deep"),
    "tee_white": Tone("shirt_white", "shirt_white", "shirt_white_shade", "tint_mid"),
    "tee_navy": Tone("shirt_navy", "shirt_navy", "shirt_navy_shade", "shirt_navy_shade"),
    "tee_pink": Tone("shirt_pink", "shirt_pink", "shirt_pink_shade", "shirt_pink_shade"),
    "tee_lime": Tone("shirt_lime", "shirt_lime", "shirt_lime_shade", "leaf_lime_dark"),
    "wind_red": Tone("sled_red_light", "sled_red_base", "sled_red_dark", "sled_red_deep"),
    "wind_teal": Tone("sled_teal_light", "sled_teal_base", "sled_teal_dark", "sled_teal_deep"),
    "tights_black": Tone("chassis_light", "chassis_base", "chassis_dark", "chassis_deep"),
    "tights_navy": Tone("shirt_navy", "shirt_navy", "shirt_navy_shade", "shirt_navy_shade"),
    "hoodie_mustard": Tone("sled_yellow_light", "hoodie_mustard", "hoodie_mustard_shade", "hoodie_mustard_shade"),
    "hoodie_grey": Tone("rail_metal_light", "shirt_grey", "rail_metal_dark", "chassis_light"),
    "shorts_khaki": Tone("khaki", "khaki", "khaki_shade", "khaki_shade"),
    "shorts_navy": Tone("shirt_navy", "shirt_navy", "shirt_navy_shade", "shirt_navy_shade"),
}

VARIANTS = [
    # name, top kind, top ramp, bottom kind, bottom ramp, shoe main, shoe accent
    dict(name="Body_A1_TeeJeans", top="tee", top_ramp="tee_white", bottom="jeans", bottom_ramp="jeans",
         shoe="shoe_grey", accent="sled_orange_base"),
    dict(name="Body_A2_TeeJeans", top="tee", top_ramp="tee_navy", bottom="jeans", bottom_ramp="jeans_dark",
         shoe="decal_white", accent="sled_red_base"),
    dict(name="Body_B1_WindbreakerTights", top="wind", top_ramp="wind_red", bottom="tights", bottom_ramp="tights_black",
         shoe="chassis_light", accent="sled_yellow_base"),
    dict(name="Body_B2_WindbreakerTights", top="wind", top_ramp="wind_teal", bottom="tights", bottom_ramp="tights_navy",
         shoe="decal_white", accent="sled_teal_base"),
    dict(name="Body_C1_HoodieShorts", top="hoodie", top_ramp="hoodie_mustard", bottom="shorts", bottom_ramp="shorts_khaki",
         shoe="shoe_grey", accent="helmet_blue"),
    dict(name="Body_C2_HoodieShorts", top="hoodie", top_ramp="hoodie_grey", bottom="shorts", bottom_ramp="shorts_navy",
         shoe="decal_white", accent="sled_orange_base"),
]


# --------------------------------------------------------------------------------------------
# Body parts
# --------------------------------------------------------------------------------------------

def torso(B, v):
    top, bot = v["top"], v["bottom"]
    T, P = RAMPS[v["top_ramp"]], RAMPS[v["bottom_ramp"]]
    bulk = {"tee": 0.0, "wind": 0.014, "hoodie": 0.018}[top]
    hem_z = {"tee": 1.0, "wind": 0.93, "hoodie": 0.92}[top]
    n = 14

    def back_prof(t):  # buttocks / shoulder blades: slightly fuller back
        return 1.0 + 0.06 * max(0.0, -math.cos(t))
    # z, rx, ry, cy, kind ('p' pants, 't' top, 'hem' hem lip)
    prof = [
        (0.835, 0.115, 0.080, 0.012, "p"), (0.875, 0.165, 0.108, 0.016, "p"), (0.93, 0.176, 0.112, 0.012, "p"),
        (0.985, 0.172, 0.108, 0.008, "p"), (1.03, 0.160, 0.100, 0.004, "t"), (1.10, 0.158, 0.100, 0.0, "t"),
        (1.18, 0.168, 0.106, -0.004, "t"), (1.26, 0.180, 0.114, -0.008, "t"), (1.33, 0.188, 0.115, -0.006, "t"),
        (1.395, 0.182, 0.106, 0.0, "t"), (1.435, 0.135, 0.088, 0.004, "t"), (1.465, 0.072, 0.062, 0.006, "t"),
    ]
    rings, kinds = [], []
    for z, rx, ry, cy, k in prof:
        kind = "t" if z >= hem_z else "p"
        b = bulk if kind == "t" else (0.004 if bot == "tights" else 0.0)
        if top == "wind" and z > 1.44:
            continue
        pts = rc.ellipse_ring((0, cy, z), rx + b, ry + b * 0.8, n, prof=back_prof if z < 1.0 or 1.2 < z < 1.42 else None)
        rings.append((pts, None))
        kinds.append((z, kind))
    # hem lip ring (top garment overhang) inserted at the hem height
    out = []
    for idx, (z, kind) in enumerate(kinds):
        out.append((rings[idx][0], z, kind))
        if idx + 1 < len(kinds) and kinds[idx][1] == "p" and kinds[idx + 1][1] == "t":
            zh = hem_z - 0.005
            rx = 0.172 + bulk + 0.012
            ry = 0.108 + bulk * 0.8 + 0.01
            out.append((rc.ellipse_ring((0, 0.008, zh - 0.004), rx - 0.012, ry - 0.01, n), zh - 0.004, "hemin"))
            out.append((rc.ellipse_ring((0, 0.008, zh), rx, ry, n), zh, "hem"))
    if top == "wind":  # stand-up collar
        out.append((rc.ellipse_ring((0, 0.006, 1.44), 0.11, 0.085, n), 1.44, "t"))
        out.append((rc.ellipse_ring((0, 0.0, 1.475), 0.078, 0.072, n), 1.475, "collar"))
        out.append((rc.ellipse_ring((0, 0.0, 1.515), 0.074, 0.068, n), 1.515, "collar"))
    if top == "hoodie":  # neck rib
        out.append((rc.ellipse_ring((0, 0.008, 1.48), 0.066, 0.06, n), 1.48, "rib"))
    rings = [([Vector(p) for p in pts], None) for pts, _z, _k in out]
    zk = [(z, k) for _p, z, k in out]

    def col(i, j, nn):
        z0, k0 = zk[i]
        z1, k1 = zk[i + 1]
        th = rc.seg_theta(j, nn)
        front = abs(th) < 0.6
        back = abs(th) > 2.3
        h = hsh("torso", v["name"], i, j)
        if k1 in ("hemin",) or k0 == "hemin":
            ramp = P if k0 == "p" else T
            return ramp.dark if k0 == "p" else T.deep
        if k0 == "hem" or k1 == "hem":
            return T.dark
        if k1 == "p" or (k0 == "p" and k1 == "p"):
            # pants / belt region
            if top == "tee" and z1 >= 0.98 and z0 < 1.0:
                return "cabin_wood_dark" if not front or h > 30 else "chrome_dark"  # belt + buckle
            if top == "tee" and z1 > 0.95:
                return "cabin_wood_dark"
            if back:
                return P.dark
            if abs(abs(th) - 1.57) < 0.3:
                return P.deep  # side seams
            if z0 < 0.9 and abs(th) < 0.5:
                return P.dark  # crotch crease
            return P.base
        # top garment
        if k1 == "collar" or k0 == "collar":
            return T.dark if k0 == "collar" else T.base
        if k1 == "rib":
            return T.dark
        if top == "wind":
            if 1.27 <= z0 < 1.33:
                return "decal_white"  # chest stripe
        if top == "hoodie" and front and 0.97 < z0 < 1.12:
            return T.dark  # kangaroo pocket
        if top == "tee" and z0 >= 1.395 and front:
            return T.dark  # neckline
        if back:
            return T.dark if (z0 > 1.18 or (i + j) % 3) else T.base
        if abs(abs(th) - 1.57) < 0.4 and 1.15 < z0 < 1.36:
            return T.dark  # fold under the arms
        if 1.24 < z0 < 1.40 and abs(th) < 1.1:
            return T.light  # chest highlight
        return T.base

    B.loft([(pts, None) for pts, _ in rings], col, cap_start=True, cap_end=True,
           cap_colors=(P.deep, "SKIN_SHADE" if top != "wind" else T.deep))
    # weights (callable per vertex): re-assign after loft
    return


def assign_torso_weights(B, start):
    for vi in range(start, len(B.verts)):
        B.weights[vi] = torso_w(B.verts[vi])


def hood(B, v):
    """Bunched hood behind the neck (hoodie variants)."""
    T = RAMPS[v["top_ramp"]]
    start = len(B.verts)
    rings = []
    for k, (y, sx, sz) in enumerate([(0.040, 0.06, 0.03), (0.07, 0.115, 0.06), (0.105, 0.12, 0.065), (0.13, 0.085, 0.045)]):
        rings.append((rc.ellipse_ring((0, y, 1.445), sx, sz, 10, axis=2), {"UpperChest": 1}))

    def col(i, j, n):
        return T.base if (i == 2 or j % 3 == 0) else T.dark
    B.loft(rings, col)
    # drawstrings
    for s in (1, -1):
        B.box((0.03 * s, -0.098, 1.385), (0.008, 0.008, 0.09), {"UpperChest": 1}, {"all": "decal_white"})
    return start


def arm(B, v, side, s):
    top = v["top"]
    T = RAMPS[v["top_ramp"]]
    bulk = {"tee": 0.0, "wind": 0.012, "hoodie": 0.014}[top]
    n = 9
    sl = f".{side}"
    WT = [(0.10, {"UpperChest": 0.4, "Shoulder" + sl: 0.3, "UpperArm" + sl: 0.3}),
          (0.165, {"Shoulder" + sl: 0.35, "UpperArm" + sl: 0.65}), (0.25, {"UpperArm" + sl: 1}),
          (0.40, {"UpperArm" + sl: 1}), (0.44, {"UpperArm" + sl: 0.5, "LowerArm" + sl: 0.5}),
          (0.48, {"LowerArm" + sl: 1}), (0.60, {"LowerArm" + sl: 1}), (0.69, {"LowerArm" + sl: 0.5, "Hand" + sl: 0.5}),
          (0.70, {"Hand" + sl: 1})]
    # x, ry (front/back radius), rz (up/down radius), kind
    base = [(0.10, 0.058, 0.064), (0.165, 0.060, 0.064), (0.24, 0.054, 0.056), (0.32, 0.049, 0.050),
            (0.405, 0.045, 0.045), (0.44, 0.043, 0.045), (0.475, 0.044, 0.044), (0.56, 0.042, 0.039),
            (0.64, 0.035, 0.031), (0.692, 0.031, 0.027)]
    sleeve_end = {"tee": 0.33, "wind": 0.655, "hoodie": 0.655}[top]
    stations = []
    for x, ry, rz in base:
        if x < sleeve_end:
            stations.append((x, ry + bulk, rz + bulk, "cloth"))
        elif not any(k == "skin" for *_, k in stations):
            # sleeve end: hem (cloth) -> step -> skin
            ryc, rzc = lerp_r(base, sleeve_end)
            stations.append((sleeve_end - 0.004, ryc + bulk + 0.006, rzc + bulk + 0.006, "cuff"))
            stations.append((sleeve_end, ryc + bulk + 0.006, rzc + bulk + 0.006, "cuffend"))
            stations.append((sleeve_end, ryc - 0.002, rzc - 0.002, "skin"))
            stations.append((x, ry, rz, "skin"))
        else:
            stations.append((x, ry, rz, "skin"))
    rings = []
    for x, ry, rz, _k in stations:
        pts = rc.ellipse_ring((x * s, 0.01, 1.405 - (0.0 if x < 0.5 else (x - 0.5) * 0.02)), ry, rz, n, axis=1)
        rings.append((pts, lerp_w(x, WT)))
    kinds = [k for *_, k in stations]
    xs = [x for x, *_ in stations]

    def col(i, j, nn):
        k0, k1 = kinds[i], kinds[i + 1]
        th = rc.seg_theta(j, nn)
        h = hsh("arm", v["name"], side, i, j)
        if k0 == "cuffend":
            return T.deep if top == "tee" else T.dark
        if k0 == "cuff" or k1 == "cuff":
            return T.dark if top != "tee" else T.base
        if k0 == "skin":
            return "SKIN_SHADE" if (th < -1.9 and h > 40) else "SKIN"
        if top == "wind" and 0.4 < abs(th) < 0.8:
            return "decal_white" if th > 0 else T.base  # sleeve stripe
        if th < -1.2:  # underside
            return T.dark
        if top != "tee" and 0.40 < xs[i] < 0.48:
            return T.dark if j % 2 else T.base  # elbow creases
        if th > 1.2 and xs[i] < 0.3:
            return T.light  # shoulder top highlight
        return T.base
    B.loft(rings, col)


def lerp_r(base, x):
    for (x0, a0, b0), (x1, a1, b1) in zip(base, base[1:]):
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0)
            return a0 + (a1 - a0) * t, b0 + (b1 - b0) * t
    return base[-1][1], base[-1][2]


def frame_ring(center, d, ref, r1, r2, n, phase=0.5):
    d = Vector(d).normalized()
    u = Vector(ref) - d * Vector(ref).dot(d)
    u.normalize()
    w = d.cross(u)
    pts = []
    for i in range(n):
        t = 2 * math.pi * (i + phase) / n
        pts.append(Vector(center) + u * (r1 * math.cos(t)) + w * (r2 * math.sin(t)))
    return pts


def hand(B, side, s):
    sl = f".{side}"
    n = 8
    # palm + finger scoop as one loft along +X (left). axis=1 rings: rx=Y half-width, ry=Z half-thickness
    st = [(0.682, 0.030, 0.020, {"Hand" + sl: 0.5, "LowerArm" + sl: 0.5}),
          (0.705, 0.040, 0.021, {"Hand" + sl: 1}),
          (0.745, 0.046, 0.019, {"Hand" + sl: 1}),
          (0.778, 0.045, 0.016, {"Hand" + sl: 0.5, "Fingers1" + sl: 0.5}),
          (0.822, 0.043, 0.0135, {"Fingers1" + sl: 0.5, "Fingers2" + sl: 0.5}),
          (0.858, 0.040, 0.012, {"Fingers2" + sl: 1}),
          (0.872, 0.030, 0.010, {"Fingers2" + sl: 1})]
    rings = [(rc.ellipse_ring((x * s, 0.0, 1.40), ry, rz, n, axis=1), w) for x, ry, rz, w in st]

    def col(i, j, nn):
        th = rc.seg_theta(j, nn)
        if i >= 3 and th > 0.3 and th < 2.8 and j % 2 == 0:
            return "SKIN_SHADE"  # finger separation lines on the back of the hand
        if th < -0.6:
            return "SKIN_SHADE" if i in (3, 4) else "SKIN"  # palm creases
        return "SKIN"
    B.loft(rings, col)
    # thumb
    t0 = Vector((0.692 * s, -0.012, 1.396))
    t1 = Vector((0.74 * s, -0.058, 1.388))
    t2 = Vector((0.768 * s, -0.082, 1.385))
    d1 = t1 - t0
    d2 = t2 - t1
    rings = [(frame_ring(t0, d1, (0, 0, 1), 0.017, 0.015, 6), {"Hand" + sl: 0.6, "Thumb1" + sl: 0.4}),
             (frame_ring(t0 + d1 * 0.45, d1, (0, 0, 1), 0.016, 0.015, 6), {"Thumb1" + sl: 1}),
             (frame_ring(t1, d1 + d2, (0, 0, 1), 0.0135, 0.013, 6), {"Thumb1" + sl: 0.5, "Thumb2" + sl: 0.5}),
             (frame_ring(t2, d2, (0, 0, 1), 0.011, 0.0105, 6), {"Thumb2" + sl: 1})]
    B.loft(rings, lambda i, j, nn: "SKIN_SHADE" if (i == 2 and j == 0) else "SKIN")


def leg(B, v, side, s):
    bot = v["bottom"]
    P = RAMPS[v["bottom_ramp"]]
    sl = f".{side}"
    n = 10
    WT = [(0.95, {"UpperLeg" + sl: 0.45, "Hips": 0.55}), (0.86, {"UpperLeg" + sl: 0.85, "Hips": 0.15}),
          (0.80, {"UpperLeg" + sl: 1}), (0.56, {"UpperLeg" + sl: 1}), (0.49, {"UpperLeg" + sl: 0.5, "LowerLeg" + sl: 0.5}),
          (0.43, {"LowerLeg" + sl: 1}), (0.15, {"LowerLeg" + sl: 1}), (0.08, {"LowerLeg" + sl: 0.5, "Foot" + sl: 0.5})]
    WT = sorted(WT)
    thin = -0.009 if bot == "tights" else 0.0
    #       z     rx     ry    kind
    base = [(0.95, 0.088, 0.094), (0.87, 0.086, 0.090), (0.76, 0.076, 0.080), (0.63, 0.064, 0.068),
            (0.545, 0.058, 0.062), (0.49, 0.057, 0.061), (0.435, 0.055, 0.060), (0.34, 0.055, 0.060),
            (0.22, 0.045, 0.049), (0.13, 0.040, 0.043), (0.075, 0.040, 0.042)]
    stations = []
    if bot == "shorts":
        for z, rx, ry in base:
            if z > 0.60:
                stations.append((z, rx + 0.012, ry + 0.012, "cloth"))
        stations += [(0.595, 0.074, 0.078, "cloth"), (0.59, 0.074, 0.078, "hem"), (0.59, 0.060, 0.064, "skin")]
        for z, rx, ry in base:
            if z < 0.58:
                k = "sock" if z < 0.17 else "skin"
                stations.append((z, rx + (0.004 if k == "sock" else 0), ry + (0.004 if k == "sock" else 0), k))
        stations.insert(-2, (0.17, 0.046, 0.05, "sockedge"))
    else:
        for z, rx, ry in base:
            stations.append((z, rx + thin, ry + thin, "cloth"))
        if bot == "jeans":  # turned-up hem
            stations[-1] = (0.115, 0.047, 0.051, "cuff")
            stations.append((0.085, 0.047, 0.051, "cuffend"))
            stations.append((0.085, 0.036, 0.04, "cloth"))
    rings = []
    for z, rx, ry, _k in stations:
        cx = 0.092 - (0.95 - z) * 0.006
        cy = 0.0 if z > 0.5 else (0.004 if z > 0.3 else 0.0)

        def knee_prof(t, z=z):
            if 0.44 < z < 0.56:
                return 1.0 + 0.10 * max(0.0, math.cos(t)) ** 2  # knee cap
            if 0.25 < z < 0.42:
                return 1.0 + 0.10 * max(0.0, -math.cos(t)) ** 2  # calf
            return 1.0
        rings.append((rc.ellipse_ring((cx * s, cy, z), rx, ry, n, prof=knee_prof), lerp_w(z, WT)))
    kinds = [k for *_, k in stations]
    zs = [z for z, *_ in stations]

    def col(i, j, nn):
        k0, k1 = kinds[i], kinds[i + 1]
        th = rc.seg_theta(j, nn) * s  # + = outer side for both legs
        h = hsh("leg", v["name"], side, i, j)
        if k0 in ("skin",) and k1 in ("skin", "sockedge"):
            return "SKIN_SHADE" if (abs(th) > 2.4 and h > 50) else "SKIN"
        if k1 == "sockedge" or k0 in ("sock", "sockedge"):
            return "shirt_white" if k0 != "sockedge" or h > 30 else "shirt_white_shade"
        if k0 == "hem":
            return "SKIN"
        if k1 == "hem" or k0 == "cuff" or k1 == "cuff" or k0 == "cuffend":
            return P.dark if k0 != "cuff" else P.light
        z0 = zs[i]
        if bot == "tights" and abs(th - 1.57) < 0.35:
            return "decal_white" if z0 > 0.2 else P.light  # side stripe
        if abs(th - 1.57) < 0.32 and bot == "jeans":
            return P.deep  # outer seam
        if abs(th) > 2.2:
            return P.dark  # back of the leg
        if 0.43 < z0 < 0.56 and abs(th) < 1.3:
            return P.dark if (i + j) % 2 == 0 else P.base  # knee folds
        if bot == "jeans" and z0 < 0.2:
            return P.dark if (i + j) % 2 == 0 else P.base  # stacked folds at the ankle
        if 0.62 < z0 < 0.9 and abs(th) < 0.75:
            return P.light  # thigh top (catches light when seated)
        return P.base
    B.loft(rings, col)


def shoe(B, v, side, s):
    sl = f".{side}"
    n = 10
    main, acc = v["shoe"], v["accent"]
    # y (front=-), rx, rz, cz, weights
    st = [(0.072, 0.036, 0.040, 0.050, {"Foot" + sl: 1}),
          (0.045, 0.048, 0.060, 0.068, {"Foot" + sl: 1}),
          (-0.01, 0.052, 0.058, 0.064, {"Foot" + sl: 1}),
          (-0.07, 0.055, 0.046, 0.050, {"Foot" + sl: 1}),
          (-0.125, 0.054, 0.036, 0.038, {"Foot" + sl: 0.5, "Toes" + sl: 0.5}),
          (-0.17, 0.047, 0.030, 0.032, {"Toes" + sl: 1}),
          (-0.198, 0.032, 0.022, 0.027, {"Toes" + sl: 1})]
    rings = [(rc.ellipse_ring((0.095 * s, y, cz), rx, rz, n, axis=2), w) for y, rx, rz, cz, w in st]

    def col(i, j, nn):
        th = rc.seg_theta(j, nn) * s
        a = abs(th)
        if a > 2.2:
            return "rubber_worn" if i in (0, 5) else "decal_white"  # sole
        if a > 1.75:
            return "decal_white"  # midsole band
        if a < 0.35 and i in (1, 2, 3):
            return "decal_white" if i != 1 else "rubber"  # laces / collar
        if 0.9 < th < 1.6 and i in (2, 3):
            return acc  # outer side logo stripe
        if i == 0 and a < 1.2:
            return acc  # heel tab
        return main
    B.loft(rings, col)


def build_body(v, arm_ob):
    B = rc.Builder()
    s0 = len(B.verts)
    torso(B, v)
    assign_torso_weights(B, s0)
    if v["top"] == "hoodie":
        hood(B, v)
    if v["top"] == "wind":  # zipper strip down the front
        for z0, z1, y in ((0.94, 1.18, -0.121), (1.18, 1.33, -0.130), (1.33, 1.45, -0.124)):
            B.box((0, y, (z0 + z1) / 2), (0.012, 0.01, z1 - z0), torso_w(Vector((0, 0, (z0 + z1) / 2))),
                  {"all": "decal_black"})
    for side, s in (("L", 1), ("R", -1)):
        arm(B, v, side, s)
        hand(B, side, s)
        leg(B, v, side, s)
        shoe(B, v, side, s)
    return B


# --------------------------------------------------------------------------------------------
# Head + helmet
# --------------------------------------------------------------------------------------------
HEADS = [
    dict(name="Head_A", hair="hair_brown", brow="hair_brown", beard=False, brow_tilt=-0.16),  # focused
    dict(name="Head_B", hair="eye_dark", brow="eye_dark", beard=True, brow_tilt=0.0),          # calm, bearded
    dict(name="Head_C", hair="sand_light", brow="trunk_base", beard=False, brow_tilt=0.12),     # cheerful
]


def build_head(hv, arm_ob):
    B = rc.Builder()
    hair, brow = hv["hair"], hv["brow"]
    # neck
    nk = [(1.405, 0.052, 0.050, {"UpperChest": 0.6, "Neck": 0.4}), (1.46, 0.050, 0.048, {"Neck": 1}),
          (1.52, 0.050, 0.050, {"Neck": 0.5, "Head": 0.5}), (1.56, 0.048, 0.050, {"Head": 1})]
    B.loft([(rc.ellipse_ring((0, 0.012, z), rx, ry, 8), w) for z, rx, ry, w in nk],
           lambda i, j, n: "SKIN_SHADE" if i == 2 and abs(rc.seg_theta(j, n)) < 0.9 else "SKIN")
    # skull: z, rx, ry, cy
    n = 12
    pr = [(1.522, 0.030, 0.026, -0.058), (1.545, 0.058, 0.056, -0.036), (1.575, 0.074, 0.084, -0.013),
          (1.61, 0.082, 0.096, -0.004), (1.645, 0.086, 0.099, 0.0), (1.68, 0.087, 0.101, 0.003),
          (1.712, 0.078, 0.091, 0.008), (1.738, 0.050, 0.062, 0.012)]

    def face_prof(t):  # flatter face, rounder back
        return 1.0 - 0.06 * max(0.0, math.cos(t)) ** 4
    rings = [(rc.ellipse_ring((0, cy, z), rx, ry, n, prof=face_prof), {"Head": 1}) for z, rx, ry, cy in pr]

    def col(i, j, nn):
        th = abs(rc.seg_theta(j, nn))
        h = hsh("head", hv["name"], i, j)
        if i >= 5 or (i == 4 and th > 1.2) or (i == 3 and th > 2.0):
            return hair
        if hv["beard"] and i <= 2 and (th > 0.4 or i == 0):
            return hair if h > 15 else "SKIN_SHADE"
        if i == 4 and 1.2 < th < 1.7:
            return hair  # sideburns
        if i in (1, 2) and th > 1.0:
            return "SKIN_SHADE" if h > 50 else "SKIN"  # jaw shadow
        return "SKIN"
    B.loft(rings, col, cap_colors=("SKIN_SHADE" if not hv["beard"] else hair, hair))
    w = {"Head": 1}
    # nose (closed tetra-ish)
    top = B.v((0, -0.101, 1.645), w)
    l = B.v((0.017, -0.094, 1.600), w)
    r = B.v((-0.017, -0.094, 1.600), w)
    tip = B.v((0, -0.126, 1.604), w)
    B.f((top, l, tip), "SKIN")
    B.f((top, tip, r), "SKIN")
    B.f((l, r, tip), "SKIN_SHADE")
    B.f((top, r, l), "SKIN")
    # eyes, brows, mouth, ears
    for s in (1, -1):
        B.box((0.034 * s, -0.0905, 1.638), (0.016, 0.012, 0.021), w, {"all": "eye_dark"})
        rot = Matrix.Rotation(-hv["brow_tilt"] * s, 3, 'Y')
        B.box((0.036 * s, -0.093, 1.668), (0.034, 0.012, 0.0085), w, {"all": brow}, rot=rot)
        B.box((0.088 * s, 0.006, 1.632), (0.014, 0.03, 0.044), w, {"all": "SKIN", "left": "SKIN_SHADE", "right": "SKIN_SHADE"})
    B.box((0, -0.0925, 1.578), (0.034, 0.010, 0.0065), w, {"all": "SKIN_SHADE"})
    return B


def build_helmet(arm_ob):
    B = rc.Builder()
    w = {"Head": 1}
    n = 16
    cy = 0.006
    RX, RY = 0.112, 0.128
    ztop = 1.792

    def rim_z(t):
        return 1.668 + 0.022 * math.cos(t)  # front edge higher than back

    phis = [0, 12, 32, 52, 70, 84]
    rings = []
    # inner lip (so the shell has thickness from below)
    lip = []
    for i in range(n):
        t = 2 * math.pi * (i + 0.5) / n
        lip.append(Vector((RX * 0.9 * math.sin(t), cy - RY * 0.9 * math.cos(t), rim_z(t) + 0.012)))
    rings.append((lip, w))
    for ph in phis:
        p = math.radians(ph)
        pts = []
        for i in range(n):
            t = 2 * math.pi * (i + 0.5) / n
            rz = rim_z(t)
            k = math.cos(p) * (1.0 + 0.03 * math.cos(t) ** 2)
            pts.append(Vector((RX * k * math.sin(t), cy - RY * k * math.cos(t), rz + (ztop - rz) * math.sin(p))))
        rings.append((pts, w))

    def col(i, j, nn):
        th = rc.seg_theta(j, nn)
        a = abs(th)
        if i == 0:
            return "decal_black"  # inner lip / padding
        if i == 1:
            return "HELM_DARK"  # rim band (visor line all round)
        if i >= 3 and (a < 0.25 or a > 2.9):
            return "HELM_DARK"  # centre ridge
        if i in (3, 4) and 0.5 < a < 1.0 and j % 2 == 0:
            return "decal_black"  # vents
        if i == 2 and 1.3 < a < 1.9:
            return "decal_white"  # side stripe
        return "HELM"
    B.loft(rings, col, cap_start=False, cap_end=True, cap_colors=(None, "HELM"))
    # visor / brim (closed slab across the front)
    m = 7
    top_pts, bot_pts = [], []
    for k in range(m):
        t = math.radians(-58 + 116 * k / (m - 1))
        rz = rim_z(t) + 0.006
        base = Vector((RX * 1.01 * math.sin(t), cy - RY * 1.01 * math.cos(t), rz))
        outv = Vector((math.sin(t) * 0.6, -math.cos(t), 0)).normalized()
        reach = 0.032 * math.cos(t) ** 2 + 0.008
        tip = base + outv * reach + Vector((0, 0, -0.006))
        top_pts.append((base + Vector((0, 0, 0.006)), tip + Vector((0, 0, 0.004))))
        bot_pts.append((base - Vector((0, 0, 0.004)), tip - Vector((0, 0, 0.002))))
    idx = []
    for (tb, tt), (bb, bt) in zip(top_pts, bot_pts):
        idx.append([B.v(tb, w), B.v(tt, w), B.v(bt, w), B.v(bb, w)])
    for k in range(m - 1):
        a, b = idx[k], idx[k + 1]
        B.f((a[0], a[1], b[1], b[0]), "HELM_DARK")
        B.f((a[1], a[2], b[2], b[1]), "HELM_DARK")
        B.f((a[2], a[3], b[3], b[2]), "decal_black")
        B.f((a[3], a[0], b[0], b[3]), "decal_black")
    B.f(tuple(idx[0]), "HELM_DARK")
    B.f(tuple(reversed(idx[-1])), "HELM_DARK")
    # chin straps: from the rim in front of the ears down to a buckle under the chin
    for s in (1, -1):
        p0 = Vector((0.095 * s, -0.012, 1.655))
        p1 = Vector((0.066 * s, -0.050, 1.560))
        p2 = Vector((0.012 * s, -0.064, 1.524))
        for a, b in ((p0, p1), (p1, p2)):
            d = b - a
            c = (a + b) * 0.5
            zax = d.normalized()
            xs = Vector((s, 0, 0))
            xax = (xs - zax * xs.dot(zax)).normalized()
            yax = zax.cross(xax)
            rot = Matrix((xax, yax, zax)).transposed()
            B.box(c, (0.004, 0.011, d.length + 0.006), w, {"all": "rubber"}, rot=rot)
    B.box((0, -0.066, 1.522), (0.026, 0.012, 0.014), w, {"all": "chrome_dark", "front": "chrome_base"})
    return B


# --------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------

def fix_normals(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    for p in ob.data.polygons:
        p.use_smooth = False


def main():
    rc.reset_scene()
    arm_ob = build_armature()
    mats = rc.base_materials()
    report = {}
    bodies = []
    for v in VARIANTS:
        B = build_body(v, arm_ob)
        ob = B.build(v["name"], arm_ob, mats)
        fix_normals(ob)
        bodies.append(ob)
        report[v["name"]] = rc.mesh_tris(ob)
    heads = []
    for hv in HEADS:
        ob = build_head(hv, arm_ob).build(hv["name"], arm_ob, mats)
        fix_normals(ob)
        heads.append(ob)
        report[hv["name"]] = rc.mesh_tris(ob)
    helmet = build_helmet(arm_ob).build("Helmet", arm_ob, mats)
    fix_normals(helmet)
    report["Helmet"] = rc.mesh_tris(helmet)

    import rider_anim
    rider_anim.build_all_actions(arm_ob)

    os.makedirs(rc.BLEND_DIR, exist_ok=True)
    blend = os.path.join(rc.BLEND_DIR, "Rider_Main.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)

    out_dir = rc.output_char_dir()
    fbx = os.path.join(out_dir, "SK_Rider.fbx")
    rc.export_fbx(fbx, [arm_ob] + bodies + heads + [helmet], animated=True)
    print("RIDER_TRIS", report)
    worst = max(report[b.name] for b in bodies) + max(report[h.name] for h in heads) + report["Helmet"]
    print("RIDER_MAX_ACTIVE_TRIS", worst)
    print("RIDER_BONES", len(arm_ob.data.bones))
    print("RIDER_FBX", fbx)


if __name__ == "__main__":
    main()
