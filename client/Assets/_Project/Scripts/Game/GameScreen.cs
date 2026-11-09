using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Cards;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.Main;
using WarPoker.Net;
using WarPoker.Troops;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// The war-map table. The server is the only authority:
    ///  - table:state  → cancel animations, clear the queue, render the snapshot.
    ///  - table:event  → queued; each one plays its animation from `data`, then renders `state`.
    /// Everything on screen is always re-rendered from a snapshot; animations are decoration.
    /// </summary>
    public class GameScreen : ScreenBase
    {
        const int MaxSeats = 8;
        static readonly Vector2 Center = new Vector2(0, 30);
        const float RadiusX = 720f, RadiusY = 330f;
        static readonly Vector2 BoardCardSize = new Vector2(96, 134);
        static readonly Vector2 DeckPos = new Vector2(-345, 40);
        static readonly Vector2 PotPos = new Vector2(0, -60);
        static readonly Vector2 ActionBarPos = new Vector2(560, -482);

        RectTransform _board;
        FxLayer _fx;
        readonly SeatView[] _seats = new SeatView[MaxSeats];
        readonly CardView[] _boardCards = new CardView[5];
        readonly Image[] _boardSlots = new Image[5];
        TroopStackView _potTroops;
        Text _potText, _info, _centerMsg, _readyInfo;
        RectTransform _potRt, _deckRt;
        ActionBar _actions;
        Button _readyButton, _showButton;
        GameObject _readyPanel;
        bool _showRequested;

        // Removed from the table (busted, idle, ...): the hand finishes on screen, then this dialog explains.
        string _removedReason;
        GameObject _dialog;
        Text _dialogTitle, _dialogText;
        float _nextReadyTick;

        // Showdown ranking board
        CanvasGroup _rankGroup;
        RectTransform _rankPanel;
        readonly List<RankRow> _rankRows = new List<RankRow>();

        class RankRow
        {
            public RectTransform Rect;
            public Image Bg;
            public Text Rank, Name, Hand;
            public CardView[] Cards;
            public CanvasGroup Group;
        }

        Snapshot _state;
        long _lastSeq = -1;
        readonly Queue<TableEventMsg> _queue = new Queue<TableEventMsg>();
        Coroutine _runner;
        float _stepStarted;
        Snapshot _playingState;   // state of the event whose animation is running
        readonly HashSet<string> _highlight = new HashSet<string>();
        readonly Dictionary<int, string> _handNames = new Dictionary<int, string>();
        long _clockOffset;
        bool _hasClock;
        bool _leaving;

        // ---------- build ----------

        protected override void Build()
        {
            var bg = UIKit.Deco(Root, "Map", MapArt.Background(), Color.white);
            UIKit.Stretch(bg.rectTransform);

            // Fixed 1920×1080 design board, scaled to fit any aspect ratio.
            _board = UIKit.Rect("Board", Root);
            UIKit.Center(_board, Vector2.zero, new Vector2(1920, 1080));
            _board.gameObject.AddComponent<FitInside>();

            // Battlefield: a faint ring drawn around the centre.
            var field = UIKit.Deco(_board, "Battlefield", ProcSprites.Glow(128), new Color(1f, 1f, 1f, 0.10f));
            UIKit.Center(field.rectTransform, Center + new Vector2(0, -10), new Vector2(900, 420));

            // Seats around an ellipse; display slot 0 (me) at bottom centre.
            for (int d = 0; d < MaxSeats; d++)
            {
                float a = SeatLayout.AngleDeg(d, MaxSeats) * Mathf.Deg2Rad;
                Vector2 pos = Center + new Vector2(Mathf.Cos(a) * RadiusX, Mathf.Sin(a) * RadiusY);
                Vector2 betPos = BetSpot(pos);
                var view = SeatView.Create(_board, d, pos, betPos);
                view.ServerNow = ServerNow;
                _seats[d] = view;
            }

            // Board cards with empty slots.
            for (int i = 0; i < 5; i++)
            {
                Vector2 pos = Center + new Vector2(10 + (i - 2) * 106f, 10);
                var slot = UIKit.Deco(_board, "Slot" + i, ProcSprites.RoundedRect(10, 3, 0.4f), new Color(1f, 1f, 1f, 0.22f));
                UIKit.Center(slot.rectTransform, pos, BoardCardSize);
                _boardSlots[i] = slot;
                var card = CardView.Create(_board, "Board" + i, BoardCardSize);
                UIKit.Center(card.Rect, pos, BoardCardSize);
                card.SetVisible(false);
                _boardCards[i] = card;
            }

            // Deck (a short stack of backs).
            _deckRt = UIKit.Rect("Deck", _board);
            UIKit.Center(_deckRt, DeckPos, SeatView.CardSize);
            for (int i = 0; i < 3; i++)
            {
                var c = CardView.Create(_deckRt, "DeckCard" + i, SeatView.CardSize);
                c.Rect.anchoredPosition = new Vector2(-i * 2f, i * 2f);
            }

            // Pot total. The soldiers themselves stay in front of each player until the hand ends.
            _potRt = UIKit.Rect("Pot", _board);
            UIKit.Center(_potRt, PotPos, new Vector2(260, 52));
            _potTroops = TroopStackView.Create(_potRt, "Army", new Vector2(260, 52), 48f);
            _potTroops.LabelColor = Theme.Ink;
            UIKit.Stretch((RectTransform)_potTroops.transform);
            var potBg = UIKit.Panel(_board, "PotLabelBg", "plank", Color.white, 0.6f, true);
            // the pot number sits right under the pot squad
            UIKit.Center(potBg.rectTransform, PotPos + new Vector2(0, -46), new Vector2(190, 40));
            _potText = UIKit.Text(potBg.transform, "Text", "", 24, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_potText.rectTransform);
            _potText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Top-left: retreat + table info.
            var leave = UIKit.Button(_board, "Leave", Strings.Leave, new Vector2(190, 64), Theme.Leather, Theme.ParchmentLight, 28, OnLeave);
            UIKit.Center((RectTransform)leave.transform, new Vector2(-835, 490), new Vector2(190, 64));
            _info = UIKit.Text(_board, "Info", "", 24, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.AddOutline(_info, 2f);
            UIKit.Center(_info.rectTransform, new Vector2(-700, 420), new Vector2(460, 60));

            _centerMsg = UIKit.Text(_board, "CenterMessage", "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.AddOutline(_centerMsg, 3f);
            UIKit.Center(_centerMsg.rectTransform, Center + new Vector2(0, 150), new Vector2(900, 60));

            // Ready panel (waiting phase), same area as the action bar.
            var rp = UIKit.Rect("ReadyPanel", _board);
            UIKit.Center(rp, ActionBarPos + new Vector2(0, 30), new Vector2(780, 160));
            _readyPanel = rp.gameObject;
            _readyInfo = UIKit.Text(rp, "Info", "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.AddOutline(_readyInfo, 2f);
            UIKit.Center(_readyInfo.rectTransform, new Vector2(0, 52), new Vector2(780, 40));
            _readyButton = UIKit.Button(rp, "Ready", Strings.Ready, new Vector2(440, 110), Theme.Forest, Theme.GoldLight, 46, OnReady);
            UIKit.Center((RectTransform)_readyButton.transform, new Vector2(0, -20), new Vector2(440, 96));
            _readyPanel.SetActive(false);

            // A winner by fold may show their cards (like other poker apps); hidden otherwise.
            _showButton = UIKit.Button(_board, "ShowCards", Strings.ShowCards, new Vector2(250, 86), Theme.Gold, Theme.ParchmentLight, 32, OnShowCards);
            UIKit.Center((RectTransform)_showButton.transform, new Vector2(300, -360), new Vector2(250, 86));
            _showButton.gameObject.SetActive(false);

            BuildRankBoard();
            BuildRemovedDialog();

            _actions = ActionBar.Create(_board, ActionBarPos);
            _actions.OnAction = SendAction;

            _fx = FxLayer.Create(_board, this);
        }

        /// <summary>Bet spot: just outside the camp, on the line toward the pot.</summary>
        const float RankRowH = 72f, RankRowGap = 8f, RankW = 820f;

        void BuildRankBoard()
        {
            var panel = UIKit.Panel(_board, "RankBoard", "panel_window", Color.white, 0.55f);
            _rankPanel = panel.rectTransform;
            UIKit.Center(_rankPanel, Center + new Vector2(0, 20), new Vector2(RankW, 300));
            _rankGroup = UIKit.Group(panel.gameObject);
            _rankGroup.blocksRaycasts = false;
            var ribbon = UIKit.Panel(_rankPanel, "Ribbon", "ribbon", Color.white, 0.7f, true);
            ribbon.rectTransform.anchorMin = ribbon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            ribbon.rectTransform.anchoredPosition = new Vector2(0, 6);
            ribbon.rectTransform.sizeDelta = new Vector2(520, 96);
            var title = UIKit.Text(ribbon.transform, "Title", Strings.Showdown, 40, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(title.rectTransform);
            title.rectTransform.offsetMin = new Vector2(110, 8);
            title.rectTransform.offsetMax = new Vector2(-110, -4);
            UIKit.Title(title, 2f);
            panel.gameObject.SetActive(false);
        }

        RankRow GetRankRow(int i)
        {
            while (_rankRows.Count <= i)
            {
                var row = new RankRow();
                var bg = UIKit.Panel(_rankPanel, "Row" + _rankRows.Count, "plank", Color.white, 0.6f, true);
                row.Bg = bg;
                row.Rect = bg.rectTransform;
                row.Rect.anchorMin = row.Rect.anchorMax = new Vector2(0.5f, 1f);
                row.Rect.sizeDelta = new Vector2(RankW - 70f, RankRowH);
                row.Group = UIKit.Group(bg.gameObject);
                row.Rank = UIKit.Text(bg.transform, "Rank", "", 36, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Place(row.Rank.rectTransform, new Vector2(0, 0.5f), new Vector2(50, 0), new Vector2(80, RankRowH));
                UIKit.AddOutline(row.Rank, 2f);
                row.Name = UIKit.Text(bg.transform, "Name", "", 30, Theme.ParchmentLight, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(row.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(190, 0), new Vector2(190, RankRowH));
                row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
                row.Hand = UIKit.Text(bg.transform, "Hand", "", 28, Theme.GoldLight, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(row.Hand.rectTransform, new Vector2(0, 0.5f), new Vector2(390, 0), new Vector2(210, RankRowH));
                row.Hand.horizontalOverflow = HorizontalWrapMode.Overflow;
                row.Cards = new CardView[5];
                var cardSize = new Vector2(42, 58);
                for (int c = 0; c < 5; c++)
                {
                    var card = CardView.Create(bg.transform, "Card" + c, cardSize);
                    card.Rect.anchorMin = card.Rect.anchorMax = new Vector2(1f, 0.5f);
                    card.Rect.anchoredPosition = new Vector2(-254 + c * 46, 0);
                    row.Cards[c] = card;
                }
                _rankRows.Add(row);
            }
            return _rankRows[i];
        }

        void BuildRemovedDialog()
        {
            var shade = UIKit.Image(Root, "RemovedDialog", null, new Color(0f, 0f, 0f, 0.55f)); // blocks clicks behind it
            UIKit.Stretch(shade.rectTransform);
            _dialog = shade.gameObject;
            var panel = UIKit.Panel(shade.transform, "Panel", "panel_window", Color.white, 0.7f);
            UIKit.Center(panel.rectTransform, Vector2.zero, new Vector2(820, 480));
            var ribbon = UIKit.Panel(panel.transform, "Ribbon", "ribbon", Color.white, 0.8f, true);
            UIKit.Center(ribbon.rectTransform, new Vector2(0, 236), new Vector2(620, 120));
            _dialogTitle = UIKit.Text(ribbon.transform, "Title", "", 50, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_dialogTitle.rectTransform);
            _dialogTitle.rectTransform.offsetMin = new Vector2(130, 10);
            _dialogTitle.rectTransform.offsetMax = new Vector2(-130, -6);
            UIKit.Title(_dialogTitle, 2f);
            _dialogText = UIKit.Text(panel.transform, "Text", "", 34, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_dialogText.rectTransform, new Vector2(0, 30), new Vector2(680, 200));
            var back = UIKit.Button(panel.transform, "Back", Strings.BackToLobby, new Vector2(380, 100), Theme.Crimson, Theme.ParchmentLight, 40, () =>
            {
                _removedReason = null;
                _dialog.SetActive(false);
                ExitToLobby(null);
            });
            UIKit.Center((RectTransform)back.transform, new Vector2(0, -150), new Vector2(380, 100));
            _dialog.SetActive(false);
        }

        /// <summary>The server took us off the table. Retreating by choice goes straight back; anything else
        /// (busted, idle, away) lets the hand play out on screen first, then shows a dialog.</summary>
        public void OnRemoved(string reason)
        {
            if (reason == "left")
            {
                if (!_leaving) ExitToLobby(Strings.RemovedReason(reason));
                return;
            }
            _removedReason = reason ?? "";
            _showButton.gameObject.SetActive(false);
            if (_runner == null) ShowRemovedDialog();
        }

        void ShowRemovedDialog()
        {
            if (_removedReason == null || _dialog.activeSelf) return;
            _actions.Hide();
            _readyPanel.SetActive(false);
            _showButton.gameObject.SetActive(false);
            bool broke = _removedReason == "broke";
            _dialogTitle.text = broke ? Strings.DefeatedTitle : Strings.RemovedTitle;
            _dialogText.text = broke ? Strings.BrokeDetail : Strings.RemovedReason(_removedReason);
            _dialog.SetActive(true);
            _dialog.transform.SetAsLastSibling();
            var panel = _dialog.transform.GetChild(0);
            panel.localScale = Vector3.one * 0.6f;
            StartCoroutine(Tween.Scale(panel, Vector3.one, 0.3f, Ease.OutBack));
        }

        void HideRankBoard()
        {
            if (_rankPanel != null) _rankPanel.gameObject.SetActive(false);
        }

        static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";
            switch (n % 10) { case 1: return n + "st"; case 2: return n + "nd"; case 3: return n + "rd"; default: return n + "th"; }
        }

        static Vector2 BetSpot(Vector2 camp)
        {
            Vector2 dir = (Center - camp);
            if (dir.sqrMagnitude < 1f) return camp;
            dir.Normalize();
            float hx = SeatView.CampSize.x * 0.5f, hy = SeatView.CampSize.y * 0.5f;
            float tx = Mathf.Abs(dir.x) > 0.001f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.001f ? hy / Mathf.Abs(dir.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            // Push out until the whole marker clears the camp (the local camp is drawn 12% larger):
            // the marker's own half-size along the direction, plus a small gap.
            float bx = SeatView.BetSize.x * 0.5f, by = SeatView.BetSize.y * 0.5f;
            float cx = hx * 1.12f + bx, cy = hy * 1.12f + by;
            float sx = Mathf.Abs(dir.x) > 0.001f ? cx / Mathf.Abs(dir.x) : float.MaxValue;
            float sy = Mathf.Abs(dir.y) > 0.001f ? cy / Mathf.Abs(dir.y) : float.MaxValue;
            return camp + dir * (Mathf.Min(sx, sy) + 8f);
        }

        // ---------- screen lifecycle ----------

        public override void OnShow()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _leaving = false;
            _removedReason = null;
            if (_dialog != null) _dialog.SetActive(false);
        }

        public override void OnHide()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            CancelAnimations();
            _queue.Clear();
            _state = null;
            _lastSeq = -1;
            _hasClock = false;
        }

        /// <summary>Called by the lobby right after room:join succeeds, before the snapshot arrives.</summary>
        public void PrepareForJoin(string tableName)
        {
            CancelAnimations();
            _queue.Clear();
            _state = null;
            _lastSeq = -1;
            _hasClock = false;
            foreach (var s in _seats) s.Clear();
            foreach (var c in _boardCards) c.SetVisible(false);
            _potTroops.SetAmount(0);
            _potText.text = Strings.PotLabel + "0";
            _actions.Hide();
            _readyPanel.SetActive(false);
            _info.text = tableName;
            _centerMsg.text = Strings.Joining;
        }

        public void ExitToLobby(string message)
        {
            _leaving = true;
            App.Show(App.LobbyScreen);
            if (!string.IsNullOrEmpty(message)) App.Toast(message, 3.5f);
        }

        // ---------- clock ----------

        long ServerNow()
        {
            return Session.NowMs() + _clockOffset;
        }

        void SampleClock(long serverTime)
        {
            if (serverTime <= 0) return;
            long offset = serverTime - Session.NowMs();
            // Network delay only ever makes the sample look "older", so keep the largest.
            if (!_hasClock || offset > _clockOffset) _clockOffset = offset;
            _hasClock = true;
        }

        // ---------- server input ----------

        public void OnTableState(Snapshot s)
        {
            if (s == null) return;
            CancelAnimations();
            _queue.Clear();
            SampleClock(s.serverTime);
            _lastSeq = s.seq;
            if (s.phase == "waiting") { _highlight.Clear(); _handNames.Clear(); }
            Apply(s);
        }

        public void OnTableEvent(TableEventMsg e)
        {
            if (e == null || e.state == null) return;
            SampleClock(e.state.serverTime);
            if (_lastSeq >= 0 && e.seq <= _lastSeq) return;          // duplicate / old
            bool gap = _lastSeq >= 0 && e.seq > _lastSeq + 1;
            _lastSeq = e.seq;
            _queue.Enqueue(e);
            if (gap)
            {
                Debug.LogWarning("[game] event gap before seq " + e.seq + "; requesting a snapshot");
                App.RequestTableSync();
            }
            if (_runner == null) _runner = StartCoroutine(RunQueue());
        }

        IEnumerator RunQueue()
        {
            while (_queue.Count > 0)
            {
                var e = _queue.Dequeue();
                int backlog = 0;
                foreach (var q in _queue) if (Animates(q.type)) backlog++;
                // Never fall behind: speed up when events pile up, skip animations when far behind.
                Tween.Speed = backlog >= 5 ? 3f : (backlog >= 2 ? 1.7f : 1f);
                _stepStarted = Time.unscaledTime;
                _playingState = e.state;
                if (backlog <= 8 && _state != null)
                {
                    var anim = Play(e);
                    if (anim != null) yield return anim;
                }
                _playingState = null;
                Apply(e.state);
            }
            Tween.Speed = 1f;
            _runner = null;
            if (_removedReason != null) ShowRemovedDialog();
        }

        static bool Animates(string type)
        {
            return type != "turn" && type != "connection" && type != "handEnded";
        }

        void Update()
        {
            // Safety net: if an animation ever got stuck (or threw), drop it and catch up.
            if (_runner != null && Time.unscaledTime - _stepStarted > 10f)
            {
                Debug.LogWarning("[game] animation stuck; catching up");
                Snapshot last = _playingState;
                while (_queue.Count > 0) last = _queue.Dequeue().state;
                CancelAnimations();
                _playingState = null;
                if (last != null) Apply(last);
                else if (_state != null) Apply(_state);
                if (_removedReason != null) ShowRemovedDialog();
            }

            // Ready countdown text.
            if (_state != null && _state.phase == "waiting" && _state.readyDeadline > 0 && Time.unscaledTime >= _nextReadyTick)
            {
                _nextReadyTick = Time.unscaledTime + 0.5f;
                RenderReady(_state);
            }
        }

        void CancelAnimations()
        {
            StopAllCoroutines();
            _runner = null;
            Tween.Speed = 1f;
            if (_fx != null) _fx.Clear();
            HideRankBoard();
            if (_board != null) _board.anchoredPosition = Vector2.zero;
        }

        // ---------- rendering (always from a snapshot) ----------

        SeatView ViewFor(int seat)
        {
            int yourSeat = _state != null ? _state.yourSeat : -1;
            if (seat < 0) return null;
            return _seats[SeatLayout.ToDisplay(seat, yourSeat, MaxSeats)];
        }

        void Apply(Snapshot s)
        {
            if (s == null) return;
            _state = s;
            if (s.bigBlind > 0) TroopStackView.Unit = s.bigBlind;
            int max = Mathf.Clamp(s.maxSeats, 1, MaxSeats);
            _centerMsg.text = "";

            for (int d = 0; d < MaxSeats; d++)
            {
                int seat = SeatLayout.ToSeat(d, s.yourSeat, max);
                if (d >= max) { _seats[d].Clear(); continue; }
                string handName;
                _handNames.TryGetValue(seat, out handName);
                _seats[d].Render(seat, s.Seat(seat), s, handName, _highlight);
            }

            // Board
            int boardCount = s.board != null ? s.board.Count : 0;
            for (int i = 0; i < 5; i++)
            {
                var card = _boardCards[i];
                card.ResetVisual();
                if (i < boardCount)
                {
                    card.SetVisible(true);
                    card.SetCard(s.board[i], true);
                    card.SetHighlight(_highlight.Contains(card.Code ?? ""));
                }
                else
                {
                    card.SetVisible(false);
                }
            }

            // Pot: only the total here; each player's soldiers stay at their own bet spot.
            _potRt.gameObject.SetActive(false);
            _potText.transform.parent.gameObject.SetActive(s.potTotal > 0);
            _potText.text = Strings.PotLabel + UIKit.Num(s.potTotal);
            if (s.phase != "showdown") HideRankBoard();

            if (!s.canShowCards) _showRequested = false;
            _showButton.gameObject.SetActive(s.canShowCards && !_showRequested);

            // Info
            string info = (s.name ?? s.level ?? "") + "   Blinds " + UIKit.Num(s.smallBlind) + "/" + UIKit.Num(s.bigBlind);
            if (s.handNumber > 0) info += "\n" + Strings.Hand + s.handNumber;
            _info.text = info;

            RenderReady(s);
            _actions.Render(s);
        }

        void RenderReady(Snapshot s)
        {
            var me = s.Me;
            bool waiting = s.phase == "waiting";
            _readyPanel.SetActive(waiting && me != null);
            if (!waiting || me == null) return;

            int seated = 0, ready = 0;
            if (s.seats != null)
                foreach (var seat in s.seats)
                    if (seat != null) { seated++; if (seat.ready) ready++; }

            _readyButton.gameObject.SetActive(!me.ready);
            _readyButton.interactable = true;
            if (seated < 2) _readyInfo.text = "Waiting for more commanders to join (" + ready + "/" + seated + " ready)";
            else _readyInfo.text = Strings.WaitingForOthers + " (" + ready + "/" + seated + " ready)";
            if (s.readyDeadline > 0)
            {
                long secs = System.Math.Max(0, (s.readyDeadline - ServerNow() + 999) / 1000);
                _readyInfo.text = me.ready
                    ? "Battle starts in " + secs + "s (" + ready + "/" + seated + " ready)"
                    : "Press Ready within " + secs + "s or return to the barracks!";
            }
            if (seated < 2 && me.ready) _centerMsg.text = "Awaiting an opponent...";
        }

        void RenderHighlights()
        {
            foreach (var s in _seats) s.SetHighlights(_highlight);
            foreach (var c in _boardCards)
                c.SetHighlight(c.gameObject.activeSelf && c.Code != null && _highlight.Contains(c.Code));
        }

        // ---------- player input ----------

        void OnReady()
        {
            _readyButton.interactable = false;
            App.Socket.Request("table:ready", new JObject(), ack =>
            {
                if (!ack.Value<bool>("ok"))
                {
                    _readyButton.interactable = true;
                    App.Toast(Strings.ErrorText(ack.Value<string>("error")));
                }
            });
        }

        void OnShowCards()
        {
            _showRequested = true;
            _showButton.gameObject.SetActive(false);
            App.Socket.Request("table:show", new JObject(), ack =>
            {
                if (!ack.Value<bool>("ok")) App.Toast(Strings.ErrorText(ack.Value<string>("error")));
            });
        }

        void SendAction(string type, long amount)
        {
            var payload = new JObject { ["type"] = type };
            if (type == "raise") payload["amount"] = amount;
            App.Socket.Request("table:action", payload, ack =>
            {
                if (!ack.Value<bool>("ok"))
                {
                    App.Toast(Strings.ErrorText(ack.Value<string>("error")));
                    _actions.Unlock();
                }
            });
        }

        void OnLeave()
        {
            if (_leaving) return;
            if (_state == null)
            {
                ExitToLobby(null);
                return;
            }
            _leaving = true;
            App.Socket.Request("table:leave", new JObject(), ack =>
            {
                bool ok = ack.Value<bool>("ok");
                string error = ack.Value<string>("error");
                if (ok || error == "NOT_SEATED")
                {
                    var bal = ack["balance"];
                    if (ok && bal != null && bal.Type == JTokenType.Integer && App.Session.User != null)
                        App.Session.SetBalance((long)bal, App.Session.User.nextRefillAt);
                    ExitToLobby(null);
                }
                else
                {
                    _leaving = false;
                    App.Toast(Strings.ErrorText(error));
                }
            });
        }

        // ---------- event animations ----------

        IEnumerator Play(TableEventMsg e)
        {
            switch (e.type)
            {
                case "playerJoined": return PlayJoined(e);
                case "playerLeft": return PlayLeft(e);
                case "playerReady": return PlayReady(e);
                case "handStarted": return PlayDeal(e);
                case "blinds": return PlayBlinds(e);
                case "action": return PlayAction(e);
                case "street": return PlayStreet(e);
                case "showdown": return PlayShowdown(e);
                case "potAwarded": return PlayAwards(e);
                case "cardsShown": return PlayCardsShown(e);
                case "waiting": return PlayWaiting(e);
                default: return null; // turn, connection, handEnded: the snapshot says it all
            }
        }

        T Data<T>(TableEventMsg e) where T : class
        {
            try { return e.data != null ? e.data.ToObject<T>() : null; }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[game] bad data for " + e.type + ": " + ex.Message);
                return null;
            }
        }

        IEnumerator PlayJoined(TableEventMsg e)
        {
            var d = Data<SeatOnlyData>(e);
            Apply(e.state);
            if (d == null) yield break;
            var view = ViewFor(d.seat);
            if (view != null) yield return view.Pop(view.transform, 0.6f);
        }

        IEnumerator PlayLeft(TableEventMsg e)
        {
            var d = Data<SeatOnlyData>(e);
            if (d == null) yield break;
            var view = ViewFor(d.seat);
            if (view != null && view.Occupied) yield return view.FadeOut(0.3f);
        }

        IEnumerator PlayReady(TableEventMsg e)
        {
            var d = Data<SeatOnlyData>(e);
            Apply(e.state);
            if (d == null) yield break;
            var view = ViewFor(d.seat);
            if (view != null) yield return view.Pop(view.ReadyBadge, 1.6f);
        }

        IEnumerator PlayWaiting(TableEventMsg e)
        {
            _highlight.Clear();
            _handNames.Clear();
            Apply(e.state);
            yield return _fx.FloatText(Center + new Vector2(0, 150), "The battle pauses - press Ready", Color.white, 34);
        }

        IEnumerator PlayDeal(TableEventMsg e)
        {
            var d = Data<HandStartedData>(e);
            _highlight.Clear();
            _handNames.Clear();
            Apply(e.state);
            if (d == null || d.seats == null) yield break;

            foreach (var seat in d.seats)
            {
                var v = ViewFor(seat);
                if (v != null) { v.SetCardsShown(0); if (v.IsMe) v.ShowBacks(); }
            }

            Vector2 deck = _fx.ToLocal(_deckRt);
            var steps = new List<IEnumerator>();
            float delay = 0f;
            for (int round = 0; round < 2; round++)
            {
                foreach (var seat in d.seats)
                {
                    var v = ViewFor(seat);
                    if (v == null || !v.Occupied) continue;
                    int shown = round + 1;
                    Vector2 target = _fx.ToLocal(v.CardRect(round));
                    steps.Add(_fx.FlyCard(deck, target, SeatView.CardSize, 0.22f, delay, () => v.SetCardsShown(shown)));
                    delay += 0.06f;
                }
            }
            yield return Tween.Parallel(this, steps);

            // Reveal my own two cards.
            var me = _state != null ? ViewFor(_state.yourSeat) : null;
            if (me != null && me.IsMe && _state.yourCards != null && _state.yourCards.Count == 2)
            {
                var myState = _state.Me;
                if (myState != null && myState.inHand) yield return me.FlipCards(_state.yourCards);
            }
        }

        IEnumerator PlayBlinds(TableEventMsg e)
        {
            var d = Data<BlindsData>(e);
            if (d == null) yield break;
            var steps = new List<IEnumerator>();
            if (d.sb != null) steps.Add(MarchToBet(d.sb.seat, d.sb.amount));
            if (d.bb != null) steps.Add(MarchToBet(d.bb.seat, d.bb.amount));
            yield return Tween.Parallel(this, steps);
        }

        IEnumerator MarchToBet(int seat, long amount)
        {
            var v = ViewFor(seat);
            if (v == null || amount <= 0) yield break;
            yield return _fx.MarchTroops(_fx.ToLocal(v.Rect), _fx.ToLocal(v.BetRect), amount, 0.45f, 4);
        }

        IEnumerator PlayAction(TableEventMsg e)
        {
            var a = Data<ActionData>(e);
            if (a == null) yield break;
            var v = ViewFor(a.seat);
            if (v == null) yield break;
            Vector2 camp = _fx.ToLocal(v.Rect);
            Vector2 textPos = camp + new Vector2(0, 100);

            if (a.type == "fold")
            {
                var steps = new List<IEnumerator>
                {
                    v.FoldAnim(this),
                    _fx.FloatText(textPos, a.auto ? "FOLD (time)" : "FOLD", Theme.ParchmentLight)
                };
                yield return Tween.Parallel(this, steps);
                yield break;
            }
            if (a.type == "check")
            {
                yield return _fx.FloatText(textPos, a.auto ? "CHECK (time)" : "CHECK", Theme.ParchmentLight);
                yield break;
            }

            bool allIn = a.allIn || a.type == "allin";
            string label;
            switch (a.type)
            {
                case "call": label = "CALL " + UIKit.Num(a.amount); break;
                case "raise": label = "RAISE TO " + UIKit.Num(a.bet); break;
                default: label = "ALL-IN " + UIKit.Num(a.bet); break;
            }
            if (allIn) label = "ALL-IN " + UIKit.Num(a.bet) + "!";

            var parallel = new List<IEnumerator>
            {
                _fx.FloatText(textPos, label, allIn ? Theme.GoldLight : Theme.ParchmentLight, allIn ? 40 : 30)
            };
            if (a.amount > 0)
                parallel.Add(_fx.MarchTroops(camp, _fx.ToLocal(v.BetRect), a.amount, 0.5f, allIn ? 6 : 4));
            if (allIn)
            {
                parallel.Add(_fx.Flash(new Color(1f, 0.85f, 0.5f, 0.5f), 0.35f));
                parallel.Add(_fx.Shake(_board, 14f, 0.35f));
                parallel.Add(_fx.Burst(camp, 26, Theme.GoldLight, Theme.Crimson));
            }
            yield return Tween.Parallel(this, parallel);
        }

        IEnumerator PlayStreet(TableEventMsg e)
        {
            var d = Data<StreetData>(e);

            // Bets stay where they are (in front of each player) until the hand ends.
            // New board cards flip over one by one.
            Apply(e.state);
            int boardCount = e.state.board != null ? e.state.board.Count : 0;
            int newCount = d != null && d.newCards != null ? d.newCards.Count : 0;
            int first = Mathf.Max(0, boardCount - newCount);
            for (int i = first; i < boardCount && i < 5; i++) _boardCards[i].ShowBack();
            for (int i = first; i < boardCount && i < 5; i++)
            {
                yield return _boardCards[i].FlipTo(e.state.board[i], 0.26f);
                yield return Tween.Delay(0.05f);
            }
        }

        IEnumerator PlayShowdown(TableEventMsg e)
        {
            var d = Data<ShowdownData>(e);
            if (d == null || d.reveals == null) yield break;
            foreach (var r in d.reveals)
                if (r != null && !string.IsNullOrEmpty(r.handName))
                    _handNames[r.seat] = r.rank > 0 ? Ordinal(r.rank) + "  " + r.handName : r.handName;
            Apply(e.state);

            // 1) Opponents' cards flip; mine are already face up.
            var steps = new List<IEnumerator>();
            float delay = 0f;
            foreach (var r in d.reveals)
            {
                if (r == null || r.cards == null || r.cards.Count < 2) continue;
                var v = ViewFor(r.seat);
                if (v == null || v.IsMe) continue;
                v.ShowBacks();
                steps.Add(Tween.Sequence(Tween.Delay(delay), v.FlipCards(r.cards)));
                delay += 0.2f;
            }
            yield return Tween.Parallel(this, steps);
            yield return Tween.Delay(0.4f);

            // 2) Ranking board: every hand, best first, with its best five cards.
            var order = new List<Reveal>();
            foreach (var r in d.reveals) if (r != null) order.Add(r);
            order.Sort((x, y) => (x.rank <= 0 ? 99 : x.rank).CompareTo(y.rank <= 0 ? 99 : y.rank));
            yield return ShowRankBoard(order);
            yield return Tween.Delay(1.6f);
        }

        IEnumerator ShowRankBoard(List<Reveal> order)
        {
            if (order.Count == 0) yield break;
            float h = 92f + order.Count * (RankRowH + RankRowGap) + 20f;
            _rankPanel.sizeDelta = new Vector2(RankW, h);
            _rankPanel.gameObject.SetActive(true);
            _rankPanel.SetAsLastSibling();
            _rankGroup.alpha = 1f;
            for (int i = 0; i < _rankRows.Count; i++) _rankRows[i].Rect.gameObject.SetActive(false);

            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                var row = GetRankRow(i);
                bool top = r.rank == 1;
                var seat = _state != null ? _state.Seat(r.seat) : null;
                row.Rect.gameObject.SetActive(true);
                row.Rect.anchoredPosition = new Vector2(0, -78f - i * (RankRowH + RankRowGap) - RankRowH * 0.5f);
                row.Bg.sprite = UISkin.Get(top ? "panel_gold" : "plank") ?? row.Bg.sprite;
                row.Rank.text = r.rank > 0 ? Ordinal(r.rank) : "-";
                row.Rank.color = top ? Theme.Crimson : Theme.GoldLight;
                row.Name.text = seat != null ? seat.username : ("Seat " + r.seat);
                row.Name.color = top ? Theme.Ink : Theme.ParchmentLight;
                row.Hand.text = r.handName ?? "";
                row.Hand.color = top ? Theme.CrimsonDark : Theme.GoldLight;
                var best = r.bestCards != null && r.bestCards.Count > 0 ? r.bestCards : r.cards;
                for (int c = 0; c < 5; c++)
                {
                    var card = row.Cards[c];
                    bool has = best != null && c < best.Count;
                    card.SetVisible(has);
                    if (!has) continue;
                    card.ResetVisual();
                    card.SetCard(best[c], true);
                    // the player's own hole cards light up inside their best five
                    card.SetHighlight(r.cards != null && r.cards.Contains(best[c]));
                }
                row.Group.alpha = 0f;
                row.Rect.localScale = Vector3.one * 0.9f;
                yield return Tween.Parallel(this, new List<IEnumerator>
                {
                    Tween.Fade(row.Group, 1f, 0.22f),
                    Tween.Scale(row.Rect, Vector3.one, 0.22f, Ease.OutBack)
                });
                yield return Tween.Delay(0.12f);
            }
        }

        IEnumerator PlayAwards(TableEventMsg e)
        {
            var d = Data<PotAwardedData>(e);
            if (d == null || d.awards == null || d.awards.Count == 0) yield break;

            // The ranking board makes room for the battlefield.
            if (_rankPanel.gameObject.activeSelf)
            {
                yield return Tween.Fade(_rankGroup, 0f, 0.3f);
                HideRankBoard();
            }

            // Light up the winning cards.
            foreach (var award in d.awards)
                if (award != null && award.bestCards != null) _highlight.UnionWith(award.bestCards);
            RenderHighlights();

            // What each winner takes, in total.
            var won = new Dictionary<int, long>();
            foreach (var award in d.awards)
            {
                if (award == null || award.winners == null) continue;
                foreach (var w in award.winners)
                {
                    long t; won.TryGetValue(w.seat, out t);
                    won[w.seat] = t + w.amount;
                }
            }
            if (won.Count == 0) yield break;

            // Busted players' soldiers fall where they stand first...
            var busted = new HashSet<SeatView>();
            if (e.state != null && e.state.seats != null)
                foreach (var seat in e.state.seats)
                {
                    if (seat == null || !seat.inHand || seat.stack > 0 || won.ContainsKey(seat.seat)) continue;
                    var bv = ViewFor(seat.seat);
                    if (bv != null && bv.DisplayedBet > 0 && TroopArt.Frames("death") != null) { bv.BetDies(); busted.Add(bv); }
                }
            if (busted.Count > 0) yield return Tween.Delay(0.9f);

            // ...then every player's soldiers march from their bet spot to the winner(s).
            var steps = new List<IEnumerator>();
            foreach (var v in _seats)
            {
                if (busted.Contains(v)) continue; // the fallen stay down until the hand is cleared
                long bet = v.DisplayedBet;
                if (bet <= 0) continue;
                Vector2 from = _fx.ToLocal(v.BetRect);
                v.HideBet();
                foreach (var kv in won)
                {
                    var wv = ViewFor(kv.Key);
                    if (wv == null) continue;
                    steps.Add(_fx.MarchTroops(from, _fx.ToLocal(wv.Rect), bet / won.Count, 0.7f, 5));
                }
            }
            if (steps.Count > 0) yield return Tween.Parallel(this, steps);

            // A crown over the biggest winner, "+N" over any others.
            int topSeat = -1; long topAmount = -1;
            foreach (var kv in won) if (kv.Value > topAmount) { topAmount = kv.Value; topSeat = kv.Key; }
            var after = new List<IEnumerator>();
            foreach (var kv in won)
            {
                var wv = ViewFor(kv.Key);
                if (wv == null) continue;
                Vector2 camp = _fx.ToLocal(wv.Rect);
                string line = "+" + UIKit.Num(kv.Value);
                if (kv.Key == topSeat) after.Add(_fx.CrownAt(camp + new Vector2(0, 150), line, 1.3f));
                else after.Add(_fx.FloatText(camp + new Vector2(0, 110), line, Theme.GoldLight, 40));
                after.Add(wv.Pop(wv.transform, 1.15f));
                wv.PlaySquad("victory", TroopArt.VictoryFps); // the winner's troops cheer
            }
            yield return Tween.Parallel(this, after);
        }

        IEnumerator PlayCardsShown(TableEventMsg e)
        {
            var d = Data<CardsShownData>(e);
            if (d == null || d.cards == null || d.cards.Count < 2) yield break;
            var v = ViewFor(d.seat);
            if (v == null) yield break;
            if (!v.IsMe) { v.ShowBacks(); yield return v.FlipCards(d.cards); }
            yield return _fx.FloatText(_fx.ToLocal(v.Rect) + new Vector2(0, 100), Strings.Shows, Theme.GoldLight, 34);
        }
    }
}
