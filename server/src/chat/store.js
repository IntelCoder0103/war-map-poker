// The chat list: emojis and short messages players can send at the table.
// Everything goes through here so the cached list, the version number and the "changed" push stay in step.
const fs = require('fs');
const { ChatItem, ChatSettings } = require('./model');
const { toStrip, toStripAsync } = require('./images');
const defaults = require('./defaults');

const MAX_TEXT = 40;
const MAX_LABEL = 24;
const MAX_ENABLED = { emoji: 30, text: 20 }; // what fits the app's picker without scrolling

function fail(code, message, status = 400) {
  return Object.assign(new Error(message), { code, status });
}

function createStore({ onChange = () => {} } = {}) {
  let cache = null;          // { version, cooldownMs, items: [...], byId: Map }
  const images = new Map();  // id -> { png, rev }

  async function settings() {
    return (await ChatSettings.findById('chat')) || ChatSettings.create({ _id: 'chat', cooldownMs: defaults.COOLDOWN_MS });
  }

  async function changed() {
    const s = await ChatSettings.findByIdAndUpdate('chat', { $inc: { version: 1 } }, { new: true, upsert: true });
    cache = null;
    onChange(s.version);
    return s.version;
  }

  function publicItem(d) {
    return {
      id: String(d._id), kind: d.kind, text: d.text, label: d.label,
      image: d.kind === 'emoji' ? `/chat/img/${d._id}.png?r=${d.rev}` : null,
      frames: d.frames, fps: d.fps,
    };
  }

  async function nextOrder(kind) {
    const last = await ChatItem.findOne({ kind }).sort({ order: -1 });
    return last ? last.order + 1 : 0;
  }

  // A built-in's picture as stored: an animation strip as it is, a still through toStrip.
  function builtinArt(e) {
    const file = fs.readFileSync(e.file);
    return e.frames > 1 ? { png: file, frames: e.frames, fps: e.fps || 12 } : toStrip(file);
  }

  async function addBuiltin(e, enabled) {
    const art = builtinArt(e);
    return ChatItem.create({
      kind: 'emoji', label: e.label || e.id, builtin: e.id, image: art.png, frames: art.frames, fps: art.fps,
      order: await nextOrder('emoji'), enabled,
    });
  }

  function cleanText(text, max, code) {
    const t = String(text == null ? '' : text).replace(/\s+/g, ' ').trim();
    if (!t || t.length > max) throw fail(code, `Use 1 to ${max} characters.`);
    return t;
  }

  async function checkEnabledRoom(kind) {
    const n = await ChatItem.countDocuments({ kind, enabled: true });
    if (n >= MAX_ENABLED[kind]) {
      throw fail('TOO_MANY_ENABLED', `At most ${MAX_ENABLED[kind]} ${kind === 'emoji' ? 'emojis' : 'messages'} can be switched on (that's what fits in the app). Switch one off first.`);
    }
  }

  const store = {
    MAX_TEXT, MAX_LABEL, MAX_ENABLED,

    /**
     * First start: fills an empty database with the built-in emojis and messages.
     * Later starts: built-ins whose picture changed in a new version (say, a still that became an
     * animation) get the new one; their name, on/off and order stay as the admin left them.
     */
    async init() {
      const existing = await ChatSettings.findById('chat');
      if (existing) return store.syncBuiltins();
      await ChatSettings.create({ _id: 'chat', cooldownMs: defaults.COOLDOWN_MS, version: 1 });
      if (await ChatItem.countDocuments()) return;
      for (const e of defaults.builtinEmojis()) await addBuiltin(e, !!e.on);
      await ChatItem.insertMany(defaults.MESSAGES.map((text, i) => ({ kind: 'text', text, order: i, enabled: true })));
    },

    /** What the app gets: switched-on items only, in order. */
    async catalog() {
      if (cache) return cache;
      const s = await settings();
      const docs = await ChatItem.find({ enabled: true }).sort({ kind: 1, order: 1 });
      const items = docs.map(publicItem);
      cache = { version: s.version, cooldownMs: s.cooldownMs, items, byId: new Map(items.map((i) => [i.id, i])) };
      return cache;
    },

    async enabledItem(id) {
      const c = await store.catalog();
      return c.byId.get(String(id)) || null;
    },

    async image(id) {
      const hit = images.get(String(id));
      if (hit) return hit;
      let doc;
      try { doc = await ChatItem.findById(id).select('+image rev kind'); } catch (e) { return null; }
      if (!doc || doc.kind !== 'emoji' || !doc.image) return null;
      const entry = { png: doc.image, rev: doc.rev };
      images.set(String(id), entry);
      return entry;
    },

    /** Everything, including switched-off items, for the admin page. */
    async adminList() {
      const s = await settings();
      const docs = await ChatItem.find().sort({ kind: 1, order: 1 });
      const have = new Set(docs.map((d) => d.builtin).filter(Boolean));
      const missingBuiltins = defaults.builtinEmojis().filter((e) => !have.has(e.id)).length;
      return {
        version: s.version, cooldownMs: s.cooldownMs, maxEnabled: MAX_ENABLED, maxText: MAX_TEXT,
        missingBuiltins,
        items: docs.map((d) => ({ ...publicItem(d), enabled: d.enabled, builtin: d.builtin })),
      };
    },

    async addText(text) {
      const t = cleanText(text, MAX_TEXT, 'BAD_TEXT');
      await checkEnabledRoom('text');
      const doc = await ChatItem.create({ kind: 'text', text: t, order: await nextOrder('text'), enabled: true });
      await changed();
      return publicItem(doc);
    },

    async addEmoji(label, data) {
      const l = cleanText(label || 'Emoji', MAX_LABEL, 'BAD_LABEL');
      const art = await toStripAsync(data);
      const enabled = (await ChatItem.countDocuments({ kind: 'emoji', enabled: true })) < MAX_ENABLED.emoji;
      const doc = await ChatItem.create({
        kind: 'emoji', label: l, image: art.png, frames: art.frames, fps: art.fps,
        order: await nextOrder('emoji'), enabled,
      });
      await changed();
      return { ...publicItem(doc), enabled };
    },

    async update(id, { text, label, enabled } = {}) {
      let doc;
      try { doc = await ChatItem.findById(id); } catch (e) { doc = null; }
      if (!doc) throw fail('NOT_FOUND', 'That item no longer exists.', 404);
      if (text !== undefined && doc.kind === 'text') doc.text = cleanText(text, MAX_TEXT, 'BAD_TEXT');
      if (label !== undefined && doc.kind === 'emoji') doc.label = cleanText(label, MAX_LABEL, 'BAD_LABEL');
      if (enabled !== undefined && !!enabled !== doc.enabled) {
        if (enabled) await checkEnabledRoom(doc.kind);
        doc.enabled = !!enabled;
      }
      await doc.save();
      await changed();
      return publicItem(doc);
    },

    async remove(id) {
      let res;
      try { res = await ChatItem.deleteOne({ _id: id }); } catch (e) { res = { deletedCount: 0 }; }
      if (!res.deletedCount) throw fail('NOT_FOUND', 'That item no longer exists.', 404);
      images.delete(String(id));
      await changed();
    },

    /** Sets the order of the given items (one kind at a time is fine): ids[0] comes first. */
    async reorder(ids) {
      if (!Array.isArray(ids) || !ids.length) throw fail('BAD_ORDER', 'Send the ids in their new order.');
      await ChatItem.bulkWrite(ids.map((id, i) => ({ updateOne: { filter: { _id: id }, update: { $set: { order: i } } } })));
      await changed();
    },

    async setCooldown(ms) {
      const v = Number(ms);
      if (!Number.isInteger(v) || v < 500 || v > 60000) throw fail('BAD_COOLDOWN', 'The cooldown must be 500 to 60000 ms.');
      await ChatSettings.updateOne({ _id: 'chat' }, { $set: { cooldownMs: v } }, { upsert: true });
      await changed();
    },

    /** Updates built-in pictures that differ from the ones shipped. Returns how many changed. */
    async syncBuiltins() {
      const shipped = new Map(defaults.builtinEmojis().map((e) => [e.id, e]));
      const docs = await ChatItem.find({ builtin: { $ne: null } }).select('+image');
      let updated = 0;
      for (const d of docs) {
        const e = shipped.get(d.builtin);
        if (!e) continue;
        const art = builtinArt(e);
        if (d.image && Buffer.from(d.image).equals(art.png) && d.frames === art.frames && d.fps === art.fps) continue;
        d.image = art.png;
        d.frames = art.frames;
        d.fps = art.fps;
        d.rev = (d.rev || 1) + 1; // a new URL, so apps don't keep the old picture
        await d.save();
        images.delete(String(d._id));
        updated++;
      }
      if (updated) await changed();
      return updated;
    },

    /** Adds back built-in emojis that were deleted, switched off (they wait in the library). */
    async restoreBuiltins() {
      const have = new Set((await ChatItem.find({ builtin: { $ne: null } })).map((d) => d.builtin));
      let added = 0;
      for (const e of defaults.builtinEmojis()) {
        if (have.has(e.id)) continue;
        await addBuiltin(e, false);
        added++;
      }
      if (added) await changed();
      return added;
    },
  };
  return store;
}

module.exports = { createStore, MAX_ENABLED, MAX_TEXT };
