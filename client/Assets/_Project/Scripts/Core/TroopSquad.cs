using System;

namespace WarPoker.Core
{
    /// <summary>
    /// There is one soldier type. An amount is drawn as a squad of identical soldiers whose size
    /// grows with the square root of the amount, measured in big blinds:
    /// figures = ceil(sqrt(amount / bigBlind)), between 1 and MaxFigures.
    /// E.g. at blinds 10/20: a 20 bet → 1, 80 → 2, 1,000 (50 BB) → 8, 8,000 (400 BB) → 20.
    /// The same stack in big blinds looks the same in every room.
    /// </summary>
    public static class TroopSquad
    {
        /// <summary>Largest squad drawn (game.json "maxSquadFigures" can change it).</summary>
        public static int MaxFigures = 20;

        /// <summary>How many soldier figures to draw (0 for amounts ≤ 0).</summary>
        public static int Figures(long amount, long unit)
        {
            if (amount <= 0) return 0;
            if (unit <= 0) unit = 1;
            double n = Math.Ceiling(Math.Sqrt((double)amount / unit) - 1e-9);
            if (n < 1) n = 1;
            return n > MaxFigures ? MaxFigures : (int)n;
        }

        /// <summary>Figures for a marching animation, capped at maxFigures.</summary>
        public static int MarchFigures(long amount, long unit, int maxFigures)
        {
            int n = Figures(amount, unit);
            if (maxFigures < 0) maxFigures = 0;
            return n < maxFigures ? n : maxFigures;
        }
    }
}
