using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat;

public sealed class CharacterInstance
{
    private readonly List<DeckPreset> _decks = [];

    public string InstanceId { get; }
    public string DefinitionId { get; private set; } = "";
    public CharacterDto? Definition { get; private set; }
    public IReadOnlyList<DeckPreset> Decks => _decks;
    public int CurrentDeckIndex { get; private set; }
    public bool IsDeckLocked { get; private set; }

    public CharacterInstance(CharacterDto definition, string? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        InstanceId = instanceId ?? Guid.NewGuid().ToString("N");
        BindDefinition(definition);
        _decks.Add(DeckPreset.CreateWithExclusiveCards(definition));
        CurrentDeckIndex = 0;
    }

    public CharacterInstance()
    {
        InstanceId = Guid.NewGuid().ToString("N");
        DefinitionId = "";
    }

    public void BindDefinition(CharacterDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        DefinitionId = definition.Id;
    }

    public void SetDeckLocked(bool locked) => IsDeckLocked = locked;

    public bool TryCreateDeck()
    {
        if (IsDeckLocked || Definition is null)
            return false;
        if (_decks.Count >= CombatConstants.MaxDecksPerCharacter)
            return false;

        _decks.Add(DeckPreset.CreateWithExclusiveCards(Definition));
        return true;
    }

    public bool TryEditDeck(int deckIndex, Action<DeckPreset> edit)
    {
        if (IsDeckLocked)
            return false;
        if (deckIndex < 0 || deckIndex >= _decks.Count)
            return false;

        edit(_decks[deckIndex]);
        return true;
    }

    public bool TrySetCurrentDeck(int index)
    {
        if (IsDeckLocked)
            return false;
        if (index < 0 || index >= _decks.Count)
            return false;

        CurrentDeckIndex = index;
        return true;
    }

    public void ApplyDeckSnapshots(IReadOnlyList<IReadOnlyList<string>> deckSnapshots, int currentDeckIndex)
    {
        ArgumentNullException.ThrowIfNull(deckSnapshots);
        if (deckSnapshots.Count == 0)
            return;

        _decks.Clear();
        foreach (var cardIds in deckSnapshots)
            _decks.Add(new DeckPreset(Guid.NewGuid().ToString("N"), null, cardIds));

        CurrentDeckIndex = currentDeckIndex >= 0 && currentDeckIndex < _decks.Count
            ? currentDeckIndex
            : 0;
    }

    public DeckPreset? GetCurrentDeck()
    {
        if (_decks.Count == 0 || CurrentDeckIndex < 0 || CurrentDeckIndex >= _decks.Count)
            return null;
        return _decks[CurrentDeckIndex];
    }

    public HashSet<string> GetBuildableCardIds(IReadOnlySet<string> obtainedCardIds)
    {
        var set = new HashSet<string>(obtainedCardIds, StringComparer.Ordinal);
        if (Definition is not null)
        {
            foreach (var cardId in Definition.Cards)
                set.Add(cardId);
        }
        return set;
    }

    public DeckValidationResult ValidateCurrentDeck(IReadOnlySet<string> obtainedCardIds)
    {
        var deck = GetCurrentDeck();
        if (deck is null)
            return DeckValidationResult.Fail([]);
        return deck.Validate(GetBuildableCardIds(obtainedCardIds));
    }

    public Dictionary<string, float> ComputeAttributeMap(GameDefinitionRegistry definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var deck = GetCurrentDeck();
        if (deck is null)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var totals = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var cardId in deck.CardIds)
        {
            if (!definitions.Store.TryGetCard(cardId, out var card) || card.Stats is null)
                continue;
            var contribution = AttributeContributionMapper.MapCardStats(card.Stats);
            foreach (var (attributeId, value) in contribution)
            {
                totals.TryGetValue(attributeId, out var current);
                totals[attributeId] = current + value;
            }
        }

        return totals;
    }

    public CharacterAttributes ComputeAttributes(GameDefinitionRegistry definitions) =>
        CharacterAttributes.FromMap(ComputeAttributeMap(definitions));
}