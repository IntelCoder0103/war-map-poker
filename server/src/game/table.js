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
    this.showable = null; // { seat, userId, cards } — a winner by fold may show their cards until the next hand
    this.readyTimer = null; // waiting phase: sends players who never press Ready back to the lobby
    this.readyDeadline = 0;
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

  // The buy-in has already been taken from the player's balance. `session` identifies this sitting in the wallet.
  seatPlayer(user, stack, session = null) {
    const seat = this.seats.indexOf(null);
    if (seat === -1) throw new GameError('TABLE_FULL');
    this.seats[seat] = {
      userId: user.userId, username: user.username, stack, session,
      ready: this.started, // joining a running game counts as ready
      connected: true, ghost: false, discTimer: null,
      timeouts: 0, // turns in a row that ran out; too many and the player goes back to the lobby
      leaveReason: null, // set when an all-in player leaves: they are paid when the hand ends
      leaveAfterHand: false, // retreat requested while holding cards: leave once they fold or the hand ends
    };
    this._updateReadyTimer(); // before broadcasting, so snapshots carry the countdown
    this._broadcast('playerJoined', { seat, username: user.username });
    this.hooks.onChange(this);
    return seat;
  }

  ready(userId) {
    const seat = this._requireSeat(userId);
    const s = this.seats[seat];
    if (!s.ready) {
      s.ready = true;
      this._updateReadyTimer(); // before broadcasting, so snapshots carry the countdown
      this._broadcast('playerReady', { seat });
    }
    this._maybeStartHand();
  }

  action(userId, type, amount) {
    const seat = this._requireSeat(userId);
    if (!this.hand) throw new GameError('NOT_YOUR_TURN');
    const events = this.hand.act(seat, type, amount);
    this.seats[seat].timeouts = 0; // acting by hand shows the player is here
    this._process(events);
  }

  // reason: 'left' | 'broke' | 'timeout' | 'idle'. Returns the player's public user (with the new balance),
  // or null when the payout comes later (an all-in player stays in the hand until it ends).
  async leave(userId, reason = 'left') {
    const seat = this._requireSeat(userId);
    const s = this.seats[seat];
    if (s.discTimer) clearTimeout(s.discTimer);
    s.discTimer = null;

    const p = this.hand && this.hand.players.get(seat);
    if (p && p.allIn && !p.folded) {
      // All-in: nothing is left to decide, so they keep their share of the pot.
      // The seat stays in the hand and is paid out when the hand ends.
      s.ghost = true;
      s.leaveReason = reason;
      this.hooks.notify(s.userId, 'table:removed', { reason });
      this._broadcast('playerLeft', { seat, username: s.username, reason });
      this.hooks.onChange(this);
      return null;
    }

    if (p && !p.folded) {
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

  // Retreat. Not holding cards (between hands, or already folded): leave at once.
  // Holding cards: mark the seat; the player keeps playing and leaves as soon as they fold
  // (by choice or on the turn timer) or when the hand ends. on=false cancels. Returns { leftNow, user }.
  async setLeaveAfterHand(userId, on = true) {
    const seat = this._requireSeat(userId);
    const s = this.seats[seat];
    if (on && !this._holdsCards(seat)) return { leftNow: true, user: await this.leave(userId, 'left') };
    if (s.leaveAfterHand === !!on) return { leftNow: false };
    s.leaveAfterHand = !!on;
    this._broadcast('leaving', { seat, on: s.leaveAfterHand });
    return { leftNow: false };
  }

  _holdsCards(seat) {
    const p = this.hand && this.hand.players.get(seat);
    return !!(p && !p.folded);
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
      privateCode: this.level.code || null, // set for private rooms: share it so friends can join
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
      readyDeadline: this.readyDeadline || 0, // ms; while waiting, when players who aren't ready go back to the lobby
      seats: this.seats.map((s, seat) => {
        if (!s) return null;
        const p = byPlayer.get(seat);
        // Someone who left mid-hand stays on screen (offline, with their bet) until the hand ends.
        if (s.ghost && !(p && this.hand && hs === this.handState)) return null;
        return {
          seat, userId: s.userId, username: s.username,
          stack: p ? p.stack : s.stack,
          bet: p ? p.bet : 0,
          totalBet: p ? p.committed : 0,
          inHand: !!p,
          folded: p ? p.folded : false,
          allIn: p ? p.allIn : false,
          ready: s.ready,
          connected: s.connected && !s.ghost,
          leavingAfterHand: !!s.leaveAfterHand,
          lastAction: p ? p.lastAction : null,
          cards: p ? p.cards : null,
        };
      }),
      legal,
      canShowCards: !!(this.showable && this.showable.userId === userId && this.showable.seat === yourSeat),
    };
  }

  // A player who won because everyone else folded may reveal their hole cards before the next hand.
  showCards(userId) {
    const seat = this._requireSeat(userId);
    const sh = this.showable;
    if (!sh || sh.userId !== userId || sh.seat !== seat || this.hand || !this.handState) throw new GameError('CANNOT_SHOW');
    this.showable = null;
    const hs = this.handState;
    this.handState = { ...hs, players: hs.players.map((p) => (p.seat === seat ? { ...p, cards: sh.cards.slice() } : p)) };
    this._broadcast('cardsShown', { seat, cards: sh.cards.slice() });
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
    this.showable = null;
    this._updateReadyTimer(); // a hand is running: no ready countdown
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
    // A fold win can be shown: set it before the last events go out, so their snapshots say so.
    const h = this.hand;
    if (h && h.isComplete && h.foldWinner !== undefined && this.seats[h.foldWinner] && !this.seats[h.foldWinner].ghost) {
      this.showable = { seat: h.foldWinner, userId: this.seats[h.foldWinner].userId, cards: h.cardsOf(h.foldWinner) };
    }
    for (const e of events) {
      if (e.type === 'turn') {
        this.turnDeadline = Date.now() + this.settings.turnTimeMs;
        e.data.deadline = this.turnDeadline;
      }
      this.handState = e.state;
      this._broadcast(e.type, e.data, e.state);
    }
    if (!this.hand) return;
    if (this.hand.isComplete) {
      this._finishHand(); // also removes players who asked to retreat
      return;
    }
    // A player who asked to retreat just folded: they go now, the hand plays on without them.
    for (let seat = 0; seat < this.seats.length; seat++) {
      const s = this.seats[seat];
      if (s && !s.ghost && s.leaveAfterHand && !this._holdsCards(seat)) {
        s.leaveAfterHand = false; // handled here, once
        this.leave(s.userId, 'left').catch((e) => this.hooks.log('retreat after fold failed', e));
      }
    }
    if (this.hand && !this.hand.isComplete) this._armTurnTimer();
  }

  _armTurnTimer() {
    const seat = this.hand.turnSeat;
    if (seat === -1) return;
    this.turnTimer = setTimeout(() => {
      this.turnTimer = null;
      if (!this.hand || this.hand.turnSeat !== seat) return;
      const s = this.seats[seat];
      this._process(this.hand.timeout(seat));
      // Too many missed turns in a row: the player is away, so send them back to the lobby.
      if (s && !s.ghost && this.seats[seat] === s && ++s.timeouts >= this.settings.idleTimeouts) {
        this.leave(s.userId, 'idle').catch((e) => this.hooks.log('leave on idle failed', e));
      }
    }, Math.max(0, this.turnDeadline - Date.now()));
  }

  _clearTurnTimer() {
    if (this.turnTimer) clearTimeout(this.turnTimer);
    this.turnTimer = null;
  }

  _finishHand() {
    const hand = this.hand;
    const stacks = hand.stacks();
    this.hand = null;
    const delay = hand.hadShowdown ? this.settings.showdownDelayMs ?? this.settings.nextHandDelayMs : this.settings.nextHandDelayMs;
    this.turnDeadline = 0;

    const toSave = [];
    const leftAllIn = []; // [seatInfo, stack]: left while all-in, paid out now
    for (const [seatStr, stack] of Object.entries(stacks)) {
      const seat = Number(seatStr);
      const s = this.seats[seat];
      if (!s) continue;
      if (s.ghost) {
        this.seats[seat] = null; // the player left mid-hand (folded ones were paid when they left)
        if (s.leaveReason) leftAllIn.push([s, stack]);
        continue;
      }
      s.stack = stack;
      toSave.push({ userId: s.userId, session: s.session, stack });
    }
    this.hooks.wallet.saveStacks(toSave).catch((e) => this.hooks.log('saveStacks failed', e));

    // Arm the next hand first, so removing players below can't start one right away.
    this.nextHandTimer = setTimeout(() => {
      this.nextHandTimer = null;
      this._maybeStartHand();
    }, delay);

    for (const [s, stack] of leftAllIn) {
      this.hooks.onPlayerGone(s.userId, this);
      this._payOut(s, stack).catch((e) => this.hooks.log('pay out after all-in leave failed', e));
    }
    for (let seat = 0; seat < this.seats.length; seat++) {
      const s = this.seats[seat];
      if (s && !s.ghost && s.leaveAfterHand && s.stack > 0) {
        this.seats[seat] = null;
        if (s.discTimer) clearTimeout(s.discTimer);
        this._removed(seat, s, s.stack, 'left').catch((e) => this.hooks.log('leave after hand failed', e));
      }
    }
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
    this.showable = null;
    this.handState = null;
    this.handCards = {};
    this.handSeatUsers = {};
    for (const s of this.seats) if (s) s.ready = false;
    this._broadcast('waiting', {});
    this._updateReadyTimer();
  }

  // Waiting for Ready: once 2+ players are ready, the others get readyTimeoutMs to join in,
  // then go back to the lobby, so one idle player can't hold up the whole table.
  _updateReadyTimer() {
    const seated = this.seats.filter((s) => s && !s.ghost);
    const ready = seated.filter((s) => s.ready).length;
    const waitingOnSomeone = !this.started && !this.hand && ready >= 2 && ready < seated.length;
    if (!waitingOnSomeone) {
      if (this.readyTimer) clearTimeout(this.readyTimer);
      this.readyTimer = null;
      this.readyDeadline = 0;
      return;
    }
    if (this.readyTimer) return;
    this.readyDeadline = Date.now() + this.settings.readyTimeoutMs;
    this.readyTimer = setTimeout(() => {
      this.readyTimer = null;
      this.readyDeadline = 0;
      if (this.started || this.hand) return;
      for (const s of this.seats.filter((x) => x && !x.ghost && !x.ready)) {
        this.leave(s.userId, 'idle').catch((e) => this.hooks.log('leave when not ready failed', e));
      }
    }, this.settings.readyTimeoutMs);
  }

  // ---------- helpers ----------

  async _removed(seat, s, stack, reason) {
    this._updateReadyTimer(); // before broadcasting, so snapshots carry the right countdown
    this._broadcast('playerLeft', { seat, username: s.username, reason });
    this.hooks.onPlayerGone(s.userId, this);
    this.hooks.notify(s.userId, 'table:removed', { reason });
    this.hooks.onChange(this);
    if (!this.hand) this._maybeStartHand();
    return this._payOut(s, stack);
  }

  async _payOut(s, stack) {
    const pending = this.hooks.wallet.cashOut(s.userId, stack, s.session);
    // The player may already be back in the lobby: a new join waits for this to be saved.
    if (this.hooks.trackPayout) this.hooks.trackPayout(s.userId, pending);
    const user = await pending;
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
    if (this.readyTimer) clearTimeout(this.readyTimer);
    for (const s of this.seats) if (s && s.discTimer) clearTimeout(s.discTimer);
  }
}

module.exports = { Table };
