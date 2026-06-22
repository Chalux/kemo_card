using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatContext
{
	public CombatSimulation Simulation { get; }
	public int TurnNumber { get; }

	public CombatContext(CombatSimulation simulation, int turnNumber)
	{
		Simulation = simulation;
		TurnNumber = turnNumber;
	}
}
