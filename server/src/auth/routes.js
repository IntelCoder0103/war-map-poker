const express = require('express');
const bcrypt = require('bcryptjs');
const User = require('../db/User');
const { publicUser } = require('../db/wallet');
const { sign } = require('./token');
const config = require('../config');

const router = express.Router();
const USERNAME = /^[A-Za-z0-9_]{3,16}$/;

function fail(res, status, error, message) {
  return res.status(status).json({ error, message });
}

router.post('/register', async (req, res, next) => {
  try {
    const { username, password } = req.body || {};
    if (typeof username !== 'string' || !USERNAME.test(username)) {
      return fail(res, 400, 'INVALID_USERNAME', 'Username must be 3-16 letters, numbers or _.');
    }
    if (typeof password !== 'string' || password.length < 6 || password.length > 64) {
      return fail(res, 400, 'INVALID_PASSWORD', 'Password must be 6-64 characters.');
    }
    const passwordHash = await bcrypt.hash(password, 10);
    let user;
    try {
      user = await User.create({
        username, usernameLower: username.toLowerCase(), passwordHash, balance: config.startingBalance,
      });
    } catch (e) {
      if (e.code === 11000) return fail(res, 409, 'USERNAME_TAKEN', 'That username is taken.');
      throw e;
    }
    res.json({ token: sign(user), user: publicUser(user) });
  } catch (e) {
    next(e);
  }
});

router.post('/login', async (req, res, next) => {
  try {
    const { username, password } = req.body || {};
    const user = typeof username === 'string' && await User.findOne({ usernameLower: username.toLowerCase() });
    const ok = user && typeof password === 'string' && await bcrypt.compare(password, user.passwordHash);
    if (!ok) return fail(res, 401, 'BAD_CREDENTIALS', 'Wrong username or password.');
    res.json({ token: sign(user), user: publicUser(user) });
  } catch (e) {
    next(e);
  }
});

module.exports = router;
