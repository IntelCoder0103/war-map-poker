#!/usr/bin/env python3
"""Draws the placeholder card pictures: Assets/_Project/Resources/Cards/Art/{code}.png for all 52 cards.

Each suit is an army with its own colour (hearts red, diamonds orange, clubs green, spades blue).
  2-10   that many soldiers in formation, laid out like the pips on a classic card
  J      a soldier with a star badge
  Q      a soldier with a small crown, sunburst behind
  K      a soldier with a big crown, gold sunburst behind
  A      a heraldic shield with the suit, crossed spears, sunburst behind

The pictures are made from the game's own art (Troops/soldier.png, UI/crown.png, UI/star.png,
UI/sunburst.png). Replace any file with real art; the game picks it up with no code change.

Usage:  python3 client/Tools/make_card_art.py      (needs Pillow and numpy)
"""
import math
import os
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, '..', 'Assets', '_Project', 'Resources')
OUT = os.environ.get('CARD_ART_OUT') or os.path.join(RES, 'Cards', 'Art')

W, H = 240, 450          # design size; pixel constants below are in these units
S = 2                    # drawn at 2x, then scaled down for smooth edges
CW, CH = W * S, H * S
OUT_W, OUT_H = 160, 300  # saved size: multiples of 4 (so phones can compress it), sharp at the size cards show

RANKS = ['2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K', 'A']
SUITS = {
    #       top tint          bottom tint       frame             army hue (deg)
    'h': ((255, 236, 233), (246, 196, 192), (205, 110, 108), 356),
    'd': ((255, 241, 222), (248, 210, 168), (212, 145, 80), 28),
    'c': ((232, 245, 229), (190, 222, 188), (96, 156, 104), 128),
    's': ((233, 240, 252), (192, 208, 236), (104, 130, 186), 212),
}


# ---------- suit shapes (same maths as CardArt.InSuit in the client) ----------

def in_heart(x, y):
    x = x * 1.12
    y = y * 1.18 + 0.12
    a = x * x + y * y - 1
    return a * a * a - x * x * y * y * y <= 0


def in_stem(x, y, y0, y1):
    t = np.clip((y - y0) / (y1 - y0), 0, 1)
    hw = 0.035 + 0.17 * (1 - t) * (1 - t)
    return (y >= y0) & (y <= y1) & (np.abs(x - 0.5) <= hw)


def in_circle(x, y, cx, cy, r):
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def suit_mask(suit, size):
    """Anti-aliased mask (L image) of the suit symbol, y up like the client code."""
    n = size * 4
    g = (np.arange(n) + 0.5) / n
    x, y = np.meshgrid(g, 1 - g)
    if suit == 'h':
        m = in_heart((x - 0.5) / 0.43, (y - 0.47) / 0.43)
    elif suit == 'd':
        dx, dy = np.abs(x - 0.5) / 0.37, np.abs(y - 0.5) / 0.46
        m = dx ** 0.88 + dy ** 0.88 <= 1
    elif suit == 's':
        m = in_heart((x - 0.5) / 0.42, (0.58 - y) / 0.42) | in_stem(x, y, 0.06, 0.42)
    else:
        m = (in_circle(x, y, 0.5, 0.70, 0.20) | in_circle(x, y, 0.285, 0.43, 0.20) |
             in_circle(x, y, 0.715, 0.43, 0.20) |
             ((x >= 0.40) & (x <= 0.60) & (y >= 0.42) & (y <= 0.62)) | in_stem(x, y, 0.06, 0.46))
    img = Image.fromarray((m * 255).astype(np.uint8), 'L')
    return img.resize((size, size), Image.LANCZOS)


# ---------- source art ----------

def load(rel):
    return Image.open(os.path.join(RES, rel)).convert('RGBA')


def trim(img):
    return img.crop(img.split()[3].point(lambda a: 255 if a > 8 else 0).getbbox())


SOLDIER = trim(load('Troops/soldier.png'))
CROWN = trim(load('UI/crown.png'))
STAR = trim(load('UI/star.png'))
SUNBURST = trim(load('UI/sunburst.png'))

# Where the helmet top sits in the trimmed soldier (for placing crowns and badges).
_a = np.array(SOLDIER.split()[3])
_cols = range(int(SOLDIER.width * 0.42), int(SOLDIER.width * 0.62))
HELMET_X = sum(_cols) / len(_cols) / SOLDIER.width
HELMET_Y = min(np.where(_a[:, c] > 128)[0].min() for c in _cols) / SOLDIER.height


def recolor(img, hue_deg):
    """Turns the soldier's blue cloth into the army colour (keeps shading and the gold trim)."""
    if hue_deg == 212:
        return img
    rgb = np.array(img.convert('RGB').convert('HSV')).astype(np.int16)
    a = np.array(img.split()[3])
    h, s, v = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    blue = (h >= 115) & (h <= 175) & (s > 50) & (v > 40)
    h[blue] = int(hue_deg / 360 * 255)
    s[blue] = np.minimum(255, s[blue] * 1.05).astype(np.int16)
    out = Image.fromarray(np.dstack([h, s, v]).astype(np.uint8), 'HSV').convert('RGBA')
    out.putalpha(Image.fromarray(a))
    return out


def tint(img, color, alpha=1.0):
    """Recolours an image to one colour, keeping its alpha (times `alpha`)."""
    solid = Image.new('RGBA', img.size, color + (255,))
    a = img.split()[3].point(lambda p: int(p * alpha))
    solid.putalpha(a)
    return solid


def paste(canvas, img, cx, bottom, height):
    """Pastes img scaled to `height`, centred on cx with its bottom at `bottom` (canvas pixels)."""
    w = max(1, round(img.width * height / img.height))
    im = img.resize((w, round(height)), Image.LANCZOS)
    canvas.alpha_composite(im, (round(cx - w / 2), round(bottom - height)))
    return w


def shadow(canvas, cx, y, w):
    sh = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(sh)
    d.ellipse([cx - w / 2, y - w * 0.11, cx + w / 2, y + w * 0.11], fill=(40, 30, 20, 70))
    canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(3 * S)))


def soldier_at(canvas, troop, cx, bottom, height):
    shadow(canvas, cx, bottom - height * 0.04, height * 0.62)
    return paste(canvas, troop, cx, bottom, height)


# ---------- backgrounds ----------

def background(suit):
    top, bottom, frame, _ = SUITS[suit]
    grad = np.zeros((CH, CW, 4), np.uint8)
    t = np.linspace(0, 1, CH)[:, None]
    for i in range(3):
        grad[..., i] = (top[i] * (1 - t) + bottom[i] * t).astype(np.uint8)
    grad[..., 3] = 255
    img = Image.fromarray(grad, 'RGBA')

    # soft light in the upper middle
    glow = Image.new('L', (CW, CH), 0)
    ImageDraw.Draw(glow).ellipse([CW * 0.05, CH * 0.05, CW * 0.95, CH * 0.6], fill=120)
    img = Image.composite(Image.new('RGBA', img.size, (255, 255, 255, 255)), img,
                          glow.filter(ImageFilter.GaussianBlur(60 * S)))

    # a faint suit symbol as a watermark
    wm = suit_mask(suit, int(CW * 0.9))
    wm = wm.point(lambda p: int(p * 0.10))
    layer = Image.new('RGBA', img.size, frame + (0,))
    layer.putalpha(0)
    mark = Image.new('RGBA', wm.size, frame + (255,))
    mark.putalpha(wm)
    img.alpha_composite(mark, (int(CW * 0.05), int(CH * 0.5 - wm.height / 2)))

    # gentle ground at the bottom
    ground = Image.new('RGBA', img.size, (0, 0, 0, 0))
    g = ImageDraw.Draw(ground)
    g.ellipse([-CW * 0.4, CH * 0.88, CW * 1.4, CH * 1.25], fill=frame + (60,))
    img.alpha_composite(ground.filter(ImageFilter.GaussianBlur(6 * S)))
    return img


def finish(img, suit):
    """Rounded corners and a thin frame."""
    frame = SUITS[suit][2]
    r = 18 * S
    mask = Image.new('L', img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, CW - 1, CH - 1], r, fill=255)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([S, S, CW - 1 - S, CH - 1 - S], r - S, outline=frame + (255,), width=3 * S)
    d.rounded_rectangle([8 * S, 8 * S, CW - 1 - 8 * S, CH - 1 - 8 * S], r - 8 * S, outline=frame + (110,), width=S)
    img.putalpha(mask)
    return img.resize((OUT_W, OUT_H), Image.LANCZOS)


# ---------- pictures ----------

L, C, R = 0.27, 0.5, 0.73
PIPS = {
    2: [(C, .40), (C, .86)],
    3: [(C, .30), (C, .58), (C, .86)],
    4: [(L, .44), (R, .44), (L, .86), (R, .86)],
    5: [(L, .36), (R, .36), (C, .61), (L, .86), (R, .86)],
    6: [(L, .30), (R, .30), (L, .58), (R, .58), (L, .86), (R, .86)],
    7: [(C, .27), (L, .47), (R, .47), (L, .66), (R, .66), (L, .86), (R, .86)],
    8: [(L, .25), (R, .25), (L, .45), (R, .45), (L, .66), (R, .66), (L, .86), (R, .86)],
    9: [(C, .22), (L, .38), (R, .38), (L, .54), (R, .54), (L, .70), (R, .70), (L, .86), (R, .86)],
    10: [(L, .22), (R, .22), (L, .38), (R, .38), (L, .54), (R, .54), (L, .70), (R, .70), (L, .86), (R, .86)],
}


def number_card(suit, n, troop):
    img = background(suit)
    h = CH * (0.30 if n <= 3 else 0.25 if n <= 6 else 0.21 if n <= 8 else 0.185)
    for x, y in sorted(PIPS[n], key=lambda p: p[1]):  # back rows first
        soldier_at(img, troop, CW * x, CH * y, h)
    return img


def sunburst(img, cx, cy, size, color, alpha):
    sb = SUNBURST.resize((int(size), int(size * SUNBURST.height / SUNBURST.width)), Image.LANCZOS)
    full = Image.new('RGBA', (sb.width, sb.height * 2), (0, 0, 0, 0))
    full.alpha_composite(sb, (0, 0))
    full.alpha_composite(sb.rotate(180), (0, sb.height))
    full = tint(full, color, alpha)
    img.alpha_composite(full, (int(cx - full.width / 2), int(cy - full.height / 2)))


def face_card(suit, rank, troop):
    img = background(suit)
    frame = SUITS[suit][2]
    cx, foot, h = CW * 0.5, CH * 0.88, CH * 0.56
    if rank == 'K':
        sunburst(img, cx, CH * 0.50, CW * 1.5, (255, 200, 60), 0.75)
    elif rank == 'Q':
        sunburst(img, cx, CH * 0.50, CW * 1.3, frame, 0.45)
    # a little mound to stand on
    mound = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(mound).ellipse([cx - CW * 0.42, foot - CH * 0.045, cx + CW * 0.42, foot + CH * 0.05],
                                  fill=frame + (120,))
    img.alpha_composite(mound.filter(ImageFilter.GaussianBlur(2 * S)))
    w = soldier_at(img, troop, cx, foot, h)
    left = cx - w / 2
    head_x, head_y = left + HELMET_X * w, foot - h + HELMET_Y * h
    if rank == 'K':
        paste(img, CROWN, head_x, head_y + h * 0.05, h * 0.26)
    elif rank == 'Q':
        paste(img, CROWN, head_x, head_y + h * 0.04, h * 0.17)
    else:  # J: a star badge above the helmet
        paste(img, STAR, head_x, head_y - h * 0.03, h * 0.20)
    return img


def shield_path(cx, top, w, h):
    pts = []
    for i in range(41):  # flat top, sides curving into a point
        t = i / 40
        x = cx - w / 2 + w * t
        pts.append((x, top))
    for i in range(41):
        t = i / 40
        y = top + h * 0.45 + (h * 0.55) * t
        x = cx + (w / 2) * math.cos(t * math.pi / 2) ** 0.8
        pts.append((x, y if i else top + h * 0.45))
    for i in range(41):
        t = 1 - i / 40
        y = top + h * 0.45 + (h * 0.55) * t
        x = cx - (w / 2) * math.cos(t * math.pi / 2) ** 0.8
        pts.append((x, y))
    return pts


def spear(d, x0, y0, x1, y1, width):
    d.line([(x0, y0), (x1, y1)], fill=(92, 60, 36, 255), width=int(width))
    ang = math.atan2(y1 - y0, x1 - x0)
    L = width * 4.2
    tip = (x1 + math.cos(ang) * L, y1 + math.sin(ang) * L)
    nx, ny = -math.sin(ang) * width * 1.4, math.cos(ang) * width * 1.4
    d.polygon([tip, (x1 + nx, y1 + ny), (x1 - nx, y1 - ny)], fill=(200, 208, 216, 255), outline=(70, 70, 80, 255))


def ace_card(suit, army):
    img = background(suit)
    frame = SUITS[suit][2]
    sunburst(img, CW * 0.5, CH * 0.48, CW * 1.6, (255, 205, 70), 0.8)
    d = ImageDraw.Draw(img)
    # crossed spears behind the shield
    sw = 7 * S
    spear(d, CW * 0.10, CH * 0.80, CW * 0.84, CH * 0.20, sw)
    spear(d, CW * 0.90, CH * 0.80, CW * 0.16, CH * 0.20, sw)
    # the shield: gold rim, army-coloured field, white suit
    sw_, sh_ = CW * 0.74, CH * 0.50
    top = CH * 0.24
    army_rgb = tuple(int(c) for c in Image.new('HSV', (1, 1), (int(army / 360 * 255), 190, 185)).convert('RGB').getpixel((0, 0)))
    sh = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon(shield_path(CW * 0.5 + 4 * S, top + 8 * S, sw_, sh_), fill=(30, 20, 10, 110))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(5 * S)))
    d = ImageDraw.Draw(img)
    d.polygon(shield_path(CW * 0.5, top, sw_, sh_), fill=(232, 168, 40, 255), outline=(150, 96, 20, 255))
    inset = 12 * S
    d.polygon(shield_path(CW * 0.5, top + inset, sw_ - inset * 2, sh_ - inset * 1.8), fill=army_rgb + (255,))
    # a lighter top half, for some depth
    hi = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(hi).polygon(shield_path(CW * 0.5, top + inset, sw_ - inset * 2, sh_ - inset * 1.8), fill=(255, 255, 255, 40))
    cut = Image.new('L', img.size, 0)
    ImageDraw.Draw(cut).rectangle([0, 0, CW, top + sh_ * 0.40], fill=255)
    hi.putalpha(Image.fromarray(np.minimum(np.array(hi.split()[3]), np.array(cut))))
    img.alpha_composite(hi)
    # the suit in white with a soft dark outline
    size = int(sw_ * 0.62)
    m = suit_mask(suit, size)
    ox, oy = int(CW * 0.5 - size / 2), int(top + sh_ * 0.40 - size / 2)
    outline = Image.new('RGBA', m.size, (40, 20, 10, 255))
    outline.putalpha(m.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.GaussianBlur(2)))
    img.alpha_composite(outline, (ox, oy))
    white = Image.new('RGBA', m.size, (255, 252, 240, 255))
    white.putalpha(m)
    img.alpha_composite(white, (ox, oy))
    # a crown on top of the shield
    paste(img, CROWN, CW * 0.5, top + 14 * S, CH * 0.13)
    return img


# ---------- Unity import settings ----------

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  textureFormat: 1
  maxTextureSize: 4096
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 1
    mipBias: -0.3
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 4096
    textureCompression: 1
    compressionQuality: 50
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(path):
    meta = path + '.meta'
    if os.path.exists(meta):
        return  # keep the GUID Unity already knows
    with open(meta, 'w') as f:
        f.write(META.format(guid=uuid.uuid4().hex))


def main():
    os.makedirs(OUT, exist_ok=True)
    for suit, (_, _, _, hue) in SUITS.items():
        troop = recolor(SOLDIER, hue)
        for rank in RANKS:
            if rank == 'A':
                img = ace_card(suit, hue)
            elif rank in 'JQK':
                img = face_card(suit, rank, troop)
            else:
                img = number_card(suit, 10 if rank == 'T' else int(rank), troop)
            path = os.path.join(OUT, rank + suit + '.png')
            finish(img, suit).save(path, optimize=True)
            write_meta(path)
    print('wrote 52 cards to', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
