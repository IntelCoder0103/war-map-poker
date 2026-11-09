const crypto = require('crypto');

const RANKS = '23456789TJQKA';
const SUITS = 'cdhs';

function newDeck() {
  const deck = [];
  for (const r of RANKS) for (const s of SUITS) deck.push(r + s);
  return deck;
}

// Fisher-Yates with a cryptographic random source.
function shuffle(cards) {
  const a = cards.slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = crypto.randomInt(i + 1);
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

function shuffledDeck() {
  return shuffle(newDeck());
}

module.exports = { RANKS, SUITS, newDeck, shuffle, shuffledDeck };
