using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Frame.Gas.Magnitude;

namespace KemoCard.Frame.Gas;

public sealed record ApplyGameplayEffectResult(bool Success, string? Error = null, Guid? Handle = null);

public sealed class AbilitySystemComponent
{
    public AttributeSet Attributes { get; } = new();
    public AttributeAggregator Aggregator { get; }
    public GameplayTagContainer Tags { get; } = new();

    private readonly List<ActiveGameplayEffect> _activeEffects = [];
    private readonly MagnitudeEvaluator _magnitudeEvaluator = new();
    private readonly ExecutionRunner _executionRunner = new();

    public AbilitySystemComponent()
    {
        Aggregator = new AttributeAggregator(Attributes);
    }

    public float GetCurrentValue(string attributeId) => Attributes.GetCurrentValue(attributeId);

    public float GetBaseValue(string attributeId) => Attributes.GetBaseValue(attributeId);

    /// <summary>
    /// 修改属性基础值，并按当前修饰符重算当前值。
    /// </summary>
    /// <remarks>
    /// 持有 ASC 的调用方必须走这里，不要直接调用 <see cref="AttributeSet.SetBaseValue"/>：
    /// 后者会把 <c>CurrentValue</c> 直接写成 baseValue，等于抹掉聚合进来的修饰符贡献
    /// （例如队伍 ASC 上域 GameplayEffect 提供的 MaxHealth 修饰），且不会触发重算。
    /// </remarks>
    public void SetBaseValue(string attributeId, float baseValue)
    {
        Attributes.SetBaseValue(attributeId, baseValue);
        Aggregator.Recalculate(attributeId);
    }

    public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => _activeEffects;

    public IGameplayEffectHookDispatcher? HookDispatcher { get; set; }

    public Action<ActiveGameplayEffect>? OnPeriodicTriggered { get; set; }

    public void OnTurnStart()
    {
        var suspensionChanged = false;

        foreach (var effect in _activeEffects)
        {
            if (effect.IsExpired)
                continue;

            if (UpdateSuspensionState(effect, deferRecalculate: true))
                suspensionChanged = true;

            if (effect.IsSuspended)
                continue;

            if (effect.OnTurnStart())
                OnPeriodicTriggered?.Invoke(effect);

            var hooks = effect.Def.Hooks;
            if (HookDispatcher is not null && hooks.OnTurnStart.Count > 0)
                HookDispatcher.DispatchTurnStart(effect, hooks.OnTurnStart);
        }

        if (suspensionChanged)
            Aggregator.RecalculateAll();
    }

    public void OnTurnEnd()
    {
        var expired = new List<ActiveGameplayEffect>();

        foreach (var effect in _activeEffects)
        {
            if (effect.IsExpired)
                continue;

            effect.OnTurnEnd();

            if (effect.IsExpired)
                expired.Add(effect);
            else
            {
                var hooks = effect.Def.Hooks;
                if (HookDispatcher is not null && hooks.OnTurnEnd.Count > 0)
                    HookDispatcher.DispatchTurnEnd(effect, hooks.OnTurnEnd);
            }
        }

        foreach (var effect in expired)
        {
            var hooks = effect.Def.Hooks;
            if (HookDispatcher is not null && hooks.OnRemove.Count > 0)
                HookDispatcher.DispatchRemove(effect, hooks.OnRemove);
            RemoveActiveEffectInternal(effect.Handle);
        }

        if (expired.Count > 0)
            RefreshGrantedTags();

        Aggregator.RecalculateAll();
    }

    public ApplyGameplayEffectResult ApplyGameplayEffect(GameplayEffectSpec spec)
    {
        if (spec is null)
            return new ApplyGameplayEffectResult(false, "Spec is null.");

        if (spec.TargetAsc is not null && !ReferenceEquals(spec.TargetAsc, this))
            return new ApplyGameplayEffectResult(false, "Spec target does not match current ASC.");

        if (!CanApplyGameplayEffect(spec))
            return new ApplyGameplayEffectResult(false, "Tag requirement check failed.");

        RemoveEffectsMatchingTags(spec.Def.RemoveEffectsWithTags);

        return spec.Def.DurationPolicy switch
        {
            EDurationPolicy.Instant => ApplyInstantEffect(spec),
            EDurationPolicy.HasDuration or EDurationPolicy.Infinite => ApplyActiveEffect(spec),
            _ => new ApplyGameplayEffectResult(false, "Unknown duration policy."),
        };
    }

    public bool RemoveActiveEffect(Guid handle)
    {
        if (!RemoveActiveEffectInternal(handle))
            return false;

        RefreshGrantedTags();
        Aggregator.RecalculateAll();
        return true;
    }

    private bool RemoveActiveEffectInternal(Guid handle)
    {
        var index = _activeEffects.FindIndex(effect => effect.Handle == handle);
        if (index < 0)
            return false;

        _activeEffects.RemoveAt(index);
        Aggregator.RemoveModifiersForHandle(handle);
        return true;
    }

    private ApplyGameplayEffectResult ApplyInstantEffect(GameplayEffectSpec spec)
    {
        var changedAttributes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modifierDef in spec.Def.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifierDef.AttributeId))
                continue;

            var magnitude = EvaluateMagnitude(spec, modifierDef.Magnitude);
            ApplyInstantModifier(modifierDef.AttributeId, modifierDef.Operation, magnitude);
            changedAttributes.Add(modifierDef.AttributeId);
        }

        foreach (var attributeId in changedAttributes)
            Aggregator.Recalculate(attributeId);

        if (spec.Def.Executions.Count > 0)
            _executionRunner.Run(spec, this);

        return new ApplyGameplayEffectResult(true);
    }

    private ApplyGameplayEffectResult ApplyActiveEffect(GameplayEffectSpec spec)
    {
        var existing = FindStackTarget(spec);
        if (existing is not null)
        {
            existing.AddStack();
            RegisterEffectModifiers(existing);
            RefreshGrantedTags();
            return new ApplyGameplayEffectResult(true, Handle: existing.Handle);
        }

        var active = new ActiveGameplayEffect(spec);
        if (active.IsExpired)
            return new ApplyGameplayEffectResult(false, "Effect expired immediately.");

        UpdateSuspensionState(active);
        _activeEffects.Add(active);
        RegisterEffectModifiers(active);
        RefreshGrantedTags();
        return new ApplyGameplayEffectResult(true, Handle: active.Handle);
    }

    private ActiveGameplayEffect? FindStackTarget(GameplayEffectSpec spec)
    {
        if (spec.Def.StackingPolicy == EStackingPolicy.None)
            return null;

        foreach (var effect in _activeEffects)
        {
            if (!string.Equals(effect.Def.Id, spec.Def.Id, StringComparison.Ordinal))
                continue;

            if (spec.Def.StackingPolicy == EStackingPolicy.AggregateByTarget)
                return effect;

            if (spec.Def.StackingPolicy == EStackingPolicy.AggregateBySource &&
                ReferenceEquals(effect.Spec.SourceAsc, spec.SourceAsc))
                return effect;
        }

        return null;
    }

    private void RegisterEffectModifiers(ActiveGameplayEffect effect, bool recalculate = true)
    {
        if (effect.IsExpired || effect.IsSuspended)
            return;

        var grouped = new Dictionary<string, List<AttributeModifier>>(StringComparer.Ordinal);
        foreach (var modifierDef in effect.Def.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifierDef.AttributeId))
                continue;

            var magnitude = EvaluateMagnitude(effect.Spec, modifierDef.Magnitude) * effect.Stacks;
            if (!grouped.TryGetValue(modifierDef.AttributeId, out var modifiers))
            {
                modifiers = [];
                grouped[modifierDef.AttributeId] = modifiers;
            }

            modifiers.Add(new AttributeModifier(
                modifierDef.Operation,
                magnitude,
                sourceHandle: effect.Handle));
        }

        foreach (var pair in grouped)
        {
            Aggregator.SetModifiersForHandle(pair.Key, effect.Handle, pair.Value, recalculate);
        }
    }

    private float EvaluateMagnitude(GameplayEffectSpec spec, MagnitudeDefDto magnitudeDef)
    {
        return _magnitudeEvaluator.Evaluate(
            magnitudeDef,
            new MagnitudeEvaluationContext
            {
                SourceAsc = spec.SourceAsc,
                TargetAsc = this,
                SetByCaller = spec.SetByCaller.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            });
    }

    private void ApplyInstantModifier(string attributeId, EAttributeModifierOp operation, float magnitude)
    {
        var baseValue = Attributes.GetBaseValue(attributeId);
        var updated = operation switch
        {
            EAttributeModifierOp.Add => baseValue + magnitude,
            EAttributeModifierOp.Multiply => baseValue * magnitude,
            EAttributeModifierOp.Divide => magnitude == 0f ? baseValue : baseValue / magnitude,
            EAttributeModifierOp.Override => magnitude,
            _ => baseValue,
        };
        SetBaseValue(attributeId, updated);
    }

    private bool CanApplyGameplayEffect(GameplayEffectSpec spec)
    {
        if (spec.Def.ApplicationRequiredTags.Count > 0 &&
            !Tags.HasAll(spec.Def.ApplicationRequiredTags))
            return false;

        if (spec.Def.ApplicationBlockedTags.Count > 0 &&
            Tags.HasAny(spec.Def.ApplicationBlockedTags))
            return false;

        foreach (var immunityTag in spec.Def.ImmunityTags)
        {
            if (Tags.HasImmunityTo(immunityTag))
                return false;
        }

        return true;
    }

    private void RemoveEffectsMatchingTags(IReadOnlyList<string> removeTags)
    {
        if (removeTags.Count == 0)
            return;

        var toRemove = new List<Guid>();
        foreach (var effect in _activeEffects)
        {
            if (EffectMatchesAnyRemoveTag(effect, removeTags))
                toRemove.Add(effect.Handle);
        }

        foreach (var handle in toRemove)
            RemoveActiveEffectInternal(handle);

        if (toRemove.Count > 0)
        {
            RefreshGrantedTags();
            Aggregator.RecalculateAll();
        }
    }

    private static bool EffectMatchesAnyRemoveTag(ActiveGameplayEffect effect, IReadOnlyList<string> removeTags)
    {
        foreach (var grantedTag in effect.Def.GrantedTags)
        {
            foreach (var removeTag in removeTags)
            {
                if (GameplayTag.Matches(grantedTag, removeTag))
                    return true;
            }
        }

        return false;
    }

    private void RefreshGrantedTags()
    {
        Tags.ClearGrantedTags();
        foreach (var effect in _activeEffects)
        {
            if (effect.IsExpired || effect.IsSuspended)
                continue;

            foreach (var tag in effect.Def.GrantedTags)
                Tags.AddGrantedTag(tag);
        }
    }

    private bool UpdateSuspensionState(ActiveGameplayEffect effect, bool deferRecalculate = false)
    {
        if (effect.IsExpired)
            return false;

        var shouldSuspend = effect.Def.OngoingRequiredTags.Count > 0 &&
            !Tags.HasAll(effect.Def.OngoingRequiredTags);
        if (effect.IsSuspended == shouldSuspend)
            return false;

        effect.IsSuspended = shouldSuspend;
        if (shouldSuspend)
            Aggregator.RemoveModifiersForHandle(effect.Handle);
        else
            RegisterEffectModifiers(effect, recalculate: !deferRecalculate);

        RefreshGrantedTags();
        return true;
    }
}