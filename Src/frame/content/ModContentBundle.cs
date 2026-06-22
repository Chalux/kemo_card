namespace KemoCard.Frame.Content;

public sealed record ModContentBundle(
	string ModId,
	IReadOnlyList<string> Characters,
	IReadOnlyList<string> Enemies,
	IReadOnlyList<string> Battles,
	IReadOnlyList<string> Events,
	IReadOnlyList<string> Cards,
	IReadOnlyList<string> Items,
	IReadOnlyList<string> Skills,
	IReadOnlyList<string> Buffs,
	IReadOnlyList<string> Effects,
	ModDefinitionsBundle Definitions)
{
	public IReadOnlyList<string> Attributes { get; init; } = [];

	public IReadOnlyList<string> GameplayEffects { get; init; } = [];

	public IReadOnlyList<string> GameplayTags { get; init; } = [];

	public IReadOnlyList<string> SkillActions { get; init; } = [];
}
