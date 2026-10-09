using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class KagiContentTests
{
    internal static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);
    internal static CombatTargetRef Enemy(int i = 0) => new(ECombatSide.Enemy, i);

    #region 内容与目标锁定

    [Test]
    public void Exact_character_id_and_four_cards_validate_and_have_standard_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(e => e.Message)));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var def = BaseGameContent.Load().Characters["Kagi"];
        Assert.That(def.Role, Is.EqualTo(ERole.Controller));
        Assert.That(def.Race, Is.EqualTo(ERace.Animal));
        Assert.That(def.Element, Is.EqualTo(EElement.Red));
        Assert.That(def.ActiveSkillChain.Single().Cooldown, Is.EqualTo(8));
        Assert.That(def.Passives.Select(p => p.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Red));
            Assert.That(card.Role, Is.EqualTo(ERole.Controller));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_requires_a_selected_living_enemy_and_does_not_pay_for_invalid_targets()
    {
        using var sim = Build();
        var caster = sim.PlayerTeam.Characters[0];
        caster.GainSkillCounter(8);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.False);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [Player()])).Success, Is.False);
        Assert.That(caster.SkillCounter, Is.EqualTo(8));
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [Enemy(1)])).Success, Is.True);
        Assert.That(caster.SkillCounter, Is.Zero);
        var target = sim.EnemyTeam.Enemies[1];
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(5));
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(0.5f));
        Assert.That(target.Buffs.HasTag(BuiltinBuffTags.Marked), Is.True);
        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.HasTag(BuiltinBuffTags.Marked), Is.False);
        for (var turn = 0; turn < 3; turn++) sim.Buffs.FireTurnEnd(sim);
        Assert.That(target.Buffs.HasTag(BuiltinBuffTags.Marked), Is.False);
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.Taunt), Is.Zero);
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.Zero);
    }

    [Test]
    public void Enemy_taunt_limits_single_and_random_targets_but_not_all_targets()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Enemy(1), "kagi_target_mark");
        var card = BaseGameContent.Load().Cards["kagi_lockon_pursuit"];
        Assert.That(CombatTargeting.IsLegalTarget(sim, card, 0, Enemy()), Is.False);
        Assert.That(CombatTargeting.IsLegalTarget(sim, card, 0, Enemy(1)), Is.True);
        Assert.That(CombatTargeting.CollectLegalTargetsForScope(sim, ETargetSide.Enemy, ETargetScope.RandomN, 0), Is.EqualTo(new[] { Enemy(1) }));
        Assert.That(CombatTargeting.CollectLegalTargetsForScope(sim, ETargetSide.Enemy, ETargetScope.All, 0), Has.Count.EqualTo(2));
        sim.EnemyTeam.Enemies[0].Asc.SetBaseValue(AttributeIds.Taunt, 5);
        Assert.That(CombatTargeting.CollectLegalTargetsForScope(sim, ETargetSide.Enemy, ETargetScope.Single, 0), Has.Count.EqualTo(2));
    }

    #endregion

    #region 被动攻守与进度

    [Test]
    public void Stance_switches_immediately_when_marks_apply_dispel_expire_or_the_last_marked_enemy_dies()
    {
        using var sim = Build();
        ApplyPassive(sim, 2);
        AssertStance(sim, false);
        sim.Buffs.Apply(sim, Enemy(), "kagi_target_mark");
        AssertStance(sim, true);
        sim.Buffs.Dispel(sim, Enemy(), "kagi_target_mark");
        AssertStance(sim, false);
        sim.Buffs.Apply(sim, Enemy(), "kagi_hunters_mark_buff");
        sim.Buffs.FireTurnEnd(sim);
        AssertStance(sim, true);
        sim.Buffs.FireTurnEnd(sim);
        AssertStance(sim, false);
        sim.Buffs.Apply(sim, Enemy(), "kagi_target_mark");
        sim.Buffs.Apply(sim, Enemy(1), "kagi_target_mark");
        sim.EffectExecutor.ApplyFixedDamage(sim, Player(), [Enemy()], 10000);
        AssertStance(sim, true);
        sim.EffectExecutor.ApplyFixedDamage(sim, Player(), [Enemy(1)], 10000);
        AssertStance(sim, false);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Buffs.HasTag(BuiltinBuffTags.Marked)), Is.True, "死者仍有标记，但不参与存在判定");
    }

    [Test]
    public void Mark_and_own_bonus_add_for_card_normal_attack_orbs_and_teammate_damage()
    {
        using var sim = Build();
        ApplyPassive(sim, 2);
        sim.Buffs.Apply(sim, Enemy(), "kagi_target_mark");
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_pursuit_hit" }, sim, Player(), [Enemy()]);
        Assert.That(Hp(sim), Is.EqualTo(895), "(10+20)*1.75*2");
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
        sim.NormalAttacks.Execute(sim);
        Assert.That(Hp(sim), Is.EqualTo(860), "20*1.75");
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Red, 0, 3);
        sim.Orbs.TriggerManual(sim);
        Assert.That(Hp(sim), Is.EqualTo(723.5f).Within(0.001), "每球(10+0.8*20)*1.75");
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_pursuit_hit" }, sim, Player(1), [Enemy()]);
        Assert.That(Hp(sim), Is.EqualTo(633.5f).Within(0.001), "队友只享受目标50%增伤");
    }

    [Test]
    public void Defensive_stance_reduces_all_fixed_damage_only_for_kagi()
    {
        using var sim = Build();
        ApplyPassive(sim, 2);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_pursuit_hit" }, sim, Enemy(), [Player()]);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(985), "敌方物攻为0，每段10*0.75");
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_pursuit_hit" }, sim, Enemy(), [Player(1)]);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(965));
    }

    [Test]
    public void Passive_three_stacks_self_red_and_animal_every_eight_floor_turns()
    {
        using var sim = Build();
        ApplyPassive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 6, 2, 2, 0 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 7; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 6, 2, 2, 0 }));
    }

    #endregion

    #region 第五槽充能与卡牌

    [Test]
    public void Fifth_slot_charge_triggers_each_three_cards_without_expiring_or_ticking_from_other_slots()
    {
        using var sim = Build();
        ApplyPassive(sim, 1);
        ApplyPassive(sim, 4);
        var slots = sim.PlayerTeam.Characters[0].HandSlots;
        Assert.That(slots.Take(4).All(s => !s.Buffs.HasTag(BuiltinBuffTags.SlotCharge)), Is.True);
        var charge = slots[4].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)!;
        Assert.That(charge.ChargeRequired, Is.EqualTo(3));
        Assert.That(charge.RemainingTurns, Is.Null);
        sim.Buffs.FireSlotCardPlayed(sim, 0, slots[3]);
        Assert.That(charge.ChargeCounter, Is.EqualTo(3));
        for (var round = 0; round < 2; round++)
        {
            foreach (var enemy in sim.EnemyTeam.Enemies) enemy.ActionCount = 1;
            sim.Buffs.FireSlotCardPlayed(sim, 0, slots[4]);
            sim.Buffs.FireSlotCardPlayed(sim, 0, slots[4]);
            Assert.That(sim.EnemyTeam.Enemies.All(e => e.ActionCount == 1), Is.True);
            sim.Buffs.FireSlotCardPlayed(sim, 0, slots[4]);
            Assert.That(sim.EnemyTeam.Enemies.Count(e => e.ActionCount == 2), Is.EqualTo(1));
            Assert.That(charge.ChargeCounter, Is.EqualTo(3));
            sim.Buffs.FireTurnEnd(sim);
            Assert.That(slots[4].Buffs.FindByTag(BuiltinBuffTags.SlotCharge), Is.SameAs(charge));
        }
    }

    [Test]
    public void Exclusive_cards_apply_marks_hit_twice_weaken_every_enemy_and_prepare_self()
    {
        using var sim = Build();
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_apply_hunters_mark" }, sim, Player(), [Enemy()]);
        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.HasTag(BuiltinBuffTags.Marked), Is.True);
        sim.Buffs.Apply(sim, Enemy(), "kagi_target_mark");
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(0.75f));
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_apply_roar" }, sim, Player(), [Enemy(), Enemy(1)]);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.MagicAttack) == -12), Is.True);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_ambush_shield" }, sim, Player(), [Player()]);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "kagi_ambush_charge" }, sim, Player(), [Player()]);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(25));
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(2));
    }

    [Test]
    public void All_four_cards_execute_through_the_real_queue_with_priorities_and_correct_targets()
    {
        var cards = BaseGameContent.Load().Characters["Kagi"].Cards.ToArray();
        using var sim = Build(cards: cards);
        ApplyPassive(sim, 2);
        for (var slot = 0; slot < 4; slot++)
        {
            var targets = slot < 2 ? new[] { Enemy(1) } : Array.Empty<CombatTargetRef>();
            var result = sim.TryApply(new PlayCardCommand(0, slot, targets));
            Assert.That(result.Success, Is.True, result.Error);
        }
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        Assert.That(sim.EnemyTeam.Enemies[1].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(910), "标记先结算，两段(10+20)*1.5");
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(1000));
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.MagicAttack) == -12), Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(25));
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(2));
        Assert.That(sim.PlayerTeam.Characters[0].Graveyard, Has.Count.EqualTo(4));
    }

    #endregion

    #region 构造与断言

    internal static CombatSimulation Build(bool start = false, string[]? cards = null)
    {
        var identities = new[] { (EElement.Red, ERace.Animal), (EElement.Red, ERace.Human), (EElement.Green, ERace.Animal), (EElement.Blue, ERace.God) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "Kagi" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 10, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["Kagi"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters) character.RefillAvailableEnergy();
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState([new EnemyUnit("e0", "slime", 1000), new EnemyUnit("e1", "slime", 1000)]),
            new CombatRuleEngine([]), BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009,
            initialBuffs: start ? BaseGameContent.Load().Characters["Kagi"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
    }

    internal static void ApplyPassive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"kagi_passive_p{i}");
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();
    private static float Hp(CombatSimulation sim) => sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
    private static void AssertStance(CombatSimulation sim, bool hunting)
    {
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.DamageDealtScale), Is.EqualTo(hunting ? 0.25f : 0));
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(hunting ? 0 : -0.25f));
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(c => c.Asc.GetCurrentValue(AttributeIds.DamageDealtScale) == 0), Is.True);
    }

    #endregion
}