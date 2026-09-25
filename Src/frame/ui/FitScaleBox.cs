using Godot;

namespace KemoCard.Frame.UI;

/// <summary>
/// 适配缩放盒（单子节点）：子节点正常铺满可用空间；当可用空间小于子节点的需求尺寸时，
/// 子节点保持需求尺寸并整体等比缩小、居中（只缩不放），保证任意宽高比下内容完整可见、不错位。
/// </summary>
/// <remarks>
/// <para>背景：<c>window/stretch/aspect=expand</c> 下可见区只会变大不会变小（设计区恒 ≥ 1280×720），
/// 但容器的最小尺寸之和一旦超过设计区，锚定布局会把整棵子树按最小尺寸撑开并居中溢出屏幕
/// （典型：战斗界面 1360×752 &gt; 1280×720，左右两侧各被裁掉数十像素）。</para>
/// <para>用法：把界面的根容器作为唯一子节点挂进来，安全留白设在 FitScaleBox 自身的锚点偏移上；
/// 子节点锚点由本组件在运行时接管。空间富余时行为与全屏锚定一致，不影响既有铺满布局。</para>
/// <para>重排时机：自身 <c>Resized</c>、子节点 <c>MinimumSizeChanged</c>（如战斗中动态增删敌人改变了
/// 需求尺寸）。计算在帧末延迟执行，等容器排序完成后再取值。</para>
/// </remarks>
public partial class FitScaleBox : Control
{
    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑（见 ui-mod-binding 规格 §4.3）。</summary>
    private readonly BindingScope _binder = new();

    private Control? _content;
    private bool _relayoutQueued;

    public override void _EnterTree()
    {
        base._EnterTree();

        // 界面每次入树（含进缓存后重开）都要重新登记：UnbindAll 已在上次离场清空账簿。
        _binder.OnResized(this, QueueRelayout);
        BindContent();
    }

    /// <summary>框架唯一离场入口。sealed：子类不得 override —— 请改 override OnExitTree。</summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();
        _binder.UnbindAll();
        base._ExitTree();
    }

    /// <summary>框架级离场生命周期：只做非订阅类清理（子节点引用下次入树会重建）。</summary>
    protected virtual void OnExitTree() => _content = null;

    #region 内部

    private void BindContent()
    {
        _content = GetChildCount() > 0 ? GetChild(0) as Control : null;
        if (_content is null)
        {
            return;
        }

        // 锚点由本组件接管：直接写 Size/Position，锚点必须全部归零，否则锚定布局会回写尺寸与缩放打架。
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);

        // 闭包捕获局部引用而不是字段：OnExitTree 会把 _content 置空，而解绑动作在它之后执行——
        // 读字段会拿到 null，导致旧的订阅漏解绑（缓存重开时重复订阅）。
        var content = _content;
        _binder.Bind(
            () => content.MinimumSizeChanged += QueueRelayout,
            () =>
            {
                if (GodotObject.IsInstanceValid(content))
                {
                    content.MinimumSizeChanged -= QueueRelayout;
                }
            });
        QueueRelayout();
    }

    private void QueueRelayout()
    {
        if (_relayoutQueued || !IsInsideTree())
        {
            return;
        }

        _relayoutQueued = true;
        CallDeferred(MethodName.DeferredRelayout);
    }

    private void DeferredRelayout()
    {
        _relayoutQueued = false;
        if (_content is null || !GodotObject.IsInstanceValid(_content))
        {
            return;
        }

        var available = Size;
        var (childSize, scale) = FitScaleMath.Compute(available, _content.GetCombinedMinimumSize());

        // 仅在变化时写入：子节点尺寸回写可能再次触发 MinimumSizeChanged（如自动换行 Label），
        // 无条件赋值会形成「重排 → 尺寸变化 → 重排」的自我循环。
        if (!_content.Size.IsEqualApprox(childSize))
        {
            _content.Size = childSize;
        }

        var scaleVec = new Vector2(scale, scale);
        if (!_content.Scale.IsEqualApprox(scaleVec))
        {
            _content.Scale = scaleVec;
        }

        var position = (available - childSize * scale) * 0.5f;
        if (!_content.Position.IsEqualApprox(position))
        {
            _content.Position = position;
        }
    }

    #endregion
}