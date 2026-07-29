namespace KemoCard.Mod.Combat.Commands;

public sealed record CancelQueuedCardCommand(
    int CharacterIndex,
    long? QueueSequence = null,
    string? CardRuntimeInstanceId = null) : ICombatCommand;