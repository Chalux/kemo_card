using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class ZeusContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);
    private static CombatTargetRef Enemy(int index = 0) => new(ECombatSide.Enemy, index);

    #region 出货定义与主动技

    [Test]
    public void Character_and_exclusive_cards_are_admitted_with_the_requested_identity()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        var definitions = BaseGameContent.Load();
        var zeus = definitions.Characters["zeus"];
        Assert.That(zeus.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(zeus.Role, Is.EqualTo(ERole.Guard));
        Assert.That(zeus.Race, Is.EqualTo(ERace.God));
        Assert.That(zeus.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(zeus.ActiveSkillChain.Select(tier => tier.Cooldown), Is.EqualTo(new[] { 6, 4 }));
        Assert.That(zeus.Cards, Has.Count.EqualTo(4));
        foreach (var id in zeus.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow));
            Assert.That(card.Role, Is.EqualTo(ERole.Guard));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10),
                Is.EqualTo(40), "每张卡遵守 40 最大生命等价预算");
        }
    }

    [TestCase(6, 15, 20)]
    [TestCase(10, 20, 40)]
    public void Active_tiers_grant_two_turn_defenses_and_counter_the_actual_attacker(int progress, int defense, int counter)
    {
        using var sim = NewSimulation();
        var zeus = sim.PlayerTeam.Characters[0];
        zeus.GainSkillCounter(progress);
        var cast = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(cast.Success, Is.True, cast.Error);
        Assert.That(zeus.SkillCounter, Is.Zero);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(defense));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.EqualTo(defense));

        // 护盾吸收也算受击，反击只能打来源（敌人 1），不能随机打敌人 0。
        zeus.Asc.Attributes.SetCurrentValue(AttributeIds.Shield, 50);
        DamagePipeline.Settle(sim, Enemy(1), Player(), 10);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(500));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(500 - counter));

        sim.Buffs.FireTurnEnd(sim);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(defense));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.Zero);
        DamagePipeline.Settle(sim, Enemy(1), Player(), 10);
        sim.FlushOnDamagedHits();
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(500 - counter), "到期后不再反击");
    }

    [Test]
    public void Charged_guard_replaces_base_guard_and_each_hit_keeps_its_source()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_lord_guard");
        sim.Buffs.Apply(sim, Player(), "zeus_charged_guard");
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("zeus_lord_guard"), Is.Null);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(20));
        DamagePipeline.Settle(sim, Enemy(0), Player(), 1);
        DamagePipeline.Settle(sim, Enemy(1), Player(), 1);
        DamagePipeline.Settle(sim, Enemy(1), Player(), 1);
        sim.FlushOnDamagedHits();
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(460));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(420));
    }

    [TestCase(1, 41, 479.5f)]
    [TestCase(3, 43, 437f)]
    public void Defense_negates_health_loss_but_still_triggers_wrath_and_counterattack(int hits, int magicAttack, float enemyHp)
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p2");
        sim.Buffs.Apply(sim, Player(), "zeus_lord_guard");
        sim.EffectExecutor.ExecuteSkillActionRef(new SkillActionRefDto
        {
            ActionId = "training_dummy_strike", Params = new Dictionary<string, object> { ["Amount"] = 5, ["AttackCount"] = 0 },
        }, sim, Enemy(1), [Player()]);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(40));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(500), "显式零次攻击不产生受击事件");
        sim.EffectExecutor.ExecuteSkillActionRef(new SkillActionRefDto
        {
            ActionId = "training_dummy_strike", Params = new Dictionary<string, object> { ["Amount"] = 5, ["AttackCount"] = hits },
        },
            sim, Enemy(1), [Player()]);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(magicAttack));
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(500));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(enemyHp));
    }

    #endregion

    #region 被动边界

    [Test]
    public void Slot_damage_immunity_prevents_the_slot_payload()
    {
        using var sim = NewSimulation();
        var character = sim.PlayerTeam.Characters[0];
        var slotBuff = new BuffDto
        {
            Id = "test_slot_damage",
            Tags = [BuiltinBuffTags.SlotDamage],
            Hooks = new BuffEffectHooksDto { OnSlotCardPlayed = [new EffectRefDto { EffectId = "chalux_charge_burst" }] },
        };
        character.HandSlots[0].Buffs.Add(slotBuff, null);
        // 使用真实出货伤害效果，先证明载荷可造成伤害，再验证免疫门闩。
        sim.Buffs.FireSlotCardPlayed(sim, 0, character.HandSlots[0]);
        var hpAfterControl = sim.PlayerTeam.SharedHp;
        Assert.That(hpAfterControl, Is.LessThan(100));
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p1");
        sim.Buffs.FireSlotCardPlayed(sim, 0, character.HandSlots[0]);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hpAfterControl));
    }

    [Test]
    public void Wrath_grows_per_enemy_hit_caps_at_thirty_and_clears_at_floor_start()
    {
        using var sim = NewSimulation();
        var zeus = sim.PlayerTeam.Characters[0];
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p2");
        for (var hit = 0; hit < 35; hit++)
            DamagePipeline.Settle(sim, Enemy(), Player(), 1);
        sim.FlushOnDamagedHits();
        Assert.That(zeus.Buffs.Find("zeus_wrath")!.Stacks, Is.EqualTo(30));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(70));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(5));
        sim.Buffs.FireWaveStart(sim);
        Assert.That(zeus.Buffs.Find("zeus_wrath"), Is.Null);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(40));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(5));
        DamagePipeline.Settle(sim, Player(), Player(), 1);
        sim.FlushOnDamagedHits();
        Assert.That(zeus.Buffs.Find("zeus_wrath"), Is.Null, "自身伤害不计受击成长");
    }

    [Test]
    public void Floor_and_six_turn_charge_stack_self_element_and_race_independently()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p3");
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
        sim.Buffs.FireTurnStart(sim); // 波内第 0 回合不重复阶层奖励。
        for (var turn = 1; turn <= 5; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 8, 2, 2, 0 }));
    }

    [Test]
    public void Lethal_guard_cancels_the_triggering_hit_is_undispellable_and_is_once_per_battle()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p4");
        DamagePipeline.Settle(sim, Enemy(), Player(), 150);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.True);
        Assert.That(sim.Buffs.Dispel(sim, Player(), "zeus_immortality"), Is.Zero);
        DamagePipeline.Settle(sim, Enemy(), Player(), 150);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        DamagePipeline.Settle(sim, Enemy(), Player(1), 1);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(99), "免疫只保护持有者");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.True);
        sim.Buffs.FireWaveStart(sim); // 换阶层不能重置每场战斗仅一次。
        sim.Buffs.FireTurnStart(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.False);
        DamagePipeline.Settle(sim, Enemy(), Player(), 150);
        Assert.That(sim.PlayerTeam.SharedHp, Is.Zero);
    }

    [Test]
    public void Shield_and_nonlethal_damage_do_not_spend_the_lethal_guard()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p4");
        sim.PlayerTeam.Characters[0].Asc.Attributes.SetCurrentValue(AttributeIds.Shield, 100);
        DamagePipeline.Settle(sim, Enemy(), Player(), 120);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(80));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("zeus_immortality"), Is.Null);
        DamagePipeline.Settle(sim, Player(), Player(), 100); // 致命的自身持续伤害也能触发。
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(80));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("zeus_immortality"), Is.Not.Null);
    }

    [Test]
    public void Immunity_gained_during_turn_start_lasts_until_the_following_turn_start()
    {
        using var sim = NewSimulation(turnStartRule: new LethalTurnStartRule());
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p4");
        sim.BeginNextTurn(incrementTurnsIntoWave: true);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage), Is.True);
        sim.BeginNextTurn(incrementTurnsIntoWave: true);
        Assert.That(sim.PlayerTeam.SharedHp, Is.Zero, "旧免疫必须在回合开始伤害之前解除");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void Battle_start_health_lock_does_not_spend_lethal_protection()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "zeus_passive_p4");
        sim.PlayerTeam.SharedHpLocked = true;
        DamagePipeline.Settle(sim, Enemy(), Player(), 150);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("zeus_immortality"), Is.Null);
        sim.PlayerTeam.SharedHpLocked = false;
        DamagePipeline.Settle(sim, Enemy(), Player(), 150);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("zeus_immortality"), Is.Not.Null);
    }

    #endregion

    #region 专属卡实战

    [Test]
    public void Thunderbolt_damages_only_the_selected_enemy()
    {
        using var sim = NewSimulation("zeus_thunderbolt");
        Play(sim, Enemy(1));
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(500));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(452));
    }

    [Test]
    public void Aegis_grants_consumable_shield_and_temporary_taunt()
    {
        using var sim = NewSimulation("zeus_aegis");
        Play(sim, Player());
        var zeus = sim.PlayerTeam.Characters[0];
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(30));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(3));
        DamagePipeline.Settle(sim, Enemy(), Player(), 40);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(90));
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(zeus.Asc.GetCurrentValue(AttributeIds.Taunt), Is.Zero);
    }

    [Test]
    public void Judgment_hits_all_enemies_and_only_charges_the_caster()
    {
        using var sim = NewSimulation("zeus_divine_judgment");
        Play(sim, Enemy());
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(448));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(448));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 2, 0, 0, 0 }));
    }

    [Test]
    public void Edict_charges_allies_with_independent_identity_bonuses()
    {
        using var sim = NewSimulation("zeus_olympian_edict");
        Play(sim, Player());
        Assert.That(Counters(sim), Is.EqualTo(new[] { 3, 2, 2, 1 }));
    }

    #endregion

    #region 场景构造

    private static CombatSimulation NewSimulation(string? cardId = null, ICombatRule? turnStartRule = null)
    {
        var definitions = BaseGameContent.Load();
        var identities = new[]
        {
            (EElement.Yellow, ERace.God), (EElement.Yellow, ERace.Human),
            (EElement.Red, ERace.God), (EElement.Green, ERace.Human),
        };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "zeus" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 25, [AttributeIds.MagicAttack] = 40,
                [AttributeIds.PhysicalAttack] = 0, [AttributeIds.NormalAttackDamageDealtScale] = -1,
                [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 && cardId is not null ? [new CardRuntimeEntry(cardId, "zeus-card")] : null,
            skillCounterCap: 20,
            activeSkillChain: index == 0 ? definitions.Characters["zeus"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        if (cardId is not null)
            characters[0].DrawCards(1);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        return new CombatSimulation(new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([new EnemyUnit("e0", "slime", 500), new EnemyUnit("e1", "slime", 500)]),
            new CombatRuleEngine(turnStartRule is null ? [] : [turnStartRule]), BaseGameContent.BuildRegistry(),
            initialPhase: ECombatPhase.Player, runSeed: 20261008);
    }

    private static void Play(CombatSimulation sim, CombatTargetRef target)
    {
        var id = sim.PlayerTeam.Characters[0].HandSlots[0].CardId!;
        var card = BaseGameContent.Load().Cards[id];
        IReadOnlyList<CombatTargetRef> targets = [target];
        if (card.TargetScope == ETargetScope.All)
        {
            var count = card.TargetSide == ETargetSide.Ally ? sim.PlayerTeam.Characters.Count : sim.EnemyTeam.Enemies.Count;
            targets = Enumerable.Range(0, count).Select(index =>
                card.TargetSide == ETargetSide.Ally ? Player(index) : Enemy(index)).ToArray();
        }
        var result = sim.TryApply(new PlayCardCommand(0, 0, targets));
        Assert.That(result.Success, Is.True, result.Error);
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static float EnemyHp(CombatSimulation sim, int index) =>
        sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Health);

    private static int[] Counters(CombatSimulation sim) =>
        sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();

    private sealed class LethalTurnStartRule : ICombatRule
    {
        public string Id => "test.lethal_turn_start";
        public int Priority => 0;
        public void OnTurnStart(CombatContext ctx) => DamagePipeline.Settle(ctx.Simulation, Player(), Player(), 150);
    }

    #endregion
}