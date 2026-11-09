// Rooms (one per level) and the tables inside them. A full table opens another one at the same level.
const { Table } = require('./table');
const { GameError } = require('./holdemEngine');

class TableManager {
  // hooks: { wallet, notify(userId, event, payload), onLobbyChange(rooms), log }
  constructor({ levels, settings, hooks }) {
    this.levels = levels;
    this.settings = settings;
    this.hooks = hooks;
    this.tables = new Map(levels.map((l) => [l.id, []])); // levelId -> [Table]
    this.counters = new Map(levels.map((l) => [l.id, 0]));
    this.userTable = new Map(); // userId -> Table
    this.joining = new Set();
    this._lobbyTimer = null;
    for (const l of levels) this._newTable(l);
  }

  tableOf(userId) {
    return this.userTable.get(userId) || null;
  }

  async join(user, levelId) {
    const level = this.levels.find((l) => l.id === levelId);
    if (!level) throw new GameError('UNKNOWN_LEVEL');
    const current = this.userTable.get(user.userId);
    // Left while all-in: still in that hand until it ends, then paid out.
    if (current && current.seatOf(user.userId) === -1) throw new GameError('LEAVING_TABLE');
    if (current || this.joining.has(user.userId)) throw new GameError('ALREADY_SEATED');
    this.joining.add(user.userId);
    try {
      const session = await this.hooks.wallet.buyIn(user.userId, level.buyIn);
      const tables = this.tables.get(level.id);
      const table = tables.find((t) => t.hasFreeSeat()) || this._newTable(level);
      this.userTable.set(user.userId, table);
      table.seatPlayer(user, level.buyIn, session);
      return table;
    } finally {
      this.joining.delete(user.userId);
    }
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
    if (this._lobbyTimer) clearTimeout(this._lobbyTimer);
  }

  _newTable(level) {
    const n = this.counters.get(level.id) + 1;
    this.counters.set(level.id, n);
    const table = new Table({
      id: `${level.id}-${n}`,
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
      },
    });
    this.tables.get(level.id).push(table);
    return table;
  }

  _onTableChange(table) {
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
