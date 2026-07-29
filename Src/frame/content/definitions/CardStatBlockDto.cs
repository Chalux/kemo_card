using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CardStatBlockDto
{
    [JsonPropertyName("attributes")]
    public Dictionary<string, float> Attributes { get; init; } = new(StringComparer.Ordinal);

    // 兼容旧版内容字段，后续可迁移为 attributes 字典。
    [JsonPropertyName("hpCap")]
    public int HpCap { get; init; }

    [JsonPropertyName("physicalAttack")]
    public int PhysicalAttack { get; init; }

    [JsonPropertyName("physicalDefense")]
    public int PhysicalDefense { get; init; }

    [JsonPropertyName("magicAttack")]
    public int MagicAttack { get; init; }

    [JsonPropertyName("magicDefense")]
    public int MagicDefense { get; init; }

    [JsonPropertyName("healPower")]
    public int HealPower { get; init; }

    [JsonPropertyName("physicalShield")]
    public int PhysicalShield { get; init; }

    [JsonPropertyName("magicShield")]
    public int MagicShield { get; init; }

    [JsonPropertyName("damageScale")]
    public int DamageScale { get; init; }

    [JsonPropertyName("damageTakenScale")]
    public int DamageTakenScale { get; init; }

    [JsonPropertyName("taunt")]
    public int Taunt { get; init; }

    [JsonPropertyName("drawCount")]
    public int DrawCount { get; init; }

    [JsonPropertyName("maxEnergy")]
    public int MaxEnergy { get; init; }

    [JsonPropertyName("initialEnergy")]
    public int InitialEnergy { get; init; }
}