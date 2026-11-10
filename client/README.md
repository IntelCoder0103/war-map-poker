# War-Map Poker — Android app (Unity)

Texas Hold'em on an old war map, where you bet soldiers instead of chips. Android only, in landscape. The app is built with **Unity 2022.1.22f1**.

The app only shows what the server sends and passes on what the player chose. The server, in `../server`, runs the game.

- **Server guide:** [../server/README.md](../server/README.md)
- **Message format:** [../docs/protocol.md](../docs/protocol.md)
- **Changing wording, art and settings without code:** [../docs/customizing.md](../docs/customizing.md)

---

## Contents
1. [Try it on a phone (no Unity needed)](#1-try-it-on-a-phone-no-unity-needed)
2. [Quick start for developers](#2-quick-start-for-developers)
3. [The WarPoker menu](#3-the-warpoker-menu)
4. [Building the APK](#4-building-the-apk)
5. [Server address](#5-server-address)
6. [Config files](#6-config-files)
7. [Art you can replace](#7-art-you-can-replace)
8. [Sound effects](#8-sound-effects)
9. [How the code is organised](#9-how-the-code-is-organised)
10. [Testing](#10-testing)
11. [Before a store release](#11-before-a-store-release)
12. [Troubleshooting](#12-troubleshooting)
13. [Unity 2022.1 on newer Linux](#13-unity-20221-on-newer-linux)

---

## 1. Try it on a phone (no Unity needed)

1. Get the APK: [../demo/WarPoker-demo.apk](../demo/WarPoker-demo.apk), or `Builds/Android/WarPoker.apk` after a build.
2. Install it.
   - **Over USB:** turn on USB debugging on the phone, then run `adb install -r WarPoker-demo.apk`.
   - **Without a computer:** copy the file to the phone, open it, and allow "Install unknown apps" for your file manager.
3. Start a server (see the [server quick start](../server/README.md#1-quick-start-with-docker)), or use the demo server.
4. In the app, check the **Server** field on the login screen (see [section 5](#5-server-address)). Then press **Register**, pick a room and press **March!**
5. Need more players? Start bots on the server: `docker compose run --rm bots`.

The phone needs Android 7.0 (API 24) or newer, on a 64-bit (ARM64) processor.

---

## 2. Quick start for developers

### Install
- **Unity Hub** with **Unity 2022.1.22f1**, plus the module **Android Build Support**. Tick **OpenJDK** and **Android SDK & NDK Tools** under it.
- **git**, on your PATH. Unity fetches the socket library from GitHub (`com.itisnajim.socketiounity`).
- Internet access the first time you open the project, so Unity can download packages.

Use exactly 2022.1.22f1. A newer Unity opens the project but upgrades it, so don't commit the upgraded `ProjectSettings/ProjectVersion.txt` or `Packages/packages-lock.json`.

### Open and play in the Editor
1. Start a server on this computer: `cd server && docker compose up -d` (or `npm start`).
2. In Unity Hub, choose **Add → Add project from disk**, select this `client/` folder, and open it with 2022.1.22f1. The first import takes a few minutes.
3. Open `Assets/_Project/Scenes/Main.unity` and press **Play**.
4. On the login screen, set **Server** to `http://localhost:3000`, then register.
5. Optional: start bots so the table fills: `docker compose run --rm bots` in `server/`.

The field first shows `serverUrl` from `Resources/Config/game.json` (the demo server). Change it once to `http://localhost:3000`; the Editor remembers it. Before building for phones, check `serverUrl` (section 5).

### Build an APK
In the Unity menu bar, choose **WarPoker → Build Android APK**. The result is `Builds/Android/WarPoker.apk`. More options are in [section 4](#4-building-the-apk).

---

## 3. The WarPoker menu

All project tools are in the **WarPoker** menu in Unity's menu bar.

| Menu item | What it does |
|---|---|
| **Setup Project** | Creates `Main.unity` if it's missing, and applies the Android settings: package `com.warpoker.game`, IL2CPP, ARM64, min API 24, landscape, internet permission, plain HTTP allowed. It runs once on its own on a fresh checkout, and running it again is safe. |
| **Switch Platform To Android** | Same as File → Build Settings → Android → Switch Platform. |
| **Build Android APK** | Builds `Builds/Android/WarPoker.apk`, signed with the debug key (fine for testing). |
| **Build Desktop (Linux)** | Builds `Builds/Linux/WarPoker.x86_64`, a desktop copy for quick tests. Android settings are left alone. |
| **Update Config Files** | Adds any missing keys to `Resources/Config/game.json`, `text.json` and `art.json`, with their default values. Your edits are kept. Run it after updates that add new text. |
| **Export Card Preview** | Saves `Previews/cards_preview.png` (all card faces) and `Previews/cards_mock.png` (a table mock-up). |
| **Export Map Preview** | Saves the drawn map and compass as PNGs in `Previews/`. Useful as a starting point for an artist. |
| **Render Troop Sprites** | Re-renders the soldier animations in `Resources/Troops/` from the Asset Store pack "Mini Legion Footman PBR HP Polyart". The pack isn't in the repo because its licence doesn't allow sharing it; import it from the Asset Store first (*My Assets*). Not needed unless you change the soldier. |

---

## 4. Building the APK

**From the Editor:** WarPoker → Build Android APK, or File → Build Settings → Android → Build. "Build And Run" also installs it on a connected phone.

**From the command line** (no window; good for build machines):

```bash
# Windows: "C:\Program Files\Unity\Hub\Editor\2022.1.22f1\Editor\Unity.exe"
# macOS:   /Applications/Unity/Hub/Editor/2022.1.22f1/Unity.app/Contents/MacOS/Unity
# Linux:   ~/Unity/Hub/Editor/2022.1.22f1/Editor/Unity
Unity -batchmode -quit -buildTarget Android -projectPath client \
      -executeMethod WarPoker.EditorTools.AndroidBuild.BuildApk -logFile build.log
```

- The APK is written to `client/Builds/Android/WarPoker.apk` (about 22 MB).
- A failed build exits with code 1; `build.log` says why. Search it for `error CS` (code errors) or `[WarPoker]`.
- Close any Editor that has the project open first. Unity can't open one project twice.

Other batch commands use the same pattern, with a different `-executeMethod`:

| `-executeMethod` | `-buildTarget` |
|---|---|
| `WarPoker.EditorTools.AndroidBuild.BuildApk` | `Android` |
| `WarPoker.EditorTools.DesktopBuild.BuildLinux` | `Linux64` |
| `WarPoker.EditorTools.ConfigFiles.Update` | (none) |

**Install it:**
- `adb install -r client/Builds/Android/WarPoker.apk` (`-r` keeps the app's saved login).
- Uninstall first if the phone has a build signed with a different key.

**Update the demo APK in the repo:** copy the new build to `demo/WarPoker-demo.apk` and commit.

---

## 5. Server address

The app connects to the address in the **Server** field on the login screen. The phone remembers it.

The field starts out with the first one of these that exists:
1. The address you typed earlier, saved on the phone.
2. `serverUrl` in `Assets/_Project/Resources/Config/game.json`. It's currently `http://95.217.62.59:3456`, the demo server. **Change this before building for your own server.**
3. In the Editor, `http://localhost:3000`. On a phone, `http://10.0.2.2:3000`, which is how the Android emulator reaches its host PC.

| Server runs on | Address |
|---|---|
| This PC; playing in the Editor | `http://localhost:3000` |
| This PC; Android emulator | `http://10.0.2.2:3000` |
| This PC; real phone on the same Wi-Fi | `http://<PC's LAN IP>:3000`, for example `http://192.168.1.20:3000` |
| An internet server | `http://<IP>:<port>`, or `https://your.domain` in production |

You can type the address without `http://`; the app adds it.

**Plain HTTP is allowed in development.** Setup Project sets *Player Settings → Other → Allow downloads over HTTP* to **Always allowed**. For release, use HTTPS (the app then uses secure WebSockets on its own) and set that option back to **Not allowed**.

---

## 6. Config files

They're in `Assets/_Project/Resources/Config/`. Edit them, then rebuild; you don't need to change code. Every key is optional and falls back to its default. The full key list is in [../docs/customizing.md](../docs/customizing.md).

| File | Holds |
|---|---|
| `game.json` | `serverUrl` (default server address), `showdownHoldSeconds` (how long the ranking board stays), `marchSeconds`, `winnerCrownSeconds`, `maxSquadFigures`, `wordingFile` |
| `text.json` | Every text players see: buttons, titles, messages, error texts, hand names. Translate the app by editing it, or by copying it and pointing `wordingFile` at the copy. |
| `art.json` | Where each image comes from: fonts, buttons and panels (with their 9-slice borders), the soldier animations, the map, the cards. You only need it when a new file's name or folder differs. |

If you make the end-of-hand animations longer, also raise `showdownDelayMs` in `server/config.json`. Otherwise the next hand starts while they're still playing.

---

## 7. Art you can replace

Drop a PNG at the same path, with the same name, and rebuild. No code changes are needed. Every image under `Resources/` is imported as a UI sprite automatically (`Editor/ArtImportRules.cs`). If an image is missing, the app draws a placeholder in code.

| What | Path under `Assets/_Project/Resources/` | Size and notes |
|---|---|---|
| Card picture, per card | `Cards/Art/{rank}{suit}.png`. Ranks `2 3 4 5 6 7 8 9 T J Q K A`, suits `c d h s`, for example `Ah.png`, `Td.png`. | 160×300 px. Both sides must be a multiple of 4 so phones can compress them. |
| Card picture for any card without its own | `Cards/Art/default.png` | 160×300 px |
| Card back | `Cards/back.png` | 256×360 px; fills the whole card, so include the rounded corners. |
| Suit icons | `Cards/Suits/c.png`, `d.png`, `h.png`, `s.png` | 64×64 px, **white** on transparent. The code tints them red or black. |
| Soldier | `Troops/soldier.png` (icon) and `soldier_idle/_walk/_victory/_death.png` | Animations are one row of square frames; frame rate and scale are in `art.json`. |
| Buttons, panels, ribbon, icons | `UI/*.png` | 9-slice borders are in `art.json`. |
| Fonts | `Fonts/LilitaOne.ttf` (body), `Fonts/PottaOne.ttf` (titles) | Set in `art.json` |
| Map background | `Map/background.png` | 1920×1080 or larger. Without it, the map is drawn in code. |
| Compass | `Map/compass.png` | 256×256, transparent |

**The card face:**
- A white card with a narrow strip on the left: the suit icon at the top and the rank at the bottom.
- A thin divider, then the picture.
- The showdown ranking board uses a compact card instead: a small suit in the corner and a big rank in the middle.

**The current card pictures are placeholders,** drawn by [Tools/make_card_art.py](Tools/make_card_art.py) from the game's own art:
- Each suit is an army in its own colour.
- 2 to 10 show that many soldiers.
- J, Q and K show a soldier with a star, a small crown or a big crown.
- A shows a shield.

To redraw them after editing the script, run `python3 client/Tools/make_card_art.py`. It needs Pillow and numpy.

Third-party art, fonts and sounds, with their licences, are listed in [CREDITS.md](CREDITS.md).

---

## 8. Sound effects

The clips are in `Assets/_Project/Resources/Audio/`, named `{sound}_{n}.ogg`. Each time a sound plays, the app picks one of its variants at random.
- **More variety:** add another variant, for example `deal_5.ogg`.
- **A different sound:** overwrite the file.
- **Loudness:** each sound's volume is set in `Scripts/FX/Sfx.cs`.

A speaker button in the lobby, and next to Retreat at the table, turns sound on and off. The phone remembers the setting.

| Sound | Plays when |
|---|---|
| `click` | Any button |
| `shuffle`, `deal` | A hand starts: a shuffle, then a slide for each card dealt |
| `flip` | A card turns face up |
| `check`, `fold` | A player checks or folds |
| `march` | Soldiers march: bets, blinds, the pot going to the winner |
| `allin` | Someone goes all-in (sword clash) |
| `ready`, `join`, `leave` | A player presses Ready (sword drawn), sits down, or leaves |
| `turn`, `tick` | Your turn starts; each of your last 5 seconds |
| `coins`, `win`, `lose`, `fall` | The pot is won; you won; you lost a hand you played to the end; a busted player's soldiers fall |
| `notify` | An invite to a private room arrives |

---

## 9. How the code is organised

**One scene, everything built in code:**
- `Main.unity` holds a camera and a `Bootstrap` object. `Bootstrap` creates `App`, which builds all of the interface from code and switches between three screens: **Login → Lobby → Game**.
- The interface uses uGUI, with no prefabs and no TextMeshPro.
- The design size is 1920×1080. It scales to any screen and keeps clear of notches.

| Folder (`Assets/_Project/Scripts/`) | What's there |
|---|---|
| `App/` | `App` (screens, socket events, toasts), `Bootstrap`, `Session` (login token, saved for auto-login), `GameSettings` (reads `game.json`) |
| `Auth/` | Login and register screen, with the Server field |
| `Lobby/` | Balance and refill, the room grid (3×2 squares), private rooms (create, join by code, invites) |
| `Game/` | `GameScreen` (the table, event animations, showdown board), `SeatView` (a player's camp), `ActionBar` (Fold, Check/Call, Raise, All-in), `MapArt` (the map drawn in code) |
| `Cards/` | `CardView` (a card), `CardArt` (finds pictures, with drawn fallbacks) |
| `Troops/` | Soldier sprites and the squad shown for an amount |
| `FX/` | `Tween` (small animation helper), `FxLayer` (flying cards, marching soldiers, flashes), `SpriteAnim`, `Sfx` (sound) |
| `Net/` | `ApiClient` (REST), `SocketClient` (Socket.IO), `Dto` (message types), `ServerConfig` (server address), `MainThread` |
| `UI/` | `UIKit` (builds buttons, texts, panels), `UISkin` and `ArtCatalog` (art lookup), `Theme` (colours), `Modal` (dialogs), `SoundToggle` |
| `Core/` | Separate assembly with no Unity dependencies: wording (`Strings`), card codes, seat layout, raise maths, refill rule. Covered by tests. |
| `../Editor/` | The WarPoker menu tools (section 3) |

**How the table stays in sync with the server:**
- `table:event` messages go into a queue. Each one plays its animation, then the app shows the snapshot that came with it, which is the truth.
- `table:state` replaces everything at once (after joining or reconnecting).
- When the queue gets long, animations speed up, and they're skipped if the app falls far behind.
- A gap in event numbers makes the app ask the server for a fresh snapshot.
- If the connection drops, a banner shows and the app reconnects on its own; the server then sends the full table again.

---

## 10. Testing

**More than one player:**
- **Editor + phone:** run the server on your PC, and on the phone use your PC's LAN IP (section 5).
- **Bots:** `docker compose run --rm bots` in `server/` (see the [server guide](../server/README.md#7-bots-for-testing)).
- **Two Editors:** add ParrelSync (`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync` via Package Manager → Add package from git URL), open a clone, and log in as a different user in each.
- **Desktop build:** WarPoker → Build Desktop (Linux), then run several copies.
- One account can be connected once. Logging in elsewhere shows "opened on another device" on the first one.

**Reconnects:** turn airplane mode on mid-hand, then off. A "Connection lost" banner shows, then the table comes back as it was.

**Unit tests:** Window → General → Test Runner → EditMode → Run All. They're in `Assets/_Project/Tests/Editor` (wording, card codes, seat layout, raise maths, refill rule, soldier squad sizes).

---

## 11. Before a store release

- [ ] **Signing:** the builds are signed with Unity's debug key. Create your own keystore in Player Settings → Publishing Settings, and **keep it safe**: updates must be signed with the same key. Don't commit it; `*.keystore` and `*.jks` are git-ignored. Then set `useCustomKeystore = true` in `Editor/AndroidBuild.cs`, or build from File → Build Settings.
- [ ] **Version:** raise *Version* and *Bundle Version Code* in Player Settings for every upload. Setup Project starts them at 0.1.0 and 1.
- [ ] **Server:** set `serverUrl` in `game.json` to your HTTPS address, and set *Allow downloads over HTTP* to **Not allowed**.
- [ ] **Google Play** needs an `.aab` and a recent target API. Unity 2022.1 targets API 30 at most, and Play currently asks for newer, so a store release means moving to Unity 2022.3 LTS or later. The project opens there; test it fully afterwards.
- [ ] **Placeholder art:** swap in the real card pictures, map and card back (section 7).

---

## 12. Troubleshooting

| Symptom | Fix |
|---|---|
| "Can't reach the server" in the app | Check the Server field. On a phone, open `http://<address>/health` in the browser; it should show `{"ok":true}`. If not, check the IP, port and firewall (see the [server guide](../server/README.md#4-connecting-phones)). |
| Package errors on first open (`com.itisnajim.socketiounity`) | git isn't installed or isn't on the PATH. Install git, restart Unity Hub, reopen the project. |
| "Android Build Support is not installed" | Unity Hub → Installs → 2022.1.22f1 → Add modules → Android Build Support (with OpenJDK and SDK & NDK). |
| `adb install` fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | The phone has a build signed with another key. Run `adb uninstall com.warpoker.game` first. |
| After translating, some new text still shows in English | That text was added after your `text.json` was made. Run WarPoker → Update Config Files to add the missing keys (in English), then translate them. |
| The batch build says the project is already open | Close the Editor that has `client/` open. |
| The next hand starts before the showdown animation ends | Raise `showdownDelayMs` in `server/config.json`, or lower the timings in `game.json`. |

---

## 13. Unity 2022.1 on newer Linux

Windows and macOS need none of this. Unity 2022.1 is older than Ubuntu 23.10 and later (such as 24.04), so it needs four fixes there. The dev server already has them.

1. **libssl 1.1** for Unity's built-in compiler (error: "No usable version of libssl was found"). Copy `libssl.so.1.1` and `libcrypto.so.1.1` from the Ubuntu 20.04 package to `Editor/Data/libssl1.1/`, and start Unity with that folder on `LD_LIBRARY_PATH`. The `unity-2022.1` launcher in `~/.local/bin` does this.
2. **A bee_backend wrapper,** for the known hang at "Initial Asset Database Refresh" (Unity issue UUM-54803). Rename `Editor/Data/bee_backend` to `bee_backend_real`, and put a small script in its place that drops the `--stdin-canary` argument.
3. **OpenJDK 8** in `PlaybackEngines/AndroidPlayer/OpenJDK`, because 2022.1 rejects JDK 11 ("JDK not found"). Unity's own copy is `download_unity/open-jdk/open-jdk-linux-x64/jdk8u172-b11_….zip`.
4. **A JKS debug keystore.** JDK 8 can't read the PKCS12 `~/.android/debug.keystore` that newer tools write. Convert it with `keytool -importkeystore … -deststoretype JKS`; it keeps the same key.
