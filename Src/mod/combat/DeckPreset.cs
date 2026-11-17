using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public sealed class DeckPreset
{
    private readonly List<string> _cardIds;
    private readonly IReadOnlyList<string> _cardView;
    private Func<bool>? _isLocked;

    public string DeckId { get; }
    public string? DisplayName { get; private set; }
    public IReadOnlyList<string> CardIds => _cardView;
    internal void BindLock(Func<bool> isLocked) => _isLocked = isLocked;

    public DeckPreset(string deckId, string? displayName, IEnumerable<string>? cardIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deckId);
        DeckId = deckId;
        DisplayName = displayName;
        _cardIds = cardIds?.ToList() ?? [];
        _cardView = _cardIds.AsReadOnly();
    }

    public static DeckPreset CreateWithExclusiveCards(CharacterDto definition, string? deckId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var cards = definition.Cards.Take(CombatConstants.MaxCardsPerDeck).ToList();
        return new DeckPreset(deckId ?? Guid.NewGuid().ToString("N"), null, cards);
    }

    public bool TryAddCard(string cardId, IReadOnlySet<string> buildableCardIds)
    {
        if (_isLocked?.Invoke() == true)
            return false;
        if (_cardIds.Count >= CombatConstants.MaxCardsPerDeck)
            return false;
        if (_cardIds.Contains(cardId, StringComparer.Ordinal))
            return false;
        if (!buildableCardIds.Contains(cardId))
            return false;

        _cardIds.Add(cardId);
        return true;
    }

    public bool TryRemoveCard(string cardId) => _isLocked?.Invoke() != true && _cardIds.Remove(cardId);

    /// <summary>升阶时在原位置替换旧链卡，已有新阶卡时去重；战斗锁定时拒绝修改。</summary>
    public bool TryReplaceCards(IReadOnlySet<string> oldIds, string replacementId)
    {
        if (_isLocked?.Invoke() == true || !_cardIds.Any(oldIds.Contains)) return false;
        var updated = _cardIds.Select(id => oldIds.Contains(id) ? replacementId : id).Distinct(StringComparer.Ordinal).ToList();
        _cardIds.Clear();
        _cardIds.AddRange(updated);
        return true;
    }

    public DeckValidationResult Validate(IReadOnlySet<string> buildableCardIds)
    {
        if (_cardIds.Count < CombatConstants.MinCardsPerDeck)
            return DeckValidationResult.Fail([]);

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