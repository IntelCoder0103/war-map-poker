# War-Map Poker

Texas Hold'em on an old war map, betting soldiers. Android (Unity 2022.1.22) + Node 16.2 + MongoDB 4.2.

- [docs/design.md](docs/design.md): what the game is and how it plays
- [docs/protocol.md](docs/protocol.md): the client/server message format
- [server/README.md](server/README.md): run and test the server
- [client/README.md](client/README.md): open, run, and build the Unity app

## Demo APK
[demo/WarPoker-demo.apk](demo/WarPoker-demo.apk): install on an Android phone (allow "install unknown apps").
It connects to the demo server at `http://95.217.62.59:3456`, so it only works while that server is running;
the server address can be changed on the login screen.

## Quick start
```bash
cd server
docker compose up -d mongo
npm install && npm start                       # server on :3000
node tools/bots.js --count 3                   # optional: bots to play against
```
Then open `client/` in Unity 2022.1.22f1 and follow client/README.md.

## Customizing
Wording, art, client settings and server game settings live in config files, so most changes need no code. See [docs/customizing.md](docs/customizing.md).
