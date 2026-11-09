// Every change to a player's soldiers goes through here, as single atomic updates.
const User = require('./User');
const config = require('../config');
const { GameError } = require('../game/holdemEngine');

function publicUser(u) {
  const next = u.lastRefillAt ? u.lastRefillAt.getTime() + config.refill.cooldownMs : null;
  return { id: String(u._id), username: u.username, balance: u.balance, nextRefillAt: next };
}

// Move `amount` from the balance to a table stack.
async function buyIn(userId, amount) {
  const u = await User.findOneAndUpdate(
    { _id: userId, atTable: false, balance: { $gte: amount } },
    { $inc: { balance: -amount }, $set: { atTable: true, tableStack: amount } },
    { new: true },
  );
  if (u) return u.balance;
  const current = await User.findById(userId);
  if (!current) throw new GameError('UNAUTHORIZED');
  throw new GameError(current.atTable ? 'ALREADY_SEATED' : 'INSUFFICIENT_BALANCE');
}

// Save table stacks after each hand: [{ userId, stack }].
async function saveStacks(entries) {
  if (entries.length === 0) return;
  await User.bulkWrite(entries.map(({ userId, stack }) => ({
    updateOne: { filter: { _id: userId, atTable: true }, update: { $set: { tableStack: stack } } },
  })));
}

// Move a table stack back to the balance.
async function cashOut(userId, stack) {
  const u = await User.findOneAndUpdate(
    { _id: userId, atTable: true },
    { $inc: { balance: stack }, $set: { atTable: false, tableStack: 0 } },
    { new: true },
  );
  return u ? publicUser(u) : null;
}

async function refill(userId) {
  const now = new Date();
  const u = await User.findOneAndUpdate(
    {
      _id: userId, atTable: false, balance: { $lt: config.refill.threshold },
      $or: [{ lastRefillAt: null }, { lastRefillAt: { $lte: new Date(now - config.refill.cooldownMs) } }],
    },
    { $set: { balance: config.refill.topUpTo, lastRefillAt: now } },
    { new: true },
  );
  if (!u) throw new GameError('REFILL_NOT_AVAILABLE');
  return publicUser(u);
}

// On startup no one is seated, so give any stranded table soldiers back.
async function recover() {
  const res = await User.updateMany(
    { atTable: true },
    [{ $set: { balance: { $add: ['$balance', '$tableStack'] }, tableStack: 0, atTable: false } }],
  );
  return res.modifiedCount;
}

module.exports = { publicUser, buyIn, saveStacks, cashOut, refill, recover };
