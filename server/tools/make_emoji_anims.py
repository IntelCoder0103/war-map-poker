#!/usr/bin/env python3
"""
Animates the built-in emojis: reads the still pictures in art/emoji/<id>.png (drawn with Codex, see
make_emoji_art.py) and writes assets/emoji/<id>.png, a horizontal strip of square frames (frame 0 on
the left). That's the format the server makes from an uploaded GIF, but with smooth (8-bit)
transparency, which a GIF can't have. It also writes "frames" and "fps" into the manifest.

    python3 tools/make_emoji_anims.py                   # all of them
    python3 tools/make_emoji_anims.py fire joy          # just these
    python3 tools/make_emoji_anims.py --sheet out.png   # also a contact sheet of every 3rd frame
    python3 tools/make_emoji_anims.py --gif DIR         # also sample .gif files (for trying uploads)

Needs Pillow, and (to keep downloads small) the imagequant package: pip install imagequant. It
saves each strip as a 256-colour PNG with smooth transparency, the way pngquant does, about a
quarter of the size and hard to tell apart. Without it the strips are saved in full colour.

All motion is done in code, in the spirit of Telegram's animated emojis: squash and
stretch with overshoot, and parts that move on their own where a picture allows it (blinking
eyelids, a chattering jaw, a waving flag, a bulging bicep, a heart that splits), plus effects
drawn on top (tears, steam, sparkles, Zzz, confetti, coins).
"""
import json
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

try:
    import imagequant
except ImportError:  # optional: full-colour strips, about 4x bigger
    imagequant = None

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.path.normpath(os.path.join(HERE, '..'))
STILL = os.path.join(SERVER, 'art', 'emoji')
EMOJI = os.path.join(SERVER, 'assets', 'emoji')
FONT = os.path.normpath(os.path.join(SERVER, '..', 'client', 'Assets', '_Project', 'Resources', 'Fonts', 'LilitaOne.ttf'))

W = 256           # frames are drawn at this size...
OUT = 128         # ...and shrunk to this (FRAME_SIZE in src/chat/images.js)
N = 24            # frames per loop
FPS = 16          # so a loop lasts 1.5 s

INK = (70, 30, 12, 255)          # the dark brown outline the pictures use
TEAR = (70, 175, 245, 255)
GOLD = (255, 205, 50, 255)
SPARK = (255, 250, 210, 255)

PI = math.pi
TAU = 2 * math.pi


# ---------------------------------------------------------------- timing helpers (t in [0, 1))

def wave(t, k=1, phase=0.0):
    return math.sin(TAU * (k * t + phase))


def bump(t, at, width):
    """A smooth bump centred on `at`, wrapping around the loop."""
    d = min(abs(t - at), 1 - abs(t - at))
    return math.exp(-(d / width) ** 2)


def spring(t, at, freq=3.0, decay=6.0):
    """A kick at `at` that rings down (wraps round the loop)."""
    u = (t - at) % 1.0
    return math.exp(-decay * u) * math.sin(TAU * freq * u)


def ease_out(u):
    u = min(1.0, max(0.0, u))
    return 1 - (1 - u) ** 3


def window(t, start, end):
    """0..1 progress through [start, end), or None outside it."""
    if start <= t < end:
        return (t - start) / (end - start)
    return None


def blink_amount(t, at, length=0.16):
    """Eyelid closure 0..1 for a blink starting at `at` (down, held shut a moment, up)."""
    u = window(t, at, at + length)
    if u is None:
        return 0.0
    return min(1.0, 1.6 * math.sin(PI * u))


# ---------------------------------------------------------------- geometry

def aff(scale=1.0, sx=1.0, sy=1.0, angle=0.0, pivot=(128, 128), dx=0.0, dy=0.0):
    """Inverse map (output point -> source point) of: scale about `pivot`, rotate `angle` degrees
    (clockwise on screen), then move by (dx, dy)."""
    a = math.radians(angle)
    c, s = math.cos(a), math.sin(a)
    kx, ky = scale * sx, scale * sy
    px, py = pivot

    def inv(x, y):
        u, v = x - px - dx, y - py - dy
        u, v = c * u + s * v, -s * u + c * v
        return u / kx + px, v / ky + py
    return inv


def chain(*maps):
    """chain(outer, ..., inner): the inverse of applying inner first, then ..., then outer."""
    def inv(x, y):
        for m in maps:
            x, y = m(x, y)
        return x, y
    return inv


def warp(img, inv, grid=16):
    """Draws img through an inverse map, on a grid of small quads (exact for affine maps).
    Done premultiplied, so edges stay clean."""
    pm = img.convert('RGBa')
    step = W // grid
    data = []
    for gy in range(grid):
        for gx in range(grid):
            x0, y0, x1, y1 = gx * step, gy * step, (gx + 1) * step, (gy + 1) * step
            q = inv(x0, y0) + inv(x0, y1) + inv(x1, y1) + inv(x1, y0)
            data.append(((x0, y0, x1, y1), q))
    return pm.transform((W, W), Image.MESH, data, resample=Image.BICUBIC).convert('RGBA')


def place(img, **kw):
    return warp(img, aff(**kw))


def bulge(center, radius, amount):
    """Inverse map that swells the area round `center` (amount > 0), with a smooth falloff."""
    cx, cy = center

    def inv(x, y):
        dx, dy = x - cx, y - cy
        r = math.hypot(dx, dy)
        if r >= radius or r == 0:
            return x, y
        f = 1 - amount * (1 - (r / radius) ** 2) ** 2
        return cx + dx * f, cy + dy * f
    return inv


# ---------------------------------------------------------------- layers and masks

def blank():
    return Image.new('RGBA', (W, W), (0, 0, 0, 0))


def over(*layers):
    out = layers[0].copy()
    for layer in layers[1:]:
        out.alpha_composite(layer)
    return out


def cut(img, mask):
    """img with its alpha multiplied by mask (an L image)."""
    out = img.copy()
    out.putalpha(ImageChops.multiply(img.split()[3], mask))
    return out


def erase(img, mask):
    return cut(img, ImageChops.invert(mask))


def mask_of(draw_fn, feather=0.0):
    m = Image.new('L', (W, W), 0)
    draw_fn(ImageDraw.Draw(m))
    return m.filter(ImageFilter.GaussianBlur(feather)) if feather else m


def rect_mask(x0, y0, x1, y1, feather=0.0):
    return mask_of(lambda d: d.rectangle([x0, y0, x1, y1], fill=255), feather)


def ellipse_mask(box, feather=0.0):
    return mask_of(lambda d: d.ellipse(list(box), fill=255), feather)


def components(img, threshold=128):
    """The separate opaque parts of a picture, as L masks, biggest first."""
    a = img.split()[3].point(lambda v: 255 if v >= threshold else 0)
    px = a.load()
    seen = set()
    found = []
    for y in range(W):
        for x in range(W):
            if px[x, y] and (x, y) not in seen:
                stack, pts = [(x, y)], []
                seen.add((x, y))
                while stack:
                    cx, cy = stack.pop()
                    pts.append((cx, cy))
                    for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                        if 0 <= nx < W and 0 <= ny < W and px[nx, ny] and (nx, ny) not in seen:
                            seen.add((nx, ny))
                            stack.append((nx, ny))
                m = Image.new('L', (W, W), 0)
                mp = m.load()
                for p in pts:
                    mp[p] = 255
                found.append((len(pts), m))
    found.sort(key=lambda f: -f[0])
    # soften back to the picture's own edge
    return [ImageChops.multiply(m.filter(ImageFilter.MaxFilter(5)), img.split()[3].point(lambda v: 255 if v else 0)) for _, m in found]


def tinted(img, color, amount):
    r, g, b, a = img.split()
    mixed = Image.blend(Image.merge('RGB', (r, g, b)), Image.new('RGB', img.size, color[:3]), amount)
    return Image.merge('RGBA', (*mixed.split(), a))


def glow(img, color, strength, radius=10, grow=1.0):
    """A soft halo in `color` behind the picture's shape."""
    a = img.split()[3]
    if grow != 1.0:
        a = place(Image.merge('RGBA', (a, a, a, a)), scale=grow).split()[3]
    a = a.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: int(v * strength))
    g = Image.new('RGBA', (W, W), color[:3] + (0,))
    g.putalpha(a)
    return g


def shine(img, t, start=0.0, length=0.45, width=40, region=None, strength=170):
    """A bright diagonal band that sweeps across the picture's opaque parts."""
    u = window(t, start, start + length)
    if u is None:
        return img
    band = Image.new('L', (W, W), 0)
    x = -120 + u * (W + 240)
    ImageDraw.Draw(band).polygon([(x, 0), (x + width, 0), (x + width - 120, W), (x - 120, W)], fill=strength)
    band = band.filter(ImageFilter.GaussianBlur(8))
    m = ImageChops.multiply(band, img.split()[3])
    if region is not None:
        m = ImageChops.multiply(m, region)
    layer = Image.new('RGBA', (W, W), (255, 255, 240, 0))
    layer.putalpha(m)
    return over(img, layer)


def face_color(img, box):
    """The skin colour round an eye: the median of saturated, mid-bright pixels on rings round it."""
    x0, y0, x1, y1 = box
    cx, cy, rx, ry = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2 + 7, (y1 - y0) / 2 + 7
    px = img.load()
    cols = []
    for k in range(72):
        a = TAU * k / 72
        for rr in (1.0, 1.2, 1.4):
            x, y = int(cx + math.cos(a) * rx * rr), int(cy + math.sin(a) * ry * rr)
            if 0 <= x < W and 0 <= y < W:
                r, g, b, al = px[x, y]
                if al > 250 and 100 < (r + g + b) / 3 < 240 and max(r, g, b) - min(r, g, b) > 50:
                    cols.append((r, g, b))
    if not cols:
        return (250, 200, 40, 255)
    cols.sort(key=sum)
    return cols[len(cols) // 2] + (255,)


def lids(img, boxes, closure, color=None, droop_only=False):
    """Paints eyelids over the eyes. closure 0 = open, 1 = shut (then a lash curve is drawn).
    droop_only: a heavy lid that comes down part way, with no lash curve."""
    if closure <= 0.02:
        return img
    out = img.copy()
    for box in boxes:
        col = color or face_color(img, box)
        x0, y0, x1, y1 = box[0] - 3, box[1] - 3, box[2] + 3, box[3] + 3
        h = y1 - y0
        c = min(closure, 1.0)
        edge = y0 + h * c
        layer = blank()
        ImageDraw.Draw(layer).ellipse([x0, y0, x1, y1], fill=col)
        out.alpha_composite(cut(layer, ImageChops.multiply(ellipse_mask((x0, y0, x1, y1)), rect_mask(0, 0, W, edge))))
        if c >= 0.97 and not droop_only:
            cy = (y0 + y1) / 2 + h * 0.05
            pts = [(x0 + 2 + (x1 - x0 - 4) * i / 10, cy + math.sin(PI * i / 10) * h * 0.18) for i in range(11)]
            ImageDraw.Draw(out).line(pts, fill=INK, width=5, joint='curve')
        else:
            line = blank()
            ImageDraw.Draw(line).rectangle([x0, edge - 2.5, x1, edge + 2.5], fill=INK)
            out.alpha_composite(cut(line, ellipse_mask((x0 + 1, y0, x1 - 1, y1))))
    return out


# ---------------------------------------------------------------- little drawings (effects)

def outlined_poly(d, pts, fill, width=3.0):
    d.polygon(pts, fill=INK)
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    inner = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        r = math.hypot(dx, dy) or 1
        k = max(0.0, (r - width) / r)
        inner.append((cx + dx * k, cy + dy * k))
    d.polygon(inner, fill=fill)


def teardrop(d, x, y, r, direction=90.0, color=TEAR):
    """A drop with its round end at (x, y), moving towards `direction` (degrees, 90 = straight down);
    its pointed tail trails behind."""
    a = math.radians(direction + 180)
    pts = []
    for i in range(28):
        th = TAU * i / 28
        rr = r * (1 + 1.3 * max(0.0, math.cos(th)) ** 6)
        pts.append((x + math.cos(th + a) * rr, y + math.sin(th + a) * rr))
    outlined_poly(d, pts, color, width=2.5)
    d.ellipse([x - r * 0.55, y - r * 0.55, x - r * 0.1, y - r * 0.1], fill=(255, 255, 255, 220))


def sparkle(d, x, y, r, color=SPARK, rot=0.0, points=4, inner=0.28, alpha=255):
    if r < 1:
        return
    pts = []
    for i in range(points * 2):
        rr = r if i % 2 == 0 else r * inner
        a = rot + PI * i / points
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    d.polygon(pts, fill=color[:3] + (alpha,))


def sparkles(layer, spots, t, period=1.0):
    """Twinkling stars at spots [(x, y, phase, r)]: each one grows and shrinks once per period."""
    d = ImageDraw.Draw(layer)
    for x, y, ph, r in spots:
        u = ((t / period) + ph) % 1.0
        k = max(0.0, math.sin(PI * min(1.0, u * 2.2)))
        if k > 0.03:
            sparkle(d, x, y, 1.35 * r * k, rot=u * 0.8)
    return layer


def heart_pts(x, y, s, angle=0.0):
    a = math.radians(angle)
    c, sn = math.cos(a), math.sin(a)
    pts = []
    for i in range(40):
        th = TAU * i / 40
        hx = 16 * math.sin(th) ** 3
        hy = -(13 * math.cos(th) - 5 * math.cos(2 * th) - 2 * math.cos(3 * th) - math.cos(4 * th))
        hx, hy = hx * s / 16, hy * s / 16
        pts.append((x + c * hx - sn * hy, y + sn * hx + c * hy))
    return pts


def mini_heart(d, x, y, s, angle=0.0, alpha=255):
    d.polygon(heart_pts(x, y, s + 3, angle), fill=INK[:3] + (alpha,))
    d.polygon(heart_pts(x, y, s, angle), fill=(240, 30, 50, alpha))


PUFF = ((0, 0, 1.0), (-0.7, 0.35, 0.7), (0.7, 0.3, 0.75), (0.1, -0.6, 0.7))


def puff(d, x, y, r, alpha=255):
    for ox, oy, k in PUFF:
        cx, cy, rr = x + ox * r, y + oy * r, r * k
        d.ellipse([cx - rr - 3, cy - rr - 3, cx + rr + 3, cy + rr + 3], fill=INK[:3] + (alpha,))
    for ox, oy, k in PUFF:
        cx, cy, rr = x + ox * r, y + oy * r, r * k
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=(250, 238, 235, alpha))


def text(layer, s, x, y, size, color, angle=0.0, alpha=255, stroke=4):
    if size < 4:
        return
    font = ImageFont.truetype(FONT, int(size))
    tmp = Image.new('RGBA', (int(size * 2.4), int(size * 2.4)), (0, 0, 0, 0))
    ImageDraw.Draw(tmp).text((tmp.width / 2, tmp.height / 2), s, font=font, fill=color[:3] + (255,), anchor='mm',
                             stroke_width=stroke, stroke_fill=INK)
    if angle:
        tmp = tmp.rotate(-angle, resample=Image.BICUBIC)
    if alpha < 255:
        tmp.putalpha(tmp.split()[3].point(lambda v: v * alpha // 255))
    layer.alpha_composite(tmp, (int(x - tmp.width / 2), int(y - tmp.height / 2)))


def coin(d, x, y, r, spin, alpha=255):
    w = max(2.0, r * abs(math.cos(spin)))
    d.ellipse([x - w - 2.5, y - r - 2.5, x + w + 2.5, y + r + 2.5], fill=INK[:3] + (alpha,))
    d.ellipse([x - w, y - r, x + w, y + r], fill=(250, 200, 40, alpha))
    if w > 5:
        d.ellipse([x - w * 0.6, y - r * 0.6, x + w * 0.6, y + r * 0.6], outline=(205, 140, 20, alpha), width=2)


CONFETTI = [(230, 40, 60), (40, 120, 230), (250, 200, 30), (60, 190, 80), (240, 100, 200), (255, 140, 0)]


def confetto(d, x, y, w, h, spin, color, alpha=255):
    c, s = math.cos(spin), math.sin(spin)
    h = h * max(0.25, abs(math.cos(spin * 1.7)))   # tumbling
    pts = [(x + c * ox - s * oy, y + s * ox + c * oy) for ox, oy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
    d.polygon(pts, fill=color + (alpha,))


def impact_lines(d, cx, cy, r0, r1, angles, width=6, color=GOLD, alpha=255):
    for pass_, wd, col in ((0, width + 4, INK), (1, width, color)):
        for a in angles:
            a = math.radians(a)
            d.line([(cx + math.cos(a) * r0, cy + math.sin(a) * r0), (cx + math.cos(a) * r1, cy + math.sin(a) * r1)],
                   fill=col[:3] + (alpha,), width=wd)


def ring(d, cx, cy, r, width, color, alpha):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color[:3] + (alpha,), width=max(1, int(width)))


# ---------------------------------------------------------------- the animations
# Each is f(src, t) -> a W x W frame; t runs 0..1 over the loop, and the loop is seamless.
# Coordinates are in the 256 px still picture.

def thumbs_up(src, t):
    # a quick upward flick from the wrist that rings down, and a glint on the thumb
    k = spring(t, 0.05, freq=2.2, decay=4.5)
    img = place(src, scale=0.9 + 0.04 * max(0, k), angle=-14 * k, pivot=(105, 228), dy=6)
    return over(img, sparkles(blank(), [(160, 30, 0.95, 28)], t))


def clap(src, t):
    # two claps a loop: the hands squeeze together and spring back, with sparks flying out
    squeeze = 0.0
    fx = blank()
    d = ImageDraw.Draw(fx)
    for at in (0.1, 0.5):
        squeeze += bump(t, at, 0.05)
        u = window(t, at, at + 0.22)
        if u is not None:
            e = ease_out(u)
            impact_lines(d, 128, 120, 92 + 40 * e, 112 + 50 * e, [-60, -30, 200, 230], alpha=int(255 * (1 - u)))
    img = place(src, scale=0.86 + 0.05 * squeeze, sx=1 - 0.12 * squeeze, sy=1 + 0.04 * squeeze, pivot=(128, 200), dy=10)
    return over(img, fx)


def joy(src, t):
    # a belly laugh: quick bounces with squash, a little rocking, tears spurting out both sides
    b = abs(wave(t, 4))
    img = place(src, scale=0.86, sx=1 + 0.05 * (1 - b), sy=1 - 0.05 * (1 - b) + 0.03 * b,
                angle=4 * wave(t, 1), pivot=(128, 240), dy=10 - 12 * b)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for side in (-1, 1):
        for i in range(3):
            u = (t * 2 + i / 3 + (0.15 if side > 0 else 0)) % 1.0
            x = 128 + side * (84 + 38 * u)
            y = 150 - 34 * u + 110 * u * u
            teardrop(d, x, y, 13 * (1 - 0.3 * u), direction=math.degrees(math.atan2(-34 + 220 * u, side * 38)))
    return over(img, fx)


def rofl(src, t):
    # rolling about: a big rock from side to side with a hop, tears flying off
    img = place(src, scale=0.82, angle=22 * wave(t, 1), dy=8 - 12 * abs(wave(t, 2)))
    fx = blank()
    d = ImageDraw.Draw(fx)
    for i in range(4):
        u = (t * 2 + i / 4) % 1.0
        side = 1 if i % 2 == 0 else -1
        x = 128 + side * (84 + 32 * u)
        y = 100 + side * 26 - 40 * u + 120 * u * u
        teardrop(d, x, y, 12 * (1 - 0.3 * u), direction=90 if u > 0.4 else (60 if side > 0 else 120))
    return over(img, fx)


def cool(src, t):
    # a slow, cool nod, and a shine sliding across the sunglasses
    nod = bump(t, 0.25, 0.1) + bump(t, 0.75, 0.1) * 0.6
    img = place(src, scale=0.92, sy=1 - 0.04 * nod, sx=1 + 0.02 * nod, angle=-3 * wave(t, 1), pivot=(128, 240), dy=6 + 4 * nod)
    dark = img.convert('L').point(lambda v: 255 if v < 70 else 0)
    img = shine(img, t, start=0.35, length=0.4, width=34, region=ImageChops.multiply(rect_mask(35, 95, 225, 170), dark), strength=200)
    return over(img, sparkles(blank(), [(196, 112, 0.25, 18)], t))


THINK_EYES = [(79, 75, 115, 105), (142, 60, 178, 97)]


def thinking(src, t):
    # the head tilts one way then the other, a blink, and a question mark pops up
    face = lids(src, THINK_EYES, blink_amount(t, 0.62))
    img = place(face, scale=0.88, angle=6 * wave(t, 1, 0.25), pivot=(100, 220), dy=12)
    fx = blank()
    u = window(t, 0.1, 0.9)
    if u is not None:
        pop = ease_out(min(1, u * 4)) * (1 if u < 0.85 else (1 - u) / 0.15)
        text(fx, '?', 208, 52 - 6 * wave(t, 2), 64 * pop, (255, 220, 60), angle=12 * wave(t, 1))
    return over(img, fx)


def scream(src, t):
    # frozen in horror: stretched tall, trembling, going pale blue, cold sweat flying
    rnd = random.Random(int(t * N) * 13 + 3)
    stretch = 0.5 + 0.5 * wave(t, 1, -0.25)
    img = place(src, scale=0.88, sx=1 - 0.05 * stretch, sy=1 + 0.07 * stretch, pivot=(128, 240),
                dx=rnd.uniform(-3, 3), dy=10 + rnd.uniform(-2, 2))
    img = tinted(img, (120, 170, 255), 0.12 * stretch)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for i, (x, side) in enumerate(((40, -1), (216, 1))):
        u = (t + i * 0.5) % 1.0
        teardrop(d, x + side * 14 * u, 70 + 30 * u + 50 * u * u, 12, direction=90, color=(150, 215, 255, 255))
    return over(img, fx)


def sob(src, t):
    # sobbing: shuddering heaves, and tears running down the streams and dripping off
    heave = sum(bump(t, at, 0.07) for at in (0.2, 0.34, 0.7, 0.84))
    img = place(src, scale=0.9, sy=1 - 0.05 * heave, sx=1 + 0.03 * heave, pivot=(128, 240), dy=8 - 6 * heave)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for x in (44, 212):
        for i in range(3):
            u = (t * 2 + i / 3) % 1.0
            if u < 0.85:
                teardrop(d, x + (2 if x > 128 else -2) * u, 150 + 90 * u, 12)
    return over(img, fx)


def rage(src, t):
    # boiling over: hard shaking, going redder, steam blasting out in puffs
    rnd = random.Random(int(t * N) * 31 + 5)
    boil = 0.5 + 0.5 * wave(t, 2)
    img = place(src, scale=0.86 + 0.03 * boil, angle=rnd.uniform(-3, 3), dx=rnd.uniform(-6, 6), dy=10 + rnd.uniform(-4, 4))
    img = tinted(img, (230, 0, 0), 0.08 + 0.14 * boil)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for i, (x, y, dirx) in enumerate(((200, 52, 1), (56, 62, -1))):
        u = (t * 2 + i * 0.5) % 1.0
        puff(d, x + dirx * 28 * u, y - 22 * u, 10 + 12 * u, alpha=int(255 * (1 - u) ** 0.6))
    return over(img, fx)


def fire(src, t):
    # a living flame: the tip sways more than the base, it flares, and embers float up
    def flame(x, y):
        k = max(0.0, 1 - y / 235) ** 1.6
        return x - k * (14 * wave(t, 1) + 6 * wave(t, 3, 0.2) + 4 * math.sin(y * 0.05 + TAU * 2 * t)), y
    flare = 0.5 + 0.5 * wave(t, 2)
    img = warp(src, chain(aff(scale=0.9, sy=0.97 + 0.06 * flare, sx=1.02 - 0.03 * flare, pivot=(128, 235), dy=8), flame), grid=32)
    img = tinted(img, (255, 240, 140), 0.08 * flare)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(7)
    for _ in range(7):
        x0, ph, speed, r = rnd.uniform(80, 180), rnd.random(), rnd.choice([1, 2]), rnd.uniform(5, 7)
        u = (t * speed + ph) % 1.0
        y = 120 - u * 112
        x = x0 + 10 * math.sin(TAU * (u * 2 + ph))
        al = int(255 * min(1, 1.6 * (1 - u)) * min(1, u * 5))
        rr = r * (1 - 0.4 * u)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=(255, 120, 10, al))
        d.ellipse([x - rr * 0.55, y - rr * 0.55, x + rr * 0.55, y + rr * 0.55], fill=(255, 235, 120, al))
    return over(glow(img, (255, 150, 30), 0.25 + 0.2 * flare, radius=12), img, fx)


def muscle(src, t):
    # flexing: the bicep swells up twice and the arm pumps, with a gleam on the steel
    flex = bump(t, 0.22, 0.09) + bump(t, 0.62, 0.09)
    img = warp(src, chain(aff(scale=0.9, angle=-6 * flex, pivot=(210, 200), dy=8), bulge((125, 165), 75, 0.22 * flex)), grid=32)
    return over(img, sparkles(blank(), [(120, 125, 0.0, 20), (95, 40, 0.5, 14)], t, period=0.5))


def pray(src, t):
    # a gentle bow, a warm glow that breathes, sparkles round the hands
    breathe = 0.5 + 0.5 * wave(t, 1)
    img = place(src, scale=0.9, pivot=(128, 240), sy=1 - 0.02 * breathe, dy=10 - 3 * breathe)
    halo = glow(img, (255, 210, 90), 0.35 + 0.4 * breathe, radius=14, grow=1.06)
    return over(halo, img, sparkles(blank(), [(60, 60, 0.0, 16), (200, 50, 0.33, 14), (215, 150, 0.66, 12)], t))


def handshake(src, t):
    # a firm shake: up and down twice with a slight twist, motion lines at the sides
    k = wave(t, 2)
    img = place(src, scale=0.9, angle=5 * k, dy=-12 * k)
    fx = blank()
    d = ImageDraw.Draw(fx)
    a = int(160 * abs(k))
    for x in (20, 236):
        for dy in (-14, 14):
            d.line([(x, 110 + dy - 10 * k), (x, 130 + dy - 10 * k)], fill=INK[:3] + (a,), width=5)
    return over(img, fx)


def party(src, t):
    # the popper kicks back, and confetti bursts out of its mouth and drifts down
    kick = ease_out(min(1, t * 5)) * (1 - min(1, max(0, t - 0.2) * 2.5))
    img = place(src, scale=0.88, angle=-10 * kick, pivot=(50, 220), dx=-6 * kick, dy=10 + 6 * kick)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(3)
    for _ in range(18):
        a = math.radians(rnd.uniform(-100, -5))
        v = rnd.uniform(45, 95)
        x = 165 + math.cos(a) * v * ease_out(t)
        y = 100 + math.sin(a) * v * ease_out(t) + 70 * t * t
        al = int(255 * (1 - t) ** 0.7 * min(1, t * 12))
        confetto(d, x, y, rnd.uniform(14, 22), rnd.uniform(8, 12), rnd.uniform(0, PI) + t * rnd.uniform(6, 14), rnd.choice(CONFETTI), al)
    return over(img, fx)


def crown(src, t):
    # hops, lands with a squash, and its jewels twinkle as a shine passes over the gold
    hop = math.sin(PI * t / 0.45) if t < 0.45 else 0.0
    land = bump(t, 0.47, 0.05)
    img = place(src, scale=0.88, sx=1 + 0.08 * land, sy=1 - 0.08 * land + 0.04 * hop, pivot=(128, 225), dy=10 - 26 * hop,
                angle=6 * hop * wave(t, 1))
    img = shine(img, t, start=0.5, length=0.4)
    return over(img, sparkles(blank(), [(70, 140, 0.55, 14), (128, 132, 0.7, 16), (190, 140, 0.85, 14)], t))


def trophy(src, t):
    # raised in triumph: a lift and a wobble, a shine across the gold, twinkles
    lift = bump(t, 0.25, 0.14)
    img = place(src, scale=0.9 + 0.04 * lift, angle=5 * spring(t, 0.3, 2, 5), pivot=(128, 230), dy=8 - 12 * lift)
    img = shine(img, t, start=0.3, length=0.45)
    return over(img, sparkles(blank(), [(60, 40, 0.0, 18), (205, 55, 0.33, 15), (150, 140, 0.66, 11)], t))


def swords(src, t):
    # clash: the swords jolt, sparks burst from where they cross, then a gleam runs along them
    hit = bump(t, 0.12, 0.04)
    img = place(src, scale=0.88 + 0.06 * hit, angle=4 * spring(t, 0.12, 3, 7), dy=6)
    img = shine(img, t, start=0.45, length=0.4, width=30)
    fx = blank()
    d = ImageDraw.Draw(fx)
    u = window(t, 0.1, 0.4)
    if u is not None:
        e, al = ease_out(u), int(255 * (1 - u))
        sparkle(d, 128, 140, 40 * (1 - u) + 10, alpha=al, points=6, inner=0.35)
        for k in range(8):
            a, r = TAU * k / 8 + 0.3, 20 + 70 * e
            x, y = 128 + math.cos(a) * r, 140 + math.sin(a) * r
            d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(255, 220, 90, al))
    return over(img, fx)


def shield(src, t):
    # braced for a blow: a quick shove forward, a ring of impact, then a glint
    hit = bump(t, 0.2, 0.05)
    img = place(src, scale=0.88 + 0.08 * hit, dy=8)
    img = shine(img, t, start=0.45, length=0.4, width=36)
    fx = blank()
    d = ImageDraw.Draw(fx)
    u = window(t, 0.18, 0.5)
    if u is not None:
        ring(d, 128, 128, 90 + 40 * ease_out(u), 6 * (1 - u) + 1, (255, 230, 120), int(220 * (1 - u)))
    sparkles(fx, [(70, 60, 0.8, 18)], t)
    return over(img, fx)


def skull(src, t):
    # cackling: the jaw chatters, the head wobbles, the eye sockets glow red
    jaw = rect_mask(70, 206, 190, 256, feather=1.5)
    chatter = max(0.0, wave(t, 4))
    face = over(erase(src, jaw), place(cut(src, jaw), dy=9 * chatter))
    move = dict(scale=0.88, angle=7 * wave(t, 1), pivot=(128, 220), dy=8 - 4 * abs(wave(t, 2)))
    img = place(face, **move)
    eyes = blank()
    d = ImageDraw.Draw(eyes)
    k = 0.5 + 0.5 * wave(t, 2)
    for x in (96, 160):
        d.ellipse([x - 12, 155, x + 12, 179], fill=(255, 60, 30, int(200 * k)))
    eyes = place(eyes.filter(ImageFilter.GaussianBlur(5)), **move)
    return over(img, cut(eyes, img.split()[3]))


def clover(src, t):
    # a lucky charm: swaying on its stem with a little twirl, sparkles all round
    img = place(src, scale=0.9, sx=0.75 + 0.25 * abs(math.cos(TAU * t)), angle=10 * wave(t, 1), pivot=(150, 245), dy=6)
    return over(img, sparkles(blank(), [(50, 50, 0.0, 16), (210, 40, 0.25, 14), (220, 170, 0.5, 12), (40, 180, 0.75, 13)], t))


def sleeping(src, t):
    # fast asleep: slow breathing, the Z's swell with each snore, and a little z floats away
    breathe = 0.5 + 0.5 * wave(t, 1)
    r, g, b = src.convert('RGB').split()
    blue = ImageChops.subtract(b, r).point(lambda v: 255 if v > 60 else 0)
    zmask = ImageChops.multiply(blue.filter(ImageFilter.MaxFilter(9)), rect_mask(118, 0, 256, 100))
    zmask = ImageChops.multiply(zmask, src.split()[3])
    face = place(src, scale=0.86, sy=1 + 0.04 * breathe, sx=1 - 0.015 * breathe, pivot=(128, 240), dy=12)
    # the Z's grow from where they touch the head (never smaller than drawn, so they cover the originals)
    zs = place(cut(src, zmask), scale=0.86, pivot=(128, 240), dy=12)
    zs = place(zs, scale=1 + 0.18 * breathe, angle=-5 * breathe, pivot=(150, 95))
    fx = blank()
    u = (t + 0.3) % 1.0
    al = int(255 * min(1, u * 5) * min(1, (1 - u) * 3))
    text(fx, 'z', 212 + 18 * u, 78 - 55 * u, 28 + 14 * u, (70, 150, 255), angle=-15, alpha=al, stroke=3)
    return over(face, zs, fx)


def clown(src, t):
    # honk honk! the red nose squeezes and pops, the face bounces, honk lines fly out
    honk = bump(t, 0.2, 0.06) + bump(t, 0.35, 0.06)
    b = abs(wave(t, 2))
    img = warp(src, chain(aff(scale=0.88, sy=1 + 0.03 * b, pivot=(128, 240), dy=8 - 8 * b), bulge((128, 158), 34, 0.35 * honk)), grid=32)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for at in (0.2, 0.35):
        u = window(t, at, at + 0.15)
        if u is not None:
            impact_lines(d, 128, 160, 50 + 20 * u, 64 + 26 * u, [200, 340], width=4, color=(255, 255, 255), alpha=int(255 * (1 - u)))
    return over(img, fx)


def mind_blown(src, t):
    # the top of the head erupts: the mushroom cloud swells and rolls, debris flies
    boom = 0.5 + 0.5 * wave(t, 1, -0.25)
    top = rect_mask(0, 0, 256, 112, feather=3)
    cloud = warp(cut(src, top), aff(scale=1.0 + 0.12 * boom, sy=1 + 0.05 * boom, pivot=(128, 112), angle=3 * wave(t, 2)))
    rnd = random.Random(int(t * N))
    face = over(place(erase(src, top), dx=rnd.uniform(-1.5, 1.5)), cloud)
    img = place(face, scale=0.86, pivot=(128, 245), dy=12)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(11)
    for _ in range(6):
        a = math.radians(rnd.uniform(-170, -10))
        u = (t + rnd.random()) % 1.0
        x = 128 + math.cos(a) * (40 + 90 * u)
        y = 90 + math.sin(a) * (40 + 70 * u) + 60 * u * u
        s = rnd.uniform(5, 8)
        d.polygon([(x - s, y), (x, y - s), (x + s, y + s * 0.4)], fill=(255, 160, 40, int(255 * (1 - u))))
    return over(img, fx)


def devil(src, t):
    # an evil chuckle: quick little bounces, a sly tilt, a dark purple aura
    b = abs(wave(t, 3))
    img = place(src, scale=0.86, angle=-5 * wave(t, 1), sy=1 - 0.03 * (1 - b), pivot=(128, 240), dy=10 - 8 * b)
    return over(glow(img, (170, 40, 230), 0.4 + 0.3 * abs(wave(t, 1)), radius=12, grow=1.05), img)


def hundred(src, t):
    # stamped on: slams in big, wobbles, then a shine and sparkles
    u = window(t, 0.0, 0.2)
    s = 1.04 - 0.16 * ease_out(u) if u is not None else 0.88 + 0.04 * spring(t, 0.2, 2.5, 6)
    img = place(src, scale=s, angle=-4 * spring(t, 0.2, 2, 6), dy=4)
    img = shine(img, t, start=0.35, length=0.4)
    return over(img, sparkles(blank(), [(225, 40, 0.4, 18), (40, 190, 0.7, 14)], t))


def partying(src, t):
    # party time: bobbing to the beat, the blower unrolls with a toot, confetti rains down
    beat = abs(wave(t, 2))
    blow = bump(t, 0.3, 0.12)
    blower = rect_mask(168, 118, 256, 200, feather=2)
    face = over(erase(src, blower), warp(cut(src, blower), aff(sx=1 + 0.25 * blow, sy=1 + 0.06 * blow, pivot=(168, 160))))
    img = place(face, scale=0.86, angle=6 * wave(t, 1), pivot=(128, 240), dy=10 - 8 * beat)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(5)
    for _ in range(12):
        x0, ph, col = rnd.uniform(10, 246), rnd.random(), rnd.choice(CONFETTI)
        u = (t + ph) % 1.0
        confetto(d, x0 + 10 * wave(u, 1), -10 + 270 * u, 12, 7, u * 9 + ph * 6, col, int(255 * min(1, (1 - u) * 4)))
    return over(img, fx)


SALUTE_EYES = [(95, 121, 112, 152), (189, 123, 206, 154)]


def salute(src, t):
    # snaps to attention: a sharp straighten that rings, a blink, the star on the helmet glints
    k = spring(t, 0.08, freq=2.5, decay=6)
    face = lids(src, SALUTE_EYES, blink_amount(t, 0.6))
    img = place(face, scale=0.88 + 0.03 * max(0, k), angle=-5 * k, pivot=(140, 240), dy=10)
    return over(img, sparkles(blank(), [(150, 42, 0.0, 18)], t))


def eyes(src, t):
    # looking round: a glance left, a glance right, and a blink
    look = 0.0
    for at, v in ((0.1, -1), (0.45, 1)):
        u = window(t, at, at + 0.3)
        if u is not None:
            look = v * math.sin(PI * u) ** 0.5
    face = lids(src, [(19, 48, 125, 197), (131, 48, 231, 197)], blink_amount(t, 0.8, 0.14), color=(238, 236, 244, 255))
    return place(face, scale=0.9, pivot=(128, 122), dx=10 * look, angle=3 * look)


def broken_heart(src, t):
    # heartbreak: the two halves drift apart, wobble, and snap back together with a shudder
    parts = components(src)
    left, right = parts[0], parts[1] if len(parts) > 1 else Image.new('L', (W, W), 0)
    if right.getbbox() and left.getbbox()[0] > right.getbbox()[0]:
        left, right = right, left
    apart = math.sin(PI * t / 0.55) if t < 0.55 else 0.0
    snap = spring(t, 0.55, 3, 8)
    lh = warp(cut(src, left), aff(angle=-5 * apart, pivot=(120, 230), dx=-8 * apart - 3 * snap))
    rh = warp(cut(src, right), aff(angle=5 * apart, pivot=(136, 230), dx=8 * apart + 3 * snap))
    return place(over(lh, rh), scale=0.8, dy=12 + 4 * apart)


CHICKEN_EYES = [(59, 124, 105, 173), (124, 119, 177, 173)]


def chicken(src, t):
    # a nervous wreck: trembling, a scared double blink, sweat dripping down
    rnd = random.Random(int(t * N) * 7)
    closed = max(blink_amount(t, 0.3, 0.12), blink_amount(t, 0.45, 0.12))
    face = lids(src, CHICKEN_EYES, closed, color=(250, 248, 245, 255))
    img = place(face, scale=0.88, dx=rnd.uniform(-2.5, 2.5), dy=8 + rnd.uniform(-1.5, 1.5), angle=rnd.uniform(-2, 2))
    fx = blank()
    d = ImageDraw.Draw(fx)
    for i in range(2):
        u = (t + i * 0.5) % 1.0
        teardrop(d, 212 + 6 * u, 128 + 100 * u * u, 12, direction=90, color=(150, 215, 255, 255))
    return over(img, fx)


def heart(src, t):
    # lub-dub, with little hearts popping out on the beats
    beat = bump(t, 0.12, 0.06) + 0.7 * bump(t, 0.32, 0.06)
    img = place(src, scale=0.82 + 0.14 * beat)
    fx = blank()
    d = ImageDraw.Draw(fx)
    for a, at in ((-130, 0.12), (-50, 0.15), (-90, 0.33)):
        u = window(t, at, at + 0.55)
        if u is not None:
            r, ar = 70 + 70 * ease_out(u), math.radians(a)
            mini_heart(d, 128 + math.cos(ar) * r, 128 + math.sin(ar) * r, 14 * (1 - 0.4 * u), angle=a + 90,
                       alpha=int(255 * min(1, (1 - u) * 2.5)))
    return over(img, fx)


def war_chest(src, t):
    # treasure! the chest jumps, gold coins spin up out of it, the gold twinkles
    hop = bump(t, 0.1, 0.07)
    img = place(src, scale=0.88, sy=1 - 0.05 * hop, pivot=(128, 240), dy=10 - 14 * hop)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(9)
    for i in range(4):
        vx, vy, ph = rnd.uniform(-60, 60), rnd.uniform(140, 190), rnd.random()
        u = window(t, 0.08 + i * 0.06, 0.68 + i * 0.06)
        if u is not None:
            coin(d, 128 + vx * u, 90 - vy * u + 230 * u * u, 13, u * 12 + ph * 6, alpha=int(255 * min(1, (1 - u) * 4)))
    sparkles(fx, [(100, 70, 0.0, 14), (170, 90, 0.5, 12)], t, period=0.5)
    return over(img, fx)


def wink(src, t):
    # a cheeky wink: a head tilt with a bounce, and a twinkle at the winking eye
    k = spring(t, 0.15, 2, 5)
    img = place(src, scale=0.88 + 0.03 * max(0, k), angle=-8 * k, pivot=(128, 240), dy=10)
    return over(img, sparkles(blank(), [(200, 108, 0.1, 22)], t))


ROLL_EYES = [(36, 92, 113, 157), (142, 93, 217, 158)]


def eye_roll(src, t):
    # ugh: the lids drop to half-mast with a weary sigh (a sag and a tilt), then lift again
    u = window(t, 0.15, 0.85)
    sigh = math.sin(PI * u) if u is not None else 0.0
    face = lids(src, ROLL_EYES, 0.5 * sigh ** 0.6, droop_only=True)
    return place(face, scale=0.88, sy=1 - 0.04 * sigh, sx=1 + 0.02 * sigh, angle=-6 * sigh, pivot=(128, 240), dy=10 + 3 * sigh)


def smirk(src, t):
    # one eyebrow goes up, twice, with a smug little tilt
    dark = src.convert('L').point(lambda v: 255 if v < 120 else 0)
    brow = ImageChops.multiply(rect_mask(140, 50, 232, 96), dark).filter(ImageFilter.MaxFilter(5))
    raise_ = bump(t, 0.25, 0.07) + bump(t, 0.45, 0.07)
    fill = Image.new('RGBA', (W, W), face_color(src, (150, 60, 220, 90)))
    base = over(src, cut(fill, ImageChops.multiply(brow, src.split()[3])))
    face = over(base, place(cut(src, brow), dy=-9 * raise_, angle=-4 * raise_, pivot=(186, 75)))
    return place(face, scale=0.88, angle=4 * wave(t, 1), pivot=(128, 240), dy=10)


def bomb(src, t):
    # about to blow: the fuse fizzes, the bomb trembles and swells, glowing hotter
    rnd = random.Random(int(t * N) * 17 + 1)
    s = 0.86 + 0.04 * (0.5 + 0.5 * wave(t, 4))
    img = place(src, scale=s, dx=rnd.uniform(-2.5, 2.5), dy=6 + rnd.uniform(-2, 2), pivot=(120, 154))
    img = tinted(img, (255, 60, 20), 0.07 * (0.5 + 0.5 * wave(t, 4)))
    tipx, tipy = 120 + (198 - 120) * s, 154 + (46 - 154) * s + 6
    fx = blank()
    d = ImageDraw.Draw(fx)
    flick = 0.7 + 0.3 * rnd.random()
    sparkle(d, tipx, tipy, 32 * flick, (255, 230, 80), points=6, inner=0.4, rot=t * 9, alpha=230)
    sparkle(d, tipx, tipy, 15 * flick, (255, 255, 230), points=6, inner=0.5, rot=-t * 9)
    for _ in range(6):
        a, dist, r = rnd.uniform(0, TAU), rnd.uniform(20, 44), rnd.uniform(2, 3.5)
        x, y = tipx + math.cos(a) * dist, tipy + math.sin(a) * dist
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 200, 60, 255))
    return over(img, fx)


def white_flag(src, t):
    # surrender: the cloth ripples in the wind while the pole stays put
    def flutter(x, y):
        k = max(0.0, (x - 75) / 180)
        return x - 3 * k * wave(t, 1, -x / 120), y - 16 * k * math.sin(TAU * t - x * 0.045) - 4 * k
    pole = rect_mask(0, 0, 76, 256, feather=4)
    cloth = warp(erase(src, pole), flutter, grid=32)
    return place(over(cut(src, pole), cloth), scale=0.9, angle=2 * wave(t, 1), pivot=(50, 245), dy=8)


def bullseye(src, t):
    # a hit! the target jolts and wobbles, and a ring ripples out from the middle
    hit = spring(t, 0.08, 3, 6)
    img = place(src, scale=0.88 + 0.03 * max(0, hit), angle=4 * hit, dx=6 * hit, dy=6)
    fx = blank()
    u = window(t, 0.06, 0.5)
    if u is not None:
        ring(ImageDraw.Draw(fx), 112, 130, 20 + 100 * ease_out(u), 6 * (1 - u) + 1, (255, 255, 255), int(230 * (1 - u)))
    return over(img, fx)


TURTLE_EYES = [(84, 94, 120, 109), (31, 116, 60, 128)]


def turtle(src, t):
    # taking it slow: a gentle plod from side to side, and a lazy blink
    face = lids(src, TURTLE_EYES, blink_amount(t, 0.55, 0.22), color=(130, 200, 60, 255))
    return place(face, scale=0.9, angle=2.5 * wave(t, 1), pivot=(128, 245), dx=6 * wave(t, 1), dy=8 - 4 * abs(wave(t, 2)))


def cheers(src, t):
    # cheers! the mugs lean apart and clink together, foam splashing up
    split = rect_mask(0, 0, 128, 256)
    u = window(t, 0.0, 0.3)
    lean = math.sin(PI * u) if u is not None else 0.0
    clink = spring(t, 0.3, 3, 8)
    lm = warp(cut(src, split), aff(angle=-10 * lean + 2 * clink, pivot=(40, 240)))
    rm = warp(erase(src, split), aff(angle=10 * lean - 2 * clink, pivot=(216, 240)))
    img = place(over(lm, rm), scale=0.88, dy=10)
    fx = blank()
    d = ImageDraw.Draw(fx)
    rnd = random.Random(4)
    w_ = window(t, 0.28, 0.8)
    if w_ is not None:
        for _ in range(8):
            a, v = math.radians(rnd.uniform(-160, -20)), rnd.uniform(40, 80)
            x = 128 + math.cos(a) * v * ease_out(w_)
            y = 40 + math.sin(a) * v * ease_out(w_) + 80 * w_ * w_
            r = rnd.uniform(4, 7) * (1 - 0.4 * w_)
            al = int(255 * min(1, (1 - w_) * 2.5))
            d.ellipse([x - r - 2, y - r - 2, x + r + 2, y + r + 2], fill=INK[:3] + (al,))
            d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 250, 235, al))
        s = window(t, 0.28, 0.6)
        if s is not None:
            sparkle(d, 128, 60, 22 * math.sin(PI * s))
    return over(img, fx)


ANIMS = {
    'thumbs_up': thumbs_up, 'clap': clap, 'joy': joy, 'rofl': rofl, 'cool': cool, 'thinking': thinking,
    'scream': scream, 'sob': sob, 'rage': rage, 'fire': fire, 'muscle': muscle, 'pray': pray,
    'handshake': handshake, 'party': party, 'crown': crown, 'trophy': trophy, 'swords': swords,
    'shield': shield, 'skull': skull, 'clover': clover, 'sleeping': sleeping, 'clown': clown,
    'mind_blown': mind_blown, 'devil': devil, 'hundred': hundred, 'partying': partying, 'salute': salute,
    'eyes': eyes, 'broken_heart': broken_heart, 'chicken': chicken, 'heart': heart, 'war_chest': war_chest,
    'wink': wink, 'eye_roll': eye_roll, 'smirk': smirk, 'bomb': bomb, 'white_flag': white_flag,
    'bullseye': bullseye, 'turtle': turtle, 'cheers': cheers,
}


# ---------------------------------------------------------------- output

MARGIN = 4   # clear pixels kept round the edge (at W)


def fit(frames):
    """Shrinks and centres all frames by the same amount, so that everything any frame shows
    (the motion and the effects) stays inside the square. Faint glow fringes don't count."""
    box = None
    for f in frames:
        b = f.split()[3].point(lambda v: 255 if v > 60 else 0).getbbox()
        if b:
            box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    if box is None:
        return frames
    bw, bh = box[2] - box[0], box[3] - box[1]
    s = min(1.0, (W - 2 * MARGIN) / bw, (W - 2 * MARGIN) / bh)
    cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    if s > 0.995 and abs(cx - W / 2) < 1 and abs(cy - W / 2) < 1:
        return frames
    return [place(f, scale=s, pivot=(cx, cy), dx=W / 2 - cx, dy=W / 2 - cy) for f in frames]


def render(emoji_id):
    src = Image.open(os.path.join(STILL, emoji_id + '.png')).convert('RGBA')
    maker = ANIMS[emoji_id]
    frames = fit([maker(src, i / N) for i in range(N)])
    frames = [f.convert('RGBa').resize((OUT, OUT), Image.LANCZOS).convert('RGBA') for f in frames]
    strip = Image.new('RGBA', (OUT * N, OUT), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        strip.paste(f, (i * OUT, 0))
    save_small(strip, os.path.join(EMOJI, emoji_id + '.png'))
    return frames


def save_small(img, path):
    """Saves as a 256-colour PNG with an alpha palette when imagequant is installed (players
    download every emoji, often over mobile data), else as a full-colour PNG."""
    if imagequant is None:
        img.save(path, optimize=True)
        return
    q = imagequant.quantize_pil_image(img, dithering_level=1.0, max_colors=256, min_quality=0, max_quality=100)
    q.save(path, optimize=True)


def write_gif(frames, path):
    # GIF transparency is on/off, so the frames go on parchment (a sample for trying uploads)
    flat = []
    for f in frames:
        bg = Image.new('RGBA', f.size, (240, 226, 192, 255))
        bg.alpha_composite(f)
        flat.append(bg.convert('RGB').quantize(255))
    flat[0].save(path, save_all=True, append_images=flat[1:], duration=int(1000 / FPS), loop=0, disposal=2)


def update_manifest(ids):
    path = os.path.join(EMOJI, 'manifest.json')
    m = json.load(open(path, encoding='utf-8'))
    for e in m['emojis']:
        if e['id'] in ids:
            e['frames'], e['fps'] = N, FPS
    rows = ['    { ' + ', '.join(json.dumps(k) + ': ' + json.dumps(v, ensure_ascii=False) for k, v in e.items()) + ' }'
            for e in m['emojis']]
    body = ['{', '  "_comment": ' + json.dumps(m['_comment'], ensure_ascii=False) + ',', '  "emojis": [', ',\n'.join(rows), '  ]', '}']
    open(path, 'w', encoding='utf-8').write('\n'.join(body) + '\n')


def main():
    args = sys.argv[1:]
    if imagequant is None:
        print('note: imagequant is not installed (pip install imagequant), so the strips are saved in full colour, about 4x bigger')
    sheet_path = gif_dir = None
    if '--sheet' in args:
        i = args.index('--sheet')
        sheet_path = args[i + 1]
        del args[i:i + 2]
    if '--gif' in args:
        i = args.index('--gif')
        gif_dir = args[i + 1]
        del args[i:i + 2]
    ids = args or list(ANIMS)
    unknown = [i for i in ids if i not in ANIMS]
    if unknown:
        sys.exit('No animation for: ' + ', '.join(unknown))
    all_frames = {}
    for emoji_id in ids:
        all_frames[emoji_id] = render(emoji_id)
        size = os.path.getsize(os.path.join(EMOJI, emoji_id + '.png'))
        print(f'{emoji_id}: {N} frames @ {FPS} fps, {size // 1024} KB')
        if gif_dir:
            os.makedirs(gif_dir, exist_ok=True)
            write_gif(all_frames[emoji_id], os.path.join(gif_dir, emoji_id + '.gif'))
    update_manifest(set(ids))
    if sheet_path:
        cols = list(range(0, N, 3))
        sheet = Image.new('RGBA', (OUT * len(cols) + 110, OUT * len(ids)), (235, 222, 190, 255))
        d = ImageDraw.Draw(sheet)
        for r, emoji_id in enumerate(ids):
            d.text((4, r * OUT + 58), emoji_id, fill=(0, 0, 0, 255))
            for c, fi in enumerate(cols):
                sheet.alpha_composite(all_frames[emoji_id][fi], (110 + c * OUT, r * OUT))
        sheet.save(sheet_path)


if __name__ == '__main__':
    main()
