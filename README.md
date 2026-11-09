# War-Map Poker

Texas Hold'em on an old war map, betting soldiers. Android (Unity 2022.3) + Node 16 + MongoDB.

- [docs/design.md](docs/design.md): what the game is and how it plays
- [docs/protocol.md](docs/protocol.md): the client/server message format
- [server/README.md](server/README.md): run and test the server
- [client/README.md](client/README.md): open, run, and build the Unity app

## Quick start
```bash
cd server
docker compose up -d mongo
npm install && npm start                       # server on :3000
node tools/bots.js --count 3                   # optional: bots to play against
```
Then open `client/` in Unity 2022.3 and follow client/README.md.
