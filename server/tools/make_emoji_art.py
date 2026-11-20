#!/usr/bin/env python3
"""Draws the built-in chat emojis with Codex image generation.

Reads assets/emoji/manifest.json and, for every emoji that has no art/emoji/<id>.png yet, asks the
Codex CLI to draw it in the game's style (five per Codex run, two runs at a time). Each result is checked
for a transparent background, trimmed, padded to a square and saved at 256x256.

  python3 server/tools/make_emoji_art.py            # draw the missing ones
  python3 server/tools/make_emoji_art.py fire skull # redraw these (delete their PNGs first, or pass --force)

Needs: the Codex CLI logged in (codex login), Pillow. CODEX_MODEL picks the model (default gpt-6.1-sol).
The style references are the soldier sprite and art/emoji/thumbs_up.png (when it exists).
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
EMOJI = os.path.normpath(os.path.join(HERE, '..', 'assets', 'emoji'))   # manifest.json
STILL = os.path.normpath(os.path.join(HERE, '..', 'art', 'emoji'))      # the pictures
SOLDIER = os.path.normpath(os.path.join(HERE, '..', '..', 'client', 'Assets', '_Project', 'Resources', 'Troops', 'soldier.png'))
MODEL = os.environ.get('CODEX_MODEL', 'gpt-6.1-sol')
SIZE = 256
BATCH = 5
WORKERS = 2

STYLE = """Style for every icon: glossy mobile-game cartoon matching the reference images
(bold dark-brown outline, soft shading with white highlights, bright saturated colours),
with the proportions of Telegram / Apple emoji. Faces are classic round yellow emoji faces unless said otherwise.
Every icon: one single centred object, square 1024x1024, FULLY TRANSPARENT background
(no backdrop, no circle or plate behind it, no drop shadow on the ground, no text, no border),
filling about 85% of the canvas."""


def prompt(items):
    lines = '\n'.join(f'- {e["id"]}.png: {e["draw"]}' for e in items)
    return f"""Use your image generation tool to create {len(items)} game emoji icons, one image per icon
(call the tool once per icon), and save each in the current folder with exactly the file name given:
{lines}

{STYLE}

After saving them, check that each file is 1024x1024 RGBA with transparent corners; redo any that is not.
Finally print the list of saved files."""


def good(path):
    try:
        im = Image.open(path).convert('RGBA')
    except Exception:
        return False
    a = im.split()[3]
    w, h = im.size
    corners = [a.getpixel((x, y)) for x, y in ((2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3))]
    clear = sum(a.histogram()[:10]) / (w * h)
    return max(corners) < 10 and clear > 0.15


def finish(src, dst):
    im = Image.open(src).convert('RGBA')
    box = im.split()[3].point(lambda p: 255 if p > 12 else 0).getbbox()
    im = im.crop(box)
    side = int(max(im.size) * 1.08)
    sq = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    sq.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    sq.resize((SIZE, SIZE), Image.LANCZOS).save(dst, optimize=True)


def run_batch(items):
    work = tempfile.mkdtemp(prefix='emoji_')
    refs = []
    for ref in (SOLDIER, os.path.join(STILL, 'thumbs_up.png')):
        if os.path.exists(ref):
            shutil.copy(ref, os.path.join(work, 'style_ref_' + os.path.basename(ref)))
            refs += ['-i', os.path.join(work, 'style_ref_' + os.path.basename(ref))]
    cmd = ['codex', 'exec', '-m', MODEL, '--skip-git-repo-check', '-s', 'workspace-write', '-C', work, prompt(items)] + refs
    try:
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1800)
    except subprocess.TimeoutExpired:
        pass
    done = []
    for e in items:
        raw = os.path.join(work, e['id'] + '.png')
        if os.path.exists(raw) and good(raw):
            finish(raw, os.path.join(STILL, e['id'] + '.png'))
            done.append(e['id'])
    shutil.rmtree(work, ignore_errors=True)
    print('batch', [e['id'] for e in items], '-> ok', done, flush=True)
    return done


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    force = '--force' in sys.argv
    emojis = json.load(open(os.path.join(EMOJI, 'manifest.json'), encoding='utf-8'))['emojis']
    if args:
        emojis = [e for e in emojis if e['id'] in args]
    for attempt in range(3):
        todo = [e for e in emojis if force or not os.path.exists(os.path.join(STILL, e['id'] + '.png'))]
        force = False
        if not todo:
            break
        batches = [todo[i:i + BATCH] for i in range(0, len(todo), BATCH)]
        print(f'pass {attempt + 1}: {len(todo)} to draw in {len(batches)} batches', flush=True)
        with ThreadPoolExecutor(WORKERS) as pool:
            list(pool.map(run_batch, batches))
    missing = [e['id'] for e in emojis if not os.path.exists(os.path.join(STILL, e['id'] + '.png'))]
    print('missing:', missing or 'none')


if __name__ == '__main__':
    main()
