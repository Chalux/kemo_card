namespace KemoCard.Mod.Combat.Runtime;

public sealed record CombatDomain(
    string GameplayEffectId,
    Guid ActiveEffectHandle,
    IReadOnlyDictionary<string, object>? Params);