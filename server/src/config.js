// All server settings in one place.
//
// Order of precedence (later wins):
//   1. the defaults below
//   2. server/config.json (game settings: economy, timers, room levels) — edit this file, no code changes
//      (another file can be used with CONFIG_FILE=/path/to/file.json)
//   3. environment variables (deployment: PORT, MONGO_URL, JWT_SECRET, and timer overrides for tests)
const fs = require('fs');
const path = require('path');

const env = process.env;

const defaults = {
  port: 3000,
  mongoUrl: 'mongodb://localhost:27017/warpoker',
  jwtSecret: 'dev-only-secret-change-me',
  jwtExpiresIn: '30d',

  // Soldiers a new account starts with, and the daily top-up for players who run low.
  startingBalance: 2000,
  refill: { threshold: 1000, topUpTo: 1000, cooldownMs: 24 * 60 * 60 * 1000 },

  maxSeats: 8,
  turnTimeMs: 20000,
  // Pause before the next hand: shorter when everyone folded, longer after a showdown
  // so clients can show the hand ranking and march the soldiers to the winner.
  nextHandDelayMs: 4500,
  showdownDelayMs: 8000,
  disconnectGraceMs: 60000,
  // Missed turns in a row before an idle player is sent back to the lobby.
  idleTimeouts: 3,
  // Once 2+ players pressed Ready, how long the others have before they go back to the lobby.
  readyTimeoutMs: 30000,

  // One "room" per level. Each level can have several tables.
  // Blinds are 1% / 2% of the buy-in, so every room starts players with 100 big blinds.
  levels: [
    { id: 'recruit', name: 'Recruit', buyIn: 1000, smallBlind: 10, bigBlind: 20 },
    { id: 'soldier', name: 'Soldier', buyIn: 2000, smallBlind: 20, bigBlind: 40 },
    { id: 'captain', name: 'Captain', buyIn: 5000, smallBlind: 50, bigBlind: 100 },
    { id: 'general', name: 'General', buyIn: 10000, smallBlind: 100, bigBlind: 200 },
    { id: 'king', name: 'King', buyIn: 50000, smallBlind: 500, bigBlind: 1000 },
  ],
};

// Objects merge key by key; arrays (like `levels`) and plain values replace.
function merge(base, extra) {
  const out = { ...base };
  for (const [k, v] of Object.entries(extra || {})) {
    if (k.startsWith('_')) continue; // "_comment" keys are notes for people editing the file
    out[k] = v && typeof v === 'object' && !Array.isArray(v) && base[k] && typeof base[k] === 'object' && !Array.isArray(base[k])
      ? merge(base[k], v)
      : v;
  }
  return out;
}

function fromFile() {
  const file = env.CONFIG_FILE || path.join(__dirname, '..', 'config.json');
  if (!fs.existsSync(file)) return {};
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (e) {
    throw new Error(`Invalid JSON in ${file}: ${e.message}`);
  }
}

function fromEnv() {
  const num = (name) => (env[name] !== undefined && env[name] !== '' ? Number(env[name]) : undefined);
  const out = {
    port: num('PORT'),
    mongoUrl: env.MONGO_URL || undefined,
    jwtSecret: env.JWT_SECRET || undefined,
    turnTimeMs: num('TURN_TIME_MS'),
    nextHandDelayMs: num('NEXT_HAND_DELAY_MS'),
    showdownDelayMs: num('SHOWDOWN_DELAY_MS'),
    disconnectGraceMs: num('DISCONNECT_GRACE_MS'),
    idleTimeouts: num('IDLE_TIMEOUTS'),
    readyTimeoutMs: num('READY_TIMEOUT_MS'),
  };
  for (const k of Object.keys(out)) if (out[k] === undefined) delete out[k];
  return out;
}

// Refuses settings that would break the game, with a message saying what to fix.
function validate(cfg) {
  const problems = [];
  const ids = new Set();
  if (!Array.isArray(cfg.levels) || cfg.levels.length === 0) problems.push('levels must be a non-empty list');
  for (const l of cfg.levels || []) {
    const where = `level "${l && l.id}"`;
    if (!l || typeof l.id !== 'string' || !l.id) { problems.push('every level needs an "id"'); continue; }
    if (ids.has(l.id)) problems.push(`${where}: duplicate id`);
    ids.add(l.id);
    for (const k of ['buyIn', 'smallBlind', 'bigBlind']) {
      if (!Number.isInteger(l[k]) || l[k] <= 0) problems.push(`${where}: ${k} must be a positive whole number`);
    }
    if (l.smallBlind > l.bigBlind) problems.push(`${where}: smallBlind is bigger than bigBlind`);
    if (l.bigBlind > l.buyIn) problems.push(`${where}: bigBlind is bigger than buyIn`);
  }
  if (!Number.isInteger(cfg.maxSeats) || cfg.maxSeats < 2 || cfg.maxSeats > 8) problems.push('maxSeats must be 2..8 (the client shows 8 camps)');
  if (problems.length) throw new Error(`Bad server config:\n  - ${problems.join('\n  - ')}`);
  return cfg;
}

module.exports = validate(merge(merge(defaults, fromFile()), fromEnv()));
module.exports.defaults = defaults;
