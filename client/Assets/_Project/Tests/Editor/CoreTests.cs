using NUnit.Framework;
using WarPoker.Core;

namespace WarPoker.Tests
{
    public class TroopSquadTests
    {
        [Test]
        public void ZeroOrNegativeGivesNoFigures()
        {
            Assert.AreEqual(0, TroopSquad.Figures(0));
            Assert.AreEqual(0, TroopSquad.Figures(-5));
        }

        [Test]
        public void OneMoreFigurePerDigitUpToFive()
        {
            Assert.AreEqual(1, TroopSquad.Figures(1));
            Assert.AreEqual(1, TroopSquad.Figures(9));
            Assert.AreEqual(2, TroopSquad.Figures(10));
            Assert.AreEqual(2, TroopSquad.Figures(99));
            Assert.AreEqual(3, TroopSquad.Figures(100));
            Assert.AreEqual(4, TroopSquad.Figures(1000));
            Assert.AreEqual(4, TroopSquad.Figures(9999));
            Assert.AreEqual(5, TroopSquad.Figures(10000));
            Assert.AreEqual(5, TroopSquad.Figures(123456789));
        }

        [Test]
        public void MarchFiguresAreCapped()
        {
            Assert.AreEqual(3, TroopSquad.MarchFigures(30020, 3));
            Assert.AreEqual(2, TroopSquad.MarchFigures(20, 5));
            Assert.AreEqual(0, TroopSquad.MarchFigures(0, 5));
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
