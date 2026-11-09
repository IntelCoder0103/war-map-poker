namespace WarPoker.Core
{
    /// <summary>All interface text in one place so it can be translated later.</summary>
    public static class Strings
    {
        public const string GameTitle = "War-Map Poker";

        // Login
        public const string Username = "Commander name";
        public const string Password = "Password";
        public const string Login = "Login";
        public const string Register = "Enlist";
        public const string ServerAddress = "Server";
        public const string Connecting = "Sending a messenger...";
        public const string Retry = "Retry";
        public const string ServerUnreachable = "Cannot reach the server. Check the address and your connection.";
        public const string SessionReplaced = "Your account was opened on another device.";
        public const string SessionExpired = "Your session expired. Please log in again.";

        // Lobby
        public const string Logout = "Logout";
        public const string Refill = "Claim refill";
        public const string RefillIn = "Refill in ";
        public const string RefillOnlyBelow = "Refill below 1,000";
        public const string ChooseBattlefield = "Choose your battlefield";
        public const string BuyIn = "Buy-in ";
        public const string Blinds = "Blinds ";
        public const string Players = " seated";
        public const string March = "March!";
        public const string Joining = "Marching to the table...";

        // Game
        public const string Leave = "Retreat";
        public const string Ready = "Ready for battle";
        public const string WaitingForOthers = "Waiting for the other commanders";
        public const string NextHand = "Joins next hand";
        public const string Fold = "Fold";
        public const string Check = "Check";
        public const string Call = "Call ";
        public const string Raise = "Raise";
        public const string RaiseTo = "Raise to ";
        public const string AllIn = "All-in";
        public const string HalfPot = "1/2 Pot";
        public const string Pot = "Pot";
        public const string Min = "Min";
        public const string PotLabel = "Pot ";
        public const string ShowCards = "Show cards";
        public const string Shows = "SHOWS";
        public const string Showdown = "Showdown";
        public const string Uncontested = "The enemy retreated";
        public const string Offline = "offline";
        public const string ReadyBadge = "READY";
        public const string OpenCamp = "Open camp";
        public const string Reconnecting = "Connection lost - reconnecting...";
        public const string Hand = "Hand #";

        public static string ErrorText(string code)
        {
            switch (code)
            {
                case "INVALID_USERNAME": return "Name must be 3-16 letters, digits or _.";
                case "INVALID_PASSWORD": return "Password must be 6-64 characters.";
                case "USERNAME_TAKEN": return "That name is already taken.";
                case "BAD_CREDENTIALS": return "Wrong name or password.";
                case "CANNOT_SHOW": return "Too late to show those cards.";
                case "UNAUTHORIZED": return SessionExpired;
                case "REFILL_NOT_AVAILABLE": return "Refill is not available yet.";
                case "UNKNOWN_LEVEL": return "That battlefield does not exist.";
                case "INSUFFICIENT_BALANCE": return "Your army is too small for this battlefield.";
                case "ALREADY_SEATED": return "You are already seated at a table.";
                case "NOT_SEATED": return "You are not seated.";
                case "NOT_YOUR_TURN": return "It is not your turn.";
                case "ILLEGAL_ACTION": return "That move is not allowed now.";
                case "BAD_AMOUNT": return "That amount is not allowed.";
                case "NOT_CONNECTED": return "Not connected to the server.";
                case "TIMEOUT": return "The server did not answer in time.";
                case "NETWORK": return ServerUnreachable;
                default: return string.IsNullOrEmpty(code) ? "Something went wrong." : "Error: " + code;
            }
        }

        public static string RemovedReason(string reason)
        {
            switch (reason)
            {
                case "broke": return "Your army was wiped out. Back to the barracks!";
                case "timeout": return "You were away too long; your troops returned to the barracks.";
                case "left": return "You left the table.";
                default: return "You were removed from the table.";
            }
        }
    }
}
