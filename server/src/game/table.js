// One poker table: seats, ready-up, hand loop, turn timers and disconnects.
// The Hold'em rules live in holdemEngine.js; this file wires them to players.
const { HoldemHand, GameError } = require('./holdemEngine');
const { shuffledDeck } = require('./deck');

class Table {
  // hooks: { wallet, notify(userId, event, payload), onPlayerGone(userId), onChange(table), log }
  constructor({ id, level, settings, hooks }) {
    this.id = id;
    this.level = level; // { id, name, buyIn, smallBlind, bigBlind }
    this.settings = settings; // { maxSeats, turnTimeMs, nextHandDelayMs, disconnectGraceMs }
    this.hooks = hooks;
    this.seats = new Array(settings.maxSeats).fill(null);
    this.started = false; // true once the first hand began; hands then run back to back
    this.hand = null;
    this.handState = null; // last engine state, kept on screen between hands
    this.handCards = {}; // seat -> hole cards for the current/last hand
    this.handSeatUsers = {}; // seat -> userId of who played that seat in the current/last hand
    this.handNumber = 0;
    this.dealerSeat = -1;
    this.seq = 0;
    this.turnTimer = null;
    this.turnDeadline = 0;
    this.nextHandTimer = null;
  }

  // ---------- queries ----------

  seatOf(userId) {
    return this.seats.findIndex((s) => s && !s.ghost && s.userId === userId);
  }

  get playerCount() {
    return this.seats.filter((s) => s && !s.ghost).length;
  }

  hasFreeSeat() {
    return this.seats.some((s) => s === null);
  }

  get isEmpty() {
    return this.seats.every((s) => s === null);
  }

  // ---------- player commands ----------

  // The buy-in has already been taken from the player's balance.
  seatPlayer(user, stack) {
    const seat = this.seats.indexOf(null);
    if (seat === -1) throw new GameError('TABLE_FULL');
    this.seats[seat] = {
      userId: user.userId, username: user.username, stack,
      ready: this.started, // joining a running game counts as ready
      connected: true, ghost: false, discTimer: null,
    };
    this._broadcast('playerJoined', { seat, username: user.username });
    this.hooks.onChange(this);
    return seat;
  }

  ready(userId) {
    const seat = this._requireSeat(userId);
    const s = this.seats[seat];
    if (!s.ready) {
      s.ready = true;
      this._broadcast('playerReady', { seat });
    }
    this._maybeStartHand();
  }

  action(userId, type, amount) {
    const seat = this._requireSeat(userId);
    if (!this.hand) throw new GameError('NOT_YOUR_TURN');
    this._process(this.hand.act(seat, type, amount));
  }

  // reason: 'left' | 'broke' | 'timeout'. Returns the player's public user (with the new balance).
  async leave(userId, reason = 'left') {
    const seat = this._requireSeat(userId);
    const s = this.seats[seat];
    if (s.discTimer) clearTimeout(s.discTimer);

    if (this.hand && this.hand.players.has(seat)) {
      this._process(this.hand.forceFold(seat));
      // Folding may have ended the hand, and a broke player is already removed then.
      if (this.seats[seat] !== s) return null;
    }
    let stack = s.stack;
    if (this.hand && this.hand.players.has(seat)) {
      // Still mid-hand: their folded soldiers stay in the pot; keep the seat blocked until the hand ends.
      stack = this.hand.stacks()[seat];
      s.ghost = true;
    } else {
      this.seats[seat] = null;
    }
    return this._removed(seat, s, stack, reason);
  }

  setConnected(userId, connected) {
    const seat = this.seatOf(userId);
    if (seat === -1) return;
    const s = this.seats[seat];
    if (s.connected === connected) return;
    s.connected = connected;
    if (s.discTimer) clearTimeout(s.discTimer);
    s.discTimer = null;
    if (!connected) {
      s.discTimer = setTimeout(() => {
        this.leave(userId, 'timeout').catch((e) => this.hooks.log('leave on timeout failed', e));
      }, this.settings.disconnectGraceMs);
    }
    this._broadcast('connection', { seat, connected });
  }

  // ---------- snapshot ----------

  snapshotFor(userId, handState = this.handState) {
    const yourSeat = this.seatOf(userId);
    const hs = handState;
    const live = this.hand && hs === this.handState;
    const legal = live && yourSeat !== -1 && hs.turnSeat === yourSeat && this.handSeatUsers[yourSeat] === userId
      ? this.hand.legalFor(yourSeat) : null;
    // Only use hand data for a seat if the same person still sits there
    // (a seat can change hands between two hands).
    const byPlayer = new Map((hs ? hs.players : [])
      .filter((p) => this.seats[p.seat] && this.handSeatUsers[p.seat] === this.seats[p.seat].userId)
      .map((p) => [p.seat, p]));

    return {
      tableId: this.id,
      level: this.level.id, name: this.level.name,
      buyIn: this.level.buyIn, smallBlind: this.level.smallBlind, bigBlind: this.level.bigBlind,
      maxSeats: this.settings.maxSeats,
      seq: this.seq,
      serverTime: Date.now(),
      phase: hs ? hs.phase : 'waiting',
      handNumber: this.handNumber,
      dealerSeat: hs ? hs.dealerSeat : -1,
      sbSeat: hs ? hs.sbSeat : -1,
      bbSeat: hs ? hs.bbSeat : -1,
      board: hs ? hs.board : [],
      pots: hs ? hs.pots : [],
      potTotal: hs ? hs.potTotal : 0,
      currentBet: hs ? hs.currentBet : 0,
      turnSeat: hs ? hs.turnSeat : -1,
      turnDeadline: hs && hs.turnSeat !== -1 ? this.turnDeadline : 0,
      turnTimeMs: this.settings.turnTimeMs,
      yourSeat,
      yourCards: yourSeat !== -1 && byPlayer.has(yourSeat) ? (this.handCards[yourSeat] || []) : [],
      seats: this.seats.map((s, seat) => {
        if (!s || s.ghost) return null;
        const p = byPlayer.get(seat);
        return {
          seat, userId: s.userId, username: s.username,
          stack: p ? p.stack : s.stack,
          bet: p ? p.bet : 0,
          inHand: !!p,
          folded: p ? p.folded : false,
          allIn: p ? p.allIn : false,
          ready: s.ready,
          connected: s.connected,
          lastAction: p ? p.lastAction : null,
          cards: p ? p.cards : null,
        };
      }),
      legal,
    };
  }

  // ---------- hand loop ----------

  _eligibleSeats() {
    return this.seats.map((s, i) => (s && !s.ghost && s.stack > 0 ? i : -1)).filter((i) => i !== -1);
  }

  _maybeStartHand() {
    if (this.hand || this.nextHandTimer) return;
    const eligible = this._eligibleSeats();
    if (!this.started) {
      const seated = this.seats.filter((s) => s && !s.ghost);
      if (eligible.length >= 2 && seated.every((s) => s.ready)) this._startHand();
      return;
    }
    if (eligible.length >= 2) this._startHand();
    else this._backToWaiting();
  }

  _startHand() {
    const eligible = this._eligibleSeats();
    this.started = true;
    this.handNumber++;
    this.dealerSeat = eligible.find((s) => s > this.dealerSeat) ?? eligible[0];
    this.hand = new HoldemHand({
      players: eligible.map((seat) => ({ seat, stack: this.seats[seat].stack })),
      dealerSeat: this.dealerSeat,
      smallBlind: this.level.smallBlind,
      bigBlind: this.level.bigBlind,
      deck: shuffledDeck(),
      handNumber: this.handNumber,
    });
    const events = this.hand.start(); // deals the cards
    this.handCards = {};
    this.handSeatUsers = {};
    for (const seat of eligible) {
      this.handCards[seat] = this.hand.cardsOf(seat);
      this.handSeatUsers[seat] = this.seats[seat].userId;
    }
    this._process(events);
  }

  // Send engine events out one by one, then arm the turn timer or wrap up the hand.
  _process(events) {
    this._clearTurnTimer();
    for (const e of events) {
      if (e.type === 'turn') {
        this.turnDeadline = Date.now() + this.settings.turnTimeMs;
        e.data.deadline = this.turnDeadline;
      }
      this.handState = e.state;
      this._broadcast(e.type, e.data, e.state);
    }
    if (!this.hand) return;
    if (this.hand.isComplete) this._finishHand();
    else this._armTurnTimer();
  }

  _armTurnTimer() {
    const seat = this.hand.turnSeat;
    if (seat === -1) return;
    this.turnTimer = setTimeout(() => {
      this.turnTimer = null;
      if (this.hand && this.hand.turnSeat === seat) this._process(this.hand.timeout(seat));
    }, Math.max(0, this.turnDeadline - Date.now()));
  }

  _clearTurnTimer() {
    if (this.turnTimer) clearTimeout(this.turnTimer);
    this.turnTimer = null;
  }

  _finishHand() {
    const stacks = this.hand.stacks();
    this.hand = null;
    this.turnDeadline = 0;

    const toSave = [];
    for (const [seatStr, stack] of Object.entries(stacks)) {
      const seat = Number(seatStr);
      const s = this.seats[seat];
      if (!s) continue;
      if (s.ghost) {
        this.seats[seat] = null; // the player already left and was paid out
        continue;
      }
      s.stack = stack;
      toSave.push({ userId: s.userId, stack });
    }
    this.hooks.wallet.saveStacks(toSave).catch((e) => this.hooks.log('saveStacks failed', e));

    // Arm the next hand first, so removing players below can't start one right away.
    this.nextHandTimer = setTimeout(() => {
      this.nextHandTimer = null;
      this._maybeStartHand();
    }, this.settings.nextHandDelayMs);

    for (let seat = 0; seat < this.seats.length; seat++) {
      const s = this.seats[seat];
      if (s && !s.ghost && s.stack === 0) {
        this.seats[seat] = null;
        this._removed(seat, s, 0, 'broke').catch((e) => this.hooks.log('remove broke failed', e));
      }
    }

    if (this._eligibleSeats().length < 2) {
      clearTimeout(this.nextHandTimer);
      this.nextHandTimer = null;
      this._backToWaiting();
    }
    this.hooks.onChange(this);
  }

  _backToWaiting() {
    this.started = false;
    this.handState = null;
    this.handCards = {};
    this.handSeatUsers = {};
    for (const s of this.seats) if (s) s.ready = false;
    this._broadcast('waiting', {});
  }

  // ---------- helpers ----------

  async _removed(seat, s, stack, reason) {
    this._broadcast('playerLeft', { seat, username: s.username, reason });
    this.hooks.onPlayerGone(s.userId, this);
    this.hooks.notify(s.userId, 'table:removed', { reason });
    this.hooks.onChange(this);
    if (!this.hand) this._maybeStartHand();
    const user = await this.hooks.wallet.cashOut(s.userId, stack);
    if (user) this.hooks.notify(s.userId, 'user:balance', { balance: user.balance, nextRefillAt: user.nextRefillAt });
    return user;
  }

  _requireSeat(userId) {
    const seat = this.seatOf(userId);
    if (seat === -1) throw new GameError('NOT_SEATED');
    return seat;
  }

  _broadcast(type, data, handState = this.handState) {
    this.seq++;
    for (const s of this.seats) {
      if (!s || s.ghost) continue;
      this.hooks.notify(s.userId, 'table:event', {
        seq: this.seq, type, data, state: this.snapshotFor(s.userId, handState),
      });
    }
  }

  // Stop all timers (server shutdown / tests).
  dispose() {
    this._clearTurnTimer();
    if (this.nextHandTimer) clearTimeout(this.nextHandTimer);
    for (const s of this.seats) if (s && s.discTimer) clearTimeout(s.discTimer);
  }
}

module.exports = { Table };
