using UnityEngine;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// The war-map background in an old Chinese style. Uses Resources/Map/background if present;
    /// otherwise draws one procedurally: aged rice paper with the faint square grid of old Chinese
    /// maps, ink-wash mountains fading into mist, a river with brushed waves, the Great Wall on a
    /// far ridge, pines, walled towns, a red seal, and a vermilion border with the 回 (hui) pattern.
    /// The compass is a bagua disc (Resources/Map/compass override).
    /// </summary>
    public static class MapArt
    {
        const int W = 1152, H = 648;

        // Bright "blue-green landscape" (青绿山水) palette: mineral green and azurite on cream silk.
        static readonly Color Paper = new Color(0.99f, 0.95f, 0.84f);
        static readonly Color PaperAged = new Color(0.94f, 0.85f, 0.66f);
        static readonly Color InkC = new Color(0.13f, 0.12f, 0.12f);
        static readonly Color Water = new Color(0.30f, 0.72f, 0.82f);
        static readonly Color Vermilion = new Color(0.86f, 0.18f, 0.12f);
        static readonly Color GoldC = new Color(1.00f, 0.79f, 0.26f);
        static readonly Color Jade = new Color(0.30f, 0.66f, 0.42f);      // mountain foot
        static readonly Color Azurite = new Color(0.14f, 0.40f, 0.66f);   // mountain top
        static readonly Color PineGreen = new Color(0.10f, 0.40f, 0.24f);

        // Mountain peaks: x (0..1 of width), base y, height, half-width, darkness.
        // Kept to the edges so the battlefield in the middle stays clear.
        static readonly float[,] Peaks =
        {
            // far range along the top (pale)
            { 0.10f, 0.74f, 0.12f, 0.10f, 0.35f }, { 0.22f, 0.75f, 0.09f, 0.08f, 0.30f },
            { 0.36f, 0.77f, 0.08f, 0.09f, 0.28f }, { 0.62f, 0.77f, 0.09f, 0.08f, 0.28f },
            { 0.76f, 0.75f, 0.11f, 0.09f, 0.32f }, { 0.90f, 0.73f, 0.13f, 0.10f, 0.36f },
            // near ranges in the corners (dark)
            { 0.06f, 0.52f, 0.20f, 0.08f, 0.85f }, { 0.14f, 0.50f, 0.14f, 0.07f, 0.75f },
            { 0.93f, 0.50f, 0.21f, 0.08f, 0.85f }, { 0.85f, 0.48f, 0.13f, 0.06f, 0.70f },
            { 0.08f, 0.10f, 0.16f, 0.09f, 0.80f }, { 0.92f, 0.08f, 0.17f, 0.09f, 0.80f },
        };

        public static Sprite Background()
        {
            var custom = Resources.Load<Sprite>("Map/background");
            if (custom != null) return custom;
            Sprite cached;
            if (ProcSprites.TryGet("map_bg_cn", out cached)) return cached;
            var sprite = ProcSprites.MakeSprite(ProcSprites.MakeTexture(W, H, BackgroundPixels()), Vector4.zero);
            return ProcSprites.Store("map_bg_cn", sprite);
        }

        public const int Width = W, Height = H;

        /// <summary>The procedural map as raw pixels (also used by the Editor preview export).</summary>
        public static Color[] BackgroundPixels()
        {
            var px = new Color[W * H];
            float aspect = (float)W / H;
            float band = 0.045f; // border thickness, as a fraction of height

            for (int y = 0; y < H; y++)
            {
                float v = (float)y / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W;
                    px[y * W + x] = MapPixel(u, v, aspect, band);
                }
            }
            DrawSeal(px, W - (int)(H * 0.12f), (int)(H * 0.11f), (int)(H * 0.048f));
            return px;
        }

        static Color MapPixel(float u, float v, float aspect, float band)
        {
            // ---- border: vermilion band with the 回 pattern in gold ----
            float ex = Mathf.Min(u, 1f - u) * aspect, ey = Mathf.Min(v, 1f - v);
            float edge = Mathf.Min(ex, ey);
            if (edge < band) return BorderPixel(u, v, aspect, band, ex, ey);

            // ---- rice paper: soft age stains and fine fibres ----
            float n1 = Mathf.PerlinNoise(u * 2.6f + 11.3f, v * 2.6f + 7.7f);
            float fibre = Mathf.PerlinNoise(u * 420f, v * 30f) * 0.5f + Mathf.PerlinNoise(u * 25f, v * 380f) * 0.5f;
            Color c = Color.Lerp(Paper, PaperAged, Mathf.Clamp01((n1 - 0.40f) * 1.3f) * 0.6f);
            c = Color.Lerp(c, PaperAged, (fibre - 0.5f) * 0.18f + 0.04f);
            // darker towards the frame, like old scrolls
            float inner = edge - band;
            c = Color.Lerp(c, PaperAged, Mathf.Clamp01(1f - inner / 0.12f) * 0.30f);

            // ---- the square grid of old Chinese maps (计里画方) ----
            float cell = 1f / 18f;
            float gx = Mathf.Repeat(u * aspect / cell, 1f), gy = Mathf.Repeat(v / cell, 1f);
            if (gx < 0.035f || gy < 0.035f) c = Color.Lerp(c, InkC, 0.05f);

            // ---- river with brushed wave lines ----
            float riverY = 0.30f + 0.09f * Mathf.Sin(u * 5.2f + 0.6f) + 0.035f * Mathf.Sin(u * 13f + 2f) - 0.10f * u;
            float width = 0.022f + 0.012f * Mathf.Sin(u * 3f + 1f);
            float dr = v - riverY;
            if (Mathf.Abs(dr) < width)
            {
                c = Color.Lerp(c, Water, 0.85f);
                // repeating white wave arcs inside the river
                float t = (dr / width) * 2.2f + Mathf.Sin(u * 140f) * 0.22f;
                if (Mathf.Repeat(t, 1f) < 0.12f) c = Color.Lerp(c, Color.white, 0.55f);
            }
            float bank = Mathf.Abs(Mathf.Abs(dr) - width);
            if (bank < 0.0022f) c = Color.Lerp(c, InkC, 0.75f * (0.6f + 0.4f * Mathf.PerlinNoise(u * 60f, 3f)));

            // ---- ink-wash mountains fading into mist ----
            float ink = 0f, ridge = 0f, height = 0f;
            for (int i = 0; i < Peaks.GetLength(0); i++)
            {
                float px0 = Peaks[i, 0], baseY = Peaks[i, 1], h = Peaks[i, 2], hw = Peaks[i, 3], dark = Peaks[i, 4];
                float dx = u - px0; // widths are in u units
                if (Mathf.Abs(dx) > hw || v < baseY - 0.02f || v > baseY + h + 0.02f) continue;
                float k = 1f - Mathf.Abs(dx) / hw;
                float wob = (Mathf.PerlinNoise(u * 40f + i * 7.1f, i * 3.3f) - 0.5f) * 0.25f * h;
                float top = baseY + h * Mathf.Pow(k, 1.4f) + wob * k;
                if (v > top) continue;
                float depth = (top - v) / h;
                // dark contour at the ridge, soft wash below, texture strokes (cun), mist at the base
                float a = 0.55f * Mathf.Exp(-depth * 5f) + 0.25f;
                bool isRidge = top - v < 0.004f;
                float strokes = Mathf.PerlinNoise(u * 90f + i, v * 18f);
                float fill = (0.65f + 0.35f * strokes) * Mathf.SmoothStep(0f, 1f, (v - baseY + 0.02f) / (h * 0.45f)); // mist at the foot
                fill = Mathf.Clamp01(fill * (0.55f + 0.45f * dark));
                if (fill > ink) { ink = fill; height = Mathf.Clamp01(1f - depth); }
                if (isRidge) ridge = Mathf.Max(ridge, 0.8f * dark);
            }
            // colour wash: jade at the foot, azurite towards the peaks, ink outline on the ridge
            Color rock = Color.Lerp(Jade, Azurite, Mathf.SmoothStep(0.45f, 1f, height));
            c = Color.Lerp(c, rock, ink * 0.9f);
            c = Color.Lerp(c, InkC, ridge);

            // ---- mist drifting across the mountains ----
            float mist = 0f;
            float[] mistY = { 0.60f, 0.79f, 0.16f };
            for (int i = 0; i < mistY.Length; i++)
            {
                float my = mistY[i] + 0.015f * Mathf.Sin(u * 7f + i * 2f);
                float band2 = Mathf.Exp(-Mathf.Pow((v - my) / 0.018f, 2f));
                float wisp = Mathf.PerlinNoise(u * 6f + i * 13f, i * 4.1f);
                mist = Mathf.Max(mist, band2 * Mathf.Clamp01((wisp - 0.35f) * 2.2f));
            }
            c = Color.Lerp(c, Paper, mist * 0.85f);

            // ---- the Great Wall along the far ridge ----
            float wallY = 0.845f + 0.022f * Mathf.Sin(u * 9f + 0.5f) + 0.012f * Mathf.Sin(u * 23f);
            if (u > 0.07f && u < 0.93f)
            {
                float wd = v - wallY;
                bool wallBody = wd > -0.004f && wd < 0.0035f;
                bool merlon = wd >= 0.0035f && wd < 0.009f && Mathf.Repeat(u * 140f, 1f) < 0.5f;
                float towerPos = Mathf.Repeat(u * 9f + 0.5f, 1f);
                bool tower = towerPos < 0.035f && wd > -0.004f && wd < 0.020f;
                if (wallBody || merlon || tower) c = Color.Lerp(c, new Color(0.45f, 0.30f, 0.20f), 0.85f);
            }

            // ---- pines scattered at the foot of the mountains ----
            c = Pines(c, u, v, aspect);

            // ---- walled towns ----
            c = Town(c, u, v, aspect, 0.24f, 0.62f, 0.022f);
            c = Town(c, u, v, aspect, 0.77f, 0.63f, 0.020f);
            c = Town(c, u, v, aspect, 0.30f, 0.17f, 0.018f);
            c = Town(c, u, v, aspect, 0.70f, 0.13f, 0.020f);

            c.a = 1f;
            return c;
        }

        static Color BorderPixel(float u, float v, float aspect, float band, float ex, float ey)
        {
            Color c = Vermilion;
            // position along the band and across it (0 = outer edge, 1 = inner edge)
            bool horizontal = ey < ex;
            float across = (horizontal ? ey : ex) / band;
            float along = horizontal ? u * aspect : v;
            // thin gold lines at the inner and outer edges
            if (across < 0.10f || across > 0.90f) return Color.Lerp(c, GoldC, 0.85f);
            if (across < 0.16f || across > 0.84f) return Color.Lerp(c, new Color(0.3f, 0.05f, 0.03f), 0.5f);
            // 回 motif: a square inside a square, one per cell
            float cellSize = band * 0.68f;
            float a = Mathf.Repeat(along / cellSize, 1f);
            float b = Mathf.InverseLerp(0.16f, 0.84f, across);
            float ox = Mathf.Abs(a - 0.5f), oy = Mathf.Abs(b - 0.5f);
            float m = Mathf.Max(ox, oy);
            bool outerRing = m > 0.36f && m < 0.44f;
            bool innerRing = m > 0.14f && m < 0.22f;
            if (outerRing || innerRing) c = Color.Lerp(c, GoldC, 0.8f);
            // a little age
            c = Color.Lerp(c, new Color(0.55f, 0.10f, 0.06f), Mathf.PerlinNoise(u * 30f, v * 30f) * 0.12f);
            return c;
        }

        static Color Pines(Color c, float u, float v, float aspect)
        {
            const float cw = 0.022f, ch = 0.04f;
            int ix = Mathf.FloorToInt(u / cw), iy = Mathf.FloorToInt(v / ch);
            float r = Hash(ix, iy);
            // only near mountain feet: left/right sides, avoid the centre and the river
            bool sideZone = (u < 0.22f || u > 0.78f) && v > 0.40f && v < 0.60f;
            bool lowZone = (u < 0.20f || u > 0.80f) && v > 0.02f && v < 0.14f;
            if (r > 0.28f || !(sideZone || lowZone)) return c;
            // jitter each tree inside its cell and keep it fully inside, so they don't line up in rows
            float cx = 0.35f + Hash(ix + 31, iy) * 0.3f;
            float oy = Hash(ix, iy + 57) * 0.3f;
            float lx = (u - ix * cw) / cw;
            float ly = ((v - iy * ch) / ch - oy) / 0.7f;
            float scale = 0.75f + Hash(ix + 5, iy + 9) * 0.25f;
            // trunk
            bool trunk = Mathf.Abs(lx - cx) < 0.04f && ly > 0.05f && ly < 0.75f * scale;
            // three flat tiers of needles
            bool tier = false;
            for (int t = 0; t < 3; t++)
            {
                float ty = (0.30f + t * 0.18f) * scale, hwid = (0.30f - t * 0.07f) * scale;
                if (ly > ty && ly < ty + 0.09f && Mathf.Abs(lx - cx) < hwid * (1f - (ly - ty) / 0.12f)) tier = true;
            }
            if (trunk || tier) return Color.Lerp(c, trunk && !tier ? InkC : PineGreen, 0.8f);
            return c;
        }

        static Color Town(Color c, float u, float v, float aspect, float cx, float cy, float size)
        {
            float dx = (u - cx) * aspect, dy = v - cy;
            float m = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (m > size * 1.6f) return c;
            // square wall with a gate gap at the bottom
            bool wall = m > size * 0.86f && m < size;
            if (wall && dy < -size * 0.8f && Mathf.Abs(dx) < size * 0.18f) wall = false;
            // corner towers
            bool corner = Mathf.Abs(Mathf.Abs(dx) - size * 0.93f) < size * 0.16f && Mathf.Abs(Mathf.Abs(dy) - size * 0.93f) < size * 0.16f;
            // a small pagoda in the middle: three tiers of roofs
            bool pagoda = false;
            for (int t = 0; t < 3; t++)
            {
                float ry = -size * 0.45f + t * size * 0.32f;
                float hwid = size * (0.42f - t * 0.1f);
                if (dy > ry && dy < ry + size * 0.09f && Mathf.Abs(dx) < hwid) pagoda = true;
                if (dy > ry + size * 0.09f && dy < ry + size * 0.32f && Mathf.Abs(dx) < hwid * 0.45f && Mathf.Abs(dx) > hwid * 0.3f) pagoda = true;
            }
            if (wall || corner) return Color.Lerp(c, InkC, 0.75f);
            if (pagoda) return Color.Lerp(c, Vermilion, 0.75f);
            if (m < size * 0.86f) return Color.Lerp(c, Paper, 0.35f);
            return c;
        }

        // A square vermilion seal with "carved" white strokes, slightly worn.
        static void DrawSeal(Color[] px, int cx, int cy, int half)
        {
            for (int y = cy - half; y <= cy + half; y++)
            {
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float a = (x - cx) / (float)half, b = (y - cy) / (float)half; // -1..1
                    float worn = Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                    if (Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)) > 0.97f - worn * 0.05f) continue;
                    bool frame = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)) > 0.80f;
                    // blocky strokes in a 2x2 layout, like a four-character seal
                    bool stroke =
                        (Mathf.Abs(a) < 0.05f) || // centre divider
                        (Mathf.Abs(b - 0.40f) < 0.07f && a < -0.15f) ||
                        (Mathf.Abs(a + 0.45f) < 0.07f && b > 0.10f) ||
                        (Mathf.Abs(b - 0.55f) < 0.07f && a > 0.15f) || (Mathf.Abs(b - 0.25f) < 0.07f && a > 0.15f) ||
                        (Mathf.Abs(a - 0.45f) < 0.07f && b < -0.15f) || (Mathf.Abs(b + 0.45f) < 0.07f && a > 0.15f) ||
                        (Mathf.Abs(b + 0.30f) < 0.07f && a < -0.15f) || (Mathf.Abs(a + 0.30f) < 0.07f && b < -0.15f && b > -0.75f);
                    Color seal = (!frame && stroke) || (frame && Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)) < 0.86f)
                        ? new Color(0.95f, 0.88f, 0.78f) : Vermilion;
                    if (worn > 0.78f) seal = Color.Lerp(seal, new Color(0.93f, 0.88f, 0.76f), 0.6f);
                    int i = y * W + x;
                    px[i] = Color.Lerp(px[i], seal, 0.9f);
                }
            }
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        /// <summary>A bagua disc: the eight trigrams around a yin-yang, in ink and vermilion.</summary>
        public static Sprite Compass()
        {
            var custom = Resources.Load<Sprite>("Map/compass");
            if (custom != null) return custom;
            return ProcSprites.FromFn("compass_bagua", 192, 192, CompassPixel, 2);
        }

        // Earlier Heaven order, starting at the top and going clockwise. Bits: inner -> outer, 1 = solid line.
        static readonly int[] Trigrams = { 0b111, 0b110, 0b101, 0b100, 0b000, 0b001, 0b010, 0b011 };

        /// <summary>One pixel of the bagua disc, in normalized coordinates (0..1).</summary>
        public static Color CompassPixel(float x, float y)
        {
            Color ink = new Color(0.10f, 0.09f, 0.08f);
            Color paper = new Color(0.95f, 0.91f, 0.80f);
            Color red = new Color(0.66f, 0.14f, 0.09f);
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 0.48f) return Color.clear;
                if (r > 0.455f) return red;                 // outer rim
                if (r > 0.445f) return paper;

                const float R = 0.20f;                      // yin-yang radius
                if (r < R) return YinYang(dx, dy, R, ink, paper);
                if (r < R + 0.015f) return red;

                // trigram ring
                float ang = Mathf.Atan2(dx, dy);            // 0 at the top, clockwise
                float sector = Mathf.Repeat(ang / (Mathf.PI / 4f) + 0.5f, 8f);
                int k = Mathf.FloorToInt(sector) % 8;
                float local = (sector - Mathf.Floor(sector) - 0.5f) * (Mathf.PI / 4f) * r; // arc distance
                for (int line = 0; line < 3; line++)
                {
                    float lr = 0.27f + line * 0.055f;
                    if (Mathf.Abs(r - lr) < 0.016f && Mathf.Abs(local) < 0.052f)
                    {
                        bool solid = ((Trigrams[k] >> line) & 1) == 1;
                        if (solid || Mathf.Abs(local) > 0.012f) return ink;
                    }
                }
                return new Color(paper.r, paper.g, paper.b, 0.92f);
            }
        }

        static Color YinYang(float dx, float dy, float R, Color dark, Color light)
        {
            float half = R * 0.5f;
            float dTop = Mathf.Sqrt(dx * dx + (dy - half) * (dy - half));
            float dBot = Mathf.Sqrt(dx * dx + (dy + half) * (dy + half));
            if (dTop < R * 0.13f) return dark;
            if (dBot < R * 0.13f) return light;
            if (dTop < half) return light;
            if (dBot < half) return dark;
            return dx < 0 ? light : dark;
        }
    }
}
