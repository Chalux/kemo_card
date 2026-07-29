namespace KemoCard.Mod.Combat.Rules;

public enum EEndDecisionKind
{
    None,
    Victory,
    Defeat,
}

public struct EndDecision
{
    public EEndDecisionKind Kind { get; set; }
}