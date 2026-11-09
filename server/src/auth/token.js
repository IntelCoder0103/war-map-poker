const jwt = require('jsonwebtoken');
const config = require('../config');

function sign(user) {
  return jwt.sign({ sub: String(user._id), username: user.username }, config.jwtSecret, { expiresIn: config.jwtExpiresIn });
}

// Returns { userId, username } or null.
function verify(token) {
  try {
    const p = jwt.verify(token, config.jwtSecret);
    return { userId: p.sub, username: p.username };
  } catch (e) {
    return null;
  }
}

function requireAuth(req, res, next) {
  const m = /^Bearer (.+)$/.exec(req.headers.authorization || '');
  const auth = m && verify(m[1]);
  if (!auth) return res.status(401).json({ error: 'UNAUTHORIZED', message: 'Please log in again.' });
  req.auth = auth;
  next();
}

module.exports = { sign, verify, requireAuth };
