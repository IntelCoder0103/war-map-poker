# War-Map Poker

Texas Hold'em on an old war map, betting soldiers. Android (Unity 2022.1.22) + Node 16.2 + MongoDB 4.2.

- [docs/design.md](docs/design.md): what the game is and how it plays
- [docs/protocol.md](docs/protocol.md): the client/server message format
- [server/README.md](server/README.md): server guide (Docker and plain Node, offline, settings, bots, tests, production)
- [client/README.md](client/README.md): app guide (install, Unity, building the APK, server address, art, sound)

## Demo APK
[demo/WarPoker-demo.apk](demo/WarPoker-demo.apk): install on an Android phone (allow "install unknown apps").
It connects to the demo server at `http://95.217.62.59:3456`, so it only works while that server is running;
the server address can be changed on the login screen.

## Quick start
```bash
cd server
docker compose build && docker compose up -d   # MongoDB 4.2 + server on http://localhost:3000
docker compose run --rm bots                   # optional: 3 bots to play against
```
No Docker? `npm ci && npm start` with Node 16.2+ and MongoDB 4.2. Offline use, phones, settings and production
setup are in the **[server guide](server/README.md)**.

Then install the [demo APK](demo/WarPoker-demo.apk), or open `client/` in Unity 2022.1.22f1: see the
**[client guide](client/README.md)**.

## Customizing
Wording, art, client settings and server game settings live in config files, so most changes need no code. See [docs/customizing.md](docs/customizing.md).
