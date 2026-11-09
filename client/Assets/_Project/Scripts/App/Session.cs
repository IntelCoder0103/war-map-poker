using System;
using UnityEngine;
using WarPoker.Net;

namespace WarPoker.Main
{
    /// <summary>The logged-in user: JWT (kept in PlayerPrefs for auto-login) and the latest user object.</summary>
    public class Session
    {
        // Launching with "-profile <name>" keeps a separate saved login, so two windows on one
        // machine can be two different players (e.g. ./WarPoker.x86_64 -profile p2).
        static readonly string TokenKey = "warpoker.token" + ProfileSuffix();
        static readonly string LastUserKey = "warpoker.lastUser" + ProfileSuffix();

        static string ProfileSuffix()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-profile" && !string.IsNullOrEmpty(args[i + 1])) return "." + args[i + 1];
            return "";
        }

        public string Token { get; private set; }
        public UserDto User { get; private set; }

        /// <summary>Raised on the main thread whenever user/balance data changes.</summary>
        public event Action Changed;

        public bool HasToken { get { return !string.IsNullOrEmpty(Token); } }

        public string LastUsername
        {
            get { return PlayerPrefs.GetString(LastUserKey, ""); }
        }

        public void LoadSaved()
        {
            Token = PlayerPrefs.GetString(TokenKey, "");
        }

        public void SignIn(string token, UserDto user)
        {
            Token = token;
            PlayerPrefs.SetString(TokenKey, token ?? "");
            if (user != null) PlayerPrefs.SetString(LastUserKey, user.username ?? "");
            PlayerPrefs.Save();
            SetUser(user);
        }

        public void SetUser(UserDto user)
        {
            User = user;
            Changed?.Invoke();
        }

        public void SetBalance(long balance, long? nextRefillAt)
        {
            if (User == null) return;
            User.balance = balance;
            User.nextRefillAt = nextRefillAt;
            Changed?.Invoke();
        }

        public void SignOut()
        {
            Token = "";
            User = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
