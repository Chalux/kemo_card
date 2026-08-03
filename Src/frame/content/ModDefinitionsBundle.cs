using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed record ModDefinitionsBundle(
    IReadOnlyDictionary<string, CharacterDto> Characters,
    IReadOnlyDictionary<string, EnemyDto> Enemies,
    IReadOnlyDictionary<string, BattleDto> Battles,
    IReadOnlyDictionary<string, EventDto> Events,
    IReadOnlyDictionary<string, ItemDto> Items,
    IReadOnlyDictionary<string, CardDto> Cards,
    IReadOnlyDictionary<string, SkillDto> Skills,
    IReadOnlyDictionary<string, BuffDto> Buffs,
    IReadOnlyDictionary<string, EffectDto> Effects,
    IReadOnlyDictionary<string, AttributeDefDto> Attributes,
    IReadOnlyDictionary<string, GameplayEffectDefDto> GameplayEffects,
    IReadOnlyDictionary<string, GameplayTagDefDto> GameplayTags,
    IReadOnlyDictionary<string, SkillActionDto> SkillActions,
    IReadOnlyDictionary<string, StoryDto> Stories)
{
    public static ModDefinitionsBundle Empty { get; } = new(
        new Dictionary<string, CharacterDto>(StringComparer.Ordinal),
        new Dictionary<string, EnemyDto>(StringComparer.Ordinal),
        new Dictionary<string, BattleDto>(StringComparer.Ordinal),
        new Dictionary<string, EventDto>(StringComparer.Ordinal),
        new Dictionary<string, ItemDto>(StringComparer.Ordinal),
        new Dictionary<string, CardDto>(StringComparer.Ordinal),
        new Dictionary<string, SkillDto>(StringComparer.Ordinal),
        new Dictionary<string, BuffDto>(StringComparer.Ordinal),
        new Dictionary<string, EffectDto>(StringComparer.Ordinal),
        new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
        new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal),
        new Dictionary<string, GameplayTagDefDto>(StringComparer.Ordinal),
        new Dictionary<string, SkillActionDto>(StringComparer.Ordinal),
        new Dictionary<string, StoryDto>(StringComparer.Ordinal));
}