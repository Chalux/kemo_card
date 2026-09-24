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

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}