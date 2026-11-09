using System.Collections.Generic;
using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>Colour of a point in normalized sprite space (x, y in 0..1, y up). Color.clear = empty.</summary>
    public delegate Color PixelFn(float x, float y);

    /// <summary>
    /// Procedural placeholder sprites drawn into Texture2Ds at runtime (no art files needed).
    /// Everything is cached, so each sprite is generated once.
    /// </summary>
    public static class ProcSprites
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static bool TryGet(string key, out Sprite sprite)
        {
            return Cache.TryGetValue(key, out sprite) && sprite != null;
        }

        public static Sprite Store(string key, Sprite sprite)
        {
            Cache[key] = sprite;
            return sprite;
        }

        // ---------- generic raster ----------

        /// <summary>Rasterizes a pixel function with ss×ss supersampling (anti-aliased edges).</summary>
        public static Color[] Raster(int w, int h, PixelFn fn, int ss)
        {
            if (ss < 1) ss = 1;
            var pixels = new Color[w * h];
            float inv = 1f / (ss * ss);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float u = (x + (sx + 0.5f) / ss) / w;
                            float v = (y + (sy + 0.5f) / ss) / h;
                            Color c = fn(u, v);
                            // premultiply so transparent samples don't darken edges
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    }
                    Color o;
                    if (a > 0.0001f) o = new Color(r / a, g / a, b / a, a * inv);
                    else o = new Color(0, 0, 0, 0);
                    pixels[y * w + x] = o;
                }
            }
            return pixels;
        }

        /// <summary>Adds an outline of the given pixel radius behind the opaque parts.</summary>
        public static void Outline(Color[] px, int w, int h, int radius, Color outline)
        {
            var src = (Color[])px.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (src[i].a >= 0.999f) continue;
                    float maxA = 0f;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            if (dx * dx + dy * dy > radius * radius + 1) continue;
                            float sa = src[yy * w + xx].a;
                            if (sa > maxA) maxA = sa;
                        }
                    }
                    if (maxA <= 0f) continue;
                    // composite the original pixel over the outline colour
                    Color top = src[i];
                    float oa = outline.a * maxA;
                    float outA = top.a + oa * (1f - top.a);
                    if (outA <= 0.0001f) continue;
                    Color c = (top * top.a + outline * oa * (1f - top.a)) / outA;
                    c.a = outA;
                    px[i] = c;
                }
            }
        }

        public static Texture2D MakeTexture(int w, int h, Color[] pixels)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        public static Sprite MakeSprite(Texture2D tex, Vector4 border)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
        }

        /// <summary>Raster + texture + sprite, cached under key.</summary>
        public static Sprite FromFn(string key, int w, int h, PixelFn fn, int ss = 3,
            int outlinePx = 0, Color outline = default(Color))
        {
            Sprite s;
            if (TryGet(key, out s)) return s;
            var px = Raster(w, h, fn, ss);
            if (outlinePx > 0) Outline(px, w, h, outlinePx, outline);
            return Store(key, MakeSprite(MakeTexture(w, h, px), Vector4.zero));
        }

        // ---------- UI basics ----------

        /// <summary>White rounded rectangle with a grey border baked in (tint with Image.color). 9-sliced.</summary>
        public static Sprite RoundedRect(int radius = 14, int border = 3, float borderShade = 0.55f)
        {
            string key = "rr_" + radius + "_" + border + "_" + borderShade;
            Sprite s;
            if (TryGet(key, out s)) return s;
            int size = radius * 2 + 8;
            var fill = Color.white;
            var edge = new Color(borderShade, borderShade, borderShade, 1f);
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px0 = x + 0.5f - half, py0 = y + 0.5f - half;
                    float d = RoundBoxDist(px0, py0, half - 1f, half - 1f, radius);
                    float alpha = Mathf.Clamp01(0.5f - d);
                    Color c = fill;
                    if (border > 0)
                    {
                        float t = Mathf.Clamp01(d + border + 0.5f); // 1 inside the border band
                        c = Color.Lerp(fill, edge, t);
                    }
                    c.a = alpha;
                    px[y * size + x] = c;
                }
            }
            int slice = radius + border + 1;
            return Store(key, MakeSprite(MakeTexture(size, size, px), new Vector4(slice, slice, slice, slice)));
        }

        public static Sprite Circle(int size = 64)
        {
            return FromFn("circle_" + size, size, size, (x, y) =>
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                return dx * dx + dy * dy <= 0.25f ? Color.white : Color.clear;
            }, 3);
        }

        /// <summary>Circle with a darker baked rim (for badges and buttons).</summary>
        /// <summary>
        /// A poker chip / dealer button: face colour, a rim with light notches around the edge,
        /// an inner ring and soft top-light shading. Cached per colour pair.
        /// </summary>
        public static Sprite PokerChip(Color face, Color rim, Color notch)
        {
            string key = "chip_" + ColorUtility.ToHtmlStringRGB(face) + ColorUtility.ToHtmlStringRGB(rim);
            Sprite cached;
            if (TryGet(key, out cached)) return cached;
            Color edge = new Color(0.12f, 0.08f, 0.05f, 1f);
            return FromFn(key, 128, 128, (x, y) =>
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centre .. 1 edge
                if (r > 1f) return Color.clear;
                if (r > 0.94f) return edge;                                  // dark outline
                float shade = 1.08f - 0.22f * (0.5f - dy) - 0.1f * r;        // light from the top
                Color c;
                if (r > 0.70f)
                {
                    // rim with 8 light notches
                    float a = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
                    bool isNotch = Mathf.Repeat(a * 8f + 0.25f, 1f) < 0.32f;
                    c = isNotch ? notch : rim;
                }
                else if (r > 0.64f) c = Color.Lerp(notch, rim, 0.35f);      // thin inner ring
                else c = face;
                c = new Color(Mathf.Clamp01(c.r * shade), Mathf.Clamp01(c.g * shade), Mathf.Clamp01(c.b * shade), 1f);
                // glossy highlight
                float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(0.38f, 0.72f)) / 0.22f);
                return Color.Lerp(c, Color.white, hl * 0.25f);
            }, 3);
        }

        public static Sprite Disc(int size = 64, float rim = 0.12f)
        {
            return FromFn("disc_" + size + "_" + rim, size, size, (x, y) =>
            {
                float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f));
                if (d > 0.5f) return Color.clear;
                return d > 0.5f - rim * 0.5f ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            }, 3);
        }

        /// <summary>A ring, used as the radial turn-timer (Image.Type.Filled).</summary>
        public static Sprite Ring(int size = 128, float thickness = 0.12f)
        {
            return FromFn("ring_" + size + "_" + thickness, size, size, (x, y) =>
            {
                float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f));
                return d <= 0.5f && d >= 0.5f - thickness ? Color.white : Color.clear;
            }, 3);
        }

        /// <summary>Soft radial glow, white, fading to transparent.</summary>
        public static Sprite Glow(int size = 128)
        {
            return FromFn("glow_" + size, size, size, (x, y) =>
            {
                float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - d);
                return new Color(1, 1, 1, a * a);
            }, 1);
        }

        /// <summary>Soft rounded-rect glow (for camp highlights and card highlights). 9-sliced.</summary>
        public static Sprite SoftRect()
        {
            string key = "softrect";
            Sprite s;
            if (TryGet(key, out s)) return s;
            int size = 64;
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundBoxDist(x + 0.5f - half, y + 0.5f - half, half - 20f, half - 20f, 8f);
                    float a = Mathf.Clamp01(1f - Mathf.Max(0f, d) / 12f);
                    px[y * size + x] = new Color(1, 1, 1, a * a);
                }
            }
            return Store(key, MakeSprite(MakeTexture(size, size, px), new Vector4(28, 28, 28, 28)));
        }

        /// <summary>A banner/pennant with a swallow-tail bottom, white (tint it).</summary>
        public static Sprite Pennant()
        {
            return FromFn("pennant", 64, 96, (x, y) =>
            {
                if (x < 0.06f || x > 0.94f || y > 0.97f) return Color.clear;
                // notch at the bottom: V cut
                float notch = 0.22f * (1f - Mathf.Abs(x - 0.5f) * 2f);
                if (y < notch) return Color.clear;
                bool edge = x < 0.12f || x > 0.88f || y > 0.92f || y < notch + 0.05f;
                return edge ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
            }, 3);
        }

        // ---------- shape helpers (normalized coords) ----------

        public static float RoundBoxDist(float px, float py, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px) - hx + r;
            float qy = Mathf.Abs(py) - hy + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        public static bool InCircle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        public static bool InEllipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }

        public static bool InBox(float x, float y, float x0, float y0, float x1, float y1)
        {
            return x >= x0 && x <= x1 && y >= y0 && y <= y1;
        }

        public static bool InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Distance from p to segment ab.</summary>
        public static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float apx = px - ax, apy = py - ay;
            float len2 = abx * abx + aby * aby;
            float t = len2 > 0f ? Mathf.Clamp01((apx * abx + apy * aby) / len2) : 0f;
            float dx = apx - abx * t, dy = apy - aby * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Even-odd point-in-polygon.</summary>
        public static bool InPoly(float x, float y, Vector2[] pts)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                if (((pts[i].y > y) != (pts[j].y > y)) &&
                    (x < (pts[j].x - pts[i].x) * (y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Heater shield (flat top, pointed bottom), shrunk by inset.</summary>
        public static bool InShield(float x, float y, float inset)
        {
            float l = 0.14f + inset, r = 0.86f - inset, top = 0.9f - inset, mid = 0.48f;
            if (y > top || x < l || x > r) return false;
            if (y >= mid) return true;
            // lower part: two arcs meeting at the bottom point
            float bottom = 0.06f + inset * 1.6f;
            float t = (mid - y) / (mid - bottom);   // 0 at mid, 1 at the point
            if (t > 1f) return false;
            float halfW = (r - l) * 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            return Mathf.Abs(x - 0.5f) <= halfW;
        }
    }
}
