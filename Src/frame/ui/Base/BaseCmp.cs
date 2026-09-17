using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 组件基类（属 BaseUI 家族，因此自动获得 <see cref="BaseUI.Binder"/> 与统一离场解绑）。
/// </summary>
public abstract partial class BaseCmp : BaseUI
{
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
    }
}