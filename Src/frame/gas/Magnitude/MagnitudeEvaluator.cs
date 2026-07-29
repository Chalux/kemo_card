using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Magnitude;

public sealed class MagnitudeEvaluator
{
    public float Evaluate(MagnitudeDefDto def, MagnitudeEvaluationContext context) =>
        def.Kind switch
        {
            EMagnitudeKind.Scalar => def.Scalar,
            EMagnitudeKind.SetByCaller => EvaluateSetByCaller(def, context),
            EMagnitudeKind.AttributeBased => EvaluateAttributeBased(def, context),
            EMagnitudeKind.Custom => throw new NotSupportedException($"Custom magnitude is not supported yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(def), def.Kind, "Unknown magnitude kind."),
        };

    private static float EvaluateSetByCaller(MagnitudeDefDto def, MagnitudeEvaluationContext context)
    {
        if (string.IsNullOrEmpty(def.CallerName))
            return 0f;
        return context.SetByCaller.TryGetValue(def.CallerName, out var value) ? value : 0f;
    }

    private static float EvaluateAttributeBased(MagnitudeDefDto def, MagnitudeEvaluationContext context)
    {
        var asc = def.Capture switch
        {
            EAttributeCapture.Source => context.SourceAsc,
            EAttributeCapture.Target => context.TargetAsc,
            _ => throw new ArgumentOutOfRangeException(nameof(def), def.Capture, "Unknown attribute capture."),
        };
        if (asc is null || string.IsNullOrEmpty(def.AttributeId))
            return 0f;
        return asc.GetCurrentValue(def.AttributeId) * def.Coefficient;
    }
}