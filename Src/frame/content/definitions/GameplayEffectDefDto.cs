using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class GameplayEffectDefDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("durationPolicy")]
    public EDurationPolicy DurationPolicy { get; init; }

    [JsonPropertyName("durationTurns")]
    public int DurationTurns { get; init; }

    [JsonPropertyName("periodTurns")]
    public int PeriodTurns { get; init; }

    [JsonPropertyName("stackingPolicy")]
    public EStackingPolicy StackingPolicy { get; init; }

    [JsonPropertyName("maxStacks")]
    public int MaxStacks { get; init; } = 1;

    [JsonPropertyName("modifiers")]
    public List<AttributeModifierDefDto> Modifiers { get; init; } = [];

    [JsonPropertyName("executions")]
    public List<ExecutionDefDto> Executions { get; init; } = [];

    [JsonPropertyName("grantedTags")]
    public List<string> GrantedTags { get; init; } = [];

    [JsonPropertyName("applicationRequiredTags")]
    public List<string> ApplicationRequiredTags { get; init; } = [];

    [JsonPropertyName("applicationBlockedTags")]
    public List<string> ApplicationBlockedTags { get; init; } = [];

    [JsonPropertyName("ongoingRequiredTags")]
    public List<string> OngoingRequiredTags { get; init; } = [];

    [JsonPropertyName("immunityTags")]
    public List<string> ImmunityTags { get; init; } = [];

    [JsonPropertyName("removeEffectsWithTags")]
    public List<string> RemoveEffectsWithTags { get; init; } = [];

    [JsonPropertyName("hooks")]
    public GameplayEffectHooksDto Hooks { get; init; } = new();
}