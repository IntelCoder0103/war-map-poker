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
            try
            {
                if (File.Exists(file))
                {
                    bytes = File.ReadAllBytes(file);
                    File.SetLastWriteTimeUtc(file, DateTime.UtcNow); // "last used", for the cache cleanup
                }
            }
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

            // Stills get mipmaps (drawn smaller in the picker); animation strips don't need them, and
            // markNonReadable drops the CPU copy, so a long strip costs only its GPU memory.
            bool still = item.frames <= 1;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, still);
            if (!tex.LoadImage(bytes, true))
            {
                UnityEngine.Object.Destroy(tex);
                yield break;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = still ? FilterMode.Trilinear : FilterMode.Bilinear;
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

        // Pictures are kept on the phone to save mobile data, even for emojis the admin switched off
        // (they may come back). Removed only: an emoji's older picture once it has a new one, and,
        // past CacheLimit, the longest-unused pictures the list doesn't use now.
        const long CacheLimit = 8L * 1024 * 1024;

        void PruneDiskCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                var keep = new HashSet<string>();
                var current = new Dictionary<string, string>(); // file name without the version -> current file
                foreach (var i in Items)
                {
                    if (i.image == null) continue;
                    string name = FileName(i.image);
                    keep.Add(name);
                    current[Unversioned(name)] = name;
                }
                var spare = new List<FileInfo>();
                long total = 0;
                foreach (var path in Directory.GetFiles(CacheDir, "*.png"))
                {
                    var f = new FileInfo(path);
                    string replacedBy;
                    if (!keep.Contains(f.Name) && current.TryGetValue(Unversioned(f.Name), out replacedBy))
                    {
                        f.Delete(); // an old version of a picture that has changed: never needed again
                        continue;
                    }
                    total += f.Length;
                    if (!keep.Contains(f.Name)) spare.Add(f);
                }
                spare.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (var f in spare)
                {
                    if (total <= CacheLimit) break;
                    total -= f.Length;
                    f.Delete();
                }
            }
            catch (Exception e) { Debug.LogWarning("[chat] cache cleanup failed: " + e.Message); }
        }

        // "chat_img_<id>_png_r_<rev>.png" -> "chat_img_<id>_png_r"
        static string Unversioned(string fileName)
        {
            string n = fileName.EndsWith(".png") ? fileName.Substring(0, fileName.Length - 4) : fileName;
            int cut = n.LastIndexOf('_');
            return cut > 0 ? n.Substring(0, cut) : n;
        }
    }
}
