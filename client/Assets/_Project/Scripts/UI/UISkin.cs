using System.Collections.Generic;
using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>
    /// Sprites from the UI kit in Resources/UI (pzUH "Free Fantasy Game GUI" + "Medieval Game Button
    /// Pack", CC0). Each sprite gets its 9-slice border here, so panels and buttons stretch cleanly.
    /// Returns null when a file is missing; callers then fall back to procedural sprites.
    /// </summary>
    public static class UISkin
    {
        public enum ButtonColor { Red, Green, Gold, Blue, Wood }

        // name -> border (left, bottom, right, top) in source pixels
        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "btn_red", new Vector4(46, 44, 46, 44) }, { "btn_red_pressed", new Vector4(46, 44, 46, 44) },
            { "btn_green", new Vector4(46, 44, 46, 44) }, { "btn_green_pressed", new Vector4(46, 44, 46, 44) },
            { "btn_gold", new Vector4(46, 44, 46, 44) }, { "btn_gold_pressed", new Vector4(46, 44, 46, 44) },
            { "btn_blue", new Vector4(46, 44, 46, 44) }, { "btn_blue_pressed", new Vector4(46, 44, 46, 44) },
            { "btn_disabled", new Vector4(46, 44, 46, 44) },
            { "btn_wood", new Vector4(30, 30, 30, 30) }, { "btn_wood_pressed", new Vector4(30, 30, 30, 30) },
            { "btn_wood_disabled", new Vector4(30, 30, 30, 30) },
            { "panel_window", new Vector4(110, 110, 110, 110) },
            { "panel_wide", new Vector4(80, 80, 80, 80) },
            { "panel_gold", new Vector4(14, 14, 14, 14) },
            { "plank", new Vector4(24, 24, 24, 24) },
            { "ribbon", new Vector4(150, 0, 150, 0) },
            { "bar_frame", new Vector4(92, 24, 24, 24) },
            { "bar_fill", new Vector4(12, 12, 12, 12) },
        };

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string name)
        {
            Sprite s;
            if (Cache.TryGetValue(name, out s)) return s;
            var src = Resources.Load<Sprite>("UI/" + name);
            Vector4 border;
            if (src != null && Borders.TryGetValue(name, out border))
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
