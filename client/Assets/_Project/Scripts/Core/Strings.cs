using System;
using System.Collections.Generic;
using System.Globalization;

namespace WarPoker.Core
{
    /// <summary>
    /// All wording shown to players. Every phrase has a key and an English default below.
    /// To change wording without touching code, edit Resources/Config/text.json: any key there
    /// replaces the default (loaded at startup, see GameSettings). Missing keys fall back to the
    /// default, so the file only needs the lines you change. Phrases with values use {0}, {1}, ...
    /// </summary>
    public static class Strings
    {
        // key -> English default. Keep keys stable: text.json and the code both refer to them.
        static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            // General
            { "GameTitle", "War-Map Poker" },
            { "Subtitle", "Hold'em on the field of battle" },
            { "Reconnecting", "Connection lost - reconnecting..." },
            { "SoldiersLabel", "soldiers" },

            // Login
            { "Username", "Commander name" },
            { "Password", "Password" },
            { "Login", "Login" },
            { "Register", "Enlist" },
            { "ServerAddress", "Server" },
            { "Connecting", "Sending a messenger..." },
            { "Retry", "Retry" },
            { "EnterNameAndPassword", "Enter a name and a password." },
            { "ServerUnreachable", "Cannot reach the server. Check the address and your connection." },
            { "SessionReplaced", "Your account was opened on another device." },
            { "SessionExpired", "Your session expired. Please log in again." },

            // Lobby
            { "Logout", "Logout" },
            { "Refill", "Claim refill" },
            { "RefillIn", "Refill in {0}" },
            { "RefillOnlyBelow", "Refill below {0}" },
            { "RefillAvailable", "Top up to {0}" },
            { "RefillArrived", "Fresh recruits have arrived!" },
            { "ChooseBattlefield", "Choose your battlefield" },
            { "BuyIn", "Buy-in {0}" },
            { "Blinds", "Blinds {0} / {1}" },
            { "Players", "{0} seated" },
            { "March", "March!" },
            { "Joining", "Marching to the table..." },
            { "NeedSoldiers", "You need {0} soldiers to march on {1}. Your army: {2}." },

            // Table
            { "Leave", "Retreat" },
            { "CancelRetreat", "Stay" },
            { "Tag.leaving", "RETREATING" },
            { "RetreatAfterHand", "You will retreat when this hand ends" },
            { "TableInfo", "{0}   Blinds {1}/{2}" },
            { "Hand", "Hand #{0}" },
            { "Ready", "Ready for battle" },
            { "ReadyBadge", "READY" },
            { "WaitingForPlayers", "Waiting for more commanders to join ({0}/{1} ready)" },
            { "WaitingForOthers", "Waiting for the other commanders ({0}/{1} ready)" },
            { "BattleStartsIn", "Battle starts in {0}s ({1}/{2} ready)" },
            { "PressReadyWithin", "Press Ready within {0}s or return to the barracks!" },
            { "AwaitingOpponent", "Awaiting an opponent..." },
            { "BattlePauses", "The battle pauses - press Ready" },
            { "NextHand", "Joins next hand" },
            { "OpenCamp", "Open camp" },
            { "Offline", "offline" },
            { "SeatFallback", "Seat {0}" },
            { "DealerMark", "D" },
            { "SmallBlindMark", "SB" },
            { "BigBlindMark", "BB" },
            { "PotLabel", "Pot {0}" },

            // Action buttons
            { "Fold", "Fold" },
            { "Check", "Check" },
            { "Call", "Call {0}" },
            { "Raise", "Raise" },
            { "RaiseTo", "Raise to {0}" },
            { "RaiseToAll", "Raise to {0} (all)" },
            { "AllIn", "All-in" },
            { "Min", "Min" },
            { "HalfPot", "1/2 Pot" },
            { "Pot", "Pot" },
            { "ShowCards", "Show cards" },

            // Text that floats over a camp when a player acts
            { "Float.fold", "FOLD" },
            { "Float.foldAuto", "FOLD (time)" },
            { "Float.check", "CHECK" },
            { "Float.checkAuto", "CHECK (time)" },
            { "Float.call", "CALL {0}" },
            { "Float.raise", "RAISE TO {0}" },
            { "Float.allin", "ALL-IN {0}!" },
            { "Float.shows", "SHOWS" },

            // Last action under a camp
            { "Action.fold", "FOLD" },
            { "Action.check", "CHECK" },
            { "Action.call", "CALL" },
            { "Action.raise", "RAISE" },
            { "Action.allin", "ALL-IN" },
            { "Action.sb", "SMALL BLIND" },
            { "Action.bb", "BIG BLIND" },

            // Showdown
            { "Showdown", "Showdown" },
            { "Ordinal.1", "{0}st" },
            { "Ordinal.2", "{0}nd" },
            { "Ordinal.3", "{0}rd" },
            { "Ordinal.other", "{0}th" },
            { "RankedHand", "{0}  {1}" },
            // Poker hand names as the server sends them; change the right side to rename them.
            { "HandRank.High Card", "High Card" },
            { "HandRank.Pair", "Pair" },
            { "HandRank.Two Pair", "Two Pair" },
            { "HandRank.Three of a Kind", "Three of a Kind" },
            { "HandRank.Straight", "Straight" },
            { "HandRank.Flush", "Flush" },
            { "HandRank.Full House", "Full House" },
            { "HandRank.Four of a Kind", "Four of a Kind" },
            { "HandRank.Straight Flush", "Straight Flush" },
            { "HandRank.Royal Flush", "Royal Flush" },

            // Removed from the table
            { "DefeatedTitle", "Defeated" },
            { "RemovedTitle", "Retreat" },
            { "BrokeDetail", "Your army was wiped out in battle.\nRecruit fresh soldiers in the barracks and fight again!" },
            { "BackToLobby", "Back to barracks" },
            { "Removed.broke", "Your army was wiped out. Back to the barracks!" },
            { "Removed.timeout", "You were away too long; your troops returned to the barracks." },
            { "Removed.idle", "You were idle too long; your troops returned to the barracks." },
            { "Removed.left", "You left the table." },
            { "Removed.other", "You were removed from the table." },

            // Server error codes
            { "Error.INVALID_USERNAME", "Name must be 3-16 letters, digits or _." },
            { "Error.INVALID_PASSWORD", "Password must be 6-64 characters." },
            { "Error.USERNAME_TAKEN", "That name is already taken." },
            { "Error.BAD_CREDENTIALS", "Wrong name or password." },
            { "Error.CANNOT_SHOW", "Too late to show those cards." },
            { "Error.UNAUTHORIZED", "Your session expired. Please log in again." },
            { "Error.REFILL_NOT_AVAILABLE", "Refill is not available yet." },
            { "Error.UNKNOWN_LEVEL", "That battlefield does not exist." },
            { "Error.INSUFFICIENT_BALANCE", "Your army is too small for this battlefield." },
            { "Error.ALREADY_SEATED", "You are already seated at a table." },
            { "Error.LEAVING_TABLE", "Your last battle is still being decided. Try again in a moment." },
            { "Error.NOT_SEATED", "You are not seated." },
            { "Error.NOT_YOUR_TURN", "It is not your turn." },
            { "Error.ILLEGAL_ACTION", "That move is not allowed now." },
            { "Error.BAD_AMOUNT", "That amount is not allowed." },
            { "Error.NOT_CONNECTED", "Not connected to the server." },
            { "Error.TIMEOUT", "The server did not answer in time." },
            { "Error.NETWORK", "Cannot reach the server. Check the address and your connection." },
            { "Error.unknown", "Something went wrong." },
            { "Error.other", "Error: {0}" },
        };

        static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();

        /// <summary>Every key with its English default (used to write a complete text.json).</summary>
        public static IEnumerable<KeyValuePair<string, string>> AllDefaults { get { return Defaults; } }

        /// <summary>Replaces wording from a key/value table (text.json).</summary>
        public static void Load(IDictionary<string, string> table)
        {
            Overrides.Clear();
            if (table == null) return;
            foreach (var kv in table)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) Overrides[kv.Key] = kv.Value;
        }

        public static bool Has(string key)
        {
            return Overrides.ContainsKey(key) || Defaults.ContainsKey(key);
        }

        /// <summary>The wording for a key (text.json, else the default, else the key itself).</summary>
        public static string Get(string key)
        {
            string v;
            if (Overrides.TryGetValue(key, out v)) return v;
            if (Defaults.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>Wording with {0}, {1}, ... filled in. A broken template is shown as-is instead of throwing.</summary>
        public static string Format(string key, params object[] args)
        {
            string template = Get(key);
            try { return string.Format(CultureInfo.InvariantCulture, template, args); }
            catch (FormatException) { return template; }
        }

        // ---------- shortcuts for fixed phrases ----------

        public static string GameTitle { get { return Get("GameTitle"); } }
        public static string Subtitle { get { return Get("Subtitle"); } }
        public static string Reconnecting { get { return Get("Reconnecting"); } }
        public static string SoldiersLabel { get { return Get("SoldiersLabel"); } }
        public static string Username { get { return Get("Username"); } }
        public static string Password { get { return Get("Password"); } }
        public static string Login { get { return Get("Login"); } }
        public static string Register { get { return Get("Register"); } }
        public static string ServerAddress { get { return Get("ServerAddress"); } }
        public static string Connecting { get { return Get("Connecting"); } }
        public static string Retry { get { return Get("Retry"); } }
        public static string ServerUnreachable { get { return Get("ServerUnreachable"); } }
        public static string SessionReplaced { get { return Get("SessionReplaced"); } }
        public static string SessionExpired { get { return Get("SessionExpired"); } }
        public static string Logout { get { return Get("Logout"); } }
        public static string Refill { get { return Get("Refill"); } }
        public static string ChooseBattlefield { get { return Get("ChooseBattlefield"); } }
        public static string March { get { return Get("March"); } }
        public static string Joining { get { return Get("Joining"); } }
        public static string Leave { get { return Get("Leave"); } }
        public static string Ready { get { return Get("Ready"); } }
        public static string ReadyBadge { get { return Get("ReadyBadge"); } }
        public static string NextHand { get { return Get("NextHand"); } }
        public static string OpenCamp { get { return Get("OpenCamp"); } }
        public static string Offline { get { return Get("Offline"); } }
        public static string Fold { get { return Get("Fold"); } }
        public static string Check { get { return Get("Check"); } }
        public static string Raise { get { return Get("Raise"); } }
        public static string AllIn { get { return Get("AllIn"); } }
        public static string Min { get { return Get("Min"); } }
        public static string HalfPot { get { return Get("HalfPot"); } }
        public static string Pot { get { return Get("Pot"); } }
        public static string ShowCards { get { return Get("ShowCards"); } }
        public static string Showdown { get { return Get("Showdown"); } }
        public static string DefeatedTitle { get { return Get("DefeatedTitle"); } }
        public static string RemovedTitle { get { return Get("RemovedTitle"); } }
        public static string BrokeDetail { get { return Get("BrokeDetail"); } }
        public static string BackToLobby { get { return Get("BackToLobby"); } }

        // ---------- lookups by code ----------

        public static string ErrorText(string code)
        {
            if (string.IsNullOrEmpty(code)) return Get("Error.unknown");
            return Has("Error." + code) ? Get("Error." + code) : Format("Error.other", code);
        }

        public static string RemovedReason(string reason)
        {
            return reason != null && Has("Removed." + reason) ? Get("Removed." + reason) : Get("Removed.other");
        }

        /// <summary>A camp's last action (fold, check, call, raise, allin, sb, bb).</summary>
        public static string ActionWord(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            return Has("Action." + action) ? Get("Action." + action) : action.ToUpperInvariant();
        }

        /// <summary>A poker hand name from the server ("Flush"), renamed through HandRank.* when set.</summary>
        public static string HandName(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return "";
            return Has("HandRank." + serverName) ? Get("HandRank." + serverName) : serverName;
        }

        /// <summary>1 -> "1st", 2 -> "2nd", 11 -> "11th" (via the Ordinal.* templates).</summary>
        public static string Ordinal(int n)
        {
            int tens = n % 100, ones = n % 10;
            string key = tens >= 11 && tens <= 13 ? "Ordinal.other"
                : ones == 1 ? "Ordinal.1" : ones == 2 ? "Ordinal.2" : ones == 3 ? "Ordinal.3" : "Ordinal.other";
            return Format(key, n);
        }
    }
}
