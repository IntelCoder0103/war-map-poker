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
  nextHandDelayMs: Number(env.NEXT_HAND_DELAY_MS || 4000),
  disconnectGraceMs: Number(env.DISCONNECT_GRACE_MS || 60000),

  // One "room" per level. Each level can have several tables.
  levels: [
    { id: 'recruit', name: 'Recruit', buyIn: 1000, smallBlind: 10, bigBlind: 20 },
    { id: 'soldier', name: 'Soldier', buyIn: 5000, smallBlind: 50, bigBlind: 100 },
    { id: 'captain', name: 'Captain', buyIn: 20000, smallBlind: 200, bigBlind: 400 },
    { id: 'general', name: 'General', buyIn: 100000, smallBlind: 1000, bigBlind: 2000 },
    { id: 'king', name: 'King', buyIn: 500000, smallBlind: 5000, bigBlind: 10000 },
  ],
};
