"""Shared helpers for the rider (SK_Rider) pipeline.

Self-contained (does not depend on lr_common.py so the rider pipeline can run on its own).
Contents: palette lookup, a small skinned-mesh builder (lofts / boxes with per-face palette colours
and per-vertex bone weights), materials, FBX export (ART_GUIDE settings) and Eevee render helpers.

Conventions (Blender): 1 unit = 1 m, Z up, the rider faces -Y (exports to Unity +Z with
axis_forward='-Z', axis_up='Y'). Character left = +X.
"""
import json
import math
import os

import bpy
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ART_DIR = os.path.dirname(SCRIPT_DIR)
ROOT = os.path.dirname(ART_DIR)
PALETTE_PNG = os.path.join(ART_DIR, "Textures", "LR_Palette.png")
PALETTE_JSON = os.path.join(ART_DIR, "Textures", "LR_Palette.json")
RENDER_DIR = os.path.join(ART_DIR, "Renders")
BLEND_DIR = os.path.join(ART_DIR, "Blender")
UNITY_PROJECT = os.path.join(ROOT, "Game")
UNITY_CHAR_DIR = os.path.join(UNITY_PROJECT, "Assets", "_LugeRush", "Art", "Models", "Characters")
FALLBACK_CHAR_DIR = os.path.join(ART_DIR, "Export", "Characters")

GRID = 16

# Special colour tokens -> (material, base palette cell). The material shifts the UV horizontally
# in Unity (_BaseMap_ST.z = k/16) to pick skin tone k (0..5) or helmet colour k (0..7).
SPECIAL = {
    "SKIN": ("LR_Skin", "skin_1"),
    "SKIN_SHADE": ("LR_Skin", "skin_1_shade"),
    "HELM": ("LR_Helmet", "helmet_yellow"),
    "HELM_DARK": ("LR_Helmet", "helmet_yellow_dark"),
}
MATERIALS = ("LR_Palette", "LR_Skin", "LR_Helmet")

_cells = None


def cells():
    global _cells
    if _cells is None:
        with open(PALETTE_JSON, encoding="utf-8") as f:
            _cells = json.load(f)["cells"]
    return _cells


def resolve_color(name):
    """Return (material_index, (u, v)) for a palette cell name or a SPECIAL token."""
    mat = "LR_Palette"
    if name in SPECIAL:
        mat, name = SPECIAL[name]
    c = cells()[name]
    return MATERIALS.index(mat), (c["uv"][0], c["uv"][1])


def output_char_dir():
    if os.path.exists(os.path.join(UNITY_PROJECT, "ProjectSettings", "ProjectVersion.txt")):
        d = UNITY_CHAR_DIR
    else:
        d = FALLBACK_CHAR_DIR
    os.makedirs(d, exist_ok=True)
    return d


# --------------------------------------------------------------------------------------------
# Scene / materials
# --------------------------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = 30
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    return sc


def palette_image():
    img = bpy.data.images.get("LR_Palette.png")
    if img is None:
        img = bpy.data.images.load(PALETTE_PNG, check_existing=True)
    return img


def make_material(name, u_offset=0.0):
    """Palette material. u_offset only affects Blender previews (Unity: set _BaseMap_ST offset)."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.85
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = 'Closest'
    uv = nt.nodes.new("ShaderNodeUVMap")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.name = "Offset"
    mp.inputs["Location"].default_value = (u_offset, 0.0, 0.0)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def set_material_offset(mat, k):
    mat.node_tree.nodes["Offset"].inputs["Location"].default_value = (k / GRID, 0.0, 0.0)


def base_materials():
    return [make_material(n) for n in MATERIALS]


# --------------------------------------------------------------------------------------------
# Mesh builder
# --------------------------------------------------------------------------------------------

def ellipse_ring(center, rx, ry, n, axis=0, phase=0.5, prof=None):
    """Ring of n points around `center`.

    axis 0: ring lies in XY plane (vertical tubes; angle 0 = front -Y, +90deg = +X).
    axis 1: ring in YZ plane (tubes along X; angle 0 = front -Y, +90deg = +Z).  rx->Y radius, ry->Z radius
    axis 2: ring in XZ plane (tubes along Y; angle 0 = top +Z, +90deg = +X).    rx->X radius, ry->Z radius
    prof(theta) -> radial multiplier (shaping).  Faces are centred on angle 0 when phase=0.5.
    """
    c = Vector(center)
    pts = []
    for i in range(n):
        t = 2.0 * math.pi * (i + phase) / n
        m = prof(t) if prof else 1.0
        s, co = math.sin(t), math.cos(t)
        if axis == 0:
            p = Vector((rx * s, -ry * co, 0.0))
        elif axis == 1:
            p = Vector((0.0, -rx * co, ry * s))
        else:
            p = Vector((rx * s, 0.0, ry * co))
        pts.append(c + p * m)
    return pts


def seg_theta(j, n, phase=0.5):
    """Centre angle (radians, -pi..pi) of loft segment j (between ring vertices j and j+1)."""
    t = 2.0 * math.pi * (j + phase + 0.5) / n
    return math.atan2(math.sin(t), math.cos(t))


class Builder:
    """Accumulates vertices (with bone weights) and coloured faces, then creates one mesh object."""

    def __init__(self):
        self.verts = []      # Vector
        self.weights = []    # dict bone -> w
        self.faces = []      # (idx tuple, color name)

    def v(self, co, w):
        self.verts.append(Vector(co))
        self.weights.append(dict(w) if w else {})
        return len(self.verts) - 1

    def f(self, idx, color):
        self.faces.append((tuple(idx), color))

    def loft(self, rings, colorfn, cap_start=True, cap_end=True, cap_colors=None):
        """rings: list of (points, weights). colorfn(ring_index, seg_index, n) -> colour name for the
        quad between ring i and i+1, segment j. Caps are triangle fans to a centre vertex."""
        idx = []
        for pts, w in rings:
            idx.append([self.v(p, w) for p in pts])
        n = len(rings[0][0])
        for i in range(len(rings) - 1):
            a, b = idx[i], idx[i + 1]
            for j in range(n):
                j2 = (j + 1) % n
                self.f((a[j], a[j2], b[j2], b[j]), colorfn(i, j, n))
        cc = cap_colors or (None, None)
        for k, (on, ring_i, rev) in enumerate(((cap_start, 0, True), (cap_end, len(rings) - 1, False))):
            if not on:
                continue
            pts, w = rings[ring_i]
            ctr = sum((Vector(p) for p in pts), Vector()) / len(pts)
            if isinstance(on, Vector):
                ctr = on
            ci = self.v(ctr, w)
            col = cc[k] or colorfn(max(0, ring_i - 1) if k else 0, 0, n)
            r = idx[ring_i]
            for j in range(n):
                j2 = (j + 1) % n
                if rev:
                    self.f((r[j2], r[j], ci), col)
                else:
                    self.f((r[j], r[j2], ci), col)
        return idx

    def box(self, center, size, w, colors, rot=None):
        """Axis aligned box (optionally rotated by 3x3 `rot`). colors: dict face->colour
        keys: 'top','bottom','front'(-Y),'back','left'(+X),'right'(-X) or 'all'."""
        c = Vector(center)
        hx, hy, hz = (s * 0.5 for s in size)
        corners = []
        for z in (-hz, hz):
            for y in (-hy, hy):
                for x in (-hx, hx):
                    p = Vector((x, y, z))
                    if rot is not None:
                        p = rot @ p
                    corners.append(self.v(c + p, w))
        # index: x + 2*y + 4*z  (0=-,1=+)
        def q(a, b, cc_, d):
            return (corners[a], corners[b], corners[cc_], corners[d])
        allc = colors.get("all")
        faces = {
            "bottom": q(0, 2, 3, 1), "top": q(4, 5, 7, 6),
            "front": q(0, 1, 5, 4), "back": q(2, 6, 7, 3),
            "left": q(1, 3, 7, 5), "right": q(0, 4, 6, 2),
        }
        for k, fi in faces.items():
            self.f(fi, colors.get(k, allc))

    def tris(self):
        return sum(len(fi) - 2 for fi, _ in self.faces)

    def build(self, name, armature_obj=None, materials=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], [fi for fi, _ in self.faces])
        me.update(calc_edges=True)
        mats = materials or base_materials()
        for m in mats:
            me.materials.append(m)
        uvl = me.uv_layers.new(name="UVMap")
        # tiny UV triangle inside the cell centre (avoids zero-area UV warnings in Unity)
        eps = 0.12 / (GRID * 16)
        jit = ((-eps, -eps), (eps, -eps), (eps, eps), (-eps, eps))
        for poly, (_fi, col) in zip(me.polygons, self.faces):
            mi, (u, v) = resolve_color(col)
            poly.material_index = mi
            poly.use_smooth = False
            for k, li in enumerate(poly.loop_indices):
                du, dv = jit[k % 4]
                uvl.data[li].uv = (u + du, v + dv)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        if armature_obj is not None:
            groups = {}
            for vi, w in enumerate(self.weights):
                tot = sum(w.values())
                for bone, val in w.items():
                    if val <= 0:
                        continue
                    g = groups.get(bone)
                    if g is None:
                        g = groups[bone] = ob.vertex_groups.new(name=bone)
                    g.add([vi], val / tot, 'REPLACE')
            ob.parent = armature_obj
            mod = ob.modifiers.new("Armature", 'ARMATURE')
            mod.object = armature_obj
        return ob


def mesh_tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# --------------------------------------------------------------------------------------------
# FBX export
# --------------------------------------------------------------------------------------------

def export_fbx(path, objects, animated=True):
    """ART_GUIDE FBX settings. Take names are the bare action names (Sit_Idle ...), not
    'Armature|Action', by patching the exporter's naming helper during the call."""
    import io_scene_fbx.export_fbx_bin as efb
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    orig = efb.get_blenderID_name

    def take_name(bid):
        if isinstance(bid, tuple) and len(bid) == 2 and isinstance(bid[1], bpy.types.Action):
            return bid[1].name
        return orig(bid)
    efb.get_blenderID_name = take_name
    try:
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
            axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
            use_armature_deform_only=False, primary_bone_axis='Y', secondary_bone_axis='X',
            bake_anim=animated, bake_anim_use_all_actions=animated, bake_anim_use_nla_strips=False,
            bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
            path_mode='STRIP', embed_textures=False)
    finally:
        efb.get_blenderID_name = orig


# --------------------------------------------------------------------------------------------
# Render helpers
# --------------------------------------------------------------------------------------------

def setup_render(res=(768, 768), bg=(0.80, 0.84, 0.88)):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.get("RiderWorld") or bpy.data.worlds.new("RiderWorld")
    world.use_nodes = True
    bgn = world.node_tree.nodes.get("Background")
    if bgn is None:
        bgn = world.node_tree.nodes.new("ShaderNodeBackground")
        out = world.node_tree.nodes.get("World Output") or world.node_tree.nodes.new("ShaderNodeOutputWorld")
        world.node_tree.links.new(bgn.outputs[0], out.inputs[0])
    bgn.inputs[0].default_value = (*bg, 1.0)
    bgn.inputs[1].default_value = 0.9
    sc.world = world
    if "Sun" not in bpy.data.objects:
        sun = bpy.data.lights.new("Sun", 'SUN')
        sun.energy = 3.2
        sun.angle = math.radians(8)
        so = bpy.data.objects.new("Sun", sun)
        sc.collection.objects.link(so)
        so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
        fill = bpy.data.lights.new("Fill", 'SUN')
        fill.energy = 0.8
        fo = bpy.data.objects.new("Fill", fill)
        sc.collection.objects.link(fo)
        fo.rotation_euler = (math.radians(70), 0, math.radians(150))
    try:
        sc.eevee.use_shadows = True
    except AttributeError:
        pass
    return sc


def look_at_camera(name, loc, target, lens=50.0, ortho=None):
    cam = bpy.data.objects.get(name)
    if cam is None:
        cd = bpy.data.cameras.new(name)
        cam = bpy.data.objects.new(name, cd)
        bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(loc)
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    if ortho:
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = 'PERSP'
        cam.data.lens = lens
    cam.data.clip_start = 0.01
    bpy.context.scene.camera = cam
    return cam


def render_to(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
