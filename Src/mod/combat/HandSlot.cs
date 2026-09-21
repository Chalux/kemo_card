using KemoCard.Mod.Combat.Buffs;

namespace KemoCard.Mod.Combat;

public sealed class HandSlot
{
    public int SlotIndex { get; }
    public string? CardId { get; private set; }
    public string? RuntimeInstanceId { get; private set; }

    /// <summary>挂在本槽位上的 buff（充能 / 槽位伤害等）：无 ASC，只承载钩子与 tag。</summary>
    public BuffContainer Buffs { get; } = new();

    public bool IsEmpty => CardId is null;

    /// <summary>已标记入队时为对应的队列序号；<c>null</c> 表示未标记。标记不使卡牌离手。</summary>
    public long? MarkedSequence { get; private set; }

    public bool IsMarked => MarkedSequence.HasValue;

    public HandSlot(int slotIndex) => SlotIndex = slotIndex;

    public void PlaceCard(string cardId, string runtimeInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        CardId = cardId;
        RuntimeInstanceId = runtimeInstanceId;
    }

    public void ClearCard()
    {
        CardId = null;
        RuntimeInstanceId = null;
        MarkedSequence = null;
    }

    public void Mark(long sequence) => MarkedSequence = sequence;

    public void Unmark() => MarkedSequence = null;
}
