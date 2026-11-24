namespace KemoCard.Mod.Combat.Commands;

public sealed record LaunchMarbleCommand(float AngleRadians) : ICombatCommand
{
    public int CharacterIndex => -1;
}

public sealed record MarkMarbleTargetCommand(int EnemyIndex) : ICombatCommand
{
    public int CharacterIndex => -1;
}

public sealed record EndMarbleCommand : ICombatCommand
{
    public int CharacterIndex => -1;
}
