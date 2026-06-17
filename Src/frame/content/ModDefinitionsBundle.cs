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
