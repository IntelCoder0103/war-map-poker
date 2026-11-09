using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>
    /// Bright mobile-game palette that matches the UI kit (warm wood, cream parchment, glossy
    /// red / green / gold / blue), keeping the Chinese war-theme accents (vermilion and gold).
    /// Old names are kept so every screen picks the new colours up.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Parchment = new Color(0.99f, 0.93f, 0.78f);      // cream panel
        public static readonly Color ParchmentDark = new Color(0.85f, 0.69f, 0.42f);
        public static readonly Color ParchmentLight = new Color(1.00f, 0.98f, 0.92f);
        public static readonly Color Ink = new Color(0.27f, 0.15f, 0.06f);            // kit wood brown
        public static readonly Color InkSoft = new Color(0.48f, 0.33f, 0.18f);
        public static readonly Color Crimson = new Color(0.90f, 0.16f, 0.12f);        // bright vermilion
        public static readonly Color CrimsonDark = new Color(0.62f, 0.07f, 0.05f);
        public static readonly Color Gold = new Color(1.00f, 0.76f, 0.10f);           // kit gold
        public static readonly Color GoldLight = new Color(1.00f, 0.90f, 0.45f);
        public static readonly Color Forest = new Color(0.42f, 0.74f, 0.10f);         // kit green
        public static readonly Color Navy = new Color(0.12f, 0.46f, 0.85f);           // kit blue
        public static readonly Color Steel = new Color(0.78f, 0.84f, 0.88f);
        public static readonly Color Leather = new Color(0.50f, 0.29f, 0.11f);        // kit wood
        public static readonly Color Disabled = new Color(0.66f, 0.66f, 0.66f);
        public static readonly Color Shade = new Color(0.05f, 0.03f, 0.02f, 0.55f);
        public static readonly Color CardRed = new Color(0.86f, 0.10f, 0.12f);
        public static readonly Color CardBlack = new Color(0.08f, 0.08f, 0.12f);
        public static readonly Color Positive = new Color(0.36f, 0.78f, 0.16f);
    }
}
