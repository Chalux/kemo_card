namespace KemoCard.Frame.Content;

public sealed record ModContentBundle(
	string ModId,
	IReadOnlyList<string> Characters,
	IReadOnlyList<string> Battles,
	IReadOnlyList<string> Events,
	IReadOnlyList<string> Cards,
	IReadOnlyList<string> Items,
	IReadOnlyList<string> Skills,
	IReadOnlyList<string> Buffs);
