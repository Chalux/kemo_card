using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CardDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("artistNameId")]
    public string ArtistNameId { get; init; } = "";

    [JsonPropertyName("element")]
    public int Element { get; init; }

    [JsonPropertyName("costType")]
    public ECostType CostType { get; init; }

    [JsonPropertyName("cost")]
    public int Cost { get; init; }

    [JsonPropertyName("baseValue")]
    public int BaseValue { get; init; }

    [JsonPropertyName("skillRefs")]
    public List<SkillRefDto> SkillRefs { get; init; } = [];

    [JsonPropertyName("role")]
    public ERole Role { get; init; } = ERole.None;

    [JsonPropertyName("hideInDex")]
    public bool HideInDex { get; init; }

    [JsonPropertyName("isExclusive")]
    public bool IsExclusive { get; init; }

    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("cardType")]
    public ECardType CardType { get; init; }

    [JsonPropertyName("targetSide")]
    public ETargetSide TargetSide { get; init; }

    [JsonPropertyName("targetScope")]
    public ETargetScope TargetScope { get; init; }

    [JsonPropertyName("targetCount")]
    public int TargetCount { get; init; } = 1;

    [JsonPropertyName("retargetPolicy")]
    public ERetargetPolicy RetargetPolicy { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("rarity")]
    public ERarity Rarity { get; init; }

    [JsonPropertyName("cardGroupId")]
    public string? CardGroupId { get; init; }

    [JsonPropertyName("upgradeTier")]
    public int UpgradeTier { get; init; }

    [JsonPropertyName("playConditions")]
    public List<ConditionRefDto> PlayConditions { get; init; } = [];

    [JsonPropertyName("costScaling")]
    public CostScalingDto? CostScaling { get; init; }

    [JsonPropertyName("animationId")]
    public string? AnimationId { get; init; }

    [JsonPropertyName("sfxId")]
    public string? SfxId { get; init; }

    [JsonPropertyName("stats")]
    public CardStatBlockDto? Stats { get; init; }
}