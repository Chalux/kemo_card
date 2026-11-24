using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class AwakeningBrothersTests
{
    private static readonly CombatTargetRef Boss = new(ECombatSide.Enemy, 0);
    private static readonly CombatTargetRef Hero = new(ECombatSide.Player, 0);

    #region 出货内容与入场

    [Test]
    public void Shipped_content_passes_validation_and_bosses_have_the_requested_identity()
    {
        var registry = BaseGameContent.BuildRegistry();
        Assert.That(new ContentDefinitionValidator().Validate(registry.Store), Is.Empty);
        var kyle = registry.Store.Enemies["kyle"];
        var george = registry.Store.Enemies["george"];
        Assert.Multiple(() =>
        {
            Assert.That(kyle.Element, Is.EqualTo(EElement.Red));
            Assert.That(george.Element, Is.EqualTo(EElement.Green));
            Assert.That(kyle.Race, Is.EqualTo(ERace.Human));
            Assert.That(george.Race, Is.EqualTo(ERace.Human));
            Assert.That(kyle.LifeBars.Select(bar => bar.MaxHp), Is.EqualTo(new[] { 400, 400 }));
            Assert.That(george.LifeBars.Select(bar => bar.MaxHp), Is.EqualTo(new[] { 400, 600 }));
            Assert.That(kyle.BaseAttributes[AttributeIds.PhysicalAttack], Is.EqualTo(5));
            Assert.That(george.BaseAttributes[AttributeIds.MagicAttack], Is.EqualTo(5));
        });
    }

    #endregion

    #region 破条边界

    [Test]
    public void Immediate_cards_cannot_hit_the_next_bar_before_the_boss_acts()
    {
        using var sim = Start();
        var boss = sim.EnemyTeam.Enemies[0];
        var hero = sim.PlayerTeam.Characters[0];
        var store = sim.Definitions.Store;
        store.EffectsMutable["test.break_bar"] = new()
        {
            Id = "test.break_bar", Kind = EEffectKind.Damage,
            Params = new Dictionary<string, object> { ["amount"] = 450 }
        };
        store.SkillsMutable["test.double_hit"] = new()
        {
            Id = "test.double_hit",
            EffectRefs = [new() { EffectId = "test.break_bar" }, new() { EffectId = "test.break_bar" }]
        };
        store.CardsMutable["test.double_hit"] = new()
        {
            Id = "test.double_hit", CostType = ECostType.None,
            TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.Single,
            SkillRefs = [new() { SkillId = "test.double_hit" }]
        };
        hero.HandSlots[0].PlaceCard("test.double_hit", "hit-1");
        hero.HandSlots[1].PlaceCard("test.double_hit", "hit-2");
        sim.Presentation.Clear();

        var first = sim.TryApply(new PlayCardCommand(0, 0, [Boss], "hit-1"));
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(boss.LifeBarIndex, Is.Zero);
        Assert.That(boss.CurrentHp, Is.Zero);
        Assert.That(boss.IsAwaitingLifeBar, Is.True);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.Presentation.Drain().OfType<DamageDealtEvent>().Sum(hit => hit.Amount), Is.EqualTo(400f));

        var second = sim.TryApply(new PlayCardCommand(0, 1, [Boss], "hit-2"));
        Assert.That(second.Success, Is.True, second.Error);
        Assert.That(boss.LifeBarIndex, Is.Zero);
        Assert.That(boss.CurrentHp, Is.Zero);
        Assert.That(sim.Presentation.Drain().OfType<EnemyLifeBarChangedEvent>(), Is.Empty);

        Assert.That(sim.TryApply(new EndTurnCommand()).Success, Is.True);
        Assert.That(sim.TryApply(new EndMarbleCommand()).Success, Is.True, "Boss 已受换条保护，允许立即结束弹珠");
        sim.AdvanceAutomaticPhases();
        Assert.That(boss.LifeBarIndex, Is.EqualTo(1));
        Assert.That(boss.CurrentHp, Is.EqualTo(400));
        Assert.That(boss.IsAwaitingLifeBar, Is.False);
    }

    [Test]
    public void Card_execution_keeps_zero_hp_boss_targetable_and_blocks_every_followup_hit_until_it_acts()
    {
        using var sim = Start();
        var enemy = sim.EnemyTeam.Enemies[0];
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.Presentation.Clear();
        var turn = sim.TurnNumber;
        var first = Hit(sim, 999);
        Assert.That(first.HealthLoss, Is.EqualTo(400));
        Assert.That(enemy.CurrentHp, Is.Zero);
        Assert.That(enemy.IsAwaitingLifeBar, Is.True);
        Assert.That(enemy.IsAlive, Is.True);
        Assert.That(enemy.Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.True);
        Assert.That(Hit(sim, 10000).HealthLoss, Is.Zero);
        sim.CheckEndConditions();
        Assert.That(sim.CurrentWaveIndex, Is.Zero);
        Assert.That(sim.TurnNumber, Is.EqualTo(turn));
        Assert.That(sim.Presentation.Drain().OfType<DamageDealtEvent>().Single().TargetAlive, Is.True);

        // 待行动期间新加的状态、行动延迟也在换条边界清除。
        sim.Buffs.Apply(sim, Boss, "george_chant");
        enemy.ActionCount = 5;
        var hp = sim.PlayerTeam.SharedHp;
        EnemyTurn(sim);
        Assert.That(enemy.LifeBarIndex, Is.EqualTo(1));
        Assert.That(enemy.CurrentHp, Is.EqualTo(400));
        Assert.That(enemy.IsAwaitingLifeBar, Is.False);
        Assert.That(enemy.Buffs.All, Is.Empty);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 25));
        Assert.That(enemy.ActionSequenceIndex, Is.EqualTo(1));
    }

    [TestCase(ECombatPhase.Player)]
    [TestCase(ECombatPhase.Enemy)]
    [TestCase(ECombatPhase.BattleStart)]
    public void Other_phases_switch_immediately_without_ending_turn_or_carrying_over_damage(ECombatPhase phase)
    {
        using var sim = Start();
        sim.TransitionTo(phase);
        if (phase == ECombatPhase.Enemy)
            sim.EnemyActionIndex = 0; // 敌人已开始行动，属于立即换条区间。
        var enemy = sim.EnemyTeam.Enemies[0];
        var turn = sim.TurnNumber;
        Hit(sim, 999);
        Assert.Multiple(() =>
        {
            Assert.That(enemy.LifeBarIndex, Is.EqualTo(1));
            Assert.That(enemy.CurrentHp, Is.EqualTo(400));
            Assert.That(enemy.IsAwaitingLifeBar, Is.False);
            Assert.That(enemy.Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.False);
            Assert.That(sim.TurnNumber, Is.EqualTo(turn));
            Assert.That(sim.Phase, Is.EqualTo(phase));
        });
    }

    [Test]
    public void Life_bar_reset_clears_undispellable_buffs_and_gameplay_effects_but_preserves_explicit_exceptions()
    {
        using var sim = Start();
        var enemy = sim.EnemyTeam.Enemies[0];
        sim.Buffs.Apply(sim, Boss, "george_chant");
        var persistent = enemy.Buffs.Add(new BuffDto { Id = "persistent", PersistAcrossLifeBars = true }, null);
        var transientGe = new GameplayEffectDefDto
        {
            Id = "transient-ge", DurationPolicy = EDurationPolicy.Infinite,
            GrantedTags = [BuiltinBuffTags.Undispellable]
        };
        var persistentGe = new GameplayEffectDefDto
        {
            Id = "persistent-ge", DurationPolicy = EDurationPolicy.Infinite,
            PersistAcrossLifeBars = true
        };
        enemy.Asc.ApplyGameplayEffect(new(transientGe, enemy.Asc, enemy.Asc));
        enemy.Asc.ApplyGameplayEffect(new(persistentGe, enemy.Asc, enemy.Asc));
        Hit(sim, 999);
        Assert.That(enemy.Buffs.All, Is.EqualTo(new[] { persistent }));
        Assert.That(enemy.Asc.ActiveEffects.Select(effect => effect.Def.Id), Is.EqualTo(new[] { "persistent-ge" }));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.Zero);
    }

    [Test]
    public void Pending_life_bar_cannot_be_healed_back_into_the_old_bar()
    {
        using var sim = Start();
        sim.TransitionTo(ECombatPhase.CardExecution);
        Hit(sim, 999);
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Heal(100);
        sim.Definitions.Store.EffectsMutable["test.heal"] = new()
        {
            Id = "test.heal", Kind = EEffectKind.Heal,
            Params = new Dictionary<string, object> { ["amount"] = 100 }
        };
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "test.heal" }, sim, Boss, [Boss]);
        Assert.That(enemy.CurrentHp, Is.Zero);
    }

    [Test]
    public void Breaking_a_later_enemy_before_its_turn_during_enemy_phase_also_defers_transition()
    {
        using var sim = Start();
        var extra = CombatSimulationFactory.SpawnWaveEnemies(new()
        {
            EnemySpawns = [new() { EnemyId = "kyle", Count = 2 }]
        }, sim.Definitions, out _)!;
        sim.EnemyTeam.ReplaceEnemies(extra);
        sim.EffectLifecycle.AttachHolders();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.EnemyActionIndex = 0;
        DamagePipeline.Settle(sim, Hero, new(ECombatSide.Enemy, 1), 999);
        Assert.That(extra[1].IsAwaitingLifeBar, Is.True);
        Assert.That(extra[1].LifeBarIndex, Is.Zero);
        Assert.That(extra[1].CurrentHp, Is.Zero);
        sim.AdvancePhase();
        Assert.That(extra[1].LifeBarIndex, Is.EqualTo(1));
        Assert.That(extra[1].CurrentHp, Is.EqualTo(400));
        Assert.That(extra[1].ActionSequenceIndex, Is.EqualTo(1));
    }

    #endregion

    #region 行动循环与状态

    [Test]
    public void First_bars_repeat_two_single_hits_then_one_sweep()
    {
        using var sim = Start();
        for (var wave = 0; wave < 2; wave++)
        {
            var prefix = wave == 0 ? "kyle" : "george";
            var skills = Enumerable.Range(0, 6).Select(_ => EnemyTurn(sim).Single().SkillId).ToArray();
            var single = prefix == "kyle" ? "kyle_single_strike" : "george_single_spell";
            var sweep = prefix == "kyle" ? "kyle_sweep" : "george_spell_sweep";
            Assert.That(skills, Is.EqualTo(new[] { single, single, sweep, single, single, sweep }));
            if (wave == 0)
            {
                Hit(sim, 999);
                Hit(sim, 999);
                sim.CheckEndConditions();
            }
        }
    }

    [Test]
    public void Kyle_second_bar_repeats_a_four_turn_cycle_and_delay_does_not_advance_sequence()
    {
        using var sim = Start();
        Hit(sim, 999);
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.ActionCount = 2;
        Assert.That(EnemyTurn(sim), Is.Empty);
        Assert.That(enemy.ActionSequenceIndex, Is.Zero);
        var actions = Enumerable.Range(0, 8).Select(_ => EnemyTurn(sim).SingleOrDefault()?.SkillId).ToArray();
        Assert.That(actions, Is.EqualTo(new string?[] {
            "kyle_single_strike", "kyle_weaken", null, "kyle_heavy_sweep",
            "kyle_single_strike", "kyle_weaken", null, "kyle_heavy_sweep" }));
    }

    [Test]
    public void George_second_bar_chants_to_fifteen_stacks_and_attack_uses_current_magic_attack()
    {
        using var sim = Start();
        Hit(sim, 999);
        Hit(sim, 999);
        sim.CheckEndConditions();
        Hit(sim, 999);
        var enemy = sim.EnemyTeam.Enemies[0];
        Assert.That(enemy.MaxHp, Is.EqualTo(600));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(5));
        var hp = sim.PlayerTeam.SharedHp;
        EnemyTurn(sim);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - CombatConstants.SlotCount * 20));
        EnemyTurn(sim);
        EnemyTurn(sim);
        Assert.That(enemy.Buffs.Find("george_chant")!.Stacks, Is.EqualTo(1));
        hp = sim.PlayerTeam.SharedHp;
        EnemyTurn(sim);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - CombatConstants.SlotCount * 30));
        for (var i = 0; i < 20; i++)
            sim.Buffs.Apply(sim, Boss, "george_chant");
        Assert.That(enemy.Buffs.Find("george_chant")!.Stacks, Is.EqualTo(15));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(155));
        Assert.That(sim.Buffs.Dispel(sim, Boss, "george_chant"), Is.Zero);
        Hit(sim, 999);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
    }

    [TestCase("brothers_physical_attack_down", AttributeIds.PhysicalAttack)]
    [TestCase("brothers_magic_attack_down", AttributeIds.MagicAttack)]
    public void Attack_down_instances_stack_and_expire_independently(string buffId, string attribute)
    {
        using var sim = Start();
        var character = sim.PlayerTeam.Characters[0];
        sim.Buffs.Apply(sim, Hero, buffId);
        EnemyTurn(sim);
        sim.Buffs.Apply(sim, Hero, buffId);
        Assert.That(character.Buffs.All.Count(instance => instance.Def.Id == buffId), Is.EqualTo(2));
        Assert.That(character.Asc.GetCurrentValue(attribute), Is.EqualTo(-12));
        EnemyTurn(sim);
        EnemyTurn(sim);
        Assert.That(character.Buffs.All.Count(instance => instance.Def.Id == buffId), Is.EqualTo(1));
        Assert.That(character.Asc.GetCurrentValue(attribute), Is.EqualTo(-6));
        EnemyTurn(sim);
        Assert.That(character.Buffs.Find(buffId), Is.Null);
        Assert.That(character.Asc.GetCurrentValue(attribute), Is.Zero);
    }

    #endregion

    private static DamageSettlementResult Hit(CombatSimulation sim, int amount) =>
        DamagePipeline.Settle(sim, Hero, Boss, amount);

    private static EnemyActionStartedEvent[] EnemyTurn(CombatSimulation sim)
    {
        sim.Presentation.Clear();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        return sim.Presentation.Drain().OfType<EnemyActionStartedEvent>().ToArray();
    }

    private static CombatSimulation Start()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.CardsMutable["test.hp"] = new() { Id = "test.hp", Stats = new() { HpCap = 10000 } };
        var party = Enumerable.Range(0, KemoCard.Mod.Combat.CombatConstants.SlotCount).Select(index => new CharacterInstance(
            new CharacterDto { Id = $"hero-{index}", Cards = ["test.hp"] }, $"inst-{index}")).ToArray();
        var sim = CombatSimulationFactory.TryCreate(registry.Store.Battles["awakening_brothers"], party,
            registry, new HostRng(42, "combat"), 42, null, null, "base.game", CombatRuleCatalog.CreateDefault(), out var error);
        Assert.That(sim, Is.Not.Null, error);
        sim!.RunBattleStart();
        return sim;
    }
}
