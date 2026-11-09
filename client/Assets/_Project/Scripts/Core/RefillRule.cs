namespace WarPoker.Core
{
    /// <summary>The refill rule from docs/protocol.md (display only — the server decides).</summary>
    public static class RefillRule
    {
        public const long Threshold = 1000;

        /// <summary>Available when balance &lt; 1000 and nextRefillAt is null or in the past.</summary>
        public static bool IsAvailable(long balance, long? nextRefillAt, long nowMs)
        {
            if (balance >= Threshold) return false;
            return !nextRefillAt.HasValue || nextRefillAt.Value <= nowMs;
        }

        /// <summary>Milliseconds until the refill unlocks (0 if available now or not applicable).</summary>
        public static long MsUntilAvailable(long balance, long? nextRefillAt, long nowMs)
        {
            if (balance >= Threshold || !nextRefillAt.HasValue) return 0;
            long ms = nextRefillAt.Value - nowMs;
            return ms > 0 ? ms : 0;
        }

        /// <summary>Formats a duration as H:MM:SS (or M:SS under an hour).</summary>
        public static string FormatCountdown(long ms)
        {
            if (ms < 0) ms = 0;
            long total = (ms + 999) / 1000;
            long h = total / 3600;
            long m = (total % 3600) / 60;
            long s = total % 60;
            if (h > 0) return h + ":" + m.ToString("00") + ":" + s.ToString("00");
            return m + ":" + s.ToString("00");
        }
    }
}
