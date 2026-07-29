namespace KemoCard.Mod.Combat.Rules.Builtin;

public sealed class SharedHpDefeatRule : ICombatRule
{
    public const string RuleId = "builtin.shared_hp_defeat";
    public string Id => RuleId;
    public int Priority => 1000;

    public void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision)
    {
        if (ctx.Simulation.PlayerTeam.IsDefeated)
            decision.Kind = EEndDecisionKind.Defeat;
    }
}