# War-Map Poker — Design

## What it is
Texas Hold'em for Android. Instead of a green felt table, players sit around an old parchment war map, and instead of chips they bet soldiers. A bet is just a number of soldiers. There is one soldier type. The game draws the number next to a small squad of identical soldiers, and bigger amounts get a bigger squad:

| Amount | Figures drawn |
|---|---|
| 1–9 | 1 |
| 10–99 | 2 |
| 100–999 | 3 |
| 1,000–9,999 | 4 |
| 10,000 or more | 5 |

So 1,250 soldiers is drawn as 4 soldier figures next to "1,250".

## Stack
- **Client:** Unity 2022.3 LTS (2D), Android only (min API 24, IL2CPP, ARM64).
- **Server:** Node 16, Express, Socket.IO 4.
- **Database:** MongoDB 6.
- **Network:** made for mobile 4G. Messages are small, the client reconnects on its own, and the server holds a disconnected player's seat for 60 seconds.

## Screens
1. **Login / Register:** username and password.
2. **Lobby:** your soldier balance, a free refill button, and 5 rooms. Each room shows its starting soldiers (buy-in), its blinds, and how many players are in it.
3. **Game (war map):**
   - Up to 8 army camps around the map; you always sit at the bottom.
   - The shared cards and the pot army are in the middle of the battlefield.
   - Action buttons appear on your turn: Fold, Check/Call, Raise (with a slider), All-in.

Animations and effects are on the game screen only: dealing, card flips, troops marching to the pot and back to the winner, fold, an all-in flash, the winner's cards lighting up with a victory banner, and the active camp glowing with a turn timer.

## Rooms and economy
| Room | Starting soldiers (buy-in) | Blinds |
|---|---|---|
| Recruit | 1,000 | 10 / 20 |
| Soldier | 5,000 | 50 / 100 |
| Captain | 20,000 | 200 / 400 |
| General | 100,000 | 1,000 / 2,000 |
| King | 500,000 | 5,000 / 10,000 |

- **New accounts** start with 2,000 soldiers.
- **Refill:** if your balance is under 1,000 and you're not seated, you can claim a free top-up to 1,000 once every 24 hours.
- **Joining a room** moves the buy-in from your balance to your table stack. Leaving moves the stack back.
- **Tables:** up to 8 players per table. When a table is full, the room quietly opens another table at the same level.

## Table flow
1. Tap a room, and you're seated right away.
2. Everyone presses **Ready**. The game starts when all seated players are ready (at least 2).
3. After that, hands run back to back, about 4 seconds apart. New players are dealt in from the next hand.
4. A player who loses all their soldiers is sent back to the lobby.
5. If fewer than 2 players are left, the table goes back to waiting for Ready.
6. **Turn timer:** 20 seconds. When it runs out, the server checks for you if it can, otherwise it folds.
7. **Disconnects:** a disconnected player keeps their seat for 60 seconds, then is removed and their soldiers go back to their balance.

## Cards
- A standard card shape. A narrow strip down the left side shows the rank above the suit symbol.
- The large main area is an image slot. It holds placeholder art now, and the client's art can drop in per card without code changes.
- Card backs come from the same art slots. See client/README.md for the art slot paths.

## Rules details
Standard No-Limit Hold'em:
- **Heads-up:** the dealer posts the small blind and acts first before the flop.
- **Raises:** the minimum raise is the size of the last raise. A short all-in doesn't reopen raising for players who already acted.
- **Side pots:** built for every all-in level.
- **Split pots:** the odd soldier goes to the winner closest to the dealer's left.

## Not in this version
- Real-money purchases or ads
- Chat
- Friends
- Private tables
- Leaderboards
- Sound
- iOS
