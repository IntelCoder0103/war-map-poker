const express = require('express');
const User = require('../db/User');
const wallet = require('../db/wallet');
const { requireAuth } = require('../auth/token');

const router = express.Router();

router.get('/', requireAuth, async (req, res, next) => {
  try {
    const user = await User.findById(req.auth.userId);
    if (!user) return res.status(401).json({ error: 'UNAUTHORIZED', message: 'Please log in again.' });
    res.json({ user: wallet.publicUser(user) });
  } catch (e) {
    next(e);
  }
});

router.post('/refill', requireAuth, async (req, res, next) => {
  try {
    res.json({ user: await wallet.refill(req.auth.userId) });
  } catch (e) {
    if (e.code === 'REFILL_NOT_AVAILABLE') {
      return res.status(400).json({ error: e.code, message: 'Refill is not available yet.' });
    }
    next(e);
  }
});

module.exports = router;
