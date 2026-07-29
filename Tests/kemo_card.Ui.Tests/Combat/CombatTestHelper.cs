using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Ui.Tests.Combat;

internal static class CombatTestHelper
{
    public static GameDefinitionRegistry CreateRegistry(params CardDto[] cards)
    {
        var cardDict = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        var definitions = new ModDefinitionsBundle(
            ModDefinitionsBundle.Empty.Characters,
            ModDefinitionsBundle.Empty.Enemies,
            ModDefinitionsBundle.Empty.Battles,
            ModDefinitionsBundle.Empty.Events,
            ModDefinitionsBundle.Empty.Items,
            cardDict,
            ModDefinitionsBundle.Empty.Skills,
            ModDefinitionsBundle.Empty.Buffs,
            ModDefinitionsBundle.Empty.Effects)
        {
            Attributes = ModDefinitionsBundle.Empty.Attributes,
            GameplayEffects = ModDefinitionsBundle.Empty.GameplayEffects,
            GameplayTags = ModDefinitionsBundle.Empty.GameplayTags,
            SkillActions = ModDefinitionsBundle.Empty.SkillActions,
        };

        var bundle = new ModContentBundle(
            ModId: "test.mod",
            Characters: [],
            Enemies: [],
            Battles: [],
            Events: [],
            Cards: cardDict.Keys.ToList(),
            Items: [],
            Skills: [],
            Buffs: [],
            Effects: [],
            Definitions: definitions)
        {
            Attributes = [],
            GameplayEffects = [],
            GameplayTags = [],
            SkillActions = [],
        };

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([bundle], out _);
        return registry;
    }

    public static GameDefinitionRegistry CreateFullRegistry(
        IReadOnlyDictionary<string, CardDto>? cards = null,
        IReadOnlyDictionary<string, SkillDto>? skills = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, EnemyDto>? enemies = null,
        IReadOnlyDictionary<string, BattleDto>? battles = null,
        IReadOnlyDictionary<string, CharacterDto>? characters = null,
        IReadOnlyDictionary<string, AttributeDefDto>? attributes = null,
        IReadOnlyDictionary<string, GameplayEffectDefDto>? gameplayEffects = null,
        IReadOnlyDictionary<string, GameplayTagDefDto>? gameplayTags = null,
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null)
    {
        var registry = new GameDefinitionRegistry();
        RebuildInto(
            registry, cards, skills, effects, buffs, enemies, battles,
            characters, attributes, gameplayEffects, gameplayTags, skillActions);
        return registry;
    }

    /// <summary>
    /// 用新的定义集重建已有 registry。<see cref="GameDefinitionRegistry.Store"/> 为同一实例且就地重建，
    /// 因此已创建的 <c>CombatSimulation</c> 会立刻看到新定义——用于测试动态费用等场景。
    /// </summary>
    public static void RebuildInto(
        GameDefinitionRegistry registry,
        IReadOnlyDictionary<string, CardDto>? cards = null,
        IReadOnlyDictionary<string, SkillDto>? skills = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, EnemyDto>? enemies = null,
        IReadOnlyDictionary<string, BattleDto>? battles = null,
        IReadOnlyDictionary<string, CharacterDto>? characters = null,
        IReadOnlyDictionary<string, AttributeDefDto>? attributes = null,
        IReadOnlyDictionary<string, GameplayEffectDefDto>? gameplayEffects = null,
        IReadOnlyDictionary<string, GameplayTagDefDto>? gameplayTags = null,
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        cards ??= new Dictionary<string, CardDto>(StringComparer.Ordinal);
        skills ??= new Dictionary<string, SkillDto>(StringComparer.Ordinal);
        effects ??= new Dictionary<string, EffectDto>(StringComparer.Ordinal);
        buffs ??= new Dictionary<string, BuffDto>(StringComparer.Ordinal);
        enemies ??= new Dictionary<string, EnemyDto>(StringComparer.Ordinal);
        battles ??= new Dictionary<string, BattleDto>(StringComparer.Ordinal);
        characters ??= new Dictionary<string, CharacterDto>(StringComparer.Ordinal);
        attributes ??= new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal);
        gameplayEffects ??= new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal);
        gameplayTags ??= new Dictionary<string, GameplayTagDefDto>(StringComparer.Ordinal);
        skillActions ??= new Dictionary<string, SkillActionDto>(StringComparer.Ordinal);

        var definitions = new ModDefinitionsBundle(
            characters, enemies, battles,
            ModDefinitionsBundle.Empty.Events,
            ModDefinitionsBundle.Empty.Items,
            cards, skills, buffs, effects)
        {
            Attributes = attributes,
            GameplayEffects = gameplayEffects,
            GameplayTags = gameplayTags,
            SkillActions = skillActions,
        };

        var bundle = new ModContentBundle(
            ModId: "test.mod",
            Characters: characters.Keys.ToList(),
            Enemies: enemies.Keys.ToList(),
            Battles: battles.Keys.ToList(),
            Events: [],
            Cards: cards.Keys.ToList(),
            Items: [],
            Skills: skills.Keys.ToList(),
            Buffs: buffs.Keys.ToList(),
            Effects: effects.Keys.ToList(),
            Definitions: definitions)
        {
            Attributes = attributes.Keys.ToList(),
            GameplayEffects = gameplayEffects.Keys.ToList(),
            GameplayTags = gameplayTags.Keys.ToList(),
            SkillActions = skillActions.Keys.ToList(),
        };

        registry.Rebuild([bundle], out _);
    }
}