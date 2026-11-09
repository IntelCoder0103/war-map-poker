const { evaluate, findWinners, rankHands } = require('../../src/game/handEval');

test('names hands and returns best five cards in our format', () => {
  const r = evaluate(['Ah', 'Kh', 'Qh', 'Jh', 'Th', '2c', '3d']);
  expect(r.handName).toBe('Royal Flush');
  expect(r.bestCards.sort()).toEqual(['Ah', 'Jh', 'Kh', 'Qh', 'Th'].sort());
});

test('wheel straight keeps the ace as A', () => {
  const r = evaluate(['Ah', '2d', '3c', '4s', '5h', 'Kc', 'Td']);
  expect(r.handName).toBe('Straight');
  expect(r.bestCards).toContain('Ah');
});

test('flush beats straight', () => {
  const board = ['2h', '7h', '9h', '8c', 'Td'];
  const { winners } = findWinners([
    { seat: 0, cards: ['Jc', '6s', ...board] }, // straight 6-T
    { seat: 1, cards: ['Ah', '3h', ...board] }, // flush
  ]);
  expect(winners).toEqual([1]);
});

test('ties produce several winners', () => {
  const board = ['Ah', 'Kd', 'Qs', 'Jc', 'Th'];
  const { winners } = findWinners([
    { seat: 0, cards: ['2c', '3d', ...board] },
    { seat: 4, cards: ['4c', '5d', ...board] },
  ]);
  expect(winners.sort()).toEqual([0, 4]);
});

test('rankHands orders every hand and shares ranks on ties', () => {
  const board = ['2h', '7h', '9h', '8c', 'Td'];
  const r = rankHands([
    { seat: 0, cards: ['Jc', '6s', ...board] }, // straight
    { seat: 1, cards: ['Ah', '3h', ...board] }, // flush
    { seat: 2, cards: ['Js', '6d', ...board] }, // same straight
    { seat: 3, cards: ['Kc', '4d', ...board] }, // king high
  ]);
  expect(r.get(1).rank).toBe(1);
  expect(r.get(0).rank).toBe(2);
  expect(r.get(2).rank).toBe(2);
  expect(r.get(3).rank).toBe(4);
  expect(r.get(1).evaluation.handName).toBe('Flush');
});
