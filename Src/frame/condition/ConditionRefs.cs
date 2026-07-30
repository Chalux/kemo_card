namespace KemoCard.Frame.Condition;

public sealed class ConditionRefs
{
    public IReadOnlyList<string> ItemIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FlagIds { get; init; } = Array.Empty<string>();
}