"""Build the PCL Patcher application icon.

The master is drawn at 4096 px and reduced with premultiplied-alpha Lanczos so
the transparent edge stays clean at 16 px.  Outputs:

  assets/icon/pcl-patcher-icon.svg          vector master
  assets/icon/pcl-patcher-icon-1024.png     raster master
  assets/icon/pcl-patcher-icon-*.png        16 ... 512 px
  assets/icon/pcl-patcher.ico               multi-size Windows icon
  assets/icon/pcl-patcher-mark-512.png      tile-less mark for the title bar
  assets/icon/preview.png                   size / surface preview
"""
from __future__ import annotations

import math
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
OUT = HERE
S = 4096
K = S / 1024.0
MARGIN = 44
HALF = (1024 - 2 * MARGIN) / 2  # 468
N = 4.0

INK = (3, 40, 91, 92)
BADGE_RING = (5, 49, 112, 255)
GOLD_TOP = (255, 209, 102)
GOLD_BOTTOM = (240, 154, 26)


def sc(v: float) -> int:
    return int(round(v * K))


def box(x0, y0, x1, y1):
    return [int(round(x0 * K)), int(round(y0 * K)), int(round(x1 * K)), int(round(y1 * K))]


def pt(x: float, y: float) -> tuple[float, float]:
    return (x * K, y * K)


def squircle_points(margin: float, steps: int = 720, scale: float = 1.0):
    half = (1024 - 2 * margin) / 2
    cx = cy = 512.0
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        c, s = math.cos(t), math.sin(t)
        x = cx + half * math.copysign(abs(c) ** (2 / N), c)
        y = cy + half * math.copysign(abs(s) ** (2 / N), s)
        pts.append((x * scale, y * scale))
    return pts


def squircle_mask(size, margin: float, n: float = N):
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    cx = cy = (w - 1) / 2
    half = (w - 2 * margin) / 2
    nx = np.abs((xx - cx) / half)
    ny = np.abs((yy - cy) / half)
    inside = nx**n + ny**n <= 1.0
    m = Image.new("L", size, 0)
    m.putdata((inside.ravel() * 255).astype(np.uint8))
    return m


def linear_gradient(size, c0, c1, angle=48):
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    rad = math.radians(angle)
    t = xx * math.cos(rad) + yy * math.sin(rad)
    t = (t - t.min()) / (t.max() - t.min())
    a = np.array(c0, float)
    b = np.array(c1, float)
    arr = a[None, None, :] + (b - a)[None, None, :] * t[:, :, None]
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def mask_rgba(mask: Image.Image, color=(255, 255, 255, 255)) -> Image.Image:
    layer = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    layer.paste(color, (0, 0), mask)
    return layer


def p_mask(size, shift_y=0):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    # The bowl owns the top-left shoulder; the stem starts below it.
    d.rounded_rectangle(box(330, 430, 480, 770 + shift_y), radius=sc(40), fill=255)
    d.rounded_rectangle(box(330, 270, 740, 610), radius=sc(170), fill=255)
    cut = Image.new("L", size, 0)
    cd = ImageDraw.Draw(cut)
    cd.rounded_rectangle(box(480, 382, 626, 522), radius=sc(70), fill=255)
    return Image.composite(Image.new("L", size, 0), m, cut)


def badge_shapes(size):
    cx, cy, r = 812, 812, 148
    outer = Image.new("L", size, 0)
    inner = Image.new("L", size, 0)
    ImageDraw.Draw(outer).ellipse(box(cx - r, cy - r, cx + r, cy + r), fill=255)
    ImageDraw.Draw(inner).ellipse(box(cx - r + 13, cy - r + 13, cx + r - 13, cy + r - 13), fill=255)
    return outer, inner


def build_full():
    size = (S, S)
    tile_mask = squircle_mask(size, sc(MARGIN), N)
    tile = Image.new("RGBA", size, (0, 0, 0, 0))
    tile.paste(linear_gradient(size, (10, 78, 179), (47, 139, 255), 48).convert("RGBA"),
               (0, 0), tile_mask)

    # soft top light
    gloss = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(gloss).ellipse(box(-120, -330, 1140, 470), fill=(255, 255, 255, 30))
    gloss = gloss.filter(ImageFilter.GaussianBlur(sc(30)))
    gloss.putalpha(Image.composite(gloss.split()[3], Image.new("L", size, 0), tile_mask))
    tile.alpha_composite(gloss)

    # inner rim follows the squircle contour
    rim = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(rim).line([pt(x, y) for x, y in squircle_points(MARGIN + 4, 720)],
                             fill=(255, 255, 255, 38), width=sc(5), joint="curve")
    rim.putalpha(Image.composite(rim.split()[3], Image.new("L", size, 0), tile_mask))
    tile.alpha_composite(rim)

    # P shadow, clipped to the tile
    pm = p_mask(size)
    shadow_mask = pm.filter(ImageFilter.GaussianBlur(sc(14)))
    shadow_mask = Image.composite(shadow_mask, Image.new("L", size, 0), tile_mask)
    shadow = mask_rgba(shadow_mask, INK)
    tile.alpha_composite(shadow, (0, sc(10)))
    tile.alpha_composite(mask_rgba(pm, (255, 255, 255, 255)))

    # badge contact shadow
    outer, inner = badge_shapes(size)
    bs = outer.filter(ImageFilter.GaussianBlur(sc(22)))
    bs = Image.composite(bs, Image.new("L", size, 0), tile_mask)
    tile.alpha_composite(mask_rgba(bs, (3, 34, 78, 105)), (0, sc(14)))

    tile.alpha_composite(mask_rgba(outer, BADGE_RING))
    gold = linear_gradient(size, GOLD_TOP, GOLD_BOTTOM, 48).convert("RGBA")
    gold_layer = Image.new("RGBA", size, (0, 0, 0, 0))
    gold_layer.paste(gold, (0, 0), inner)
    tile.alpha_composite(gold_layer)

    d = ImageDraw.Draw(tile)
    cx, cy = 812, 812
    arm, thick = 72, 34
    d.rounded_rectangle(box(cx - arm, cy - thick / 2, cx + arm, cy + thick / 2),
                        radius=sc(12), fill=(255, 255, 255, 255))
    d.rounded_rectangle(box(cx - thick / 2, cy - arm, cx + thick / 2, cy + arm),
                        radius=sc(12), fill=(255, 255, 255, 255))
    return tile


def build_mark():
    """Tile-less mark for the blue title bar."""
    size = (S, S)
    mark = Image.new("RGBA", size, (0, 0, 0, 0))
    pm = p_mask(size)
    shadow_mask = pm.filter(ImageFilter.GaussianBlur(sc(12)))
    mark.alpha_composite(mask_rgba(shadow_mask, (3, 30, 70, 70)), (0, sc(9)))
    mark.alpha_composite(mask_rgba(pm))

    outer, inner = badge_shapes(size)
    bs = outer.filter(ImageFilter.GaussianBlur(sc(16)))
    mark.alpha_composite(mask_rgba(bs, (3, 30, 70, 80)), (0, sc(10)))
    mark.alpha_composite(mask_rgba(outer, (7, 50, 108, 255)))
    gold = linear_gradient(size, GOLD_TOP, GOLD_BOTTOM, 48).convert("RGBA")
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    layer.paste(gold, (0, 0), inner)
    mark.alpha_composite(layer)
    d = ImageDraw.Draw(mark)
    cx, cy = 812, 812
    arm, thick = 72, 34
    d.rounded_rectangle(box(cx - arm, cy - thick / 2, cx + arm, cy + thick / 2),
                        radius=sc(12), fill=(255, 255, 255, 255))
    d.rounded_rectangle(box(cx - thick / 2, cy - arm, cx + thick / 2, cy + arm),
                        radius=sc(12), fill=(255, 255, 255, 255))
    return mark


def resize_rgba(im: Image.Image, n: int) -> Image.Image:
    """Resize with premultiplied alpha to avoid dark fringes."""
    a = np.asarray(im, dtype=np.float32) / 255.0
    alpha = a[:, :, 3:4]
    prem = np.concatenate([a[:, :, :3] * alpha, alpha], axis=2)
    src = Image.fromarray((prem * 255.0 + 0.5).clip(0, 255).astype(np.uint8), "RGBA")
    small = np.asarray(src.resize((n, n), Image.LANCZOS), dtype=np.float32) / 255.0
    sa = small[:, :, 3:4]
    rgb = np.divide(small[:, :, :3], sa, out=np.zeros_like(small[:, :, :3]), where=sa > 0)
    out = np.concatenate([rgb, sa], axis=2)
    return Image.fromarray((out * 255.0 + 0.5).clip(0, 255).astype(np.uint8), "RGBA")


def ico_frame(im: Image.Image) -> bytes:
    """A 32bpp BGRA DIB frame (bottom-up) with a 1bpp AND mask.

    256 px frames are stored as PNG, which is what the Windows shell expects
    for the large icon and what keeps the file compact.
    """
    w, h = im.size
    if w >= 256:
        import io
        buffer = io.BytesIO()
        im.save(buffer, "PNG", optimize=True)
        return buffer.getvalue()
    arr = np.asarray(im.convert("RGBA"), dtype=np.uint8)
    bgra = arr[:, :, [2, 1, 0, 3]]
    xor = bgra[::-1].tobytes()
    row_bytes = (w + 31) // 32 * 4
    mask = bytearray(row_bytes * h)
    transparent = arr[:, :, 3] < 8
    for y in range(h):
        base = (h - 1 - y) * row_bytes
        for x in range(w):
            if transparent[y, x]:
                mask[base + (x >> 3)] |= 0x80 >> (x & 7)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0,
                         len(xor) + len(mask), 0, 0, 0, 0)
    return header + xor + bytes(mask)


def write_ico(path: Path, images: list[Image.Image]):
    frames = [ico_frame(im) for im in images]
    count = len(frames)
    offset = 6 + 16 * count
    directory = bytearray(struct.pack("<HHH", 0, 1, count))
    for im, data in zip(images, frames):
        w, h = im.size
        directory += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32,
                                 len(data), offset)
        offset += len(data)
    path.write_bytes(bytes(directory) + b"".join(frames))


def svg_squircle(margin: float) -> str:
    pts = squircle_points(margin, 240, 1.0)
    return "M " + " L ".join(f"{x:.2f},{y:.2f}" for x, y in pts) + " Z"


def build_svg() -> str:
    tile_path = svg_squircle(MARGIN)
    rim_path = svg_squircle(MARGIN + 4)
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <title>PCL Patcher</title>
  <desc>Third-party Plain Craft Launcher patcher: blue PCL tile, white P, gold patch badge.</desc>
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#0A4EB3"/>
      <stop offset="1" stop-color="#2F8BFF"/>
    </linearGradient>
    <linearGradient id="gold" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#FFD166"/>
      <stop offset="1" stop-color="#F09A1A"/>
    </linearGradient>
    <radialGradient id="gloss" cx="0.5" cy="0" r="0.9">
      <stop offset="0" stop-color="#fff" stop-opacity="0.14"/>
      <stop offset="1" stop-color="#fff" stop-opacity="0"/>
    </radialGradient>
    <clipPath id="tileClip"><path d="{tile_path}"/></clipPath>
    <mask id="pMask" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024">
      <rect width="1024" height="1024" fill="#000"/>
      <rect x="330" y="430" width="150" height="340" rx="40" fill="#fff"/>
      <rect x="330" y="270" width="410" height="340" rx="170" fill="#fff"/>
      <rect x="480" y="382" width="146" height="140" rx="70" fill="#000"/>
    </mask>
    <filter id="blur14" x="-30%" y="-30%" width="160%" height="160%">
      <feGaussianBlur stdDeviation="14"/>
    </filter>
    <filter id="blur22" x="-40%" y="-40%" width="180%" height="180%">
      <feGaussianBlur stdDeviation="22"/>
    </filter>
  </defs>

  <path d="{tile_path}" fill="url(#tile)"/>
  <g clip-path="url(#tileClip)">
    <ellipse cx="512" cy="70" rx="840" ry="400" fill="url(#gloss)"/>
    <path d="{rim_path}" fill="none" stroke="#fff" stroke-opacity="0.15" stroke-width="5"/>
  </g>

  <g filter="url(#blur14)" opacity="0.36">
    <rect x="0" y="10" width="1024" height="1024" fill="#03285B" mask="url(#pMask)"/>
  </g>
  <rect width="1024" height="1024" fill="#FFFFFF" mask="url(#pMask)"/>

  <g filter="url(#blur22)" opacity="0.41">
    <circle cx="812" cy="826" r="148" fill="#03224E"/>
  </g>
  <circle cx="812" cy="812" r="148" fill="#053170"/>
  <circle cx="812" cy="812" r="135" fill="url(#gold)"/>
  <rect x="740" y="795" width="144" height="34" rx="12" fill="#fff"/>
  <rect x="795" y="740" width="34" height="144" rx="12" fill="#fff"/>
</svg>
'''


def build_preview(full: Image.Image) -> Image.Image:
    board = Image.new("RGB", (1400, 840), "#EDF3FB")
    d = ImageDraw.Draw(board)
    title = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 26)
    label = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 14)
    word = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 30)
    word_small = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 18)
    d.rounded_rectangle([40, 40, 1360, 420], radius=22, fill="#FFFFFF")
    d.text((72, 72), "PCL Patcher  ·  application icon", fill="#0F172A", font=title)

    # small-size ladder, centred on a common baseline
    ladder = [16, 20, 24, 32, 40, 48, 64, 128]
    for i, n in enumerate(ladder):
        cx = 112 + i * 112
        im = resize_rgba(full, n)
        board.paste(im, (cx - n // 2, 230 - n // 2), im)
        d.text((cx - 8, 360), str(n), fill="#64748B", font=label)

    # large 256
    big = resize_rgba(full, 256)
    board.paste(big, (1170 - 128, 150), big)
    d.text((1170 - 12, 118), "256", fill="#64748B", font=label)

    # dark surface: title bar and taskbar
    d.rounded_rectangle([40, 450, 1360, 800], radius=22, fill="#0E66C2")
    d.text((72, 478), "Title bar / taskbar", fill="#CFE4FF", font=label)
    icon32 = resize_rgba(full, 32)
    board.paste(icon32, (72, 538), icon32)
    d.text((118, 532), "PCL", fill="#FFFFFF", font=word)
    d.text((196, 542), "Patcher", fill="#E6F0FF", font=word_small)

    d.rectangle([40, 700, 1360, 800], fill="#202020")
    x = 76
    for n in (16, 24, 32, 48, 64):
        im = resize_rgba(full, n)
        board.paste(im, (x, 750 - n // 2), im)
        x += n + 26
    d.text((x + 26, 714), "Windows taskbar, 100–200% DPI", fill="#B9C3D0", font=label)
    return board


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    full = build_full()
    mark = build_mark()

    full.resize((1024, 1024), Image.LANCZOS).save(OUT / "pcl-patcher-icon-1024.png")
    for n in (512, 256, 128, 64, 48, 32, 24, 20, 16):
        resize_rgba(full, n).save(OUT / f"pcl-patcher-icon-{n}.png")
    resize_rgba(mark, 512).save(OUT / "pcl-patcher-mark-512.png")
    resize_rgba(mark, 256).save(OUT / "pcl-patcher-mark-256.png")

    ico_images = [resize_rgba(full, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    write_ico(OUT / "pcl-patcher.ico", ico_images)

    (OUT / "pcl-patcher-icon.svg").write_text(build_svg(), encoding="utf-8")
    build_preview(full).save(OUT / "preview.png")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
