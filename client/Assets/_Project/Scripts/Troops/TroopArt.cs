using System.Collections.Generic;
using UnityEngine;
using WarPoker.UI;

namespace WarPoker.Troops
{
    /// <summary>
    /// The one soldier sprite: Resources/Troops/soldier if present (rendered 3D voxel soldier),
    /// otherwise a procedural spearman placeholder.
    /// </summary>
    public static class TroopArt
    {
        static Sprite _soldier;
        static readonly Dictionary<string, Sprite[]> Strips = new Dictionary<string, Sprite[]>();

        // Playback speed per clip (frames per second).
        public const float IdleFps = 11f, WalkFps = 12f, VictoryFps = 9f, DeathFps = 14f;

        /// <summary>
        /// The rendered frames leave room around the soldier (spear, victory raise, death fall),
        /// so the standing figure fills about half the frame: draw frames this much larger
        /// than the figure size you want on screen.
        /// </summary>
        public const float FrameScale = 2f;

        /// <summary>
        /// Animation frames from Resources/Troops/soldier_{clip} (idle, walk, victory, death):
        /// one row of square frames, frame count = width / height. Null if the strip is missing.
        /// </summary>
        public static Sprite[] Frames(string clip)
        {
            Sprite[] frames;
            if (Strips.TryGetValue(clip, out frames)) return frames;
            frames = null;
            var strip = Resources.Load<Sprite>("Troops/soldier_" + clip);
            if (strip != null)
            {
                var tex = strip.texture;
                Rect r = strip.rect;
                int size = Mathf.RoundToInt(r.height);
                int n = size > 0 ? Mathf.RoundToInt(r.width) / size : 0;
                if (n > 0)
                {
                    frames = new Sprite[n];
                    for (int i = 0; i < n; i++)
                    {
                        frames[i] = Sprite.Create(tex, new Rect(r.x + i * size, r.y, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                        frames[i].name = "soldier_" + clip + "_" + i;
                    }
                }
            }
            Strips[clip] = frames;
            return frames;
        }

        static readonly Color Skin = new Color(0.93f, 0.78f, 0.62f);
        static readonly Color Wood = new Color(0.45f, 0.29f, 0.14f);
        static readonly Color Steel = new Color(0.72f, 0.75f, 0.8f);
        static readonly Color Tunic = new Color(0.86f, 0.18f, 0.12f);
        static readonly Color Outline = new Color(0.18f, 0.12f, 0.07f, 1f);

        public static Sprite Soldier()
        {
            if (_soldier != null) return _soldier;
            _soldier = Resources.Load<Sprite>("Troops/soldier");
            if (_soldier == null) _soldier = ProcSprites.FromFn("troop_soldier", 64, 64, Spearman, 3, 2, Outline);
            return _soldier;
        }

        static Color Spearman(float x, float y)
        {
            // spear on the right with a steel tip
            if (ProcSprites.InTri(x, y, 0.7f, 0.82f, 0.82f, 0.82f, 0.76f, 0.98f)) return Steel;
            if (ProcSprites.SegDist(x, y, 0.76f, 0.04f, 0.76f, 0.84f) < 0.025f) return Wood;
            // head with a helmet, torso, legs
            if (ProcSprites.InCircle(x, y, 0.45f, 0.74f, 0.1f)) return y > 0.77f ? Steel : Skin;
            if (ProcSprites.InBox(x, y, 0.33f, 0.36f, 0.57f, 0.64f)) return Tunic;
            if (ProcSprites.InBox(x, y, 0.35f, 0.08f, 0.43f, 0.37f) || ProcSprites.InBox(x, y, 0.47f, 0.08f, 0.55f, 0.37f))
                return Wood;
            return Color.clear;
        }
    }
}
