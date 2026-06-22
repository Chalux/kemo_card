using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Magnitude;

namespace KemoCard.Ui.Tests.Gas;

internal static class GasTestHelper
{
	public static AttributeSet CreateAttributeSet(params (string Id, float Base)[] attrs)
	{
		var set = new AttributeSet();
		foreach (var (id, baseValue) in attrs)
			set.InitAttribute(id, baseValue);
		return set;
	}

	public static AbilitySystemComponent CreateAscWithAttributes(params (string Id, float Base)[] attrs)
	{
		var asc = new AbilitySystemComponent();
		foreach (var (id, baseValue) in attrs)
			asc.Attributes.InitAttribute(id, baseValue);
		return asc;
	}

	public static MagnitudeEvaluationContext CreateMagnitudeContext(
		AbilitySystemComponent? sourceAsc = null,
		AbilitySystemComponent? targetAsc = null,
		Dictionary<string, float>? setByCaller = null) =>
		new()
		{
			SourceAsc = sourceAsc,
			TargetAsc = targetAsc,
			SetByCaller = setByCaller ?? [],
		};

	public static GameplayEffectDefDto InstantAddModifier(string attributeId, float magnitude)
	{
		return new GameplayEffectDefDto
		{
			Id = $"ge.instant.add.{attributeId}",
			DurationPolicy = EDurationPolicy.Instant,
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
			Modifiers =
			[
				new AttributeModifierDefDto
				{
					AttributeId = attributeId,
					Operation = EAttributeModifierOp.Add,
					Magnitude = new MagnitudeDefDto
					{
						Kind = EMagnitudeKind.Scalar,
						Scalar = magnitude,
					},
				},
			],
		};
	}
}
