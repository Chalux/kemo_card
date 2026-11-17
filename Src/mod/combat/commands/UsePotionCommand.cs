using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Commands;

public sealed record UsePotionCommand(int PotionSlot, string ItemId, int CharacterIndex,
    IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand
{
    public IReadOnlyList<CombatTargetRef> Targets { get; } = Array.AsReadOnly(Targets.ToArray());
}