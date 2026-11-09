// Table rules around leaving, idling and getting ready.
const User = require('../../src/db/User');
const wallet = require('../../src/db/wallet');
const { start, api, connect, emit, until, wait } = require('./setup');

let srv;
afterEach(async () => { if (srv) await srv.close(); srv = null; });

async function player(name, balance = 5000) {
  const { token } = (await api(srv, 'POST', '/auth/register', { username: name, password: 'secret1' })).body;
  await User.updateOne({ username: name }, { balance });
  const p = { name, token, state: null, events: [], removed: [] };
  p.socket = await connect(srv, token, (s) => {
    s.on('table:state', (st) => { p.state = st; });
    s.on('table:event', (e) => { p.state = e.state; p.events.push(e); });
    s.on('table:removed', ({ reason }) => p.removed.push(reason));
  });
  return p;
}

const total = async (names) => (await User.find({ username: { $in: names } }))
  .reduce((t, u) => t + u.balance + u.tableStack, 0);

test('an all-in player who leaves keeps their share of the pot', async () => {
  srv = await start({ turnTimeMs: 30000, nextHandDelayMs: 5000 });
  const a = await player('allin_a');
  const b = await player('allin_b');
  for (const p of [a, b]) expect((await emit(p.socket, 'room:join', { level: 'recruit' })).ok).toBe(true);
  for (const p of [a, b]) await emit(p.socket, 'table:ready');
  await until(() => (a.state && a.state.legal) || (b.state && b.state.legal));

  const [first, second] = a.state.legal ? [a, b] : [b, a];
  expect((await emit(first.socket, 'table:action', { type: 'allin' })).ok).toBe(true);
  // Leaving while all-in: the payout waits for the hand to end.
  expect(await emit(first.socket, 'table:leave')).toEqual({ ok: true, balance: null });
  expect(first.removed).toEqual(['left']);
  // Still in the hand: the other player sees the seat (offline) and can call.
  await until(() => second.events.some((e) => e.type === 'playerLeft') && second.state.legal);
  expect(second.state.seats[first.state.yourSeat]).toMatchObject({ connected: false });
  expect((await emit(second.socket, 'table:action', { type: 'call' })).ok).toBe(true);

  await until(() => second.events.some((e) => e.type === 'handEnded'));
  const showdown = second.events.find((e) => e.type === 'showdown');
  expect(showdown.data.reveals.map((r) => r.seat).sort()).toEqual([0, 1]); // the leaver was not folded
  await wait(200);
  const left = await User.findOne({ username: first.name });
  expect(left.atTable).toBe(false);
  expect(await total([a.name, b.name])).toBe(10000);
  // Can join again once it's settled.
  expect((await emit(first.socket, 'room:join', { level: 'recruit' })).ok).toBe(true);
  for (const p of [a, b]) p.socket.close();
});

test('joining again while the all-in hand is still running says so', async () => {
  srv = await start({ turnTimeMs: 30000 });
  const a = await player('wait_a');
  const b = await player('wait_b');
  for (const p of [a, b]) await emit(p.socket, 'room:join', { level: 'recruit' });
  for (const p of [a, b]) await emit(p.socket, 'table:ready');
  await until(() => (a.state && a.state.legal) || (b.state && b.state.legal));
  const [first] = a.state.legal ? [a, b] : [b, a];
  await emit(first.socket, 'table:action', { type: 'allin' });
  await emit(first.socket, 'table:leave');
  expect(await emit(first.socket, 'room:join', { level: 'recruit' })).toEqual({ ok: false, error: 'LEAVING_TABLE' });
  for (const p of [a, b]) p.socket.close();
});

test('a player who misses 3 turns in a row goes back to the lobby', async () => {
  srv = await start({ turnTimeMs: 150, nextHandDelayMs: 30 });
  const idle = await player('idle_one');
  const busy = await player('busy_one');
  for (const p of [idle, busy]) await emit(p.socket, 'room:join', { level: 'recruit' });
  // The busy player checks/calls whenever it's their turn; the idle one never acts.
  busy.socket.on('table:event', (e) => {
    const l = e.state.legal;
    if (l && e.state.turnSeat === e.state.yourSeat) emit(busy.socket, 'table:action', { type: l.canCheck ? 'check' : 'call' });
  });
  for (const p of [idle, busy]) await emit(p.socket, 'table:ready');
  await until(() => idle.removed.length > 0, 15000);
  expect(idle.removed).toEqual(['idle']);
  await wait(100);
  expect(await total([idle.name, busy.name])).toBe(10000);
  expect(await User.countDocuments({ username: idle.name, atTable: true })).toBe(0);
  for (const p of [idle, busy]) p.socket.close();
});

test('once two are ready, a player who never presses Ready is sent back after the countdown', async () => {
  srv = await start({ readyTimeoutMs: 300 });
  const a = await player('ready_a');
  const b = await player('ready_b');
  const c = await player('never_ready');
  for (const p of [a, b, c]) await emit(p.socket, 'room:join', { level: 'recruit' });
  await emit(a.socket, 'table:ready');
  await emit(b.socket, 'table:ready');
  await until(() => a.state.readyDeadline > 0);
  await until(() => c.removed.length > 0, 5000);
  expect(c.removed).toEqual(['idle']);
  // The two ready players start without them.
  await until(() => a.events.some((e) => e.type === 'handStarted'), 5000);
  expect((await User.findOne({ username: c.name })).balance).toBe(5000);
  for (const p of [a, b, c]) p.socket.close();
});

test('a late save from an earlier sitting cannot touch a newer one', async () => {
  srv = await start();
  await api(srv, 'POST', '/auth/register', { username: 'sitter', password: 'secret1' });
  const u = await User.findOne({ username: 'sitter' });
  const first = await wallet.buyIn(u._id, 1000);
  await wallet.cashOut(u._id, 1500, first);
  const second = await wallet.buyIn(u._id, 1000);
  await wallet.saveStacks([{ userId: u._id, session: first, stack: 99999 }]); // stale
  expect(await wallet.cashOut(u._id, 10, first)).toBe(null); // stale cash-out does nothing
  const now = await User.findOne({ username: 'sitter' });
  expect(now).toMatchObject({ atTable: true, tableStack: 1000, balance: 1500 });
  await wallet.cashOut(u._id, 1000, second);
  expect((await User.findOne({ username: 'sitter' })).balance).toBe(2500);
});
