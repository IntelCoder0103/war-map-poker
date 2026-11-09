const mongoose = require('mongoose');

const userSchema = new mongoose.Schema({
  username: { type: String, required: true },
  usernameLower: { type: String, required: true, unique: true },
  passwordHash: { type: String, required: true },
  balance: { type: Number, required: true, min: 0 },
  lastRefillAt: { type: Date, default: null },
  // Soldiers currently sitting at a table. Kept in the database so a server crash
  // can give them back (see wallet.recover).
  atTable: { type: Boolean, default: false },
  tableStack: { type: Number, default: 0 },
  // Id of the current sitting. Saves and cash-outs must match it, so a late write
  // from an earlier table can never change a newer sitting.
  tableSession: { type: String, default: null },
}, { timestamps: true });

module.exports = mongoose.models.User || mongoose.model('User', userSchema);
