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
        static readonly Vector2 PotPos = new Vector2(-40, -100);
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
        Button _readyButton;
        GameObject _readyPanel;

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

            var compass = UIKit.Deco(_board, "Compass", MapArt.Compass(), new Color(1, 1, 1, 0.85f));
            UIKit.Center(compass.rectTransform, new Vector2(-820, -415), new Vector2(150, 150));

            // Battlefield: a faint ring drawn around the centre.
            var field = UIKit.Deco(_board, "Battlefield", ProcSprites.Glow(128), new Color(0.55f, 0.35f, 0.15f, 0.25f));
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
                var slot = UIKit.Deco(_board, "Slot" + i, ProcSprites.RoundedRect(10, 3, 0.4f), new Color(0.3f, 0.2f, 0.1f, 0.18f));
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

            // Pot army + number.
            _potRt = UIKit.Rect("Pot", _board);
            UIKit.Center(_potRt, PotPos, new Vector2(260, 46));
            _potTroops = TroopStackView.Create(_potRt, "Army", new Vector2(260, 46), 32f);
            _potTroops.LabelColor = Theme.Ink;
            UIKit.Stretch((RectTransform)_potTroops.transform);
            var potBg = UIKit.Deco(_board, "PotLabelBg", ProcSprites.RoundedRect(12, 2, 0.5f), new Color(0.2f, 0.13f, 0.07f, 0.85f));
            UIKit.Center(potBg.rectTransform, PotPos + new Vector2(230, 0), new Vector2(190, 42));
            _potText = UIKit.Text(potBg.transform, "Text", "", 24, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_potText.rectTransform);
            _potText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Top-left: retreat + table info.
            var leave = UIKit.Button(_board, "Leave", Strings.Leave, new Vector2(190, 64), Theme.Leather, Theme.ParchmentLight, 28, OnLeave);
            UIKit.Center((RectTransform)leave.transform, new Vector2(-835, 490), new Vector2(190, 64));
            _info = UIKit.Text(_board, "Info", "", 24, Theme.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Center(_info.rectTransform, new Vector2(-700, 420), new Vector2(460, 60));

            _centerMsg = UIKit.Text(_board, "CenterMessage", "", 40, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_centerMsg.rectTransform, Center + new Vector2(0, 150), new Vector2(900, 60));

            // Ready panel (waiting phase), same area as the action bar.
            var rp = UIKit.Rect("ReadyPanel", _board);
            UIKit.Center(rp, ActionBarPos + new Vector2(0, 30), new Vector2(780, 160));
            _readyPanel = rp.gameObject;
            _readyInfo = UIKit.Text(rp, "Info", "", 26, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_readyInfo.rectTransform, new Vector2(0, 52), new Vector2(780, 40));
            _readyButton = UIKit.Button(rp, "Ready", Strings.Ready, new Vector2(440, 96), Theme.Crimson, Theme.GoldLight, 40, OnReady);
            UIKit.Center((RectTransform)_readyButton.transform, new Vector2(0, -20), new Vector2(440, 96));
            _readyPanel.SetActive(false);

            _actions = ActionBar.Create(_board, ActionBarPos);
            _actions.OnAction = SendAction;

            _fx = FxLayer.Create(_board, this);
        }

        /// <summary>Bet spot: just outside the camp, on the line toward the pot.</summary>
        static Vector2 BetSpot(Vector2 camp)
        {
            Vector2 dir = (Center - camp);
            if (dir.sqrMagnitude < 1f) return camp;
            dir.Normalize();
            float hx = SeatView.CampSize.x * 0.5f, hy = SeatView.CampSize.y * 0.5f;
            float tx = Mathf.Abs(dir.x) > 0.001f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.001f ? hy / Mathf.Abs(dir.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            return camp + dir * (t + 42f);
        }

        // ---------- screen lifecycle ----------

        public override void OnShow()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _leaving = false;
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
                int backlog = _queue.Count;
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
        }

        void Update()
        {
            // Safety net: if an animation ever got stuck (or threw), drop it and catch up.
            if (_runner != null && Time.unscaledTime - _stepStarted > 6f)
            {
                Debug.LogWarning("[game] animation stuck; catching up");
                Snapshot last = _playingState;
                while (_queue.Count > 0) last = _queue.Dequeue().state;
                CancelAnimations();
                _playingState = null;
                if (last != null) Apply(last);
                else if (_state != null) Apply(_state);
            }
        }

        void CancelAnimations()
        {
            StopAllCoroutines();
            _runner = null;
            Tween.Speed = 1f;
            if (_fx != null) _fx.Clear();
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

            // Pot: troops show what's gathered; the label shows the total incl. current bets.
            _potRt.gameObject.SetActive(true);
            _potTroops.SetAmount(s.GatheredPot);
            _potText.text = Strings.PotLabel + UIKit.Num(s.potTotal);

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
            yield return _fx.FloatText(Center + new Vector2(0, 150), "The battle pauses - press Ready", Theme.Ink, 34);
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

            // 1) This street's bets march into the pot.
            var gather = new List<IEnumerator>();
            Vector2 pot = _fx.ToLocal(_potRt);
            foreach (var v in _seats)
            {
                long bet = v.DisplayedBet;
                if (bet <= 0) continue;
                Vector2 from = _fx.ToLocal(v.BetRect);
                v.HideBet();
                gather.Add(_fx.MarchTroops(from, pot, bet, 0.4f, 3));
            }
            if (gather.Count > 0) yield return Tween.Parallel(this, gather);

            // 2) New board cards flip over one by one.
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
                if (r != null && !string.IsNullOrEmpty(r.handName)) _handNames[r.seat] = r.handName;
            Apply(e.state);

            // Opponents' cards flip; mine are already face up.
            var steps = new List<IEnumerator>();
            float delay = 0f;
            foreach (var r in d.reveals)
            {
                if (r == null || r.cards == null || r.cards.Count < 2) continue;
                var v = ViewFor(r.seat);
                if (v == null || v.IsMe) continue;
                v.ShowBacks();
                steps.Add(Tween.Sequence(Tween.Delay(delay), v.FlipCards(r.cards)));
                delay += 0.25f;
            }
            yield return Tween.Parallel(this, steps);
            yield return Tween.Delay(0.4f);
        }

        IEnumerator PlayAwards(TableEventMsg e)
        {
            var d = Data<PotAwardedData>(e);
            if (d == null || d.awards == null || d.awards.Count == 0) yield break;
            Vector2 pot = _fx.ToLocal(_potRt);

            // Light up the best cards of all awards.
            foreach (var award in d.awards)
                if (award != null && award.bestCards != null) _highlight.UnionWith(award.bestCards);
            RenderHighlights();

            // The pot army marches to each winner while the victory banner shows.
            var steps = new List<IEnumerator>();
            _potRt.gameObject.SetActive(false);
            foreach (var award in d.awards)
            {
                if (award == null || award.winners == null) continue;
                foreach (var w in award.winners)
                {
                    var v = ViewFor(w.seat);
                    if (v != null) steps.Add(_fx.MarchTroops(pot, _fx.ToLocal(v.Rect), w.amount, 0.6f, 6));
                }
            }

            var main = d.awards[0];
            if (main != null && main.winners != null && main.winners.Count > 0)
            {
                var names = new List<string>();
                long total = 0;
                foreach (var w in main.winners)
                {
                    var seat = _state != null ? _state.Seat(w.seat) : null;
                    names.Add(seat != null ? seat.username : ("Seat " + w.seat));
                    total += w.amount;
                }
                string how = string.IsNullOrEmpty(main.handName) ? Strings.Uncontested : main.handName;
                string line = string.Join(" & ", names.ToArray()) + "  -  " + how + "  (+" + UIKit.Num(total) + ")";
                steps.Add(_fx.Victory(line, 1.1f));
            }
            yield return Tween.Parallel(this, steps);
            _potRt.gameObject.SetActive(true);
        }
    }
}
