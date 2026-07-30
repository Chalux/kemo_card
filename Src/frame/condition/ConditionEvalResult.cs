namespace KemoCard.Frame.Condition;

public sealed class ConditionEvalResult
{
    public required bool Passed { get; init; }
    public required IReadOnlyList<LeafResult> Leaves { get; init; }
}