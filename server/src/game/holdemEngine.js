// One hand of No-Limit Texas Hold'em. Pure logic: no timers, sockets or database.
// Every public method returns the list of events it produced, in order.
const { computePots } = require('./pots');
const { findWinners } = require('./handEval');

const STREETS = ['preflop', 'flop', 'turn', 'river'];

class GameError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}

class HoldemHand {
  // players: [{ seat, stack }], all with stack > 0. deck: shuffled array of card codes.
  constructor({ players, dealerSeat, smallBlind, bigBlind, deck, handNumber = 1 }) {
    if (players.length < 2) throw new GameError('NOT_ENOUGH_PLAYERS');
    this.handNumber = handNumber;
    this.smallBlind = smallBlind;
    this.bigBlind = bigBlind;
    this.deck = deck.slice();
    this.order = players.map((p) => p.seat).sort((a, b) => a - b);
    this.players = new Map();
    for (const { seat, stack } of players) {
      this.players.set(seat, {
        seat, stack, bet: 0, committed: 0, cards: [],
        folded: false, allIn: false, acted: false, raiseRightsId: -1, lastAction: null, revealed: false,
      });
    }
    this.dealerSeat = this.players.has(dealerSeat) ? dealerSeat : this.order[0];
    this.phase = 'new';
    this.board = [];
    this.currentBet = 0;
    this.minRaise = bigBlind;
    this.fullRaiseId = 0;
    this.turnSeat = -1;
    this.sbSeat = -1;
    this.bbSeat = -1;
    this.events = [];
  }

  // ---------- public API ----------

  start() {
    if (this.phase !== 'new') throw new GameError('ALREADY_STARTED');
    const headsUp = this.order.length === 2;
    this.sbSeat = headsUp ? this.dealerSeat : this._next(this.dealerSeat);
    this.bbSeat = this._next(this.sbSeat);

    const dealOrder = [];
    let s = this.dealerSeat;
    for (let i = 0; i < this.order.length; i++) {
      s = this._next(s);
      dealOrder.push(s);
    }
    for (let round = 0; round < 2; round++) {
      for (const seat of dealOrder) this.players.get(seat).cards.push(this.deck.pop());
    }

    this.phase = 'preflop';
    this._emit('handStarted', {
      handNumber: this.handNumber, dealerSeat: this.dealerSeat,
      sbSeat: this.sbSeat, bbSeat: this.bbSeat, seats: dealOrder,
    });

    const sb = this._post(this.sbSeat, this.smallBlind, 'sb');
    const bb = this._post(this.bbSeat, this.bigBlind, 'bb');
    this._emit('blinds', { sb: { seat: this.sbSeat, amount: sb }, bb: { seat: this.bbSeat, amount: bb } });
    this.currentBet = this.bigBlind;

    if (this._roundComplete()) this._endStreet(); // e.g. both players all-in from the blinds
    else {
      this.turnSeat = this._next(this.bbSeat, (p) => this._needsToAct(p));
      this._emit('turn', { seat: this.turnSeat });
    }
    return this._drain();
  }

  act(seat, type, amount, auto = false) {
    if (!STREETS.includes(this.phase)) throw new GameError('HAND_NOT_ACTIVE');
    if (seat !== this.turnSeat) throw new GameError('NOT_YOUR_TURN');
    const legal = this.legalFor(seat);
    const p = this.players.get(seat);
    let put = 0;

    switch (type) {
      case 'fold':
        p.folded = true;
        break;
      case 'check':
        if (!legal.canCheck) throw new GameError('ILLEGAL_ACTION');
        break;
      case 'call':
        if (!legal.canCall) throw new GameError('ILLEGAL_ACTION');
        put = legal.callAmount;
        break;
      case 'raise': {
        if (!legal.canRaise) throw new GameError('ILLEGAL_ACTION');
        const to = Number(amount);
        if (!Number.isInteger(to) || to < legal.minRaiseTo || to > legal.maxRaiseTo) throw new GameError('BAD_AMOUNT');
        put = to - p.bet;
        break;
      }
      case 'allin':
        if (!legal.canAllIn) throw new GameError('ILLEGAL_ACTION');
        put = p.stack;
        break;
      default:
        throw new GameError('ILLEGAL_ACTION');
    }

    if (put > 0) this._put(p, put);
    p.acted = true;
    p.lastAction = p.folded ? 'fold' : p.allIn ? 'allin' : type;
    p.raiseRightsId = this.fullRaiseId;
    this._emit('action', {
      seat, type: p.lastAction, amount: put, bet: p.bet, stack: p.stack, allIn: p.allIn, auto,
    });
    this._afterChange();
    return this._drain();
  }

  // What the server does when the turn timer runs out: check if possible, otherwise fold.
  timeout(seat) {
    const legal = this.legalFor(seat);
    if (!legal) throw new GameError('NOT_YOUR_TURN');
    return this.act(seat, legal.canCheck ? 'check' : 'fold', undefined, true);
  }

  // Fold a player out of turn (they left the table or were disconnected too long).
  forceFold(seat) {
    const p = this.players.get(seat);
    if (!p || p.folded || !STREETS.includes(this.phase)) return [];
    if (seat === this.turnSeat) return this.act(seat, 'fold', undefined, true);
    p.folded = true;
    p.lastAction = 'fold';
    this._emit('action', { seat, type: 'fold', amount: 0, bet: p.bet, stack: p.stack, allIn: p.allIn, auto: true });
    this._afterChange();
    return this._drain();
  }

  legalFor(seat) {
    if (!STREETS.includes(this.phase) || seat !== this.turnSeat) return null;
    const p = this.players.get(seat);
    const toCall = this.currentBet - p.bet;
    const othersCanAct = this._seats().some((s) => s !== seat && this._canAct(this.players.get(s)));
    const raiseRights = (!p.acted || p.raiseRightsId < this.fullRaiseId) && othersCanAct && p.stack > toCall;
    const minRaiseTo = this.currentBet + this.minRaise;
    const maxRaiseTo = p.bet + p.stack;
    return {
      canFold: true,
      canCheck: toCall === 0,
      canCall: toCall > 0,
      callAmount: Math.min(toCall, p.stack),
      canRaise: raiseRights && maxRaiseTo >= minRaiseTo,
      canAllIn: p.stack > 0 && (p.stack <= toCall || raiseRights),
      minRaiseTo: Math.min(minRaiseTo, maxRaiseTo),
      maxRaiseTo,
    };
  }

  get isComplete() {
    return this.phase === 'complete';
  }

  // Stacks as they are right now: { seat: stack }.
  stacks() {
    const out = {};
    for (const p of this.players.values()) out[p.seat] = p.stack;
    return out;
  }

  // Soldiers on the table for this hand (pots + bets). Used to check that nothing is created or lost.
  committedTotal() {
    let t = 0;
    for (const p of this.players.values()) t += p.committed;
    return t;
  }

  publicState() {
    const players = [...this.players.values()];
    const done = this.phase === 'complete';
    const pots = done ? [] : computePots(players.map((p) => ({ seat: p.seat, committed: p.committed - p.bet, folded: p.folded })));
    const potTotal = done ? 0 : players.reduce((t, p) => t + p.committed, 0);
    return {
      phase: this.phase === 'complete' ? 'showdown' : this.phase,
      dealerSeat: this.dealerSeat, sbSeat: this.sbSeat, bbSeat: this.bbSeat,
      board: this.board.slice(),
      pots, potTotal,
      currentBet: this.currentBet,
      turnSeat: this.turnSeat,
      players: players.map((p) => ({
        seat: p.seat, stack: p.stack, bet: p.bet, folded: p.folded, allIn: p.allIn,
        lastAction: p.lastAction, cards: p.revealed ? p.cards.slice() : null,
      })),
    };
  }

  cardsOf(seat) {
    const p = this.players.get(seat);
    return p ? p.cards.slice() : [];
  }

  // ---------- internals ----------

  _seats() {
    return this.order;
  }

  _next(seat, pred = () => true) {
    let start = this.order.indexOf(seat);
    if (start === -1) {
      // seat isn't playing: start just before the first seat after it
      const after = this.order.findIndex((s) => s > seat);
      start = (after === -1 ? 0 : after) - 1;
    }
    for (let i = 1; i <= this.order.length; i++) {
      const s = this.order[(start + i + this.order.length) % this.order.length];
      if (pred(this.players.get(s))) return s;
    }
    return -1;
  }

  _canAct(p) {
    return !p.folded && !p.allIn;
  }

  _live() {
    return [...this.players.values()].filter((p) => !p.folded);
  }

  _put(p, amount) {
    const a = Math.min(amount, p.stack);
    const newBet = p.bet + a;
    if (newBet > this.currentBet) {
      const raiseSize = newBet - this.currentBet;
      if (raiseSize >= this.minRaise) {
        this.minRaise = raiseSize;
        this.fullRaiseId++;
      }
      this.currentBet = newBet; // a short all-in raises the bet but doesn't reopen raising
    }
    p.stack -= a;
    p.bet = newBet;
    p.committed += a;
    if (p.stack === 0) p.allIn = true;
    return a;
  }

  _post(seat, blind, label) {
    const p = this.players.get(seat);
    const a = Math.min(blind, p.stack);
    p.stack -= a;
    p.bet += a;
    p.committed += a;
    if (p.stack === 0) p.allIn = true;
    p.lastAction = label;
    return a;
  }

  _needsToAct(p) {
    return this._canAct(p) && (!p.acted || p.bet < this.currentBet);
  }

  _roundComplete() {
    const actors = [...this.players.values()].filter((p) => this._canAct(p));
    if (actors.length === 0) return true;
    // A lone player who isn't facing a bet has no one to play against.
    if (actors.length === 1 && actors[0].bet >= this.currentBet) return true;
    return actors.every((p) => !this._needsToAct(p));
  }

  _afterChange() {
    if (this._live().length === 1) return this._winWithoutShowdown();
    if (this._roundComplete()) return this._endStreet();
    if (!this._needsToAct(this.players.get(this.turnSeat))) {
      this.turnSeat = this._next(this.turnSeat, (p) => this._needsToAct(p));
      this._emit('turn', { seat: this.turnSeat });
    }
  }

  _endStreet() {
    // Keep dealing streets until someone has to act or the river is done.
    for (;;) {
      for (const p of this.players.values()) {
        p.bet = 0;
        p.acted = false;
        if (!p.folded) p.lastAction = p.allIn ? 'allin' : null;
      }
      this.currentBet = 0;
      this.minRaise = this.bigBlind;
      this.fullRaiseId++;

      if (this.phase === 'river') return this._showdown();

      const nextPhase = STREETS[STREETS.indexOf(this.phase) + 1];
      this.deck.pop(); // burn
      const n = nextPhase === 'flop' ? 3 : 1;
      const newCards = [];
      for (let i = 0; i < n; i++) newCards.push(this.deck.pop());
      this.board.push(...newCards);
      this.phase = nextPhase;
      this.turnSeat = -1;
      this._emit('street', { phase: nextPhase, newCards, board: this.board.slice() });

      const actors = [...this.players.values()].filter((p) => this._canAct(p));
      if (actors.length >= 2) {
        this.turnSeat = this._next(this.dealerSeat, (p) => this._canAct(p));
        this._emit('turn', { seat: this.turnSeat });
        return;
      }
    }
  }

  _showdown() {
    this.phase = 'showdown';
    this.turnSeat = -1;
    const live = this._fromDealerLeft(this._live().map((p) => p.seat));
    const { results } = findWinners(live.map((seat) => ({ seat, cards: this._sevenCards(seat) })));
    for (const seat of live) this.players.get(seat).revealed = true;
    this._emit('showdown', {
      reveals: live.map((seat) => ({ seat, cards: this.cardsOf(seat), handName: results.get(seat).handName })),
    });

    const pots = computePots([...this.players.values()].map((p) => ({ seat: p.seat, committed: p.committed, folded: p.folded })));
    const awards = pots.map((pot) => {
      const { winners, results: res } = findWinners(pot.eligibleSeats.map((seat) => ({ seat, cards: this._sevenCards(seat) })));
      const best = res.get(winners[0]);
      // A pot with a single contender (an uncalled bet) is just returned; no hand to show.
      const contested = pot.eligibleSeats.length > 1;
      return {
        amount: pot.amount,
        winners: this._split(pot.amount, winners),
        handName: contested ? best.handName : null,
        bestCards: contested ? best.bestCards : [],
      };
    });
    this._award(awards);
  }

  _winWithoutShowdown() {
    const winner = this._live()[0].seat;
    const total = this.committedTotal();
    this.turnSeat = -1;
    this._award([{ amount: total, winners: [{ seat: winner, amount: total }], handName: null, bestCards: [] }]);
  }

  _award(awards) {
    for (const a of awards) for (const w of a.winners) this.players.get(w.seat).stack += w.amount;
    for (const p of this.players.values()) {
      p.bet = 0;
      p.committed = 0;
    }
    this.phase = 'complete';
    this._emit('potAwarded', { awards });
    this._emit('handEnded', { handNumber: this.handNumber });
  }

  // Split evenly; leftover soldiers go one each to the winners closest to the dealer's left.
  _split(amount, seats) {
    const ordered = this._fromDealerLeft(seats);
    const share = Math.floor(amount / ordered.length);
    let rest = amount - share * ordered.length;
    return ordered.map((seat) => {
      const extra = rest > 0 ? 1 : 0;
      rest -= extra;
      return { seat, amount: share + extra };
    });
  }

  _fromDealerLeft(seats) {
    const set = new Set(seats);
    const out = [];
    let s = this.dealerSeat;
    for (let i = 0; i < this.order.length; i++) {
      s = this._next(s);
      if (set.has(s)) out.push(s);
    }
    return out;
  }

  _sevenCards(seat) {
    return this.players.get(seat).cards.concat(this.board);
  }

  // Each event carries the table state right after it, so clients can animate step by step.
  _emit(type, data) {
    this.events.push({ type, data, state: this.publicState() });
  }

  _drain() {
    const e = this.events;
    this.events = [];
    return e;
  }
}

module.exports = { HoldemHand, GameError, STREETS };
