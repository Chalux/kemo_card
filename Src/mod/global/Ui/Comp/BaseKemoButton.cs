using Godot;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Global.Ui.Tip;
using KemoCard.Frame.Logging;

using KemoCard.Frame.UI;
namespace KemoCard.Mod.Global.Ui.Comp;

/// <summary>
/// 项目按钮基类：统一悬停/按压/禁用动效与词条提示。
/// 所有界面按钮必须挂载本脚本（原生 Button 一律禁止），主按钮可勾选 <see cref="Primary"/> 启用琥珀金变体。
/// </summary>
public partial class BaseKemoButton : Button
{
    [Export] public bool EnableHoverScale { get; set; } = true;
    [Export] public bool EnablePressScale { get; set; } = true;
    [Export] public bool EnableHoverBrighten { get; set; } = true;
    [Export] public float HoverScale { get; set; } = 1.06f;
    [Export] public float PressScale { get; set; } = 0.94f;
    /// <summary>悬停进入时长：Back 缓动带轻微过冲，产生“弹起”手感。</summary>
    [Export] public float HoverDuration { get; set; } = 0.14f;
    /// <summary>按压时长：明显短于悬停，让按下反馈比悬停更“脆”。</summary>
    [Export] public float PressDuration { get; set; } = 0.06f;
    /// <summary>离开时长。</summary>
    [Export] public float LeaveDuration { get; set; } = 0.1f;
    /// <summary>悬停提亮强度（modulate RGB 增量）。</summary>
    [Export] public float HoverBrightness { get; set; } = 0.06f;
    /// <summary>禁用态不透明度。</summary>
    [Export] public float DisabledAlpha { get; set; } = 0.55f;
    [Export] public float TipDelaySec { get; set; } = 0.15f;
    [Export] public TipSide PreferTipSide { get; set; } = TipSide.Right;
    /// <summary>主按钮（琥珀金强调样式）：挂载后应用全局主题的 Primary 变体。</summary>
    [Export] public bool Primary { get; set; }

    /// <summary>无参词条 id 列表（编辑器快捷配置）。</summary>
    [Export] public string[] KeywordIds { get; set; } = [];

    private Tween? _scaleTween;
    private Tween? _modulateTween;
    private Tween? _tipDelayTween;
    private bool _hoveredOrFocused;
    private bool _pressedVisual;
    private bool _disabledVisual;
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
        if (Primary)
        {
            ThemeTypeVariation = KemoTheme.PrimaryVariation;
        }

        // 场景里初始即禁用的按钮（如故事选择的确认键）没有状态变化信号，只能在此初始化视觉。
        _disabledVisual = Disabled;
        if (Disabled)
        {
            Modulate = new Color(1f, 1f, 1f, DisabledAlpha);
        }
    }

    /// <summary>
    /// 禁用态没有变更信号，用每帧一次的布尔比较兜底（开销可忽略）；
    /// 恢复启用时交还悬停/按压动效。
    /// </summary>
    public override void _Process(double delta)
    {
        if (Disabled == _disabledVisual)
        {
            return;
        }

        _disabledVisual = Disabled;
        if (Disabled)
        {
            KillVisualTweens();
            Scale = Vector2.One;
            Modulate = new Color(1f, 1f, 1f, DisabledAlpha);
        }
        else
        {
            Modulate = Colors.White;
            RefreshVisuals();
        }
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
        RefreshVisuals();
    }

    private void OnButtonUp()
    {
        _pressedVisual = false;
        RefreshVisuals();
    }

    private void EnterHighlight()
    {
        _hoveredOrFocused = true;
        RefreshVisuals();
        ScheduleShowTips();
    }

    private void LeaveHighlight()
    {
        _hoveredOrFocused = false;
        _pressedVisual = false;
        RefreshVisuals();
        CancelTipDelay();
        KeywordTipService.Current?.HideTips(this);
    }

    #endregion

    #region 动画

    private void RefreshVisuals()
    {
        if (Disabled)
        {
            return; // 禁用态视觉由 _Process 轮询单独接管
        }

        float target;
        float duration;
        Tween.TransitionType trans;
        Tween.EaseType ease;
        if (_pressedVisual && EnablePressScale)
        {
            target = PressScale;
            duration = PressDuration;
            trans = Tween.TransitionType.Quad;
            ease = Tween.EaseType.In;
        }
        else if (_hoveredOrFocused && EnableHoverScale)
        {
            target = HoverScale;
            duration = HoverDuration;
            trans = Tween.TransitionType.Back;
            ease = Tween.EaseType.Out;
        }
        else
        {
            target = 1f;
            duration = LeaveDuration;
            trans = Tween.TransitionType.Sine;
            ease = Tween.EaseType.Out;
        }

        AnimateScale(target, duration, trans, ease);

        var bright = _hoveredOrFocused && !_pressedVisual && EnableHoverBrighten
            ? 1f + HoverBrightness
            : 1f;
        AnimateModulate(new Color(bright, bright, bright), duration);
    }

    private void AnimateScale(float target, float duration, Tween.TransitionType trans, Tween.EaseType ease)
    {
        _scaleTween?.Kill();
        if (duration <= 0f)
        {
            Scale = new Vector2(target, target);
            return;
        }

        _scaleTween = CreateTween();
        _scaleTween.TweenProperty(this, "scale", new Vector2(target, target), duration)
            .SetTrans(trans)
            .SetEase(ease);
    }

    private void AnimateModulate(Color target, float duration)
    {
        _modulateTween?.Kill();
        if (duration <= 0f)
        {
            Modulate = target;
            return;
        }

        _modulateTween = CreateTween();
        _modulateTween.TweenProperty(this, "modulate", target, duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    private void KillVisualTweens()
    {
        _scaleTween?.Kill();
        _scaleTween = null;
        _modulateTween?.Kill();
        _modulateTween = null;
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
