const { HoldemHand } = require('../../src/game/holdemEngine');
const { shuffledDeck } = require('../../src/game/deck');
const { deckFor, sum } = require('./helpers');

function hand(stacks, opts = {}) {
  const players = Object.entries(stacks).map(([seat, stack]) => ({ seat: Number(seat), stack }));
  return new HoldemHand({
    players, dealerSeat: opts.dealer ?? 0, smallBlind: opts.sb ?? 10, bigBlind: opts.bb ?? 20,
    deck: opts.deck || shuffledDeck(),
  });
}
const types = (events) => events.map((e) => e.type);
const last = (events, type) => events.filter((e) => e.type === type).pop();

describe('positions and blinds', () => {
  test('3 players: SB and BB left of dealer, first to act after BB', () => {
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 });
    const ev = h.start();
    expect(types(ev)).toEqual(['handStarted', 'blinds', 'turn']);
    expect(ev[0].data).toMatchObject({ dealerSeat: 0, sbSeat: 1, bbSeat: 2, seats: [1, 2, 0] });
    expect(ev[2].data.seat).toBe(0);
    expect(h.stacks()).toEqual({ 0: 1000, 1: 990, 2: 980 });
    expect(h.cardsOf(0)).toHaveLength(2);
  });

  test('heads-up: dealer is SB and acts first preflop, last after the flop', () => {
    const h = hand({ 3: 1000, 5: 1000 }, { dealer: 3 });
    const ev = h.start();
    expect(ev[0].data).toMatchObject({ sbSeat: 3, bbSeat: 5 });
    expect(h.turnSeat).toBe(3);
    h.act(3, 'call');
    const ev2 = h.act(5, 'check');
    expect(types(ev2)).toEqual(['action', 'street', 'turn']);
    expect(last(ev2, 'turn').data.seat).toBe(5);
  });

  test('big blind gets an option to check or raise when everyone just calls', () => {
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 });
    h.start();
    h.act(0, 'call');
    h.act(1, 'call');
    expect(h.legalFor(2)).toMatchObject({ canCheck: true, canRaise: true, minRaiseTo: 40 });
    const ev = h.act(2, 'check');
    expect(last(ev, 'street').data.phase).toBe('flop');
    expect(h.board).toHaveLength(3);
    expect(h.turnSeat).toBe(1);
  });
});

describe('betting rules', () => {
  test('min raise is the size of the last raise', () => {
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 });
    h.start();
    expect(h.legalFor(0)).toMatchObject({ canCall: true, callAmount: 20, minRaiseTo: 40, maxRaiseTo: 1000 });
    expect(() => h.act(0, 'raise', 30)).toThrow('BAD_AMOUNT');
    h.act(0, 'raise', 60);
    expect(h.legalFor(1).minRaiseTo).toBe(100);
    expect(() => h.act(2, 'fold')).toThrow('NOT_YOUR_TURN');
    h.act(1, 'fold');
    const ev = h.act(2, 'fold');
    expect(last(ev, 'potAwarded').data.awards).toEqual([
      { amount: 90, winners: [{ seat: 0, amount: 90 }], handName: null, bestCards: [] },
    ]);
    expect(h.isComplete).toBe(true);
    expect(h.stacks()).toEqual({ 0: 1030, 1: 990, 2: 980 });
  });

  test('a short all-in raise does not reopen raising for players who already acted', () => {
    const h = hand({ 0: 1000, 1: 130, 2: 1000, 3: 1000 });
    h.start(); // dealer 0, sb 1, bb 2, first 3
    h.act(3, 'raise', 100);
    h.act(0, 'call');
    h.act(1, 'allin'); // to 130: only +30, less than a full raise of 80
    expect(h.currentBet).toBe(130);
    expect(h.legalFor(2)).toMatchObject({ canRaise: true, minRaiseTo: 210 }); // BB hasn't acted yet
    h.act(2, 'call');
    expect(h.legalFor(3)).toMatchObject({ canRaise: false, canAllIn: false, canCall: true, callAmount: 30 });
    expect(() => h.act(3, 'raise', 300)).toThrow('ILLEGAL_ACTION');
    h.act(3, 'call');
    const ev = h.act(0, 'call');
    expect(last(ev, 'street').data.phase).toBe('flop');
  });

  test('cannot check when facing a bet', () => {
    const h = hand({ 0: 1000, 1: 1000 });
    h.start();
    expect(() => h.act(0, 'check')).toThrow('ILLEGAL_ACTION');
  });
});

describe('showdown', () => {
  test('all-ins run out the board and side pots go to the right players', () => {
    // deal order [1, 2, 0]
    const deck = deckFor([['Ah', 'Ad'], ['Qh', 'Jd'], ['Kh', 'Ks']], ['Kc', '8d', '4s', '7h', '2s']);
    const h = hand({ 0: 100, 1: 300, 2: 1000 }, { deck });
    h.start();
    h.act(0, 'allin');
    h.act(1, 'allin');
    const ev = h.act(2, 'call');
    expect(types(ev)).toEqual(['action', 'street', 'street', 'street', 'showdown', 'potAwarded', 'handEnded']);
    const { awards } = last(ev, 'potAwarded').data;
    expect(awards[0]).toMatchObject({ amount: 300, winners: [{ seat: 0, amount: 300 }], handName: 'Three of a Kind' });
    expect(awards[1]).toMatchObject({ amount: 400, winners: [{ seat: 1, amount: 400 }], handName: 'Pair' });
    expect(h.stacks()).toEqual({ 0: 300, 1: 400, 2: 700 });
    expect(last(ev, 'showdown').data.reveals.map((r) => r.seat)).toEqual([1, 2, 0]);
  });

  test('split pot gives the odd soldier to the winner closest to the dealer\'s left', () => {
    const deck = deckFor([['3c', '4d'], ['5c', '6d'], ['7c', '8d']], ['Ah', 'Kd', 'Qs', 'Jc', 'Th']);
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 }, { deck, sb: 5, bb: 20 });
    h.start();
    h.act(0, 'call');
    h.act(1, 'fold');
    h.act(2, 'check');
    let ev;
    for (let street = 0; street < 3; street++) {
      h.act(2, 'check');
      ev = h.act(0, 'check');
    }
    const { awards } = last(ev, 'potAwarded').data;
    expect(awards).toEqual([{
      amount: 45, winners: [{ seat: 2, amount: 23 }, { seat: 0, amount: 22 }],
      handName: 'Straight', bestCards: expect.any(Array),
    }]);
    expect(h.stacks()).toEqual({ 0: 1002, 1: 995, 2: 1003 });
  });
});

describe('timeouts and leaving', () => {
  test('timeout folds when facing a bet and checks when it can', () => {
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 });
    h.start();
    const ev = h.timeout(0);
    expect(ev[0].data).toMatchObject({ seat: 0, type: 'fold', auto: true });
    h.act(1, 'call');
    const ev2 = h.timeout(2);
    expect(ev2[0].data).toMatchObject({ seat: 2, type: 'check', auto: true });
  });

  test('forceFold out of turn keeps the turn where it was', () => {
    const h = hand({ 0: 1000, 1: 1000, 2: 1000 });
    h.start();
    const ev = h.forceFold(2);
    expect(types(ev)).toEqual(['action']);
    expect(h.turnSeat).toBe(0);
    const ev2 = h.act(0, 'fold');
    expect(last(ev2, 'potAwarded').data.awards[0].winners).toEqual([{ seat: 1, amount: 30 }]);
  });

  test('short big blind all-in from posting', () => {
    const h = hand({ 0: 1000, 1: 15 }, { dealer: 0 }); // heads-up: 0 is SB, 1 is BB with only 15
    h.start();
    expect(h.players.get(1).allIn).toBe(true);
    const ev = h.act(0, 'call');
    expect(types(ev)).toContain('showdown');
    expect(sum(h.stacks())).toBe(1015);
  });
});

test('random play never creates or loses soldiers (3000 hands)', () => {
  for (let i = 0; i < 3000; i++) {
    const n = 2 + Math.floor(Math.random() * 7);
    const seats = [0, 1, 2, 3, 4, 5, 6, 7].sort(() => Math.random() - 0.5).slice(0, n);
    const stacks = {};
    for (const s of seats) stacks[s] = 1 + Math.floor(Math.random() * 2000);
    const before = sum(stacks);
    const h = hand(stacks, { dealer: seats[0] });
    h.start();
    let guard = 0;
    while (!h.isComplete) {
      if (++guard > 500) throw new Error('hand did not finish');
      const seat = h.turnSeat;
      const l = h.legalFor(seat);
      const options = ['fold'];
      if (l.canCheck) options.push('check', 'check');
      if (l.canCall) options.push('call', 'call');
      if (l.canRaise) options.push('raise');
      if (l.canAllIn) options.push('allin');
      if (Math.random() < 0.05) {
        // someone else leaves mid-hand
        const other = seats.find((s) => s !== seat && !h.players.get(s).folded);
        if (other !== undefined) { h.forceFold(other); continue; }
      }
      const t = options[Math.floor(Math.random() * options.length)];
      const amt = t === 'raise' ? l.minRaiseTo + Math.floor(Math.random() * (l.maxRaiseTo - l.minRaiseTo + 1)) : undefined;
      h.act(seat, t, amt);
      expect(sum(h.stacks()) + h.committedTotal()).toBe(before);
    }
    expect(sum(h.stacks())).toBe(before);
  }
});
