using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed record ModDefinitionsBundle(
	IReadOnlyDictionary<string, CardDto> Cards,
	IReadOnlyDictionary<string, SkillDto> Skills,
	IReadOnlyDictionary<string, BuffDto> Buffs,
	IReadOnlyDictionary<string, EffectDto> Effects)
{
	public static ModDefinitionsBundle Empty { get; } = new(
		new Dictionary<string, CardDto>(StringComparer.Ordinal),
		new Dictionary<string, SkillDto>(StringComparer.Ordinal),
		new Dictionary<string, BuffDto>(StringComparer.Ordinal),
		new Dictionary<string, EffectDto>(StringComparer.Ordinal));
}
