using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Gas;

public static class CombatGasBridge
{
	public static AbilitySystemComponent? ResolveTargetAsc(CombatSimulation simulation, CombatTargetRef target)
	{
		ArgumentNullException.ThrowIfNull(simulation);

		if (target.Side == ECombatSide.Player)
		{
			if (target.Index < 0)
				return simulation.PlayerTeam.Asc;
			if (target.Index < simulation.PlayerTeam.Characters.Count)
				return simulation.PlayerTeam.Characters[target.Index].Asc;
			return null;
		}

		if (target.Index < 0 || target.Index >= simulation.EnemyTeam.Enemies.Count)
			return null;
		return simulation.EnemyTeam.Enemies[target.Index].Asc;
	}

	public static AbilitySystemComponent? ResolveSourceAsc(CombatSimulation simulation, CombatTargetRef source)
	{
		ArgumentNullException.ThrowIfNull(simulation);
		return ResolveTargetAsc(simulation, source);
	}
}
