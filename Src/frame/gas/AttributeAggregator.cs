namespace KemoCard.Frame.Gas;

public sealed class AttributeAggregator
{
    private readonly AttributeSet _set;
    private readonly Dictionary<string, List<AttributeModifier>> _baseModifiers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AttributeModifier>> _modifiers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<Guid, List<AttributeModifier>>> _modifiersByHandle = new(StringComparer.Ordinal);

    public AttributeAggregator(AttributeSet set) => _set = set;

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
            _set.SetCurrentValue(attributeId, baseValue);
            return;
        }

        if (list.Any(m => m.Op == EAttributeModifierOp.Override))
        {
            var lastOverride = list.Last(m => m.Op == EAttributeModifierOp.Override);
            _set.SetCurrentValue(attributeId, lastOverride.Magnitude);
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

        _set.SetCurrentValue(attributeId, current);
    }

    public void RecalculateAll()
    {
        foreach (var attributeId in _modifiers.Keys)
            Recalculate(attributeId);
    }

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
}