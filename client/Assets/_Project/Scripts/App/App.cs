using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Auth;
using WarPoker.Core;
using WarPoker.Game;
using WarPoker.Lobby;
using WarPoker.Net;
using WarPoker.UI;

namespace WarPoker.Main
{
    /// <summary>
    /// App singleton: owns the session, REST + socket clients and the three screens
    /// (Login → Lobby → Game), which are panels in one scene rather than separate scenes.
    /// Routes server socket events to the right screen.
    /// </summary>
    public class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        public Session Session { get; private set; }
        public ApiClient Api { get; private set; }
        public SocketClient Socket { get; private set; }

        public LoginScreen LoginScreen { get; private set; }
        public LobbyScreen LobbyScreen { get; private set; }
        public GameScreen GameScreen { get; private set; }
        public ScreenBase Current { get; private set; }

        RectTransform _screensRoot;
        Text _toast;
        CanvasGroup _toastGroup;
        Coroutine _toastRoutine;
        GameObject _reconnectBanner;
        bool _socketWanted;   // true while signed in: disconnects show the banner
        float _offlineSince = -1f;

        /// <summary>Creates the App if it doesn't exist yet (called by Bootstrap and the runtime fallback).</summary>
        public static App Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[App]");
            DontDestroyOnLoad(go);
            return go.AddComponent<App>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            GameSettings.Load(); // config files: wording, art catalog, display tuning (before any UI is built)

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Input.multiTouchEnabled = false;
            MainThread.Ensure();

            Session = new Session();
            Session.LoadSaved();
            Api = new ApiClient(this, () => ServerConfig.Url, () => Session.Token);
            Socket = new SocketClient();
            Socket.Connected += OnSocketConnected;
            Socket.Disconnected += OnSocketDisconnected;
            Socket.ConnectError += OnSocketError;
            Socket.EventReceived += OnSocketEvent;

            BuildUI();
        }

        void Start()
        {
            if (Instance != this) return;
            if (Session.HasToken) AutoLogin();
            else ShowLogin(null);
        }

        void Update()
        {
            if (Instance != this || Socket == null) return;
            Socket.Tick();

            // "Reconnecting..." banner after a short grace period without a connection.
            bool offline = _socketWanted && !Socket.IsConnected;
            if (!offline) _offlineSince = -1f;
            else if (_offlineSince < 0f) _offlineSince = Time.unscaledTime;
            bool show = offline && Time.unscaledTime - _offlineSince > 1.5f;
            if (_reconnectBanner.activeSelf != show) _reconnectBanner.SetActive(show);
        }

        void OnDestroy()
        {
            if (Instance == this && Socket != null) Socket.Close();
        }

        void OnApplicationQuit()
        {
            if (Socket != null) Socket.Close();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused || !_socketWanted || Socket == null) return;
            // Back from background: mobile networks often drop idle sockets silently.
            if (Socket.SecondsSinceAlive > 30f) Socket.ForceReconnect();
            else if (Current == GameScreen && Socket.IsConnected) RequestTableSync();
        }

        // ---------- UI ----------

        void BuildUI()
        {
            UIKit.EnsureEventSystem();
            if (Camera.main == null)
            {
                // Overlay canvases don't need a camera, but this avoids "no camera" warnings.
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.14f, 0.08f);
                cam.orthographic = true;
                cam.cullingMask = 0;
                DontDestroyOnLoad(camGo);
            }

            var canvas = UIKit.CreateCanvas("UI", 0);
            DontDestroyOnLoad(canvas.gameObject);
            var canvasRt = (RectTransform)canvas.transform;

            // Full-bleed backdrop behind everything (fills letterbox / notch areas).
            var backdrop = UIKit.Deco(canvasRt, "Backdrop", null, new Color(0.22f, 0.15f, 0.09f));
            UIKit.Stretch(backdrop.rectTransform);

            _screensRoot = UIKit.Stretch(UIKit.Rect("Screens", canvasRt));
            _screensRoot.gameObject.AddComponent<SafeArea>();

            LoginScreen = CreateScreen<LoginScreen>("Login");
            LobbyScreen = CreateScreen<LobbyScreen>("Lobby");
            GameScreen = CreateScreen<GameScreen>("Game");

            // Reconnecting banner (top).
            var banner = UIKit.Panel(canvasRt, "Reconnecting", "bar_fill", Theme.CrimsonDark, 0.8f);
            UIKit.Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(620, 54));
            banner.raycastTarget = false;
            var bt = UIKit.Text(banner.transform, "Text", Strings.Reconnecting, 26, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(bt.rectTransform);
            _reconnectBanner = banner.gameObject;
            _reconnectBanner.SetActive(false);

            // Toast (bottom-ish center).
            var toastBg = UIKit.Panel(canvasRt, "Toast", "plank", Color.white, 0.8f, true);
            UIKit.Place(toastBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(1000, 110));
            _toastGroup = UIKit.Group(toastBg.gameObject);
            _toastGroup.blocksRaycasts = false;
            _toast = UIKit.Text(toastBg.transform, "Text", "", 30, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_toast.rectTransform, 12f);
            toastBg.gameObject.SetActive(false);
        }

        T CreateScreen<T>(string name) where T : ScreenBase
        {
            var rt = UIKit.Stretch(UIKit.Rect(name, _screensRoot));
            var screen = rt.gameObject.AddComponent<T>();
            screen.Init(this);
            rt.gameObject.SetActive(false);
            return screen;
        }

        public void Show(ScreenBase screen)
        {
            if (Current == screen) return;
            if (Current != null)
            {
                Current.OnHide();
                Current.gameObject.SetActive(false);
            }
            Current = screen;
            if (Current != null)
            {
                Current.gameObject.SetActive(true);
                Current.OnShow();
            }
        }

        public void Toast(string message, float seconds = 2.8f)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine(message, seconds));
        }

        IEnumerator ToastRoutine(string message, float seconds)
        {
            _toast.text = message;
            var go = _toastGroup.gameObject;
            go.SetActive(true);
            go.transform.SetAsLastSibling();
            _toastGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                _toastGroup.alpha = 1f - t / 0.3f;
                yield return null;
            }
            go.SetActive(false);
            _toastRoutine = null;
        }

        // ---------- auth flow ----------

        public void ShowLogin(string message)
        {
            Show(LoginScreen);
            LoginScreen.SetMessage(message, Session.HasToken);
        }

        /// <summary>Uses the saved JWT: GET /me, then straight to the lobby.</summary>
        public void AutoLogin()
        {
            Show(LoginScreen);
            LoginScreen.SetBusy(true, Strings.Connecting);
            Api.Me(r =>
            {
                LoginScreen.SetBusy(false, null);
                if (r.Ok && r.Data != null && r.Data.user != null)
                {
                    OnSignedIn(Session.Token, r.Data.user);
                }
                else if (r.Status == 401 || r.Error == "UNAUTHORIZED")
                {
                    Session.SignOut();
                    ShowLogin(Strings.SessionExpired);
                }
                else
                {
                    ShowLogin(Strings.ErrorText(r.Error));
                }
            });
        }

        public void OnSignedIn(string token, UserDto user)
        {
            Session.SignIn(token, user);
            _socketWanted = true;
            Socket.Connect(ServerConfig.Url, token);
            Show(LobbyScreen);
        }

        public void Logout(string message)
        {
            _socketWanted = false;
            _reconnectBanner.SetActive(false);
            Socket.Close();
            Session.SignOut();
            ShowLogin(message);
        }

        /// <summary>Refreshes the user (balance) via REST.</summary>
        public void RefreshUser()
        {
            Api.Me(r =>
            {
                if (r.Ok && r.Data != null && r.Data.user != null) Session.SetUser(r.Data.user);
                else if (r.Status == 401) Logout(Strings.SessionExpired);
            });
        }

        public void RequestTableSync()
        {
            Socket.Request("table:sync", new JObject(), null);
        }

        // ---------- socket ----------

        void OnSocketConnected()
        {
            if (Current == GameScreen)
            {
                // The server sends table:state on connect anyway; ask again to be safe.
                RequestTableSync();
            }
            else if (Current == LobbyScreen)
            {
                LobbyScreen.Subscribe();
            }
        }

        void OnSocketDisconnected(string reason)
        {
            // The banner is driven from Update; the library reconnects by itself.
            if (_socketWanted && Current == GameScreen) Debug.Log("[app] disconnected at the table: " + reason);
        }

        void OnSocketError(string message)
        {
            if (message == "UNAUTHORIZED") Logout(Strings.SessionExpired);
        }

        void OnSocketEvent(string name, JToken payload)
        {
            try
            {
                switch (name)
                {
                    case "lobby:rooms":
                        LobbyScreen.OnRooms(payload);
                        break;

                    case "table:state":
                    {
                        var snapshot = payload != null ? payload.ToObject<Snapshot>() : null;
                        if (snapshot == null) break;
                        if (Current != GameScreen)
                        {
                            // Joined, or reconnected while still seated: go to the table.
                            if (snapshot.yourSeat < 0) break;
                            Show(GameScreen);
                        }
                        GameScreen.OnTableState(snapshot);
                        break;
                    }

                    case "table:event":
                    {
                        if (Current != GameScreen || payload == null) break;
                        var msg = payload.ToObject<TableEventMsg>();
                        if (msg != null) GameScreen.OnTableEvent(msg);
                        break;
                    }

                    case "user:balance":
                    {
                        var b = payload != null ? payload.ToObject<BalanceMsg>() : null;
                        if (b != null) Session.SetBalance(b.balance, b.nextRefillAt);
                        break;
                    }

                    case "table:removed":
                    {
                        var m = payload != null ? payload.ToObject<RemovedMsg>() : null;
                        string reason = m != null ? m.reason : null;
                        // Let the hand finish on screen (ranking, payouts), then explain what happened.
                        if (Current == GameScreen) GameScreen.OnRemoved(reason);
                        break;
                    }

                    case "room:invite":
                    {
                        var inv = payload != null ? payload.ToObject<InviteMsg>() : null;
                        if (inv == null) break;
                        if (Current == LobbyScreen) LobbyScreen.OnInvite(inv);
                        else if (Current == GameScreen) Toast(Strings.Format("InviteArrivedAtTable", inv.from, inv.code), 4f);
                        break;
                    }

                    case "session:replaced":
                        Logout(Strings.SessionReplaced);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[app] failed to handle " + name + ": " + e);
            }
        }
    }
}
