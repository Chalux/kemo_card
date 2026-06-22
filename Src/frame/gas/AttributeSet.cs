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
