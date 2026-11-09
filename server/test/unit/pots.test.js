const { computePots } = require('../../src/game/pots');

test('single pot when everyone put in the same', () => {
  expect(computePots([
    { seat: 0, committed: 100, folded: false },
    { seat: 1, committed: 100, folded: false },
  ])).toEqual([{ amount: 200, eligibleSeats: [0, 1] }]);
});

test('side pots for several all-ins of different sizes', () => {
  const pots = computePots([
    { seat: 0, committed: 50, folded: false },  // all-in short
    { seat: 1, committed: 200, folded: false }, // all-in medium
    { seat: 2, committed: 500, folded: false },
    { seat: 3, committed: 500, folded: false },
  ]);
  expect(pots).toEqual([
    { amount: 200, eligibleSeats: [0, 1, 2, 3] },
    { amount: 450, eligibleSeats: [1, 2, 3] },
    { amount: 600, eligibleSeats: [2, 3] },
  ]);
});

test('folded soldiers stay in the pot but folded players cannot win', () => {
  const pots = computePots([
    { seat: 0, committed: 300, folded: true },
    { seat: 1, committed: 100, folded: false },
    { seat: 2, committed: 400, folded: false },
  ]);
  expect(pots).toEqual([
    { amount: 300, eligibleSeats: [1, 2] },
    { amount: 500, eligibleSeats: [2] },
  ]);
  expect(pots.reduce((t, p) => t + p.amount, 0)).toBe(800);
});

test('empty when nothing committed', () => {
  expect(computePots([{ seat: 0, committed: 0, folded: false }])).toEqual([]);
});
