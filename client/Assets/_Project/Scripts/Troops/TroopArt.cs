using System.Collections.Generic;
using UnityEngine;
using WarPoker.Core;
using WarPoker.UI;

namespace WarPoker.Troops
{
    /// <summary>
    /// Unit sprites: Resources/Troops/{spearman|archer|ranger|cavalry|knight} if present,
    /// otherwise a procedural placeholder figure (distinct shape and colour per unit).
    /// </summary>
    public static class TroopArt
    {
        static readonly Dictionary<TroopUnit, Sprite> Sprites = new Dictionary<TroopUnit, Sprite>();

        static readonly Color Skin = new Color(0.93f, 0.78f, 0.62f);
        static readonly Color Wood = new Color(0.45f, 0.29f, 0.14f);
        static readonly Color Steel = new Color(0.72f, 0.75f, 0.8f);
        static readonly Color Outline = new Color(0.18f, 0.12f, 0.07f, 1f);

        public static Sprite Get(TroopUnit unit)
        {
            Sprite s;
            if (Sprites.TryGetValue(unit, out s) && s != null) return s;
            s = Resources.Load<Sprite>("Troops/" + TroopBreakdown.KeyOf(unit));
            if (s == null) s = Procedural(unit);
            Sprites[unit] = s;
            return s;
        }

        static Sprite Procedural(TroopUnit unit)
        {
            PixelFn fn;
            switch (unit)
            {
                case TroopUnit.Knight: fn = Knight; break;
                case TroopUnit.Cavalry: fn = Cavalry; break;
                case TroopUnit.Ranger: fn = Ranger; break;
                case TroopUnit.Archer: fn = Archer; break;
                default: fn = Spearman; break;
            }
            return ProcSprites.FromFn("troop_" + unit, 64, 64, fn, 3, 2, Outline);
        }

        // A standing soldier: head + torso + legs, with a tunic colour.
        static Color Soldier(float x, float y, Color tunic, Color helmet)
        {
            if (ProcSprites.InCircle(x, y, 0.45f, 0.74f, 0.1f))
                return y > 0.77f ? helmet : Skin;
            if (ProcSprites.InBox(x, y, 0.33f, 0.36f, 0.57f, 0.64f)) return tunic;
            if (ProcSprites.InBox(x, y, 0.35f, 0.08f, 0.43f, 0.37f) || ProcSprites.InBox(x, y, 0.47f, 0.08f, 0.55f, 0.37f))
                return Wood;
            return Color.clear;
        }

        static Color Spearman(float x, float y)
        {
            // spear on the right with a steel tip
            if (ProcSprites.InTri(x, y, 0.7f, 0.82f, 0.82f, 0.82f, 0.76f, 0.98f)) return Steel;
            if (ProcSprites.SegDist(x, y, 0.76f, 0.04f, 0.76f, 0.84f) < 0.025f) return Wood;
            return Soldier(x, y, new Color(0.72f, 0.58f, 0.36f), Steel);
        }

        static Color Archer(float x, float y)
        {
            // bow: arc on the right, with a string
            float dx = x - 0.56f, dy = y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (x > 0.66f && r > 0.29f && r < 0.34f) return Wood;
            if (Mathf.Abs(x - 0.71f) < 0.01f && Mathf.Abs(y - 0.5f) < 0.26f) return new Color(0.9f, 0.9f, 0.85f);
            return Soldier(x, y, new Color(0.3f, 0.52f, 0.25f), new Color(0.3f, 0.45f, 0.22f));
        }

        static Color Ranger(float x, float y)
        {
            Color cloak = new Color(0.16f, 0.36f, 0.34f);
            // hooded cloak: a tall triangle with a hood circle
            if (ProcSprites.InCircle(x, y, 0.5f, 0.74f, 0.13f))
                return ProcSprites.InCircle(x, y, 0.5f, 0.71f, 0.07f) ? Skin : cloak;
            if (ProcSprites.InTri(x, y, 0.22f, 0.08f, 0.78f, 0.08f, 0.5f, 0.74f)) return cloak;
            // dagger
            if (ProcSprites.SegDist(x, y, 0.8f, 0.2f, 0.88f, 0.5f) < 0.025f) return Steel;
            return Color.clear;
        }

        static Color Cavalry(float x, float y)
        {
            Color horse = new Color(0.52f, 0.33f, 0.18f);
            Color rider = new Color(0.62f, 0.15f, 0.12f);
            if (ProcSprites.InCircle(x, y, 0.47f, 0.86f, 0.07f)) return Skin;
            if (ProcSprites.InBox(x, y, 0.39f, 0.52f, 0.55f, 0.8f)) return rider;
            if (ProcSprites.InEllipse(x, y, 0.47f, 0.44f, 0.3f, 0.13f)) return horse;
            if (ProcSprites.SegDist(x, y, 0.72f, 0.48f, 0.86f, 0.68f) < 0.06f) return horse;  // neck + head
            if (ProcSprites.SegDist(x, y, 0.08f, 0.5f, 0.18f, 0.42f) < 0.025f) return Wood;     // tail
            float legW = 0.03f;
            if (ProcSprites.SegDist(x, y, 0.26f, 0.4f, 0.24f, 0.06f) < legW) return horse;
            if (ProcSprites.SegDist(x, y, 0.36f, 0.4f, 0.38f, 0.06f) < legW) return horse;
            if (ProcSprites.SegDist(x, y, 0.6f, 0.4f, 0.58f, 0.06f) < legW) return horse;
            if (ProcSprites.SegDist(x, y, 0.68f, 0.4f, 0.72f, 0.06f) < legW) return horse;
            return Color.clear;
        }

        static Color Knight(float x, float y)
        {
            // heraldic shield: steel with a gold rim and a crimson cross
            if (!ProcSprites.InShield(x, y, 0f)) return Color.clear;
            if (!ProcSprites.InShield(x, y, 0.07f)) return Theme.Gold;
            bool cross = Mathf.Abs(x - 0.5f) < 0.07f || Mathf.Abs(y - 0.6f) < 0.07f;
            return cross ? new Color(0.65f, 0.12f, 0.1f) : Steel;
        }
    }
}
