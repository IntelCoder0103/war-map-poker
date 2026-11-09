using System;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Core;
using WarPoker.Net;
using WarPoker.UI;

namespace WarPoker.Game
{
    /// <summary>
    /// Bottom-right action bar, shown only when snapshot.legal is set (my turn):
    /// Fold / Check or Call N / Raise (slider minRaiseTo..maxRaiseTo + Min, 1/2 Pot, Pot) / All-in.
    /// Only reads `legal`; the server decides what is allowed.
    /// </summary>
    public class ActionBar : MonoBehaviour
    {
        /// <summary>(type, amount) — amount is used for "raise" only.</summary>
        public Action<string, long> OnAction;

        Button _fold, _checkCall, _raise, _allIn, _confirm, _min, _half, _pot;
        Text _checkCallLabel, _confirmLabel, _value;
        Slider _slider;
        GameObject _bar, _raisePanel;
        LegalDto _legal;
        Snapshot _state;
        bool _pending;

        public static ActionBar Create(RectTransform board, Vector2 barPos)
        {
            var rt = UIKit.Rect("ActionBar", board);
            UIKit.Center(rt, Vector2.zero, Vector2.zero);
            var bar = rt.gameObject.AddComponent<ActionBar>();
            bar.Build(rt, barPos);
            return bar;
        }

        void Build(RectTransform root, Vector2 barPos)
        {
            // Main row
            var barImg = UIKit.Panel(root, "Bar", "plank", Color.white, 0.9f);
            UIKit.Center(barImg.rectTransform, barPos, new Vector2(780, 104));
            _bar = barImg.gameObject;
            var b = barImg.transform;
            Vector2 size = new Vector2(180, 82);
            _fold = UIKit.Button(b, "Fold", Strings.Fold, size, Theme.Crimson, Theme.ParchmentLight, 30, () => Send("fold", 0));
            UIKit.Center((RectTransform)_fold.transform, new Vector2(-288, 0), size);
            _checkCall = UIKit.Button(b, "CheckCall", Strings.Check, size, Theme.Forest, Theme.ParchmentLight, 28, OnCheckCall);
            UIKit.Center((RectTransform)_checkCall.transform, new Vector2(-96, 0), size);
            _checkCallLabel = UIKit.ButtonLabel(_checkCall);
            _raise = UIKit.Button(b, "Raise", Strings.Raise, size, Theme.Gold, Theme.ParchmentLight, 30, ToggleRaise);
            UIKit.Center((RectTransform)_raise.transform, new Vector2(96, 0), size);
            _allIn = UIKit.Button(b, "AllIn", Strings.AllIn, size, Theme.Navy, Theme.GoldLight, 30, () => Send("allin", 0));
            UIKit.Center((RectTransform)_allIn.transform, new Vector2(288, 0), size);

            // Raise panel (above the bar)
            var panel = UIKit.Panel(root, "RaisePanel", "panel_wide", Color.white, 0.45f);
            UIKit.Center(panel.rectTransform, barPos + new Vector2(0, 128), new Vector2(780, 136));
            _raisePanel = panel.gameObject;
            var p = panel.transform;

            _slider = UIKit.Slider(p, "Amount", new Vector2(500, 36));
            UIKit.Center((RectTransform)_slider.transform, new Vector2(-110, 32), new Vector2(500, 36));
            _slider.wholeNumbers = true;
            _slider.onValueChanged.AddListener(v => UpdateValueLabel());
            _value = UIKit.Text(p, "Value", "", 30, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Center(_value.rectTransform, new Vector2(270, 32), new Vector2(200, 44));

            Vector2 q = new Vector2(124, 54);
            _min = UIKit.Button(p, "Min", Strings.Min, q, Theme.ParchmentDark, Theme.Ink, 24, () => SetRaise(_legal != null ? _legal.minRaiseTo : 0));
            UIKit.Center((RectTransform)_min.transform, new Vector2(-300, -34), q);
            _half = UIKit.Button(p, "HalfPot", Strings.HalfPot, q, Theme.ParchmentDark, Theme.Ink, 24, () => SetPotFraction(0.5));
            UIKit.Center((RectTransform)_half.transform, new Vector2(-166, -34), q);
            _pot = UIKit.Button(p, "Pot", Strings.Pot, q, Theme.ParchmentDark, Theme.Ink, 24, () => SetPotFraction(1.0));
            UIKit.Center((RectTransform)_pot.transform, new Vector2(-32, -34), q);
            _confirm = UIKit.Button(p, "Confirm", Strings.Format("RaiseTo", ""), new Vector2(300, 58), Theme.Gold, Theme.ParchmentLight, 26, ConfirmRaise);
            UIKit.Center((RectTransform)_confirm.transform, new Vector2(210, -34), new Vector2(300, 58));
            _confirmLabel = UIKit.ButtonLabel(_confirm);

            Hide();
        }

        public void Hide()
        {
            _bar.SetActive(false);
            _raisePanel.SetActive(false);
            _legal = null;
        }

        /// <summary>Show for this snapshot if it's my turn (legal != null), otherwise hide.</summary>
        public void Render(Snapshot s)
        {
            _state = s;
            var legal = s != null ? s.legal : null;
            bool myTurn = legal != null && s.yourSeat >= 0 && s.turnSeat == s.yourSeat;
            if (!myTurn)
            {
                Hide();
                _pending = false;
                return;
            }
            bool fresh = _legal == null;
            _legal = legal;
            _pending = false;
            _bar.SetActive(true);

            _fold.gameObject.SetActive(legal.canFold);
            if (legal.canCheck)
            {
                _checkCall.gameObject.SetActive(true);
                _checkCallLabel.text = Strings.Check;
            }
            else if (legal.canCall)
            {
                _checkCall.gameObject.SetActive(true);
                _checkCallLabel.text = Strings.Format("Call", UIKit.Num(legal.callAmount));
            }
            else
            {
                _checkCall.gameObject.SetActive(false);
            }
            _raise.gameObject.SetActive(legal.canRaise);
            _allIn.gameObject.SetActive(legal.canAllIn);
            SetInteractable(true);

            if (legal.canRaise)
            {
                _slider.minValue = legal.minRaiseTo;
                _slider.maxValue = Mathf.Max(legal.minRaiseTo, legal.maxRaiseTo);
                if (fresh || _slider.value < legal.minRaiseTo || _slider.value > legal.maxRaiseTo)
                    _slider.SetValueWithoutNotify(legal.minRaiseTo);
                UpdateValueLabel();
                if (fresh) _raisePanel.SetActive(false);
            }
            else
            {
                _raisePanel.SetActive(false);
            }
        }

        void SetInteractable(bool on)
        {
            _fold.interactable = on;
            _checkCall.interactable = on;
            _raise.interactable = on;
            _allIn.interactable = on;
            _confirm.interactable = on;
        }

        void OnCheckCall()
        {
            if (_legal == null) return;
            if (_legal.canCheck) Send("check", 0);
            else if (_legal.canCall) Send("call", 0);
        }

        void ToggleRaise()
        {
            if (_legal == null || !_legal.canRaise) return;
            _raisePanel.SetActive(!_raisePanel.activeSelf);
        }

        void SetRaise(long amount)
        {
            if (_legal == null) return;
            _slider.value = RaiseMath.Clamp(amount, _legal.minRaiseTo, _legal.maxRaiseTo);
            UpdateValueLabel();
        }

        void SetPotFraction(double fraction)
        {
            if (_legal == null || _state == null) return;
            SetRaise(RaiseMath.PotFractionRaiseTo(_state.currentBet, _state.potTotal, _legal.callAmount,
                fraction, _legal.minRaiseTo, _legal.maxRaiseTo));
        }

        long RaiseAmount
        {
            get { return (long)Mathf.Round(_slider.value); }
        }

        void UpdateValueLabel()
        {
            if (_legal == null) return;
            long v = RaiseAmount;
            _value.text = UIKit.Num(v);
            _confirmLabel.text = Strings.Format(v >= _legal.maxRaiseTo ? "RaiseToAll" : "RaiseTo", UIKit.Num(v));
        }

        void ConfirmRaise()
        {
            if (_legal == null) return;
            Send("raise", RaiseMath.Clamp(RaiseAmount, _legal.minRaiseTo, _legal.maxRaiseTo));
        }

        void Send(string type, long amount)
        {
            if (_pending || _legal == null) return;
            _pending = true;
            SetInteractable(false);
            if (OnAction != null) OnAction(type, amount);
        }

        /// <summary>Called when the server rejected the action: let the player try again.</summary>
        public void Unlock()
        {
            _pending = false;
            if (_legal != null) SetInteractable(true);
        }
    }
}
