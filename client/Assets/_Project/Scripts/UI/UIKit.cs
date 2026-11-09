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
        static Font _font;

        /// <summary>Unity 2022.3 built-in font for legacy Text.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

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
            t.fontStyle = style;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Adds a soft drop shadow (built-in Shadow effect) to a graphic.</summary>
        public static void AddShadow(Graphic g, float distance = 2f, float alpha = 0.5f)
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, alpha);
            sh.effectDistance = new Vector2(distance, -distance);
        }

        public static Button Button(Transform parent, string name, string label, Vector2 size, Color bg,
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
            bg.sprite = ProcSprites.RoundedRect(10, 3, 0.6f);
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = Theme.ParchmentLight;

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
            input.textComponent.color = Theme.Ink;
            var ph = input.placeholder as Text;
            if (ph != null)
            {
                ph.text = placeholder;
                ph.color = new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.45f);
                ph.fontStyle = FontStyle.Italic;
            }
            input.lineType = InputField.LineType.SingleLine;
            if (password)
            {
                input.contentType = InputField.ContentType.Password;
                input.asteriskChar = '*';
            }
            input.caretColor = Theme.Ink;
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
                bg.sprite = rounded; bg.type = UnityEngine.UI.Image.Type.Sliced; bg.color = Theme.ParchmentDark;
            }
            if (slider.fillRect != null)
            {
                var fill = slider.fillRect.GetComponent<Image>();
                if (fill != null) { fill.sprite = rounded; fill.type = UnityEngine.UI.Image.Type.Sliced; fill.color = Theme.Crimson; }
            }
            if (slider.handleRect != null)
            {
                var handle = slider.handleRect.GetComponent<Image>();
                if (handle != null) { handle.sprite = ProcSprites.Disc(64, 0.2f); handle.color = Theme.Gold; }
                slider.handleRect.sizeDelta = new Vector2(size.y * 1.2f, size.y * 0.6f);
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
