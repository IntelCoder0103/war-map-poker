using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.UI;

namespace WarPoker.Cards
{
    /// <summary>
    /// A playing card built from code, in a clean modern style: soft drop shadow, white rounded body,
    /// a narrow left strip with a bold rank above the suit pip, and a large main art area.
    /// Art comes from <see cref="CardArt"/>.
    /// </summary>
    public class CardView : MonoBehaviour
    {
        public RectTransform Rect { get; private set; }
        public string Code { get; private set; }
        public bool FaceUp { get; private set; }

        CanvasGroup _group;
        Image _highlight, _shadow, _body, _suit, _art, _back, _dim;
        Text _rank;
        RectTransform _face;
        int _rankSize, _rankSizeTen;

        public static CardView Create(Transform parent, string name, Vector2 size)
        {
            var rt = UIKit.Rect(name, parent);
            rt.sizeDelta = size;
            var view = rt.gameObject.AddComponent<CardView>();
            view.Build(size, false);
            return view;
        }

        /// <summary>
        /// A small, easy-to-read card: a big rank with the suit under it and no art area.
        /// Used where many cards sit side by side (the showdown ranking board).
        /// </summary>
        public static CardView CreateCompact(Transform parent, string name, Vector2 size)
        {
            var rt = UIKit.Rect(name, parent);
            rt.sizeDelta = size;
            var view = rt.gameObject.AddComponent<CardView>();
            view.Build(size, true);
            return view;
        }

        void Build(Vector2 size, bool compact)
        {
            Rect = (RectTransform)transform;
            _group = UIKit.Group(gameObject);
            _group.blocksRaycasts = false;
            _group.interactable = false;
            float w = size.x, h = size.y;

            _highlight = UIKit.Deco(transform, "Highlight", ProcSprites.SoftRect(), Theme.GoldLight);
            UIKit.Center(_highlight.rectTransform, Vector2.zero, size + new Vector2(40, 40));
            _highlight.enabled = false;

            // Corner radius in UI units; the 9-sliced sprites have a 36 px radius, so scale them to fit.
            float radius = Mathf.Max(3f, w * 0.09f);
            float ppu = 36f / radius;

            // Soft drop shadow, a little below the card.
            _shadow = UIKit.Deco(transform, "Shadow", CardArt.Shadow(), new Color(0f, 0f, 0f, 0.5f));
            _shadow.type = Image.Type.Sliced;
            _shadow.pixelsPerUnitMultiplier = ppu;
            float blur = 24f / ppu;
            var sh = _shadow.rectTransform;
            UIKit.Stretch(sh);
            Vector2 drop = new Vector2(w * 0.015f, -h * 0.03f);
            sh.offsetMin = new Vector2(-blur, -blur) + drop;
            sh.offsetMax = new Vector2(blur, blur) + drop;

            _body = UIKit.Deco(transform, "Body", CardArt.Body(), Color.white);
            _body.type = Image.Type.Sliced;
            _body.pixelsPerUnitMultiplier = ppu;
            UIKit.Stretch(_body.rectTransform);
            _face = _body.rectTransform;

            if (compact)
            {
                BuildCompactFace(w, h);
                FinishBuild(ppu);
                return;
            }

            // Left strip: rank on top, suit pip below.
            float pad = Mathf.Max(2f, w * 0.035f);
            float stripW = Mathf.Max(14f, w * 0.28f);
            var strip = UIKit.Rect("Strip", _face);
            strip.anchorMin = new Vector2(0, 0);
            strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.offsetMin = new Vector2(pad * 0.6f, pad);
            strip.offsetMax = new Vector2(pad * 0.6f + stripW, -pad * 0.5f);

            _rankSize = Mathf.Max(10, Mathf.RoundToInt(Mathf.Min(h * 0.22f, stripW * 1.15f)));
            _rankSizeTen = Mathf.Max(9, Mathf.RoundToInt(_rankSize * 0.8f));
            _rank = UIKit.Text(strip, "Rank", "", _rankSize, Theme.CardBlack, TextAnchor.UpperCenter, FontStyle.Bold);
            _rank.horizontalOverflow = HorizontalWrapMode.Overflow;
            _rank.verticalOverflow = VerticalWrapMode.Overflow;
            var rrt = _rank.rectTransform;
            rrt.anchorMin = new Vector2(0, 1);
            rrt.anchorMax = new Vector2(1, 1);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0, 0);
            rrt.sizeDelta = new Vector2(0, _rankSize * 1.1f);

            float suitSize = stripW * 0.80f;
            _suit = UIKit.Deco(strip, "Suit", null, Theme.CardBlack);
            _suit.preserveAspect = true;
            var srt = _suit.rectTransform;
            srt.anchorMin = new Vector2(0.5f, 1);
            srt.anchorMax = new Vector2(0.5f, 1);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0, -_rankSize * 1.02f);
            srt.sizeDelta = new Vector2(suitSize, suitSize);

            // Main art area: everything right of the strip.
            _art = UIKit.Deco(_face, "Art", null, Color.white);
            _art.preserveAspect = true;
            var art = _art.rectTransform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(pad * 0.6f + stripW + pad * 0.4f, pad);
            art.offsetMax = new Vector2(-pad, -pad);

            FinishBuild(ppu);
        }

        // Compact face: a small suit pip in the top-left corner and the rank big in the middle.
        void BuildCompactFace(float w, float h)
        {
            float pip = Mathf.Max(8f, w * 0.30f), pad = Mathf.Max(2f, w * 0.07f);
            _suit = UIKit.Deco(_face, "Suit", null, Theme.CardBlack);
            _suit.preserveAspect = true;
            var srt = _suit.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(pad, -pad);
            srt.sizeDelta = new Vector2(pip, pip);

            _rankSize = Mathf.Max(12, Mathf.RoundToInt(Mathf.Min(h * 0.62f, w * 0.9f)));
            _rankSizeTen = Mathf.Max(10, Mathf.RoundToInt(Mathf.Min(h * 0.5f, w * 0.6f)));
            _rank = UIKit.Text(_face, "Rank", "", _rankSize, Theme.CardBlack, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rank.horizontalOverflow = HorizontalWrapMode.Overflow;
            _rank.verticalOverflow = VerticalWrapMode.Overflow;
            var rrt = _rank.rectTransform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            // a little below centre, clear of the corner pip
            rrt.offsetMin = new Vector2(0, -h * 0.02f);
            rrt.offsetMax = new Vector2(0, -h * 0.14f);
        }

        void FinishBuild(float ppu)
        {
            _back = UIKit.Deco(transform, "Back", CardArt.Back(), Color.white);
            UIKit.Stretch(_back.rectTransform);

            _dim = UIKit.Deco(transform, "Dim", CardArt.Body(), new Color(0, 0, 0, 0.45f));
            _dim.type = Image.Type.Sliced;
            _dim.pixelsPerUnitMultiplier = ppu;
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
            _rank.fontSize = card.RankLabel.Length > 1 ? _rankSizeTen : _rankSize;
            _rank.color = ink;
            _suit.sprite = CardArt.Suit(card.Suit);
            _suit.color = ink;
            if (_art != null) _art.sprite = CardArt.Main(card);
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
