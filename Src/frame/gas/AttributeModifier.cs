namespace KemoCard.Frame.Gas;

public sealed class AttributeModifier
{
	public EAttributeModifierOp Op { get; }
	public float Magnitude { get; }
	public int Order { get; }
	public object? SourceHandle { get; }

	public AttributeModifier(
		EAttributeModifierOp op,
		float magnitude,
		int order = 0,
		object? sourceHandle = null)
	{
		Op = op;
		Magnitude = magnitude;
		Order = order;
		SourceHandle = sourceHandle;
	}
}
