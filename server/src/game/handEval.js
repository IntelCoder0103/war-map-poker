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

module.exports = { evaluate, findWinners };
