using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class SkillRefDto
{
	[JsonPropertyName("skillId")]
	public string SkillId { get; init; } = "";

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}

public sealed class EffectRefDto
{
	[JsonPropertyName("effectId")]
	public string EffectId { get; init; } = "";

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}

public sealed class TargetSpecDto
{
	[JsonPropertyName("side")]
	public ETargetSide Side { get; init; }

	[JsonPropertyName("scope")]
	public ETargetScope Scope { get; init; }

	[JsonPropertyName("targetCount")]
	public int TargetCount { get; init; } = 1;

	[JsonPropertyName("retargetPolicy")]
	public ERetargetPolicy RetargetPolicy { get; init; }
}

public sealed class ConditionRefDto
{
	[JsonPropertyName("kind")]
	public string Kind { get; init; } = "";

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}

public sealed class CostScalingDto
{
	[JsonPropertyName("costType")]
	public ECostType CostType { get; init; }

	[JsonPropertyName("scalingKind")]
	public ECostScalingKind ScalingKind { get; init; }

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}
