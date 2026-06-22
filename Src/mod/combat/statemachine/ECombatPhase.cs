namespace KemoCard.Mod.Combat.StateMachine;

public enum ECombatPhase
{
	BattleStart,
	Player,
	CardExecution,
	Enemy,
	WaveTransition,
	Victory,
	Defeat,
}
