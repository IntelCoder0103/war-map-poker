# War-Map Poker — Unity client

Texas Hold'em on an old parchment war map. Chips are soldiers. Android, landscape.
The server (Node, `../server`) is the only authority; this client only renders snapshots and sends the player's choices.
The contract is `../docs/protocol.md`.

## Requirements
- Unity **2022.3 LTS** (made for 2022.3.50f1; any 2022.3.x should work) with **Android Build Support** (SDK/NDK + OpenJDK) from Unity Hub.
- Internet access the first time you open the project: packages are resolved from the Unity registry and from GitHub
  (`com.itisnajim.socketiounity`, a git package, so **git must be installed and on PATH**).

## First open
1. Unity Hub → *Add project from disk* → select this `client/` folder → open with 2022.3.
2. Wait for package import and the script compile to finish.
   (`ProjectSettings/` only contains `ProjectVersion.txt`; Unity creates the rest with defaults.)
3. Menu **WarPoker → Setup Project**. It:
   - creates `Assets/_Project/Scenes/Main.unity` (a camera + a `Bootstrap` object) and puts it in Build Settings,
   - sets Android player settings: company `WarPoker`, product `War-Map Poker`, package `com.warpoker.game`,
     IL2CPP, ARM64 only, min API 24, landscape only, internet permission, *Allow downloads over HTTP* = Always (dev).
   The setup also runs automatically once if `Main.unity` doesn't exist yet. Running it again is safe.
4. Menu **WarPoker → Switch Platform To Android** (or File → Build Settings → Android → Switch Platform).
5. Open `Main.unity` and press **Play**.

## Architecture (short)
- **One scene.** `Bootstrap` creates the `App` singleton, which builds *all* UI from code (uGUI, legacy `Text`
  with the built-in `LegacyRuntime.ttf`, no TextMeshPro, no prefabs) and switches three panels:
  **Login → Lobby → Game**. There are no separate scenes. If the Bootstrap object is missing, a
  `[RuntimeInitializeOnLoadMethod]` fallback starts the app anyway.
- Canvas: Screen Space Overlay, CanvasScaler 1920×1080, match 0.5, safe-area aware. The game board is a fixed
  1920×1080 design rect scaled to fit any aspect ratio.
- `Scripts/Core` (assembly **WarPoker.Core**, no Unity/3rd-party deps): troop breakdown, card parsing, seat rotation,
  refill rule, quick-raise math, all UI strings. Covered by EditMode tests.
- `Scripts/Net`: `ApiClient` (UnityWebRequest + Newtonsoft), `SocketClient` (SocketIOUnity), DTOs, `MainThread` dispatcher.
  All socket callbacks and acks are posted to the main thread through one queue, so server order is kept.
- `Scripts/Game`: `GameScreen` keeps an event queue. `table:event` → play the animation for `type` from `data`, then render
  `state`. `table:state` → cancel animations, clear the queue, render. The queue speeds up when it grows (≥2: 1.7×, ≥5: 3×)
  and skips animations entirely when more than 8 events are waiting. Gaps in `seq` trigger `table:sync`.
- `Scripts/FX`: own coroutine tween helper (`Tween`, `Ease`) instead of DOTween, and `FxLayer` (pooled Images for
  marching troops, flying cards, UI "particles", flash/shake, victory banner).

## Server address
- Default: `http://localhost:3000` in the Editor, `http://10.0.2.2:3000` on devices (the Android emulator's alias for
  your PC).
- The login screen has an editable **Server** field (saved in PlayerPrefs). For a real phone use your PC's LAN IP,
  e.g. `http://192.168.1.20:3000`, or a public address for 4G.
- Optional: ship a different default with `Assets/_Project/Resources/config.json`:
  `{ "serverUrl": "http://192.168.1.20:3000" }`.

### Cleartext HTTP (dev only)
Android blocks plain `http://` by default. The setup script sets *Player Settings → Other → Allow downloads over HTTP*
to **Always allowed** so REST works against a dev server. The Socket.IO WebSocket (`ws://`) uses .NET sockets and is not
affected by that setting. **Production must use HTTPS/WSS** (`https://…`; SocketIOUnity then uses `wss://`) and the
option should be set back to *Not allowed*.

## Build the APK
File → Build Settings → Android → *Build* (or *Build And Run* with a phone connected, USB debugging on).
Install manually with `adb install -r WarMapPoker.apk`.

## Testing with two (or more) clients
- **ParrelSync** (https://github.com/VeriorPies/ParrelSync): add it via Package Manager → *Add package from git URL*
  `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`, create a clone, open it in a 2nd Editor, log in with
  a different user in each. (PlayerPrefs are per clone project, so tokens don't collide.)
- Or Editor + phone/emulator: start the server, use your PC's LAN IP in the phone's Server field.
- Only one socket per user: logging in with the same user elsewhere shows "opened on another device" on the first client.
- Reconnect test: toggle airplane mode mid-hand. A "Connection lost - reconnecting..." banner appears; on reconnect the
  server re-sends `table:state` (and the client also sends `table:sync`). A watchdog recreates the socket if no server
  ping arrives for 60 s, and on app resume after >30 s of silence.

## EditMode tests
Window → General → Test Runner → EditMode → Run All. Tests live in `Assets/_Project/Tests/Editor`
(assembly `WarPoker.Tests.Editor`, needs `com.unity.test-framework`, already in the manifest).

## Art slots (drop-in, no code changes)
Everything has a procedural placeholder. To replace it, put a sprite at the path below (import as
**Texture Type: Sprite (2D and UI)**; the file name without extension is what matters).

| What | Path (under `Assets/_Project/Resources/`) | Recommended size |
|---|---|---|
| Card main art, per card | `Cards/Art/{code}.png` — e.g. `Cards/Art/Ah.png`, `Cards/Art/Td.png`, `Cards/Art/2c.png` (rank `23456789TJQKA` + suit `cdhs`) | 256×360 px (portrait, ~0.7 aspect; shown with preserve-aspect in the area right of the rank strip) |
| Card main art, fallback for all cards | `Cards/Art/default.png` | 256×360 px |
| Card back | `Cards/back.png` | 256×360 px (fills the whole card; include your own rounded corners/border) |
| Suit icons | `Cards/Suits/c.png`, `d.png`, `h.png`, `s.png` | 64×64 px, **white** silhouette on transparent (tinted red/black by code) |
| Troop units | `Troops/spearman.png`, `archer.png`, `ranger.png`, `cavalry.png`, `knight.png` | 128×128 px, transparent, facing right (mirrored when marching left) |
| Map background | `Map/background.png` | 1920×1080 px or larger (stretched to the screen) |
| Compass rose | `Map/compass.png` | 256×256 px, transparent |

Lookup order for card art: `Cards/Art/{code}` → `Cards/Art/default` → procedural heraldic shield in the suit colour.
Card layout: white rounded body; a narrow strip on the **left** with the rank on top and the suit icon below; the large
main area shows the art.

Soldiers (display only): one soldier type, sprite `Resources/Troops/soldier` (procedural fallback). An amount is drawn
as a squad of 1–5 identical figures, one more per digit (1–9 → 1, 10–99 → 2, … 10,000+ → 5), next to the number.

## Notes / deviations
- Built-in render pipeline (no URP): the client is pure uGUI, so URP 2D wasn't needed.
- No DOTween (not cleanly on UPM): own `Tween` helper. No ParticleSystem (doesn't render in an overlay canvas):
  UI-image particles.
- No `CardArtSet` ScriptableObject asset (can't be authored without the Editor): art is found by the Resources naming
  convention above.
- Bets of the current street are shown at a "bet spot" in front of each camp; on `street` they march into the pot.

## Customizing
Wording, art, client settings and server game settings live in config files, so most changes need no code. See [docs/customizing.md](../docs/customizing.md).
