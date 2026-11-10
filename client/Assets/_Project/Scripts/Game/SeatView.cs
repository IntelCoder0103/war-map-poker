using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Cards;
using WarPoker.Core;
using WarPoker.FX;
using WarPoker.Net;
using WarPoker.Troops;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// One army camp on the map: banner with name, stack (number + troops), two cards,
    /// dealer/blind markers, turn glow + radial timer, ready badge, offline dim.
    /// Plus a "bet spot" on the battlefield (between camp and pot) for this street's bet.
    /// Render() always rebuilds the full look from a snapshot, so animations may leave things anywhere.
    /// </summary>
    public class SeatView : MonoBehaviour
    {
        public static readonly Vector2 CampSize = new Vector2(260, 150);
        public static readonly Vector2 CardSize = new Vector2(78, 108);
        /// <summary>Bet marker on the battlefield: a squad standing on the number plank.</summary>
        public static readonly Vector2 BetSize = new Vector2(96, 72);

        public RectTransform Rect { get; private set; }
        public RectTransform BetRect { get; private set; }
        public int Seat { get; private set; } = -1;
        public bool IsMe { get; private set; }
        public bool Occupied { get; private set; }

        /// <summary>Server time source (ms), set by the game screen for the turn timer.</summary>
        public Func<long> ServerNow;

        CanvasGroup _group;           // whole camp (offline dim, leave fade)
        Image _glow, _body, _banner, _timer, _timerBack, _dealer, _blind, _readyBadge;
        Text _name, _stack, _tag, _offline, _blindText, _empty;
        RectTransform _bannerRt;
        TroopStackView _troops;
        readonly CardView[] _cards = new CardView[2];
        GameObject _content;          // everything shown only when the seat is taken

        TroopStackView _betTroops;
        Text _betText;
        CanvasGroup _betGroup;

        bool _isTurn;
        long _deadline, _turnTimeMs = 20000;
        long _tickedSecond = -1;
        int _cardsRendered;           // how many card slots the last Render showed

        static readonly Vector2 BannerPos = new Vector2(0, 50);

        public static SeatView Create(RectTransform board, int display, Vector2 pos, Vector2 betPos)
        {
            var rt = UIKit.Rect("Camp" + display, board);
            UIKit.Center(rt, pos, CampSize);
            var view = rt.gameObject.AddComponent<SeatView>();
            view.Rect = rt;
            view.Build(board, betPos);
            return view;
        }

        void Build(RectTransform board, Vector2 betPos)
        {
            _group = UIKit.Group(gameObject);
            _group.blocksRaycasts = false;

            _glow = UIKit.Deco(transform, "Glow", ProcSprites.SoftRect(), Theme.GoldLight);
            UIKit.Center(_glow.rectTransform, Vector2.zero, CampSize + new Vector2(90, 90));
            _glow.enabled = false;

            _empty = UIKit.Text(transform, "Empty", Strings.OpenCamp, 24, new Color(1f, 1f, 1f, 0.75f),
                TextAnchor.MiddleCenter, FontStyle.Italic);
            UIKit.AddOutline(_empty, 1.5f, 0.5f);
            UIKit.Center(_empty.rectTransform, Vector2.zero, CampSize);

            _content = UIKit.Rect("Content", transform).gameObject;
            UIKit.Stretch((RectTransform)_content.transform);
            var c = _content.transform;

            _body = UIKit.Panel(c, "Body", "panel_wide", Color.white, 0.38f, true);
            UIKit.Center(_body.rectTransform, Vector2.zero, CampSize);
            UIKit.AddShadow(_body, 4f, 0.35f);

            _banner = UIKit.Panel(c, "Banner", "bar_fill", Theme.Crimson, 0.9f, true);
            _bannerRt = _banner.rectTransform;
            UIKit.Center(_bannerRt, BannerPos, new Vector2(244, 42));
            _name = UIKit.Text(_bannerRt, "Name", "", 26, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_name.rectTransform, 4f);
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.AddOutline(_name, 2f);

            _stack = UIKit.Text(c, "Stack", "", 28, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Center(_stack.rectTransform, new Vector2(-38, 6), new Vector2(130, 36));

            _troops = TroopStackView.Create(c, "Troops", new Vector2(130, 44), 36f);
            UIKit.Center((RectTransform)_troops.transform, new Vector2(-40, -36), new Vector2(130, 44));

            for (int i = 0; i < 2; i++)
            {
                var card = CardView.Create(c, "Card" + i, CardSize);
                UIKit.Center(card.Rect, CardHome(i), CardSize);
                card.Rect.localEulerAngles = new Vector3(0, 0, i == 0 ? 6f : -6f);
                _cards[i] = card;
            }

            _tag = UIKit.Text(c, "Tag", "", 20, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_tag.rectTransform, new Vector2(70, -62), new Vector2(200, 28));
            _tag.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.AddShadow(_tag, 2f, 0.9f);

            _readyBadge = UIKit.Panel(c, "Ready", "bar_fill", Theme.Positive, 0.7f, true);
            UIKit.Center(_readyBadge.rectTransform, new Vector2(78, 86), new Vector2(104, 30));
            var rt = UIKit.Text(_readyBadge.transform, "Text", Strings.ReadyBadge, 18, Theme.ParchmentLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(rt.rectTransform);
            UIKit.AddOutline(rt, 1.5f);

            _offline = UIKit.Text(c, "Offline", Strings.Offline, 26, Theme.Crimson, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_offline.rectTransform, new Vector2(0, -2), new Vector2(240, 40));
            UIKit.AddShadow(_offline, 1f, 0.6f);

            // Turn timer ring (top-right corner).
            _timerBack = UIKit.Deco(c, "TimerBack", ProcSprites.Disc(64, 0.1f), new Color(0.25f, 0.17f, 0.1f, 0.85f));
            UIKit.Center(_timerBack.rectTransform, new Vector2(128, 72), new Vector2(54, 54));
            _timer = UIKit.Deco(c, "Timer", ProcSprites.Ring(128, 0.2f), Theme.Positive);
            UIKit.Center(_timer.rectTransform, new Vector2(128, 72), new Vector2(60, 60));
            _timer.type = Image.Type.Filled;
            _timer.fillMethod = Image.FillMethod.Radial360;
            _timer.fillOrigin = (int)Image.Origin360.Top;
            _timer.fillClockwise = false;

            // Dealer button and blind badge (top-left corner).
            // Dealer button and blind chips: poker chips with a notched rim.
            _dealer = UIKit.Deco(c, "Dealer", ProcSprites.PokerChip(new Color(0.97f, 0.95f, 0.9f), new Color(0.80f, 0.14f, 0.12f), Color.white), Color.white);
            UIKit.Center(_dealer.rectTransform, new Vector2(-140, 74), new Vector2(54, 54));
            UIKit.AddShadow(_dealer, 2f, 0.45f);
            var dt = UIKit.Text(_dealer.transform, "D", Strings.Get("DealerMark"), 30, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(dt.rectTransform);
            _blind = UIKit.Deco(c, "Blind", null, Color.white);
            UIKit.Center(_blind.rectTransform, new Vector2(-142, 18), new Vector2(54, 54));
            UIKit.AddShadow(_blind, 2f, 0.45f);
            _blindText = UIKit.Text(_blind.transform, "Text", "", 21, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_blindText.rectTransform);
            UIKit.AddOutline(_blindText, 2f);

            // Bet spot on the battlefield (sibling on the board so it can sit outside the camp).
            var bet = UIKit.Rect("Bet" + name, board);
            UIKit.Center(bet, betPos, BetSize);
            BetRect = bet;
            _betGroup = UIKit.Group(bet.gameObject);
            _betGroup.blocksRaycasts = false;
            _betTroops = TroopStackView.Create(bet, "Troops", new Vector2(BetSize.x, 40), 38f);
            _betTroops.LabelColor = Theme.ParchmentLight;
            UIKit.Center((RectTransform)_betTroops.transform, new Vector2(0, 14), new Vector2(BetSize.x, 40));
            var betBg = UIKit.Panel(bet, "NumberBg", "plank", Color.white, 0.5f, true);
            UIKit.Center(betBg.rectTransform, new Vector2(0, -21), new Vector2(BetSize.x, 30));
            _betText = UIKit.Text(betBg.transform, "Number", "", 20, Theme.GoldLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_betText.rectTransform);
            _betText.horizontalOverflow = HorizontalWrapMode.Overflow;
            bet.gameObject.SetActive(false);

            Clear();
        }

        static Vector2 CardHome(int i)
        {
            return new Vector2(i == 0 ? 52f : 98f, -14f);
        }

        public RectTransform CardRect(int i)
        {
            return _cards[Mathf.Clamp(i, 0, 1)].Rect;
        }

        public CardView Card(int i)
        {
            return _cards[Mathf.Clamp(i, 0, 1)];
        }

        /// <summary>Empty camp (no player).</summary>
        public void Clear()
        {
            Seat = -1;
            Occupied = false;
            IsMe = false;
            _isTurn = false;
            _content.SetActive(false);
            _empty.gameObject.SetActive(true);
            _glow.enabled = false;
            _group.alpha = 1f;
            transform.localScale = Vector3.one;
            BetRect.gameObject.SetActive(false);
            _cardsRendered = 0;
        }

        /// <summary>Full redraw from the snapshot. handName / highlight come from the game screen.</summary>
        public void Render(int seatIndex, SeatDto seat, Snapshot s, string handName, HashSet<string> highlight)
        {
            Seat = seatIndex;
            IsMe = seatIndex == s.yourSeat && s.yourSeat >= 0;
            transform.localScale = Vector3.one * (IsMe ? 1.12f : 1f);
            _group.alpha = 1f;

            if (seat == null)
            {
                Clear();
                Seat = seatIndex;
                return;
            }
            Occupied = true;
            _content.SetActive(true);
            _empty.gameObject.SetActive(false);

            bool waiting = s.phase == "waiting";
            _name.text = seat.username ?? "?";
            _stack.text = UIKit.Num(seat.stack);
            _troops.SetAmount(seat.stack);

            // Banner: navy for me, crimson for others; lowered and grey when folded.
            bool folded = seat.inHand && seat.folded && !waiting;
            _banner.color = folded ? Theme.Disabled : (IsMe ? Theme.Navy : Theme.Crimson);
            _bannerRt.anchoredPosition = folded ? BannerPos + new Vector2(0, -10) : BannerPos;
            _bannerRt.localEulerAngles = Vector3.zero;

            // Offline
            _offline.gameObject.SetActive(!seat.connected);
            _body.color = seat.connected ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.95f);
            if (!seat.connected) _group.alpha = 0.6f;

            // Markers
            _dealer.gameObject.SetActive(!waiting && s.dealerSeat == seatIndex);
            bool sb = !waiting && s.sbSeat == seatIndex, bb = !waiting && s.bbSeat == seatIndex;
            _blind.gameObject.SetActive(sb || bb);
            _blindText.text = Strings.Get(bb ? "BigBlindMark" : "SmallBlindMark");
            _blind.sprite = bb
                ? ProcSprites.PokerChip(new Color(0.95f, 0.58f, 0.10f), new Color(0.72f, 0.30f, 0.05f), new Color(1f, 0.92f, 0.65f))   // big blind: orange-gold
                : ProcSprites.PokerChip(new Color(0.20f, 0.52f, 0.92f), new Color(0.10f, 0.28f, 0.62f), new Color(0.85f, 0.93f, 1f));  // small blind: blue
            _readyBadge.gameObject.SetActive(waiting && seat.ready);
            _readyBadge.transform.localScale = Vector3.one;

            // Turn
            _isTurn = !waiting && s.turnSeat == seatIndex && s.turnSeat >= 0;
            if (s.turnDeadline != _deadline) _tickedSecond = -1;
            _deadline = s.turnDeadline;
            _turnTimeMs = s.turnTimeMs > 0 ? s.turnTimeMs : 20000;
            _glow.enabled = _isTurn;
            _timer.gameObject.SetActive(_isTurn && _deadline > 0);
            _timerBack.gameObject.SetActive(_isTurn && _deadline > 0);

            // Cards
            List<string> faces = null;
            bool showCards = seat.inHand && !waiting;
            if (showCards)
            {
                if (IsMe && s.yourCards != null && s.yourCards.Count == 2) faces = s.yourCards;
                else if (seat.cards != null && seat.cards.Count == 2) faces = seat.cards;
                if (folded && !IsMe && seat.cards == null) showCards = false; // folded opponents: cards gone
            }
            _cardsRendered = showCards ? 2 : 0;
            for (int i = 0; i < 2; i++)
            {
                var card = _cards[i];
                card.ResetVisual();
                card.Rect.anchoredPosition = CardHome(i);
                card.Rect.localEulerAngles = new Vector3(0, 0, i == 0 ? 6f : -6f);
                card.SetVisible(showCards);
                if (!showCards) continue;
                if (faces != null) card.SetCard(faces[i], true);
                else { card.SetCard(null, false); }
                card.SetDim(folded);
                card.SetHighlight(highlight != null && card.Code != null && card.FaceUp && highlight.Contains(card.Code));
            }

            // Tag: hand name at showdown, otherwise the last action / sitting-out note.
            if (!string.IsNullOrEmpty(handName))
            {
                _tag.text = handName;
                _tag.color = Theme.GoldLight;
            }
            else if (!waiting && seat.leavingAfterHand)
            {
                _tag.text = Strings.Get("Tag.leaving");
                _tag.color = Theme.GoldLight;
            }
            else if (!waiting && !seat.inHand)
            {
                _tag.text = Strings.NextHand;
                _tag.color = Theme.ParchmentLight;
            }
            else
            {
                _tag.text = !waiting && !string.IsNullOrEmpty(seat.lastAction) ? ActionWord(seat.lastAction) : "";
                _tag.color = Theme.ParchmentLight;
            }

            // Bet spot: everything this player put in during the hand stays in front of them
            // until the pot is paid out (older servers only send this street's bet).
            long committed = seat.totalBet > 0 ? seat.totalBet : seat.bet;
            bool hasBet = committed > 0 && !waiting && seat.inHand;
            BetRect.gameObject.SetActive(hasBet);
            _betGroup.alpha = folded ? 0.55f : 1f;
            if (hasBet)
            {
                _betTroops.SetAmount(committed);
                _betText.text = UIKit.Num(committed);
            }
        }

        public static string ActionWord(string action)
        {
            return Strings.ActionWord(action);
        }

        /// <summary>Shows only the first n card slots (dealing animation reveals them one by one).</summary>
        public void SetCardsShown(int n)
        {
            for (int i = 0; i < 2; i++) _cards[i].SetVisible(i < n && i < _cardsRendered);
        }

        public void ShowBacks()
        {
            for (int i = 0; i < 2; i++) _cards[i].ShowBack();
        }

        public void SetHighlights(HashSet<string> codes)
        {
            for (int i = 0; i < 2; i++)
            {
                var c = _cards[i];
                c.SetHighlight(codes != null && c.gameObject.activeSelf && c.FaceUp && c.Code != null && codes.Contains(c.Code));
            }
        }

        /// <summary>The camp's squad plays a one-shot clip (victory) and goes back to idling.</summary>
        public void PlaySquad(string clip, float fps) { _troops.PlayOnce(clip, fps); }

        /// <summary>The soldiers at the bet spot fall where they stand (the player is busted).</summary>
        public void BetDies() { _betTroops.PlayDeath(); }

        public void HideBet()
        {
            BetRect.gameObject.SetActive(false);
        }

        public long DisplayedBet { get { return BetRect.gameObject.activeSelf ? _betTroops.Amount : 0; } }

        // ---------- animations ----------

        /// <summary>Flip both cards to the given faces, one after the other.</summary>
        public IEnumerator FlipCards(List<string> codes)
        {
            if (codes == null || codes.Count < 2) yield break;
            for (int i = 0; i < 2; i++)
            {
                _cards[i].SetVisible(true);
                yield return _cards[i].FlipTo(codes[i], 0.24f);
            }
        }

        /// <summary>Fold: cards fade and drop (dim for my own), banner lowers and greys.</summary>
        public IEnumerator FoldAnim(MonoBehaviour host)
        {
            var steps = new List<IEnumerator>();
            for (int i = 0; i < 2; i++)
            {
                var card = _cards[i];
                if (!card.gameObject.activeSelf) continue;
                if (IsMe)
                {
                    card.SetDim(true);
                    continue;
                }
                steps.Add(Tween.Fade(card.Group, 0f, 0.3f));
                steps.Add(Tween.Move(card.Rect, card.Rect.anchoredPosition + new Vector2(0, -40), 0.3f, Ease.InQuad));
                steps.Add(Tween.RotateZ(card.Rect, i == 0 ? 30f : -30f, 0.3f));
            }
            steps.Add(Tween.Move(_bannerRt, BannerPos + new Vector2(0, -10), 0.3f, Ease.OutBounce));
            var from = _banner.color;
            steps.Add(Tween.Value(0.3f, k => _banner.color = Color.Lerp(from, Theme.Disabled, k)));
            yield return Tween.Parallel(host, steps);
        }

        /// <summary>Little pop (join / ready).</summary>
        public IEnumerator Pop(Transform target, float from)
        {
            if (target == null) target = transform;
            Vector3 end = target.localScale;
            target.localScale = end * from;
            yield return Tween.Scale(target, end, 0.25f, Ease.OutBack);
        }

        public Transform ReadyBadge { get { return _readyBadge.transform; } }

        public IEnumerator FadeOut(float duration)
        {
            yield return Tween.Fade(_group, 0f, duration);
        }

        void Update()
        {
            if (!_isTurn) return;
            // Pulsing glow
            var c = _glow.color;
            c.a = 0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            _glow.color = c;

            // Timer ring
            if (_deadline > 0 && ServerNow != null)
            {
                long remaining = _deadline - ServerNow();
                float frac = Mathf.Clamp01((float)remaining / _turnTimeMs);
                _timer.fillAmount = frac;
                _timer.color = frac > 0.5f ? Theme.Positive : Color.Lerp(Theme.Crimson, Theme.Gold, frac * 2f);
                // my own turn: a tick for each of the last five seconds
                long second = remaining > 0 ? (remaining + 999) / 1000 : 0;
                if (IsMe && second >= 1 && second <= 5 && second != _tickedSecond)
                {
                    _tickedSecond = second;
                    Sfx.Play("tick", 1f, 0.3f);
                }
            }
        }
    }
}
