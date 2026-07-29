using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class GameplayTagDefDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("parent")]
    public string? Parent { get; init; }
}