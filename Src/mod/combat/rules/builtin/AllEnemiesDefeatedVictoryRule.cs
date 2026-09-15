namespace KemoCard.Mod.Combat.Rules.Builtin;

public sealed class AllEnemiesDefeatedVictoryRule : ICombatRule
{
    public const string RuleId = "builtin.all_enemies_defeated_victory";
    public string Id => RuleId;
    public int Priority => 900;

    public void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision)
    {
        // 已有决定就不再覆盖：本规则 Priority(900) 低于 SharedHpDefeatRule(1000)，
        // 无条件写 Victory 会把先跑出来的「玩家账本归零」判负改写成胜利（同归于尽判胜）。
        if (decision.Kind != EEndDecisionKind.None)
            return;
        if (!ctx.Simulation.EnemyTeam.AllDefeated)
            return;
        if (ctx.Simulation.HasMoreWaves)
            return;
        decision.Kind = EEndDecisionKind.Victory;
    }
}