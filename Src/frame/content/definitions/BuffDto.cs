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

    /// <summary>
    /// 暴风（2026-09-23）：该槽打出卡牌时，把附近手牌槽的牌吹散（弃置）。
    /// 与"手牌槽伤害"同属槽位减益，但惩罚是手牌而非血量：槽位定义声明载荷效果与
    /// <c>adjacentSlots</c>（向外扩散几个槽），由 <c>BuffRuntime.FireSlotCardPlayed</c> 逐槽触发。
    /// </summary>
    public const string SlotStorm = "slot.storm";

    /// <summary>特征：免疫手牌槽伤害效果（chalux 被动1）。</summary>
    public const string TraitImmuneSlotDamage = "trait.immune_slot_damage";

    /// <summary>特征：免疫手牌槽暴风效果（冯·诺依曼 被动1）——自己的手牌不会被暴风吹散。</summary>
    public const string TraitImmuneSlotStorm = "trait.immune_slot_storm";

    /// <summary>
    /// 追打（2026-09-21）：持有者在<b>不是</b>本回合普攻归属角色时，仍以
    /// <c>params.percent</c>%（缺省 100）的攻击力参与该次普攻。多个追打 buff 同时存在时取最高值。
    /// </summary>
    public const string TraitFollowUp = "trait.follow_up";

    /// <summary>特征：免疫手牌槽封印（封印以 <c>combat.state.sealed</c> 标签承载）。</summary>
    public const string TraitImmuneSeal = "trait.immune_seal";

    /// <summary>仅槽位 buff：该槽当前牌的费用视为 0（<c>CardCostCalculator</c> 读它）。</summary>
    public const string SlotFreeCost = "slot.free_cost";

    /// <summary>特征：免疫中毒（带 <c>debuff.poison</c> 标签的效果对该角色无效）。</summary>
    public const string TraitImmunePoison = "trait.immune_poison";
}

/// <summary>buff 的投放范围：被动等团队型 buff 声明挂到每个队友。</summary>
public enum EBuffApplyScope
{
    Self,
    AllAllies,
}

/// <summary>
/// 连携属性注入的一条规则（2026-09-26 参数化，取代 <c>trait.chain_inject_red</c> /
/// <c>trait.chain_yellow_counts_blue</c>）：打出 <see cref="From"/> 中任一属性的卡时，
/// 该卡在连携统计里额外计入 <see cref="Add"/> 的属性；<see cref="From"/> 为空 = 任意卡。
/// 配 <c>applyScope: AllAllies</c> 即全队生效。
/// </summary>
public sealed class ChainElementInjectDto
{
    [JsonPropertyName("from")]
    public List<EElement> From { get; init; } = [];

    [JsonPropertyName("add")]
    public List<EElement> Add { get; init; } = [];
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

    /// <summary>持有者打出的卡牌<b>结算完成后</b>触发（逐张，本回合内累计；2026-09-21 新增）。</summary>
    [JsonPropertyName("onCardSettled")]
    public List<EffectRefDto> OnCardSettled { get; init; } = [];

    /// <summary>
    /// <b>本回合全部卡牌结算结束</b>后触发一次（普攻之前；2026-09-21 新增）。
    /// 与 onCardSettled 的区别是"整段结算结束"，适合需要"本回合共打出几张"这类终局统计的效果。
    /// </summary>
    [JsonPropertyName("onCardExecutionEnd")]
    public List<EffectRefDto> OnCardExecutionEnd { get; init; } = [];

    /// <summary>充能球触发结算后触发（每个参与产球的角色各一次；2026-09-21 新增）。</summary>
    [JsonPropertyName("onOrbTriggered")]
    public List<EffectRefDto> OnOrbTriggered { get; init; } = [];

    /// <summary>
    /// 持有者<b>受到一次敌方来源伤害</b>后触发（2026-09-25 新增）。
    /// 同一批次（一次敌方技能）内的全部伤害先结算完，再按受击次数逐次触发——
    /// 中毒 / 手牌槽伤害等由持有者自身结算的来源不计入（见战斗规格「受击钩子」）。
    /// </summary>
    [JsonPropertyName("onDamaged")]
    public List<EffectRefDto> OnDamaged { get; init; } = [];
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

    /// <summary>
    /// 持有者条件（2026-09-26 统一为战斗条件列表）：空列表 = 无条件常驻；
    /// 不满足时 buff 休眠（不参与属性聚合、不显示图标、不触发钩子）但不移除，
    /// 条件随战斗中种族/属性变化自动激活/休眠。主体 = 持有者。
    /// </summary>
    [JsonPropertyName("conditions")]
    public List<ConditionRefDto> Conditions { get; init; } = [];

    /// <summary>
    /// 连携属性注入（2026-09-26 参数化）：打出 <c>from</c> 中任一属性的卡时，
    /// 该卡在连携统计里额外计入 <c>add</c> 的属性；<c>from</c> 缺省 = 任意卡。
    /// 典型：<c>[{ "add": ["Red"] }]</c>（红卡计入红）、<c>[{ "from": ["Yellow"], "add": ["Blue"] }]</c>。
    /// </summary>
    [JsonPropertyName("chainElementInject")]
    public List<ChainElementInjectDto> ChainElementInject { get; init; } = [];

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