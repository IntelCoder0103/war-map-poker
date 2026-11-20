using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using WarPoker.Net;

namespace WarPoker.Chat
{
    /// <summary>
    /// The emojis and messages players can send at the table, as the server lists them (GET /chat).
    /// Emoji pictures are downloaded once and kept on the device (persistentDataPath/chat), so the
    /// admin can add or change emojis without an app update. Refresh() runs on every socket connect
    /// and when the server says the list changed (chat:updated).
    /// </summary>
    public class ChatCatalog
    {
        public event Action Changed;

        public int Version { get; private set; }
        public int CooldownMs { get; private set; }
        public List<ChatItemDto> Items { get; private set; }

        readonly MonoBehaviour _host;
        readonly ApiClient _api;
        readonly Func<string> _baseUrl;
        readonly Dictionary<string, Sprite[]> _frames = new Dictionary<string, Sprite[]>(); // by image path
        readonly HashSet<string> _loading = new HashSet<string>();
        bool _refreshing, _again;

        static string CacheDir { get { return Path.Combine(Application.persistentDataPath, "chat"); } }

        public ChatCatalog(MonoBehaviour host, ApiClient api, Func<string> baseUrl)
        {
            _host = host;
            _api = api;
            _baseUrl = baseUrl;
            Version = -1;
            CooldownMs = 3000;
            Items = new List<ChatItemDto>();
        }

        public IEnumerable<ChatItemDto> Emojis
        {
            get { foreach (var i in Items) if (i.IsEmoji) yield return i; }
        }

        public IEnumerable<ChatItemDto> Messages
        {
            get { foreach (var i in Items) if (!i.IsEmoji) yield return i; }
        }

        public ChatItemDto Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var i in Items) if (i.id == id) return i;
            return null;
        }

        /// <summary>The emoji's frames (one for a still picture), or null while it is still downloading.</summary>
        public Sprite[] Frames(ChatItemDto item)
        {
            Sprite[] f;
            return item != null && item.image != null && _frames.TryGetValue(item.image, out f) ? f : null;
        }

        /// <summary>The server announced a new version: fetch the list if ours is older.</summary>
        public void OnUpdated(int version)
        {
            if (version != Version) Refresh();
        }

        public void Refresh()
        {
            if (_refreshing) { _again = true; return; }
            _refreshing = true;
            _api.Chat(r =>
            {
                _refreshing = false;
                if (r.Ok && r.Data != null)
                {
                    Version = r.Data.version;
                    CooldownMs = Mathf.Max(0, r.Data.cooldownMs);
                    Items = r.Data.items ?? new List<ChatItemDto>();
                    foreach (var item in Items)
                        if (item.IsEmoji && item.image != null && !_frames.ContainsKey(item.image) && !_loading.Contains(item.image))
                            _host.StartCoroutine(LoadImage(item));
                    PruneDiskCache();
                    if (Changed != null) Changed();
                }
                else
                {
                    Debug.LogWarning("[chat] could not load the chat list: " + r.Error);
                }
                if (_again) { _again = false; Refresh(); }
            });
        }

        IEnumerator LoadImage(ChatItemDto item)
        {
            string key = item.image;
            _loading.Add(key);
            string file = Path.Combine(CacheDir, FileName(key));
            byte[] bytes = null;
            try { if (File.Exists(file)) bytes = File.ReadAllBytes(file); }
            catch (Exception e) { Debug.LogWarning("[chat] cache read failed: " + e.Message); }

            if (bytes == null)
            {
                using (var req = UnityWebRequest.Get(_baseUrl() + key))
                {
                    req.timeout = 20;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        bytes = req.downloadHandler.data;
                        try
                        {
                            Directory.CreateDirectory(CacheDir);
                            File.WriteAllBytes(file, bytes);
                        }
                        catch (Exception e) { Debug.LogWarning("[chat] cache write failed: " + e.Message); }
                    }
                    else
                    {
                        Debug.LogWarning("[chat] could not download " + key + ": " + req.error);
                    }
                }
            }
            _loading.Remove(key);
            if (bytes == null) yield break;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                yield break;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            int size = tex.height;
            int count = Mathf.Max(1, Mathf.Min(item.frames, tex.width / Mathf.Max(1, size)));
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(i * size, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                frames[i].name = "chat_" + item.id + "_" + i;
            }
            _frames[key] = frames;
            if (Changed != null) Changed();
        }

        static readonly Regex Unsafe = new Regex("[^A-Za-z0-9]+");

        static string FileName(string imagePath)
        {
            return Unsafe.Replace(imagePath, "_").Trim('_') + ".png";
        }

        // Drops cached pictures the list no longer uses, so the folder doesn't grow forever.
        void PruneDiskCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                var keep = new HashSet<string>();
                foreach (var i in Items) if (i.image != null) keep.Add(FileName(i.image));
                foreach (var f in Directory.GetFiles(CacheDir, "*.png"))
                    if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch (Exception e) { Debug.LogWarning("[chat] cache cleanup failed: " + e.Message); }
        }
    }
}
