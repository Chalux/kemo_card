using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Commands;

public sealed record PlayCardCommand(
    int CharacterIndex,
    int HandSlotIndex,
    IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand
{
    public IReadOnlyList<CombatTargetRef> Targets { get; } = Array.AsReadOnly(Targets.ToArray());
}