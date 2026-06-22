using System.Text.Json.Serialization;
using KemoCard.Frame.Gas;

namespace KemoCard.Frame.Content.Definitions;

public enum EDurationPolicy
{
	Instant,
	HasDuration,
	Infinite,
}

public enum EStackingPolicy
{
	None,
	AggregateBySource,
	AggregateByTarget,
}

public enum EMagnitudeKind
{
	Scalar,
	SetByCaller,
	AttributeBased,
	Custom,
}

public enum EAttributeCapture
{
	Source,
	Target,
}

public sealed class MagnitudeDefDto
{
	[JsonPropertyName("kind")]
	public EMagnitudeKind Kind { get; init; }

	[JsonPropertyName("scalar")]
	public float Scalar { get; init; }

	[JsonPropertyName("callerName")]
	public string? CallerName { get; init; }

	[JsonPropertyName("attributeId")]
	public string? AttributeId { get; init; }

	[JsonPropertyName("coefficient")]
	public float Coefficient { get; init; } = 1f;

	[JsonPropertyName("capture")]
	public EAttributeCapture Capture { get; init; }
}

public sealed class AttributeModifierDefDto
{
	[JsonPropertyName("attributeId")]
	public string AttributeId { get; init; } = "";

	[JsonPropertyName("operation")]
	public EAttributeModifierOp Operation { get; init; }

	[JsonPropertyName("magnitude")]
	public MagnitudeDefDto Magnitude { get; init; } = new();
}

public sealed class ExecutionDefDto
{
	[JsonPropertyName("kind")]
	public string Kind { get; init; } = "Damage";

	[JsonPropertyName("damageType")]
	public string DamageType { get; init; } = "Physical";
}

public sealed class GameplayEffectHooksDto
{
	[JsonPropertyName("onApply")]
	public List<SkillActionRefDto> OnApply { get; init; } = [];

	[JsonPropertyName("onTurnStart")]
	public List<SkillActionRefDto> OnTurnStart { get; init; } = [];

	[JsonPropertyName("onTurnEnd")]
	public List<SkillActionRefDto> OnTurnEnd { get; init; } = [];

	[JsonPropertyName("onStackChanged")]
	public List<SkillActionRefDto> OnStackChanged { get; init; } = [];

	[JsonPropertyName("onRemove")]
	public List<SkillActionRefDto> OnRemove { get; init; } = [];
}
