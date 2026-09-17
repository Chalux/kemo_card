using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// 界面 / 组件的订阅登记簿。**所有**订阅（Godot 信号、事件总线、静态门面事件）都必须经此登记，
/// 由框架在离场时一次解绑。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它：</b>此前订阅分散在 <c>BaseUI</c> 的 <c>OnClicks</c>/<c>Bind</c> 与各组件手搓的
/// <c>_bound</c> 守卫两套实现里，裸 <c>+=</c> 不在覆盖范围内；界面各自写的 <c>_eventsBound</c> 守卫一旦忘记复位，
/// 缓存重开时 <c>InitEvent</c> 提前返回，已解绑的控件就再也不会重新绑定（见 ui-mod-binding 规格 §2.2）。
/// 账本式登记不需要守卫，因此该缺陷类别从根上消失。</para>
/// <para><b>非 Godot 依赖：</b>本类刻意不引用任何 Godot 类型，因此可被现有「不依赖场景树」的测试直接覆盖。
/// Godot 信号的语义化糖见 <see cref="BindingScopeSignals"/>。</para>
/// <para><b>可重入登记：</b><see cref="UnbindAll"/> 之后本对象<b>仍可继续登记</b>。关闭走缓存时节点只是
/// <c>RemoveChild</c>（未释放），重开会再次 <c>InitEvent</c> 重新订阅；若解绑后禁止订阅，重开必然失败。</para>
/// </remarks>
public sealed class BindingScope
{
    private readonly List<Action> _unbinders = [];

    /// <summary>是否还有已登记的订阅。</summary>
    public bool HasBindings => _unbinders.Count > 0;

    /// <summary>已登记的订阅数量（供诊断与测试断言订阅不随开关次数增长）。</summary>
    public int Count => _unbinders.Count;

    /// <summary>只登记解绑动作，不立即订阅。</summary>
    public void Add(Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(unsubscribe);
        _unbinders.Add(unsubscribe);
    }

    /// <summary>登记并立即订阅。</summary>
    public void Bind(Action subscribe, Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        subscribe();
        _unbinders.Add(unsubscribe);
    }

    /// <summary>
    /// 逆序执行全部解绑并清空账本。可重复调用（账本已空时为无操作）。
    /// </summary>
    /// <remarks>
    /// 逆序是为了让「后建立的依赖先拆」——与构造顺序相反，避免先拆掉被依赖的订阅。
    /// 单个解绑抛异常时记录并继续：一个坏解绑不应阻断其余清理。
    /// </remarks>
    public void UnbindAll()
    {
        for (var i = _unbinders.Count - 1; i >= 0; i--)
        {
            try
            {
                _unbinders[i]();
            }
            catch (Exception ex)
            {
                AppLog.Error($"UI 订阅解绑失败（已继续清理其余订阅）：{ex.Message}", "UI");
            }
        }

        _unbinders.Clear();
    }
}