"""Luge Rush - Forest kit "Yeşil Vadi" (tropical rainforest, closest to the reference video).

Run:  blender -b -P Art/Scripts/env_forest.py
FBX -> Environment/Forest/, renders -> Art/Renders/env_forest_*.png, source -> Art/Blender/Env_Forest.blend
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import env_common as E  # noqa: E402
import env_kit_common as K  # noqa: E402
from env_common import B, R, Vector, shade, pick, const  # noqa: E402

BIOME = "Forest"

# canopy ramps light -> deep (top faces light, undersides dark)
CANOPY = [
    ["leaf_bright_base", "leaf_mid_light", "leaf_mid_base", "leaf_mid_dark", "leaf_dark_dark", "leaf_dark_deep"],
    ["leaf_lime_base", "leaf_bright_base", "leaf_bright_dark", "leaf_mid_dark", "leaf_mid_deep", "leaf_dark_deep"],
    ["leaf_mid_base", "leaf_dark_light", "leaf_dark_base", "leaf_mid_deep", "leaf_dark_dark", "leaf_dark_deep"],
    ["leaf_lime_light", "leaf_lime_base", "leaf_lime_dark", "leaf_bright_dark", "leaf_bright_deep", "leaf_dark_deep"],
    ["leaf_dark_light", "leaf_mid_dark", "leaf_dark_base", "leaf_dark_dark", "leaf_mid_deep", "leaf_dark_deep"],
]
BARK = ["trunk_light", "trunk_base", "trunk_dark", "trunk_deep"]
BARK_PALE = ["granite_base", "trunk_light", "granite_dark", "trunk_base", "granite_cool"]
LEAF_TOP = ["leaf_bright_base", "leaf_bright_light", "leaf_lime_base", "leaf_lime_light"]
LEAF_BOT = ["leaf_mid_dark", "leaf_mid_deep", "leaf_dark_dark"]


def bark_cf(rng, ramp=BARK, moss=0.0):
    def cf(f):
        if moss and f.normal.z > -0.2 and rng.random() < moss:
            return rng.choice(["moss", "fern", "leaf_dark_light"])
        return rng.choices(ramp, weights=[2, 3, 3, 1, 1][:len(ramp)])[0]
    return cf


def bend_path(rng, base, height, n, lean, lean_dir, wobble=0.15, curve=1.6):
    pts = []
    for i in range(n):
        t = i / (n - 1)
        off = lean * height * (t ** curve)
        x = base[0] + math.cos(lean_dir) * off + (rng.uniform(-wobble, wobble) if 0 < i < n - 1 else 0)
        y = base[1] + math.sin(lean_dir) * off + (rng.uniform(-wobble, wobble) if 0 < i < n - 1 else 0)
        pts.append(Vector((x, y, base[2] + t * height)))
    return pts


def cluster(b, c, r, ramp, flat=0.55, sats=2, sub=2):
    """Lumpy foliage cluster: one main blob + satellite blobs; light top / dark underside."""
    rng = b.rng
    cf = shade(ramp, spread=0.45, rng=rng)
    b.ico(c, (r * rng.uniform(0.9, 1.15), r * rng.uniform(0.9, 1.15), r * flat), sub, cf, jit=0.22,
          rot=(0, 0, rng.uniform(0, 6)))
    for _ in range(sats):
        a = rng.uniform(0, math.tau)
        rr = r * rng.uniform(0.45, 0.7)
        cc = Vector(c) + Vector((math.cos(a) * r * 0.75, math.sin(a) * r * 0.75, rng.uniform(-0.25, 0.15) * r))
        b.ico(cc, (rr, rr, rr * flat * 1.1), 1, cf, jit=0.2, rot=(0, 0, rng.uniform(0, 6)))


# --------------------------------------------------------------------------- tall thin tropical tree
def tall_tree(name, var):
    b = B(2000 + var * 7)
    rng = b.rng
    H = [19.0, 21.0, 16.0, 22.0, 15.0][var]
    r0 = [0.26, 0.3, 0.2, 0.28, 0.22][var]
    lean_dir = rng.uniform(0, math.tau)
    lean = [0.07, 0.04, 0.16, 0.05, 0.1][var]
    ramp = CANOPY[[0, 2, 1, 4, 3][var]]
    bark = BARK_PALE if var in (1, 3) else BARK
    if var == 2:  # tall curved palm
        pts = bend_path(rng, (0, 0, -0.2), H, 9, lean, lean_dir, wobble=0.05, curve=2.0)
        radii = [r0 * 1.6] + [r0 * (1 - 0.35 * i / 8) for i in range(1, 9)]
        faces, caps, rings = b.tube(pts, radii, 6, bark_cf(rng, ["trunk_light", "trunk_base", "granite_warm", "trunk_dark"]),
                                    cap_top=True, jit=0.05)
        # ring scars: alternate colour bands
        for i, f in enumerate(faces):
            if (i // 6) % 2:
                f[b.L] = E.IDX["trunk_dark"]
        top = pts[-1]
        for k in range(10):
            a = k * math.tau / 10 + rng.uniform(-0.2, 0.2)
            el = rng.uniform(-0.05, 0.6) if k % 3 else rng.uniform(-0.6, -0.3)
            d = Vector((math.cos(a), math.sin(a), el))
            dry = k == 4
            b.leaf(top, d, rng.uniform(3.2, 4.4), rng.uniform(0.38, 0.5), 5, rng.uniform(1.0, 1.8),
                   ["leaf_dry", "dry_bush_base"] if dry else [rng.choice(LEAF_TOP[:2]), rng.choice(LEAF_TOP)],
                   ["dry_bush_dark"] if dry else LEAF_BOT, fold=0.25, torn=0.55)
        for k in range(4):  # coconuts
            a = k * math.tau / 4 + 0.4
            b.ico(top + Vector((math.cos(a) * 0.28, math.sin(a) * 0.28, -0.35)), 0.17, 1,
                  pick(["leaf_lime_deep", "bamboo_deep", "trunk_light"], rng=rng), jit=0.1)
        return b.finish(name)

    if var == 1:  # forked Y trunk
        pts = bend_path(rng, (0, 0, -0.2), H * 0.55, 5, lean, lean_dir)
        b.tube(pts, [r0 * 1.7, r0 * 1.05, r0 * 0.95, r0 * 0.9, r0 * 0.8], 6, bark_cf(rng, bark), cap_top=False, jit=0.06)
        fork = pts[-1]
        for s in (-1, 1):
            a = lean_dir + s * rng.uniform(0.5, 0.9)
            p2 = fork + Vector((math.cos(a) * 2.2, math.sin(a) * 2.2, H * 0.25))
            p3 = p2 + Vector((math.cos(a) * 1.2, math.sin(a) * 1.2, H * 0.2))
            b.tube([fork - Vector((0, 0, 0.4)), p2, p3], [r0 * 0.75, r0 * 0.55, r0 * 0.3], 5, bark_cf(rng, bark), jit=0.06)
            cluster(b, p3 + Vector((0, 0, 0.6)), rng.uniform(2.6, 3.2), ramp, sats=2)
            cluster(b, p2 + Vector((math.cos(a + 1.5) * 1.2, math.sin(a + 1.5) * 1.2, 0.3)), 1.6, ramp, sub=1, sats=1)
        return b.finish(name)

    pts = bend_path(rng, (0, 0, -0.2), H, 7, lean, lean_dir)
    radii = [r0 * 1.8] + [r0 * (1 - 0.55 * i / 6) for i in range(1, 7)]
    b.tube(pts, radii, 6, bark_cf(rng, bark, moss=0.1 if var == 4 else 0.0), cap_top=True, jit=0.07)
    # root flare fins
    for k in range(4):
        a = k * math.tau / 4 + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        sd = Vector((-d.y, d.x, 0)) * 0.06
        pp = [d * r0 * 0.5 + Vector((0, 0, 1.4)), d * r0 * 0.5 - Vector((0, 0, 0.2)), d * (r0 + 0.9) - Vector((0, 0, 0.15))]
        b.hull([p + sd for p in pp] + [p - sd for p in pp], bark_cf(rng, bark))
    top = pts[-1]
    if var == 0:  # classic: three branches + crown
        for k in range(3):
            i = rng.choice([4, 5])
            a = lean_dir + k * math.tau / 3 + rng.uniform(-0.4, 0.4)
            p0 = pts[i]
            p1 = p0 + Vector((math.cos(a) * 2.4, math.sin(a) * 2.4, rng.uniform(1.6, 2.6)))
            b.tube([p0, p1], [r0 * 0.5, r0 * 0.25], 4, bark_cf(rng, bark))
            cluster(b, p1 + Vector((0, 0, 0.4)), rng.uniform(2.0, 2.6), ramp, sats=1)
        cluster(b, top + Vector((0, 0, 0.5)), 3.0, ramp, sats=2)
    elif var == 3:  # tiered rainforest tree: clusters stacked on alternate sides
        for k, i in enumerate((3, 4, 5)):
            a = lean_dir + k * 2.3
            p0 = pts[i] + Vector((0, 0, rng.uniform(-0.5, 0.5)))
            p1 = p0 + Vector((math.cos(a) * 2.0, math.sin(a) * 2.0, 1.0))
            b.tube([p0, p1], [r0 * 0.45, r0 * 0.2], 4, bark_cf(rng, bark))
            cluster(b, p1, rng.uniform(1.5, 2.1), ramp, sub=1 if k == 0 else 2, sats=1, flat=0.45)
        cluster(b, top + Vector((0, 0, 0.3)), 2.8, ramp, sats=2, flat=0.5)
    else:  # var 4: slender, sparse crown, vine wrapped, epiphytes
        cluster(b, top + Vector((0.6, 0, 0.2)), 2.2, ramp, sats=2, flat=0.6)
        p0 = pts[5]
        a = lean_dir + 2.0
        p1 = p0 + Vector((math.cos(a) * 1.8, math.sin(a) * 1.8, 1.4))
        b.tube([p0, p1], [r0 * 0.4, r0 * 0.2], 4, bark_cf(rng, bark))
        cluster(b, p1, 1.4, ramp, sub=1, sats=1)
        vine = []
        for i in range(22):
            t = i / 21
            z = t * H * 0.75
            # interpolate trunk centre
            seg = min(5, int(t * 0.75 * 6))
            fz = (z - pts[seg].z) / max(0.01, pts[seg + 1].z - pts[seg].z)
            c = pts[seg].lerp(pts[seg + 1], max(0, min(1, fz)))
            ang = t * math.tau * 2.5
            rr = (r0 * (1.0 - 0.4 * t)) + 0.05
            vine.append(c + Vector((math.cos(ang) * rr, math.sin(ang) * rr, 0)))
        b.tube(vine, 0.045, 3, pick(["leaf_dark_base", "moss", "leaf_mid_dark"], rng=rng), cap_top=False)
        for k in range(2):  # epiphyte leaf rosettes on trunk
            c = pts[2 + k * 2]
            for j in range(4):
                a = j * math.tau / 4 + rng.uniform(0, 1)
                b.leaf(c, Vector((math.cos(a), math.sin(a), 0.5)), 0.9, 0.18, 2, 0.8, LEAF_TOP[2:], LEAF_BOT, fold=0.1)
    return b.finish(name)


# --------------------------------------------------------------------------- giant broad-crown tree
def giant_tree(name, var):
    b = B(2100 + var * 11)
    rng = b.rng
    H = [22.0, 26.0, 19.0][var]
    r0 = [0.95, 1.15, 0.8][var]
    ramp = CANOPY[[2, 4, 0][var]]
    bark = BARK if var != 1 else BARK_PALE
    lean_dir = rng.uniform(0, math.tau)
    trunk_h = H * 0.55
    pts = bend_path(rng, (0, 0, -0.3), trunk_h, 5, 0.05, lean_dir, wobble=0.2)
    b.tube(pts, [r0 * 1.35, r0, r0 * 0.85, r0 * 0.75, r0 * 0.7], 8, bark_cf(rng, bark, moss=0.12), cap_top=False, jit=0.06)
    # buttress roots
    for k in range(5):
        a = k * math.tau / 5 + rng.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0))
        sd = Vector((-d.y, d.x, 0)) * 0.14
        L = rng.uniform(2.0, 3.2)
        pp = [d * r0 * 0.6 + Vector((0, 0, rng.uniform(2.5, 3.5))), d * r0 * 0.6 - Vector((0, 0, 0.3)),
              d * (r0 + L) - Vector((0, 0, 0.2)), d * (r0 + L * 0.45) + Vector((0, 0, 0.7))]
        b.hull([p + sd for p in pp] + [p - sd * 0.6 for p in pp], bark_cf(rng, bark, moss=0.15))
    top = pts[-1]
    # main limbs
    nlimb = [5, 6, 4][var]
    ends = []
    for k in range(nlimb):
        a = lean_dir + k * math.tau / nlimb + rng.uniform(-0.3, 0.3)
        spread = rng.uniform(4.5, 7.0) * (1.2 if var == 1 else 1)
        rise = rng.uniform(3.5, 6.0)
        p0 = top - Vector((0, 0, rng.uniform(0, 1.5)))
        p1 = p0 + Vector((math.cos(a) * spread * 0.5, math.sin(a) * spread * 0.5, rise * 0.7))
        p2 = p0 + Vector((math.cos(a) * spread, math.sin(a) * spread, rise))
        b.tube([p0, p1, p2], [r0 * 0.45, r0 * 0.3, r0 * 0.14], 5, bark_cf(rng, bark, moss=0.2), jit=0.08)
        ends.append(p2)
    # canopy: broad umbrella of blobs over limb ends + centre
    for p in ends:
        cluster(b, p + Vector((0, 0, 0.8)), rng.uniform(3.6, 4.6), ramp, flat=0.42, sats=1)
    cluster(b, top + Vector((0, 0, H - trunk_h - 1.5)), 4.8, ramp, flat=0.45, sats=1)
    # hanging lianas
    for k in range(6):
        p = ends[k % len(ends)] + Vector((rng.uniform(-1.5, 1.5), rng.uniform(-1.5, 1.5), -0.6))
        L = rng.uniform(4, 9)
        b.tube([p, p + Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), -L * 0.5)),
                p + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), -L))], 0.05, 3,
               pick(["leaf_dark_dark", "moss", "trunk_dark"], rng=rng), cap_top=False)
    return b.finish(name)


# --------------------------------------------------------------------------- bamboo cluster
def bamboo(name, var):
    b = B(2200 + var * 5)
    rng = b.rng
    n = [7, 8, 6, 8][var]
    Hm = [9.0, 11.0, 7.0, 10.0][var]
    spread = [0.9, 1.3, 0.7, 1.6][var]
    sprays = [1, 1, 2, 1][var]
    BAM = ["bamboo_light", "bamboo_base", "bamboo_dark", "bamboo_deep"]
    yellow = var == 2  # golden bamboo variant
    for k in range(n):
        a = rng.uniform(0, math.tau)
        rr = math.sqrt(rng.random()) * spread
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, -0.2))
        h = Hm * rng.uniform(0.65, 1.1)
        lean = rng.uniform(0.08, 0.28) * (1.4 if var == 3 else 1)
        lean_dir = a + rng.uniform(-0.6, 0.6)
        segs = 7
        pts = bend_path(rng, base, h, segs + 1, lean, lean_dir, wobble=0.03, curve=2.2)
        rad = rng.uniform(0.07, 0.11) * (1.25 if var == 1 else 1)
        faces, caps, rings = b.tube(pts, [rad * (1 - 0.4 * i / segs) for i in range(segs + 1)], 5,
                                    const("bamboo_base"), cap_top=True, jit=0.0)
        for i, f in enumerate(faces + caps):
            seg = i // 5
            t = seg / segs
            if yellow:
                col = ["leaf_young", "bamboo_light", "flower_yellow"][(seg + k) % 3]
            else:
                idx = min(3, max(0, int(round((1 - t) * 2.2 + (seg % 2) * 0.8 + rng.uniform(-0.4, 0.4)))))
                col = BAM[idx]
            f[b.L] = E.IDX[col]
        # leaf sprays on the upper half
        for s in range(sprays):
            i = rng.randint(segs // 2 + 1, segs)
            c = pts[i]
            for j in range(4):
                aa = rng.uniform(0, math.tau)
                d = Vector((math.cos(aa), math.sin(aa), rng.uniform(-0.2, 0.4)))
                b.leaf(c, d, rng.uniform(0.9, 1.3), rng.uniform(0.11, 0.15), 2, rng.uniform(0.6, 1.2),
                       [rng.choice(["leaf_lime_base", "leaf_bright_light", "leaf_lime_light", "leaf_bright_base"])],
                       ["leaf_mid_dark"], fold=0.0, rib=False)
        # feathery leaf fans on the arching upper culm (light tops, dark undersides)
        fol = (["leaf_young", "leaf_lime_light", "leaf_lime_base"] if yellow else
               ["leaf_lime_base", "leaf_bright_base", "leaf_bright_light", "leaf_mid_light", "leaf_bright_dark"])
        for i in ([segs - 2, segs] if k % 2 == 0 else [segs - 1, segs]):
            c = pts[i]
            out = Vector((c.x, c.y, 0))
            ang = math.atan2(out.y, out.x) if out.length > 0.05 else rng.uniform(0, math.tau)
            for j in range(6):
                aa = ang + rng.uniform(-1.6, 1.6)
                d = Vector((math.cos(aa), math.sin(aa), rng.uniform(-0.1, 0.5)))
                b.leaf(c, d, rng.uniform(1.1, 1.6), rng.uniform(0.14, 0.2), 2, rng.uniform(0.9, 1.6),
                       [rng.choice(fol), rng.choice(fol)], ["leaf_mid_dark", "leaf_dark_base"], fold=0.0, rib=False)
    for k in range(3):  # young shoots
        a = rng.uniform(0, math.tau)
        p = Vector((math.cos(a) * spread * 0.8, math.sin(a) * spread * 0.8, -0.05))
        b.tube([p, p + Vector((0, 0, rng.uniform(0.4, 0.9)))], [0.09, 0.0], 5,
               pick(["bamboo_deep", "trunk_light", "leaf_dry"], rng=rng), cap_top=False)
    return b.finish(name)


# --------------------------------------------------------------------------- banana / big-leaf plants
def banana(name, var):
    b = B(2300 + var * 3)
    rng = b.rng
    stem_cols = ["leaf_lime_deep", "bamboo_deep", "trunk_light", "leaf_bright_deep"]
    if var == 3:  # elephant ear: heart leaves on long stalks
        for k in range(7):
            a = k * math.tau / 7 + rng.uniform(-0.3, 0.3)
            L = rng.uniform(0.9, 1.5)
            top = Vector((math.cos(a) * L * 0.45, math.sin(a) * L * 0.45, L))
            b.tube([Vector((0, 0, -0.05)), top * 0.5 + Vector((0, 0, L * 0.15)), top], [0.045, 0.035, 0.02], 3,
                   pick(["leaf_lime_dark", "leaf_bright_dark"], rng=rng), cap_top=False)
            d = Vector((math.cos(a), math.sin(a), rng.uniform(-0.6, -0.1)))
            b.leaf(top, d, rng.uniform(0.9, 1.3), rng.uniform(0.45, 0.6), 3, 0.5,
                   [rng.choice(["leaf_bright_base", "leaf_mid_light", "leaf_bright_light"]), "leaf_bright_base"],
                   LEAF_BOT, fold=0.15, petiole=0.35, profile=0.55)
        return b.finish(name)
    stems = [1, 3, 2][var]
    for s in range(stems):
        a = rng.uniform(0, math.tau)
        base = Vector((math.cos(a), math.sin(a), 0)) * (0 if s == 0 else rng.uniform(0.5, 0.9))
        H = [2.8, 2.4, 1.2][var] * (1 if s == 0 else rng.uniform(0.55, 0.8))
        top = base + Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), H))
        r = [0.16, 0.13, 0.1][var] * (1 if s == 0 else 0.75)
        b.tube([base - Vector((0, 0, 0.1)), base.lerp(top, 0.5), top], [r * 1.25, r, r * 0.8], 6,
               pick(stem_cols, rng=rng), cap_top=True, jit=0.05)
        nl = [8, 6, 5][var] if s == 0 else 4
        for k in range(nl):
            a2 = k * math.tau / nl + rng.uniform(-0.3, 0.3)
            dry = (k == 0 and var != 2 and s == 0)
            el = rng.uniform(0.6, 1.4) if not dry else -0.5
            d = Vector((math.cos(a2), math.sin(a2), el))
            L = rng.uniform(1.8, 2.6) * (0.6 if var == 2 else 1) * (0.8 if s else 1)
            W = L * rng.uniform(0.17, 0.22)
            if dry:
                b.leaf(top - Vector((0, 0, 0.3)), d, L * 0.9, W * 0.7, 4, 2.6, ["leaf_dry", "dry_bush_base"],
                       ["dry_bush_dark"], fold=0.2, torn=0.5)
            else:
                b.leaf(top, d, L, W, 4, rng.uniform(1.0, 1.7),
                       [rng.choice(LEAF_TOP), rng.choice(LEAF_TOP), rng.choice(LEAF_TOP[:2])],
                       LEAF_BOT, fold=0.18, torn=rng.choice([0.0, 0.25, 0.5]), petiole=0.12)
    return b.finish(name)


# --------------------------------------------------------------------------- ferns
def fern(name, var):
    b = B(2400 + var * 9)
    rng = b.rng
    cols_top = [["fern", "leaf_mid_base", "leaf_bright_base"], ["leaf_bright_base", "leaf_lime_base", "leaf_bright_light"],
                ["fern", "leaf_mid_light", "leaf_bright_base"]][var]
    cols_bot = ["leaf_dark_base", "leaf_dark_dark"]
    base = Vector((0, 0, 0))
    n = [11, 9, 9][var]
    L0 = [1.0, 1.3, 1.6][var]
    if var == 2:  # small tree fern
        H = 1.8
        pts = [Vector((0, 0, -0.1)), Vector((0.08, 0.02, H * 0.5)), Vector((0.12, -0.05, H))]
        b.tube(pts, [0.16, 0.12, 0.13], 5, pick(["trunk_dark", "trunk_deep", "moss"], rng=rng), cap_top=True, jit=0.1)
        base = pts[-1]
    for k in range(n):
        a = k * math.tau / n + rng.uniform(-0.2, 0.2)
        el = rng.uniform(0.5, 1.4)
        d = Vector((math.cos(a), math.sin(a), el))
        b.leaf(base, d, L0 * rng.uniform(0.75, 1.15), rng.uniform(0.13, 0.18), 4, rng.uniform(1.4, 2.1),
               [cols_top[0], rng.choice(cols_top), cols_top[-1]], cols_bot, fold=0.0, rib=False, torn=0.6,
               petiole=0.2, profile=0.6)
    return b.finish(name)


# --------------------------------------------------------------------------- vine curtain
def vines(name, var):
    b = B(2500 + var)
    rng = b.rng
    W = [5.0, 3.5][var]
    Z = [6.0, 4.5][var]
    branch = [Vector((-W / 2 - 0.4, 0, Z + rng.uniform(-0.2, 0.2))), Vector((-W / 6, 0.1, Z + 0.25)),
              Vector((W / 6, -0.1, Z - 0.1)), Vector((W / 2 + 0.4, 0, Z + 0.15))]
    b.tube(branch, [0.2, 0.17, 0.16, 0.12], 5, bark_cf(rng, BARK, moss=0.45), cap_top=True, cap_bot=True)
    n = [16, 14][var]
    for k in range(n):
        x = -W / 2 + (k + rng.uniform(0.1, 0.9)) * W / n
        L = rng.uniform(1.8, Z - 0.6) if var == 0 else rng.uniform(1.2, 3.2)
        p0 = Vector((x, rng.uniform(-0.1, 0.1), Z - 0.05))
        segs = 4
        pts = [p0 + Vector((math.sin(i * 1.3 + k) * 0.12, rng.uniform(-0.12, 0.12), -L * i / segs)) for i in range(segs + 1)]
        b.tube(pts, [0.03] * (segs + 1), 3, pick(["leaf_dark_base", "leaf_mid_dark", "trunk_dark"], rng=rng), cap_top=False)
        for j in range(2 if var == 0 else 3):
            p = pts[rng.randint(1, segs)]
            a = rng.uniform(0, math.tau)
            b.leaf(p, Vector((math.cos(a), math.sin(a), 0.2)), rng.uniform(0.25, 0.4), 0.11, 2, 0.8,
                   [rng.choice(["leaf_bright_base", "leaf_mid_light", "leaf_lime_base"])], ["leaf_dark_base"],
                   fold=0.0, rib=False)
        if var == 1 and k % 3 == 0:
            p = pts[-1]
            b.hull([p + Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), rng.uniform(-0.1, 0.05))) for _ in range(5)],
                   const(rng.choice(["flower_pink", "flower_red", "flower_purple"])))
    return b.finish(name)


# --------------------------------------------------------------------------- flowering bush
def flower_bush(name, var):
    b = B(2600 + var * 3)
    rng = b.rng
    flower = [["flower_red", "sled_red_light"], ["flower_yellow", "sign_yellow"], ["flower_pink", "flower_white"]][var]
    ramp = CANOPY[[2, 0, 3][var]]
    blobs = [4, 5, 3][var]
    tops = []
    for k in range(blobs):
        a = k * math.tau / blobs + rng.uniform(-0.4, 0.4)
        rr = 0.45 if k else 0.0
        r = rng.uniform(0.45, 0.7) * (1.2 if k == 0 else 1)
        c = Vector((math.cos(a) * rr, math.sin(a) * rr, r * 0.75 + (0.2 if k == 0 else 0)))
        b.ico(c, (r, r * rng.uniform(0.85, 1.1), r * 0.8), 1, shade(ramp, spread=0.4, rng=rng), jit=0.25)
        tops.append((c, r))
    for k in range(10):
        c, r = rng.choice(tops)
        a = rng.uniform(0, math.tau)
        el = rng.uniform(0.2, 1.1)
        p = c + Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el) * 0.8)) * r * 0.95
        s = rng.uniform(0.07, 0.11)
        b.hull([p + Vector((rng.uniform(-s, s), rng.uniform(-s, s), rng.uniform(-s, s) * 0.6)) for _ in range(5)],
               const(rng.choice(flower)))
    return b.finish(name)


# --------------------------------------------------------------------------- mossy rock
def moss_rock(name, var):
    cat = ["M", "L", "L"][var]
    o = K.build_rock(name + "_tmp", cat, var + 1, pal=K.P_COMMON, moss=0.55, seed_base=2700)
    # rebuild with moss pads: simpler to build fresh
    E.bpy.data.objects.remove(o)
    b = B(2700 + var)
    rng = b.rng
    spec = K.ROCK_SIZES[cat]
    size = tuple(s * [1.0, 0.9, 1.25][var] for s in spec["size"])
    pts = b.rock_points(size, spec["n"] + 6, cuts=3, squash=[0.8, 0.65, 0.9][var])

    def cf(f):
        if f.normal.z > 0.35 and rng.random() < 0.75:
            return rng.choice(["moss", "fern", "leaf_dark_base", "leaf_mid_dark", "leaf_dark_light"])
        if f.normal.z > -0.1 and rng.random() < 0.25:
            return "moss"
        return shade(K.GRANITE[2:], spread=0.6, rng=rng)(f)
    b.hull(pts, cf, merge_deg=1.5)
    for k in range(2):
        a = rng.uniform(0, math.tau)
        c = Vector((math.cos(a) * size[0] * 0.3, math.sin(a) * size[1] * 0.3, size[2] * 0.62))
        b.ico(c, (size[0] * 0.35, size[1] * 0.3, size[2] * 0.12), 1, pick(["moss", "fern", "leaf_dark_base"], rng=rng), jit=0.2)
    if var == 2:  # small fern growing at the base
        for k in range(6):
            a = k * math.tau / 6 + 0.3
            b.leaf(Vector((size[0] * 0.9, 0, 0)), Vector((math.cos(a), math.sin(a), 0.9)), 0.7, 0.1, 3, 1.4,
                   ["fern", "leaf_bright_base"], ["leaf_dark_base"], fold=0.0, rib=False, torn=0.5)
    return b.finish(name)


# --------------------------------------------------------------------------- footbridge
WOOD = ["wood_light", "wood_base", "wood_dark", "wood_deep"]


def footbridge(name):
    """Wooden footbridge spanning the track along X (span 16 m, clear height ~4.25 m). Pivot: under span centre."""
    b = B(2800)
    rng = b.rng
    wcf = shade(WOOD, spread=0.6, rng=rng)
    deck_z = 4.65
    half = 8.0
    wy = 1.1
    # towers / posts
    for x in (-5.2, 5.2):
        for y in (-wy, wy):
            b.beam((x + rng.uniform(-0.05, 0.05), y, -0.5), (x, y, deck_z + 1.15), 0.26, 0.26, wcf, jit=0.015)
        # X brace between the two legs of each tower
        b.beam((x, -wy, 0.6), (x, wy, deck_z - 0.5), 0.12, 0.08, wcf)
        b.beam((x, wy, 0.6), (x, -wy, deck_z - 0.5), 0.12, 0.08, wcf)
        # knee braces to girders
        for y in (-wy, wy):
            for s in (-1, 1):
                b.beam((x, y, deck_z - 1.7), (x + s * 1.6, y, deck_z - 0.25), 0.12, 0.12, wcf)
    # girders
    for y in (-wy + 0.15, wy - 0.15):
        b.beam((-half, y, deck_z - 0.2), (half, y, deck_z - 0.2), 0.22, 0.34, wcf, jit=0.01)
    # cross beams
    for x in (-7.2, -2.6, 0.0, 2.6, 7.2):
        b.beam((x, -wy - 0.15, deck_z - 0.06), (x, wy + 0.15, deck_z - 0.06), 0.16, 0.14, wcf)
    # planks
    n = 30
    for i in range(n):
        x = -half + (i + 0.5) * (2 * half / n)
        w = 2 * half / n - 0.05
        col = rng.choices(WOOD, weights=[3, 4, 2, 1])[0]
        b.box((x, rng.uniform(-0.04, 0.04), deck_z + 0.05 + rng.uniform(-0.01, 0.015)), (w, wy * 2 + 0.1, 0.06),
              const(col), rot=(rng.uniform(-0.015, 0.015), 0, rng.uniform(-0.02, 0.02)))
    # railings
    for y in (-wy, wy):
        for i in range(9):
            x = -half + 0.2 + i * (2 * half - 0.4) / 8
            if abs(abs(x) - 5.2) < 0.4:
                continue
            b.beam((x, y, deck_z + 0.08), (x + rng.uniform(-0.03, 0.03), y, deck_z + 1.1), 0.1, 0.1, wcf)
        b.beam((-half, y, deck_z + 1.1), (half, y, deck_z + 1.1), 0.14, 0.09, wcf, jit=0.01)
        b.beam((-half, y, deck_z + 0.6), (half, y, deck_z + 0.6), 0.06, 0.06, const("rope"))
    # moss and a few leaves creeping on the tower bases (organic touch)
    for x in (-5.2, 5.2):
        for k in range(3):
            a = rng.uniform(0, math.tau)
            b.leaf(Vector((x, -wy, rng.uniform(0.6, 3.0))), Vector((math.cos(a), math.sin(a), 0.3)), 0.4, 0.14, 2, 0.6,
                   ["leaf_bright_base"], ["leaf_dark_base"], fold=0.0, rib=False)
    return b.finish(name)


# --------------------------------------------------------------------------- viewing platform
def platform(name):
    b = B(2900)
    rng = b.rng
    wcf = shade(WOOD, spread=0.6, rng=rng)
    Z = 2.8
    W, D = 4.4, 3.2
    xs = (-W / 2, 0.0, W / 2)
    ys = (-D / 2, D / 2)
    for x in xs:
        for y in ys:
            b.beam((x + rng.uniform(-0.05, 0.05), y + rng.uniform(-0.05, 0.05), -0.4), (x, y, Z), 0.22, 0.22, wcf, jit=0.02)
    for y in ys:
        b.beam((-W / 2 - 0.2, y, Z - 0.15), (W / 2 + 0.2, y, Z - 0.15), 0.18, 0.28, wcf)
    for x in xs:  # diagonal braces
        b.beam((x, -D / 2, 0.4), (x, D / 2, Z - 0.4), 0.1, 0.08, wcf)
    n = 12
    for i in range(n):
        y = -D / 2 + (i + 0.5) * D / n
        b.box((rng.uniform(-0.04, 0.04), y, Z + 0.03), (W + 0.3, D / n - 0.04, 0.06),
              const(rng.choices(WOOD, weights=[3, 4, 2, 1])[0]), rot=(0, rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01)))
    # roof posts
    for x in (-W / 2, W / 2):
        for y in ys:
            b.beam((x, y, Z), (x, y, Z + 2.5), 0.16, 0.16, wcf)
    # railing front + sides (stairs open on the back-left)
    rails = [((-W / 2, -D / 2), (W / 2, -D / 2)), ((-W / 2, -D / 2), (-W / 2, D / 2)), ((W / 2, -D / 2), (W / 2, D / 2)),
             ((-W / 2 + 1.2, D / 2), (W / 2, D / 2))]
    for (x0, y0), (x1, y1) in rails:
        b.beam((x0, y0, Z + 1.0), (x1, y1, Z + 1.0), 0.12, 0.08, wcf)
        b.beam((x0, y0, Z + 0.5), (x1, y1, Z + 0.5), 0.05, 0.05, const("rope"))
        L = math.hypot(x1 - x0, y1 - y0)
        for i in range(1, int(L / 1.1)):
            t = i / int(L / 1.1)
            b.beam((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, Z), (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, Z + 1.0), 0.07, 0.07, wcf)
    # stairs down the back-left
    steps = 9
    for i in range(steps):
        t = (i + 1) / steps
        b.box((-W / 2 + 0.6, D / 2 + 0.3 + (1 - t) * 3.0, Z * t - 0.05), (1.0, 0.3, 0.07),
              const(rng.choice(WOOD[:3])))
    for x in (-W / 2 + 0.08, -W / 2 + 1.12):
        b.beam((x, D / 2 + 3.4, -0.2), (x, D / 2, Z), 0.08, 0.22, wcf)
    # thatched hip roof
    rz = Z + 2.5
    ov = 0.6
    corners = [Vector((-W / 2 - ov, -D / 2 - ov, rz - 0.25)), Vector((W / 2 + ov, -D / 2 - ov, rz - 0.25)),
               Vector((W / 2 + ov, D / 2 + ov, rz - 0.25)), Vector((-W / 2 - ov, D / 2 + ov, rz - 0.25))]
    ridge = [Vector((-0.8, 0, rz + 1.4)), Vector((0.8, 0, rz + 1.5))]
    vc = [b.v(c + Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), rng.uniform(-0.08, 0.08)))) for c in corners]
    vr = [b.v(r) for r in ridge]
    thatch = ["leaf_dry", "dry_bush_base", "dry_bush_light", "dry_bush_dark"]
    # split each roof plane into strips for colour variation
    planes = [[vc[0], vc[1], vr[1], vr[0]], [vc[2], vc[3], vr[0], vr[1]], [vc[1], vc[2], vr[1]], [vc[3], vc[0], vr[0]]]
    roof_faces = [b.face(pl, "leaf_dry", out=Vector((0, 0, 1))) for pl in planes]
    for g in bmesh_subdivide(b, roof_faces, 2):
        g[b.L] = E.IDX[rng.choice(thatch)]
    # underside (dark)
    b.face([b.v(c - Vector((0, 0, 0.12))) for c in corners], "dry_bush_deep", out=Vector((0, 0, -1)))
    # fringe
    for i in range(4):
        a, c = corners[i], corners[(i + 1) % 4]
        for k in range(6):
            t0, t1 = k / 6, (k + 1) / 6
            p0, p1 = a.lerp(c, t0), a.lerp(c, t1)
            dz = rng.uniform(0.2, 0.4)
            b.face([b.v(p0), b.v(p1), b.v(p1 - Vector((0, 0, dz))), b.v(p0 - Vector((0, 0, dz * 1.1)))],
                   rng.choice(thatch[1:]), out=(p0 + p1) * 0.5 - Vector((0, 0, rz)))
    return b.finish(name)


def bmesh_subdivide(b, fs, cuts):
    import bmesh
    edges = list({e for f in fs for e in f.edges})
    res = bmesh.ops.subdivide_edges(b.bm, edges=edges, cuts=cuts, use_grid_fill=True)
    faces = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
    for g in faces:
        g.normal_update()
    # jitter new inner verts a little for a ruffled thatch
    for g in res["geom_inner"]:
        if isinstance(g, bmesh.types.BMVert):
            g.co.z += b.rng.uniform(-0.06, 0.06)
    return faces


# --------------------------------------------------------------------------- build all
def build_kit():
    objs = {}

    def add(o):
        objs[o.name] = o
    for v in range(5):
        add(tall_tree(f"SM_Env_Forest_TallTree_{v + 1:02d}", v))
    for v in range(3):
        add(giant_tree(f"SM_Env_Forest_GiantTree_{v + 1:02d}", v))
    for v in range(4):
        add(bamboo(f"SM_Env_Forest_Bamboo_{v + 1:02d}", v))
        add(banana(f"SM_Env_Forest_BananaPlant_{v + 1:02d}", v))
    for v in range(3):
        add(fern(f"SM_Env_Forest_Fern_{v + 1:02d}", v))
        add(flower_bush(f"SM_Env_Forest_FlowerBush_{v + 1:02d}", v))
        add(moss_rock(f"SM_Env_Forest_MossRock_{v + 1:02d}", v))
    for v in range(2):
        add(vines(f"SM_Env_Forest_VineCurtain_{v + 1:02d}", v))
    add(footbridge("SM_Env_Forest_Footbridge_01"))
    add(platform("SM_Env_Forest_ViewPlatform_01"))
    return objs


def compose(objs, common):
    """Preview diorama: wet track through dense jungle, noon light + green haze."""
    bpy = E.bpy
    coll = bpy.data.collections.new("Composition")
    bpy.context.scene.collection.children.link(coll)
    ts = E.TrackScene(lambda y: 4.0 * math.sin(y / 38.0) - 0.02 * y, grade=0.09,
                      bank_l=lambda d: 3.8 * E.smoothstep(0.4, 6.5, d) + 0.25 * max(0.0, d - 6.5),
                      bank_r=lambda d: 4.6 * E.smoothstep(0.5, 6.0, d) + 0.35 * max(0.0, d - 6.0), seed=21)
    g = ts.build_ground(["leaf_dark_base", "moss", "soil_dark", "leaf_dark_dark", "fern"],
                        ["leaf_dark_dark", "leaf_mid_deep", "leaf_dark_deep", "soil_deep"], y_range=(-20, 230))
    t = ts.build_track(y_range=(-20, 230))
    for o in (g, t):
        bpy.context.scene.collection.objects.unlink(o)
        coll.objects.link(o)
    get = lambda key: [o for n, o in objs.items() if key in n]  # noqa: E731
    cget = lambda key: [o for n, o in common.items() if key in n]  # noqa: E731
    placed = []
    yr = (-5, 220)
    ts.scatter(cget("Slope"), 40, (-0.2, 0.3), yr, (0.9, 1.2), sink=0.25, tilt=0.0, face_track=True, coll=coll)
    ts.scatter(get("GiantTree"), 10, (7, 22), (10, 220), (0.9, 1.15), min_gap=8, placed=placed, coll=coll)
    ts.scatter(get("TallTree"), 120, (2.5, 30), yr, (0.8, 1.25), min_gap=2.6, placed=placed, coll=coll, tilt=0.07)
    ts.scatter(get("Bamboo"), 34, (1.0, 8), yr, (0.85, 1.25), min_gap=3.0, placed=placed, coll=coll, tilt=0.05)
    ts.scatter(get("BananaPlant"), 70, (0.5, 9), yr, (0.8, 1.35), min_gap=1.4, placed=placed, coll=coll)
    ts.scatter(get("Fern"), 160, (0.2, 10), yr, (0.8, 1.5), min_gap=0.8, placed=placed, coll=coll)
    ts.scatter(get("FlowerBush"), 22, (0.5, 6), yr, (0.8, 1.2), min_gap=1.3, placed=placed, coll=coll)
    ts.scatter(get("MossRock") + cget("RockMedium"), 26, (0.3, 9), yr, (0.7, 1.2), min_gap=1.4, placed=placed, coll=coll)
    ts.scatter(cget("Grass"), 160, (0.0, 6), yr, (0.8, 1.6), coll=coll)
    ts.scatter(cget("Stump") + cget("Log"), 5, (1, 7), yr, (0.9, 1.1), min_gap=2, placed=placed, coll=coll)
    # hero pieces
    yb = 42.0
    br = E.instance(objs["SM_Env_Forest_Footbridge_01"], (ts.cx(yb), yb, ts.zt(yb)), 0.04, 1.0, coll=coll)
    yv = 24.0
    xv = ts.cx(yv) - ts.half - 6.0
    E.instance(objs["SM_Env_Forest_ViewPlatform_01"], (xv, yv, ts.h(xv, yv) - 0.2), 0.3, 1.0, coll=coll)
    for k, yy in enumerate((14.0, 70.0)):
        xx = ts.cx(yy) + ts.half + 2.2
        E.instance(objs[f"SM_Env_Forest_VineCurtain_{k + 1:02d}"], (xx, yy, ts.h(xx, yy) - 0.3), 1.45, 1.0, coll=coll)
    # far hills
    for i, (nm, y, x, s) in enumerate((("MountainCard_02", 420, -60, 1.4), ("MountainCard_01", 650, 80, 1.8))):
        E.instance(common["SM_Env_Common_" + nm], (x, y, -40), 0.0, s, coll=coll)
    return ts


def main():
    E.reset()
    common = K.build_kit()
    for o in common.values():
        o.hide_render = True
        o.hide_viewport = True
    objs = build_kit()
    for o in objs.values():
        E.export_fbx(o, BIOME)
    E.write_stats("forest", objs.values())
    bpy = E.bpy

    # ---- variant grids
    E.setup_render((1600, 900), samples=32)
    E.world("#A9CCE6", "#E6EEE8", strength=1.0)
    E.sun(55, 145, "#FFF3DC", 3.4, 3)
    g = E.ground_plane(140, ["tint_light", "tint_light", "tint_mid"], z=-0.02)
    groups = {
        "trees": [["TallTree"], ["GiantTree", "Bamboo"]],
        "plants": [["BananaPlant", "Fern"], ["FlowerBush", "MossRock", "VineCurtain"]],
        "structures": [["Footbridge", "ViewPlatform"]],
    }
    for gname, rows_keys in groups.items():
        for o in objs.values():
            o.hide_render = True
        rows = []
        for keys in rows_keys:
            row = [o for n, o in objs.items() if any(k in n for k in keys)]
            rows.append(row)
            for o in row:
                o.hide_render = False
        E.grid_layout(rows, gap=1.5 if gname != "trees" else 3.0, row_gap=3.0)
        shown = [o for r in rows for o in r]
        g.location = (0, 20, 0)
        cam = E.fit_camera(shown, elev=18 if gname == "trees" else 28, azim=-22)
        E.render(os.path.join(E.RENDER_DIR, f"env_forest_grid_{gname}.png"))
        bpy.data.objects.remove(cam)
    for o in objs.values():
        o.hide_render = True
        o.location = (0, -500, -200)
    g.hide_render = True
    bpy.data.objects.remove(bpy.data.objects["Sun"])

    # ---- composition
    E.setup_render((1280, 720), samples=64, exposure=0.1)
    E.world("#BFD9E8", "#DDE8D8", strength=1.2, fog=(0.0045, "#B9D6AE", 0.4))
    E.sun(68, 200, "#FFF1D6", 4.2, 4)
    ts = compose(objs, common)
    cy = -6.0
    E.camera((ts.cx(cy) + 0.3, cy, ts.zt(cy) + 1.6), (ts.cx(30) - 0.5, 30, ts.zt(30) + 2.4), lens=24)
    E.render(os.path.join(E.RENDER_DIR, "env_forest_composition.png"))
    E.save_blend("Env_Forest.blend")


if __name__ == "__main__":
    main()
