# War-Map Poker — Server

Node 16 + Express + Socket.IO 4 + MongoDB. The server is the only authority: it shuffles, deals, enforces the rules, and sends each player only their own cards. The message format is in [../docs/protocol.md](../docs/protocol.md).

## Run it

You need Docker, or Node 16 and MongoDB 6.

```bash
# Option A: everything in Docker (server on Node 16, Mongo 6)
docker compose up -d mongo
docker compose up server            # http://localhost:3000

# Option B: local Node
docker compose up -d mongo          # or your own MongoDB
npm install
npm start                           # PORT=3000 MONGO_URL=mongodb://localhost:27017/warpoker
```

Environment variables:

| Variable | Default | |
|---|---|---|
| `PORT` | 3000 | |
| `MONGO_URL` | `mongodb://localhost:27017/warpoker` | |
| `JWT_SECRET` | `dev-only-secret-change-me` | **Must be set in production** |
| `TURN_TIME_MS` | 20000 | |
| `NEXT_HAND_DELAY_MS` | 4000 | |
| `DISCONNECT_GRACE_MS` | 60000 | |

Room levels, starting balance, refill rules, and seat count are in `src/config.js`.

## Test it

```bash
docker compose run --rm test     # all tests on Node 16 against Mongo (what CI should run)
npm test                         # same, on your local Node (Mongo at localhost:27017)
npm run test:unit                # engine-only tests, no database needed
```

What the tests cover:
- **Engine:** positions and blinds (including heads-up), min-raise rules, a short all-in not reopening raises, side pots, split pots with the odd soldier, timeouts, and leaving mid-hand.
- **Random play:** 3,000 random hands with no soldiers ever created or lost.
- **API:** register, login, `/me`, the refill rules, room list, and the join rules.
- **Bots:** 9 bots over real sockets. The 9th player opens a second table, play runs for 40+ hands, one bot drops and gets timed out, and broke bots rejoin. The test then checks that every soldier is back in the database, and that no player ever received another player's hole cards before showdown.
- **Reconnect:** a player who reconnects mid-hand keeps their seat and their cards, and a second login replaces the first connection.

## Play against bots (manual testing)

```bash
node tools/bots.js --url http://localhost:3000 --level recruit --count 3
```
Then join Recruit from the Unity app and press Ready.

## Layout

| Path | What it does |
|---|---|
| `src/game/holdemEngine.js` | The rules for one hand. Pure logic, with no timers or I/O. |
| `src/game/pots.js`, `handEval.js`, `deck.js` | Side pots, hand ranking (pokersolver), and the crypto shuffle |
| `src/game/table.js` | Seats, Ready, the hand loop, turn timers, disconnects, and the per-player snapshot |
| `src/game/tableManager.js` | The 5 room levels and their tables; opens a new table when one is full |
| `src/db/wallet.js` | Every soldier movement, as single atomic MongoDB updates |
| `src/socket/handlers.js` | Socket auth and events |
| `src/auth`, `src/account` | REST: register, login, `/me`, refill |

**How soldiers are kept safe:**
- Buying in moves soldiers from `balance` to `tableStack` in one atomic update.
- `tableStack` is saved after every hand.
- Leaving moves the stack back to `balance`.
- If the server crashes, the next start returns every `tableStack` to its owner. The hand in progress is lost, but no soldiers are.

## Before production
- Set a strong `JWT_SECRET`, and serve over HTTPS/WSS behind a reverse proxy.
- Add rate limiting on `/auth/*`, for example with `express-rate-limit`.
- **Node 16 has been end-of-life since Sept 2023 and gets no security fixes.** The code also runs on Node 18 and 20 (it's developed on 18 here). Moving to Node 20 is recommended.
- There's one game process. Tables live in memory, so running several server instances would need sticky sessions plus a table-ownership scheme.

## Customizing
Wording, art, client settings and server game settings live in config files, so most changes need no code. See [docs/customizing.md](../docs/customizing.md).
