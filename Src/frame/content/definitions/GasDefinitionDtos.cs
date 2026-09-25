using System.Text.Json.Serialization;
using KemoCard.Frame.Gas;

namespace KemoCard.Frame.Content.Definitions;

public enum EDurationPolicy
{
    Instant,
    HasDuration,
    Infinite,
}

public enum EStackingPolicy
{
    None,
    AggregateBySource,
    AggregateByTarget,
}

public enum EMagnitudeKind
{
    Scalar,
    SetByCaller,
    AttributeBased,
    Custom,

    /// <summary>
    /// 按队伍匹配人数缩放（2026-09-21 新增）：<c>perCount × 队伍中命中筛选项的角色数</c>。
    /// 用于「队伍每有 1 名黄属性·动物角色，自身最大生命 +40」这类被动。
    /// </summary>
    PartyCountScaled,

    /// <summary>
    /// 按<b>队伍生命上限</b>缩放（2026-09-25 新增）：<c>flat + floor(队伍生命上限 × ratio)</c>。
    /// 在 buff 实例创建（卡牌/技能结算）时取值一次并缓存，之后不随队伍生命上限变化——
    /// 「+1 物防，叠加 3% 队伍生命上限的数值（向下取整，仅在卡牌执行时取数值）」。
    /// </summary>
    TeamMaxHealthScaled,
}

public enum EAttributeCapture
{
    Source,
    Target,
}

public sealed class MagnitudeDefDto
{
    [JsonPropertyName("kind")]
    public EMagnitudeKind Kind { get; init; }

    [JsonPropertyName("scalar")]
    public float Scalar { get; init; }

    [JsonPropertyName("callerName")]
    public string? CallerName { get; init; }

    [JsonPropertyName("attributeId")]
    public string? AttributeId { get; init; }

    [JsonPropertyName("coefficient")]
    public float Coefficient { get; init; } = 1f;

    [JsonPropertyName("capture")]
    public EAttributeCapture Capture { get; init; }

    /// <summary><see cref="EMagnitudeKind.PartyCountScaled"/>：每个命中角色的数值。</summary>
    [JsonPropertyName("perCount")]
    public float PerCount { get; init; }

    /// <summary><see cref="EMagnitudeKind.PartyCountScaled"/>：人数统计的元素筛选（空 = 不筛）。</summary>
    [JsonPropertyName("countElementAny")]
    public List<EElement>? CountElementAny { get; init; }

    /// <summary><see cref="EMagnitudeKind.PartyCountScaled"/>：人数统计的种族筛选（空 = 不筛）。</summary>
    [JsonPropertyName("countRaceAny")]
    public List<ERace>? CountRaceAny { get; init; }

    /// <summary><see cref="EMagnitudeKind.TeamMaxHealthScaled"/>：队伍生命上限的换算比例。</summary>
    [JsonPropertyName("ratio")]
    public float Ratio { get; init; }

    /// <summary><see cref="EMagnitudeKind.TeamMaxHealthScaled"/>：在比例换算结果上再加的固定值。</summary>
    [JsonPropertyName("flat")]
    public float Flat { get; init; }

    /// <summary>
    /// <see cref="EMagnitudeKind.PartyCountScaled"/>：元素与种族筛选之间的关系。
    /// 缺省 <c>false</c> = 取"或"（描述里的 `·`）；只有显式写「且」时才置 <c>true</c>。
    /// </summary>
    [JsonPropertyName("matchAll")]
    public bool MatchAll { get; init; }
}

public sealed class AttributeModifierDefDto
{
    [JsonPropertyName("attributeId")]
    public string AttributeId { get; init; } = "";

    [JsonPropertyName("operation")]
    public EAttributeModifierOp Operation { get; init; }

    [JsonPropertyName("magnitude")]
    public MagnitudeDefDto Magnitude { get; init; } = new();

    /// <summary>
    /// 元素掩码（2026-09-21 新增，仅 <c>OrbDamageScale</c> 使用）：0 = 所有充能球（含物理/魔法球）；
    /// 否则为 <see cref="EElement"/> 位掩码（如 15 = 红|蓝|绿|黄四色属性球，4 = 仅绿属性球）。
    /// 带掩码的修正按元素拆成 <c>OrbDamageScale:&lt;Element&gt;</c> 分别记账，球结算时只吃自己那一份。
    /// </summary>
    [JsonPropertyName("elementMask")]
    public int ElementMask { get; init; }
}

public sealed class ExecutionDefDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "Damage";

    /// <summary>
    /// 伤害维度①：<c>Physical</c>（默认）/ <c>Magical</c> / <c>Elemental</c>；
    /// 旧内容也接受直接写属性名（等价于物理 + 该属性）。见 <see cref="DamageTypeParser"/>。
    /// </summary>
    [JsonPropertyName("damageType")]
    public string DamageType { get; init; } = "Physical";

    /// <summary>
    /// 伤害维度②：属性标签（<c>None</c>/<c>Red</c>/<c>Blue</c>/<c>Green</c>/<c>Yellow</c>，
    /// 多属性用 <c>,</c> 分隔）。供连携统计与克制/抗性规则读取。
    /// </summary>
    [JsonPropertyName("element")]
    public string Element { get; init; } = "";

    /// <summary>
    /// 源攻击力系数（2026-09-21 新增）：缺省 <c>1.0</c> = 100% 攻击力，即历史口径。
    /// 「3 + 25% 物攻」这类卡填 <c>0.25</c>。只缩放攻击力，<c>Amount</c> 与 <c>Damage</c> 属性不受影响。
    /// </summary>
    [JsonPropertyName("attackScale")]
    public float AttackScale { get; init; } = 1f;

    /// <summary>
    /// 攻击力来源属性覆盖（2026-09-24 新增）：非空时用它作为"源攻击力"，而不是按 <see cref="DamageType"/>
    /// 取物攻/魔攻——「6 + 100% 回复量的魔法伤害」即 <c>attackAttribute: "HealPower"</c>；
    /// 防御侧仍按 <see cref="DamageType"/> 取（魔法 → 魔防），元素维度不变。属性 id 取自 <c>content/attributes/</c>。
    /// </summary>
    [JsonPropertyName("attackAttribute")]
    public string AttackAttribute { get; init; } = "";
}

public sealed class GameplayEffectHooksDto
{
    [JsonPropertyName("onApply")]
    public List<SkillActionRefDto> OnApply { get; init; } = [];

    [JsonPropertyName("onTurnStart")]
    public List<SkillActionRefDto> OnTurnStart { get; init; } = [];

    [JsonPropertyName("onTurnEnd")]
    public List<SkillActionRefDto> OnTurnEnd { get; init; } = [];

    [JsonPropertyName("onStackChanged")]
    public List<SkillActionRefDto> OnStackChanged { get; init; } = [];

    [JsonPropertyName("onRemove")]
    public List<SkillActionRefDto> OnRemove { get; init; } = [];
}