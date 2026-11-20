// HTTP for chat:
//   GET  /chat                 the list the app shows (switched-on items)
//   GET  /chat/img/<id>.png    an emoji picture (a strip of frames when animated)
//   /admin/api/...             the admin page's API, behind ADMIN_PASSWORD
const crypto = require('crypto');
const express = require('express');
const jwt = require('jsonwebtoken');

function publicRouter(store) {
  const r = express.Router();
  r.get('/', async (req, res, next) => {
    try {
      const c = await store.catalog();
      res.set('Cache-Control', 'no-cache');
      res.json({ version: c.version, cooldownMs: c.cooldownMs, items: c.items });
    } catch (e) { next(e); }
  });
  r.get('/img/:file', async (req, res, next) => {
    try {
      const id = String(req.params.file).replace(/\.png$/, '');
      const img = await store.image(id);
      if (!img) return res.status(404).json({ error: 'NOT_FOUND' });
      // The URL carries ?r=<rev>, so a changed picture gets a new URL: safe to cache for long.
      res.set('Cache-Control', 'public, max-age=31536000, immutable');
      res.type('png').send(img.png);
    } catch (e) { next(e); }
  });
  return r;
}

function adminRouter(store, { password, secret, log = () => {} }) {
  const r = express.Router();
  r.use(express.json({ limit: '4mb' })); // uploads arrive as base64 inside JSON

  const send = (res, e) => {
    if (e && e.code) return res.status(e.status || 400).json({ error: e.code, message: e.message });
    log('admin error', e);
    return res.status(500).json({ error: 'SERVER_ERROR', message: 'Something went wrong.' });
  };
  const wrap = (fn) => async (req, res) => {
    try { res.json((await fn(req)) || { ok: true }); } catch (e) { send(res, e); }
  };

  // Without ADMIN_PASSWORD the admin page is switched off.
  r.use((req, res, next) => {
    if (!password) return res.status(404).json({ error: 'ADMIN_DISABLED', message: 'The admin page is off. Set ADMIN_PASSWORD on the server and restart it.' });
    next();
  });

  // A few wrong passwords from one address lock it out for a minute.
  const failures = new Map(); // ip -> { count, until }
  r.post('/login', (req, res) => {
    const ip = req.ip || 'unknown';
    const f = failures.get(ip) || { count: 0, until: 0 };
    if (f.until > Date.now()) return res.status(429).json({ error: 'TOO_MANY_ATTEMPTS', message: 'Too many wrong passwords. Try again in a minute.' });
    const given = Buffer.from(String((req.body && req.body.password) || ''));
    const want = Buffer.from(password);
    const ok = given.length === want.length && crypto.timingSafeEqual(given, want);
    if (!ok) {
      f.count++;
      if (f.count >= 5) { f.until = Date.now() + 60000; f.count = 0; }
      failures.set(ip, f);
      return res.status(401).json({ error: 'BAD_PASSWORD', message: 'Wrong password.' });
    }
    failures.delete(ip);
    res.json({ token: jwt.sign({ role: 'admin' }, secret, { expiresIn: '12h' }) });
  });

  r.use((req, res, next) => {
    const m = /^Bearer (.+)$/.exec(req.headers.authorization || '');
    try {
      const p = m && jwt.verify(m[1], secret);
      if (p && p.role === 'admin') return next();
    } catch (e) { /* fall through */ }
    res.status(401).json({ error: 'UNAUTHORIZED', message: 'Please log in again.' });
  });

  r.get('/chat', wrap(() => store.adminList()));
  r.post('/chat/text', wrap((req) => store.addText(req.body.text)));
  r.post('/chat/emoji', wrap((req) => {
    const data = Buffer.from(String(req.body.data || '').replace(/^data:[^,]*,/, ''), 'base64');
    return store.addEmoji(req.body.label, data);
  }));
  r.patch('/chat/:id', wrap((req) => store.update(req.params.id, req.body || {})));
  r.delete('/chat/:id', wrap(async (req) => { await store.remove(req.params.id); return { ok: true }; }));
  r.put('/chat/order', wrap(async (req) => { await store.reorder(req.body.ids); return { ok: true }; }));
  r.put('/settings', wrap(async (req) => { await store.setCooldown(req.body.cooldownMs); return { ok: true }; }));
  r.post('/chat/restore', wrap(async () => ({ added: await store.restoreBuiltins() })));
  return r;
}

module.exports = { publicRouter, adminRouter };
