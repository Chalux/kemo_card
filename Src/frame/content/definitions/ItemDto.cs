using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class ItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("rarity")]
    public ERarity Rarity { get; init; }

    [JsonPropertyName("useSkillRefs")]
    public List<SkillRefDto> UseSkillRefs { get; init; } = [];

    [JsonPropertyName("targetSpec")]
    public TargetSpecDto? TargetSpec { get; init; }

    [JsonPropertyName("maxStack")]
    public int MaxStack { get; init; } = 1;

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("shopPrice")]
    public int? ShopPrice { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}