namespace KemoCard.Frame.Gas.Magnitude;

public sealed class MagnitudeEvaluationContext
{
    public AbilitySystemComponent? SourceAsc { get; init; }
    public AbilitySystemComponent? TargetAsc { get; init; }
    public Dictionary<string, float> SetByCaller { get; init; } = [];
}