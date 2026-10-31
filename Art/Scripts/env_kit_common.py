"""Luge Rush - Common environment kit (slopes, rocks, grass, gravel, fence, logs, far-mountain cards).

Run:  blender -b -P Art/Scripts/env_kit_common.py
Outputs FBX to Game/Assets/_LugeRush/Art/Models/Environment/Common/, renders to Art/Renders/env_common_*.png,
source to Art/Blender/Env_Common.blend. Builder functions are re-used by the biome kits (palette parameter).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import env_common as E  # noqa: E402
from env_common import B, R, Vector, shade, pick  # noqa: E402

# --------------------------------------------------------------------------- palettes
GRANITE = ["granite_light", "granite_warm", "granite_base", "granite_cool", "granite_dark", "granite_deep"]
P_COMMON = {
    "soil": ["soil_light", "soil_base", "soil_dark", "soil_deep"],
    "grass": ["leaf_mid_base", "leaf_bright_dark", "leaf_mid_dark", "leaf_dark_light", "leaf_dark_base", "moss"],
    "rock": GRANITE,
    "moss": ["moss", "fern", "leaf_dark_light", "leaf_mid_dark"],
    "blades": ["leaf_mid_dark", "leaf_mid_base", "leaf_bright_base", "leaf_bright_light", "leaf_lime_base"],
}
P_ALPINE = {
    "soil": ["soil_light", "soil_base", "soil_dark", "soil_deep"],
    "grass": ["alpine_grass_light", "alpine_grass_warm", "alpine_grass_base", "alpine_grass_cool", "alpine_grass_dark"],
    "rock": GRANITE,
    "moss": ["alpine_grass_dark", "alpine_grass_deep", "moss"],
    "blades": ["alpine_grass_deep", "alpine_grass_dark", "alpine_grass_base", "alpine_grass_light", "alpine_grass_warm"],
}
P_CANYON = {
    "soil": ["sand_base", "sand_dark", "canyon_orange_base", "sand_deep"],
    "grass": ["sand_base", "sand_light", "sand_dark", "canyon_orange_light", "sand_base"],
    "rock": ["canyon_rock_light", "canyon_orange_base", "canyon_rock_warm", "canyon_rock_base", "canyon_rock_cool",
             "canyon_rock_dark", "rock_deep", "canyon_rock_deep"],
    "moss": ["dry_bush_base", "dry_bush_dark", "sand_deep"],
    "blades": ["dry_bush_deep", "dry_bush_dark", "dry_bush_base", "dry_bush_light", "sand_light"],
}


def light_shade(ramp, ldir=(0.45, -0.55, 0.7), spread=0.5, rng=None):
    """Colour by facing a key light direction (good for big silhouettes / cliffs)."""
    ld = Vector(ldir).normalized()
    rng = rng or E.random

    def cf(f):
        d = f.normal.dot(ld) * 0.5 + 0.5
        t = (1 - d) * (len(ramp) - 1) + rng.gauss(0, spread)
        return ramp[max(0, min(len(ramp) - 1, int(round(t))))]
    return cf


# --------------------------------------------------------------------------- rocks
ROCK_SIZES = {
    "S": dict(size=(0.35, 0.28, 0.3), n=16, cuts=2),
    "M": dict(size=(0.9, 0.7, 0.75), n=34, cuts=3),
    "L": dict(size=(2.0, 1.6, 1.7), n=60, cuts=4),
}


def build_rock(name, cat, var, pal=P_COMMON, moss=0.0, seed_base=100):
    b = B(seed_base + var * 17 + ord(cat))
    rng = b.rng
    spec = ROCK_SIZES[cat]
    sx, sy, sz = spec["size"]
    k = [(1.0, 1.0, 1.0), (1.35, 0.8, 0.6), (0.85, 0.9, 1.35), (1.2, 1.15, 0.8)][var % 4]
    size = (sx * k[0] * rng.uniform(0.9, 1.1), sy * k[1] * rng.uniform(0.9, 1.1), sz * k[2])
    rock_ramp = pal["rock"]

    def cf(f):
        if moss and f.normal.z > 0.55 and rng.random() < moss + 0.25:
            return rng.choice(pal["moss"])
        if f.calc_center_median().z < size[2] * 0.12 and rng.random() < 0.35:
            return rng.choice(rock_ramp[-2:])
        return shade(rock_ramp, spread=0.7, rng=rng)(f)

    pts = b.rock_points(size, spec["n"], cuts=spec["cuts"] + (1 if var == 1 else 0),
                        squash=[0.75, 0.55, 0.95, 0.7][var % 4])
    b.hull(pts, cf, merge_deg=1.5)
    if cat == "L" and var == 3:  # split boulder: second leaning chunk
        pts2 = b.rock_points((size[0] * 0.55, size[1] * 0.6, size[2] * 0.7), 18, cuts=3)
        off = Vector((size[0] * 0.95, size[1] * 0.2, 0))
        b.hull([p + off for p in pts2], cf, merge_deg=1.5)
    if cat in "ML" and var in (0, 2):  # small satellite stones
        for s in range(2 if cat == "L" else 1):
            a = rng.uniform(0, math.tau)
            off = Vector((math.cos(a) * size[0] * 1.05, math.sin(a) * size[1] * 1.05, 0))
            pts3 = b.rock_points((size[0] * 0.22, size[1] * 0.2, size[2] * 0.2), 8, cuts=1)
            b.hull([p + off for p in pts3], cf, merge_deg=1.5)
    return b.finish(name)


# --------------------------------------------------------------------------- slope / bank
def build_slope(name, var, pal=P_COMMON, length=8.0):
    """Track-side earth bank. Toe at x=0 (track edge), rises toward +X. Length along Y, centred."""
    b = B(300 + var * 31)
    rng = b.rng
    H = [2.4, 3.6, 1.6][var % 3]
    W = [4.5, 4.0, 3.8][var % 3]
    nu, nv = 6, 8
    steep = [1.4, 2.2, 1.0][var % 3]

    def prof(t):
        # smooth S-curve, steepness per variant
        s = t ** steep / (t ** steep + (1 - t) ** steep) if 0 < t < 1 else t
        return s

    hmod = [rng.uniform(0.85, 1.15) for _ in range(nv + 1)]

    def fn(i, j):
        t = i / nu
        y = -length / 2 + j * length / nv
        x = t * W
        z = prof(t) * H * (hmod[j] if 0 < j < nv else 1.0)
        if 0 < i < nu and 0 < j < nv:
            x += rng.uniform(-0.25, 0.25)
            z += rng.uniform(-0.18, 0.18) * H / 2.4
            y += rng.uniform(-0.25, 0.25)
        if i == nu:  # flat top shelf
            x = W + 1.5
            z = H * (hmod[j] if 0 < j < nv else 1.0)
        return Vector((x, y, z - (0.15 if i == 0 else 0)))

    def cf(f):
        nz = f.normal.z
        c = f.calc_center_median()
        if (nz > 0.9 and rng.random() < 0.8) or c.z > H * 0.85:
            return rng.choice(pal["grass"])
        if var == 1 and 0.25 < nz < 0.7 and rng.random() < 0.45:
            return rng.choice(pal["rock"][1:5])
        if rng.random() < 0.12:
            return rng.choice(pal["moss"])
        return shade(pal["soil"], spread=0.6, rng=rng)(f)

    faces, grid = b.ribbon_grid(fn, nu, nv, cf)
    # grass lip overhanging the top edge
    lip_i = nu - 1
    for j in range(nv):
        if rng.random() < 0.35:
            continue
        a, c = grid[lip_i][j].co, grid[lip_i][j + 1].co
        ov = rng.uniform(0.2, 0.5)
        o1 = a + Vector((-ov, 0, 0.05))
        o2 = c + Vector((-ov * rng.uniform(0.6, 1.2), 0, 0.05))
        va, vc = b.v(a + Vector((0.02, 0, 0.12))), b.v(c + Vector((0.02, 0, 0.12)))
        vo1, vo2 = b.v(o1 + Vector((rng.uniform(-0.1, 0.1), 0, -0.12))), b.v(o2 + Vector((rng.uniform(-0.1, 0.1), 0, -0.12)))
        b.face([va, vc, vo2, vo1], rng.choice(pal["grass"][:3]), out=Vector((-0.3, 0, 1)))
        b.face([b.v(vo1.co), b.v(vo2.co), b.v(vo2.co + Vector((0.15, 0, -0.18))), b.v(vo1.co + Vector((0.15, 0, -0.18)))],
               pal["grass"][-1], out=Vector((-1, 0, -0.2)))
    # stones half-buried at the toe and on the face
    for k in range(3 + var):
        y = rng.uniform(-length / 2 + 0.6, length / 2 - 0.6)
        t = rng.uniform(0.0, 0.5)
        base = Vector((t * W, y, prof(t) * H - 0.1))
        s = rng.uniform(0.18, 0.45) * (1.6 if var == 1 else 1)
        pts = b.rock_points((s, s * 0.8, s * 0.7), 9, cuts=1)
        b.hull([p + base for p in pts], shade(pal["rock"], rng=rng), merge_deg=1.5)
    return b.finish(name)


# --------------------------------------------------------------------------- grass tufts
def build_grass(name, var, pal=P_COMMON, flowers=("flower_yellow", "flower_white")):
    b = B(500 + var * 13)
    rng = b.rng
    n = [22, 14, 14, 10][var % 4]
    length = [0.45, 0.85, 0.55, 0.6][var % 4]
    width = [0.035, 0.03, 0.04, 0.09][var % 4]
    bl = pal["blades"]
    for k in range(n):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(0, 0.18 + (0.1 if var == 3 else 0))
        base = Vector((math.cos(a) * r, math.sin(a) * r, -0.02))
        tilt = rng.uniform(0.15, 0.6)
        d = Vector((math.cos(a) * tilt, math.sin(a) * tilt, 1))
        L = length * rng.uniform(0.6, 1.2)
        cols = [bl[0], rng.choice(bl[1:3]), rng.choice(bl[2:])]
        b.blade(base, d, L, width * rng.uniform(0.8, 1.2), 2, rng.uniform(0.4, 1.2), cols)
    if var == 2:  # seed heads / tiny flowers
        for k in range(5):
            a = rng.uniform(0, math.tau)
            p = Vector((math.cos(a) * 0.15, math.sin(a) * 0.15, length * rng.uniform(0.8, 1.1)))
            b.tube([p - Vector((0, 0, length * 0.8)), p], [0.008, 0.008], 3, E.const(bl[1]), cap_top=False)
            pts = [p + Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), rng.uniform(-0.04, 0.05))) for _ in range(5)]
            b.hull(pts, E.const(rng.choice(flowers)))
    return b.finish(name)


# --------------------------------------------------------------------------- gravel
def build_gravel(name, var, pal=P_COMMON):
    b = B(700 + var)
    rng = b.rng
    R0 = 1.3
    n = 10
    ring = [b.v((math.cos(k * math.tau / n) * R0 * rng.uniform(0.75, 1.1),
                 math.sin(k * math.tau / n) * R0 * rng.uniform(0.75, 1.1), 0.01)) for k in range(n)]
    if var == 0:
        b.face(ring, pal["soil"][2], out=E.UP)
        count, hmax = 26, 0.0
    else:  # pile
        apex = b.v((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), 0.55))
        for k in range(n):
            b.face([ring[k], ring[(k + 1) % n], apex], rng.choice(pal["rock"][2:5]), out=E.UP)
        count, hmax = 24, 0.55
    for k in range(count):
        a = rng.uniform(0, math.tau)
        r = math.sqrt(rng.random()) * R0 * 0.9
        z = hmax * (1 - r / R0) if hmax else 0.0
        s = rng.uniform(0.05, 0.14)
        pts = b.rock_points((s, s * 0.85, s * 0.7), 6, cuts=0, noise=0.3)
        off = Vector((math.cos(a) * r, math.sin(a) * r, z - 0.01))
        b.hull([p + off for p in pts], pick(pal["rock"], rng=rng))
    return b.finish(name)


# --------------------------------------------------------------------------- fence
WOOD = ["wood_light", "wood_base", "wood_dark", "wood_deep"]


def build_fence(name, var, wood=WOOD):
    b = B(800 + var)
    rng = b.rng
    cf = shade(wood, spread=0.7, rng=rng)
    xs = [-2.0, 0.0, 2.0]
    tops = []
    for i, x in enumerate(xs):
        lean = (rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06))
        if var == 1 and i == 2:
            lean = (0.25, -0.12)
        h = 1.15 + rng.uniform(-0.08, 0.08)
        p0 = Vector((x, 0, -0.2))
        p1 = Vector((x + lean[0] * h, lean[1] * h, h))
        b.beam(p0, p1, 0.13, 0.13, cf, jit=0.01)
        # pointed cap
        apex = b.v(p1 + Vector((0, 0, 0.12)))
        q = [b.v(p1 + Vector((dx, dy, 0))) for dx, dy in ((-0.07, -0.07), (0.07, -0.07), (0.07, 0.07), (-0.07, 0.07))]
        for k in range(4):
            b.face([q[k], q[(k + 1) % 4], apex], wood[0] if k % 2 else wood[1], out=(q[k].co + q[(k + 1) % 4].co) / 2 - p1)
        tops.append(p1)
    for hz in (0.45, 0.92):
        for i in range(2):
            a = Vector((xs[i] - (0.15 if i == 0 else 0), 0.09, hz + rng.uniform(-0.04, 0.04)))
            c = Vector((xs[i + 1] + (0.15 if i == 1 else 0), 0.09, hz + rng.uniform(-0.04, 0.04)))
            if var == 1 and i == 1 and hz > 0.5:  # broken rail hanging down
                c = Vector((xs[i + 1] - 0.3, 0.12, 0.12))
            b.beam(a, c, 0.06, 0.12, cf, jit=0.008)
    return b.finish(name)


# --------------------------------------------------------------------------- stumps / logs
BARK = ["trunk_light", "trunk_base", "trunk_dark", "trunk_deep"]


def _rings_cap(b, ring, centre, normal, outer, inner):
    """Cut face with tree-ring detail: outer band + inner disc."""
    inn = [b.v(centre + (v.co - centre) * 0.6 - normal * 0.02) for v in ring]
    n = len(ring)
    for k in range(n):
        b.face([ring[k], ring[(k + 1) % n], inn[(k + 1) % n], inn[k]], outer, out=normal)
    b.face(inn, inner, out=normal)


def build_stump(name, var, bark=BARK, moss=("moss", "fern")):
    b = B(900 + var)
    rng = b.rng
    r = [0.42, 0.3][var % 2]
    h = [0.55, 0.85][var % 2]
    sides = 8

    def cf(f):
        if f.normal.z > 0.2 and rng.random() < 0.5:
            return rng.choice(moss)
        return pick(bark, weights=[2, 3, 3, 1], rng=rng)(f)
    pts = [(0, 0, -0.1), (rng.uniform(-0.03, 0.03), 0, h * 0.5), (rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), h)]
    faces, caps, rings = b.tube(pts, [r * 1.2, r, r * 0.95], sides, cf, cap_top=False, jit=0.1)
    top = rings[-1]
    # jagged break on variant 1
    if var == 1:
        for k, v in enumerate(top):
            v.co.z += rng.uniform(0, 0.25) if k % 2 else 0
    centre = sum((v.co for v in top), Vector()) / len(top)
    _rings_cap(b, top, centre, Vector((0, 0, 1)), "wood_light", "wood_base")
    for k in range(4):
        a = k * math.tau / 4 + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        b.tube([d * r * 0.6 + Vector((0, 0, 0.25)), d * (r + 0.35) + Vector((0, 0, 0.0)), d * (r + 0.6) + Vector((0, 0, -0.12))],
               [0.14, 0.09, 0.03], 5, pick(bark[1:], rng=rng), cap_top=False)
    return b.finish(name)


def build_log(name, var=0, bark=BARK, moss=("moss", "fern", "leaf_dark_light")):
    b = B(950 + var)
    rng = b.rng
    L = 3.6
    r = 0.3

    def cf(f):
        if f.normal.z > 0.45 and rng.random() < 0.55:
            return rng.choice(moss)
        return pick(bark, weights=[2, 3, 3, 1], rng=rng)(f)
    pts = [Vector((-L / 2 + i * L / 4, rng.uniform(-0.06, 0.06), r * 0.85 + rng.uniform(-0.03, 0.03))) for i in range(5)]
    radii = [r * 1.05, r, r * 0.97, r * 0.92, r * 0.88]
    faces, caps, rings = b.tube(pts, radii, 8, cf, cap_top=False, jit=0.07)
    for ring, p, n in ((rings[0], pts[0], Vector((-1, 0, 0))), (rings[-1], pts[-1], Vector((1, 0, 0)))):
        _rings_cap(b, ring, p, n, "wood_light", "wood_base")
    for k in range(2):  # broken branch stubs
        p = pts[1 + k * 2] + Vector((0, 0, r * 0.7))
        d = Vector((rng.uniform(-0.3, 0.3), rng.choice((-0.6, 0.6)), 0.8)).normalized()
        b.tube([p, p + d * 0.5], [0.08, 0.05], 5, pick(bark, rng=rng), cap_top=True)
    return b.finish(name)


# --------------------------------------------------------------------------- far mountain cards
def build_mountain_card(name, var, ramp=None, snow=None, width=320.0, seed=1000, forest=False, sharp=1.0, height=70.0):
    """Background silhouette strip facing -Y (toward the track). Pivot bottom centre."""
    b = B(seed + var)
    rng = b.rng
    n = 32
    ramp = ramp or ["mtn_far_light", "mtn_far_base", "mtn_far_dark"]
    # ridge heights: a few peaks + noise
    peaks = [(rng.uniform(0.05, 0.95), rng.uniform(0.45, 1.0), rng.uniform(0.06, 0.16) / sharp) for _ in range(5)]
    hs = []
    for i in range(n + 1):
        t = i / n
        h = 0.18 + sum(pk[1] * math.exp(-((t - pk[0]) / pk[2]) ** 2) for pk in peaks)
        h += rng.uniform(-0.05, 0.05) * (2 if forest else 1)
        hs.append(h)
    hmax = max(hs)
    hs = [max(0.12, h / hmax) * height for h in hs]
    ridge, mid, base = [], [], []
    for i in range(n + 1):
        x = -width / 2 + i * width / n + (rng.uniform(-2, 2) if 0 < i < n else 0)
        ridge.append(b.v((x, rng.uniform(-3, 3), hs[i])))
        mid.append(b.v((x + rng.uniform(-3, 3), -hs[i] * 0.12, hs[i] * rng.uniform(0.45, 0.62))))
        base.append(b.v((x, -hs[i] * 0.22, -8.0)))
    ld = Vector((0.55, -0.6, 0.45)).normalized()

    def col(f, upper):
        c = f.calc_center_median()
        if snow and c.z > height * 0.62 and upper:
            return snow[0] if f.normal.dot(ld) > 0.1 else snow[1]
        d = f.normal.dot(ld)
        if upper:
            return ramp[0] if d > 0.35 else (ramp[1] if d > -0.1 else ramp[2])
        return ramp[1] if d > 0.3 else ramp[2]

    for i in range(n):
        # upper band: two tris with alternating diagonal
        if i % 2 == 0:
            tris_u = [[ridge[i], mid[i], mid[i + 1]], [ridge[i], mid[i + 1], ridge[i + 1]]]
        else:
            tris_u = [[ridge[i], mid[i], ridge[i + 1]], [ridge[i + 1], mid[i], mid[i + 1]]]
        for t in tris_u:
            f = b.face(t, ramp[0], out=Vector((0, -1, 0.2)))
            f[b.L] = E.IDX[col(f, True)]
        for t in ([mid[i], base[i], base[i + 1]], [mid[i], base[i + 1], mid[i + 1]]):
            f = b.face(t, ramp[1], out=Vector((0, -1, 0.1)))
            f[b.L] = E.IDX[col(f, False)]
    return b.finish(name)


# --------------------------------------------------------------------------- cliff / wall modules (shared by Alpine + Canyon)
def cliff_wall(b, W, H, D, nx, nz, face_cf, top_cf, seed_shift=0, overhang=0.0, depth_noise=0.35, ledges=0.0,
               lean=0.0, band_jit=0.0, rows_z=None, xjit=0.3):
    """Rock wall facing -Y. Front grid nx*nz with depth noise, optional strata ledges and overhang.
    Pivot: bottom centre of the front face. Back face at y=+D (closed), top capped, sides closed."""
    rng = b.rng
    if rows_z:
        zrows = [0.0] + [z for z in rows_z if 0.5 < z < H - 0.5] + [H]
        nz = len(zrows) - 1
    else:
        zrows = [0.0]
        for k in range(1, nz):
            zrows.append(H * k / nz + rng.uniform(-band_jit, band_jit) * H / nz)
        zrows.append(H)
    band_off = [rng.uniform(-ledges, ledges) for _ in range(nz + 1)]
    grid = []
    for i in range(nx + 1):
        col = []
        for k in range(nz + 1):
            t = k / nz
            x = -W / 2 + i * W / nx
            z = zrows[k]
            y = -rng.uniform(0, D * depth_noise) - overhang * (t ** 2) + band_off[k] - lean * z
            if 0 < i < nx:
                x += rng.uniform(-xjit, xjit) * W / nx
            if 0 < k < nz:
                z += rng.uniform(-0.2, 0.2) * (H / nz if not rows_z else 0.4)
            else:
                z += rng.uniform(-0.15, 0.15) * (H / nz if k == nz else 0)
            col.append(b.v((x, y, z - (0.3 if k == 0 else 0))))
        grid.append(col)
    front = []
    for i in range(nx):
        for k in range(nz):
            f = b.bm.faces.new([grid[i][k], grid[i + 1][k], grid[i + 1][k + 1], grid[i][k + 1]])
            f.normal_update()
            if f.normal.y > 0.2 and abs(f.normal.z) < 0.5:
                pass
            front.append(f)
    # make sure front faces point to -Y
    for f in front:
        if f.normal.y > 0 and f.normal.z < 0.3:
            f.normal_flip()
    # triangulate front quads so strongly bent quads still shade per facet
    import bmesh as _bm
    res = _bm.ops.triangulate(b.bm, faces=front, quad_method="BEAUTY")
    front = [f for f in res["faces"]]
    b.paint(front, face_cf)
    # top cap
    top_back = [b.v((x, D + rng.uniform(-0.3, 0.3), H + rng.uniform(-0.2, 0.4))) for x in (W / 2, -W / 2)]
    top_ring = [grid[i][nz] for i in range(nx + 1)]
    tf = b.face(top_ring + top_back, NAMES0, out=Vector((0, 0, 1)))
    b.paint(_bm.ops.triangulate(b.bm, faces=[tf])["faces"], top_cf)
    # back
    bb = [b.v((-W / 2, D, -0.3)), b.v((W / 2, D, -0.3))]
    b.face([bb[0], bb[1], top_back[0], top_back[1]], "granite_deep", out=Vector((0, 1, 0)))
    # sides
    left = [grid[0][k] for k in range(nz + 1)] + [top_back[1], bb[0]]
    right = [grid[nx][k] for k in range(nz + 1)] + [top_back[0], bb[1]]
    for ring, sgn in ((left, -1), (right, 1)):
        f = b.face(ring, NAMES0, out=Vector((sgn, 0, 0)))
        b.paint(_bm.ops.triangulate(b.bm, faces=[f])["faces"], face_cf)
    return grid


NAMES0 = "granite_base"


# --------------------------------------------------------------------------- kit assembly
def build_kit():
    objs = {}
    for v in range(3):
        objs[f"SM_Env_Common_Slope_{v + 1:02d}"] = build_slope(f"SM_Env_Common_Slope_{v + 1:02d}", v)
    for cat, label in (("S", "RockSmall"), ("M", "RockMedium"), ("L", "RockLarge")):
        for v in range(4):
            nm = f"SM_Env_Common_{label}_{v + 1:02d}"
            objs[nm] = build_rock(nm, cat, v)
    for v in range(4):
        nm = f"SM_Env_Common_Grass_{v + 1:02d}"
        objs[nm] = build_grass(nm, v)
    for v in range(2):
        nm = f"SM_Env_Common_Gravel_{v + 1:02d}"
        objs[nm] = build_gravel(nm, v)
        nm = f"SM_Env_Common_Fence_{v + 1:02d}"
        objs[nm] = build_fence(nm, v)
        nm = f"SM_Env_Common_Stump_{v + 1:02d}"
        objs[nm] = build_stump(nm, v)
    objs["SM_Env_Common_Log_01"] = build_log("SM_Env_Common_Log_01")
    objs["SM_Env_Common_MountainCard_01"] = build_mountain_card("SM_Env_Common_MountainCard_01", 0, height=75)
    objs["SM_Env_Common_MountainCard_02"] = build_mountain_card(
        "SM_Env_Common_MountainCard_02", 1, ramp=["mtn_forest_haze", "mtn_forest_far", "leaf_dark_deep"], forest=True,
        sharp=0.6, height=45)
    objs["SM_Env_Common_MountainCard_03"] = build_mountain_card(
        "SM_Env_Common_MountainCard_03", 2, ramp=["mist_blue", "mtn_far_light", "mtn_far_base"], sharp=1.8, height=110)
    return objs


def main():
    E.reset()
    objs = build_kit()
    for o in objs.values():
        E.export_fbx(o, "Common")
    E.write_stats("common", objs.values())
    # ---- grid renders
    E.setup_render((1600, 900), samples=32)
    E.world("#9FC4E0", "#E4ECEF", strength=1.0)
    E.sun(48, 140, "#FFF4E2", 3.2, 3)
    props = [o for n, o in objs.items() if "MountainCard" not in n]
    rows = [[objs[n] for n in objs if "Slope" in n] + [objs[n] for n in objs if "RockLarge" in n],
            [objs[n] for n in objs if "RockMedium" in n] + [objs[n] for n in objs if "Stump" in n or "Log" in n],
            [objs[n] for n in objs if "RockSmall" in n or "Grass" in n or "Gravel" in n or "Fence" in n]]
    E.grid_layout(rows, gap=1.2, row_gap=1.6)
    g = E.ground_plane(80, ["tint_light", "tint_light", "tint_mid"], z=-0.02)
    g.location = (0, 6, 0)
    for o in objs.values():
        if "MountainCard" in o.name:
            o.hide_render = True
    cam = E.fit_camera(props, elev=30, azim=-25)
    E.render(os.path.join(E.RENDER_DIR, "env_common_grid.png"))
    # mountain cards alone
    for o in props:
        o.hide_render = True
    g.hide_render = True
    cards = [o for o in objs.values() if "MountainCard" in o.name]
    for i, o in enumerate(cards):
        o.hide_render = False
        o.location = (0, i * 60, 0)
    bpy = E.bpy
    bpy.data.objects.remove(cam)
    E.camera((0, -420, 60), (0, 0, 55), lens=40)
    E.render(os.path.join(E.RENDER_DIR, "env_common_mountains.png"))
    E.save_blend("Env_Common.blend")


if __name__ == "__main__":
    main()
