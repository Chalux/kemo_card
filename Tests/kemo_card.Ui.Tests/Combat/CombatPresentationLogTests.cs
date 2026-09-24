using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 战斗规格 §16 表现事件流：逻辑只在编排层记录值事件，界面 Drain 后播放。
/// 守卫事件顺序与载荷（hpAfter / 槽位索引 / 相位），以及 Drain 语义。
/// </summary>
[TestFixture]
public sealed class CombatPresentationLogTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

    [Test]
    public void Log_emit_and_drain_round_trip()
    {
        var log = new CombatPresentationLog();
        Assert.That(log.Count, Is.Zero);
        Assert.That(log.Drain(), Is.Empty);

        log.Emit(new BattleStartedEvent());
        log.Emit(new WaveStartedEvent(1));
        Assert.That(log.Count, Is.EqualTo(2));

        var drained = log.Drain();
        Assert.That(drained, Has.Count.EqualTo(2));
        Assert.That(drained[0], Is.TypeOf<BattleStartedEvent>());
        Assert.That(drained[1], Is.EqualTo(new WaveStartedEvent(1)));
        Assert.That(log.Count, Is.Zero, "Drain 后清空");
        Assert.That(log.Drain(), Is.Empty);
    }

    [Test]
    public void PlayCard_emits_energy_change_only()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
        sim.Presentation.Clear();

        var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

        Assert.That(result.Success, Is.True, result.Error);
        var events = sim.Presentation.Drain();
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.EqualTo(new EnergyChangedEvent(0, Current: 5, Available: 3, Max: 5)));
    }

    [Test]
    public void Cancel_queued_card_emits_refund_energy_change()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
        sim.TryApply(new PlayCardCommand(0, 0, Enemy0));
        sim.Presentation.Clear();

        var result = sim.TryApply(new CancelQueuedCardCommand(0));

        Assert.That(result.Success, Is.True, result.Error);
        var events = sim.Presentation.Drain();
        Assert.That(events.OfType<EnergyChangedEvent>().Single().Available, Is.EqualTo(5));
    }

    [Test]
    public void Executing_a_marked_card_emits_settle_damage_and_discard_in_order()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5, handSize: 1);
        var enemy = sim.EnemyTeam.Enemies[0];
        var hpBefore = enemy.CurrentHp;
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        sim.Presentation.Clear();

        for (var i = 0; i < 4; i++)
            Assert.That(sim.TryApply(new ConfirmCharacterCommand(i)).Success, Is.True);

        // 第四次确认推进到 CardExecution；执行阶段由 AdvancePhase 驱动。
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
        var phaseEvents = sim.Presentation.Drain();
        Assert.That(phaseEvents.OfType<PhaseChangedEvent>().Single().To, Is.EqualTo(ECombatPhase.CardExecution));

        sim.AdvancePhase();
        var events = sim.Presentation.Drain();

        var settleStart = events.OfType<CardSettleStartedEvent>().Single();
        Assert.That(settleStart.CharacterIndex, Is.Zero);
        Assert.That(settleStart.SlotIndex, Is.Zero);
        Assert.That(settleStart.CardId, Is.EqualTo(CombatSimulationTestBuilder.MarkableCardId));
        Assert.That(settleStart.Targets, Is.EqualTo(Enemy0));

        var damage = events.OfType<DamageDealtEvent>().First();
        Assert.That(damage.Target, Is.EqualTo(Enemy0[0]));
        Assert.That(damage.Source, Is.EqualTo(new CombatTargetRef(ECombatSide.Player, 0)));
        Assert.That(damage.Amount, Is.GreaterThan(0f));
        Assert.That(damage.TargetHpAfter, Is.LessThan(hpBefore));
        Assert.That(damage.TargetMaxHp, Is.EqualTo(enemy.MaxHp));
        Assert.That(damage.TargetAlive, Is.True);

        var settleEnd = events.OfType<CardSettleEndedEvent>().Single();
        var discard = events.OfType<CardDiscardedEvent>().Single(evt => evt.CharacterIndex == 0);
        Assert.That(discard.SlotIndex, Is.Zero);
        Assert.That(discard.Channel, Is.EqualTo(EDiscardChannel.CardExecution));

        var order = events.ToList();
        Assert.That(order.IndexOf(settleStart), Is.LessThan(order.IndexOf(damage)));
        Assert.That(order.IndexOf(damage), Is.LessThan(order.IndexOf(settleEnd)));
        Assert.That(order.IndexOf(settleEnd), Is.LessThan(order.IndexOf(discard)));

        // 执行阶段之后进入敌方阶段：相位事件跟随其后。
        Assert.That(events.OfType<PhaseChangedEvent>().Any(evt => evt.To == ECombatPhase.Enemy), Is.True);
    }

    [Test]
    public void AdvanceAutomaticPhases_runs_execution_and_enemy_phase_back_to_player()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5, handSize: 1);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        for (var i = 0; i < 4; i++)
            Assert.That(sim.TryApply(new ConfirmCharacterCommand(i)).Success, Is.True);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
        sim.Presentation.Clear();

        sim.AdvanceAutomaticPhases();

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player), "执行 → 敌方 → 回到玩家阶段");
        Assert.That(sim.TurnNumber, Is.EqualTo(2));
        var phases = sim.Presentation.Drain().OfType<PhaseChangedEvent>().Select(evt => evt.To).ToList();
        Assert.That(phases, Is.EqualTo(new[] { ECombatPhase.Enemy, ECombatPhase.Player }));
    }

    [Test]
    public void Damage_event_reports_target_defeated()
    {
        using var sim = CombatSimulationTestBuilder.WithQueuedCard();
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.ApplyDamage(enemy.CurrentHp - 1);
        sim.Presentation.Clear();

        sim.AdvancePhase();

        var damage = sim.Presentation.Drain().OfType<DamageDealtEvent>().First();
        Assert.That(damage.TargetHpAfter, Is.Zero);
        Assert.That(damage.TargetAlive, Is.False);
    }

    [Test]
    public void Orb_grant_and_trigger_emit_orb_events()
    {
        var registry = CombatTestHelper.CreateFullRegistry(orbs: new Dictionary<string, OrbTypeDto>(StringComparer.Ordinal)
        {
            [BuiltinOrbTypes.Blue] = new()
            {
                Id = BuiltinOrbTypes.Blue,
                DamageKind = EDamageKind.Elemental,
                Element = EElement.Blue,
                PerOrbAmount = 6f,
                AttackBonusScale = 1f,
                AttackSource = EOrbAttackSource.Higher,
            },
        });
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests(
                $"c{i}",
                new Dictionary<string, float>(StringComparer.Ordinal) { [AttributeIds.MaxHealth] = 10f }))
            .ToArray();
        using var sim = new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 200)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player);
        sim.Presentation.Clear();

        sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3);
        var gained = sim.Presentation.Drain().OfType<OrbGainedEvent>().ToList();
        Assert.That(gained, Has.Count.EqualTo(3));
        Assert.That(gained.Select(evt => evt.QueueCount), Is.EqualTo(new[] { 1, 2, 3 }));

        Assert.That(sim.TryApply(new TriggerOrbsCommand()).Success, Is.True);
        var triggered = sim.Presentation.Drain().OfType<OrbsTriggeredEvent>().Single();
        Assert.That(triggered.Automatic, Is.False);
        Assert.That(triggered.OrbTypeIds, Is.EqualTo(new[] { BuiltinOrbTypes.Blue, BuiltinOrbTypes.Blue, BuiltinOrbTypes.Blue }));
    }

    [Test]
    public void Presentation_events_never_hold_runtime_object_references()
    {
        var eventTypes = typeof(CombatPresentationEvent).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(CombatPresentationEvent)) && !type.IsAbstract)
            .ToList();
        Assert.That(eventTypes, Is.Not.Empty);

        var forbidden = new[] { "EnemyUnit", "CharacterBattleInstance", "BuffInstance", "CombatSimulation", "Godot" };
        foreach (var type in eventTypes)
        {
            foreach (var property in type.GetProperties())
            {
                var name = property.PropertyType.FullName ?? property.PropertyType.Name;
                foreach (var token in forbidden)
                    Assert.That(name, Does.Not.Contain(token), $"{type.Name}.{property.Name} 持有 {token}");
            }
        }
    }
}