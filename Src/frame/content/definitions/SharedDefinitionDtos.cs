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

/// <summary>主动技蓄力链的一档（战斗规格 §5.1）。<c>Cap</c> 由各档 <see cref="Cooldown"/> 之和构成。</summary>
public sealed class ActiveSkillChainEntryDto
{
	[JsonPropertyName("skillId")]
	public string SkillId { get; init; } = "";

	[JsonPropertyName("cooldown")]
	public int Cooldown { get; init; }
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

public sealed class BuffRefDto
{
	[JsonPropertyName("buffId")]
	public string BuffId { get; init; } = "";

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}

public sealed class EnemySpawnDto
{
	[JsonPropertyName("enemyId")]
	public string EnemyId { get; init; } = "";

	[JsonPropertyName("count")]
	public int Count { get; init; } = 1;

	[JsonPropertyName("hpScale")]
	public double? HpScale { get; init; }

	[JsonPropertyName("skillOverrides")]
	public List<SkillRefDto>? SkillOverrides { get; init; }
}

public sealed class BattleWaveDto
{
	[JsonPropertyName("enemySpawns")]
	public List<EnemySpawnDto> EnemySpawns { get; init; } = [];

	[JsonPropertyName("waveScriptPath")]
	public string? WaveScriptPath { get; init; }
}

public sealed class RewardEntryDto
{
	[JsonPropertyName("kind")]
	public ERewardKind Kind { get; init; }

	[JsonPropertyName("weight")]
	public int Weight { get; init; } = 1;

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}

public sealed class BattleRewardDto
{
	[JsonPropertyName("entries")]
	public List<RewardEntryDto> Entries { get; init; } = [];
}

public sealed class EventPageDto
{
	[JsonPropertyName("textId")]
	public string TextId { get; init; } = "";

	[JsonPropertyName("imagePath")]
	public string? ImagePath { get; init; }
}

public sealed class EventOptionDto
{
	[JsonPropertyName("optionId")]
	public string OptionId { get; init; } = "";

	[JsonPropertyName("labelId")]
	public string LabelId { get; init; } = "";

	[JsonPropertyName("descId")]
	public string? DescId { get; init; }

	[JsonPropertyName("conditions")]
	public List<ConditionRefDto> Conditions { get; init; } = [];

	[JsonPropertyName("effectRefs")]
	public List<EffectRefDto> EffectRefs { get; init; } = [];

	[JsonPropertyName("nextPageIndex")]
	public int? NextPageIndex { get; init; }
}
