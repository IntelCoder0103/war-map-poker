// 8+ bots play real hands over Socket.IO against a real MongoDB.
const User = require('../../src/db/User');
const { start, api, connect, emit, wait, until } = require('./setup');

let srv;
beforeAll(async () => { srv = await start(); });
afterAll(async () => { await srv.close(); });

const START_BALANCE = 50000;

async function makeBot(name) {
  const { token } = (await api(srv, 'POST', '/auth/register', { username: name, password: 'secret1' })).body;
  await User.updateOne({ username: name }, { balance: START_BALANCE });
  const userId = (await User.findOne({ username: name }))._id;
  const bot = { name, token, userId, socket: null, state: null, errors: [], log: [], handsEnded: 0, removed: [], acting: false, stop: true }; // idle until the test starts play
  await attach(bot);
  return bot;
}

async function attach(bot) {
  await connect(srv, bot.token, (sock) => { bot.socket = sock; listen(bot, sock); });
}

function listen(bot, sock) {
  sock.on('table:state', (st) => onState(bot, st, 'state'));
  sock.on('table:event', (ev) => {
    bot.log.push({ type: ev.type, hand: ev.state.handNumber, table: ev.state.tableId, phase: ev.state.phase, json: JSON.stringify(ev), yourCards: ev.state.yourCards });
    if (ev.type === 'handEnded') bot.handsEnded++;
    onState(bot, ev.state, ev.type);
  });
  sock.on('table:removed', ({ reason }) => {
    bot.removed.push(reason);
    bot.state = null;
    if (reason === 'broke' && !bot.stop) setTimeout(() => join(bot), 10);
  });
}

async function join(bot, level = 'recruit') {
  const r = await emit(bot.socket, 'room:join', { level });
  if (!r.ok) bot.errors.push(`join:${r.error}`);
  return r;
}

function onState(bot, st, why) {
  bot.state = st;
  if (bot.stop) return;
  const me = st.yourSeat >= 0 && st.seats[st.yourSeat];
  if (st.phase === 'waiting' && me && !me.ready) emit(bot.socket, 'table:ready');
  if (st.legal && st.turnSeat === st.yourSeat && !bot.acting) {
    bot.acting = true;
    setImmediate(async () => {
      const l = st.legal;
      const opts = ['fold'];
      if (l.canCheck) opts.push('check', 'check', 'check');
      if (l.canCall) opts.push('call', 'call', 'call');
      if (l.canRaise) opts.push('raise');
      if (l.canAllIn && Math.random() < 0.3) opts.push('allin');
      const type = opts[Math.floor(Math.random() * opts.length)];
      const amount = type === 'raise' ? l.minRaiseTo + Math.floor(Math.random() * Math.min(200, l.maxRaiseTo - l.minRaiseTo + 1)) : undefined;
      const r = await emit(bot.socket, 'table:action', { type, amount });
      // NOT_YOUR_TURN can legitimately race with the turn timer; anything else is a bug.
      if (!r.ok && r.error !== 'NOT_YOUR_TURN') bot.errors.push(`action:${type}:${r.error}`);
      bot.acting = false;
    });
  }
}

async function totalSoldiers() {
  const users = await User.find({ username: /^bot/ });
  return users.reduce((t, u) => t + u.balance + u.tableStack, 0);
}

test('bots play many hands: soldiers are conserved and hole cards stay private', async () => {
  const bots = [];
  for (let i = 0; i < 9; i++) bots.push(await makeBot(`bot${i}`));
  for (const b of bots) expect((await join(b)).ok).toBe(true);

  // 9 players -> table 1 is full (8), the 9th opens table 2.
  const tableIds = new Set(bots.map((b) => b.state && b.state.tableId));
  expect([...tableIds].sort()).toEqual(['recruit-1', 'recruit-2']);
  const rooms = (await api(srv, 'GET', '/rooms')).body.rooms;
  expect(rooms[0]).toMatchObject({ players: 9, tables: 2 });

  // The lone player at table 2 can't play alone; send them back to the lobby.
  const loner = bots.find((b) => b.state.tableId === 'recruit-2');
  expect((await emit(loner.socket, 'table:leave')).ok).toBe(true);

  const players = bots.filter((b) => b !== loner);
  // Start playing only now, so nobody busts and re-joins while the seats are being counted.
  for (const b of players) {
    b.stop = false;
    if (b.state) onState(b, b.state, 'start');
  }
  await until(() => players.every((b) => b.handsEnded >= 5), 60000);

  // Timeout: a bot disappears for longer than the grace period and gets removed.
  const gone = players[2];
  gone.stop = true;
  gone.socket.disconnect();

  await until(() => players.filter((b) => b !== gone).every((b) => b.handsEnded >= 40), 120000);

  // Everyone leaves; all soldiers must be back in balances.
  for (const b of bots) b.stop = true;
  await wait(50);
  for (const b of bots) {
    if (b.socket.connected && b.state && b.state.yourSeat >= 0) await emit(b.socket, 'table:leave');
  }
  await wait(200);
  const goneUser = await User.findOne({ username: gone.name });
  expect(goneUser.atTable).toBe(false);
  expect(await User.countDocuments({ atTable: true })).toBe(0);
  expect(await totalSoldiers()).toBe(START_BALANCE * bots.length);

  // No bot ever saw another player's hole cards before showdown.
  const cardsByHand = new Map(); // table#hand -> Map(botName -> cards)
  for (const b of bots) {
    for (const e of b.log) {
      if (e.yourCards && e.yourCards.length) {
        const k = `${e.table}#${e.hand}`;
        if (!cardsByHand.has(k)) cardsByHand.set(k, new Map());
        cardsByHand.get(k).set(b.name, e.yourCards);
      }
    }
  }
  let checked = 0;
  for (const b of bots) {
    for (const e of b.log) {
      if (e.phase === 'showdown' || e.type === 'showdown') continue;
      const hand = cardsByHand.get(`${e.table}#${e.hand}`);
      if (!hand) continue;
      for (const [owner, cards] of hand) {
        if (owner === b.name) continue;
        for (const c of cards) {
          if (e.json.includes(`"${c}"`)) {
            const ownerEntries = bots.find((x) => x.name === owner).log.filter((x) => x.table === e.table && x.hand === e.hand && x.yourCards.length).slice(0, 2).map((x) => x.type + ' ' + x.json);
            throw new Error(`${b.name} saw ${owner}'s card ${c} during ${e.type}\nVIEWER: ${e.json}\nOWNER: ${ownerEntries.join('\n')}`);
          }
        }
      }
      checked++;
    }
  }
  expect(checked).toBeGreaterThan(1000);
  expect(cardsByHand.size).toBeGreaterThan(40);

  for (const b of bots) expect(b.errors).toEqual([]);
  for (const b of bots) b.socket.close();
}, 240000);

test('reconnecting mid-hand restores the seat and the same cards', async () => {
  await srv.close();
  srv = await start({ turnTimeMs: 60000 });
  const a = await makeBot('bot_ra');
  const b = await makeBot('bot_rb');
  a.stop = true; b.stop = true; // no auto-play
  await join(a); await join(b);
  await emit(a.socket, 'table:ready');
  await emit(b.socket, 'table:ready');
  await until(() => a.state && a.state.phase === 'preflop' && a.state.yourCards.length === 2);
  const cards = a.state.yourCards;

  a.socket.disconnect();
  await until(() => b.state.seats[a.state.yourSeat].connected === false);
  a.state = null;
  await attach(a);
  await until(() => a.state && a.state.yourSeat >= 0);
  expect(a.state.phase).toBe('preflop');
  expect(a.state.yourCards).toEqual(cards);
  await until(() => b.state.seats[a.state.yourSeat].connected === true);

  // Logging in again elsewhere replaces the old connection.
  const replaced = new Promise((r) => a.socket.once('session:replaced', r));
  const second = await connect(srv, a.token);
  await replaced;
  second.close();
  for (const x of [a, b]) x.socket.close();
});
