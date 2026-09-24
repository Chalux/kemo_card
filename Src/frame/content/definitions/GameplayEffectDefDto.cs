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

    /// <summary>
    /// 吸血系数（2026-09-24 新增）：&gt; 0 且**来源是玩家侧**时，本 GE 对非玩家侧目标造成的
    /// **实际生命损失**按该比例回复**队伍共享账本**（「为己方回复 10% 伤害量」= <c>0.1</c>）。
    /// 玩家侧目标的生命变化本就转到账本（见 <c>SharedHpSettlement</c>），不走这条。
    /// </summary>
    [JsonPropertyName("lifestealScale")]
    public float LifestealScale { get; init; }

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