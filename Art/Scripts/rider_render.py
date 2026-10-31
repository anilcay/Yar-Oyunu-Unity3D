"""Preview renders for SK_Rider (opens Art/Blender/Rider_Main.blend, never saves it).

Run:  blender.exe -b -P Art/Scripts/rider_render.py [-- lineup rig anim fp]
Outputs (Art/Renders/):
  rider_lineup_front.png, rider_lineup_34.png  - 6 body variants x head/skin/helmet combos
  rider_rig_pose.png                            - seated pose: mesh | bone sticks, side + 3/4
  rider_anim_<Clip>.png                         - 5-frame contact strip per clip
  rider_anim_overview.png                       - one key pose of every clip
  rider_fp_view.png                             - first-person check from the eye (head/helmet hidden)
The orange sled in the renders is a render-only placeholder (Grip/Seat assumptions of rider_anim.py).
"""
import math
import os
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rider_anim as ra  # noqa: E402
import rider_common as rc  # noqa: E402

TMP = os.path.join(tempfile.gettempdir(), "rider_frames")
os.makedirs(TMP, exist_ok=True)
SEAT_H = -ra.GROUND_Z  # placeholder sled: seat point height above ground


def simple_mat(name, rgb, rough=0.7, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def add_box(name, center, size, mat, parent=None, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.data.materials.append(mat)
    if parent:
        o.parent = parent
    return o


def add_cyl(name, a, b, r, mat):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=r, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.name = name
    o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    o.data.materials.append(mat)
    return o


def placeholder_sled(origin):
    """Simple luge tray around the rider. origin = Seat point in world."""
    o = Vector(origin)
    orange = simple_mat("PH_Orange", srgb("#F2792B"), 0.5)
    dark = simple_mat("PH_Dark", srgb("#2E3238"), 0.6)
    chrome = simple_mat("PH_Chrome", srgb("#C9CDD2"), 0.25, 0.8)
    objs = []
    z0 = o.z - SEAT_H  # ground
    objs.append(add_box("PH_Floor", o + Vector((0, -0.35, -0.155)), (0.62, 1.30, 0.05), orange))
    for s in (1, -1):
        objs.append(add_box("PH_Side", o + Vector((0.34 * s, -0.33, -0.08)), (0.06, 1.34, 0.20), orange))
        objs.append(add_cyl("PH_Wheel", o + Vector((0.30 * s, 0.15, z0 + 0.07 - o.z)),
                            o + Vector((0.38 * s, 0.15, z0 + 0.07 - o.z)), 0.07, dark))
        objs.append(add_cyl("PH_Wheel", o + Vector((0.30 * s, -0.85, z0 + 0.07 - o.z)),
                            o + Vector((0.38 * s, -0.85, z0 + 0.07 - o.z)), 0.07, dark))
        objs.append(add_box("PH_Grip", o + ra.GRIP_PIVOT + Vector((0.21 * s, 0, 0)), (0.07, 0.035, 0.035), dark))
    objs.append(add_box("PH_Back", o + Vector((0, 0.30, -0.02)), (0.62, 0.06, 0.30), orange))
    objs.append(add_box("PH_Nose", o + Vector((0, -1.00, -0.06)), (0.62, 0.08, 0.22), orange))
    objs.append(add_box("PH_SeatPad", o + Vector((0, 0.02, -0.12)), (0.36, 0.34, 0.03), dark))
    g = o + ra.GRIP_PIVOT
    objs.append(add_cyl("PH_Bar", g + Vector((-0.25, 0, 0)), g + Vector((0.25, 0, 0)), 0.013, chrome))
    objs.append(add_cyl("PH_Column", g, g + Vector((0, 0.10, -0.30)), 0.016, chrome))
    return objs


def ground():
    m = simple_mat("PH_Asphalt", srgb("#4A4E52"), 0.9)
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    g = bpy.context.active_object
    g.name = "PH_Ground"
    g.data.materials.append(m)
    return g


def label(text, cam, size=0.06, pos=(-0.9, -0.85)):
    """Text object glued to the camera (bottom-left)."""
    cu = bpy.data.curves.new("Lbl", 'FONT')
    cu.body = text
    cu.size = size
    o = bpy.data.objects.new("Lbl", cu)
    bpy.context.scene.collection.objects.link(o)
    m = bpy.data.materials.get("LblMat") or bpy.data.materials.new("LblMat")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (0.02, 0.02, 0.02, 1)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    cu.materials.append(m)
    o.parent = cam
    o.location = (pos[0], pos[1], -2.0)
    return o


def set_label_pos(lbl, cam):
    # place at the bottom-left of a perspective camera's frustum at depth 2
    w = 2.0 * 36.0 / cam.data.lens
    ar = bpy.context.scene.render.resolution_y / bpy.context.scene.render.resolution_x
    lbl.location = (-w / 2 + 0.05 * w, -w * ar / 2 + 0.04 * w, -2.0)
    lbl.data.size = 0.055 * w


def stitch(paths, cols, out_path, pad=6, bg=(1, 1, 1, 1)):
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        imgs.append(a)
        bpy.data.images.remove(im)
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    H, W = rows * h + (rows + 1) * pad, cols * w + (cols + 1) * pad
    big = np.ones((H, W, 4), dtype=np.float32)
    big[:, :] = bg
    for i, a in enumerate(imgs):
        r, c = divmod(i, cols)
        y0 = H - pad - (r + 1) * h - r * pad  # numpy rows bottom-up like Blender pixels
        x0 = pad + c * (w + pad)
        big[y0:y0 + h, x0:x0 + w] = a
    out = bpy.data.images.new("stitch", W, H, alpha=True)
    out.pixels = big.ravel()
    out.filepath_raw = out_path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    print("WROTE", out_path)


# --------------------------------------------------------------------------------------------
def objs():
    arm = bpy.data.objects["Rider"]
    bodies = [o for o in bpy.data.objects if o.name.startswith("Body_")]
    heads = [o for o in bpy.data.objects if o.name.startswith("Head_")]
    return arm, sorted(bodies, key=lambda o: o.name), sorted(heads, key=lambda o: o.name), bpy.data.objects["Helmet"]


def show_only(visible):
    for o in bpy.data.objects:
        if o.type == 'MESH' and (o.name.startswith(("Body_", "Head_")) or o.name == "Helmet"):
            o.hide_render = o not in visible
            o.hide_viewport = o not in visible


def instance(arm, meshes, offset, skin_k, helm_k):
    a2 = arm.copy()
    a2.animation_data_clear()
    bpy.context.scene.collection.objects.link(a2)
    a2.location = offset
    out = [a2]
    skin = rc.make_material(f"LR_Skin_{skin_k}", skin_k / 16)
    helm = rc.make_material(f"LR_Helmet_{helm_k}", helm_k / 16)
    for m in meshes:
        c = m.copy()
        c.data = m.data.copy()
        c.data.materials[1] = skin
        c.data.materials[2] = helm
        bpy.context.scene.collection.objects.link(c)
        c.parent = a2
        c.modifiers["Armature"].object = a2
        c.hide_render = False
        out.append(c)
    return out


def apply_pose(arm, P):
    rig = ra.Rig(arm)
    basis = ra.to_basis(rig, ra.solve(rig, P))
    for b, m in basis.items():
        arm.pose.bones[b].matrix_basis = m


STAND = ra.P_(hip_pos=(0, 0, 0.96), hip_rot=(0, 0, 0), spine=(0, 0, 0), chest=(0, 0, 0), uchest=(0, 0, 0),
              neck=(0, 0, 0), head=(0, 0, 0), clavfwd_L=0, clavfwd_R=0, grip_L=0, grip_R=0,
              hpos_L=(0.33, 0.02, 0.96), hpos_R=(-0.33, 0.02, 0.96),
              hdir_L=(0.15, -0.05, -1), hdir_R=(-0.15, -0.05, -1), hpalm_L=(1, 0, 0), hpalm_R=(-1, 0, 0),
              elbow_L=(0.3, 1, 0), elbow_R=(-0.3, 1, 0), curl_L=0.25, curl_R=0.25, thumb_L=0.2, thumb_R=0.2,
              foot_L=(0.10, 0.0, 0.085), foot_R=(-0.10, 0.0, 0.085), knee_L=(0.1, -1, 0), knee_R=(-0.1, -1, 0),
              fdir_L=(0.06, -1, -0.45), fdir_R=(-0.06, -1, -0.45), fsole_L=(0, 0, -1), fsole_R=(0, 0, -1))


def do_lineup():
    arm, bodies, heads, helmet = objs()
    show_only([])
    combos = [(0, 0, 0), (1, 2, 1), (2, 3, 2), (3, 1, 3), (4, 4, 6), (5, 5, 7)]  # head, skin, helmet per body
    insts = []
    sp = 0.95
    for i, body in enumerate(bodies):
        hi, sk, hk = combos[i]
        x = (i - (len(bodies) - 1) / 2) * sp
        inst = instance(arm, [body, heads[hi % len(heads)], helmet], (x, 0, 0), sk, hk)
        apply_pose(inst[0], STAND)
        insts.append(inst)
    sc = rc.setup_render((1800, 820))
    cam = rc.look_at_camera("Cam", (0, -9.0, 1.05), (0, 0, 0.88), lens=50)
    rc.render_to(os.path.join(rc.RENDER_DIR, "rider_lineup_front.png"))
    rc.look_at_camera("Cam", (5.6, -7.0, 1.6), (0.3, 0, 0.85), lens=50)
    rc.render_to(os.path.join(rc.RENDER_DIR, "rider_lineup_34.png"))
    # close-up of heads / helmets
    sc.render.resolution_x, sc.render.resolution_y = (1800, 700)
    rc.look_at_camera("Cam", (0.6, -3.6, 1.75), (0, 0, 1.60), lens=85)
    rc.render_to(os.path.join(rc.RENDER_DIR, "rider_lineup_heads.png"))
    for inst in insts:
        for o in inst:
            bpy.data.objects.remove(o, do_unlink=True)


def bone_sticks(arm):
    mat = simple_mat("Stick", (0.02, 0.9, 0.3), 0.4)
    jm = simple_mat("Joint", (0.9, 0.05, 0.6), 0.4)
    out = []
    dg = bpy.context.evaluated_depsgraph_get()
    for pb in arm.pose.bones:
        a = arm.matrix_world @ pb.head
        b = arm.matrix_world @ pb.tail
        out.append(add_cyl("Stick", a, b, 0.007, mat))
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.012, location=a, segments=8, ring_count=6)
        s = bpy.context.active_object
        s.data.materials.append(jm)
        out.append(s)
    return out


def setup_rider_scene(body="Body_A1_TeeJeans", head="Head_A"):
    arm, bodies, heads, helmet = objs()
    show_only([bpy.data.objects[body], bpy.data.objects[head], helmet])
    arm.location = (0, 0, SEAT_H)
    ground()
    placeholder_sled((0, 0, SEAT_H))
    return arm


def do_rig():
    arm = setup_rider_scene()
    arm.animation_data.action = bpy.data.actions["Sit_Idle"]
    sc = rc.setup_render((640, 640))
    sc.frame_set(0)
    shots = []
    views = [("side", (3.0, -0.35, SEAT_H + 0.35), (0, -0.35, SEAT_H + 0.2)),
             ("34", (1.9, -2.4, SEAT_H + 1.2), (0, -0.3, SEAT_H + 0.15))]
    for nm, loc, tgt in views:
        rc.look_at_camera("Cam", loc, tgt, lens=40)
        p = os.path.join(TMP, f"rig_mesh_{nm}.png")
        rc.render_to(p)
        shots.append(p)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render and
              (o.name.startswith(("Body_", "Head_")) or o.name == "Helmet")]
    for o in meshes:
        o.hide_render = True
    sticks = bone_sticks(arm)
    for nm, loc, tgt in views:
        rc.look_at_camera("Cam", loc, tgt, lens=40)
        p = os.path.join(TMP, f"rig_bones_{nm}.png")
        rc.render_to(p)
        shots.append(p)
    for o in sticks:
        bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes:
        o.hide_render = False
    stitch([shots[0], shots[2], shots[1], shots[3]], 2, os.path.join(rc.RENDER_DIR, "rider_rig_pose.png"))


ANIM_FRAMES = {
    "Sit_Idle": [0, 22, 45, 67, 90], "Steer_Left": [0, 45], "Steer_Right": [0, 45], "Brake": [0, 45],
    "Drift_Left": [0, 45], "Drift_Right": [0, 45], "Push_Start": [0, 7, 15, 21, 30], "Bump": [0, 2, 5, 8, 12],
    "Lift_Relax": [0, 45, 95, 135, 180], "Look_Back": [0, 8, 15, 45, 60], "Win_Cheer": [0, 8, 15, 30, 45],
    "Lose_Shrug": [0, 6, 12, 36, 60], "Wave": [0, 8, 15, 30, 45],
}
OVERVIEW = [("Sit_Idle", 0), ("Steer_Left", 0), ("Steer_Right", 0), ("Brake", 0), ("Drift_Left", 0), ("Drift_Right", 0),
            ("Push_Start", 7), ("Push_Start", 15), ("Bump", 2), ("Lift_Relax", 60), ("Look_Back", 30), ("Win_Cheer", 0),
            ("Lose_Shrug", 20), ("Lose_Shrug", 60), ("Wave", 15), ("Sit_Idle", 45)]


def do_anim(only=None):
    arm = setup_rider_scene()
    sc = rc.setup_render((420, 420))
    cam = rc.look_at_camera("Cam", (2.0, -2.6, SEAT_H + 1.25), (0, -0.25, SEAT_H + 0.2), lens=35)
    lbl = label("", cam)
    set_label_pos(lbl, cam)
    for clip, frames in ANIM_FRAMES.items():
        if only and clip not in only:
            continue
        arm.animation_data.action = bpy.data.actions[clip]
        paths = []
        cams = [((2.0, -2.6, SEAT_H + 1.25), 35)]
        if len(frames) == 2:  # steering poses: show from the front and from behind too
            cams = [((2.0, -2.6, SEAT_H + 1.25), 35), ((0.0, -3.2, SEAT_H + 0.9), 35), ((0.0, 2.4, SEAT_H + 1.4), 35),
                    ((3.2, -0.3, SEAT_H + 0.5), 35), ((-2.0, -2.6, SEAT_H + 1.25), 35)]
            frames = [frames[0]] * len(cams)
        for i, f in enumerate(frames):
            loc, lens = cams[min(i, len(cams) - 1)]
            rc.look_at_camera("Cam", loc, (0, -0.25, SEAT_H + 0.2), lens=lens)
            sc.frame_set(f)
            lbl.data.body = f"{clip}  f{f}"
            p = os.path.join(TMP, f"anim_{clip}_{i}.png")
            rc.render_to(p)
            paths.append(p)
        stitch(paths, len(paths), os.path.join(rc.RENDER_DIR, f"rider_anim_{clip}.png"))
    if only:
        return
    rc.look_at_camera("Cam", (2.0, -2.6, SEAT_H + 1.25), (0, -0.25, SEAT_H + 0.2), lens=35)
    paths = []
    for i, (clip, f) in enumerate(OVERVIEW):
        arm.animation_data.action = bpy.data.actions[clip]
        sc.frame_set(f)
        lbl.data.body = f"{clip}  f{f}"
        p = os.path.join(TMP, f"ov_{i}.png")
        rc.render_to(p)
        paths.append(p)
    stitch(paths, 4, os.path.join(rc.RENDER_DIR, "rider_anim_overview.png"))


def do_fp():
    arm = setup_rider_scene()
    for o in bpy.data.objects:
        if o.name.startswith("Head_") or o.name == "Helmet":
            o.hide_render = True
    sc = rc.setup_render((960, 540))
    # trees/rails for depth cue are not needed; plain ground + sled
    paths = []
    for clip, f in (("Sit_Idle", 0), ("Steer_Left", 0), ("Brake", 0), ("Drift_Right", 20)):
        arm.animation_data.action = bpy.data.actions[clip]
        sc.frame_set(f)
        bpy.context.view_layer.update()
        hb = arm.pose.bones["Head"]
        rest = arm.data.bones["Head"].matrix_local
        eye_rest = Vector((0, -0.085, 1.645))
        Mw = arm.matrix_world @ hb.matrix @ rest.inverted()
        eye = Mw @ eye_rest
        fwd = (Mw.to_3x3() @ Vector((0, -1, 0))).normalized()
        cam = rc.look_at_camera("CamFP", eye, eye + fwd, lens=14)
        # look a bit further down than the head's forward (video-like framing)
        cam.rotation_euler.rotate_axis('X', math.radians(-12))
        lbl = label(f"FP  {clip}", cam)
        set_label_pos(lbl, cam)
        p = os.path.join(TMP, f"fp_{clip}.png")
        rc.render_to(p)
        paths.append(p)
        bpy.data.objects.remove(lbl, do_unlink=True)
    stitch(paths, 2, os.path.join(rc.RENDER_DIR, "rider_fp_view.png"))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    what = [a for a in argv if not a.startswith("clip=")] or ["lineup", "rig", "anim", "fp"]
    only = [a[5:] for a in argv if a.startswith("clip=")] or None
    blend = os.path.join(rc.BLEND_DIR, "Rider_Main.blend")
    for w in what:
        bpy.ops.wm.open_mainfile(filepath=blend)
        {"lineup": do_lineup, "rig": do_rig, "anim": lambda: do_anim(only), "fp": do_fp}[w]()


if __name__ == "__main__":
    main()
