namespace KemoCard.Mod.Combat;

public sealed class HandSlot
{
	private readonly List<HandSlotEffectRef> _slotEffects = [];

	public int SlotIndex { get; }
	public string? CardId { get; private set; }
	public string? RuntimeInstanceId { get; private set; }
	public IReadOnlyList<HandSlotEffectRef> SlotEffects => _slotEffects;
	public bool IsEmpty => CardId is null;

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
	}

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
