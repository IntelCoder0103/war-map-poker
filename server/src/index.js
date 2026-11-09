const { startServer } = require('./app');

startServer()
  .then(({ port }) => console.log(`War-Map Poker server listening on :${port}`))
  .catch((e) => {
    console.error('failed to start', e);
    process.exit(1);
  });
