namespace KemoCard.Mod.Combat.Runtime;

public readonly record struct CombatTargetRef(ECombatSide Side, int Index)
{
	public static CombatTargetRef PlayerTeam => new(ECombatSide.Player, -1);
}
