using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Writes the editable config files in Assets/_Project/Resources/Config/ from the built-in defaults:
    /// game.json, text.json and art.json. Existing values are kept; only missing keys are added,
    /// so it is safe to run after updates (new wording keys appear with their English default).
    /// Command line: -executeMethod WarPoker.EditorTools.ConfigFiles.Update
    /// </summary>
    public static class ConfigFiles
    {
        const string Dir = "Assets/_Project/Resources/Config";

        [MenuItem("WarPoker/Update Config Files")]
        public static void Update()
        {
            Directory.CreateDirectory(Dir);

            // game.json: client settings. Carries over the server address from the old Resources/config.json.
            var game = new JObject
            {
                ["serverUrl"] = OldServerUrl() ?? "http://localhost:3000",
                ["wordingFile"] = "Config/text",
                ["maxSquadFigures"] = 20,
                ["showdownHoldSeconds"] = 1.6,
                ["marchSeconds"] = 0.7,
                ["winnerCrownSeconds"] = 1.3,
            };
            int added = Merge(Dir + "/game.json", game);

            // text.json: every wording key with its English default, in the order of Core/Strings.
            var text = new JObject();
            foreach (var kv in Strings.AllDefaults) text[kv.Key] = kv.Value;
            added += Merge(Dir + "/text.json", text);

            // art.json: every art path, 9-slice border and soldier clip.
            added += Merge(Dir + "/art.json", ArtCatalog.DefaultsAsJson());

            AssetDatabase.Refresh();
            Debug.Log("[WarPoker] Config files in " + Dir + " are up to date (" + added + " new entries added).");
        }

        static string OldServerUrl()
        {
            const string old = "Assets/_Project/Resources/config.json";
            if (!File.Exists(old)) return null;
            try { return (string)JObject.Parse(File.ReadAllText(old))["serverUrl"]; }
            catch { return null; }
        }

        /// <summary>Adds keys from `defaults` that the file doesn't have yet (recursively). Returns how many were added.</summary>
        static int Merge(string path, JObject defaults)
        {
            JObject current = null;
            if (File.Exists(path))
            {
                try { current = JObject.Parse(File.ReadAllText(path)); }
                catch (JsonException e)
                {
                    Debug.LogError("[WarPoker] " + path + " is not valid JSON, left unchanged: " + e.Message);
                    return 0;
                }
            }
            if (current == null) current = new JObject();
            int added = AddMissing(current, defaults);
            File.WriteAllText(path, current.ToString(Formatting.Indented) + "\n");
            return added;
        }

        static int AddMissing(JObject target, JObject defaults)
        {
            int added = 0;
            foreach (var p in defaults.Properties())
            {
                var existing = target[p.Name];
                if (existing == null) { target[p.Name] = p.Value.DeepClone(); added++; }
                else if (existing is JObject eo && p.Value is JObject po) added += AddMissing(eo, po);
            }
            return added;
        }
    }
}
