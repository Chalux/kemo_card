using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class TeamDomainManagerTests
{
    [Test]
    public void SetDomain_replaces_existing_domain_on_same_team()
    {
        var gameplayEffects = new Dictionary<string, GameplayEffectDefDto>
        {
            ["domain_a"] = new()
            {
                Id = "domain_a",
                DurationPolicy = EDurationPolicy.Infinite,
                StackingPolicy = EStackingPolicy.None,
            },
            ["domain_b"] = new()
            {
                Id = "domain_b",
                DurationPolicy = EDurationPolicy.Infinite,
                StackingPolicy = EStackingPolicy.None,
            },
        };
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: gameplayEffects);
        var enemy = new EnemyUnit("e", "slime", 10);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry);
        var manager = new TeamDomainManager(sim);

        Assert.That(manager.TrySetPlayerDomain("domain_a"), Is.True);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("domain_a"));
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects, Has.Count.EqualTo(1));

        Assert.That(manager.TrySetPlayerDomain("domain_b"), Is.True);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("domain_b"));
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects, Has.Count.EqualTo(1));
    }

    [Test]
    public void Player_and_enemy_domains_do_not_conflict()
    {
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
        {
            ["d1"] = new() { Id = "d1", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = EStackingPolicy.None },
            ["d2"] = new() { Id = "d2", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = EStackingPolicy.None },
        });
        var sim = CombatSimulationTestBuilder.Minimal(new EnemyUnit("e", "slime", 10), registry);
        var manager = new TeamDomainManager(sim);

        manager.TrySetPlayerDomain("d1");
        manager.TrySetEnemyDomain("d2");

        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("d1"));
        Assert.That(sim.EnemyTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("d2"));
    }
}