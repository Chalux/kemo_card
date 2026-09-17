using Godot;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Ui.Tip;
using KemoCard.Frame.Logging;

using KemoCard.Frame.UI;
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
    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑（见 ui-mod-binding 规格 §4.3）。</summary>
    private readonly BindingScope _binder = new();

    /// <summary>
    /// 订阅登记挂在 <c>_EnterTree</c>：Godot 的 <c>_Ready</c> 每个节点只调用一次，
    /// 界面进缓存走 <c>RemoveChild</c>，重开时 <c>AddChild</c> 不会再触发 <c>_Ready</c>，
    /// 挂在 <c>_Ready</c> 上会让悬停/词条在缓存重开后永久失效。
    /// </summary>
    public override void _EnterTree()
    {
        base._EnterTree();

        _binder.OnResized(this, OnResized);
        _binder.OnMouseEnterExit(this, OnMouseEntered, OnMouseExited);
        _binder.Bind(() => FocusEntered += OnFocusEntered, () => FocusEntered -= OnFocusEntered);
        _binder.Bind(() => FocusExited += OnFocusExited, () => FocusExited -= OnFocusExited);
        _binder.Bind(() => ButtonDown += OnButtonDown, () => ButtonDown -= OnButtonDown);
        _binder.Bind(() => ButtonUp += OnButtonUp, () => ButtonUp -= OnButtonUp);
    }

    public override void _Ready()
    {
        base._Ready();
        PivotOffset = Size / 2f;
        SyncKeywordTipsFromIds();
    }

    /// <summary>框架唯一离场入口。sealed：子类不得 override —— 请改 override OnExitTree。</summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();
        _binder.UnbindAll();
        base._ExitTree();
    }

    /// <summary>框架级离场生命周期：只做非订阅类清理。</summary>
    protected virtual void OnExitTree()
    {
        CancelTipDelay();
        KeywordTipService.Current?.HideTips(this);
    }

    /// <summary>以完整请求列表设置词条（含命名参数）。</summary>
    public void SetKeywordTips(IReadOnlyList<KeywordTipRequest>? tips)
    {
        _keywordTips = tips ?? Array.Empty<KeywordTipRequest>();
    }

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