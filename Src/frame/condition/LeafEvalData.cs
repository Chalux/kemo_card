namespace KemoCard.Frame.Condition;

public sealed class LeafEvalData
{
    public required bool Passed { get; init; }
    public IReadOnlyList<object?> Fill { get; init; } = Array.Empty<object?>();
    public ConditionProgress? Progress { get; init; }
    public ConditionRefs? Refs { get; init; }
}