using System.Globalization;
using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

public sealed partial class CombatAuditRegressionTests
{
    #region 结算与重入

    [TestCase(true)]
    [TestCase(false)]
    public void Domain_hook_replacement_preserves_the_new_slot_and_its_full_duration(bool applyHook)
    {
        var action = new SkillActionDto
        {
            Id = "replace", Kind = ESkillActionKind.SetDomain,
            Params = new() { ["gameplayEffectId"] = "new", ["turns"] = 1 }
        };
        var hooks = new GameplayEffectHooksDto
        {
            OnApply = applyHook ? [new() { ActionId = action.Id }] : [],
            OnTurnEnd = applyHook ? [] : [new() { ActionId = action.Id }],
        };
        var old = new GameplayEffectDefDto { Id = "old", DurationPolicy = EDurationPolicy.Infinite, Hooks = hooks };
        var next = new GameplayEffectDefDto { Id = "new", DurationPolicy = EDurationPolicy.Infinite };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [old.Id] = old, [next.Id] = next },
            skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action }));
        Assert.That(sim.DomainManager.TrySetPlayerDomain(old.Id, null, turns: 5), Is.True);
        if (!applyHook)
            sim.DomainManager.FireTurnEndHooks();
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo(next.Id));
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects.Single().Def.Id, Is.EqualTo(next.Id));
        sim.DomainManager.FireTurnEndHooks();
        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Null);
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Sustained_ge_expires_on_character_team_and_enemy_in_real_turns(int holderKind)
    {
        var ge = new GameplayEffectDefDto { Id = "duration", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 2 };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge }));
        var target = holderKind switch { 0 => Player(), 1 => KemoCard.Mod.Combat.Runtime.CombatTargetRef.PlayerTeam, _ => Enemy() };
        var asc = KemoCard.Mod.Combat.Gas.CombatGasBridge.ResolveTargetAsc(sim, target)!;
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Player(), [target], ge.Id);
        for (var i = 0; i < 2; i++)
        {
            sim.TransitionTo(ECombatPhase.Enemy);
            sim.AdvancePhase();
        }
        Assert.That(asc.ActiveEffects, Is.Empty);
    }

    [Test]
    public void Periodic_effect_keeps_enemy_source_identity_after_that_enemy_dies()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "periodic", DurationPolicy = EDurationPolicy.Infinite, PeriodTurns = 1,
            Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge }));
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy(), [Player()], ge.Id, new Dictionary<string, object> { ["Amount"] = 10 });
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 6);
        sim.EnemyTeam.Enemies[0].ApplyDamage(1000);
        sim.DomainManager.FireTurnStartHooks();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(96));
    }

    [Test]
    public void Failed_random_card_mark_does_not_advance_target_rng()
    {
        var card = new CardDto
        {
            Id = "random", CostType = ECostType.Energy, Cost = 10,
            TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.RandomN
        };
        using var a = Build(CombatTestHelper.CreateRegistry(card), enemyCount: 3);
        using var b = Build(CombatTestHelper.CreateRegistry(card), enemyCount: 3);
        a.PlayerTeam.Characters[0].HandSlots[0].PlaceCard(card.Id, "rt");
        Assert.That(a.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.False);
        Assert.That(a.RetargetRng.NextInt(0, 1000000), Is.EqualTo(b.RetargetRng.NextInt(0, 1000000)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Overkill_channels_have_the_same_rule_shield_and_actual_loss_contract(bool gas)
    {
        var def = new GameplayEffectDefDto
        {
            Id = "hit", DurationPolicy = EDurationPolicy.Instant,
            Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        var rule = new ObserveDamageRule();
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [def.Id] = def }), rules: [rule]);
        sim.PlayerTeam.ApplySharedDamage(80);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 50);
        if (gas)
            new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy(), [Player()], def.Id, new Dictionary<string, object> { ["Amount"] = 100 });
        else
            sim.EffectExecutor.ApplyFixedDamage(sim, Enemy(), [Player()], 100);
        Assert.Multiple(() =>
        {
            Assert.That(rule.Before, Is.EqualTo(100));
            Assert.That(rule.After, Is.EqualTo(20));
            Assert.That(sim.PlayerTeam.SharedHpExact, Is.Zero);
            Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
            Assert.That(sim.Presentation.Pending.OfType<DamageDealtEvent>().Single().Amount, Is.EqualTo(20));
        });
    }

    private sealed class ObserveDamageRule : ICombatRule
    {
        public string Id => "observe";
        public int Priority => 0;
        public float Before { get; private set; }
        public float After { get; private set; }
        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) => Before = packet.Amount;
        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet) => After = packet.Amount;
    }

    [Test]
    public void Script_proposal_reentry_is_bounded_and_a_later_root_can_execute()
    {
        var action = new SkillActionDto { Id = "loop", Kind = ESkillActionKind.ExecuteScript, ScriptPath = "loop.js" };
        var host = new RecursiveHost();
        using var sim = Build(CombatTestHelper.CreateFullRegistry(skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action }), scriptHost: host);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = action.Id }, sim, Player(), [Player()]);
        Assert.That(host.Calls, Is.InRange(1, 32));
        Assert.That(sim.RejectedEffectExecutionCount, Is.GreaterThan(0));
        host.Recurse = false;
        var before = host.Calls;
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = action.Id }, sim, Player(), [Player()]);
        Assert.That(host.Calls, Is.EqualTo(before + 1));
    }

    private sealed class RecursiveHost : IContentEffectScriptHost
    {
        public int Calls { get; private set; }
        public bool Recurse { get; set; } = true;
        public bool TryExecute(string modId, string scriptPath, string scriptEntry,
            IReadOnlyDictionary<string, object>? context, out IReadOnlyList<Dictionary<string, object>> proposedEffects)
        {
            Calls++;
            proposedEffects = Recurse ? [new() { ["actionId"] = "loop" }] : [];
            return true;
        }
    }

    [Test]
    public void Buff_recursion_bypassing_admission_is_bounded_at_runtime()
    {
        var registry = CombatTestHelper.CreateFullRegistry();
        registry.Store.EffectsMutable["apply"] = new() { Id = "apply", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "loop" } };
        registry.Store.BuffsMutable["loop"] = new()
        {
            Id = "loop", StackRule = EBuffStackRule.Replace,
            DurationType = EBuffDurationType.Permanent, Hooks = new() { OnApply = [new() { EffectId = "apply" }] }
        };
        using var sim = Build(registry);
        Assert.DoesNotThrow(() => sim.Buffs.Apply(sim, Player(), "loop"));
        Assert.That(sim.RejectedEffectExecutionCount, Is.GreaterThan(0));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.All, Has.Count.EqualTo(1));
    }

    [Test]
    public void Instant_ge_apply_hook_damage_is_not_transferred_twice()
    {
        var hit = new GameplayEffectDefDto
        {
            Id = "hit", DurationPolicy = EDurationPolicy.Instant,
            Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        var action = new SkillActionDto
        {
            Id = "apply_hit", Kind = ESkillActionKind.ApplyGameplayEffect,
            Params = new() { ["gameplayEffectId"] = hit.Id, ["Amount"] = 7 }
        };
        var parent = new GameplayEffectDefDto
        {
            Id = "parent", DurationPolicy = EDurationPolicy.Instant,
            Hooks = new() { OnApply = [new() { ActionId = action.Id }] }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [hit.Id] = hit, [parent.Id] = parent },
            skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action }));
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Player(), [Enemy()], parent.Id);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(93));
        Assert.That(sim.Presentation.Pending.OfType<DamageDealtEvent>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Periodic_enemy_ge_uses_the_same_shared_hp_damage_settlement()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "periodic", DurationPolicy = EDurationPolicy.HasDuration,
            DurationTurns = 2, PeriodTurns = 1, Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge }));
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy(), [Player()], ge.Id, new Dictionary<string, object> { ["Amount"] = 10 });
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 6);
        sim.DomainManager.FireTurnStartHooks();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(96));
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Health), Is.Zero);
    }

    [Test]
    public void Ge_added_to_a_later_holder_during_tick_waits_until_the_next_batch()
    {
        var action = new SkillActionDto { Id = "add", Kind = ESkillActionKind.ApplyGameplayEffect, Params = new() { ["gameplayEffectId"] = "new" } };
        var original = new GameplayEffectDefDto
        {
            Id = "original", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 2,
            Hooks = new() { OnTurnEnd = [new() { ActionId = action.Id }] }
        };
        var added = new GameplayEffectDefDto { Id = "new", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 1 };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [original.Id] = original, [added.Id] = added },
            skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action }));
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Player(), [Player()], original.Id);
        sim.DomainManager.FireTurnEndHooks();
        Assert.That(sim.PlayerTeam.Characters[0].Asc.ActiveEffects.Single(effect => effect.Def.Id == "new").RemainingTurns, Is.EqualTo(1));
    }

    [Test]
    public void Buff_added_to_a_later_holder_at_turn_end_does_not_tick_immediately()
    {
        var effect = new EffectDto { Id = "add", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "new", ["hookTargets"] = "allies" } };
        var original = new BuffDto
        {
            Id = "original", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnTurnEnd = [new() { EffectId = effect.Id }] }
        };
        var added = new BuffDto { Id = "new", DurationType = EBuffDurationType.Turns, Duration = 1 };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [effect.Id] = effect },
            buffs: new Dictionary<string, BuffDto> { [original.Id] = original, [added.Id] = added }));
        sim.Buffs.Apply(sim, Player(), original.Id);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(character => character.Buffs.Find(added.Id) is not null), Is.True);
    }

    [Test]
    public void Legacy_script_uses_effect_owner_even_if_action_has_the_same_id()
    {
        var effect = new EffectDto { Id = "same", Kind = EEffectKind.ExecuteScript, ScriptPath = "effect.js" };
        var action = new SkillActionDto { Id = "same", Kind = ESkillActionKind.ExecuteScript, ScriptPath = "action.js" };
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([
            new ModContentBundle("effect.mod", ModDefinitionsBundle.Empty with { Effects = new Dictionary<string, EffectDto> { [effect.Id] = effect } }),
            new ModContentBundle("action.mod", ModDefinitionsBundle.Empty with { SkillActions = new Dictionary<string, SkillActionDto> { [action.Id] = action } })], out _);
        var host = new CaptureHost();
        using var sim = Build(registry, scriptHost: host, modId: "battle.mod");
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = effect.Id }, sim, Player(), [Player()]);
        Assert.That(host.CalledMod, Is.EqualTo("effect.mod"));
    }

    #endregion

    #region 快照、准入与资源不变量

    [Test]
    public void Self_side_team_scope_still_resolves_to_the_shared_ledger()
    {
        using var sim = Build(CombatTestHelper.CreateFullRegistry());
        var targets = CombatTargeting.ResolvePlayerTargets(sim, ETargetSide.Self, ETargetScope.Team, 1, 0, [], out _);
        Assert.That(targets, Is.EqualTo(new[] { KemoCard.Mod.Combat.Runtime.CombatTargetRef.PlayerTeam }));
    }

    [Test]
    public void Command_queue_and_presentation_keep_immutable_target_snapshots()
    {
        var card = new CardDto { Id = "card", TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.Single };
        using var sim = Build(CombatTestHelper.CreateRegistry(card), enemyCount: 2);
        var targets = new List<KemoCard.Mod.Combat.Runtime.CombatTargetRef> { Enemy() };
        var command = new PlayCardCommand(0, 0, targets);
        sim.PlayerTeam.Characters[0].HandSlots[0].PlaceCard(card.Id, "rt");
        targets[0] = Enemy(1);
        Assert.That(sim.TryApply(command).Success, Is.True);
        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Targets.Single(), Is.EqualTo(Enemy()));
        sim.Presentation.Emit(new CardSettleStartedEvent(0, 0, card.Id, targets));
        targets.Clear();
        Assert.That(sim.Presentation.Pending.OfType<CardSettleStartedEvent>().Single().Targets.Single(), Is.EqualTo(Enemy(1)));
        Assert.Throws<NotSupportedException>(() => ((IList<KemoCard.Mod.Combat.Runtime.CombatTargetRef>)command.Targets).Clear());
    }

    [Test]
    public void Invalid_single_target_set_does_not_spend_energy_or_mark_a_card()
    {
        var card = new CardDto
        {
            Id = "card", CostType = ECostType.Energy, Cost = 2,
            TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.Single
        };
        using var sim = Build(CombatTestHelper.CreateRegistry(card));
        var holder = sim.PlayerTeam.Characters[0];
        holder.HandSlots[0].PlaceCard(card.Id, "rt");
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.False);
        Assert.That(holder.AvailableEnergy, Is.EqualTo(5));
        Assert.That(holder.HandSlots[0].IsMarked, Is.False);
        Assert.That(sim.CardQueue.Count, Is.Zero);
    }

    [Test]
    public void Mark_and_cancel_conserve_card_identity_and_paid_energy()
    {
        var card = new CardDto { Id = "card", CostType = ECostType.Energy, Cost = 2 };
        using var sim = Build(CombatTestHelper.CreateRegistry(card));
        var holder = sim.PlayerTeam.Characters[0];
        holder.HandSlots[0].PlaceCard(card.Id, "rt");
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.True);
        var entry = sim.CardQueue.PeekAllOrdered().Single();
        Assert.That(holder.AvailableEnergy + entry.Paid, Is.EqualTo(5));
        CombatStateMachine.CancelMarkAndRefund(sim, entry);
        CombatStateMachine.CancelMarkAndRefund(sim, entry);
        Assert.That(holder.AvailableEnergy, Is.EqualTo(5));
        Assert.That(holder.HandSlots[0].RuntimeInstanceId, Is.EqualTo("rt"));
        Assert.That(holder.HandSlots[0].IsMarked, Is.False);
        Assert.That(sim.CardQueue.Count, Is.Zero);
    }

    [Test]
    public void Captured_deck_reference_and_snapshot_restore_cannot_bypass_battle_lock()
    {
        var instance = new CharacterInstance(new CharacterDto { Id = "hero", Cards = ["exclusive"] });
        var deck = instance.GetCurrentDeck()!;
        instance.SetDeckLocked(true);
        Assert.That(deck.TryAddCard("owned", new HashSet<string> { "owned" }), Is.False);
        Assert.That(deck.TryRemoveCard("exclusive"), Is.False);
        Assert.Throws<InvalidOperationException>(() => instance.ApplyDeckSnapshots([["owned"]], 0));
        Assert.That(deck.CardIds, Is.EqualTo(new[] { "exclusive" }));
    }

    [Test]
    public void Runtime_numeric_parameters_are_invariant_finite_and_range_checked()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto>
            {
                ["hit"] = new() { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = "1.5" } },
            }));
            sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "hit" }, sim, Player(), [Enemy()]);
            Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(98.5f));
            Assert.That(ContentParameters.TryFloat(float.NaN, out _), Is.False);
            Assert.That(ContentParameters.TryFloat(double.PositiveInfinity, out _), Is.False);
            Assert.That(ContentParameters.TryInt(long.MaxValue, out _), Is.False);
            Assert.That(ContentParameters.TryInt(JsonSerializer.SerializeToElement(1e30), out _), Is.False);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public void Deferred_turn_hook_and_refresh_feedback_are_not_rejected_as_sync_cycles()
    {
        var store = new GameDefinitionStore();
        store.EffectsMutable["apply"] = new() { Id = "apply", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "safe" } };
        store.BuffsMutable["safe"] = new()
        {
            Id = "safe", DurationType = EBuffDurationType.Permanent,
            StackRule = EBuffStackRule.Refresh, Hooks = new() { OnApply = [new() { EffectId = "apply" }], OnTurnStart = [new() { EffectId = "apply" }] }
        };
        Assert.That(new ContentDefinitionValidator().Validate(store), Is.Empty);
    }

    [Test]
    public void Unsupported_effect_and_buff_magnitude_are_rejected_explicitly()
    {
        var store = new GameDefinitionStore();
        store.EffectsMutable["unsupported"] = new() { Id = "unsupported", Kind = EEffectKind.ModifyStat };
        store.BuffsMutable["unsupported"] = new()
        {
            Id = "unsupported", Modifiers =
            [new() { AttributeId = AttributeIds.PhysicalAttack, Magnitude = new() { Kind = EMagnitudeKind.AttributeBased } }]
        };
        var errors = new ContentDefinitionValidator().Validate(store);
        Assert.That(errors.Any(error => error.Category == EContentCategory.Effect && error.Message.Contains("not implemented")), Is.True);
        Assert.That(errors.Any(error => error.Category == EContentCategory.Buff && error.Message.Contains("Unsupported")), Is.True);
    }

    [Test]
    public void Terminal_turn_start_rejects_input_without_resource_refill()
    {
        var rule = new DefeatAtStartRule();
        using var sim = Build(CombatTestHelper.CreateFullRegistry(), rules: [rule]);
        sim.PlayerTeam.Characters[0].TryConsumeAvailableEnergy(3);
        sim.RunBattleStart();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(2));
        Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.False);
    }

    private sealed class DefeatAtStartRule : ICombatRule
    {
        public string Id => "defeat";
        public int Priority => 0;
        public void OnTurnStart(CombatContext ctx) => ctx.Simulation.PlayerTeam.ApplySharedDamage(1000);
    }

    #endregion
}