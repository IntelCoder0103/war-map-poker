# Draws the sound on/off round icons in the style of the other round icons.
import sys, os
from PIL import Image, ImageDraw
ui = sys.argv[1]
K = 4
def glyph(mute, size):
    g = Image.new('L', (size * K, size * K), 0); d = ImageDraw.Draw(g)
    s = K
    cx, cy = 64 * s, 66 * s
    # speaker box and cone
    d.rounded_rectangle([cx - 30 * s, cy - 11 * s, cx - 14 * s, cy + 11 * s], 3 * s, fill=255)
    d.polygon([(cx - 18 * s, cy - 11 * s), (cx + 2 * s, cy - 27 * s), (cx + 2 * s, cy + 27 * s), (cx - 18 * s, cy + 11 * s)], fill=255)
    d.rounded_rectangle([cx - 2 * s, cy - 27 * s, cx + 4 * s, cy + 27 * s], 3 * s, fill=255)
    if mute:
        w = 7 * s
        d.line([(cx + 12 * s, cy - 12 * s), (cx + 34 * s, cy + 12 * s)], fill=255, width=w)
        d.line([(cx + 12 * s, cy + 12 * s), (cx + 34 * s, cy - 12 * s)], fill=255, width=w)
        for (x, y) in [(12, -12), (34, 12), (12, 12), (34, -12)]:
            d.ellipse([cx + x * s - w / 2, cy + y * s - w / 2, cx + x * s + w / 2, cy + y * s + w / 2], fill=255)
    else:
        for r, w in [(14, 6), (27, 6)]:
            d.arc([cx - r * s + 4 * s, cy - r * s, cx + r * s + 4 * s, cy + r * s], -55, 55, fill=255, width=w * s)
    return g
def make(base, mute, top, side, shade, out):
    b = Image.open(os.path.join(ui, base)).convert('RGBA')
    W, H = b.size
    m = glyph(mute, max(W, H)).resize((max(W, H), max(W, H)), Image.LANCZOS).crop((0, 0, W, H))
    sh = Image.new('RGBA', (W, H), shade + (255,)); sh.putalpha(m)
    b.alpha_composite(sh, (0, 4))
    face = Image.new('RGBA', (W, H), top + (255,))
    right = Image.new('RGBA', (W, H), side + (255,))
    half = Image.new('L', (W, H), 0); ImageDraw.Draw(half).rectangle([W // 2 + 2, 0, W, H], fill=255)
    face.paste(right, (0, 0), half)
    face.putalpha(m)
    b.alpha_composite(face)
    b.save(os.path.join(ui, out))
for mute, name in [(False, 'icon_sound'), (True, 'icon_mute')]:
    make('round_wood.png', mute, (255, 255, 255), (232, 232, 236), (165, 165, 172), name + '.png')
    make('round_gold.png', mute, (255, 238, 30), (255, 214, 20), (226, 128, 10), name + '_pressed.png')
