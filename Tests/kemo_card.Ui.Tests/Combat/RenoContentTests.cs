using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 雷诺（绿·支援者·龙族）的元数据、接线与端到端效果。
/// <para>四条被动：P1 免疫暴风（tag）、P2 回合开始队伍回血、P3 阶层与每 9 回合技能计数、
/// P4 绿·龙族物攻魔攻 +12 且队伍治疗输出 +50%。</para>
/// <para>治疗全部走队伍共享账本（规格 §1.3），断言读 <c>PlayerTeam.SharedHpExact</c>。</para>
/// </summary>
[TestFixture]
public sealed class RenoContentTests
{
    private const string CharacterId = "reno";
    private const string HoardCard = "reno_dragon_hoard";
    private const string BlessingCard = "reno_dragon_blessing";
    private const string BreathCard = "reno_dragon_breath";
    private const string RoarCard = "reno_dragon_roar";

    private const string BoonAllyBuff = "reno_boon_ally";
    private const string BoonSelfBuff = "reno_boon_self";
    private const string RegenBuff = "reno_regen_team";
    private const string HoardHealBuff = "reno_hoard_heal";
    private const string RoarBuff = "reno_roar";
    private const string BreathAtkBuff = "reno_breath_atk";

    /// <summary>测试用恢复量：让治疗断言是整数。</summary>
    private const float HealPowerForTests = 6f;

    #region 角色元数据

    [Test]
    public void Character_definition_matches_the_design()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var reno), Is.True, "雷诺未随内容出货");
        Assert.That(reno!.DisplayNameId, Is.EqualTo("char.reno.name"));
        Assert.That(reno.Element, Is.EqualTo(EElement.Green));
        Assert.That(reno.Role, Is.EqualTo(ERole.Support));
        Assert.That(reno.Race, Is.EqualTo(ERace.Dragon));
        Assert.That(reno.MaxEnergy, Is.EqualTo(8));
        Assert.That(reno.InitialEnergy, Is.EqualTo(3));
        Assert.That(reno.Cards, Is.EqualTo(new[] { HoardCard, BlessingCard, BreathCard, RoarCard }));
    }

    [Test]
    public void Character_has_four_potential_passives_on_the_standard_tiers()
    {
        var definitions = BaseGameContent.Load();
        var reno = definitions.Characters[CharacterId];

        Assert.That(reno.Passives, Has.Count.EqualTo(4));
        Assert.That(
            reno.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(
            reno.Passives.Select(passive => passive.BuffId),
            Is.EqualTo(new[] { "reno_passive_p1", "reno_passive_p2", "reno_passive_p3", "reno_passive_p4" }));

        foreach (var passive in reno.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    #endregion

    #region 主动技接线

    [Test]
    public void Active_skill_chain_is_a_single_tier_with_ally_targeting()
    {
        var definitions = BaseGameContent.Load();
        var reno = definitions.Characters[CharacterId];

        Assert.That(reno.ActiveSkillChain, Has.Count.EqualTo(1), "龙之加护单档");
        Assert.That(reno.ActiveSkillChain[0].SkillId, Is.EqualTo("reno_dragon_boon"));
        Assert.That(reno.ActiveSkillChain[0].Cooldown, Is.EqualTo(9));

        var skill = definitions.Skills["reno_dragon_boon"];
        Assert.That(skill.TargetOverride, Is.Not.Null, "主动技必须显式声明 targetOverride");
        Assert.That(skill.TargetOverride!.Side, Is.EqualTo(ETargetSide.Ally));
        Assert.That(skill.TargetOverride.Scope, Is.EqualTo(ETargetScope.Single));
        Assert.That(
            skill.ActionRefs.Select(actionRef => actionRef.ActionId),
            Is.EqualTo(new[] { "reno_apply_boon_ally", "reno_apply_boon_self" }));
    }

    [Test]
    public void Active_skill_buffs_resolve_with_expected_values()
    {
        var definitions = BaseGameContent.Load();

        var ally = definitions.Buffs[BoonAllyBuff];
        Assert.That(ally.DurationType, Is.EqualTo(EBuffDurationType.Turns));
        Assert.That(ally.Duration, Is.EqualTo(3));
        Assert.That(
            ally.Modifiers.Select(modifier => modifier.AttributeId),
            Is.EqualTo(new[] { "PhysicalAttack", "MagicAttack" }));
        Assert.That(ally.Modifiers.All(modifier => modifier.Magnitude.Scalar == 12f), Is.True, "物攻·魔攻 +12");

        var self = definitions.Buffs[BoonSelfBuff];
        Assert.That(self.Duration, Is.EqualTo(3));
        Assert.That(self.Modifiers.Single().AttributeId, Is.EqualTo("HealPower"));
        Assert.That(self.Modifiers.Single().Magnitude.Scalar, Is.EqualTo(6f), "恢复量 +6");
    }

    #endregion

    #region 专属卡

    [Test]
    public void Exclusive_cards_are_green_support_exclusive_cards()
    {
        var definitions = BaseGameContent.Load();

        foreach (var cardId in new[] { HoardCard, BlessingCard, BreathCard, RoarCard })
        {
            Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Green), $"{cardId} 是绿属性（整数位掩码 4）");
            Assert.That(card.Role, Is.EqualTo(ERole.Support), $"{cardId} 职业跟随角色");
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.CardGroupId, Is.Null, "无升级链");
            Assert.That(card.UpgradeTier, Is.Zero);
        }
    }

    [TestCase(HoardCard, 1, 8)]
    [TestCase(BlessingCard, 2, 6)]
    [TestCase(BreathCard, 3, 7)]
    [TestCase(RoarCard, 5, 5)]
    public void Card_costs_and_priorities_match_the_design(string cardId, int cost, int priority)
    {
        var definitions = BaseGameContent.Load();
        var card = definitions.Cards[cardId];

        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.CardType, Is.EqualTo(ECardType.Support));
        Assert.That(card.Priority, Is.EqualTo(priority));
    }

    [Test]
    public void Card_deck_attribute_bonuses_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        AssertCardStats(definitions, HoardCard, healPower: 1, maxHealth: 30);
        AssertCardStats(definitions, BlessingCard, healPower: 1, maxHealth: 30);
        AssertCardStats(definitions, BreathCard, healPower: 2, maxHealth: 20);
        AssertCardStats(definitions, RoarCard, healPower: 0, maxHealth: 40);
    }

    private static void AssertCardStats(
        KemoCard.Frame.Content.ModDefinitionsBundle definitions,
        string cardId,
        int healPower,
        int maxHealth)
    {
        var card = definitions.Cards[cardId];
        Assert.That(card.Stats, Is.Not.Null, $"{cardId} 缺少 stats");
        Assert.That(
            card.Stats!.Attributes.GetValueOrDefault(AttributeIds.HealPower),
            Is.EqualTo(healPower),
            $"{cardId} 恢复量加成");
        Assert.That(
            card.Stats.Attributes.GetValueOrDefault(AttributeIds.MaxHealth),
            Is.EqualTo(maxHealth),
            $"{cardId} 生命加成");
    }

    [Test]
    public void Every_card_skill_resolves_and_carries_a_description()
    {
        var definitions = BaseGameContent.Load();
        var reno = definitions.Characters[CharacterId];

        foreach (var cardId in reno.Cards)
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
    public void Passive_one_grants_storm_immunity()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["reno_passive_p1"];

        Assert.That(buff.EffectiveTags, Does.Contain(BuiltinBuffTags.TraitImmuneSlotStorm));
    }

    [Test]
    public void Passive_two_heals_the_team_at_turn_start()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["reno_passive_p2"];

        Assert.That(buff.Hooks.OnTurnStart, Has.Count.EqualTo(1));
        var hook = buff.Hooks.OnTurnStart[0];
        Assert.That(hook.EffectId, Is.EqualTo("reno_p2_team_regen"));
        Assert.That(hook.Params, Is.Not.Null);
        Assert.That(StringParam(hook.Params!, "hookTargets"), Is.EqualTo("team"), "治疗落点是队伍共享账本");

        var effect = definitions.Effects["reno_p2_team_regen"];
        Assert.That(effect.Kind, Is.EqualTo(EEffectKind.Heal));
        Assert.That(effect.Params, Is.Not.Null);
        Assert.That(CombatTestHelper.IntParam(effect.Params, "amount"), Is.EqualTo(12));
        Assert.That(CombatTestHelper.IntParam(effect.Params, "healPowerScale"), Is.EqualTo(1), "吃 100% 恢复量");
    }

    [Test]
    public void Passive_three_gains_skill_counter_per_wave_and_every_nine_turns()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["reno_passive_p3"];

        Assert.That(buff.Hooks.OnWaveStart, Has.Count.EqualTo(3), "自身 + 绿 + 龙族");
        Assert.That(buff.Hooks.OnTurnStart, Has.Count.EqualTo(3));

        foreach (var hook in buff.Hooks.OnTurnStart)
            Assert.That(CombatTestHelper.IntParam(hook.Params, "turnInterval"), Is.EqualTo(9), "阶层内每 9 回合");

        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[0].Params, "amount"), Is.EqualTo(3), "自身 +3");
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[1].Params, "amount"), Is.EqualTo(2), "绿 +2");
        Assert.That(CombatTestHelper.IntParam(buff.Hooks.OnWaveStart[2].Params, "amount"), Is.EqualTo(2), "龙族 +2");
    }

    [Test]
    public void Passive_four_buffs_green_dragons_and_amplifies_team_healing()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["reno_passive_p4"];

        Assert.That(buff.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies), "挂到每个队友");
        Assert.That(buff.Conditions, Has.Count.EqualTo(1));
        Assert.That(buff.Conditions[0].Kind, Is.EqualTo("IdentityMatch"));
        Assert.That(
            buff.Modifiers.Select(modifier => modifier.AttributeId),
            Is.EqualTo(new[] { "PhysicalAttack", "MagicAttack" }));
        Assert.That(buff.Modifiers.All(modifier => modifier.Magnitude.Scalar == 12f), Is.True, "物攻·魔攻 +12");

        // 治疗加成走 onApply 挂一个独立的团队 buff。
        Assert.That(buff.Hooks.OnApply, Has.Count.EqualTo(1));
        var effect = definitions.Effects[buff.Hooks.OnApply[0].EffectId];
        Assert.That(effect.Kind, Is.EqualTo(EEffectKind.ApplyBuff));
        Assert.That(effect.Params, Is.Not.Null);
        Assert.That(StringParam(effect.Params!, "buffId"), Is.EqualTo("reno_p4_heal_amp"));

        var amp = definitions.Buffs["reno_p4_heal_amp"];
        Assert.That(
            amp.ApplyScope,
            Is.EqualTo(EBuffApplyScope.Self),
            "逐个队友由效果的 hookTargets: allies 负责，buff 自身不再展开");
        Assert.That(amp.Conditions, Has.Count.EqualTo(1), "持有者条件：只有绿属性施疗者吃加成");
        Assert.That(amp.Conditions[0].Kind, Is.EqualTo("IdentityMatch"));
        Assert.That(StringParam(amp.Conditions[0].Params!, "elementAny"), Does.Contain("Green"));
        var modifier = amp.Modifiers.Single();
        Assert.That(modifier.AttributeId, Is.EqualTo("HealingDealtScale"));
        Assert.That(modifier.Magnitude.Scalar, Is.EqualTo(0.5f), "治疗输出 +50%");
    }

    #endregion

    #region 端到端：治疗（队伍共享账本）

    /// <summary>被动2：回合开始队伍恢复 12 + 100% 恢复量。恢复量 6 → 回 18。</summary>
    [Test]
    public void Passive_two_regenerates_twelve_plus_full_heal_power()
    {
        using var sim = BuildSimulation();
        sim.Buffs.Apply(sim, Player(0), "reno_passive_p2");
        sim.PlayerTeam.ApplySharedDamage(50f);
        var before = sim.PlayerTeam.SharedHpExact;

        sim.Buffs.FireTurnStart(sim);

        Assert.That(
            sim.PlayerTeam.SharedHpExact - before,
            Is.EqualTo(12f + HealPowerForTests).Within(0.001f),
            "12 + 100% 恢复量");
    }

    /// <summary>卡1 的队伍再生：回合开始恢复 6 + 100% 恢复量。恢复量 6 → 回 12。</summary>
    [Test]
    public void Dragon_hoard_regenerates_six_plus_full_heal_power()
    {
        using var sim = BuildPlayerPhase(HoardCard);
        var reno = sim.PlayerTeam.Characters[0];

        MarkAndExecute(sim, HoardCard, [Self()]);
        Assert.That(reno.Buffs.Find(HoardHealBuff), Is.Not.Null, "自身恢复量 buff 未挂上");
        Assert.That(reno.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(HealPowerForTests + 6f).Within(0.001f));

        // 再生只挂一个实例（挂在施法者身上）：若挂到全队，回合开始会按人数各触发一次队伍治疗。
        Assert.That(reno.Buffs.Find(RegenBuff), Is.Not.Null, "再生 buff 未挂上");
        foreach (var other in sim.PlayerTeam.Characters.Skip(1))
            Assert.That(other.Buffs.Find(RegenBuff), Is.Null, "再生不应逐槽重复挂载（否则队伍治疗会翻倍）");

        sim.PlayerTeam.ApplySharedDamage(50f);
        var before = sim.PlayerTeam.SharedHpExact;

        sim.Buffs.FireTurnStart(sim);

        Assert.That(
            sim.PlayerTeam.SharedHpExact - before,
            Is.EqualTo(6f + HealPowerForTests + 6f).Within(0.001f),
            "6 + 100% 恢复量（含卡1 的 +6）");
    }

    /// <summary>被动4：队伍治疗输出 +50%。治疗 12 + 6 = 18 → ×1.5 = 27。</summary>
    [Test]
    public void Passive_four_amplifies_healing_by_fifty_percent()
    {
        using var sim = BuildSimulation();
        sim.Buffs.Apply(sim, Player(0), "reno_passive_p4");
        var reno = sim.PlayerTeam.Characters[0];

        Assert.That(
            reno.Asc.GetCurrentValue(AttributeIds.HealingDealtScale),
            Is.EqualTo(0.5f).Within(0.001f),
            "治疗输出倍率进入属性聚合");

        sim.PlayerTeam.ApplySharedDamage(50f);
        var before = sim.PlayerTeam.SharedHpExact;

        sim.Buffs.Apply(sim, Player(0), "reno_passive_p2");
        sim.Buffs.FireTurnStart(sim);

        Assert.That(
            sim.PlayerTeam.SharedHpExact - before,
            Is.EqualTo((12f + HealPowerForTests) * 1.5f).Within(0.001f),
            "(12 + 100% 恢复量) × 1.5");
    }

    /// <summary>
    /// 被动4 的治疗加成<b>严格限定绿属性施疗者</b>：蓝属性角色拿到的是休眠实例，
    /// 不进属性聚合，因此它的治疗不吃 +50%。
    /// </summary>
    /// <remarks>
    /// 两段各自用独立模拟：被动2 是按"持有者各触发一次"结算的，
    /// 同一个模拟里给两个角色都挂被动2 会让回合开始的治疗翻倍，测不出单次倍率。
    /// </remarks>
    [Test]
    public void Passive_four_heal_amplification_only_applies_to_green_healers()
    {
        // 蓝属性施疗者：buff 休眠 → 不吃加成，治疗 12 + 100% 恢复量 = 18。
        using (var blueSim = BuildSimulationWithFirstElement(EElement.Blue))
        {
            blueSim.Buffs.Apply(blueSim, Player(0), "reno_passive_p4");
            var blue = blueSim.PlayerTeam.Characters[0];
            var greenAlly = blueSim.PlayerTeam.Characters[1];

            Assert.That(
                blue.Asc.GetCurrentValue(AttributeIds.HealingDealtScale),
                Is.Zero,
                "蓝属性施疗者不吃治疗加成（buff 休眠）");
            Assert.That(
                greenAlly.Asc.GetCurrentValue(AttributeIds.HealingDealtScale),
                Is.EqualTo(0.5f).Within(0.001f),
                "绿属性施疗者吃治疗加成");

            blueSim.PlayerTeam.ApplySharedDamage(50f);
            var before = blueSim.PlayerTeam.SharedHpExact;
            blueSim.Buffs.Apply(blueSim, Player(0), "reno_passive_p2");
            blueSim.Buffs.FireTurnStart(blueSim);

            Assert.That(
                blueSim.PlayerTeam.SharedHpExact - before,
                Is.EqualTo(12f + HealPowerForTests).Within(0.001f),
                "蓝属性施疗者：不放大");
        }

        // 绿属性施疗者：(12 + 100% 恢复量) × 1.5 = 27。
        using (var greenSim = BuildSimulationWithFirstElement(EElement.Green))
        {
            greenSim.Buffs.Apply(greenSim, Player(0), "reno_passive_p4");
            var green = greenSim.PlayerTeam.Characters[0];

            Assert.That(
                green.Asc.GetCurrentValue(AttributeIds.HealingDealtScale),
                Is.EqualTo(0.5f).Within(0.001f),
                "绿属性施疗者吃治疗加成");

            greenSim.PlayerTeam.ApplySharedDamage(50f);
            var before = greenSim.PlayerTeam.SharedHpExact;
            greenSim.Buffs.Apply(greenSim, Player(0), "reno_passive_p2");
            greenSim.Buffs.FireTurnStart(greenSim);

            Assert.That(
                greenSim.PlayerTeam.SharedHpExact - before,
                Is.EqualTo((12f + HealPowerForTests) * 1.5f).Within(0.001f),
                "绿属性施疗者：×1.5");
        }
    }

    #endregion

    #region 端到端：卡牌效果

    /// <summary>卡2：己方单体 2 回合物攻·魔攻 +12。</summary>
    [Test]
    public void Dragon_blessing_buffs_one_ally_for_two_turns()
    {
        using var sim = BuildPlayerPhase(BlessingCard);
        var target = sim.PlayerTeam.Characters[1];
        var physicalBefore = target.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var magicBefore = target.Asc.GetCurrentValue(AttributeIds.MagicAttack);

        MarkAndExecute(sim, BlessingCard, [Player(1)]);

        var buff = target.Buffs.Find("reno_blessing");
        Assert.That(buff, Is.Not.Null, "被点名的队友应获得祝福");
        Assert.That(buff!.RemainingTurns, Is.EqualTo(2));
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(physicalBefore + 12f).Within(0.001f));
        Assert.That(target.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(magicBefore + 12f).Within(0.001f));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("reno_blessing"), Is.Null, "没被点名的角色不发");
    }

    /// <summary>卡3：自身恢复量 +12（3 回合），己方全体物攻 +12（2 回合）。</summary>
    [Test]
    public void Dragon_breath_buffs_self_heal_power_and_all_allies_attack()
    {
        using var sim = BuildPlayerPhase(BreathCard);
        var reno = sim.PlayerTeam.Characters[0];

        MarkAndExecute(sim, BreathCard, [Self()]);

        Assert.That(reno.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(HealPowerForTests + 12f).Within(0.001f));
        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Buffs.Find(BreathAtkBuff), Is.Not.Null, "己方全体都该拿到物攻 buff");
        }
    }

    /// <summary>卡4：己方全体 1 回合物攻 +50，且物理攻击的卡牌与普攻攻击次数各 +1。</summary>
    [Test]
    public void Dragon_roar_grants_team_attack_and_extra_attacks()
    {
        using var sim = BuildPlayerPhase(RoarCard);
        var reno = sim.PlayerTeam.Characters[0];
        var physicalBefore = reno.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);

        MarkAndExecute(sim, RoarCard, [Self()]);

        foreach (var character in sim.PlayerTeam.Characters)
            Assert.That(character.Buffs.Find(RoarBuff), Is.Not.Null, "己方全体都该拿到咆哮");

        Assert.That(reno.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(physicalBefore + 50f).Within(0.001f));
        Assert.That(
            reno.Asc.GetCurrentValue(AttributeIds.PhysicalCardAttackCount),
            Is.EqualTo(1f).Within(0.001f),
            "物理攻击的卡牌攻击次数 +1");
        Assert.That(
            reno.Asc.GetCurrentValue(AttributeIds.NormalAttackCount),
            Is.EqualTo(1f).Within(0.001f),
            "普通攻击次数 +1");
    }

    #endregion

    #region 端到端：物理卡牌攻击次数加成（龙之咆哮）

    /// <summary>
    /// 物理卡牌（莱因哈特·辉耀宝刀）在咆哮下多打 1 段。
    /// </summary>
    /// <remarks>
    /// 用"同场景差分"而不是绝对总额：一次出牌的总掉血里还混着普攻与追打
    /// （实测无咆哮 63、有咆哮 151），写死总额会把别的机制一起锁死。
    /// 差分 = 多 1 段卡牌伤害 + 普攻轮数 1→2 + 物攻 +50 的增量，
    /// 因此只断言"显著大于单段卡牌伤害"，并用魔法卡做反向对照。
    /// </remarks>
    [Test]
    public void Roar_adds_one_hit_to_physical_cards()
    {
        const float attack = 40f;
        var perHit = 3f + 0.25f * attack; // Amount 3 + 25% 物攻

        var without = PlayCardDamage(attack, withRoar: false);
        var with = PlayCardDamage(attack, withRoar: true);

        Assert.That(
            with - without,
            Is.GreaterThanOrEqualTo(perHit + 1f),
            $"咆哮应让物理卡多打 1 段（无咆哮 {without} → 有咆哮 {with}）");
    }

    /// <summary>
    /// 魔法卡牌（图灵·机械位移）不受咆哮的"物理卡牌次数"加成影响：总掉血完全不变
    /// （实测 62 → 62）。
    /// </summary>
    [Test]
    public void Roar_does_not_add_hits_to_magical_cards()
    {
        const float attack = 40f;

        var without = PlayCardDamage(attack, withRoar: false, cardId: "turing_machine_shift");
        var with = PlayCardDamage(attack, withRoar: true, cardId: "turing_machine_shift");

        Assert.That(with, Is.EqualTo(without).Within(0.001f), "魔法卡不吃物理卡牌次数加成");
    }

    /// <summary>打完一张卡后敌人掉了多少血（同场景，便于差分）。</summary>
    private static float PlayCardDamage(
        float attack,
        bool withRoar,
        string cardId = "reinhardt_radiant_blade")
    {
        using var sim = BuildCardPlayground(CharacterId, cardId, attack);
        var target = enemy(sim);
        if (withRoar)
            sim.Buffs.Apply(sim, Player(1), RoarBuff);

        var before = target.Asc.GetCurrentValue(AttributeIds.Health);
        PlayCardForSlot(sim, 1, cardId);
        return before - target.Asc.GetCurrentValue(AttributeIds.Health);
    }

    #endregion

    #region 装配

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static string? StringParam(IReadOnlyDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static CombatTargetRef Self() => Player(0);

    private static EnemyUnit enemy(CombatSimulation simulation) => simulation.EnemyTeam.Enemies[0];

    /// <summary>
    /// 打真实卡牌的最小战场：槽位 0 是绿·龙族（用于挂咆哮），槽位 1 打出指定卡。
    /// 槽位 1 的攻防按 <paramref name="attack"/> 给，便于算定值伤害。
    /// </summary>
    private static CombatSimulation BuildCardPlayground(string renoId, string handCardId, float attack)
    {
        var reno = CharacterBattleInstance.CreateForTests(
            renoId,
            CharacterAttributes(),
            element: EElement.Green,
            race: ERace.Dragon);

        var cardOwner = CharacterBattleInstance.CreateForTests(
            "card_owner",
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = 50f,
                [AttributeIds.PhysicalAttack] = attack,
                [AttributeIds.MagicAttack] = attack,
                [AttributeIds.MaxEnergy] = 10f,
                [AttributeIds.InitialEnergy] = 10f,
            },
            drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}")],
            element: EElement.Yellow,
            race: ERace.Human);

        var characters = new List<CharacterBattleInstance> { reno, cardOwner };
        for (var index = 2; index < CombatConstants.SlotCount; index++)
            characters.Add(CreateReno(index));

        cardOwner.DrawCards(1);
        foreach (var character in characters)
            character.RefillAvailableEnergy();

        return NewSimulation([.. characters]);
    }

    /// <summary>由指定槽位标记一张手牌并进入结算阶段（与 <c>ReinhardtContentTests</c> 同约定）。</summary>
    private static void PlayCardForSlot(CombatSimulation sim, int characterIndex, string cardId)
    {
        var slotIndex = FindSlot(sim.PlayerTeam.Characters[characterIndex], cardId);
        var marked = sim.TryApply(new PlayCardCommand(
            characterIndex,
            slotIndex,
            [new CombatTargetRef(ECombatSide.Enemy, 0)]));
        Assert.That(marked.Success, Is.True, marked.Error);

        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static IReadOnlyDictionary<string, float> CharacterAttributes() =>
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.HealPower] = HealPowerForTests,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };

    /// <summary>只用于验证被动：4 名绿·龙族角色，无手牌。</summary>
    private static CombatSimulation BuildSimulation() =>
        NewSimulation([.. Enumerable.Range(0, CombatConstants.SlotCount).Select(index => CreateReno(index))]);

    /// <summary>槽位 0 是指定属性的角色，其余三个槽位是绿·龙族（用于验证属性门闩）。</summary>
    private static CombatSimulation BuildSimulationWithFirstElement(EElement element)
    {
        var characters = new List<CharacterBattleInstance>
        {
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                CharacterAttributes(),
                element: element,
                race: ERace.Dragon),
        };
        for (var index = 1; index < CombatConstants.SlotCount; index++)
            characters.Add(CreateReno(index));

        return NewSimulation([.. characters]);
    }

    /// <summary>4 名绿·龙族角色，槽位 0 手上抓一张指定卡。</summary>
    private static CombatSimulation BuildPlayerPhase(string handCardId)
    {
        var characters = new List<CharacterBattleInstance>();
        for (var index = 0; index < CombatConstants.SlotCount; index++)
        {
            characters.Add(CreateReno(
                index,
                index == 0 ? [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-0")] : null));
        }

        characters[0].DrawCards(1);
        foreach (var character in characters)
            character.RefillAvailableEnergy();

        return NewSimulation([.. characters]);
    }

    private static CharacterBattleInstance CreateReno(int index, IEnumerable<CardRuntimeEntry>? drawPile = null)
    {
        var character = CharacterBattleInstance.CreateForTests(
            CharacterId,
            CharacterAttributes(),
            drawPile: drawPile,
            element: EElement.Green,
            race: ERace.Dragon);
        return character;
    }

    private static CombatSimulation NewSimulation(CharacterBattleInstance[] characters) =>
        new(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 500)]),
            new CombatRuleEngine([]),
            BaseGameContent.BuildRegistry(),
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
