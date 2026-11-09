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
    /// Lobby: user, balance, refill, and the rooms (a 3-column grid of square cards) with live player counts
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

        // Private rooms
        Modal _createDlg, _codeDlg, _inviteDlg;
        InputField _buyInInput, _bigBlindInput, _codeInput;
        Text _smallBlindText, _privateHint;
        PrivateLimitsDto _limits = new PrivateLimitsDto();
        InviteMsg _invite;

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
            // The frame has room above the figure (spear): lift it so the soldier sits centred in the bar.
            AnchorCenter(icon.rectTransform, -110, 14, 56 * TroopArt.FrameScale, 56 * TroopArt.FrameScale);
            _balance = UIKit.Text(bar.transform, "Balance", "", 46, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorCenter(_balance.rectTransform, 90, 12, 300, 60);
            var balLabel = UIKit.Text(bar.transform, "BalanceLabel", Strings.SoldiersLabel, 22, Theme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Italic);
            AnchorCenter(balLabel.rectTransform, 90, -26, 300, 30);

            _refill = UIKit.Button(bar.transform, "Refill", Strings.Refill, new Vector2(250, 64), Theme.Forest, Theme.ParchmentLight, 26, OnRefill);
            // Refill and Logout share one row; the refill note sits under the refill button.
            AnchorRight((RectTransform)_refill.transform, -230, 10, 250, 64);
            _refillInfo = UIKit.Text(bar.transform, "RefillInfo", "", 20, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            AnchorRight(_refillInfo.rectTransform, -205, -38, 300, 28);

            var logout = UIKit.Button(bar.transform, "Logout", Strings.Logout, new Vector2(170, 64), Theme.Leather, Theme.ParchmentLight, 26,
                () => App.Logout(null));
            AnchorRight((RectTransform)logout.transform, -40, 10, 170, 64);

            // Room list
            var header = UIKit.Text(Root, "Header", Strings.ChooseBattlefield, 46, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(1000, 70));
            UIKit.Title(header, 3f);

            _list = UIKit.Rect("Rooms", Root);
            UIKit.Place(_list, new Vector2(0.5f, 0.5f), new Vector2(0, GridCenterY), new Vector2(GridW, GridH));

            _status = UIKit.Text(Root, "Status", "", 30, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(1400, 60));
            UIKit.AddOutline(_status, 2f);

            _privateTile = BuildPrivateTile();
            BuildPrivateDialogs();
        }

        // Rooms are square cards in a 3-column grid; the last square holds the private-room options.
        const int GridCols = 3;
        const float GridW = 1100f, GridH = 730f, GridGap = 30f, GridCenterY = -70f;
        RectTransform _privateTile;

        float CardSize(int cells)
        {
            int rows = Mathf.Max(1, (cells + GridCols - 1) / GridCols);
            float byHeight = (GridH - (rows - 1) * GridGap) / rows;
            float byWidth = (GridW - (GridCols - 1) * GridGap) / GridCols;
            return Mathf.Min(byHeight, byWidth);
        }

        void PlaceCell(RectTransform rt, int index, int cells)
        {
            float size = CardSize(cells);
            int rows = Mathf.Max(1, (cells + GridCols - 1) / GridCols);
            int col = index % GridCols, row = index / GridCols;
            float x = (col - (GridCols - 1) * 0.5f) * (size + GridGap);
            float y = ((rows - 1) * 0.5f - row) * (size + GridGap);
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(340, 340));
            float k = size / 340f; // cards are laid out at 340x340 and scaled to fit
            rt.localScale = new Vector3(k, k, 1f);
        }

        RectTransform BuildPrivateTile()
        {
            var card = UIKit.Panel(_list, "PrivateRooms", "panel_window", Color.white, 0.42f);
            var t = card.transform;
            var name = UIKit.Text(t, "Name", Strings.Get("CreatePrivate"), 40, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(name.rectTransform, new Vector2(0, 128), new Vector2(300, 56));
            var tag = UIKit.Text(t, "Tagline", Strings.Get("PrivateTagline"), 22, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.Center(tag.rectTransform, new Vector2(0, 92), new Vector2(300, 32));
            var crown = UIKit.Deco(t, "Crown", UISkin.Get("crown"), Color.white);
            crown.preserveAspect = true;
            UIKit.Center(crown.rectTransform, new Vector2(0, 32), new Vector2(110, 76));
            var create = UIKit.Button(t, "Create", Strings.Get("Create"), new Vector2(250, 66), Theme.Gold, Theme.ParchmentLight, 30, OpenCreate);
            UIKit.Center((RectTransform)create.transform, new Vector2(0, -52), new Vector2(250, 66));
            var byCode = UIKit.Button(t, "JoinWithCode", Strings.Get("JoinWithCode"), new Vector2(250, 66), Theme.Forest, Theme.ParchmentLight, 28, OpenJoinCode);
            UIKit.Center((RectTransform)byCode.transform, new Vector2(0, -126), new Vector2(250, 66));
            PlaceCell(card.rectTransform, 5, 6);
            return card.rectTransform;
        }

        void BuildPrivateDialogs()
        {
            // Create: buy-in and big blind; the small blind is half the big blind.
            _createDlg = Modal.Create(Root, "CreatePrivateDialog", new Vector2(860, 600));
            _createDlg.Title.text = Strings.Get("CreatePrivateTitle");
            _buyInInput = _createDlg.AddInput(Strings.Get("PrivateBuyIn"), "1000", 150, InputField.ContentType.IntegerNumber, 7);
            _bigBlindInput = _createDlg.AddInput(Strings.Get("PrivateBigBlind"), "20", 50, InputField.ContentType.IntegerNumber, 6);
            _buyInInput.onValueChanged.AddListener(_ => UpdateSmallBlind());
            _bigBlindInput.onValueChanged.AddListener(_ => UpdateSmallBlind());
            _smallBlindText = UIKit.Text(_createDlg.Panel, "SmallBlind", "", 28, Theme.InkSoft, TextAnchor.MiddleRight, FontStyle.Italic);
            UIKit.Center(_smallBlindText.rectTransform, new Vector2(60, -18), new Vector2(620, 40));
            _privateHint = UIKit.Text(_createDlg.Panel, "Hint", "", 24, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.Center(_privateHint.rectTransform, new Vector2(0, -78), new Vector2(740, 70));
            _createDlg.AddButton(Strings.Get("Cancel"), Theme.Leather, new Vector2(-180, -225), new Vector2(280, 90), () => _createDlg.Hide());
            _createDlg.AddButton(Strings.Get("Create"), Theme.Gold, new Vector2(180, -225), new Vector2(280, 90), CreatePrivate);

            // Join with a code.
            _codeDlg = Modal.Create(Root, "JoinCodeDialog", new Vector2(760, 440));
            _codeDlg.Title.text = Strings.Get("JoinCodeTitle");
            _codeInput = _codeDlg.AddInput(Strings.Get("RoomCode"), "ABC234", 60, InputField.ContentType.Alphanumeric, 12);
            _codeInput.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            _codeDlg.AddButton(Strings.Get("Cancel"), Theme.Leather, new Vector2(-170, -145), new Vector2(260, 90), () => _codeDlg.Hide());
            _codeDlg.AddButton(Strings.Get("Join"), Theme.Forest, new Vector2(170, -145), new Vector2(260, 90), () => JoinByCode(_codeInput.text, _codeDlg));

            // An invitation from another player.
            _inviteDlg = Modal.Create(Root, "InviteDialog", new Vector2(860, 460));
            _inviteDlg.Title.text = Strings.Get("InviteTitle");
            _inviteDlg.PlaceMessage(new Vector2(0, 40), new Vector2(760, 170));
            _inviteDlg.AddButton(Strings.Get("Decline"), Theme.Leather, new Vector2(-180, -150), new Vector2(280, 90), () => _inviteDlg.Hide());
            _inviteDlg.AddButton(Strings.Get("Join"), Theme.Forest, new Vector2(180, -150), new Vector2(280, 90), () =>
            {
                if (_invite != null) JoinByCode(_invite.code, _inviteDlg);
            });
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
                if (r.Ok && r.Data != null && r.Data.privateRooms != null) _limits = r.Data.privateRooms;
                if (r.Ok && r.Data != null && r.Data.rooms != null && _rooms.Count == 0) SetRooms(r.Data.rooms);
            });
            if (App.Socket.IsConnected) Subscribe();
        }

        public override void OnHide()
        {
            foreach (var d in new[] { _createDlg, _codeDlg, _inviteDlg }) d.Hide();
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
                    var limits = ack["privateRooms"];
                    if (limits != null && limits.Type == JTokenType.Object) _limits = limits.ToObject<PrivateLimitsDto>();
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
            int cells = rooms.Count + 1;
            for (int i = 0; i < rooms.Count; i++)
            {
                var row = i < _rows.Count ? _rows[i] : CreateRow(i);
                var room = rooms[i];
                row.Level = room.id;
                row.Name.text = room.name;
                row.BuyIn.text = Strings.Format("BuyIn", UIKit.Num(room.buyIn));
                row.Blinds.text = Strings.Format("Blinds", UIKit.Num(room.smallBlind), UIKit.Num(room.bigBlind));
                row.Players.text = Strings.Format("Players", room.players);
                PlaceCell((RectTransform)row.Button.transform, i, cells);
                row.Button.gameObject.SetActive(true);
            }
            for (int i = rooms.Count; i < _rows.Count; i++) _rows[i].Button.gameObject.SetActive(false);
            PlaceCell(_privateTile, rooms.Count, cells);
            Refresh();
        }

        RoomRow CreateRow(int index)
        {
            var row = new RoomRow();
            var card = UIKit.Panel(_list, "Room" + index, "panel_window", Color.white, 0.42f);
            card.rectTransform.sizeDelta = new Vector2(340, 340);
            row.Button = card.gameObject.AddComponent<Button>();
            row.Button.targetGraphic = card;
            var colors = row.Button.colors;
            colors.highlightedColor = new Color(1f, 0.97f, 0.88f);
            colors.pressedColor = new Color(0.85f, 0.8f, 0.7f);
            colors.fadeDuration = 0.08f;
            row.Button.colors = colors;
            var t = row.Button.transform;

            // Square card, top to bottom: name, seated count, squad badge, buy-in, blinds, March.
            row.Name = UIKit.Text(t, "Name", "", 42, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(row.Name.rectTransform, new Vector2(0, 128), new Vector2(300, 56));
            row.Players = UIKit.Text(t, "Players", "", 22, Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.Center(row.Players.rectTransform, new Vector2(0, 92), new Vector2(300, 32));

            // A bigger squad for a bigger room (1 soldier for Recruit ... 5 for King).
            var crest = TroopStackView.Create(t, "Crest", new Vector2(300, 104), 76f);
            UIKit.Center((RectTransform)crest.transform, new Vector2(0, 26), new Vector2(300, 104));
            crest.SetFigures(index + 1);

            row.BuyIn = UIKit.Text(t, "BuyIn", "", 30, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(row.BuyIn.rectTransform, new Vector2(0, -42), new Vector2(300, 40));
            row.Blinds = UIKit.Text(t, "Blinds", "", 24, Theme.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Center(row.Blinds.rectTransform, new Vector2(0, -76), new Vector2(300, 34));

            var marchBg = UIKit.Panel(t, "MarchBg", "btn_red", Color.white, 0.55f, true);
            UIKit.Center(marchBg.rectTransform, new Vector2(0, -126), new Vector2(220, 66));
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
                if (OnJoined(ack, room != null ? room.name : row.Level)) return;
                string code = ack.Value<string>("error");
                if (code == "INSUFFICIENT_BALANCE" && room != null && App.Session.User != null)
                    _status.text = Strings.Format("NeedSoldiers", UIKit.Num(room.buyIn), room.name, UIKit.Num(App.Session.User.balance));
                else
                    _status.text = Strings.ErrorText(code);
            });
        }

        /// <summary>Common handling of a successful join (or "already seated"). False if the caller should show the error.</summary>
        bool OnJoined(JObject ack, string tableName)
        {
            if (ack.Value<bool>("ok"))
            {
                _status.text = "";
                // table:state follows; App switches to the game screen when it arrives.
                // If it arrived first we're already there. Otherwise show the board now.
                if (App.Current != App.GameScreen)
                {
                    App.GameScreen.PrepareForJoin(tableName);
                    App.Show(App.GameScreen);
                    App.RequestTableSync();
                }
                return true;
            }
            if (ack.Value<string>("error") == "ALREADY_SEATED")
            {
                // We are seated somewhere already: ask for the snapshot, which brings us to the table.
                _status.text = Strings.Joining;
                App.RequestTableSync();
                return true;
            }
            return false;
        }

        // ---------- private rooms ----------

        void OpenCreate()
        {
            if (string.IsNullOrEmpty(_buyInInput.text)) _buyInInput.text = "1000";
            if (string.IsNullOrEmpty(_bigBlindInput.text)) _bigBlindInput.text = "20";
            _privateHint.text = Strings.Format("PrivateHint", UIKit.Num(_limits.minBuyIn), UIKit.Num(_limits.maxBuyIn), _limits.minStackBigBlinds);
            UpdateSmallBlind();
            _createDlg.Show();
        }

        void UpdateSmallBlind()
        {
            long bb;
            _smallBlindText.text = long.TryParse(_bigBlindInput.text, out bb) && bb > 0
                ? Strings.Format("PrivateSmallBlind", UIKit.Num(bb / 2)) : "";
        }

        void CreatePrivate()
        {
            if (_joining) return;
            long buyIn, bb;
            if (!long.TryParse(_buyInInput.text, out buyIn) || !long.TryParse(_bigBlindInput.text, out bb))
            {
                _createDlg.Error.text = Strings.ErrorText("BAD_STAKES");
                return;
            }
            // Same rules as the server, checked here first for a quicker answer.
            var user = App.Session.User;
            if (bb < 2 || buyIn < _limits.minBuyIn || buyIn > _limits.maxBuyIn || buyIn < bb * _limits.minStackBigBlinds)
            {
                _createDlg.Error.text = Strings.ErrorText("BAD_STAKES");
                return;
            }
            if (user != null && user.balance < buyIn)
            {
                _createDlg.Error.text = Strings.ErrorText("INSUFFICIENT_BALANCE");
                return;
            }
            _joining = true;
            _createDlg.Error.text = Strings.Joining;
            App.Socket.Request("room:createPrivate", new JObject { ["buyIn"] = buyIn, ["bigBlind"] = bb }, ack =>
            {
                _joining = false;
                string code = ack.Value<string>("code");
                if (OnJoined(ack, Strings.Format("PrivateName", code ?? "")))
                {
                    _createDlg.Hide();
                    return;
                }
                _createDlg.Error.text = Strings.ErrorText(ack.Value<string>("error"));
            });
        }

        void OpenJoinCode()
        {
            _codeInput.text = "";
            _codeDlg.Show();
        }

        void JoinByCode(string code, Modal dialog)
        {
            if (_joining) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                dialog.Error.text = Strings.ErrorText("ROOM_NOT_FOUND");
                return;
            }
            _joining = true;
            dialog.Error.text = Strings.Joining;
            App.Socket.Request("room:joinCode", new JObject { ["code"] = code }, ack =>
            {
                _joining = false;
                if (OnJoined(ack, Strings.Format("PrivateName", code)))
                {
                    dialog.Hide();
                    return;
                }
                dialog.Error.text = Strings.ErrorText(ack.Value<string>("error"));
            });
        }

        /// <summary>room:invite while in the lobby: offer to join.</summary>
        public void OnInvite(InviteMsg invite)
        {
            if (invite == null || string.IsNullOrEmpty(invite.code)) return;
            _invite = invite;
            _inviteDlg.Message.text = Strings.Format("InviteText", invite.from, UIKit.Num(invite.buyIn),
                UIKit.Num(invite.smallBlind), UIKit.Num(invite.bigBlind), invite.players, invite.maxSeats);
            _inviteDlg.Show();
        }

        void OnRefill()
        {
            _refill.interactable = false;
            App.Api.Refill(r =>
            {
                if (r.Ok && r.Data != null && r.Data.user != null)
                {
                    App.Session.SetUser(r.Data.user);
                    _status.text = Strings.Get("RefillArrived");
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
            else if (available) _refillInfo.text = Strings.Format("RefillAvailable", UIKit.Num(RefillRule.Threshold));
            else if (user.balance >= RefillRule.Threshold) _refillInfo.text = Strings.Format("RefillOnlyBelow", UIKit.Num(RefillRule.Threshold));
            else _refillInfo.text = Strings.Format("RefillIn", RefillRule.FormatCountdown(RefillRule.MsUntilAvailable(user.balance, user.nextRefillAt, now)));

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
