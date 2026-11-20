// Quick chat at the table (emojis and short messages from the server's list) and the admin page's API.
const http = require('http');
const express = require('express');
const User = require('../../src/db/User');
const { adminRouter } = require('../../src/chat/routes');
const { start, api, connect, emit, until, wait } = require('./setup');

const ADMIN_PASSWORD = 'test-admin-pass';
// 8x8 animated GIF (3 frames) and a 4x4 PNG
const GIF = 'R0lGODlhCAAIAIEAAAAAAMgeHgAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQJCgAAACwAAAAACAAIAAAIGAABCBxIsCCAAAgDEEyocCDDhQkNSpQYEAAh+QQJCgAAACwCAAIABAAEAIEAAADIHh4AAAAAAAAICQADCBxIsGCAgAAh+QQJCgAAACwEAAIABAAEAIEAAADIHh4AAAAAAAAICQADCBxIsGCAgAA7';
const PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAFUlEQVR4nGOUqzjxnwEJMDGgAcICAJqmAmUTgLWtAAAAAElFTkSuQmCC';

let srv;
beforeAll(async () => { srv = await start({ adminPassword: ADMIN_PASSWORD }); });
afterAll(async () => { await srv.close(); });

function getRaw(path) {
  return new Promise((resolve, reject) => {
    http.get(srv.url + path, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, type: res.headers['content-type'], cache: res.headers['cache-control'], body: Buffer.concat(chunks) }));
    }).on('error', reject);
  });
}

async function player(name) {
  const { token } = (await api(srv, 'POST', '/auth/register', { username: name, password: 'secret1' })).body;
  await User.updateOne({ username: name }, { balance: 5000 });
  const p = { name, token, state: null, chats: [], updates: [] };
  p.socket = await connect(srv, token, (s) => {
    s.on('table:state', (st) => { p.state = st; });
    s.on('table:event', (e) => { p.state = e.state; });
    s.on('table:chat', (m) => p.chats.push(m));
    s.on('chat:updated', (m) => p.updates.push(m));
  });
  return p;
}

async function admin() {
  const r = await api(srv, 'POST', '/admin/api/login', { password: ADMIN_PASSWORD });
  expect(r.status).toBe(200);
  return r.body.token;
}

test('the app gets the switched-on list, with pictures for emojis', async () => {
  const r = await api(srv, 'GET', '/chat');
  expect(r.status).toBe(200);
  expect(r.body.cooldownMs).toBe(3000);
  expect(r.body.version).toBeGreaterThanOrEqual(1);
  const texts = r.body.items.filter((i) => i.kind === 'text');
  expect(texts.map((t) => t.text)).toContain('Good luck, commander!');
  expect(texts).toHaveLength(16);
  for (const e of r.body.items.filter((i) => i.kind === 'emoji')) {
    expect(e.image).toMatch(/^\/chat\/img\/[0-9a-f]{24}\.png\?r=\d+$/);
    expect(e.frames).toBe(1);
  }
  const emoji = r.body.items.find((i) => i.kind === 'emoji');
  if (emoji) {
    const img = await getRaw(emoji.image);
    expect(img.status).toBe(200);
    expect(img.type).toMatch(/image\/png/);
    expect(img.cache).toMatch(/immutable/);
    expect(img.body.slice(1, 4).toString()).toBe('PNG');
  }
  expect((await getRaw('/chat/img/000000000000000000000000.png')).status).toBe(404);
  expect((await getRaw('/chat/img/nonsense.png')).status).toBe(404);
});

test('a seated player sends a message; everyone at the table sees it over the sender', async () => {
  const a = await player('chat_a');
  const b = await player('chat_b');
  const outsider = await player('chat_out');
  const { items } = (await api(srv, 'GET', '/chat')).body;
  const hello = items.find((i) => i.kind === 'text');

  // Not seated yet.
  expect(await emit(a.socket, 'table:chat', { id: hello.id })).toEqual({ ok: false, error: 'NOT_SEATED' });

  for (const p of [a, b]) expect((await emit(p.socket, 'room:join', { level: 'recruit' })).ok).toBe(true);
  await until(() => a.state && b.state && a.state.yourSeat >= 0 && b.state.yourSeat >= 0);

  expect(await emit(a.socket, 'table:chat', { id: 'nope' })).toEqual({ ok: false, error: 'UNKNOWN_ITEM' });
  expect(await emit(a.socket, 'table:chat', { id: hello.id })).toEqual({ ok: true });
  await until(() => a.chats.length === 1 && b.chats.length === 1);
  expect(b.chats[0]).toEqual({ seat: a.state.yourSeat, id: hello.id });
  expect(a.chats[0]).toEqual(b.chats[0]); // the sender sees their own bubble too
  await wait(50);
  expect(outsider.chats).toHaveLength(0); // other tables and the lobby don't

  // One message per cooldown.
  expect(await emit(a.socket, 'table:chat', { id: hello.id })).toEqual({ ok: false, error: 'COOLDOWN' });
  expect((await emit(b.socket, 'table:chat', { id: hello.id })).ok).toBe(true); // per player

  for (const p of [a, b]) { await emit(p.socket, 'table:leave'); p.socket.close(); }
  outsider.socket.close();
});

test('admin: password, editing, ordering, uploads, limits, and the live push to apps', async () => {
  const watcher = await player('chat_watch');
  expect((await api(srv, 'POST', '/admin/api/login', { password: 'wrong' })).status).toBe(401);
  expect((await api(srv, 'GET', '/admin/api/chat')).status).toBe(401);
  expect((await api(srv, 'GET', '/admin/api/chat', null, watcher.token)).status).toBe(401); // a player token is not an admin token
  const token = await admin();

  // An admin token is not a player login either.
  expect((await api(srv, 'GET', '/me', null, token)).status).toBe(401);
  await expect(connect(srv, token)).rejects.toThrow('UNAUTHORIZED');

  let list = (await api(srv, 'GET', '/admin/api/chat', null, token)).body;
  expect(list.maxEnabled).toEqual({ emoji: 30, text: 20 });
  const v0 = list.version;

  // Add, edit, switch off: each change bumps the version and tells connected apps.
  const added = (await api(srv, 'POST', '/admin/api/chat/text', { text: '  Fortune   favours the bold  ' }, token)).body;
  expect(added.text).toBe('Fortune favours the bold');
  await until(() => watcher.updates.length >= 1);
  expect(watcher.updates[watcher.updates.length - 1].version).toBeGreaterThan(v0);
  expect((await api(srv, 'POST', '/admin/api/chat/text', { text: 'x'.repeat(41) }, token)).body.error).toBe('BAD_TEXT');
  expect((await api(srv, 'PATCH', `/admin/api/chat/${added.id}`, { text: 'Fortune favours the brave' }, token)).body.text).toBe('Fortune favours the brave');
  await api(srv, 'PATCH', `/admin/api/chat/${added.id}`, { enabled: false }, token);
  let app = (await api(srv, 'GET', '/chat')).body;
  expect(app.items.find((i) => i.id === added.id)).toBeUndefined();
  expect(app.version).toBeGreaterThan(v0);

  // Order: move the new message to the top.
  list = (await api(srv, 'GET', '/admin/api/chat', null, token)).body;
  const textIds = list.items.filter((i) => i.kind === 'text').map((i) => i.id);
  await api(srv, 'PATCH', `/admin/api/chat/${added.id}`, { enabled: true }, token);
  await api(srv, 'PUT', '/admin/api/chat/order', { ids: [added.id, ...textIds.filter((id) => id !== added.id)] }, token);
  app = (await api(srv, 'GET', '/chat')).body;
  expect(app.items.filter((i) => i.kind === 'text')[0].id).toBe(added.id);

  // At most 20 messages switched on.
  for (let i = 0; i < 3; i++) expect((await api(srv, 'POST', '/admin/api/chat/text', { text: `Extra ${i}` }, token)).status).toBe(200);
  const full = await api(srv, 'POST', '/admin/api/chat/text', { text: 'One too many' }, token);
  expect(full.status).toBe(400);
  expect(full.body.error).toBe('TOO_MANY_ENABLED');

  // Uploads: a PNG becomes a 256 px square, an animated GIF a strip of 128 px frames.
  const png = (await api(srv, 'POST', '/admin/api/chat/emoji', { label: 'Blue', data: `data:image/png;base64,${PNG}` }, token)).body;
  expect(png).toMatchObject({ kind: 'emoji', label: 'Blue', frames: 1 });
  const gif = (await api(srv, 'POST', '/admin/api/chat/emoji', { label: 'Runner', data: GIF }, token)).body;
  expect(gif).toMatchObject({ kind: 'emoji', frames: 3, fps: 10 });
  const strip = await getRaw(gif.image);
  expect(strip.status).toBe(200);
  expect(strip.body.readUInt32BE(16)).toBe(3 * 128); // PNG width
  expect(strip.body.readUInt32BE(20)).toBe(128);     // PNG height
  expect((await api(srv, 'POST', '/admin/api/chat/emoji', { label: 'Bad', data: Buffer.from('not an image').toString('base64') }, token)).body.error).toBe('BAD_IMAGE');

  // Players can send uploaded emojis, and only switched-on ones.
  const a = await player('chat_up');
  expect((await emit(a.socket, 'room:join', { level: 'soldier' })).ok).toBe(true);
  await until(() => a.state && a.state.yourSeat >= 0);
  expect((await emit(a.socket, 'table:chat', { id: gif.id })).ok).toBe(true);
  await api(srv, 'PATCH', `/admin/api/chat/${png.id}`, { enabled: false }, token);
  await wait(3100);
  expect(await emit(a.socket, 'table:chat', { id: png.id })).toEqual({ ok: false, error: 'UNKNOWN_ITEM' });

  // Delete, and the cooldown setting.
  expect((await api(srv, 'DELETE', `/admin/api/chat/${png.id}`, null, token)).status).toBe(200);
  expect((await api(srv, 'DELETE', `/admin/api/chat/${png.id}`, null, token)).status).toBe(404);
  expect((await getRaw(png.image)).status).toBe(404);
  expect((await api(srv, 'PUT', '/admin/api/settings', { cooldownMs: 100 }, token)).body.error).toBe('BAD_COOLDOWN');
  await api(srv, 'PUT', '/admin/api/settings', { cooldownMs: 1500 }, token);
  expect((await api(srv, 'GET', '/chat')).body.cooldownMs).toBe(1500);

  // Five wrong passwords lock the address out for a minute.
  for (let i = 0; i < 5; i++) await api(srv, 'POST', '/admin/api/login', { password: 'nope' });
  expect((await api(srv, 'POST', '/admin/api/login', { password: ADMIN_PASSWORD })).status).toBe(429);

  await emit(a.socket, 'table:leave');
  a.socket.close();
  watcher.socket.close();
});

test('without ADMIN_PASSWORD the admin API is switched off', async () => {
  const app = express();
  app.use('/admin/api', adminRouter(null, { password: '', secret: 'x' }));
  const server = await new Promise((resolve) => { const s = app.listen(0, () => resolve(s)); });
  const res = await new Promise((resolve) => {
    const req = http.request(`http://localhost:${server.address().port}/admin/api/login`, { method: 'POST', headers: { 'content-type': 'application/json' } }, (r) => {
      let buf = ''; r.on('data', (c) => { buf += c; }); r.on('end', () => resolve({ status: r.statusCode, body: JSON.parse(buf) }));
    });
    req.end(JSON.stringify({ password: '' }));
  });
  await new Promise((resolve) => server.close(resolve));
  expect(res.status).toBe(404);
  expect(res.body.error).toBe('ADMIN_DISABLED');
});
