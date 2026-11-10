# War-Map Poker server image. Dependencies are installed at build time, so once the image is
# built the server (and the tests) run with no internet connection.
#
#   docker compose build            # once, online
#   docker compose up -d            # server on :3000 plus MongoDB 4.2

# "dev": all dependencies (jest, socket.io-client), used by the test and bots services.
FROM node:16.2.0-alpine AS dev
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci && npm cache clean --force
COPY . .

# "app": what runs in production: runtime dependencies and the server code only.
FROM node:16.2.0-alpine AS app
WORKDIR /app
ENV NODE_ENV=production PORT=3000
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY src ./src
COPY config.json ./
USER node
EXPOSE 3000
CMD ["node", "src/index.js"]
