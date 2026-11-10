using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WarPoker.FX
{
    /// <summary>
    /// Sound effects. Clips live in Resources/Audio named {sound}_{n}.ogg (deal_1, deal_2, ...);
    /// Play("deal") picks one of them at random, so adding variants or replacing a sound needs no code.
    /// Sounds: click, deal, shuffle, flip, check, fold, march, allin, ready, join, leave, turn, tick,
    /// coins, fall, win, lose, notify. The on/off switch is kept in PlayerPrefs.
    /// </summary>
    public static class Sfx
    {
        const string Folder = "Audio";
        const string PrefKey = "sfx.on";
        const int Voices = 12;

        // Per-sound loudness, so the set sounds even (the source files differ a lot).
        static readonly Dictionary<string, float> Volume = new Dictionary<string, float>
        {
            { "click", 0.7f }, { "deal", 0.8f }, { "shuffle", 0.9f }, { "flip", 0.9f },
            { "check", 0.55f }, { "fold", 1f }, { "march", 0.45f }, { "allin", 0.5f },
            { "ready", 0.6f }, { "join", 0.45f }, { "leave", 0.4f }, { "turn", 0.55f },
            { "tick", 0.5f }, { "coins", 0.8f }, { "fall", 0.7f }, { "win", 0.55f },
            { "lose", 0.45f }, { "notify", 0.5f },
        };

        // Music cues keep their pitch; everything else is varied a little so repeats don't sound robotic.
        static readonly HashSet<string> KeepPitch = new HashSet<string> { "win", "lose", "turn" };

        static readonly Dictionary<string, List<AudioClip>> Clips = new Dictionary<string, List<AudioClip>>();
        static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        static AudioSource[] _voices;
        static int _next;
        static int _on = -1;

        public static bool On
        {
            get
            {
                if (_on < 0) _on = PlayerPrefs.GetInt(PrefKey, 1);
                return _on == 1;
            }
            set
            {
                _on = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, _on);
                PlayerPrefs.Save();
                if (!value && _voices != null) foreach (var v in _voices) v.Stop();
            }
        }

        /// <summary>
        /// Plays a sound. The same sound is skipped if it already started less than minGap seconds ago,
        /// so many cards or marches at once make one sound, not a burst.
        /// </summary>
        public static void Play(string sound, float volume = 1f, float minGap = 0.05f)
        {
            if (!On) return;
            Init();
            List<AudioClip> list;
            if (!Clips.TryGetValue(sound, out list) || list.Count == 0) return;
            float now = Time.unscaledTime, last;
            if (LastPlayed.TryGetValue(sound, out last) && now - last < minGap) return;
            LastPlayed[sound] = now;

            float baseVolume;
            if (!Volume.TryGetValue(sound, out baseVolume)) baseVolume = 1f;
            var voice = FreeVoice();
            voice.clip = list[Random.Range(0, list.Count)];
            voice.volume = Mathf.Clamp01(baseVolume * volume);
            voice.pitch = KeepPitch.Contains(sound) ? 1f : Random.Range(0.95f, 1.05f);
            voice.Play();
        }

        static AudioSource FreeVoice()
        {
            for (int i = 0; i < Voices; i++)
            {
                var v = _voices[(_next + i) % Voices];
                if (!v.isPlaying) { _next = (_next + i + 1) % Voices; return v; }
            }
            var oldest = _voices[_next];
            _next = (_next + 1) % Voices;
            return oldest;
        }

        static void Init()
        {
            if (_voices != null) return;
            var go = new GameObject("Sfx");
            Object.DontDestroyOnLoad(go);
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _voices[i] = s;
            }
            var variant = new Regex(@"_\d+$");
            foreach (var clip in Resources.LoadAll<AudioClip>(Folder))
            {
                string key = variant.Replace(clip.name, "");
                List<AudioClip> list;
                if (!Clips.TryGetValue(key, out list)) Clips[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
        }
    }
}
