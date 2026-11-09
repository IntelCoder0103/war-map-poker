using System;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.FX;

namespace WarPoker.UI
{
    /// <summary>
    /// A centred dialog over a dark shade that blocks clicks behind it: ribbon title, a message,
    /// optional inputs, an error line and buttons. Build once, then Show/Hide.
    /// </summary>
    public class Modal : MonoBehaviour
    {
        public RectTransform Panel { get; private set; }
        public Text Title { get; private set; }
        public Text Message { get; private set; }
        public Text Error { get; private set; }
        public bool IsOpen { get { return gameObject.activeSelf; } }

        Vector2 _size;

        public static Modal Create(Transform parent, string name, Vector2 size)
        {
            var shade = UIKit.Image(parent, name, null, new Color(0f, 0f, 0f, 0.55f));
            UIKit.Stretch(shade.rectTransform);
            var m = shade.gameObject.AddComponent<Modal>();
            m._size = size;

            var panel = UIKit.Panel(shade.transform, "Panel", "panel_window", Color.white, 0.7f);
            UIKit.Center(panel.rectTransform, Vector2.zero, size);
            m.Panel = panel.rectTransform;

            var ribbon = UIKit.Panel(panel.transform, "Ribbon", "ribbon", Color.white, 0.8f, true);
            UIKit.Center(ribbon.rectTransform, new Vector2(0, size.y * 0.5f - 4f), new Vector2(Mathf.Min(620, size.x - 120), 120));
            m.Title = UIKit.Text(ribbon.transform, "Title", "", 46, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(m.Title.rectTransform);
            m.Title.rectTransform.offsetMin = new Vector2(130, 10);
            m.Title.rectTransform.offsetMax = new Vector2(-130, -6);
            m.Title.resizeTextForBestFit = true;
            m.Title.resizeTextMinSize = 24;
            m.Title.resizeTextMaxSize = 46;
            UIKit.Title(m.Title, 2f);

            m.Message = UIKit.Text(panel.transform, "Message", "", 30, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            m.Error = UIKit.Text(panel.transform, "Error", "", 26, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(m.Error.rectTransform, new Vector2(0, -size.y * 0.5f + 150), new Vector2(size.x - 80, 40));

            shade.gameObject.SetActive(false);
            return m;
        }

        /// <summary>Places the message text (centre position inside the panel, and its size).</summary>
        public Modal PlaceMessage(Vector2 pos, Vector2 size)
        {
            UIKit.Center(Message.rectTransform, pos, size);
            return this;
        }

        public Button AddButton(string label, Color color, Vector2 pos, Vector2 size, Action onClick)
        {
            var b = UIKit.Button(Panel, label, label, size, color, Theme.ParchmentLight, 34, onClick);
            UIKit.Center((RectTransform)b.transform, pos, size);
            return b;
        }

        /// <summary>A labelled input row: the label on the left, the field on the right.</summary>
        public InputField AddInput(string label, string placeholder, float y, InputField.ContentType type, int maxChars)
        {
            float w = _size.x - 120f, labelW = Mathf.Min(260f, w * 0.4f);
            var l = UIKit.Text(Panel, label + "Label", label, 30, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Center(l.rectTransform, new Vector2(-w * 0.5f + labelW * 0.5f, y), new Vector2(labelW, 70));
            var input = UIKit.Input(Panel, label, placeholder, new Vector2(w - labelW, 76), false);
            UIKit.Center((RectTransform)input.transform, new Vector2(w * 0.5f - (w - labelW) * 0.5f, y), new Vector2(w - labelW, 76));
            input.contentType = type;
            input.characterLimit = maxChars;
            return input;
        }

        public void Show()
        {
            Error.text = "";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Panel.localScale = Vector3.one * 0.6f;
            StartCoroutine(Tween.Scale(Panel, Vector3.one, 0.25f, Ease.OutBack));
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
