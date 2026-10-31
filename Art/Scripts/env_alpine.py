"""Luge Rush - Alpine kit "Çam Zirvesi" (pines, granite, waterfall, stream, tunnel, chalet, snowy peaks).

Run:  blender -b -P Art/Scripts/env_alpine.py
FBX -> Environment/Alpine/, renders -> Art/Renders/env_alpine_*.png, source -> Art/Blender/Env_Alpine.blend
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import env_common as E  # noqa: E402
import env_kit_common as K  # noqa: E402
from env_common import B, Vector, shade, pick, const  # noqa: E402

BIOME = "Alpine"
PINE_TOP = ["pine_light", "pine_warm", "pine_base", "pine_cool"]
PINE_BOT = ["pine_deep", "pine_dark"]
BARK = ["trunk_base", "trunk_dark", "trunk_deep", "cabin_wood_dark"]
GRAN = K.GRANITE
SNOW = ["snow_light", "snow_base", "snow_dark"]
GRASS = ["alpine_grass_light", "alpine_grass_warm", "alpine_grass_base", "alpine_grass_cool", "alpine_grass_dark"]


def granite_cf(rng, snow_z=None, grass=True, ld=(0.55, -0.6, 0.55)):
    lcf = K.light_shade(GRAN, ld, spread=0.6, rng=rng)

    def cf(f):
        c = f.calc_center_median()
        if f.normal.z > 0.6:
            if snow_z is not None and c.z > snow_z:
                return rng.choice(SNOW[:2])
            if grass and rng.random() < 0.8:
                return rng.choice(GRASS)
        if f.normal.z < -0.5:
            return rng.choice(GRAN[4:])
        return lcf(f)
    return cf


# --------------------------------------------------------------------------- pines
def pine(name, var):
    b = B(3000 + var * 13)
    rng = b.rng
    H = [14.0, 18.0, 8.5, 11.0, 12.0][var]
    layers = [7, 9, 5, 6, 7][var]
    width = [3.0, 2.4, 3.6, 2.8, 3.0][var]
    n = [10, 9, 11, 8, 10][var]
    lean_dir = rng.uniform(0, math.tau)
    trunk_top = Vector((0, 0, H * 0.92))
    if var == 3:  # windswept
        trunk_top = Vector((math.cos(lean_dir) * 1.2, math.sin(lean_dir) * 1.2, H * 0.92))
    mid = Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), H * 0.45))
    b.tube([Vector((0, 0, -0.2)), mid, trunk_top], [0.32 * width / 3, 0.22 * width / 3, 0.06], 6,
           pick(BARK, rng=rng), cap_top=False, jit=0.06)
    start = [0.18, 0.15, 0.12, 0.42, 0.2][var]
    snowy = var == 4
    for L in range(layers):
        t = L / max(1, layers - 1)
        z = H * (start + (0.93 - start) * t)
        r = width * (1 - t * 0.82) * rng.uniform(0.9, 1.1)
        if var == 1:
            r *= 0.85
        c = mid.lerp(trunk_top, max(0, (z - mid.z) / (trunk_top.z - mid.z))) if z > mid.z else Vector((0, 0, z))
        c = Vector((c.x, c.y, z))
        if var == 3:  # flagged crown: push layers downwind, trim upwind
            c += Vector((math.cos(lean_dir), math.sin(lean_dir), 0)) * r * 0.35
        h = H * (0.93 - start) / layers * rng.uniform(1.5, 1.9)
        top, bot = b.cone_layer(c, r, h, n, ["pine_base"], ["pine_deep"], droop=rng.uniform(0.18, 0.32), jit=0.14)
        for i, f in enumerate(top):
            f.normal_update()
            if snowy and f.normal.z > 0.45 and rng.random() < 0.55:
                col = rng.choice(["snow_base", "snow_light"])
            else:
                lit = f.normal.dot(Vector((0.5, -0.5, 0.7)).normalized())
                idx = 0 if lit > 0.45 else (1 if lit > 0.2 else (2 if lit > -0.1 else 3))
                idx = max(0, min(3, idx + (i % 2) + rng.choice((-1, 0, 0, 1)) - (1 if t > 0.6 else 0)))
                col = PINE_TOP[idx] if idx < 4 else PINE_BOT[0]
            f[b.L] = E.IDX[col]
        for f in bot:
            f[b.L] = E.IDX[rng.choice(PINE_BOT)]
    # tip
    tip = trunk_top + Vector((0, 0, H * 0.1))
    b.tube([trunk_top - Vector((0, 0, 0.3)), tip], [0.25, 0.0], 5, pick(PINE_TOP[:2], rng=rng), cap_top=False)
    if var == 2:  # twin leader
        p = Vector((0, 0, H * 0.7))
        p2 = p + Vector((math.cos(lean_dir) * 0.8, math.sin(lean_dir) * 0.8, H * 0.35))
        b.tube([p, p2], [0.12, 0.04], 5, pick(BARK, rng=rng))
        for L in range(2):
            top, bot = b.cone_layer(p.lerp(p2, 0.4 + L * 0.35), width * 0.45 * (1 - L * 0.4), 1.6, 8, ["pine_warm"],
                                    ["pine_deep"], droop=0.25)
            b.paint(top, pick(PINE_TOP, rng=rng))
    return b.finish(name)


def fallen_pine(name):
    b = B(3100)
    rng = b.rng
    L = 10.0
    pts = [Vector((-L / 2 + i * L / 5, rng.uniform(-0.1, 0.1), 0.32 + rng.uniform(-0.04, 0.04) + i * 0.02)) for i in range(6)]
    radii = [0.36, 0.32, 0.29, 0.25, 0.2, 0.12]

    def bark(f):
        if f.normal.z > 0.5 and rng.random() < 0.35:
            return rng.choice(["moss", "alpine_grass_dark"])
        return rng.choice(BARK + ["granite_dark"])
    faces, caps, rings = b.tube(pts, radii, 7, bark, cap_top=True, jit=0.06)
    # root plate at the base (upturned disc of soil + roots)
    c = pts[0] + Vector((-0.25, 0, 0.7))
    plate = []
    for k in range(18):
        a = rng.uniform(0, math.tau)
        rr = rng.uniform(0.8, 1.6)
        plate.append(c + Vector((rng.uniform(-0.25, 0.25), math.cos(a) * rr, math.sin(a) * rr * 0.9)))
    b.hull(plate, lambda f: rng.choice(["soil_base", "soil_dark", "soil_deep", "granite_dark"]), merge_deg=1)
    for k in range(5):
        a = k * math.tau / 5 + 0.3
        d = Vector((-0.4, math.cos(a), math.sin(a)))
        b.tube([c, c + d * 1.9], [0.08, 0.02], 4, pick(BARK, rng=rng), cap_top=False)
    # broken branches + leftover dead needle tufts
    for k in range(6):
        p = pts[1 + k % 4] + Vector((rng.uniform(-0.6, 0.6), 0, 0))
        a = rng.uniform(0, math.tau)
        d = Vector((rng.uniform(-0.3, 0.6), math.cos(a), abs(math.sin(a)) * 0.8 + 0.1)).normalized()
        q = p + d * rng.uniform(0.8, 1.6)
        b.tube([p, q], [0.07, 0.03], 4, pick(BARK, rng=rng), cap_top=False)
        if k % 2 == 0:
            top, bot = b.cone_layer(q - Vector((0, 0, 0.2)), 0.6, 0.5, 7, ["pine_deep"], ["pine_deep"], droop=0.3)
            b.paint(top, pick(["pine_dark", "pine_deep", "dry_bush_dark", "rust_dark"], rng=rng))
    return b.finish(name)


# --------------------------------------------------------------------------- granite cliffs
def cliff(name, var):
    b = B(3200 + var * 7)
    rng = b.rng
    snow_z = [None, 11.0, None, None][var]
    cf = granite_cf(rng, snow_z=snow_z)
    topcf = lambda f: rng.choice(GRASS) if rng.random() < 0.85 else rng.choice(GRAN[:3])  # noqa: E731
    if var == 0:    # wall 10 x 8
        K.cliff_wall(b, 10.0, 8.0, 4.0, 12, 9, cf, topcf, ledges=0.35, depth_noise=0.4)
    elif var == 1:  # tall wall with snow on ledges
        K.cliff_wall(b, 8.0, 14.0, 4.5, 9, 13, cf, lambda f: rng.choice(SNOW[:2]), ledges=0.45, depth_noise=0.4)
    elif var == 2:  # overhang
        K.cliff_wall(b, 9.0, 7.0, 5.0, 11, 9, cf, topcf, overhang=2.2, ledges=0.2, depth_noise=0.3)
    else:           # blocky outcrop: stacked boulders
        for k, (c, s) in enumerate([((0, 0, 0), (3.4, 2.6, 2.2)), ((0.8, 0.6, 2.6), (2.6, 2.0, 2.0)),
                                    ((-1.6, 0.9, 1.4), (2.0, 1.8, 2.0)), ((0.2, 1.2, 4.6), (1.6, 1.4, 1.5))]):
            pts = b.rock_points(s, 26, cuts=4, squash=0.8)
            b.hull([p + Vector(c) for p in pts], cf, merge_deg=1.5)
        for k in range(3):
            a = rng.uniform(0, math.tau)
            pts = b.rock_points((0.6, 0.5, 0.4), 10, cuts=1)
            off = Vector((math.cos(a) * 4.0, math.sin(a) * 3.0, 0))
            b.hull([p + off for p in pts], cf, merge_deg=1.5)
    return b.finish(name)


# --------------------------------------------------------------------------- waterfall
WATER = ["water_light", "water_base", "water_dark", "foam_white", "mist_blue"]


def waterfall(name, var):
    """Flat water cards + foam + framing rocks. Falls face -Y. Place against a cliff module."""
    b = B(3300 + var)
    rng = b.rng
    rock_cf = granite_cf(rng)

    def sheet(x0, x1, z_top, z_bot, y0, cols=4, rows=6, arc=0.8):
        grid = []
        for i in range(cols + 1):
            col = []
            for k in range(rows + 1):
                t = k / rows
                x = x0 + (x1 - x0) * i / cols + (rng.uniform(-0.08, 0.08) if 0 < i < cols else 0)
                z = z_top + (z_bot - z_top) * t
                y = y0 - arc * math.sin(t * math.pi * 0.5) * (1 - t * 0.4) - (0.25 if k == 0 else 0)
                col.append(b.v((x + (t * (x - (x0 + x1) / 2) * 0.15), y, z)))
            grid.append(col)
        for i in range(cols):
            streak = rng.choice([0, 1, 1, 2, 4])
            for k in range(rows):
                f = b.face([grid[i][k], grid[i][k + 1], grid[i + 1][k + 1], grid[i + 1][k]], "water_base",
                           out=Vector((0, -1, 0)))
                if k == 0:
                    col = "foam_white" if rng.random() < 0.7 else "water_light"
                elif k == rows - 1:
                    col = rng.choice(["foam_white", "mist_blue"])
                else:
                    col = WATER[(streak + k) % 3] if rng.random() < 0.75 else rng.choice(["mist_blue", "water_light"])
                f[b.L] = E.IDX[col]

    def pool(c, r, z):
        n = 9
        ring = [b.v(Vector(c) + Vector((math.cos(k * math.tau / n) * r * rng.uniform(0.8, 1.1),
                                        math.sin(k * math.tau / n) * r * 0.7 * rng.uniform(0.8, 1.1), z))) for k in range(n)]
        centre = b.v(Vector(c) + Vector((0, 0, z)))
        for k in range(n):
            b.face([ring[k], ring[(k + 1) % n], centre], rng.choice(["water_base", "water_dark", "water_light"]), out=E.UP)

    def foam(c, w):
        for k in range(5):
            p = Vector(c) + Vector((rng.uniform(-w, w), rng.uniform(-0.6, 0.3), 0.1))
            b.ico(p, (rng.uniform(0.4, 0.7), rng.uniform(0.35, 0.55), rng.uniform(0.2, 0.35)), 1,
                  shade(["foam_white", "foam_white", "mist_blue", "water_light"], rng=rng), jit=0.25)

    def frame_rock(c, s):
        pts = b.rock_points(s, 22, cuts=3)
        b.hull([p + Vector(c) for p in pts], rock_cf, merge_deg=1.5)

    if var == 0:
        H = 9.0
        sheet(-1.4, 1.4, H, 0.3, 0.0, cols=5, rows=7, arc=1.0)
        pool((0, -2.4, 0), 3.0, 0.05)
        foam((0, -1.6, 0), 1.3)
        frame_rock((-2.8, 0.4, 0), (1.6, 1.4, 4.5))
        frame_rock((2.9, 0.3, 0), (1.5, 1.3, 3.8))
        frame_rock((-2.2, 0.8, H - 0.8), (1.2, 1.0, 1.2))
        frame_rock((2.1, 0.8, H - 1.0), (1.3, 1.1, 1.3))
        frame_rock((-3.0, -3.6, 0), (0.8, 0.7, 0.6))
        frame_rock((2.6, -4.0, 0), (0.6, 0.5, 0.5))
    else:  # three-step cascade
        steps = [(3.6, 2.2, 0.0), (2.2, 1.0, -1.5), (1.0, 0.1, -3.0)]
        for k, (zt, zb, y) in enumerate(steps):
            w = 1.0 + k * 0.25
            sheet(-w, w, zt, zb, y, cols=3, rows=3, arc=0.4)
            pool((0, y - 0.9, zb - 0.05), 1.4 + k * 0.3, zb - 0.05)
            foam((0, y - 0.6, zb - 0.1), w * 0.7)
            frame_rock((-w - 1.0, y + 0.2, zb - 0.3), (0.9, 0.9, (zt - zb) + 0.7))
            frame_rock((w + 1.0, y + 0.1, zb - 0.3), (0.9, 0.8, (zt - zb) + 0.5))
    return b.finish(name)


# --------------------------------------------------------------------------- stream bed
def stream(name, var):
    """Stream segment 10 m along +Y (water level -0.25 m). var 0 straight, var 1 45-degree bend."""
    b = B(3400 + var)
    rng = b.rng
    n = 10
    L = 10.0

    def centre(t):
        if var == 0:
            return Vector((0.3 * math.sin(t * 5.0), -L / 2 + t * L, 0)), Vector((1, 0, 0))
        ang = t * math.radians(45)
        Rr = L / math.radians(45)
        p = Vector((Rr - Rr * math.cos(ang) - 2.0, Rr * math.sin(ang) - L / 2, 0))
        return p, Vector((math.cos(ang), -math.sin(ang), 0))

    profile = [(-3.4, 0.15), (-2.2, 0.05), (-1.4, -0.3), (-0.9, -0.42), (0.9, -0.42), (1.4, -0.3), (2.2, 0.05), (3.4, 0.15)]
    rows = []
    for j in range(n + 1):
        p, side = centre(j / n)
        row = []
        for k, (o, z) in enumerate(profile):
            jj = rng.uniform(-0.15, 0.15) if 0 < j < n else 0
            row.append(b.v(p + side * (o + jj) + Vector((0, 0, z + (rng.uniform(-0.06, 0.06) if k not in (3, 4) else 0)))))
        rows.append(row)
    for j in range(n):
        for k in range(len(profile) - 1):
            f = b.face([rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]], "soil_base", out=E.UP)
            if k in (0, 6):
                col = rng.choice(GRASS)
            elif k in (1, 5):
                col = rng.choice(["soil_base", "soil_dark", "granite_base", "alpine_grass_dark"])
            elif k in (2, 4):
                col = rng.choice(["granite_dark", "soil_deep", "granite_cool"])
            else:
                col = "granite_deep"
            f[b.L] = E.IDX[col]
    # water surface
    for j in range(n):
        p0, s0 = centre(j / n)
        p1, s1 = centre((j + 1) / n)
        w0, w1 = 1.25, 1.25
        q = [b.v(p0 - s0 * w0 + Vector((0, 0, -0.22))), b.v(p0 + Vector((0, 0, -0.22))), b.v(p0 + s0 * w0 + Vector((0, 0, -0.22)))]
        r = [b.v(p1 - s1 * w1 + Vector((0, 0, -0.22))), b.v(p1 + Vector((0, 0, -0.22))), b.v(p1 + s1 * w1 + Vector((0, 0, -0.22)))]
        for k in range(2):
            f = b.face([q[k], q[k + 1], r[k + 1], r[k]], "water_base", out=E.UP)
            f[b.L] = E.IDX[rng.choice(["water_base", "water_light", "water_dark", "water_base", "mist_blue"])]
    # stones in and along the stream
    for k in range(9):
        t = rng.random()
        p, side = centre(t)
        o = rng.uniform(-2.6, 2.6)
        s = rng.uniform(0.2, 0.5)
        pts = b.rock_points((s, s * 0.8, s * 0.6), 9, cuts=1)
        z = -0.35 if abs(o) < 1.2 else 0.0
        b.hull([q + p + side * o + Vector((0, 0, z)) for q in pts], granite_cf(rng, grass=False), merge_deg=1.5)
        if abs(o) < 1.2:  # foam ripple behind the stone
            c = p + side * o + Vector((0, 0.35, -0.2))
            b.face([b.v(c + Vector((-s, 0, 0))), b.v(c + Vector((s, 0, 0))), b.v(c + Vector((0, s * 1.6, 0)))], "foam_white", out=E.UP)
    return b.finish(name)


# --------------------------------------------------------------------------- tunnel
ARCH = [(-3.6, 0.0), (-3.65, 1.4), (-3.6, 2.8), (-3.4, 3.8), (-2.9, 4.7), (-2.0, 5.4), (-0.8, 5.8), (0.4, 5.85),
        (1.6, 5.55), (2.6, 4.95), (3.3, 4.0), (3.6, 2.9), (3.65, 1.4), (3.6, 0.0)]


def tunnel(name, mouth=True, D=8.0):
    """Rock tunnel. Track runs along +Y through it. Inner clear width >= 7.2 m, height >= 5.5 m (centre).
    mouth=True: portal with surrounding rock mass; False: plain 8 m interior segment to repeat inside the hill."""
    b = B(3500 + (0 if mouth else 1))
    rng = b.rng
    ys = [0.0, D * 0.35, D * 0.7, D]
    inner, outer = [], []
    for j, y in enumerate(ys):
        ri, ro = [], []
        for k, (x, z) in enumerate(ARCH):
            d = Vector((x, 0, z - 1.6))
            dn = d.normalized() if d.length > 0 else Vector((1, 0, 0))
            jit = rng.uniform(0.0, 0.18)  # inner jitter only outward -> clearance stays
            pi = Vector((x, y, z)) + Vector((dn.x, 0, max(0.0, dn.z))) * jit
            if z == 0.0:
                pi.z = -0.1
            ri.append(b.v(pi))
            thick = rng.uniform(3.0, 4.6) if mouth else rng.uniform(0.8, 1.2)
            po = Vector((x, y, z)) + Vector((dn.x, 0, dn.z)) * thick
            if z == 0.0:
                po = Vector((x + math.copysign(thick, x), y, -0.3))
            if mouth and j == 0:
                po.y -= rng.uniform(0.0, 0.8)
            ro.append(b.v(po + Vector((rng.uniform(-0.7, 0.7), rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.6)))))
        inner.append(ri)
        outer.append(ro)
    m = len(ARCH)
    in_cf = lambda f: rng.choice(["granite_dark", "granite_deep", "granite_cool", "granite_dark"])  # noqa: E731
    out_cf = granite_cf(rng)
    for j in range(len(ys) - 1):
        for k in range(m - 1):
            f = b.face([inner[j][k], inner[j][k + 1], inner[j + 1][k + 1], inner[j + 1][k]], "granite_dark",
                       out=Vector((0, 0, 1.6)) - Vector((ARCH[k][0], 0, ARCH[k][1])))
            f[b.L] = E.IDX[in_cf(f)]
            g = b.face([outer[j][k], outer[j][k + 1], outer[j + 1][k + 1], outer[j + 1][k]], "granite_base",
                       out=Vector((ARCH[k][0], 0, ARCH[k][1] - 1.6)))
            g[b.L] = E.IDX[out_cf(g)]
    for j, sgn in ((0, -1), (len(ys) - 1, 1)):
        for k in range(m - 1):
            f = b.face([inner[j][k], inner[j][k + 1], outer[j][k + 1], outer[j][k]], "granite_base", out=Vector((0, sgn, 0)))
            f.normal_update()
            f[b.L] = E.IDX[K.light_shade(GRAN[:5], (0.4, -0.7, 0.5), rng=rng)(f) if j == 0 else "granite_dark"]
    if mouth:
        # keystone-like boulders around the portal, grass + snow tufts on top
        for k in range(6):
            a = math.pi * (0.1 + 0.8 * k / 5)
            c = Vector((math.cos(a) * 6.5, rng.uniform(-0.6, 1.5), 1.6 + math.sin(a) * 6.0))
            pts = b.rock_points((rng.uniform(1.0, 1.7), 1.2, rng.uniform(0.9, 1.4)), 16, cuts=2)
            b.hull([p + c for p in pts], out_cf, merge_deg=1.5)
        for x in (-6.5, 6.8):
            pts = b.rock_points((1.8, 1.5, 1.6), 18, cuts=3)
            b.hull([p + Vector((x, -1.0, 0)) for p in pts], out_cf, merge_deg=1.5)
        # timber portal frame (inside clearance: posts at |x| = 3.45, beam underside at 4.9 m)
        wcf = shade(["cabin_wood_light", "cabin_wood_base", "cabin_wood_dark", "cabin_wood_deep"], rng=rng)
        for x in (-3.45, 3.45):
            b.beam((x, 0.6, -0.2), (x, 0.6, 4.9), 0.28, 0.28, wcf, jit=0.02)
        b.beam((-3.9, 0.6, 5.08), (3.9, 0.6, 5.08), 0.3, 0.36, wcf, jit=0.02)
    return b.finish(name)


# --------------------------------------------------------------------------- chalet
CW = ["cabin_wood_light", "cabin_wood_warm", "cabin_wood_base", "cabin_wood_cool", "cabin_wood_dark", "cabin_wood_deep"]


def chalet(name):
    b = B(3600)
    rng = b.rng
    W, Dp, Hw = 6.4, 5.2, 3.0
    base_h = 0.7
    # stone plinth
    for k in range(10):
        side = k % 4
        t = rng.random()
        if side in (0, 1):
            c = (-W / 2 + t * W, (-Dp / 2 if side == 0 else Dp / 2), base_h / 2 - 0.1)
        else:
            c = ((-W / 2 if side == 2 else W / 2), -Dp / 2 + t * Dp, base_h / 2 - 0.1)
        pts = b.rock_points((0.45, 0.4, 0.35), 8, cuts=1)
        b.hull([p + Vector(c) for p in pts], pick(GRAN[1:5], rng=rng))
    b.box((0, 0, base_h / 2 - 0.15), (W + 0.1, Dp + 0.1, base_h + 0.3), K.light_shade(GRAN[1:5], rng=rng))
    # log walls: alternating courses overlap at the corners
    n = 8
    lh = Hw / n
    for i in range(n):
        z = base_h + lh * (i + 0.5)
        ext = 0.35
        if i % 2 == 0:
            for y in (-Dp / 2, Dp / 2):
                b.box((rng.uniform(-0.03, 0.03), y, z), (W + 2 * ext, 0.28, lh * 0.96), const(rng.choice(CW[:4])))
        else:
            for x in (-W / 2, W / 2):
                b.box((x, rng.uniform(-0.03, 0.03), z), (0.28, Dp + 2 * ext, lh * 0.96), const(rng.choice(CW[:4])))
        # infill courses on the other pair (slightly inset)
        if i % 2 == 0:
            for x in (-W / 2, W / 2):
                b.box((x, 0, z), (0.24, Dp - 0.2, lh * 0.96), const(rng.choice(CW[1:5])))
        else:
            for y in (-Dp / 2, Dp / 2):
                b.box((0, y, z), (W - 0.2, 0.24, lh * 0.96), const(rng.choice(CW[1:5])))
    top = base_h + Hw
    # gables (front/back) with vertical boards
    pitch = 2.3
    for y in (-Dp / 2, Dp / 2):
        for k in range(6):
            x0 = -W / 2 + k * W / 6
            x1 = x0 + W / 6
            z0 = top + pitch * (1 - abs(x0) / (W / 2))
            z1 = top + pitch * (1 - abs(x1) / (W / 2))
            vs = [b.v((x0, y, top)), b.v((x1, y, top)), b.v((x1, y, z1)), b.v((x0, y, z0))]
            if abs(x0) < 1e-6 or abs(x1) < 1e-6:
                vs = [b.v((x0, y, top)), b.v((x1, y, top)), b.v((x1, y, z1)), b.v((x0, y, z0))]
            b.face(vs, rng.choice(["cabin_wood_light", "cabin_wood_warm", "cabin_wood_base"]), out=Vector((0, 1 if y > 0 else -1, 0)))
    # roof: stone slab rows on two slopes + ridge beam
    ov = 0.7
    rl = Dp + 2 * ov
    for s in (-1, 1):
        rows = 5
        for r in range(rows):
            t0, t1 = r / rows, (r + 1) / rows
            x0 = s * (W / 2 + ov) * (1 - t0)
            x1 = s * (W / 2 + ov) * (1 - t1)
            z0 = top - pitch * ov / (W / 2) + (pitch + pitch * ov / (W / 2)) * t0
            z1 = top - pitch * ov / (W / 2) + (pitch + pitch * ov / (W / 2)) * t1
            for seg in range(3):
                y0 = -rl / 2 + seg * rl / 3
                y1 = y0 + rl / 3
                lift = 0.18 + rng.uniform(-0.02, 0.03)
                vs = [b.v((x0, y0, z0 + lift)), b.v((x0, y1, z0 + lift)), b.v((x1, y1, z1 + lift)), b.v((x1, y0, z1 + lift))]
                b.face(vs, rng.choice(["granite_dark", "granite_deep", "granite_cool", "granite_base"]),
                       out=Vector((s, 0, 1)))
            # slab edge thickness on the eaves row
            if r == 0:
                vs = [b.v((x0, -rl / 2, z0 + 0.18)), b.v((x0, rl / 2, z0 + 0.18)), b.v((x0, rl / 2, z0 - 0.05)), b.v((x0, -rl / 2, z0 - 0.05))]
                b.face(vs, "granite_deep", out=Vector((s, 0, 0)))
        # underside
        xe = s * (W / 2 + ov)
        ze = top - pitch * ov / (W / 2)
        b.face([b.v((xe, -rl / 2, ze)), b.v((xe, rl / 2, ze)), b.v((0, rl / 2, top + pitch)), b.v((0, -rl / 2, top + pitch))],
               "cabin_wood_deep", out=Vector((-s * 0.3, 0, -1)))
    b.beam((0, -rl / 2 - 0.1, top + pitch + 0.2), (0, rl / 2 + 0.1, top + pitch + 0.2), 0.3, 0.25, const("cabin_wood_dark"))
    # windows with shutters + flower boxes on the long (-Y front) wall
    for x in (-1.8, 1.8):
        y = -Dp / 2 - 0.16
        z = base_h + 1.5
        b.box((x, y, z), (1.0, 0.04, 0.9), const("shadow_blue"))
        b.box((x, y - 0.02, z), (1.1, 0.03, 0.08), const("cabin_wood_light"))
        b.box((x, y - 0.02, z), (0.08, 0.03, 1.0), const("cabin_wood_light"))
        for sx in (-1, 1):
            b.box((x + sx * 0.78, y - 0.03, z), (0.45, 0.05, 1.0), const("sign_red_dark"))
        b.box((x, y - 0.18, z - 0.55), (1.1, 0.3, 0.22), const("cabin_wood_dark"))
        for k in range(5):
            p = Vector((x - 0.45 + k * 0.22, y - 0.18, z - 0.38))
            b.hull([p + Vector((rng.uniform(-0.09, 0.09), rng.uniform(-0.09, 0.09), rng.uniform(0, 0.14))) for _ in range(5)],
                   const(rng.choice(["flower_red", "flower_pink", "flower_white", "leaf_mid_base"])))
    # door
    b.box((0, -Dp / 2 - 0.15, base_h + 1.05), (1.0, 0.06, 2.1), const("cabin_wood_deep"))
    b.box((0, -Dp / 2 - 0.19, base_h + 1.05), (0.8, 0.04, 1.85), pick(["cabin_wood_cool", "cabin_wood_dark"], rng=rng))
    # chimney
    for k in range(4):
        b.box((1.6 + rng.uniform(-0.03, 0.03), 0.9, top + 1.2 + k * 0.55), (0.7, 0.7, 0.56), pick(GRAN[:5], rng=rng))
    b.box((1.6, 0.9, top + 3.4), (0.85, 0.85, 0.12), const("granite_deep"))
    # side balcony on the +X gable
    bz = base_h + 2.4
    for y in (-1.6, 1.6):
        b.beam((W / 2 + 1.3, y, -0.2), (W / 2 + 1.3, y, bz), 0.16, 0.16, const("cabin_wood_dark"))
    b.box((W / 2 + 0.7, 0, bz), (1.4, 3.6, 0.12), pick(CW[:3], rng=rng))
    b.beam((W / 2 + 1.38, -1.8, bz + 0.95), (W / 2 + 1.38, 1.8, bz + 0.95), 0.1, 0.1, const("cabin_wood_base"))
    for k in range(7):
        y = -1.7 + k * 3.4 / 6
        b.box((W / 2 + 1.38, y, bz + 0.5), (0.06, 0.18, 0.9), const(rng.choice(CW[:3])))
    # woodpile against the -X wall
    for r in range(3):
        for k in range(5 - r):
            y = -1.2 + k * 0.42 + r * 0.21
            b.tube([Vector((-W / 2 - 0.55, y, 0.2 + r * 0.36)), Vector((-W / 2 - 0.05, y, 0.2 + r * 0.36))], 0.18, 5,
                   const(rng.choice(["wood_light", "wood_base", "cabin_wood_warm"])), cap_top=True, cap_bot=True)
    return b.finish(name)


# --------------------------------------------------------------------------- alpine flowers
def flowers(name, var):
    b = B(3700 + var)
    rng = b.rng
    heads = [["flower_purple", "flower_blue"], ["flower_white", "flower_pink"], ["flower_yellow", "flower_white", "flower_red"]][var]
    for k in range(7):  # leaf rosette
        a = k * math.tau / 7
        b.blade(Vector((0, 0, 0)), Vector((math.cos(a), math.sin(a), 0.6)), 0.3, 0.05, 2, 0.8,
                ["alpine_grass_dark", "alpine_grass_base", "alpine_grass_light"])
    stems = [5, 7, 8][var]
    for k in range(stems):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(0.02, 0.22)
        p0 = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = rng.uniform(0.25, 0.55) * (1.4 if var == 0 else 1)
        p1 = p0 + Vector((rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06), h))
        b.tube([p0, p1], 0.012, 3, const("alpine_grass_dark"), cap_top=False)
        col = rng.choice(heads)
        if var == 0:  # lupine spikes: stacked buds
            for j in range(3):
                c = p1 - Vector((0, 0, j * 0.07))
                s = 0.05 - j * 0.008 + 0.02
                b.hull([c + Vector((rng.uniform(-s, s), rng.uniform(-s, s), rng.uniform(-0.03, 0.04))) for _ in range(5)], const(col))
        elif var == 1:  # daisies: flat petal disc + centre
            n = 5
            ring = [b.v(p1 + Vector((math.cos(j * math.tau / n) * 0.06, math.sin(j * math.tau / n) * 0.06, rng.uniform(-0.01, 0.01))))
                    for j in range(n)]
            cv = b.v(p1 + Vector((0, 0, 0.015)))
            for j in range(n):
                b.face([ring[j], ring[(j + 1) % n], cv], col if j % 2 else "flower_white", out=E.UP)
            b.hull([p1 + Vector((rng.uniform(-0.02, 0.02), rng.uniform(-0.02, 0.02), 0.02 + rng.uniform(0, 0.02))) for _ in range(4)],
                   const("flower_yellow"))
        else:
            s = 0.045
            b.hull([p1 + Vector((rng.uniform(-s, s), rng.uniform(-s, s), rng.uniform(-0.02, 0.03))) for _ in range(5)], const(col))
    return b.finish(name)


# --------------------------------------------------------------------------- build
def build_kit():
    objs = {}

    def add(o):
        objs[o.name] = o
    for v in range(5):
        add(pine(f"SM_Env_Alpine_Pine_{v + 1:02d}", v))
    add(fallen_pine("SM_Env_Alpine_FallenPine_01"))
    for v in range(4):
        add(cliff(f"SM_Env_Alpine_Cliff_{v + 1:02d}", v))
    for v in range(2):
        add(waterfall(f"SM_Env_Alpine_Waterfall_{v + 1:02d}", v))
        add(stream(f"SM_Env_Alpine_Stream_{v + 1:02d}", v))
    add(tunnel("SM_Env_Alpine_TunnelMouth_01", True))
    add(tunnel("SM_Env_Alpine_TunnelSegment_01", False))
    add(chalet("SM_Env_Alpine_Chalet_01"))
    add(K.build_mountain_card("SM_Env_Alpine_SnowPeaks_01", 0, ramp=["granite_light", "mtn_far_light", "mtn_far_base"],
                              snow=["snow_light", "snow_dark"], width=480, seed=3800, sharp=1.6, height=160))
    add(K.build_mountain_card("SM_Env_Alpine_SnowPeaks_02", 1, ramp=["mtn_far_light", "mtn_far_base", "mtn_far_dark"],
                              snow=["snow_base", "snow_dark"], width=420, seed=3810, sharp=2.2, height=210))
    for v in range(3):
        add(flowers(f"SM_Env_Alpine_Flowers_{v + 1:02d}", v))
    # palette swaps of the common kit so ground pieces match the alpine meadow
    for v in range(3):
        add(K.build_slope(f"SM_Env_Alpine_Slope_{v + 1:02d}", v, pal=K.P_ALPINE))
        add(K.build_grass(f"SM_Env_Alpine_Grass_{v + 1:02d}", v, pal=K.P_ALPINE,
                          flowers=("flower_purple", "flower_white", "flower_yellow")))
    return objs


def compose(objs, common):
    """Mountain-side track: granite wall + pines uphill (left), meadow dropping to the valley (right), tunnel ahead."""
    bpy = E.bpy
    coll = bpy.data.collections.new("Composition")
    bpy.context.scene.collection.children.link(coll)
    ts = E.TrackScene(lambda y: -5.0 * math.sin(y / 45.0), grade=0.07,
                      bank_l=lambda d: 5.0 * E.smoothstep(0.3, 5.0, d) + 0.9 * max(0.0, d - 5.0),
                      bank_r=lambda d: 0.4 * E.smoothstep(0.2, 2.0, d) - 0.38 * max(0.0, d - 3.0), seed=33)
    g = ts.build_ground(["alpine_grass_base", "alpine_grass_cool", "alpine_grass_dark", "alpine_grass_warm"],
                        ["alpine_grass_dark", "alpine_grass_base", "alpine_grass_cool", "alpine_grass_deep", "granite_cool"],
                        x_range=(-70, 90), y_range=(-20, 200), cell=3.0)
    t = ts.build_track(y_range=(-20, 92), asphalt=("asphalt_base", "asphalt_dark", "asphalt_dark", "asphalt_base"),
                       curb=("curb_yellow", "curb_black"))
    for o in (g, t):
        bpy.context.scene.collection.objects.unlink(o)
        coll.objects.link(o)
    get = lambda key: [o for n, o in objs.items() if key in n]  # noqa: E731
    cget = lambda key: [o for n, o in common.items() if key in n]  # noqa: E731
    placed = []
    yr = (-5, 160)
    # cliffs along the uphill side (face the track: front is -Y, rotate so it faces +X)
    for k, y in enumerate(range(2, 70, 9)):
        x = ts.cx(y) - ts.half - 4.0 - (k % 2) * 1.5
        E.instance(objs[f"SM_Env_Alpine_Cliff_0{1 + k % 3}"], (x, y, ts.h(x, y) - 1.5), math.pi / 2 + 0.15 * ((k % 3) - 1), 1.0,
                   coll=coll)
    wf = E.instance(objs["SM_Env_Alpine_Waterfall_01"], (ts.cx(34) - ts.half - 3.2, 34, ts.h(ts.cx(34) - ts.half - 3.2, 34) - 0.4),
                    math.pi / 2, 1.0, coll=coll)
    # tunnel ahead
    yt = 92.0
    E.instance(objs["SM_Env_Alpine_TunnelMouth_01"], (ts.cx(yt), yt, ts.zt(yt)), 0.0, 1.0, coll=coll)
    for s in (-1, 1):
        x = ts.cx(yt) + s * 11.5
        E.instance(objs["SM_Env_Alpine_Cliff_02"], (x, yt + 1.5, ts.zt(yt) - 1.0), 0.0, 1.0, coll=coll)
    E.instance(objs["SM_Env_Alpine_Cliff_04"], (ts.cx(yt) - 2, yt + 6, ts.zt(yt) + 6.5), 0.4, 1.6, coll=coll)
    ts.scatter(get("Pine_"), 70, (8, 40), (-5, 200), (0.8, 1.25), sides=(-1,), min_gap=3.5, placed=placed, coll=coll)
    ts.scatter(get("Pine_"), 45, (3, 60), (-5, 200), (0.7, 1.2), sides=(1,), min_gap=5.0, placed=placed, coll=coll)
    ts.scatter(get("FallenPine"), 2, (4, 12), (10, 60), (1.0, 1.0), sides=(1,), placed=placed, coll=coll)
    ts.scatter(cget("RockLarge") + get("Cliff_04"), 8, (2, 30), yr, (0.6, 1.0), sides=(1,), min_gap=5, placed=placed, coll=coll)
    ts.scatter(cget("RockMedium") + cget("RockSmall"), 40, (0.3, 20), yr, (0.7, 1.3), min_gap=1.2, placed=placed, coll=coll)
    ts.scatter(get("Flowers"), 220, (0.2, 26), yr, (0.8, 1.5), sides=(1,), coll=coll)
    ts.scatter(get("Alpine_Grass"), 200, (0.0, 26), yr, (0.8, 1.6), sides=(1,), coll=coll)
    ts.scatter(get("Alpine_Grass") + get("Flowers"), 40, (0.0, 3), yr, (0.8, 1.4), sides=(-1,), coll=coll)
    # chalet down the meadow + fence
    yc = 30.0
    xc = ts.cx(yc) + ts.half + 13
    E.instance(objs["SM_Env_Alpine_Chalet_01"], (xc, yc, ts.h(xc, yc) - 0.3), 2.0, 1.0, coll=coll)
    for k in range(4):
        y = 22 + k * 4.2
        x = ts.cx(y) + ts.half + 9
        E.instance(common["SM_Env_Common_Fence_0" + str(1 + k % 2)], (x, y, ts.h(x, y) - 0.05), math.pi / 2 + 0.05, 1.0, coll=coll)
    # stream crossing the meadow + background
    for k in range(3):
        y = 60 + k * 9.5
        x = ts.cx(y) + ts.half + 26 + k * 2
        E.instance(objs["SM_Env_Alpine_Stream_01"], (x, y, ts.h(x, y) + 0.1), 0.25, 1.0, coll=coll)
    E.instance(objs["SM_Env_Alpine_SnowPeaks_02"], (160, 1000, -150), 0.1, 1.7, coll=coll)
    E.instance(objs["SM_Env_Alpine_SnowPeaks_01"], (-120, 760, -130), -0.1, 1.5, coll=coll)
    E.instance(common["SM_Env_Common_MountainCard_02"], (40, 380, -60), 0.0, 1.4, coll=coll)
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
    E.write_stats("alpine", objs.values())
    bpy = E.bpy
    E.setup_render((1600, 900), samples=32)
    E.world("#9CC6EC", "#E8F0F5", strength=1.0)
    E.sun(45, 145, "#FFF3DC", 3.4, 3)
    g = E.ground_plane(160, ["tint_light", "tint_light", "tint_mid"], z=-0.02)
    groups = {
        "trees_rocks": [["Pine_", "FallenPine"], ["Cliff_"]],
        "water_props": [["Waterfall", "Stream"], ["Tunnel", "Chalet"], ["Flowers", "Alpine_Grass", "Alpine_Slope"]],
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
        E.grid_layout(rows, gap=2.0, row_gap=3.0)
        shown = [o for r in rows for o in r]
        g.location = (0, 20, 0)
        cam = E.fit_camera(shown, elev=26, azim=-24)
        E.render(os.path.join(E.RENDER_DIR, f"env_alpine_grid_{gname}.png"))
        bpy.data.objects.remove(cam)
    for o in objs.values():
        o.hide_render = True
        o.location = (0, -800, -300)
    g.hide_render = True
    bpy.data.objects.remove(bpy.data.objects["Sun"])
    # ---- composition: crisp morning
    E.setup_render((1280, 720), samples=64, exposure=0.0)
    E.world("#4F95D6", "#CFE3F2", strength=1.2, fog=(0.0007, "#CFE0EE", 0.2, ((0, 300, 60), (900, 1300, 300))))
    E.sun(24, 115, "#FFE6C2", 4.6, 2)
    ts = compose(objs, common)
    cy = -8.0
    E.camera((ts.cx(cy) + 0.6, cy, ts.zt(cy) + 2.4), (ts.cx(40) + 5.0, 40, ts.zt(40) + 1.0), lens=24)
    E.render(os.path.join(E.RENDER_DIR, "env_alpine_composition.png"))
    E.save_blend("Env_Alpine.blend")


if __name__ == "__main__":
    main()
