using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 组件基类（属 BaseUI 家族，因此自动获得 <see cref="BaseUI.Binder"/> 与统一离场解绑）。
/// </summary>
public abstract partial class BaseCmp : BaseUI
{
    /// <summary>
    /// 组件装饰器订阅的独立账本：窗口已挂载时再次 Open 不会重走组件的
    /// <c>_EnterTree</c>，由所属窗口在 <c>InitEvent</c> 后调用
    /// <see cref="RebindDecoratedEvents"/> 切到当前功能总线。
    /// </summary>
    private readonly BindingScope _decoratedBinder = new();

    public override EUIType UIType => EUIType.Cmp;
    public override string UIId => GetType().Name;
    public override string UIDir => string.Empty;

    /// <summary>
    /// 每次进树都会调用（首次进树与「缓存取回后重新挂载」都算），用于重新登记订阅。
    /// </summary>
    /// <remarks>
    /// <para><b>必须挂在 <c>_EnterTree</c> 而不是 <c>_Ready</c>：</b>Godot 的 <c>_Ready</c>
    /// 「每个节点只会被调用一次」，界面进缓存走的是 <c>RemoveChild</c>，重开时 <c>AddChild</c>
    /// <b>不会</b>再触发 <c>_Ready</c>（除非显式 <c>RequestReady()</c>，全仓无此调用）。
    /// 挂在 <c>_Ready</c> 上会导致组件订阅在上一次离场时被 <c>Binder</c> 解绑后永不再登记。</para>
    /// <para>离场解绑由 <see cref="BaseUI.Binder"/> 负责，这里无需（也不应）自行复位任何守卫。</para>
    /// </remarks>
    protected virtual void InitEvent() { }

    public override void _EnterTree()
    {
        base._EnterTree();
        InitEvent();
        // 组件没有自己的 UIVo：沿父链取所属界面，把装饰器订阅挂到该界面的总线上。
        RebindDecoratedEvents(BindingScopeEvents.FindOwnerVo(this));
    }

    /// <summary>
    /// 仅重建装饰器订阅（不动 <see cref="BaseUI.Binder"/> 里的其他订阅）。
    /// 由组件进树或所属窗口再次 InitEvent 时调用，确保 QuickLoad 换 RunController 后仍监听新总线。
    /// </summary>
    internal void RebindDecoratedEvents(UIVo? ownerVo)
    {
        _decoratedBinder.UnbindAll();
        _decoratedBinder.BindOwnerDecorated(ownerVo, this);
    }

    /// <summary>
    /// 所属功能总线可能已切换（例如 QuickLoad 替换 RunController）时的重绑钩子。
    /// 普通 <see cref="BaseUI.Binder"/> 总线订阅不会自动迁移；需要跟随 owner 重建的组件
    /// 可 override 本方法（在确认 <see cref="InitEvent"/> 可重复执行后，可先 Binder.UnbindAll() 再 InitEvent()）。
    /// </summary>
    protected virtual void OnOwnerRebind() { }

    internal void RebindForOwnerChange(UIVo? ownerVo)
    {
        RebindDecoratedEvents(ownerVo);
        OnOwnerRebind();
    }

    protected sealed override void OnExitTreeComplete() => _decoratedBinder.UnbindAll();
}