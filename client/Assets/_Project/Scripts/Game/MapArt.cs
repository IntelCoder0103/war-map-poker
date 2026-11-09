using System;
using System.Collections.Generic;
using UnityEngine;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// The battlefield background, in the bright style of mobile strategy games. Uses Resources/Map/background
    /// if present; otherwise draws one procedurally: lush grass with light and dark patches, a lighter
    /// clearing in the middle (cards, pot and camps sit there), dirt roads, a river with a wooden bridge,
    /// a pond, rounded tree clumps, pines and blossom trees around the edges, rocks, flowers, two small
    /// a pagoda, and the Great Wall along the top. Soft vignette, no border.
    /// The compass is a bagua medallion (Resources/Map/compass override).
    ///
    /// All drawing is in texture pixels (x right, y up). Objects are stamped over their own bounding
    /// boxes, back to front, so the cost stays proportional to the area they cover.
    /// </summary>
    public static class MapArt
    {
        const int W = 1152, H = 648;
        public const int Width = W, Height = H;

        // ---------- palette ----------
        static readonly Color GrassLight = new Color(0.56f, 0.83f, 0.25f);
        static readonly Color GrassMid = new Color(0.42f, 0.73f, 0.19f);
        static readonly Color GrassDark = new Color(0.29f, 0.60f, 0.15f);
        static readonly Color Meadow = new Color(0.63f, 0.86f, 0.32f);
        static readonly Color Sand = new Color(0.93f, 0.80f, 0.53f);
        static readonly Color SandDark = new Color(0.78f, 0.61f, 0.36f);
        static readonly Color Bank = new Color(0.90f, 0.83f, 0.55f);
        static readonly Color WaterShallow = new Color(0.33f, 0.78f, 0.96f);
        static readonly Color WaterDeep = new Color(0.13f, 0.52f, 0.88f);
        static readonly Color Stone = new Color(0.80f, 0.76f, 0.68f);
        static readonly Color StoneLight = new Color(0.93f, 0.90f, 0.83f);
        static readonly Color StoneDark = new Color(0.60f, 0.56f, 0.50f);
        static readonly Color Mortar = new Color(0.52f, 0.48f, 0.43f);
        static readonly Color Line = new Color(0.25f, 0.21f, 0.18f);
        static readonly Color RoofRed = new Color(0.86f, 0.20f, 0.13f);
        static readonly Color RoofLight = new Color(0.97f, 0.40f, 0.24f);
        static readonly Color RoofDark = new Color(0.48f, 0.08f, 0.06f);
        static readonly Color Plaster = new Color(0.98f, 0.94f, 0.84f);
        static readonly Color Timber = new Color(0.55f, 0.33f, 0.17f);
        static readonly Color TimberDark = new Color(0.33f, 0.19f, 0.10f);
        static readonly Color GoldC = new Color(1.00f, 0.80f, 0.25f);

        // ---------- layout (texture pixels; the 1920×1080 board maps at 0.6) ----------
        // Clearing around the cards and pot; camps sit on an ellipse of 432×198 around (576, 342).
        const float CX = 576f, CY = 338f, CRX = 360f, CRY = 178f;

        static readonly Vector2[] River =
        {
            new Vector2(-30, 318), new Vector2(-6, 302), new Vector2(45, 274), new Vector2(98, 248),
            new Vector2(142, 208), new Vector2(176, 152), new Vector2(214, 98), new Vector2(260, 54),
            new Vector2(300, 20), new Vector2(330, -14), new Vector2(350, -40),
        };

        static readonly Vector2[][] Roads =
        {
            // west, between the left camp and the upper-left camp
            new[] { new Vector2(-40, 386), new Vector2(-8, 392), new Vector2(62, 402), new Vector2(132, 414), new Vector2(196, 418), new Vector2(258, 396), new Vector2(318, 368), new Vector2(360, 352) },
            // east, between the right camp and the lower-right camp
            new[] { new Vector2(1190, 246), new Vector2(1160, 250), new Vector2(1092, 250), new Vector2(1030, 262), new Vector2(972, 272), new Vector2(910, 292), new Vector2(846, 314), new Vector2(800, 326) },
            // north, out of the gate in the Great Wall
            new[] { new Vector2(738, 650), new Vector2(736, 606), new Vector2(738, 562), new Vector2(733, 522), new Vector2(712, 482), new Vector2(676, 452), new Vector2(650, 436) },
            // south-west, over the bridge
            new[] { new Vector2(118, -40), new Vector2(150, -8), new Vector2(200, 30), new Vector2(262, 72), new Vector2(334, 110), new Vector2(404, 148), new Vector2(448, 196), new Vector2(474, 236), new Vector2(490, 262) },
        };

        static readonly Vector2 PondPos = new Vector2(1068, 470);
        const float PondRX = 58f, PondRY = 30f;
        static readonly Vector2 PagodaPos = new Vector2(1000, 520);
        static readonly float[] Towers = { 40f, 222f, 410f, 935f, 1118f };
        const float GateX = 738f;

        public static Sprite Background()
        {
            var custom = Resources.Load<Sprite>("Map/background");
            if (custom != null) return custom;
            Sprite cached;
            if (ProcSprites.TryGet("map_bg_green", out cached)) return cached;
            var sprite = ProcSprites.MakeSprite(ProcSprites.MakeTexture(W, H, BackgroundPixels()), Vector4.zero);
            return ProcSprites.Store("map_bg_green", sprite);
        }

        /// <summary>The procedural map as raw pixels (also used by the Editor preview export).</summary>
        public static Color[] BackgroundPixels()
        {
            var px = new Color[W * H];

            // Signed-distance fields for water and roads (negative inside), stamped along the curves.
            var water = NewField();
            var road = NewField();
            StampCurve(water, River, 14f, 0.35f, 1.7f, 14);
            for (int i = 0; i < Roads.Length; i++) StampCurve(road, Roads[i], 7f, 0.25f, 3.1f + i, 10);

            // Low-frequency noise is smooth, so sample it on a coarse grid and interpolate.
            var g1 = CoarseNoise(0.0055f, 3.1f, 7.3f);
            var g2 = CoarseNoise(0.017f, 11.2f, 5.4f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    px[i] = Ground(x + 0.5f, y + 0.5f, water[i], road[i], Coarse(g1, x, y), Coarse(g2, x, y));
                }

            var rng = new Rng(20240611);
            var objs = new List<Obj>(900);
            AddScenery(objs, rng, water, road);

            // Shadows first (they fall on the ground only), then everything back to front.
            WallShadow(px);
            foreach (var o in objs) if (o.Shadow != null) o.Shadow(px);
            objs.Sort((a, b) => b.Y.CompareTo(a.Y));
            foreach (var o in objs) o.Draw(px);

            Vignette(px);
            return px;
        }

        // =====================================================================
        // Ground
        // =====================================================================

        const int Grid = 8;
        const int GW = W / Grid + 2, GH = H / Grid + 2;

        static float[] CoarseNoise(float freq, float ox, float oy)
        {
            var g = new float[GW * GH];
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                    g[y * GW + x] = Mathf.PerlinNoise(x * Grid * freq + ox, y * Grid * freq + oy);
            return g;
        }

        static float Coarse(float[] g, int x, int y)
        {
            int ix = x / Grid, iy = y / Grid;
            float fx = (x % Grid + 0.5f) / Grid, fy = (y % Grid + 0.5f) / Grid;
            int i = iy * GW + ix;
            float a = g[i] + (g[i + 1] - g[i]) * fx;
            float b = g[i + GW] + (g[i + GW + 1] - g[i + GW]) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>Smooth value noise on an integer lattice (pure arithmetic, cheap), 0..1.</summary>
        static float VNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        static Color Ground(float x, float y, float wd, float rd, float n1, float n2)
        {
            float n3 = VNoise(x * 0.16f + 1.7f, y * 0.22f + 9.1f);
            float patch = n1 * 0.62f + n2 * 0.38f;
            Color c = patch < 0.5f
                ? Color.Lerp(GrassDark, GrassMid, Smooth(0.27f, 0.5f, patch))
                : Color.Lerp(GrassMid, GrassLight, Smooth(0.5f, 0.70f, patch));
            float grain = (n3 - 0.5f) * 0.12f + (Hash((int)x, (int)y) - 0.5f) * 0.035f;

            // lighter meadow in the middle, with a ragged edge
            float ex = (x - CX) / CRX, ey = (y - CY) / CRY;
            float e = Mathf.Sqrt(ex * ex + ey * ey) + (n2 - 0.5f) * 0.20f;
            float clear = 1f - Smooth(0.70f, 1.12f, e);
            c = Color.Lerp(c, Meadow, clear * 0.72f);
            // mown rings in the meadow, very faint
            c *= 1f + grain * (1f - clear * 0.5f);

            // behind the Great Wall: hazy hills
            float wy = WallY(x);
            if (y > wy + 6f)
            {
                float t = Mathf.Clamp01((y - wy - 6f) / 40f);
                c = Color.Lerp(c * 0.86f, new Color(0.52f, 0.74f, 0.50f), t * 0.55f);
            }

            // ---- roads ----
            if (rd < 4f)
            {
                float fade = 1f - clear * 0.92f;
                // worn grass at the edge
                float rim = Mathf.Clamp01(1f - Mathf.Abs(rd - 1.6f) / 2.2f) * 0.18f * fade;
                c = Color.Lerp(c, c * 0.72f, rim);
                float a = Mathf.Clamp01(0.5f - rd) * fade;
                if (a > 0f)
                {
                    float pebble = Mathf.PerlinNoise(x * 0.42f + 5f, y * 0.42f);
                    Color s = Color.Lerp(SandDark, Sand, Smooth(-0.5f, -3.5f, rd));
                    s *= 0.96f + (Mathf.PerlinNoise(x * 0.06f, y * 0.06f + 3f) - 0.5f) * 0.12f;
                    if (pebble > 0.73f) s = Color.Lerp(s, SandDark * 0.92f, 0.6f);
                    else if (pebble < 0.22f) s = Color.Lerp(s, new Color(1f, 0.93f, 0.74f), 0.5f);
                    c = Color.Lerp(c, s, a);
                }
            }

            // ---- water (river + pond) ----
            float pdx = (x - PondPos.x) / PondRX, pdy = (y - PondPos.y) / PondRY;
            if (Mathf.Abs(pdx) < 1.6f && Mathf.Abs(pdy) < 1.8f)
            {
                float wob = (Mathf.PerlinNoise(x * 0.05f + 40f, y * 0.05f) - 0.5f) * 0.35f;
                float pd = (Mathf.Sqrt(pdx * pdx + pdy * pdy) - 1f + wob) * PondRY;
                wd = Mathf.Min(wd, pd);
            }
            if (wd < 9f)
            {
                // grass darkens towards the bank, then a sandy bank, a light shore line and deep water
                c = Color.Lerp(c, c * 0.78f, Mathf.Clamp01(1f - (wd - 4.5f) / 4.5f) * 0.6f);
                c = Color.Lerp(c, Bank * (0.95f + grain), Mathf.Clamp01(5f - wd));
                c = Color.Lerp(c, Bank * 0.78f, Mathf.Clamp01(1f - Mathf.Abs(wd - 1.2f) / 1.0f) * 0.5f);
                float wa = Mathf.Clamp01(0.5f - wd);
                if (wa > 0f)
                {
                    float depth = Mathf.Clamp01(-wd / 12f);
                    Color w = Color.Lerp(WaterShallow, WaterDeep, Smooth(0.1f, 0.85f, depth));
                    // flowing highlights, stretched along the river (it runs to the south-east)
                    float a1 = (x - y) * 0.7f, b1 = (x + y) * 0.7f;
                    float streak = Mathf.PerlinNoise(a1 * 0.035f + 9f, b1 * 0.16f);
                    w = Color.Lerp(w, new Color(0.72f, 0.93f, 1f), Smooth(0.66f, 0.74f, streak) * 0.55f * depth);
                    w = Color.Lerp(w, WaterDeep * 0.85f, Smooth(0.30f, 0.22f, streak) * 0.25f);
                    // light shore line just inside the water
                    float shore = Mathf.Clamp01(1f - Mathf.Abs(wd + 2.2f) / 1.1f);
                    w = Color.Lerp(w, new Color(0.88f, 0.98f, 1f), shore * 0.75f);
                    c = Color.Lerp(c, w, wa);
                }
            }

            c.a = 1f;
            return c;
        }

        static void Vignette(Color[] px)
        {
            Color tint = new Color(0.30f, 0.46f, 0.36f);
            for (int y = 0; y < H; y++)
            {
                float dy = (y + 0.5f) / H * 2f - 1f;
                for (int x = 0; x < W; x++)
                {
                    float dx = (x + 0.5f) / W * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.85f + dy * dy);
                    float v = Smooth(0.80f, 1.50f, r);
                    if (v <= 0f) continue;
                    int i = y * W + x;
                    Color c = px[i];
                    px[i] = Color.Lerp(c, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f), v * 0.75f);
                }
            }
        }

        // =====================================================================
        // Distance fields
        // =====================================================================

        static float[] NewField()
        {
            var f = new float[W * H];
            for (int i = 0; i < f.Length; i++) f[i] = 99f;
            return f;
        }

        /// <summary>Union of discs along a Catmull-Rom curve: f = min(distance - halfWidth).</summary>
        static void StampCurve(float[] f, Vector2[] ctrl, float hw, float vary, float seed, int margin)
        {
            var pts = SampleCurve(ctrl, 1.5f);
            for (int k = 0; k < pts.Count; k++)
            {
                Vector2 p = pts[k];
                float w = hw * (1f + vary * (Mathf.PerlinNoise(k * 0.012f, seed) - 0.5f) * 2f);
                int r = Mathf.CeilToInt(w + margin);
                int x0 = Mathf.Max(0, (int)(p.x - r)), x1 = Mathf.Min(W - 1, (int)(p.x + r));
                int y0 = Mathf.Max(0, (int)(p.y - r)), y1 = Mathf.Min(H - 1, (int)(p.y + r));
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - p.y;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - p.x;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - w;
                        int i = y * W + x;
                        if (d < f[i]) f[i] = d;
                    }
                }
            }
        }

        static List<Vector2> SampleCurve(Vector2[] c, float step)
        {
            var list = new List<Vector2>();
            for (int i = 0; i < c.Length - 1; i++)
            {
                Vector2 p0 = c[Mathf.Max(0, i - 1)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(c.Length - 1, i + 2)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int s = 0; s < n; s++)
                {
                    float t = s / (float)n, t2 = t * t, t3 = t2 * t;
                    list.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            list.Add(c[c.Length - 1]);
            return list;
        }

        static float Field(float[] f, float x, float y)
        {
            int ix = Mathf.Clamp((int)x, 0, W - 1), iy = Mathf.Clamp((int)y, 0, H - 1);
            return f[iy * W + ix];
        }

        // =====================================================================
        // Scenery placement
        // =====================================================================

        class Obj
        {
            public float Y;                 // sort key: the object's foot (lower = nearer = drawn later)
            public Action<Color[]> Draw;
            public Action<Color[]> Shadow;
        }

        static void Add(List<Obj> list, float y, Action<Color[]> draw, Action<Color[]> shadow = null)
        {
            list.Add(new Obj { Y = y, Draw = draw, Shadow = shadow });
        }

        static bool NearPagoda(float x, float y, float pad)
        {
            return InBox(x, y, PagodaPos + new Vector2(0, 14), 22 + pad, 34 + pad);
        }

        static bool InBox(float x, float y, Vector2 c, float hx, float hy)
        {
            return Mathf.Abs(x - c.x) < hx && Mathf.Abs(y - c.y) < hy;
        }

        static float ClearingE(float x, float y)
        {
            float ex = (x - CX) / CRX, ey = (y - CY) / CRY;
            return Mathf.Sqrt(ex * ex + ey * ey);
        }

        static bool Blocked(float[] water, float[] road, float x, float y, float r)
        {
            if (x < -10 || x > W + 10 || y < -10) return true;
            if (Field(water, x, y) < r + 4f || Field(road, x, y) < r + 3f) return true;
            float pdx = (x - PondPos.x) / (PondRX + r + 6f), pdy = (y - PondPos.y) / (PondRY + r + 6f);
            if (pdx * pdx + pdy * pdy < 1f) return true;
            if (y > WallY(x) - 24f) return true;
            return NearPagoda(x, y, r);
        }

        static void AddScenery(List<Obj> objs, Rng rng, float[] water, float[] road)
        {
            // ---- the Great Wall and the forest behind it ----
            for (float x = -8f; x < W + 10f; x += 13f + rng.Next() * 8f)
            {
                float y = WallY(x) + 16f + rng.Next() * 14f;
                AddRoundTree(objs, rng, x, y, 9f + rng.Next() * 5f, 3, true);
            }
            Add(objs, WallY(GateX) - 18f, DrawWall);

            // ---- towns, pagoda, bridge ----
            AddStamp(objs, PagodaPos, 24, 50, PagodaStandalone, 2, 1);
            AddBridge(objs, water);

            // ---- trees: dense along the edges, a few groves, none in the clearing ----
            const float cell = 21f;
            for (float gy = -cell * 0.5f; gy < H; gy += cell)
            {
                for (float gx = -cell * 0.5f; gx < W + cell; gx += cell)
                {
                    float x = gx + rng.Next() * cell, y = gy + rng.Next() * cell;
                    float edge = Mathf.Min(Mathf.Min(x, W - x), y * 1.25f);
                    float p = 1f - Smooth(30f, 150f, edge);
                    float grove = Smooth(0.60f, 0.70f, Mathf.PerlinNoise(x * 0.010f + 20f, y * 0.010f + 40f));
                    p = Mathf.Max(p * 0.92f, grove * 0.75f);
                    float e = ClearingE(x, y);
                    if (e < 1.12f) p = 0f;
                    else if (e < 1.45f) p *= 0.25f;
                    if (rng.Next() > p) continue;
                    float r = 10f + rng.Next() * 6f;
                    if (Blocked(water, road, x, y, r * 0.7f)) continue;
                    float kind = rng.Next();
                    if (kind < 0.20f) AddPine(objs, rng, x, y, r);
                    else if (kind < 0.245f) AddBlossom(objs, rng, x, y, r);
                    else AddRoundTree(objs, rng, x, y, r, 2 + (int)(rng.Next() * 3f), false);
                }
            }

            // ---- rocks along roads, banks and edges ----
            int rocks = 0;
            for (int tries = 0; tries < 400 && rocks < 34; tries++)
            {
                float x = rng.Next() * W, y = rng.Next() * (H - 60f);
                float near = Mathf.Min(Field(water, x, y), Field(road, x, y));
                float edge = Mathf.Min(Mathf.Min(x, W - x), y);
                bool good = (near > 6f && near < 20f) || edge < 70f;
                if (!good || ClearingE(x, y) < 1.15f || Blocked(water, road, x, y, 3f)) continue;
                AddRock(objs, rng, x, y, 4f + rng.Next() * 4.5f);
                rocks++;
            }

            // ---- grass tufts and flowers (lighter touch in the clearing) ----
            for (int k = 0; k < 520; k++)
            {
                float x = rng.Next() * W, y = rng.Next() * (H - 50f);
                float e = ClearingE(x, y);
                if (e < 0.9f && rng.Next() < 0.75f) continue;
                if (Blocked(water, road, x, y, 1f)) continue;
                AddTuft(objs, rng, x, y);
            }
            Color[] petals = { new Color(1f, 1f, 1f), new Color(1f, 0.88f, 0.25f), new Color(1f, 0.55f, 0.70f), new Color(0.95f, 0.30f, 0.25f) };
            for (int k = 0; k < 70; k++)
            {
                float x = rng.Next() * W, y = rng.Next() * (H - 50f);
                if (ClearingE(x, y) < 1.05f || Blocked(water, road, x, y, 2f)) continue;
                Color pc = petals[(int)(rng.Next() * petals.Length) % petals.Length];
                int n = 3 + (int)(rng.Next() * 4f);
                for (int j = 0; j < n; j++)
                {
                    float fx = x + (rng.Next() - 0.5f) * 14f, fy = y + (rng.Next() - 0.5f) * 8f;
                    Add(objs, fy, p => Flower(p, fx, fy, pc));
                }
            }
        }

        // =====================================================================
        // Trees, rocks, tufts, flowers
        // =====================================================================

        static readonly Color[][] TreePalettes =
        {
            // hi, mid, lo, outline
            new[] { new Color(0.58f, 0.86f, 0.27f), new Color(0.30f, 0.66f, 0.17f), new Color(0.15f, 0.45f, 0.13f), new Color(0.08f, 0.26f, 0.08f) },
            new[] { new Color(0.50f, 0.80f, 0.30f), new Color(0.22f, 0.58f, 0.20f), new Color(0.10f, 0.38f, 0.14f), new Color(0.05f, 0.22f, 0.08f) },
            new[] { new Color(0.72f, 0.90f, 0.30f), new Color(0.44f, 0.74f, 0.17f), new Color(0.24f, 0.52f, 0.12f), new Color(0.12f, 0.30f, 0.07f) },
        };
        static readonly Color[] HazePalette = { new Color(0.48f, 0.74f, 0.42f), new Color(0.30f, 0.58f, 0.34f), new Color(0.20f, 0.44f, 0.30f), new Color(0.12f, 0.30f, 0.22f) };
        static readonly Color[] BlossomPalette = { new Color(1.00f, 0.84f, 0.90f), new Color(0.97f, 0.60f, 0.74f), new Color(0.82f, 0.36f, 0.52f), new Color(0.45f, 0.14f, 0.26f) };
        static readonly Color[] RockPalette = { new Color(0.90f, 0.89f, 0.86f), new Color(0.68f, 0.68f, 0.68f), new Color(0.47f, 0.48f, 0.50f), new Color(0.24f, 0.25f, 0.28f) };

        static void AddRoundTree(List<Obj> objs, Rng rng, float x, float y, float r, int bumps, bool haze)
        {
            Color[] pal = haze ? HazePalette : TreePalettes[(int)(rng.Next() * 3f) % 3];
            // canopy blobs: a main ball with a few smaller ones around its top and sides
            var balls = new List<Vector3> { new Vector3(0f, r * 0.95f, r) };
            float start = rng.Next() * Mathf.PI;
            for (int b = 0; b < bumps; b++)
            {
                float ang = start + b * (Mathf.PI * 2f / bumps) + (rng.Next() - 0.5f) * 0.6f;
                float rr = r * (0.58f + rng.Next() * 0.18f);
                balls.Add(new Vector3(Mathf.Cos(ang) * r * 0.62f, r * 0.95f + Mathf.Sin(ang) * r * 0.5f, rr));
            }
            balls.Sort((a, b) => b.y.CompareTo(a.y));
            bool trunk = !haze;
            Add(objs, y, p =>
            {
                if (trunk) Trunk(p, x, y, r);
                foreach (var b in balls) Ball(p, x + b.x, y + b.y, b.z, b.z, pal[0], pal[1], pal[2], pal[3], 1.3f, true);
            }, haze ? (Action<Color[]>)null : p => ShadowBlob(p, x + r * 0.55f, y + r * 0.15f, r * 1.45f, r * 0.75f, 0.45f));
        }

        static void AddBlossom(List<Obj> objs, Rng rng, float x, float y, float r)
        {
            var balls = new List<Vector3> { new Vector3(0f, r * 0.95f, r * 0.9f) };
            for (int b = 0; b < 4; b++)
            {
                float ang = b * 1.57f + rng.Next() * 0.5f;
                balls.Add(new Vector3(Mathf.Cos(ang) * r * 0.6f, r * 0.95f + Mathf.Sin(ang) * r * 0.45f, r * 0.55f));
            }
            balls.Sort((a, b) => b.y.CompareTo(a.y));
            Add(objs, y, p =>
            {
                Trunk(p, x, y, r);
                foreach (var b in balls) Ball(p, x + b.x, y + b.y, b.z, b.z, BlossomPalette[0], BlossomPalette[1], BlossomPalette[2], BlossomPalette[3], 1.3f, true);
            }, p => ShadowBlob(p, x + r * 0.55f, y + r * 0.15f, r * 1.4f, r * 0.7f, 0.45f));
        }

        static void Trunk(Color[] p, float x, float y, float r)
        {
            float hw = Mathf.Max(1.6f, r * 0.17f);
            FillRect(p, x - hw - 1f, y - 1f, x + hw + 1f, y + r * 0.6f, TimberDark);
            FillRect(p, x - hw, y, x + hw, y + r * 0.6f, Timber);
            FillRect(p, x - hw, y, x - hw * 0.2f, y + r * 0.6f, new Color(0.68f, 0.44f, 0.24f));
        }

        static void AddPine(List<Obj> objs, Rng rng, float x, float y, float r)
        {
            float h = r * 2.9f, hw = r * 1.0f;
            Color[] pal = TreePalettes[1];
            Add(objs, y, p => Stamp(p, x - hw - 3f, y - 3f, x + hw + 3f, y + h + 2f, 2, (sx, sy) =>
            {
                float lx = sx - x, ly = sy - y;
                // trunk
                if (ly < h * 0.16f && ly > -1.5f && Mathf.Abs(lx) < r * 0.2f)
                    return Mathf.Abs(lx) > r * 0.2f - 1.1f || ly < -0.4f ? TimberDark : Timber;
                // three tiers, the top one in front
                for (int t = 2; t >= 0; t--)
                {
                    float b0 = h * (0.12f + t * 0.24f), top = b0 + h * (0.46f - t * 0.06f);
                    float w = hw * (1f - t * 0.2f);
                    float k = (top - ly) / (top - b0);         // 0 at the tip, 1 at the base
                    if (k < -0.05f || ly < b0 - 1.4f) continue;
                    float side = w * k - Mathf.Abs(lx);
                    float d = Mathf.Min(side * 0.85f, ly - b0 + 0.6f * Mathf.Abs(lx) / w);
                    if (d < -1.3f) continue;
                    if (d < 0f) return pal[3];
                    float rel = lx / Mathf.Max(0.01f, w * k);
                    Color c = rel < -0.15f ? pal[0] : rel < 0.35f ? pal[1] : pal[2];
                    if (ly - b0 < 2.2f) c = Color.Lerp(c, pal[2], 0.6f);
                    return c;
                }
                return Color.clear;
            }), p => ShadowBlob(p, x + r * 0.8f, y + r * 0.2f, r * 1.3f, r * 0.6f, 0.45f));
        }

        static void AddRock(List<Obj> objs, Rng rng, float x, float y, float r)
        {
            int n = 1 + (int)(rng.Next() * 2.5f);
            var parts = new List<Vector3>();
            for (int k = 0; k < n; k++)
                parts.Add(new Vector3((rng.Next() - 0.5f) * r * 1.4f, (rng.Next() - 0.3f) * r * 0.5f, r * (k == 0 ? 1f : 0.55f + rng.Next() * 0.3f)));
            parts.Sort((a, b) => b.y.CompareTo(a.y));
            Add(objs, y, p =>
            {
                foreach (var b in parts) Ball(p, x + b.x, y + b.y + b.z * 0.5f, b.z, b.z * 0.72f, RockPalette[0], RockPalette[1], RockPalette[2], RockPalette[3], 1.1f, false);
            }, p => ShadowBlob(p, x + r * 0.4f, y + r * 0.1f, r * 1.5f, r * 0.6f, 0.4f));
        }

        static void AddTuft(List<Obj> objs, Rng rng, float x, float y)
        {
            bool light = rng.Next() < 0.4f;
            float s = 0.8f + rng.Next() * 0.6f;
            Add(objs, y, p =>
            {
                int x0 = (int)(x - 5), x1 = (int)(x + 5), y0 = (int)(y - 1), y1 = (int)(y + 7);
                for (int yy = Mathf.Max(0, y0); yy <= Mathf.Min(H - 1, y1); yy++)
                    for (int xx = Mathf.Max(0, x0); xx <= Mathf.Min(W - 1, x1); xx++)
                    {
                        float fx = xx + 0.5f, fy = yy + 0.5f;
                        float d = Mathf.Min(ProcSprites.SegDist(fx, fy, x, y, x - 3f * s, y + 4.5f * s),
                                  Mathf.Min(ProcSprites.SegDist(fx, fy, x, y, x + 0.4f * s, y + 6f * s),
                                            ProcSprites.SegDist(fx, fy, x, y, x + 3.2f * s, y + 4f * s)));
                        float a = Mathf.Clamp01(1.2f - d);
                        if (a <= 0f) continue;
                        int i = yy * W + xx;
                        Color c = p[i];
                        Color t = light ? Color.Lerp(c, new Color(0.75f, 0.95f, 0.40f), 0.55f) : c * 0.70f;
                        p[i] = Color.Lerp(c, t, a);
                    }
            });
        }

        static void Flower(Color[] p, float x, float y, Color petal)
        {
            Ball(p, x, y, 1.9f, 1.9f, Color.Lerp(petal, Color.white, 0.4f), petal, petal * 0.8f, petal * 0.55f, 0.6f, false);
            int i = Mathf.Clamp((int)y, 0, H - 1) * W + Mathf.Clamp((int)x, 0, W - 1);
            p[i] = Color.Lerp(p[i], new Color(1f, 0.85f, 0.2f), 0.7f);
        }

        // =====================================================================
        // Bridge
        // =====================================================================

        static void AddBridge(List<Obj> objs, float[] water)
        {
            var pts = SampleCurve(Roads[3], 1.5f);
            int best = 1;
            float bestD = 99f;
            for (int k = 1; k < pts.Count - 1; k++)
            {
                float d = Field(water, pts[k].x, pts[k].y);
                if (d < bestD) { bestD = d; best = k; }
            }
            Vector2 c = pts[best];
            Vector2 dir = (pts[Mathf.Min(pts.Count - 1, best + 6)] - pts[Mathf.Max(0, best - 6)]).normalized;
            Vector2 n = new Vector2(-dir.y, dir.x);
            const float half = 25f, hw = 8f;
            float ext = half + hw + 4f;
            Add(objs, c.y - 30f, p => Stamp(p, c.x - ext, c.y - ext, c.x + ext, c.y + ext, 2, (sx, sy) =>
            {
                Vector2 q = new Vector2(sx - c.x, sy - c.y);
                float s = Vector2.Dot(q, dir), t = Vector2.Dot(q, n);
                if (Mathf.Abs(s) > half + 1.2f) return Color.clear;
                float at = Mathf.Abs(t);
                // rails with posts
                if (at > hw && at < hw + 3.2f)
                {
                    bool post = Mathf.Repeat(s + half, 12.5f) < 3f || Mathf.Abs(s) > half - 3f;
                    if (at > hw + 2.4f || at < hw + 0.8f) return TimberDark;
                    return post ? new Color(0.50f, 0.30f, 0.15f) : new Color(0.72f, 0.47f, 0.25f);
                }
                if (at > hw) return Color.clear;
                if (Mathf.Abs(s) > half) return TimberDark;
                // planks across the deck
                float pl = Mathf.Repeat(s + half, 3.6f);
                if (pl < 0.6f) return TimberDark;
                float shade = Hash(Mathf.FloorToInt((s + half) / 3.6f), 7) * 0.12f;
                Color wood = Color.Lerp(new Color(0.80f, 0.56f, 0.30f), new Color(0.66f, 0.43f, 0.22f), shade * 4f);
                return t < -hw + 1.5f ? wood * 0.82f : wood;
            }), p => ShadowRot(p, c + new Vector2(3f, -4f), dir, n, half, hw + 3f, 0.4f));
        }

        // =====================================================================
        // Great Wall
        // =====================================================================

        static float WallY(float x)
        {
            return 600f + 6f * Mathf.Sin(x * 0.0105f + 0.4f) + 3f * Mathf.Sin(x * 0.031f + 1.3f);
        }

        static void WallShadow(Color[] p)
        {
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f;
                float top = WallY(fx) - 12.5f, len = 9f;
                foreach (float tx in Towers)
                    if (Mathf.Abs(fx - tx) < 16f) { top = WallY(tx) - 16.5f; len = 12f; }
                if (Mathf.Abs(fx - GateX) < 25f) { top = WallY(GateX) - 16.5f; len = 12f; }
                for (int y = Mathf.Max(0, (int)(top - len)); y <= (int)top + 1 && y < H; y++)
                {
                    float k = Mathf.Clamp01((y + 0.5f - (top - len)) / len);
                    int i = y * W + x;
                    p[i] = Color.Lerp(p[i], p[i] * new Color(0.60f, 0.68f, 0.72f), k * k * 0.85f);
                }
            }
        }

        static void DrawWall(Color[] p)
        {
            Stamp(p, -1f, 560f, W + 1f, H, 2, WallPixel);
        }

        static Color WallPixel(float x, float y)
        {
            Color c = Color.clear;
            float t = y - WallY(x);
            if (t >= -13.5f && t < 11.5f) c = WallBand(x, t);
            foreach (float tx in Towers)
            {
                if (Mathf.Abs(x - tx) > 17f) continue;
                Color tc = TowerPixel(x - tx, y - WallY(tx), 15f, false);
                if (tc.a > 0f) c = tc;
            }
            if (Mathf.Abs(x - GateX) < 40f)
            {
                Color gc = TowerPixel(x - GateX, y - WallY(GateX), 24f, true);
                if (gc.a > 0f) c = gc;
            }
            return c;
        }

        static Color Bricks(float x, float t, float bw, float bh, Color light, Color dark, float t0, float t1)
        {
            float k = Mathf.Clamp01((t - t0) / (t1 - t0));
            Color c = Color.Lerp(dark, light, k);
            int row = Mathf.FloorToInt((t - t0) / bh);
            float off = (row & 1) * bw * 0.5f;
            float fx = Mathf.Repeat(x + off, bw), fy = Mathf.Repeat(t - t0, bh);
            c *= 0.95f + Hash(Mathf.FloorToInt((x + off) / bw), row + 31) * 0.10f;
            if (fy < 0.55f || fx < 0.6f) c = Color.Lerp(c, Mortar, 0.55f);
            c.a = 1f;
            return c;
        }

        static Color WallBand(float x, float t)
        {
            if (t < -12.5f) return Line;
            if (t < 0f) return Bricks(x, t, 7f, 3f, Stone, StoneDark, -12.5f, 0f);
            // crenellations on the front parapet
            if (t < 5.5f)
            {
                float m = Mathf.Repeat(x, 9f);
                bool merlon = m < 5f;
                if (merlon)
                {
                    if (t > 4.6f || m < 0.7f || m > 4.3f) return Line;
                    return t > 3.4f ? StoneLight : Stone;
                }
                if (t < 0.8f) return Line;
                return new Color(0.50f, 0.47f, 0.43f);
            }
            if (t < 9f) return new Color(0.66f, 0.62f, 0.56f);    // walkway
            if (t < 10.5f) return StoneLight;                     // back parapet
            return Line;
        }

        static Color TowerPixel(float lx, float lt, float hw, bool gate)
        {
            float ax = Mathf.Abs(lx);
            float roofTop = gate ? 34f : 0f;
            // Chinese pavilion on the gate tower
            if (gate && lt >= 13f && lt < roofTop)
            {
                Color rc = Roof(lx, lt, 20f, 14f, hw + 4f, 4f);
                if (rc.a > 0f) return rc;
                if (lt < 21f && ax < hw - 6f)
                {
                    if (lt < 14f) return Line;
                    if (Mathf.Repeat(lx + 2f, 8f) < 2.2f) return new Color(0.72f, 0.12f, 0.08f); // red pillars
                    return lt < 19f ? new Color(0.30f, 0.12f, 0.08f) : new Color(0.55f, 0.10f, 0.06f);
                }
            }
            if (ax > hw || lt < -17f || lt > 17.5f) return Color.clear;
            if (ax > hw - 1.1f || lt < -16f) return Line;
            // gate arch
            if (gate && ax < 8f && lt < -3f)
            {
                float archTop = -9f;
                bool open = lt < archTop || (lx * lx + (lt - archTop) * (lt - archTop) < 64f);
                bool rim = lt < archTop + 1.2f ? ax > 6.8f : (lx * lx + (lt - archTop) * (lt - archTop) > 46f);
                if (open) return rim ? Line : new Color(0.20f, 0.13f, 0.10f);
            }
            if (lt < 6f)
            {
                Color b = Bricks(lx + 40f, lt, 8f, 3.4f, Stone, StoneDark, -16f, 6f);
                if (lx > hw * 0.45f) b = Color.Lerp(b, StoneDark * 0.85f, 0.45f);   // side face in shade
                return b;
            }
            if (lt < 7f) return Line;
            if (lt < 13f) return lx > hw * 0.45f ? Stone : StoneLight;                // top platform
            // merlons on the tower top
            float m = Mathf.Repeat(lx + hw, 7f);
            if (m < 4f)
            {
                if (lt > 16.5f || m < 0.7f || m > 3.3f) return Line;
                return StoneLight;
            }
            return lt < 14f ? Line : Color.clear;
        }

        /// <summary>A sweeping Chinese roof: concave sides, eaves curling up at the corners.</summary>
        static Color Roof(float lx, float ly, float y0, float h, float hwBottom, float hwTop)
        {
            float ax = Mathf.Abs(lx);
            float eave = y0 + 3.2f * Mathf.Pow(Mathf.Clamp01(ax / hwBottom), 3f);   // upturned corners
            float k = (ly - y0) / h;
            if (ly < eave - 1.6f || k > 1f) return Color.clear;
            float hw = hwTop + (hwBottom - hwTop) * Mathf.Pow(1f - Mathf.Clamp01(k), 2.2f);
            if (ax > hw + 0.9f) return Color.clear;
            if (ax > hw || ly < eave) return RoofDark;                 // outline and eave edge
            if (k > 0.86f) return ly > y0 + h - 0.9f ? RoofDark : new Color(0.36f, 0.08f, 0.05f); // ridge
            Color c = Color.Lerp(RoofRed, RoofLight, Mathf.Clamp01((k - 0.25f) * 1.6f) * 0.6f);
            if (lx > 0f) c = Color.Lerp(c, RoofRed * 0.8f, 0.35f);
            if (Mathf.Repeat(lx, 2.6f) < 0.75f) c = Color.Lerp(c, RoofDark, 0.35f);         // tile rows
            if (ly < eave + 1.2f) c = Color.Lerp(c, RoofDark, 0.5f);
            c.a = 1f;
            return c;
        }

        // =====================================================================
        // Pagoda (local coordinates around the centre)
        // =====================================================================

        static Color House(float lx, float ly)
        {
            float ax = Mathf.Abs(lx);
            Color r = Roof(lx, ly, 4f, 8f, 13f, 5f);
            if (r.a > 0f) return r;
            if (ax > 9.5f || ly < -4.5f || ly > 4.5f) return Color.clear;
            if (ax > 8.6f || ly < -3.6f) return Line;
            if (ax < 2f && ly < 1.5f) return TimberDark;                         // door
            if (ax > 4f && ax < 7f && ly > -1f && ly < 2f) return new Color(0.35f, 0.18f, 0.10f); // windows
            if (Mathf.Abs(ax - 8f) < 0.6f) return Timber;
            return ly > 3f ? Plaster * 0.85f : Plaster;
        }

        /// <summary>A pagoda of n tiers whose base sits at ly = 0.</summary>
        static Color Pagoda(float lx, float ly, int tiers)
        {
            float ax = Mathf.Abs(lx);
            Color c = Color.clear;
            float y = 0f;
            for (int t = 0; t < tiers; t++)
            {
                float bw = 8f - t * 1.6f, bh = 6f - t * 0.6f, rh = 6f - t * 0.6f;
                // body
                if (ly >= y && ly < y + bh && ax < bw + 0.9f)
                {
                    if (ax > bw || ly < y + 0.9f) c = Line;
                    else if (ax < 1.6f && ly < y + bh - 1.5f) c = new Color(0.25f, 0.10f, 0.06f);
                    else c = Mathf.Repeat(lx + 20f, 4f) < 1.1f ? new Color(0.72f, 0.12f, 0.08f) : Plaster;
                }
                Color r = Roof(lx, ly, y + bh - 0.5f, rh, bw + 6f, 1.5f + (tiers - 1 - t) * 0.6f);
                if (r.a > 0f) c = r;
                y += bh + rh - 1.5f;
            }
            // golden finial
            if (ax < 0.9f && ly >= y && ly < y + 6f) c = ly > y + 5.1f || ax > 0.6f ? GoldC * 0.7f : GoldC;
            if (lx * lx + (ly - y - 3f) * (ly - y - 3f) < 2.6f) c = GoldC;
            c.a = c.a > 0f ? 1f : 0f;
            return c;
        }

        static Color PagodaStandalone(float lx, float ly)
        {
            // a small stone platform under a five-tier pagoda
            Color c = Color.clear;
            float ax = Mathf.Abs(lx);
            if (ax < 19f && ly > -16f && ly < -5f)
                c = ax > 18f || ly < -15f ? Line : ly > -7.5f ? StoneLight : Bricks(lx + 30f, ly, 6f, 2.6f, Stone, StoneDark, -15f, -7.5f);
            if (ax < 16f && ly >= -5f && ly < 0.5f) c = ax > 15f ? Line : new Color(0.86f, 0.79f, 0.60f);
            return Over(c, Pagoda(lx, ly + 2f, 5));
        }

        static Color Over(Color under, Color over)
        {
            return over.a > 0f ? over : under;
        }

        // =====================================================================
        // Raster helpers
        // =====================================================================

        static void AddStamp(List<Obj> objs, Vector2 c, float hx, float hy, Func<float, float, Color> fn, int ss, float shadow)
        {
            Add(objs, c.y - hy * 0.4f, p => Stamp(p, c.x - hx, c.y - hy * 0.6f, c.x + hx, c.y + hy, ss, (x, y) => fn(x - c.x, y - c.y)),
                shadow > 0f ? p => ShadowBlob(p, c.x + 6f, c.y - hy * 0.3f, hx * 1.05f, hy * 0.45f, 0.35f) : (Action<Color[]>)null);
        }

        /// <summary>Supersampled stamp: fn returns a colour with alpha 0 (nothing) or 1 (covered).</summary>
        static void Stamp(Color[] p, float fx0, float fy0, float fx1, float fy1, int ss, Func<float, float, Color> fn)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(fx0)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(fx1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(fy0)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(fy1));
            float inv = 1f / (ss * ss);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            Color c = fn(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss);
                            if (c.a <= 0f) continue;
                            r += c.r; g += c.g; b += c.b; a += 1f;
                        }
                    if (a <= 0f) continue;
                    int i = y * W + x;
                    float k = a * inv;
                    Color d = p[i];
                    p[i] = new Color(d.r + (r / a - d.r) * k, d.g + (g / a - d.g) * k, d.b + (b / a - d.b) * k, 1f);
                }
            }
        }

        static void FillRect(Color[] p, float x0, float y0, float x1, float y1, Color c)
        {
            for (int y = Mathf.Max(0, Mathf.FloorToInt(y0)); y <= Mathf.Min(H - 1, Mathf.FloorToInt(y1)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(x0)); x <= Mathf.Min(W - 1, Mathf.FloorToInt(x1)); x++)
                {
                    float cov = Mathf.Clamp01(Mathf.Min(x + 1f, x1) - Mathf.Max(x, x0)) * Mathf.Clamp01(Mathf.Min(y + 1f, y1) - Mathf.Max(y, y0));
                    if (cov <= 0f) continue;
                    int i = y * W + x;
                    p[i] = Color.Lerp(p[i], c, cov);
                }
        }

        /// <summary>A toon-shaded ellipsoid lit from the top-left, with an outline (tree canopies, rocks).</summary>
        static void Ball(Color[] p, float cx, float cy, float rx, float ry, Color hi, Color mid, Color lo, Color line, float lineW, bool leafy)
        {
            int x0 = Mathf.Max(0, (int)(cx - rx - 2)), x1 = Mathf.Min(W - 1, (int)(cx + rx + 2));
            int y0 = Mathf.Max(0, (int)(cy - ry - 2)), y1 = Mathf.Min(H - 1, (int)(cy + ry + 2));
            float rmin = Mathf.Min(rx, ry);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                    float len = Mathf.Sqrt(nx * nx + ny * ny);
                    float d = (len - 1f) * rmin;
                    if (d > 0.5f) continue;
                    float a = Mathf.Clamp01(0.5f - d);
                    float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - len * len));
                    float l = -0.50f * nx + 0.55f * ny + 0.67f * nz;
                    if (leafy) l += (VNoise(x * 0.42f, y * 0.42f) - 0.5f) * 0.32f;
                    Color c = Color.Lerp(lo, mid, Smooth(0.18f, 0.30f, l));
                    c = Color.Lerp(c, hi, Smooth(0.74f, 0.84f, l));
                    c = Color.Lerp(c, line, Mathf.Clamp01(d + lineW + 0.5f));
                    int i = y * W + x;
                    p[i] = Color.Lerp(p[i], c, a);
                }
            }
        }

        static void ShadowBlob(Color[] p, float cx, float cy, float rx, float ry, float strength)
        {
            int x0 = Mathf.Max(0, (int)(cx - rx - 1)), x1 = Mathf.Min(W - 1, (int)(cx + rx + 1));
            int y0 = Mathf.Max(0, (int)(cy - ry - 1)), y1 = Mathf.Min(H - 1, (int)(cy + ry + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float e = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = strength * Smooth(1f, 0.55f, e);
                    if (a <= 0f) continue;
                    int i = y * W + x;
                    Color c = p[i];
                    p[i] = Color.Lerp(c, new Color(c.r * 0.45f, c.g * 0.55f, c.b * 0.6f, 1f), a);
                }
        }

        static void ShadowRot(Color[] p, Vector2 c, Vector2 dir, Vector2 n, float half, float hw, float strength)
        {
            float ext = half + hw + 2f;
            for (int y = Mathf.Max(0, (int)(c.y - ext)); y <= Mathf.Min(H - 1, (int)(c.y + ext)); y++)
                for (int x = Mathf.Max(0, (int)(c.x - ext)); x <= Mathf.Min(W - 1, (int)(c.x + ext)); x++)
                {
                    Vector2 q = new Vector2(x + 0.5f - c.x, y + 0.5f - c.y);
                    float s = Mathf.Abs(Vector2.Dot(q, dir)) - half, t = Mathf.Abs(Vector2.Dot(q, n)) - hw;
                    float a = strength * Mathf.Clamp01(1f - Mathf.Max(s, t) / 2.5f);
                    if (a <= 0f) continue;
                    int i = y * W + x;
                    p[i] = Color.Lerp(p[i], p[i] * new Color(0.45f, 0.55f, 0.65f), a);
                }
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        /// <summary>Small deterministic RNG (same map on every device).</summary>
        class Rng
        {
            uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
            public float Next()
            {
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return (_s & 0xFFFFFF) / 16777216f;
            }
        }

        // =====================================================================
        // Compass: a bagua medallion
        // =====================================================================

        /// <summary>A bagua medallion: gold rim, red ring with gold trigrams, yin-yang in the middle.</summary>
        public static Sprite Compass()
        {
            var custom = Resources.Load<Sprite>("Map/compass");
            if (custom != null) return custom;
            return ProcSprites.FromFn("compass_bagua_v2", 192, 192, CompassPixel, 2);
        }

        // Earlier Heaven order, starting at the top and going clockwise. Bits: inner -> outer, 1 = solid line.
        static readonly int[] Trigrams = { 0b111, 0b110, 0b101, 0b100, 0b000, 0b001, 0b010, 0b011 };

        /// <summary>One pixel of the bagua medallion, in normalized coordinates (0..1).</summary>
        public static Color CompassPixel(float x, float y)
        {
            Color ink = new Color(0.16f, 0.10f, 0.08f);
            Color cream = new Color(1.00f, 0.96f, 0.86f);
            Color goldHi = new Color(1.00f, 0.93f, 0.55f), gold = new Color(1.00f, 0.76f, 0.16f), goldLo = new Color(0.72f, 0.42f, 0.06f);
            Color red = new Color(0.78f, 0.15f, 0.12f), redDark = new Color(0.50f, 0.07f, 0.06f);

            float dx = x - 0.5f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            // soft drop shadow below the disc
            if (r > 0.465f)
            {
                float sdx = dx - 0.012f, sdy = dy + 0.022f;
                float sr = Mathf.Sqrt(sdx * sdx + sdy * sdy);
                float a = Smooth(0.495f, 0.45f, sr) * 0.45f;
                if (r > 0.475f) return a > 0f ? new Color(0f, 0f, 0f, a) : Color.clear;
                return new Color(0.35f, 0.18f, 0.03f);    // dark outer line
            }
            float ang = Mathf.Atan2(dx, dy);              // 0 at the top, clockwise
            float lit = (-dx * 0.6f + dy * 0.8f) / Mathf.Max(0.001f, r);   // -1..1, light from the top-left

            // bevelled gold rim
            if (r > 0.405f)
            {
                float k = (r - 0.405f) / 0.06f;          // 0 inner .. 1 outer
                float bevel = k > 0.5f ? lit : -lit;     // outer half catches the light, inner half is in shade
                Color g = Color.Lerp(gold, bevel > 0f ? goldHi : goldLo, Mathf.Abs(bevel) * 0.8f);
                // little studs at the 8 directions
                float sector = Mathf.Repeat(ang / (Mathf.PI / 4f) + 0.5f, 1f) - 0.5f;
                float sd = Mathf.Sqrt(Mathf.Pow(sector * Mathf.PI / 4f * 0.435f, 2f) + Mathf.Pow(r - 0.435f, 2f));
                if (sd < 0.016f) g = Color.Lerp(goldHi, gold, sd / 0.016f);
                if (r < 0.412f) g = goldLo * 0.8f;
                g.a = 1f;
                return g;
            }

            const float R = 0.19f;   // yin-yang radius
            if (r < R - 0.008f) return YinYang(dx, dy, R, ink, cream);
            if (r < R + 0.03f)
            {
                if (r < R) return goldLo;
                Color g = Color.Lerp(gold, lit > 0 ? goldHi : goldLo, Mathf.Abs(lit) * 0.6f);
                g.a = 1f;
                return r > R + 0.024f ? goldLo : g;
            }

            // red ring with trigrams
            Color ring = Color.Lerp(red, redDark, Smooth(0.25f, 0.40f, r) * 0.7f);
            ring = Color.Lerp(ring, red * 1.1f, Mathf.Max(0f, lit) * 0.15f);
            float sec = Mathf.Repeat(ang / (Mathf.PI / 4f) + 0.5f, 8f);
            int s = Mathf.FloorToInt(sec) % 8;
            float local = (sec - Mathf.Floor(sec) - 0.5f) * (Mathf.PI / 4f) * r; // arc distance from the sector centre
            // thin divider between sectors
            float edgeArc = (0.5f - Mathf.Abs(sec - Mathf.Floor(sec) - 0.5f)) * (Mathf.PI / 4f) * r;
            if (edgeArc < 0.004f && r > R + 0.04f) ring = Color.Lerp(ring, redDark * 0.8f, 0.8f);
            for (int line = 0; line < 3; line++)
            {
                float lr = 0.262f + line * 0.048f;
                if (Mathf.Abs(r - lr) < 0.015f && Mathf.Abs(local) < 0.05f)
                {
                    bool solid = ((Trigrams[s] >> line) & 1) == 1;
                    if (solid || Mathf.Abs(local) > 0.011f)
                        return Mathf.Abs(r - lr) > 0.010f || Mathf.Abs(local) > 0.044f ? goldLo : Color.Lerp(gold, goldHi, 0.4f);
                }
            }
            ring.a = 1f;
            return ring;
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
