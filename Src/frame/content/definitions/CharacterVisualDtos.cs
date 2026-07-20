using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CharacterPortraitsDto
{
    [JsonPropertyName("default")]
    public string Default { get; init; } = nameof(EPortraitKey.Neutral);

    [JsonPropertyName("entries")]
    public List<PortraitEntryDto> Entries { get; init; } = [];

    [JsonPropertyName("routes")]
    public Dictionary<string, string> Routes { get; init; } = new(StringComparer.Ordinal);
}

public sealed class PortraitEntryDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
}

public sealed class CharacterPresentationDto
{
    [JsonPropertyName("kind")]
    public EPresentationKind Kind { get; init; } = EPresentationKind.SpriteFrames;

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("defaultAnim")]
    public string DefaultAnim { get; init; } = "idle";

    [JsonPropertyName("anims")]
    public List<string>? Anims { get; init; }
}
