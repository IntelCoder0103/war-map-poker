const mongoose = require('mongoose');
const { io } = require('socket.io-client');
const { startServer } = require('../../src/app');

const MONGO_URL = process.env.MONGO_URL || 'mongodb://localhost:27017/warpoker_test';

async function start(overrides = {}) {
  await mongoose.connect(MONGO_URL);
  await mongoose.connection.dropDatabase();
  await mongoose.disconnect();
  const srv = await startServer({
    port: 0, mongoUrl: MONGO_URL, log: () => {},
    turnTimeMs: 1000, nextHandDelayMs: 30, showdownDelayMs: 30, disconnectGraceMs: 400, ...overrides,
  });
  srv.url = `http://localhost:${srv.port}`;
  return srv;
}

async function api(srv, method, path, body, token) {
  const res = await fetchJson(`${srv.url}${path}`, method, body, token);
  return res;
}

// Node 16 has no global fetch, so use http directly.
function fetchJson(url, method, body, token) {
  const http = require('http');
  return new Promise((resolve, reject) => {
    const data = body ? JSON.stringify(body) : null;
    const req = http.request(url, {
      method,
      headers: {
        'content-type': 'application/json',
        ...(token ? { authorization: `Bearer ${token}` } : {}),
        ...(data ? { 'content-length': Buffer.byteLength(data) } : {}),
      },
    }, (res) => {
      let buf = '';
      res.on('data', (c) => { buf += c; });
      res.on('end', () => resolve({ status: res.statusCode, body: buf ? JSON.parse(buf) : null }));
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

// `setup(socket)` registers listeners before connecting, so nothing sent on connect is missed.
function connect(srv, token, setup = () => {}) {
  return new Promise((resolve, reject) => {
    const s = io(srv.url, { auth: { token }, transports: ['websocket'], reconnection: false, forceNew: true, autoConnect: false });
    setup(s);
    s.connect();
    s.once('connect', () => resolve(s));
    s.once('connect_error', (e) => reject(e));
  });
}

function emit(socket, event, payload = {}) {
  return new Promise((resolve) => socket.emit(event, payload, resolve));
}

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

async function until(pred, timeoutMs = 20000, stepMs = 20) {
  const t0 = Date.now();
  while (!pred()) {
    if (Date.now() - t0 > timeoutMs) throw new Error('timed out waiting');
    await wait(stepMs);
  }
}

module.exports = { start, api, connect, emit, wait, until };
