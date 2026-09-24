using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 界面出牌用的目标解析口径（战斗规格 §11.7.1）：哪些卡需要玩家点选、哪些自动填默认目标。
/// </summary>
[TestFixture]
public sealed class CombatTargetingTests
{
    private static CardDto Card(ETargetSide side, ETargetScope scope, int count = 1) => new()
    {
        Id = "card",
        TargetSide = side,
        TargetScope = scope,
        TargetCount = count,
    };

    [Test]
    [TestCase(ETargetSide.Enemy, ETargetScope.Single, true)]
    [TestCase(ETargetSide.Ally, ETargetScope.Single, true)]
    [TestCase(ETargetSide.Any, ETargetScope.Single, true)]
    [TestCase(ETargetSide.Self, ETargetScope.Single, false)]
    [TestCase(ETargetSide.Self, ETargetScope.Self, false)]
    [TestCase(ETargetSide.Enemy, ETargetScope.All, false)]
    [TestCase(ETargetSide.Ally, ETargetScope.Team, false)]
    [TestCase(ETargetSide.Enemy, ETargetScope.RandomN, false)]
    public void RequiresExplicitTarget_only_for_single_non_self(ETargetSide side, ETargetScope scope, bool expected)
    {
        Assert.That(CombatTargeting.RequiresExplicitTarget(Card(side, scope)), Is.EqualTo(expected));
    }

    [Test]
    public void Auto_targets_self_resolves_to_caster()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();

        var ok = CombatTargeting.TryResolveAutoTargets(sim, Card(ETargetSide.Self, ETargetScope.Self), 2, out var targets);

        Assert.That(ok, Is.True);
        Assert.That(targets, Is.EqualTo(new[] { new CombatTargetRef(ECombatSide.Player, 2) }));
    }

    [Test]
    public void Auto_targets_team_resolves_to_ledger()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();

        var ok = CombatTargeting.TryResolveAutoTargets(sim, Card(ETargetSide.Ally, ETargetScope.Team), 0, out var targets);

        Assert.That(ok, Is.True);
        Assert.That(targets, Is.EqualTo(new[] { CombatTargetRef.PlayerTeam }));
    }

    [Test]
    public void Auto_targets_all_enemies_skips_dead_units()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        sim.EnemyTeam.ReplaceEnemies(
        [
            new EnemyUnit("e0", "slime", maxHp: 10),
            new EnemyUnit("e1", "slime", maxHp: 10),
            new EnemyUnit("e2", "slime", maxHp: 10),
        ]);
        sim.EnemyTeam.Enemies[1].ApplyDamage(10);

        var ok = CombatTargeting.TryResolveAutoTargets(sim, Card(ETargetSide.Enemy, ETargetScope.All), 0, out var targets);

        Assert.That(ok, Is.True);
        Assert.That(targets, Is.EqualTo(new[]
        {
            new CombatTargetRef(ECombatSide.Enemy, 0),
            new CombatTargetRef(ECombatSide.Enemy, 2),
        }));
    }

    [Test]
    public void Auto_targets_random_n_picks_distinct_legal_targets()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        sim.EnemyTeam.ReplaceEnemies(
        [
            new EnemyUnit("e0", "slime", maxHp: 10),
            new EnemyUnit("e1", "slime", maxHp: 10),
            new EnemyUnit("e2", "slime", maxHp: 10),
        ]);

        var ok = CombatTargeting.TryResolveAutoTargets(sim, Card(ETargetSide.Enemy, ETargetScope.RandomN, count: 2), 0, out var targets);

        Assert.That(ok, Is.True);
        Assert.That(targets, Has.Count.EqualTo(2));
        Assert.That(targets.Distinct().Count(), Is.EqualTo(2));
        Assert.That(targets.All(target => target.Side == ECombatSide.Enemy), Is.True);
    }

    [Test]
    public void Explicit_single_target_card_is_not_auto_resolved()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();

        var ok = CombatTargeting.TryResolveAutoTargets(sim, Card(ETargetSide.Enemy, ETargetScope.Single), 0, out var targets);

        Assert.That(ok, Is.False);
        Assert.That(targets, Is.Empty);
        Assert.That(CombatTargeting.IsLegalTarget(sim, Card(ETargetSide.Enemy, ETargetScope.Single), 0, new CombatTargetRef(ECombatSide.Enemy, 0)), Is.True);
        Assert.That(CombatTargeting.IsLegalTarget(sim, Card(ETargetSide.Enemy, ETargetScope.Single), 0, new CombatTargetRef(ECombatSide.Player, 0)), Is.False);
    }
}