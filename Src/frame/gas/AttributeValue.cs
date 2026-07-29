namespace KemoCard.Frame.Gas;

public sealed class AttributeValue
{
    public float BaseValue { get; private set; }
    public float CurrentValue { get; internal set; }

    public AttributeValue(float baseValue)
    {
        BaseValue = baseValue;
        CurrentValue = baseValue;
    }

    public void SetBase(float value) => BaseValue = value;
}