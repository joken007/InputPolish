
import os
from PIL import Image, ImageDraw, ImageFont

out = r"E:\脚本\input-polish\ext\icons"
os.makedirs(out, exist_ok=True)

FONT = r"C:\Windows\Fonts\msyhbd.ttc"

def make(size):
    S = 8  # supersample
    w = size * S
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    # vertical gradient mask circle
    grad = Image.new("RGB", (w, w), (0, 0, 0))
    d = ImageDraw.Draw(grad)
    c1 = (91, 109, 255)
    c2 = (168, 85, 247)
    for y in range(w):
        t = y / max(1, w - 1)
        # diagonal-ish gradient
        col = tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(3))
        d.line([(0, y), (w, y)], fill=col)
    mask = Image.new("L", (w, w), 0)
    md = ImageDraw.Draw(mask)
    md.ellipse([0, 0, w - 1, w - 1], fill=255)
    img.paste(grad, (0, 0), mask)

    # glyph
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, int(w * 0.62))
    text = "译"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    draw.text(((w - tw) / 2 - bbox[0], (w - th) / 2 - bbox[1] - w * 0.02), text, font=font, fill=(255, 255, 255, 255))

    return img.resize((size, size), Image.LANCZOS)

for s in (16, 32, 48, 128):
    p = os.path.join(out, "icon%d.png" % s)
    make(s).save(p)
    print("wrote", p)
