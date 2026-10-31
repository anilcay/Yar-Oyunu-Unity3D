"""Shared helpers for Luge Rush environment kits (Environment Artist).

Used by env_kit_common.py, env_forest.py, env_alpine.py, env_canyon.py.
Run kits with:  blender -b -P Art/Scripts/env_<kit>.py

Geometry is built in a single bmesh per asset. Every face carries a palette
index ("pal" face layer); finish() bakes it into UVs that point at the centre
of the matching LR_Palette.png cell, triangulates, flat-shades and creates the
object. Pivot = world origin = ground contact point. 1 unit = 1 m.
"""
import bpy
import bmesh
import json
import math
import os
import random
from mathutils import Vector, Matrix, Euler

SCRIPTS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(SCRIPTS))
PAL_PNG = os.path.join(ROOT, "Art", "Textures", "LR_Palette.png")
PAL_JSON = os.path.join(ROOT, "Art", "Textures", "LR_Palette.json")
with open(PAL_JSON, encoding="utf-8") as _f:
    PAL = json.load(_f)["cells"]
NAMES = sorted(PAL)
IDX = {n: i for i, n in enumerate(NAMES)}

RENDER_DIR = os.path.join(ROOT, "Art", "Renders")
BLEND_DIR = os.path.join(ROOT, "Art", "Blender")
STATS_DIR = os.path.join(ROOT, "Art", "Export", "Environment")
_UNITY_READY = os.path.exists(os.path.join(ROOT, "Game", "ProjectSettings", "ProjectVersion.txt"))
EXPORT_ROOT = (os.path.join(ROOT, "Game", "Assets", "_LugeRush", "Art", "Models", "Environment")
               if _UNITY_READY else os.path.join(ROOT, "Art", "Export", "Environment"))

UP = Vector((0, 0, 1))


def R(prefix, keys=("light", "base", "dark", "deep")):
    """Palette ramp light -> deep for a material prefix (only existing cells)."""
    out = [f"{prefix}_{k}" for k in keys if f"{prefix}_{k}" in PAL]
    assert out, prefix
    return out


def lerp(a, b, t):
    return a + (b - a) * t


# --------------------------------------------------------------------------- colour functions
def shade(ramp, spread=0.55, bias=0.0, rng=None):
    """Colour by face normal: up-facing -> first (light) entry, down-facing -> last (deep)."""
    rng = rng or random

    def cf(f):
        t = (1.0 - (f.normal.z * 0.5 + 0.5)) * (len(ramp) - 1)
        t += rng.gauss(0, spread) + bias
        return ramp[max(0, min(len(ramp) - 1, int(round(t))))]
    return cf


def pick(colors, weights=None, rng=None):
    rng = rng or random

    def cf(f):
        return rng.choices(colors, weights=weights)[0]
    return cf


def const(name):
    return lambda f: name


# --------------------------------------------------------------------------- builder
class B:
    def __init__(self, seed=0):
        self.bm = bmesh.new()
        self.L = self.bm.faces.layers.int.new("pal")
        self.rng = random.Random(seed)

    # -- low level
    def v(self, co):
        return self.bm.verts.new(Vector(co))

    def face(self, verts, col, out=None):
        f = self.bm.faces.new(verts)
        f.normal_update()
        if out is not None and f.normal.dot(out) < 0:
            f.normal_flip()
        f[self.L] = IDX[col]
        return f

    def paint(self, faces, cf):
        for f in faces:
            f.normal_update()
            f[self.L] = IDX[cf(f)]
        return faces

    def jitter(self, verts, amt, z_amt=None):
        r = self.rng
        za = amt if z_amt is None else z_amt
        for v in verts:
            v.co += Vector((r.uniform(-amt, amt), r.uniform(-amt, amt), r.uniform(-za, za)))

    # -- primitives
    def tube(self, pts, radii, sides, cf, cap_top=True, cap_bot=False, jit=0.0, rot0=None, star=None):
        """Tapered tube along a polyline. radii: per point. star=(k, ratio) alternates radius (ribs)."""
        rng = self.rng
        pts = [Vector(p) for p in pts]
        n = len(pts)
        if isinstance(radii, (int, float)):
            radii = [radii] * n
        rot0 = rng.uniform(0, math.tau) if rot0 is None else rot0
        rings, centres, prev_u = [], [], None
        for i, p in enumerate(pts):
            if i == 0:
                t = pts[1] - p
            elif i == n - 1:
                t = p - pts[i - 1]
            else:
                t = pts[i + 1] - pts[i - 1]
            t.normalize()
            if prev_u is None:
                ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
                u = (ref - t * ref.dot(t)).normalized()
            else:
                u = (prev_u - t * prev_u.dot(t)).normalized()
            w = t.cross(u)
            prev_u = u
            r = radii[i]
            if r <= 1e-4:
                rings.append([self.v(p)])
            else:
                ring = []
                for k in range(sides):
                    a = rot0 + k * math.tau / sides
                    rr = r * (1 + rng.uniform(-jit, jit))
                    if star and k % star[0] == 1:
                        rr *= star[1]
                    ring.append(self.v(p + (u * math.cos(a) + w * math.sin(a)) * rr))
                rings.append(ring)
            centres.append(p)
        faces = []
        for i in range(n - 1):
            a, b = rings[i], rings[i + 1]
            c = (centres[i] + centres[i + 1]) * 0.5
            for k in range(sides):
                k2 = (k + 1) % sides
                if len(a) == 1 and len(b) == 1:
                    continue
                if len(b) == 1:
                    vs = [a[k], a[k2], b[0]]
                elif len(a) == 1:
                    vs = [a[0], b[k2], b[k]]
                else:
                    vs = [a[k], a[k2], b[k2], b[k]]
                f = self.bm.faces.new(vs)
                f.normal_update()
                fc = f.calc_center_median()
                if f.normal.dot(fc - c) < 0:
                    f.normal_flip()
                faces.append(f)
        caps = []
        if cap_top and len(rings[-1]) > 2:
            f = self.face(rings[-1], NAMES[0], out=pts[-1] - pts[-2])
            caps.append(f)
        if cap_bot and len(rings[0]) > 2:
            f = self.face(rings[0], NAMES[0], out=pts[0] - pts[1])
            caps.append(f)
        self.paint(faces + caps, cf)
        return faces, caps, rings

    def ico(self, c, rad, sub, cf, jit=0.15, rot=(0, 0, 0)):
        if isinstance(rad, (int, float)):
            rad = (rad, rad, rad)
        m = Matrix.Translation(Vector(c)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*rad, 1))
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0, matrix=m, calc_uvs=False)
        vs = res["verts"]
        mr = min(rad)
        for v in vs:
            v.co += Vector((self.rng.uniform(-1, 1), self.rng.uniform(-1, 1), self.rng.uniform(-1, 1))) * jit * mr
        faces = list({f for v in vs for f in v.link_faces})
        return self.paint(faces, cf)

    def box(self, c, size, cf, rot=(0, 0, 0), jit=0.0):
        m = Matrix.Translation(Vector(c)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m, calc_uvs=False)
        vs = res["verts"]
        if jit:
            self.jitter(vs, jit)
        faces = list({f for v in vs for f in v.link_faces})
        return self.paint(faces, cf)

    def beam(self, p0, p1, w, h, cf, jit=0.0, up=UP):
        """Rectangular beam between two points (planks, posts, rails)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        x = d.normalized()
        upv = Vector(up)
        if abs(x.dot(upv)) > 0.95:
            upv = Vector((1, 0, 0)) if abs(x.x) < 0.9 else Vector((0, 1, 0))
        y = upv.cross(x).normalized()
        z = x.cross(y)
        m = Matrix((
            (x.x * L, y.x * w, z.x * h, (p0.x + p1.x) / 2),
            (x.y * L, y.y * w, z.y * h, (p0.y + p1.y) / 2),
            (x.z * L, y.z * w, z.z * h, (p0.z + p1.z) / 2),
            (0, 0, 0, 1)))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m, calc_uvs=False)
        vs = res["verts"]
        if jit:
            self.jitter(vs, jit)
        faces = list({f for v in vs for f in v.link_faces})
        return self.paint(faces, cf)

    def hull(self, pts, cf, merge_deg=0.0):
        vs = [self.v(p) for p in pts]
        res = bmesh.ops.convex_hull(self.bm, input=vs, use_existing_faces=False)
        rem = list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
        if rem:
            bmesh.ops.delete(self.bm, geom=rem, context="VERTS")
        faces = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace) and g.is_valid]
        if merge_deg > 0:
            res2 = bmesh.ops.dissolve_limit(self.bm, angle_limit=math.radians(merge_deg),
                                            verts=list({v for f in faces for v in f.verts}),
                                            edges=list({e for f in faces for e in f.edges}),
                                            delimit=set())
            faces = list({f for v in vs if v.is_valid for f in v.link_faces})
        return self.paint(faces, cf)

    def rock_points(self, size, n, cuts=3, flat_bottom=True, squash=0.75, sink=0.12, noise=0.18):
        """Random points of an irregular boulder: ellipsoid + planar cuts (big facets)."""
        rng = self.rng
        sx, sy, sz = size
        pts = []
        for _ in range(n):
            d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
            k = 1 + rng.uniform(-noise, noise)
            pts.append(Vector((d.x * sx * k, d.y * sy * k, d.z * sz * k * squash)))
        for _ in range(cuts):
            nrm = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.2, 1))).normalized()
            off = rng.uniform(0.45, 0.8) * max(sx, sy, sz) * 0.8
            for p in pts:
                dd = p.dot(nrm)
                if dd > off:
                    p -= nrm * (dd - off)
        zmin = min(p.z for p in pts)
        out = []
        for p in pts:
            p.z -= zmin + sink * sz
            if flat_bottom and p.z < -sink * sz * 0.5:
                p.z = -sink * sz * 0.5 - rng.uniform(0, 0.02)
            out.append(p)
        return out

    def leaf(self, base, direction, length, width, segs, droop, ctop, cbot, fold=0.12, rib=True,
             twist=0.0, torn=0.0, double=True, petiole=0.08, profile=0.8, tip_w=0.0):
        """Folded leaf strip (banana / fern / blade). ctop/cbot = colour lists light->dark along the leaf."""
        rng = self.rng
        p = Vector(base)
        d = Vector(direction).normalized()
        side = d.cross(UP)
        if side.length < 1e-3:
            side = Vector((1, 0, 0))
        side.normalize()
        step = length / segs
        L, M, Rr, centres = [], [], [], []
        for i in range(segs + 1):
            t = i / segs
            w = width * (math.sin(math.pi * min(1.0, max(petiole, t)) ** profile) if t < 1 else tip_w)
            if torn and 0 < i < segs:
                w *= rng.uniform(1 - torn, 1)
            upl = side.cross(d).normalized()
            if upl.z < 0:
                upl = -upl
            ang = twist * t
            s_rot = side * math.cos(ang) + upl * math.sin(ang)
            mid = p + upl * fold * w
            M.append(mid)
            L.append(p + s_rot * w)
            Rr.append(p - s_rot * w)
            centres.append(upl)
            # advance with droop (rotate direction toward -Z around side axis)
            p = p + d * step
            rot = Matrix.Rotation(-droop / segs, 3, side)
            d = (rot @ d).normalized()
        faces_top = []

        def build(offset, col_list, flip):
            vL = [self.v(c + centres[i] * offset) for i, c in enumerate(L)]
            vM = [self.v(c + centres[i] * offset) for i, c in enumerate(M)]
            vR = [self.v(c + centres[i] * offset) for i, c in enumerate(Rr)]
            fs = []
            for i in range(segs):
                col = col_list[min(len(col_list) - 1, int(i / segs * len(col_list)))]
                tip = (i == segs - 1 and tip_w == 0.0)
                hint = centres[i] * (-1 if flip else 1)
                if rib:
                    if tip:
                        quads = [[vM[i], vL[i], vM[i + 1]], [vR[i], vM[i], vM[i + 1]]]
                    else:
                        quads = [[vM[i], vL[i], vL[i + 1], vM[i + 1]], [vR[i], vM[i], vM[i + 1], vR[i + 1]]]
                    sidecols = [col, col_list[min(len(col_list) - 1, int(i / segs * len(col_list)) + (1 if rng.random() < 0.5 else 0))]]
                else:
                    if tip:
                        quads = [[vR[i], vL[i], vM[i + 1]]]
                    else:
                        quads = [[vR[i], vL[i], vL[i + 1], vR[i + 1]]]
                    sidecols = [col]
                for q, sc in zip(quads, sidecols):
                    fs.append(self.face(q, sc, out=hint))
            return fs
        faces_top = build(0.0, ctop, False)
        faces_bot = build(-0.006, cbot, True) if double else []
        return faces_top + faces_bot

    def blade(self, base, direction, length, width, segs, droop, cols, double=True):
        """Thin grass blade / needle card."""
        return self.leaf(base, direction, length, width, segs, droop, cols, cols[::-1], fold=0.0, rib=False,
                         double=double, petiole=0.6, profile=1.0)

    def cone_layer(self, c, r, h, n, ctop, cbot, droop=0.25, jit=0.1, star=0.72):
        """Jagged pine-needle layer: star ring + apex + dark concave underside."""
        rng = self.rng
        c = Vector(c)
        a0 = rng.uniform(0, math.tau)
        ring = []
        for k in range(n):
            a = a0 + k * math.tau / n + rng.uniform(-0.15, 0.15)
            rr = r * (1.0 if k % 2 == 0 else star) * rng.uniform(1 - jit, 1 + jit)
            z = -droop * r * (1.0 if k % 2 == 0 else 0.5) + rng.uniform(-jit, jit) * 0.3 * r
            ring.append(self.v(c + Vector((math.cos(a) * rr, math.sin(a) * rr, z))))
        apex = self.v(c + Vector((rng.uniform(-jit, jit) * r * 0.3, rng.uniform(-jit, jit) * r * 0.3, h)))
        under = self.v(c + Vector((0, 0, h * 0.18)))
        top, bot = [], []
        for k in range(n):
            a, b = ring[k], ring[(k + 1) % n]
            f = self.face([a, b, apex], ctop[0], out=(a.co + b.co) * 0.5 - c + Vector((0, 0, r * 0.3)))
            top.append(f)
            g = self.face([b, a, under], cbot[0], out=Vector((0, 0, -1)))
            bot.append(g)
        return top, bot

    def ribbon_grid(self, fn, nu, nv, cf):
        """Grid surface; fn(i, j) -> Vector. Returns faces and vertex grid."""
        grid = [[self.v(fn(i, j)) for j in range(nv + 1)] for i in range(nu + 1)]
        faces = []
        for i in range(nu):
            for j in range(nv):
                faces.append(self.bm.faces.new([grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]))
        self.paint(faces, cf)
        return faces, grid

    def tris(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    # -- output
    def finish(self, name, collection=None):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
        # drop degenerate faces
        bad = [f for f in bm.faces if f.calc_area() < 1e-7]
        if bad:
            bmesh.ops.delete(bm, geom=bad, context="FACES_ONLY")
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        uv = me.uv_layers.new(name="UVMap")
        attr = me.attributes["pal"]
        off = ((-0.004, -0.004), (0.004, -0.004), (0.0, 0.004))
        for poly in me.polygons:
            cell = PAL[NAMES[attr.data[poly.index].value]]["uv"]
            for k, li in enumerate(poly.loop_indices):
                uv.data[li].uv = (cell[0] + off[k % 3][0], cell[1] + off[k % 3][1])
            poly.use_smooth = False
        me.attributes.remove(me.attributes["pal"])
        me.materials.append(palette_material())
        obj = bpy.data.objects.new(name, me)
        (collection or bpy.context.scene.collection).objects.link(obj)
        obj["tris"] = len(me.polygons)
        return obj


# --------------------------------------------------------------------------- scene / material
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


_MAT = {}


def palette_material():
    if "m" in _MAT and _MAT["m"].name in bpy.data.materials:
        return _MAT["m"]
    m = bpy.data.materials.new("M_LR_Palette")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(PAL_PNG, check_existing=True)
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.88
    try:
        bsdf.inputs["Specular IOR Level"].default_value = 0.25
    except KeyError:
        pass
    m.use_backface_culling = True
    _MAT["m"] = m
    return m


def export_fbx(obj, biome, filename=None):
    folder = os.path.join(EXPORT_ROOT, biome)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, (filename or obj.name) + ".fbx")
    loc = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             path_mode="STRIP", embed_textures=False)
    obj.location = loc
    return path


def write_stats(kit, objs):
    os.makedirs(STATS_DIR, exist_ok=True)
    data = {}
    for o in objs:
        dims = o.dimensions
        data[o.name] = {"tris": int(o["tris"]), "size_m": [round(dims.x, 2), round(dims.y, 2), round(dims.z, 2)]}
    with open(os.path.join(STATS_DIR, f"stats_{kit}.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    for k, d in data.items():
        print(f"  {k:42s} {d['tris']:5d} tris  {d['size_m']}")


def setup_render(res=(1280, 720), samples=48, exposure=0.0):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.eevee.taa_render_samples = samples
    try:
        sc.eevee.use_shadows = True
        sc.eevee.shadow_resolution_scale = 1.0
        sc.eevee.use_raytracing = False
    except AttributeError:
        pass
    sc.view_settings.view_transform = "AgX"
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    sc.view_settings.exposure = exposure
    sc.render.image_settings.file_format = "PNG"


def world(zenith, horizon, ground=None, strength=1.0, fog=None, mid=0.55):
    """Gradient sky. fog=(density, color, anisotropy) -> world volume (Eevee)."""
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = strength
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    cr = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], cr.inputs["Fac"])
    els = cr.color_ramp.elements
    els[0].position = 0.0
    els[0].color = (*hexrgb(horizon), 1)
    els[1].position = mid
    els[1].color = (*hexrgb(zenith), 1)
    nt.links.new(cr.outputs["Color"], bg.inputs["Color"])
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    if fog:
        fog_box(fog[0], fog[1], fog[2] if len(fog) > 2 else 0.3, fog[3] if len(fog) > 3 else ((0, 150, 40), (500, 600, 160)))
    return w


def fog_box(density, color, aniso=0.3, box=((0, 150, 40), (500, 600, 160))):
    """Atmospheric haze as a volume cube (a world volume renders black in this Eevee build)."""
    me = bpy.data.meshes.new("FogBox")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("FogBox", me)
    bpy.context.scene.collection.objects.link(o)
    o.location = box[0]
    o.scale = box[1]
    m = bpy.data.materials.new("M_Fog")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Density"].default_value = density
    vol.inputs["Color"].default_value = (*hexrgb(color), 1)
    vol.inputs["Anisotropy"].default_value = aniso
    nt.links.new(vol.outputs["Volume"], nt.nodes["Material Output"].inputs["Volume"])
    me.materials.append(m)
    sc = bpy.context.scene
    sc.eevee.volumetric_start = 0.5
    sc.eevee.volumetric_end = 800.0
    sc.eevee.volumetric_tile_size = "4"
    sc.eevee.volumetric_samples = 64
    return o


def hexrgb(h):
    """sRGB hex -> linear rgb."""
    h = h.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


def sun(elev_deg, azim_deg, color, strength, angle_deg=2.0):
    ld = bpy.data.lights.new("Sun", "SUN")
    ld.energy = strength
    ld.color = hexrgb(color)
    ld.angle = math.radians(angle_deg)
    o = bpy.data.objects.new("Sun", ld)
    bpy.context.scene.collection.objects.link(o)
    # Blender sun shines along its local -Z. Elevation above horizon, azimuth from +Y clockwise.
    el, az = math.radians(elev_deg), math.radians(azim_deg)
    d = Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el)))  # towards the sun
    o.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    return o


def camera(loc, target, lens=35, ortho=None):
    cd = bpy.data.cameras.new("Cam")
    cd.lens = lens
    cd.clip_end = 3000
    if ortho:
        cd.type = "ORTHO"
        cd.ortho_scale = ortho
    o = bpy.data.objects.new("Cam", cd)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = o
    return o


def render(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def save_blend(name):
    os.makedirs(BLEND_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, name), compress=True)


def instance(obj, loc, rot_z=0.0, scale=1.0, tilt=(0.0, 0.0), coll=None):
    o = obj.copy()
    o.location = loc
    o.rotation_euler = (tilt[0], tilt[1], rot_z)
    o.scale = (scale, scale, scale)
    o.hide_render = False
    o.hide_viewport = False
    (coll or bpy.context.scene.collection).objects.link(o)
    return o


def grid_layout(rows, gap=1.5, row_gap=2.5):
    """rows: list of lists of objects. Lay out along X, rows stacked along +Y (back)."""
    y = 0.0
    for row in rows:
        depth = max(o.dimensions.y for o in row)
        x = 0.0
        width = sum(o.dimensions.x for o in row) + gap * (len(row) - 1)
        x = -width / 2
        for o in row:
            bb = [o.matrix_world @ Vector(c) for c in o.bound_box] if False else None
            minx = min(Vector(c).x for c in o.bound_box)
            maxx = max(Vector(c).x for c in o.bound_box)
            miny = min(Vector(c).y for c in o.bound_box)
            maxy = max(Vector(c).y for c in o.bound_box)
            o.location = (x - minx, y - (miny + maxy) / 2, 0)
            x += (maxx - minx) + gap
        y += depth + row_gap


def fit_camera(objs, elev=24, azim=-28, aspect=16 / 9, margin=1.08, lens=None):
    """Orthographic 3/4 camera that frames every object."""
    el, az = math.radians(elev), math.radians(azim)
    fwd = -Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))  # camera looks along fwd
    right = fwd.cross(UP).normalized()
    up = right.cross(fwd).normalized()
    bpy.context.view_layer.update()
    pts = []
    for o in objs:
        for c in o.bound_box:
            pts.append(o.matrix_world @ Vector(c))
    xs = [p.dot(right) for p in pts]
    ys = [p.dot(up) for p in pts]
    zs = [p.dot(fwd) for p in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    scale = max(w, h * aspect) * margin
    centre = right * cx + up * cy + fwd * (min(zs) - 50)
    cam = camera(centre, centre + fwd, ortho=scale)
    cam.data.clip_end = (max(zs) - min(zs)) + 200
    return cam


def ground_plane(size, color_ramp, seed=1, z=0.0, n=10):
    """Faceted ground slab for grid renders."""
    b = B(seed)
    s = size / 2
    b.ribbon_grid(lambda i, j: Vector((-s + i * size / n + b.rng.uniform(-0.2, 0.2) * size / n,
                                       -s + j * size / n + b.rng.uniform(-0.2, 0.2) * size / n,
                                       z + b.rng.uniform(-0.03, 0.0))), n, n, pick(color_ramp, rng=b.rng))
    return b.finish("Ground")


# --------------------------------------------------------------------------- composition scene (preview renders)
class TrackScene:
    """Preview diorama: a descending track strip with banks on both sides.

    cx(y): track centre-line x; grade: drop per metre; bank_l/bank_r(d): bank height at distance d from track edge.
    """

    def __init__(self, cx, grade=0.08, half=3.0, bank_l=None, bank_r=None, seed=7):
        self.cx, self.grade, self.half = cx, grade, half
        self.bank_l = bank_l or (lambda d: 3.0 * smoothstep(0.6, 7.0, d) + 0.15 * max(0.0, d - 7))
        self.bank_r = bank_r or self.bank_l
        self.rng = random.Random(seed)

    def zt(self, y):
        return -self.grade * y

    def h(self, x, y):
        d = x - self.cx(y)
        side = self.bank_r if d > 0 else self.bank_l
        dd = abs(d) - self.half
        z = self.zt(y)
        if dd > 0:
            z += side(dd) + 0.25 * math.sin(x * 0.37 + y * 0.21) * min(1.0, dd / 4) + 0.2 * math.sin(y * 0.13 - x * 0.5) * min(1.0, dd / 4)
        return z

    def build_ground(self, ramp_near, ramp_far, x_range=(-60, 60), y_range=(-20, 200), cell=2.5, name="Terrain"):
        b = B(11)
        nx = int((x_range[1] - x_range[0]) / cell)
        ny = int((y_range[1] - y_range[0]) / cell)
        rng = b.rng

        def fn(i, j):
            x = x_range[0] + i * cell + rng.uniform(-0.3, 0.3) * cell
            y = y_range[0] + j * cell + rng.uniform(-0.3, 0.3) * cell
            return Vector((x, y, self.h(x, y) - 0.03))

        def cf(f):
            c = f.calc_center_median()
            dd = abs(c.x - self.cx(c.y)) - self.half
            ramp = ramp_near if dd < 4 else ramp_far
            return shade(ramp, spread=0.8, rng=rng)(f)
        b.ribbon_grid(fn, nx, ny, cf)
        return b.finish(name)

    def build_track(self, y_range=(-20, 200), step=1.0, asphalt=("asphalt_wet", "asphalt_wet", "asphalt_deep", "asphalt_dark"),
                    curb=("curb_yellow", "curb_black"), curb_w=0.3):
        b = B(12)
        rng = b.rng
        n = int((y_range[1] - y_range[0]) / step)
        xs = [-self.half - curb_w, -self.half, -self.half / 3, self.half / 3, self.half, self.half + curb_w]
        rows = []
        for j in range(n + 1):
            y = y_range[0] + j * step
            c = self.cx(y)
            z = self.zt(y)
            row = []
            for k, x in enumerate(xs):
                zz = z + (0.12 if k in (0, 5) else 0.04)
                row.append(b.v((c + x, y, zz)))
            rows.append(row)
        for j in range(n):
            for k in range(len(xs) - 1):
                q = [rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]]
                if k in (0, len(xs) - 2):
                    col = curb[(j // 2) % 2]
                else:
                    col = rng.choice(asphalt)
                b.face(q, col, out=UP)
        return b.finish("Track")

    def scatter(self, protos, n, d_range, y_range, s_range=(0.85, 1.15), sides=(-1, 1), sink=0.08,
                tilt=0.04, min_gap=0.0, placed=None, coll=None, face_track=False):
        out = []
        rng = self.rng
        tries = 0
        placed = placed if placed is not None else []
        while len(out) < n and tries < n * 40:
            tries += 1
            y = rng.uniform(*y_range)
            side = rng.choice(sides)
            d = rng.uniform(*d_range)
            x = self.cx(y) + side * (self.half + d)
            if min_gap and any((x - px) ** 2 + (y - py) ** 2 < (min_gap * max(1.0, pr)) ** 2 for px, py, pr in placed):
                continue
            proto = rng.choice(protos)
            s = rng.uniform(*s_range)
            rz = rng.uniform(0, math.tau)
            if face_track:
                rz = (0 if side > 0 else math.pi) + rng.uniform(-0.25, 0.25)
            o = instance(proto, (x, y, self.h(x, y) - sink), rz, s,
                         (rng.uniform(-tilt, tilt), rng.uniform(-tilt, tilt)), coll)
            placed.append((x, y, s))
            out.append(o)
        return out


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)
