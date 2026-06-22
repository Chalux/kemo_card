using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Commands;

public sealed record CastInstantSkillCommand(
	int CharacterIndex,
	string SkillId,
	IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand;
