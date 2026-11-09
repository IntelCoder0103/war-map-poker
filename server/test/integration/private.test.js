// Private rooms: custom stakes, join by code, invites by username.
const User = require('../../src/db/User');
const { start, api, connect, emit, until, wait } = require('./setup');

let srv;
beforeAll(async () => { srv = await start(); });
afterAll(async () => { await srv.close(); });

async function player(name, balance = 5000) {
  const { token } = (await api(srv, 'POST', '/auth/register', { username: name, password: 'secret1' })).body;
  await User.updateOne({ username: name }, { balance });
  const p = { name, state: null, invites: [] };
  p.socket = await connect(srv, token, (s) => {
    s.on('table:state', (st) => { p.state = st; });
    s.on('table:event', (e) => { p.state = e.state; });
    s.on('room:invite', (inv) => p.invites.push(inv));
  });
  return p;
}

test('create with custom stakes, join by code, and the room is not in the public list', async () => {
  const host = await player('host_a');
  const guest = await player('guest_a');

  // Stakes are checked: whole numbers, within limits, at least 10 big blinds deep.
  expect(await emit(host.socket, 'room:createPrivate', { buyIn: 50, bigBlind: 2 })).toEqual({ ok: false, error: 'BAD_STAKES' });
  expect(await emit(host.socket, 'room:createPrivate', { buyIn: 1000, bigBlind: 200 })).toEqual({ ok: false, error: 'BAD_STAKES' });
  expect(await emit(host.socket, 'room:createPrivate', { buyIn: 9000, bigBlind: 50 })).toEqual({ ok: false, error: 'INSUFFICIENT_BALANCE' });

  const created = await emit(host.socket, 'room:createPrivate', { buyIn: 3000, bigBlind: 50 });
  expect(created.ok).toBe(true);
  expect(created.code).toMatch(/^[A-HJ-NP-Z2-9]{6}$/);
  await until(() => host.state && host.state.yourSeat >= 0);
  expect(host.state).toMatchObject({ privateCode: created.code, buyIn: 3000, smallBlind: 25, bigBlind: 50 });
  expect((await User.findOne({ username: 'host_a' })).balance).toBe(2000);

  // Not listed with the public rooms.
  const list = (await api(srv, 'GET', '/rooms')).body;
  expect(list.rooms.every((r) => r.players === 0)).toBe(true);
  expect(list.privateRooms).toEqual({ minBuyIn: 100, maxBuyIn: 1000000, minStackBigBlinds: 10 });

  expect(await emit(guest.socket, 'room:joinCode', { code: 'ZZZZZZ' })).toEqual({ ok: false, error: 'ROOM_NOT_FOUND' });
  const joined = await emit(guest.socket, 'room:joinCode', { code: created.code.toLowerCase() }); // case doesn't matter
  expect(joined).toMatchObject({ ok: true, code: created.code });
  await until(() => guest.state && guest.state.yourSeat >= 0);
  expect(guest.state.seats.filter(Boolean).map((s) => s.username).sort()).toEqual(['guest_a', 'host_a']);

  // Both leave: the room closes and its code stops working.
  for (const p of [host, guest]) await emit(p.socket, 'table:leave');
  await wait(50);
  const late = await player('late_a');
  expect(await emit(late.socket, 'room:joinCode', { code: created.code })).toEqual({ ok: false, error: 'ROOM_NOT_FOUND' });
  for (const p of [host, guest, late]) p.socket.close();
});

test('invite by username', async () => {
  const host = await player('host_b');
  const friend = await player('friend_b');
  const { token } = (await api(srv, 'POST', '/auth/register', { username: 'offline_b', password: 'secret1' })).body;
  expect(token).toBeTruthy();

  // Only from a private room.
  expect(await emit(host.socket, 'room:invite', { username: 'friend_b' })).toEqual({ ok: false, error: 'NOT_SEATED' });
  const { code } = await emit(host.socket, 'room:createPrivate', { buyIn: 1000, bigBlind: 20 });

  expect(await emit(host.socket, 'room:invite', { username: 'nobody_here' })).toEqual({ ok: false, error: 'USER_NOT_FOUND' });
  expect(await emit(host.socket, 'room:invite', { username: 'offline_b' })).toEqual({ ok: false, error: 'USER_OFFLINE' });
  expect(await emit(host.socket, 'room:invite', { username: 'HOST_B' })).toEqual({ ok: false, error: 'CANNOT_INVITE_SELF' });

  expect(await emit(host.socket, 'room:invite', { username: 'Friend_B' })).toEqual({ ok: true, username: 'friend_b' });
  await until(() => friend.invites.length === 1);
  expect(friend.invites[0]).toMatchObject({ from: 'host_b', code, buyIn: 1000, smallBlind: 10, bigBlind: 20, players: 1 });
  // Flood guard.
  expect(await emit(host.socket, 'room:invite', { username: 'friend_b' })).toEqual({ ok: false, error: 'TOO_MANY_INVITES' });

  // The friend accepts with the code from the invite.
  expect((await emit(friend.socket, 'room:joinCode', { code: friend.invites[0].code })).ok).toBe(true);
  for (const p of [host, friend]) { await emit(p.socket, 'table:leave'); p.socket.close(); }
});

test('a full private room says so', async () => {
  const players = [];
  for (let i = 0; i < 9; i++) players.push(await player(`full_${i}`));
  const { code } = await emit(players[0].socket, 'room:createPrivate', { buyIn: 1000, bigBlind: 20 });
  for (let i = 1; i < 8; i++) expect((await emit(players[i].socket, 'room:joinCode', { code })).ok).toBe(true);
  expect(await emit(players[8].socket, 'room:joinCode', { code })).toEqual({ ok: false, error: 'TABLE_FULL' });
  expect((await User.findOne({ username: 'full_8' })).balance).toBe(5000); // nothing taken
  for (const p of players) p.socket.close();
});
