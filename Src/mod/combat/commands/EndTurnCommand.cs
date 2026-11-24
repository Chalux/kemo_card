namespace KemoCard.Mod.Combat.Commands;

public sealed record EndTurnCommand : ICombatCommand
{
    public int CharacterIndex => -1;
}
