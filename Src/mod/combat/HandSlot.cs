namespace KemoCard.Mod.Combat;

public sealed class HandSlot
{
    private readonly List<HandSlotEffectRef> _slotEffects = [];

    public int SlotIndex { get; }
    public string? CardId { get; private set; }
    public string? RuntimeInstanceId { get; private set; }
    public IReadOnlyList<HandSlotEffectRef> SlotEffects => _slotEffects;
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

    public bool TryAddSlotEffect(string buffId, IReadOnlyDictionary<string, object>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        _slotEffects.Add(new HandSlotEffectRef(buffId, parameters));
        return true;
    }

    public bool TryRemoveSlotEffect(string buffId)
    {
        var index = _slotEffects.FindIndex(effect => effect.BuffId == buffId);
        if (index < 0)
            return false;
        _slotEffects.RemoveAt(index);
        return true;
    }
}