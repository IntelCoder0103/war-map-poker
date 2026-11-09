namespace WarPoker.Core
{
    /// <summary>
    /// Quick-bet helpers for the raise slider. UI convenience only: the server validates
    /// every amount, and the result is always clamped into [minRaiseTo, maxRaiseTo].
    /// </summary>
    public static class RaiseMath
    {
        /// <summary>
        /// "Raise to" amount for a fraction of the pot (1 = pot-sized raise):
        /// call first, then raise by fraction × (pot after the call).
        /// </summary>
        public static long PotFractionRaiseTo(long currentBet, long potTotal, long callAmount,
            double fraction, long minRaiseTo, long maxRaiseTo)
        {
            long potAfterCall = potTotal + callAmount;
            long raiseTo = currentBet + (long)System.Math.Round(potAfterCall * fraction);
            return Clamp(raiseTo, minRaiseTo, maxRaiseTo);
        }

        public static long Clamp(long value, long min, long max)
        {
            if (max < min) max = min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
