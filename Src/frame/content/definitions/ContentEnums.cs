namespace KemoCard.Frame.Content.Definitions;

public static class ElementFlags
{
    public const int None = 0;
    public const int Red = 1 << 0;
    public const int Blue = 1 << 1;
    public const int Green = 1 << 2;
    public const int Yellow = 1 << 3;
    public const int Yin = 1 << 4;
    public const int Yang = 1 << 5;
}

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
}

[Flags]
public enum EElement
{
    None = 0,
    Red = 1,
    Blue = 1 << 2,
    Green = 1 << 3,
    Yellow = 1 << 4,
    Yin = 1 << 5,
    Yang = 1 << 6,
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