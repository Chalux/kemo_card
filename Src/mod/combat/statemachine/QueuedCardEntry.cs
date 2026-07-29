using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

/// <param name="Paid">标记入队时实扣的当前可用能量；取消标记时按此额度退还。</param>
public sealed record QueuedCardEntry(
	int CharacterIndex,
	string CardId,
	string RuntimeInstanceId,
	int Priority,
	IReadOnlyList<CombatTargetRef> Targets,
	long Sequence,
	int Paid = 0);
