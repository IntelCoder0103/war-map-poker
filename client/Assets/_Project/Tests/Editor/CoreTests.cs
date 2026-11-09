using NUnit.Framework;
using WarPoker.Core;

namespace WarPoker.Tests
{
    public class TroopSquadTests
    {
        [Test]
        public void ZeroOrNegativeGivesNoFigures()
        {
            Assert.AreEqual(0, TroopSquad.Figures(0, 20));
            Assert.AreEqual(0, TroopSquad.Figures(-5, 20));
        }

        [Test]
        public void GrowsWithSquareRootOfBigBlinds()
        {
            Assert.AreEqual(1, TroopSquad.Figures(5, 20));     // less than a big blind still shows one
            Assert.AreEqual(1, TroopSquad.Figures(20, 20));    // 1 BB
            Assert.AreEqual(2, TroopSquad.Figures(80, 20));    // 4 BB
            Assert.AreEqual(3, TroopSquad.Figures(100, 20));   // 5 BB -> ceil(2.24)
            Assert.AreEqual(8, TroopSquad.Figures(1000, 20));  // 50 BB -> ceil(7.07)
            Assert.AreEqual(8, TroopSquad.Figures(500000, 10000)); // same depth, King room
        }

        [Test]
        public void CappedAndSafeWithBadUnit()
        {
            Assert.AreEqual(TroopSquad.MaxFigures, TroopSquad.Figures(100000000, 20));
            Assert.AreEqual(3, TroopSquad.Figures(9, 0));      // unit 0 is treated as 1
            Assert.AreEqual(4, TroopSquad.MarchFigures(1000, 20, 4));
            Assert.AreEqual(0, TroopSquad.MarchFigures(0, 20, 4));
        }
    }

    public class StringsTests
    {
        [TearDown]
        public void Reset() { Strings.Load(null); }

        [Test]
        public void DefaultsFormatsAndOverrides()
        {
            Assert.AreEqual("Buy-in 1,000", Strings.Format("BuyIn", "1,000"));
            Strings.Load(new System.Collections.Generic.Dictionary<string, string> { { "BuyIn", "Entry: {0}" }, { "Ready", "Go!" } });
            Assert.AreEqual("Entry: 50", Strings.Format("BuyIn", 50));
            Assert.AreEqual("Go!", Strings.Ready);
            Assert.AreEqual("Retreat", Strings.Leave); // not overridden -> default
        }

        [Test]
        public void SafeFallbacks()
        {
            Assert.AreEqual("no.such.key", Strings.Get("no.such.key"));
            Strings.Load(new System.Collections.Generic.Dictionary<string, string> { { "BuyIn", "Broken {0" } });
            Assert.AreEqual("Broken {0", Strings.Format("BuyIn", 5)); // bad template shown as-is, no exception
            Assert.AreEqual("Error: WEIRD_CODE", Strings.ErrorText("WEIRD_CODE"));
            Assert.AreEqual("1st", Strings.Ordinal(1));
            Assert.AreEqual("12th", Strings.Ordinal(12));
            Assert.AreEqual("23rd", Strings.Ordinal(23));
        }
    }

    public class CardCodeTests
    {
        [Test]
        public void ParsesValidCards()
        {
            CardCode c;
            Assert.IsTrue(CardCode.TryParse("Ah", out c));
            Assert.AreEqual('A', c.Rank); Assert.AreEqual('h', c.Suit);
            Assert.IsTrue(c.IsRed);
            Assert.AreEqual("A", c.RankLabel);

            Assert.IsTrue(CardCode.TryParse("Td", out c));
            Assert.AreEqual("10", c.RankLabel);
            Assert.IsTrue(c.IsRed);

            Assert.IsTrue(CardCode.TryParse("2c", out c));
            Assert.IsFalse(c.IsRed);
            Assert.AreEqual("2c", c.Code);

            Assert.IsTrue(CardCode.TryParse("ks", out c));
            Assert.AreEqual("Ks", c.Code);
        }

        [Test]
        public void RejectsInvalidCards()
        {
            CardCode c;
            Assert.IsFalse(CardCode.TryParse(null, out c));
            Assert.IsFalse(CardCode.TryParse("", out c));
            Assert.IsFalse(CardCode.TryParse("1h", out c));
            Assert.IsFalse(CardCode.TryParse("Ax", out c));
            Assert.IsFalse(CardCode.TryParse("10h", out c));
        }
    }

    public class SeatLayoutTests
    {
        [Test]
        public void LocalSeatIsAlwaysDisplayZero()
        {
            for (int me = 0; me < 8; me++)
                Assert.AreEqual(0, SeatLayout.ToDisplay(me, me, 8));
        }

        [Test]
        public void RotatesClockwise()
        {
            Assert.AreEqual(1, SeatLayout.ToDisplay(4, 3, 8));
            Assert.AreEqual(7, SeatLayout.ToDisplay(2, 3, 8));
            Assert.AreEqual(5, SeatLayout.ToDisplay(0, 3, 8));
        }

        [Test]
        public void NotSeatedMeansNoRotation()
        {
            for (int s = 0; s < 8; s++)
                Assert.AreEqual(s, SeatLayout.ToDisplay(s, -1, 8));
        }

        [Test]
        public void ToSeatInvertsToDisplay()
        {
            for (int me = -1; me < 8; me++)
                for (int s = 0; s < 8; s++)
                    Assert.AreEqual(s, SeatLayout.ToSeat(SeatLayout.ToDisplay(s, me, 8), me, 8));
        }

        [Test]
        public void SlotZeroIsBottom()
        {
            Assert.AreEqual(-90f, SeatLayout.AngleDeg(0, 8), 0.001f);
            Assert.AreEqual(-270f, SeatLayout.AngleDeg(4, 8), 0.001f);
        }
    }

    public class RefillRuleTests
    {
        [Test]
        public void AvailableOnlyBelowThresholdAndAfterTime()
        {
            Assert.IsTrue(RefillRule.IsAvailable(500, null, 1000));
            Assert.IsTrue(RefillRule.IsAvailable(500, 900, 1000));
            Assert.IsTrue(RefillRule.IsAvailable(500, 1000, 1000));
            Assert.IsFalse(RefillRule.IsAvailable(500, 2000, 1000));
            Assert.IsFalse(RefillRule.IsAvailable(1000, null, 1000));
        }

        [Test]
        public void CountdownFormatting()
        {
            Assert.AreEqual("0:05", RefillRule.FormatCountdown(5000));
            Assert.AreEqual("1:00:00", RefillRule.FormatCountdown(3600000));
            Assert.AreEqual("0:00", RefillRule.FormatCountdown(-10));
            Assert.AreEqual(2000, RefillRule.MsUntilAvailable(10, 3000, 1000));
            Assert.AreEqual(0, RefillRule.MsUntilAvailable(5000, 3000, 1000));
        }
    }

    public class RaiseMathTests
    {
        [Test]
        public void PotRaiseIsClamped()
        {
            // Pot 100 total (incl. a bet of 20 to call): pot after call 120, raise to 20 + 120.
            Assert.AreEqual(140, RaiseMath.PotFractionRaiseTo(20, 100, 20, 1.0, 40, 1000));
            Assert.AreEqual(80, RaiseMath.PotFractionRaiseTo(20, 100, 20, 0.5, 40, 1000));
            Assert.AreEqual(1000, RaiseMath.PotFractionRaiseTo(20, 5000, 20, 1.0, 40, 1000));
            Assert.AreEqual(400, RaiseMath.PotFractionRaiseTo(20, 10, 20, 0.5, 400, 1000));
        }
    }
}
