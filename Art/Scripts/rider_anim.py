"""Rider animation clips (seated luge poses), authored procedurally.

A pose is a dict of parameters (hip transform, spine/neck/head eulers, hand/foot targets, handlebar steer
...). An analytic solver (FK for the spine, two-bone IK for arms/legs) turns a pose into armature-space bone
matrices, which are converted to pose-bone local transforms and keyed on every frame (30 fps).

Axes (armature space == sled space, origin = Seat): +X = rider's left, -Y = forward (downhill), +Z = up.
Euler params (degrees): rx + = bend forward, ry + = lean/roll to the rider's left, rz + = turn/look left.
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30

# ---- Sled contract (assumed; see Art/ANIMATION.md). Relative to the Seat empty. -------------
GRIP_PIVOT = Vector((0.0, -0.55, 0.20))   # handlebar centre (Grip.L/R = pivot +-0.20 X)
GRIP_HALF = 0.20
GROUND_Z = -0.25                          # ground below the Seat (Seat ~0.25 m above ground)

D = math.radians


def E(rx, ry, rz):
    """World-axis rotation: X first, then Y, then Z (Blender 'XYZ')."""
    return (Matrix.Rotation(D(rz), 3, 'Z') @ Matrix.Rotation(D(ry), 3, 'Y') @ Matrix.Rotation(D(rx), 3, 'X'))


def frame(u, p):
    u = Vector(u).normalized()
    p = Vector(p) - u * Vector(p).dot(u)
    p.normalize()
    return Matrix((u, p, u.cross(p))).transposed()


def nrm(v):
    return Vector(v).normalized()


# --------------------------------------------------------------------------------------------
# Rest data + solver
# --------------------------------------------------------------------------------------------
class Rig:
    def __init__(self, arm_ob):
        self.ob = arm_ob
        self.bones = arm_ob.data.bones
        self.M = {b.name: b.matrix_local.copy() for b in self.bones}
        self.R0 = {k: m.to_3x3().normalized() for k, m in self.M.items()}
        self.head = {b.name: b.head_local.copy() for b in self.bones}
        self.tail = {b.name: b.tail_local.copy() for b in self.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in self.bones}
        self.order = [b.name for b in self.bones]  # parents come before children
        self.L = {k: (self.tail[k] - self.head[k]).length for k in self.head}
        self.dir = {k: (self.tail[k] - self.head[k]).normalized() for k in self.head}


def two_bone(S, T, a, b, pole):
    d = T - S
    L = max(abs(a - b) + 1e-4, min(d.length, (a + b) * 0.9995))
    dn = d.normalized()
    T = S + dn * L
    w = Vector(pole) - dn * Vector(pole).dot(dn)
    if w.length < 1e-6:
        w = dn.orthogonal()
    w.normalize()
    ca = max(-1.0, min(1.0, (a * a + L * L - b * b) / (2 * a * L)))
    sa = math.sqrt(1 - ca * ca)
    Ej = S + (dn * ca + w * sa) * a
    return Ej, T, w


def lerp(a, b, t):
    if isinstance(a, (int, float)):
        return a + (b - a) * t
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def add(a, b):
    if isinstance(a, (int, float)):
        return a + b
    return tuple(x + y for x, y in zip(a, b))


BASE = {
    "hip_pos": (0.0, 0.0, 0.0), "hip_rot": (-12.0, 0.0, 0.0),
    "spine": (6.0, 0.0, 0.0), "chest": (6.0, 0.0, 0.0), "uchest": (5.0, 0.0, 0.0),
    "neck": (2.0, 0.0, 0.0), "head": (4.0, 0.0, 0.0),
    "shrug_L": 0.0, "shrug_R": 0.0, "clavfwd_L": 6.0, "clavfwd_R": 6.0,
    "steer": 0.0, "bar_pull": 0.0,
    "grip_L": 1.0, "grip_R": 1.0,
    "hpos_L": (0.25, -0.40, 0.15), "hpos_R": (-0.25, -0.40, 0.15),
    "hdir_L": (0.1, -1.0, -0.3), "hdir_R": (-0.1, -1.0, -0.3),
    "hpalm_L": (0.0, 0.0, -1.0), "hpalm_R": (0.0, 0.0, -1.0),
    "elbow_L": (0.8, 0.35, -0.5), "elbow_R": (-0.8, 0.35, -0.5),
    "curl_L": 1.0, "curl_R": 1.0, "thumb_L": 1.0, "thumb_R": 1.0,
    "foot_L": (0.135, -0.76, 0.02), "foot_R": (-0.135, -0.76, 0.02),
    "knee_L": (0.25, 0.0, 1.0), "knee_R": (-0.25, 0.0, 1.0),
    "fdir_L": (0.04, -0.45, 0.9), "fdir_R": (-0.04, -0.45, 0.9),
    "toes_L": 0.0, "toes_R": 0.0,
    "fsole_L": (0.0, -1.0, -0.15), "fsole_R": (0.0, -1.0, -0.15),
}
GRIP_DIR_L = Vector((0.12, -1.0, -0.38))
GRIP_PALM_L = Vector((0.0, 0.15, -1.0))


def grip_frame(P, side):
    s = 1 if side == "L" else -1
    R = Matrix.Rotation(D(P["steer"]), 3, 'Z')
    pull = P["bar_pull"]
    pivot = GRIP_PIVOT + Vector((0, 0.13 * pull, 0.07 * pull))
    tilt = Matrix.Rotation(D(-25 * pull), 3, 'X')  # bar pulled back tilts the hands up
    g = pivot + R @ Vector((GRIP_HALF * s, 0, 0))
    dvec = R @ tilt @ Vector((GRIP_DIR_L.x * s, GRIP_DIR_L.y, GRIP_DIR_L.z))
    palm = R @ tilt @ Vector((GRIP_PALM_L.x * s, GRIP_PALM_L.y, GRIP_PALM_L.z))
    dvec.normalize()
    palm = palm - dvec * palm.dot(dvec)
    palm.normalize()
    wrist = g - dvec * 0.055 - palm * 0.028
    return wrist, dvec, palm


def solve(rig, P):
    Q, H = {}, {}
    par = rig.parent

    def fk(b, Rl):
        p = par[b]
        Q[b] = Q[p] @ Rl
        H[b] = H[p] + Q[p] @ (rig.head[b] - rig.head[p])

    Q["Hips"] = E(*P["hip_rot"])
    H["Hips"] = Vector(P["hip_pos"])
    fk("Spine", E(*P["spine"]))
    fk("Chest", E(*P["chest"]))
    fk("UpperChest", E(*P["uchest"]))
    fk("Neck", E(*P["neck"]))
    fk("Head", E(*P["head"]))
    for side, s in (("L", 1), ("R", -1)):
        sh = f"Shoulder.{side}"
        fk(sh, Matrix.Rotation(D(-P[f"shrug_{side}"] * s), 3, 'Y') @ Matrix.Rotation(D(-P[f"clavfwd_{side}"] * s), 3, 'Z'))
        ua, la, hd = f"UpperArm.{side}", f"LowerArm.{side}", f"Hand.{side}"
        S = H[sh] + Q[sh] @ (rig.head[ua] - rig.head[sh])
        gw, gd, gp = grip_frame(P, side)
        g = P[f"grip_{side}"]
        wrist = gw.lerp(Vector(P[f"hpos_{side}"]), 1 - g)
        hdir = gd.lerp(nrm(P[f"hdir_{side}"]), 1 - g)
        hpalm = gp.lerp(nrm(P[f"hpalm_{side}"]), 1 - g)
        Ej, T, w = two_bone(S, wrist, rig.L[ua], rig.L[la], Vector(P[f"elbow_{side}"]))
        rest_pole = Vector((0, 1, 0))
        for b, a0, a1 in ((ua, S, Ej), (la, Ej, T)):
            u = (a1 - a0).normalized()
            Q[b] = frame(u, w - u * w.dot(u)) @ frame(rig.dir[b], rest_pole).transposed()
        H[ua], H[la] = S, Ej
        Q[hd] = frame(hdir, hpalm) @ frame(rig.dir[hd], (0, 0, -1)).transposed()
        H[hd] = T
        # fingers / thumb curl
        cax = rig.dir[hd].cross(Vector((0, 0, -1))).normalized()
        c = P[f"curl_{side}"]
        f1, f2 = f"Fingers1.{side}", f"Fingers2.{side}"
        Q[f1] = Q[hd] @ Matrix.Rotation(D(80 * c), 3, cax)
        H[f1] = H[hd] + Q[hd] @ (rig.head[f1] - rig.head[hd])
        Q[f2] = Q[f1] @ Matrix.Rotation(D(70 * c), 3, cax)
        H[f2] = H[f1] + Q[f1] @ (rig.head[f2] - rig.head[f1])
        t1, t2 = f"Thumb1.{side}", f"Thumb2.{side}"
        tc = P[f"thumb_{side}"]
        tax = rig.dir[t1].cross(Vector((0, 0, -1))).normalized()
        Q[t1] = Q[hd] @ Matrix.Rotation(D(30 * tc), 3, tax) @ Matrix.Rotation(D(-20 * tc * s), 3, 'Z')
        H[t1] = H[hd] + Q[hd] @ (rig.head[t1] - rig.head[hd])
        Q[t2] = Q[t1] @ Matrix.Rotation(D(35 * tc), 3, tax)
        H[t2] = H[t1] + Q[t1] @ (rig.head[t2] - rig.head[t1])
        # legs
        ul, ll, ft, to = f"UpperLeg.{side}", f"LowerLeg.{side}", f"Foot.{side}", f"Toes.{side}"
        S = H["Hips"] + Q["Hips"] @ (rig.head[ul] - rig.head["Hips"])
        Ej, T, w = two_bone(S, Vector(P[f"foot_{side}"]), rig.L[ul], rig.L[ll], Vector(P[f"knee_{side}"]))
        rest_pole = Vector((0, -1, 0))
        for b, a0, a1 in ((ul, S, Ej), (ll, Ej, T)):
            u = (a1 - a0).normalized()
            Q[b] = frame(u, w - u * w.dot(u)) @ frame(rig.dir[b], rest_pole).transposed()
        H[ul], H[ll] = S, Ej
        fd = nrm(P[f"fdir_{side}"])
        sv = Vector(P[f"fsole_{side}"])
        sole = sv - fd * sv.dot(fd)  # sole normal (seated: faces the sled nose)
        rest_sole = Vector((0, 0, -1))
        Q[ft] = frame(fd, sole) @ frame(rig.dir[ft], rest_sole).transposed()
        H[ft] = T
        tax = rig.dir[to].cross(Vector((0, 0, 1))).normalized()
        Q[to] = Q[ft] @ Matrix.Rotation(D(P[f"toes_{side}"]), 3, tax)
        H[to] = H[ft] + Q[ft] @ (rig.head[to] - rig.head[ft])
    mats = {}
    for b in rig.order:
        mats[b] = Matrix.Translation(H[b]) @ (Q[b] @ rig.R0[b]).to_4x4()
    return mats


def to_basis(rig, mats):
    out = {}
    for b in rig.order:
        p = rig.parent[b]
        if p is None:
            out[b] = rig.M[b].inverted() @ mats[b]
        else:
            rel = rig.M[p].inverted() @ rig.M[b]
            out[b] = rel.inverted() @ mats[p].inverted() @ mats[b]
    return out


# --------------------------------------------------------------------------------------------
# Clip authoring
# --------------------------------------------------------------------------------------------
MIRROR_VEC = ("hip_pos", "hpos", "hdir", "hpalm", "elbow", "foot", "knee", "fdir", "fsole")
MIRROR_EUL = ("hip_rot", "spine", "chest", "uchest", "neck", "head")


def mirror(P):
    out = {}
    for k, v in P.items():
        nk = k
        if k.endswith("_L"):
            nk = k[:-2] + "_R"
        elif k.endswith("_R"):
            nk = k[:-2] + "_L"
        base = k.rsplit("_", 1)[0] if k[-2:] in ("_L", "_R") else k
        if base in MIRROR_VEC:
            v = (-v[0], v[1], v[2])
        elif base in MIRROR_EUL:
            v = (v[0], -v[1], -v[2])
        elif k == "steer":
            v = -v
        out[nk] = v
    return out


def P_(**kw):
    p = dict(BASE)
    p.update(kw)
    return p


def ease(t):
    return t * t * (3 - 2 * t)


class Clip:
    def __init__(self, name, length, keys, loop, additive=None, ease_fn=ease):
        self.name, self.length, self.keys, self.loop = name, length, keys, loop
        self.additive = additive
        self.ease = ease_fn

    def pose(self, f):
        keys = self.keys
        if f <= keys[0][0]:
            P = dict(keys[0][1])
        elif f >= keys[-1][0]:
            P = dict(keys[-1][1])
        else:
            for (f0, p0), (f1, p1) in zip(keys, keys[1:]):
                if f0 <= f <= f1:
                    t = self.ease((f - f0) / (f1 - f0))
                    P = {k: lerp(p0[k], p1[k], t) for k in p0}
                    break
        if self.additive:
            for k, dv in self.additive(f, self.length).items():
                P[k] = add(P[k], dv)
        return P


def sinf(f, n, cycles, phase=0.0):
    return math.sin(2 * math.pi * (cycles * f / n + phase))


def breath(f, n, amp=1.0):
    b = sinf(f, n, 1) * amp
    sway = sinf(f, n, 1, 0.25) * amp
    return {"chest": (1.2 * b, 0, 0), "uchest": (0.8 * b, 0.4 * sway, 0), "shrug_L": 1.2 * b, "shrug_R": 1.2 * b,
            "hip_rot": (0, 1.0 * sway, 0), "spine": (0, -0.6 * sway, 0),
            "head": (0.6 * sinf(f, n, 2), -0.5 * sway, 1.5 * sinf(f, n, 1, 0.6) * amp)}


def tremble(f, n, amp=1.0):
    return {"hip_pos": (0.003 * amp * sinf(f, n, 15), 0, 0.004 * amp * sinf(f, n, 11, 0.3)),
            "chest": (0.8 * amp * sinf(f, n, 9), 0.6 * amp * sinf(f, n, 7), 0),
            "head": (0.6 * amp * sinf(f, n, 13), 0, 0.5 * amp * sinf(f, n, 6))}


def merge(*ds):
    out = {}
    for d in ds:
        for k, v in d.items():
            out[k] = add(out[k], v) if k in out else v
    return out


def clips():
    n90 = 90
    SIT = P_()
    STEER_L = P_(steer=22.0, hip_pos=(0.015, 0, 0), hip_rot=(-12, 7, 3), spine=(6, 5, 2), chest=(6, 5, 2),
                 uchest=(5, 3, 1), neck=(2, -6, 4), head=(4, -10, 4),
                 elbow_L=(0.6, 0.6, -0.5), elbow_R=(-0.9, 0.2, -0.5), foot_L=(0.12, -0.74, 0.03),
                 knee_L=(0.15, 0, 1), knee_R=(-0.4, 0, 1))
    BRAKE = P_(bar_pull=1.0, hip_rot=(-24, 0, 0), spine=(-2, 0, 0), chest=(0, 0, 0), uchest=(2, 0, 0),
               neck=(8, 0, 0), head=(14, 0, 0), foot_L=(0.15, -0.79, -0.01), foot_R=(-0.15, -0.79, -0.01),
               fdir_L=(0.05, -0.3, 0.95), fdir_R=(-0.05, -0.3, 0.95), elbow_L=(0.7, 0.6, -0.4),
               elbow_R=(-0.7, 0.6, -0.4), shrug_L=4, shrug_R=4)
    DRIFT_L = P_(steer=30.0, hip_pos=(0.04, 0.0, 0.01), hip_rot=(-10, 13, 5), spine=(8, 9, 3), chest=(6, 8, 3),
                 uchest=(5, 5, 2), neck=(0, -10, 3), head=(6, -18, 4),
                 elbow_L=(0.5, 0.7, -0.5), elbow_R=(-0.9, 0.1, -0.4), shrug_L=-3, shrug_R=5,
                 foot_L=(0.11, -0.69, 0.07), knee_L=(0.3, 0, 1),
                 foot_R=(-0.22, -0.83, -0.02), knee_R=(-0.6, 0, 1), fdir_R=(-0.25, -0.55, 0.8), toes_R=-15)
    out = []
    out.append(Clip("Sit_Idle", n90, [(0, SIT), (n90, SIT)], True, lambda f, n: breath(f, n)))
    out.append(Clip("Steer_Left", n90, [(0, STEER_L), (n90, STEER_L)], True, lambda f, n: breath(f, n, 0.8)))
    out.append(Clip("Steer_Right", n90, [(0, mirror(STEER_L)), (n90, mirror(STEER_L))], True,
                    lambda f, n: breath(f, n, 0.8)))
    out.append(Clip("Brake", n90, [(0, BRAKE), (n90, BRAKE)], True, lambda f, n: breath(f, n, 0.6)))
    out.append(Clip("Drift_Left", n90, [(0, DRIFT_L), (n90, DRIFT_L)], True,
                    lambda f, n: merge(breath(f, n, 0.5), tremble(f, n))))
    out.append(Clip("Drift_Right", n90, [(0, mirror(DRIFT_L)), (n90, mirror(DRIFT_L))], True,
                    lambda f, n: mirror(merge(breath(f, n, 0.5), tremble(f, n)))))

    # Push_Start: release, reach down beside the sled, shove back, re-grip (1 s)
    reach = P_(grip_L=0.0, grip_R=0.0, hip_rot=(-2, 0, 0), spine=(14, 0, 0), chest=(12, 0, 0), uchest=(8, 0, 0),
               neck=(-6, 0, 0), head=(-8, 0, 0), clavfwd_L=14, clavfwd_R=14, shrug_L=-6, shrug_R=-6,
               hpos_L=(0.42, -0.26, -0.13), hpos_R=(-0.42, -0.26, -0.13),
               hdir_L=(0.3, -0.5, -1.0), hdir_R=(-0.3, -0.5, -1.0), hpalm_L=(0.2, 0.3, -1), hpalm_R=(-0.2, 0.3, -1),
               elbow_L=(0.5, 1.0, 0.0), elbow_R=(-0.5, 1.0, 0.0), curl_L=0.15, curl_R=0.15, thumb_L=0.2, thumb_R=0.2)
    shove = dict(reach, hip_rot=(-18, 0, 0), spine=(0, 0, 0), chest=(2, 0, 0), uchest=(2, 0, 0),
                 neck=(6, 0, 0), head=(8, 0, 0), clavfwd_L=-8, clavfwd_R=-8,
                 hpos_L=(0.40, 0.22, -0.12), hpos_R=(-0.40, 0.22, -0.12),
                 hdir_L=(0.3, 0.1, -1.0), hdir_R=(-0.3, 0.1, -1.0), elbow_L=(0.6, 1.0, 0.5), elbow_R=(-0.6, 1.0, 0.5))
    lift = dict(shove, hpos_L=(0.36, 0.05, 0.10), hpos_R=(-0.36, 0.05, 0.10), hip_rot=(-14, 0, 0), spine=(4, 0, 0),
                curl_L=0.5, curl_R=0.5, hdir_L=(0.2, -0.6, -0.6), hdir_R=(-0.2, -0.6, -0.6))
    out.append(Clip("Push_Start", 30, [(0, SIT), (7, reach), (15, shove), (21, lift), (30, SIT)], False))

    # Bump: 0.4 s jolt (12 frames)
    j1 = P_(hip_pos=(0.025, 0.01, 0.025), hip_rot=(-16, 5, 2), spine=(0, 3, 0), chest=(2, 2, 0), head=(-6, -6, -4),
            neck=(-4, 0, 0), steer=-8.0, shrug_L=8, shrug_R=8)
    j2 = P_(hip_pos=(-0.01, -0.01, 0.0), hip_rot=(-8, -3, -1), spine=(12, -2, 0), chest=(10, 0, 0), head=(14, 4, 3),
            neck=(6, 0, 0), steer=5.0)
    j3 = P_(hip_rot=(-13, 1, 0), spine=(4, 0, 0), head=(2, 0, 0), steer=-2.0)
    out.append(Clip("Bump", 12, [(0, SIT), (2, j1), (5, j2), (8, j3), (12, SIT)], False,
                    ease_fn=lambda t: t * t * (3 - 2 * t)))

    # Lift_Relax: hands resting on the thighs, leaning back, looking around (6 s loop)
    rel = P_(grip_L=0.0, grip_R=0.0, hip_rot=(-22, 0, 0), spine=(2, 0, 0), chest=(2, 0, 0), uchest=(2, 0, 0),
             neck=(4, 0, 0), head=(-2, 0, 0), clavfwd_L=0, clavfwd_R=0, shrug_L=-4, shrug_R=-4,
             hpos_L=(0.14, -0.33, 0.115), hpos_R=(-0.14, -0.31, 0.115),
             hdir_L=(0.05, -1.0, -0.2), hdir_R=(-0.05, -1.0, -0.2), hpalm_L=(-0.2, 0, -1), hpalm_R=(0.2, 0, -1),
             elbow_L=(1.0, 0.4, -0.3), elbow_R=(-1.0, 0.4, -0.3), curl_L=0.35, curl_R=0.35, thumb_L=0.3, thumb_R=0.3,
             foot_L=(0.18, -0.72, 0.03), foot_R=(-0.17, -0.73, 0.02), knee_L=(0.45, 0, 1), knee_R=(-0.45, 0, 1))
    look_l = dict(rel, neck=(2, 2, 18), head=(-8, 4, 26), uchest=(2, 0, 6), hpos_L=(0.15, -0.30, 0.13))
    look_r = dict(rel, neck=(2, -2, -16), head=(-4, -3, -24), uchest=(2, 0, -5), hpos_R=(-0.15, -0.34, 0.12))
    up = dict(rel, head=(-18, 0, 4), neck=(-4, 0, 0))
    out.append(Clip("Lift_Relax", 180, [(0, rel), (25, rel), (45, look_l), (75, look_l), (95, up), (110, rel),
                                         (125, look_r), (150, look_r), (180, rel)], True,
                    lambda f, n: breath(f, n, 0.7)))

    # Look_Back: over the left shoulder, hands stay on the bar (2 s one-shot; hold = frames 15-45)
    lb = P_(hip_rot=(-12, 0, 5), spine=(5, 0, 10), chest=(4, 2, 14), uchest=(2, 2, 14), neck=(-2, 6, 32),
            head=(-4, 8, 45), shrug_L=-4, shrug_R=6, elbow_L=(0.5, 0.8, -0.3), clavfwd_L=-6, clavfwd_R=14)
    out.append(Clip("Look_Back", 60, [(0, SIT), (15, lb), (45, lb), (60, SIT)], False,
                    lambda f, n: breath(f, n, 0.5)))

    # Win_Cheer: both fists in the air, pumping (2 s loop)
    w_up = P_(grip_L=0.0, grip_R=0.0, hip_rot=(-18, 0, 0), spine=(-2, 0, 0), chest=(-4, 0, 0), uchest=(-4, 0, 0),
              neck=(-6, 0, 0), head=(-12, 0, 0), shrug_L=22, shrug_R=22, clavfwd_L=0, clavfwd_R=0,
              hpos_L=(0.27, -0.06, 0.92), hpos_R=(-0.27, -0.06, 0.92),
              hdir_L=(0.1, -0.2, 1.0), hdir_R=(-0.1, -0.2, 1.0), hpalm_L=(-0.3, -1.0, 0.0), hpalm_R=(0.3, -1.0, 0.0),
              elbow_L=(1.0, 0.3, -0.2), elbow_R=(-1.0, 0.3, -0.2), curl_L=1.4, curl_R=1.4, thumb_L=1.2, thumb_R=1.2)
    w_dn = dict(w_up, hpos_L=(0.32, -0.08, 0.74), hpos_R=(-0.32, -0.08, 0.74), hip_rot=(-15, 0, 0), head=(-6, 0, 0),
                hdir_L=(0.2, -0.4, 1.0), hdir_R=(-0.2, -0.4, 1.0), shrug_L=14, shrug_R=14)
    out.append(Clip("Win_Cheer", 60, [(0, w_up), (15, w_dn), (30, w_up), (45, w_dn), (60, w_up)], True,
                    lambda f, n: {"hip_rot": (0, 3 * sinf(f, n, 1), 0), "head": (0, 0, 8 * sinf(f, n, 1, 0.1))}))

    # Lose_Shrug: palms up shrug, then slump (2 s one-shot, ends slumped)
    sh = P_(grip_L=0.0, grip_R=0.0, hip_rot=(-14, 0, 0), spine=(4, 0, 0), chest=(2, 0, 0), uchest=(0, 0, 0),
            neck=(0, 3, 0), head=(-2, 10, 4), shrug_L=20, shrug_R=20, clavfwd_L=4, clavfwd_R=4,
            hpos_L=(0.33, -0.30, 0.20), hpos_R=(-0.33, -0.30, 0.20),
            hdir_L=(0.6, -1.0, 0.1), hdir_R=(-0.6, -1.0, 0.1), hpalm_L=(0, 0, 1), hpalm_R=(0, 0, 1),
            elbow_L=(0.4, 0.6, -1.0), elbow_R=(-0.4, 0.6, -1.0), curl_L=0.15, curl_R=0.15, thumb_L=0.0, thumb_R=0.0)
    slump = P_(grip_L=0.0, grip_R=0.0, hip_rot=(-20, 0, 0), spine=(12, 0, 0), chest=(10, 0, 0), uchest=(8, 0, 0),
               neck=(12, 0, 0), head=(16, -4, -6), shrug_L=-6, shrug_R=-6, clavfwd_L=10, clavfwd_R=10,
               hpos_L=(0.14, -0.36, 0.11), hpos_R=(-0.14, -0.36, 0.11),
               hdir_L=(0.05, -1.0, -0.3), hdir_R=(-0.05, -1.0, -0.3), hpalm_L=(-0.2, 0, -1), hpalm_R=(0.2, 0, -1),
               elbow_L=(1.0, 0.4, -0.3), elbow_R=(-1.0, 0.4, -0.3), curl_L=0.3, curl_R=0.3, thumb_L=0.3, thumb_R=0.3)
    out.append(Clip("Lose_Shrug", 60, [(0, SIT), (12, sh), (28, sh), (46, slump), (60, slump)], False))

    # Wave: right hand waves, left hand stays on the bar (2 s loop)
    wv = P_(grip_R=0.0, hip_rot=(-14, -2, 0), spine=(4, -2, 0), chest=(2, -2, -4), uchest=(2, 0, -4),
            neck=(0, 0, -4), head=(-4, -6, -8), shrug_R=10, clavfwd_R=4,
            hpos_R=(-0.36, -0.14, 0.66), hdir_R=(-0.15, -0.1, 1.0), hpalm_R=(0, -1.0, 0.0),
            elbow_R=(-1.0, 0.2, -0.6), curl_R=0.0, thumb_R=0.0)
    wa = dict(wv, hpos_R=(-0.27, -0.16, 0.68), hdir_R=(0.25, -0.1, 1.0))
    wb = dict(wv, hpos_R=(-0.44, -0.12, 0.62), hdir_R=(-0.55, -0.1, 1.0))
    out.append(Clip("Wave", 60, [(0, wa), (15, wb), (30, wa), (45, wb), (60, wa)], True,
                    lambda f, n: breath(f, n, 0.5)))
    return out


# --------------------------------------------------------------------------------------------
# Baking to actions
# --------------------------------------------------------------------------------------------

def _fcurve(act, ob, path, idx, group):
    try:
        return act.fcurve_ensure_for_datablock(ob, path, index=idx, group_name=group)
    except AttributeError:  # older API
        return act.fcurves.new(path, index=idx, action_group=group)


def bake_clip(rig, clip):
    ob = rig.ob
    ob.animation_data_create()
    act = bpy.data.actions.get(clip.name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(clip.name)
    act.use_fake_user = True
    ob.animation_data.action = act
    frames = list(range(0, clip.length + 1))
    data = {b: [] for b in rig.order}
    prevq = {}
    for f in frames:
        P = clip.pose(f)
        if clip.loop and f == clip.length:
            P = clip.pose(0)  # exact loop closure
        basis = to_basis(rig, solve(rig, P))
        for b in rig.order:
            loc, q, _s = basis[b].decompose()
            if b in prevq and prevq[b].dot(q) < 0:
                q = -q
            prevq[b] = q
            data[b].append((loc, q))
    for b in rig.order:
        pb = f'pose.bones["{b}"]'
        chans = [("rotation_quaternion", 4)]
        if b == "Hips":
            chans.append(("location", 3))
        for path, cnt in chans:
            for i in range(cnt):
                fc = _fcurve(act, ob, f"{pb}.{path}", i, b)
                fc.keyframe_points.add(len(frames))
                co = []
                for f, (loc, q) in zip(frames, data[b]):
                    co += [float(f), (q[i] if path.startswith("rot") else loc[i])]
                fc.keyframe_points.foreach_set("co", co)
                for kp in fc.keyframe_points:
                    kp.interpolation = 'LINEAR'
                fc.update()
    try:
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, clip.length
        act.use_cyclic = clip.loop
    except (AttributeError, TypeError) as ex:
        print("frame range warn", ex)
    return act


def build_all_actions(arm_ob):
    rig = Rig(arm_ob)
    acts = []
    for c in clips():
        acts.append(bake_clip(rig, c))
        print("CLIP", c.name, c.length, "loop" if c.loop else "once")
    arm_ob.animation_data.action = bpy.data.actions["Sit_Idle"]
    return acts
