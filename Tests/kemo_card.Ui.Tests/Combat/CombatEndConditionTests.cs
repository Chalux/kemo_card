using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEndConditionTests
{
    [Test]
    public void Shared_hp_zero_triggers_defeat()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void All_enemies_dead_triggers_victory_when_no_more_waves()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
    }

    /// <summary>
    /// 同归于尽必须判负：胜利规则 Priority(900) 低于判负规则(1000)，
    /// 此前它无条件写 Victory，会把先跑出来的判负改写成胜利。
    /// </summary>
    [Test]
    public void Simultaneous_death_resolves_to_defeat_not_victory()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        sim.CheckEndConditions();

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void Victory_rule_does_not_override_an_existing_defeat_decision()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.Defeat };
        new AllEnemiesDefeatedVictoryRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Defeat));
    }

    [Test]
    public void Victory_rule_still_decides_victory_when_nothing_decided_yet()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.None };
        new AllEnemiesDefeatedVictoryRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Victory));
    }

    [Test]
    public void Defeat_rule_marks_defeat_when_ledger_is_empty()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.None };
        new SharedHpDefeatRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Defeat));
    }
}