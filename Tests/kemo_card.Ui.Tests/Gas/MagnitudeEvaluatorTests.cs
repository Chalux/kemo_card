using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Magnitude;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class MagnitudeEvaluatorTests
{
	[Test]
	public void Scalar_magnitude_returns_constant()
	{
		var ctx = GasTestHelper.CreateMagnitudeContext();
		var eval = new MagnitudeEvaluator();
		var def = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 6f };
		Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(6f));
	}

	[Test]
	public void SetByCaller_reads_named_value_from_spec()
	{
		var ctx = GasTestHelper.CreateMagnitudeContext(setByCaller: new Dictionary<string, float> { ["Amount"] = 8f });
		var eval = new MagnitudeEvaluator();
		var def = new MagnitudeDefDto { Kind = EMagnitudeKind.SetByCaller, CallerName = "Amount" };
		Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(8f));
	}

	[Test]
	public void AttributeBased_multiplies_source_attribute()
	{
		var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var ctx = GasTestHelper.CreateMagnitudeContext(sourceAsc: source);
		var eval = new MagnitudeEvaluator();
		var def = new MagnitudeDefDto
		{
			Kind = EMagnitudeKind.AttributeBased,
			AttributeId = AttributeIds.PhysicalAttack,
			Coefficient = 1.5f,
			Capture = EAttributeCapture.Source,
		};
		Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(15f));
	}
}
