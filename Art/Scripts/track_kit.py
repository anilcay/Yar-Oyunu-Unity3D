"""Luge Rush - modular track kit (edges, signs, start/finish, lift, tunnel, props).

Run:  blender -b -P Art/Scripts/track_kit.py              (build + export + renders)
      blender -b -P Art/Scripts/track_kit.py -- --no-render

Conventions (see Art/README.md)
- Blender front = -Y = Unity +Z.  Modules that tile start at the pivot (0,0,0) and run along
  Blender -Y (= Unity +Z) for their nominal length.  x = 0 is the module centre line.
  The "outside" of edge modules (post brackets, net braces) is Blender -X = Unity +X.
- Signs / gates / arch face the oncoming riders: their front is Blender +Y (= Unity -Z).
- Lamp meshes of the start gate use material "LR_Palette_Emissive" (same atlas as emission map).
- Lift: the haul cable runs CABLE_X beside the lane centre (Unity +0.75 m, right of travel).  The sled
  corridor (|x| < LANE_CLEAR, z < LANE_CLEAR_Z) stays free of all station geometry.

--only A,B limits FBX export to those modules (FBX files embed a timestamp, so unchanged ones stay put).
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

import lr_common as lr  # noqa: E402

FONT = r"C:\Windows\Fonts\ariblk.ttf"
MODULES = {}  # name -> builder

# Lift contract: TrackSceneBuilder.CableLateral = +0.75 (Unity +X = Blender -X)
CABLE_X = -0.75       # bull wheel plane / cable line
CABLE_Z = 0.255       # SM_Track_Lift_Cable_4m centre height
CABLE_END_Y = -3.0    # both stations: the cable modules stop 3 m into the 6 m station
LANE_CLEAR = 0.60     # sled corridor half width (widest sled 0.47 m)
LANE_CLEAR_Z = 1.10   # sled corridor height


def module(fn):
    MODULES[fn.__name__] = fn
    return fn


def viewer_matrix(y0=0.0, x0=0.0, z0=0.0):
    """Map 'sign space' (u = right as seen by an oncoming rider, v = up, w = towards the rider)
    to Blender: (-u + x0, w + y0, v + z0)."""
    return Matrix(((-1, 0, 0, x0), (0, 0, 1, y0), (0, 1, 0, z0), (0, 0, 0, 1)))


def back_matrix(y0=0.0, x0=0.0, z0=0.0):
    """Same as viewer_matrix but for the back face (readable by someone behind, looking +Y)."""
    return Matrix(((1, 0, 0, x0), (0, 0, -1, y0), (0, 1, 0, z0), (0, 0, 0, 1)))


def bolt(mb, p, n=(0, 0, 1), r=0.012, cell="galv_light"):
    p, n = Vector(p), Vector(n).normalized()
    return mb.cylinder(p, p + n * 0.012, r, cell, segments=6)


# ============================================================================= edge modules
@module
def SM_Track_Curb_2m():
    """Raised yellow/black curb, 2 m, 0.3 m wide.  Track side = Blender +X (Unity -X)."""
    mb = lr.MeshBuilder(seed=21)
    prof = [(0.15, 0.0), (0.15, 0.03), (0.07, 0.11), (-0.12, 0.12), (-0.15, 0.09), (-0.15, 0.0)]
    n = 8
    rings = [[(x, -2.0 * i / n, z) for x, z in prof] for i in range(n + 1)]
    grid, caps = mb.loft(rings, "curb_yellow")
    rng = random.Random(4)
    for i, row in enumerate(grid):
        yellow = (i // 2) % 2 == 0
        for f in row:
            if yellow:
                c = "curb_yellow" if rng.random() > 0.25 else "curb_yellow_dirty"
            else:
                c = "curb_black" if rng.random() > 0.3 else "curb_black_worn"
            if f.normal.z < 0.2 and f.normal.x < 0:  # outer face slightly darker
                c = lr.darker(c)
            f[mb.pal] = lr.cell_index(c)
    mb.paint(caps, "concrete")
    # bottom faces never visible: concrete
    mb.paint([f for f in mb.bm.faces if f.normal.z < -0.9], "concrete_dark")
    return mb.to_object("SM_Track_Curb_2m")


def rail_post(mb, y, h=0.95):
    mb.box((0, y, h / 2), (0.07, 0.07, h), {"top": "rail_metal_light", "side": "rail_metal_base"}, bevel=0.008)
    mb.box((0, y, 0.01), (0.18, 0.18, 0.02), "concrete_dark")
    mb.box((-0.06, y, 0.62), (0.06, 0.04, 0.12), "rail_metal_dark")  # bracket on the outside


@module
def SM_Track_Rail_4m():
    """Metal guard rail: posts every 2 m + two horizontal pipes.  <= 200 tris."""
    mb = lr.MeshBuilder(seed=22)
    for y in (0.0, -2.0):
        rail_post(mb, y)
    for z, c in ((0.5, "rail_metal_base"), (0.88, "rail_metal_light")):
        mb.cylinder((0.0, 0.0, z), (0.0, -4.0, z), 0.032, c, segments=6, caps=False)
    return mb.to_object("SM_Track_Rail_4m")


@module
def SM_Track_Rail_End():
    """Rail terminal: a post with both pipes bending down into the ground (1 m, runs along -Y)."""
    mb = lr.MeshBuilder(seed=23)
    rail_post(mb, 0.0)
    for z, r in ((0.5, 0.45), (0.88, 0.8)):
        pts = []
        for k in range(6):
            a = math.pi / 2 * k / 5
            pts.append((0.0, -r * math.sin(a) * 1.0, z - z * (1 - math.cos(a)) * 0.98))
        pts = [(0.0, 0.0, z)] + [(x, y - 0.0, zz) for x, y, zz in pts[1:]]
        mb.sweep(pts, 0.032, "rail_metal_base", sides=6)
    mb.box((0, -0.45, 0.04), (0.35, 0.35, 0.08), "concrete", bevel=0.02)
    mb.box((0, -0.8, 0.04), (0.3, 0.3, 0.08), "concrete", bevel=0.02)
    mb.box((0.0, -0.05, 0.75), (0.09, 0.03, 0.18), "hazard_orange")  # reflector plate
    return mb.to_object("SM_Track_Rail_End")


@module
def SM_Track_Net_4m():
    """Catch-net barrier: 2 posts + diamond net (double-sided strands) + top rope."""
    mb = lr.MeshBuilder(seed=24)
    h0, h1 = 0.25, 1.75
    for y in (-0.04, -2.04):  # post face flush with the module start -> bounds exactly 4 m, 2 m pitch
        mb.box((0, y, 0.95), (0.08, 0.08, 1.9), {"top": "rail_metal_light", "side": "net_black"})
        mb.box((-0.35, y, 0.6), (0.05, 0.05, 1.4), "rail_metal_dark", rot=Euler((0, math.radians(-28), 0)))
    mb.cylinder((0, 0.0, h1), (0, -4.0, h1), 0.02, "rope", segments=4, caps=False)
    mb.cylinder((0, 0.0, h0), (0, -4.0, h0), 0.016, "net_black", segments=4, caps=False)
    sp = 0.5
    w = 0.02
    y_hi, y_lo = -w, -4.0 + w      # strands stay inside the module even with their width
    k = -int(1.5 / sp) - 1
    while k * sp < 4.0 + 1.5:
        for sgn in (1, -1):
            # strand from the bottom edge (y = -k*sp) diagonally up, clipped to the module length
            a = Vector((0, -k * sp, h0))
            b = Vector((0, -k * sp - sgn * (h1 - h0), h1))
            dy = b.y - a.y
            t_hi, t_lo = (y_hi - a.y) / dy, (y_lo - a.y) / dy
            t0, t1 = max(0.0, min(t_hi, t_lo)), min(1.0, max(t_hi, t_lo))
            if t1 - t0 < 1e-3:
                continue
            a, b = a.lerp(b, t0), a.lerp(b, t1)
            d = (b - a).normalized()
            side = Vector((0, -d.z, d.y)) * w
            mb.surface([[a - side, b - side], [a + side, b + side]], "net_green", double_sided=True)
        k += 1
    return mb.to_object("SM_Track_Net_4m")


# ============================================================================= signs
def sign_post(mb, h=2.0, x=0.0):
    mb.cylinder((x, 0, 0), (x, 0, h), 0.035, "galv_light", segments=6, cap_cell="rail_metal_base")
    mb.box((x, 0, 0.02), (0.22, 0.22, 0.04), "concrete_dark")


def sign_panel(mb, w, h, zc, shape="rect"):
    """Yellow sign with black border, front face Blender +Y."""
    m = viewer_matrix(0.05)
    if shape == "diamond":
        s = w / 2
        outer = [(0, zc + s), (-s, zc), (0, zc - s), (s, zc)]
        inner = [(0, zc + s * 0.86), (-s * 0.86, zc), (0, zc - s * 0.86), (s * 0.86, zc)]
    else:
        outer = [(-w / 2, zc - h / 2), (w / 2, zc - h / 2), (w / 2, zc + h / 2), (-w / 2, zc + h / 2)]
        b = 0.04
        inner = [(-w / 2 + b, zc - h / 2 + b), (w / 2 - b, zc - h / 2 + b), (w / 2 - b, zc + h / 2 - b),
                 (-w / 2 + b, zc + h / 2 - b)]
    fs = mb.prism(outer, 0.025, "decal_black", matrix=m @ Matrix.Translation((0, 0, -0.025)))
    mb.paint([f for f in fs if f.normal.y < -0.5], "rail_metal_base")  # galvanised back
    mb.prism(inner, 0.004, "sign_yellow", matrix=m)
    # mounting brackets behind
    for dz in (-h * 0.3, h * 0.3):
        mb.box((0, 0.0, zc + dz), (0.12, 0.05, 0.04), "rail_metal_dark")
    return m


def arrow_curve(mb, m, zc, direction=1, depth=0.006):
    """Bold curved arrow in sign space; direction +1 = turns to the rider's right."""
    pts = []
    r = 0.17
    cx = -direction * 0.0
    for k in range(7):
        a = math.pi * (1 - k / 6 * 0.5)  # 180deg -> 90deg
        pts.append((cx + direction * (r * math.cos(a) + r * 0.6), zc - 0.15 + r * math.sin(a) * 1.4))
    pts = [(direction * -0.11, zc - 0.25)] + pts
    width = 0.045
    left, right = [], []
    for i, p in enumerate(pts):
        a = Vector(pts[max(i - 1, 0)])
        b = Vector(pts[min(i + 1, len(pts) - 1)])
        t = (b - a).normalized()
        nrm = Vector((-t.y, t.x))
        left.append(Vector(p) + nrm * width)
        right.append(Vector(p) - nrm * width)
    outline = [tuple(v) for v in left] + [tuple(v) for v in reversed(right)]
    mb.prism(outline, depth, "decal_black", matrix=m @ Matrix.Translation((0, 0, 0.004)))
    tip = Vector(pts[-1])
    t = (Vector(pts[-1]) - Vector(pts[-2])).normalized()
    nrm = Vector((-t.y, t.x))
    head = [tuple(tip + nrm * 0.11), tuple(tip + t * 0.13), tuple(tip - nrm * 0.11)]
    if direction > 0:
        head = list(reversed(head))
    fs = mb.prism(head, depth, "decal_black", matrix=m @ Matrix.Translation((0, 0, 0.004)))
    return fs


def _sign(name, kind):
    mb = lr.MeshBuilder(seed=hash(name) & 0xffff)
    sign_post(mb, 2.1)
    zc = 1.75
    if kind in ("left", "right"):
        m = sign_panel(mb, 0.55, 0.75, zc)
        arrow_curve(mb, m, zc, direction=1 if kind == "right" else -1)
    elif kind == "slow":
        m = sign_panel(mb, 0.6, 0.8, zc)
        mb.text("YAVAŞ", "decal_black", size=0.13, depth=0.006, font_path=FONT, resolution=1,
                matrix=m @ Matrix.Translation((0, zc + 0.2, 0.004)))
        for k in range(2):  # double chevron pointing down
            y = zc - 0.02 - k * 0.15
            ch = [(-0.17, y + 0.06), (0, y - 0.06), (0.17, y + 0.06), (0.17, y + 0.12), (0, y), (-0.17, y + 0.12)]
            mb.prism(ch, 0.006, "decal_black", matrix=m @ Matrix.Translation((0, 0, 0.004)))
    elif kind == "warn":
        m = sign_panel(mb, 0.8, 0.8, zc, shape="diamond")
        mb.prism([(-0.035, zc - 0.02), (0.035, zc - 0.02), (0.05, zc + 0.24), (-0.05, zc + 0.24)], 0.006,
                 "decal_black", matrix=m @ Matrix.Translation((0, 0, 0.004)))
        mb.prism([(-0.04, zc - 0.17), (0.04, zc - 0.17), (0.04, zc - 0.09), (-0.04, zc - 0.09)], 0.006,
                 "decal_black", matrix=m @ Matrix.Translation((0, 0, 0.004)))
    elif kind == "helmet":  # "keep hands on the handle" info sign (red frame)
        m = sign_panel(mb, 0.55, 0.75, zc)
        mb.cylinder(m @ Vector((0, zc + 0.05, 0.004)), m @ Vector((0, zc + 0.05, 0.012)), 0.2, "sign_red",
                    segments=12)
        mb.cylinder(m @ Vector((0, zc + 0.05, 0.012)), m @ Vector((0, zc + 0.05, 0.016)), 0.15, "sign_yellow",
                    segments=12)
        mb.text("!", "decal_black", size=0.25, depth=0.006, font_path=FONT, resolution=1,
                matrix=m @ Matrix.Translation((0, zc + 0.05, 0.016)))
        mb.text("DUR", "decal_black", size=0.1, depth=0.006, font_path=FONT, resolution=1,
                matrix=m @ Matrix.Translation((0, zc - 0.27, 0.004)))
    return mb.to_object(name)


@module
def SM_Track_Sign_CurveLeft():
    return _sign("SM_Track_Sign_CurveLeft", "left")


@module
def SM_Track_Sign_CurveRight():
    return _sign("SM_Track_Sign_CurveRight", "right")


@module
def SM_Track_Sign_Slow():
    return _sign("SM_Track_Sign_Slow", "slow")


@module
def SM_Track_Sign_Warning():
    return _sign("SM_Track_Sign_Warning", "warn")


@module
def SM_Track_Sign_Stop():
    return _sign("SM_Track_Sign_Stop", "helmet")


def _marker(name, label):
    mb = lr.MeshBuilder(seed=7)
    mb.box((0, 0, 0.55), (0.14, 0.14, 1.1), "checker_white", bevel=0.015)
    for z in (0.25, 0.6):
        mb.box((0, 0, z), (0.148, 0.148, 0.12), "sign_red")
    mb.box((0, 0.0, 1.0), (0.34, 0.18, 0.24), "checker_white", bevel=0.01)
    size = 0.18 if len(label) == 1 else 0.11
    for m in (viewer_matrix(0.09), back_matrix(-0.09)):
        mb.text(label, "decal_black", size=size, depth=0.008, font_path=FONT, resolution=1,
                matrix=m @ Matrix.Translation((0, 1.0, 0)))
    mb.box((0, 0, 0.02), (0.24, 0.24, 0.04), "concrete_dark")
    return mb.to_object(name)


for _n in ("1", "2", "3", "4", "5"):
    MODULES["SM_Track_MarkerPost_0" + _n] = (lambda n=_n: _marker("SM_Track_MarkerPost_0" + n, n))
for _n in ("100", "200", "300"):
    MODULES["SM_Track_MarkerPost_%sm" % _n] = (lambda n=_n: _marker("SM_Track_MarkerPost_%sm" % n, n))


# ============================================================================= start
@module
def SM_Track_StartGate():
    """Gate over the start line (inner width 5 m).  Lamps are separate child meshes so code can
    toggle them: Lamp_Red_1..3, Lamp_Green (emissive cells); off = disable renderer (dark lens stays)."""
    mb = lr.MeshBuilder(seed=31)
    W, H = 5.0, 3.6
    for sx in (-1, 1):
        x = sx * (W / 2 + 0.2)
        mb.box((x, 0, H / 2), (0.35, 0.35, H), {"top": "steel_blue_light", "side": "steel_blue_base"},
               bevel=0.03)
        mb.box((x, 0, 0.1), (0.6, 0.6, 0.2), "concrete", bevel=0.03)
        for k in range(6):  # hazard stripes on the pillar foot
            mb.box((x, 0, 0.3 + k * 0.16), (0.37, 0.37, 0.08), "hazard_orange" if k % 2 == 0 else "decal_black")
        for dy in (-0.1, 0.1):
            for dx in (-0.2, 0.2):
                bolt(mb, (x + dx, dy + 0.0, 0.2))
    # beam + sign board
    mb.box((0, 0, H + 0.35), (W + 1.1, 0.4, 0.7), {"top": "steel_blue_light", "side": "steel_blue_base",
                                                    "bottom": "steel_blue_dark"}, bevel=0.03)
    fm = viewer_matrix(0.2)
    mb.box((0, 0.205, H + 0.35), (W - 0.2, 0.01, 0.5), "decal_white")
    mb.text("BAŞLANGIÇ", "steel_blue_dark", size=0.36, depth=0.02, font_path=FONT,
            matrix=fm @ Matrix.Translation((0.0, H + 0.35, 0.012)))
    bm = back_matrix(-0.2)
    mb.box((0, -0.205, H + 0.35), (W - 0.2, 0.01, 0.5), "hazard_orange")
    mb.text("LUGE RUSH", "decal_white", size=0.34, depth=0.02, font_path=FONT,
            matrix=bm @ Matrix.Translation((0.0, H + 0.35, 0.012)))
    # countdown light box hanging under the beam
    mb.box((0, 0.05, H - 0.3), (1.6, 0.3, 0.42), "decal_black", bevel=0.03)
    for sx in (-0.5, 0.5):
        mb.cylinder((sx, 0.05, H - 0.09), (sx, 0.05, H), 0.025, "rail_metal_base", segments=6)
    lamps = []
    xs = [0.6, 0.2, -0.2, -0.6]  # Blender +X = rider's left -> lamps light up left to right
    for i, x in enumerate(xs):
        # dark lens (always visible) + visor
        mb.cylinder((x, 0.2, H - 0.3), (x, 0.21, H - 0.3), 0.14, "glass_dark", segments=10)
        mb.box((x, 0.27, H - 0.3 + 0.15), (0.3, 0.14, 0.02), "decal_black")
        lamps.append(x)
    gate = mb.to_object("SM_Track_StartGate")
    emis = lr.palette_material("LR_Palette_Emissive", emissive=True)
    names = ["Lamp_Red_1", "Lamp_Red_2", "Lamp_Red_3", "Lamp_Green"]
    for x, nm in zip(lamps, names):
        lm = lr.MeshBuilder()
        lm.cylinder((x, 0.21, H - 0.3), (x, 0.235, H - 0.3), 0.12, "emit_green" if "Green" in nm else "emit_red",
                    segments=10)
        lr.set_parent(lm.to_object(nm, origin=(x, 0.22, H - 0.3), material=emis), gate)
    return gate


@module
def SM_Track_StartLine():
    """Checkered start line decal (4 m wide x 0.4 m), sits 5 mm above the road."""
    mb = lr.MeshBuilder()
    n, rows = 20, 2
    w = 4.0
    pts = [[(-w / 2 + w * j / n, -0.2 * i, 0.005) for j in range(n + 1)] for i in range(rows + 1)]
    mb.surface(pts, "checker_white", cell_fn=lambda i, j: "checker_black" if (i + j) % 2 else "checker_white")
    return mb.to_object("SM_Track_StartLine")


@module
def SM_Track_GridSlot():
    """White U bracket marking one start slot (0.9 x 1.6 m), pivot = slot centre."""
    mb = lr.MeshBuilder()
    t, z = 0.08, 0.005
    for x0, y0, x1, y1 in ((-0.45, 0.8, 0.45, 0.8 - t), (-0.45, 0.8, -0.45 + t, 0.2), (0.45 - t, 0.8, 0.45, 0.2)):
        mb.poly([(x0, y1, z), (x1, y1, z), (x1, y0, z), (x0, y0, z)], "decal_white")
    return mb.to_object("SM_Track_GridSlot")


# ============================================================================= finish
def checker_flag(mb, pole_base, height, flag_w=1.1, flag_h=0.75, side=1, seed=0):
    """Pole + waving checkered flag (double-sided).  side = which way the flag flies (x sign)."""
    px, py, pz = pole_base
    mb.cylinder((px, py, pz), (px, py, pz + height), 0.04, "galv_light", segments=6)
    mb.sphere((px, py, pz + height + 0.05), 0.06, "chrome_light", segments=6, rings=4)
    mb.box((px, py, pz + 0.15), (0.4, 0.4, 0.3), "concrete", bevel=0.03)
    nu, nv = 6, 4
    rows = []
    for i in range(nv + 1):
        r = []
        for j in range(nu + 1):
            u = j / nu
            x = px + side * u * flag_w
            z = pz + height - 0.05 - flag_h * i / nv - u * 0.08
            y = py + math.sin(u * math.pi * 1.6 + seed) * 0.09 * u
            r.append((x, y, z))
        rows.append(r)
    mb.surface(rows, "checker_white", double_sided=True,
               cell_fn=lambda i, j: "checker_black" if (i + j) % 2 else "checker_white")


@module
def SM_Track_FinishArch():
    """Big green arch over the finish line, 'LUGE RUSH' in geometry on both faces, checkered flags."""
    mb = lr.MeshBuilder(seed=41)
    W = 5.6           # inner width
    H = 4.2           # clearance
    pw = 0.7          # pillar width
    green = {"top": "arch_green_light", "side": "arch_green_base", "bottom": "arch_green_dark"}
    for sx in (-1, 1):
        x = sx * (W / 2 + pw / 2)
        # tapered pillar with plinth and cap
        mb.box((x, 0, H / 2 + 0.2), (pw, 0.8, H + 0.4), green, bevel=0.06)
        mb.box((x, 0, 0.15), (pw + 0.25, 1.0, 0.3), "concrete", bevel=0.04)
        # white vertical racing stripes on the pillar front
        for dx in (-0.12, 0.12):
            mb.box((x + dx, 0.4, 1.75 + (H - 1.75) / 2), (0.07, 0.04, H - 1.75), "decal_white")
        # checkered band on pillar
        for k in range(4):
            for j in range(2):
                c = "checker_black" if (k + j) % 2 else "checker_white"
                mb.box((x - 0.15 + j * 0.3, 0.4, 0.55 + k * 0.3), (0.3, 0.04, 0.3), c)
        # corner gussets (arch shape)
        fs = mb.prism([(x_, -y_) for x_, y_ in [(0, 0), (0, 0.9), (0.9, 0)]], 0.6, "arch_green_dark",
                      matrix=Matrix.Translation((sx * W / 2, 0.3, H)) @ Matrix(
                          ((-sx, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
        mb.shade(fs, "arch_green_base", variation=0.0)
    # top header
    hz = H + 0.8
    mb.box((0, 0, hz), (W + 2 * pw + 0.4, 0.9, 1.6), green, bevel=0.07)
    # roof crown (gives the arch a curved top silhouette)
    crown = []
    for k in range(9):
        a = math.pi * k / 8
        crown.append((math.cos(a) * (W / 2 + pw), hz + 0.8 + math.sin(a) * 0.55))
    crown = [(-(W / 2 + pw), hz + 0.8)] + crown[1:-1] + [((W / 2 + pw), hz + 0.8)]
    crown = list(reversed(crown))
    mb.prism(crown, 0.7, "arch_green_light", matrix=Matrix(((1, 0, 0, 0), (0, 0, 1, -0.35), (0, 1, 0, 0),
                                                           (0, 0, 0, 1))))
    # sign panel faces + letters (front and back)
    for m, yface in ((viewer_matrix(0.46), 0.46), (back_matrix(-0.46), -0.46)):
        mb.box((0, yface, hz + 0.1), (W + 0.4, 0.02, 1.05), "arch_green_dark")
        mb.text("LUGE RUSH", "decal_white", size=0.78, depth=0.06, font_path=FONT, spacing=1.05,
                matrix=m @ Matrix.Translation((0, hz + 0.12, 0.01)))
        # checkered strip under the letters
        n = 22
        for j in range(n):
            for i in range(2):
                c = "checker_black" if (i + j) % 2 else "checker_white"
                cw = (W + 0.4) / n
                p = m @ Vector((-(W + 0.4) / 2 + cw * (j + 0.5), hz - 0.51 - i * 0.18, 0.012))
                mb.box(p, (cw, 0.024, 0.18), c)
    # red banner under the header (front)
    mb.box((0, 0.3, H - 0.15), (W, 0.04, 0.3), "bunting_red", bevel=0.01)
    # finish flags
    checker_flag(mb, (-(W / 2 + pw + 0.9), 0.4, 0), 5.2, side=-1, seed=0.5)
    checker_flag(mb, ((W / 2 + pw + 0.9), 0.4, 0), 5.2, side=1, seed=2.0)
    # bolts on the plinths
    for sx in (-1, 1):
        for dx in (-0.3, 0.3):
            for dy in (-0.35, 0.35):
                bolt(mb, (sx * (W / 2 + pw / 2) + dx, dy, 0.3))
    return mb.to_object("SM_Track_FinishArch")


@module
def SM_Track_FinishLine():
    """Wide checkered finish line decal (5 m x 0.6 m)."""
    mb = lr.MeshBuilder()
    n, rows, w = 20, 3, 5.0
    pts = [[(-w / 2 + w * j / n, -0.2 * i, 0.005) for j in range(n + 1)] for i in range(rows + 1)]
    mb.surface(pts, "checker_white", cell_fn=lambda i, j: "checker_black" if (i + j) % 2 else "checker_white")
    return mb.to_object("SM_Track_FinishLine")


@module
def SM_Track_FinishCanopy_4m():
    """Covered finish area roof module (4 m long, 6 m wide): steel portal frame at the module start,
    purlins, translucent-look roof sheet, and strings of plain red pennants + hanging red flags."""
    mb = lr.MeshBuilder(seed=51)
    W, H, L = 6.0, 3.6, 4.0
    steel = {"top": "galv_light", "side": "rail_metal_base", "bottom": "rail_metal_dark"}
    for sx in (-1, 1):
        x = sx * W / 2
        mb.box((x, -0.1, H / 2), (0.16, 0.16, H), steel, bevel=0.012)
        mb.box((x, -0.1, 0.03), (0.35, 0.35, 0.06), "concrete_dark")
        for dx in (-0.12, 0.12):
            bolt(mb, (x + dx, -0.1 + 0.12, 0.06))
    # gable rafters
    ridge = H + 0.8
    for sx in (-1, 1):
        mb.cylinder((sx * (W / 2 + 0.2), -0.1, H - 0.05), (0, -0.1, ridge), 0.07, "rail_metal_base", segments=4)
        mb.cylinder((sx * W / 2, -0.1, H - 0.4), (sx * W / 4, -0.1, H + 0.35), 0.035, "rail_metal_dark", segments=4)
    mb.box((0, -0.1, H), (W + 0.2, 0.12, 0.14), steel, bevel=0.01)  # tie beam
    # purlins along the module
    for t in (0.0, 0.5, 1.0):
        x = (W / 2 + 0.2) * (1 - t)
        z = H - 0.05 + (ridge - H + 0.05) * t
        for sx in ((-1, 1) if t < 1 else (1,)):
            mb.box((sx * x, -L / 2, z + 0.06), (0.08, L, 0.08), "rail_metal_light")
    # roof sheets (corrugated, double-sided so it reads from below)
    for sx in (-1, 1):
        rows = []
        n = 10
        for i in range(2):
            y = -L * i
            r = []
            for j in range(n + 1):
                t = j / n
                x = sx * (W / 2 + 0.35) * (1 - t)
                z = H - 0.05 + (ridge - H + 0.05) * t + 0.12 + (0.03 if j % 2 else 0.0)
                r.append((x, y, z))
            rows.append(r)
        mb.surface(rows, "galv_light", double_sided=True,
                   cell_fn=lambda i, j: "galv_light" if j % 2 else "rail_metal_light")
    # pennant strings across the width (sag), plus long strings along the length
    rng = random.Random(5)
    for k, y in enumerate((-0.6, -1.6, -2.6, -3.6)):
        n = 14
        pts = []
        for j in range(n + 1):
            t = j / n
            x = -W / 2 + W * t
            z = H - 0.1 - math.sin(t * math.pi) * 0.45
            pts.append(Vector((x, y, z)))
        mb.cylinder(pts[0], pts[n // 2], 0.008, "rope", segments=3, caps=False)
        mb.cylinder(pts[n // 2], pts[-1], 0.008, "rope", segments=3, caps=False)
        for j in range(n):
            a, b = pts[j], pts[j + 1]
            mid = (a + b) / 2 + Vector((0, rng.uniform(-0.04, 0.04), -0.38))
            c = "bunting_red" if (j + k) % 2 == 0 else "bunting_red_dark"
            mb.tri2(a, b, mid, c, lr.darker(c))
    # hanging rectangular plain red flags along the ridge (swallow-tail, no emblem)
    for j in range(4):
        y = -0.5 - j * 1.0
        for sx in (-1, 1):
            x = sx * 1.3
            top = H + 0.45 - abs(x) / (W / 2) * 0.7
            sway = rng.uniform(-0.05, 0.05)
            rows = [[(x - 0.35, y, top), (x + 0.35, y, top)],
                    [(x - 0.35, y + sway, top - 0.6), (x + 0.35, y + sway, top - 0.6)]]
            mb.surface(rows, "bunting_red", double_sided=True)
            mb.tri2((x - 0.35, y + sway, top - 0.6), (x, y + sway, top - 0.6), (x - 0.35, y + sway * 2, top - 0.95),
                    "bunting_red", "bunting_red_dark")
            mb.tri2((x, y + sway, top - 0.6), (x + 0.35, y + sway, top - 0.6), (x + 0.35, y + sway * 2, top - 0.95),
                    "bunting_red", "bunting_red_dark")
            mb.cylinder((x - 0.4, y, top), (x + 0.4, y, top), 0.012, "rail_metal_dark", segments=4)
    return mb.to_object("SM_Track_FinishCanopy_4m")


# ============================================================================= lift
def lift_housing(mb, L, W, H, roof_cell="roof_red"):
    """Open steel shed over the lift lane (start at y=0 running to -L)."""
    for y in (0.0, -L):
        for sx in (-1, 1):
            mb.box((sx * W / 2, y, H / 2), (0.15, 0.15, H), {"top": "steel_blue_light", "side": "steel_blue_base"},
                   bevel=0.012)
            mb.box((sx * W / 2, y, 0.05), (0.32, 0.32, 0.1), "concrete_dark")
    for sx in (-1, 1):
        mb.box((sx * W / 2, -L / 2, H), (0.14, L + 0.15, 0.18), "steel_blue_dark")
    # mono-pitch roof
    rows = [[(-W / 2 - 0.3, 0.4, H + 0.15), (W / 2 + 0.3, 0.4, H + 0.45)],
            [(-W / 2 - 0.3, -L - 0.4, H + 0.15), (W / 2 + 0.3, -L - 0.4, H + 0.45)]]
    mb.surface(rows, roof_cell, double_sided=True)
    # roof edge fascia
    mb.box((W / 2 + 0.3, -L / 2, H + 0.38), (0.05, L + 0.8, 0.16), "roof_red_dark")
    mb.box((-W / 2 - 0.3, -L / 2, H + 0.08), (0.05, L + 0.8, 0.16), "roof_red_dark")


def wheel_assembly(mb, y, r=0.6):
    """Bull wheel beside the sled corridor, in the cable plane (x = CABLE_X, axle across the lane).
    The haul strand leaves its underside at CABLE_Z.  Everything sits on the outer side of the wheel:
    axle cantilevered from one pedestal, plate and guards start outside the corridor."""
    x, s = CABLE_X, -1  # s = outward direction (Blender -X)
    z = CABLE_Z + r
    c = Vector((x, y, z))
    ax = Vector((1, 0, 0))
    mb.cylinder(c - ax * 0.05, c + ax * 0.05, r, "rail_metal_base", segments=16, cap_cell="steel_blue_dark")
    for off in (-0.06, 0.06):  # groove flanges
        mb.cylinder(c + ax * (off - 0.012), c + ax * (off + 0.012), r + 0.03, "rail_metal_light", segments=16,
                    cap_cell="steel_blue_base")
    mb.cylinder(c + ax * s * -0.08, c + ax * s * 0.4, 0.05, "chrome_dark", segments=6)     # axle
    mb.cylinder(c + ax * s * -0.085, c + ax * s * 0.1, r * 0.2, "hazard_orange", segments=8)  # hub
    px = x + s * 0.36
    mb.box((px, y, z / 2), (0.14, 0.5, z), {"top": "steel_blue_light", "side": "steel_blue_base"}, bevel=0.01,
           taper=(0.8, 0.45))
    for gx in (x - s * 0.11, x + s * 0.5):  # yellow guards parallel to the lane
        mb.sweep([(gx, y + 0.8, 0.0), (gx, y + 0.8, 1.0), (gx, y - 0.8, 1.0), (gx, y - 0.8, 0.0)], 0.03,
                 "sign_yellow", sides=6)
    x0, x1 = -LANE_CLEAR - 0.02, x + s * 0.65
    mb.box(((x0 + x1) / 2, y, 0.03), (abs(x1 - x0), 2.0, 0.06), "concrete")


def cable_ends(mb, y_wheel, r=0.6):
    """Haul strand from the cable modules' end to the wheel + return strand into a ground sheave."""
    x = CABLE_X
    mb.cylinder((x, CABLE_END_Y, CABLE_Z), (x, y_wheel, CABLE_Z), 0.012, "rubber_worn", segments=4, caps=False)
    gx = x - 0.13
    mb.cylinder((x, y_wheel, CABLE_Z + 2 * r), (gx, CABLE_END_Y, 0.09), 0.012, "rubber_worn", segments=4, caps=False)
    mb.box((gx, CABLE_END_Y, 0.06), (0.14, 0.3, 0.12), {"top": "hazard_orange", "side": "steel_blue_dark"}, bevel=0.01)


@module
def SM_Track_Lift_StationBottom():
    """Bottom (return / tension) station: bull wheel, counterweight tower, open roof (6 m)."""
    mb = lr.MeshBuilder(seed=61)
    wheel_assembly(mb, -1.2)
    cable_ends(mb, -1.2)
    lift_housing(mb, 6.0, 4.4, 3.2)
    # counterweight tower outside the lane
    x = -3.2
    for dx in (-0.35, 0.35):
        for dy in (-0.35, 0.35):
            mb.box((x + dx, -3.0 + dy, 2.2), (0.1, 0.1, 4.4), "steel_blue_base")
    for z in (1.0, 2.2, 3.4, 4.4):
        mb.box((x, -3.0, z), (0.8, 0.8, 0.08), "steel_blue_dark")
    mb.box((x, -3.0, 1.6), (0.6, 0.6, 1.0), {"top": "concrete", "side": "concrete_dark"}, bevel=0.03)
    mb.box((x, -3.0, 4.55), (0.6, 0.6, 0.2), "hazard_orange", bevel=0.02)
    mb.cylinder((x + 0.3, -3.0, 4.5), (CABLE_X - 0.5, -1.2, CABLE_Z + 0.6), 0.015, "rubber", segments=4)
    # control box + warning stripes
    mb.box((2.6, -0.8, 0.8), (0.5, 0.4, 0.8), {"top": "galv_light", "side": "steel_blue_light"}, bevel=0.02)
    mb.box((2.6, -0.59, 0.95), (0.3, 0.02, 0.2), "glass_dark")
    mb.box((2.6, -0.585, 0.7), (0.06, 0.02, 0.06), "emit_green")
    mb.box((2.6, -0.8, 0.2), (0.1, 0.1, 0.4), "rail_metal_base")
    for k in (0, 1, 3, 4):  # floor stripes either side of the corridor
        mb.box((-2.2 + k * 1.1, -5.8, 0.02), (0.55, 0.3, 0.02), "hazard_orange" if k % 2 == 0 else "decal_black")
    return mb.to_object("SM_Track_Lift_StationBottom")


@module
def SM_Track_Lift_StationTop():
    """Top (drive) station: bull wheel, motor house with gearbox, operator cabin, roof (6 m)."""
    mb = lr.MeshBuilder(seed=62)
    wheel_assembly(mb, -4.6)
    cable_ends(mb, -4.6)
    lift_housing(mb, 6.0, 4.4, 3.4)
    # motor house beside the wheel
    mb.box((-3.0, -4.6, 1.0), (1.6, 2.0, 2.0), {"top": "galv_light", "side": "steel_blue_base"}, bevel=0.04)
    mb.box((-2.18, -4.6, 1.2), (0.04, 1.2, 0.8), "rail_metal_dark")  # vent
    for k in range(5):
        mb.box((-2.15, -4.6, 0.9 + k * 0.15), (0.03, 1.1, 0.04), "rail_metal_light")
    # drive shaft into the wheel axle
    mb.cylinder((-2.2, -4.6, CABLE_Z + 0.6), (CABLE_X - 0.4, -4.6, CABLE_Z + 0.6), 0.1, "hazard_orange", segments=8)
    mb.box((-3.0, -4.6, 2.15), (1.8, 2.2, 0.15), "roof_red_dark", bevel=0.02)
    # operator cabin with windows
    cx, cy = 3.2, -1.0
    mb.box((cx, cy, 1.2), (1.4, 1.4, 2.4), {"top": "galv_light", "side": "checker_white"}, bevel=0.03)
    for (dx, dy, sx, sy) in ((0, 0.71, 1.0, 0.02), (-0.71, 0, 0.02, 1.0)):
        mb.box((cx + dx, cy + dy, 1.6), (sx, sy, 0.7), "glass_dark")
    mb.box((cx, cy, 2.5), (1.7, 1.7, 0.18), "roof_red", bevel=0.03)
    mb.box((cx, cy + 0.72, 0.6), (0.6, 0.02, 1.1), "steel_blue_dark")  # door
    mb.box((cx - 0.3, cy + 0.73, 0.65), (0.06, 0.03, 0.06), "chrome_light")
    # top-of-lift sign: lane exit arrow
    m = viewer_matrix(0.3, z0=0)
    mb.box((0, -6.0, 3.0), (2.4, 0.1, 0.6), "sign_yellow", bevel=0.02)
    mb.text("ÇIKIŞ", "decal_black", size=0.35, depth=0.02, font_path=FONT,
            matrix=viewer_matrix(-5.95) @ Matrix.Translation((0, 3.0, 0)))
    return mb.to_object("SM_Track_Lift_StationTop")


@module
def SM_Track_Lift_CablePost():
    """Low roller post holding the haul cable at 0.25 m (place every 4-8 m in the lane centre)."""
    mb = lr.MeshBuilder(seed=63)
    mb.box((0, 0, 0.04), (0.5, 0.3, 0.08), "concrete", bevel=0.015)
    for sx in (-1, 1):
        mb.box((sx * 0.1, 0, 0.18), (0.03, 0.12, 0.22), "steel_blue_base", bevel=0.005)
    mb.cylinder((-0.1, 0, 0.2), (0.1, 0, 0.2), 0.045, "rubber", segments=8, cap_cell="rubber_worn")
    mb.cylinder((-0.12, 0, 0.2), (0.12, 0, 0.2), 0.012, "chrome_base", segments=6)
    for sx in (-1, 1):
        bolt(mb, (sx * 0.2, 0.08, 0.08))
    return mb.to_object("SM_Track_Lift_CablePost")


@module
def SM_Track_Lift_Cable_4m():
    """Haul cable segment (4 m along -Y) at height 0.25 m (top of the rollers)."""
    mb = lr.MeshBuilder()
    mb.cylinder((0, 0, 0.255), (0, -4, 0.255), 0.012, "rubber_worn", segments=4, caps=False)
    return mb.to_object("SM_Track_Lift_Cable_4m")


@module
def SM_Track_Lift_Hook():
    """Cable grip + arm + hook that latches the sled.  Pivot = grip centre on the cable;
    hook bar top sits ~0.07 m above the pivot (sled chassis bar)."""
    mb = lr.MeshBuilder(seed=64)
    mb.box((0, 0, 0), (0.08, 0.16, 0.06), {"top": "hazard_orange", "side": "hazard_orange_dark"}, bevel=0.01)
    for dy in (-0.05, 0.05):
        bolt(mb, (0.04, dy, 0.0), n=(1, 0, 0), r=0.008)
    pts = [(0, 0.03, 0.03), (0, 0.06, 0.08), (0, 0.06, 0.14), (0, 0.02, 0.18), (0, -0.03, 0.16)]
    mb.sweep(pts, 0.014, "rail_metal_light", sides=6)
    mb.cylinder((0, -0.02, -0.03), (0, 0.02, -0.03), 0.02, "rubber", segments=6)
    return mb.to_object("SM_Track_Lift_Hook")


@module
def SM_Track_Lift_RampRail_4m():
    """Low guard rail for the lift lane: yellow top pipe + kick plate, posts every 2 m."""
    mb = lr.MeshBuilder(seed=65)
    for y in (0.0, -2.0):
        mb.box((0, y, 0.3), (0.06, 0.06, 0.6), "galv_light", bevel=0.006)
        mb.box((0, y, 0.01), (0.14, 0.14, 0.02), "concrete_dark")
    mb.cylinder((0, 0, 0.6), (0, -4, 0.6), 0.03, "sign_yellow", segments=6, caps=False)
    mb.box((0, -2, 0.1), (0.02, 4.0, 0.16), {"side": "hazard_orange", "top": "hazard_orange_dark"})
    return mb.to_object("SM_Track_Lift_RampRail_4m")


# ============================================================================= tunnel + props
@module
def SM_Track_Tunnel_4m():
    """Short covered section: wooden frames + rafters, red metal roof, plank side skirts (4 m, 5 m wide)."""
    mb = lr.MeshBuilder(seed=71)
    W, H, L = 5.2, 2.8, 4.0
    wood = "wood_base"
    for y in (-1.0, -3.0):
        for sx in (-1, 1):
            f = mb.box((sx * W / 2, y, H / 2), (0.2, 0.2, H), wood, bevel=0.015)
            mb.shade(f, wood, variation=0.3)
            mb.jitter(f, 0.012)
            mb.box((sx * W / 2, y, 0.06), (0.32, 0.32, 0.12), "concrete_dark")
            mb.box((sx * (W / 2 - 0.35), y, H - 0.32), (0.6, 0.12, 0.12), "wood_dark",
                   rot=Euler((0, sx * math.radians(45), 0)))  # knee brace
        mb.box((0, y, H + 0.05), (W + 0.3, 0.18, 0.2), wood, bevel=0.012)
        for sx in (-1, 1):  # rafters
            mb.cylinder((sx * (W / 2 + 0.35), y, H + 0.05), (0, y, H + 1.1), 0.08, "wood_dark", segments=4)
    # ridge + purlins
    for t in (0.0, 0.5, 1.0):
        for sx in ((-1, 1) if t < 1 else (1,)):
            x = sx * (W / 2 + 0.35) * (1 - t)
            z = H + 0.12 + 1.05 * t
            f = mb.box((x, -L / 2, z + 0.08), (0.1, L, 0.1), "wood_dark")
            mb.jitter(f, 0.01)
    # corrugated metal roof
    for sx in (-1, 1):
        n = 12
        rows = []
        for y in (0.0, -L):
            r = []
            for j in range(n + 1):
                t = j / n
                x = sx * (W / 2 + 0.55) * (1 - t)
                z = H + 0.25 + 1.05 * t * (W / 2 + 0.35) / (W / 2 + 0.55) + (0.035 if j % 2 else 0)
                r.append((x, y, z))
            rows.append(r)
        mb.surface(rows, "roof_red", double_sided=True, cell_fn=lambda i, j: "roof_red" if j % 2 else "roof_red_dark")
    # plank skirts (lower walls, gaps between planks)
    rng = random.Random(3)
    for sx in (-1, 1):
        for k in range(4):
            z = 0.15 + k * 0.24
            f = mb.box((sx * (W / 2 + 0.12), -L / 2, z), (0.04, L, 0.2), wood, bevel=0.006)
            mb.shade(f, rng.choice(["wood_base", "wood_light", "wood_dark"]), variation=0.2)
            mb.jitter(f, 0.008)
    # small lamp under the ridge
    mb.box((0, -L / 2, H + 0.65), (0.25, 0.25, 0.1), "rail_metal_dark")
    mb.box((0, -L / 2, H + 0.58), (0.2, 0.2, 0.04), "lamp_glass")
    return mb.to_object("SM_Track_Tunnel_4m")


@module
def SM_Track_LightPole():
    """Trackside light pole (5 m) with an arm reaching over the track (towards Blender +X)."""
    mb = lr.MeshBuilder(seed=81)
    mb.box((0, 0, 0.1), (0.4, 0.4, 0.2), "concrete", bevel=0.03)
    for dx in (-0.12, 0.12):
        for dy in (-0.12, 0.12):
            bolt(mb, (dx, dy, 0.2))
    mb.cylinder((0, 0, 0.2), (0, 0, 5.0), 0.07, "rail_metal_base", radius2=0.05, segments=8)
    mb.sweep([(0, 0, 4.7), (0.3, 0, 5.05), (1.1, 0, 5.15)], 0.04, "rail_metal_light", sides=6)
    mb.box((1.2, 0, 5.1), (0.5, 0.22, 0.12), {"top": "rail_metal_light", "side": "rail_metal_dark"}, bevel=0.02)
    mb.box((1.2, 0, 5.03), (0.4, 0.16, 0.03), "lamp_glass")
    mb.box((0, 0.08, 1.4), (0.14, 0.04, 0.3), "steel_blue_base", bevel=0.01)  # access hatch
    return mb.to_object("SM_Track_LightPole")


@module
def SM_Track_TrashBin():
    mb = lr.MeshBuilder(seed=82)
    f = mb.cylinder((0, 0, 0.05), (0, 0, 0.85), 0.27, "bin_green_base", radius2=0.3, segments=10)
    mb.shade(f, "bin_green_base", variation=0.3)
    mb.cylinder((0, 0, 0.85), (0, 0, 0.95), 0.33, "bin_green_dark", radius2=0.3, segments=10)
    mb.cylinder((0, 0, 0.95), (0, 0, 1.0), 0.18, "bin_green_light", segments=10)
    mb.box((0, 0.3, 0.6), (0.24, 0.02, 0.12), "decal_white")
    mb.cylinder((0, 0, 0.0), (0, 0, 0.05), 0.25, "rail_metal_dark", segments=10)
    for a in range(3):
        ang = a * 2 * math.pi / 3
        mb.box((math.cos(ang) * 0.29, math.sin(ang) * 0.29, 0.45), (0.03, 0.03, 0.8), "bin_green_dark",
               rot=Euler((0, 0, ang)))
    return mb.to_object("SM_Track_TrashBin")


def decor_sled(mb, base, yaw, colour, wheels=False):
    """Low-poly sled from the same body generator as SM_Sled_* (spec_decor), real colour cells."""
    import sleds_build as sb
    sp = sb.spec_decor()
    S = sp["stations"]
    before = set(mb.bm.faces)
    sb.build_body(mb, sp)
    fam = {"tint_white": colour + "_light", "tint_light": colour + "_base", "tint_mid": colour + "_dark",
           "tint_dark": colour + "_deep"}
    for f in mb.bm.faces:
        if f not in before:
            n = lr.cell_name(f[mb.pal])
            if n in fam:
                f[mb.pal] = lr.cell_index(fam[n])
    sb.skirt(mb, S)
    r, z = sp["bumper_r"], sp["bumper_z"]
    mb.sweep(sb.bumper_path(S, [3, 4], S[-1][0] - r * 0.6, z, off=0.01), r, "rubber", sides=4)
    mb.sweep(sb.bumper_path(S, [1, 0], S[0][0] + r * 0.6, z, off=0.01), r * 1.1, "rubber", sides=4)
    if wheels:
        for x, y in ((0.3, -0.48), (-0.3, -0.48), (0.3, 0.55), (-0.3, 0.55)):
            mb.cylinder((x - 0.022, y, 0.05), (x + 0.022, y, 0.05), 0.05, "rubber", segments=6, cap_cell="chrome_base")
    new = [f for f in mb.bm.faces if f not in before]
    mb.transform(new, Matrix.Translation(base) @ Matrix.Rotation(yaw, 4, "Z"))
    return new


@module
def SM_Track_SledStack():
    """Decor: sleds waiting at the bottom - a nested stack of 4 on a pallet + 2 parked beside it."""
    mb = lr.MeshBuilder(seed=91)
    rng = random.Random(9)
    cols = ["sled_orange", "sled_teal", "sled_red", "sled_yellow", "sled_orange", "sled_teal"]
    for k in range(4):
        decor_sled(mb, Vector((rng.uniform(-0.02, 0.02), rng.uniform(-0.03, 0.03), 0.03 + k * 0.17)),
                   rng.uniform(-0.04, 0.04), cols[k])
    for k, x in enumerate((1.0, 1.95)):
        decor_sled(mb, Vector((x, 0.1 * k, 0.0)), rng.uniform(-0.12, 0.12), cols[4 + k], wheels=True)
    # wooden pallet under the stack
    for dx in (-0.3, 0.0, 0.3):
        mb.box((dx, 0, 0.05), (0.1, 1.5, 0.03), "wood_base")
    for k in range(3):
        mb.box((0, -0.6 + k * 0.6, 0.02), (0.8, 0.12, 0.04), "wood_dark")
    return mb.to_object("SM_Track_SledStack")


# ============================================================================= run
def build_all():
    lr.reset_scene()
    objs = {}
    for name, fn in MODULES.items():
        ob = fn()
        objs[name] = ob
    return objs


def export_all(objs, only=None):
    out_dir = lr.unity_model_dir("Track")
    rep = []
    for name, ob in objs.items():
        h = lr.hierarchy(ob)
        if only is None or name in only:
            lr.export_fbx(h, os.path.join(out_dir, name + ".fbx"))
        rep.append((name, lr.tri_count(h)))
    return rep


def place(ob, loc, yaw_deg=0.0):
    ob.location = Vector(loc)
    ob.rotation_euler = Euler((0, 0, math.radians(yaw_deg)))


def road(length, width, y0=0.0, name="PreviewRoad"):
    mb = lr.MeshBuilder(seed=2)
    rng = random.Random(1)
    n = int(length / 2)
    rows = [[(-width / 2 + width * j / 4, y0 - 2 * i, 0.0) for j in range(5)] for i in range(n + 1)]
    mb.surface(rows, "asphalt_base",
               cell_fn=lambda i, j: rng.choice(["asphalt_base"] * 5 + ["asphalt_dark", "asphalt_wet"]))
    return mb.to_object(name)


def ground_plane(size, name="PreviewGrass"):
    mb = lr.MeshBuilder(seed=3)
    mb.poly([(-size, -size, -0.02), (size, -size, -0.02), (size, size, -0.02), (-size, size, -0.02)], "leaf_mid_base")
    return mb.to_object(name)


def copy(ob, loc, yaw=0.0):
    root, objs = lr.duplicate_hierarchy(ob, suffix=".c%d" % random.randint(0, 1 << 30))
    place(root, loc, yaw)
    return root


def render_kit(objs):
    # hide originals (they stay at the origin for export) by moving them far away
    for o in objs.values():
        o.location = (1000, 1000, -1000)
    ren = os.path.join(lr.ART, "Renders")
    shown = []
    # --- scene 1: a stretch of track with the full edge kit, signs, tunnel, props, start gate
    L = 24.0
    shown.append(road(L, 4.0, 0.0))
    for y in range(0, int(L), 2):
        shown.append(copy(objs["SM_Track_Curb_2m"], (2.15, -y, 0), 180))
        shown.append(copy(objs["SM_Track_Curb_2m"], (-2.15, -y - 2, 0), 0))
    for y in range(0, 12, 4):
        shown.append(copy(objs["SM_Track_Rail_4m"], (-2.6, -y, 0)))
    shown.append(copy(objs["SM_Track_Rail_End"], (-2.6, -12.0, 0), 0))
    for y in range(0, 12, 4):
        shown.append(copy(objs["SM_Track_Net_4m"], (2.7, -y - 4, 0), 180))
    shown.append(copy(objs["SM_Track_Tunnel_4m"], (0, -14.0, 0)))
    shown.append(copy(objs["SM_Track_Tunnel_4m"], (0, -18.0, 0)))
    shown.append(copy(objs["SM_Track_StartGate"], (0, -3.0, 0)))
    shown.append(copy(objs["SM_Track_StartLine"], (0, -3.0, 0)))
    for k in range(4):
        shown.append(copy(objs["SM_Track_GridSlot"], (-1.0 + 2.0 * (k % 2), -0.2 + 2.0 * (k // 2) + 0.5, 0)))
    shown.append(copy(objs["SM_Track_Sign_CurveLeft"], (3.4, -8.0, 0)))
    shown.append(copy(objs["SM_Track_Sign_Warning"], (-3.3, -13.0, 0)))
    shown.append(copy(objs["SM_Track_LightPole"], (-3.4, -6.5, 0), 0))
    shown.append(copy(objs["SM_Track_TrashBin"], (-3.6, 2.0, 0)))
    shown.append(copy(objs["SM_Track_SledStack"], (4.6, 2.5, 0), 20))
    shown.append(copy(objs["SM_Track_MarkerPost_02"], (3.4, -11.0, 0)))
    frame = [o for o in shown if o.name.startswith("SM_") or o.name.startswith("Preview")]
    frame_objs = []
    for o in frame:
        if not o.name.startswith("PreviewGrass"):
            frame_objs += lr.hierarchy(o)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_kit.png"), size=1024, bg="#BFD8E6",
                             view=(1.1, 1.3, 0.9), aspect=4 / 3, margin=1.02,
                             sun_dir=(-0.6, -0.4, -1.0))
    lr.cleanup(made)
    for o in shown:
        lr.cleanup(lr.hierarchy(o))

    # --- scene 2: finish arch + canopy
    shown = [road(20, 5.0, 4.0, "PreviewRoad2"), road(0.1, 0.1, 50, "PreviewDummy")]
    shown.append(copy(objs["SM_Track_FinishArch"], (0, 0, 0)))
    shown.append(copy(objs["SM_Track_FinishLine"], (0, 0.3, 0)))
    for k in range(3):
        shown.append(copy(objs["SM_Track_FinishCanopy_4m"], (0, -3.0 - 4 * k, 0)))
    for y in range(-2, 16, 2):
        shown.append(copy(objs["SM_Track_Curb_2m"], (2.65, -y + 4, 0), 180))
        shown.append(copy(objs["SM_Track_Curb_2m"], (-2.65, -y + 2, 0), 0))
    frame_objs = []
    for o in shown[2:4]:
        frame_objs += lr.hierarchy(o)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_finish.png"), size=1024, bg="#BFD8E6",
                             view=(0.55, 1.4, 0.45), aspect=4 / 3, margin=1.05,
                             sun_dir=(-0.5, -0.6, -1.0))
    lr.cleanup(made)
    for o in shown:
        lr.cleanup(lr.hierarchy(o))

    # --- scene 3: lift
    shown = [road(0.1, 0.1, 50, "PreviewDummy3")]
    # same layout as TrackSceneBuilder.BuildArtLift: lift from y = 2 to y = -24
    lane = road(26, 3.0, 2.0, "PreviewLiftLane")
    shown.append(lane)
    shown.append(copy(objs["SM_Track_Lift_StationBottom"], (0, 2.0, 0)))
    shown.append(copy(objs["SM_Track_Lift_StationTop"], (0, -18.0, 0)))
    for y in range(-4, -17, -6):
        shown.append(copy(objs["SM_Track_Lift_CablePost"], (CABLE_X, y, 0)))
    for y in range(-1, -21, -4):
        shown.append(copy(objs["SM_Track_Lift_Cable_4m"], (CABLE_X, y, 0)))
    for y in range(-4, -16, -4):
        shown.append(copy(objs["SM_Track_Lift_RampRail_4m"], (1.55, y, 0)))
        shown.append(copy(objs["SM_Track_Lift_RampRail_4m"], (-1.55, y, 0)))
    shown.append(copy(objs["SM_Track_Lift_Hook"], (CABLE_X, -8.2, CABLE_Z)))
    frame_objs = []
    for o in shown[1:]:
        frame_objs += lr.hierarchy(o)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_lift.png"), size=1024, bg="#BFD8E6",
                             view=(1.3, 0.9, 0.8), aspect=4 / 3, margin=1.0)
    lr.cleanup(made)
    # lane-level view into the top station: the sled corridor must be empty
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_lift_lane.png"), size=1024, bg="#BFD8E6",
                             aspect=4 / 3, lens=22, cam_loc=(0.35, -13.5, 0.95), target=(-0.25, -24.0, 0.6))
    lr.cleanup(made)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_lift_wheel.png"), size=1024, bg="#BFD8E6",
                             aspect=4 / 3, lens=24, cam_loc=(1.5, -2.6, 1.5), target=(-0.75, 0.8, 0.8))
    lr.cleanup(made)
    for o in shown:
        lr.cleanup(lr.hierarchy(o))

    # --- scene 3b: inside the covered finish area
    shown = [road(16, 5.0, 2.0, "PreviewRoad4")]
    for k in range(4):
        shown.append(copy(objs["SM_Track_FinishCanopy_4m"], (0, -4 * k, 0)))
    for y in range(-2, 16, 2):
        shown.append(copy(objs["SM_Track_Curb_2m"], (2.65, -y, 0), 180))
        shown.append(copy(objs["SM_Track_Curb_2m"], (-2.65, -y - 2, 0), 0))
    shown.append(copy(objs["SM_Track_SledStack"], (-4.2, -9.0, 0), 80))
    shown.append(copy(objs["SM_Track_TrashBin"], (3.8, -6.0, 0)))
    frame_objs = []
    for o in shown[1:5]:
        frame_objs += lr.hierarchy(o)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_canopy.png"), size=1024, bg="#BFD8E6",
                             aspect=4 / 3, lens=24, target=(0.3, -10.0, 2.6), cam_loc=(-1.2, 1.5, 1.6))
    lr.cleanup(made)
    for o in shown:
        lr.cleanup(lr.hierarchy(o))

    # --- scene 4: catalogue of props / signs in a row
    row = ["SM_Track_Sign_CurveLeft", "SM_Track_Sign_CurveRight", "SM_Track_Sign_Slow", "SM_Track_Sign_Warning",
           "SM_Track_Sign_Stop", "SM_Track_MarkerPost_01", "SM_Track_MarkerPost_100m", "SM_Track_TrashBin",
           "SM_Track_Lift_Hook", "SM_Track_Lift_CablePost", "SM_Track_Rail_End"]
    shown = []
    for k, n in enumerate(row):
        shown.append(copy(objs[n], (-k * 1.0, 0, 0)))
    shown.append(copy(objs["SM_Track_Net_4m"], (-15.0, 0.0, 0), 90))
    shown.append(copy(objs["SM_Track_SledStack"], (-16.2, 0, 0), 180))
    frame_objs = []
    for o in shown:
        frame_objs += lr.hierarchy(o)
    made = lr.render_preview(frame_objs, os.path.join(ren, "track_props.png"), size=1024, bg="#C9DCE6",
                             view=(-0.25, 1.0, 0.25), aspect=2.0, margin=1.0)
    lr.cleanup(made)
    for o in shown:
        lr.cleanup(lr.hierarchy(o))
    for o in objs.values():
        o.location = (0, 0, 0)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    objs = build_all()
    # lay the kit out in a grid in the .blend for browsing (exported at the origin first)
    only = None
    if "--only" in argv:
        only = set(argv[argv.index("--only") + 1].split(","))
        unknown = only - set(MODULES)
        assert not unknown, "unknown modules: %s" % sorted(unknown)
    rep = export_all(objs, only)
    for i, (name, ob) in enumerate(objs.items()):
        ob.location = (0, 0, 0)
    if "--no-render" not in argv:
        render_kit(objs)
    for i, (name, ob) in enumerate(objs.items()):
        ob.location = ((i % 6) * 8.0, -(i // 6) * 9.0, 0)
    lr.save_blend(os.path.join(lr.ART, "Blender", "Track_Kit.blend"))
    for name, t in rep:
        print("SUMMARY %-34s tris=%d" % (name, t))
