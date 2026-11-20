// Fill a table with simple bots so one person can test the game from a phone or the Unity Editor.
//   node tools/bots.js [--url http://localhost:3000] [--level recruit] [--count 3] [--chat 0.1]
// Bots register as bot_<n> (password "botpass1"), press Ready, and play simple random poker.
// Now and then they send a chat emoji or message (--chat: the chance per action and per pot won; 0 = never).
// Needs the socket.io-client dev dependency (npm install).
const http = require('http');
const { io } = require('socket.io-client');

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1]]);
  return acc;
}, []));
const URL_BASE = args.url || 'http://localhost:3000';
const LEVEL = args.level || 'recruit';
const COUNT = Number(args.count || 3);
const CHAT = args.chat !== undefined ? Number(args.chat) : 0.1;

function post(path, body) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify(body);
    const req = http.request(URL_BASE + path, {
      method: 'POST', headers: { 'content-type': 'application/json', 'content-length': Buffer.byteLength(data) },
    }, (res) => {
      let buf = '';
      res.on('data', (c) => { buf += c; });
      res.on('end', () => resolve({ status: res.statusCode, body: JSON.parse(buf || '{}') }));
    });
    req.on('error', reject);
    req.end(data);
  });
}

function getJson(path) {
  return new Promise((resolve) => {
    http.get(URL_BASE + path, (res) => {
      let buf = '';
      res.on('data', (c) => { buf += c; });
      res.on('end', () => { try { resolve(JSON.parse(buf)); } catch (e) { resolve(null); } });
    }).on('error', () => resolve(null));
  });
}

// The chat list, refreshed when the admin changes it.
let chatItems = [];
async function loadChat() {
  const c = await getJson('/chat');
  if (c && Array.isArray(c.items)) chatItems = c.items;
}

async function login(name) {
  const creds = { username: name, password: 'botpass1' };
  let r = await post('/auth/login', creds);
  if (r.status !== 200) r = await post('/auth/register', creds);
  if (r.status !== 200) throw new Error(`${name}: ${JSON.stringify(r.body)}`);
  return r.body;
}

async function runBot(i) {
  const name = `bot_${i}`;
  const { token, user } = await login(name);
  const socket = io(URL_BASE, { auth: { token }, transports: ['websocket'], autoConnect: false });
  const say = (...m) => console.log(`[${name}]`, ...m);
  let acting = false;
  // Emojis mostly, sometimes a message.
  const chatter = (chance) => {
    if (!chatItems.length || Math.random() >= chance) return;
    const emojis = chatItems.filter((c) => c.kind === 'emoji');
    const pool = emojis.length && Math.random() < 0.75 ? emojis : chatItems;
    const item = pool[Math.floor(Math.random() * pool.length)];
    setTimeout(() => socket.emit('table:chat', { id: item.id }, (r) => say('chat', item.text || item.label, r.ok ? '' : r.error)), 300 + Math.random() * 900);
  };

  const onState = (st) => {
    const me = st.yourSeat >= 0 && st.seats[st.yourSeat];
    if (st.phase === 'waiting' && me && !me.ready) socket.emit('table:ready', {}, () => say('ready'));
    if (!st.legal || acting) return;
    acting = true;
    setTimeout(() => {
      const l = st.legal;
      let type = l.canCheck ? 'check' : 'call';
      const roll = Math.random();
      if (roll < 0.15 && !l.canCheck) type = 'fold';
      else if (roll > 0.85 && l.canRaise) type = 'raise';
      socket.emit('table:action', { type, amount: type === 'raise' ? l.minRaiseTo : undefined }, (r) => {
        acting = false;
        say(type, r.ok ? '' : r.error);
        if (r.ok) chatter(CHAT);
      });
    }, 800 + Math.random() * 1200); // feel a bit human
  };

  socket.on('table:state', onState);
  socket.on('table:event', (ev) => {
    if (ev.type === 'potAwarded') {
      say('pot awarded', JSON.stringify(ev.data.awards.map((a) => a.winners)));
      const won = ev.data.awards.some((a) => a.winners.some((w) => w.seat === ev.state.yourSeat));
      if (won) chatter(CHAT * 3);
    }
    onState(ev.state);
  });
  socket.on('table:removed', ({ reason }) => {
    say('removed:', reason);
    if (reason === 'broke') setTimeout(join, 2000);
  });
  socket.on('connect_error', (e) => say('connect error', e.message));
  socket.on('chat:updated', () => loadChat());

  async function join() {
    socket.emit('room:join', { level: LEVEL }, (r) => say('join', r.ok ? r.tableId : r.error));
  }
  socket.on('connect', () => join());
  socket.connect();
  say(`balance ${user.balance}`);
}

(async () => {
  await loadChat();
  for (let i = 1; i <= COUNT; i++) await runBot(i);
})().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
