using KemoCard.Frame.UI;
namespace KemoCard.Mod.Global.Ui;

using Godot;
using System;
using System.Collections.Generic;
using KemoCard.Frame.Logging;

/// <summary>
/// 虚拟列表组件，参考 LayaAir List 和 FairyGUI GList 的虚拟列表设计。
/// 只渲染可见区域的列表项，通过对象池复用节点，支持大量数据的流畅滚动。
/// </summary>
public partial class VirtualList : Control
{
    #region Export 字段

    /// <summary>
    /// 滚动区域，需在场景中预先放置并拖入引用
    /// </summary>
    [Export] public ScrollContainer? ScrollArea { get; set; }

    /// <summary>
    /// 列表项模板场景，根节点必须为 Control 类型
    /// </summary>
    [Export] public PackedScene? ItemTemplate { get; set; }

    /// <summary>
    /// 列表项在滚动方向上的**步长**（相邻两项的间距基准），而不是条目自身的尺寸。
    /// </summary>
    /// <remarks>
    /// 条目尺寸一律由预制体（<see cref="ItemTemplate"/>）决定，列表不会拉伸它：
    /// 占位卡片类预制体宽度固定，若按列表宽度拉伸会导致立绘/卡面变形。
    /// 因此本值应 ≥ 预制体在滚动方向上的高度（垂直列表）/宽度（水平列表），
    /// 差值即为两项之间的留白（叠加 <see cref="Spacing"/>）。
    /// </remarks>
    [Export] public float ItemSize { get; set; } = 48f;

    /// <summary>
    /// 列表项之间的间距
    /// </summary>
    [Export] public float Spacing { get; set; } = 2f;

    /// <summary>
    /// 是否垂直滚动（false 为水平滚动）
    /// </summary>
    [Export] public bool IsVertical { get; set; } = true;

    /// <summary>
    /// 是否把条目拉伸到列表横轴尺寸（默认 <c>false</c>）。
    /// </summary>
    /// <remarks>
    /// 仅在"整行文本条"这类本来就该铺满宽度的条目上打开；
    /// 固定尺寸的立绘/卡面必须保持关闭，否则会被拉变形。
    /// 流动布局（<see cref="FlowLayout"/>）下无意义，忽略。
    /// </remarks>
    [Export] public bool StretchItemAcrossAxis { get; set; }

    /// <summary>
    /// 是否启用流动（换行）布局：条目沿主轴排满一行/列后自动换到下一行/列。
    /// </summary>
    /// <remarks>
    /// <para>主轴与滚动轴的关系与 <see cref="IsVertical"/> 一致：<c>true</c> → 从左到右排满一行后
    /// 换行、竖向滚动；<c>false</c> → 从上到下排满一列后换列、横向滚动。行容量按可视主轴长度现算，
    /// 因此容器变宽/变窄会自动改变每行条目数。</para>
    /// <para>打开后 <see cref="ItemSize"/> 不再参与布局：单元格尺寸改由 <see cref="FlowItemSize"/>
    /// 决定（留空则自动测量一次 <see cref="ItemTemplate"/> 的尺寸），同线间距是
    /// <see cref="Spacing"/>、相邻线间距是 <see cref="LineSpacing"/>。</para>
    /// </remarks>
    [Export] public bool FlowLayout { get; set; }

    /// <summary>
    /// 流动布局的单元格尺寸（条目空间的 宽 × 高）；<see cref="Vector2.Zero"/> = 由模板决定（自动测量）。
    /// </summary>
    /// <remarks>网格类列表建议显式填写：模板尺寸会随主题/字体/立绘比例漂移，写死单元格才能保证
    /// 每行容量稳定、条目间距均匀。</remarks>
    [Export] public Vector2 FlowItemSize { get; set; } = Vector2.Zero;

    /// <summary>流动布局相邻行/列的间距；<c>&lt; 0</c>（默认）表示复用 <see cref="Spacing"/>。</summary>
    [Export] public float LineSpacing { get; set; } = -1f;

    #endregion

    #region 公共属性

    /// <summary>
    /// 列表项渲染回调，参数为 (索引, 列表项节点)
    /// </summary>
    public Action<int, Control>? OnRenderItem { get; set; }

    /// <summary>
    /// 当前列表项总数
    /// </summary>
    public int ItemCount => _itemCount;

    #endregion

    #region 私有字段

    private Control? _itemContainer;

    private readonly List<Control> _activeItems = new();

    private readonly Stack<Control> _itemPool = new();

    private int _itemCount;

    private int _firstVisibleIndex = -1;

    private int _visibleItemCount;

    private double _lastScrollValue;

    private ScrollBar? _scrollBar;

    private bool _initialized;

    /// <summary>流动布局的单元格尺寸缓存（模板自动测量结果；显式配置 <see cref="FlowItemSize"/> 时不使用）。</summary>
    private Vector2 _flowCellSize;

    private bool _flowCellMeasured;

    #endregion

    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑（见 ui-mod-binding 规格 §4.3）。</summary>
    private readonly BindingScope _binder = new();

    #region 生命周期

    /// <summary>
    /// 框架唯一入树入口：<b>订阅登记必须发生在这里</b>（AGENT.md §3 / ui-mod-binding 规格 §12.1）。
    /// Godot 的 <c>_Ready</c> 每个节点只调用一次，而界面进缓存走 <c>RemoveChild</c>、重开走 <c>AddChild</c>，
    /// 不会再触发 <c>_Ready</c>——订阅若挂在 <c>_Ready</c>，第一次离场被 <c>Binder</c> 解绑后就永不重建，
    /// 列表会"一半控件活着、一半死了"（滚动条与 Resized 全失效，<c>Refresh</c>/<c>ScrollTo</c> 静默变空操作）。
    /// </summary>
    public override void _EnterTree()
    {
        base._EnterTree();

        // 重新入树（界面走缓存）时模板/主题尺寸可能已变，丢弃上次的自动测量结果。
        _flowCellMeasured = false;
        EnsureInitialized();
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
        _scrollBar = null;
        // 只把在显示的条目归还池，**刻意不清空池**：这些节点留在容器下，缓存重开时可原样复用；
        // 否则每次重开都要重新 Instantiate + 加载卡面/立绘（十几张卡），把对象池的意义整个抵消。
        // 真正销毁时它们随场景树一起释放（见 ReturnItemToPool 的说明），不会泄漏。
        ClearActiveItems();
        _itemCount = 0;
        _initialized = false;
        // 订阅由框架在 _ExitTree 里 UnbindAll 解绑；"是否已登记"的唯一判据是账簿本身（见 EnsureBound），
        // 这里不再另记一份状态，避免自写守卫与账簿不同步。
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        EnsureBound();
        if (ScrollArea == null)
        {
            return;
        }

        // 复用仍然有效的容器：离场只是 RemoveChild，容器节点（连同池里的条目）并未释放；
        // 每次重入树都新建会导致 ScrollArea 下堆积多个 ItemContainer，滚动范围翻倍。
        if (_itemContainer == null || !GodotObject.IsInstanceValid(_itemContainer))
        {
            _itemContainer = new Control
            {
                Name = "ItemContainer",
                MouseFilter = MouseFilterEnum.Pass
            };
            ScrollArea.AddChild(_itemContainer);
        }

        _initialized = true;
    }

    /// <summary>登记订阅（幂等）。可被 <see cref="_EnterTree"/> 与公开方法共同驱动。</summary>
    private void EnsureBound()
    {
        // 判据是账簿是否为空（AGENT.md §3：不得自写 _eventsBound 类守卫）：UnbindAll 后账簿清空，
        // 重入树时自然重新登记，不需要额外状态跟着一起复位。
        if (_binder.HasBindings) return;

        if (ScrollArea == null)
        {
            AppLog.Error("VirtualList: ScrollArea 未设置", "VirtualList");
            return;
        }

        _scrollBar = IsVertical ? ScrollArea.GetVScrollBar() : ScrollArea.GetHScrollBar();
        if (_scrollBar != null)
        {
            _binder.OnValueChanged(_scrollBar, OnScrollValueChanged);
        }

        // 界面通常在"刚入树、布局尚未跑完"的同帧调用 SetData（宿主 OnOpen），此时可视区尺寸为 0，
        // 只能算出一个可见项；布局完成后必须重算，否则列表会长期只显示第 1 项。
        _binder.OnResized(this, OnViewportResized);
        _binder.OnResized(ScrollArea, OnViewportResized);
    }

    /// <summary>可视区尺寸变化后重算可见范围（尺寸未变时 <see cref="Refresh"/> 内部会自行收敛）。</summary>
    private void OnViewportResized()
    {
        if (_initialized && _itemCount > 0)
        {
            Refresh();
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置列表数据并立即刷新
    /// </summary>
    /// <param name="count">列表项总数</param>
    /// <param name="renderItem">列表项渲染回调，每次项进入可见区域时调用</param>
    public void SetData(int count, Action<int, Control> renderItem)
    {
        EnsureInitialized();
        OnRenderItem = renderItem;
        _itemCount = Math.Max(0, count);
        UpdateContentSize();
        ResetScrollPosition();
        // 必须强制重渲染：UpdateVisibleRange 在「首索引 + 可见数」都没变时会提前 return
        // （既不清缓存也不回调渲染），只靠它刷新会让列表项保留旧内容——徽标 / 标记 / 可点性
        // 这类"数量不变但数据已变"的刷新会静默失效。
        ResetVisibleItems();
        UpdateVisibleRange();
    }

    /// <summary>
    /// 刷新当前可见区域，用于数据变更后更新显示
    /// </summary>
    public void Refresh()
    {
        if (!_initialized) return;
        UpdateContentSize();
        ResetVisibleItems();
        UpdateVisibleRange();
    }

    /// <summary>
    /// 滚动到指定索引的列表项（流动布局下滚到该条目所在的整行/列）
    /// </summary>
    public void ScrollTo(int index)
    {
        if (!_initialized || _scrollBar == null || _itemCount == 0) return;

        index = Math.Clamp(index, 0, _itemCount - 1);

        float targetPos;
        if (FlowLayout)
        {
            var metrics = ResolveFlowMetrics();
            targetPos = VirtualListFlowLayout.LineOf(index, metrics) * ResolveFlowLineStep();
        }
        else
        {
            float cellStep = ItemSize + Spacing;
            targetPos = index * cellStep;
        }

        _lastScrollValue = targetPos;
        _scrollBar.Value = Math.Clamp(targetPos, 0, (float)_scrollBar.MaxValue);
        UpdateVisibleRange();
    }

    /// <summary>
    /// 丢弃自动测量的流动单元格尺寸（模板/主题尺寸变化后调用）。
    /// 显式配置了 <see cref="FlowItemSize"/> 时无影响。下一次布局计算会重新测量。
    /// </summary>
    public void InvalidateFlowMeasurement()
    {
        _flowCellMeasured = false;
    }

    /// <summary>
    /// 获取指定索引对应的当前激活列表项节点，未显示时返回 null
    /// </summary>
    public Control? GetActiveItem(int index)
    {
        foreach (var item in _activeItems)
        {
            if (item.GetMeta("ItemIndex", -1).AsInt32() == index)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// 返回所有当前激活的列表项枚举
    /// </summary>
    public IEnumerable<Control> GetActiveItems()
    {
        return _activeItems;
    }

    #endregion

    #region 虚拟列表核心

    private void OnScrollValueChanged(double value)
    {
        if (Math.Abs(value - _lastScrollValue) > 0.001)
        {
            _lastScrollValue = value;
            UpdateVisibleRange();
        }
    }

    private void UpdateVisibleRange()
    {
        if (_itemContainer == null || _itemCount == 0)
        {
            ClearActiveItems();
            return;
        }

        if (FlowLayout)
        {
            var metrics = ResolveFlowMetrics();
            var (first, count) = VirtualListFlowLayout.VisibleRange(
                _itemCount,
                metrics,
                (float)_lastScrollValue,
                GetViewportSize(),
                ResolveFlowCellCrossSize(),
                EffectiveLineSpacing);
            ApplyVisibleRange(first, count);
            return;
        }

        float viewportSize = GetViewportSize();
        float cellStep = ItemSize + Spacing;
        if (cellStep <= 0) return;

        int newFirstIndex = Mathf.FloorToInt((float)_lastScrollValue / cellStep);
        newFirstIndex = Math.Clamp(newFirstIndex, 0, _itemCount - 1);

        int newVisibleCount = Mathf.CeilToInt(viewportSize / cellStep) + 1;
        newVisibleCount = Math.Min(newVisibleCount, _itemCount - newFirstIndex);

        ApplyVisibleRange(newFirstIndex, newVisibleCount);
    }

    /// <summary>可见区间发生变化时才重建条目（未变即提前返回，避免每次滚动都重渲染）。</summary>
    private void ApplyVisibleRange(int firstIndex, int visibleCount)
    {
        if (visibleCount <= 0)
        {
            ClearActiveItems();
            return;
        }

        if (firstIndex == _firstVisibleIndex && visibleCount == _visibleItemCount) return;

        _firstVisibleIndex = firstIndex;
        _visibleItemCount = visibleCount;

        ReconcileItems(firstIndex, firstIndex + visibleCount - 1);
    }

    private void ReconcileItems(int firstIndex, int lastIndex)
    {
        if (_itemContainer == null) return;

        var newActiveItems = new List<Control>(lastIndex - firstIndex + 1);

        float cellStep = ItemSize + Spacing;
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            Control? item = DetachActiveItem(i) ?? GetItemFromPool();

            item.SetMeta("ItemIndex", i);
            PositionItem(item, i, cellStep);
            RenderItem(i, item);

            if (item.GetParent() != _itemContainer)
            {
                _itemContainer.AddChild(item);
            }

            item.Visible = true;
            newActiveItems.Add(item);
        }

        foreach (var item in _activeItems)
        {
            ReturnItemToPool(item);
        }

        _activeItems.Clear();
        _activeItems.AddRange(newActiveItems);
    }

    private Control? DetachActiveItem(int index)
    {
        for (int i = 0; i < _activeItems.Count; i++)
        {
            if (_activeItems[i].GetMeta("ItemIndex", -1).AsInt32() == index)
            {
                var item = _activeItems[i];
                _activeItems.RemoveAt(i);
                return item;
            }
        }
        return null;
    }

    private void RenderItem(int index, Control item)
    {
        OnRenderItem?.Invoke(index, item);
    }

    #endregion

    #region 对象池

    private Control GetItemFromPool()
    {
        if (_itemPool.Count > 0)
        {
            var item = _itemPool.Pop();
            item.Visible = true;
            return item;
        }

        return CreateNewItem();
    }

    private Control CreateNewItem()
    {
        if (ItemTemplate == null)
        {
            AppLog.Error("VirtualList: ItemTemplate 未设置，无法创建列表项", "VirtualList");
            return new Control();
        }

        Node? instance = ItemTemplate.Instantiate();
        if (instance is Control itemControl)
        {
            return itemControl;
        }

        AppLog.Error("VirtualList: ItemTemplate 的根节点必须是 Control 类型", "VirtualList");
        instance?.QueueFree();
        return new Control();
    }

    private void ReturnItemToPool(Control item)
    {
        item.Visible = false;
        item.RemoveMeta("ItemIndex");

        // 刻意**保留父子关系**（只隐藏、不 RemoveChild）：池里的节点必须挂在容器下，
        // 否则它们是"无父节点"的孤儿——Godot 不会随场景树释放孤儿节点，
        // 界面被 QueueFree 时会留下 ObjectDB 泄漏；挂在容器下则随子树一起释放，
        // 而且缓存重开（界面走 RemoveChild/AddChild）时池还是满的，可以直接复用。
        _itemPool.Push(item);
    }

    private void ClearActiveItems()
    {
        foreach (var item in _activeItems)
        {
            ReturnItemToPool(item);
        }
        _activeItems.Clear();
        _firstVisibleIndex = -1;
        _visibleItemCount = 0;
    }

    #endregion

    #region 布局计算

    private void UpdateContentSize()
    {
        if (_itemContainer == null) return;

        if (FlowLayout)
        {
            // 流动布局：内容总尺寸只在滚动轴上需要显式最小值（＝总行/列占位）。
            // 主轴交给 ScrollContainer 拉伸，不能写死——否则行长度会被算成内容宽度而冒出多余的滚动条。
            float contentSize = ResolveFlowMetrics().ContentSize;
            _itemContainer.CustomMinimumSize = IsVertical
                ? new Vector2(0, contentSize)
                : new Vector2(contentSize, 0);
            return;
        }

        float totalSize = Math.Max(0, _itemCount * (ItemSize + Spacing) - Spacing);
        if (IsVertical)
        {
            _itemContainer.CustomMinimumSize = new Vector2(0, totalSize);
        }
        else
        {
            _itemContainer.CustomMinimumSize = new Vector2(totalSize, 0);
        }
    }

    /// <summary>
    /// 把条目摆到第 <paramref name="index"/> 个格子上。
    /// </summary>
    /// <remarks>
    /// **只设位置、不改尺寸**：条目尺寸由预制体决定（见 <see cref="ItemSize"/> 的说明）。
    /// 早期实现按"格子尺寸"给条目 set_size，会因容器/可视区尺寸而把固定尺寸的卡面拉变形，
    /// 且在容器尚未布局时（宽度为 0）会把条目宽度算成 0。
    /// 需要铺满横轴的整行条目请打开 <see cref="StretchItemAcrossAxis"/>。
    /// 流动布局（<see cref="FlowLayout"/>）下按行列坐标换算位置，<paramref name="cellStep"/> 不参与。
    /// </remarks>
    private void PositionItem(Control item, int index, float cellStep)
    {
        if (FlowLayout)
        {
            item.Position = VirtualListFlowLayout.Position(
                index,
                ResolveFlowMetrics(),
                ResolveFlowCellMainSize(),
                ResolveFlowCellCrossSize(),
                Spacing,
                EffectiveLineSpacing,
                IsVertical);
            return;
        }

        float pos = index * cellStep;

        if (IsVertical)
        {
            item.Position = new Vector2(0, pos);
            if (StretchItemAcrossAxis)
            {
                item.Size = new Vector2(GetAcrossSize(), item.Size.Y);
            }
        }
        else
        {
            item.Position = new Vector2(pos, 0);
            if (StretchItemAcrossAxis)
            {
                item.Size = new Vector2(item.Size.X, GetAcrossSize());
            }
        }
    }

    /// <summary>横轴可用尺寸（垂直列表为宽度，水平列表为高度），优先取滚动区。</summary>
    private float GetAcrossSize()
    {
        var scrollSize = ScrollArea?.Size ?? Vector2.Zero;
        if (scrollSize.X > 0 && scrollSize.Y > 0)
        {
            return IsVertical ? scrollSize.X : scrollSize.Y;
        }

        return IsVertical ? Size.X : Size.Y;
    }

    private float GetViewportSize()
    {
        return IsVertical
            ? (ScrollArea?.Size.Y ?? Size.Y)
            : (ScrollArea?.Size.X ?? Size.X);
    }

    private void ResetScrollPosition()
    {
        if (_scrollBar != null)
        {
            _scrollBar.Value = 0;
            _lastScrollValue = 0;
        }
    }

    private void ResetVisibleItems()
    {
        ClearActiveItems();
    }

    #endregion

    #region 流动布局

    /// <summary>相邻行/列的间距：<see cref="LineSpacing"/> &lt; 0（默认）时复用 <see cref="Spacing"/>。</summary>
    private float EffectiveLineSpacing => LineSpacing >= 0f ? LineSpacing : Spacing;

    /// <summary>行长度（主轴可用尺寸）：竖向滚动取可视宽度，横向滚动取可视高度。</summary>
    private float GetLineLength()
    {
        var scrollSize = ScrollArea?.Size ?? Vector2.Zero;
        var value = IsVertical ? scrollSize.X : scrollSize.Y;
        if (value > 0f)
        {
            return value;
        }

        return IsVertical ? Size.X : Size.Y;
    }

    /// <summary>条目在主轴（行内排列方向）上的尺寸。</summary>
    private float ResolveFlowCellMainSize()
    {
        var cell = ResolveFlowCellSize();
        return IsVertical ? cell.X : cell.Y;
    }

    /// <summary>条目在横轴（换行方向）上的尺寸，即滚动轴上的占位。</summary>
    private float ResolveFlowCellCrossSize()
    {
        var cell = ResolveFlowCellSize();
        return IsVertical ? cell.Y : cell.X;
    }

    private float ResolveFlowLineStep() =>
        VirtualListFlowLayout.LineStep(ResolveFlowCellCrossSize(), EffectiveLineSpacing);

    /// <summary>按当前行长度与单元格尺寸现算流动布局度量（容器变宽/变窄即时生效）。</summary>
    private VirtualListFlowLayout.Metrics ResolveFlowMetrics() =>
        VirtualListFlowLayout.Compute(
            _itemCount,
            GetLineLength(),
            ResolveFlowCellMainSize(),
            ResolveFlowCellCrossSize(),
            Spacing,
            EffectiveLineSpacing);

    /// <summary>
    /// 流动布局的单元格尺寸：显式配置 <see cref="FlowItemSize"/> 优先，否则测量一次模板实例并缓存。
    /// </summary>
    /// <remarks>模板根节点既可能是普通 Control（靠 <c>custom_minimum_size</c>），也可能是容器
    /// （尺寸由子节点决定），因此取"自身 Size"与"合并最小尺寸"的较大者；两者都测不出时退回
    /// <see cref="ItemSize"/>，保证布局仍可算而不是按 0 尺寸算出一行放不下任何条目。</remarks>
    private Vector2 ResolveFlowCellSize()
    {
        if (FlowItemSize.X > 0f && FlowItemSize.Y > 0f)
        {
            return FlowItemSize;
        }

        if (_flowCellMeasured)
        {
            return _flowCellSize;
        }

        _flowCellMeasured = true;

        var probe = GetItemFromPool() ?? CreateNewItem();
        var combined = probe.GetCombinedMinimumSize();
        var size = probe.Size;
        _flowCellSize = new Vector2(
            MathF.Max(size.X, combined.X),
            MathF.Max(size.Y, combined.Y));

        if (_flowCellSize.X <= 0f || _flowCellSize.Y <= 0f)
        {
            _flowCellSize = new Vector2(
                _flowCellSize.X > 0f ? _flowCellSize.X : MathF.Max(1f, ItemSize),
                _flowCellSize.Y > 0f ? _flowCellSize.Y : MathF.Max(1f, ItemSize));
            AppLog.Warning(
                $"VirtualList: 流动布局测不出条目尺寸（模板未设置或尺寸为 0），退回 ItemSize={ItemSize}。",
                "VirtualList");
        }

        // 量完即归还对象池：这一份实例后续照常复用，不额外浪费节点。
        ReturnItemToPool(probe);
        return _flowCellSize;
    }

    #endregion
}