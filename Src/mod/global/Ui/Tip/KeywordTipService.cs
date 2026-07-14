using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui.Tip;

/// <summary>
/// 顶层词条提示服务：相对锚点显示多条堆叠 tip，支持左右偏好与贴边翻转。
/// </summary>
public partial class KeywordTipService : CanvasLayer
{
    public const string PanelScenePath = "res://Src/mod/global/Ui/Tip/KeywordTipPanel.tscn";

    private const float Gap = 8f;
    private const float StackSeparation = 6f;

    public static KeywordTipService? Current { get; private set; }

    [Export] private Control? _tipRoot;
    [Export] private VBoxContainer? _tipStack;
    [Export] private PackedScene? _panelScene;

    private Control? _currentAnchor;
    private TipSide _pendingPreferSide = TipSide.Right;

    public override void _EnterTree()
    {
        base._EnterTree();
        Current = this;
        Layer = 100;
        EnsurePanelScene();
    }

    public override void _ExitTree()
    {
        DetachAnchorWatcher();
        if (Current == this)
        {
            Current = null;
        }

        base._ExitTree();
    }

    public void ShowTips(Control anchor, IReadOnlyList<KeywordTipRequest> tips, TipSide preferSide = TipSide.Right)
    {
        if (anchor == null || !GodotObject.IsInstanceValid(anchor))
        {
            HideTips();
            return;
        }

        if (tips == null || tips.Count == 0)
        {
            HideTips();
            return;
        }

        if (_tipRoot == null || _tipStack == null)
        {
            AppLog.Warning("KeywordTipService: TipRoot/TipStack 未绑定，无法显示提示。", "Keyword");
            return;
        }

        EnsurePanelScene();
        if (_panelScene == null)
        {
            AppLog.Warning("KeywordTipService: 无法加载 KeywordTipPanel 场景。", "Keyword");
            return;
        }

        DetachAnchorWatcher();
        _currentAnchor = anchor;
        _currentAnchor.TreeExited += OnAnchorTreeExited;

        ClearPanels();

        var catalog = KeywordCatalog.Shared;
        foreach (var request in tips)
        {
            if (!catalog.TryGet(request.KeywordId, out var entry) || entry == null)
            {
                AppLog.Warning($"KeywordTipService: 未知词条 id '{request.KeywordId}'，已跳过。", "Keyword");
                continue;
            }

            var panel = _panelScene.Instantiate<KeywordTipPanel>();
            var title = Localization.Tr(entry.TitleKey);
            var desc = KeywordTextFormatter.ApplyParams(Localization.Tr(entry.DescKey), request.Parameters);
            panel.SetContent(title, desc);
            _tipStack.AddChild(panel);
        }

        if (_tipStack.GetChildCount() == 0)
        {
            HideTips();
            return;
        }

        _tipRoot.Visible = true;
        _tipStack.AddThemeConstantOverride("separation", (int)StackSeparation);
        _pendingPreferSide = preferSide;
        CallDeferred(MethodName.DeferredPositionStack);
    }

    private void DeferredPositionStack() => PositionStack(_pendingPreferSide);

    public void HideTips()
    {
        DetachAnchorWatcher();
        ClearPanels();
        if (_tipRoot != null)
        {
            _tipRoot.Visible = false;
        }
    }

    public void HideTips(Control anchor)
    {
        if (_currentAnchor == null || anchor == null)
        {
            return;
        }

        if (_currentAnchor == anchor)
        {
            HideTips();
        }
    }

    #region 定位

    private void PositionStack(TipSide preferSide)
    {
        if (_tipRoot == null || _tipStack == null || _currentAnchor == null
            || !GodotObject.IsInstanceValid(_currentAnchor))
        {
            return;
        }

        _tipStack.ResetSize();
        var tipSize = _tipStack.GetCombinedMinimumSize();
        if (tipSize.X <= 0f || tipSize.Y <= 0f)
        {
            tipSize = _tipStack.Size;
        }

        var viewport = GetViewport()?.GetVisibleRect() ?? new Rect2(Vector2.Zero, new Vector2(1920, 1080));
        var anchorRect = _currentAnchor.GetGlobalRect();

        var side = preferSide;
        if (!FitsHorizontally(side, anchorRect, tipSize, viewport))
        {
            var flipped = side == TipSide.Right ? TipSide.Left : TipSide.Right;
            if (FitsHorizontally(flipped, anchorRect, tipSize, viewport))
            {
                side = flipped;
            }
        }

        var pos = ComputePosition(side, anchorRect, tipSize);
        pos.X = Mathf.Clamp(pos.X, viewport.Position.X, viewport.End.X - tipSize.X);
        pos.Y = Mathf.Clamp(pos.Y, viewport.Position.Y, viewport.End.Y - tipSize.Y);

        _tipRoot.GlobalPosition = pos;
        _tipRoot.Size = tipSize;
    }

    private static bool FitsHorizontally(TipSide side, Rect2 anchorRect, Vector2 tipSize, Rect2 viewport)
    {
        var x = side == TipSide.Right
            ? anchorRect.End.X + Gap
            : anchorRect.Position.X - tipSize.X - Gap;
        return x >= viewport.Position.X && x + tipSize.X <= viewport.End.X;
    }

    private static Vector2 ComputePosition(TipSide side, Rect2 anchorRect, Vector2 tipSize)
    {
        var x = side == TipSide.Right
            ? anchorRect.End.X + Gap
            : anchorRect.Position.X - tipSize.X - Gap;
        var y = anchorRect.Position.Y;
        return new Vector2(x, y);
    }

    #endregion

    #region 内部

    private void EnsurePanelScene()
    {
        if (_panelScene != null)
        {
            return;
        }

        if (ResourceLoader.Exists(PanelScenePath))
        {
            _panelScene = ResourceLoader.Load<PackedScene>(PanelScenePath);
        }
    }

    private void ClearPanels()
    {
        if (_tipStack == null)
        {
            return;
        }

        foreach (var child in _tipStack.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void DetachAnchorWatcher()
    {
        if (_currentAnchor != null && GodotObject.IsInstanceValid(_currentAnchor))
        {
            _currentAnchor.TreeExited -= OnAnchorTreeExited;
        }

        _currentAnchor = null;
    }

    private void OnAnchorTreeExited()
    {
        _currentAnchor = null;
        ClearPanels();
        if (_tipRoot != null)
        {
            _tipRoot.Visible = false;
        }
    }

    #endregion
}
