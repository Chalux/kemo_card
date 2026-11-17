using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class EnemyIntentAndDamageTests
{
    [TestCase("awakening_brothers")]
    [TestCase("forest_black_bears")]
    [TestCase("undead_discovery")]
    public void Real_character_party_loses_health_from_enemy_attacks(string battleId)
    {
        using var sim = Start(battleId, realParty: true);
        var before = sim.PlayerTeam.SharedHp;
        sim.Presentation.Clear();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        var damage = sim.Presentation.Drain().OfType<DamageDealtEvent>()
            .Where(evt => evt.Target.Side == ECombatSide.Player).ToArray();
        Assert.That(damage.Sum(evt => evt.Amount), Is.GreaterThan(0));
        Assert.That(sim.PlayerTeam.SharedHp, Is.LessThan(before));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(before - damage.Sum(evt => evt.Amount)).Within(1));
    }

    [Test]
    public void Intent_is_available_before_enemy_turn_and_matches_execution_and_next_step()
    {
        using var sim = Start("awakening_brothers");
        var enemy = sim.EnemyTeam.Enemies[0];
        Assert.That(enemy.PlannedSkillId, Is.EqualTo("kyle_single_strike"));
        Assert.That(enemy.Intent!.SkillNameId, Is.EqualTo("skill.kyle_single_strike.name"));
        Assert.That(enemy.Intent.ActionCount, Is.EqualTo(1));
        sim.Presentation.Clear();
        sim.EnemyIntents.RefreshAll();
        Assert.That(sim.Presentation.Drain(), Is.Empty, "刷新相同意图不会重新抽选或重复发事件");
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        Assert.That(sim.Presentation.Drain().OfType<EnemyActionStartedEvent>().Single().SkillId, Is.EqualTo("kyle_single_strike"));
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        Assert.That(enemy.PlannedSkillId, Is.EqualTo("kyle_sweep"));
    }

    [Test]
    public void Broken_bar_intent_waits_for_transition_and_then_previews_the_new_loop()
    {
        using var sim = Start("awakening_brothers");
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.Presentation.Clear();
        DamagePipeline.Settle(sim, new(ECombatSide.Player, 0), new(ECombatSide.Enemy, 0), 99999);
        var enemy = sim.EnemyTeam.Enemies[0];
        Assert.That(enemy.Intent!.AwaitingLifeBar, Is.True);
        Assert.That(sim.Presentation.Drain().OfType<EnemyIntentChangedEvent>().Single().AwaitingLifeBar, Is.True);
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.EnemyLifeBars.PrepareAction(0);
        Assert.That(enemy.Intent.AwaitingLifeBar, Is.False);
        Assert.That(enemy.PlannedSkillId, Is.EqualTo("kyle_single_strike"));
    }

    [Test]
    public void Intent_tracks_conditional_summoning_and_action_delays()
    {
        using var sim = Start("undead_discovery");
        sim.PlayerTeam.HealShared(sim.PlayerTeam.MaxHp);
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(99999);
        sim.CheckEndConditions();
        var malice = sim.EnemyTeam.Enemies[1];
        Assert.That(malice.PlannedSkillId, Is.EqualTo("evil_thought_double"));
        DamagePipeline.Settle(sim, new(ECombatSide.Player, 0), new(ECombatSide.Enemy, 0), 99999);
        Assert.That(malice.PlannedSkillId, Is.EqualTo("evil_thought_summon_1"));
        malice.ActionCount = 3;
        sim.EnemyIntents.RefreshAll();
        Assert.That(malice.Intent!.ActionCount, Is.EqualTo(3));
    }

    [Test]
    public void Shared_health_is_not_restored_by_attribute_recalculation()
    {
        using var sim = Start("awakening_brothers");
        var team = sim.PlayerTeam;
        // Health 修饰符被移除后会留在聚合器的重算集合；这里模拟同一条合法 GAS 路径。
        var handle = Guid.NewGuid();
        team.Asc.Aggregator.SetModifiersForHandle(AttributeIds.Health, handle, []);
        team.Asc.Aggregator.RemoveModifiersForHandle(handle);
        team.ApplySharedDamage(25);
        var damaged = team.SharedHp;
        team.Asc.OnTurnEnd();
        Assert.That(team.SharedHp, Is.EqualTo(damaged));
        team.HealShared(10);
        team.Asc.OnTurnEnd();
        Assert.That(team.SharedHp, Is.EqualTo(damaged + 10), "治疗后的余量也应保留");
        team.ApplySharedDamage(team.MaxHp);
        team.Asc.OnTurnEnd();
        Assert.That(team.IsDefeated, Is.True, "重算不能复活队伍");
    }

    [Test]
    public void Random_intent_is_reserved_through_delays_and_matches_the_executed_skill()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.EnemiesMutable["test.intent.random"] = new()
        {
            Id = "test.intent.random", MaxHp = 1000,
            SkillRefs = [new() { SkillId = "kyle_single_strike" }, new() { SkillId = "kyle_sweep" }]
        };
        var character = CharacterBattleInstance.CreateForTests("test-player", new CharacterAttributes(10000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var enemy = new EnemyUnit("random-enemy", "test.intent.random", 1000) { ActionCount = 3 };
        using var sim = new CombatSimulation(new PlayerTeamState([character], 10000), new EnemyTeamState([enemy]),
            new CombatRuleEngine([]), registry);
        sim.RunBattleStart();
        var planned = enemy.PlannedSkillId;
        Assert.That(planned, Is.Not.Null);
        for (var count = 3; count > 1; count--)
        {
            for (var refresh = 0; refresh < 10; refresh++)
                sim.EnemyIntents.RefreshAll();
            sim.Presentation.Clear();
            sim.TransitionTo(ECombatPhase.Enemy);
            sim.AdvancePhase();
            Assert.That(enemy.ActionCount, Is.EqualTo(count - 1));
            Assert.That(enemy.PlannedSkillId, Is.EqualTo(planned));
            Assert.That(sim.Presentation.Drain().OfType<EnemyActionStartedEvent>(), Is.Empty);
        }
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        Assert.That(sim.Presentation.Drain().OfType<EnemyActionStartedEvent>().Single().SkillId, Is.EqualTo(planned));
    }

    private static CombatSimulation Start(string battleId, bool realParty = false)
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.CardsMutable["test.intent.hp"] = new() { Id = "test.intent.hp", Stats = new() { HpCap = 10000 } };
        var actualPartyIds = new[] { "chalux", "Kagi", "mother_machine", "betelgeuse" };
        var party = Enumerable.Range(0, 4).Select(index => new CharacterInstance(realParty
            ? registry.Store.Characters[actualPartyIds[index]]
            : new CharacterDto { Id = $"intent-{index}", Cards = ["test.intent.hp"] }, $"intent-{index}")).ToArray();
        var initialBuffs = realParty ? party.SelectMany((character, index) => character.Definition!.Passives
            .Where(passive => passive.RequiredPotential == 0)
            .Select(passive => new BattleStartBuffEntry(index, passive.BuffId, passive.Params))).ToArray() : [];
        var sim = CombatSimulationFactory.TryCreate(registry.Store.Battles[battleId], party, registry,
            new HostRng(42, "combat"), 42, null, null, "base.game", CombatRuleCatalog.CreateDefault(), out var error,
            initialBuffs: initialBuffs);
        Assert.That(sim, Is.Not.Null, error);
        sim!.RunBattleStart();
        return sim;
    }
}