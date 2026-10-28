
import math, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(OUT, "assets")
os.makedirs(ASSETS, exist_ok=True)

FONT = r"C:\Windows\Fonts\msyhbd.ttc"
A = (124, 92, 255)    # violet
B = (58, 132, 255)    # blue

def squircle_mask(size, radius_ratio=0.2237, ss=1):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    r = int(size * radius_ratio)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=255)
    return m

def gradient(size, a, b):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    t = (x + y) / (2.0 * (size - 1))
    t = np.clip(t, 0, 1)
    t = t * t * (3 - 2 * t)   # smoothstep for a softer blend
    out = np.zeros((size, size, 3), dtype=np.uint8)
    for i in range(3):
        out[:, :, i] = (a[i] + (b[i] - a[i]) * t).astype(np.uint8)
    return Image.fromarray(out, "RGB").convert("RGBA")

def star_points(cx, cy, r, inner=0.30):
    pts = []
    for k in range(8):
        ang = -math.pi / 2 + k * math.pi / 4
        rr = r if k % 2 == 0 else r * inner
        pts.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
    return pts

def render(size, with_sparkle, glyph_scale):
    S = size
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    plate = gradient(S, A, B)

    # soft top-left highlight
    hi = Image.new("L", (S, S), 0)
    hd = ImageDraw.Draw(hi)
    hd.ellipse([-int(S * 0.30), -int(S * 0.42), int(S * 0.86), int(S * 0.72)], fill=52)
    hi = hi.filter(ImageFilter.GaussianBlur(S * 0.10))
    plate_rgba = plate.copy()
    plate_rgba.putalpha(hi)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255))

    body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    body.alpha_composite(plate)
    body.alpha_composite(Image.composite(white, Image.new("RGBA", (S, S), (0, 0, 0, 0)), hi))

    # inner ring for depth
    ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    inset = max(1, int(S * 0.006))
    rd.rounded_rectangle([inset, inset, S - 1 - inset, S - 1 - inset],
                         radius=int(S * 0.2237), outline=(255, 255, 255, 44),
                         width=max(1, int(S * 0.008)))
    body.alpha_composite(ring)

    # glyph with soft shadow
    fsize = int(S * glyph_scale)
    font = ImageFont.truetype(FONT, fsize)
    txt = "润"
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    bb = sd.textbbox((0, 0), txt, font=font)
    gw, gh = bb[2] - bb[0], bb[3] - bb[1]
    gx = (S - gw) / 2 - bb[0] - S * 0.035
    gy = (S - gh) / 2 - bb[1] + S * 0.020
    sd.text((gx, gy), txt, font=font, fill=(24, 16, 60, 110))
    shadow = shadow.filter(ImageFilter.GaussianBlur(max(1, S * 0.018)))
    body.alpha_composite(shadow)

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.text((gx, gy - S * 0.012), txt, font=font, fill=(255, 255, 255, 255))
    body.alpha_composite(layer)

    if with_sparkle:
        cx, cy, r = S * 0.795, S * 0.205, S * 0.105
        glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        gd.ellipse([cx - r * 1.9, cy - r * 1.9, cx + r * 1.9, cy + r * 1.9], fill=(180, 220, 255, 70))
        glow = glow.filter(ImageFilter.GaussianBlur(max(1, S * 0.045)))
        body.alpha_composite(glow)
        sp = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        spd = ImageDraw.Draw(sp)
        spd.polygon(star_points(cx, cy, r), fill=(255, 255, 255, 255))
        spd.polygon(star_points(cx + r * 1.05, cy + r * 1.30, r * 0.34), fill=(235, 246, 255, 235))
        body.alpha_composite(sp)

    # clip to squircle
    mask = squircle_mask(S)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(body, (0, 0), mask)
    return out

MASTER = 1024
SS = 4
plan = {
    16: dict(sparkle=False, glyph=0.60),
    20: dict(sparkle=False, glyph=0.60),
    24: dict(sparkle=True,  glyph=0.58),
    32: dict(sparkle=True,  glyph=0.58),
    40: dict(sparkle=True,  glyph=0.58),
    48: dict(sparkle=True,  glyph=0.58),
    64: dict(sparkle=True,  glyph=0.58),
    128: dict(sparkle=True, glyph=0.58),
    256: dict(sparkle=True, glyph=0.58),
}

big = render(MASTER * SS, True, 0.58)
pils = {}
for s, opt in plan.items():
    if s >= 128:
        src = render(MASTER * SS, opt["sparkle"], opt["glyph"]).resize((s, s), Image.LANCZOS)
    else:
        src = render(s * SS, opt["sparkle"], opt["glyph"]).resize((s, s), Image.LANCZOS)
    pils[s] = src
    src.save(os.path.join(ASSETS, "icon-%d.png" % s))

big.resize((512, 512), Image.LANCZOS).save(os.path.join(ASSETS, "icon-512.png"))
pils[256].resize((180, 180), Image.LANCZOS).save(os.path.join(ASSETS, "icon-180.png"))

# ---- ICO writer: BMP entries for <= 48, PNG entries above ----
def dib(im):
    w, h = im.size
    px = im.load()
    hdr = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    rows = []
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor = b"".join(rows)
    mask_row = ((w + 31) // 32) * 4
    and_rows = []
    for y in range(h - 1, -1, -1):
        bits = bytearray(mask_row)
        for x in range(w):
            if px[x, y][3] < 128:
                bits[x >> 3] |= 0x80 >> (x & 7)
        and_rows.append(bytes(bits))
    return hdr + xor + b"".join(and_rows)

entries = []
payloads = []
for s in sorted(plan):
    im = pils[s]
    if s <= 48:
        data = dib(im)
    else:
        import io
        buf = io.BytesIO()
        im.save(buf, format="PNG", optimize=True)
        data = buf.getvalue()
    payloads.append((s, data))

import io
ico = io.BytesIO()
ico.write(struct.pack("<HHH", 0, 1, len(payloads)))
offset = 6 + 16 * len(payloads)
for s, data in payloads:
    ico.write(struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
for s, data in payloads:
    ico.write(data)
open(os.path.join(ASSETS, "icon.ico"), "wb").write(ico.getvalue())

print("sizes:", sorted(plan))
print("ico bytes:", os.path.getsize(os.path.join(ASSETS, "icon.ico")))
for n in ("icon-256.png", "icon-512.png", "icon-16.png"):
    print(n, os.path.getsize(os.path.join(ASSETS, n)))