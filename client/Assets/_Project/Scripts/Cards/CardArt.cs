using System.Collections.Generic;
using UnityEngine;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Cards
{
    /// <summary>
    /// Card art lookup by convention (replaces a CardArtSet asset, which can't be authored without the Editor):
    ///   main art: Resources/Cards/Art/{code}  (e.g. Cards/Art/Ah) → Cards/Art/default → procedural crest
    ///   back:     Resources/Cards/back → procedural back
    ///   suits:    Resources/Cards/Suits/{c|d|h|s} → procedural suit shape
    /// Drop sprites into those folders (Texture Type: Sprite) and they are used without code changes.
    /// </summary>
    public static class CardArt
    {
        static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>();

        static Sprite Load(string path)
        {
            Sprite s;
            if (Loaded.TryGetValue(path, out s)) return s;
            s = Resources.Load<Sprite>(path);
            Loaded[path] = s; // cache misses too (null)
            return s;
        }

        /// <summary>True when the sprite came from Resources (real art) rather than the procedural fallback.</summary>
        public static bool HasCustomArt(string code)
        {
            return Load("Cards/Art/" + code) != null || Load("Cards/Art/default") != null;
        }

        public static Sprite Main(CardCode card)
        {
            var s = Load("Cards/Art/" + card.Code);
            if (s != null) return s;
            s = Load("Cards/Art/default");
            if (s != null) return s;
            return Crest(card.Suit);
        }

        public static Sprite Back()
        {
            var s = Load("Cards/back");
            return s != null ? s : ProceduralBack();
        }

        public static Sprite Suit(char suit)
        {
            var s = Load("Cards/Suits/" + suit);
            return s != null ? s : ProceduralSuit(suit);
        }

        public static Color SuitColor(char suit)
        {
            return suit == 'h' || suit == 'd' ? Theme.CardRed : Theme.CardBlack;
        }

        // ---------- procedural fallbacks ----------

        /// <summary>White suit silhouette (tint with Image.color).</summary>
        public static Sprite ProceduralSuit(char suit)
        {
            return ProcSprites.FromFn("suit_" + suit, 64, 64, (x, y) =>
                InSuit(suit, x, y) ? Color.white : Color.clear, 4);
        }

        public static bool InSuit(char suit, float x, float y)
        {
            switch (suit)
            {
                case 'h':
                    return ProcSprites.InCircle(x, y, 0.3f, 0.64f, 0.21f) ||
                           ProcSprites.InCircle(x, y, 0.7f, 0.64f, 0.21f) ||
                           ProcSprites.InTri(x, y, 0.1f, 0.58f, 0.9f, 0.58f, 0.5f, 0.06f);
                case 'd':
                    return Mathf.Abs(x - 0.5f) / 0.36f + Mathf.Abs(y - 0.5f) / 0.47f <= 1f;
                case 's':
                    return ProcSprites.InCircle(x, y, 0.31f, 0.4f, 0.19f) ||
                           ProcSprites.InCircle(x, y, 0.69f, 0.4f, 0.19f) ||
                           ProcSprites.InTri(x, y, 0.12f, 0.45f, 0.88f, 0.45f, 0.5f, 0.95f) ||
                           ProcSprites.InTri(x, y, 0.5f, 0.42f, 0.33f, 0.05f, 0.67f, 0.05f);
                default: // clubs
                    return ProcSprites.InCircle(x, y, 0.5f, 0.7f, 0.18f) ||
                           ProcSprites.InCircle(x, y, 0.29f, 0.43f, 0.18f) ||
                           ProcSprites.InCircle(x, y, 0.71f, 0.43f, 0.18f) ||
                           ProcSprites.InBox(x, y, 0.42f, 0.4f, 0.58f, 0.62f) ||
                           ProcSprites.InTri(x, y, 0.5f, 0.45f, 0.34f, 0.05f, 0.66f, 0.05f);
            }
        }

        /// <summary>Placeholder main art: a heraldic shield in the suit colour with the suit on it.</summary>
        public static Sprite Crest(char suit)
        {
            Color main = suit == 'h' || suit == 'd' ? new Color(0.68f, 0.14f, 0.12f) : new Color(0.16f, 0.2f, 0.3f);
            Color gold = Theme.Gold;
            Color paper = new Color(0.96f, 0.93f, 0.85f);
            return ProcSprites.FromFn("crest_" + suit, 128, 160, (x, y) =>
            {
                // map into a square-ish space for the shield
                float sx = x, sy = (y - 0.05f) / 0.9f;
                if (ProcSprites.InShield(sx, sy, 0f))
                {
                    if (!ProcSprites.InShield(sx, sy, 0.035f)) return gold;
                    // suit in the middle of the shield
                    float ux = (sx - 0.5f) / 0.42f + 0.5f, uy = (sy - 0.56f) / 0.42f + 0.5f;
                    if (ux >= 0 && ux <= 1 && uy >= 0 && uy <= 1 && InSuit(suit, ux, uy)) return paper;
                    // faint diagonal hatching for an engraved look
                    bool hatch = Mathf.Repeat((sx + sy) * 28f, 1f) < 0.15f;
                    return hatch ? Color.Lerp(main, Color.black, 0.15f) : main;
                }
                return Color.clear;
            }, 2);
        }

        /// <summary>Crimson back with gold frame and lattice.</summary>
        public static Sprite ProceduralBack()
        {
            Color red = new Color(0.5f, 0.1f, 0.08f);
            Color redDark = new Color(0.36f, 0.06f, 0.05f);
            Color gold = Theme.Gold;
            return ProcSprites.FromFn("card_back", 128, 180, (x, y) =>
            {
                float px = x * 128f, py = y * 180f;
                float d = ProcSprites.RoundBoxDist(px - 64f, py - 90f, 63f, 89f, 12f);
                if (d > 0f) return Color.clear;
                if (d > -4f) return new Color(0.95f, 0.92f, 0.85f);   // white rim
                if (d > -7f) return gold;                            // gold frame
                // diamond lattice
                float a = Mathf.Repeat((px + py) / 16f, 1f), b = Mathf.Repeat((px - py) / 16f, 1f);
                bool line = a < 0.12f || b < 0.12f;
                // central medallion
                float dx = px - 64f, dy = py - 90f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < 26f) return r > 22f ? gold : (ProcSprites.InShield(dx / 40f + 0.5f, dy / 40f + 0.5f, 0f) ? gold : redDark);
                return line ? Color.Lerp(red, gold, 0.45f) : red;
            }, 2);
        }
    }
}
