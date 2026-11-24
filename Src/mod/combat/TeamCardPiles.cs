using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Combat;

/// <summary>全队唯一的战斗抽牌堆与墓地。牌归构筑它的角色持有、施放和付费。</summary>
public sealed class TeamCardPiles
{
    private readonly IReadOnlyList<CharacterBattleInstance> _owners;
    private readonly List<CardRuntimeEntry> _draw;
    private readonly List<CardRuntimeEntry> _grave = [];
    public IReadOnlyList<CardRuntimeEntry> DrawPile { get; }
    public IReadOnlyList<CardRuntimeEntry> Graveyard { get; }
    public int HandCount => _owners.Sum(owner => owner.HandSlots.Count(slot => !slot.IsEmpty));
    public int RemainingHandSpace => CombatConstants.TeamHandLimit - HandCount;

    internal TeamCardPiles(IReadOnlyList<CharacterBattleInstance> owners, IEnumerable<CardRuntimeEntry>? cards = null)
    {
        _owners = owners;
        _draw = cards?.ToList() ?? [];
        DrawPile = _draw.AsReadOnly();
        Graveyard = _grave.AsReadOnly();
    }

    internal void ImportOwner(TeamCardPiles previous, int oldIndex, int newIndex)
    {
        _draw.AddRange(previous.DrawPile.Where(c => c.CharacterIndex == oldIndex).Select(c => c with { CharacterIndex = newIndex }));
        _grave.AddRange(previous.Graveyard.Where(c => c.CharacterIndex == oldIndex).Select(c => c with { CharacterIndex = newIndex }));
    }

    internal void ShuffleDrawPile(HostRng rng) => Shuffle(_draw, rng);

    // 组队入口也支持已持牌的角色。保留低槽位的手牌，多出的实例放回牌堆底。
    internal void ReturnExcessHandCards()
    {
        if (HandCount <= CombatConstants.TeamHandLimit)
            return;

        var retained = 0;
        for (var slotIndex = 0; slotIndex < CombatConstants.TeamHandLimit; slotIndex++)
        {
            for (var owner = 0; owner < _owners.Count; owner++)
            {
                var slot = _owners[owner].HandSlots[slotIndex];
                if (slot.IsEmpty)
                    continue;
                if (retained < CombatConstants.TeamHandLimit)
                {
                    retained++;
                    continue;
                }
                _draw.Insert(0, new CardRuntimeEntry(slot.CardId!, slot.RuntimeInstanceId!, owner));
                slot.ClearCard();
            }
        }
    }

    internal void Discard(CardRuntimeEntry card) => _grave.Add(card);

    internal void DiscardTop()
    {
        if (_draw.Count == 0)
            return;
        Discard(_draw[^1]);
        _draw.RemoveAt(_draw.Count - 1);
    }

    internal int Draw(int count, HostRng? rng, Action<int, HandSlot>? onDrawn = null, int? requiredOwner = null)
    {
        var limit = Math.Min(count, RemainingHandSpace);
        var drawn = 0;
        while (drawn < limit)
        {
            if (_draw.Count == 0)
            {
                if (rng is null || _grave.Count == 0 ||
                    (requiredOwner.HasValue && !_grave.Exists(card => card.CharacterIndex == requiredOwner)))
                    break;
                Shuffle(_grave, rng);
                _draw.AddRange(_grave);
                _grave.Clear();
            }
            var index = requiredOwner.HasValue ? _draw.FindLastIndex(card => card.CharacterIndex == requiredOwner) : _draw.Count - 1;
            if (index < 0)
                break;
            var entry = _draw[index];
            var slot = _owners[entry.CharacterIndex].HandSlots.First(s => s.IsEmpty);
            _draw.RemoveAt(index);
            slot.PlaceCard(entry.CardId, entry.RuntimeInstanceId);
            onDrawn?.Invoke(entry.CharacterIndex, slot);
            drawn++;
        }
        return drawn;
    }

    private static void Shuffle(List<CardRuntimeEntry> cards, HostRng rng)
    {
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = rng.NextInt(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }
}