using System;
using UnityEngine;
using WarPoker.Net;

namespace WarPoker.Main
{
    /// <summary>The logged-in user: JWT (kept in PlayerPrefs for auto-login) and the latest user object.</summary>
    public class Session
    {
        const string TokenKey = "warpoker.token";
        const string LastUserKey = "warpoker.lastUser";

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
