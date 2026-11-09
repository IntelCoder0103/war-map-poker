using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Troops
{
    /// <summary>
    /// Draws an amount as troops: one group per unit type (largest first), up to 5 overlapping
    /// sprites per group, plus a "×N" label. Children are pooled and reused.
    /// </summary>
    public class TroopStackView : MonoBehaviour
    {
        public const int MaxSpritesPerUnit = 5;

        public float IconSize = 30f;
        public Color LabelColor = Theme.Ink;

        RectTransform _rt;
        readonly List<Image> _icons = new List<Image>();
        readonly List<Text> _labels = new List<Text>();
        long _amount = -1;

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
            if (amount == _amount) return;
            _amount = amount;
            Rebuild();
        }

        /// <summary>Forces a redraw on the next SetAmount (e.g. after an animation hid children).</summary>
        public void Invalidate()
        {
            _amount = -1;
        }

        void Rebuild()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            var groups = TroopBreakdown.Break(_amount);
            float step = IconSize * 0.38f;
            float labelW = IconSize * 1.3f;
            float gap = IconSize * 0.25f;

            // measure
            float total = 0f;
            foreach (var g in groups)
            {
                int n = (int)System.Math.Min(g.Count, MaxSpritesPerUnit);
                total += IconSize + (n - 1) * step + (g.Count > 1 ? labelW : 0f) + gap;
            }
            if (groups.Count > 0) total -= gap;

            int iconIndex = 0, labelIndex = 0;
            float x = -total * 0.5f;
            foreach (var g in groups)
            {
                int n = (int)System.Math.Min(g.Count, MaxSpritesPerUnit);
                Sprite sprite = TroopArt.Get(g.Unit);
                for (int i = 0; i < n; i++)
                {
                    var img = Icon(iconIndex++);
                    img.sprite = sprite;
                    var irt = img.rectTransform;
                    irt.anchoredPosition = new Vector2(x + IconSize * 0.5f + i * step, (i % 2 == 0 ? 0f : IconSize * 0.06f));
                    irt.sizeDelta = new Vector2(IconSize, IconSize);
                    irt.SetAsLastSibling();
                }
                x += IconSize + (n - 1) * step;
                if (g.Count > 1)
                {
                    var t = Label(labelIndex++);
                    t.text = "×" + g.Count;
                    var trt = t.rectTransform;
                    trt.anchoredPosition = new Vector2(x + labelW * 0.5f, -IconSize * 0.18f);
                    trt.sizeDelta = new Vector2(labelW, IconSize * 0.7f);
                    trt.SetAsLastSibling();
                    x += labelW;
                }
                x += gap;
            }

            for (int i = iconIndex; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);
            for (int i = labelIndex; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false);
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

        Text Label(int index)
        {
            while (_labels.Count <= index)
            {
                var t = UIKit.Text(transform, "Count", "", Mathf.RoundToInt(IconSize * 0.5f), LabelColor,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                _labels.Add(t);
            }
            var label = _labels[index];
            label.color = LabelColor;
            label.gameObject.SetActive(true);
            return label;
        }
    }
}
