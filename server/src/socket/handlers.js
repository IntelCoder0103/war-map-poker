const { verify } = require('../auth/token');
const User = require('../db/User');
const wallet = require('../db/wallet');

function attachSockets(io, manager, log) {
  const sockets = new Map(); // userId -> socket (one live socket per user)
  const recentInvites = new Map(); // "from:to" -> time of the last invite (simple flood guard)
  setInterval(() => {
    const old = Date.now() - 60000;
    for (const [k, t] of recentInvites) if (t < old) recentInvites.delete(k);
  }, 60000).unref();

  io.use(async (socket, next) => {
    const auth = verify(socket.handshake.auth && socket.handshake.auth.token);
    if (!auth) return next(new Error('UNAUTHORIZED'));
    try {
      if (!(await User.exists({ _id: auth.userId }))) return next(new Error('UNAUTHORIZED'));
    } catch (e) {
      return next(new Error('UNAUTHORIZED'));
    }
    socket.data.user = auth;
    next();
  });

  io.on('connection', (socket) => {
    const user = socket.data.user;
    const old = sockets.get(user.userId);
    sockets.set(user.userId, socket);
    if (old) {
      old.emit('session:replaced', {});
      old.disconnect(true);
    }

    const table = manager.tableOf(user.userId);
    if (table) {
      table.setConnected(user.userId, true);
      socket.emit('table:state', table.snapshotFor(user.userId));
    }

    // Wrap a handler so it always answers the ack with { ok, ... } or { ok: false, error }.
    const on = (event, fn) => socket.on(event, async (payload, ack) => {
      if (typeof payload === 'function') { ack = payload; payload = {}; }
      const reply = typeof ack === 'function' ? ack : () => {};
      try {
        const result = await fn(payload || {});
        reply({ ok: true, ...(result || {}) });
      } catch (e) {
        if (e.code) reply({ ok: false, error: e.code });
        else {
          log('socket handler error', event, e);
          reply({ ok: false, error: 'SERVER_ERROR' });
        }
      }
    });
    const seatedTable = () => {
      const t = manager.tableOf(user.userId);
      if (!t) throw Object.assign(new Error('NOT_SEATED'), { code: 'NOT_SEATED' });
      return t;
    };

    on('lobby:subscribe', () => {
      socket.join('lobby');
      return { rooms: manager.rooms(), privateRooms: manager.privateLimits() };
    });
    on('lobby:unsubscribe', () => {
      socket.leave('lobby');
    });
    on('room:join', async ({ level }) => {
      const t = await manager.join(user, level);
      // The connection may have dropped while the buy-in was being saved: then start the
      // disconnect grace timer now, or the seat would wait for a player who isn't there.
      if (!socket.connected || sockets.get(user.userId) !== socket) {
        if (!sockets.has(user.userId)) t.setConnected(user.userId, false);
      }
      // Send the snapshot right after the ack.
      setImmediate(() => socket.emit('table:state', t.snapshotFor(user.userId)));
      const u = await User.findById(user.userId);
      if (u) socket.emit('user:balance', { balance: u.balance, nextRefillAt: wallet.publicUser(u).nextRefillAt });
      return { tableId: t.id };
    });
    // ---- private rooms ----
    const sendTableState = (t) => {
      setImmediate(() => socket.emit('table:state', t.snapshotFor(user.userId)));
    };
    const sendBalance = async () => {
      const u = await User.findById(user.userId);
      if (u) socket.emit('user:balance', { balance: u.balance, nextRefillAt: wallet.publicUser(u).nextRefillAt });
    };
    on('room:createPrivate', async ({ buyIn, bigBlind }) => {
      const t = await manager.createPrivate(user, { buyIn, bigBlind });
      sendTableState(t);
      await sendBalance();
      return { tableId: t.id, code: t.level.code };
    });
    on('room:joinCode', async ({ code }) => {
      const t = await manager.joinByCode(user, code);
      sendTableState(t);
      await sendBalance();
      return { tableId: t.id, code: t.level.code };
    });
    on('room:invite', async ({ username }) => {
      const t = seatedTable();
      if (!t.level.isPrivate) throw Object.assign(new Error('NOT_PRIVATE'), { code: 'NOT_PRIVATE' });
      const name = String(username || '').trim().toLowerCase();
      const target = name ? await User.findOne({ usernameLower: name }) : null;
      if (!target) throw Object.assign(new Error('USER_NOT_FOUND'), { code: 'USER_NOT_FOUND' });
      const targetId = String(target._id);
      if (targetId === user.userId) throw Object.assign(new Error('CANNOT_INVITE_SELF'), { code: 'CANNOT_INVITE_SELF' });
      if (!sockets.has(targetId)) throw Object.assign(new Error('USER_OFFLINE'), { code: 'USER_OFFLINE' });
      // At most one invite to the same person every few seconds.
      const key = user.userId + ':' + targetId;
      const now = Date.now();
      if ((recentInvites.get(key) || 0) > now - 5000) throw Object.assign(new Error('TOO_MANY_INVITES'), { code: 'TOO_MANY_INVITES' });
      recentInvites.set(key, now);
      sockets.get(targetId).emit('room:invite', {
        from: user.username, code: t.level.code,
        buyIn: t.level.buyIn, smallBlind: t.level.smallBlind, bigBlind: t.level.bigBlind,
        players: t.playerCount, maxSeats: t.settings.maxSeats,
      });
      return { username: target.username };
    });

    on('table:ready', () => seatedTable().ready(user.userId));
    on('table:action', ({ type, amount }) => seatedTable().action(user.userId, type, amount));
    on('table:leave', async () => {
      const u = await seatedTable().leave(user.userId, 'left');
      return { balance: u ? u.balance : null };
    });
    on('table:show', () => seatedTable().showCards(user.userId));
    on('table:leaveAfterHand', async ({ on }) => {
      const r = await seatedTable().setLeaveAfterHand(user.userId, on !== false);
      return { leftNow: r.leftNow, balance: r.user ? r.user.balance : null };
    });
    on('table:sync', () => {
      socket.emit('table:state', seatedTable().snapshotFor(user.userId));
    });

    socket.on('disconnect', () => {
      if (sockets.get(user.userId) !== socket) return; // replaced by a newer connection
      sockets.delete(user.userId);
      const t = manager.tableOf(user.userId);
      if (t) t.setConnected(user.userId, false);
    });
  });

  return {
    notify(userId, event, payload) {
      const s = sockets.get(userId);
      if (s) s.emit(event, payload);
    },
    lobby(rooms) {
      io.to('lobby').emit('lobby:rooms', { rooms });
    },
  };
}

module.exports = { attachSockets };
