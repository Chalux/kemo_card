using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class EnemyDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("maxHp")]
    public int MaxHp { get; init; }

    [JsonPropertyName("baseAttributes")]
    public Dictionary<string, float> BaseAttributes { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("element")]
    public EElement Element { get; init; }

    [JsonPropertyName("role")]
    public ERole Role { get; init; }

    /// <summary>
    /// 种族（可组合 Flags，2026-09-23 新增，与 <see cref="CharacterDto.Race"/> 同口径）。
    /// 敌人在此之前只有属性/职业，无法被「蓝属性·人类·学术」这类**跨属性与种族**的筛选命中
    /// （冯·诺依曼主动技领域即首个使用者）；未声明时为 <see cref="ERace.None"/>。
    /// </summary>
    [JsonPropertyName("race")]
    public ERace Race { get; init; } = ERace.None;

    [JsonPropertyName("skillRefs")]
    public List<SkillRefDto> SkillRefs { get; init; } = [];

    [JsonPropertyName("buffRefs")]
    public List<BuffRefDto> BuffRefs { get; init; } = [];

    /// <summary>独立生命条；空列表沿用 maxHp 与随机技能。首条也需明确声明。</summary>
    [JsonPropertyName("lifeBars")]
    public List<EnemyLifeBarDto> LifeBars { get; init; } = [];

    [JsonPropertyName("entrySkillRefs")]
    public List<SkillRefDto> EntrySkillRefs { get; init; } = [];

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}

public sealed class EnemyLifeBarDto
{
    [JsonPropertyName("turnStartMechanisms")]
    public List<TeamHpMechanismDto> TurnStartMechanisms { get; init; } = [];

    [JsonPropertyName("prioritySummonCondition")]
    public EnemySummonConditionDto? PrioritySummonCondition { get; init; }

    [JsonPropertyName("entrySkillRefs")]
    public List<SkillRefDto> EntrySkillRefs { get; init; } = [];

    /// <summary>满足同伴标签或召唤条件时重复此行动；标签条件结束重启常规循环，召唤条件结束续接循环。</summary>
    [JsonPropertyName("prioritySkillId")]
    public string? PrioritySkillId { get; init; }

    [JsonPropertyName("requiredAliveEnemyTag")]
    public string? RequiredAliveEnemyTag { get; init; }

    [JsonPropertyName("maxHp")]
    public int MaxHp { get; init; }

    [JsonPropertyName("actionSequence")]
    public List<EnemyActionDto> ActionSequence { get; init; } = [];
}

public sealed class EnemySummonConditionDto
{
    [JsonPropertyName("enemyId")]
    public string EnemyId { get; init; } = "";

    [JsonPropertyName("maxAlive")]
    public int MaxAlive { get; init; }

    [JsonPropertyName("maxSummons")]
    public int MaxSummons { get; init; }
}

public sealed class EnemyActionDto
{
    [JsonPropertyName("skillId")]
    public string SkillId { get; init; } = "";

    /// <summary>本次行动等待的敌方回合数；1 为下个敌方回合行动。</summary>
    [JsonPropertyName("actionCount")]
    public int ActionCount { get; init; } = 1;
}