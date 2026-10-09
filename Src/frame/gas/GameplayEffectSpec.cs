using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public sealed class GameplayEffectSpec
{
    public GameplayEffectDefDto Def { get; }
    public AbilitySystemComponent? SourceAsc { get; }
    public AbilitySystemComponent? TargetAsc { get; }
    public IReadOnlyDictionary<string, float> SetByCaller { get; }
    public Guid? Handle { get; }
    /// <summary>可选的宿主伤害接收器；接收未按生命余额截断的计算结果，负责结算与写入。</summary>
    public Action<ExecutionDefDto, float>? DamageReceiver { get; }
    /// <summary>宿主为 Instant 数值写入提供结算边界；生命周期钩子在该边界之外分发。</summary>
    public Action<Action>? InstantExecutionScope { get; }
    /// <summary>持有者可在 onApply 之前发布效果所属的业务槽位，确保重入看到已提交的状态。</summary>
    public Action<ActiveGameplayEffect>? ActiveEffectRegistered { get; }

    public GameplayEffectSpec(
        GameplayEffectDefDto def,
        AbilitySystemComponent? sourceAsc = null,
        AbilitySystemComponent? targetAsc = null,
        Dictionary<string, float>? setByCaller = null,
        Guid? handle = null,
        Action<ExecutionDefDto, float>? damageReceiver = null,
        Action<Action>? instantExecutionScope = null,
        Action<ActiveGameplayEffect>? activeEffectRegistered = null)
    {
        Def = def ?? throw new ArgumentNullException(nameof(def));
        SourceAsc = sourceAsc;
        TargetAsc = targetAsc;
        SetByCaller = new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(
            setByCaller is null ? new Dictionary<string, float>(StringComparer.Ordinal)
                : new Dictionary<string, float>(setByCaller, StringComparer.Ordinal));
        Handle = handle;
        DamageReceiver = damageReceiver;
        InstantExecutionScope = instantExecutionScope;
        ActiveEffectRegistered = activeEffectRegistered;
    }
}