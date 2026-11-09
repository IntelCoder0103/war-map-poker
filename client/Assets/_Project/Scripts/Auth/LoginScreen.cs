using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.Main;
using WarPoker.Net;
using WarPoker.UI;

namespace WarPoker.Auth
{
    /// <summary>Login / register form with an editable server address (saved in PlayerPrefs).</summary>
    public class LoginScreen : ScreenBase
    {
        InputField _user, _pass, _server;
        Button _login, _register, _retry;
        Text _message;
        bool _busy;

        protected override void Build()
        {
            var bg = UIKit.Deco(Root, "Background", BackgroundSprite(), Color.white);
            UIKit.Stretch(bg.rectTransform);
            bg.preserveAspect = false;
            var shade = UIKit.Deco(Root, "Shade", null, new Color(0.05f, 0.03f, 0.02f, 0.10f));
            UIKit.Stretch(shade.rectTransform);

            var panel = UIKit.Panel(Root, "Panel", "panel_window", Color.white, 0.8f);
            UIKit.Center(panel.rectTransform, new Vector2(0, -20), new Vector2(780, 820));
            UIKit.AddShadow(panel, 8f, 0.45f);
            var p = panel.transform;

            // Title on a ribbon across the top of the window.
            var ribbon = UIKit.Panel(p, "Ribbon", "ribbon", Color.white, 1f, true);
            UIKit.Center(ribbon.rectTransform, new Vector2(0, 400), new Vector2(860, 150));
            var title = UIKit.Text(ribbon.transform, "Title", Strings.GameTitle, 54, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(title.rectTransform);
            title.rectTransform.offsetMin = new Vector2(150, 10);
            title.rectTransform.offsetMax = new Vector2(-150, -6);
            UIKit.Title(title, 2f);
            title.resizeTextForBestFit = true; title.resizeTextMaxSize = 54; title.resizeTextMinSize = 28;
            var sub = UIKit.Text(p, "Subtitle", Strings.Subtitle, 30, Theme.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Center(sub.rectTransform, new Vector2(0, 278), new Vector2(700, 44));

            _user = UIKit.Input(p, "Username", Strings.Username, new Vector2(560, 80), false);
            UIKit.Center((RectTransform)_user.transform, new Vector2(0, 170), new Vector2(560, 80));
            _user.characterLimit = 16;
            _user.text = App.Session.LastUsername;

            _pass = UIKit.Input(p, "Password", Strings.Password, new Vector2(560, 80), true);
            UIKit.Center((RectTransform)_pass.transform, new Vector2(0, 70), new Vector2(560, 80));
            _pass.characterLimit = 64;

            _login = UIKit.Button(p, "Login", Strings.Login, new Vector2(270, 90), Theme.Crimson, Theme.ParchmentLight, 36, OnLogin);
            UIKit.Center((RectTransform)_login.transform, new Vector2(-145, -45), new Vector2(270, 90));
            _register = UIKit.Button(p, "Register", Strings.Register, new Vector2(270, 90), Theme.Forest, Theme.ParchmentLight, 36, OnRegister);
            UIKit.Center((RectTransform)_register.transform, new Vector2(145, -45), new Vector2(270, 90));

            _message = UIKit.Text(p, "Message", "", 26, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_message.rectTransform, new Vector2(0, -140), new Vector2(680, 80));

            _retry = UIKit.Button(p, "Retry", Strings.Retry, new Vector2(220, 64), Theme.Navy, Theme.ParchmentLight, 28, () => App.AutoLogin());
            UIKit.Center((RectTransform)_retry.transform, new Vector2(0, -215), new Vector2(220, 64));
            _retry.gameObject.SetActive(false);

            var serverLabel = UIKit.Text(p, "ServerLabel", Strings.ServerAddress, 24, Theme.InkSoft, TextAnchor.MiddleRight);
            UIKit.Center(serverLabel.rectTransform, new Vector2(-250, -320), new Vector2(150, 50));
            _server = UIKit.Input(p, "Server", ServerConfig.DefaultUrl, new Vector2(470, 58), false);
            UIKit.Center((RectTransform)_server.transform, new Vector2(85, -320), new Vector2(470, 58));
            _server.contentType = InputField.ContentType.Standard;
            _server.text = ServerConfig.Url;
            _server.onEndEdit.AddListener(v => ServerConfig.Url = v);
        }

        static Sprite BackgroundSprite()
        {
            return WarPoker.Game.MapArt.Background();
        }

        public override void OnShow()
        {
            _pass.text = "";
            _server.text = ServerConfig.Url;
            _message.text = "";
            _retry.gameObject.SetActive(false);
        }

        /// <summary>Shows a message (error or info). canRetry shows a Retry button for auto-login.</summary>
        public void SetMessage(string message, bool canRetry)
        {
            _message.text = message ?? "";
            _retry.gameObject.SetActive(canRetry && !string.IsNullOrEmpty(message));
        }

        public void SetBusy(bool busy, string message)
        {
            _busy = busy;
            _login.interactable = !busy;
            _register.interactable = !busy;
            if (busy)
            {
                _message.text = message ?? "";
                _message.color = Theme.InkSoft;
                _retry.gameObject.SetActive(false);
            }
            else
            {
                _message.color = Theme.Crimson;
            }
        }

        void OnLogin() { Submit(false); }
        void OnRegister() { Submit(true); }

        void Submit(bool register)
        {
            if (_busy) return;
            string user = _user.text.Trim();
            string pass = _pass.text;
            if (user.Length == 0 || pass.Length == 0)
            {
                SetMessage(Strings.Get("EnterNameAndPassword"), false);
                return;
            }
            ServerConfig.Url = _server.text;
            _server.text = ServerConfig.Url;

            SetBusy(true, Strings.Connecting);
            System.Action<ApiResult<AuthResponse>> done = r =>
            {
                SetBusy(false, null);
                if (r.Ok && r.Data != null && !string.IsNullOrEmpty(r.Data.token))
                {
                    SetMessage(null, false);
                    App.OnSignedIn(r.Data.token, r.Data.user);
                }
                else
                {
                    SetMessage(Strings.ErrorText(r.Error), false);
                }
            };
            if (register) App.Api.Register(user, pass, done);
            else App.Api.Login(user, pass, done);
        }
    }
}
