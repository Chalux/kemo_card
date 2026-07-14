using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui;

public partial class AlertDlg : BaseDlg
{
    [Export] private Label? _lblTitle;
    [Export] private Label? _lblDesc;
    [Export] private Button? _btnOk;
    [Export] private Button? _btnCancel;

    private AlertDlgPayload _payload = new();
    private bool _handled;
    private bool _eventsBound;
    private bool _countdownActive;
    private double _remaining;
    private int _displayedSeconds = -1;

    public override string UIId => GlobalUiIds.Alert;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (_eventsBound)
        {
            return;
        }

        _eventsBound = true;

        if (_btnOk != null)
        {
            OnClicks(_btnOk, OnOkPressed);
        }

        if (_btnCancel != null)
        {
            OnClicks(_btnCancel, OnCancelPressed);
        }
    }

    protected override void OnOpen()
    {
        _handled = false;
        _payload = GetTypedPayload<AlertDlgPayload>();
        _remaining = _payload.Time;
        _countdownActive = _payload.Time > 0;
        _displayedSeconds = -1;

        if (_lblTitle != null)
        {
            _lblTitle.Text = _payload.TitleKey;
        }

        if (_btnOk != null)
        {
            _btnOk.Text = _payload.OkTextKey;
        }

        if (_btnCancel != null)
        {
            _btnCancel.Text = _payload.CancelTextKey;
        }

        RefreshDesc(force: true);
        SetProcess(_countdownActive);
    }

    protected override void UpdateView()
    {
    }

    protected override void OnClose()
    {
        StopCountdown();
        if (_handled)
        {
            return;
        }

        _handled = true;
        switch (_payload.CallbackWhenClose)
        {
            case AlertClosePolicy.Ok:
                _payload.OkCallback?.Invoke();
                break;
            case AlertClosePolicy.Cancel:
                _payload.CancelCallback?.Invoke();
                break;
            case AlertClosePolicy.None:
                break;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!_countdownActive || _handled)
        {
            return;
        }

        _remaining -= delta;
        RefreshDesc(force: false);
        if (_remaining <= 0)
        {
            OnTimeout();
        }
    }

    #region 按钮与倒计时

    private void OnOkPressed() => Complete(ok: true);

    private void OnCancelPressed() => Complete(ok: false);

    private void OnTimeout() => Complete(ok: false);

    private void Complete(bool ok)
    {
        if (_handled)
        {
            return;
        }

        _handled = true;
        StopCountdown();
        if (ok)
        {
            _payload.OkCallback?.Invoke();
        }
        else
        {
            _payload.CancelCallback?.Invoke();
        }

        Close();
    }

    private void StopCountdown()
    {
        _countdownActive = false;
        SetProcess(false);
    }

    private void RefreshDesc(bool force)
    {
        if (_lblDesc == null)
        {
            return;
        }

        if (_payload.Time > 0)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(_remaining));
            if (!force && seconds == _displayedSeconds)
            {
                return;
            }

            _displayedSeconds = seconds;
            _lblDesc.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
            _lblDesc.Text = string.Format(Localization.Tr(_payload.DescKey), seconds);
            return;
        }

        _lblDesc.AutoTranslateMode = Node.AutoTranslateModeEnum.Inherit;
        _lblDesc.Text = _payload.DescKey;
    }

    #endregion
}
