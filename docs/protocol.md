# War-Map Poker — Client/Server Protocol

This is the contract between the Unity client and the Node server. All JSON uses camelCase.

## Basics
- Server base URL (dev): `http://<host>:3000`. The same port serves REST and Socket.IO (default path `/socket.io/`).
- Cards are 2-character strings: a rank from `23456789TJQKA` plus a suit from `c d h s` (clubs, diamonds, hearts, spades). Examples: `"Ah"`, `"Td"`, `"2c"`.
- Amounts are whole numbers of soldiers. 1 soldier = 1 unit.
- Times are milliseconds since the Unix epoch. Every snapshot includes `serverTime` so the client can correct for clock drift.
- Errors look like `{ "error": "CODE", "message": "human text" }`.

## Room levels
| id | name | buyIn | smallBlind | bigBlind |
|---|---|---|---|---|
| recruit | Recruit | 1000 | 10 | 20 |
| soldier | Soldier | 5000 | 50 | 100 |
| captain | Captain | 20000 | 200 | 400 |
| general | General | 100000 | 1000 | 2000 |
| king | King | 500000 | 5000 | 10000 |

## Soldiers (client display only)
There is one soldier type. To draw an amount, show the number next to a squad of identical soldier figures, one more figure per digit, up to 5:

| Amount | Figures drawn |
|---|---|
| 1–9 | 1 |
| 10–99 | 2 |
| 100–999 | 3 |
| 1,000–9,999 | 4 |
| 10,000 or more | 5 |

## REST

**User object:** `{ "id", "username", "balance", "nextRefillAt" }`
- `nextRefillAt` is a ms time, or `null`.
- A refill is available when `balance < 1000`, `nextRefillAt` is `null` or in the past, and the user is not seated at a table.

| Method + path | Body | Success | Errors |
|---|---|---|---|
| `POST /auth/register` | `{ username, password }` | `200 { token, user }` | `400 INVALID_USERNAME` (3–16 chars, `[A-Za-z0-9_]`), `400 INVALID_PASSWORD` (6–64 chars), `409 USERNAME_TAKEN` |
| `POST /auth/login` | `{ username, password }` | `200 { token, user }` | `401 BAD_CREDENTIALS` |
| `GET /me` | — | `200 { user }` | `401 UNAUTHORIZED` |
| `POST /me/refill` | — | `200 { user }` | `400 REFILL_NOT_AVAILABLE` |
| `GET /rooms` | — | `200 { rooms: [Room] }` | — |
| `GET /health` | — | `200 { ok: true }` | — |

- `/me` and `/me/refill` need the header `Authorization: Bearer <token>`.
- **Room object:** `{ id, name, buyIn, smallBlind, bigBlind, players, tables, maxSeats: 8 }`. `players` counts everyone seated across all tables at that level.

## Socket.IO
- **Connect:** `io(url, { auth: { token } , transports: ["websocket"] })`. An invalid token rejects the connection with error message `UNAUTHORIZED`.
- **One socket per user.** If the same user connects again, the newer socket replaces the older one, and the older one gets `session:replaced` and is disconnected.
- **Reconnect:** when a user reconnects while seated, the server automatically sends `table:state` with the current snapshot. It is sent the moment the socket connects, so register all event handlers *before* connecting. Sending `table:sync` after each reconnect is a safe extra.

### Client → server
Every request takes an ack callback, which is called with `{ ok: true, ... }` or `{ ok: false, error: "CODE" }`.

| Event | Payload | Ack on success | Notes |
|---|---|---|---|
| `lobby:subscribe` | `{}` | `{ ok, rooms }` | The server then sends `lobby:rooms` whenever counts change |
| `lobby:unsubscribe` | `{}` | `{ ok }` | |
| `room:join` | `{ level }` | `{ ok, tableId }` | Errors: `UNKNOWN_LEVEL`, `INSUFFICIENT_BALANCE`, `ALREADY_SEATED`. After the ack, `table:state` follows. |
| `table:ready` | `{}` | `{ ok }` | Error: `NOT_SEATED` |
| `table:action` | `{ type, amount? }` | `{ ok }` | `type`: `fold`, `check`, `call`, `raise`, `allin`. For `raise`, `amount` is the total bet to raise *to* this street. Errors: `NOT_YOUR_TURN`, `ILLEGAL_ACTION`, `BAD_AMOUNT` |
| `table:leave` | `{}` | `{ ok, balance }` | Your stack goes back to your balance. If you're in a hand, you fold first. |
| `table:show` | `{}` | `{ ok }` | Only for the player who just won because everyone else folded, until the next hand starts (`canShowCards` in the snapshot). Reveals their hole cards to the table. Error: `CANNOT_SHOW` |
| `table:sync` | `{}` | `{ ok }` | The server sends a fresh `table:state` |

### Server → client
| Event | Payload |
|---|---|
| `lobby:rooms` | `{ rooms: [Room] }` |
| `table:state` | `Snapshot` (see below) |
| `table:event` | `{ seq, type, data, state: Snapshot }`. `state` is the snapshot *after* the event, made for this player. Use `data` to animate, then treat `state` as the truth. |
| `user:balance` | `{ balance, nextRefillAt }` |
| `table:removed` | `{ reason }`, where `reason` is `broke`, `left`, or `timeout` (disconnected too long). The client goes back to the lobby. |
| `session:replaced` | `{}` |

### Snapshot
```jsonc
{
  "tableId": "recruit-1",
  "level": "recruit", "name": "Recruit",
  "buyIn": 1000, "smallBlind": 10, "bigBlind": 20, "maxSeats": 8,
  "seq": 42,                    // the seq of the last event that was applied
  "serverTime": 1731100000000,
  "phase": "waiting",           // waiting | preflop | flop | turn | river | showdown
  "handNumber": 3,
  "dealerSeat": 0, "sbSeat": 1, "bbSeat": 2,      // -1 when there is no hand
  "board": ["Ah","Td","7c"],
  "pots": [ { "amount": 300, "eligibleSeats": [0,1,2] } ],  // pots from earlier streets; bets on this street are not in them yet
  "potTotal": 340,              // pots + all current bets
  "currentBet": 40,             // highest bet this street
  "turnSeat": 1,                // -1 when no one is acting
  "turnDeadline": 1731100020000,// ms; 0 when no one is acting
  "turnTimeMs": 20000,
  "yourSeat": 3,                // -1 if you are not seated
  "yourCards": ["As","Kd"],     // [] when you have no cards
  "seats": [                    // always 8 entries; null = empty seat
    { "seat": 0, "userId": "…", "username": "bob", "stack": 960,
      "bet": 40,                // committed on this street
      "totalBet": 120,          // committed in this whole hand (blinds + every street); 0 once the pot is paid out
      "inHand": true,           // dealt into the current hand
      "folded": false, "allIn": false,
      "ready": true,            // pressed Ready (matters during the waiting phase)
      "connected": true,
      "lastAction": "raise",    // fold|check|call|raise|allin|sb|bb|null; resets each street
      "cards": null }           // revealed cards at showdown, otherwise null
  ],
  "canShowCards": false,        // true right after you won by everyone folding: offer a "Show cards" button (table:show)
  "legal": null                 // filled in only when it's your turn:
  // { "canFold": true, "canCheck": false, "canCall": true, "callAmount": 20,
  //   "canRaise": true, "canAllIn": true, "minRaiseTo": 80, "maxRaiseTo": 1000 }
  // maxRaiseTo is your bet + your stack, which is all-in.
  // If the min raise is more than you have, canRaise is false and only allin is offered.
  // canAllIn is false when going all-in would be a raise you're not allowed to make
  // (after a short all-in, players who already acted may only call or fold).
  // Show the All-in button only when canAllIn is true.
}
```

### Event types (`table:event.type`) and their `data`
| type | data |
|---|---|
| `playerJoined` | `{ seat, username }` |
| `playerLeft` | `{ seat, username, reason }` |
| `playerReady` | `{ seat }` |
| `connection` | `{ seat, connected }` |
| `handStarted` | `{ handNumber, dealerSeat, sbSeat, bbSeat, seats: [seat…] }` (the seats dealt in, in dealing order starting left of the dealer). `yourCards` in `state` now holds your two cards. Play the deal animation. |
| `blinds` | `{ sb: { seat, amount }, bb: { seat, amount } }` |
| `turn` | `{ seat, deadline }` |
| `action` | `{ seat, type, amount, bet, stack, allIn, auto }`. `amount` is the soldiers added by this action (troops march to the pot). `auto` is true when the action came from a timeout. |
| `street` | `{ phase, newCards: [...], board: [...] }`. Bets have been gathered into `pots` (animate the troops merging into the pot). |
| `showdown` | `{ reveals: [ { seat, cards, handName, bestCards, rank } ] }`. `rank` orders the hands best first (1 = best); tied hands share a rank (1, 1, 3). |
| `potAwarded` | `{ awards: [ { amount, winners: [ { seat, amount } ], handName, bestCards: [...] } ] }`. `handName` is `null` and `bestCards` is `[]` when everyone else folded. |
| `waiting` | `{}`. The table went back to the `waiting` phase: fewer than 2 players with soldiers are left, and everyone's `ready` was reset. Show the Ready button again. |
| `handEnded` | `{ handNumber }`. The next hand starts after about 8 seconds after a showdown, or 4.5 seconds when everyone folded, if 2 or more players with soldiers remain. Otherwise the table goes back to `waiting`. |
| `cardsShown` | `{ seat, cards }`. A fold winner chose to show their hole cards; `state.seats[seat].cards` now holds them. |

### Table flow
1. **Join:** `room:join` seats you at the first table of that level with a free seat. If every table is full, a new one opens. Your buy-in moves from your balance to your stack.
2. **Ready:** while the table is in `waiting`, each player sends `table:ready`. The first hand starts when 2 or more players are seated and *all* seated players are ready.
3. **Auto-continue:** once started, hands run back to back. Anyone who joins mid-hand gets `inHand=false` and is dealt in from the next hand. They are treated as ready, since the game is already running.
4. **Broke:** after a hand, any player with a stack of 0 is removed (`table:removed {reason:"broke"}`).
5. **Back to waiting:** if fewer than 2 players are left, the table returns to `waiting`, and everyone's `ready` resets to false.
6. **Turn timeout:** after 20 seconds, the server checks for you if it can, otherwise it folds (`auto: true`).
7. **Disconnects:** if you're disconnected for more than 60 seconds, you're removed (`timeout`), and your stack goes back to your balance.
