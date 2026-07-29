namespace KemoCard.Mod.Combat.Commands;

public sealed record ConfirmCharacterCommand(int CharacterIndex) : ICombatCommand;