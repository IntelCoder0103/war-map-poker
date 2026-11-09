// Rooms (one per level) and the tables inside them. A full table opens another one at the same level.
// Private rooms: a player creates a table with custom stakes; others join with its code (or an invite).
const crypto = require('crypto');
const { Table } = require('./table');
const { GameError } = require('./holdemEngine');

class TableManager {
  // hooks: { wallet, notify(userId, event, payload), onLobbyChange(rooms), log }
  constructor({ levels, settings, hooks, privateRooms }) {
    this.levels = levels;
    this.privateRules = privateRooms || { minBuyIn: 100, maxBuyIn: 1000000, minStackBigBlinds: 10, codeLength: 6 };
    this.privateTables = new Map(); // code -> Table
    this.settings = settings;
    this.hooks = hooks;
    this.tables = new Map(levels.map((l) => [l.id, []])); // levelId -> [Table]
    this.counters = new Map(levels.map((l) => [l.id, 0]));
    this.userTable = new Map(); // userId -> Table
    this.joining = new Set();
    this.payouts = new Map(); // userId -> cash-out still being saved after leaving a table
    this._lobbyTimer = null;
    for (const l of levels) this._newTable(l);
  }

  tableOf(userId) {
    return this.userTable.get(userId) || null;
  }

  // Seats the user at `table` after taking `buyIn` from their balance (atomic, see wallet.buyIn).
  async _seat(user, buyIn, pickTable) {
    this._checkNotSeated(user);
    this.joining.add(user.userId);
    try {
      // Just left (or busted at) a table and tapped join right away: let that cash-out land first.
      const pending = this.payouts.get(user.userId);
      if (pending) await pending;
      const session = await this.hooks.wallet.buyIn(user.userId, buyIn);
      let table;
      try {
        table = pickTable();
      } catch (e) {
        // The room vanished or filled up while the buy-in was saved: give the soldiers back.
        await this.hooks.wallet.cashOut(user.userId, buyIn, session);
        throw e;
      }
      this.userTable.set(user.userId, table);
      table.seatPlayer(user, buyIn, session);
      return table;
    } finally {
      this.joining.delete(user.userId);
    }
  }

  _checkNotSeated(user) {
    const current = this.userTable.get(user.userId);
    // Left while all-in: still in that hand until it ends, then paid out.
    if (current && current.seatOf(user.userId) === -1) throw new GameError('LEAVING_TABLE');
    if (current || this.joining.has(user.userId)) throw new GameError('ALREADY_SEATED');
  }

  // ---------- private rooms ----------

  // stakes: { buyIn, bigBlind } (whole numbers). The small blind is half the big blind.
  async createPrivate(user, stakes) {
    const r = this.privateRules;
    const buyIn = Number(stakes && stakes.buyIn);
    const bigBlind = Number(stakes && stakes.bigBlind);
    if (!Number.isInteger(buyIn) || !Number.isInteger(bigBlind) || bigBlind < 2
      || buyIn < r.minBuyIn || buyIn > r.maxBuyIn || buyIn < bigBlind * r.minStackBigBlinds) {
      throw new GameError('BAD_STAKES');
    }
    const code = this._newCode();
    const level = {
      id: 'private', name: 'Private', buyIn, smallBlind: Math.floor(bigBlind / 2), bigBlind, code, isPrivate: true,
    };
    return this._seat(user, buyIn, () => {
      if (this.privateTables.has(code)) return this.privateTables.get(code);
      const table = this._makeTable(level, `private-${code}`);
      this.privateTables.set(code, table);
      return table;
    });
  }

  async joinByCode(user, code) {
    const key = String(code || '').trim().toUpperCase();
    const table = this.privateTables.get(key);
    if (!table) throw new GameError('ROOM_NOT_FOUND');
    this._checkNotSeated(user);
    if (!table.hasFreeSeat()) throw new GameError('TABLE_FULL');
    return this._seat(user, table.level.buyIn, () => {
      if (this.privateTables.get(key) !== table) throw new GameError('ROOM_NOT_FOUND');
      if (!table.hasFreeSeat()) throw new GameError('TABLE_FULL');
      return table;
    });
  }

  // What the lobby shows in the "private room" dialog.
  privateLimits() {
    const { minBuyIn, maxBuyIn, minStackBigBlinds } = this.privateRules;
    return { minBuyIn, maxBuyIn, minStackBigBlinds };
  }

  privateTable(code) {
    return this.privateTables.get(String(code || '').trim().toUpperCase()) || null;
  }

  _newCode() {
    // No 0/O/1/I, so codes are easy to read out loud.
    const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (;;) {
      let code = '';
      for (let i = 0; i < this.privateRules.codeLength; i++) code += alphabet[crypto.randomInt(alphabet.length)];
      if (!this.privateTables.has(code)) return code;
    }
  }

  // ---------- public rooms ----------

  async join(user, levelId) {
    const level = this.levels.find((l) => l.id === levelId);
    if (!level) throw new GameError('UNKNOWN_LEVEL');
    const tables = this.tables.get(level.id);
    return this._seat(user, level.buyIn, () => tables.find((t) => t.hasFreeSeat()) || this._newTable(level));
  }

  rooms() {
    return this.levels.map((l) => {
      const tables = this.tables.get(l.id);
      return {
        id: l.id, name: l.name, buyIn: l.buyIn, smallBlind: l.smallBlind, bigBlind: l.bigBlind,
        players: tables.reduce((n, t) => n + t.playerCount, 0),
        tables: tables.length,
        maxSeats: this.settings.maxSeats,
      };
    });
  }

  dispose() {
    for (const list of this.tables.values()) for (const t of list) t.dispose();
    for (const t of this.privateTables.values()) t.dispose();
    if (this._lobbyTimer) clearTimeout(this._lobbyTimer);
  }

  _newTable(level) {
    const n = this.counters.get(level.id) + 1;
    this.counters.set(level.id, n);
    const table = this._makeTable(level, `${level.id}-${n}`);
    this.tables.get(level.id).push(table);
    return table;
  }

  _makeTable(level, id) {
    return new Table({
      id,
      level,
      settings: this.settings,
      hooks: {
        wallet: this.hooks.wallet,
        notify: this.hooks.notify,
        log: this.hooks.log,
        onPlayerGone: (userId, t) => {
          if (this.userTable.get(userId) === t) this.userTable.delete(userId);
        },
        onChange: (t) => this._onTableChange(t),
        trackPayout: (userId, promise) => {
          const done = promise.catch(() => {}).then(() => {
            if (this.payouts.get(userId) === done) this.payouts.delete(userId);
          });
          this.payouts.set(userId, done);
        },
      },
    });
  }

  _onTableChange(table) {
    if (table.level.isPrivate) {
      // A private room closes when its last player leaves; its code stops working.
      if (table.isEmpty && !table.hand && this.privateTables.get(table.level.code) === table) {
        table.dispose();
        this.privateTables.delete(table.level.code);
      }
      return; // private rooms are not part of the lobby list
    }
    // Close extra tables once they are empty; always keep one per level.
    const list = this.tables.get(table.level.id);
    if (table.isEmpty && list.length > 1 && !table.hand) {
      table.dispose();
      list.splice(list.indexOf(table), 1);
    }
    // Batch lobby updates so a busy server doesn't flood the lobby.
    if (!this._lobbyTimer) {
      this._lobbyTimer = setTimeout(() => {
        this._lobbyTimer = null;
        this.hooks.onLobbyChange(this.rooms());
      }, 250);
    }
  }
}

module.exports = { TableManager };
