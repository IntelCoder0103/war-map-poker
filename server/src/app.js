// Builds the HTTP + Socket.IO server. index.js starts it; tests start it on a random port.
const http = require('http');
const express = require('express');
const { Server } = require('socket.io');
const mongoose = require('mongoose');
const config = require('./config');
const wallet = require('./db/wallet');
const { TableManager } = require('./game/tableManager');
const { attachSockets } = require('./socket/handlers');

async function startServer(overrides = {}) {
  const cfg = { ...config, ...overrides };
  const log = overrides.log || ((...args) => console.error('[warpoker]', ...args));

  await mongoose.connect(cfg.mongoUrl);
  await mongoose.model('User').syncIndexes();
  const recovered = await wallet.recover();
  if (recovered) log(`returned table soldiers to ${recovered} player(s) after restart`);

  const app = express();
  app.use(express.json({ limit: '10kb' }));
  app.get('/health', (req, res) => res.json({ ok: true }));
  app.use('/auth', require('./auth/routes'));
  app.use('/me', require('./account/routes'));

  const server = http.createServer(app);
  const io = new Server(server, {
    pingInterval: 10000, // notice dropped 4G connections fairly quickly
    pingTimeout: 8000,
    maxHttpBufferSize: 10000,
  });

  let bridge = null;
  const manager = new TableManager({
    levels: cfg.levels,
    settings: {
      maxSeats: cfg.maxSeats, turnTimeMs: cfg.turnTimeMs,
      nextHandDelayMs: cfg.nextHandDelayMs,
      showdownDelayMs: cfg.showdownDelayMs ?? cfg.nextHandDelayMs, disconnectGraceMs: cfg.disconnectGraceMs,
    },
    hooks: {
      wallet,
      log,
      notify: (userId, event, payload) => bridge && bridge.notify(userId, event, payload),
      onLobbyChange: (rooms) => bridge && bridge.lobby(rooms),
    },
  });
  bridge = attachSockets(io, manager, log);

  app.get('/rooms', (req, res) => res.json({ rooms: manager.rooms() }));
  // eslint-disable-next-line no-unused-vars
  app.use((err, req, res, next) => {
    log('http error', err);
    res.status(500).json({ error: 'SERVER_ERROR', message: 'Something went wrong.' });
  });

  await new Promise((resolve) => server.listen(cfg.port, resolve));

  return {
    port: server.address().port,
    manager,
    async close() {
      manager.dispose();
      await new Promise((resolve) => io.close(() => resolve())); // also closes the HTTP server
      await mongoose.disconnect();
    },
  };
}

module.exports = { startServer };
