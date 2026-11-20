// Chat items players can send at the table: emojis (pictures) and short messages.
// The admin page edits them; the app downloads the list from GET /chat.
const mongoose = require('mongoose');

const chatItemSchema = new mongoose.Schema({
  kind: { type: String, enum: ['emoji', 'text'], required: true },
  text: { type: String, default: '' },        // the message (kind "text")
  label: { type: String, default: '' },       // a short name for an emoji, shown on the admin page
  builtin: { type: String, default: null },   // id from assets/emoji/manifest.json, for built-in emojis
  image: { type: Buffer, select: false },     // PNG: one square, or a strip of square frames
  frames: { type: Number, default: 1 },
  fps: { type: Number, default: 0 },
  rev: { type: Number, default: 1 },          // bumped when the picture changes (cache busting)
  order: { type: Number, default: 0 },
  enabled: { type: Boolean, default: true },
}, { timestamps: true });

// One document holding the chat settings and a version that changes with every edit,
// so the app knows when to download the list again.
const chatSettingsSchema = new mongoose.Schema({
  _id: { type: String, default: 'chat' },
  cooldownMs: { type: Number, default: 3000 },
  version: { type: Number, default: 1 },
});

module.exports = {
  ChatItem: mongoose.models.ChatItem || mongoose.model('ChatItem', chatItemSchema),
  ChatSettings: mongoose.models.ChatSettings || mongoose.model('ChatSettings', chatSettingsSchema),
};
