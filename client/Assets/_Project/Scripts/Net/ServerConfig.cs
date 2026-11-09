using UnityEngine;

namespace WarPoker.Net
{
    /// <summary>
    /// Where the server lives. Default: Android emulator's alias for the host machine.
    /// The login screen lets you edit it; the value is kept in PlayerPrefs.
    /// The default comes from "serverUrl" in Resources/Config/game.json
    /// (older builds used Resources/config.json, which is still read as a fallback).
    /// </summary>
    public static class ServerConfig
    {
        public const string BuiltInDefault = "http://10.0.2.2:3000";
        const string PrefKey = "warpoker.serverUrl";

        [System.Serializable]
        class ConfigFile
        {
            public string serverUrl;
        }

        public static string DefaultUrl
        {
            get
            {
                foreach (var path in new[] { "Config/game", "config" })
                {
                    var asset = Resources.Load<TextAsset>(path);
                    if (asset == null) continue;
                    try
                    {
                        var cfg = JsonUtility.FromJson<ConfigFile>(asset.text);
                        if (cfg != null && !string.IsNullOrEmpty(cfg.serverUrl)) return Normalize(cfg.serverUrl);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Resources/" + path + ".json is invalid: " + e.Message);
                    }
                }
#if UNITY_EDITOR
                // In the Editor the server is usually on this machine.
                return "http://localhost:3000";
#else
                return BuiltInDefault;
#endif
            }
        }

        public static string Url
        {
            get
            {
                string saved = PlayerPrefs.GetString(PrefKey, "");
                return string.IsNullOrEmpty(saved) ? DefaultUrl : saved;
            }
            set
            {
                PlayerPrefs.SetString(PrefKey, Normalize(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>Trims, adds http:// if missing, removes a trailing slash.</summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return DefaultUrl;
            url = url.Trim();
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "http://" + url;
            while (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
            return url;
        }
    }
}
