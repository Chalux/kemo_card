namespace KemoCard.Mod.Combat.Rules.Builtin;

public sealed class AllEnemiesDefeatedVictoryRule : ICombatRule
{
	public const string RuleId = "builtin.all_enemies_defeated_victory";
	public string Id => RuleId;
	public int Priority => 900;

	public void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision)
	{
		if (!ctx.Simulation.EnemyTeam.AllDefeated)
			return;
		if (ctx.Simulation.HasMoreWaves)
			return;
		decision.Kind = EEndDecisionKind.Victory;
	}
}
