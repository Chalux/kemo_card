namespace KemoCard.Mod.Combat;

public sealed class HandSlot
{
    public int SlotIndex { get; }
    public string? CardId { get; private set; }
    public string? RuntimeInstanceId { get; private set; }

    public bool IsEmpty => CardId is null;

    public HandSlot(int slotIndex) => SlotIndex = slotIndex;

    internal void PlaceCard(string cardId, string runtimeInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        CardId = cardId;
        RuntimeInstanceId = runtimeInstanceId;
    }

    internal void ClearCard()
    {
        CardId = null;
        RuntimeInstanceId = null;
    }
}
