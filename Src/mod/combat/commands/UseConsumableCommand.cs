using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Commands;

public sealed record UseConsumableCommand(int ConsumableSlot, string ItemId, int CharacterIndex,
    IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand
{
    public IReadOnlyList<CombatTargetRef> Targets { get; } = Array.AsReadOnly(Targets.ToArray());
}