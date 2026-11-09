// Fill a table with simple bots so one person can test the game from a phone or the Unity Editor.
//   node tools/bots.js [--url http://localhost:3000] [--level recruit] [--count 3]
// Bots register as bot_<n> (password "botpass1"), press Ready, and play simple random poker.
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
      });
    }, 800 + Math.random() * 1200); // feel a bit human
  };

  socket.on('table:state', onState);
  socket.on('table:event', (ev) => {
    if (ev.type === 'potAwarded') say('pot awarded', JSON.stringify(ev.data.awards.map((a) => a.winners)));
    onState(ev.state);
  });
  socket.on('table:removed', ({ reason }) => {
    say('removed:', reason);
    if (reason === 'broke') setTimeout(join, 2000);
  });
  socket.on('connect_error', (e) => say('connect error', e.message));

  async function join() {
    socket.emit('room:join', { level: LEVEL }, (r) => say('join', r.ok ? r.tableId : r.error));
  }
  socket.on('connect', () => join());
  socket.connect();
  say(`balance ${user.balance}`);
}

(async () => {
  for (let i = 1; i <= COUNT; i++) await runBot(i);
})().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
