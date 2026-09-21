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

public enum ERarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
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

[Flags]
public enum ERace
{
    None = 0,
    Human = 1,
    Canine = 1 << 1,
    Feline = 1 << 2,
    Bird = 1 << 3,
    Insect = 1 << 4,
    Beast = 1 << 5,
    Fish = 1 << 6,
    Reptile = 1 << 7,
    Plant = 1 << 8,
    Machine = 1 << 9,
    Demonic = 1 << 10,
    Angel = 1 << 11,
    Dragon = 1 << 12,
    God = 1 << 13,
    Devil = 1 << 14,
    Undead = 1 << 15,
    UnKnown = 1 << 16,
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