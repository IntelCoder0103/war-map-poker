using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WarPoker.UI
{
    /// <summary>
    /// Small helpers to build uGUI hierarchies from code (no prefabs/scenes needed).
    /// All positions use a centered anchor unless stated otherwise.
    /// </summary>
    public static class UIKit
    {
        static Font _font, _titleFont;

        /// <summary>Body/UI font: Lilita One (OFL), falls back to Unity's built-in font.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.Load<Font>(ArtCatalog.Path("fonts.body"));
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>Title font: Potta One (OFL, brush-inspired), falls back to the body font.</summary>
        public static Font TitleFont
        {
            get
            {
                if (_titleFont == null) _titleFont = Resources.Load<Font>(ArtCatalog.Path("fonts.title"));
                return _titleFont != null ? _titleFont : Font;
            }
        }

        /// <summary>True when the custom (already heavy) font is loaded, so faux-bold is skipped.</summary>
        static bool HeavyFont { get { return Font != null && Font.name != "LegacyRuntime"; } }

        // ---------- canvas ----------

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // ---------- rect helpers ----------

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Fill the parent completely (optionally with an inset).</summary>
        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Anchor at a point of the parent (0..1), offset by pos, with a fixed size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Centered placement (anchor = parent center).</summary>
        public static RectTransform Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            return Place(rt, new Vector2(0.5f, 0.5f), pos, size);
        }

        // ---------- widgets ----------

        public static Image Image(Transform parent, string name, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        /// <summary>Non-interactive image (doesn't block clicks).</summary>
        public static Image Deco(Transform parent, string name, Sprite sprite, Color color)
        {
            var img = Image(parent, name, sprite, color);
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, string content, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            // Lilita One is already a heavy face: Unity's faux bold only smears it.
            t.fontStyle = HeavyFont && style == FontStyle.Bold ? FontStyle.Normal
                : HeavyFont && style == FontStyle.BoldAndItalic ? FontStyle.Italic : style;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Switches a text to the title font with a dark outline (headers, banners).</summary>
        public static Text Title(Text t, float outline = 3f)
        {
            t.font = TitleFont;
            t.fontStyle = FontStyle.Normal;
            AddOutline(t, outline);
            return t;
        }

        /// <summary>Dark outline around text, the usual mobile-game look for text on colour.</summary>
        public static void AddOutline(Graphic g, float distance = 2f, float alpha = 0.85f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.18f, 0.09f, 0.03f, alpha);
            o.effectDistance = new Vector2(distance, -distance);
        }

        /// <summary>A kit panel (9-sliced). borderScale shrinks the frame for small panels.</summary>
        public static Image Panel(Transform parent, string name, string skin, Color tint, float borderScale = 1f, bool deco = false)
        {
            var sprite = UISkin.Get(skin);
            Image img;
            if (sprite == null)
            {
                img = Image(parent, name, ProcSprites.RoundedRect(16, 4, 0.5f), Theme.Parchment * tint);
            }
            else
            {
                img = Image(parent, name, sprite, tint);
                img.type = UnityEngine.UI.Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, borderScale);
            }
            if (deco) img.raycastTarget = false;
            return img;
        }

        /// <summary>Adds a soft drop shadow (built-in Shadow effect) to a graphic.</summary>
        public static void AddShadow(Graphic g, float distance = 2f, float alpha = 0.5f)
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, alpha);
            sh.effectDistance = new Vector2(distance, -distance);
        }

        /// <summary>Button in one of the kit colours (theme colour picks the closest kit colour).</summary>
        public static Button Button(Transform parent, string name, string label, Vector2 size, Color bg,
            Color textColor, int fontSize, Action onClick)
        {
            if (UISkin.Available)
                return SkinButton(parent, name, label, size, UISkin.FromThemeColor(bg), fontSize, onClick);
            return PlainButton(parent, name, label, size, bg, textColor, fontSize, onClick);
        }

        public static Button SkinButton(Transform parent, string name, string label, Vector2 size,
            UISkin.ButtonColor color, int fontSize, Action onClick)
        {
            var img = Image(parent, name, UISkin.Get(UISkin.Key(color)), Color.white);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            // keep the frame in proportion to the button height
            img.pixelsPerUnitMultiplier = Mathf.Clamp(150f / Mathf.Max(40f, size.y), 1f, 3f) * (color == UISkin.ButtonColor.Wood ? 0.9f : 1f);
            img.rectTransform.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.SpriteSwap;
            var st = btn.spriteState;
            st.pressedSprite = UISkin.Get(UISkin.Key(color) + "_pressed");
            st.selectedSprite = null;
            st.disabledSprite = UISkin.Get(UISkin.DisabledKey(color));
            btn.spriteState = st;
            var t = Text(img.transform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 6f);
            t.rectTransform.offsetMin = new Vector2(14, 6);
            t.rectTransform.offsetMax = new Vector2(-14, -4);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(10, fontSize / 2);
            t.resizeTextMaxSize = fontSize;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(t, 2f);
            btn.onClick.AddListener(() => WarPoker.FX.Sfx.Play("click"));
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        static Button PlainButton(Transform parent, string name, string label, Vector2 size, Color bg,
            Color textColor, int fontSize, Action onClick)
        {
            var img = Image(parent, name, ProcSprites.RoundedRect(14, 3, 0.45f), bg);
            img.rectTransform.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var t = Text(img.transform, "Label", label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 4f);
            btn.onClick.AddListener(() => WarPoker.FX.Sfx.Play("click"));
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static Text ButtonLabel(Button b)
        {
            return b.GetComponentInChildren<Text>(true);
        }

        /// <summary>Text input built with uGUI's DefaultControls, then restyled.</summary>
        public static InputField Input(Transform parent, string name, string placeholder, Vector2 size, bool password)
        {
            var res = new DefaultControls.Resources();
            var go = DefaultControls.CreateInputField(res);
            go.name = name;
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;

            var bg = go.GetComponent<Image>();
            var plank = UISkin.Get("plank");
            bool dark = plank != null;
            bg.sprite = dark ? plank : ProcSprites.RoundedRect(10, 3, 0.6f);
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = dark ? Color.white : Theme.ParchmentLight;
            Color ink = dark ? Theme.ParchmentLight : Theme.Ink;

            var input = go.GetComponent<InputField>();
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.font = Font;
                t.fontSize = Mathf.RoundToInt(size.y * 0.42f);
                t.alignment = TextAnchor.MiddleLeft;
                t.supportRichText = false;
                var trt = t.rectTransform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(16, 4);
                trt.offsetMax = new Vector2(-16, -4);
            }
            input.textComponent.color = ink;
            var ph = input.placeholder as Text;
            if (ph != null)
            {
                ph.text = placeholder;
                ph.color = new Color(ink.r, ink.g, ink.b, 0.45f);
                ph.fontStyle = FontStyle.Normal;
            }
            input.lineType = InputField.LineType.SingleLine;
            if (password)
            {
                input.contentType = InputField.ContentType.Password;
                input.asteriskChar = '*';
            }
            input.caretColor = ink;
            input.selectionColor = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.45f);
            input.customCaretColor = true;
            return input;
        }

        /// <summary>Horizontal slider built with DefaultControls, restyled.</summary>
        public static Slider Slider(Transform parent, string name, Vector2 size)
        {
            var res = new DefaultControls.Resources();
            var go = DefaultControls.CreateSlider(res);
            go.name = name;
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;

            var slider = go.GetComponent<Slider>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            var rounded = ProcSprites.RoundedRect(8, 2, 0.5f);

            // Background is the first Image child named "Background".
            var bgT = go.transform.Find("Background");
            if (bgT != null)
            {
                var bg = bgT.GetComponent<Image>();
                var plank = UISkin.Get("plank");
                bg.sprite = plank != null ? plank : rounded; bg.type = UnityEngine.UI.Image.Type.Sliced;
                bg.color = plank != null ? Color.white : Theme.ParchmentDark;
                bg.pixelsPerUnitMultiplier = 2f;
            }
            if (slider.fillRect != null)
            {
                var fill = slider.fillRect.GetComponent<Image>();
                var fillSprite = UISkin.Get("bar_fill");
                if (fill != null)
                {
                    fill.sprite = fillSprite != null ? fillSprite : rounded; fill.type = UnityEngine.UI.Image.Type.Sliced;
                    fill.color = fillSprite != null ? Theme.GoldLight : Theme.Crimson;
                    fill.pixelsPerUnitMultiplier = 2f;
                }
            }
            if (slider.handleRect != null)
            {
                var handle = slider.handleRect.GetComponent<Image>();
                var knob = UISkin.Get("round_gold");
                if (handle != null)
                {
                    handle.sprite = knob != null ? knob : ProcSprites.Disc(64, 0.2f);
                    handle.color = knob != null ? Color.white : Theme.Gold;
                    handle.preserveAspect = true;
                }
                slider.handleRect.sizeDelta = knob != null ? new Vector2(size.y * 1.6f, size.y * 0.9f) : new Vector2(size.y * 1.2f, size.y * 0.6f);
            }
            return slider;
        }

        public static CanvasGroup Group(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        public static string Num(long n)
        {
            return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
