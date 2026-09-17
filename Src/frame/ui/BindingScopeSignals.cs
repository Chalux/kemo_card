using Godot;

namespace KemoCard.Frame.UI;

/// <summary>
/// <see cref="BindingScope"/> 的 Godot 信号语义化糖。
/// </summary>
/// <remarks>
/// <para>糖挂在 <see cref="BindingScope"/> 上而<b>不是</b>基类上，是因为宿主节点无法共享基类：
/// <c>BaseUI</c> 是 <c>Control</c>，而 <c>BaseKemoButton : Button</c>、<c>VirtualList : Control</c>、
/// <c>KeywordTipService : CanvasLayer</c> 等受 Godot C# 继承限制无法复用 <c>BaseUI</c>。
/// 用扩展方法即可让两类宿主拥有**完全相同**的订阅写法，替换掉此前约 40 处裸 <c>+=</c>。</para>
/// <para>所有方法都等价于 <c>Bind(() =&gt; signal += handler, () =&gt; signal -= handler)</c>，
/// 因此离场时会被 <see cref="BindingScope.UnbindAll"/> 统一解绑。解绑前会检查节点是否仍然有效
/// （沿用旧 <c>ClearLifeCycle</c> 的 <c>IsInstanceValid</c> 保护：子节点可能先于父界面被释放）。</para>
/// <para>带参数的信号在 Godot 4.6 里是**专用委托类型**（如
/// <c>OptionButton.ItemSelectedEventHandler</c>）而非 <c>Action&lt;long&gt;</c>，
/// 故这些重载直接接收 Godot 委托，避免多一层包装与装箱。</para>
/// </remarks>
public static class BindingScopeSignals
{
    /// <summary>按钮按下。</summary>
    public static void OnPressed(this BindingScope scope, BaseButton button, Action handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => button.Pressed += handler, Guarded(button, () => button.Pressed -= handler));
    }

    /// <summary>下拉框选中项（参数为索引）。</summary>
    public static void OnItemSelected(
        this BindingScope scope,
        OptionButton option,
        OptionButton.ItemSelectedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => option.ItemSelected += handler, Guarded(option, () => option.ItemSelected -= handler));
    }

    /// <summary>输入框提交（回车）。</summary>
    public static void OnTextSubmitted(
        this BindingScope scope,
        LineEdit edit,
        LineEdit.TextSubmittedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => edit.TextSubmitted += handler, Guarded(edit, () => edit.TextSubmitted -= handler));
    }

    /// <summary>输入框文本变化。</summary>
    public static void OnTextChanged(
        this BindingScope scope,
        LineEdit edit,
        LineEdit.TextChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => edit.TextChanged += handler, Guarded(edit, () => edit.TextChanged -= handler));
    }

    /// <summary>数值控件（Slider / SpinBox / ProgressBar 等）值变化。</summary>
    public static void OnValueChanged(
        this BindingScope scope,
        Godot.Range range,
        Godot.Range.ValueChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => range.ValueChanged += handler, Guarded(range, () => range.ValueChanged -= handler));
    }

    /// <summary>Tab 切换（参数为页索引）。</summary>
    public static void OnTabChanged(
        this BindingScope scope,
        TabContainer tabs,
        TabContainer.TabChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => tabs.TabChanged += handler, Guarded(tabs, () => tabs.TabChanged -= handler));
    }

    /// <summary>列表项点击。</summary>
    public static void OnItemClicked(
        this BindingScope scope,
        ItemList list,
        ItemList.ItemClickedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => list.ItemClicked += handler, Guarded(list, () => list.ItemClicked -= handler));
    }

    /// <summary>列表项选中（参数为索引）。</summary>
    public static void OnItemSelected(
        this BindingScope scope,
        ItemList list,
        ItemList.ItemSelectedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => list.ItemSelected += handler, Guarded(list, () => list.ItemSelected -= handler));
    }

    /// <summary>树节点点击。</summary>
    public static void OnTreeItemSelected(this BindingScope scope, Tree tree, Action handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => tree.ItemSelected += handler, Guarded(tree, () => tree.ItemSelected -= handler));
    }

    /// <summary>鼠标移入 / 移出（成对登记，保证两者一起被解绑）。</summary>
    public static void OnMouseEnterExit(this BindingScope scope, Control control, Action enter, Action exit)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(enter);
        ArgumentNullException.ThrowIfNull(exit);
        scope.Bind(() => control.MouseEntered += enter, Guarded(control, () => control.MouseEntered -= enter));
        scope.Bind(() => control.MouseExited += exit, Guarded(control, () => control.MouseExited -= exit));
    }

    /// <summary>尺寸变化。</summary>
    public static void OnResized(this BindingScope scope, Control control, Action handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(handler);
        scope.Bind(() => control.Resized += handler, Guarded(control, () => control.Resized -= handler));
    }

    /// <summary>焦点变化。</summary>
    public static void OnFocusChanged(this BindingScope scope, Control control, Action<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(handler);

        // 必须用局部函数而不是两个内联 lambda：内联写法会生成两个不同的委托方法，
        // -= 与 += 不匹配，解绑会静默失效。
        void OnFocusEntered() => handler(true);
        void OnFocusExited() => handler(false);

        scope.Bind(
            () => control.FocusEntered += OnFocusEntered,
            Guarded(control, () => control.FocusEntered -= OnFocusEntered));
        scope.Bind(
            () => control.FocusExited += OnFocusExited,
            Guarded(control, () => control.FocusExited -= OnFocusExited));
    }

    /// <summary>鼠标按下（等价于 <c>BaseUI.OnClicks</c> 的非 Button 分支）。</summary>
    public static void OnGuiInputLeftClick(this BindingScope scope, Control control, Action handler)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(handler);

        void InputHandler(InputEvent @event)
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                handler();
            }
        }

        scope.Bind(() => control.GuiInput += InputHandler, Guarded(control, () => control.GuiInput -= InputHandler));
    }

    /// <summary>
    /// 兜底：任意「增减委托」配对的 Godot 信号（含需要额外判断的委托，如 <c>Node.TreeExited</c>）。
    /// </summary>
    /// <remarks>此重载不知道宿主节点，因此<b>不做</b> <c>IsInstanceValid</c> 保护；调用方自行在解绑动作里判断。</remarks>
    public static void OnSignal<THandler>(
        this BindingScope scope,
        THandler handler,
        Action<THandler> add,
        Action<THandler> remove) where THandler : Delegate
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        scope.Bind(() => add(handler), () => remove(handler));
    }

    /// <summary>
    /// 包一层节点有效性检查：子节点可能先于父界面被释放，此时对已失效节点做 <c>-=</c> 会抛异常。
    /// </summary>
    private static Action Guarded(Node node, Action remove) =>
        () =>
        {
            if (GodotObject.IsInstanceValid(node))
            {
                remove();
            }
        };
}