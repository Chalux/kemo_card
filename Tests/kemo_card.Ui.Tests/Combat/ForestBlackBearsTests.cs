using KemoCard.Frame.Content;
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
public sealed class ForestBlackBearsTests
{
    private static readonly CombatTargetRef Hero = new(ECombatSide.Player, 0);

    #region 配置、入场与普通黑熊

    [Test]
    public void Shipped_bears_have_the_requested_identity_health_and_wave_order()
    {
        var registry = BaseGameContent.BuildRegistry();
        Assert.That(new ContentDefinitionValidator().Validate(registry.Store), Is.Empty);
        var definitions = new[] { "black_bear", "elite_black_bear", "black_bear_leader" }
            .Select(id => registry.Store.Enemies[id]).ToArray();
        Assert.That(definitions.SelectMany(def => def.LifeBars).Select(bar => bar.MaxHp),
            Is.EqualTo(new[] { 600, 800, 1000, 1200, 1500 }));
        foreach (var definition in definitions)
        {
            Assert.That(definition.Element, Is.EqualTo(EElement.Yellow));
            Assert.That(definition.Race, Is.EqualTo(ERace.Animal));
            foreach (var attribute in new[] { AttributeIds.PhysicalAttack, AttributeIds.MagicAttack,
                         AttributeIds.PhysicalDefense, AttributeIds.MagicDefense })
                Assert.That(definition.BaseAttributes[attribute], Is.Zero);
        }
        var battle = registry.Store.Battles["forest_black_bears"];
        Assert.That(battle.Waves[1].EnemySpawns.Select(spawn => spawn.EnemyId),
            Is.EqualTo(new[] { "black_bear", "elite_black_bear", "black_bear" }));
    }

    [Test]
    public void Ordinary_bears_enter_with_storm_and_first_wave_actions_are_offset()
    {
        using var sim = Start();
        var entries = sim.Presentation.Drain().OfType<BuffAppliedEvent>()
            .Where(evt => evt.BuffId == "bear_slot_storm").ToArray();
        Assert.That(entries, Has.Length.EqualTo(8));
        Assert.That(entries.GroupBy(evt => evt.Holder.Target.Index).Select(group => group.Count()),
            Is.EqualTo(new[] { 2, 2, 2, 2 }));
        var hp = sim.PlayerTeam.SharedHp;
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_double_strike", "bear_roar" }));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 50));
        Assert.That(sim.EnemyTeam.Enemies[1].Buffs.Find("bear_roar")!.Stacks, Is.EqualTo(1));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_roar", "bear_double_strike" }));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 100));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_double_strike", "bear_roar" }));
    }

    [Test]
    public void Storm_strength_one_discards_one_adjacent_card_and_remains_on_the_slot()
    {
        using var sim = Start();
        var character = sim.PlayerTeam.Characters[0];
        foreach (var handSlot in character.HandSlots)
            handSlot.PlaceCard("test.bear.hp", $"storm-test-{handSlot.SlotIndex}");
        var slot = character.HandSlots.First(candidate => candidate.Buffs.Find("bear_slot_storm") is not null);
        var before = character.HandSlots.Count(candidate => candidate.CardId is not null);
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(character.HandSlots.Count(candidate => candidate.CardId is not null), Is.EqualTo(before - 1));
        Assert.That(slot.Buffs.Find("bear_slot_storm"), Is.Not.Null);
    }

    [Test]
    public void Repeated_random_single_attacks_allow_both_repeated_and_different_targets()
    {
        using var sim = Start();
        var same = false;
        var different = false;
        for (var trial = 0; trial < 40; trial++)
        {
            var hits = Execute(sim, 0, "bear_double_strike").OfType<DamageDealtEvent>().ToArray();
            Assert.That(hits, Has.Length.EqualTo(2));
            Assert.That(hits.All(hit => hit.Amount == 25), Is.True);
            same |= hits[0].Target == hits[1].Target;
            different |= hits[0].Target != hits[1].Target;
        }
        Assert.That(same && different, Is.True);
    }

    #endregion

    #region 精英条件循环与群体咆哮

    [Test]
    public void Second_wave_bears_start_together_and_elite_waits_before_sweep_then_rallies_everyone()
    {
        using var sim = Start();
        ClearWave(sim);
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_double_strike", "bear_double_strike" }));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_roar", "bear_elite_sweep", "bear_roar" }));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_double_strike", "bear_rally", "bear_double_strike" }));
        Assert.That(sim.EnemyTeam.Enemies.Select(enemy => enemy.Buffs.Find("bear_roar")!.Stacks), Is.EqualTo(new[] { 3, 2, 3 }));
    }

    [Test]
    public void Elite_second_bar_supports_living_ordinary_bears_then_restarts_its_solo_cycle()
    {
        using var sim = Start();
        ClearWave(sim);
        sim.Buffs.Apply(sim, new(ECombatSide.Enemy, 1), "bear_roar");
        Hit(sim, 1);
        Assert.That(sim.EnemyTeam.Enemies[1].Buffs.Find("bear_roar"), Is.Null);
        var first = Turn(sim);
        Assert.That(first[1].SkillId, Is.EqualTo("bear_support"));
        Assert.That(sim.EnemyTeam.Enemies[1].Buffs.Find("bear_roar"), Is.Null);
        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.Find("bear_roar")!.Stacks, Is.EqualTo(2));
        Hit(sim, 0);
        Turn(sim);
        Assert.That(sim.EnemyTeam.Enemies[2].Buffs.Find("bear_roar")!.Stacks, Is.EqualTo(5));
        Hit(sim, 2);
        var hp = sim.PlayerTeam.SharedHp;
        Assert.That(Turn(sim).Single().SkillId, Is.EqualTo("bear_elite_fury"));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 100));
        Assert.That(Turn(sim).Single().SkillId, Is.EqualTo("bear_elite_roar"));
        hp = sim.PlayerTeam.SharedHp;
        Assert.That(Turn(sim).Single().SkillId, Is.EqualTo("bear_elite_fury"));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 150));
    }

    [Test]
    public void Roar_caps_at_twenty_and_is_dispellable()
    {
        using var sim = Start();
        for (var i = 0; i < 30; i++)
            Execute(sim, 0, "bear_elite_roar");
        var enemy = sim.EnemyTeam.Enemies[0];
        Assert.That(enemy.Buffs.Find("bear_roar")!.Stacks, Is.EqualTo(20));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(100));
        Assert.That(sim.Buffs.Dispel(sim, new(ECombatSide.Enemy, 0), "bear_roar"), Is.EqualTo(1));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.Zero);
    }

    #endregion

    #region 首领换条、破甲与能量

    [TestCase(0, 0)]
    [TestCase(5, 10)]
    [TestCase(12, 20)]
    public void Leader_doubles_preserved_roar_once_and_resets_other_statuses(int stacks, int expected)
    {
        using var sim = Start();
        ClearWave(sim);
        ClearWave(sim);
        for (var i = 0; i < stacks; i++)
            sim.Buffs.Apply(sim, new(ECombatSide.Enemy, 0), "bear_leader_roar");
        sim.Buffs.Apply(sim, new(ECombatSide.Enemy, 0), "bear_armor_break");
        sim.TransitionTo(ECombatPhase.CardExecution);
        Hit(sim, 0);
        var enemy = sim.EnemyTeam.Enemies[0];
        Assert.That(enemy.LifeBarIndex, Is.Zero);
        var hp = sim.PlayerTeam.SharedHp;
        var actions = Turn(sim);
        Assert.That(actions.Select(evt => evt.SkillId), Is.EqualTo(new[] { "bear_double_roar", "bear_leader_fury" }));
        Assert.That(enemy.LifeBarIndex, Is.EqualTo(1));
        Assert.That(enemy.MaxHp, Is.EqualTo(1500));
        Assert.That(enemy.Buffs.Find("bear_armor_break"), Is.Null);
        Assert.That(enemy.Buffs.Find("bear_leader_roar")?.Stacks ?? 0, Is.EqualTo(expected));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 4 * (35 + expected * 5 * 1.5)));
        Assert.That(Turn(sim).Single().SkillId, Is.EqualTo("bear_leader_triple"));
        Assert.That(enemy.Buffs.Find("bear_leader_roar")?.Stacks ?? 0, Is.EqualTo(expected));
    }

    [Test]
    public void Leader_first_bar_has_a_four_turn_cycle()
    {
        using var sim = Start();
        ClearWave(sim);
        ClearWave(sim);
        var actions = Enumerable.Range(0, 8).Select(_ => Turn(sim).SingleOrDefault()?.SkillId).ToArray();
        Assert.That(actions, Is.EqualTo(new string?[] { "bear_leader_double", "bear_leader_roar", null, "bear_leader_sweep",
            "bear_leader_double", "bear_leader_roar", null, "bear_leader_sweep" }));
    }

    [Test]
    public void Each_leader_hit_applies_its_own_armor_break_and_durations_do_not_refresh_each_other()
    {
        using var sim = Start();
        ClearWave(sim);
        ClearWave(sim);
        var character = sim.PlayerTeam.Characters[0];
        character.Asc.SetBaseValue(AttributeIds.Taunt, 100);
        var events = Execute(sim, 0, "bear_leader_double");
        Assert.That(events.OfType<DamageDealtEvent>().Select(hit => hit.Amount), Is.EqualTo(new[] { 25f, 31f }));
        Assert.That(character.Buffs.All.Count(buff => buff.Def.Id == "bear_armor_break"), Is.EqualTo(2));
        sim.Buffs.FireTurnEnd(sim);
        events = Execute(sim, 0, "bear_leader_triple");
        Assert.That(events.OfType<DamageDealtEvent>().Select(hit => hit.Amount), Is.EqualTo(new[] { 47f, 53f, 59f }));
        Assert.That(character.Buffs.All.Count(buff => buff.Def.Id == "bear_armor_break"), Is.EqualTo(5));
        Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(-30));
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(character.Buffs.All.Count(buff => buff.Def.Id == "bear_armor_break"), Is.EqualTo(3));
        Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(-18));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(character.Buffs.Find("bear_armor_break"), Is.Null);
    }

    [Test]
    public void Leader_entry_reduces_the_energy_allowance_once_without_changing_its_cap()
    {
        using var sim = Start();
        ClearWave(sim);
        var before = sim.PlayerTeam.Characters.Select(character => character.CurrentEnergy).ToArray();
        ClearWave(sim);
        for (var i = 0; i < 4; i++)
        {
            var character = sim.PlayerTeam.Characters[i];
            // 入场扣额度后，换波照常开始下一回合并再生1点。
            Assert.That(character.CurrentEnergy, Is.EqualTo(Math.Min(Math.Max(0, before[i] - 2) + 1, 10)));
            Assert.That(character.AvailableEnergy, Is.EqualTo(character.CurrentEnergy));
            Assert.That(character.MaxEnergy, Is.EqualTo(10));
        }
        before = sim.PlayerTeam.Characters.Select(character => character.CurrentEnergy).ToArray();
        Hit(sim, 0);
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.CurrentEnergy), Is.EqualTo(before));
    }

    #endregion

    [Test]
    public void Invalid_start_index_random_repeat_and_unpaired_priority_are_rejected()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.BattlesMutable["bad-start"] = new()
        {
            Id = "bad-start", Waves = [new() { EnemySpawns = [new() { EnemyId = "black_bear", InitialActionIndex = 2 }] }]
        };
        registry.Store.SkillsMutable["bad-repeat"] = new()
        {
            Id = "bad-repeat", EnemyRandomTargetRepeats = 2,
            TargetOverride = new() { Side = ETargetSide.Enemy, Scope = ETargetScope.All }
        };
        registry.Store.EnemiesMutable["bad-priority"] = new()
        {
            Id = "bad-priority", MaxHp = 10, SkillRefs = [new() { SkillId = "bear_roar" }],
            LifeBars = [new() { MaxHp = 10, ActionSequence = [new() { SkillId = "bear_roar" }], PrioritySkillId = "bear_roar" }]
        };
        var errors = new ContentDefinitionValidator().Validate(registry.Store);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-start"), Is.True);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-repeat"), Is.True);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-priority"), Is.True);
        Assert.That(CombatSimulationFactory.SpawnWaveEnemies(registry.Store.Battles["bad-start"].Waves[0], registry, out _), Is.Null);
    }

    private static CombatPresentationEvent[] Execute(CombatSimulation sim, int enemyIndex, string skillId)
    {
        sim.Presentation.Clear();
        var skill = sim.Definitions.Store.Skills[skillId];
        SkillPayloadExecutor.Execute(sim, skill, new(ECombatSide.Enemy, enemyIndex),
            CombatTargetResolver.ResolveEnemySkillTargets(sim, skill, enemyIndex));
        return sim.Presentation.Drain().ToArray();
    }

    private static void Hit(CombatSimulation sim, int enemyIndex) =>
        DamagePipeline.Settle(sim, Hero, new(ECombatSide.Enemy, enemyIndex), 100000);

    private static void ClearWave(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.Player);
        for (var i = 0; i < sim.EnemyTeam.Enemies.Count; i++)
            while (sim.EnemyTeam.Enemies[i].IsAlive)
                Hit(sim, i);
        sim.CheckEndConditions();
    }

    private static EnemyActionStartedEvent[] Turn(CombatSimulation sim)
    {
        sim.Presentation.Clear();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        return sim.Presentation.Drain().OfType<EnemyActionStartedEvent>().ToArray();
    }

    private static CombatSimulation Start()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.CardsMutable["test.bear.hp"] = new() { Id = "test.bear.hp", Stats = new() { HpCap = 10000 } };
        var party = Enumerable.Range(0, 4).Select(index => new CharacterInstance(
            new CharacterDto { Id = $"hero-{index}", Cards = ["test.bear.hp"], InitialEnergy = 6, MaxEnergy = 10 }, $"inst-{index}")).ToArray();
        var sim = CombatSimulationFactory.TryCreate(registry.Store.Battles["forest_black_bears"], party,
            registry, new HostRng(42, "combat"), 42, null, null, "base.game", CombatRuleCatalog.CreateDefault(), out var error);
        Assert.That(sim, Is.Not.Null, error);
        sim!.RunBattleStart();
        return sim;
    }
}