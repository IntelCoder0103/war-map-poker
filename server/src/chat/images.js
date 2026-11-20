// Turns an uploaded PNG or GIF into what the app draws: a square PNG, or for an animated GIF a
// horizontal strip of square frames (frame 0 on the left). Unity can't decode GIFs on the phone,
// so the server does it once, at upload time. Pure JavaScript (pngjs, gifuct-js): no native code.
const path = require('path');
const { Worker } = require('worker_threads');
const { PNG } = require('pngjs');
const { parseGIF, decompressFrames } = require('gifuct-js');

const STATIC_SIZE = 256;   // a still picture
const FRAME_SIZE = 128;    // each frame of an animation
const MAX_FRAMES = 48;     // longer GIFs are thinned out evenly
const MAX_BYTES = 2 * 1024 * 1024;

function fail(code, message) {
  return Object.assign(new Error(message), { code, status: 400 });
}

function typeOf(buf) {
  if (buf.length > 8 && buf[0] === 0x89 && buf.toString('ascii', 1, 4) === 'PNG') return 'png';
  if (buf.length > 6 && buf.toString('ascii', 0, 4) === 'GIF8') return 'gif';
  return null;
}

/** Decodes to { frames: [{ width, height, data (RGBA) }], delays: [ms] }. */
function decode(buf) {
  if (!Buffer.isBuffer(buf) || buf.length === 0) throw fail('BAD_IMAGE', 'No image data.');
  if (buf.length > MAX_BYTES) throw fail('IMAGE_TOO_BIG', 'Images must be 2 MB or smaller.');
  const type = typeOf(buf);
  if (type === 'png') {
    if (buf.length > 24 && buf.readUInt32BE(16) * buf.readUInt32BE(20) > 4096 * 4096) throw fail('IMAGE_TOO_BIG', 'That PNG is too large.');
    let png;
    try { png = PNG.sync.read(buf); } catch (e) { throw fail('BAD_IMAGE', 'That PNG could not be read.'); }
    return { frames: [{ width: png.width, height: png.height, data: png.data }], delays: [0] };
  }
  if (type === 'gif') return decodeGif(buf);
  throw fail('BAD_IMAGE', 'Only PNG and GIF images are supported.');
}

// Draws each GIF frame onto a full canvas, honouring the disposal methods, like a browser does.
function decodeGif(buf) {
  let gif, parts;
  try {
    gif = parseGIF(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.length));
    parts = decompressFrames(gif, true);
  } catch (e) {
    throw fail('BAD_IMAGE', 'That GIF could not be read.');
  }
  if (!parts.length) throw fail('BAD_IMAGE', 'That GIF has no frames.');
  const W = gif.lsd.width, H = gif.lsd.height;
  if (W * H > 2048 * 2048) throw fail('IMAGE_TOO_BIG', 'That GIF is too large.');
  let canvas = Buffer.alloc(W * H * 4);
  const frames = [], delays = [];
  for (const p of parts) {
    const before = p.disposalType === 3 ? Buffer.from(canvas) : null;
    const { left, top, width, height } = p.dims;
    for (let y = 0; y < height; y++) {
      const cy = top + y;
      if (cy < 0 || cy >= H) continue;
      for (let x = 0; x < width; x++) {
        const cx = left + x;
        if (cx < 0 || cx >= W) continue;
        const s = (y * width + x) * 4;
        if (p.patch[s + 3] === 0) continue; // transparent pixel: keep what's underneath
        const d = (cy * W + cx) * 4;
        canvas[d] = p.patch[s]; canvas[d + 1] = p.patch[s + 1]; canvas[d + 2] = p.patch[s + 2]; canvas[d + 3] = 255;
      }
    }
    frames.push({ width: W, height: H, data: Buffer.from(canvas) });
    delays.push(p.delay || 0);
    if (p.disposalType === 2) {
      for (let y = 0; y < height; y++) {
        const cy = top + y;
        if (cy < 0 || cy >= H) continue;
        const start = (cy * W + Math.max(0, left)) * 4;
        canvas.fill(0, start, start + Math.max(0, Math.min(width, W - left)) * 4);
      }
    } else if (before) {
      canvas = before;
    }
  }
  return { frames, delays };
}

// Scales an RGBA image to fit a size x size square (centred, transparent around it).
// Box-filters with premultiplied alpha when shrinking, so edges stay clean.
function fitSquare(img, size) {
  const scale = Math.min(size / img.width, size / img.height);
  const w = Math.max(1, Math.round(img.width * scale)), h = Math.max(1, Math.round(img.height * scale));
  const ox = Math.floor((size - w) / 2), oy = Math.floor((size - h) / 2);
  const out = Buffer.alloc(size * size * 4);
  const sx = img.width / w, sy = img.height / h;
  for (let y = 0; y < h; y++) {
    const y0 = y * sy, y1 = Math.max(y0 + 1e-6, (y + 1) * sy);
    for (let x = 0; x < w; x++) {
      const x0 = x * sx, x1 = Math.max(x0 + 1e-6, (x + 1) * sx);
      let r = 0, g = 0, b = 0, a = 0, total = 0;
      if (sx <= 1 && sy <= 1) {
        // growing: nearest-neighbour-ish bilinear sample at the pixel centre
        const fx = Math.min(img.width - 1, (x + 0.5) * sx - 0.5), fy = Math.min(img.height - 1, (y + 0.5) * sy - 0.5);
        const ix = Math.max(0, Math.floor(fx)), iy = Math.max(0, Math.floor(fy));
        const tx = Math.max(0, fx - ix), ty = Math.max(0, fy - iy);
        for (const [px, py, wgt] of [[ix, iy, (1 - tx) * (1 - ty)], [ix + 1, iy, tx * (1 - ty)], [ix, iy + 1, (1 - tx) * ty], [ix + 1, iy + 1, tx * ty]]) {
          const cx = Math.min(img.width - 1, px), cy = Math.min(img.height - 1, py);
          const i = (cy * img.width + cx) * 4, al = img.data[i + 3] / 255 * wgt;
          r += img.data[i] * al; g += img.data[i + 1] * al; b += img.data[i + 2] * al; a += al; total += wgt;
        }
      } else {
        for (let py = Math.floor(y0); py < Math.ceil(y1); py++) {
          const wy = Math.min(py + 1, y1) - Math.max(py, y0);
          for (let px = Math.floor(x0); px < Math.ceil(x1); px++) {
            const wx = Math.min(px + 1, x1) - Math.max(px, x0);
            const wgt = wx * wy;
            const i = (Math.min(img.height - 1, py) * img.width + Math.min(img.width - 1, px)) * 4;
            const al = img.data[i + 3] / 255 * wgt;
            r += img.data[i] * al; g += img.data[i + 1] * al; b += img.data[i + 2] * al; a += al; total += wgt;
          }
        }
      }
      const o = ((oy + y) * size + ox + x) * 4;
      if (a > 0) {
        out[o] = Math.round(r / a); out[o + 1] = Math.round(g / a); out[o + 2] = Math.round(b / a);
        out[o + 3] = Math.round(a / total * 255);
      }
    }
  }
  return out;
}

/** A PNG that is already a STATIC_SIZE square (like the built-in emojis) is stored as it is. */
function isReadySquare(buf) {
  return typeOf(buf) === 'png' && buf.length > 24 &&
    buf.readUInt32BE(16) === STATIC_SIZE && buf.readUInt32BE(20) === STATIC_SIZE;
}

/** A ready-made animation strip (like the built-in animated emojis): `frames` FRAME_SIZE squares side by side. */
function isReadyStrip(buf, frames) {
  return frames > 1 && typeOf(buf) === 'png' && buf.length > 24 &&
    buf.readUInt32BE(16) === FRAME_SIZE * frames && buf.readUInt32BE(20) === FRAME_SIZE;
}

/**
 * Converts an upload into the stored picture.
 * Returns { png: Buffer, frames, fps, size }: frames === 1 for a still image.
 */
function toStrip(buf) {
  if (isReadySquare(buf)) return { png: buf, frames: 1, fps: 0, size: STATIC_SIZE };
  let { frames, delays } = decode(buf);
  if (frames.length > MAX_FRAMES) {
    const step = frames.length / MAX_FRAMES;
    const keep = Array.from({ length: MAX_FRAMES }, (_, i) => Math.floor(i * step));
    delays = keep.map((i) => delays[i] * step);
    frames = keep.map((i) => frames[i]);
  }
  const animated = frames.length > 1;
  const size = animated ? FRAME_SIZE : STATIC_SIZE;
  const strip = new PNG({ width: size * frames.length, height: size });
  frames.forEach((f, n) => {
    const sq = fitSquare(f, size);
    for (let y = 0; y < size; y++) sq.copy(strip.data, (y * strip.width + n * size) * 4, y * size * 4, (y + 1) * size * 4);
  });
  // GIF delays are in ms here; very small values mean "as fast as possible", which browsers show at 10 fps.
  const ms = delays.map((d) => (d >= 20 ? d : 100));
  const avg = ms.reduce((s, d) => s + d, 0) / ms.length;
  const fps = animated ? Math.max(2, Math.min(30, Math.round(1000 / avg))) : 0;
  return { png: PNG.sync.write(strip), frames: frames.length, fps, size };
}

/** toStrip on a worker thread (uploads): the game keeps running while a big GIF is converted. */
function toStripAsync(buf) {
  if (isReadySquare(buf)) return Promise.resolve(toStrip(buf));
  if (!Buffer.isBuffer(buf) || buf.length === 0) return Promise.reject(fail('BAD_IMAGE', 'No image data.'));
  if (buf.length > MAX_BYTES) return Promise.reject(fail('IMAGE_TOO_BIG', 'Images must be 2 MB or smaller.'));
  return new Promise((resolve, reject) => {
    const worker = new Worker(path.join(__dirname, 'imageWorker.js'), { workerData: buf });
    const timer = setTimeout(() => { worker.terminate(); reject(fail('IMAGE_TOO_BIG', 'That image took too long to convert.')); }, 30000);
    worker.once('message', (m) => {
      clearTimeout(timer);
      worker.terminate();
      if (m.ok) resolve({ png: Buffer.from(m.png), frames: m.frames, fps: m.fps, size: m.size });
      else reject(fail(m.code, m.message));
    });
    worker.once('error', (e) => { clearTimeout(timer); reject(fail('BAD_IMAGE', e.message)); });
  });
}

module.exports = { toStrip, toStripAsync, isReadyStrip, decode, typeOf, STATIC_SIZE, FRAME_SIZE, MAX_FRAMES, MAX_BYTES };
