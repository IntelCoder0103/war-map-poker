using UnityEngine;
using UnityEngine.UI;
using WarPoker.FX;

namespace WarPoker.UI
{
    /// <summary>Round button that turns sound effects on and off; the icon shows the current setting.</summary>
    public class SoundToggle : MonoBehaviour
    {
        Image _icon;
        Button _button;

        /// <summary>Creates the button; place it with UIKit.Center or UIKit.Place. The icon is 128x137.</summary>
        public static SoundToggle Create(Transform parent, float height)
        {
            var img = UIKit.Image(parent, "SoundToggle", null, Color.white);
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(height * 128f / 137f, height);
            var t = img.gameObject.AddComponent<SoundToggle>();
            t._icon = img;
            t._button = img.gameObject.AddComponent<Button>();
            t._button.targetGraphic = img;
            t._button.transition = Selectable.Transition.SpriteSwap;
            t._button.onClick.AddListener(t.Toggle);
            t.Refresh();
            return t;
        }

        void OnEnable()
        {
            // another screen's toggle may have changed the setting
            if (_icon != null) Refresh();
        }

        void Toggle()
        {
            Sfx.On = !Sfx.On;
            Refresh();
            Sfx.Play("click");
        }

        void Refresh()
        {
            string key = Sfx.On ? "icon_sound" : "icon_mute";
            _icon.sprite = UISkin.Get(key);
            var st = _button.spriteState;
            st.pressedSprite = UISkin.Get(key + "_pressed");
            st.selectedSprite = null;
            _button.spriteState = st;
        }
    }
}
