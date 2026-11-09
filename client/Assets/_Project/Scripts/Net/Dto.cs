using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// Data shapes from docs/protocol.md. Field names match the JSON (camelCase) exactly.
// Public fields + Newtonsoft; link.xml keeps this namespace from being stripped by IL2CPP.
namespace WarPoker.Net
{
    public class UserDto
    {
        public string id;
        public string username;
        public long balance;
        public long? nextRefillAt;
    }

    public class AuthResponse
    {
        public string token;
        public UserDto user;
    }

    public class UserResponse
    {
        public UserDto user;
    }

    public class RoomDto
    {
        public string id;
        public string name;
        public long buyIn;
        public long smallBlind;
        public long bigBlind;
        public int players;
        public int tables;
        public int maxSeats = 8;
    }

    public class RoomsResponse
    {
        public List<RoomDto> rooms;
    }

    public class ErrorResponse
    {
        public string error;
        public string message;
    }

    public class PotDto
    {
        public long amount;
        public List<int> eligibleSeats;
    }

    public class LegalDto
    {
        public bool canFold;
        public bool canCheck;
        public bool canCall;
        public long callAmount;
        public bool canRaise;
        public bool canAllIn;
        public long minRaiseTo;
        public long maxRaiseTo;
    }

    public class SeatDto
    {
        public int seat;
        public string userId;
        public string username;
        public long stack;
        public long bet;
        public long totalBet;       // committed in the whole hand (stays in front of the player until the pot is paid)
        public bool inHand;
        public bool folded;
        public bool allIn;
        public bool ready;
        public bool connected = true;
        public bool leavingAfterHand;   // pressed Retreat during a hand: folds on their turn, leaves when it ends
        public string lastAction;
        public List<string> cards;
    }

    public class Snapshot
    {
        public string tableId;
        public string level;
        public string name;
        public long buyIn;
        public long smallBlind;
        public long bigBlind;
        public int maxSeats = 8;
        public long seq;
        public long serverTime;
        public string phase = "waiting";
        public int handNumber;
        public int dealerSeat = -1;
        public int sbSeat = -1;
        public int bbSeat = -1;
        public List<string> board;
        public List<PotDto> pots;
        public long potTotal;
        public long currentBet;
        public int turnSeat = -1;
        public long turnDeadline;
        public long turnTimeMs = 20000;
        public int yourSeat = -1;
        public List<string> yourCards;
        public List<SeatDto> seats;
        public LegalDto legal;
        public bool canShowCards;   // I won by everyone folding and may show my cards (table:show)
        public long readyDeadline;  // waiting phase: when players who aren't ready go back to the lobby (0 = no countdown)

        /// <summary>Seat entry or null (empty seat / out of range).</summary>
        public SeatDto Seat(int index)
        {
            if (seats == null || index < 0 || index >= seats.Count) return null;
            return seats[index];
        }

        public SeatDto Me { get { return Seat(yourSeat); } }

        public long GatheredPot
        {
            get
            {
                long sum = 0;
                if (pots != null) foreach (var p in pots) if (p != null) sum += p.amount;
                return sum;
            }
        }
    }

    public class TableEventMsg
    {
        public long seq;
        public string type;
        public JObject data;
        public Snapshot state;
    }

    public class BalanceMsg
    {
        public long balance;
        public long? nextRefillAt;
    }

    public class RemovedMsg
    {
        public string reason;
    }

    // ---- table:event data payloads ----

    public class SeatAmount
    {
        public int seat;
        public long amount;
    }

    public class HandStartedData
    {
        public int handNumber;
        public int dealerSeat;
        public int sbSeat;
        public int bbSeat;
        public List<int> seats;
    }

    public class BlindsData
    {
        public SeatAmount sb;
        public SeatAmount bb;
    }

    public class ActionData
    {
        public int seat;
        public string type;
        public long amount;
        public long bet;
        public long stack;
        public bool allIn;
        public bool auto;
    }

    public class StreetData
    {
        public string phase;
        public List<string> newCards;
        public List<string> board;
    }

    public class Reveal
    {
        public int seat;
        public List<string> cards;
        public string handName;
        public List<string> bestCards;
        public int rank;            // 1 = best hand; ties share a rank
    }

    public class CardsShownData
    {
        public int seat;
        public List<string> cards;
    }

    public class ShowdownData
    {
        public List<Reveal> reveals;
    }

    public class Award
    {
        public long amount;
        public List<SeatAmount> winners;
        public string handName;
        public List<string> bestCards;
    }

    public class PotAwardedData
    {
        public List<Award> awards;
    }

    public class SeatOnlyData
    {
        public int seat;
        public string username;
        public string reason;
        public bool connected;
    }
}
