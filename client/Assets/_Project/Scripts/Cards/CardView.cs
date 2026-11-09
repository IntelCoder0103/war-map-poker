using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.UI;

namespace WarPoker.Cards
{
    /// <summary>
    /// A playing card built from code: white rounded body, a narrow left strip with the rank
    /// above the suit icon, and a large main art area. Art comes from <see cref="CardArt"/>.
    /// </summary>
    public class CardView : MonoBehaviour
    {
        public RectTransform Rect { get; private set; }
        public string Code { get; private set; }
        public bool FaceUp { get; private set; }

        CanvasGroup _group;
        Image _highlight, _body, _suit, _art, _back, _dim;
        Text _rank;
        RectTransform _face;

        public static CardView Create(Transform parent, string name, Vector2 size)
        {
            var rt = UIKit.Rect(name, parent);
            rt.sizeDelta = size;
            var view = rt.gameObject.AddComponent<CardView>();
            view.Build(size);
            return view;
        }

        void Build(Vector2 size)
        {
            Rect = (RectTransform)transform;
            _group = UIKit.Group(gameObject);
            _group.blocksRaycasts = false;
            _group.interactable = false;
            float w = size.x, h = size.y;

            _highlight = UIKit.Deco(transform, "Highlight", ProcSprites.SoftRect(), Theme.GoldLight);
            UIKit.Center(_highlight.rectTransform, Vector2.zero, size + new Vector2(40, 40));
            _highlight.enabled = false;

            int radius = Mathf.Clamp(Mathf.RoundToInt(w * 0.12f), 4, 14);
            _body = UIKit.Deco(transform, "Body", ProcSprites.RoundedRect(radius, 2, 0.35f), Color.white);
            UIKit.Stretch(_body.rectTransform);
            _face = _body.rectTransform;

            // Left strip: rank on top, suit icon below.
            float stripW = Mathf.Max(14f, w * 0.27f);
            var strip = UIKit.Rect("Strip", _face);
            strip.anchorMin = new Vector2(0, 0);
            strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.offsetMin = new Vector2(3, 3);
            strip.offsetMax = new Vector2(3 + stripW, -3);

            int fontSize = Mathf.Max(10, Mathf.RoundToInt(Mathf.Min(h * 0.2f, stripW * 1.05f)));
            _rank = UIKit.Text(strip, "Rank", "", fontSize, Theme.CardBlack, TextAnchor.UpperCenter, FontStyle.Bold);
            _rank.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rrt = _rank.rectTransform;
            rrt.anchorMin = new Vector2(0, 1);
            rrt.anchorMax = new Vector2(1, 1);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0, -1);
            rrt.sizeDelta = new Vector2(0, fontSize * 1.15f);

            float suitSize = stripW * 0.85f;
            _suit = UIKit.Deco(strip, "Suit", null, Theme.CardBlack);
            _suit.preserveAspect = true;
            var srt = _suit.rectTransform;
            srt.anchorMin = new Vector2(0.5f, 1);
            srt.anchorMax = new Vector2(0.5f, 1);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0, -fontSize * 1.2f - 2f);
            srt.sizeDelta = new Vector2(suitSize, suitSize);

            // Main art area: everything right of the strip.
            _art = UIKit.Deco(_face, "Art", null, Color.white);
            _art.preserveAspect = true;
            var art = _art.rectTransform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(stripW + 5, 5);
            art.offsetMax = new Vector2(-5, -5);

            _back = UIKit.Deco(transform, "Back", CardArt.Back(), Color.white);
            UIKit.Stretch(_back.rectTransform);

            _dim = UIKit.Deco(transform, "Dim", ProcSprites.RoundedRect(radius, 0, 0.5f), new Color(0, 0, 0, 0.45f));
            UIKit.Stretch(_dim.rectTransform);
            _dim.enabled = false;

            ShowBack();
        }

        /// <summary>Shows the given card face up (or its back if faceUp is false). Invalid codes show the back.</summary>
        public void SetCard(string code, bool faceUp)
        {
            CardCode card;
            if (!CardCode.TryParse(code, out card))
            {
                Code = null;
                ShowBack();
                return;
            }
            Code = card.Code;
            Color ink = CardArt.SuitColor(card.Suit);
            _rank.text = card.RankLabel;
            _rank.color = ink;
            _suit.sprite = CardArt.Suit(card.Suit);
            _suit.color = ink;
            _art.sprite = CardArt.Main(card);
            SetFaceUp(faceUp);
        }

        public void ShowBack()
        {
            SetFaceUp(false);
        }

        public void SetFaceUp(bool up)
        {
            FaceUp = up && Code != null;
            _face.gameObject.SetActive(FaceUp);
            _back.enabled = !FaceUp;
        }

        public void SetHighlight(bool on)
        {
            _highlight.enabled = on;
        }

        public void SetDim(bool on)
        {
            _dim.enabled = on;
        }

        public void SetVisible(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        public float Alpha
        {
            get { return _group.alpha; }
            set { _group.alpha = value; }
        }

        public CanvasGroup Group { get { return _group; } }

        /// <summary>Clears anything an animation may have left behind.</summary>
        public void ResetVisual()
        {
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;
            _group.alpha = 1f;
        }

        /// <summary>Flip from back to the given face.</summary>
        public IEnumerator FlipTo(string code, float duration = 0.26f)
        {
            SetCard(code, false);
            ResetVisual();
            yield return Tween.Flip(transform, () => SetFaceUp(true), duration);
        }
    }
}
