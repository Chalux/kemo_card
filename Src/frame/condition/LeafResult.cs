namespace KemoCard.Frame.Condition;

public sealed class LeafResult
{
    public required string CondType { get; init; }
    public required bool Passed { get; init; }
    public required string ShortTipKey { get; init; }
    public required string LongTipKey { get; init; }
    public IReadOnlyList<object?> Fill { get; init; } = Array.Empty<object?>();
    public ConditionProgress? Progress { get; init; }
    public ConditionRefs? Refs { get; init; }
}