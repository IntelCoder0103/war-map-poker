namespace WarPoker.Core
{
    /// <summary>
    /// A parsed card string such as "Ah" or "Td" (rank from 23456789TJQKA, suit from c d h s).
    /// </summary>
    public struct CardCode
    {
        public char Rank;   // '2'..'9', 'T', 'J', 'Q', 'K', 'A'
        public char Suit;   // 'c', 'd', 'h', 's'

        public const string Ranks = "23456789TJQKA";
        public const string Suits = "cdhs";

        public bool IsRed { get { return Suit == 'h' || Suit == 'd'; } }

        /// <summary>Rank as shown on the card strip: "10" for T, otherwise the rank letter.</summary>
        public string RankLabel { get { return Rank == 'T' ? "10" : Rank.ToString(); } }

        /// <summary>The canonical 2-character code, e.g. "Ah".</summary>
        public string Code { get { return new string(new[] { Rank, Suit }); } }

        public static bool TryParse(string text, out CardCode card)
        {
            card = default(CardCode);
            if (string.IsNullOrEmpty(text) || text.Length != 2) return false;
            char r = char.ToUpperInvariant(text[0]);
            char s = char.ToLowerInvariant(text[1]);
            if (Ranks.IndexOf(r) < 0 || Suits.IndexOf(s) < 0) return false;
            card.Rank = r;
            card.Suit = s;
            return true;
        }

        public override string ToString()
        {
            return Code;
        }
    }
}
