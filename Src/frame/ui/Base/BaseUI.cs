using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 基础 UI 类：所有界面都应继承自此类（无法继承的节点类型见 ui-mod-binding 规格 §4.3 的 6 行模式）。
/// </summary>
/// <remarks>
/// 订阅生命周期由框架统一负责：子类把订阅登记进 <see cref="Binder"/>，框架在离场时一次解绑。
/// 子类<b>不得</b> override 引擎的 <c>_ExitTree</c>，请改 override <see cref="OnExitTree"/>。
/// </remarks>
public abstract partial class BaseUI : Control, IUIMeta
{
    /// <summary>
    /// 订阅登记簿。**任何**订阅（Godot 信号、事件总线、静态门面事件）都必须经此登记。
    /// </summary>
    protected BindingScope Binder { get; } = new();

    /// <summary>
    /// UI 的 ID
    /// </summary>
    public abstract string UIId { get; }
    /// <summary>
    /// UI 的目录
    /// </summary>
    public abstract string UIDir { get; }
    /// <summary>
    /// UI 的类型
    /// </summary>
    public abstract EUIType UIType { get; }
    /// <summary>
    /// 基础打开选项
    /// </summary>
    public virtual UIOpenOpt? BaseOpenOpt => null;
    /// <summary>
    /// 打开选项
    /// </summary>
    public virtual UIOpenOpt? OpenOpt => null;

    /// <summary>
    /// 获取 UI 的场景路径
    /// </summary>
    public string GetScenePath()
    {
        return $"res://{UIDir}/{UIId}.tscn";
    }

    public override void _Ready()
    {
        OnReady();
    }

    /// <summary>
    /// 框架唯一的离场入口。<b>sealed：子类不得 override</b> — 请改 override <see cref="OnExitTree"/>。
    /// </summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();                       // 1. 子类补充清理（此时订阅仍有效）
        Binder.UnbindAll();                 // 2. 框架保证解绑（子类忘了也解）
        GlobalEvents.Bus.OffCaller(this);   // 3. 跨功能总线按 caller 清理
        base._ExitTree();
    }

    protected virtual void OnReady()
    {
    }

    /// <summary>
    /// 框架级离场生命周期：子类只做<b>非订阅类</b>清理。
    /// 订阅一律走 <see cref="Binder"/>，否则会漏出框架之外（ui-mod-binding 规格 §2.2 缺陷的成因）。
    /// </summary>
    protected virtual void OnExitTree()
    {
    }

    #region 订阅登记（转发到 Binder）

    /// <summary>登记并立即订阅；离场时自动解绑。</summary>
    protected void Bind(Action subscribe, Action unsubscribe) => Binder.Bind(subscribe, unsubscribe);

    /// <summary>批量登记点击回调。</summary>
    protected void OnClicks(params (Node node, Action action)[] clicks)
    {
        foreach (var (node, action) in clicks)
        {
            OnClicks(node, action);
        }
    }

    /// <summary>
    /// 登记「左键点击」回调：<see cref="BaseButton"/> 走 <c>Pressed</c>，其余 <see cref="Control"/> 走
    /// <c>GuiInput</c> 左键判定。
    /// </summary>
    /// <remarks>
    /// 与旧实现的行为差异：同一节点重复登记会<b>累加</b>监听（旧实现是替换）。
    /// 现有调用点都在 <c>InitEvent</c> 内各登记一次，且离场会整体解绑，因此不会累积。
    /// </remarks>
    protected void OnClicks(Node node, Action callback)
    {
        if (node is BaseButton button)
        {
            Binder.OnPressed(button, callback);
            return;
        }

        if (node is Control control)
        {
            Binder.OnGuiInputLeftClick(control, callback);
        }
    }

    #endregion
}