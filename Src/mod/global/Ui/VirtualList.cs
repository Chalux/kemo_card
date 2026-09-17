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
    /// 列表项在滚动方向上的固定尺寸
    /// </summary>
    [Export] public float ItemSize { get; set; } = 48f;

    /// <summary>
    /// 列表项之间的间距
    /// </summary>
    [Export] public float Spacing { get; set; } = 2f;

    /// <summary>
    /// 是否垂直滚动（false 为水平滚动）
    /// </summary>
    [Export] public bool IsVertical { get; set; } = true;

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

    #endregion

    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑（见 ui-mod-binding 规格 §4.3）。</summary>
    private readonly BindingScope _binder = new();

    #region 生命周期

    public override void _Ready()
    {
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
        ClearAllItems();
        // 复位初始化状态，让本节点重入树后能重新取滚动条并重新登记订阅。
        // 注意：Godot 的 _Ready 每个节点只调用一次（界面进缓存走 RemoveChild，重开 AddChild
        // 不会再触发 _Ready），因此重入树后是 SetData / Refresh 等公开方法再次驱动
        // EnsureInitialized()，而不是 _Ready。
        _initialized = false;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        if (ScrollArea == null)
        {
            AppLog.Error("VirtualList: ScrollArea 未设置", "VirtualList");
            return;
        }

        // 复用仍然有效的容器：离场只是 RemoveChild，容器节点并未释放；
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

        _scrollBar = IsVertical ? ScrollArea.GetVScrollBar() : ScrollArea.GetHScrollBar();
        if (_scrollBar != null)
        {
            _binder.OnValueChanged(_scrollBar, OnScrollValueChanged);
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
    /// 滚动到指定索引的列表项
    /// </summary>
    public void ScrollTo(int index)
    {
        if (!_initialized || _scrollBar == null || _itemCount == 0) return;

        index = Math.Clamp(index, 0, _itemCount - 1);
        float cellStep = ItemSize + Spacing;
        float targetPos = index * cellStep;

        _lastScrollValue = targetPos;
        _scrollBar.Value = Math.Clamp(targetPos, 0, (float)_scrollBar.MaxValue);
        UpdateVisibleRange();
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

        float viewportSize = GetViewportSize();
        float cellStep = ItemSize + Spacing;
        if (cellStep <= 0) return;

        int newFirstIndex = Mathf.FloorToInt((float)_lastScrollValue / cellStep);
        newFirstIndex = Math.Clamp(newFirstIndex, 0, _itemCount - 1);

        int newVisibleCount = Mathf.CeilToInt(viewportSize / cellStep) + 1;
        newVisibleCount = Math.Min(newVisibleCount, _itemCount - newFirstIndex);

        if (newFirstIndex == _firstVisibleIndex && newVisibleCount == _visibleItemCount) return;

        _firstVisibleIndex = newFirstIndex;
        _visibleItemCount = newVisibleCount;

        ReconcileItems(newFirstIndex, newFirstIndex + newVisibleCount - 1);
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

        if (item.GetParent() == _itemContainer)
        {
            _itemContainer!.RemoveChild(item);
        }

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

    private void ClearAllItems()
    {
        ClearActiveItems();

        while (_itemPool.Count > 0)
        {
            _itemPool.Pop().QueueFree();
        }
        _itemCount = 0;
    }

    #endregion

    #region 布局计算

    private void UpdateContentSize()
    {
        if (_itemContainer == null) return;

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

    private void PositionItem(Control item, int index, float cellStep)
    {
        float pos = index * cellStep;
        float containerWidth = _itemContainer?.Size.X ?? Size.X;
        float containerHeight = _itemContainer?.Size.Y ?? Size.Y;

        if (IsVertical)
        {
            item.Position = new Vector2(0, pos);
            item.Size = new Vector2(containerWidth, ItemSize);
        }
        else
        {
            item.Position = new Vector2(pos, 0);
            item.Size = new Vector2(ItemSize, containerHeight);
        }
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
}