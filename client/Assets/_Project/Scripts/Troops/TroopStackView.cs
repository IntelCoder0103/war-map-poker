using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Troops
{
    /// <summary>
    /// Draws an amount as a small squad of identical soldiers (1-5 figures, more for bigger
    /// amounts, see TroopSquad). The exact number is shown by the caller next to it.
    /// Children are pooled and reused.
    /// </summary>
    public class TroopStackView : MonoBehaviour
    {
        public float IconSize = 30f;
        public Color LabelColor = Theme.Ink; // kept for callers; the squad has no label of its own

        RectTransform _rt;
        readonly List<Image> _icons = new List<Image>();
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
            Draw(TroopSquad.Figures(amount));
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
            // a loose rank: figures overlap a little, alternate rows give depth
            float step = IconSize * 0.55f;
            float total = n > 0 ? IconSize + (n - 1) * step : 0f;
            float x = -total * 0.5f;
            Sprite sprite = TroopArt.Soldier();
            for (int i = 0; i < n; i++)
            {
                var img = Icon(i);
                img.sprite = sprite;
                var irt = img.rectTransform;
                irt.anchoredPosition = new Vector2(x + IconSize * 0.5f + i * step, i % 2 == 0 ? 0f : IconSize * 0.08f);
                irt.sizeDelta = new Vector2(IconSize, IconSize);
                // odd figures stand a step behind
                if (i % 2 == 1) irt.SetAsFirstSibling(); else irt.SetAsLastSibling();
            }
            for (int i = n; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);
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
