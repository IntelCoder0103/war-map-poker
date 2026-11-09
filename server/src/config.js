// All tunable game settings in one place.
const env = process.env;

module.exports = {
  port: Number(env.PORT || 3000),
  mongoUrl: env.MONGO_URL || 'mongodb://localhost:27017/warpoker',
  jwtSecret: env.JWT_SECRET || 'dev-only-secret-change-me',
  jwtExpiresIn: '30d',

  startingBalance: 2000,
  refill: { threshold: 1000, topUpTo: 1000, cooldownMs: 24 * 60 * 60 * 1000 },

  maxSeats: 8,
  turnTimeMs: Number(env.TURN_TIME_MS || 20000),
  // Pause before the next hand: shorter when everyone folded, longer after a showdown
  // so clients can show the hand ranking and march the soldiers to the winner.
  nextHandDelayMs: Number(env.NEXT_HAND_DELAY_MS || 4500),
  showdownDelayMs: Number(env.SHOWDOWN_DELAY_MS || 8000),
  disconnectGraceMs: Number(env.DISCONNECT_GRACE_MS || 60000),
  // Missed turns in a row before an idle player is sent back to the lobby.
  idleTimeouts: Number(env.IDLE_TIMEOUTS || 3),
  // Once 2+ players pressed Ready, how long the others have before they go back to the lobby.
  readyTimeoutMs: Number(env.READY_TIMEOUT_MS || 30000),

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
