using System.Globalization;
using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Effects;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

public sealed partial class CombatAuditRegressionTests
{
    #region 护盾资源

    [TestCase(EAttributeModifierOp.Add, 40f)]
    [TestCase(EAttributeModifierOp.Multiply, 5f)]
    [TestCase(EAttributeModifierOp.Override, 50f)]
    public void Shield_consumption_survives_all_modifier_operations_and_recalculation(EAttributeModifierOp operation, float magnitude)
    {
        var asc = new AbilitySystemComponent();
        asc.SetBaseValue(AttributeIds.Shield, 10);
        asc.Aggregator.SetModifiers(AttributeIds.Shield, [new AttributeModifier(operation, magnitude)]);
        Assert.That(asc.Aggregator.ConsumeCurrentValue(AttributeIds.Shield, 10), Is.EqualTo(10));
        asc.Aggregator.RecalculateAll();
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(40));
        Assert.That(asc.Aggregator.ConsumeCurrentValue(AttributeIds.Shield, 100), Is.EqualTo(40));
        asc.Aggregator.RecalculateAll();
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
    }

    [Test]
    public void Shield_stack_update_keeps_consumption_and_removed_capacity_does_not_create_future_debt()
    {
        var buff = new BuffDto
        {
            Id = "shield", DurationType = EBuffDurationType.Permanent, StackRule = EBuffStackRule.Add,
            MaxStacks = 2, Modifiers = [Add(AttributeIds.Shield, 50)]
        };
        var gain = new SkillActionDto { Id = "gain", Kind = ESkillActionKind.GainShield, Params = new() { ["amount"] = 10 } };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff },
            skillActions: new Dictionary<string, SkillActionDto> { [gain.Id] = gain }));
        var asc = sim.PlayerTeam.Characters[0].Asc;
        Assert.That(sim.Buffs.Apply(sim, Player(), buff.Id).Success, Is.True);
        sim.EffectExecutor.ApplyFixedDamage(sim, Enemy(), [Player()], 10);
        Assert.That(sim.Buffs.Apply(sim, Player(), buff.Id).Instance!.Stacks, Is.EqualTo(2));
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(90));
        Assert.That(sim.Buffs.Dispel(sim, Player(), buff.Id), Is.EqualTo(1));
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = gain.Id }, sim, Player(), [Player()]);
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(10));
    }

    [Test]
    public void GainShield_does_not_copy_active_modifiers_into_base_value()
    {
        var buff = new BuffDto { Id = "shield", DurationType = EBuffDurationType.Permanent, Modifiers = [Add(AttributeIds.Shield, 50)] };
        var gain = new SkillActionDto { Id = "gain", Kind = ESkillActionKind.GainShield, Params = new() { ["amount"] = 10 } };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff },
            skillActions: new Dictionary<string, SkillActionDto> { [gain.Id] = gain }));
        var asc = sim.PlayerTeam.Characters[0].Asc;
        sim.Buffs.Apply(sim, Player(), buff.Id);
        sim.EffectExecutor.ApplyFixedDamage(sim, Enemy(), [Player()], 10);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = gain.Id }, sim, Player(), [Player()]);
        Assert.That(asc.GetBaseValue(AttributeIds.Shield), Is.EqualTo(10));
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(50));
        asc.Aggregator.RecalculateAll();
        Assert.That(asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(50));
    }

    #endregion

    #region 数值与执行边界

    [TestCase("2", true, 2)]
    [TestCase("2.0", true, 2)]
    [TestCase("2.5", false, 0)]
    [TestCase("2147483647", true, int.MaxValue)]
    [TestCase("2147483648", false, 0)]
    public void Integer_parameters_have_the_same_integrality_and_range_rules_for_json_and_memory(string json, bool expected, int result)
    {
        using var document = JsonDocument.Parse(json);
        Assert.That(ContentParameters.TryInt(document.RootElement, out var fromJson), Is.EqualTo(expected));
        Assert.That(ContentParameters.TryInt(decimal.Parse(json, CultureInfo.InvariantCulture), out var fromMemory), Is.EqualTo(expected));
        if (expected)
            Assert.That((fromJson, fromMemory), Is.EqualTo((result, result)));
    }

    [Test]
    public void Numeric_parameters_are_culture_independent_and_reject_nonfinite_values()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (var value in new object[] { 1.25f, 1.25d, 1.25m, "1.25" })
                Assert.That(ContentParameters.TryFloat(value, out var parsed) && parsed == 1.25f, Is.True);
            foreach (var value in new object[] { double.MaxValue, float.NaN, double.PositiveInfinity, "NaN", "1,25" })
                Assert.That(ContentParameters.TryFloat(value, out _), Is.False);
            Assert.That(ContentParameters.TryInt(1.000000000000000000001m, out _), Is.False);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Skill_payload_preserves_override_priority_without_mutating_definition_or_reference(bool legacy)
    {
        var defaults = new Dictionary<string, object> { ["amount"] = 1 };
        var reference = new Dictionary<string, object> { ["amount"] = 2 };
        var payload = new Dictionary<string, object> { ["amount"] = 3 };
        var action = new SkillActionDto { Id = "gain", Kind = ESkillActionKind.GainShield, Params = defaults };
        var effect = new EffectDto { Id = "gain", Kind = EEffectKind.GainShield, Params = defaults };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action },
            effects: new Dictionary<string, EffectDto> { [effect.Id] = effect }));
        var skill = new SkillDto
        {
            Id = "skill", ActionRefs = legacy ? [] : [new() { ActionId = action.Id, Params = reference }],
            EffectRefs = [new() { EffectId = effect.Id, Params = reference }]
        };
        SkillPayloadExecutor.Execute(sim, skill, Player(), [Player()], payload);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(3));
        Assert.That(defaults["amount"], Is.EqualTo(1));
        Assert.That(reference["amount"], Is.EqualTo(2));
    }

    [Test]
    public void Sibling_damage_actions_share_the_hit_budget_and_next_payload_gets_a_fresh_budget()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "hit", DurationPolicy = EDurationPolicy.Instant,
            Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        var action = new SkillActionDto
        {
            Id = "hit", Kind = ESkillActionKind.ApplyGameplayEffect,
            Params = new() { ["gameplayEffectId"] = ge.Id, ["Amount"] = 1, ["AttackCount"] = 999 }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge },
            skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action }));
        sim.EnemyTeam.Enemies[0].Asc.Attributes.SetCurrentValue(AttributeIds.Health, 10000);
        var skill = new SkillDto { Id = "burst", ActionRefs = Enumerable.Range(0, 6).Select(_ => new SkillActionRefDto { ActionId = action.Id }).ToList() };
        SkillPayloadExecutor.Execute(sim, skill, Player(), [Enemy()]);
        var afterBurst = sim.EnemyTeam.Enemies[0].CurrentHp;
        Assert.That(afterBurst, Is.GreaterThanOrEqualTo(10000 - 4096));
        Assert.That(afterBurst, Is.LessThan(10000 - 3000));
        Assert.That(sim.RejectedEffectExecutionCount, Is.GreaterThan(0));
        SkillPayloadExecutor.Execute(sim, new SkillDto { Id = "one", ActionRefs = [new() { ActionId = action.Id, Params = new() { ["AttackCount"] = 1 } }] }, Player(), [Enemy()]);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(afterBurst - 1));
    }

    [TestCase(1000)]
    [TestCase(int.MaxValue)]
    public void Content_validation_rejects_attack_count_above_runtime_limit(int count)
    {
        var effect = new EffectDto { Id = "unbounded", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 1, ["AttackCount"] = count } };
        var registry = CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [effect.Id] = effect });
        Assert.That(registry.Store.Effects.ContainsKey(effect.Id), Is.False);
    }

    #endregion

    #region 条件与嵌套上下文

    [TestCase(false)]
    [TestCase(true)]
    public void Nonmatching_orb_batch_does_not_consume_the_hook_once_gate(bool perWave)
    {
        BaseGameContent.RegisterBuiltinConditions();
        var effect = new EffectDto
        {
            Id = "shield", Kind = EEffectKind.GainShield, Params = new() { ["amount"] = 1 },
            Conditions = [new() { Kind = "OrbTriggered", Params = new() { ["orbTypeId"] = "yellow" } }]
        };
        var buff = new BuffDto
        {
            Id = "conditional", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnOrbTriggered = [new() { EffectId = effect.Id, Params = new() { [perWave ? "oncePerWave" : "oncePerTurn"] = true } }] }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [effect.Id] = effect },
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }, orbs: new Dictionary<string, OrbTypeDto>
            { ["green"] = new() { Id = "green", Element = EElement.Green }, ["yellow"] = new() { Id = "yellow", Element = EElement.Yellow } }));
        sim.Buffs.Apply(sim, Player(), buff.Id);
        sim.Orbs.Grant(sim, "green", 0, 7);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
        sim.Orbs.Grant(sim, "yellow", 0, 7);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(1));
        sim.Orbs.Grant(sim, "yellow", 0, 7);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(1));
    }

    [Test]
    public void Already_fired_once_hook_does_not_advance_random_target_selection()
    {
        var effect = new EffectDto { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 1 } };
        var buff = new BuffDto
        {
            Id = "once", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnOrbTriggered = [new() { EffectId = effect.Id, Params = new() { ["oncePerTurn"] = true, ["hookTargets"] = "randomEnemy" } }] }
        };
        var registry = CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [effect.Id] = effect },
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }, orbs: new Dictionary<string, OrbTypeDto>
            { ["yellow"] = new() { Id = "yellow", Element = EElement.Yellow } });
        using var control = Build(registry, enemyCount: 3);
        using var repeated = Build(registry, enemyCount: 3);
        foreach (var sim in new[] { control, repeated })
        {
            sim.Buffs.Apply(sim, Player(), buff.Id);
            sim.Orbs.Grant(sim, "yellow", 0, 7);
        }
        repeated.Orbs.Grant(repeated, "yellow", 0, 7);
        Assert.That(repeated.EnemyTeam.Enemies.Sum(enemy => enemy.CurrentHp), Is.EqualTo(299));
        Assert.That(repeated.RetargetRng.NextInt(0, 1000000), Is.EqualTo(control.RetargetRng.NextInt(0, 1000000)));
    }

    [TestCase(EBuffStackRule.Refresh, 1)]
    [TestCase(EBuffStackRule.Add, 3)]
    public void Finite_buff_apply_and_stack_feedback_keeps_a_single_instance(EBuffStackRule rule, int stacks)
    {
        var buff = new BuffDto
        {
            Id = "finite", DurationType = EBuffDurationType.Permanent, StackRule = rule, MaxStacks = 3,
            Hooks = new() { OnApply = [new() { EffectId = "apply" }], OnStackChanged = [new() { EffectId = "apply" }] }
        };
        var effect = new EffectDto { Id = "apply", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = buff.Id } };
        var registry = CombatTestHelper.CreateFullRegistry(buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff },
            effects: new Dictionary<string, EffectDto> { [effect.Id] = effect });
        Assert.That(registry.Store.Buffs.ContainsKey(buff.Id), Is.True);
        using var sim = Build(registry);
        Assert.That(sim.Buffs.Apply(sim, Player(), buff.Id).Success, Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.All.Single().Stacks, Is.EqualTo(stacks));
        Assert.That(sim.RejectedEffectExecutionCount, Is.Zero);
    }

    [TestCase(EStackingPolicy.AggregateByTarget)]
    [TestCase(EStackingPolicy.AggregateBySource)]
    public void Finite_ge_apply_and_stack_feedback_reaches_max_stacks_without_budget_rejection(EStackingPolicy policy)
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "finite", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = policy, MaxStacks = 3,
            Hooks = new() { OnApply = [new() { ActionId = "stack" }], OnStackChanged = [new() { ActionId = "stack" }] }
        };
        var action = new SkillActionDto { Id = "stack", Kind = ESkillActionKind.ApplyGameplayEffect, Params = new() { ["gameplayEffectId"] = ge.Id } };
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge },
            skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action });
        Assert.That(registry.Store.GameplayEffects.ContainsKey(ge.Id), Is.True);
        using var sim = Build(registry);
        new GameplayEffectApplicator(registry).ApplyToTargets(sim, Player(), [Player()], ge.Id);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.ActiveEffects.Single().Stacks, Is.EqualTo(3));
        Assert.That(sim.RejectedEffectExecutionCount, Is.Zero);
    }

    [Test]
    public void Aggregated_ge_remove_and_recreate_feedback_is_rejected()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "loop", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = EStackingPolicy.AggregateByTarget,
            Hooks = new() { OnApply = [new() { ActionId = "remove" }], OnRemove = [new() { ActionId = "apply" }] }
        };
        var apply = new SkillActionDto { Id = "apply", Kind = ESkillActionKind.ApplyGameplayEffect, Params = new() { ["gameplayEffectId"] = ge.Id } };
        var remove = new SkillActionDto { Id = "remove", Kind = ESkillActionKind.RemoveGameplayEffect, Params = new() { ["gameplayEffectId"] = ge.Id } };
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge },
            skillActions: new Dictionary<string, SkillActionDto> { [apply.Id] = apply, [remove.Id] = remove });
        Assert.That(registry.Store.GameplayEffects.ContainsKey(ge.Id), Is.False);
    }

    [Test]
    public void Magic_damage_target_filter_reads_each_candidate_instead_of_the_source()
    {
        BaseGameContent.RegisterBuiltinConditions();
        using var sim = Build(CombatTestHelper.CreateFullRegistry());
        sim.EffectExecutor.ApplyFixedDamage(sim, Enemy(), [Player(1)], 1, kind: EDamageKind.Magical);
        sim.RollMagicDamageTurnLedger();
        using var filter = JsonDocument.Parse("{\"condition\":{\"kind\":\"TookMagicDamageLastTurn\"}}");
        var targets = CombatTargetSelector.Resolve(sim, Player(), new Dictionary<string, object> { ["targetFilter"] = filter.RootElement });
        Assert.That(targets, Is.EqualTo(new[] { Player(1) }));
    }

    [TestCase("{\"self\":true}", 0)]
    [TestCase("{\"excludeSelf\":true}", 4)]
    public void Enemy_source_cannot_alias_a_player_in_identity_filters(string json, int expectedCount)
    {
        using var sim = Build(CombatTestHelper.CreateFullRegistry());
        using var filter = JsonDocument.Parse(json);
        var targets = CombatTargetSelector.Resolve(sim, Enemy(), new Dictionary<string, object> { ["targetFilter"] = filter.RootElement });
        Assert.That(targets.Count, Is.EqualTo(expectedCount));
    }

    [Test]
    public void Nested_card_context_restores_outer_values_even_when_inner_execution_throws()
    {
        using var sim = Build(CombatTestHelper.CreateFullRegistry());
        using (sim.EnterCardContext(ECardType.Physics, 0, (int)EElement.Yellow, 0.5f))
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var inner = sim.EnterCardContext(ECardType.Magical, 1, (int)EElement.Green, 0.1f);
                throw new InvalidOperationException("test");
            });
            Assert.That((sim.CurrentCardType, sim.CurrentCardSourceIndex, sim.CurrentChainCardElementFlags, sim.CurrentChainBonus),
                Is.EqualTo(((ECardType?)ECardType.Physics, 0, (int)EElement.Yellow, 0.5f)));
        }
        Assert.That(sim.CurrentCardType, Is.Null);
        Assert.That(sim.CurrentCardSourceIndex, Is.EqualTo(-1));
        Assert.That(sim.CurrentChainCardElementFlags, Is.Zero);
        Assert.That(sim.CurrentChainBonus, Is.Zero);
    }

    [Test]
    public void Inner_orb_trigger_masks_outer_batch_until_its_hooks_and_restores_it_on_exit()
    {
        using var sim = Build(CombatTestHelper.CreateFullRegistry(orbs: new Dictionary<string, OrbTypeDto>
        { ["yellow"] = new() { Id = "yellow", Element = EElement.Yellow }, ["green"] = new() { Id = "green", Element = EElement.Green } }));
        sim.BeginOrbTriggeredBatch(["yellow"]);
        using (sim.EnterOrbTriggerScope())
        {
            Assert.That(sim.OrbBatchMatches((int)EElement.Yellow, null), Is.False);
            sim.BeginOrbTriggeredBatch(["green"]);
            Assert.That(sim.OrbBatchMatches((int)EElement.Green, null), Is.True);
            sim.EndOrbTriggeredBatch();
        }
        Assert.That(sim.OrbBatchMatches((int)EElement.Yellow, null), Is.True);
        sim.EndOrbTriggeredBatch();
        Assert.That(sim.OrbBatchMatches(0, null), Is.False);
    }

    #endregion
}