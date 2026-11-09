using System.Collections.Generic;
using UnityEngine;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Cards
{
    /// <summary>
    /// Card art lookup by convention (replaces a CardArtSet asset, which can't be authored without the Editor):
    ///   main art: Resources/Cards/Art/{code}  (e.g. Cards/Art/Ah) → Cards/Art/default → procedural placeholder
    ///   back:     Resources/Cards/back → procedural back
    ///   suits:    Resources/Cards/Suits/{c|d|h|s} → procedural suit pip
    /// Drop sprites into those folders (Texture Type: Sprite) and they are used without code changes.
    ///
    /// The procedural art is drawn at 2-4× the on-screen size into mipmapped textures, so it stays crisp
    /// on high-density phones and doesn't shimmer when drawn small.
    /// </summary>
    public static class CardArt
    {
        static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>();

        static string ArtFolder { get { return ArtCatalog.Path("cards.art"); } }

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
            return Load(ArtFolder + "/" + code) != null || Load(ArtFolder + "/default") != null;
        }

        public static Sprite Main(CardCode card)
        {
            var s = Load(ArtFolder + "/" + card.Code);
            if (s != null) return s;
            s = Load(ArtFolder + "/default");
            if (s != null) return s;
            return Crest(card.Suit);
        }

        public static Sprite Back()
        {
            var s = Load(ArtCatalog.Path("cards.back"));
            return s != null ? s : ProceduralBack();
        }

        public static Sprite Suit(char suit)
        {
            var s = Load(ArtCatalog.Path("cards.suits") + "/" + suit);
            return s != null ? s : ProceduralSuit(suit);
        }

        public static Color SuitColor(char suit)
        {
            return suit == 'h' || suit == 'd' ? Theme.CardRed : Theme.CardBlack;
        }

        // ---------- procedural fallbacks ----------

        /// <summary>White suit pip (tint with Image.color). Smooth, modern shapes; mipmapped.</summary>
        public static Sprite ProceduralSuit(char suit)
        {
            return MipSprite("suit2_" + suit, 128, 128, (x, y) =>
                InSuit(suit, x, y) ? Color.white : Color.clear, 4, Color.white, Vector4.zero, 100f);
        }

        /// <summary>Is (x, y) in 0..1 inside the suit shape (fills about 0.08..0.92 of the box)?</summary>
        public static bool InSuit(char suit, float x, float y)
        {
            switch (suit)
            {
                case 'h':
                    return InHeart((x - 0.5f) / 0.43f, (y - 0.47f) / 0.43f);
                case 'd':
                {
                    // rhombus with slightly curved (concave) sides
                    float dx = Mathf.Abs(x - 0.5f) / 0.37f, dy = Mathf.Abs(y - 0.5f) / 0.46f;
                    return Mathf.Pow(dx, 0.88f) + Mathf.Pow(dy, 0.88f) <= 1f;
                }
                case 's':
                    // an upside-down heart on a flared stem
                    return InHeart((x - 0.5f) / 0.42f, (0.58f - y) / 0.42f) || InStem(x, y, 0.06f, 0.42f);
                default: // clubs
                    return ProcSprites.InCircle(x, y, 0.5f, 0.70f, 0.20f) ||
                           ProcSprites.InCircle(x, y, 0.285f, 0.43f, 0.20f) ||
                           ProcSprites.InCircle(x, y, 0.715f, 0.43f, 0.20f) ||
                           ProcSprites.InBox(x, y, 0.40f, 0.42f, 0.60f, 0.62f) ||
                           InStem(x, y, 0.06f, 0.46f);
            }
        }

        /// <summary>Classic heart curve (x²+y²−1)³ − x²y³ ≤ 0, normalized to roughly −1..1.</summary>
        static bool InHeart(float x, float y)
        {
            x *= 1.12f; y = y * 1.18f + 0.12f;
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f;
        }

        /// <summary>Stem with concave flared sides, from the bottom (y0) up to y1.</summary>
        static bool InStem(float x, float y, float y0, float y1)
        {
            if (y < y0 || y > y1) return false;
            float t = (y - y0) / (y1 - y0);                  // 0 at the foot, 1 at the top
            float hw = 0.035f + 0.17f * (1f - t) * (1f - t);   // concave flare
            return Mathf.Abs(x - 0.5f) <= hw;
        }

        /// <summary>
        /// Placeholder main art: a softly tinted panel with a large, light suit symbol and a fine
        /// geometric ring. Neutral enough to sit under any real art the client adds later.
        /// </summary>
        public static Sprite Crest(char suit)
        {
            bool red = suit == 'h' || suit == 'd';
            Color top = red ? new Color(1.00f, 0.95f, 0.95f) : new Color(0.95f, 0.96f, 0.97f);
            Color bottom = red ? new Color(0.99f, 0.86f, 0.87f) : new Color(0.85f, 0.88f, 0.91f);
            Color edge = red ? new Color(0.93f, 0.72f, 0.74f) : new Color(0.74f, 0.78f, 0.83f);
            Color pipHi = red ? new Color(0.98f, 0.62f, 0.65f) : new Color(0.60f, 0.66f, 0.74f);
            Color pipLo = red ? new Color(0.91f, 0.42f, 0.47f) : new Color(0.40f, 0.46f, 0.55f);
            const int w = 124, h = 248;
            const float aspect = (float)w / h;
            return MipSprite("crest2_" + suit, w, h, (x, y) =>
            {
                float px = x * w, py = y * h;
                float d = ProcSprites.RoundBoxDist(px - w * 0.5f, py - h * 0.5f, w * 0.5f - 1f, h * 0.5f - 1f, 14f);
                if (d > 0f) return Color.clear;
                Color c = Color.Lerp(bottom, top, y);
                if (d > -2.5f) return edge;                                       // crisp panel edge
                // soft glow behind the symbol
                float gx = (x - 0.5f) * aspect, gy = y - 0.5f;
                float gr = Mathf.Sqrt(gx * gx + gy * gy);
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(1f - gr / 0.32f) * 0.55f);
                // fine concentric rings
                float ring = Mathf.Abs(gr - 0.30f), ring2 = Mathf.Abs(gr - 0.33f);
                if (ring < 0.0035f || ring2 < 0.002f) c = Color.Lerp(c, edge, 0.7f);
                // big suit, centred, with a soft vertical gradient
                float s = 0.80f;                                                  // symbol size, as a fraction of width
                float ux = (x - 0.5f) / s + 0.5f, uy = (y - 0.5f) * h / (w * s) + 0.5f;
                if (ux >= 0f && ux <= 1f && uy >= 0f && uy <= 1f && InSuit(suit, ux, uy))
                    c = Color.Lerp(pipLo, pipHi, uy);
                // a few small pips around the ring, like a compass
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * Mathf.PI * 0.5f;
                    float cx = 0.5f + Mathf.Sin(ang) * 0.315f / aspect, cy = 0.5f + Mathf.Cos(ang) * 0.315f;
                    float sx = (x - cx) * w / 13f + 0.5f, sy = (y - cy) * h / 13f + 0.5f;
                    if (sx >= 0f && sx <= 1f && sy >= 0f && sy <= 1f && InSuit(suit, sx, sy)) c = pipLo;
                }
                c.a = 1f;
                return c;
            }, 3, bottom, Vector4.zero, 100f);
        }

        /// <summary>Modern back: deep navy with a fine diamond lattice, a thin gold frame and a compass-star emblem.</summary>
        public static Sprite ProceduralBack()
        {
            const int w = 192, h = 268;
            Color navy = new Color(0.06f, 0.13f, 0.36f);
            Color navyLight = new Color(0.16f, 0.33f, 0.70f);
            Color navyDark = new Color(0.03f, 0.07f, 0.22f);
            Color gold = new Color(1.00f, 0.80f, 0.30f), goldHi = new Color(1.00f, 0.95f, 0.70f), goldLo = new Color(0.78f, 0.52f, 0.12f);
            return MipSprite("card_back2", w, h, (x, y) =>
            {
                float px = x * w, py = y * h;
                float dx = px - w * 0.5f, dy = py - h * 0.5f;
                float d = ProcSprites.RoundBoxDist(dx, dy, w * 0.5f - 1f, h * 0.5f - 1f, 17f);
                if (d > 0f) return Color.clear;
                // radial gradient, lighter in the middle
                float rr = Mathf.Sqrt(dx * dx / (w * w * 0.25f) + dy * dy / (h * h * 0.25f));
                Color c = Color.Lerp(navyLight, navy, Mathf.Clamp01(rr * 1.05f));
                if (d > -2.2f) return navyDark;                                    // dark rim
                // gold frame line, inset
                float f = ProcSprites.RoundBoxDist(dx, dy, w * 0.5f - 11f, h * 0.5f - 11f, 9f);
                if (Mathf.Abs(f) < 1.6f) return Color.Lerp(gold, goldHi, 0.3f);
                if (f > 0f)
                {
                    // outer band: solid, a touch darker
                    c = Color.Lerp(c, navyDark, 0.35f);
                    c.a = 1f;
                    return c;
                }
                // fine diamond lattice with dots at the crossings
                const float cell = 13f;
                float a = Mathf.Repeat((dx + dy) / cell, 1f), b = Mathf.Repeat((dx - dy) / cell, 1f);
                float la = Mathf.Min(a, 1f - a) * cell, lb = Mathf.Min(b, 1f - b) * cell;
                float lineD = Mathf.Min(la, lb);
                c = Color.Lerp(c, navyLight * 1.15f, Mathf.Clamp01(1f - lineD / 0.9f) * 0.45f);
                float cxr = Mathf.Sqrt(la * la + lb * lb);
                if (cxr < 1.8f) c = Color.Lerp(c, gold, 0.55f);
                // second thin frame just inside the gold line
                if (Mathf.Abs(f + 5f) < 0.7f) c = Color.Lerp(c, gold, 0.5f);

                // emblem: a dark disc with a gold ring and a compass star
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                const float R = 46f;
                if (r < R + 7f)
                {
                    if (r > R + 5f) c = Color.Lerp(c, navyDark, 0.6f);            // soft halo edge
                    else if (r > R)
                    {
                        float lit = (-dx * 0.6f + dy * 0.8f) / Mathf.Max(1f, r);
                        c = Color.Lerp(gold, lit > 0 ? goldHi : goldLo, Mathf.Abs(lit) * 0.7f);
                    }
                    else if (r > R - 2f) c = navyDark;
                    else
                    {
                        c = Color.Lerp(navyDark, navy, r / R);
                        if (Mathf.Abs(r - (R - 7f)) < 0.8f) c = Color.Lerp(c, gold, 0.6f);
                        Color star = Star(dx / (R - 9f), dy / (R - 9f), gold, goldHi, goldLo, navyLight);
                        if (star.a > 0f) c = star;
                    }
                }
                c.a = 1f;
                return c;
            }, 2, navyDark, Vector4.zero, 100f);
        }

        /// <summary>Compass star in unit coordinates: four long gold points and four short light-blue ones, bevelled.</summary>
        static Color Star(float x, float y, Color gold, Color hi, Color lo, Color blue)
        {
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            // long points along the axes: |minor| <= w * (1 - |major|)
            float major = Mathf.Max(ax, ay), minor = Mathf.Min(ax, ay);
            bool longPt = major <= 1f && minor <= 0.24f * (1f - major);
            // short points on the diagonals (rotate 45°)
            float rx = (x + y) * 0.7071f, ry = (x - y) * 0.7071f;
            float amaj = Mathf.Max(Mathf.Abs(rx), Mathf.Abs(ry)), amin = Mathf.Min(Mathf.Abs(rx), Mathf.Abs(ry));
            bool shortPt = amaj <= 0.62f && amin <= 0.2f * (1f - amaj / 0.62f);
            if (longPt)
            {
                // bevel: each point is split along its axis into a light and a dark half
                bool onX = ax > ay;
                float side = onX ? y * Mathf.Sign(x) : -x * Mathf.Sign(y);
                Color c = side > 0f ? hi : Color.Lerp(gold, lo, 0.35f);
                c.a = 1f;
                return c;
            }
            if (shortPt)
            {
                Color c = (rx * ry > 0f) ? Color.Lerp(blue, Color.white, 0.55f) : blue;
                c.a = 1f;
                return c;
            }
            if (x * x + y * y < 0.012f) return hi;
            return Color.clear;
        }

        // ---------- card body, shadow ----------

        /// <summary>
        /// White card body with a hairline grey edge, 9-sliced. The corner radius is 36 sprite pixels,
        /// so set Image.pixelsPerUnitMultiplier = 36 / wantedRadius.
        /// </summary>
        public static Sprite Body()
        {
            Sprite s;
            if (ProcSprites.TryGet("card_body2", out s)) return s;
            const int size = 96;
            const float R = 36f, edge = 5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = ProcSprites.RoundBoxDist(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f, size * 0.5f - 0.5f, size * 0.5f - 0.5f, R);
                    float a = Mathf.Clamp01(0.5f - d);
                    Color c = Color.Lerp(Color.white, new Color(0.78f, 0.79f, 0.82f), Mathf.Clamp01(d + edge + 0.5f));
                    c.a = a;
                    px[y * size + x] = c;
                }
            var tex = MakeMipTexture(size, size, px);
            return ProcSprites.Store("card_body2", Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(R + 4, R + 4, R + 4, R + 4)));
        }

        /// <summary>Soft rounded shadow, 9-sliced; the blur spans 24 sprite pixels outside a 36-pixel radius box.</summary>
        public static Sprite Shadow()
        {
            Sprite s;
            if (ProcSprites.TryGet("card_shadow2", out s)) return s;
            const int size = 160;
            const float R = 36f, blur = 24f;
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = ProcSprites.RoundBoxDist(x + 0.5f - half, y + 0.5f - half, half - blur, half - blur, R);
                    float t = Mathf.Clamp01(1f - (d + blur * 0.35f) / blur);
                    px[y * size + x] = new Color(0f, 0f, 0f, t * t * (3f - 2f * t));
                }
            var tex = MakeMipTexture(size, size, px);
            float b = blur + R + 2f;
            return ProcSprites.Store("card_shadow2", Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b)));
        }

        // ---------- helpers ----------

        static Sprite MipSprite(string key, int w, int h, PixelFn fn, int ss, Color bleed, Vector4 border, float ppu)
        {
            Sprite s;
            if (ProcSprites.TryGet(key, out s)) return s;
            var px = ProcSprites.Raster(w, h, fn, ss);
            // transparent texels take the bleed colour so mip levels don't get dark fringes
            for (int i = 0; i < px.Length; i++)
                if (px[i].a <= 0.0001f) px[i] = new Color(bleed.r, bleed.g, bleed.b, 0f);
            var tex = MakeMipTexture(w, h, px);
            return ProcSprites.Store(key, Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border));
        }

        static Texture2D MakeMipTexture(int w, int h, Color[] px)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 0;
            tex.mipMapBias = -0.3f;
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
