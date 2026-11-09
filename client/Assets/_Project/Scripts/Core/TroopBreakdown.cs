using System.Collections.Generic;

namespace WarPoker.Core
{
    /// <summary>Troop unit types, largest first. Values follow docs/protocol.md.</summary>
    public enum TroopUnit
    {
        Knight,
        Cavalry,
        Ranger,
        Archer,
        Spearman
    }

    /// <summary>One group of a breakdown, e.g. 3 × Cavalry.</summary>
    public struct TroopCount
    {
        public TroopUnit Unit;
        public long Count;

        public TroopCount(TroopUnit unit, long count)
        {
            Unit = unit;
            Count = count;
        }

        public override string ToString()
        {
            return Unit + " x" + Count;
        }
    }

    /// <summary>Turns a soldier amount into troop units, greedy, largest unit first (like making change).</summary>
    public static class TroopBreakdown
    {
        public static readonly TroopUnit[] Units =
        {
            TroopUnit.Knight, TroopUnit.Cavalry, TroopUnit.Ranger, TroopUnit.Archer, TroopUnit.Spearman
        };

        public static long ValueOf(TroopUnit unit)
        {
            switch (unit)
            {
                case TroopUnit.Knight: return 10000;
                case TroopUnit.Cavalry: return 1000;
                case TroopUnit.Ranger: return 100;
                case TroopUnit.Archer: return 10;
                default: return 1;
            }
        }

        /// <summary>Lowercase name, used for Resources paths ("Troops/knight").</summary>
        public static string KeyOf(TroopUnit unit)
        {
            switch (unit)
            {
                case TroopUnit.Knight: return "knight";
                case TroopUnit.Cavalry: return "cavalry";
                case TroopUnit.Ranger: return "ranger";
                case TroopUnit.Archer: return "archer";
                default: return "spearman";
            }
        }

        /// <summary>Breakdown with only the non-zero groups, largest unit first. Amounts ≤ 0 give an empty list.</summary>
        public static List<TroopCount> Break(long amount)
        {
            var result = new List<TroopCount>();
            if (amount <= 0) return result;
            long rest = amount;
            foreach (var unit in Units)
            {
                long value = ValueOf(unit);
                long count = rest / value;
                if (count > 0)
                {
                    result.Add(new TroopCount(unit, count));
                    rest -= count * value;
                }
            }
            return result;
        }

        /// <summary>
        /// A short list of unit icons for a marching animation: the largest units first,
        /// at most <paramref name="maxIcons"/> icons in total.
        /// </summary>
        public static List<TroopUnit> MarchIcons(long amount, int maxIcons)
        {
            var icons = new List<TroopUnit>();
            if (maxIcons <= 0) return icons;
            foreach (var group in Break(amount))
            {
                for (long i = 0; i < group.Count && icons.Count < maxIcons; i++)
                    icons.Add(group.Unit);
                if (icons.Count >= maxIcons) break;
            }
            return icons;
        }
    }
}
