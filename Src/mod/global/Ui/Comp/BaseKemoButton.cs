using Godot;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Ui.Tip;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseKemoButton : Button
{
    [Export] public bool EnableHoverScale { get; set; } = true;
    [Export] public bool EnablePressScale { get; set; } = true;
    [Export] public float HoverScale { get; set; } = 1.05f;
    [Export] public float PressScale { get; set; } = 0.95f;
    [Export] public float AnimDuration { get; set; } = 0.08f;
    [Export] public float TipDelaySec { get; set; } = 0.15f;
    [Export] public TipSide PreferTipSide { get; set; } = TipSide.Right;

    /// <summary>无参词条 id 列表（编辑器快捷配置）。</summary>
    [Export] public string[] KeywordIds { get; set; } = [];

    private Tween? _scaleTween;
    private Tween? _tipDelayTween;
    private bool _hoveredOrFocused;
    private bool _pressedVisual;
    private IReadOnlyList<KeywordTipRequest> _keywordTips = Array.Empty<KeywordTipRequest>();
    private bool _bound;

    public override void _Ready()
    {
        base._Ready();
        PivotOffset = Size / 2f;
        Resized += OnResized;
        EnsureBound();
        SyncKeywordTipsFromIds();
    }

    public override void _ExitTree()
    {
        Unbind();
        CancelTipDelay();
        KeywordTipService.Current?.HideTips(this);
        base._ExitTree();
    }

    /// <summary>以完整请求列表设置词条（含命名参数）。</summary>
    public void SetKeywordTips(IReadOnlyList<KeywordTipRequest>? tips)
    {
        _keywordTips = tips ?? Array.Empty<KeywordTipRequest>();
    }

    #region 绑定

    private void EnsureBound()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
        FocusEntered += OnFocusEntered;
        FocusExited += OnFocusExited;
        ButtonDown += OnButtonDown;
        ButtonUp += OnButtonUp;
    }

    private void Unbind()
    {
        if (!_bound)
        {
            return;
        }

        _bound = false;
        MouseEntered -= OnMouseEntered;
        MouseExited -= OnMouseExited;
        FocusEntered -= OnFocusEntered;
        FocusExited -= OnFocusExited;
        ButtonDown -= OnButtonDown;
        ButtonUp -= OnButtonUp;
        Resized -= OnResized;
    }

    #endregion

    #region 交互

    private void OnResized() => PivotOffset = Size / 2f;

    private void OnMouseEntered() => EnterHighlight();

    private void OnMouseExited()
    {
        if (HasFocus())
        {
            return;
        }

        LeaveHighlight();
    }

    private void OnFocusEntered() => EnterHighlight();

    private void OnFocusExited()
    {
        if (IsHovered())
        {
            return;
        }

        LeaveHighlight();
    }

    private void OnButtonDown()
    {
        _pressedVisual = true;
        RefreshScale();
    }

    private void OnButtonUp()
    {
        _pressedVisual = false;
        RefreshScale();
    }

    private void EnterHighlight()
    {
        _hoveredOrFocused = true;
        RefreshScale();
        ScheduleShowTips();
    }

    private void LeaveHighlight()
    {
        _hoveredOrFocused = false;
        _pressedVisual = false;
        RefreshScale();
        CancelTipDelay();
        KeywordTipService.Current?.HideTips(this);
    }

    #endregion

    #region 动画

    private void RefreshScale()
    {
        var target = 1f;
        if (_pressedVisual && EnablePressScale)
        {
            target = PressScale;
        }
        else if (_hoveredOrFocused && EnableHoverScale)
        {
            target = HoverScale;
        }

        AnimateScale(target);
    }

    private void AnimateScale(float target)
    {
        _scaleTween?.Kill();
        if (AnimDuration <= 0f)
        {
            Scale = new Vector2(target, target);
            return;
        }

        _scaleTween = CreateTween();
        _scaleTween.TweenProperty(this, "scale", new Vector2(target, target), AnimDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    #endregion

    #region 提示

    private void SyncKeywordTipsFromIds()
    {
        if (_keywordTips.Count > 0 || KeywordIds == null || KeywordIds.Length == 0)
        {
            return;
        }

        var list = new List<KeywordTipRequest>(KeywordIds.Length);
        foreach (var id in KeywordIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            list.Add(new KeywordTipRequest(id));
        }

        _keywordTips = list;
    }

    private void ScheduleShowTips()
    {
        CancelTipDelay();
        SyncKeywordTipsFromIds();
        if (_keywordTips.Count == 0)
        {
            return;
        }

        if (TipDelaySec <= 0f)
        {
            ShowTipsNow();
            return;
        }

        _tipDelayTween = CreateTween();
        _tipDelayTween.TweenInterval(TipDelaySec);
        _tipDelayTween.TweenCallback(Callable.From(ShowTipsNow));
    }

    private void CancelTipDelay()
    {
        _tipDelayTween?.Kill();
        _tipDelayTween = null;
    }

    private void ShowTipsNow()
    {
        if (!_hoveredOrFocused || _keywordTips.Count == 0)
        {
            return;
        }

        var service = KeywordTipService.Current;
        if (service == null)
        {
            AppLog.Warning("BaseKemoButton: KeywordTipService.Current 为空，无法显示词条提示。", "BaseKemoButton");
            return;
        }

        service.ShowTips(this, _keywordTips, PreferTipSide);
    }

    #endregion
}