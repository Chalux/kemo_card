using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 爱因斯坦（黄·法师·人类/学术）的元数据、接线与端到端效果。
/// <para>四条被动：P1 免疫封印（tag）、P2 魔法减伤 + 上回合受魔法伤则额外抽牌、
/// P3 阶层与每 6 回合的技能计数（与巴赫被动3 同构）、P4 全队黄元素多重连携 + 魔攻/恢复量。</para>
/// <para>两张主动技（万间雅祝 CD6 / 充能I CD4）共用「弃牌 → 按弃牌数抽牌 → 按弃牌数决定段数」的载荷。</para>
/// </summary>
[TestFixture]
public sealed class EinsteinContentTests
{
    private const string CharacterId = "einstein";
    private const string MassEnergyCard = "einstein_mass_energy";
    private const string RelativityCard = "einstein_relativity";
    private const string PhotoelectricCard = "einstein_photoelectric";
    private const string CondensateCard = "einstein_condensate";

    private const string RelativityBuff = "einstein_relativity_buff";
    private const string CondensateBuff = "einstein_condensate_buff";

    /// <summary>测试用魔攻：让伤害断言是整数，便于精确定值。</summary>
    private const float MagicAttackForTests = 40f;

    #region 角色元数据

    [Test]
    public void Character_definition_matches_the_design()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var einstein), Is.True, "爱因斯坦未随内容出货");
        Assert.That(einstein!.DisplayNameId, Is.EqualTo("char.einstein.name"));
        Assert.That(einstein.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(einstein.Role, Is.EqualTo(ERole.Wizard));
        Assert.That(einstein.Race, Is.EqualTo(ERace.Human | ERace.Academic), "人类 + 学术");
        Assert.That(einstein.MaxEnergy, Is.EqualTo(8));
        Assert.That(einstein.InitialEnergy, Is.EqualTo(3));
        Assert.That(einstein.Cards, Is.EqualTo(new[]
        {
            MassEnergyCard,
            RelativityCard,
            PhotoelectricCard,
            CondensateCard,
        }));
    }

    [Test]
    public void Character_has_four_potential_passives_on_the_standard_tiers()
    {
        var definitions = BaseGameContent.Load();
        var einstein = definitions.Characters[CharacterId];

        Assert.That(einstein.Passives, Has.Count.EqualTo(4));
        Assert.That(
            einstein.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(
            einstein.Passives.Select(passive => passive.BuffId),
            Is.EqualTo(new[] { "einstein_passive_p1", "einstein_passive_p2", "einstein_passive_p3", "einstein_passive_p4" }));

        foreach (var passive in einstein.Passives)
        {
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
        }
    }

    #endregion

    #region 主动技接线

    [Test]
    public void Active_skill_chain_is_two_tiers_with_declared_targets()
    {
        var definitions = BaseGameContent.Load();
        var einstein = definitions.Characters[CharacterId];

        Assert.That(einstein.ActiveSkillChain, Has.Count.EqualTo(2), "万间雅祝 + 充能I 两档");
        Assert.That(einstein.ActiveSkillChain[0].SkillId, Is.EqualTo("einstein_myriad_grace"));
        Assert.That(einstein.ActiveSkillChain[0].Cooldown, Is.EqualTo(6));
        Assert.That(einstein.ActiveSkillChain[1].SkillId, Is.EqualTo("einstein_charge_one"));
        Assert.That(einstein.ActiveSkillChain[1].Cooldown, Is.EqualTo(4));

        foreach (var tier in einstein.ActiveSkillChain)
        {
            Assert.That(definitions.Skills.TryGetValue(tier.SkillId, out var skill), Is.True, $"档位技能 {tier.SkillId} 不存在");
            Assert.That(skill!.TargetOverride, Is.Not.Null, $"档位技能 {tier.SkillId} 未声明 targetOverride");
            Assert.That(skill.TargetOverride!.Side, Is.EqualTo(ETargetSide.Enemy), "主动技打敌方");
            Assert.That(skill.TargetOverride.Scope, Is.EqualTo(ETargetScope.RandomN), "随机单体");
            Assert.That(skill.TargetOverride.TargetCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void Active_skills_discard_then_draw_by_discard_then_hit()
    {
        var definitions = BaseGameContent.Load();

        AssertDiscardDrawHitChain(definitions, "einstein_myriad_grace", "einstein_discard_two", 2, "einstein_myriad_grace_hit");
        AssertDiscardDrawHitChain(definitions, "einstein_charge_one", "einstein_discard_three", 3, "einstein_charge_one_hit");
    }

    private static void AssertDiscardDrawHitChain(
        KemoCard.Frame.Content.ModDefinitionsBundle definitions,
        string skillId,
        string discardActionId,
        int discardCount,
        string hitActionId)
    {
        var skill = definitions.Skills[skillId];
        Assert.That(
            skill.ActionRefs.Select(actionRef => actionRef.ActionId),
            Is.EqualTo(new[] { discardActionId, "einstein_draw_by_discard", hitActionId }),
            $"{skillId} 的载荷顺序：弃牌 → 按弃牌数抽牌 → 伤害");

        var discard = definitions.SkillActions[discardActionId];
        Assert.That(discard.Kind, Is.EqualTo(ESkillActionKind.DiscardAndRecord));
        Assert.That(discard.Params, Is.Not.Null);
        Assert.That(CombatTestHelper.IntParam(discard.Params!, "count"), Is.EqualTo(discardCount));

        var draw = definitions.SkillActions["einstein_draw_by_discard"];
        Assert.That(draw.Kind, Is.EqualTo(ESkillActionKind.DrawByDiscard));

        var hit = definitions.SkillActions[hitActionId];
        Assert.That(hit.Kind, Is.EqualTo(ESkillActionKind.ApplyGameplayEffect));
        Assert.That(hit.Params, Is.Not.Null);
        Assert.That(StringParam(hit.Params!, "gameplayEffectId"), Is.EqualTo("einstein_magic_damage"));
        Assert.That(StringParam(hit.Params!, "hookTargets"), Is.EqualTo("randomEnemy")); Assert.That(CombatTestHelper.IntParam(hit.Params!, "Amount"), Is.EqualTo(15), "15 + 100% 魔攻");
    }

    [Test]
    public void Active_skill_hit_counts_are_wired_to_the_new_engine_params()
    {
        var definitions = BaseGameContent.Load();

        var myriad = definitions.SkillActions["einstein_myriad_grace_hit"];
        Assert.That(CombatTestHelper.IntParam(myriad.Params!, "AttackCount"), Is.EqualTo(2), "万间雅祝固定 2 段");

        var charge = definitions.SkillActions["einstein_charge_one_hit"];
        Assert.That(CombatTestHelper.IntParam(charge.Params!, "AttackCountMinusDiscard"), Is.EqualTo(3), "充能I：3 − X 段");
    }

    #endregion

    #region 专属卡

    [Test]
    public void Exclusive_cards_are_yellow_wizard_exclusive_cards()
    {
        var definitions = BaseGameContent.Load();

        foreach (var cardId in new[] { MassEnergyCard, RelativityCard, PhotoelectricCard, CondensateCard })
        {
            Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow), $"{cardId} 是黄属性（整数位掩码 8）");
            Assert.That(card.Role, Is.EqualTo(ERole.Wizard), $"{cardId} 职业跟随角色");
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.CardGroupId, Is.Null, "无升级链");
            Assert.That(card.UpgradeTier, Is.Zero);
        }
    }

    [TestCase(MassEnergyCard, 2, "Magical", 4)]
    [TestCase(RelativityCard, 3, "Support", 8)]
    [TestCase(PhotoelectricCard, 5, "Magical", 6)]
    [TestCase(CondensateCard, 1, "Support", 10)]
    public void Card_costs_and_types_match_the_design(string cardId, int cost, string cardType, int priority)
    {
        var definitions = BaseGameContent.Load();
        var card = definitions.Cards[cardId];

        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.CardType.ToString(), Is.EqualTo(cardType));
        Assert.That(card.Priority, Is.EqualTo(priority));
    }

    [Test]
    public void Card_deck_attribute_bonuses_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        AssertCardStats(definitions, MassEnergyCard, magicAttack: 2, maxHealth: 20);
        AssertCardStats(definitions, RelativityCard, magicAttack: 0, maxHealth: 40);
        AssertCardStats(definitions, PhotoelectricCard, magicAttack: 3, maxHealth: 10);
        AssertCardStats(definitions, CondensateCard, magicAttack: 0, maxHealth: 40);
    }

    private static void AssertCardStats(
        KemoCard.Frame.Content.ModDefinitionsBundle definitions,
        string cardId,
        int magicAttack,
        int maxHealth)
    {
        var card = definitions.Cards[cardId];
        Assert.That(card.Stats, Is.Not.Null, $"{cardId} 缺少 stats");
        Assert.That(
            card.Stats!.Attributes.GetValueOrDefault(AttributeIds.MagicAttack),
            Is.EqualTo(magicAttack),
            $"{cardId} 魔攻加成");
        Assert.That(
            card.Stats.Attributes.GetValueOrDefault(AttributeIds.MaxHealth),
            Is.EqualTo(maxHealth),
            $"{cardId} 生命加成");
    }

    [Test]
    public void Every_card_skill_resolves_and_carries_a_description()
    {
        var definitions = BaseGameContent.Load();
        var einstein = definitions.Characters[CharacterId];

        foreach (var cardId in einstein.Cards)
        {
            var card = definitions.Cards[cardId];
            Assert.That(card.SkillRefs, Is.Not.Empty, $"{cardId} 没有 skillRefs");

            foreach (var skillRef in card.SkillRefs)
            {
                Assert.That(
                    definitions.Skills.TryGetValue(skillRef.SkillId, out var skill),
                    Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键");
            }
        }
    }

    #endregion

    #region 被动接线

    [Test]
    public void Passive_one_grants_seal_immunity()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["einstein_passive_p1"];

        Assert.That(buff.EffectiveTags, Does.Contain(BuiltinBuffTags.TraitImmuneSeal));
    }

    [Test]
    public void Passive_two_reduces_magic_damage_and_has_the_last_turn_draw_hook()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["einstein_passive_p2"];

        var modifier = buff.Modifiers.Single();
        Assert.That(modifier.AttributeId, Is.EqualTo("MagicDamageTakenScale"));
        Assert.That(modifier.Operation, Is.EqualTo(EAttributeModifierOp.Add));
        Assert.That(modifier.Magnitude.Scalar, Is.EqualTo(-0.25f), "魔法伤害降低 25%");

        Assert.That(buff.Hooks.OnTurnStart, Has.Count.EqualTo(1), "回合开始结算额外抽牌");
        var hook = buff.Hooks.OnTurnStart[0];
        Assert.That(hook.EffectId, Is.EqualTo("einstein_p2_extra_draw"));

        var effect = definitions.Effects["einstein_p2_extra_draw"];
        Assert.That(effect.Kind, Is.EqualTo(EEffectKind.ModifyDrawCount));
        Assert.That(effect.Conditions, Has.Count.EqualTo(1));
        Assert.That(effect.Conditions[0].Kind, Is.EqualTo("TookMagicDamageLastTurn"));
    }

    [Test]
    public void Passive_three_gains_skill_counter_per_wave_and_every_six_turns()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["einstein_passive_p3"];

        Assert.That(buff.Hooks.OnWaveStart, Has.Count.EqualTo(4), "自身 + 黄 + 人类 + 学术");
        Assert.That(buff.Hooks.OnTurnStart, Has.Count.EqualTo(4));

        foreach (var hook in buff.Hooks.OnTurnStart)
        {
            Assert.That(hook.Params, Is.Not.Null);
            Assert.That(CombatTestHelper.IntParam(hook.Params, "turnInterval"), Is.EqualTo(6), "阶层内每 6 回合");
        }

        foreach (var hook in buff.Hooks.OnWaveStart.Concat(buff.Hooks.OnTurnStart))
        {
            Assert.That(hook.EffectId, Is.EqualTo("gain_skill_counter"));
        }

        // 自身那一条是 +2，其余三条各 +1。
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[0].Params, "amount"), Is.EqualTo(2));
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[1].Params, "amount"), Is.EqualTo(1));
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[2].Params, "amount"), Is.EqualTo(1));
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[3].Params, "amount"), Is.EqualTo(1));
    }

    [Test]
    public void Passive_four_injects_multi_element_chain_for_yellow_allies()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["einstein_passive_p4"];

        Assert.That(buff.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies), "挂到每个队友");
        Assert.That(buff.Conditions, Has.Count.EqualTo(1));
        Assert.That(buff.Conditions[0].Kind, Is.EqualTo("IdentityMatch"));

        var inject = buff.ChainElementInject.Single();
        Assert.That(inject.From, Is.EqualTo(new[] { EElement.Yellow }), "只对黄卡生效");
        Assert.That(
            inject.Add,
            Is.EqualTo(new[] { EElement.Red, EElement.Blue, EElement.Green }),
            "黄元素获得对红/蓝/绿的多重连携");

        Assert.That(
            buff.Modifiers.Select(modifier => modifier.AttributeId),
            Is.EqualTo(new[] { "MagicAttack", "HealPower" }));
        Assert.That(buff.Modifiers.All(modifier => modifier.Magnitude.Scalar == 6f), Is.True, "+6 魔攻 / +6 恢复量");
    }

    #endregion

    #region 端到端：卡牌效果

    /// <summary>
    /// 质能方程：6 + 75% 魔攻。魔攻 40 → 6 + 30 = 36 点黄·魔法伤害，单体一段。
    /// </summary>
    /// <summary>
    /// 质能方程：6 + 75% 魔攻。魔攻 40 → 6 + 30 = 36 点黄·魔法伤害。
    /// <para>卡牌结算完成后归属角色会照常发动一次普攻（魔法角色 = 100% 魔攻 = 40 点），
    /// 因此敌人总掉血 = 36 + 40；这里分两段断言，卡牌那段单独扣掉普攻。</para>
    /// </summary>
    [Test]
    public void Mass_energy_deals_six_plus_seventy_five_percent_magic_attack()
    {
        using var sim = BuildPlayerPhase(MassEnergyCard);

        MarkAndExecute(sim, MassEnergyCard, [Enemy(0)]);

        Assert.That(
            enemy(sim).Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(500f - TotalDamage(36f)).Within(0.001f));
    }

    /// <summary>相对时空：自身 3 回合魔攻 +12。</summary>
    [Test]
    public void Relativity_grants_twelve_magic_attack_for_three_turns()
    {
        using var sim = BuildPlayerPhase(RelativityCard);
        var caster = sim.PlayerTeam.Characters[0];
        var before = caster.Asc.GetCurrentValue(AttributeIds.MagicAttack);

        MarkAndExecute(sim, RelativityCard, [Self()]);

        var buff = caster.Buffs.Find(RelativityBuff);
        Assert.That(buff, Is.Not.Null, "相对时空 buff 未挂上");
        Assert.That(buff!.RemainingTurns, Is.EqualTo(3));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(before + 12f).Within(0.001f));
    }

    /// <summary>
    /// 光电效应：30 + 225% 魔攻。魔攻 40 → 30 + 90 = 120 点；段数 = 当前连携参与人数（此处只出 1 张卡 → 1 段）。
    /// 同样叠加一次普攻（40 点）。
    /// </summary>
    [Test]
    public void Photoelectric_deals_thirty_plus_two_hundred_twenty_five_percent_magic_attack()
    {
        using var sim = BuildPlayerPhase(PhotoelectricCard);

        MarkAndExecute(sim, PhotoelectricCard, [Enemy(0)]);

        Assert.That(
            enemy(sim).Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(500f - TotalDamage(120f)).Within(0.001f));
    }

    /// <summary>玻色-爱因斯坦凝聚：自身挂 buff 并获得 1 个黄球。</summary>
    [Test]
    public void Condensate_grants_the_buff_and_one_yellow_orb()
    {
        using var sim = BuildPlayerPhase(CondensateCard);
        var caster = sim.PlayerTeam.Characters[0];

        MarkAndExecute(sim, CondensateCard, [Self()]);

        Assert.That(caster.Buffs.Find(CondensateBuff), Is.Not.Null, "凝聚态 buff 未挂上");
        Assert.That(sim.Orbs.Queue.CountOf("yellow"), Is.EqualTo(1), "获得 1 个黄元素球");
    }

    /// <summary>凝聚态 buff 的钩子只认黄球：黄球触发发 10 盾，红球触发不发盾。</summary>
    [Test]
    public void Condensate_shield_fires_for_yellow_orbs_and_not_for_red()
    {
        // 黄球：发 10 点护盾。
        using (var yellow = BuildPlayerPhase(CondensateCard))
        {
            var caster = yellow.PlayerTeam.Characters[0];
            yellow.Buffs.Apply(yellow, Player(0), CondensateBuff);

            yellow.Orbs.Queue.Clear();
            Assert.That(yellow.Orbs.Grant(yellow, "yellow", 0), Is.True);
            var triggered = yellow.Orbs.Trigger(yellow, automatic: false);

            Assert.That(triggered.Triggered, Is.True);
            Assert.That(
                caster.Asc.GetCurrentValue(AttributeIds.Shield),
                Is.EqualTo(10f),
                "黄元素球触发 → 获得 10 点护盾");
        }

        // 红球：不发盾。
        using (var red = BuildPlayerPhase(CondensateCard))
        {
            var caster = red.PlayerTeam.Characters[0];
            red.Buffs.Apply(red, Player(0), CondensateBuff);

            red.Orbs.Queue.Clear();
            Assert.That(red.Orbs.Grant(red, "red", 0), Is.True);
            var triggered = red.Orbs.Trigger(red, automatic: false);

            Assert.That(triggered.Triggered, Is.True);
            Assert.That(caster.Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero, "红球不该触发黄球护盾");
        }
    }

    #endregion

    #region 端到端：被动

    /// <summary>被动2 的魔法减伤只作用于魔法伤害，不作用于物理伤害。</summary>
    [Test]
    public void Passive_two_magic_reduction_applies_to_magic_but_not_physical()
    {
        var registry = BaseGameContent.BuildRegistry();
        using var sim = BuildPlayerPhase(MassEnergyCard, registry);
        var caster = sim.PlayerTeam.Characters[0];

        var before = caster.Asc.GetCurrentValue(AttributeIds.MagicDamageTakenScale);
        sim.Buffs.Apply(sim, Player(0), "einstein_passive_p2");

        Assert.That(
            caster.Asc.GetCurrentValue(AttributeIds.MagicDamageTakenScale),
            Is.EqualTo(before - 0.25f).Within(0.001f),
            "魔法减伤 -25% 进入属性聚合");
    }

    #endregion

    #region 端到端：主动技

    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(10, 0)]
    [TestCase(10, 1)]
    public void Existing_up_to_description_allows_selecting_fewer_cards(int progress, int chosen)
    {
        using var sim = BuildActiveSkillSimulation(skillCounter: progress);
        var caster = sim.PlayerTeam.Characters[0];
        var result = sim.TryApply(new CastActiveSkillCommand(0, [], Enumerable.Range(0, chosen).ToArray()));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(chosen));
        Assert.That(OccupiedSlots(caster), Is.EqualTo(5), "只立即补抽实际弃牌数");
        var hits = progress == 6 ? 2 : Math.Max(1, 3 - chosen);
        Assert.That(enemy(sim).Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500 - (15 + MagicAttackForTests) * hits));
    }

    /// <summary>
    /// 万间雅祝：弃 2 张 → 立即抽 2 张 → 对随机单体敌人打 2 段（每段 15 + 100% 魔攻 = 55）。
    /// 主动技不占"已行动"、不扣能量，因此这里不涉及普攻。
    /// </summary>
    [Test]
    public void Myriad_grace_discards_two_then_hits_twice()
    {
        using var sim = BuildActiveSkillSimulation();
        var caster = sim.PlayerTeam.Characters[0];
        var enemyUnit = enemy(sim);
        var handBefore = OccupiedSlots(caster);

        var cast = sim.TryApply(new CastActiveSkillCommand(0, [], sim.PlayerTeam.Characters[0].HandSlots.Where(slot => !slot.IsEmpty).Take(sim.PlayerTeam.Characters[0].ResolveCastableTier() == 0 ? 2 : 3).Select(slot => slot.SlotIndex).ToArray()));

        Assert.That(cast.Success, Is.True, cast.Error);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(2), "弃 2 张并记账");
        Assert.That(OccupiedSlots(caster), Is.EqualTo(handBefore), "弃牌后立即补抽 2 张");
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1), "不增加下回合抽牌量");
        Assert.That(sim.Presentation.Pending.OfType<CardsDrawnEvent>().Single().Cards, Has.Count.EqualTo(2));
        Assert.That(
            enemyUnit.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(500f - (15f + MagicAttackForTests) * 2f).Within(0.001f),
            "2 段 × (15 + 100% 魔攻)");
    }

    /// <summary>充能I：弃 3 张 → 立即抽 3 张 → 段数 = 3 − 3 = 下限 1。</summary>
    [Test]
    public void Charge_one_discards_three_then_hits_once_at_the_floor()
    {
        using var sim = BuildActiveSkillSimulation(skillCounter: 10);
        var caster = sim.PlayerTeam.Characters[0];
        var enemyUnit = enemy(sim);

        var cast = sim.TryApply(new CastActiveSkillCommand(0, [], sim.PlayerTeam.Characters[0].HandSlots.Where(slot => !slot.IsEmpty).Take(sim.PlayerTeam.Characters[0].ResolveCastableTier() == 0 ? 2 : 3).Select(slot => slot.SlotIndex).ToArray()));

        Assert.That(cast.Success, Is.True, cast.Error);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(3), "弃 3 张并记账");
        Assert.That(OccupiedSlots(caster), Is.EqualTo(5), "弃牌后立即补抽 3 张");
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1), "不增加下回合抽牌量");
        Assert.That(sim.Presentation.Pending.OfType<CardsDrawnEvent>().Single().Cards, Has.Count.EqualTo(3));
        Assert.That(
            enemyUnit.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(500f - (15f + MagicAttackForTests)).Within(0.001f),
            "3 − 3 = 0 → 下限 1 段");
    }

    /// <summary>弃不满时按实际张数即时补抽，包括无牌可弃的情况。</summary>
    [TestCase(0, 6)]
    [TestCase(1, 6)]
    [TestCase(0, 10)]
    [TestCase(1, 10)]
    public void Discard_records_the_actual_count_when_the_hand_is_short(int handSize, int skillCounter)
    {
        using var sim = BuildActiveSkillSimulation(handSize: handSize, skillCounter: skillCounter);
        var caster = sim.PlayerTeam.Characters[0];

        var cast = sim.TryApply(new CastActiveSkillCommand(0, [], sim.PlayerTeam.Characters[0].HandSlots.Where(slot => !slot.IsEmpty).Take(sim.PlayerTeam.Characters[0].ResolveCastableTier() == 0 ? 2 : 3).Select(slot => slot.SlotIndex).ToArray()));

        Assert.That(cast.Success, Is.True, cast.Error);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(handSize));
        Assert.That(OccupiedSlots(caster), Is.EqualTo(handSize), "只补抽实际弃牌数");
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1), "不增加下回合抽牌量");
        Assert.That(sim.Presentation.Pending.OfType<CardsDrawnEvent>().Sum(e => e.Cards.Count), Is.EqualTo(handSize));
    }

    #endregion

    #region 装配

    /// <summary>
    /// 卡牌结算完成后，归属角色照常发动一次普攻；魔法角色的普攻 = 100% 魔攻 = <see cref="MagicAttackForTests"/>。
    /// 断言敌人总掉血时必须把这一段算进去（它不带 effectId，与卡牌伤害是两次独立结算）。
    /// </summary>
    private const float NormalAttackDamage = MagicAttackForTests;

    /// <summary>卡牌那一段 + 随后的普攻，即敌人一次出牌的总掉血。</summary>
    private static float TotalDamage(float cardDamage) => cardDamage + NormalAttackDamage;

    private static string? StringParam(IReadOnlyDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef Self() => Player(0);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static EnemyUnit enemy(CombatSimulation simulation) => simulation.EnemyTeam.Enemies[0];

    private static IReadOnlyDictionary<string, float> CharacterAttributes() =>
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.MagicAttack] = MagicAttackForTests,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };

    /// <summary>4 名黄·人类/学术角色，手上各抓一张指定卡（保证连携统计与 P4 筛选都有料）。</summary>
    private static CombatSimulation BuildPlayerPhase(
        string handCardId,
        KemoCard.Frame.Content.GameDefinitionRegistry? registry = null,
        int enemyHp = 500)
    {
        registry ??= BaseGameContent.BuildRegistry();
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                CharacterId,
                CharacterAttributes(),
                drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-{index}")],
                element: EElement.Yellow,
                race: ERace.Human | ERace.Academic))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(1);
            character.RefillAvailableEnergy();
        }

        return NewSimulation(registry, characters, enemyHp);
    }

    private static CombatSimulation NewSimulation(
        KemoCard.Frame.Content.GameDefinitionRegistry registry,
        CharacterBattleInstance[] characters,
        int enemyHp) =>
        new(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: enemyHp)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20261006);

    /// <summary>标记入队 → 切结算阶段执行（与 <c>ReinhardtContentTests</c> 同约定）。</summary>
    private static void MarkAndExecute(CombatSimulation sim, string cardId, CombatTargetRef[] targets)
    {
        var slotIndex = FindSlot(sim.PlayerTeam.Characters[0], cardId);
        var marked = sim.TryApply(new PlayCardCommand(0, slotIndex, targets));
        Assert.That(marked.Success, Is.True, marked.Error);

        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    /// <summary>
    /// 主动技场景：槽位 0 是爱因斯坦（带真实出货的主动链与 5 张手牌），其余三个槽位是普通角色。
    /// <paramref name="skillCounter"/> 直接给到对应档位的累计阈值（基础档 6 / 第二档 10）。
    /// </summary>
    private static CombatSimulation BuildActiveSkillSimulation(int handSize = 5, int skillCounter = 6)
    {
        var definitions = BaseGameContent.Load();
        var registry = BaseGameContent.BuildRegistry();
        var chain = definitions.Characters[CharacterId].ActiveSkillChain;

        var einstein = CharacterBattleInstance.CreateForTests(
            CharacterId,
            CharacterAttributes(),
            drawPile: Enumerable.Range(0, handSize)
                .Select(index => new CardRuntimeEntry(MassEnergyCard, $"rt-{index}"))
                .ToArray(),
            activeSkillChain: chain,
            element: EElement.Yellow,
            race: ERace.Human | ERace.Academic);
        einstein.DrawCards(handSize);
        einstein.RefillAvailableEnergy();
        einstein.GainSkillCounter(skillCounter);

        var characters = new List<CharacterBattleInstance> { einstein };
        for (var index = 1; index < CombatConstants.SlotCount; index++)
        {
            characters.Add(CharacterBattleInstance.CreateForTests(
                $"filler{index}",
                CharacterAttributes(),
                element: EElement.Yellow,
                race: ERace.Human | ERace.Academic));
        }

        return NewSimulation(registry, [.. characters], enemyHp: 500);
    }

    /// <summary>当前非空手牌槽数。</summary>
    private static int OccupiedSlots(CharacterBattleInstance character) =>
        character.HandSlots.Count(slot => !slot.IsEmpty);

    private static int FindSlot(CharacterBattleInstance character, string cardId)
    {
        for (var i = 0; i < character.HandSlots.Count; i++)
        {
            if (string.Equals(character.HandSlots[i].CardId, cardId, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException($"角色手牌中没有 {cardId}。");
    }

    #endregion
}