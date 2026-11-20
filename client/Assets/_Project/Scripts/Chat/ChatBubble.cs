using UnityEngine;
using UnityEngine.UI;
using WarPoker.FX;
using WarPoker.Net;
using WarPoker.UI;

namespace WarPoker.Chat
{
    /// <summary>
    /// What pops up over a camp when someone chats: a big emoji (playing its frames when animated)
    /// or a short message on a parchment speech bubble. It pops in, bobs gently, then fades away.
    /// Runs on its own Update, so the game screen stopping its animations doesn't freeze it.
    /// </summary>
    public class ChatBubble : MonoBehaviour
    {
        const float EmojiSize = 124f, MaxTextWidth = 480f, Life = 3.2f, FadeTime = 0.35f;

        RectTransform _rt;
        CanvasGroup _group;
        Vector2 _home;
        float _t;
        bool _emoji;

        /// <summary>Shows an item at `anchor` (board position of the camp's centre).</summary>
        public static ChatBubble Show(RectTransform layer, ChatItemDto item, Sprite[] frames, Vector2 anchor, Vector2 boardHalf)
        {
            var rt = UIKit.Rect("Bubble", layer);
            var b = rt.gameObject.AddComponent<ChatBubble>();
            b._rt = rt;
            b._group = UIKit.Group(rt.gameObject);
            b._group.blocksRaycasts = false;
            b._emoji = item.IsEmoji;

            Vector2 size;
            bool below = false;
            if (item.IsEmoji)
            {
                size = new Vector2(EmojiSize, EmojiSize);
                var img = UIKit.Deco(rt, "Emoji", frames != null ? frames[0] : null, Color.white);
                img.preserveAspect = true;
                UIKit.Stretch(img.rectTransform);
                if (frames != null && frames.Length > 1) SpriteAnim.On(img).Play(frames, Mathf.Max(2, item.fps), true);
            }
            else
            {
                var text = UIKit.Text(rt, "Text", item.text, 32, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = Mathf.Clamp(text.preferredWidth + 44f, 120f, MaxTextWidth);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                float h = w >= MaxTextWidth ? 100f : 68f; // long lines wrap to two rows
                size = new Vector2(w, h);

                var tail = UIKit.Deco(rt, "Tail", ProcSprites.RoundedRect(4, 3, 0.55f), Theme.ParchmentLight);
                tail.rectTransform.sizeDelta = new Vector2(26, 26);
                tail.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
                var bg = UIKit.Deco(rt, "Back", ProcSprites.RoundedRect(18, 3, 0.55f), Theme.ParchmentLight);
                UIKit.Stretch(bg.rectTransform);
                text.transform.SetAsLastSibling();
                UIKit.Stretch(text.rectTransform, 0f);
                text.rectTransform.offsetMin = new Vector2(20, 6);
                text.rectTransform.offsetMax = new Vector2(-20, -6);

                // Above the camp, unless that's off the top of the board: then below it.
                below = anchor.y + 128f + h * 0.5f > boardHalf.y - 8f;
                tail.rectTransform.anchoredPosition = new Vector2(0, below ? h * 0.5f : -h * 0.5f);
            }

            // Place it over the camp, kept inside the board.
            float up = item.IsEmoji ? 125f : 128f; // clear of the camp's name banner
            Vector2 pos = anchor + new Vector2(0, up);
            if (item.IsEmoji && pos.y + size.y * 0.5f > boardHalf.y - 8f) below = true;
            if (below) pos = anchor - new Vector2(0, up);
            pos.x = Mathf.Clamp(pos.x, -boardHalf.x + size.x * 0.5f + 8f, boardHalf.x - size.x * 0.5f - 8f);
            pos.y = Mathf.Clamp(pos.y, -boardHalf.y + size.y * 0.5f + 8f, boardHalf.y - size.y * 0.5f - 8f);
            UIKit.Center(rt, pos, size);
            b._home = pos;
            rt.localScale = Vector3.zero;
            return b;
        }

        public void Dismiss()
        {
            if (_t < Life - FadeTime) _t = Life - FadeTime;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            // pop in (overshoot), then a gentle bob for emojis, then fade out
            float pop = Mathf.Clamp01(_t / 0.28f);
            float s = pop < 1f ? Ease.OutBack(pop) : 1f;
            _rt.localScale = new Vector3(s, s, 1f);
            float bob = _emoji ? Mathf.Sin(_t * 5f) * 5f : 0f;
            _rt.anchoredPosition = _home + new Vector2(0, bob);
            float fade = Mathf.Clamp01((Life - _t) / FadeTime);
            _group.alpha = fade;
            if (_t >= Life) Destroy(gameObject);
        }
    }
}
