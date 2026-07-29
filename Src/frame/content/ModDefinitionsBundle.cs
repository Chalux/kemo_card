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
    IReadOnlyDictionary<string, EffectDto> Effects)
{
    public IReadOnlyDictionary<string, AttributeDefDto> Attributes { get; init; } =
        new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, GameplayEffectDefDto> GameplayEffects { get; init; } =
        new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, GameplayTagDefDto> GameplayTags { get; init; } =
        new Dictionary<string, GameplayTagDefDto>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SkillActionDto> SkillActions { get; init; } =
        new Dictionary<string, SkillActionDto>(StringComparer.Ordinal);

    public static ModDefinitionsBundle Empty { get; } = new(
        new Dictionary<string, CharacterDto>(StringComparer.Ordinal),
        new Dictionary<string, EnemyDto>(StringComparer.Ordinal),
        new Dictionary<string, BattleDto>(StringComparer.Ordinal),
        new Dictionary<string, EventDto>(StringComparer.Ordinal),
        new Dictionary<string, ItemDto>(StringComparer.Ordinal),
        new Dictionary<string, CardDto>(StringComparer.Ordinal),
        new Dictionary<string, SkillDto>(StringComparer.Ordinal),
        new Dictionary<string, BuffDto>(StringComparer.Ordinal),
        new Dictionary<string, EffectDto>(StringComparer.Ordinal));
}