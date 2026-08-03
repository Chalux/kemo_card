using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class StoryDto
{
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