# War-Map Poker — Server

The game server for War-Map Poker. It runs on Node 16.2 with Express, Socket.IO 4 and MongoDB 4.2.

The server is the only authority. It shuffles, deals and enforces the rules, and it sends each player only their own cards. The app only shows what the server sends and passes on what the player chose.

- **Message format** (REST and Socket.IO): [../docs/protocol.md](../docs/protocol.md)
- **Game settings** you can change without code: [../docs/customizing.md](../docs/customizing.md)
- **The Android app:** [../client/README.md](../client/README.md)

---

## Contents
1. [Quick start with Docker](#1-quick-start-with-docker)
2. [Quick start without Docker](#2-quick-start-without-docker)
3. [Running offline (no internet)](#3-running-offline-no-internet)
4. [Connecting phones](#4-connecting-phones)
5. [Everyday commands](#5-everyday-commands)
6. [Settings](#6-settings)
7. [Bots for testing](#7-bots-for-testing)
8. [Tests](#8-tests)
9. [API at a glance](#9-api-at-a-glance)
10. [How the server works](#10-how-the-server-works)
11. [Running in production](#11-running-in-production)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Quick start with Docker

You need Docker with the Compose plugin (`docker compose version` should work). Docker installs Node and MongoDB for you, inside containers.

```bash
cd server
docker compose build          # once: builds the server image (needs internet)
docker compose up -d          # starts MongoDB 4.2 and the server in the background
curl http://localhost:3000/health
# {"ok":true}
```

The server is now on **http://localhost:3000**, and both containers restart on their own after a crash or a reboot.

| To… | Run |
|---|---|
| Watch the log | `docker compose logs -f server` |
| Stop everything | `docker compose down` (the data stays) |
| Use another port | `SERVER_PORT=3456 docker compose up -d` |
| Set the login secret | `JWT_SECRET='a-long-random-string' docker compose up -d` |

To set these permanently, put them in a file named `.env` next to `docker-compose.yml`. Don't commit that file.

```
SERVER_PORT=3456
JWT_SECRET=a-long-random-string
```

---

## 2. Quick start without Docker

You need:
- **Node.js 16.2 or newer.** 16.2 is the version the project targets. The code also runs on Node 18 and 20.
- **MongoDB 4.2.** That's the version the project targets; 6.0 has also been used with this server.

```bash
# Node 16.2 with nvm (https://github.com/nvm-sh/nvm); skip this if you already have Node
nvm install 16.2.0 && nvm use 16.2.0

cd server
npm ci                        # once: installs the exact dependency versions from package-lock.json
npm start                     # http://localhost:3000, database mongodb://localhost:27017/warpoker
```

Settings come from environment variables, for example:

```bash
PORT=3456 \
MONGO_URL=mongodb://localhost:27017/warpoker \
JWT_SECRET='a-long-random-string' \
npm start
```

**Without your own MongoDB install,** you can run only the database in Docker and the server with your local Node:

```bash
docker compose up -d mongo    # MongoDB on localhost:27017 (this machine only)
npm start
```

`npm ci --omit=dev` installs only what the server needs to run. Use it on a production machine; you then can't run the tests or the bots there.

---

## 3. Running offline (no internet)

Neither way of running the server needs internet once it's set up. Phones on the same Wi-Fi can play against it (see [section 4](#4-connecting-phones)).

### With Docker
The image already contains every dependency. So after **one** online `docker compose build`, starting, stopping and restarting don't use the internet:

```bash
docker compose build          # online, once (and again after you change package.json)
# ... later, with no internet:
docker compose up -d
```

**To set up a machine that never has internet,** prepare the images on a connected machine:

```bash
# on a machine with internet, in server/
docker compose build
docker pull mongo:4.2
docker save war-map-poker-server war-map-poker-server-dev mongo:4.2 -o warpoker-images.tar
# copy warpoker-images.tar and the whole server/ folder to the offline machine, then there:
docker load -i warpoker-images.tar
docker compose up -d
```

The code changes often, but the images rarely do. After you copy updated code, run `docker compose up -d --build`. That rebuild works offline as long as `package.json` and `package-lock.json` haven't changed.

### Without Docker
Run `npm ci` once on a machine with internet, then copy the whole `server/` folder, **including `node_modules/`**, to the offline machine. No dependency contains native code, so `node_modules/` works on any operating system. You also need Node and MongoDB installed on the offline machine; their installers can be copied over too.

---

## 4. Connecting phones

The app asks for the server address on its login screen (the **Server** field), and the phone remembers it. The address it shows first is built into the app; see the client guide.

| Where the server runs | Address to type in the app |
|---|---|
| Your PC; the phone is on the same Wi-Fi | `http://<PC's LAN IP>:3000`, for example `http://192.168.1.20:3000` |
| Your PC; Android emulator on that PC | `http://10.0.2.2:3000` |
| A server with a public IP | `http://<public IP>:<port>`, for example the demo server `http://95.217.62.59:3456` |
| Behind HTTPS (production) | `https://play.example.com` |

**Finding your LAN IP:**
- Linux: `hostname -I`
- macOS: `ipconfig getifaddr en0`
- Windows: `ipconfig`, then look for "IPv4 Address"

**If the phone can't connect:**
- Make sure the port is open in the PC's firewall.
  - Ubuntu: `sudo ufw allow 3000/tcp`
  - Windows: allow Node or Docker when Windows asks, or add an inbound rule for the port.
- Check from the phone's browser: `http://<address>/health` should show `{"ok":true}`.

---

## 5. Everyday commands

| Task | With Docker | Without Docker |
|---|---|---|
| Start | `docker compose up -d` | `npm start` |
| Stop | `docker compose down` | Ctrl+C (or `systemctl stop warpoker`, see §11) |
| Restart after editing `config.json` | `docker compose restart server` | stop, then `npm start` |
| Update after pulling new code | `docker compose up -d --build` | `npm ci`, then restart |
| Log | `docker compose logs -f server` | printed to the terminal (`journalctl -u warpoker -f` with systemd) |
| Is it up? | `curl localhost:3000/health` | same |
| Room list with player counts | `curl localhost:3000/rooms` | same |
| Database shell | `docker compose exec mongo mongo warpoker` | `mongo warpoker` (or `mongosh warpoker`) |
| Back up the database | `docker compose exec -T mongo mongodump --archive --gzip --db warpoker > backup.gz` | `mongodump --archive=backup.gz --gzip --db warpoker` |
| Restore a backup | `docker compose exec -T mongo mongorestore --archive --gzip --drop < backup.gz` | `mongorestore --archive=backup.gz --gzip --drop` |
| Wipe all accounts (start fresh) | `docker compose down -v` (deletes the database volume) | `mongo warpoker --eval 'db.dropDatabase()'` |

### Useful database one-liners
Run these in the database shell.

```js
db.users.find({}, { username: 1, balance: 1, tableStack: 1, atTable: 1 })   // list players
db.users.updateOne({ username: "alice" }, { $set: { balance: 50000 } })     // give a player soldiers
db.users.deleteOne({ username: "bot_1" })                                   // delete an account
```

- Change balances only while the player is **not** seated. While they sit at a table, their soldiers are in `tableStack`.
- Passwords are stored as bcrypt hashes and can't be read back. To reset one, delete the account and let the player register again.

---

## 6. Settings

There are two places for settings. You can leave out anything in either; it keeps its built-in default.

### `server/config.json`: game settings
Rooms, buy-ins and blinds, starting soldiers, the refill, timers, and private-room limits. Edit it, then restart the server. With Docker, the file is mounted into the container, so no rebuild is needed: `docker compose restart server`.

```json
{
  "startingBalance": 2000,
  "refill": { "threshold": 1000, "topUpTo": 1000, "cooldownMs": 86400000 },
  "maxSeats": 8,
  "turnTimeMs": 20000,
  "nextHandDelayMs": 4500,
  "showdownDelayMs": 12000,
  "disconnectGraceMs": 60000,
  "idleTimeouts": 3,
  "readyTimeoutMs": 30000,
  "privateRooms": { "minBuyIn": 100, "maxBuyIn": 1000000, "minStackBigBlinds": 10, "codeLength": 6 },
  "levels": [
    { "id": "recruit", "name": "Recruit", "buyIn": 1000, "smallBlind": 10, "bigBlind": 20 }
  ]
}
```

What every key means is in [docs/customizing.md](../docs/customizing.md#server-settings-serverconfigjson). Times are in milliseconds.

The server checks the file when it starts. If a setting can't work, it refuses to start and says what to fix, for example `Bad server config: level "recruit": smallBlind is bigger than bigBlind`.

### Environment variables: deployment settings

| Variable | Default | What it sets |
|---|---|---|
| `PORT` | `3000` | HTTP and Socket.IO port. With Docker, use `SERVER_PORT` instead; the container always listens on 3000 inside. |
| `MONGO_URL` | `mongodb://localhost:27017/warpoker` | The database. The database name is the last part. |
| `JWT_SECRET` | `dev-only-secret-change-me` | Signs login tokens. **Set a long random value on any real server.** If you change it, everyone has to log in again. |
| `CONFIG_FILE` | `server/config.json` | Use a different game-settings file. |
| `TURN_TIME_MS`, `NEXT_HAND_DELAY_MS`, `SHOWDOWN_DELAY_MS`, `DISCONNECT_GRACE_MS`, `IDLE_TIMEOUTS`, `READY_TIMEOUT_MS` | from `config.json` | Override single timers, mainly for tests. |

The order of precedence is built-in defaults → `config.json` → environment variables (later wins).

> **One server per database.** When the server starts, it gives every seated player's soldiers back to their balance, because the tables of a crashed server are gone. A second server started on the same database would do this to the first server's players. Give each server its own database name in `MONGO_URL`.

---

## 7. Bots for testing

Bots fill a table so one person can test from a phone or the Unity Editor. They register as `bot_1`, `bot_2`, … (password `botpass1`), press Ready, and play simple random poker. Stop them with Ctrl+C.

```bash
# Docker: 3 bots in the Recruit room of the Docker server
docker compose run --rm bots
# other options:
docker compose run --rm bots node tools/bots.js --url http://server:3000 --level captain --count 5

# Without Docker (needs the dev dependencies: npm ci, not --omit=dev)
node tools/bots.js --url http://localhost:3000 --level recruit --count 3
```

| Option | Default | |
|---|---|---|
| `--url` | `http://localhost:3000` | Server address. Inside Docker, the server is `http://server:3000`. |
| `--level` | `recruit` | Room id from `config.json` (`recruit`, `soldier`, `captain`, `general`, `king`) |
| `--count` | `3` | Number of bots (8 seats per table; extra players open another table) |

Then join the same room from the app and press **Ready**.

---

## 8. Tests

```bash
docker compose run --rm test           # all tests: Node 16.2 + MongoDB 4.2 (works offline after the build)
npm test                               # all tests on your local Node; needs MongoDB on localhost:27017
npm run test:unit                      # rules engine only; no database needed, a few seconds
npx jest test/integration/rules.test.js   # a single file
```

- The tests use their own database (`warpoker_test`), so they never touch real accounts.
- The full run takes about half a minute; the bot test plays real hands over sockets.
- With Docker, the test container runs your current code, so you don't need to rebuild after a code change. Rebuild only after changing `package.json`: `docker compose build test`.

**What the tests cover:**

| Area | Covered |
|---|---|
| Engine | Positions and blinds (including heads-up), min-raise rules, a short all-in not reopening raises, side pots, split pots with the odd soldier, timeouts, leaving mid-hand. |
| Random play | 3,000 random hands, with no soldier ever created or lost. |
| API | Register, login, `/me`, refill rules, room list, join rules. |
| Rules | The ready countdown, idle players, Retreat (with and without cards in hand), leaving while all-in. |
| Private rooms | Custom stakes and their limits, join by code, invites, full rooms, the room closing when empty. |
| Bots | 9 bots over real sockets for 40+ hands: a second table opens, a bot drops and times out, broke bots rejoin. Afterwards every soldier must be back in the database, and no player may have seen another player's hole cards before showdown. |
| Reconnect | A player reconnecting mid-hand keeps their seat and cards; a second login replaces the first connection. |

---

## 9. API at a glance

The full contract, with every payload, error code and the snapshot format, is in [../docs/protocol.md](../docs/protocol.md).

**REST** (JSON). `/me` routes need the header `Authorization: Bearer <token>`.

| Request | Does |
|---|---|
| `POST /auth/register` `{username, password}` | Creates an account. Returns `{token, user}`. |
| `POST /auth/login` `{username, password}` | Returns `{token, user}`. |
| `GET /me` | The logged-in user and their balance. |
| `POST /me/refill` | Free soldiers when the balance is low; limited by `refill` in `config.json`. |
| `GET /rooms` | Room levels with live player counts, and the private-room limits. |
| `GET /health` | `{"ok":true}`. Use it for uptime checks. |

**Socket.IO.** Connect with `io(url, { auth: { token }, transports: ["websocket"] })`.

| The app sends | For |
|---|---|
| `lobby:subscribe` / `lobby:unsubscribe` | Live room counts |
| `room:join {level}` | Sit at a public table (pays the buy-in) |
| `room:createPrivate {buyIn, bigBlind}`, `room:joinCode {code}`, `room:invite {username}` | Private rooms |
| `table:ready`, `table:action {type, amount}`, `table:show` | Playing |
| `table:leave`, `table:leaveAfterHand {on}` | Leaving and Retreat |
| `table:sync` | Ask for a fresh snapshot |

| The server sends | When |
|---|---|
| `table:state` | A full snapshot for this player (after joining, reconnecting, or `table:sync`) |
| `table:event` | Something happened (`handStarted`, `action`, `street`, `showdown`, `potAwarded`, …), plus the new snapshot |
| `lobby:rooms`, `user:balance`, `table:removed`, `room:invite`, `session:replaced` | Lobby counts, balance changes, removal from a table, invites, logged in elsewhere |

---

## 10. How the server works

| Path | What it does |
|---|---|
| `src/index.js`, `src/app.js` | Starts the server: database, REST routes, Socket.IO, the table manager |
| `src/config.js` + `config.json` | Settings: defaults, then the file, then environment variables, then validation |
| `src/game/holdemEngine.js` | The rules of one hand. Pure logic, with no timers or I/O, so it's easy to test. |
| `src/game/pots.js`, `handEval.js`, `deck.js` | Side pots, hand ranking (pokersolver), and the shuffle (`crypto.randomInt`) |
| `src/game/table.js` | One table: seats, Ready, the hand loop, turn timers, disconnects, Retreat, and each player's own snapshot |
| `src/game/tableManager.js` | The room levels and their tables (opens another table when one is full), and private rooms |
| `src/db/User.js`, `src/db/wallet.js` | Accounts, and every soldier movement as a single atomic MongoDB update |
| `src/auth/`, `src/account/` | REST: register, login, `/me`, refill |
| `src/socket/handlers.js` | Socket login check, and the lobby, room and table events |
| `tools/bots.js` | Test bots |
| `test/unit`, `test/integration` | Jest tests |

**How soldiers are kept safe:**
- Joining a table moves the buy-in from `balance` to `tableStack` in one atomic update; it fails if the balance is too low.
- `tableStack` is saved after every hand. Leaving moves it back to `balance`.
- If the server crashes or restarts, the next start returns every `tableStack` to its owner. The hand in progress is lost, but no soldiers are.
- Tables live in memory, in one process. Restarting the server sends everyone back to the lobby with their soldiers.

---

## 11. Running in production

**Checklist:**
- [ ] Set a strong `JWT_SECRET`, for example from `openssl rand -hex 32`.
- [ ] Put the server behind HTTPS (nginx or Caddy, see below), and set the app's server address to `https://…`. Then turn off plain HTTP in the app (see the client guide).
- [ ] Keep MongoDB reachable from this machine only. The compose file already binds it to `127.0.0.1`. Otherwise turn on MongoDB authentication.
- [ ] Back up the database regularly (§5).
- [ ] Add rate limiting on `/auth/*` against password guessing, for example with `express-rate-limit`.
- [ ] Run one server process. Several processes would need sticky sessions and a way to share tables.
- [ ] Node 16 stopped getting security fixes in Sept 2023. The code runs on Node 20 unchanged: change `node:16.2.0-alpine` to `node:20-alpine` in the `Dockerfile`, and the `engines` field in `package.json`.

**Docker:** `docker compose up -d` already restarts after crashes and reboots, as long as Docker itself starts at boot (`sudo systemctl enable docker`).

**Without Docker: a systemd service.** Save this as `/etc/systemd/system/warpoker.service`:

```ini
[Unit]
Description=War-Map Poker server
After=network.target mongod.service

[Service]
WorkingDirectory=/opt/war-map-poker/server
ExecStart=/usr/bin/node src/index.js
Environment=PORT=3000
Environment=MONGO_URL=mongodb://localhost:27017/warpoker
Environment=JWT_SECRET=change-me-to-a-long-random-string
Restart=always
User=warpoker

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now warpoker
journalctl -u warpoker -f        # log
```

**HTTPS with nginx.** WebSockets need the two `Upgrade` lines:

```nginx
server {
    server_name play.example.com;
    listen 443 ssl;
    # ssl_certificate ... (for example from certbot)
    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 120s;
    }
}
```

---

## 12. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `EADDRINUSE` on start | Something else uses the port. Pick another: `PORT=3456 npm start`, or `SERVER_PORT=3456 docker compose up -d`. |
| `MongooseServerSelectionError` / `ECONNREFUSED 27017` | MongoDB isn't running. Start it: `docker compose up -d mongo` or `sudo systemctl start mongod`. |
| `Bad server config: …` | `config.json` has a value that can't work; the message says which. Invalid JSON is reported with its position. |
| The app says it can't reach the server | Open `http://<address>/health` in the phone's browser. If that fails, check the IP, the port and the firewall (§4). |
| Everyone is logged out after a restart | `JWT_SECRET` changed (or isn't set and the default differs between machines). Keep it fixed. |
| "Opened on another device" in the app | The same account logged in elsewhere. Only one connection per account is allowed; the newer one wins. |
| A player's soldiers look stuck | They're at a table (`atTable: true`). They come back when the player leaves, or when the server restarts. |
| `docker compose run --rm test` fails right after a `package.json` change | Rebuild the test image: `docker compose build test`. |
| `npm ci` fails offline | Use the Docker image, or copy `node_modules/` from a machine where `npm ci` worked (§3). |
