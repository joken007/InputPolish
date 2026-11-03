# -*- coding: utf-8 -*-
"""InputPolish 矢量图标源（唯一事实来源）

运行：
  <bundled-python> tools/icons-gen.py

产出：
  assets/icon-*.png, assets/icon.ico   应用图标 / 托盘图标（矢量绘制，没有「润」字）
  ext/icons/icon*.png                  浏览器扩展工具栏图标
  ext/ip-icons.js                      扩展用的内联 SVG 图标库（自动生成，勿手改）
  src/IconsData.cs                     C# 用的同一套路径数据（自动生成，勿手改）
  assets/icon-preview.png              图标总览（肉眼验形用）
  ICON-SPEC.md                         图标规范（谁改形状都从这里改）

坐标约定：24x24 网格，描边 2 单位、圆头圆角，纯单色（白/墨），
所有曲线用三次贝塞尔（C）表达，因此 C# 与 JS 只需要解析 M/L/H/V/C/Z。
"""
import io
import json
import math
import os
import struct

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
EXT = os.path.join(ROOT, "ext")
SRC = os.path.join(ROOT, "src")
SS = 4  # 超采样倍数

VIOLET = (124, 92, 255)
BLUE = (58, 132, 255)

# ---------------------------------------------------------------- 路径工具


def circle(cx, cy, r):
    k = 0.5522847498307936 * r
    return (
        "M%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f Z"
        % (
            cx - r, cy,
            cx - r, cy - k, cx - k, cy - r, cx, cy - r,
            cx + k, cy - r, cx + r, cy - k, cx + r, cy,
            cx + r, cy + k, cx + k, cy + r, cx, cy + r,
            cx - k, cy + r, cx - r, cy + k, cx - r, cy,
        )
    )


def ellipse(cx, cy, rx, ry):
    kx = 0.5522847498307936 * rx
    ky = 0.5522847498307936 * ry
    return (
        "M%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f Z"
        % (
            cx - rx, cy,
            cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry,
            cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy,
            cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry,
            cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy,
        )
    )


def rrect(x, y, w, h, r):
    k = 0.5522847498307936 * r
    x2, y2 = x + w, y + h
    return (
        "M%.2f %.2f"
        "L%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "L%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "L%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f"
        "L%.2f %.2f"
        "C%.2f %.2f %.2f %.2f %.2f %.2f Z"
        % (
            x + r, y, x2 - r, y,
            x2 - r + k, y, x2, y + r - k, x2, y + r,
            x2, y2 - r,
            x2, y2 - r + k, x2 - r + k, y2, x2 - r, y2,
            x + r, y2,
            x + r - k, y2, x, y2 - r + k, x, y2 - r,
            x, y + r,
            x, y + r - k, x + r - k, y, x + r, y,
        )
    )


def poly(pts):
    return "M" + "L".join("%.2f %.2f" % p for p in pts) + "Z"


def star(cx, cy, r, inner=0.32):
    pts = []
    for k in range(8):
        ang = -math.pi / 2 + k * math.pi / 4
        rr = r if k % 2 == 0 else r * inner
        pts.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
    return poly(pts)


def line(x1, y1, x2, y2):
    return "M%.2f %.2fL%.2f %.2f" % (x1, y1, x2, y2)


# ---------------------------------------------------------------- 图标定义
# 每一段：默认描边（2 单位，圆头），"f:" 前缀 = 填充，"w1.6:" 前缀 = 该段线宽

NIB = (
    "M7.40 9.60"
    "C7.40 7.06 9.46 5.00 12.00 5.00"
    "C14.54 5.00 16.60 7.06 16.60 9.60"
    "L12.00 21.00Z"
)

ICONS = {
    # 品牌标记：钢笔尖（润色 / 编辑），应用图标、托盘、桌面悬浮球待命态都用它
    "brand": [
        NIB,
        "w1.5:" + line(12, 11.6, 12, 18.3),
        "w1.5:" + circle(12, 9.7, 1.45),
    ],
    # 托盘 / 小尺寸简化版：实心笔尖，16px 也认得出来
    "brand_small": ["f:" + NIB],
    # 翻译（整页 / 划词）：地球
    "translate": [
        circle(12, 12, 7.2),
        ellipse(12, 12, 3.4, 7.2),
        line(4.8, 12, 19.2, 12),
    ],
    # 总结：文稿 + 折角
    "summarize": [
        "M7.00 4.00L14.20 4.00L17.80 7.60L17.80 20.00L7.00 20.00Z",
        "M14.20 4.00L14.20 7.60L17.80 7.60",
        line(9.6, 12.2, 15.3, 12.2),
        line(9.6, 15.8, 13.4, 15.8),
    ],
    # 设置：滑杆
    "settings": [
        line(4.5, 7, 19.5, 7),
        line(4.5, 12, 19.5, 12),
        line(4.5, 17, 19.5, 17),
        "f:" + circle(9, 7, 2.1),
        "f:" + circle(15, 12, 2.1),
        "f:" + circle(8, 17, 2.1),
    ],
    # 撤销：回转箭头
    "undo": [
        "M8.8 5.2L4.6 9.4L8.8 13.6",
        "M4.6 9.4L14.0 9.4"
        "C16.8 9.4 19.1 11.7 19.1 14.5"
        "C19.1 17.3 16.8 19.6 14.0 19.6L9.2 19.6",
    ],
    # 退出：门 + 右向箭头
    "quit": [
        "M10.2 4.2L6.4 4.2L6.4 19.8L10.2 19.8",
        line(11.4, 12, 18.6, 12),
        "M15.4 8.8L18.6 12L15.4 15.2",
    ],
    # 启用：播放三角
    "play": ["f:" + poly([(8.2, 5.4), (18.6, 12.0), (8.2, 18.6)])],
    # 暂停：两根圆角竖条
    "pause": ["f:" + rrect(7.4, 5.2, 3.4, 13.6, 1.7), "f:" + rrect(13.2, 5.2, 3.4, 13.6, 1.7)],
    # 完成：对勾
    "check": ["w2.4:M5.0 12.6L10.0 17.6L19.0 7.0"],
    # 出错：圆 + 叹号
    "warning": [
        circle(12, 12, 7.6),
        "w2.2:" + line(12, 7.4, 12, 13.0),
        "f:" + circle(12, 16.3, 1.2),
    ],
    # 已暂停 / 关闭：圆 + 斜杠
    "off": [circle(12, 12, 7.6), "M6.6 17.4L17.4 6.6"],
    # 处理中：缺口圆弧（旋转动画用）
    "spinner": [
        "M12 4.6C16.1 4.6 19.4 7.9 19.4 12C19.4 16.1 16.1 19.4 12 19.4"
        "C8.7 19.4 5.9 17.3 5.1 14.4",
    ],
    # 页面大纲：项目符号列表
    "outline": [
        "f:" + circle(6, 7, 1.5),
        "f:" + circle(6, 12, 1.5),
        "f:" + circle(6, 17, 1.5),
        line(10.2, 7, 19, 7),
        line(10.2, 12, 19, 12),
        line(10.2, 17, 15.8, 17),
    ],
    # 导出：下载到托盘
    "export": [
        line(12, 4.2, 12, 14.6),
        "M7.8 10.6L12 14.8L16.2 10.6",
        "M5.4 16.8L5.4 18.6"
        "C5.4 19.4 6.0 20.0 6.8 20.0L17.2 20.0"
        "C18.0 20.0 18.6 19.4 18.6 18.6L18.6 16.8",
    ],
    # 笔记：书签
    "note": ["M7.6 4.6L16.4 4.6L16.4 19.6L12.0 16.2L7.6 19.6Z"],
    # 编辑提示词：铅笔
    "pen": [
        "M4.6 19.4L5.4 15.4L15.7 5.1"
        "C16.6 4.2 18.0 4.2 18.9 5.1"
        "C19.8 6.0 19.8 7.4 18.9 8.3L8.6 18.6Z",
        "w1.4:" + line(5.4, 15.4, 8.6, 18.6),
    ],
    # 新建：加号
    "plus": ["w2.4:" + line(12, 5.6, 12, 18.4), "w2.4:" + line(5.6, 12, 18.4, 12)],
    # 复制：两张叠起来的纸
    "copy": [
        "M9.0 5.4L16.0 5.4C17.1 5.4 18.0 6.3 18.0 7.4L18.0 14.4",
        rrect(4.6, 8.6, 10.8, 10.8, 1.9),
    ],
    # 关闭：叉
    "close": ["M6.6 6.6L17.4 17.4", "M17.4 6.6L6.6 17.4"],
    # 提问：对话气泡 + 三点
    "chat": [
        rrect(4.6, 5.2, 14.8, 10.6, 3.0),
        "M8.6 15.8L8.6 19.4L12.6 15.8",
        "f:" + circle(9.2, 10.5, 1.05),
        "f:" + circle(12.0, 10.5, 1.05),
        "f:" + circle(14.8, 10.5, 1.05),
    ],
    # 搜索 / 筛选：放大镜
    "search": [circle(10.8, 10.8, 5.6), "M15.0 15.0L19.6 19.6"],
    # 双语对照：两栏（左原文、右译文）
    "columns": [rrect(4.6, 4.8, 6.2, 14.4, 1.8), rrect(13.2, 4.8, 6.2, 14.4, 1.8)],
}

# 小尺寸（<=32px）用简化版本，避免细线糊在一起
SMALL_VARIANT = {"brand": "brand_small"}


# ---------------------------------------------------------------- 路径解析


def flatten(d, steps=30):
    """把 M/L/H/V/C/Z 路径拆成若干条折线（绝对坐标）。"""
    toks = []
    i = 0
    while i < len(d):
        c = d[i]
        if c in "MmLlHhVvCcZz":
            toks.append(c)
            i += 1
        elif c in " ,":
            i += 1
        else:
            j = i
            while j < len(d) and d[j] not in "MmLlHhVvCcZz ,":
                j += 1
            toks.append(d[i:j])
            i = j

    subs = []
    cur = None
    x = y = 0.0
    sx = sy = 0.0
    i = 0

    def num():
        nonlocal i
        v = float(toks[i])
        i += 1
        return v

    while i < len(toks):
        t = toks[i]
        i += 1
        if t in "Mm":
            nx, ny = num(), num()
            if t == "m":
                nx, ny = nx + x, ny + y
            if cur:
                subs.append(cur)
            cur = {"pts": [(nx, ny)], "closed": False}
            x, y = nx, ny
            sx, sy = nx, ny
        elif t in "Ll":
            nx, ny = num(), num()
            if t == "l":
                nx, ny = nx + x, ny + y
            cur["pts"].append((nx, ny))
            x, y = nx, ny
        elif t in "Hh":
            nx = num()
            if t == "h":
                nx += x
            cur["pts"].append((nx, y))
            x = nx
        elif t in "Vv":
            ny = num()
            if t == "v":
                ny += y
            cur["pts"].append((x, ny))
            y = ny
        elif t in "Cc":
            x1, y1, x2, y2, nx, ny = num(), num(), num(), num(), num(), num()
            if t == "c":
                x1, y1 = x1 + x, y1 + y
                x2, y2 = x2 + x, y2 + y
                nx, ny = nx + x, ny + y
            p0 = (x, y)
            for k in range(1, steps + 1):
                s = k / float(steps)
                m = 1.0 - s
                bx = m ** 3 * p0[0] + 3 * m * m * s * x1 + 3 * m * s * s * x2 + s ** 3 * nx
                by = m ** 3 * p0[1] + 3 * m * m * s * y1 + 3 * m * s * s * y2 + s ** 3 * ny
                cur["pts"].append((bx, by))
            x, y = nx, ny
        elif t in "Zz":
            if cur:
                cur["closed"] = True
                subs.append(cur)
                cur = None
            x, y = sx, sy
    if cur:
        subs.append(cur)
    return subs


def split_part(part):
    fill = False
    width = 2.0
    while True:
        if part.startswith("f:"):
            fill, part = True, part[2:]
        elif part.startswith("w") and ":" in part[:7]:
            head, rest = part.split(":", 1)
            try:
                width = float(head[1:])
            except ValueError:
                break
            part = rest
        else:
            break
    return fill, width, part


def draw_parts(img, parts, color, scale, dx=0.0, dy=0.0, stroke_scale=1.0):
    dr = ImageDraw.Draw(img)
    for raw in parts:
        fill, width, d = split_part(raw)
        wpx = max(1, int(round(width * scale * stroke_scale)))
        for sp in flatten(d):
            pts = [(dx + px * scale, dy + py * scale) for px, py in sp["pts"]]
            if fill:
                if len(pts) >= 3:
                    dr.polygon(pts, fill=color)
            else:
                seq = pts + [pts[0]] if sp["closed"] else pts
                if len(seq) < 2:
                    continue
                # 逐段粗线 + 顶点圆点 = 圆头圆角描边。
                # 不用 Pillow 的 joint="curve"：折线点密时它会画出「毛刺」。
                r = wpx / 2.0
                for a, b in zip(seq, seq[1:]):
                    dr.line([a, b], fill=color, width=wpx)
                for cx, cy in seq:
                    dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)


# ---------------------------------------------------------------- 应用图标


def gradient(size, a, b):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    t = (x + y) / (2.0 * (size - 1))
    t = np.clip(t, 0, 1)
    t = t * t * (3 - 2 * t)
    out = np.zeros((size, size, 3), dtype=np.uint8)
    for i in range(3):
        out[:, :, i] = (a[i] + (b[i] - a[i]) * t).astype(np.uint8)
    return Image.fromarray(out, "RGB").convert("RGBA")


def squircle_mask(size, radius_ratio=0.2237):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * radius_ratio), fill=255)
    return m


def app_icon(size, ss=SS):
    """应用 / 托盘 / 扩展工具栏图标：渐变方 plate + 白色钢笔尖（+ 高光星）。"""
    small = size <= 32
    S = size * ss
    plate = gradient(S, VIOLET, BLUE)

    hi = Image.new("L", (S, S), 0)
    hd = ImageDraw.Draw(hi)
    hd.ellipse([-int(S * 0.30), -int(S * 0.42), int(S * 0.86), int(S * 0.72)], fill=52)
    hi = hi.filter(ImageFilter.GaussianBlur(S * 0.10))
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    body.alpha_composite(plate)
    body.alpha_composite(Image.composite(white, Image.new("RGBA", (S, S), (0, 0, 0, 0)), hi))

    # 内描边，增加厚度
    ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    inset = max(1, int(S * 0.006))
    rd.rounded_rectangle(
        [inset, inset, S - 1 - inset, S - 1 - inset],
        radius=int(S * 0.2237),
        outline=(255, 255, 255, 44),
        width=max(1, int(S * 0.008)),
    )
    body.alpha_composite(ring)

    # 钢笔尖：24 网格中心 (12, 13) -> plate 中心
    scale = S / 24.0 * (0.80 if small else 0.72)
    dx = S / 2.0 - 12.0 * scale
    dy = S / 2.0 - 13.0 * scale
    parts = ICONS["brand_small"] if small else ICONS["brand"]
    glyph = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    if not small:
        # 小尺寸是实心笔尖，不用阴影，免得糊
        shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        draw_parts(shadow, parts, (18, 12, 52, 110), scale, dx, dy + S * 0.012, 1.0)
        shadow = shadow.filter(ImageFilter.GaussianBlur(max(1, S * 0.012)))
        body.alpha_composite(shadow)
    draw_parts(glyph, parts, (255, 255, 255, 255), scale, dx, dy, 1.0)
    body.alpha_composite(glyph)

    if not small:
        cx, cy = S * 0.795, S * 0.200
        glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        r = S * 0.095
        gd.ellipse([cx - r * 1.9, cy - r * 1.9, cx + r * 1.9, cy + r * 1.9], fill=(180, 220, 255, 60))
        glow = glow.filter(ImageFilter.GaussianBlur(max(1, S * 0.040)))
        body.alpha_composite(glow)
        sp = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        draw_parts(sp, ["f:" + star(19.2, 4.9, 2.75)], (255, 255, 255, 255), S / 24.0, 0, 0)
        body.alpha_composite(sp)

    mask = squircle_mask(S)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(body, (0, 0), mask)
    return out.resize((size, size), Image.LANCZOS)


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


ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def build_app_icons():
    pils = {}
    for s in ICO_SIZES:
        im = app_icon(s, ss=(SS if s < 128 else 2))
        pils[s] = im
        im.save(os.path.join(ASSETS, "icon-%d.png" % s))
    app_icon(512, ss=2).save(os.path.join(ASSETS, "icon-512.png"))
    app_icon(180, ss=3).save(os.path.join(ASSETS, "icon-180.png"))

    payloads = []
    for s in ICO_SIZES:
        im = pils[s]
        if s <= 48:
            data = dib(im)
        else:
            buf = io.BytesIO()
            im.save(buf, format="PNG", optimize=True)
            data = buf.getvalue()
        payloads.append((s, data))

    ico = io.BytesIO()
    ico.write(struct.pack("<HHH", 0, 1, len(payloads)))
    offset = 6 + 16 * len(payloads)
    for s, data in payloads:
        ico.write(struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    for s, data in payloads:
        ico.write(data)
    with open(os.path.join(ASSETS, "icon.ico"), "wb") as f:
        f.write(ico.getvalue())

    icons_dir = os.path.join(EXT, "icons")
    os.makedirs(icons_dir, exist_ok=True)
    for s in (16, 32, 48, 128):
        pils.get(s, app_icon(s)).save(os.path.join(icons_dir, "icon%d.png" % s))

    # 图标总览（肉眼验形用）：每个图标 120px
    names = list(ICONS.keys())
    sheet = Image.new("RGBA", (len(names) * 120, 120), (255, 255, 255, 255))
    for i, name in enumerate(names):
        parts = ICONS[name]
        tile = Image.new("RGBA", (120, 120), (255, 255, 255, 255))
        draw_parts(tile, parts, (32, 38, 54, 255), 120 / 24.0 * 0.78, 120 * 0.11, 120 * 0.11, 1.0)
        sheet.alpha_composite(tile, (i * 120, 0))
    sheet.save(os.path.join(ASSETS, "icon-preview.png"))


# ---------------------------------------------------------------- 扩展 JS

def build_js():
    lines = [
        "// AUTO-GENERATED by tools/icons-gen.py — 不要手改，改 tools/icons-gen.py 后重新生成。",
        "// 24x24 网格的矢量图标（纯 path，用 currentColor 上色）。",
        "(function () {",
        "  'use strict';",
        "  var RAW = {",
    ]
    for name, parts in ICONS.items():
        lines.append("    %s: %s," % (json.dumps(name), json.dumps(parts, ensure_ascii=False)))
    lines += [
        "  };",
        "  var NS = 'http://www.w3.org/2000/svg';",
        "",
        "  function parts(name) { return RAW[name] || RAW.brand; }",
        "",
        "  // 生成本地 <svg> 节点：size 像素、sw 描边倍数",
        "  function make(name, opts) {",
        "    opts = opts || {};",
        "    var size = opts.size || 18;",
        "    var sw = opts.strokeScale || 1;",
        "    var svg = document.createElementNS(NS, 'svg');",
        "    svg.setAttribute('viewBox', '0 0 24 24');",
        "    svg.setAttribute('width', String(size));",
        "    svg.setAttribute('height', String(size));",
        "    svg.setAttribute('aria-hidden', 'true');",
        "    svg.setAttribute('focusable', 'false');",
        "    svg.style.display = 'block';",
        "    svg.style.flex = '0 0 auto';",
        "    parts(name).forEach(function (raw) {",
        "      var fill = false;",
        "      var w = 2;",
        "      var d = raw;",
        "      while (true) {",
        "        if (d.indexOf('f:') === 0) { fill = true; d = d.slice(2); }",
        "        else if (/^w[0-9.]+:/.test(d)) {",
        "          var k = d.indexOf(':');",
        "          w = parseFloat(d.slice(1, k));",
        "          d = d.slice(k + 1);",
        "        } else break;",
        "      }",
        "      var p = document.createElementNS(NS, 'path');",
        "      p.setAttribute('d', d);",
        "      if (fill) {",
        "        p.setAttribute('fill', 'currentColor');",
        "      } else {",
        "        p.setAttribute('fill', 'none');",
        "        p.setAttribute('stroke', 'currentColor');",
        "        p.setAttribute('stroke-width', String(w * sw));",
        "        p.setAttribute('stroke-linecap', 'round');",
        "        p.setAttribute('stroke-linejoin', 'round');",
        "      }",
        "      svg.appendChild(p);",
        "    });",
        "    return svg;",
        "  }",
        "",
        "  // 生成 HTML 字符串（塞 innerHTML 用）",
        "  function markup(name, opts) {",
        "    var el = make(name, opts);",
        "    return el.outerHTML;",
        "  }",
        "",
        "  function has(name) { return Object.prototype.hasOwnProperty.call(RAW, name); }",
        "",
        "  window.IPIcons = { raw: RAW, make: make, markup: markup, has: has };",
        "})();",
        "",
    ]
    with open(os.path.join(EXT, "ip-icons.js"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


# ---------------------------------------------------------------- C# 数据

CS_NAMES = {
    "brand": "Brand",
    "brand_small": "BrandSmall",
    "translate": "Translate",
    "summarize": "Summarize",
    "settings": "Settings",
    "undo": "Undo",
    "quit": "Quit",
    "play": "Play",
    "pause": "Pause",
    "check": "Check",
    "warning": "Warning",
    "off": "Off",
    "spinner": "Spinner",
    "outline": "Outline",
    "export": "Export",
    "note": "Note",
    "pen": "Pen",
    "plus": "Plus",
    "copy": "Copy",
    "close": "Close",
    "chat": "Chat",
    "search": "Search",
    "columns": "Columns",
}


def build_cs():
    lines = [
        "// AUTO-GENERATED by tools/icons-gen.py — 不要手改，改 tools/icons-gen.py 后重新生成。",
        "// 24x24 网格矢量图标：每段用 '|' 分隔，'f:' = 填充，'w<数字>:' = 该段线宽。",
        "namespace InputPolish",
        "{",
        "    internal static class IconsData",
        "    {",
    ]
    for name, parts in ICONS.items():
        lines.append('        public const string %s = "%s";' % (CS_NAMES.get(name, name), "|".join(parts)))
    lines += [
        "",
        "        /// <summary>小尺寸（<=32px）用的简化图标 id；没有简化版就返回原名。</summary>",
        "        public static string SmallVariant(string name)",
        "        {",
        '            if (name == "brand") return "brand_small";',
        "            return name;",
        "        }",
        "    }",
        "}",
        "",
    ]
    with open(os.path.join(SRC, "IconsData.cs"), "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write("\n".join(lines))


# ---------------------------------------------------------------- 规格文档

def build_spec():
    rows = []
    for name, parts in ICONS.items():
        joined = "".join(parts)
        note = "小尺寸简化版（实心）" if name == "brand_small" else ""
        rows.append("| `%s` | %d | %s | `%s` |" % (name, len(parts), note, joined[:110] + ("…" if len(joined) > 110 else "")))
    text = """# InputPolish 图标规范（ICON-SPEC）

> 这个文件由 `tools/icons-gen.py` 生成，改图标形状请改那个脚本再重新生成，别手改这里。

## 一、为什么不用字了

以前应用图标、托盘、悬浮球、网页悬浮球、划词胶囊、铅笔/加号用的都是**单个汉字或符号**（润 / 译 / 纲 / 导 / ✎ / ＋）。
汉字当图标有三个问题：小尺寸糊成一团、和系统字体强绑定、语言一变就失效。
现在全部换成 **24x24 网格的矢量线条图标**：同一套 path 数据同时喂给 C#（GDI+ 解析）和浏览器扩展（内联 SVG），
所以 exe 和网页里看到的是同一套形状。

## 二、绘制约定

- 画布 `viewBox="0 0 24 24"`，安全区在内切 2 单位
- 描边默认 **2 单位**、`round` 端头与转角；需要更细的用 `w1.4:` 之类的段级覆盖
- 纯单色：深色界面用白、浅色界面用墨（`currentColor`）；只有应用图标是「白色线条 + 紫蓝渐变方 plate」
- 曲线只用三次贝塞尔（`C`），C# 侧只需解析 `M/L/H/V/C/Z`
- 小于等于 32px 时用 `brand_small`（实心笔尖，去掉细线和孔），保证 16px 托盘图标不糊

## 三、图标清单

| id | 段数 | 备注 | 路径（前 110 字符） |
| --- | --- | --- | --- |
%s

## 四、各处用法

| 位置 | 用什么 | 说明 |
| --- | --- | --- |
| 应用图标 / 托盘 / 浏览器工具栏 | `brand`（<=32px 用 `brand_small`） | 紫蓝渐变 squircle plate + 白色钢笔尖 + 高光星 |
| 桌面悬浮球 待命 | `brand` | 白色钢笔尖 |
| 桌面悬浮球 处理中 | `spinner` + 进度数字 | 圆弧旋转，数字是进度不是图标 |
| 桌面悬浮球 成功 / 出错 / 暂停 | `check` / `warning` / `off` | 状态用形状区分，不再用字 |
| 悬浮球展开按钮 | `pause`/`play`、`translate`、`summarize`、`settings`、`undo`、`quit` | 图标 + 中文短标签（标签是文字，不是图标） |
| 划词胶囊 | `translate` + 「划词翻译」 | 旁边「存笔记」用 `note` |
| 网页悬浮球 | `translate`；译完变 `check` | 处理中是 `spinner` |
| 网页 mini 按钮 | 大纲 `outline` / 导出 `export` / 显示方式 `columns` | 圆形按钮 |
| 卡片按钮 | 复制 `copy` / 关闭 `close` / 存笔记 `note` / 问 `chat` | 图标 + 短标签 |
| 提示词芯片 | 铅笔 `pen` / 加号 `plus` | —— |
| 大纲侧栏 | 筛选 `search`、关闭 `close` | —— |

## 五、重新生成

```
<bundled-python> tools/icons-gen.py
```

会同时刷新：`assets/icon-*.png`、`assets/icon.ico`、`ext/icons/icon*.png`、
`ext/ip-icons.js`、`src/IconsData.cs`、`assets/icon-preview.png`。
""" % ("\n".join(rows))
    with open(os.path.join(ROOT, "ICON-SPEC.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def main():
    build_app_icons()
    build_js()
    build_cs()
    build_spec()
    print("icons:", len(ICONS))
    for n in ("icon.ico", "icon-256.png", "icon-16.png", "icon-preview.png"):
        p = os.path.join(ASSETS, n)
        print(n, os.path.getsize(p) if os.path.exists(p) else "MISSING")
    print("ip-icons.js", os.path.getsize(os.path.join(EXT, "ip-icons.js")))
    print("IconsData.cs", os.path.getsize(os.path.join(SRC, "IconsData.cs")))


if __name__ == "__main__":
    main()
