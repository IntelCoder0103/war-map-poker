using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WarPoker.FX
{
    /// <summary>Easing curves: t in 0..1 → eased 0..1.</summary>
    public static class Ease
    {
        public static float Linear(float t) { return t; }
        public static float InQuad(float t) { return t * t; }
        public static float OutQuad(float t) { return 1f - (1f - t) * (1f - t); }
        public static float InOutQuad(float t) { return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f; }
        public static float OutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float InCubic(float t) { return t * t * t; }

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float OutBounce(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }

    /// <summary>
    /// Tiny coroutine tween library (replaces DOTween). Each method returns an IEnumerator so
    /// tweens can be yielded inside other coroutines, chained with Sequence, or run together
    /// with Parallel. All tweens stop quietly if their target is destroyed.
    /// </summary>
    public static class Tween
    {
        /// <summary>Global speed multiplier (raised when the event queue falls behind).</summary>
        public static float Speed = 1f;

        static float Dt { get { return Time.unscaledDeltaTime * Speed; } }

        /// <summary>Drives t from 0 to 1 over duration, calling step(eased t) every frame (and with 1 at the end).</summary>
        public static IEnumerator Value(float duration, Action<float> step, Func<float, float> ease = null)
        {
            if (ease == null) ease = Ease.OutQuad;
            if (duration <= 0f) { step(1f); yield break; }
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Dt / duration);
                step(ease(t));
                if (t < 1f) yield return null;
            }
        }

        public static IEnumerator Move(RectTransform rt, Vector2 to, float duration, Func<float, float> ease = null)
        {
            if (rt == null) yield break;
            Vector2 from = rt.anchoredPosition;
            yield return Value(duration, k => { if (rt != null) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k); }, ease);
        }

        /// <summary>Move along an arc (a hop): the y offset peaks at <paramref name="height"/> mid-way.</summary>
        public static IEnumerator Arc(RectTransform rt, Vector2 to, float height, float duration, Func<float, float> ease = null)
        {
            if (rt == null) yield break;
            Vector2 from = rt.anchoredPosition;
            yield return Value(duration, k =>
            {
                if (rt == null) return;
                Vector2 p = Vector2.LerpUnclamped(from, to, k);
                p.y += Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * height;
                rt.anchoredPosition = p;
            }, ease);
        }

        public static IEnumerator Scale(Transform tr, Vector3 to, float duration, Func<float, float> ease = null)
        {
            if (tr == null) yield break;
            Vector3 from = tr.localScale;
            yield return Value(duration, k => { if (tr != null) tr.localScale = Vector3.LerpUnclamped(from, to, k); }, ease);
        }

        public static IEnumerator Fade(CanvasGroup cg, float to, float duration, Func<float, float> ease = null)
        {
            if (cg == null) yield break;
            float from = cg.alpha;
            yield return Value(duration, k => { if (cg != null) cg.alpha = Mathf.LerpUnclamped(from, to, k); }, ease ?? (Func<float, float>)Ease.Linear);
        }

        public static IEnumerator FadeGraphic(UnityEngine.UI.Graphic g, float to, float duration, Func<float, float> ease = null)
        {
            if (g == null) yield break;
            Color from = g.color;
            Color target = new Color(from.r, from.g, from.b, to);
            yield return Value(duration, k => { if (g != null) g.color = Color.LerpUnclamped(from, target, k); }, ease ?? (Func<float, float>)Ease.Linear);
        }

        public static IEnumerator RotateZ(Transform tr, float toDeg, float duration, Func<float, float> ease = null)
        {
            if (tr == null) yield break;
            float from = tr.localEulerAngles.z;
            if (from > 180f) from -= 360f;
            yield return Value(duration, k => { if (tr != null) tr.localEulerAngles = new Vector3(0, 0, Mathf.LerpUnclamped(from, toDeg, k)); }, ease);
        }

        /// <summary>Card flip: squash X to 0, run the swap action (change face), expand back.</summary>
        public static IEnumerator Flip(Transform tr, Action atMiddle, float duration)
        {
            if (tr == null) yield break;
            Vector3 s = tr.localScale;
            float sx = Mathf.Abs(s.x) < 0.001f ? 1f : s.x;
            yield return Value(duration * 0.5f, k => { if (tr != null) tr.localScale = new Vector3(sx * (1f - k), s.y, s.z); }, Ease.InQuad);
            if (atMiddle != null) atMiddle();
            yield return Value(duration * 0.5f, k => { if (tr != null) tr.localScale = new Vector3(sx * k, s.y, s.z); }, Ease.OutQuad);
        }

        public static IEnumerator Delay(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Dt;
                yield return null;
            }
        }

        public static IEnumerator Call(Action action)
        {
            if (action != null) action();
            yield break;
        }

        public static IEnumerator Sequence(params IEnumerator[] steps)
        {
            foreach (var s in steps)
                if (s != null) yield return s;
        }

        /// <summary>Starts every step on host and waits until all are done.</summary>
        public static IEnumerator Parallel(MonoBehaviour host, IList<IEnumerator> steps)
        {
            if (host == null || steps == null) yield break;
            int remaining = 0;
            foreach (var s in steps)
            {
                if (s == null) continue;
                remaining++;
                host.StartCoroutine(Track(s, () => remaining--));
            }
            while (remaining > 0) yield return null;
        }

        static IEnumerator Track(IEnumerator routine, Action done)
        {
            yield return routine;
            done();
        }
    }
}
