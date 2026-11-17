using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class StoryDto
{
    [JsonPropertyName("ringEventPools")]
    public List<RingEventPoolDto> RingEventPools { get; init; } = [];

    [JsonPropertyName("shopItemIds")]
    public List<string> ShopItemIds { get; init; } = [];

    [JsonPropertyName("ringBattlePools")]
    public List<RingBattlePoolDto> RingBattlePools { get; init; } = [];

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    /// <summary>可选解锁条件（内联表达式，见条件系统规格 §8）。null/JSON null = 无门槛。</summary>
    [JsonPropertyName("unlock")]
    public JsonElement? Unlock { get; init; }

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("scriptEntry")]
    public string? ScriptEntry { get; init; }

    /// <summary>仅允许单人游玩。缺省 true（当前以单人为主）。</summary>
    [JsonPropertyName("singlePlayerOnly")]
    public bool SinglePlayerOnly { get; init; } = true;
}

public sealed class RingEventPoolDto
{
    [JsonPropertyName("ring")]
    public int Ring { get; init; }
    [JsonPropertyName("events")]
    public List<WeightedEventRefDto> Events { get; init; } = [];
}

public sealed class WeightedEventRefDto
{
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = "";
    [JsonPropertyName("weight")]
    public int Weight { get; init; } = 1;
}

public sealed class RingBattlePoolDto
{
    [JsonPropertyName("ring")]
    public int Ring { get; init; }

    [JsonPropertyName("battles")]
    public List<WeightedBattleRefDto> Battles { get; init; } = [];
}

public sealed class WeightedBattleRefDto
{
    [JsonPropertyName("battleId")]
    public string BattleId { get; init; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; init; } = 1;
}