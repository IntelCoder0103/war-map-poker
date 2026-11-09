using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Main
{
    /// <summary>
    /// Loads the editable config files at startup (all under Assets/_Project/Resources/Config/):
    ///   game.json : client settings (server address, wording file, display tuning)
    ///   text.json : all wording shown to players (see Core/Strings for the keys)
    ///   art.json  : where every image, font and animation comes from (see UI/ArtCatalog)
    /// Every file and every field is optional: missing values keep the built-in defaults.
    /// docs/customizing.md explains what to change where.
    /// </summary>
    public static class GameSettings
    {
        // ---- values from game.json (defaults = current behaviour) ----

        /// <summary>Which Resources file holds the wording (lets you keep several, e.g. Config/text_holiday).</summary>
        public static string WordingFile = "Config/text";
        /// <summary>Seconds the showdown ranking board stays up after the last row appears.</summary>
        public static float ShowdownHoldSeconds = 4.5f;
        /// <summary>Seconds soldiers take to march from the bet spots to the winner.</summary>
        public static float MarchSeconds = 0.7f;
        /// <summary>Seconds the winner's crown stays up.</summary>
        public static float WinnerCrownSeconds = 1.3f;

        static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            var game = ReadJson("Config/game");
            if (game != null)
            {
                WordingFile = Str(game, "wordingFile", WordingFile);
                ShowdownHoldSeconds = Num(game, "showdownHoldSeconds", ShowdownHoldSeconds);
                MarchSeconds = Num(game, "marchSeconds", MarchSeconds);
                WinnerCrownSeconds = Num(game, "winnerCrownSeconds", WinnerCrownSeconds);
                TroopSquad.MaxFigures = Mathf.Clamp((int)Num(game, "maxSquadFigures", TroopSquad.MaxFigures), 1, 60);
            }

            var text = ReadJson(WordingFile);
            if (text != null)
            {
                var table = new Dictionary<string, string>();
                foreach (var p in text.Properties())
                    if (p.Value.Type == JTokenType.String) table[p.Name] = (string)p.Value;
                Strings.Load(table);
            }

            ArtCatalog.Load();
        }

        /// <summary>Parses a JSON TextAsset from Resources; null (with a warning) if missing or broken.</summary>
        public static JObject ReadJson(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null) return null;
            try { return JObject.Parse(asset.text); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[settings] Resources/" + resourcePath + ".json is invalid, using defaults: " + e.Message);
                return null;
            }
        }

        static string Str(JObject o, string key, string fallback)
        {
            var t = o[key];
            return t != null && t.Type == JTokenType.String && !string.IsNullOrEmpty((string)t) ? (string)t : fallback;
        }

        static float Num(JObject o, string key, float fallback)
        {
            var t = o[key];
            return t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (float)t : fallback;
        }
    }
}
