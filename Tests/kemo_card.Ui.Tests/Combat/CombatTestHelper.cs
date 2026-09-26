using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Ui.Tests.Combat;

internal static class CombatTestHelper
{
    /// <summary>读 buff 的 IdentityMatch 条件参数（第一条该类型条件）；没有则返回 null。</summary>
    public static IReadOnlyDictionary<string, object>? IdentityParams(BuffDto buff) =>
        buff.Conditions.FirstOrDefault(condition => condition.Kind == "IdentityMatch")?.Params;

    /// <summary>读参数里的枚举名列表（JSON 载入后是 JsonElement 数组，测试构造时可能是 string[]）。</summary>
    public static string[] EnumNames(IReadOnlyDictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value) || value is null)
            return [];

        return value switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } element =>
                element.EnumerateArray().Select(item => item.ToString()).ToArray(),
            IEnumerable<object> list => list.Select(item => item.ToString()!).ToArray(),
            _ => [],
        };
    }

    /// <summary>读参数里的布尔（JSON 载入后是 JsonElement 布尔）。</summary>
    public static bool BoolParam(IReadOnlyDictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value) || value is null)
            return false;

        return value switch
        {
            bool typed => typed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True } => true,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.False } => false,
            _ => string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>读参数里的整数（JSON 载入后是 JsonElement 数字）；缺失返回 0。</summary>
    public static int IntParam(IReadOnlyDictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value) || value is null)
            return 0;

        return value switch
        {
            int typed => typed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } element
                when element.TryGetInt32(out var number) => number,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }

    public static GameDefinitionRegistry CreateRegistry(params CardDto[] cards)
    {
        var cardDict = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        var definitions = ModDefinitionsBundle.Empty with { Cards = cardDict };
        var bundle = new ModContentBundle("test.mod", definitions);

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
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null,
        IReadOnlyDictionary<string, OrbTypeDto>? orbs = null)
    {
        var registry = new GameDefinitionRegistry();
        RebuildInto(
            registry, cards, skills, effects, buffs, enemies, battles,
            characters, attributes, gameplayEffects, gameplayTags, skillActions, orbs);
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
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null,
        IReadOnlyDictionary<string, OrbTypeDto>? orbs = null)
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
        orbs ??= new Dictionary<string, OrbTypeDto>(StringComparer.Ordinal);

        var definitions = ModDefinitionsBundle.Empty with
        {
            Characters = characters,
            Enemies = enemies,
            Battles = battles,
            Cards = cards,
            Skills = skills,
            Buffs = buffs,
            Effects = effects,
            Attributes = attributes,
            GameplayEffects = gameplayEffects,
            GameplayTags = gameplayTags,
            SkillActions = skillActions,
            OrbTypes = orbs,
        };

        registry.Rebuild([new ModContentBundle("test.mod", definitions)], out _);
    }
}