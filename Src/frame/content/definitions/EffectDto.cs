using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class EffectDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("kind")]
    public EEffectKind Kind { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; init; }

    [JsonPropertyName("conditions")]
    public List<ConditionRefDto> Conditions { get; init; } = [];

    [JsonPropertyName("targetOverride")]
    public TargetSpecDto? TargetOverride { get; init; }

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("scriptEntry")]
    public string? ScriptEntry { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("effectRefs")]
    public List<EffectRefDto> EffectRefs { get; init; } = [];
}