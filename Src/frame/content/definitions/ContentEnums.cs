namespace KemoCard.Frame.Content.Definitions;

public static class ElementFlags
{
	public const int None = 0;
	public const int Fire = 1 << 0;
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
