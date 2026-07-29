using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class SkillDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("effectRefs")]
    public List<EffectRefDto> EffectRefs { get; init; } = [];

    [JsonPropertyName("actionRefs")]
    public List<SkillActionRefDto> ActionRefs { get; init; } = [];

    [JsonPropertyName("targetOverride")]
    public TargetSpecDto? TargetOverride { get; init; }

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("animationId")]
    public string? AnimationId { get; init; }

    [JsonPropertyName("sfxId")]
    public string? SfxId { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}