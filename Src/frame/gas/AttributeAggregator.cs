namespace KemoCard.Frame.Gas;

public sealed class AttributeAggregator
{
    private readonly AttributeSet _set;
    private readonly Dictionary<string, List<AttributeModifier>> _baseModifiers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AttributeModifier>> _modifiers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<Guid, List<AttributeModifier>>> _modifiersByHandle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _consumed = new(StringComparer.Ordinal);

    /// <summary>最终聚合值发布后通知，基础值写入的中间态不通知。</summary>
    public event Action<string>? CurrentValuePublished;

    public AttributeAggregator(AttributeSet set) => _set = set;

    #region 聚合与资源消耗

    public void SetModifiers(string attributeId, IReadOnlyList<AttributeModifier> modifiers)
    {
        _baseModifiers[attributeId] = modifiers.OrderBy(m => m.Order).ToList();
        RefreshCompositeModifiers(attributeId);
        Recalculate(attributeId);
    }

    public void Recalculate(string attributeId)
    {
        var baseValue = _set.GetBaseValue(attributeId);
        if (!_modifiers.TryGetValue(attributeId, out var list) || list.Count == 0)
        {
            PublishCurrentValue(attributeId, baseValue);
            return;
        }

        if (list.Any(m => m.Op == EAttributeModifierOp.Override))
        {
            var lastOverride = list.Last(m => m.Op == EAttributeModifierOp.Override);
            PublishCurrentValue(attributeId, lastOverride.Magnitude);
            return;
        }

        var add = list.Where(m => m.Op == EAttributeModifierOp.Add).Sum(m => m.Magnitude);
        var mul = list.Where(m => m.Op == EAttributeModifierOp.Multiply)
            .Aggregate(1f, (acc, m) => acc * m.Magnitude);
        var current = (baseValue + add) * mul;
        foreach (var modifier in list.Where(m => m.Op == EAttributeModifierOp.Divide))
        {
            if (modifier.Magnitude != 0f)
                current /= modifier.Magnitude;
        }

        PublishCurrentValue(attributeId, current);
    }

    /// <summary>在最终聚合值之后记录实际消耗；重算与 Override 都不能补回已消耗的余量。</summary>
    public float ConsumeCurrentValue(string attributeId, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f)
            return 0f;
        var current = _set.GetCurrentValue(attributeId);
        if (!float.IsFinite(current) || current <= 0f)
            return 0f;
        var consumed = MathF.Min(current, amount);
        _consumed[attributeId] = _consumed.GetValueOrDefault(attributeId) + consumed;
        Recalculate(attributeId);
        return consumed;
    }

    private void PublishCurrentValue(string attributeId, float aggregated)
    {
        if (_consumed.TryGetValue(attributeId, out var consumed))
        {
            // 临时修饰被移除时抹掉不再有额度承载的消耗，避免形成后续授予的债务。
            var capacity = MathF.Max(0f, aggregated);
            _consumed[attributeId] = consumed = MathF.Min(consumed, capacity);
            aggregated = capacity - consumed;
        }
        _set.SetCurrentValue(attributeId, aggregated);
        CurrentValuePublished?.Invoke(attributeId);
    }

    public void RecalculateAll()
    {
        foreach (var attributeId in _modifiers.Keys.ToArray())
            Recalculate(attributeId);
    }

    #endregion

    #region 修正句柄

    public void SetModifiersForHandle(string attributeId, Guid handle, IReadOnlyList<AttributeModifier> modifiers, bool recalculate = true)
    {
        if (!_modifiersByHandle.TryGetValue(attributeId, out var byHandle))
        {
            byHandle = new Dictionary<Guid, List<AttributeModifier>>();
            _modifiersByHandle[attributeId] = byHandle;
        }

        byHandle[handle] = modifiers.OrderBy(m => m.Order).ToList();
        RefreshCompositeModifiers(attributeId);
        if (recalculate)
            Recalculate(attributeId);
    }

    public bool RemoveModifiersForHandle(Guid handle)
    {
        var removed = false;
        var impactedAttributes = new List<string>();
        foreach (var pair in _modifiersByHandle)
        {
            if (!pair.Value.Remove(handle))
                continue;
            removed = true;
            impactedAttributes.Add(pair.Key);
        }

        foreach (var attributeId in impactedAttributes)
            RefreshCompositeModifiers(attributeId);

        return removed;
    }

    private void RefreshCompositeModifiers(string attributeId)
    {
        var combined = new List<AttributeModifier>();

        if (_baseModifiers.TryGetValue(attributeId, out var staticModifiers))
            combined.AddRange(staticModifiers);

        if (_modifiersByHandle.TryGetValue(attributeId, out var byHandle))
        {
            foreach (var modifiers in byHandle.Values)
                combined.AddRange(modifiers);
        }

        _modifiers[attributeId] = combined.OrderBy(m => m.Order).ToList();
    }

    #endregion
}