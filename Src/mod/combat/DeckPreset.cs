using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public sealed class DeckPreset
{
	private readonly List<string> _cardIds;

	public string DeckId { get; }
	public string? DisplayName { get; private set; }
	public IReadOnlyList<string> CardIds => _cardIds;

	public DeckPreset(string deckId, string? displayName, IEnumerable<string>? cardIds = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(deckId);
		DeckId = deckId;
		DisplayName = displayName;
		_cardIds = cardIds?.ToList() ?? [];
	}

	public static DeckPreset CreateWithExclusiveCards(CharacterDto definition, string? deckId = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		var cards = definition.Cards.Take(CombatConstants.MaxCardsPerDeck).ToList();
		return new DeckPreset(deckId ?? Guid.NewGuid().ToString("N"), null, cards);
	}

	public bool TryAddCard(string cardId, IReadOnlySet<string> buildableCardIds)
	{
		if (_cardIds.Count >= CombatConstants.MaxCardsPerDeck)
			return false;
		if (_cardIds.Contains(cardId, StringComparer.Ordinal))
			return false;
		if (!buildableCardIds.Contains(cardId))
			return false;

		_cardIds.Add(cardId);
		return true;
	}

	public bool TryRemoveCard(string cardId) => _cardIds.Remove(cardId);

	public DeckValidationResult Validate(IReadOnlySet<string> buildableCardIds)
	{
		if (_cardIds.Count > CombatConstants.MaxCardsPerDeck)
			return DeckValidationResult.Fail(_cardIds.ToList());

		var invalid = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var cardId in _cardIds)
		{
			if (!seen.Add(cardId))
				invalid.Add(cardId);
			else if (!buildableCardIds.Contains(cardId))
				invalid.Add(cardId);
		}

		return invalid.Count == 0
			? DeckValidationResult.Ok()
			: DeckValidationResult.Fail(invalid);
	}
}
