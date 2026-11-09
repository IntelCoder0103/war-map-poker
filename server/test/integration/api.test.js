const User = require('../../src/db/User');
const { start, api, connect, emit } = require('./setup');

let srv;
beforeAll(async () => { srv = await start(); });
afterAll(async () => { await srv.close(); });

test('register, login, me', async () => {
  const r = await api(srv, 'POST', '/auth/register', { username: 'Alice_1', password: 'secret1' });
  expect(r.status).toBe(200);
  expect(r.body.user).toMatchObject({ username: 'Alice_1', balance: 2000, nextRefillAt: null });

  expect((await api(srv, 'POST', '/auth/register', { username: 'alice_1', password: 'secret1' })).body.error).toBe('USERNAME_TAKEN');
  expect((await api(srv, 'POST', '/auth/register', { username: 'a', password: 'secret1' })).body.error).toBe('INVALID_USERNAME');
  expect((await api(srv, 'POST', '/auth/register', { username: 'bobby', password: '123' })).body.error).toBe('INVALID_PASSWORD');

  expect((await api(srv, 'POST', '/auth/login', { username: 'alice_1', password: 'nope' })).status).toBe(401);
  const login = await api(srv, 'POST', '/auth/login', { username: 'ALICE_1', password: 'secret1' });
  expect(login.status).toBe(200);

  const me = await api(srv, 'GET', '/me', null, login.body.token);
  expect(me.body.user.username).toBe('Alice_1');
  expect((await api(srv, 'GET', '/me', null, 'garbage')).status).toBe(401);
});

test('refill only when low, then once per day', async () => {
  const r = await api(srv, 'POST', '/auth/register', { username: 'poor', password: 'secret1' });
  const { token } = r.body;
  expect((await api(srv, 'POST', '/me/refill', null, token)).body.error).toBe('REFILL_NOT_AVAILABLE');
  await User.updateOne({ username: 'poor' }, { balance: 50 });
  const ok = await api(srv, 'POST', '/me/refill', null, token);
  expect(ok.body.user.balance).toBe(1000);
  expect(ok.body.user.nextRefillAt).toBeGreaterThan(Date.now());
  await User.updateOne({ username: 'poor' }, { balance: 0 });
  expect((await api(srv, 'POST', '/me/refill', null, token)).body.error).toBe('REFILL_NOT_AVAILABLE');
});

test('rooms list and joining rules', async () => {
  const rooms = await api(srv, 'GET', '/rooms');
  expect(rooms.body.rooms.map((r) => r.id)).toEqual(['recruit', 'soldier', 'captain', 'general', 'king']);

  const { token } = (await api(srv, 'POST', '/auth/register', { username: 'joiner', password: 'secret1' })).body;
  const s = await connect(srv, token);
  expect(await emit(s, 'room:join', { level: 'nope' })).toEqual({ ok: false, error: 'UNKNOWN_LEVEL' });
  expect(await emit(s, 'room:join', { level: 'captain' })).toEqual({ ok: false, error: 'INSUFFICIENT_BALANCE' });
  const stateP = new Promise((r) => s.once('table:state', r));
  expect(await emit(s, 'room:join', { level: 'recruit' })).toEqual({ ok: true, tableId: 'recruit-1' });
  const state = await stateP;
  expect(state).toMatchObject({ phase: 'waiting', yourSeat: 0, buyIn: 1000 });
  expect(state.seats[0]).toMatchObject({ username: 'joiner', stack: 1000, ready: false });
  expect(await emit(s, 'room:join', { level: 'recruit' })).toEqual({ ok: false, error: 'ALREADY_SEATED' });
  expect((await User.findOne({ username: 'joiner' })).balance).toBe(1000);

  const left = await emit(s, 'table:leave');
  expect(left).toEqual({ ok: true, balance: 2000 });
  s.close();
});

test('socket rejects bad tokens', async () => {
  await expect(connect(srv, 'bad')).rejects.toThrow('UNAUTHORIZED');
});

test('a winner by fold may show their cards once, before the next hand', async () => {
  // a longer pause between hands, so there is time to show
  await srv.close();
  srv = await start({ nextHandDelayMs: 3000, turnTimeMs: 30000 });
  const mk = async (name) => {
    const { token } = (await api(srv, 'POST', '/auth/register', { username: name, password: 'secret1' })).body;
    const p = { events: [], state: null };
    p.socket = await connect(srv, token, (s) => {
      s.on('table:state', (st) => { p.state = st; });
      s.on('table:event', (ev) => { p.state = ev.state; p.events.push(ev); });
    });
    return p;
  };
  const a = await mk('shower_a');
  const b = await mk('shower_b');
  expect((await emit(a.socket, 'room:join', { level: 'recruit' })).ok).toBe(true);
  expect((await emit(b.socket, 'room:join', { level: 'recruit' })).ok).toBe(true);
  await emit(a.socket, 'table:ready');
  await emit(b.socket, 'table:ready');
  const until = async (fn) => { for (let i = 0; i < 200 && !fn(); i++) await new Promise((r) => setTimeout(r, 10)); expect(fn()).toBeTruthy(); };
  await until(() => a.state && a.state.legal && b.state && b.state.yourCards.length === 2);

  // whoever is to act folds; the other wins without a showdown
  const [actor, winner] = a.state.turnSeat === a.state.yourSeat ? [a, b] : [b, a];
  // totalBet tracks the whole hand: the blinds are already in
  expect(winner.state.seats.filter(Boolean).map((s) => s.totalBet).sort((x, y) => x - y)).toEqual([10, 20]);
  const myCards = winner.state.yourCards;
  expect((await emit(actor.socket, 'table:action', { type: 'fold' })).ok).toBe(true);
  await until(() => winner.events.some((e) => e.type === 'handEnded'));
  expect(winner.state.canShowCards).toBe(true);
  expect(actor.state.canShowCards).toBe(false);
  expect((await emit(actor.socket, 'table:show')).error).toBe('CANNOT_SHOW');

  expect((await emit(winner.socket, 'table:show')).ok).toBe(true);
  await until(() => actor.events.some((e) => e.type === 'cardsShown'));
  const shown = actor.events.find((e) => e.type === 'cardsShown');
  expect(shown.data.cards).toEqual(myCards);
  expect(shown.state.seats[shown.data.seat].cards).toEqual(myCards);
  expect((await emit(winner.socket, 'table:show')).error).toBe('CANNOT_SHOW');

  for (const p of [a, b]) { await emit(p.socket, 'table:leave'); p.socket.close(); }
});
