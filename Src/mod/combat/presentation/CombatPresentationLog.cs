namespace KemoCard.Mod.Combat.Presentation;

/// <summary>
/// 表现事件日志（战斗规格 §16.1）：逻辑侧 <see cref="Emit"/> 追加，界面侧 <see cref="Drain"/> 取走。
/// 挂在 <c>CombatSimulation.Presentation</c> 上始终存在；不是调试日志，调试面板仍用 InspectBattle 快照。
/// </summary>
public sealed class CombatPresentationLog
{
    private readonly List<CombatPresentationEvent> _events = [];

    public int Count => _events.Count;

    /// <summary>尚未取走的事件只读视图（测试与界面对账用）。</summary>
    public IReadOnlyList<CombatPresentationEvent> Pending => _events;

    public void Emit(CombatPresentationEvent presentationEvent)
    {
        ArgumentNullException.ThrowIfNull(presentationEvent);
        _events.Add(presentationEvent);
    }

    /// <summary>取走全部待播放事件并清空。</summary>
    public IReadOnlyList<CombatPresentationEvent> Drain()
    {
        if (_events.Count == 0)
            return [];

        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }

    public void Clear() => _events.Clear();
}