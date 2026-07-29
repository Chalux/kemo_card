using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class EventDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("eventKind")]
    public EEventKind EventKind { get; init; }

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("pages")]
    public List<EventPageDto> Pages { get; init; } = [];

    [JsonPropertyName("options")]
    public List<EventOptionDto> Options { get; init; } = [];

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}