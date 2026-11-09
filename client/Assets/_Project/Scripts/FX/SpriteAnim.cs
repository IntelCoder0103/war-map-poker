using System;
using UnityEngine;
using UnityEngine.UI;

namespace WarPoker.FX
{
    /// <summary>
    /// Flip-book animation on a uGUI Image: cycles through sprite frames at a fixed rate.
    /// Looping clips can start at a random frame so a squad doesn't move in lockstep.
    /// One-shot clips hold their last frame and call onDone.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SpriteAnim : MonoBehaviour
    {
        Image _image;
        Sprite[] _frames;
        float _fps = 10f, _time;
        bool _loop = true;
        int _shown = -1;
        Action _onDone;

        public Sprite[] Frames { get { return _frames; } }

        public static SpriteAnim On(Image image)
        {
            var a = image.GetComponent<SpriteAnim>();
            if (a == null) a = image.gameObject.AddComponent<SpriteAnim>();
            a._image = image;
            return a;
        }

        /// <summary>Starts a clip. randomStart picks a random first frame (looping clips only).</summary>
        public void Play(Sprite[] frames, float fps, bool loop, bool randomStart = false, Action onDone = null)
        {
            if (_image == null) _image = GetComponent<Image>();
            _frames = frames;
            _fps = Mathf.Max(1f, fps);
            _loop = loop;
            _onDone = onDone;
            _shown = -1;
            _time = loop && randomStart && frames != null && frames.Length > 0
                ? UnityEngine.Random.Range(0, frames.Length) / _fps
                : 0f;
            enabled = frames != null && frames.Length > 0;
            Show();
        }

        public void Stop()
        {
            enabled = false;
            _onDone = null;
        }

        void Update()
        {
            _time += Time.unscaledDeltaTime * Tween.Speed;
            Show();
        }

        void Show()
        {
            if (_frames == null || _frames.Length == 0) return;
            int n = _frames.Length;
            int f = Mathf.FloorToInt(_time * _fps);
            if (_loop) f %= n;
            else if (f >= n)
            {
                f = n - 1;
                enabled = false;
                var done = _onDone;
                _onDone = null;
                if (_shown != f) { _shown = f; _image.sprite = _frames[f]; }
                if (done != null) done();
                return;
            }
            if (f != _shown)
            {
                _shown = f;
                _image.sprite = _frames[f];
            }
        }
    }
}
