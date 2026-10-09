using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class MotherMachineContentTests
{
    private static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);

    #region 定义与主动充能

    [Test]
    public void Content_and_healing_targets_validate_with_the_requested_identity_and_card_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var def = BaseGameContent.Load().Characters["mother_machine"];
        Assert.That(def.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(def.Role, Is.EqualTo(ERole.Healer));
        Assert.That(def.Race, Is.EqualTo(ERace.Machine | ERace.Academic));
        Assert.That(def.ActiveSkillChain.Single().Cooldown, Is.EqualTo(10));
        Assert.That(def.Passives.Select(p => p.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow));
            Assert.That(card.Role, Is.EqualTo(ERole.Healer));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [Test]
    public void Charge_trigger_hook_rejects_missing_effect_references_at_admission()
    {
        var store = new GameDefinitionStore();
        store.BuffsMutable["test.charge_listener"] = new BuffDto
        {
            Id = "test.charge_listener", DurationType = EBuffDurationType.Permanent,
            Hooks = new BuffEffectHooksDto { OnSlotChargeTriggered = [new EffectRefDto { EffectId = "missing.charge_payload" }] }
        };
        var errors = new ContentDefinitionValidator().Validate(store);
        Assert.That(errors.Any(e => e.DefinitionId == "test.charge_listener" && e.Message.Contains("missing.charge_payload")), Is.True);
    }

    [Test]
    public void Active_requires_ten_progress_and_attaches_three_independent_charges_to_empty_slots()
    {
        using var sim = Build();
        var c = sim.PlayerTeam.Characters[0];
        c.GainSkillCounter(9);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.False);
        Assert.That(c.SkillCounter, Is.EqualTo(9));
        c.GainSkillCounter(1);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(c.SkillCounter, Is.Zero);
        Assert.That(c.HasActed, Is.False);
        foreach (var (index, turns) in new[] { (0, 1), (2, 3), (4, 5) })
        {
            var charge = c.HandSlots[index].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
            Assert.That(charge.ChargeRequired, Is.EqualTo(1));
            Assert.That(charge.RemainingTurns, Is.EqualTo(turns));
            Assert.That(c.HandSlots[index].IsEmpty, Is.True);
        }
        Assert.That(c.HandSlots[1].Buffs.HasTag(BuiltinBuffTags.SlotCharge), Is.False);
        Assert.That(c.HandSlots[3].Buffs.HasTag(BuiltinBuffTags.SlotCharge), Is.False);
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(a => a.HandSlots.All(s => !s.Buffs.HasTag(BuiltinBuffTags.SlotCharge))), Is.True);
    }

    [TestCase(0, 100)]
    [TestCase(2, 75)]
    [TestCase(4, 50)]
    public void Active_charge_heals_once_per_play_resets_and_does_not_add_heal_power(int index, int amount)
    {
        using var sim = Build();
        Passive(sim, 2);
        Cast(sim);
        PlaySlot(sim, index);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(100 + amount));
        Assert.That(HealPower(sim), Is.EqualTo(8));
        PlaySlot(sim, index);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(100 + 2 * amount));
        Assert.That(HealPower(sim), Is.EqualTo(10));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[index].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!.ChargeCounter, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
    }

    [Test]
    public void Active_charges_expire_after_one_three_and_five_turns_without_expiration_healing()
    {
        using var sim = Build();
        Cast(sim);
        for (var turn = 1; turn <= 5; turn++)
        {
            sim.Buffs.FireTurnEnd(sim);
            foreach (var (index, duration) in new[] { (0, 1), (2, 3), (4, 5) })
                Assert.That(sim.PlayerTeam.Characters[0].HandSlots[index].Buffs.HasTag(BuiltinBuffTags.SlotCharge), Is.EqualTo(turn < duration));
        }
        PlaySlot(sim, 0); PlaySlot(sim, 2); PlaySlot(sim, 4);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(100));
    }

    [Test]
    public void Recasting_replaces_three_charges_and_preserves_permanent_second_slot_progress()
    {
        using var sim = Build();
        Passive(sim, 4);
        PlaySlot(sim, 1);
        var resident = sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
        Cast(sim); sim.Buffs.FireTurnEnd(sim); Cast(sim);
        Assert.That(resident.ChargeCounter, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.FindByTag(BuiltinBuffTags.SlotCharge), Is.SameAs(resident));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!.RemainingTurns, Is.EqualTo(1));
    }

    #endregion

    #region 被动维护与叠层

    [Test]
    public void Resident_charge_heals_before_adding_stack_and_boosts_every_ally()
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(100));
        Assert.That(HealPower(sim), Is.EqualTo(6));
        PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(121), "先按15+6结算，再获得恢复量+2");
        Assert.That(HealPower(sim), Is.EqualTo(8));
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == 32), Is.True);
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.MagicAttack) == 22), Is.True);
        PlaySlot(sim, 1); PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(144), "下一次治疗吃上一层恢复量");
        Assert.That(HealPower(sim), Is.EqualTo(10));
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == 32), Is.True, "攻击增益刷新不叠加");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(32));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == 20), Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!.RemainingTurns, Is.Null);
    }

    [Test]
    public void Healing_stacks_cap_at_three_and_each_layer_expires_independently()
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        for (var turn = 0; turn < 3; turn++)
        {
            PlaySlot(sim, 1); PlaySlot(sim, 1);
            sim.Buffs.FireTurnEnd(sim);
        }
        Assert.That(HealPower(sim), Is.EqualTo(12));
        PlaySlot(sim, 1); PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("mother_machine_heal_stack")!.Stacks, Is.EqualTo(3));
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(HealPower(sim), Is.EqualTo(10), "满层额外触发不会刷新旧层期限");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(HealPower(sim), Is.EqualTo(8));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(HealPower(sim), Is.EqualTo(6));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("mother_machine_heal_stack"), Is.Null);
    }

    [Test]
    public void Only_completed_own_slot_charges_grant_stacks_including_other_charge_payloads()
    {
        using var sim = Build();
        Passive(sim, 2);
        sim.Buffs.ApplyToSlot(sim, 1, 1, "mother_machine_resident_charge", new Dictionary<string, object> { ["charge"] = 2 });
        PlaySlot(sim, 1, 1); PlaySlot(sim, 1, 1);
        PlaySlot(sim, 3);
        Assert.That(HealPower(sim), Is.EqualTo(6));
        sim.Buffs.ApplyToSlot(sim, 0, 3, "kagi_charge_three", new Dictionary<string, object> { ["charge"] = 3 });
        PlaySlot(sim, 3); PlaySlot(sim, 3);
        Assert.That(HealPower(sim), Is.EqualTo(6));
        PlaySlot(sim, 3);
        Assert.That(HealPower(sim), Is.EqualTo(8));
    }

    [Test]
    public void Dormant_charge_and_dormant_listener_do_not_gain_healing_stacks()
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        var charge = sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
        charge.SetDormant(true);
        PlaySlot(sim, 1); PlaySlot(sim, 1);
        Assert.That(charge.ChargeCounter, Is.EqualTo(2));
        Assert.That(HealPower(sim), Is.EqualTo(6));
        charge.SetDormant(false);
        sim.PlayerTeam.Characters[0].Buffs.Find("mother_machine_passive_p2")!.SetDormant(true);
        PlaySlot(sim, 1); PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(121));
        Assert.That(HealPower(sim), Is.EqualTo(6));
    }

    [Test]
    public void Full_health_charge_still_grants_stack_and_timer_immunity_does_not_block_charge()
    {
        using var sim = Build(start: true);
        sim.RunBattleStart();
        sim.PlayerTeam.HealShared(1000);
        sim.Buffs.ApplyToSlot(sim, 0, 1, "slot_timer", new Dictionary<string, object> { ["timerTurns"] = 1, ["amount"] = 100 });
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.Find("slot_timer"), Is.Null);
        PlaySlot(sim, 1); PlaySlot(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(HealPower(sim), Is.EqualTo(8));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[1].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!.ChargeCounter, Is.EqualTo(2));
    }

    [Test]
    public void Progress_stacks_self_yellow_machine_and_academic_every_ten_floor_turns()
    {
        using var sim = Build();
        Passive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 9; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
    }

    #endregion

    #region 专属卡与实际指令流程

    [Test]
    public void Four_cards_execute_with_preload_healing_shields_next_draw_and_temporary_energy()
    {
        var cards = BaseGameContent.Load().Characters["mother_machine"].Cards.ToArray();
        using var sim = Build(cards: cards);
        for (var slot = 0; slot < 4; slot++)
        {
            var result = sim.TryApply(new PlayCardCommand(0, slot, []));
            Assert.That(result.Success, Is.True, result.Error);
        }
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(185), "预载先结算，两张治疗分别50与35");
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.Shield) == 20), Is.True);
        var owner = sim.PlayerTeam.Characters[0];
        Assert.That(owner.SkillCounter, Is.EqualTo(2));
        Assert.That(owner.AvailableEnergy, Is.EqualTo(6));
        Assert.That(owner.CurrentEnergy, Is.EqualTo(10));
        Assert.That(owner.ComputeDrawCount(), Is.EqualTo(3));
        Assert.That(owner.Graveyard, Has.Count.EqualTo(4));
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(HealPower(sim), Is.EqualTo(6));
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(owner.ComputeDrawCount(), Is.EqualTo(1));
        Assert.That(owner.AvailableEnergy, Is.EqualTo(owner.CurrentEnergy));
    }

    [Test]
    public void Real_card_execution_triggers_charge_payload_before_card_healing_and_then_adds_the_stack()
    {
        using var sim = Build(cards: ["mother_machine_cache_repair"]);
        Passive(sim, 2); Cast(sim);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.True);
        sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(248), "先充能恢复100，叠层后卡牌恢复40+8");
        Assert.That(HealPower(sim), Is.EqualTo(8));
    }

    #endregion

    #region 构造与断言

    private static CombatSimulation Build(bool start = false, string[]? cards = null)
    {
        var identities = new[] { (EElement.Yellow, ERace.Machine | ERace.Academic), (EElement.Yellow, ERace.Human), (EElement.Blue, ERace.Machine), (EElement.Green, ERace.Academic) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "mother_machine" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10, [AttributeIds.HealPower] = 6,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 10, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["mother_machine"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var c in characters) c.RefillAvailableEnergy();
        var sim = new CombatSimulation(new PlayerTeamState(characters, 1000), new EnemyTeamState([new EnemyUnit("e0", "slime", 1000)]),
            new CombatRuleEngine([]), BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009,
            initialBuffs: start ? BaseGameContent.Load().Characters["mother_machine"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
        sim.PlayerTeam.ApplySharedDamage(900);
        return sim;
    }

    private static void Passive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"mother_machine_passive_p{i}");
    private static void PlaySlot(CombatSimulation sim, int slot, int owner = 0) => sim.Buffs.FireSlotCardPlayed(sim, owner, sim.PlayerTeam.Characters[owner].HandSlots[slot]);
    private static float HealPower(CombatSimulation sim) => sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.HealPower);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();
    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(10);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    #endregion
}