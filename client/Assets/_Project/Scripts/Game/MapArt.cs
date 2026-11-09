using UnityEngine;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// The war-map background. Uses Resources/Map/background if present; otherwise draws a
    /// procedural old parchment map: stained paper, a river, forests, mountains, a faint grid
    /// and burnt edges. Also a compass rose (Resources/Map/compass override).
    /// </summary>
    public static class MapArt
    {
        public static Sprite Background()
        {
            var custom = Resources.Load<Sprite>("Map/background");
            if (custom != null) return custom;
            Sprite cached;
            if (ProcSprites.TryGet("map_bg", out cached)) return cached;

            const int W = 768, H = 432;
            var px = new Color[W * H];
            Color paper = new Color(0.90f, 0.82f, 0.64f);
            Color stain = new Color(0.74f, 0.60f, 0.40f);
            Color ink = new Color(0.36f, 0.25f, 0.15f);
            Color water = new Color(0.45f, 0.55f, 0.58f);
            Color forest = new Color(0.40f, 0.45f, 0.28f);
            float aspect = (float)W / H;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    // paper: large soft stains + fine grain
                    float n1 = Mathf.PerlinNoise(u * 3.1f + 11.3f, v * 3.1f + 7.7f);
                    float n2 = Mathf.PerlinNoise(u * 22f + 3.3f, v * 22f + 1.1f);
                    Color c = Color.Lerp(paper, stain, Mathf.Clamp01((n1 - 0.45f) * 1.4f) * 0.55f + n2 * 0.08f);

                    // faint latitude/longitude grid
                    float gx = Mathf.Repeat(u * 12f, 1f), gy = Mathf.Repeat(v * 7f, 1f);
                    if (gx < 0.012f || gy < 0.02f) c = Color.Lerp(c, ink, 0.12f);

                    // river winding from top-left to bottom-right, with a tributary
                    float riverY = 0.82f - u * 0.7f + 0.07f * Mathf.Sin(u * 11f) + 0.03f * Mathf.Sin(u * 27f + 1f);
                    float dr = Mathf.Abs(v - riverY);
                    float width = 0.006f + 0.006f * u;
                    if (dr < width) c = Color.Lerp(c, water, 0.85f);
                    else if (dr < width + 0.004f) c = Color.Lerp(c, ink, 0.45f);
                    float tribX = 0.72f + 0.04f * Mathf.Sin(v * 14f);
                    if (v > riverY && Mathf.Abs(u - tribX) < 0.003f) c = Color.Lerp(c, water, 0.7f);

                    // forests: clusters of small dots where noise is high
                    float fn = Mathf.PerlinNoise(u * 6f + 40f, v * 6f + 20f);
                    if (fn > 0.66f)
                    {
                        float cx = Mathf.Repeat(u * 90f, 1f) - 0.5f, cy = Mathf.Repeat(v * 50f, 1f) - 0.5f;
                        if (cx * cx + cy * cy < 0.09f) c = Color.Lerp(c, forest, 0.75f);
                    }

                    // mountains: little ink chevrons where another noise is high
                    float mn = Mathf.PerlinNoise(u * 5f + 90f, v * 5f + 60f);
                    if (mn > 0.68f)
                    {
                        float mx = Mathf.Repeat(u * 40f, 1f), my = Mathf.Repeat(v * 22f, 1f);
                        float peak = 1f - Mathf.Abs(mx - 0.5f) * 2f; // 0..1 triangle wave
                        if (Mathf.Abs(my - peak * 0.7f) < 0.07f && my < 0.75f) c = Color.Lerp(c, ink, 0.6f);
                    }

                    // vignette + burnt, irregular edge
                    float ex = Mathf.Min(u, 1f - u) * aspect, ey = Mathf.Min(v, 1f - v);
                    float edge = Mathf.Min(ex, ey);
                    float wobble = Mathf.PerlinNoise(u * 18f, v * 18f) * 0.03f;
                    float burn = Mathf.Clamp01((0.09f + wobble - edge) / 0.09f);
                    c = Color.Lerp(c, new Color(0.45f, 0.30f, 0.16f), burn * 0.75f);
                    if (edge < 0.012f + wobble * 0.4f) c = Color.Lerp(c, new Color(0.22f, 0.14f, 0.08f), 0.85f);

                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            var sprite = ProcSprites.MakeSprite(ProcSprites.MakeTexture(W, H, px), Vector4.zero);
            return ProcSprites.Store("map_bg", sprite);
        }

        public static Sprite Compass()
        {
            var custom = Resources.Load<Sprite>("Map/compass");
            if (custom != null) return custom;
            Color ink = new Color(0.30f, 0.20f, 0.12f);
            Color red = new Color(0.58f, 0.13f, 0.10f);
            Color paper = new Color(0.95f, 0.9f, 0.76f);
            return ProcSprites.FromFn("compass", 192, 192, (x, y) =>
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                // 8-point star: long points on N/E/S/W, short ones between
                float k = Mathf.Abs(Mathf.Cos(ang * 4f));          // 1 on main axes and diagonals
                float main = Mathf.Abs(Mathf.Cos(ang));            // helps tell axes from diagonals
                bool axis = main > 0.92f || main < 0.39f;
                float reach = axis ? 0.47f : 0.3f;
                float star = reach * Mathf.Pow(k, 6f);
                if (r < Mathf.Max(star, 0.07f))
                {
                    bool north = dy > 0 && Mathf.Abs(dx) < dy * 0.4f;
                    // split each point into a light and a dark half
                    bool half = Mathf.Repeat(ang / (Mathf.PI / 4f), 1f) < 0.5f;
                    if (north) return half ? red : new Color(0.4f, 0.08f, 0.06f);
                    return half ? ink : paper;
                }
                if (r > 0.33f && r < 0.345f) return ink;
                if (r > 0.36f && r < 0.365f) return ink;
                return Color.clear;
            }, 2);
        }
    }
}
