using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.UI;

namespace WarPoker.Troops
{
    /// <summary>
    /// Draws an amount as a squad of identical soldiers: the count grows with sqrt(amount / big blind)
    /// (see TroopSquad), arranged in staggered ranks that fit the view's rect. The exact number is
    /// shown by the caller next to it. Children are pooled and reused.
    /// </summary>
    public class TroopStackView : MonoBehaviour
    {
        /// <summary>The table's big blind: one "unit" for squad sizing. Set by the game screen.</summary>
        public static long Unit = 20;

        public float IconSize = 30f;            // the largest a figure may be drawn
        public Color LabelColor = Theme.Ink;    // kept for callers; the squad has no label of its own

        RectTransform _rt;
        readonly List<Image> _icons = new List<Image>();
        long _amount = -1, _unit = -1;

        public long Amount { get { return _amount; } }

        public static TroopStackView Create(Transform parent, string name, Vector2 size, float iconSize)
        {
            var rt = UIKit.Rect(name, parent);
            rt.sizeDelta = size;
            var view = rt.gameObject.AddComponent<TroopStackView>();
            view._rt = rt;
            view.IconSize = iconSize;
            return view;
        }

        public void SetAmount(long amount)
        {
            if (amount == _amount && Unit == _unit) return;
            _amount = amount;
            _unit = Unit;
            Draw(TroopSquad.Figures(amount, Unit));
        }

        /// <summary>Shows a fixed number of figures (e.g. lobby room badges), ignoring amounts.</summary>
        public void SetFigures(int figures)
        {
            _amount = -1;
            Draw(Mathf.Clamp(figures, 0, TroopSquad.MaxFigures));
        }

        /// <summary>Forces a redraw on the next SetAmount (e.g. after an animation hid children).</summary>
        public void Invalidate()
        {
            _amount = -1;
        }

        void Draw(int n)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            for (int i = n; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);
            if (n <= 0) return;

            // Ranks: up to 6 per front rank, 2-3 ranks deep, each rank behind a bit higher and offset.
            Vector2 area = _rt.rect.size;
            if (area.x < 1f || area.y < 1f) area = _rt.sizeDelta;
            int perRank = Mathf.Min(n, n <= 6 ? 6 : Mathf.CeilToInt(n / 3f));
            int ranks = Mathf.CeilToInt(n / (float)perRank);
            const float stepK = 0.5f, rankK = 0.3f;
            float widthUnits = 1f + (perRank - 1) * stepK + (ranks > 1 ? stepK * 0.5f : 0f);
            float heightUnits = 1f + (ranks - 1) * rankK;
            float size = Mathf.Min(IconSize, area.x / widthUnits, area.y * 1.25f / heightUnits);
            float step = size * stepK, rankStep = size * rankK;

            Sprite sprite = TroopArt.Soldier();
            var idle = TroopArt.Frames("idle");
            int index = 0;
            // back ranks first so the front rank is drawn on top
            for (int r = ranks - 1; r >= 0; r--)
            {
                int inRank = r == ranks - 1 ? n - perRank * (ranks - 1) : perRank;
                float rowW = size + (inRank - 1) * step;
                float x0 = -rowW * 0.5f + size * 0.5f + (r % 2 == 1 ? step * 0.5f : 0f);
                float y = -(heightUnits * size) * 0.5f + size * 0.5f + r * rankStep;
                for (int i = 0; i < inRank; i++)
                {
                    var img = Icon(index++);
                    img.sprite = sprite;
                    // each figure idles on its own beat
                    if (idle != null) SpriteAnim.On(img).Play(idle, TroopArt.IdleFps, true, true);
                    var irt = img.rectTransform;
                    irt.anchoredPosition = new Vector2(x0 + i * step, y);
                    irt.sizeDelta = new Vector2(size, size) * TroopArt.FrameScale; // layout is in figure size
                    irt.SetAsLastSibling();
                }
            }
        }

        /// <summary>The whole squad plays a one-shot clip (e.g. "victory"), then goes back to idling.</summary>
        public void PlayOnce(string clip, float fps)
        {
            var frames = TroopArt.Frames(clip);
            var idle = TroopArt.Frames("idle");
            if (frames == null) return;
            foreach (var img in _icons)
            {
                if (img == null || !img.gameObject.activeSelf) continue;
                var anim = SpriteAnim.On(img);
                var captured = anim;
                anim.Play(frames, fps, false, false, () =>
                {
                    if (idle != null && captured != null) captured.Play(idle, TroopArt.IdleFps, true, true);
                });
            }
        }

        /// <summary>The squad falls (death clip) and stays down.</summary>
        public void PlayDeath()
        {
            var frames = TroopArt.Frames("death");
            if (frames == null) return;
            foreach (var img in _icons)
                if (img != null && img.gameObject.activeSelf) SpriteAnim.On(img).Play(frames, TroopArt.DeathFps, false);
            _amount = -1; // the next SetAmount redraws normal figures
        }

        Image Icon(int index)
        {
            while (_icons.Count <= index)
            {
                var img = UIKit.Deco(transform, "Unit", null, Color.white);
                img.preserveAspect = true;
                _icons.Add(img);
            }
            var icon = _icons[index];
            icon.gameObject.SetActive(true);
            return icon;
        }
    }
}
