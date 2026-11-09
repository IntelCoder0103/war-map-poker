namespace WarPoker.Core
{
    /// <summary>
    /// There is one soldier type. An amount is drawn as a small squad of identical soldiers next to
    /// the number: 1-9 → 1 figure, 10-99 → 2, 100-999 → 3, 1,000-9,999 → 4, 10,000+ → 5.
    /// </summary>
    public static class TroopSquad
    {
        public const int MaxFigures = 5;

        /// <summary>How many soldier figures to draw for an amount (0 for amounts ≤ 0).</summary>
        public static int Figures(long amount)
        {
            if (amount <= 0) return 0;
            int n = 1;
            for (long v = amount; v >= 10 && n < MaxFigures; v /= 10) n++;
            return n;
        }

        /// <summary>Figures for a marching animation, capped at maxFigures.</summary>
        public static int MarchFigures(long amount, int maxFigures)
        {
            int n = Figures(amount);
            return n < maxFigures ? n : (maxFigures < 0 ? 0 : maxFigures);
        }
    }
}
