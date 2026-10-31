"""Luge Rush - Canyon kit "Kızıl Kanyon" (layered mesas, strata walls, arch, cacti, mine props).

Run:  blender -b -P Art/Scripts/env_canyon.py
FBX -> Environment/Canyon/, renders -> Art/Renders/env_canyon_*.png, source -> Art/Blender/Env_Canyon.blend
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import env_common as E  # noqa: E402
import env_kit_common as K  # noqa: E402
from env_common import B, Vector, shade, pick, const  # noqa: E402

BIOME = "Canyon"
FAM = {
    "rock": ["canyon_rock_light", "canyon_rock_base", "canyon_rock_dark", "canyon_rock_deep"],
    "orange": ["canyon_orange_light", "canyon_orange_base", "canyon_orange_dark", "canyon_orange_deep"],
    "sand": ["sand_light", "sand_base", "sand_dark", "sand_deep"],
    "purple": ["canyon_rock_cool", "rock_purple", "rock_deep", "canyon_rock_deep"],
    "warm": ["canyon_rock_warm", "canyon_orange_dark", "rock_deep", "canyon_rock_deep"],
    "cream": ["sand_light", "canyon_orange_light", "sand_base", "sand_dark"],
}
STRATA_SETS = [
    ["purple", "rock", "warm", "orange", "rock", "cream", "orange", "rock", "warm", "orange", "rock"],
    ["rock", "orange", "cream", "rock", "warm", "orange", "purple", "rock", "orange", "cream"],
    ["warm", "rock", "orange", "rock", "purple", "orange", "cream", "rock", "warm", "rock"],
]
LD = Vector((0.5, -0.6, 0.6)).normalized()


def strata(rng, H, set_idx=0, bmin=1.0, bmax=3.2):
    seq = STRATA_SETS[set_idx % len(STRATA_SETS)]
    bands, z, i = [], -2.0, 0
    while z < H + 6:
        h = rng.uniform(bmin, bmax)
        bands.append((z, z + h, seq[i % len(seq)]))
        z += h
        i += 1
    return bands


def band_cf(rng, bands, ledge="sand", top_z=None, top_cols=("sand_base", "sand_light", "dry_bush_light", "sand_dark")):
    def cf(f):
        c = f.calc_center_median()
        if top_z is not None and c.z > top_z and f.normal.z > 0.5:
            return rng.choice(top_cols)
        if f.normal.z > 0.6:
            return rng.choice(FAM[ledge][:3])
        fam = bands[-1][2]
        for z0, z1, fm in bands:
            if z0 <= c.z < z1:
                fam = fm
                break
        lit = f.normal.dot(LD)
        idx = 0 if lit > 0.45 else (1 if lit > 0.05 else (2 if lit > -0.35 else 3))
        if rng.random() < 0.12:
            idx += rng.choice((-1, 1))
        return FAM[fam][max(0, min(3, idx))]
    return cf


# --------------------------------------------------------------------------- mesas / towers
def stack(b, base, H, r_of_t, n, bands, ell=(1.0, 1.0), jit=0.1, step=0.12, cap=True, cf=None, top_cols=None):
    """Layered prism: one ring per strata boundary, with a horizontal ledge step between bands."""
    rng = b.rng
    base = Vector(base)
    rings_z = []
    for z0, z1, fam in bands:
        if z1 <= 0:
            continue
        if z0 >= H:
            break
        rings_z.append(max(0.0, z0))
    rings_z.append(H)
    a0 = rng.uniform(0, math.tau)
    angs = [a0 + k * math.tau / n + rng.uniform(-0.18, 0.18) for k in range(n)]
    tilt_phi = rng.uniform(0, math.tau)
    tilt = rng.uniform(0.2, 0.5) * r_of_t(0.0) / 4.0
    basejit = [rng.uniform(1 - jit, 1 + jit) for _ in range(n)]
    pts_rings = []
    for i, z in enumerate(rings_z):
        t = z / H
        r = r_of_t(t)
        inner = 0 < i < len(rings_z) - 1
        s_in = 1 + rng.uniform(-step, step) if (inner and rng.random() < 0.55) else None
        lower, upper = [], []
        for k in range(n):
            rr = r * basejit[k] * rng.uniform(1 - jit * 0.5, 1 + jit * 0.5)
            zz = z + (tilt * math.cos(angs[k] - tilt_phi) if inner else 0.0)
            p = base + Vector((math.cos(angs[k]) * rr * ell[0], math.sin(angs[k]) * rr * ell[1], zz - (0.3 if i == 0 else 0)))
            lower.append(p)
            if s_in is not None:
                upper.append(base + (p - base) * Vector((s_in, s_in, 1)))
        pts_rings.append((lower, upper if s_in is not None else None))
    # faces: band walls between upper ring of i and lower ring of i+1 ; ledge between lower/upper of same ring
    vrings = []
    for lower, upper in pts_rings:
        vl = [b.v(p) for p in lower]
        vu = [b.v(p + Vector((0, 0, 0.002))) for p in upper] if upper else vl
        vrings.append((vl, vu))
    faces = []
    for i in range(len(vrings) - 1):
        lo = vrings[i][1]
        hi = vrings[i + 1][0]
        for k in range(n):
            k2 = (k + 1) % n
            faces.append(b.face([lo[k], lo[k2], hi[k2], hi[k]], "canyon_rock_base",
                                out=(lo[k].co + lo[k2].co) * 0.5 - (base + Vector((0, 0, lo[k].co.z)))))
        if i + 1 < len(vrings) - 1 and vrings[i + 1][0] is not vrings[i + 1][1]:
            a, c = vrings[i + 1]
            for k in range(n):
                k2 = (k + 1) % n
                f = b.face([a[k], a[k2], c[k2], c[k]], "sand_base", out=Vector((0, 0, 1)))
                faces.append(f)
    if cap:
        top = vrings[-1][0]
        f = b.face(top, "sand_base", out=Vector((0, 0, 1)))
        import bmesh as _bm
        tri = _bm.ops.triangulate(b.bm, faces=[f])["faces"]
        tc = top_cols or ["sand_base", "sand_light", "dry_bush_light", "canyon_orange_light"]
        for g in tri:
            g[b.L] = E.IDX[rng.choice(tc)]
    b.paint([f for f in faces if f.is_valid], cf)
    return vrings


def talus(b, centre, R, count, size, cf, ell=(1, 1)):
    rng = b.rng
    for k in range(count):
        a = rng.uniform(0, math.tau)
        rr = R * rng.uniform(0.95, 1.25)
        c = Vector(centre) + Vector((math.cos(a) * rr * ell[0], math.sin(a) * rr * ell[1], 0))
        s = size * rng.uniform(0.5, 1.2)
        pts = b.rock_points((s, s * 0.8, s * 0.7), 10, cuts=2)
        b.hull([p + c for p in pts], cf, merge_deg=1.5)


def mesa(name, var):
    b = B(4000 + var * 19)
    rng = b.rng
    if var == 0:   # hoodoo spire with cap rock
        H = 22.0
        bands = strata(rng, H, 0, 1.0, 2.6)
        cf = band_cf(rng, bands)
        stack(b, (0, 0, 0), H, lambda t: 4.2 * (1 - 0.62 * t) + 0.6 * math.sin(t * 9), 9, bands, jit=0.2, cf=cf)
        capb = [(H - 1, H + 3, "purple")]
        stack(b, (0.3, 0.2, H - 0.2), 2.2, lambda t: 2.6 - 0.4 * t, 8, [(-1, 3, "purple")], jit=0.15, cf=band_cf(rng, capb),
              top_cols=["rock_deep", "canyon_rock_dark"])
        talus(b, (0, 0, 0), 4.6, 7, 0.9, cf)
    elif var == 1:  # broad flat-topped mesa
        H = 18.0
        bands = strata(rng, H, 1, 1.4, 3.4)
        cf = band_cf(rng, bands)
        stack(b, (0, 0, 0), H, lambda t: 13.0 * (1 - 0.12 * t) - (2.0 if t < 0.15 else 0), 14, bands, ell=(1.0, 0.6), jit=0.14, cf=cf)
        talus(b, (0, 0, 0), 13.5, 14, 1.6, cf, ell=(1.0, 0.62))
    elif var == 2:  # twin towers on a shared plinth
        H = 8.0
        bands = strata(rng, 26, 2, 1.0, 2.8)
        cf = band_cf(rng, bands)
        stack(b, (0, 0, 0), H, lambda t: 7.0 * (1 - 0.25 * t), 11, bands, ell=(1.0, 0.65), jit=0.1, cf=cf)
        stack(b, (-2.6, 0.3, H - 0.3), 15.0, lambda t: 2.6 * (1 - 0.45 * t), 8, [(z0 - H, z1 - H, f) for z0, z1, f in bands],
              jit=0.14, cf=band_cf(rng, [(z0, z1, f) for z0, z1, f in bands]))
        stack(b, (2.8, -0.2, H - 0.3), 10.5, lambda t: 2.2 * (1 - 0.4 * t), 8, [(z0 - H, z1 - H, f) for z0, z1, f in bands],
              jit=0.14, cf=band_cf(rng, bands))
        talus(b, (0, 0, 0), 7.2, 10, 1.0, cf, ell=(1.0, 0.68))
    elif var == 3:  # eroded butte with heavy talus apron
        H = 13.0
        bands = strata(rng, H, 0, 1.2, 2.4)
        cf = band_cf(rng, bands)
        stack(b, (0, 0, 0), H, lambda t: 6.5 * (1 - 0.18 * t), 11, bands, jit=0.2, step=0.18, cf=cf)
        talus(b, (0, 0, 0), 7.0, 16, 1.4, cf)
        # sand apron
        n = 12
        ring = [b.v((math.cos(k * math.tau / n) * 11 * rng.uniform(0.85, 1.1), math.sin(k * math.tau / n) * 11 * rng.uniform(0.85, 1.1), -0.05))
                for k in range(n)]
        inner = [b.v((math.cos(k * math.tau / n) * 6.0, math.sin(k * math.tau / n) * 6.0, 1.6)) for k in range(n)]
        for k in range(n):
            b.face([ring[k], ring[(k + 1) % n], inner[(k + 1) % n], inner[k]], rng.choice(FAM["sand"][:3] + ["canyon_orange_light"]), out=E.UP)
    else:  # slender column + balanced rock
        H = 16.0
        bands = strata(rng, H, 2, 0.8, 2.0)
        cf = band_cf(rng, bands)
        stack(b, (0, 0, 0), H, lambda t: 2.4 * (1 - 0.35 * t) + 0.4 * math.sin(t * 12), 8, bands, jit=0.12, cf=cf)
        pts = b.rock_points((2.2, 1.8, 1.6), 22, cuts=3, sink=0.0)
        b.hull([p + Vector((0.3, 0, H - 0.2)) for p in pts], band_cf(rng, [(H - 2, H + 5, "purple")]), merge_deg=1.5)
        talus(b, (0, 0, 0), 3.0, 6, 0.7, cf)
    return b.finish(name)


# --------------------------------------------------------------------------- walls
def wall(name, var, gorge=False):
    b = B(4100 + var * 7 + (50 if gorge else 0))
    rng = b.rng
    if gorge:
        W, H, D = 12.0, [22.0, 18.0][var], 6.0
        bands = strata(rng, H, var + 1, 1.0, 2.6)
        K.cliff_wall(b, W, H, D, 9, 16, band_cf(rng, bands), lambda f: rng.choice(FAM["sand"][:3]),
                     overhang=[2.5, 4.5][var], ledges=0.5, depth_noise=0.08, lean=[-0.05, 0.0][var],
                     rows_z=[z1 for z0, z1, fm in bands], xjit=0.15)
        return b.finish(name)
    W, H, D, ov, lean = [(12, 10, 4.5, 0.0, 0.0), (10, 18, 5.0, 0.0, 0.04), (12, 6, 4.0, 1.8, 0.0), (10, 12, 5.0, -1.5, 0.0)][var]
    bands = strata(rng, H, var, 0.9, 2.4)
    K.cliff_wall(b, W, H, D, 9, max(6, int(H / 1.2)), band_cf(rng, bands), lambda f: rng.choice(FAM["sand"][:3] + ["dry_bush_light"]),
                 overhang=ov, ledges=0.45, depth_noise=0.08, lean=lean, rows_z=[z1 for z0, z1, fm in bands], xjit=0.15)
    talus(b, (0, -1.5, 0), 0.0, 3 + var, 0.6, band_cf(rng, bands))
    return b.finish(name)


# --------------------------------------------------------------------------- natural arch
def arch(name):
    """Natural rock arch over the track (track runs along Y). Clear span ~10.5 m, clear height ~9.5 m."""
    b = B(4200)
    rng = b.rng
    bands = strata(rng, 16, 0, 1.0, 2.2)
    cf = band_cf(rng, bands, top_z=11.5)
    for s in (-1, 1):
        stack(b, (s * 7.6, 0, 0), 7.6, lambda t: 2.4 * (1 - 0.12 * t) + 0.2 * math.sin(t * 7), 9, bands, ell=(1.0, 1.25),
              jit=0.12, cap=False, cf=cf)
        talus(b, (s * 7.6, 0, 0), 2.8, 5, 0.8, cf)
    R = 7.6
    pts, radii = [], []
    for i in range(13):
        a = math.pi * (1 - i / 12)
        pts.append(Vector((R * math.cos(a), rng.uniform(-0.1, 0.1), 7.2 + 4.0 * math.sin(a))))
        t = abs(math.cos(a))
        radii.append(1.35 + 0.9 * t ** 2)
    faces, caps, rings = b.tube(pts, radii, 8, cf, cap_top=False, jit=0.14, rot0=math.pi / 8)
    # flatten the arch cross-section a bit along Y (wider than tall)
    for ring in rings:
        c = sum((v.co for v in ring), Vector()) / len(ring)
        for v in ring:
            d = v.co - c
            v.co = c + Vector((d.x, d.y * 1.5, d.z))
    b.paint(faces, cf)
    return b.finish(name)


# --------------------------------------------------------------------------- cacti
def cactus_cf(rng, sides):
    cols = ["cactus_light", "cactus_base", "cactus_dark", "cactus_deep"]

    def make(faces):
        for i, f in enumerate(faces):
            k = i % sides
            idx = (0 if k % 2 == 0 else 2)
            f.normal_update()
            if f.normal.dot(LD) < -0.2:
                idx += 1
            if rng.random() < 0.15:
                idx = 1
            f[b_ref[0].L] = E.IDX[cols[min(3, idx)]]
    return make


b_ref = [None]


def saguaro(name, var):
    b = B(4300 + var)
    b_ref[0] = b
    rng = b.rng
    H = [7.5, 5.5, 9.0][var]
    r = [0.32, 0.28, 0.36][var]
    sides = 10
    pts = [Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), H * t)) for t in (-0.03, 0.25, 0.5, 0.72, 0.9)]
    pts.append(pts[-1] + Vector((0, 0, H * 0.07)))
    pts.append(pts[-1] + Vector((0, 0, H * 0.03)))
    radii = [r * 1.05, r, r, r * 0.97, r * 0.92, r * 0.7, r * 0.25]
    faces, caps, rings = b.tube(pts, radii, sides, const("cactus_base"), cap_top=True, star=(2, 0.82))
    cactus_cf(rng, sides)(faces)
    arms = [[(0.45, 0.0, 1.6), (0.55, 2.6, 1.3)], [(0.5, 1.2, 1.0)], [(0.4, 0.3, 1.8), (0.5, 2.2, 2.2), (0.62, 4.0, 1.4)]][var]
    for t, a, L in arms:
        p0 = Vector((0, 0, H * t))
        d = Vector((math.cos(a), math.sin(a), 0))
        out = 0.55 + rng.uniform(0, 0.25)
        apts = [p0 + d * (r * 0.6), p0 + d * (r + out * 0.6) + Vector((0, 0, 0.08)),
                p0 + d * (r + out) + Vector((0, 0, 0.45)), p0 + d * (r + out * 1.05) + Vector((0, 0, 0.45 + L * 0.6)),
                p0 + d * (r + out * 1.05) + Vector((0, 0, 0.45 + L)), p0 + d * (r + out * 1.05) + Vector((0, 0, 0.6 + L))]
        ar = r * 0.62
        f2, c2, r2 = b.tube(apts, [ar * 0.9, ar, ar, ar, ar * 0.85, ar * 0.3], 8, const("cactus_base"), cap_top=True, star=(2, 0.82))
        cactus_cf(rng, 8)(f2)
        if var == 2:
            tip = apts[-1]
            b.hull([tip + Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12), rng.uniform(0, 0.12))) for _ in range(5)],
                   const(rng.choice(["flower_white", "flower_yellow"])))
    if var == 2:
        tip = pts[-1]
        for k in range(3):
            b.hull([tip + Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), rng.uniform(0, 0.12))) for _ in range(5)],
                   const(rng.choice(["flower_white", "flower_yellow", "flower_white"])))
    return b.finish(name)


def prickly_pear(name, var):
    b = B(4400 + var)
    rng = b.rng
    cf = shade(["cactus_light", "cactus_base", "cactus_dark", "cactus_deep"], spread=0.5, rng=rng)
    pads = []

    def pad(c, ang, tilt, s):
        rot = (rng.uniform(-0.15, 0.15), tilt, ang)
        b.ico(c, (0.34 * s, 0.075 * s, 0.4 * s), 1, cf, jit=0.12, rot=rot)
        up = Vector((0, 0, 0.75 * s))
        up.rotate(E.Euler(rot))
        pads.append((Vector(c), up, ang, s))
    roots = [2, 3][var]
    for k in range(roots):
        a = k * math.tau / roots + rng.uniform(-0.4, 0.4)
        pad(Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.32)), a + math.pi / 2, rng.uniform(-0.3, 0.3), rng.uniform(0.9, 1.1))
    gen = list(pads)
    for g in range(2):
        nxt = []
        for c, up, ang, s in gen:
            for j in range(rng.choice((1, 2))):
                a2 = ang + rng.uniform(-0.4, 0.4)
                if len(pads) >= 8:
                    break
                c2 = c + up * rng.uniform(0.8, 0.9) + Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), 0))
                pad(c2, a2, rng.uniform(-0.75, 0.75), s * rng.uniform(0.75, 0.9))
                nxt.append(pads[-1])
        gen = nxt
    for c, up, ang, s in pads[-5:]:
        if rng.random() < 0.8:
            p = c + up * 0.95
            b.hull([p + Vector((rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06), rng.uniform(-0.04, 0.08))) for _ in range(5)],
                   const(rng.choice(["flower_pink", "sky_pink", "flower_red"] if var == 0 else ["flower_yellow", "flower_pink"])))
    return b.finish(name)


# --------------------------------------------------------------------------- dry brush
DRY = ["dry_bush_light", "dry_bush_base", "dry_bush_dark", "dry_bush_deep"]


def dry_bush(name, var):
    b = B(4500 + var)
    rng = b.rng
    if var == 2:  # tumbleweed
        R = 0.5
        c = Vector((0, 0, R * 0.9))
        for k in range(4):
            ax = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
            u = ax.orthogonal().normalized()
            w = ax.cross(u)
            pts = [c + (u * math.cos(t * math.tau / 6) + w * math.sin(t * math.tau / 6)) * R * rng.uniform(0.85, 1.1) for t in range(7)]
            b.tube(pts, 0.025, 3, pick(DRY[:3] + ["rope"], rng=rng), cap_top=False)
        for k in range(6):
            d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
            b.tube([c + d * R * 0.2, c + d * R * 0.95], 0.02, 3, pick(DRY, rng=rng), cap_top=False)
        return b.finish(name)
    n = [10, 7][var]
    for k in range(n):
        a = rng.uniform(0, math.tau)
        el = rng.uniform(0.5, 1.2)
        L = rng.uniform(0.6, 1.2)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
        p0 = Vector((0, 0, -0.05))
        p1 = p0 + d * L * 0.55 + Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), 0))
        p2 = p1 + (d + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 0.2))).normalized() * L * 0.5
        b.tube([p0, p1, p2], [0.035, 0.022, 0.006], 3, pick(["dry_bush_dark", "dry_bush_deep", "trunk_light"], rng=rng), cap_top=False)
        if var == 0 and k % 4 == 0:
            b.ico(p2, (0.14, 0.14, 0.1), 1, shade(DRY, rng=rng), jit=0.3)
    if var == 1:  # sage clump: soft grey-green blobs
        for k in range(4):
            a = k * math.tau / 4 + rng.uniform(-0.3, 0.3)
            c = Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.35 + rng.uniform(0, 0.2)))
            b.ico(c, (0.42, 0.38, 0.32), 1, shade(["dry_bush_light", "cactus_light", "dry_bush_base", "cactus_dark"], rng=rng), jit=0.25)
    return b.finish(name)


# --------------------------------------------------------------------------- mine rail + cart
def mine_rail(name, var):
    """6 m narrow-gauge (0.6 m) rail along Y. var 1: damaged, half-buried."""
    b = B(4600 + var)
    rng = b.rng
    L = 6.0
    for i in range(10):
        y = -L / 2 + 0.3 + i * 0.6
        if var == 1 and i in (3, 7):
            continue
        rot = (0, 0, rng.uniform(-0.06, 0.06) + (0.35 if var == 1 and i == 5 else 0))
        b.box((rng.uniform(-0.04, 0.04), y, 0.05), (1.3, 0.2, 0.12), pick(["wood_base", "wood_dark", "wood_deep", "dry_bush_dark"], rng=rng),
              rot=rot, jit=0.01)
    for x in (-0.3, 0.3):
        lift = (0.35 if (var == 1 and x > 0) else 0.0)
        p0 = Vector((x, -L / 2, 0.12))
        p1 = Vector((x, L / 2, 0.12 + lift))
        b.beam(p0, p1, 0.12, 0.025, pick(["rust_dark", "rust_deep"], rng=rng))
        b.beam(p0 + Vector((0, 0, 0.06)), p1 + Vector((0, 0, 0.06)), 0.03, 0.09, pick(["rust_base", "rust_dark"], rng=rng))
        b.beam(p0 + Vector((0, 0, 0.115)), p1 + Vector((0, 0, 0.115)), 0.06, 0.03, pick(["rail_metal_dark", "rust_light"], rng=rng))
    if var == 1:  # sand drift over the rails
        for k in range(2):
            c = Vector((rng.uniform(-0.4, 0.4), rng.uniform(-2.0, 2.0), 0.0))
            b.ico(c, (1.0, 0.8, 0.22), 1, shade(FAM["sand"], rng=rng), jit=0.2)
    return b.finish(name)


def mine_cart(name):
    b = B(4700)
    rng = b.rng
    rust = ["rust_light", "rust_base", "rust_dark", "rust_deep"]
    z0, z1 = 0.45, 1.15
    bot = [(-0.45, -0.65), (0.45, -0.65), (0.45, 0.65), (-0.45, 0.65)]
    top = [(-0.58, -0.8), (0.58, -0.8), (0.58, 0.8), (-0.58, 0.8)]
    t = 0.06
    ob = [b.v((x, y, z0)) for x, y in bot]
    ot = [b.v((x, y, z1)) for x, y in top]
    ib = [b.v((x * 0.88, y * 0.9, z0 + t)) for x, y in bot]
    it = [b.v((x - math.copysign(t, x), y - math.copysign(t, y), z1)) for x, y in top]
    cf = shade(rust, spread=0.6, rng=rng)
    for k in range(4):
        k2 = (k + 1) % 4
        mid = Vector((0, 0, (z0 + z1) / 2))
        f = b.face([ob[k], ob[k2], ot[k2], ot[k]], "rust_base", out=(ob[k].co + ot[k2].co) * 0.5 - mid)
        f[b.L] = E.IDX[cf(f)]
        g = b.face([ib[k], ib[k2], it[k2], it[k]], "rust_dark", out=mid - (ib[k].co + it[k2].co) * 0.5)
        g[b.L] = E.IDX[rng.choice(["rust_dark", "rust_deep"])]
        b.face([ot[k], ot[k2], it[k2], it[k]], "rail_metal_dark", out=E.UP)
    b.face(ob, "rust_deep", out=Vector((0, 0, -1)))
    b.face(ib, "rust_deep", out=E.UP)
    # straps
    for zz in (0.62, 0.98):
        s = (zz - z0) / (z1 - z0)
        w = 0.45 + (0.58 - 0.45) * s + 0.02
        d = 0.65 + (0.8 - 0.65) * s + 0.02
        for (cx, cy, sx, sy) in ((0, -d, 2 * w, 0.03), (0, d, 2 * w, 0.03), (-w, 0, 0.03, 2 * d), (w, 0, 0.03, 2 * d)):
            b.box((cx, cy, zz), (sx, sy, 0.07), const("rust_deep"))
    # chassis + wheels
    for x in (-0.3, 0.3):
        b.beam((x, -0.75, 0.38), (x, 0.75, 0.38), 0.1, 0.1, const("rail_metal_dark"))
    for y in (-0.45, 0.45):
        b.beam((-0.4, y, 0.24), (0.4, y, 0.24), 0.06, 0.06, const("rail_metal_dark"))
        for x in (-0.32, 0.32):
            s = 1 if x > 0 else -1
            b.tube([Vector((x - s * 0.03, y, 0.24)), Vector((x + s * 0.05, y, 0.24))], 0.22, 8,
                   pick(["rust_deep", "rail_metal_dark", "rust_dark"], rng=rng), cap_top=True, cap_bot=True)
    # ore load
    for k in range(5):
        c = Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.45, 0.45), z1 - 0.12))
        pts = b.rock_points((0.22, 0.2, 0.18), 8, cuts=1, sink=0.0)
        b.hull([p + c for p in pts], pick(FAM["rock"] + ["granite_dark"], rng=rng))
    return b.finish(name)


# --------------------------------------------------------------------------- water tower
def water_tower(name):
    b = B(4800)
    rng = b.rng
    wcf = shade(["wood_light", "wood_base", "wood_dark", "dry_bush_dark"], spread=0.6, rng=rng)
    Hl = 6.0
    legs = [(-1.6, -1.6), (1.6, -1.6), (1.6, 1.6), (-1.6, 1.6)]
    for x, y in legs:
        b.beam((x * 1.15, y * 1.15, -0.3), (x * 0.9, y * 0.9, Hl), 0.24, 0.24, wcf, jit=0.015)
    for i in range(4):
        (x0, y0), (x1, y1) = legs[i], legs[(i + 1) % 4]
        for (za, zb) in ((0.5, 3.0), (3.0, 5.6)):
            fa = 1.15 - 0.25 * za / Hl
            fb = 1.15 - 0.25 * zb / Hl
            b.beam((x0 * fa, y0 * fa, za), (x1 * fb, y1 * fb, zb), 0.1, 0.06, wcf)
            b.beam((x1 * fa, y1 * fa, za), (x0 * fb, y0 * fb, zb), 0.1, 0.06, wcf)
        f = 1.15 - 0.25 * 3.0 / Hl
        b.beam((x0 * f, y0 * f, 3.0), (x1 * f, y1 * f, 3.0), 0.12, 0.12, wcf)
    b.box((0, 0, Hl + 0.1), (4.0, 4.0, 0.2), wcf)
    # tank: staves (alternating colours) + hoops
    R, Ht = 1.7, 2.6
    sides = 14
    faces, caps, rings = b.tube([Vector((0, 0, Hl + 0.2)), Vector((0, 0, Hl + 0.2 + Ht))], [R, R * 0.97], sides, wcf, cap_top=False)
    for i, f in enumerate(faces):
        f[b.L] = E.IDX[["wood_light", "wood_base", "wood_dark", "wood_base"][(i + rng.randint(0, 1)) % 4]]
    for zz in (0.35, 1.3, 2.25):
        b.tube([Vector((0, 0, Hl + 0.2 + zz)), Vector((0, 0, Hl + 0.2 + zz + 0.08))], R * 1.02, sides,
               pick(["rust_base", "rust_dark"], rng=rng), cap_top=False)
    # roof cone (rusty tin)
    b.tube([Vector((0, 0, Hl + 0.15 + Ht)), Vector((0, 0, Hl + 1.25 + Ht))], [R * 1.12, 0.0], sides,
           pick(["rust_base", "rust_dark", "rust_light", "rail_metal_dark"], rng=rng), cap_top=False)
    b.face([b.v((math.cos(k * math.tau / sides) * R * 1.1, math.sin(k * math.tau / sides) * R * 1.1, Hl + 0.14 + Ht)) for k in range(sides)],
           "rust_deep", out=Vector((0, 0, -1)))
    # spout + ladder
    b.tube([Vector((R * 0.9, 0, Hl + 0.5)), Vector((R + 0.9, 0, Hl - 0.1)), Vector((R + 1.1, 0, Hl - 0.9))], 0.11, 6,
           pick(["rust_base", "rust_dark"], rng=rng), cap_top=True)
    for y in (-0.25, 0.25):
        b.beam((-2.2, y, -0.2), (-1.85, y, Hl + 0.2), 0.06, 0.06, const("wood_dark"))
    for k in range(12):
        z = 0.4 + k * 0.5
        x = -2.2 + 0.35 * z / (Hl + 0.4)
        b.beam((x, -0.25, z), (x, 0.25, z), 0.04, 0.04, const("wood_base"))
    return b.finish(name)


# --------------------------------------------------------------------------- build
def build_kit():
    objs = {}

    def add(o):
        objs[o.name] = o
    for v in range(5):
        add(mesa(f"SM_Env_Canyon_Mesa_{v + 1:02d}", v))
    for v in range(4):
        add(wall(f"SM_Env_Canyon_Wall_{v + 1:02d}", v))
    add(arch("SM_Env_Canyon_Arch_01"))
    for v in range(3):
        add(saguaro(f"SM_Env_Canyon_Saguaro_{v + 1:02d}", v))
        add(dry_bush(f"SM_Env_Canyon_{['DryBush_01', 'DryBush_02', 'Tumbleweed_01'][v]}", v))
    for v in range(2):
        add(prickly_pear(f"SM_Env_Canyon_PricklyPear_{v + 1:02d}", v))
        add(mine_rail(f"SM_Env_Canyon_MineRail_{v + 1:02d}", v))
        add(wall(f"SM_Env_Canyon_GorgeWall_{v + 1:02d}", v, gorge=True))
    add(mine_cart("SM_Env_Canyon_MineCart_01"))
    add(water_tower("SM_Env_Canyon_WaterTower_01"))
    add(K.build_mountain_card("SM_Env_Canyon_MesaCard_01", 0, ramp=["mesa_far_light", "mesa_far_base", "mesa_far_dark"],
                              width=460, seed=4900, sharp=0.5, height=70))
    # palette swaps of the common kit (desert rocks, sandy banks, dry grass)
    for cat, label in (("S", "RockSmall"), ("M", "RockMedium"), ("L", "RockLarge")):
        for v in range(2):
            add(K.build_rock(f"SM_Env_Canyon_{label}_{v + 1:02d}", cat, v * 2 + 1, pal=K.P_CANYON, seed_base=4950))
    for v in range(3):
        add(K.build_slope(f"SM_Env_Canyon_Slope_{v + 1:02d}", v, pal=K.P_CANYON))
    for v in range(2):
        add(K.build_grass(f"SM_Env_Canyon_DryGrass_{v + 1:02d}", v, pal=K.P_CANYON, flowers=("flower_yellow", "sand_light")))
    return objs


def compose(objs, common):
    """Sunset canyon run: strata walls on both sides, arch over the track, mesas on the horizon."""
    bpy = E.bpy
    coll = bpy.data.collections.new("Composition")
    bpy.context.scene.collection.children.link(coll)
    ts = E.TrackScene(lambda y: 6.0 * math.sin(y / 50.0), grade=0.05,
                      bank_l=lambda d: 0.6 * E.smoothstep(0.3, 3.0, d) + 0.08 * max(0.0, d - 3),
                      bank_r=lambda d: 0.5 * E.smoothstep(0.3, 3.0, d) + 0.05 * max(0.0, d - 3), seed=44)
    g = ts.build_ground(["sand_base", "sand_dark", "canyon_orange_light", "sand_light"],
                        ["sand_dark", "canyon_orange_base", "sand_base", "canyon_rock_light"], x_range=(-80, 80),
                        y_range=(-20, 260), cell=3.0)
    t = ts.build_track(y_range=(-20, 260), asphalt=("asphalt_base", "asphalt_dark", "asphalt_dark", "asphalt_base"),
                       curb=("curb_yellow", "curb_black"))
    for o in (g, t):
        bpy.context.scene.collection.objects.unlink(o)
        coll.objects.link(o)
    get = lambda key: [o for n, o in objs.items() if key in n]  # noqa: E731
    placed = []
    yr = (-5, 200)
    # canyon walls both sides (front faces the track)
    for side in (-1, 1):
        for k, y in enumerate(range(-10, 120, 11)):
            d = 20 + (k % 3) * 4.0 + (10 if side > 0 and 20 < y < 70 else 0)
            x = ts.cx(y) + side * (ts.half + d)
            nm = f"SM_Env_Canyon_Wall_0{1 + (k + (side > 0)) % 4}"
            E.instance(objs[nm], (x, y, ts.h(x, y) - 0.5), (-math.pi / 2 if side > 0 else math.pi / 2) + 0.12 * ((k % 3) - 1),
                       1.0 + 0.15 * (k % 2), coll=coll)
    # gorge narrowing further ahead
    for side in (-1, 1):
        for k, y in enumerate(range(130, 200, 12)):
            x = ts.cx(y) + side * (ts.half + 4.5)
            E.instance(objs[f"SM_Env_Canyon_GorgeWall_0{1 + k % 2}"], (x, y, ts.h(x, y) - 0.5),
                       -math.pi / 2 if side > 0 else math.pi / 2, 1.0, coll=coll)
    ya = 52.0
    E.instance(objs["SM_Env_Canyon_Arch_01"], (ts.cx(ya), ya, ts.zt(ya) - 0.2), 0.05, 1.0, coll=coll)
    # far mesas
    for nm, x, y, s, rz in (("Mesa_02", -70, 330, 1.5, 0.2), ("Mesa_01", 60, 260, 1.2, 0), ("Mesa_03", 150, 420, 1.6, 0.5),
                            ("Mesa_05", -30, 230, 1.1, 0), ("Mesa_04", -140, 380, 1.4, 0.3), ("Mesa_02", 230, 600, 2.4, 1.0)):
        E.instance(objs["SM_Env_Canyon_" + nm], (x, y, ts.h(x, y) - 1), rz, s, coll=coll)
    E.instance(objs["SM_Env_Canyon_MesaCard_01"], (0, 900, -40), 0.0, 2.2, coll=coll)
    ts.scatter(get("Saguaro"), 26, (1.5, 14), yr, (0.8, 1.2), min_gap=3, placed=placed, coll=coll, tilt=0.03)
    ts.scatter(get("PricklyPear"), 30, (0.6, 12), yr, (0.8, 1.3), min_gap=1.5, placed=placed, coll=coll)
    ts.scatter(get("DryBush") + get("Tumbleweed"), 80, (0.2, 13), yr, (0.7, 1.4), min_gap=1.0, placed=placed, coll=coll)
    ts.scatter(get("RockLarge") + get("RockMedium") + get("RockSmall"), 60, (0.4, 13), yr, (0.6, 1.4), min_gap=1.2,
               placed=placed, coll=coll)
    ts.scatter(get("DryGrass"), 120, (0.0, 12), yr, (0.8, 1.5), coll=coll)
    # mine props on the right bench
    yr0 = 14.0
    for k in range(4):
        y = yr0 + k * 6.0
        x = ts.cx(y) + ts.half + 6.5
        E.instance(objs[f"SM_Env_Canyon_MineRail_0{1 + (k == 2)}"], (x, y, ts.h(x, y) - 0.05), 0.08, 1.0, coll=coll)
    x = ts.cx(yr0 + 10) + ts.half + 6.8
    E.instance(objs["SM_Env_Canyon_MineCart_01"], (x, yr0 + 10, ts.h(x, yr0 + 10) + 0.1), 0.08, 1.0, coll=coll)
    x = ts.cx(34) - ts.half - 7.5
    E.instance(objs["SM_Env_Canyon_WaterTower_01"], (x, 34, ts.h(x, 34) - 0.2), 0.4, 1.0, coll=coll)
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
    E.write_stats("canyon", objs.values())
    bpy = E.bpy
    E.setup_render((1600, 900), samples=32)
    E.world("#9CC6EC", "#EDE6DC", strength=1.0)
    E.sun(45, 145, "#FFF3DC", 3.4, 3)
    g = E.ground_plane(200, ["tint_light", "tint_light", "tint_mid"], z=-0.02)
    groups = {
        "rocks": [["Mesa_"], ["Wall_", "Arch", "GorgeWall"]],
        "props": [["Saguaro", "PricklyPear", "DryBush", "Tumbleweed", "DryGrass"],
                  ["MineRail", "MineCart", "WaterTower", "Rock"], ["Canyon_Slope"]],
    }
    for gname, rows_keys in groups.items():
        for o in objs.values():
            o.hide_render = True
        rows = []
        used = set()
        for keys in rows_keys:
            row = [o for n, o in objs.items() if any(k in n for k in keys) and n not in used]
            used.update(o.name for o in row)
            rows.append(row)
            for o in row:
                o.hide_render = False
        E.grid_layout(rows, gap=2.0, row_gap=3.0)
        shown = [o for r in rows for o in r]
        g.location = (0, 30, 0)
        cam = E.fit_camera(shown, elev=24, azim=-24)
        E.render(os.path.join(E.RENDER_DIR, f"env_canyon_grid_{gname}.png"))
        bpy.data.objects.remove(cam)
    for o in objs.values():
        o.hide_render = True
        o.location = (0, -900, -300)
    g.hide_render = True
    bpy.data.objects.remove(bpy.data.objects["Sun"])
    # ---- composition: sunset
    E.setup_render((1280, 720), samples=64, exposure=0.15)
    E.world("#5A3F8E", "#F0A060", strength=1.0, fog=(0.0011, "#D58C86", 0.5, ((0, 300, 60), (1000, 1400, 300))), mid=0.32)
    E.sun(10, 250, "#FFB070", 5.0, 1.5)
    ts = compose(objs, common)
    cy = -8.0
    E.camera((ts.cx(cy) + 0.4, cy, ts.zt(cy) + 2.2), (ts.cx(40) - 1.0, 40, ts.zt(40) + 4.0), lens=24)
    E.render(os.path.join(E.RENDER_DIR, "env_canyon_composition.png"))
    E.save_blend("Env_Canyon.blend")


if __name__ == "__main__":
    main()
