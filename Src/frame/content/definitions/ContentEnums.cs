namespace KemoCard.Frame.Content.Definitions;

public enum ECostType
{
    None,
    Energy,
    Health,
    Gold,
    Discard,
    X,
}

public enum ECardType
{
    Physics,
    Magical,
    Support,
    Guard,
    Resist,
    Weak,
    Counter,
    Healing,
    Curse,
}

public enum ECostScalingKind
{
    None,
    X,
    PerDiscard,
    PerCardInHand,
    PerEnemyAlive,
    PerAllyAlive,
}

public enum ETargetSide
{
    Self,
    Ally,
    Enemy,
    Any,
}

public enum ETargetScope
{
    Self,
    Single,
    All,
    RandomN,
    /// <summary>
    /// 对该侧队伍账本一次结算（战斗规格 §1.3）；与 <see cref="All"/> 的分槽逐次结算相区分。
    /// </summary>
    Team,
}

public enum ERetargetPolicy
{
    Default,
    RandomLegal,
    HighestHp,
    LowestHp,
    Skip,
}

/// <summary>
/// 卡牌 / 道具稀有度（2026-09-21 收敛档名）。
/// </summary>
/// <remarks>
/// 通用三档为 <see cref="Common"/> / <see cref="Rare"/> / <see cref="Legendary"/>；
/// 原 <c>Uncommon</c> 更名为 <see cref="Special"/>，原 <c>Epic</c> 更名为 <see cref="Exclusive"/>
/// （配合 `isExclusive: true` 的"角色专属卡"语义）。卡框资源与本地化文案同步改名，不保留旧名。
/// </remarks>
public enum ERarity
{
    Common,
    Special,
    Rare,
    Exclusive,
    Legendary,
}

public enum EBuffDurationType
{
    Permanent,
    Turns,
    Combat,
    UntilDispelled,
}

public enum EBuffStackRule
{
    Add,
    Refresh,
    Replace,
}

public enum EEffectKind
{
    Damage,
    Heal,
    ApplyBuff,
    RemoveBuff,
    Draw,
    Discard,
    GainResource,
    ModifyStat,
    ExecuteScript,
    ChainEffects,
    /// <summary>授予充能球（params.orbTypeId + 可选 params.count，默认 1；产球者 = 来源角色）。</summary>
    GainOrb,
    /// <summary>
    /// 给来源角色的手牌槽位挂 buff（params.buffId + params.slotIndex 或
    /// params.slotSelection: "randomNonEmpty"）。与同名技能动作同义，供 buff 钩子使用。
    /// </summary>
    AttachSlotBuff,

    /// <summary>
    /// 按"本回合打出过的卡牌数"授予充能球（params.orbTypeId + 可选 params.perCard 默认 1 +
    /// 可选 params.offset 默认 0；数量 = max(0, 出牌数 × perCard + offset)）。
    /// 「本回合打出 X 张牌则获得 X-1 个绿球」即 perCard 1 / offset -1。
    /// </summary>
    GainOrbPerPlayedCard,

    /// <summary>
    /// 按"自身卡组内命中筛选项的卡牌数"分档授予充能球：
    /// <c>params.orbTypeId</c>（必填）+ <c>params.elementMask</c>（可选，0 = 不筛属性）+
    /// <c>params.tiers</c>（<c>[[最小张数, 球数], …]</c>，取满足条件的最高档）。
    /// 「卡组内绿卡 0/4/7 张 → 2/3/4 个绿球」即 <c>tiers: [[0,2],[4,3],[7,4]]</c>。
    /// </summary>
    GainOrbByDeckCount,
}

public enum ESkillActionKind
{
    Draw,
    Discard,
    GainResource,
    ExecuteScript,
    ChainActions,
    ApplyGameplayEffect,
    RemoveGameplayEffect,
    /// <summary>投放抽牌数量修正（规格 §4.2 / §4.3），不即时抽牌。</summary>
    ModifyDrawCount,
    /// <summary>对目标挂 buff（params.buffId 必填）。</summary>
    ApplyBuff,
    /// <summary>驱散目标 buff（params.buffId 或 params.withTags）。</summary>
    RemoveBuff,
    /// <summary>给来源角色的手牌槽位挂 buff（params.buffId + params.slotIndex）。</summary>
    AttachSlotBuff,
    /// <summary>授予充能球（params.orbTypeId + 可选 params.count，默认 1；产球者 = 来源角色）。</summary>
    GainOrb,
}

[Flags]
public enum EElement
{
    None = 0,
    Red = 1,
    Blue = 1 << 1,
    Green = 1 << 2,
    Yellow = 1 << 3,
}

/// <summary>
/// 伤害的"类型"维度（与 <see cref="EElement"/> 正交的另一维）：物理 / 魔法 / 元素。
/// </summary>
/// <remarks>
/// 物理与魔法走攻防公式（<c>攻击力 − 目标对应防御</c>，见战斗规格「伤害包管线」）；
/// <see cref="Elemental"/> 是"既非物理也非魔法"的纯属性伤害（充能球的元素球、元素类效果），
/// 不吃物防/魔防。元素维度单独由 <see cref="EElement"/> 表达：物理/魔法攻击也可以带元素标签
/// （普通攻击 = 物/魔之一 + 攻击者元素）。
/// </remarks>
public enum EDamageKind
{
    Elemental,

    Physical,

    Magical,
}

public enum ERole
{
    None,
    /// <summary>
    /// 战士 物理输出
    /// </summary>
    Warrior,
    /// <summary>
    /// 术士 魔法输出
    /// </summary>
    Wizard,
    /// <summary>
    /// 治疗者 治疗
    /// </summary>
    Healer,
    /// <summary>
    /// 守护者 防御
    /// </summary>
    Guard,
    /// <summary>
    /// 护盾
    /// </summary>
    Shield,
    /// <summary>
    /// 控制者 控制
    /// </summary>
    Controller,
    /// <summary>
    /// 支援者 支援
    /// </summary>
    Support,
    /// <summary>
    /// 卡牌手 卡牌伤害
    /// </summary>
    CardPlayer,
    /// <summary>
    /// 剑士 物理普攻输出
    /// </summary>
    SwordMan,
    /// <summary>
    /// 法师 魔法普攻输出
    /// </summary>
    Mage,
    /// <summary>
    /// 炼金术士 道具制造
    /// </summary>
    Alchemist,
    /// <summary>
    /// 元素师 元素球伤害
    /// </summary>
    Elementist,
}

public enum EEventKind
{
    Data,
    Script,
}

public enum ERewardKind
{
    Gold,
    CardChoice,
    ItemGrant,
    Effect,
}

/// <summary>
/// 种族（位标志，可多选）。
/// </summary>
/// <remarks>
/// 2026-09-21 收敛：<c>Canine</c>/<c>Feline</c>/<c>Bird</c>/<c>Beast</c>/<c>Reptile</c> 五族合并为
/// <see cref="Animal"/>（动物）；<c>Demonic</c> 与 <c>Devil</c> 合并为 <see cref="Demon"/>（恶魔族）；
/// 新增 <see cref="Academic"/>（学术）/ <see cref="Fantasy"/>（幻想）/ <see cref="Astronomy"/>（天文）/
/// <see cref="Hero"/>（英雄）/ <see cref="Calamity"/>（灾厄）；原 <c>UnKnown</c> 更名为 <see cref="Unknown"/>。
/// 位值重排是安全的：种族只存在于内容 JSON（按名反序列化）与运行期 DTO，不落存档。
/// </remarks>
[Flags]
public enum ERace
{
    None = 0,
    Human = 1 << 0,
    /// <summary>动物族（原犬族 / 猫族 / 鸟族 / 兽族 / 爬行）。</summary>
    Animal = 1 << 1,
    Insect = 1 << 2,
    Fish = 1 << 3,
    Plant = 1 << 4,
    Machine = 1 << 5,
    /// <summary>恶魔族（原 Demonic + Devil）。</summary>
    Demon = 1 << 6,
    Angel = 1 << 7,
    Dragon = 1 << 8,
    God = 1 << 9,
    Undead = 1 << 10,
    /// <summary>学术。</summary>
    Academic = 1 << 11,
    /// <summary>幻想。</summary>
    Fantasy = 1 << 12,
    /// <summary>天文。</summary>
    Astronomy = 1 << 13,
    /// <summary>英雄。</summary>
    Hero = 1 << 14,
    /// <summary>灾厄。</summary>
    Calamity = 1 << 15,
    Unknown = 1 << 16,
}

public enum EPortraitKey
{
    Neutral,
    Smile,
    Angry,
    Sad,
    Surprised,
    Hurt,
    Serious,
    Happy,
    Naughty,
}

public enum EPresentationKind
{
    SpriteFrames,
    Spine,
}