"""Generates the shared low-poly palette atlas (LR_Palette.png + LR_Palette.json).

16x16 cells, 16 px each (256x256). Every model UV-maps faces into cell centers.
Run with any Python 3:  python Art/Scripts/make_palette.py
Row ownership: 0-3 sleds/track (Designer), 4-5 characters (Animator),
6-11 biomes (Environment), 12 lighting/emissive, 14 Designer free, 15 Animator free.
"""
import colorsys
import json
import os
import struct
import zlib

CELL = 16
GRID = 16
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Textures")


def hx(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def shade(hexcol, l_mul=1.0, s_mul=1.0, h_shift=0.0):
    r, g, b = (c / 255.0 for c in hx(hexcol))
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    r, g, b = colorsys.hls_to_rgb((h + h_shift) % 1.0, max(0, min(1, l * l_mul)), max(0, min(1, s * s_mul)))
    return "#%02X%02X%02X" % (round(r * 255), round(g * 255), round(b * 255))


def ramp(name, base, n=4):
    """light, base, dark, deep (+ warm/cool extras when n > 4)."""
    steps = [(1.22, 0.95, 0.0), (1.0, 1.0, 0.0), (0.78, 1.05, 0.0), (0.58, 1.1, 0.0),
             (1.08, 1.0, 0.02), (0.92, 1.0, -0.02)]
    labels = ["light", "base", "dark", "deep", "warm", "cool"]
    return [(f"{name}_{labels[i]}", shade(base, *steps[i])) for i in range(n)]


ROWS = {
    0: ramp("sled_orange", "#F2792B") + ramp("sled_red", "#D93A3A") + ramp("sled_yellow", "#F2C230") + ramp("sled_teal", "#3BB8C4"),
    1: [("tint_white", "#FFFFFF"), ("tint_light", "#E6E6E6"), ("tint_mid", "#BFBFBF"), ("tint_dark", "#8C8C8C")]
       + ramp("chassis", "#2E3238") + ramp("chrome", "#C9CDD2")
       + [("rubber", "#1A1B1D"), ("rubber_worn", "#33363A"), ("decal_white", "#F5F5F0"), ("decal_black", "#141414")],
    2: ramp("asphalt", "#4A4E52") + [("asphalt_wet", "#2B2F33"), ("curb_yellow", "#F2C230"), ("curb_yellow_dirty", "#C9A030"),
       ("curb_black", "#1E1E1E"), ("curb_black_worn", "#3A3A3A")] + ramp("rail_metal", "#8C9196", 3)
       + [("net_green", "#2F4A35"), ("net_black", "#202423"), ("concrete", "#9A9890")],
    3: ramp("wood", "#8A5A36") + [("sign_yellow", "#F5C518"), ("sign_yellow_dark", "#B88F10"), ("sign_red", "#D1332E"),
       ("sign_red_dark", "#8F1F1C")] + ramp("arch_green", "#1F7A4A", 3) + [("checker_white", "#F2F2F2"),
       ("checker_black", "#151515"), ("bunting_red", "#D9262B"), ("bunting_red_dark", "#9E1A1F"), ("rope", "#B8A27A")],
    4: [("skin_1", "#F6D3B3"), ("skin_2", "#E8B48C"), ("skin_3", "#C98E63"), ("skin_4", "#A0673F"), ("skin_5", "#7A4A2B"), ("skin_6", "#4E2E1C")]
       + ramp("jeans", "#4C6E9E") + [("shirt_white", "#EFEFEA"), ("shirt_grey", "#7E8288"), ("shirt_pink", "#E58FB0"),
       ("shirt_navy", "#2B3A5C"), ("shirt_lime", "#A6CF4A"), ("shoe_grey", "#B9BDC2")],
    5: [("helmet_yellow", "#F2C230"), ("helmet_blue", "#2D6FD1"), ("helmet_green", "#5EC24A"), ("helmet_black", "#232427"),
        ("helmet_white", "#F0F0F0"), ("helmet_pink", "#E86AA0"), ("helmet_red", "#D93A3A"), ("helmet_purple", "#7B4FC9")]
       + [(f"{n}_dark", shade(c, 0.7, 1.05)) for n, c in [("helmet_yellow", "#F2C230"), ("helmet_blue", "#2D6FD1"),
          ("helmet_green", "#5EC24A"), ("helmet_black", "#232427"), ("helmet_white", "#F0F0F0"), ("helmet_pink", "#E86AA0"),
          ("helmet_red", "#D93A3A"), ("helmet_purple", "#7B4FC9")]],
    6: ramp("leaf_dark", "#2F6B2A") + ramp("leaf_mid", "#3F8A35") + ramp("leaf_bright", "#5DA83E") + ramp("leaf_lime", "#8CC152"),
    7: ramp("trunk", "#5B4636") + ramp("bamboo", "#9BB84A") + ramp("soil", "#5E4A34")
       + [("flower_red", "#E04848"), ("flower_yellow", "#F2D04A"), ("moss", "#4F7A3A"), ("fern", "#3E7D45")],
    8: ramp("pine", "#1F4D3A", 6) + ramp("granite", "#7D8590", 6) + ramp("snow", "#EEF3F7", 4),
    9: ramp("water", "#4FA3C7") + ramp("alpine_grass", "#6B9E44", 6) + ramp("cabin_wood", "#7A4E2E", 6),
    10: ramp("canyon_rock", "#B5502E", 6) + ramp("canyon_orange", "#D9733F") + [("rock_deep", "#8C3A24"), ("rock_purple", "#6E3A4A")]
        + ramp("sand", "#E3B47A", 4),
    11: ramp("cactus", "#5E8C4A") + ramp("dry_bush", "#9C8A5A") + ramp("rust", "#8E4A2A") + [("sky_orange", "#F2994A"),
        ("sky_pink", "#E07A8F"), ("sky_purple", "#7A4FA0"), ("sky_blue", "#7FB8E6")],
    12: [("emit_warm", "#FFD27A"), ("emit_white", "#FFF6E0"), ("emit_red", "#FF4A3A"), ("emit_green", "#4AFF7A"),
         ("lamp_glass", "#FFE9B0"), ("window_glow", "#FFC864"), ("shadow_blue", "#2A3550"), ("ao_dark", "#1A1D22")],
}


def build():
    cells = {}
    pixels = [[(255, 0, 255)] * (CELL * GRID) for _ in range(CELL * GRID)]  # magenta = unused
    for row, entries in ROWS.items():
        assert len(entries) <= GRID, f"row {row} has {len(entries)} entries"
        for col, (name, hexcol) in enumerate(entries):
            assert name not in cells, name
            rgb = hx(hexcol)
            for y in range(row * CELL, (row + 1) * CELL):
                for x in range(col * CELL, (col + 1) * CELL):
                    pixels[y][x] = rgb
            cells[name] = {"col": col, "row": row, "hex": hexcol,
                           "uv": [round((col + 0.5) / GRID, 6), round(1.0 - (row + 0.5) / GRID, 6)]}
    return pixels, cells


def write_png(path, pixels):
    h, w = len(pixels), len(pixels[0])
    raw = b"".join(b"\x00" + bytes(c for px in line for c in px) for line in pixels)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    px, cells = build()
    write_png(os.path.join(OUT_DIR, "LR_Palette.png"), px)
    meta = {"grid": GRID, "cell_px": CELL, "uv_note": "uv = cell center; v=1 is top row (row 0)",
            "row_owners": {"0-3": "Designer (sleds/track)", "4-5": "Animator (characters)", "6-11": "Environment",
                           "12": "Lighting/emissive", "13": "free", "14": "Designer free", "15": "Animator free"},
            "cells": cells}
    with open(os.path.join(OUT_DIR, "LR_Palette.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    print(f"{len(cells)} colors -> {OUT_DIR}")
