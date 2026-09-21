namespace KemoCard.Frame.Content;

public sealed class ContentRegistryMerger
{
    public ContentRegistryMergeResult Merge(
        IReadOnlyList<ModContentBundle> bundles,
        GameDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(store);

        store.Clear();
        var conflicts = new List<ContentIdConflictEntry>();
        var ownerById = new Dictionary<(EContentCategory Category, string Id), string>();

        foreach (var bundle in bundles)
        {
            MergeBundle(bundle, store, ownerById, conflicts);
        }

        return new ContentRegistryMergeResult(conflicts, ownerById);
    }

    private static void MergeBundle(
        ModContentBundle bundle,
        GameDefinitionStore store,
        Dictionary<(EContentCategory Category, string Id), string> ownerById,
        List<ContentIdConflictEntry> conflicts)
    {
        var definitions = bundle.Definitions;
        TryAddAll(bundle.ModId, EContentCategory.Character, definitions.Characters, store.CharactersMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Enemy, definitions.Enemies, store.EnemiesMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Battle, definitions.Battles, store.BattlesMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Event, definitions.Events, store.EventsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Item, definitions.Items, store.ItemsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Card, definitions.Cards, store.CardsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Skill, definitions.Skills, store.SkillsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Buff, definitions.Buffs, store.BuffsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Effect, definitions.Effects, store.EffectsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Attribute, definitions.Attributes, store.AttributesMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.GameplayEffect, definitions.GameplayEffects, store.GameplayEffectsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.GameplayTag, definitions.GameplayTags, store.GameplayTagsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.SkillAction, definitions.SkillActions, store.SkillActionsMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.Story, definitions.Stories, store.StoriesMutable, ownerById, conflicts);
        TryAddAll(bundle.ModId, EContentCategory.OrbType, definitions.OrbTypes, store.OrbTypesMutable, ownerById, conflicts);
    }

    private static void TryAddAll<T>(
        string modId,
        EContentCategory category,
        IReadOnlyDictionary<string, T> source,
        Dictionary<string, T> target,
        Dictionary<(EContentCategory Category, string Id), string> ownerById,
        List<ContentIdConflictEntry> conflicts)
    {
        foreach (var (id, dto) in source)
        {
            var key = (category, id);
            if (target.TryAdd(id, dto))
            {
                ownerById[key] = modId;
                continue;
            }

            var winnerModId = ownerById[key];
            conflicts.Add(new ContentIdConflictEntry(category, id, winnerModId, modId));
        }
    }
}