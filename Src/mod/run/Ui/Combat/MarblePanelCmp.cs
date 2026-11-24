using System.Globalization;
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Marble;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 出牌阶段的弹珠盘面面板：标题 + 本轮统计 + 常驻盘面 + 待发队列（顺序与属性）。
/// 缩略实例与大盘预览实例共用同一场景：<see cref="Compact"/> 控制盘面高度与队列行数，
/// <see cref="TitleKey"/> 控制标题；右上按钮通过 <see cref="ToggleRequested"/> 交给战斗界面切换。
/// </summary>
public partial class MarblePanelCmp : BaseCmp
{
    [Export] private MarbleBoardCmp? _board;
    [Export] private Label? _lblTitle;
    [Export] private Label? _lblStats;
    [Export] private Label? _lblQueue;
    [Export] private ScrollContainer? _queueScroll;
    [Export] private Button? _btnToggle;
    [Export] private MarginContainer? _margin;

    /// <summary>缩略模式：盘面较矮、队列最多两行；大盘预览取 false。</summary>
    [Export] public bool Compact { get; set; } = true;

    /// <summary>标题翻译键；大盘预览实例在场景里覆盖。</summary>
    [Export] public string TitleKey { get; set; } = "UI_MARBLE_PANEL_TITLE";

    /// <summary>右上按钮回调：缩略面板用于打开预览，预览面板用于关闭。</summary>
    public Action? ToggleRequested { get; set; }

    public MarbleBoardCmp? Board => _board;

    private bool _interactive;
    private bool _inputLocked;

    /// <summary>弹珠阶段主盘面打开交互（左键发射 / 右键取消）；预览与缩略面板恒为只读。</summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        ApplyInputState();
    }

    /// <summary>表现播放期间禁掉盘面输入（不改变盘面的尺寸与只读属性）。</summary>
    public void SetInputLocked(bool locked)
    {
        _inputLocked = locked;
        ApplyInputState();
    }

    /// <summary>主盘面不需要「放大 / 关闭」按钮（本身已是大盘）；预览实例保持可见。</summary>
    public void SetToggleVisible(bool visible)
    {
        if (_btnToggle != null)
            _btnToggle.Visible = visible;
    }

    private void ApplyInputState()
    {
        if (_board != null)
            _board.Interactive = _interactive && !_inputLocked;
    }

    internal void FocusToggle()
    {
        if (_btnToggle?.IsVisibleInTree() == true)
            _btnToggle.GrabFocus();
    }

    /// <summary>高度跟随内容（同 <see cref="OrbQueueCmp"/>：普通 Control 不聚合子节点最小尺寸）。</summary>
    public override Vector2 _GetMinimumSize() => _margin?.GetCombinedMinimumSize() ?? base._GetMinimumSize();

    protected override void InitEvent()
    {
        if (_btnToggle != null)
            OnClicks(_btnToggle, () => ToggleRequested?.Invoke());
    }

    protected override void OnReady()
    {
        ApplyCompact();
        if (_lblTitle != null && !string.IsNullOrWhiteSpace(TitleKey))
            _lblTitle.Text = Localization.Tr(TitleKey);
    }

    public void SetCompact(bool compact)
    {
        Compact = compact;
        ApplyCompact();
    }

    /// <summary>
    /// 按模拟器状态刷新盘面 / 统计 / 队列。传 null 时清空数据（无战斗时占位）。
    /// 队列文案按 FIFO 顺序逐行展示点数与半径；缩略模式最多两行，完整列表在大盘预览查看。
    /// </summary>
    public void Bind(MarbleRuntime? runtime)
    {
        _board?.Bind(runtime);
        if (runtime is null)
        {
            if (_lblStats != null)
                _lblStats.Text = "";
            if (_lblQueue != null)
                _lblQueue.Text = "";
            return;
        }

        if (_lblStats != null)
        {
            _lblStats.Text = string.Format(
                CultureInfo.InvariantCulture,
                Localization.Tr("UI_MARBLE_STATS"),
                runtime.Count,
                runtime.Queue.Count,
                runtime.ActiveBalls.Count,
                Math.Min(runtime.Config.MaxActiveBalls, MarblePhysicsConfig.MaxActiveBallsLimit));
        }

        if (_lblQueue != null)
            _lblQueue.Text = MarbleQueueText.Format(runtime.Queue, Localization.Tr);
    }

    private void ApplyCompact()
    {
        if (_board != null)
        {
            _board.CustomMinimumSize = new Vector2(0f, Compact ? 100f : 420f);
            _board.MouseFilter = Compact ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        }
        if (_lblQueue != null)
            _lblQueue.MaxLinesVisible = Compact ? 2 : -1;
        if (_queueScroll != null)
        {
            _queueScroll.CustomMinimumSize = new Vector2(0, Compact ? 36 : 96);
            _queueScroll.VerticalScrollMode = Compact ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto;
        }

        if (_btnToggle != null)
            _btnToggle.Text = Localization.Tr(Compact ? "UI_MARBLE_PREVIEW_OPEN" : "UI_MARBLE_PREVIEW_CLOSE");
    }
}
