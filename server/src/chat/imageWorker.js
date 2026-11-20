// Runs the picture conversion on a separate thread, so a big GIF upload doesn't pause the game.
const { parentPort, workerData } = require('worker_threads');
const { toStrip } = require('./images');

try {
  const out = toStrip(Buffer.from(workerData));
  parentPort.postMessage({ ok: true, png: out.png, frames: out.frames, fps: out.fps, size: out.size });
} catch (e) {
  parentPort.postMessage({ ok: false, code: e.code || 'BAD_IMAGE', message: e.message });
}
