# Customizing War-Map Poker

Wording, art and game settings live in a few files, so you can change them without editing code.

| I want to change… | Edit this | Then |
|---|---|---|
| Any text players see (buttons, titles, messages, errors, hand names) | `client/Assets/_Project/Resources/Config/text.json` | Rebuild the app |
| The server address the app connects to | `serverUrl` in `client/Assets/_Project/Resources/Config/game.json` | Rebuild the app |
| Showdown, march and crown timings, maximum soldiers per squad | `client/Assets/_Project/Resources/Config/game.json` | Rebuild the app |
| An image, font or animation (buttons, panels, soldier, card art, map) | Put the new file in `client/Assets/_Project/Resources/…`; if its name or location differs, update `client/Assets/_Project/Resources/Config/art.json` | Rebuild the app |
| Colours | `client/Assets/_Project/Scripts/UI/Theme.cs` | Rebuild the app |
| Rooms, buy-ins, blinds, starting soldiers, refill, turn and idle timers | `server/config.json` | Restart the server |
| Server port, database, secret | Environment variables `PORT`, `MONGO_URL`, `JWT_SECRET` | Restart the server |

Every config file is optional, and so is every line in it. Anything you leave out keeps its built-in default.

## Wording: `Resources/Config/text.json`

Each line is `"Key": "Text shown"`. Change the text on the right; never rename the key on the left.

```json
"Ready": "Ready for battle",
"BuyIn": "Buy-in {0}",
"NeedSoldiers": "You need {0} soldiers to march on {1}. Your army: {2}.",
"HandRank.Flush": "Flush"
```

- `{0}`, `{1}`, … are filled in by the game: amounts, names, seconds. Keep them in your new wording. You can move them within the sentence.
- `\n` starts a new line.
- Groups of keys:
  - `Float.*` is the text that pops up over a camp when a player acts.
  - `Action.*` is the label under each camp.
  - `HandRank.*` is the poker hand names, as the server sends them.
  - `Removed.*` explains why a player left the table.
  - `Error.*` is the server error messages.
- To keep a second set of wording, for example for an event or a language, copy the file to `Config/text_event.json` and set `"wordingFile": "Config/text_event"` in `game.json`.
- After a game update, run **WarPoker → Update Config Files** in the Unity Editor. It adds any new keys with their English default and never touches your edits.

## Art: `Resources/Config/art.json`

```json
"fonts":   { "body": "Fonts/LilitaOne", "title": "Fonts/PottaOne" },
"skin":    { "btn_red": { "path": "UI/btn_red", "border": [46, 44, 46, 44] } },
"soldier": { "icon": "Troops/soldier", "frameScale": 2,
             "clips": { "idle": { "path": "Troops/soldier_idle", "fps": 11 } } },
"map":     { "background": "Map/background" },
"cards":   { "art": "Cards/Art", "back": "Cards/back", "suits": "Cards/Suits" }
```

- **Paths:** written relative to `Resources/`, with no file extension. Every image under `Resources/` is imported as a sprite automatically.
- **`skin`:** every button and panel the interface uses. `border` is the 9-slice edge in pixels, in the order left, bottom, right, top. It's the part that does not stretch when the piece is resized. Use `[0,0,0,0]` for images that must not stretch.
- **Soldier:**
  - Each animation in `clips` (idle, walk, victory, death) is a single row of square frames. The frame count is the width divided by the height.
  - `fps` sets the playback speed.
  - `frameScale` is how much larger the frame is than the figure inside it. The current frames leave room for the spear and the fall, so the value is 2.
- **Map:** drop an image at `Resources/Map/background.png` to replace the drawn map.
- **Cards:**
  - Main art: `Resources/Cards/Art/{card}.png`, for example `Ah.png` or `Td.png`. `default.png` covers any card without its own image. The art area is roughly 1:2.
  - Card back: `Resources/Cards/back.png`.
  - Suit icons: `Resources/Cards/Suits/{h|d|c|s}.png`.

## Client settings: `Resources/Config/game.json`

| Key | Meaning |
|---|---|
| `serverUrl` | The default server address. The login screen can still change it per device. |
| `wordingFile` | Which wording file to use, without `.json`. |
| `maxSquadFigures` | The most soldier figures drawn for one amount. |
| `showdownHoldSeconds` | How long the ranking board stays up after a showdown. |
| `marchSeconds` | How long soldiers take to march to the winner. |
| `winnerCrownSeconds` | How long the winner's crown shows. |

If you make the end-of-hand animations longer, raise `showdownDelayMs` in `server/config.json` too, so the next hand doesn't start while they're still playing.

## Server settings: `server/config.json`

| Key | Meaning |
|---|---|
| `startingBalance` | Soldiers a new account gets. |
| `refill` | `threshold`: a player below this can claim a refill. `topUpTo`: the balance after a refill. `cooldownMs`: the time between refills. |
| `maxSeats` | Seats per table, from 2 to 8. |
| `turnTimeMs` | Time per turn. |
| `nextHandDelayMs` | Pause before the next hand after a fold win. |
| `showdownDelayMs` | Pause before the next hand after a showdown. |
| `disconnectGraceMs` | How long a disconnected player keeps their seat. |
| `idleTimeouts` | Missed turns in a row before an idle player goes back to the lobby. |
| `readyTimeoutMs` | Once 2 or more players are ready, the time the others get to press Ready. |
| `privateRooms` | Limits for private rooms with custom stakes: `minBuyIn`, `maxBuyIn`, `minStackBigBlinds` (the buy-in must be at least this many big blinds), and `codeLength` (4 to 12 characters). |
| `levels` | The rooms, in lobby order. Each needs `id`, `name`, `buyIn`, `smallBlind` and `bigBlind`, all as whole numbers. |

Keys starting with `_` are comments. The server checks the file at startup and refuses to start with a clear message if something can't work, such as a small blind bigger than the big blind.
