const { Hand } = require('pokersolver');

// pokersolver prints tens as "10" and a low ace as "1"; convert back to our 2-char format.
function toCode(card) {
  let v = card.value;
  if (v === '1') v = 'A';
  if (v === '10') v = 'T';
  return v + card.suit;
}

function evaluate(cards) {
  const h = Hand.solve(cards);
  return {
    solved: h,
    handName: h.descr === 'Royal Flush' ? 'Royal Flush' : h.name,
    bestCards: h.cards.map(toCode),
  };
}

// entries: [{ seat, cards: [7 cards] }] -> { winners: [seat...], results: Map(seat -> evaluation) }
function findWinners(entries) {
  const results = new Map();
  for (const e of entries) results.set(e.seat, evaluate(e.cards));
  const solved = entries.map((e) => results.get(e.seat).solved);
  const winning = new Set(Hand.winners(solved));
  const winners = entries.filter((e) => winning.has(results.get(e.seat).solved)).map((e) => e.seat);
  return { winners, results };
}

// Ranks every entry from best (1) down. Tied hands share a rank (1, 1, 3, ...).
// entries: [{ seat, cards }] -> Map(seat -> { rank, evaluation })
function rankHands(entries) {
  const results = new Map();
  for (const e of entries) results.set(e.seat, evaluate(e.cards));
  const out = new Map();
  let remaining = entries.slice();
  let rank = 1;
  while (remaining.length) {
    const best = new Set(Hand.winners(remaining.map((e) => results.get(e.seat).solved)));
    const top = remaining.filter((e) => best.has(results.get(e.seat).solved));
    for (const e of top) out.set(e.seat, { rank, evaluation: results.get(e.seat) });
    rank += top.length;
    remaining = remaining.filter((e) => !best.has(results.get(e.seat).solved));
  }
  return out;
}

module.exports = { evaluate, findWinners, rankHands };
