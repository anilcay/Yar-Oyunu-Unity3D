"""Luge Rush - builds the 4 sled models (Classic / Sport / Tandem / Mini).

Run:  blender -b -P Art/Scripts/sleds_build.py            (all sleds)
      blender -b -P Art/Scripts/sleds_build.py -- Sport   (one sled)

Per sled outputs
- Game/Assets/_LugeRush/Art/Models/Sleds/SM_Sled_<Type>.fbx
- Art/Blender/Sleds_<Type>.blend
- Art/Renders/sled_<type>.png   (4 tint variants: orange / red / yellow / teal)
- Art/Renders/sled_<type>_hero.png, sled_<type>_with_rider(_back).png, sled_<type>_fp.png
  (SK_Rider in Sit_Idle on the Seat; first-person view from Camera_FP)

Hierarchy (Blender front = -Y = Unity +Z, pivot = ground contact centre).
The organising root empty is NOT written to the FBX (drop_root, see lr_common.export_fbx);
Unity recreates it from the file name:
  SM_Sled_<Type> (empty root)
    Body        tub, tint_* cells only, material "LR_Palette_Tint" -> recolour via _BaseColor
    Trim        decals, screws, bumpers, number panel (fixed colours, material "LR_Palette")
    Chassis     dark skirt + frame, axles, brake pad
    Handlebar   pivot = column base on the vertical steering axis (rotate local up axis to steer)
      Grip.L, Grip.R   on the axis line, Seat + (+-0.20, 0.55 fwd, 0.20 up)  (rider contract)
    Wheel_FL/FR/RL/RR   pivot = axle centre, axle along Unity X
    Seat, Camera_FP, (Seat2, Grip2.L, Grip2.R on Tandem)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

import lr_common as lr  # noqa: E402

FONT = r"C:\Windows\Fonts\ariblk.ttf"
TINTS = [("orange", "#F2792B"), ("red", "#D93A3A"), ("yellow", "#F2C230"), ("teal", "#3BB8C4")]


# ----------------------------------------------------------------------------- tub geometry
def ring(y, w, wb, zb, zt, rt, rb, nt=2, nb=2):
    """Closed cross-section (x, y, z) of the tub at station y.  w = half width at the rim,
    wb = half width at the bottom (flare), rt / rb = top / bottom corner radius."""
    rt = min(rt, w * 0.9, (zt - zb) * 0.45)
    rb = min(rb, wb * 0.9, (zt - zb) * 0.45)
    right = [(w - rt, zt)]
    for k in range(1, nt + 1):
        a = math.pi / 2 * (1 - k / nt)
        right.append((w - rt + rt * math.cos(a), zt - rt + rt * math.sin(a)))
    for k in range(0, nb + 1):
        a = -math.pi / 2 * k / nb
        right.append((wb - rb + rb * math.cos(a), zb + rb + rb * math.sin(a)))
    left = [(-x, z) for x, z in reversed(right)]
    return [(x, y, z) for x, z in right + left]


def side_point(rg, seg, t):
    a, b = Vector(rg[seg]), Vector(rg[(seg + 1) % len(rg)])
    return a.lerp(b, t)


def build_tub(mb, stations, cockpits, nt, nb, floor_z, rim, backrest_slope=0.14, front_slope=0.04):
    """stations: list of (y, w, wb, zb, zt, rt, rb) ordered rear -> front.
    cockpits: list of (i0, i1) station ranges hollowed out as seat wells."""
    rings = [ring(*s, nt=nt, nb=nb) for s in stations]
    grid, caps = mb.loft(rings, "tint_light")
    body_faces = [f for r in grid for f in r] + caps
    mb.shade(body_faces, "tint_light", variation=0.12)
    n = len(rings[0])
    top_seg = n - 1
    rim_faces, wells = [], []
    for i0, i1 in cockpits:
        region = [grid[i][top_seg] for i in range(i0, i1)]
        res = bmesh.ops.inset_region(mb.bm, faces=region, thickness=rim, depth=0.0, use_even_offset=True)
        rim_faces += res["faces"]
        ext = bmesh.ops.extrude_face_region(mb.bm, geom=region)
        nv = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
        nf = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMFace)]
        ys = [v.co.y for v in nv]
        ymax, ymin = max(ys), min(ys)
        for v in nv:
            v.co.z = floor_z
            if v.co.y > ymax - 1e-4:
                v.co.y -= backrest_slope      # reclined backrest
            elif v.co.y < ymin + 1e-4:
                v.co.y += front_slope
        bmesh.ops.delete(mb.bm, geom=region, context="FACES")
        walls = list({f for v in nv for f in v.link_faces if f not in nf})
        wells.append((nf, walls))
    bmesh.ops.recalc_face_normals(mb.bm, faces=list(mb.bm.faces))
    for f in mb.bm.faces:
        f.normal_update()
    mb.paint(rim_faces, "tint_white")
    for nf, walls in wells:
        mb.paint(nf, "tint_dark")
        mb.shade(walls, "tint_mid", light_up=False, variation=0.2)
    return rings


def stripe(mb, rings, i0, i1, seg, t0, t1, cell, both=True, off=0.004):
    """Decal band on the outer tub wall between stations i0..i1 on ring segment `seg`."""
    faces = []
    for sign in ((1, -1) if both else (1,)):
        for i in range(i0, i1):
            a0, a1 = side_point(rings[i], seg, t0), side_point(rings[i], seg, t1)
            b0, b1 = side_point(rings[i + 1], seg, t0), side_point(rings[i + 1], seg, t1)
            pts = []
            for p in (a0, b0, b1, a1):
                q = Vector((p.x * sign, p.y, p.z))
                q.x += off * sign
                pts.append(q)
            if sign < 0:
                pts.reverse()
            fs = mb.poly(pts, cell)
            # face outward
            for f in fs:
                f.normal_update()
                if f.normal.x * sign < 0:
                    f.normal_flip()
            faces += fs
    return faces


def waist_rail(mb, rings, seg, t, cell="chassis_dark", r=0.016, off=0.008):
    """Closed rub rail around the tub at height parameter t of the side wall segment."""
    right = [side_point(rg, seg, t) for rg in rings]
    yc = sum(p.y for p in right) / len(right)
    loop = right + [Vector((-p.x, p.y, p.z)) for p in reversed(right)]
    out = []
    for p in loop:
        d = Vector((p.x, (p.y - yc) * 0.15, 0)).normalized()
        out.append(p + d * off)
    return mb.sweep(out, r, cell, sides=4, closed=True, scale=(1.0, 0.8))


def screw(mb, pos, normal, r=0.011, cell="chrome_light"):
    p = Vector(pos)
    nrm = Vector(normal).normalized()
    return mb.cylinder(p - nrm * 0.002, p + nrm * 0.006, r, cell, segments=6, cap_cell="chrome_base")


# ----------------------------------------------------------------------------- shared parts
def wheel(name, pos, r, width, parent, rim_cell="chrome_base"):
    mb = lr.MeshBuilder()
    x, y, z = pos
    hw = width / 2
    mb.cylinder((x - hw, y, z), (x + hw, y, z), r, "rubber", segments=10, cap_cell="rubber_worn",
                rot_offset=math.pi / 10)
    # tread chamfer ring + hub
    s = 1 if x > 0 else -1
    mb.cylinder((x + s * hw, y, z), (x + s * (hw + 0.008), y, z), r * 0.55, rim_cell, segments=8,
                cap_cell="chrome_light")
    mb.cylinder((x + s * (hw + 0.008), y, z), (x + s * (hw + 0.016), y, z), r * 0.2, "chassis_dark", segments=6)
    return mb.to_object(name, origin=pos, parent=parent)


def chassis(mb, length_f, length_r, half_w, wheel_y, wheel_x, wheel_r, z_rail):
    """Dark grey frame: two rails, cross members, axle beams, wheel forks, rear brake pad."""
    for sx in (-1, 1):
        mb.box((sx * half_w, (length_f + length_r) / 2, z_rail), (0.035, length_r - length_f, 0.03),
               "chassis_base")
    for yy in (length_f + 0.06, length_r - 0.06):
        mb.box((0, yy, z_rail + 0.005), (half_w * 2, 0.03, 0.022), "chassis_dark")
    for wy in wheel_y:
        mb.cylinder((-wheel_x, wy, wheel_r), (wheel_x, wy, wheel_r), 0.012, "chrome_dark", segments=6)
        for sx in (-1, 1):
            # fork plate from rail down to axle
            x = sx * (wheel_x - 0.04)
            mb.box((x, wy, (wheel_r + z_rail) / 2), (0.012, 0.07, z_rail - wheel_r + 0.04), "chassis_light")
            mb.cylinder((x - 0.01 * sx, wy, wheel_r), (x + 0.012 * sx, wy, wheel_r), 0.016, "chrome_base",
                        segments=6)
    # brake pad block behind the rear axle
    mb.box((0, length_r - 0.02, z_rail - 0.03), (0.22, 0.05, 0.03), "rubber", bevel=0.005)


def nose_sticker(mb, y, z, w, cell):
    """Chevron decal on the nose top (Unity forward)."""
    pts = [(-w, y + 0.06, z), (0, y, z), (w, y + 0.06, z), (w, y + 0.09, z), (0, y + 0.03, z), (-w, y + 0.09, z)]
    fs = mb.poly([Vector(p) for p in pts], cell)
    for f in fs:
        if f.normal.z < 0:
            f.normal_flip()
    return fs


# ----------------------------------------------------------------------------- rider contract
# Relative to the Seat empty (= rider hip joint, Hips bone origin).  Blender axes, front = -Y.
# Source: Art/ANIMATION.md + Art/Scripts/rider_anim.py (GRIP_PIVOT, GRIP_HALF, GROUND_Z).
SEAT_Z = 0.25            # hip joint above ground
GRIP_FWD = 0.55          # handlebar centre in front of the hip
GRIP_UP = 0.20           # handlebar centre above the hip  -> 0.45 m above ground
GRIP_HALF = 0.20         # Grip.L/R = +-0.20 m
SOLE_FWD = 0.88          # foot plate (inner front wall) in front of the hip (soles at ~0.83-0.85)
EYE = (0.0, -0.08, 0.68)  # first-person eye point rel. Seat (Head bone + offset in Sit_Idle)
FLOOR_Z = 0.13           # cockpit floor; seat pad top = 0.155 (hip surface ~0.10 under the joint)


def st(y, w, zt, zb=0.10, flare=0.055, rt=0.055, rb=0.045):
    """Body station shorthand -> (y, w, wb, zb, zt, rt, rb)."""
    return (y, w, w - flare, zb, zt, rt, rb)


def seat_shell(mb, sy, rx=0.24, ry=0.17, top_c=0.66, top_s=0.38, recline=0.07, thick=0.05, n=13,
               a0=-28.0, a1=208.0):
    """Moulded seat shell: a U-shaped wall wrapping the hips (side bolsters low, backrest high and
    reclined).  Returns the top-centre point of the backrest."""
    rings = []
    top_center = None
    for k in range(n):
        a = math.radians(a0 + (a1 - a0) * k / (n - 1))
        s = max(0.0, math.sin(a))
        top = top_s + (top_c - top_s) * s ** 1.6
        d = Vector((math.cos(a) * ry, math.sin(a) * rx, 0)).normalized()  # ellipse normal
        pin = Vector((rx * math.cos(a), sy + ry * math.sin(a), FLOOR_Z))
        pout = pin + d * thick
        rec = recline * s * (top - FLOOR_Z) / (top_c - FLOOR_Z)
        mid = (pin + pout) / 2
        rings.append([(pin.x, pin.y, FLOOR_Z), (pin.x, pin.y + rec, top - 0.012),
                      (mid.x, mid.y + rec, top + 0.008), (pout.x, pout.y + rec, top - 0.012),
                      (pout.x, pout.y, FLOOR_Z - 0.02)])
        if abs(a - math.pi / 2) < 1e-3 or (top_center is None and k == n // 2):
            top_center = Vector((mid.x, mid.y + rec, top + 0.008))
    grid, caps = mb.loft(rings, "tint_light")
    for row in grid:
        mb.shade([row[0]], "tint_mid", light_up=False, variation=0.15)   # inner face
        mb.paint([row[1], row[2]], "tint_white")                          # rounded top lip
        mb.shade([row[3]], "tint_light", variation=0.15)                  # outer face
        mb.paint([row[4]], "tint_dark")
    mb.paint(caps, "tint_light")
    return top_center


def cockpit_parts(mb, sy, front_y, console_h=0.21):
    """Seat pad with thigh bolsters + centre console (leg divider) from the seat to the foot wall."""
    mb.box((0, sy + 0.03, FLOOR_Z + 0.012), (0.40, 0.30, 0.025), {"top": "tint_light", "side": "tint_mid"},
           bevel=0.01)
    for sx in (-1, 1):
        mb.box((sx * 0.25, sy - 0.22, FLOOR_Z + 0.03), (0.07, 0.42, 0.06), {"top": "tint_white", "side": "tint_mid"},
               bevel=0.012, taper=(0.6, 0.95))
    y0, y1 = sy - 0.18, front_y
    mb.box((0, (y0 + y1) / 2, (FLOOR_Z + console_h) / 2), (0.13, abs(y1 - y0), console_h - FLOOR_Z),
           {"top": "tint_light", "side": "tint_mid"}, bevel=0.014, taper=(0.65, 1.0))


def bumper_path(stations, idxs, tip_y, z, off=0.015):
    """Open path around the front (or rear) of the body through stations idxs (ordered towards the tip)."""
    right = [Vector((stations[i][1] + off, stations[i][0], z)) for i in idxs]
    left = [Vector((-p.x, p.y, p.z)) for p in reversed(right)]
    return left + [Vector((0, tip_y, z))] + right[::-1][::-1][::-1] if False else \
        left + [Vector((0, tip_y, z))] + list(reversed(right))


def handlebar(name, pivot, parent, bar_z=SEAT_Z + GRIP_UP, half=GRIP_HALF, mb=None):
    """Steering handlebar: vertical column on the steering axis + straight cross bar with grips.
    Grip centres lie ON the axis line (rider contract), so steering only rotates the hands."""
    own = mb is None
    mb = mb or lr.MeshBuilder()
    px, py, pz = pivot
    mb.cylinder((px, py, pz - 0.02), (px, py, bar_z - 0.02), 0.017, "chrome_base", segments=6)
    mb.cylinder((px, py, pz - 0.02), (px, py, pz + 0.06), 0.028, "rubber", segments=6)       # boot
    mb.box((px, py + 0.005, bar_z - 0.012), (0.075, 0.05, 0.045), "chassis_light", bevel=0.006)  # clamp
    pts = [(-half - 0.075, py + 0.012, bar_z - 0.02), (-half + 0.06, py, bar_z), (half - 0.06, py, bar_z),
           (half + 0.075, py + 0.012, bar_z - 0.02)]
    mb.sweep(pts, 0.012, "chrome_light", sides=6)
    grips = []
    for s in (1, -1):
        c = Vector((s * half, py + 0.003, bar_z - 0.003))
        mb.cylinder(c - Vector((s * 0.055, 0, 0)), c + Vector((s * 0.055, 0, 0)), 0.02, "rubber", segments=6,
                    cap_cell="rubber_worn")
        mb.box(c + Vector((-s * 0.02, -0.04, -0.012)), (0.08, 0.008, 0.006), "chassis_light")  # brake lever
        grips.append(Vector((s * half, py, bar_z)))
    if not own:
        return None, grips
    ob = mb.to_object(name, origin=pivot, parent=parent)
    lr.empty("Grip.L", grips[0], parent=ob)
    lr.empty("Grip.R", grips[1], parent=ob)
    return ob, grips


# ----------------------------------------------------------------------------- sled specs
def spec_classic():
    sy = 0.30
    S = [st(0.70, 0.28, 0.27, zb=0.13), st(0.645, 0.355, 0.29, zb=0.11), st(0.50, 0.375, 0.30),
         st(0.25, 0.375, 0.29), st(0.0, 0.365, 0.275), st(-0.25, 0.345, 0.265), st(-0.45, 0.32, 0.26),
         st(sy - SOLE_FWD - 0.045, 0.28, 0.255), st(-0.69, 0.22, 0.245, zb=0.12),
         st(-0.725, 0.13, 0.225, zb=0.13, rt=0.04, rb=0.03)]
    return dict(name="Classic", stations=S, cockpits=[(1, 7)], rim=0.045, seats=[sy],
                shells=[dict(rx=0.24, ry=0.17, top_c=0.66, top_s=0.38, recline=0.07)],
                wheel_r=0.05, wheel_w=0.045, wheel_x=0.30, wheel_y=(-0.48, 0.55),
                bumper_r=0.035, bumper_z=0.165)


def spec_sport():
    sy = 0.42
    S = [st(0.84, 0.26, 0.25, zb=0.12), st(0.78, 0.34, 0.265, zb=0.10), st(0.62, 0.36, 0.27),
         st(0.35, 0.36, 0.26), st(0.05, 0.35, 0.25), st(-0.22, 0.325, 0.24),
         st(sy - SOLE_FWD - 0.04, 0.285, 0.235), st(-0.62, 0.235, 0.215, zb=0.105),
         st(-0.74, 0.175, 0.19, zb=0.11), st(-0.82, 0.10, 0.165, zb=0.115, rt=0.03, rb=0.03)]
    return dict(name="Sport", stations=S, cockpits=[(1, 6)], rim=0.04, seats=[sy],
                shells=[dict(rx=0.23, ry=0.17, top_c=0.60, top_s=0.32, recline=0.15)],
                wheel_r=0.05, wheel_w=0.05, wheel_x=0.30, wheel_y=(-0.55, 0.66),
                bumper_r=0.03, bumper_z=0.15)


def spec_tandem():
    sy1, sy2 = -0.55, 0.61
    S = [st(1.02, 0.28, 0.27, zb=0.13), st(0.965, 0.355, 0.29, zb=0.11), st(0.80, 0.375, 0.30),
         st(0.45, 0.375, 0.29), st(0.12, 0.375, 0.285), st(-0.45, 0.365, 0.275),
         st(-0.90, 0.345, 0.268), st(-1.27, 0.31, 0.26),
         st(sy1 - SOLE_FWD - 0.045, 0.27, 0.255), st(-1.54, 0.21, 0.245, zb=0.12),
         st(-1.575, 0.125, 0.225, zb=0.13, rt=0.04, rb=0.03)]
    return dict(name="Tandem", stations=S, cockpits=[(1, 8)], rim=0.045, seats=[sy1, sy2],
                shells=[dict(rx=0.24, ry=0.17, top_c=0.62, top_s=0.38, recline=0.03),
                        dict(rx=0.24, ry=0.17, top_c=0.66, top_s=0.38, recline=0.07)],
                wheel_r=0.05, wheel_w=0.045, wheel_x=0.30, wheel_y=(-1.28, -0.30, 0.86),
                bumper_r=0.035, bumper_z=0.165)


def spec_mini():
    sy = 0.25
    S = [st(0.52, 0.31, 0.30, zb=0.12, rt=0.08, rb=0.06), st(0.47, 0.39, 0.32, zb=0.10, rt=0.08, rb=0.06),
         st(0.36, 0.405, 0.33, rt=0.08, rb=0.06), st(0.12, 0.405, 0.32, rt=0.08, rb=0.06),
         st(-0.18, 0.39, 0.31, rt=0.08, rb=0.06), st(-0.45, 0.36, 0.30, rt=0.08, rb=0.06),
         st(sy - SOLE_FWD - 0.05, 0.31, 0.29, rt=0.08, rb=0.06), st(-0.70, 0.25, 0.28, zb=0.11, rt=0.07, rb=0.05),
         st(-0.725, 0.16, 0.26, zb=0.12, rt=0.05, rb=0.04)]
    return dict(name="Mini", stations=S, cockpits=[(1, 6)], rim=0.05, seats=[sy],
                shells=[dict(rx=0.25, ry=0.16, top_c=0.66, top_s=0.42, recline=0.02, thick=0.06)],
                wheel_r=0.06, wheel_w=0.055, wheel_x=0.32, wheel_y=(-0.50, 0.38),
                bumper_r=0.055, bumper_z=0.17)


# ----------------------------------------------------------------------------- body
def build_body(mb, sp):
    """Low tub + moulded seat shell(s) + seat pads + leg console.  Tint cells only.
    Returns (rings, shell_tops, front_walls)."""
    S = sp["stations"]
    rings = build_tub(mb, S, sp["cockpits"], 2, 1, FLOOR_Z, sp["rim"], backrest_slope=0.0, front_slope=0.0)
    tops, walls = [], []
    for k, (sy, sh) in enumerate(zip(sp["seats"], sp["shells"])):
        tops.append(seat_shell(mb, sy, **sh))
        if k == 0:
            front = sy - SOLE_FWD
        else:  # tandem passenger: feet rest on the back of the front shell
            prev = sp["seats"][k - 1]
            front = prev + sp["shells"][k - 1]["ry"] + 0.07
        walls.append(front)
        cockpit_parts(mb, sy, front)
    if sp["name"] == "Sport":  # voluminous side pods (moulded, tinted)
        for sx in (-1, 1):
            pod = [(0.80, 0.33, 0.04, 0.13, 0.21), (0.66, 0.375, 0.07, 0.11, 0.235), (0.40, 0.39, 0.08, 0.11, 0.235),
                   (0.15, 0.38, 0.065, 0.115, 0.22), (-0.05, 0.355, 0.035, 0.13, 0.19)]
            rr = []
            for y, xc, hw, zb, zt in pod:
                rr.append([(sx * xc + x, yy, z) for x, yy, z in ring(y, hw, hw * 0.85, zb, zt, 0.025, 0.02, 1, 1)])
            grid, caps = mb.loft(rr, "tint_light")
            mb.shade([f for r in grid for f in r] + caps, "tint_light", variation=0.15)
    return rings, tops, walls


def skirt(cm, S):
    """Dark grey chassis skirt visible under the body all around."""
    rr = [ring(s[0], s[1] - 0.03, s[1] - 0.045, 0.045, 0.118, 0.02, 0.015, 1, 1) for s in S[:-1]]
    grid, caps = cm.loft(rr, "chassis_base")
    cm.shade([f for r in grid for f in r] + caps, "chassis_base", variation=0.15)


# ----------------------------------------------------------------------------- trim
def trim(tm, sp, rings, tops, walls):
    S = sp["stations"]
    last = len(S) - 1
    seg = 2  # vertical side wall segment (nt = 2)
    nm = sp["name"]
    # side decal stripes + rubber side strip
    stripe(tm, rings, 1, last - 2, seg, 0.05, 0.32, "decal_white")
    stripe(tm, rings, 1, last - 2, seg, 0.32, 0.42, "decal_black")
    waist_rail(tm, rings, seg, 0.62, cell="rubber", r=0.018, off=0.01)
    # front + rear bumpers
    r, z = sp["bumper_r"], sp["bumper_z"]
    front = bumper_path(S, [last - 3, last - 2, last - 1], S[last][0] - r * 0.6, z, off=0.01)
    tm.sweep(front, r, "rubber", sides=5, scale=(1.0, 0.85))
    rear = bumper_path(S, [2, 1, 0], S[0][0] + r * 0.6, z, off=0.01)
    tm.sweep(rear, r * 1.1, "rubber", sides=5, scale=(1.0, 0.85))
    # rear reflectors
    for sx in (-1, 1):
        tm.box((sx * S[0][1] * 0.55, S[0][0] + 0.004, 0.225), (0.08, 0.01, 0.03), "sign_red", bevel=0.003)
    # rim screws
    step = 4 if len(S) > 11 else 3
    for i in range(1, last - 1, step):
        s = S[i]
        w = s[1] - sp["rim"] * 0.5
        for sx in (-1, 1):
            screw(tm, (sx * w, s[0], s[4] + 0.001), (0, 0, 1), r=0.008)
    # foot pads on the foot wall(s) (rubber, ribbed)
    for wy in walls:
        for sx in (-1, 1):
            tm.box((sx * 0.135, wy + 0.012, 0.25), (0.11, 0.018, 0.2), "rubber_worn")
            for k in range(2):
                tm.box((sx * 0.135, wy + 0.023, 0.21 + k * 0.08), (0.1, 0.008, 0.014), "rubber")
    if nm == "Classic":
        y = S[last - 1][0] + 0.06
        nose_sticker(tm, y, S[last - 1][4] + 0.004, 0.08, "decal_white")
    elif nm == "Sport":
        # pod intakes + white pod stripe
        for sx in (-1, 1):
            tm.box((sx * 0.33, 0.805, 0.17), (0.06, 0.01, 0.05), "decal_black")
            q = [Vector((sx * 0.37, 0.62, 0.237)), Vector((sx * 0.37, 0.05, 0.218)),
                 Vector((sx * 0.40, 0.05, 0.218)), Vector((sx * 0.40, 0.62, 0.237))]
            fs = tm.poly(q, "decal_white")
            for f in fs:
                if f.normal.z < 0:
                    f.normal_flip()
        # race number panel on the nose deck + numbers on both walls
        i = last - 2
        ny, nz = (S[i][0] + S[i + 1][0]) / 2, (S[i][4] + S[i + 1][4]) / 2 + 0.003
        tilt = math.atan2(S[i][4] - S[i + 1][4], S[i][0] - S[i + 1][0])
        rot = Euler((tilt, 0, 0)).to_matrix().to_4x4()
        tm.box((0, ny, nz), (0.2, 0.12, 0.008), "decal_white", bevel=0.003, rot=Euler((tilt, 0, 0)))
        tm.text("7", "decal_black", size=0.1, depth=0.004, font_path=FONT, resolution=1,
                matrix=Matrix.Translation((0, ny, nz + 0.004)) @ rot)
        for sx in (-1, 1):
            p0, p1 = side_point(rings[4], seg, 0.0), side_point(rings[4], seg, 1.0)
            a_, h_ = p0.x - p1.x, p0.z - p1.z
            n = Vector((sx * h_, 0, -a_)).normalized()
            up = Vector((sx * a_, 0, h_)).normalized()
            c = side_point(rings[4], seg, 0.25)
            c = Vector((sx * c.x, c.y, c.z)) + n * 0.003
            mm = Matrix((Vector((0, sx, 0)), up, n)).transposed().to_4x4()
            mm.translation = c
            tm.prism([(-0.07, -0.04), (0.07, -0.04), (0.085, 0.0), (0.07, 0.04), (-0.07, 0.04), (-0.085, 0.0)],
                     0.004, "decal_white", matrix=mm)
            tm.text("7", "decal_black", size=0.075, depth=0.003, font_path=FONT, resolution=1,
                    matrix=mm @ Matrix.Translation((0, 0, 0.004)))
        # twin nose stripes on the deck
        for sx in (-1, 1):
            for i in range(last - 3, last):
                a, b = S[i], S[i + 1]
                pa = Vector((sx * 0.05, a[0], a[4] + 0.003))
                pb = Vector((sx * 0.05, b[0], b[4] + 0.003))
                q = [pa, pb, pb + Vector((sx * 0.025, 0, 0)), pa + Vector((sx * 0.025, 0, 0))]
                fs = tm.poly(q, "decal_white")
                for f in fs:
                    if f.normal.z < 0:
                        f.normal_flip()
    elif nm == "Tandem":
        # passenger's fixed handlebar (same geometry as the steering bar, bolted to the floor)
        sy2 = sp["seats"][1]
        handlebar(None, (0, sy2 - GRIP_FWD, 0.21), None, mb=tm)
        tm.box((0, sy2 - GRIP_FWD, 0.17), (0.11, 0.11, 0.08), "chassis_base", bevel=0.01)
        nose_sticker(tm, S[last - 1][0] + 0.06, S[last - 1][4] + 0.004, 0.08, "decal_white")
    elif nm == "Mini":
        tm.box((0, S[last][0] - 0.085, z + 0.01), (0.26, 0.012, 0.03), "sign_yellow", bevel=0.004)
        for sx, ys in ((1, (-0.05, 0.2)), (-1, (0.08,))):
            for yy in ys:
                i = 3
                p = side_point(rings[i], seg, 0.3)
                tm.cylinder((sx * (abs(p.x) + 0.001), yy, p.z), (sx * (abs(p.x) + 0.006), yy, p.z), 0.04,
                            "decal_white", segments=8)
                tm.cylinder((sx * (abs(p.x) + 0.006), yy, p.z), (sx * (abs(p.x) + 0.008), yy, p.z), 0.022,
                            "sign_red", segments=8)


# ----------------------------------------------------------------------------- assembly
def build_sled(sp):
    lr.reset_scene()
    name = "SM_Sled_" + sp["name"]
    root = lr.empty(name, (0, 0, 0), size=0.3, display="ARROWS")
    S = sp["stations"]

    bm_ = lr.MeshBuilder(seed=7)
    rings, tops, walls = build_body(bm_, sp)
    bm_.to_object("Body", material=lr.palette_material("LR_Palette_Tint"), parent=root)

    tm = lr.MeshBuilder(seed=11)
    trim(tm, sp, rings, tops, walls)
    tm.to_object("Trim", parent=root)

    cm = lr.MeshBuilder(seed=3)
    skirt(cm, S)
    wy = sp["wheel_y"]
    chassis(cm, min(wy) - 0.06, max(wy) + 0.08, 0.2, wy, sp["wheel_x"], sp["wheel_r"], sp["wheel_r"] + 0.03)
    cm.to_object("Chassis", parent=root)

    wf, wr = min(wy), max(wy)
    for nm_, x, y in (("Wheel_FL", 1, wf), ("Wheel_FR", -1, wf), ("Wheel_RL", 1, wr), ("Wheel_RR", -1, wr)):
        wheel(nm_, (x * sp["wheel_x"], y, sp["wheel_r"]), sp["wheel_r"], sp["wheel_w"], root)
    if len(wy) > 2:  # tandem middle wheels (static)
        mm = lr.MeshBuilder()
        for x in (-1, 1):
            p = (x * sp["wheel_x"], wy[1], sp["wheel_r"])
            mm.cylinder((p[0] - 0.02, p[1], p[2]), (p[0] + 0.02, p[1], p[2]), sp["wheel_r"] * 0.9, "rubber",
                        segments=8, cap_cell="rubber_worn")
        mm.to_object("Wheels_Mid", parent=root)

    sy = sp["seats"][0]
    handlebar("Handlebar", (0, sy - GRIP_FWD, 0.21), root)
    lr.empty("Seat", (0, sy, SEAT_Z), parent=root, size=0.1)
    lr.empty("Camera_FP", (EYE[0], sy + EYE[1], SEAT_Z + EYE[2]), parent=root, size=0.1)
    if len(sp["seats"]) > 1:
        sy2 = sp["seats"][1]
        lr.empty("Seat2", (0, sy2, SEAT_Z), parent=root, size=0.1)
        lr.empty("Grip2.L", (GRIP_HALF, sy2 - GRIP_FWD, SEAT_Z + GRIP_UP), parent=root)
        lr.empty("Grip2.R", (-GRIP_HALF, sy2 - GRIP_FWD, SEAT_Z + GRIP_UP), parent=root)
    return root


# ----------------------------------------------------------------------------- rider check renders
RIDER_FBX = os.path.join(lr.ROOT, "Game", "Assets", "_LugeRush", "Art", "Models", "Characters", "SK_Rider.fbx")


def import_rider(seat_obj, variant="A1", head="A"):
    """Import SK_Rider, pose Sit_Idle frame 0, put the armature origin on the seat empty."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=RIDER_FBX)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == "ARMATURE"][0]
    keep = {"Body_%s_" % variant, "Head_%s" % head, "Helmet"}
    for o in list(new):
        if o.type == "MESH" and not any(o.name.startswith(k) for k in keep):
            new.remove(o)
            bpy.data.objects.remove(o, do_unlink=True)
    act = [a for a in bpy.data.actions if a.name.endswith("Sit_Idle")][0]
    arm.animation_data_create()
    arm.animation_data.action = act
    try:
        if act.slots:
            arm.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    # the clip also keys the armature object transform -> mount it under an empty on the seat
    mount = lr.empty("RiderMount", seat_obj.matrix_world.translation.copy())
    arm.parent = mount
    arm.matrix_parent_inverse = Matrix.Identity(4)
    # the FBX references a texture path that does not exist -> point it to the shared palette
    for o in new:
        for m in getattr(o.data, "materials", []) or []:
            if m and m.node_tree:
                for nd in m.node_tree.nodes:
                    if nd.type == "TEX_IMAGE" and (nd.image is None or not os.path.exists(
                            bpy.path.abspath(nd.image.filepath))):
                        nd.image = lr.palette_image()
                        nd.interpolation = "Closest"
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    return arm, new + [mount]


def road_plane(length=30.0, width=3.6):
    mb = lr.MeshBuilder(seed=5)
    import random as _r
    rng = _r.Random(2)
    rows = [[(-width / 2 + width * j / 4, 2.0 - 2 * i, 0.0) for j in range(5)] for i in range(int(length / 2) + 1)]
    mb.surface(rows, "asphalt_base", cell_fn=lambda i, j: rng.choice(["asphalt_base"] * 4 + ["asphalt_wet", "asphalt_dark"]))
    for sx in (-1, 1):  # yellow/black curbs
        for i in range(int(length / 0.5)):
            mb.box((sx * (width / 2 + 0.12), 2.0 - i * 0.5 - 0.25, 0.05), (0.24, 0.5, 0.1),
                   "curb_yellow" if i % 2 == 0 else "curb_black")
    return mb.to_object("PreviewRoad")


def rider_renders(root, t):
    seats = [bpy.data.objects["Seat"]]
    if "Seat2" in bpy.data.objects:
        seats.append(bpy.data.objects["Seat2"])
    riders = []
    for k, s in enumerate(seats):
        arm, objs = import_rider(s, variant=("A1", "B1")[k], head=("A", "B")[k])
        riders.append((arm, objs))
    sled = lr.hierarchy(root)
    frame = sled + [o for _, objs in riders for o in objs if o.type == "MESH"]
    ren = os.path.join(lr.ART, "Renders")
    made = lr.render_preview(frame, os.path.join(ren, "sled_%s_with_rider.png" % t.lower()), size=1024,
                             view=(1.0, -0.9, 0.55), aspect=4 / 3, margin=1.05)
    lr.cleanup(made)
    made = lr.render_preview(frame, os.path.join(ren, "sled_%s_with_rider_back.png" % t.lower()), size=1024,
                             view=(-0.55, 1.0, 0.45), aspect=4 / 3, margin=1.05)
    lr.cleanup(made)
    # first person: hide the front rider's head + helmet (as in game), camera at Camera_FP
    for o in riders[0][1]:
        if o.name.startswith("Head_") or o.name.startswith("Helmet"):
            o.hide_render = True
    road = road_plane()
    cam = bpy.data.objects["Camera_FP"].matrix_world.translation.copy()
    made = lr.render_preview(frame + [road], os.path.join(ren, "sled_%s_fp.png" % t.lower()), size=1024,
                             lens=13.0, aspect=9 / 16, ground=False, cam_loc=cam,
                             target=cam + Vector((0, -1.0, -0.62)))
    lr.cleanup(made)
    lr.cleanup([road])
    for arm, objs in riders:
        lr.cleanup(objs + [arm])


# ----------------------------------------------------------------------------- run
SPECS = {"Classic": spec_classic, "Sport": spec_sport, "Tandem": spec_tandem, "Mini": spec_mini}


def run(types, renders=True):
    report = []
    for t in types:
        sp = SPECS[t]()
        root = build_sled(sp)
        objs = lr.hierarchy(root)
        tris = lr.tri_count(objs)
        out = os.path.join(lr.unity_model_dir("Sleds"), root.name + ".fbx")
        lr.export_fbx(objs, out, drop_root=True)
        lr.save_blend(os.path.join(lr.ART, "Blender", "Sleds_%s.blend" % t))
        report.append((root.name, tris, out))
        print("BUILT %s tris=%d -> %s" % (root.name, tris, out))
        if not renders:
            continue
        ren = os.path.join(lr.ART, "Renders")
        body = bpy.data.objects["Body"]
        body.material_slots[0].link = "OBJECT"
        body.material_slots[0].material = lr.palette_material("Preview_orange", tint=TINTS[0][1])
        if os.path.exists(RIDER_FBX):
            rider_renders(root, t)
        made = lr.render_preview(objs, os.path.join(ren, "sled_%s_hero.png" % t.lower()), size=1024,
                                 view=(-1.0, 1.15, 0.8), aspect=4 / 3, margin=1.05)
        lr.cleanup(made)
        frame = list(objs)
        for k, (cname, hexc) in enumerate(TINTS[1:], start=1):
            r2, objs2 = lr.duplicate_hierarchy(root, suffix=".%s" % cname)
            for o in objs2:
                if o.name.startswith("Body."):
                    o.material_slots[0].link = "OBJECT"
                    o.material_slots[0].material = lr.palette_material("Preview_" + cname, tint=hexc)
            frame += objs2
        rows = 2.9 if t == "Tandem" else 1.9
        roots = [root] + [bpy.data.objects[root.name + "." + c] for c, _ in TINTS[1:]]
        for r, (gx, gy) in zip(roots, ((0, 0), (1.05, 0), (0, rows), (1.05, rows))):
            r.location = (gx, gy, 0)
        made = lr.render_preview(frame, os.path.join(ren, "sled_%s.png" % t.lower()), size=1024,
                                 view=(1.0, -1.3, 0.85), aspect=4 / 3, margin=1.0)
        lr.cleanup(made)
    return report


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    rep = run([a for a in argv if a in SPECS] or list(SPECS), renders="--no-render" not in argv)
    for r in rep:
        print("SUMMARY", *r)
