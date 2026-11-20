// What a new database starts with. Built-in emojis come from assets/emoji/manifest.json.
const fs = require('fs');
const path = require('path');

const EMOJI_DIR = path.join(__dirname, '..', '..', 'assets', 'emoji');

const MESSAGES = [
  'Good luck, commander!',
  'Well played!',
  'Nice hand!',
  'Charge!',
  'Retreat!',
  'All in!',
  'I smell a bluff…',
  'Hold the line!',
  'Hurry up, general!',
  'Lucky shot!',
  'Victory is mine!',
  "You'll pay for that!",
  'Good game',
  'Thanks!',
  'Oops…',
  'Reinforcements needed!',
];

const COOLDOWN_MS = 3000;

/** Built-in emojis whose picture exists: [{ id, char, label, on, file }]. */
function builtinEmojis() {
  let manifest;
  try {
    manifest = JSON.parse(fs.readFileSync(path.join(EMOJI_DIR, 'manifest.json'), 'utf8'));
  } catch (e) {
    return [];
  }
  return (manifest.emojis || [])
    .map((e) => ({ ...e, file: path.join(EMOJI_DIR, `${e.id}.png`) }))
    .filter((e) => fs.existsSync(e.file));
}

module.exports = { MESSAGES, COOLDOWN_MS, builtinEmojis };
