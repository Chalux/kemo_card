using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

/// <summary>
/// Buff tag 约定：类别（被动/主动/队长技）与机制 tag 统一走 <see cref="BuffDto.Tags"/>，
/// 不设独立字段。驱散与"被动无效/沉默"类效果按这些 tag 筛选目标。
/// </summary>
public static class BuiltinBuffTags
{
    /// <summary>被动来源（角色被动等）；"被动无效"类效果按此筛选。</summary>
    public const string Passive = "buff.passive";

    /// <summary>主动技来源的增益。</summary>
    public const string Active = "buff.active";

    /// <summary>队长技来源（预留）。</summary>
    public const string Leader = "buff.leader";

    /// <summary>不可驱散：任何清除增益的效果都不移除带此 tag 的 buff。</summary>
    public const string Undispellable = "buff.undispellable";

    /// <summary>槽位伤害效果：该槽打出卡牌时，打出者受到参数伤害（结算前触发）。</summary>
    public const string SlotDamage = "slot.damage";

    /// <summary>充能：该槽打出卡牌时计数递减，归零触发载荷并重置。</summary>
    public const string SlotCharge = "slot.charge";

    /// <summary>特征：免疫手牌槽伤害效果（chalux 被动1）。</summary>
    public const string TraitImmuneSlotDamage = "trait.immune_slot_damage";

    /// <summary>特征：连携统计时给该角色打出的卡牌额外注入红属性（chalux 被动2）。</summary>
    public const string TraitChainInjectRed = "trait.chain_inject_red";
}

/// <summary>buff 的投放范围：被动等团队型 buff 声明挂到每个队友。</summary>
public enum EBuffApplyScope
{
    Self,
    AllAllies,
}

/// <summary>
/// 持有者条件：不满足时 buff 休眠（不参与属性聚合、不显示图标、不触发钩子）但不移除，
/// 条件随战斗中种族/属性变化自动激活/休眠。列表内为"或"，跨列表默认"或"，<c>matchAll</c> 为"且"。
/// </summary>
public sealed class BuffConditionDto
{
    [JsonPropertyName("elementAny")]
    public List<EElement>? ElementAny { get; init; }

    [JsonPropertyName("raceAny")]
    public List<ERace>? RaceAny { get; init; }

    /// <summary>true 时跨列表取"且"（同时满足属性与种族）；缺省"或"（任一列表命中即满足）。</summary>
    [JsonPropertyName("matchAll")]
    public bool MatchAll { get; init; }
}

public sealed class BuffEffectHooksDto
{
    [JsonPropertyName("onApply")]
    public List<EffectRefDto> OnApply { get; init; } = [];

    [JsonPropertyName("onTurnStart")]
    public List<EffectRefDto> OnTurnStart { get; init; } = [];

    [JsonPropertyName("onTurnEnd")]
    public List<EffectRefDto> OnTurnEnd { get; init; } = [];

    [JsonPropertyName("onStackChanged")]
    public List<EffectRefDto> OnStackChanged { get; init; } = [];

    [JsonPropertyName("onRemove")]
    public List<EffectRefDto> OnRemove { get; init; } = [];

    /// <summary>每个战斗波次（阶层）开始时触发。</summary>
    [JsonPropertyName("onWaveStart")]
    public List<EffectRefDto> OnWaveStart { get; init; } = [];

    /// <summary>持有者释放主动技后触发。</summary>
    [JsonPropertyName("onActiveSkillCast")]
    public List<EffectRefDto> OnActiveSkillCast { get; init; } = [];

    /// <summary>仅槽位 buff：该槽打出卡牌时触发（结算前）。目标由效果参数 <c>hookTargets</c> 决定。</summary>
    [JsonPropertyName("onSlotCardPlayed")]
    public List<EffectRefDto> OnSlotCardPlayed { get; init; } = [];
}

public sealed class BuffDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("iconPath")]
    public string IconPath { get; init; } = "";

    [JsonPropertyName("maxStacks")]
    public int MaxStacks { get; init; } = 1;

    [JsonPropertyName("stackRule")]
    public EBuffStackRule StackRule { get; init; }

    [JsonPropertyName("durationType")]
    public EBuffDurationType DurationType { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    /// <summary>
    /// 旧布尔写法的兼容通道：<c>dispellable: false</c> 在读取时归一为 <see cref="BuiltinBuffTags.Undispellable"/> tag。
    /// 新内容一律直接写 tag。
    /// </summary>
    [JsonPropertyName("dispellable")]
    public bool? LegacyDispellable { get; init; }

    [JsonPropertyName("exclusiveGroup")]
    public string? ExclusiveGroup { get; init; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    /// <summary>属性修正：镜像 GameplayEffect 的 modifier 结构；休眠 buff 不参与聚合。</summary>
    [JsonPropertyName("modifiers")]
    public List<AttributeModifierDefDto> Modifiers { get; init; } = [];

    /// <summary>持有者条件；null = 无条件常驻。</summary>
    [JsonPropertyName("condition")]
    public BuffConditionDto? Condition { get; init; }

    [JsonPropertyName("applyScope")]
    public EBuffApplyScope ApplyScope { get; init; }

    [JsonPropertyName("hooks")]
    public BuffEffectHooksDto Hooks { get; init; } = new();

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    private IReadOnlyList<string>? _effectiveTags;

    /// <summary>
    /// 归一后的 tag 集（含旧 dispellable 布尔转换），运行时一律读这里。
    /// 结果按定义缓存（定义不可变），连携统计等热路径反复读取不再每次分配新列表。
    /// </summary>
    public IReadOnlyList<string> EffectiveTags => _effectiveTags ??= BuildEffectiveTags();

    private IReadOnlyList<string> BuildEffectiveTags()
    {
        // tags: null（JSON 显式空值）按空集处理。
        var tags = Tags ?? [];
        return LegacyDispellable == false && !tags.Contains(BuiltinBuffTags.Undispellable)
            ? [.. tags, BuiltinBuffTags.Undispellable]
            : tags;
    }
}
