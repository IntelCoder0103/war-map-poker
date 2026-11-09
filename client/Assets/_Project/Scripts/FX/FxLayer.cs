using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Cards;
using WarPoker.Core;
using WarPoker.Troops;
using WarPoker.UI;

namespace WarPoker.FX
{
    /// <summary>
    /// Overlay for game-screen effects: marching troops, flying cards, UI "particles",
    /// flashes, floating text and the victory banner. Uses pooled objects only.
    /// ParticleSystem doesn't render in a Screen Space Overlay canvas, so particles are Images.
    /// </summary>
    public class FxLayer : MonoBehaviour
    {
        /// <summary>MonoBehaviour that runs parallel sub-coroutines (the game screen).</summary>
        public MonoBehaviour Host;

        RectTransform _rt;
        readonly List<Image> _imagePool = new List<Image>();
        readonly List<Text> _textPool = new List<Text>();
        readonly List<CardView> _cardPool = new List<CardView>();
        Image _flash;
        RectTransform _banner;
        CanvasGroup _bannerGroup;
        Text _bannerTitle, _bannerLine;

        public static FxLayer Create(RectTransform parent, MonoBehaviour host)
        {
            var rt = UIKit.Stretch(UIKit.Rect("FX", parent));
            var fx = rt.gameObject.AddComponent<FxLayer>();
            fx._rt = rt;
            fx.Host = host;
            fx.Build();
            return fx;
        }

        void Build()
        {
            _flash = UIKit.Deco(transform, "Flash", null, new Color(1, 1, 1, 0));
            UIKit.Center(_flash.rectTransform, Vector2.zero, new Vector2(5000, 5000));
            _flash.gameObject.SetActive(false);

            var bannerImg = UIKit.Deco(transform, "VictoryBanner", ProcSprites.RoundedRect(18, 5, 0.5f), Theme.Crimson);
            _banner = bannerImg.rectTransform;
            UIKit.Center(_banner, new Vector2(0, 40), new Vector2(980, 230));
            _bannerGroup = UIKit.Group(_banner.gameObject);
            _bannerGroup.blocksRaycasts = false;
            var trim = UIKit.Deco(_banner, "Trim", ProcSprites.RoundedRect(14, 3, 0.6f), new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.0f));
            UIKit.Stretch(trim.rectTransform, 10f);
            _bannerTitle = UIKit.Text(_banner, "Title", Strings.Victory, 86, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_bannerTitle.rectTransform, new Vector2(0, 38), new Vector2(940, 110));
            UIKit.AddShadow(_bannerTitle, 3f, 0.6f);
            _bannerLine = UIKit.Text(_banner, "Line", "", 34, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_bannerLine.rectTransform, new Vector2(0, -58), new Vector2(940, 60));
            _banner.gameObject.SetActive(false);
        }

        /// <summary>Center of a UI element, in this layer's local (anchored) coordinates.</summary>
        public Vector2 ToLocal(RectTransform target)
        {
            if (target == null) return Vector2.zero;
            Vector3 world = target.TransformPoint(target.rect.center);
            Vector3 local = _rt.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        /// <summary>Stops all effects and hides every pooled object.</summary>
        public void Clear()
        {
            StopAllCoroutines();
            foreach (var i in _imagePool) if (i != null) i.gameObject.SetActive(false);
            foreach (var t in _textPool) if (t != null) t.gameObject.SetActive(false);
            foreach (var c in _cardPool) if (c != null) c.gameObject.SetActive(false);
            _flash.gameObject.SetActive(false);
            _banner.gameObject.SetActive(false);
        }

        // ---------- pools ----------

        Image GetImage(Sprite sprite, Color color, Vector2 size)
        {
            Image img = null;
            foreach (var i in _imagePool)
                if (i != null && !i.gameObject.activeSelf) { img = i; break; }
            if (img == null)
            {
                img = UIKit.Deco(transform, "FxImage", null, Color.white);
                _imagePool.Add(img);
            }
            img.gameObject.SetActive(true);
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.type = Image.Type.Simple;
            var rt = img.rectTransform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.SetAsLastSibling();
            return img;
        }

        Text GetText(string content, int size, Color color)
        {
            Text text = null;
            foreach (var t in _textPool)
                if (t != null && !t.gameObject.activeSelf) { text = t; break; }
            if (text == null)
            {
                text = UIKit.Text(transform, "FxText", "", size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIKit.AddShadow(text, 2f, 0.7f);
                _textPool.Add(text);
            }
            text.gameObject.SetActive(true);
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.rectTransform.sizeDelta = new Vector2(600, size * 1.4f);
            text.rectTransform.localScale = Vector3.one;
            text.rectTransform.SetAsLastSibling();
            return text;
        }

        CardView GetCard(Vector2 size)
        {
            CardView card = null;
            foreach (var c in _cardPool)
                if (c != null && !c.gameObject.activeSelf && c.Rect.sizeDelta == size) { card = c; break; }
            if (card == null)
            {
                card = CardView.Create(transform, "FxCard", size);
                _cardPool.Add(card);
            }
            card.gameObject.SetActive(true);
            card.ShowBack();
            card.ResetVisual();
            card.SetHighlight(false);
            card.Rect.SetAsLastSibling();
            return card;
        }

        // ---------- effects ----------

        /// <summary>A few troop sprites march (hopping) from one point to another, then vanish.</summary>
        public IEnumerator MarchTroops(Vector2 from, Vector2 to, long amount, float duration, int maxIcons = 5)
        {
            var units = TroopBreakdown.MarchIcons(amount, maxIcons);
            if (units.Count == 0) yield break;
            var steps = new List<IEnumerator>();
            for (int i = 0; i < units.Count; i++)
            {
                // little formation: spread sideways perpendicular to the march direction
                Vector2 dir = (to - from).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float spread = (i - (units.Count - 1) * 0.5f) * 16f;
                Vector2 a = from + side * spread;
                Vector2 b = to + side * spread * 0.5f;
                steps.Add(MarchOne(TroopArt.Get(units[i]), a, b, duration, i * 0.05f));
            }
            yield return Tween.Parallel(Host, steps);
        }

        IEnumerator MarchOne(Sprite sprite, Vector2 from, Vector2 to, float duration, float delay)
        {
            if (delay > 0f) yield return Tween.Delay(delay);
            var img = GetImage(sprite, Color.white, new Vector2(34, 34));
            var rt = img.rectTransform;
            rt.anchoredPosition = from;
            bool faceLeft = to.x < from.x;
            rt.localScale = new Vector3(faceLeft ? -1f : 1f, 1f, 1f);
            yield return Tween.Value(duration, k =>
            {
                if (rt == null) return;
                Vector2 p = Vector2.Lerp(from, to, k);
                p.y += Mathf.Abs(Mathf.Sin(k * Mathf.PI * 4f)) * 7f; // marching bob
                rt.anchoredPosition = p;
            }, Ease.InOutQuad);
            yield return Tween.FadeGraphic(img, 0f, 0.1f);
            img.gameObject.SetActive(false);
        }

        /// <summary>A card back flies from one point to another (dealing).</summary>
        public IEnumerator FlyCard(Vector2 from, Vector2 to, Vector2 size, float duration, float delay, System.Action onLanded)
        {
            if (delay > 0f) yield return Tween.Delay(delay);
            var card = GetCard(size);
            card.Rect.anchoredPosition = from;
            card.Rect.localEulerAngles = new Vector3(0, 0, 25f);
            var move = Tween.Move(card.Rect, to, duration, Ease.OutCubic);
            var spin = Tween.RotateZ(card.Rect, 0f, duration, Ease.OutCubic);
            yield return Tween.Parallel(Host, new List<IEnumerator> { move, spin });
            card.gameObject.SetActive(false);
            if (onLanded != null) onLanded();
        }

        public IEnumerator Flash(Color color, float duration)
        {
            _flash.gameObject.SetActive(true);
            _flash.rectTransform.SetAsLastSibling();
            _flash.color = color;
            yield return Tween.FadeGraphic(_flash, 0f, duration, Ease.OutQuad);
            _flash.gameObject.SetActive(false);
        }

        /// <summary>Shakes a RectTransform around its current anchored position, then restores it.</summary>
        public IEnumerator Shake(RectTransform target, float amplitude, float duration)
        {
            if (target == null) yield break;
            Vector2 origin = target.anchoredPosition;
            yield return Tween.Value(duration, k =>
            {
                if (target == null) return;
                float a = amplitude * (1f - k);
                target.anchoredPosition = origin + new Vector2(Random.Range(-a, a), Random.Range(-a, a));
            }, Ease.Linear);
            if (target != null) target.anchoredPosition = origin;
        }

        /// <summary>Burst of glowing sparks from a point.</summary>
        public IEnumerator Burst(Vector2 at, int count, Color a, Color b)
        {
            var steps = new List<IEnumerator>();
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(90f, 220f);
                Vector2 to = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                float size = Random.Range(10f, 26f);
                steps.Add(Spark(at, to, size, Color.Lerp(a, b, Random.value), Random.Range(0.4f, 0.7f)));
            }
            yield return Tween.Parallel(Host, steps);
        }

        IEnumerator Spark(Vector2 from, Vector2 to, float size, Color color, float duration)
        {
            var img = GetImage(ProcSprites.Glow(64), color, new Vector2(size, size));
            var rt = img.rectTransform;
            rt.anchoredPosition = from;
            yield return Tween.Value(duration, k =>
            {
                if (rt == null) return;
                rt.anchoredPosition = Vector2.Lerp(from, to, k) + new Vector2(0, -60f * k * k); // a bit of gravity
                var c = color; c.a = color.a * (1f - k);
                img.color = c;
                rt.localScale = Vector3.one * (1f - 0.5f * k);
            }, Ease.OutQuad);
            img.gameObject.SetActive(false);
        }

        /// <summary>Text that pops up, drifts up and fades (e.g. "RAISE 80").</summary>
        public IEnumerator FloatText(Vector2 at, string content, Color color, int size = 30)
        {
            var t = GetText(content, size, color);
            var rt = t.rectTransform;
            rt.anchoredPosition = at;
            rt.localScale = Vector3.one * 0.6f;
            yield return Tween.Scale(rt, Vector3.one, 0.15f, Ease.OutBack);
            yield return Tween.Value(0.6f, k =>
            {
                if (rt == null) return;
                rt.anchoredPosition = at + new Vector2(0, 40f * k);
                var c = color; c.a = 1f - Mathf.Clamp01((k - 0.5f) * 2f);
                t.color = c;
            }, Ease.Linear);
            t.gameObject.SetActive(false);
        }

        /// <summary>"VICTORY" banner with a line of detail; pops in, holds, fades.</summary>
        public IEnumerator Victory(string line, float hold)
        {
            _banner.gameObject.SetActive(true);
            _banner.SetAsLastSibling();
            _bannerLine.text = line;
            _bannerGroup.alpha = 1f;
            _banner.localScale = Vector3.one * 0.4f;
            yield return Tween.Scale(_banner, Vector3.one, 0.3f, Ease.OutBack);
            yield return Tween.Delay(hold);
            yield return Tween.Fade(_bannerGroup, 0f, 0.25f);
            _banner.gameObject.SetActive(false);
        }
    }
}
