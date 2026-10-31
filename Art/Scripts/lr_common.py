"""Luge Rush - shared Blender helpers (palette UVs, mesh building, export, preview render).

Used by sleds_*.py / track_*.py and readable by other artists' scripts (env_*, rider_*).
Keep the public API stable; add new functions instead of changing signatures.

Usage inside a Blender script run with  blender -b -P Art/Scripts/<script>.py :

    import os, sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import lr_common as lr

    lr.reset_scene()
    mb = lr.MeshBuilder()
    mb.box((0, 0, 0.5), (1, 1, 1), "rail_metal_base", bevel=0.02)
    obj = mb.to_object("SM_Test")           # palette UVs + material + flat shading
    lr.export_fbx([obj], lr.unity_model_dir("Track") + "/SM_Test.fbx")
    lr.render_preview([obj], lr.ART + "/Renders/test.png")

Conventions
- 1 unit = 1 m.  Blender front = -Y  ->  Unity +Z (with the ART_GUIDE FBX settings).
  Blender +Z = Unity +Y.  Blender +X = Unity -X (handedness flip).  A rider facing -Y has
  his left hand on +X (Blender .L convention) which is Unity -X (= his left in Unity too).
- Every face carries a palette cell name (int face layer "lr_pal"); to_object() writes the UV of
  the cell centre to every loop of the face.  One shared material "LR_Palette" (or any name).
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

# ----------------------------------------------------------------------------- paths
SCRIPTS = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(SCRIPTS)
ROOT = os.path.dirname(ART)
PALETTE_PNG = os.path.join(ART, "Textures", "LR_Palette.png")
PALETTE_JSON = os.path.join(ART, "Textures", "LR_Palette.json")
UNITY_MODELS = os.path.join(ROOT, "Game", "Assets", "_LugeRush", "Art", "Models")


def unity_ready():
    """True when the Unity project exists (ProjectVersion.txt) -> export straight into Game/."""
    return os.path.exists(os.path.join(ROOT, "Game", "ProjectSettings", "ProjectVersion.txt"))


def unity_model_dir(category):
    """Output folder for FBX of a category ('Sleds', 'Track', 'Environment/Forest', ...).
    Falls back to Art/Export/<category> when the Unity project is not created yet."""
    d = os.path.join(UNITY_MODELS, category) if unity_ready() else os.path.join(ART, "Export", category)
    os.makedirs(d, exist_ok=True)
    return d


# ----------------------------------------------------------------------------- palette
_PAL = None


def palette():
    """dict cell name -> {'col','row','hex','uv'} (re-read from LR_Palette.json once per run)."""
    global _PAL
    if _PAL is None:
        with open(PALETTE_JSON, encoding="utf-8") as f:
            _PAL = json.load(f)["cells"]
    return _PAL


def cell_index(name):
    c = palette()[name]  # KeyError -> unknown cell name (typo) is a bug, fail loudly
    return c["row"] * 16 + c["col"]


def cell_uv(index):
    row, col = divmod(index, 16)
    return ((col + 0.5) / 16.0, 1.0 - (row + 0.5) / 16.0)


def cell_name(index):
    row, col = divmod(index, 16)
    for n, c in palette().items():
        if c["row"] == row and c["col"] == col:
            return n
    return None


def hex_to_linear(hexcol):
    h = hexcol.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


_TONE_ORDER = ["light", "base", "dark", "deep"]
_EXPLICIT_FAMILIES = [["tint_white", "tint_light", "tint_mid", "tint_dark"],
                      ["checker_white", "checker_black"],
                      ["curb_yellow", "curb_yellow_dirty"], ["curb_black_worn", "curb_black"],
                      ["sign_yellow", "sign_yellow_dark"], ["sign_red", "sign_red_dark"],
                      ["bunting_red", "bunting_red_dark"], ["rubber_worn", "rubber"],
                      ["hazard_orange", "hazard_orange_dark"], ["roof_red", "roof_red_dark"],
                      ["galv_light", "rail_metal_light", "rail_metal_base", "rail_metal_dark"],
                      ["concrete", "concrete_dark"]]


def tone(name, step):
    """Move a cell along its tone family: step -1 = lighter, +1 = darker (clamped).
    Families: <x>_light/_base/_dark/_deep ramps, tint_white..tint_dark, and a few explicit pairs.
    Unknown families return the name unchanged."""
    pal = palette()
    for fam in _EXPLICIT_FAMILIES:
        if name in fam:
            i = max(0, min(len(fam) - 1, fam.index(name) + step))
            return fam[i]
    for t in _TONE_ORDER:
        if name.endswith("_" + t):
            prefix = name[: -len(t) - 1]
            fam = [prefix + "_" + x for x in _TONE_ORDER if prefix + "_" + x in pal]
            i = max(0, min(len(fam) - 1, fam.index(name) + step))
            return fam[i]
    return name


def lighter(name):
    return tone(name, -1)


def darker(name):
    return tone(name, +1)


# ----------------------------------------------------------------------------- materials
def palette_image():
    img = bpy.data.images.get("LR_Palette.png")
    if img is None:
        img = bpy.data.images.load(PALETTE_PNG, check_existing=True)
    return img


def palette_material(name="LR_Palette", tint=None, emissive=False):
    """Material sampling LR_Palette.png with nearest filtering.
    tint: optional hex/RGB multiplied on top (preview of Unity _BaseColor tinting).
    emissive: also feed the texture into emission (preview of emissive cells)."""
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    mat = bpy.data.materials.new(name)
    try:
        mat.use_nodes = True
    except Exception:
        pass
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = "Closest"
    color_socket = tex.outputs["Color"]
    if tint is not None:
        rgb = hex_to_linear(tint) if isinstance(tint, str) else tint
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(color_socket, mix.inputs["A"])
        mix.inputs["B"].default_value = (*rgb, 1.0)
        color_socket = mix.outputs["Result"]
    nt.links.new(color_socket, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    if emissive:
        nt.links.new(color_socket, bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 3.0
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


# ----------------------------------------------------------------------------- scene
def reset_scene():
    """Empty scene with metric units (keeps nothing from the startup file)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    global _PAL
    _PAL = None
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    return sc


def empty(name, loc=(0, 0, 0), parent=None, size=0.05, display="PLAIN_AXES"):
    """Create an empty (locator).  loc is in world space; parent keeps world position."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = display
    e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.location = Vector(loc)
    if parent is not None:
        set_parent(e, parent)
    return e


def set_parent(child, parent):
    """Parent keeping the child's current world transform."""
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    # identity parent-inverse (FBX exporter + bake_space_transform mis-converts grandchildren otherwise)
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.matrix_basis = parent.matrix_world.inverted() @ mw


def hierarchy(root):
    out = [root]
    for c in root.children:
        out += hierarchy(c)
    return out


def tri_count(objs):
    n = 0
    for o in objs:
        if o.type == "MESH":
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n


def save_blend(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    try:
        bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups
    except Exception:
        pass
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)


# ----------------------------------------------------------------------------- mesh builder
def _basis_from_axis(axis):
    """Matrix whose Z column points along axis (for cylinders between two points)."""
    z = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(z.z) < 0.95 else Vector((0, 1, 0))
    x = ref.cross(z).normalized()
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed()
    return m.to_4x4()


class MeshBuilder:
    """Accumulates geometry in one bmesh; every face carries a palette cell (layer 'lr_pal').

    Cell arguments accept a name ('chrome_base') or a callable f(face) -> name, or a dict with
    keys 'top', 'bottom', 'side' (chosen by face normal).  All primitive methods return the list
    of faces they created so callers can recolour / bevel / jitter them further."""

    def __init__(self, seed=1):
        self.bm = bmesh.new()
        self.pal = self.bm.faces.layers.int.new("lr_pal")
        self.rng = random.Random(seed)

    # -- colouring ---------------------------------------------------------------
    def paint(self, faces, cell):
        """Assign cell(s) to faces.  cell: name | callable(face) | {'top','bottom','side'}."""
        for f in faces:
            if not f.is_valid:
                continue
            if callable(cell):
                name = cell(f)
            elif isinstance(cell, dict):
                n = f.normal
                if n.z > 0.5 and "top" in cell:
                    name = cell["top"]
                elif n.z < -0.5 and "bottom" in cell:
                    name = cell["bottom"]
                else:
                    name = cell.get("side", cell.get("top"))
            else:
                name = cell
            f[self.pal] = cell_index(name)

    def get_cell(self, f):
        return cell_name(f[self.pal])

    def shade(self, faces, base, light_up=True, variation=0.25, sun=(0.4, -0.6, 0.7)):
        """Low-poly tonal variation: faces pointing up / towards the sun get the lighter tone,
        faces pointing down get darker tones, plus a random +/-1 tone on `variation` of faces."""
        s = Vector(sun).normalized()
        for f in faces:
            if not f.is_valid:
                continue
            n = f.normal
            step = 0
            if light_up and n.z > 0.65:
                step = -1
            elif n.z < -0.5:
                step = 1
            elif n.dot(s) < -0.35:
                step = 1
            if self.rng.random() < variation:
                step += self.rng.choice((-1, 1))
            f[self.pal] = cell_index(tone(base, max(-1, min(2, step))))

    # -- primitives --------------------------------------------------------------
    def _new_faces_from_verts(self, verts):
        fs = set()
        for v in verts:
            fs.update(v.link_faces)
        return list(fs)

    def box(self, center, size, cell, bevel=0.0, bevel_segments=1, rot=None, highlight=True, taper=None):
        """Axis-aligned (or rot=Euler/Matrix rotated) box.  bevel>0 chamfers all edges and paints
        the chamfer faces one tone lighter (edge highlight).  taper=(sx, sy) scales the top face."""
        m = Matrix.Translation(Vector(center))
        if rot is not None:
            m = m @ (rot.to_matrix().to_4x4() if hasattr(rot, "to_matrix") else rot)
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=Matrix.Diagonal((*size, 1.0)))
        verts = res["verts"]
        if taper is not None:
            for v in verts:
                if v.co.z > 0:
                    v.co.x *= taper[0]
                    v.co.y *= taper[1]
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        faces = self._new_faces_from_verts(verts)
        for f in faces:
            f.normal_update()
        self.paint(faces, cell)
        if bevel > 0:
            faces += self.bevel_faces(faces, bevel, bevel_segments, highlight=highlight)
        return [f for f in faces if f.is_valid]

    def bevel_faces(self, faces, width, segments=1, highlight=True, edges=None):
        """Bevel all edges of the given faces (or only `edges`).  Returns the new faces."""
        if edges is None:
            edges = list({e for f in faces for e in f.edges})
        res = bmesh.ops.bevel(self.bm, geom=edges, offset=width, offset_type="OFFSET",
                              segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
        new = res.get("faces", [])
        for f in new:
            f.normal_update()
            if highlight:
                f[self.pal] = cell_index(lighter(cell_name(f[self.pal]) or "tint_white"))
        return new

    def cylinder(self, p0, p1, radius, cell, segments=8, radius2=None, caps=True, cap_cell=None, rot_offset=0.0):
        """Cylinder / cone frustum from p0 to p1.  radius2 = top radius (cone)."""
        p0, p1 = Vector(p0), Vector(p1)
        axis = p1 - p0
        length = axis.length
        m = Matrix.Translation((p0 + p1) * 0.5) @ _basis_from_axis(axis) @ Matrix.Rotation(rot_offset, 4, "Z")
        r2 = radius if radius2 is None else radius2
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segments,
                                    radius1=radius, radius2=r2, depth=length, matrix=m)
        faces = self._new_faces_from_verts(res["verts"])
        for f in faces:
            f.normal_update()
        self.paint(faces, cell)
        if cap_cell is not None:
            ax = axis.normalized()
            self.paint([f for f in faces if abs(f.normal.dot(ax)) > 0.95], cap_cell)
        return faces

    def tube(self, points, radius, cell, segments=6, closed=False):
        """Pipe through a polyline (simple per-segment cylinders with joint spheres omitted)."""
        faces = []
        for a, b in zip(points[:-1], points[1:]):
            faces += self.cylinder(a, b, radius, cell, segments=segments, caps=True)
        if closed:
            faces += self.cylinder(points[-1], points[0], radius, cell, segments=segments)
        return faces

    def sweep(self, points, radius, cell, sides=6, closed=False, scale=(1.0, 1.0), up=(0, 0, 1), caps=True,
              twist=0.0):
        """Continuous pipe/rail through a polyline (rings perpendicular to the path, no gaps at
        joints).  scale=(sx, sy) flattens the profile (sx along the side axis, sy along `up`)."""
        pts = [Vector(p) for p in points]
        n = len(pts)
        upv = Vector(up).normalized()
        rings = []
        for i, p in enumerate(pts):
            if closed:
                t = (pts[(i + 1) % n] - pts[i - 1]).normalized()
            else:
                t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            side = t.cross(upv)
            if side.length < 1e-5:
                side = t.cross(Vector((1, 0, 0)))
            side.normalize()
            nup = side.cross(t).normalized()
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides + math.pi / sides + twist
                ring.append(p + side * math.cos(a) * radius * scale[0] + nup * math.sin(a) * radius * scale[1])
            rings.append(ring)
        if closed:
            rings.append(rings[0])
            grid, cps = self.loft(rings, cell, cap_start=False, cap_end=False)
            # merge the duplicated seam ring
            seam = {v for f in grid[-1] for v in f.verts} | {v for f in grid[0] for v in f.verts}
            bmesh.ops.remove_doubles(self.bm, verts=list(seam), dist=1e-5)
            faces = [f for r in grid for f in r if f.is_valid]
        else:
            grid, cps = self.loft(rings, cell, cap_start=caps, cap_end=caps)
            faces = [f for r in grid for f in r] + cps
        return [f for f in faces if f.is_valid]

    def surface(self, rows, cell, double_sided=False, cell_fn=None):
        """Open quad surface from a 2D array of points rows[i][j] (no caps).
        cell_fn(i, j) -> name overrides `cell` per quad (checker patterns etc.).
        double_sided adds a back copy (flags, pennants, nets, roof sheets seen from below)."""
        vs = [[self.bm.verts.new(Vector(p)) for p in r] for r in rows]
        faces = []
        for i in range(len(vs) - 1):
            for j in range(len(vs[0]) - 1):
                f = self.bm.faces.new((vs[i][j], vs[i][j + 1], vs[i + 1][j + 1], vs[i + 1][j]))
                f.normal_update()
                f[self.pal] = cell_index(cell_fn(i, j) if cell_fn else cell)
                faces.append(f)
        if double_sided:
            back = []
            vb = [[self.bm.verts.new(v.co) for v in r] for r in vs]
            for i in range(len(vb) - 1):
                for j in range(len(vb[0]) - 1):
                    b = self.bm.faces.new((vb[i][j], vb[i + 1][j], vb[i + 1][j + 1], vb[i][j + 1]))
                    b.normal_update()
                    b[self.pal] = cell_index(cell_fn(i, j) if cell_fn else cell)
                    back.append(b)
            faces += back
        return faces

    def tri2(self, a, b, c, cell, cell_back=None):
        """Double-sided triangle (pennants, small fins)."""
        va = [self.bm.verts.new(Vector(p)) for p in (a, b, c)]
        vb = [self.bm.verts.new(Vector(p)) for p in (a, c, b)]
        f1 = self.bm.faces.new(va)
        f2 = self.bm.faces.new(vb)
        for f, cl in ((f1, cell), (f2, cell_back or cell)):
            f.normal_update()
            f[self.pal] = cell_index(cl)
        return [f1, f2]

    def sphere(self, center, radius, cell, segments=8, rings=5, scale=(1, 1, 1)):
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((*scale, 1.0))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=segments, v_segments=rings, radius=radius, matrix=m)
        faces = self._new_faces_from_verts(res["verts"])
        for f in faces:
            f.normal_update()
        self.paint(faces, cell)
        return faces

    def poly(self, points, cell, thickness=0.0, direction=None):
        """Flat polygon from points (optionally extruded by thickness along its normal or `direction`)."""
        verts = [self.bm.verts.new(Vector(p)) for p in points]
        f = self.bm.faces.new(verts)
        f.normal_update()
        faces = [f]
        if thickness:
            d = Vector(direction) if direction is not None else f.normal
            res = bmesh.ops.extrude_face_region(self.bm, geom=[f])
            nv = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
            bmesh.ops.translate(self.bm, verts=nv, vec=d.normalized() * thickness)
            faces = self._new_faces_from_verts(nv + verts)
            # extrude_face_region leaves the original face as the (now inner) back cap: flip it
            for ff in faces:
                ff.normal_update()
            bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self.paint(faces, cell)
        return faces

    def prism(self, points2d, depth, cell, matrix=None):
        """Extrude a 2D outline (x, y list in a local plane) by depth along local +Z, then
        transform by matrix.  Good for signs, letters, flags, brackets."""
        m = matrix if matrix is not None else Matrix()
        pts = [m @ Vector((x, y, 0)) for x, y in points2d]
        nrm = (m.to_3x3() @ Vector((0, 0, 1))).normalized()
        return self.poly(pts, cell, thickness=depth, direction=nrm)

    def loft(self, rings, cell, cap_start=True, cap_end=True):
        """Skin consecutive rings (lists of points, same count, closed loops).
        Returns (grid, caps): grid[i][j] = face between ring i/i+1 and segment j/j+1."""
        vrings = [[self.bm.verts.new(Vector(p)) for p in ring] for ring in rings]
        n = len(rings[0])
        grid = []
        for i in range(len(vrings) - 1):
            row = []
            a, b = vrings[i], vrings[i + 1]
            for j in range(n):
                k = (j + 1) % n
                f = self.bm.faces.new((a[j], a[k], b[k], b[j]))
                row.append(f)
            grid.append(row)
        caps = []
        if cap_start:
            caps.append(self.bm.faces.new(list(reversed(vrings[0]))))
        if cap_end:
            caps.append(self.bm.faces.new(vrings[-1]))
        allf = [f for r in grid for f in r] + caps
        bmesh.ops.recalc_face_normals(self.bm, faces=allf)
        for f in allf:
            f.normal_update()
        self.paint(allf, cell)
        return grid, caps

    def text(self, body, cell, size=0.3, depth=0.03, matrix=None, font_path=None, align="CENTER",
             resolution=2, spacing=1.0):
        """Low-poly extruded text built from a font curve (no textures).  The text lies in the
        local XY plane (reads along +X, up = +Y), extruded towards local +Z... then `matrix`."""
        cu = bpy.data.curves.new("lr_tmp_text", "FONT")
        cu.body = body
        cu.size = size
        cu.extrude = depth * 0.5
        cu.resolution_u = resolution
        cu.align_x = align
        cu.align_y = "CENTER"
        cu.space_character = spacing
        if font_path and os.path.exists(font_path):
            cu.font = bpy.data.fonts.load(font_path, check_existing=True)
        ob = bpy.data.objects.new("lr_tmp_text", cu)
        bpy.context.scene.collection.objects.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        tmp = bmesh.new()
        tmp.from_mesh(me)
        bmesh.ops.remove_doubles(tmp, verts=tmp.verts, dist=0.0005)
        bmesh.ops.dissolve_limit(tmp, angle_limit=math.radians(4), verts=tmp.verts, edges=tmp.edges)
        m = (matrix if matrix is not None else Matrix()) @ Matrix.Translation((0, 0, depth * 0.5))
        tmp.transform(m)
        tmp.to_mesh(me)
        tmp.free()
        bpy.data.objects.remove(ob)
        bpy.data.curves.remove(cu)
        before = set(self.bm.faces)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        faces = [f for f in self.bm.faces if f not in before]
        for f in faces:
            f.normal_update()
        self.paint(faces, cell)
        return faces

    # -- edits -------------------------------------------------------------------
    def jitter(self, faces_or_verts, amount, axes=(1, 1, 1), seed=None):
        """Random vertex offset (breaks the 'perfect box' look).  Shared verts move once."""
        rng = random.Random(seed) if seed is not None else self.rng
        verts = set()
        for g in faces_or_verts:
            if isinstance(g, bmesh.types.BMFace):
                verts.update(g.verts)
            elif isinstance(g, bmesh.types.BMVert):
                verts.add(g)
        for v in sorted(verts, key=lambda v: (round(v.co.x, 4), round(v.co.y, 4), round(v.co.z, 4))):
            v.co += Vector(tuple(rng.uniform(-amount, amount) * a for a in axes))

    def transform(self, faces, matrix):
        verts = list({v for f in faces for v in f.verts})
        bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)

    def mirror_x(self, faces):
        """Duplicate faces mirrored across X=0 (keeps cells)."""
        res = bmesh.ops.duplicate(self.bm, geom=list(faces))
        nf = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
        nv = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
        bmesh.ops.scale(self.bm, vec=(-1, 1, 1), verts=nv)
        bmesh.ops.reverse_faces(self.bm, faces=nf)
        for f in nf:
            f.normal_update()
        return nf

    def tri_count(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    # -- output ------------------------------------------------------------------
    def to_object(self, name, origin=(0, 0, 0), material=None, collection=None, parent=None):
        """Create the mesh object: palette UVs from cells, flat shading, single material.
        origin = pivot in world space (geometry is shifted so the object sits at `origin`)."""
        o = Vector(origin)
        bmesh.ops.translate(self.bm, verts=self.bm.verts, vec=-o)
        uv = self.bm.loops.layers.uv.verify()
        for f in self.bm.faces:
            u = cell_uv(f[self.pal])
            f.smooth = False
            for l in f.loops:
                l[uv].uv = u
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        me.validate()
        if hasattr(me, "shade_flat"):
            me.shade_flat()
        me.materials.append(material if material is not None else palette_material())
        ob = bpy.data.objects.new(name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        ob.location = o
        if parent is not None:
            set_parent(ob, parent)
        return ob


def recolor_object(obj, mapping):
    """Re-map palette cells of an existing mesh object: mapping {old_name: new_name}."""
    me = obj.data
    uvl = me.uv_layers.active.data
    remap = {}
    for a, b in mapping.items():
        remap[cell_uv(cell_index(a))] = cell_uv(cell_index(b))
    for p in me.polygons:
        key = tuple(uvl[p.loop_start].uv)
        for k, v in remap.items():
            if abs(k[0] - key[0]) < 1e-4 and abs(k[1] - key[1]) < 1e-4:
                for li in p.loop_indices:
                    uvl[li].uv = v
                break


# ----------------------------------------------------------------------------- export
FBX_SETTINGS = dict(axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                    bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                    add_leaf_bones=False)


def export_fbx(objects, path, anim=False, object_types=("MESH", "EMPTY"), drop_root=False):
    """Export the given objects (pass the whole hierarchy, e.g. hierarchy(root)) with the
    ART_GUIDE settings.  No .meta files are written (Unity generates them).

    KNOWN BLENDER BUG (5.2, bake_space_transform=True): objects nested 2+ levels below an
    exported top-level object get wrong local transforms (axis conversion skipped).  Keep
    exported hierarchies at most 1 level deep.  drop_root=True skips objects[0] (an organising
    empty at the origin) so its children become top-level; Unity then creates the root
    GameObject named after the file, so the in-Unity hierarchy is unchanged."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    restore = []
    if drop_root:
        root = objects[0]
        objects = list(objects[1:])
        bpy.context.view_layer.update()
        for o in objects:
            if o.parent is root:  # temporarily detach (keeps world transform)
                mw = o.matrix_world.copy()
                restore.append(o)
                o.parent = None
                o.matrix_parent_inverse = Matrix.Identity(4)
                o.matrix_world = mw
    sel = set(objects)
    for o in objects:
        depth, p = 0, o.parent
        while p is not None:
            if p in sel:
                depth += 1
            p = p.parent
        if depth >= 2:
            print("WARNING lr.export_fbx: '%s' is nested %d levels deep -> transform will be wrong in FBX"
                  % (o.name, depth))
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    kw = dict(FBX_SETTINGS)
    if anim:
        kw.update(bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False)
    else:
        kw.update(bake_anim=False)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types=set(object_types), **kw)
    for o in restore:
        set_parent(o, root)
    return path


# ----------------------------------------------------------------------------- preview render
def _world(color):
    w = bpy.data.worlds.new("LR_PreviewWorld")
    bpy.context.scene.world = w
    try:
        w.use_nodes = True
    except Exception:
        pass
    bg = w.node_tree.nodes.get("Background")
    if bg is None:
        bg = w.node_tree.nodes.new("ShaderNodeBackground")
        out = w.node_tree.nodes.get("World Output") or w.node_tree.nodes.new("ShaderNodeOutputWorld")
        w.node_tree.links.new(bg.outputs[0], out.inputs[0])
    bg.inputs[0].default_value = (*hex_to_linear(color), 1.0)
    bg.inputs[1].default_value = 1.0
    return w


def render_preview(objects, path, size=1024, bg="#BFD3DD", view=(1.0, -1.25, 0.75), lens=50.0,
                   margin=1.08, sun_dir=(-0.5, 0.8, -1.0), ground=True, aspect=1.0, target=None, cam_loc=None):
    """Eevee preview from a 3/4 angle with a flat-colour background.
    objects: what to frame.  view: direction from target to camera.  aspect = width/height.
    target: fixed look-at point (no auto re-centring).  cam_loc: explicit camera position."""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = size
    sc.render.resolution_y = int(size / aspect)
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    try:
        sc.view_settings.view_transform = "Standard"
        sc.view_settings.look = "None"
    except Exception:
        pass
    ee = sc.eevee
    for attr, val in (("taa_render_samples", 32), ("use_shadows", True), ("use_gtao", True),
                      ("shadow_ray_count", 2), ("use_raytracing", False)):
        if hasattr(ee, attr):
            try:
                setattr(ee, attr, val)
            except Exception:
                pass
    _world(bg)
    # bounds
    pts = []
    bpy.context.view_layer.update()
    for o in objects:
        if o.type == "MESH":
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
        else:
            pts.append(o.matrix_world.translation.copy())
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = Vector(target) if target is not None else (lo + hi) * 0.5
    radius = max((p - center).length for p in pts)
    # ground plane catching shadows
    made = []
    if ground:
        gme = bpy.data.meshes.new("LR_PreviewGround")
        gbm = bmesh.new()
        bmesh.ops.create_grid(gbm, x_segments=1, y_segments=1, size=radius * 12)
        gbm.to_mesh(gme)
        gbm.free()
        g = bpy.data.objects.new("LR_PreviewGround", gme)
        sc.collection.objects.link(g)
        g.location = (center.x, center.y, lo.z - 0.001)
        gm = bpy.data.materials.new("LR_PreviewGroundMat")
        gm.use_nodes = True
        b = gm.node_tree.nodes.get("Principled BSDF")
        c = hex_to_linear(bg)
        b.inputs["Base Color"].default_value = (c[0] * 0.92, c[1] * 0.92, c[2] * 0.92, 1)
        b.inputs["Roughness"].default_value = 1.0
        gme.materials.append(gm)
        made.append(g)
    # sun + fill
    sun_d = bpy.data.lights.new("LR_Sun", "SUN")
    sun_d.energy = 4.0
    sun_d.angle = math.radians(3)
    sun = bpy.data.objects.new("LR_Sun", sun_d)
    sc.collection.objects.link(sun)
    sun.rotation_euler = Vector(sun_dir).to_track_quat("-Z", "Y").to_euler()
    made.append(sun)
    # camera
    cam_d = bpy.data.cameras.new("LR_Cam")
    cam_d.lens = lens
    cam_d.sensor_fit = "AUTO"
    cam = bpy.data.objects.new("LR_Cam", cam_d)
    sc.collection.objects.link(cam)
    d = Vector(view).normalized()
    fov = 2 * math.atan(cam_d.sensor_width / (2 * lens))
    tan_h = math.tan(fov / 2) if aspect >= 1.0 else math.tan(fov / 2) * aspect
    tan_v = tan_h / aspect
    rot = (-d).to_track_quat("-Z", "Y")
    right = rot @ Vector((1, 0, 0))
    up = rot @ Vector((0, 1, 0))
    if target is None:  # re-centre on the projected extents
        xs = [(p - center).dot(right) for p in pts]
        ys = [(p - center).dot(up) for p in pts]
        center = center + right * (max(xs) + min(xs)) / 2 + up * (max(ys) + min(ys)) / 2
    dist = 0.0
    for p in pts:
        q = p - center
        dz = q.dot(d)
        dist = max(dist, dz + abs(q.dot(right)) * margin / tan_h, dz + abs(q.dot(up)) * margin / tan_v)
    if target is None:
        # refine with true perspective projection: re-centre the screen extents, then rescale distance
        for _ in range(12):
            cpos = center + d * dist
            xs, ys = [], []
            for p in pts:
                q = p - cpos
                depth = max(1e-3, -q.dot(d))
                xs.append(q.dot(right) / (depth * tan_h))
                ys.append(q.dot(up) / (depth * tan_v))
            cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
            ext = max((max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2)
            center = center + right * cx * dist * tan_h * 0.8 + up * cy * dist * tan_v * 0.8
            dist *= 1.0 + (ext * margin - 1.0) * 0.6
    cam.location = center + d * dist
    cam.rotation_euler = rot.to_euler()
    if cam_loc is not None:  # explicit camera position looking at target (interiors)
        cam.location = Vector(cam_loc)
        dist = (Vector(cam_loc) - center).length
        cam.rotation_euler = (center - Vector(cam_loc)).to_track_quat("-Z", "Y").to_euler()
    cam_d.clip_end = dist * 4 + 100
    sc.camera = cam
    made.append(cam)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return made


def cleanup(objs):
    for o in objs:
        if o and o.name in bpy.data.objects:
            bpy.data.objects.remove(o, do_unlink=True)


def duplicate_hierarchy(root, offset=(0, 0, 0), suffix=".dup"):
    """Linked-data duplicate of a hierarchy, moved by offset (for preview line-ups)."""
    mapping = {}
    for o in hierarchy(root):
        n = o.copy()
        n.name = o.name + suffix
        bpy.context.scene.collection.objects.link(n)
        mapping[o] = n
    for o, n in mapping.items():
        if o.parent in mapping:
            n.parent = mapping[o.parent]
    mapping[root].location = root.location + Vector(offset)
    return mapping[root], list(mapping.values())
