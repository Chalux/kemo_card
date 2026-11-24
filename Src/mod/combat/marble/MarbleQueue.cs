using System.Collections.ObjectModel;

namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 待发弹珠队列（FIFO）：场上没有活跃球时，发射消耗队首 1 颗；
/// 通过效果新增的弹珠追加到队尾。界面按队列顺序展示每颗球的点数与半径。
/// </summary>
/// <remarks>
/// 入队本身校验属性（非 <c>default</c>、半径不超过盘面上限），且不向公开 API
/// 暴露追加入口；外部只能经 <see cref="MarbleRuntime.EnqueueBall"/> 入队。
/// </remarks>
public sealed class MarbleQueue
{
    private readonly float _maxBallRadius;
    private readonly List<MarbleBallSpec> _items = [];
    private readonly ReadOnlyCollection<MarbleBallSpec> _itemsView;

    public MarbleQueue() : this(MarblePhysicsConfig.Default.MaxBallRadius)
    {
    }

    internal MarbleQueue(float maxBallRadius)
    {
        if (!float.IsFinite(maxBallRadius) || maxBallRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maxBallRadius));

        _maxBallRadius = maxBallRadius;
        _itemsView = _items.AsReadOnly();
    }

    public int Count => _items.Count;

    public MarbleBallSpec this[int index] => _items[index];

    /// <summary>只读视图；不返回内部 <see cref="List{T}"/> 实例，避免绕过校验直接追加。</summary>
    public IReadOnlyList<MarbleBallSpec> Items => _itemsView;

    /// <summary>仅运行时在完成校验后追加；公开 API 不提供入队入口。</summary>
    internal void Enqueue(MarbleBallSpec spec)
    {
        if (!spec.IsValid || spec.Radius > _maxBallRadius)
            throw new ArgumentOutOfRangeException(nameof(spec), "弹珠属性无效或半径超过盘面上限。");
        _items.Add(spec);
    }

    internal bool TryDequeue(out MarbleBallSpec spec)
    {
        if (_items.Count == 0)
        {
            spec = default;
            return false;
        }

        spec = _items[0];
        _items.RemoveAt(0);
        return true;
    }

    internal void Clear() => _items.Clear();
}
