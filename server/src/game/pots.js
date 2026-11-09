// Split total contributions into a main pot and side pots.
// players: [{ seat, committed, folded }]
// Returns [{ amount, eligibleSeats }], smallest (main) pot first.
// Folded players' soldiers stay in the pots, but they can't win them.
function computePots(players) {
  const levels = [...new Set(players.filter((p) => !p.folded && p.committed > 0).map((p) => p.committed))]
    .sort((a, b) => a - b);
  const maxCommitted = Math.max(0, ...players.map((p) => p.committed));
  if (levels.length === 0 || levels[levels.length - 1] < maxCommitted) levels.push(maxCommitted);

  const pots = [];
  let prev = 0;
  for (const level of levels) {
    if (level <= prev) continue;
    let amount = 0;
    for (const p of players) amount += Math.max(0, Math.min(p.committed, level) - prev);
    const eligibleSeats = players.filter((p) => !p.folded && p.committed >= level).map((p) => p.seat);
    if (amount > 0) {
      if (eligibleSeats.length === 0 && pots.length > 0) {
        pots[pots.length - 1].amount += amount; // nobody left to win this layer; fold it into the pot below
      } else {
        pots.push({ amount, eligibleSeats });
      }
    }
    prev = level;
  }

  // Merge neighbouring pots that have the same eligible players.
  const merged = [];
  for (const pot of pots) {
    const last = merged[merged.length - 1];
    if (last && sameSeats(last.eligibleSeats, pot.eligibleSeats)) last.amount += pot.amount;
    else merged.push({ amount: pot.amount, eligibleSeats: pot.eligibleSeats.slice() });
  }
  return merged;
}

function sameSeats(a, b) {
  return a.length === b.length && a.every((s, i) => s === b[i]);
}

module.exports = { computePots };
