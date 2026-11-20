using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.Net;
using WarPoker.UI;

namespace WarPoker.Chat
{
    /// <summary>
    /// The chat button at the table and the picker it opens: an Emojis tab (a grid) and a Messages tab
    /// (a list), plus a "hide others' messages" switch. Picking one calls OnSend and closes the picker.
    /// After sending, the button shows the server's cooldown as a shrinking dark wedge.
    /// </summary>
    public class ChatPicker : MonoBehaviour
    {
        const string HideOthersKey = "chat.hideOthers";
        static readonly Vector2 PanelSize = new Vector2(820, 720);
        // Both tabs scroll inside the same area, so items can be big enough for a finger.
        static readonly Vector2 ViewSize = new Vector2(700, 486);
        // Emojis: 5 columns of big tiles.
        const int Cols = 5, MaxEmojis = 30;
        const float Cell = 124f, EmojiSize = 108f;
        // Messages: one column of big buttons.
        static readonly Vector2 MsgSize = new Vector2(640, 76);
        const float MsgGap = 10f;
        const int MaxMessages = 20;

        public Action<ChatItemDto> OnSend;

        ChatCatalog _catalog;
        RectTransform _root, _panel, _emojiGrid, _msgList;
        ScrollRect _emojiScroll, _msgScroll;
        Button _button, _emojiTab, _msgTab;
        Image _cooldown;
        Text _empty, _hideLabel;
        Image _hideBox, _hideTick;
        bool _showEmojis = true, _dirty = true;
        float _readyAt, _cooldownLength = 1f;

        public static bool HideOthers
        {
            get { return PlayerPrefs.GetInt(HideOthersKey, 0) == 1; }
            set { PlayerPrefs.SetInt(HideOthersKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Builds the button at buttonPos (board coordinates) and the picker above it.
        /// The picker's bottom-left corner sits at panelCorner.
        /// </summary>
        public static ChatPicker Create(RectTransform board, ChatCatalog catalog, Vector2 buttonPos, Vector2 panelCorner)
        {
            var host = UIKit.Rect("Chat", board);
            UIKit.Stretch(host);
            var p = host.gameObject.AddComponent<ChatPicker>();
            p._catalog = catalog;
            p.Build(host, buttonPos, panelCorner);
            catalog.Changed += p.MarkDirty;
            return p;
        }

        void OnDestroy()
        {
            if (_catalog != null) _catalog.Changed -= MarkDirty;
        }

        void MarkDirty() { _dirty = true; }

        void Build(RectTransform host, Vector2 buttonPos, Vector2 corner)
        {
            // The round button. Its own Image is the click target, so no overlay blocks it.
            float h = 96f;
            var icon = UIKit.Image(host, "ChatButton", UISkin.Get("icon_chat"), Color.white);
            icon.preserveAspect = true;
            UIKit.Center(icon.rectTransform, buttonPos, new Vector2(h * 128f / 137f, h));
            _button = icon.gameObject.AddComponent<Button>();
            _button.targetGraphic = icon;
            _button.transition = Selectable.Transition.SpriteSwap;
            var st = _button.spriteState;
            st.pressedSprite = UISkin.Get("icon_chat_pressed");
            _button.spriteState = st;
            _button.onClick.AddListener(Toggle);

            _cooldown = UIKit.Deco(icon.transform, "Cooldown", ProcSprites.Circle(64), new Color(0f, 0f, 0f, 0.5f));
            _cooldown.type = Image.Type.Filled;
            _cooldown.fillMethod = Image.FillMethod.Radial360;
            _cooldown.fillOrigin = (int)Image.Origin360.Top;
            _cooldown.fillClockwise = false;
            UIKit.Center(_cooldown.rectTransform, new Vector2(0, h * 0.02f), new Vector2(h * 0.74f, h * 0.74f));
            _cooldown.enabled = false;

            // Picker: a click-catcher over the board (tap outside to close) and the panel on top of it.
            _root = UIKit.Rect("Picker", host);
            UIKit.Stretch(_root);
            var catcher = UIKit.Image(_root, "CloseArea", null, new Color(0f, 0f, 0f, 0.25f));
            UIKit.Stretch(catcher.rectTransform, -2000f); // reach past the board's edges on wide screens
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var panel = UIKit.Panel(_root, "Panel", "panel_window", Color.white, 0.6f);
            _panel = panel.rectTransform;
            UIKit.Center(_panel, corner + PanelSize * 0.5f, PanelSize);

            _emojiTab = UIKit.Button(_panel, "EmojiTab", Strings.Get("ChatEmojis"), new Vector2(240, 64), Theme.Gold, Theme.ParchmentLight, 28, () => SetTab(true));
            UIKit.Center((RectTransform)_emojiTab.transform, new Vector2(-130, PanelSize.y * 0.5f - 72), new Vector2(240, 64));
            _msgTab = UIKit.Button(_panel, "MessageTab", Strings.Get("ChatMessages"), new Vector2(240, 64), Theme.Leather, Theme.ParchmentLight, 28, () => SetTab(false));
            UIKit.Center((RectTransform)_msgTab.transform, new Vector2(130, PanelSize.y * 0.5f - 72), new Vector2(240, 64));

            _emojiScroll = BuildScroll("Emojis", out _emojiGrid);
            _msgScroll = BuildScroll("Messages", out _msgList);

            _empty = UIKit.Text(_panel, "Empty", "", 28, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_empty.rectTransform, new Vector2(0, -8), new Vector2(600, 200));

            // "Hide others' messages": a little box with a tick, and its label; the whole row is the button.
            var row = UIKit.Image(_panel, "HideOthers", null, new Color(0, 0, 0, 0));
            UIKit.Center(row.rectTransform, new Vector2(0, -PanelSize.y * 0.5f + 70), new Vector2(560, 50));
            row.gameObject.AddComponent<Button>().onClick.AddListener(() => { HideOthers = !HideOthers; Sfx.Play("click"); RefreshHide(); });
            _hideBox = UIKit.Deco(row.transform, "Box", ProcSprites.RoundedRect(8, 3, 0.35f), Color.white);
            UIKit.Center(_hideBox.rectTransform, new Vector2(-230, 0), new Vector2(40, 40));
            _hideTick = UIKit.Deco(_hideBox.transform, "Tick", ProcSprites.RoundedRect(6, 0, 1f), Theme.Forest);
            UIKit.Center(_hideTick.rectTransform, Vector2.zero, new Vector2(24, 24));
            _hideLabel = UIKit.Text(row.transform, "Label", Strings.Get("ChatHideOthers"), 26, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Center(_hideLabel.rectTransform, new Vector2(40, 0), new Vector2(480, 46));

            _root.gameObject.SetActive(false);
            RefreshHide();
        }

        // A clipped viewport that scrolls a content column, with a slim scrollbar on the right.
        ScrollRect BuildScroll(string name, out RectTransform content)
        {
            var view = UIKit.Image(_panel, name, null, new Color(0, 0, 0, 0)); // also catches drags between items
            var viewRt = view.rectTransform;
            UIKit.Center(viewRt, new Vector2(0, -4), ViewSize);
            view.gameObject.AddComponent<RectMask2D>();

            content = UIKit.Rect("Content", viewRt);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewRt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;

            var track = UIKit.Image(viewRt, "Scrollbar", ProcSprites.RoundedRect(6, 0, 1f), new Color(0f, 0f, 0f, 0.12f));
            UIKit.Place(track.rectTransform, new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(12, ViewSize.y - 8));
            var area = UIKit.Rect("Area", track.rectTransform);
            UIKit.Stretch(area);
            var handle = UIKit.Image(area, "Handle", ProcSprites.RoundedRect(6, 0, 1f), Theme.Leather);
            UIKit.Stretch(handle.rectTransform);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        static void ResetScroll(ScrollRect scroll, float height)
        {
            scroll.content.sizeDelta = new Vector2(0, height);
            scroll.StopMovement();
            scroll.content.anchoredPosition = Vector2.zero; // back to the top
        }

        public bool IsOpen { get { return _root.gameObject.activeSelf; } }

        public void SetVisible(bool on)
        {
            if (_button.gameObject.activeSelf != on) _button.gameObject.SetActive(on);
            if (!on) Close();
        }

        /// <summary>Opens the picker, or closes it if it's open (the chat button, or a tap on my own camp).</summary>
        public void Toggle()
        {
            if (IsOpen) { Close(); return; }
            if (_catalog.Version < 0) _catalog.Refresh(); // never loaded (server unreachable at connect): try again
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _panel.localScale = Vector3.one * 0.85f;
            StartCoroutine(Tween.Scale(_panel, Vector3.one, 0.18f, Ease.OutBack));
            _dirty = true;
        }

        public void Close()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void SetTab(bool emojis)
        {
            _showEmojis = emojis;
            _dirty = true;
        }

        void RefreshHide()
        {
            _hideTick.enabled = HideOthers;
        }

        void Update()
        {
            // Cooldown wedge on the button.
            float left = _readyAt - Time.unscaledTime;
            bool cooling = left > 0f;
            if (_cooldown.enabled != cooling) _cooldown.enabled = cooling;
            if (cooling) _cooldown.fillAmount = Mathf.Clamp01(left / _cooldownLength);

            if (_dirty && IsOpen)
            {
                _dirty = false;
                Rebuild();
            }
        }

        readonly List<GameObject> _cells = new List<GameObject>();

        void Rebuild()
        {
            foreach (var c in _cells) Destroy(c);
            _cells.Clear();

            SkinTab(_emojiTab, _showEmojis);
            SkinTab(_msgTab, !_showEmojis);
            _emojiScroll.gameObject.SetActive(_showEmojis);
            _msgScroll.gameObject.SetActive(!_showEmojis);

            int n = 0;
            if (_showEmojis)
            {
                foreach (var item in _catalog.Emojis)
                {
                    if (n >= MaxEmojis) break;
                    int col = n % Cols, rowIdx = n / Cols;
                    float left = -(ViewSize.x - Cols * Cell) * 0.5f + 6f; // room for the scrollbar
                    var pos = new Vector2(left - Cols * Cell * 0.5f + Cell * (col + 0.5f), -Cell * (rowIdx + 0.5f));
                    _cells.Add(EmojiCell(item, pos));
                    n++;
                }
                ResetScroll(_emojiScroll, Cell * ((n + Cols - 1) / Cols));
            }
            else
            {
                float stride = MsgSize.y + MsgGap, left = -(ViewSize.x - MsgSize.x) * 0.5f + 6f; // room for the scrollbar
                foreach (var item in _catalog.Messages)
                {
                    if (n >= MaxMessages) break;
                    _cells.Add(MessageCell(item, new Vector2(left, -MsgGap * 0.5f - stride * n - MsgSize.y * 0.5f)));
                    n++;
                }
                ResetScroll(_msgScroll, stride * n);
            }
            _empty.text = n == 0 ? Strings.Get(_catalog.Version < 0 ? "ChatLoading" : "ChatEmpty") : "";
        }

        static void SkinTab(Button b, bool active)
        {
            var img = (Image)b.targetGraphic;
            var color = active ? UISkin.ButtonColor.Gold : UISkin.ButtonColor.Wood;
            var sprite = UISkin.Get(UISkin.Key(color));
            if (sprite != null) img.sprite = sprite;
            else img.color = active ? Theme.Gold : Theme.Leather;
        }

        GameObject EmojiCell(ChatItemDto item, Vector2 pos)
        {
            var bg = UIKit.Image(_emojiGrid, "Emoji_" + item.id, ProcSprites.RoundedRect(14, 2, 0.75f), new Color(1f, 1f, 1f, 0.55f));
            UIKit.Place(bg.rectTransform, new Vector2(0.5f, 1), pos, new Vector2(Cell - 10, Cell - 10));
            var btn = bg.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => Send(item));

            var frames = _catalog.Frames(item);
            var pic = UIKit.Deco(bg.transform, "Picture", frames != null ? frames[0] : null, Color.white);
            pic.preserveAspect = true;
            UIKit.Center(pic.rectTransform, Vector2.zero, new Vector2(EmojiSize, EmojiSize));
            if (frames == null) pic.color = new Color(1f, 1f, 1f, 0f); // still downloading: an empty tile for now
            else if (frames.Length > 1) SpriteAnim.On(pic).Play(frames, Mathf.Max(2, item.fps), true);
            return bg.gameObject;
        }

        GameObject MessageCell(ChatItemDto item, Vector2 pos)
        {
            var b = UIKit.Button(_msgList, "Msg_" + item.id, item.text, MsgSize, Theme.Leather, Theme.ParchmentLight, 32, () => Send(item));
            UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1), pos, MsgSize);
            var label = UIKit.ButtonLabel(b);
            label.resizeTextMinSize = 20;
            return b.gameObject;
        }

        void Send(ChatItemDto item)
        {
            if (Time.unscaledTime < _readyAt) return;
            Close();
            if (OnSend != null) OnSend(item);
        }

        /// <summary>Starts the cooldown wedge (call when the server accepted a message).</summary>
        public void StartCooldown(float seconds)
        {
            _cooldownLength = Mathf.Max(0.1f, seconds);
            _readyAt = Time.unscaledTime + seconds;
        }
    }
}
