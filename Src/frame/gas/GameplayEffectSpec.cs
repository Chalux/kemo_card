using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public sealed class GameplayEffectSpec
{
    public GameplayEffectDefDto Def { get; }
    public AbilitySystemComponent? SourceAsc { get; }
    public AbilitySystemComponent? TargetAsc { get; }
    public IReadOnlyDictionary<string, float> SetByCaller { get; }
    public Guid? Handle { get; }

    public GameplayEffectSpec(
        GameplayEffectDefDto def,
        AbilitySystemComponent? sourceAsc = null,
        AbilitySystemComponent? targetAsc = null,
        Dictionary<string, float>? setByCaller = null,
        Guid? handle = null)
    {
        Def = def ?? throw new ArgumentNullException(nameof(def));
        SourceAsc = sourceAsc;
        TargetAsc = targetAsc;
        SetByCaller = setByCaller ?? new Dictionary<string, float>(StringComparer.Ordinal);
        Handle = handle;
    }
}