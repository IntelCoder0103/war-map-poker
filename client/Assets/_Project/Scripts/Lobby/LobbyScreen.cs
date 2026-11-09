using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.Main;
using WarPoker.Net;
using WarPoker.Troops;
using WarPoker.UI;

namespace WarPoker.Lobby
{
    /// <summary>
    /// Lobby: user, balance, refill, and the 5 rooms with live player counts
    /// (lobby:subscribe / lobby:rooms). No animations here by design.
    /// </summary>
    public class LobbyScreen : ScreenBase
    {
        Text _username, _balance, _refillInfo, _status;
        Button _refill;
        RectTransform _list;
        readonly List<RoomRow> _rows = new List<RoomRow>();
        List<RoomDto> _rooms = new List<RoomDto>();
        bool _joining;
        float _nextCountdownUpdate;

        class RoomRow
        {
            public Button Button;
            public Text Name, BuyIn, Blinds, Players, Action;
            public string Level;
        }

        protected override void Build()
        {
            var bg = UIKit.Deco(Root, "Background", WarPoker.Game.MapArt.Background(), Color.white);
            UIKit.Stretch(bg.rectTransform);
            var shade = UIKit.Deco(Root, "Shade", null, new Color(0.05f, 0.03f, 0.02f, 0.12f));
            UIKit.Stretch(shade.rectTransform);

            // Top bar
            var bar = UIKit.Panel(Root, "TopBar", "panel_wide", Color.white, 0.55f);
            var barRt = bar.rectTransform;
            barRt.anchorMin = new Vector2(0, 1);
            barRt.anchorMax = new Vector2(1, 1);
            barRt.pivot = new Vector2(0.5f, 1);
            barRt.offsetMin = new Vector2(24, -150);
            barRt.offsetMax = new Vector2(-24, -20);

            var title = UIKit.Text(bar.transform, "Title", Strings.GameTitle, 44, Theme.Crimson, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorLeft(title.rectTransform, 40, 0, 460, 70);
            UIKit.Title(title, 2f);
            _username = UIKit.Text(bar.transform, "Username", "", 30, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorLeft(_username.rectTransform, 40, -42, 460, 40);

            // Balance with a little troop icon.
            var icon = UIKit.Deco(bar.transform, "TroopIcon", TroopArt.Soldier(), Color.white);
            icon.preserveAspect = true;
            AnchorCenter(icon.rectTransform, -150, 0, 64 * TroopArt.FrameScale, 64 * TroopArt.FrameScale);
            _balance = UIKit.Text(bar.transform, "Balance", "", 46, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorCenter(_balance.rectTransform, 60, 6, 340, 64);
            var balLabel = UIKit.Text(bar.transform, "BalanceLabel", "soldiers", 22, Theme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Italic);
            AnchorCenter(balLabel.rectTransform, 60, -36, 340, 30);

            _refill = UIKit.Button(bar.transform, "Refill", Strings.Refill, new Vector2(250, 64), Theme.Forest, Theme.ParchmentLight, 26, OnRefill);
            AnchorRight((RectTransform)_refill.transform, -230, 14, 250, 64);
            _refillInfo = UIKit.Text(bar.transform, "RefillInfo", "", 20, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            AnchorRight(_refillInfo.rectTransform, -230, -40, 300, 30);

            var logout = UIKit.Button(bar.transform, "Logout", Strings.Logout, new Vector2(170, 64), Theme.Leather, Theme.ParchmentLight, 26,
                () => App.Logout(null));
            AnchorRight((RectTransform)logout.transform, -30, 0, 170, 64);

            // Room list
            var header = UIKit.Text(Root, "Header", Strings.ChooseBattlefield, 46, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(1000, 70));
            UIKit.Title(header, 3f);

            _list = UIKit.Rect("Rooms", Root);
            UIKit.Place(_list, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1300, 660));

            _status = UIKit.Text(Root, "Status", "", 30, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(1400, 60));
            UIKit.AddOutline(_status, 2f);
        }

        static void AnchorLeft(RectTransform rt, float x, float y, float w, float h)
        {
            UIKit.Place(rt, new Vector2(0, 0.5f), new Vector2(x + w * 0.5f, y + 18), new Vector2(w, h));
        }

        static void AnchorCenter(RectTransform rt, float x, float y, float w, float h)
        {
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h));
        }

        static void AnchorRight(RectTransform rt, float x, float y, float w, float h)
        {
            UIKit.Place(rt, new Vector2(1, 0.5f), new Vector2(x - w * 0.5f, y), new Vector2(w, h));
        }

        public override void OnShow()
        {
            _joining = false;
            _status.text = "";
            App.Session.Changed += Refresh;
            Refresh();
            App.RefreshUser();
            // REST first so the list shows even before the socket connects.
            App.Api.Rooms(r =>
            {
                if (r.Ok && r.Data != null && r.Data.rooms != null && _rooms.Count == 0) SetRooms(r.Data.rooms);
            });
            if (App.Socket.IsConnected) Subscribe();
        }

        public override void OnHide()
        {
            App.Session.Changed -= Refresh;
            if (App.Socket.IsConnected) App.Socket.Request("lobby:unsubscribe", new JObject(), null);
        }

        public void Subscribe()
        {
            App.Socket.Request("lobby:subscribe", new JObject(), ack =>
            {
                if (ack.Value<bool>("ok"))
                {
                    var rooms = ack["rooms"];
                    if (rooms != null) SetRooms(rooms.ToObject<List<RoomDto>>());
                }
            });
        }

        public void OnRooms(JToken payload)
        {
            if (payload == null) return;
            var msg = payload.ToObject<RoomsResponse>();
            if (msg != null && msg.rooms != null) SetRooms(msg.rooms);
        }

        void SetRooms(List<RoomDto> rooms)
        {
            if (rooms == null) return;
            _rooms = rooms;
            const float rowH = 112f, gap = 18f;
            for (int i = 0; i < rooms.Count; i++)
            {
                var row = i < _rows.Count ? _rows[i] : CreateRow(i);
                var room = rooms[i];
                row.Level = room.id;
                row.Name.text = room.name;
                row.BuyIn.text = Strings.BuyIn + UIKit.Num(room.buyIn);
                row.Blinds.text = Strings.Blinds + UIKit.Num(room.smallBlind) + " / " + UIKit.Num(room.bigBlind);
                row.Players.text = room.players + Strings.Players;
                var rt = (RectTransform)row.Button.transform;
                UIKit.Place(rt, new Vector2(0.5f, 1f), new Vector2(0, -rowH * 0.5f - i * (rowH + gap)), new Vector2(1300, rowH));
                row.Button.gameObject.SetActive(true);
            }
            for (int i = rooms.Count; i < _rows.Count; i++) _rows[i].Button.gameObject.SetActive(false);
            Refresh();
        }

        RoomRow CreateRow(int index)
        {
            var row = new RoomRow();
            var card = UIKit.Panel(_list, "Room" + index, "panel_window", Color.white, 0.42f);
            card.rectTransform.sizeDelta = new Vector2(1300, 112);
            row.Button = card.gameObject.AddComponent<Button>();
            row.Button.targetGraphic = card;
            var colors = row.Button.colors;
            colors.highlightedColor = new Color(1f, 0.97f, 0.88f);
            colors.pressedColor = new Color(0.85f, 0.8f, 0.7f);
            colors.fadeDuration = 0.08f;
            row.Button.colors = colors;
            var t = row.Button.transform;

            // Room badge: a bigger squad for a bigger room (1 soldier for Recruit ... 5 for King).
            // Squad badge on the left of the card; the text columns start after it.
            var crest = TroopStackView.Create(t, "Crest", new Vector2(250, 92), 62f);
            UIKit.Place((RectTransform)crest.transform, new Vector2(0, 0.5f), new Vector2(160, 0), new Vector2(250, 92));
            crest.SetFigures(index + 1);

            row.Name = UIKit.Text(t, "Name", "", 40, Theme.Crimson, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(row.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(400, 0), new Vector2(180, 70));
            row.BuyIn = UIKit.Text(t, "BuyIn", "", 30, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(row.BuyIn.rectTransform, new Vector2(0, 0.5f), new Vector2(610, 0), new Vector2(220, 60));
            row.Blinds = UIKit.Text(t, "Blinds", "", 28, Theme.InkSoft, TextAnchor.MiddleLeft);
            UIKit.Place(row.Blinds.rectTransform, new Vector2(0, 0.5f), new Vector2(850, 0), new Vector2(240, 60));
            row.Players = UIKit.Text(t, "Players", "", 26, Theme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIKit.Place(row.Players.rectTransform, new Vector2(1, 0.5f), new Vector2(-245, 0), new Vector2(120, 60));

            var marchBg = UIKit.Panel(t, "MarchBg", "btn_red", Color.white, 0.55f, true);
            UIKit.Place(marchBg.rectTransform, new Vector2(1, 0.5f), new Vector2(-105, 0), new Vector2(170, 78));
            row.Action = UIKit.Text(marchBg.transform, "Label", Strings.March, 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(row.Action.rectTransform);
            UIKit.AddOutline(row.Action, 2f);

            var captured = row;
            row.Button.onClick.AddListener(() => Join(captured));
            _rows.Add(row);
            return row;
        }

        void Join(RoomRow row)
        {
            if (_joining || string.IsNullOrEmpty(row.Level)) return;
            var room = _rooms.Find(r => r.id == row.Level);
            _joining = true;
            _status.text = Strings.Joining;
            App.Socket.Request("room:join", new JObject { ["level"] = row.Level }, ack =>
            {
                _joining = false;
                if (ack.Value<bool>("ok"))
                {
                    _status.text = "";
                    // table:state follows; App switches to the game screen when it arrives.
                    // If it arrived first we're already there. Otherwise show the board now.
                    if (App.Current != App.GameScreen)
                    {
                        App.GameScreen.PrepareForJoin(room != null ? room.name : row.Level);
                        App.Show(App.GameScreen);
                        App.RequestTableSync();
                    }
                    return;
                }
                string code = ack.Value<string>("error");
                if (code == "INSUFFICIENT_BALANCE" && room != null && App.Session.User != null)
                {
                    _status.text = "You need " + UIKit.Num(room.buyIn) + " soldiers to march on " + room.name +
                                   ". Your army: " + UIKit.Num(App.Session.User.balance) + ".";
                }
                else if (code == "ALREADY_SEATED")
                {
                    // We are seated somewhere already: ask for the snapshot, which brings us to the table.
                    _status.text = Strings.Joining;
                    App.RequestTableSync();
                }
                else
                {
                    _status.text = Strings.ErrorText(code);
                }
            });
        }

        void OnRefill()
        {
            _refill.interactable = false;
            App.Api.Refill(r =>
            {
                if (r.Ok && r.Data != null && r.Data.user != null)
                {
                    App.Session.SetUser(r.Data.user);
                    _status.text = "Fresh recruits have arrived!";
                }
                else
                {
                    _status.text = Strings.ErrorText(r.Error);
                }
                Refresh();
            });
        }

        void Refresh()
        {
            var user = App.Session.User;
            _username.text = user != null ? user.username : "";
            _balance.text = user != null ? UIKit.Num(user.balance) : "-";
            long now = Session.NowMs();
            bool available = user != null && RefillRule.IsAvailable(user.balance, user.nextRefillAt, now);
            _refill.interactable = available;
            if (user == null) _refillInfo.text = "";
            else if (available) _refillInfo.text = "Top up to 1,000";
            else if (user.balance >= RefillRule.Threshold) _refillInfo.text = Strings.RefillOnlyBelow;
            else _refillInfo.text = Strings.RefillIn + RefillRule.FormatCountdown(RefillRule.MsUntilAvailable(user.balance, user.nextRefillAt, now));

            // Dim rooms we can't afford (still tappable: the server explains why).
            foreach (var row in _rows)
            {
                var room = _rooms.Find(r => r.id == row.Level);
                bool affordable = user == null || room == null || user.balance >= room.buyIn;
                row.Button.image.color = affordable ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            }
        }

        void Update()
        {
            // Countdown for the refill timer.
            if (Time.unscaledTime < _nextCountdownUpdate) return;
            _nextCountdownUpdate = Time.unscaledTime + 0.5f;
            var user = App != null && App.Session != null ? App.Session.User : null;
            if (user != null && user.balance < RefillRule.Threshold) Refresh();
        }
    }
}
