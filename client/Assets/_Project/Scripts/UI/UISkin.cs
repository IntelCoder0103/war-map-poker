using System.Collections.Generic;
using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>
    /// Sprites from the UI kit (pzUH "Free Fantasy Game GUI" + "Medieval Game Button Pack", CC0).
    /// Paths and 9-slice borders come from ArtCatalog (Resources/Config/art.json), so pieces can be
    /// swapped without code changes.
    /// Returns null when a file is missing; callers then fall back to procedural sprites.
    /// </summary>
    public static class UISkin
    {
        public enum ButtonColor { Red, Green, Gold, Blue, Wood }

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string name)
        {
            Sprite s;
            if (Cache.TryGetValue(name, out s)) return s;
            var piece = ArtCatalog.SkinPiece(name); // path + 9-slice border, see Resources/Config/art.json
            var src = Resources.Load<Sprite>(piece.Path);
            Vector4 border = piece.Border;
            if (src != null && border != Vector4.zero)
            {
                s = Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
                s.name = name;
            }
            else
            {
                s = src;
            }
            Cache[name] = s;
            return s;
        }

        public static bool Available { get { return Get("btn_red") != null; } }

        public static string Key(ButtonColor c)
        {
            switch (c)
            {
                case ButtonColor.Red: return "btn_red";
                case ButtonColor.Green: return "btn_green";
                case ButtonColor.Gold: return "btn_gold";
                case ButtonColor.Blue: return "btn_blue";
                default: return "btn_wood";
            }
        }

        public static string DisabledKey(ButtonColor c)
        {
            return c == ButtonColor.Wood ? "btn_wood_disabled" : "btn_disabled";
        }

        /// <summary>Maps the old theme colours used across the screens to a kit button colour.</summary>
        public static ButtonColor FromThemeColor(Color c)
        {
            if (Near(c, Theme.Crimson) || Near(c, Theme.CrimsonDark)) return ButtonColor.Red;
            if (Near(c, Theme.Forest) || Near(c, Theme.Positive)) return ButtonColor.Green;
            if (Near(c, Theme.Navy)) return ButtonColor.Blue;
            if (Near(c, Theme.Gold) || Near(c, Theme.GoldLight)) return ButtonColor.Gold;
            return ButtonColor.Wood;
        }

        static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.05f;
        }
    }
}
