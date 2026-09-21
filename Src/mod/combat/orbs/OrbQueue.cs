namespace KemoCard.Mod.Combat.Orbs;

/// <summary>
/// 全队共享的充能球队列（FIFO）：记录入队顺序与每个球的产球者。
/// 容量与主动触发门槛是系统常量；触发时一次清空全部球。
/// </summary>
public sealed class OrbQueue
{
    /// <summary>队列长度上限；达到上限时"获得即触发"（自动触发）。</summary>
    public const int Capacity = 7;

    /// <summary>出牌阶段可主动触发的最小球数。</summary>
    public const int ManualTriggerThreshold = 3;

    private readonly List<OrbInstance> _orbs = [];

    public int Count => _orbs.Count;

    public bool IsEmpty => _orbs.Count == 0;

    public bool IsFull => _orbs.Count >= Capacity;

    public bool CanTriggerManually => _orbs.Count >= ManualTriggerThreshold;

    /// <summary>按入队顺序的只读视图（UI / 调试输出用）。</summary>
    public IReadOnlyList<OrbInstance> Orbs => _orbs;

    /// <summary>入队一个球；返回 false 表示队列已满、该球被丢弃（正常流程不会发生：满员即刻触发）。</summary>
    public bool TryEnqueue(OrbInstance orb)
    {
        if (IsFull)
            return false;

        _orbs.Add(orb);
        return true;
    }

    /// <summary>清空并返回全部球（触发结算的唯一入口）。</summary>
    public List<OrbInstance> DrainAll()
    {
        var drained = new List<OrbInstance>(_orbs);
        _orbs.Clear();
        return drained;
    }

    public int CountOf(string orbTypeId)
    {
        var count = 0;
        foreach (var orb in _orbs)
        {
            if (string.Equals(orb.OrbTypeId, orbTypeId, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    public void Clear() => _orbs.Clear();
}
