using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed record QueuedCardEntry(
	int CharacterIndex,
	string CardId,
	string RuntimeInstanceId,
	int Priority,
	IReadOnlyList<CombatTargetRef> Targets,
	long Sequence);
