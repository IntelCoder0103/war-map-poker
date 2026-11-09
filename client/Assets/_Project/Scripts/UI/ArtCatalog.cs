using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>
    /// Where every piece of art comes from, in one place. Paths are Resources paths (no extension).
    /// Defaults are below; Resources/Config/art.json overrides any of them, so swapping art means
    /// dropping new images into Resources and (if the names differ) editing art.json.
    ///
    /// art.json layout (every part optional):
    /// {
    ///   "fonts":   { "body": "Fonts/LilitaOne", "title": "Fonts/PottaOne" },
    ///   "skin":    { "btn_red": { "path": "UI/btn_red", "border": [46, 44, 46, 44] }, ... },
    ///   "soldier": { "icon": "Troops/soldier", "frameScale": 2,
    ///                "clips": { "idle": { "path": "Troops/soldier_idle", "fps": 11 }, ... } },
    ///   "map":     { "background": "Map/background", "compass": "Map/compass" },
    ///   "cards":   { "art": "Cards/Art", "back": "Cards/back", "suits": "Cards/Suits" }
    /// }
    /// Skin borders are the 9-slice insets in source pixels: left, bottom, right, top.
    /// </summary>
    public static class ArtCatalog
    {
        public struct SkinEntry
        {
            public string Path;
            public Vector4 Border;
            public SkinEntry(string path, Vector4 border) { Path = path; Border = border; }
        }

        public struct Clip
        {
            public string Path;
            public float Fps;
            public Clip(string path, float fps) { Path = path; Fps = fps; }
        }

        // ---------------- defaults ----------------

        static Vector4 B(float l, float b, float r, float t) { return new Vector4(l, b, r, t); }

        static readonly Dictionary<string, SkinEntry> DefaultSkin = new Dictionary<string, SkinEntry>
        {
            { "btn_red", new SkinEntry("UI/btn_red", B(46, 44, 46, 44)) },
            { "btn_red_pressed", new SkinEntry("UI/btn_red_pressed", B(46, 44, 46, 44)) },
            { "btn_green", new SkinEntry("UI/btn_green", B(46, 44, 46, 44)) },
            { "btn_green_pressed", new SkinEntry("UI/btn_green_pressed", B(46, 44, 46, 44)) },
            { "btn_gold", new SkinEntry("UI/btn_gold", B(46, 44, 46, 44)) },
            { "btn_gold_pressed", new SkinEntry("UI/btn_gold_pressed", B(46, 44, 46, 44)) },
            { "btn_blue", new SkinEntry("UI/btn_blue", B(46, 44, 46, 44)) },
            { "btn_blue_pressed", new SkinEntry("UI/btn_blue_pressed", B(46, 44, 46, 44)) },
            { "btn_disabled", new SkinEntry("UI/btn_disabled", B(46, 44, 46, 44)) },
            { "btn_wood", new SkinEntry("UI/btn_wood", B(30, 30, 30, 30)) },
            { "btn_wood_pressed", new SkinEntry("UI/btn_wood_pressed", B(30, 30, 30, 30)) },
            { "btn_wood_disabled", new SkinEntry("UI/btn_wood_disabled", B(30, 30, 30, 30)) },
            { "panel_window", new SkinEntry("UI/panel_window", B(110, 110, 110, 110)) },
            { "panel_wide", new SkinEntry("UI/panel_wide", B(80, 80, 80, 80)) },
            { "panel_gold", new SkinEntry("UI/panel_gold", B(14, 14, 14, 14)) },
            { "plank", new SkinEntry("UI/plank", B(24, 24, 24, 24)) },
            { "ribbon", new SkinEntry("UI/ribbon", B(150, 0, 150, 0)) },
            { "bar_frame", new SkinEntry("UI/bar_frame", B(92, 24, 24, 24)) },
            { "bar_fill", new SkinEntry("UI/bar_fill", B(12, 12, 12, 12)) },
            // not stretched (border 0)
            { "round_gold", new SkinEntry("UI/round_gold", Vector4.zero) },
            { "round_wood", new SkinEntry("UI/round_wood", Vector4.zero) },
            { "crown", new SkinEntry("UI/crown", Vector4.zero) },
            { "sunburst", new SkinEntry("UI/sunburst", Vector4.zero) },
            { "star", new SkinEntry("UI/star", Vector4.zero) },
            { "coin", new SkinEntry("UI/coin", Vector4.zero) },
        };

        static readonly Dictionary<string, Clip> DefaultClips = new Dictionary<string, Clip>
        {
            { "idle", new Clip("Troops/soldier_idle", 11f) },
            { "walk", new Clip("Troops/soldier_walk", 12f) },
            { "victory", new Clip("Troops/soldier_victory", 9f) },
            { "death", new Clip("Troops/soldier_death", 14f) },
        };

        static readonly Dictionary<string, string> DefaultPaths = new Dictionary<string, string>
        {
            { "fonts.body", "Fonts/LilitaOne" },
            { "fonts.title", "Fonts/PottaOne" },
            { "soldier.icon", "Troops/soldier" },
            { "map.background", "Map/background" },
            { "map.compass", "Map/compass" },
            { "cards.art", "Cards/Art" },
            { "cards.back", "Cards/back" },
            { "cards.suits", "Cards/Suits" },
        };

        const float DefaultFrameScale = 2f;

        // ---------------- loaded values ----------------

        static bool _loaded;
        static readonly Dictionary<string, SkinEntry> Skin = new Dictionary<string, SkinEntry>();
        static readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        static readonly Dictionary<string, string> Paths = new Dictionary<string, string>();
        static float _frameScale = DefaultFrameScale;

        /// <summary>Reads Resources/Config/art.json (once). Safe to call many times.</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var kv in DefaultSkin) Skin[kv.Key] = kv.Value;
            foreach (var kv in DefaultClips) Clips[kv.Key] = kv.Value;
            foreach (var kv in DefaultPaths) Paths[kv.Key] = kv.Value;

            var asset = Resources.Load<TextAsset>("Config/art");
            if (asset == null) return;
            try
            {
                var root = JObject.Parse(asset.text);
                ReadPaths(root, "fonts");
                ReadPaths(root, "map");
                ReadPaths(root, "cards");
                var skin = root["skin"] as JObject;
                if (skin != null)
                    foreach (var p in skin.Properties())
                    {
                        SkinEntry e;
                        Skin.TryGetValue(p.Name, out e);
                        if (string.IsNullOrEmpty(e.Path)) e.Path = "UI/" + p.Name;
                        var o = p.Value as JObject;
                        if (o != null)
                        {
                            if (o["path"] != null) e.Path = (string)o["path"];
                            var b = o["border"] as JArray;
                            if (b != null && b.Count == 4) e.Border = B((float)b[0], (float)b[1], (float)b[2], (float)b[3]);
                        }
                        else if (p.Value.Type == JTokenType.String) e.Path = (string)p.Value;
                        Skin[p.Name] = e;
                    }
                var soldier = root["soldier"] as JObject;
                if (soldier != null)
                {
                    if (soldier["icon"] != null) Paths["soldier.icon"] = (string)soldier["icon"];
                    if (soldier["frameScale"] != null) _frameScale = Mathf.Max(0.1f, (float)soldier["frameScale"]);
                    var clips = soldier["clips"] as JObject;
                    if (clips != null)
                        foreach (var p in clips.Properties())
                        {
                            Clip c;
                            Clips.TryGetValue(p.Name, out c);
                            var o = p.Value as JObject;
                            if (o == null) continue;
                            if (o["path"] != null) c.Path = (string)o["path"];
                            if (o["fps"] != null) c.Fps = Mathf.Max(1f, (float)o["fps"]);
                            Clips[p.Name] = c;
                        }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[art] Resources/Config/art.json is invalid, using built-in art paths: " + e.Message);
            }
        }

        static void ReadPaths(JObject root, string section)
        {
            var o = root[section] as JObject;
            if (o == null) return;
            foreach (var p in o.Properties())
                if (p.Value.Type == JTokenType.String) Paths[section + "." + p.Name] = (string)p.Value;
        }

        // ---------------- lookups ----------------

        /// <summary>A UI skin piece by name (btn_red, panel_window, ...). Unknown names map to UI/{name}.</summary>
        public static SkinEntry SkinPiece(string name)
        {
            Load();
            SkinEntry e;
            return Skin.TryGetValue(name, out e) ? e : new SkinEntry("UI/" + name, Vector4.zero);
        }

        /// <summary>A soldier animation clip (idle, walk, victory, death).</summary>
        public static Clip SoldierClip(string clip)
        {
            Load();
            Clip c;
            return Clips.TryGetValue(clip, out c) ? c : new Clip("Troops/soldier_" + clip, 10f);
        }

        /// <summary>
        /// Soldier frames have room around the figure (spear, raised arms, the fall), so frames are drawn
        /// this many times larger than the figure size wanted on screen.
        /// </summary>
        public static float SoldierFrameScale { get { Load(); return _frameScale; } }

        /// <summary>A plain path: fonts.body, fonts.title, soldier.icon, map.background, map.compass, cards.art, cards.back, cards.suits.</summary>
        public static string Path(string key)
        {
            Load();
            string p;
            return Paths.TryGetValue(key, out p) ? p : key;
        }

        // ---------------- defaults as JSON (for the "write config files" tool) ----------------

        public static JObject DefaultsAsJson()
        {
            var skin = new JObject();
            foreach (var kv in DefaultSkin)
                skin[kv.Key] = new JObject
                {
                    ["path"] = kv.Value.Path,
                    ["border"] = new JArray(kv.Value.Border.x, kv.Value.Border.y, kv.Value.Border.z, kv.Value.Border.w)
                };
            var clips = new JObject();
            foreach (var kv in DefaultClips)
                clips[kv.Key] = new JObject { ["path"] = kv.Value.Path, ["fps"] = kv.Value.Fps };
            return new JObject
            {
                ["fonts"] = new JObject { ["body"] = DefaultPaths["fonts.body"], ["title"] = DefaultPaths["fonts.title"] },
                ["skin"] = skin,
                ["soldier"] = new JObject { ["icon"] = DefaultPaths["soldier.icon"], ["frameScale"] = DefaultFrameScale, ["clips"] = clips },
                ["map"] = new JObject { ["background"] = DefaultPaths["map.background"], ["compass"] = DefaultPaths["map.compass"] },
                ["cards"] = new JObject { ["art"] = DefaultPaths["cards.art"], ["back"] = DefaultPaths["cards.back"], ["suits"] = DefaultPaths["cards.suits"] },
            };
        }
    }
}
