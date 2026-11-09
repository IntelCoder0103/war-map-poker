namespace WarPoker.Core
{
    /// <summary>
    /// Maps server seat indices to screen positions so the local player is always at the
    /// bottom (display index 0). Display indices go clockwise around the map.
    /// </summary>
    public static class SeatLayout
    {
        /// <summary>Server seat → display slot. If not seated (yourSeat &lt; 0), no rotation.</summary>
        public static int ToDisplay(int seat, int yourSeat, int maxSeats)
        {
            if (maxSeats <= 0) return seat;
            int offset = yourSeat >= 0 ? yourSeat : 0;
            return Mod(seat - offset, maxSeats);
        }

        /// <summary>Display slot → server seat (inverse of ToDisplay).</summary>
        public static int ToSeat(int display, int yourSeat, int maxSeats)
        {
            if (maxSeats <= 0) return display;
            int offset = yourSeat >= 0 ? yourSeat : 0;
            return Mod(display + offset, maxSeats);
        }

        /// <summary>
        /// Angle in degrees (math convention, 0 = right, 90 = up) of a display slot on the
        /// seat ellipse. Slot 0 is bottom center; the rest follow clockwise.
        /// </summary>
        public static float AngleDeg(int display, int maxSeats)
        {
            if (maxSeats <= 0) maxSeats = 8;
            return -90f - display * (360f / maxSeats);
        }

        static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }
    }
}
