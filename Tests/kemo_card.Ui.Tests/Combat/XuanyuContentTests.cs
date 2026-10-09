using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class XuanyuContentTests
{
    private const string Feather = "xuanyu_crow_feather";
    private static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);
    private static CombatTargetRef Enemy(int i = 0) => new(ECombatSide.Enemy, i);

    #region 定义与乌翎生命周期

    [Test]
    public void Content_validates_red_wizard_machine_animal_and_four_budget_cards()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        var def = BaseGameContent.Load().Characters["xuanyu"];
        Assert.That(def.Element, Is.EqualTo(EElement.Red));
        Assert.That(def.Role, Is.EqualTo(ERole.Wizard));
        Assert.That(def.Race, Is.EqualTo(ERace.Machine | ERace.Animal));
        Assert.That(def.ActiveSkillChain.Single().Cooldown, Is.EqualTo(9));
        Assert.That(def.Passives.Select(p => p.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Red));
            Assert.That(card.Role, Is.EqualTo(ERole.Wizard));
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [Test]
    public void Battle_start_adds_two_independent_feathers_and_permanent_slot_one_charge()
    {
        using var sim = Build(start: true);
        sim.RunBattleStart();
        Assert.That(Feathers(sim), Has.Length.EqualTo(2));
        Assert.That(Feathers(sim).Select(f => f.RemainingTurns), Is.EqualTo(new int?[] { 3, 3 }));
        Assert.That(Feathers(sim).Select(f => f.Handle).Distinct().Count(), Is.EqualTo(2));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmuneSlotDamage), Is.True);
        var slot = sim.PlayerTeam.Characters[0].HandSlots[0].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
        Assert.That(slot.ChargeRequired, Is.EqualTo(2));
        Assert.That(slot.RemainingTurns, Is.Null);
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.Skip(1).All(s => !s.Buffs.HasTag(BuiltinBuffTags.SlotCharge)), Is.True);
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(c => c.Buffs.Find(Feather) is null), Is.True);
    }

    [TestCase(false, 37)]
    [TestCase(true, 74)]
    public void Every_third_turn_each_feather_bursts_reapplies_and_is_not_ticked_again(bool twin, int damage)
    {
        using var sim = Build();
        Passive(sim, 1);
        if (twin) Passive(sim, 4);
        var old = Feathers(sim);
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(Hp(sim), Is.EqualTo(1000));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.Health) == 1000 - damage), Is.True, "12+40-15，每层单独扣魔防");
        Assert.That(Feathers(sim).All(f => f.RemainingTurns == 3 && !old.Contains(f)), Is.True);
        for (var i = 0; i < 3; i++) sim.Buffs.FireTurnEnd(sim);
        Assert.That(Hp(sim), Is.EqualTo(1000 - 2 * damage));
        Assert.That(Feathers(sim), Has.Length.EqualTo(twin ? 2 : 1));
    }

    [Test]
    public void Staggered_feathers_expire_separately_without_refreshing_the_other_instance()
    {
        using var sim = Build();
        Passive(sim, 1); sim.Buffs.FireTurnEnd(sim); Passive(sim, 4);
        Assert.That(Feathers(sim).Select(f => f.RemainingTurns), Is.EqualTo(new int?[] { 2, 3 }));
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(Hp(sim), Is.EqualTo(963));
        Assert.That(Feathers(sim).Select(f => f.RemainingTurns).Order(), Is.EqualTo(new int?[] { 1, 3 }));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Hp(sim), Is.EqualTo(926));
        Assert.That(Feathers(sim).Select(f => f.RemainingTurns).Order(), Is.EqualTo(new int?[] { 2, 3 }));
    }

    [Test]
    public void Independent_instance_limit_does_not_refresh_or_burst_existing_feathers()
    {
        using var sim = Build();
        Passive(sim, 1); Passive(sim, 4); sim.Buffs.FireTurnEnd(sim);
        var old = Feathers(sim);
        sim.Buffs.Apply(sim, Player(), Feather);
        Assert.That(Feathers(sim), Is.EqualTo(old));
        Assert.That(Feathers(sim).All(f => f.RemainingTurns == 2), Is.True);
        Assert.That(Hp(sim), Is.EqualTo(1000));
    }

    [Test]
    public void Explicit_dispel_removes_both_old_feathers_once_and_preserves_new_instances()
    {
        using var sim = Build();
        Passive(sim, 1); Passive(sim, 4);
        var old = Feathers(sim);
        Assert.That(sim.Buffs.Dispel(sim, Player(), Feather), Is.EqualTo(2));
        Assert.That(Hp(sim), Is.EqualTo(926));
        Assert.That(Feathers(sim), Has.Length.EqualTo(2));
        Assert.That(Feathers(sim).All(f => f.RemainingTurns == 3 && !old.Contains(f)), Is.True);
    }

    #endregion

    #region 角色驱散与技能

    [Test]
    public void Active_requires_nine_progress_boosts_attack_before_bursts_and_expires_after_three_turns()
    {
        using var sim = Build();
        Passive(sim, 1); Passive(sim, 4);
        var c = sim.PlayerTeam.Characters[0];
        c.GainSkillCounter(8);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.False);
        Assert.That(Value(sim, AttributeIds.MagicAttack), Is.EqualTo(40));
        c.GainSkillCounter(1);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(c.SkillCounter, Is.Zero);
        Assert.That(Value(sim, AttributeIds.MagicAttack), Is.EqualTo(52));
        Assert.That(Hp(sim), Is.EqualTo(902), "两层12+52-15");
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.MagicAttack), Is.EqualTo(52));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.MagicAttack), Is.EqualTo(40));
        Assert.That(Feathers(sim), Has.Length.EqualTo(2));
    }

    [Test]
    public void Cleanse_removes_role_buff_and_gas_debuffs_but_keeps_protected_buffs_slots_and_teammates()
    {
        using var sim = Build();
        AddBuff(sim, "test.debuff", ["debuff.poison"]);
        AddBuff(sim, "test.protected", ["debuff", BuiltinBuffTags.Undispellable]);
        AddBuff(sim, "test.beneficial", []);
        AddBuff(sim, "test.slot", ["debuff", BuiltinBuffTags.SlotTimer]);
        sim.Buffs.Apply(sim, Player(), "test.debuff");
        sim.Buffs.Apply(sim, Player(1), "test.debuff");
        sim.Buffs.Apply(sim, Player(), "test.protected");
        sim.Buffs.Apply(sim, Player(), "test.beneficial");
        sim.Buffs.ApplyToSlot(sim, 0, 2, "test.slot", new Dictionary<string, object> { ["timerTurns"] = 3, ["amount"] = 20 });
        AddGe(sim, "test.negative", ["debuff.weak"]);
        AddGe(sim, "test.seal", [CombatConstants.SealedTag]);
        AddGe(sim, "test.protected_ge", ["debuff.weak", BuiltinBuffTags.Undispellable]);
        AddGe(sim, "test.positive", ["buff.power"]);
        var app = new GameplayEffectApplicator(sim.Definitions);
        foreach (var id in new[] { "test.negative", "test.seal", "test.protected_ge", "test.positive" })
            Assert.That(app.ApplyToTargets(sim, Enemy(), [Player()], id), Is.True);
        Assert.That(app.ApplyToTargets(sim, Enemy(), [Player(1)], "test.negative"), Is.True);
        Assert.That(sim.Buffs.DispelDebuffs(sim, Player()), Is.EqualTo(3));
        var owner = sim.PlayerTeam.Characters[0];
        Assert.That(owner.Buffs.Find("test.debuff"), Is.Null);
        Assert.That(owner.Buffs.Find("test.protected"), Is.Not.Null);
        Assert.That(owner.Buffs.Find("test.beneficial"), Is.Not.Null);
        Assert.That(owner.HandSlots[2].Buffs.Find("test.slot"), Is.Not.Null);
        Assert.That(owner.Asc.ActiveEffects.Select(e => e.Def.Id), Is.EquivalentTo(new[] { "test.protected_ge", "test.positive" }));
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find("test.debuff"), Is.Not.Null);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.ActiveEffects.Single().Def.Id, Is.EqualTo("test.negative"));
    }

    [Test]
    public void Cleanse_does_not_remove_gas_debuffs_created_by_removal_hooks()
    {
        using var sim = Build();
        AddGe(sim, "test.spawned", ["debuff.spawned"]);
        sim.Definitions.Store.SkillActionsMutable["test.spawn_ge"] = new SkillActionDto
        { Id = "test.spawn_ge", Kind = ESkillActionKind.ApplyGameplayEffect, Params = new() { ["gameplayEffectId"] = "test.spawned" } };
        // GE removal callbacks share the same snapshot guarantee as Buff removal callbacks.
        sim.Definitions.Store.GameplayEffectsMutable["test.original"] = new GameplayEffectDefDto
        {
            Id = "test.original", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 3,
            GrantedTags = ["debuff.original"], Hooks = new() { OnRemove = [new() { ActionId = "test.spawn_ge" }] }
        };
        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy(), [Player()], "test.original");
        Assert.That(sim.Buffs.DispelDebuffs(sim, Player()), Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].Asc.ActiveEffects.Single().Def.Id, Is.EqualTo("test.spawned"));
    }

    [Test]
    public void Charge_two_cleanses_only_after_two_plays_from_slot_one_and_resets_permanently()
    {
        using var sim = Build();
        Passive(sim, 1); Passive(sim, 2); Passive(sim, 4);
        var old = Feathers(sim);
        PlaySlot(sim, 1); PlaySlot(sim, 0);
        Assert.That(Feathers(sim), Is.EqualTo(old));
        Assert.That(Hp(sim), Is.EqualTo(1000));
        PlaySlot(sim, 0);
        Assert.That(Hp(sim), Is.EqualTo(926));
        Assert.That(Feathers(sim).All(f => !old.Contains(f)), Is.True);
        var charge = sim.PlayerTeam.Characters[0].HandSlots[0].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
        Assert.That(charge.ChargeCounter, Is.EqualTo(2));
        Assert.That(charge.RemainingTurns, Is.Null);
        PlaySlot(sim, 0); PlaySlot(sim, 0);
        Assert.That(Hp(sim), Is.EqualTo(852));
        Assert.That(charge.ChargeCounter, Is.EqualTo(2));
    }

    [Test]
    public void Slot_damage_immunity_does_not_prevent_charge_or_remove_slot_debuff()
    {
        using var sim = Build();
        Passive(sim, 1); Passive(sim, 2);
        sim.Definitions.Store.EffectsMutable["test.slot_damage"] = new EffectDto
        { Id = "test.slot_damage", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 25 } };
        sim.Definitions.Store.BuffsMutable["test.slot_damage"] = new BuffDto
        {
            Id = "test.slot_damage", Tags = [BuiltinBuffTags.SlotDamage],
            Hooks = new() { OnSlotCardPlayed = [new() { EffectId = "test.slot_damage" }] }
        };
        sim.Buffs.ApplyToSlot(sim, 0, 0, "test.slot_damage");
        var hp = sim.PlayerTeam.SharedHpExact;
        PlaySlot(sim, 0); PlaySlot(sim, 0);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(hp));
        Assert.That(Hp(sim), Is.EqualTo(963));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].Buffs.Find("test.slot_damage"), Is.Not.Null);
        sim.Buffs.ApplyToSlot(sim, 1, 0, "test.slot_damage");
        PlaySlot(sim, 0, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(hp - 25));
    }

    [Test]
    public void Cleanse_action_participates_in_cycle_validation_while_delayed_feather_reapplication_is_admitted()
    {
        var store = new GameDefinitionStore();
        store.BuffsMutable["bad"] = new BuffDto
        {
            Id = "bad", StackRule = EBuffStackRule.Independent, MaxStacks = 2, Tags = ["debuff"],
            Hooks = new() { OnApply = [new() { EffectId = "clean" }], OnRemove = [new() { EffectId = "reapply" }] }
        };
        store.EffectsMutable["clean"] = new EffectDto { Id = "clean", Kind = EEffectKind.DispelDebuffs };
        store.EffectsMutable["reapply"] = new EffectDto { Id = "reapply", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "bad" } };
        Assert.That(new ContentDefinitionValidator().Validate(store).Any(e => e.Message.Contains("Synchronous reference cycle")), Is.True);
        store.BuffsMutable["bad"] = new BuffDto
        {
            Id = "bad", StackRule = EBuffStackRule.Independent, MaxStacks = 2, Tags = ["debuff"],
            Hooks = new() { OnRemove = [new() { EffectId = "reapply" }] }
        };
        Assert.That(new ContentDefinitionValidator().Validate(store).Any(e => e.Message.Contains("Synchronous reference cycle")), Is.False);
    }

    #endregion

    #region 协同与专属卡

    [Test]
    public void Progress_stacks_self_red_machine_and_animal_at_floor_start_and_every_nine_turns()
    {
        using var sim = Build();
        Passive(sim, 3); sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 2, 1, 0 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 8; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 2, 1, 0 }));
    }

    [TestCase("xuanyu_feather_shed", 926, 926, 40, 0, 1)]
    [TestCase("xuanyu_thunder_raid", 955, 1000, 40, 0, 0)]
    [TestCase("xuanyu_aerial_patrol", 1000, 1000, 48, 20, 0)]
    [TestCase("xuanyu_tempest_echo", 909, 909, 40, 0, 0)]
    public void Exclusive_cards_execute_their_declared_payloads(string card, int selectedHp, int otherHp, int attack, int shield, int progress)
    {
        using var sim = Build(cards: [card]);
        Passive(sim, 1); Passive(sim, 4);
        var result = sim.TryApply(new PlayCardCommand(0, 0, card == "xuanyu_thunder_raid" ? [Enemy()] : []));
        Assert.That(result.Success, Is.True, result.Error);
        sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase();
        Assert.That(Hp(sim), Is.EqualTo(selectedHp));
        Assert.That(Hp(sim, 1), Is.EqualTo(otherHp));
        Assert.That(Value(sim, AttributeIds.MagicAttack), Is.EqualTo(attack));
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(shield));
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(progress));
        Assert.That(sim.PlayerTeam.Characters[0].Graveyard.Single().CardId, Is.EqualTo(card));
    }

    #endregion

    #region 构造与断言

    private static CombatSimulation Build(bool start = false, string[]? cards = null)
    {
        var identities = new[] { (EElement.Red, ERace.Machine | ERace.Animal), (EElement.Red, ERace.Machine), (EElement.Blue, ERace.Animal), (EElement.Yellow, ERace.God) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "xuanyu" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 40, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["xuanyu"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var c in characters) c.RefillAvailableEnergy();
        var enemies = Enumerable.Range(0, 3).Select(i => new EnemyUnit($"e{i}", "unknown", new Dictionary<string, float>
        {
            [AttributeIds.MaxHealth] = 1000, [AttributeIds.PhysicalDefense] = 10, [AttributeIds.MagicDefense] = 15
        }));
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies), new CombatRuleEngine([]), BaseGameContent.BuildRegistry(),
            initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009,
            initialBuffs: start ? BaseGameContent.Load().Characters["xuanyu"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
    }

    private static void AddBuff(CombatSimulation sim, string id, List<string> tags) =>
        sim.Definitions.Store.BuffsMutable[id] = new BuffDto { Id = id, Tags = tags };
    private static void AddGe(CombatSimulation sim, string id, List<string> tags) =>
        sim.Definitions.Store.GameplayEffectsMutable[id] = new GameplayEffectDefDto
        { Id = id, DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 3, GrantedTags = tags };
    private static BuffInstance[] Feathers(CombatSimulation sim) => sim.PlayerTeam.Characters[0].Buffs.All.Where(f => f.Def.Id == Feather).ToArray();
    private static void Passive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"xuanyu_passive_p{i}");
    private static void PlaySlot(CombatSimulation sim, int slot, int owner = 0) => sim.Buffs.FireSlotCardPlayed(sim, owner, sim.PlayerTeam.Characters[owner].HandSlots[slot]);
    private static float Value(CombatSimulation sim, string id) => sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(id);
    private static float Hp(CombatSimulation sim, int index = 0) => sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Health);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();

    #endregion
}