using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run.Adventure;

/// <summary>卡包只投放非专属卡的链底 ID；已持有链升一阶，满级或无升级链的已持有卡出池。</summary>
public sealed class RunCardPackRewards(RunController run, GameDefinitionStore store)
{
    #region 卡包投放与升阶

    public static bool IsPackCard(CardDto card) => !card.IsExclusive && card.Rarity != ERarity.Exclusive &&
        !card.HideInDex && card.Role != ERole.None;

    public IReadOnlyList<CardDto> EligibleCards() => store.Cards.Values.Where(IsPackCard)
        .GroupBy(card => string.IsNullOrWhiteSpace(card.CardGroupId) ? $"card:{card.Id}" : $"group:{card.CardGroupId}", StringComparer.Ordinal)
        .Select(group => group.OrderBy(card => card.UpgradeTier).ThenBy(card => card.Id, StringComparer.Ordinal).First())
        .Where(card => ResolveGrant(card.Id) is not null).OrderBy(card => card.Id, StringComparer.Ordinal).ToArray();

    public CardDto? ResolveGrant(string rootId)
    {
        if (!store.TryGetCard(rootId, out var root) || !IsPackCard(root)) return null;
        var chain = Chain(root);
        if (chain[0].Id != rootId) return null;
        var highest = chain.Where(card => run.State.CardCollection.Contains(card.Id)).LastOrDefault();
        return highest is null ? root : chain.FirstOrDefault(card => card.UpgradeTier > highest.UpgradeTier);
    }

    private CardDto[] Chain(CardDto card) => string.IsNullOrWhiteSpace(card.CardGroupId) ? [card] :
        store.Cards.Values.Where(candidate => candidate.CardGroupId == card.CardGroupId && candidate.Role == card.Role && IsPackCard(candidate))
            .OrderBy(candidate => candidate.UpgradeTier).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();

    public HashSet<string> BuildableCards(CharacterInstance character)
    {
        var buildable = character.GetBuildableCardIds(run.State.CardCollection);
        var highest = store.Cards.Values.Where(card => !string.IsNullOrWhiteSpace(card.CardGroupId) && run.State.CardCollection.Contains(card.Id))
            .GroupBy(card => card.CardGroupId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Max(card => card.UpgradeTier), StringComparer.Ordinal);
        buildable.RemoveWhere(id => store.TryGetCard(id, out var card) && card.CardGroupId is { } group &&
            highest.TryGetValue(group, out var tier) && card.UpgradeTier < tier);
        return buildable;
    }

    public bool CanGrant(IReadOnlyList<string> rootIds) => !run.State.Phase.IsCombatPhase() &&
        rootIds.Distinct(StringComparer.Ordinal).Count() == rootIds.Count &&
        rootIds.All(id => ResolveGrant(id) is not null) && !run.State.CharacterPool.Any(character => character.IsDeckLocked);

    public bool Grant(IReadOnlyList<string> rootIds)
    {
        if (!CanGrant(rootIds)) return false;
        // 先计算全部结果，避免前半包入库后才发现后半包已满级。
        var grants = rootIds.Select(id => ResolveGrant(id)!).ToArray();
        foreach (var grant in grants)
        {
            var oldIds = Chain(grant).Where(card => card.UpgradeTier < grant.UpgradeTier).Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in oldIds) run.RemoveCard(id);
            run.AddCard(grant.Id);
            foreach (var character in run.State.CharacterPool)
                for (var deckIndex = 0; deckIndex < character.Decks.Count; deckIndex++)
                {
                    if (!character.Decks[deckIndex].CardIds.Any(oldIds.Contains)) continue;
                    character.TryEditDeck(deckIndex, deck => deck.TryReplaceCards(oldIds, grant.Id));
                    run.NotifyDeckChanged(character.InstanceId, deckIndex);
                }
        }
        return true;
    }

    #endregion
}