namespace KemoCard.Frame.Gas;

public sealed class AttributeSet
{
    private readonly Dictionary<string, AttributeValue> _values = new(StringComparer.Ordinal);

    public event EventHandler<AttributeChangedEventArgs>? AttributeChanged;

    public void InitAttribute(string attributeId, float baseValue)
    {
        _values[attributeId] = new AttributeValue(baseValue);
    }

    public bool HasAttribute(string attributeId) => _values.ContainsKey(attributeId);

    public float GetBaseValue(string attributeId) =>
        _values.TryGetValue(attributeId, out var v) ? v.BaseValue : 0f;

    public float GetCurrentValue(string attributeId) =>
        _values.TryGetValue(attributeId, out var v) ? v.CurrentValue : 0f;

    /// <summary>
    /// 直接写入基础值，并把 <c>CurrentValue</c> 同步为基础值。
    /// </summary>
    /// <remarks>
    /// 这是「无修饰符」语义：它会覆盖掉聚合结果。挂在 <see cref="AbilitySystemComponent"/> 上的属性
    /// 应当改用 <c>AbilitySystemComponent.SetBaseValue</c>（写入后按修饰符重算）；
    /// 只有需要临时搭建「干净工作值」的场景（例如 SharedHp 分槽结算）才直接调用本方法，
    /// 且必须自行恢复 <c>CurrentValue</c>。
    /// </remarks>
    public void SetBaseValue(string attributeId, float baseValue)
    {
        if (!_values.TryGetValue(attributeId, out var value))
        {
            InitAttribute(attributeId, baseValue);
            RaiseChanged(attributeId);
            return;
        }
        value.SetBase(baseValue);
        value.CurrentValue = baseValue;
        RaiseChanged(attributeId);
    }

    internal AttributeValue GetOrCreate(string attributeId, float defaultBase = 0f)
    {
        if (!_values.TryGetValue(attributeId, out var value))
        {
            value = new AttributeValue(defaultBase);
            _values[attributeId] = value;
        }
        return value;
    }

    internal void SetCurrentValue(string attributeId, float current)
    {
        var value = GetOrCreate(attributeId);
        value.CurrentValue = current;
        RaiseChanged(attributeId);
    }

    private void RaiseChanged(string attributeId) =>
        AttributeChanged?.Invoke(this, new AttributeChangedEventArgs(attributeId));
}

public sealed class AttributeChangedEventArgs : EventArgs
{
    public string AttributeId { get; }
    public AttributeChangedEventArgs(string attributeId) => AttributeId = attributeId;
}