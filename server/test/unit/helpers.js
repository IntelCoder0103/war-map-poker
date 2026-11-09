const { newDeck } = require('../../src/game/deck');

// Build a deck whose pop() order is exactly `draws`, followed by the rest of the deck.
function riggedDeck(draws) {
  const rest = newDeck().filter((c) => !draws.includes(c));
  return rest.concat(draws.slice().reverse());
}

// Draw order for a hand: 2 rounds of hole cards (deal order), then burn+flop, burn+turn, burn+river.
function deckFor(holeBySeatInDealOrder, board, burns = ['2c', '2d', '2h']) {
  const draws = [];
  for (let r = 0; r < 2; r++) for (const h of holeBySeatInDealOrder) draws.push(h[r]);
  draws.push(burns[0], board[0], board[1], board[2], burns[1], board[3], burns[2], board[4]);
  return riggedDeck(draws);
}

function sum(obj) {
  return Object.values(obj).reduce((a, b) => a + b, 0);
}

module.exports = { riggedDeck, deckFor, sum };
