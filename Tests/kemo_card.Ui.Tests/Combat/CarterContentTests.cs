using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 卡特（<c>carter</c>）的出货内容：红 / Shield / 动物；单档主动技「狂热琴弦」（队伍生命上限 +200 +
/// 自身嘲讽）与四张专属卡（护盾 / 团队双防 / 团队魔防 / 护盾 + 受击回血）。
/// </summary>
/// <remarks>
/// 两条被动走团队光环（<c>applyScope: AllAllies</c> + <c>IdentityMatch</c>）：
/// P2 用 <c>onTurnStart</c> 投放抽卡修正，P4 用 <c>onDamaged</c> 授予护盾。
/// 护盾本体（1 点抵 1 点伤害、只抵扣敌方点名自己槽位的伤害）见 <see cref="ShieldMechanicTests"/>。
/// </remarks>
[TestFixture]
public sealed class CarterContentTests
{
    private const string CharacterId = "carter";
    private const string BloodLadder = "carter_blood_ladder";
    private const string AishiRescue = "carter_aishi_rescue";
    private const string RadioCrackle = "carter_radio_crackle";
    private const string HeavenGate = "carter_heaven_gate_knock";

    private const string P1 = "carter_passive_p1";
    private const string P2 = "carter_passive_p2";
    private const string P3 = "carter_passive_p3";
    private const string P4 = "carter_passive_p4";

    private const string ActiveSkill = "carter_frenzied_strings";
    private const string Domain = "carter_frenzied_domain";
    private const string TauntBuff = "carter_frenzied_taunt";

    private const string AishiGuard = "carter_aishi_guard";
    private const string RadioGuard = "carter_radio_guard";
    private const string MendingBuff = "carter_heaven_gate_mending";

    #region 元数据与接线

    [TestCase(BloodLadder, 2, 40, 20, "MagicDefense", 2)]
    [TestCase(AishiRescue, 3, 40, 20, "PhysicalDefense", 2)]
    [TestCase(RadioCrackle, 3, 40, 20, "MagicDefense", 2)]
    [TestCase(HeavenGate, 1, 40, 40, "", 0)]
    public void Exclusive_cards_match_the_design(
        string cardId,
        int cost,
        int priority,
        int maxHealth,
        string extraAttribute,
        int extraValue)
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");

        Assert.That(card!.Element, Is.EqualTo((int)EElement.Red), "四张卡都是红属性");
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.BaseValue, Is.Zero, "四张都是纯增益卡，无数值");
        Assert.That(card.CardType.ToString(), Is.EqualTo("Guard"));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.Role, Is.EqualTo(ERole.Shield), "卡牌定位跟随角色定位");
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.CardGroupId, Is.Null, "无升级链");
        Assert.That(card.UpgradeTier, Is.Zero);
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        if (extraAttribute.Length > 0)
            Assert.That(card.Stats?.Attributes.GetValueOrDefault(extraAttribute), Is.EqualTo(extraValue));
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    [TestCase(BloodLadder, ETargetSide.Ally, ETargetScope.Single, true)]
    [TestCase(AishiRescue, ETargetSide.Self, ETargetScope.Self, false)]
    [TestCase(RadioCrackle, ETargetSide.Self, ETargetScope.Self, false)]
    [TestCase(HeavenGate, ETargetSide.Self, ETargetScope.Self, false)]
    public void Exclusive_cards_declare_the_expected_targeting(
        string cardId,
        ETargetSide side,
        ETargetScope scope,
        bool needsPick)
    {
        var card = BaseGameContent.Load().Cards[cardId];

        Assert.That(card.TargetSide, Is.EqualTo(side));
        Assert.That(card.TargetScope, Is.EqualTo(scope));
        Assert.That(card.TargetCount, Is.EqualTo(1));
        Assert.That(
            CombatTargeting.RequiresExplicitTarget(card),
            Is.EqualTo(needsPick),
            "「己方 1 人」必须由玩家点选目标，其余三张自己就是受益者");
    }

    [Test]
    public void Cards_are_in_the_initial_deck_and_their_skill_chains_resolve()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var carter), Is.True);

        foreach (var cardId in new[] { BloodLadder, AishiRescue, RadioCrackle, HeavenGate })
        {
            Assert.That(carter!.Cards, Does.Contain(cardId), $"卡特的初始卡组缺少 {cardId}");

            Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True);
            foreach (var skillRef in card!.SkillRefs)
            {
                Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键（卡牌描述即由技能描述拼接）");
            }
        }
    }

    [Test]
    public void Character_metadata_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var carter), Is.True, "卡特未随内容出货");

        Assert.That(carter!.Element, Is.EqualTo(EElement.Red));
        Assert.That(carter.Role, Is.EqualTo(ERole.Shield));
        Assert.That(carter.Race, Is.EqualTo(ERace.Animal));
        Assert.That(carter.MaxEnergy, Is.EqualTo(8));
        Assert.That(carter.InitialEnergy, Is.EqualTo(3));
        Assert.That(
            carter.Cards,
            Is.EqualTo(new[] { BloodLadder, AishiRescue, RadioCrackle, HeavenGate }),
            "初始卡组即四张专属卡");
        Assert.That(
            carter.ActiveSkillChain.Select(tier => (tier.SkillId, tier.Cooldown)),
            Is.EqualTo(new[] { (ActiveSkill, 6) }),
            "单档主动技，CD 6");

        Assert.That(
            carter.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        foreach (var passive in carter.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    [Test]
    public void Passives_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        // P1 自身不受手牌槽伤害效果影响。
        Assert.That(definitions.Buffs[P1].Tags, Does.Contain(BuiltinBuffTags.TraitImmuneSlotDamage));

        // P2 红属性·动物角色抽卡 +1：AllAllies + 条件（「·」= 或）+ onTurnStart 投放抽卡修正。
        var p2 = definitions.Buffs[P2];
        Assert.That(p2.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies));
        AssertIdentity(p2, "carter_p2_draw");
        Assert.That(p2.Hooks.OnTurnStart, Has.Count.EqualTo(1), "P2 必须挂在 onTurnStart 上");
        Assert.That(p2.Hooks.OnTurnStart[0].EffectId, Is.EqualTo("carter_p2_draw"));

        var draw = definitions.Effects["carter_p2_draw"];
        Assert.That(draw.Kind, Is.EqualTo(EEffectKind.ModifyDrawCount), "抽卡光环走效果通道");
        Assert.That(draw.Params?["amount"]?.ToString(), Is.EqualTo("1"));

        // P3 自身嘲讽 +5（只挂自己）。
        var p3 = definitions.Buffs[P3];
        Assert.That(p3.ApplyScope, Is.EqualTo(EBuffApplyScope.Self));
        Assert.That(Modifier(p3, "Taunt").Magnitude.Scalar, Is.EqualTo(5f));

        // P4 红属性·动物角色在受到伤害后获得 1 护盾：AllAllies + 同一套身份条件 + onDamaged。
        var p4 = definitions.Buffs[P4];
        Assert.That(p4.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies));
        AssertIdentity(p4, "carter_p4_shield");
        Assert.That(p4.Hooks.OnDamaged, Has.Count.EqualTo(1), "P4 必须挂在 onDamaged 上");
        Assert.That(p4.Hooks.OnDamaged[0].EffectId, Is.EqualTo("carter_p4_shield"));

        var shield = definitions.Effects["carter_p4_shield"];
        Assert.That(shield.Kind, Is.EqualTo(EEffectKind.GainShield), "护盾授予走效果通道");
        Assert.That(shield.Params?["amount"]?.ToString(), Is.EqualTo("1"));
    }

    [Test]
    public void Active_skill_opens_the_domain_and_grants_self_taunt()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Skills[ActiveSkill].Tags, Does.Contain("active"));
        Assert.That(
            ActionIds(definitions, ActiveSkill),
            Is.EqualTo(new[] { "carter_open_frenzied_domain", "carter_frenzied_taunt" }));

        // 队伍生命上限 +200 走队伍领域（2 回合），不是角色属性。
        var domain = Action(definitions, "carter_open_frenzied_domain");
        Assert.That(domain.Kind, Is.EqualTo(ESkillActionKind.SetDomain));
        Assert.That(Param(domain, "gameplayEffectId"), Is.EqualTo(Domain));
        Assert.That(Param(domain, "turns"), Is.EqualTo("2"));

        var domainModifier = definitions.GameplayEffects[Domain].Modifiers
            .Single(modifier => modifier.AttributeId == AttributeIds.MaxHealth);
        Assert.That(domainModifier.Magnitude.Scalar, Is.EqualTo(200f));

        // 自身 2 回合【嘲讽 +5】。
        var taunt = definitions.Buffs[TauntBuff];
        Assert.That(taunt.Duration, Is.EqualTo(2));
        Assert.That(Modifier(taunt, "Taunt").Magnitude.Scalar, Is.EqualTo(5f));
    }

    [Test]
    public void Card_effects_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        // 热血阶梯：己方 1 人获得 100 点护盾，抽卡 +3（两个动作都落在卡牌点选出的目标上）。
        Assert.That(Action(definitions, "carter_blood_ladder_shield").Kind, Is.EqualTo(ESkillActionKind.GainShield));
        Assert.That(Param(Action(definitions, "carter_blood_ladder_shield"), "amount"), Is.EqualTo("100"));
        Assert.That(Param(Action(definitions, "carter_blood_ladder_shield"), "hookTargets"), Is.Null.Or.Empty,
            "护盾落在卡牌点选出的友方身上，不再重解析目标");
        Assert.That(Param(Action(definitions, "carter_blood_ladder_draw"), "amount"), Is.EqualTo("3"));

        // 爱世的救因：己方全体 2 回合【物防 +25】，抽卡 +1。
        Assert.That(Param(Action(definitions, "carter_aishi_guard"), "hookTargets"), Is.EqualTo("allies"));
        Assert.That(Param(Action(definitions, "carter_aishi_draw"), "hookTargets"), Is.EqualTo("allies"));
        Assert.That(Param(Action(definitions, "carter_aishi_draw"), "amount"), Is.EqualTo("1"));
        var aishi = definitions.Buffs[AishiGuard];
        Assert.That(aishi.Duration, Is.EqualTo(2));
        Assert.That(Modifier(aishi, "PhysicalDefense").Magnitude.Scalar, Is.EqualTo(25f));

        // 电台作响：己方全体 1 回合【魔防 +25 并叠加队伍生命上限 5%】，抽卡 +2。
        Assert.That(Param(Action(definitions, "carter_radio_guard"), "hookTargets"), Is.EqualTo("allies"));
        Assert.That(Param(Action(definitions, "carter_radio_draw"), "hookTargets"), Is.EqualTo("allies"));
        Assert.That(Param(Action(definitions, "carter_radio_draw"), "amount"), Is.EqualTo("2"));
        var radio = definitions.Buffs[RadioGuard];
        Assert.That(radio.Duration, Is.EqualTo(1));
        var radioMagnitude = Modifier(radio, "MagicDefense").Magnitude;
        Assert.That(radioMagnitude.Kind, Is.EqualTo(EMagnitudeKind.TeamMaxHealthScaled));
        Assert.That(radioMagnitude.Flat, Is.EqualTo(25f));
        Assert.That(radioMagnitude.Ratio, Is.EqualTo(0.05f).Within(0.0001f));

        // 叩响天堂之门：自身获得 50 点护盾；1 回合【受到攻击后恢复 30 点生命】。
        Assert.That(Param(Action(definitions, "carter_heaven_gate_shield"), "amount"), Is.EqualTo("50"));
        var mending = definitions.Buffs[MendingBuff];
        Assert.That(mending.Duration, Is.EqualTo(1));
        Assert.That(mending.Hooks.OnDamaged, Has.Count.EqualTo(1));
        Assert.That(mending.Hooks.OnDamaged[0].EffectId, Is.EqualTo("carter_heaven_gate_heal"));
        Assert.That(
            mending.Hooks.OnDamaged[0].Params?["hookTargets"]?.ToString(),
            Is.EqualTo("team"),
            "玩家侧治疗只认队伍账本（规格 §1.3）");
        var heal = definitions.Effects["carter_heaven_gate_heal"];
        Assert.That(heal.Kind, Is.EqualTo(EEffectKind.Heal));
        Assert.That(heal.Params?["amount"]?.ToString(), Is.EqualTo("30"));
        Assert.That(heal.Params?["healPowerScale"]?.ToString(), Is.EqualTo("0"), "30 点不吃回复量加成");
    }

    #endregion

    #region 端到端

    [Test]
    public void Active_skill_raises_team_max_hp_and_taunts_self()
    {
        using var sim = BuildSim();
        var carter = sim.PlayerTeam.Characters[0];
        var baseMaxHp = sim.PlayerTeam.MaxHp;
        carter.GainSkillCounter(6);

        var cast = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(cast.Success, Is.True, cast.Error);

        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo(Domain));
        Assert.That(sim.PlayerTeam.MaxHp, Is.EqualTo(baseMaxHp + 200), "队伍生命上限 +200（2 回合领域）");
        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(5f));
        Assert.That(carter.Buffs.Find(TauntBuff)?.RemainingTurns, Is.EqualTo(2));
    }

    [Test]
    public void Passive_two_grants_an_extra_draw_to_red_or_animal_characters()
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = new[]
        {
            // 0 号槽：红·动物（两项全中）。
            CharacterBattleInstance.CreateForTests(
                CharacterId, DefaultAttributes(), element: EElement.Red, race: ERace.Animal),
            // 1 号槽：蓝·动物（命中"动物"）。
            CharacterBattleInstance.CreateForTests(
                "c1", DefaultAttributes(), element: EElement.Blue, race: ERace.Animal),
            // 2 号槽：红·人类（命中"红"）。
            CharacterBattleInstance.CreateForTests(
                "c2", DefaultAttributes(), element: EElement.Red, race: ERace.Human),
            // 3 号槽：蓝·人类（两项都不命中）。
            CharacterBattleInstance.CreateForTests(
                "c3", DefaultAttributes(), element: EElement.Blue, race: ERace.Human),
        };

        using var sim = BuildSim(registry, characters);
        sim.Buffs.Apply(sim, Player(0), P2);
        foreach (var character in characters)
            character.ClearDrawModifiers();

        sim.Buffs.FireTurnStart(sim);

        Assert.That(characters[0].ComputeDrawCount(), Is.EqualTo(2), "红·动物命中");
        Assert.That(characters[1].ComputeDrawCount(), Is.EqualTo(2), "「·」= 或：蓝·动物命中动物");
        Assert.That(characters[2].ComputeDrawCount(), Is.EqualTo(2), "「·」= 或：红·人类命中红");
        Assert.That(characters[3].ComputeDrawCount(), Is.EqualTo(1), "蓝·人类两项都不命中");
    }

    [Test]
    public void Passive_four_grants_a_shield_to_red_or_animal_characters_after_enemy_hits()
    {
        using var sim = BuildSim();
        sim.Buffs.Apply(sim, Player(0), P4);

        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        // 史莱姆单体攻击落在 0 号槽：本批次每受击 1 次 +1 护盾。
        // 护盾在伤害结算之后发放，因此不抵扣这一击——正是「受到伤害后获得」的字面语义。
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(1f));
        Assert.That(sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero, "没被点名的队友不发盾");
    }

    [Test]
    public void Blood_ladder_grants_shield_and_draw_to_the_chosen_ally_only()
    {
        using var sim = BuildSim();
        ExecuteAction(sim, "carter_blood_ladder_shield", Player(0), [Player(1)]);
        ExecuteAction(sim, "carter_blood_ladder_draw", Player(0), [Player(1)]);

        Assert.That(sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(100f));
        Assert.That(sim.PlayerTeam.Characters[1].ComputeDrawCount(), Is.EqualTo(4), "基础 1 + 抽卡修正 3");
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero, "没被选中的角色不发盾");
        Assert.That(sim.PlayerTeam.Characters[0].ComputeDrawCount(), Is.EqualTo(1));
    }

    [Test]
    public void Shield_absorbs_enemy_damage_one_for_one_before_the_shared_ledger()
    {
        // 护盾吃满整击时不会产生 DamageDealt 事件（最终伤害为 0），因此原始伤害要从无护盾的同种子对照跑里取。
        var rawDamage = RawSlimeDamage();
        Assert.That(rawDamage, Is.GreaterThan(0), "史莱姆这一击必须造成伤害");

        using var sim = BuildSim();
        var carter = sim.PlayerTeam.Characters[0];
        ExecuteAction(sim, "carter_blood_ladder_shield", Player(0), [Player(0)]);
        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(100f));

        var hpBefore = sim.PlayerTeam.SharedHp;
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(100f - rawDamage), "1 点护盾抵 1 点伤害");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hpBefore), "100 点护盾足够吃下这一击，账本不掉血");
    }

    [Test]
    public void Aishi_rescue_grants_defense_and_draw_to_every_ally()
    {
        using var sim = BuildSim();
        ExecuteAction(sim, "carter_aishi_guard", Player(0), [Player(0)]);
        ExecuteAction(sim, "carter_aishi_draw", Player(0), [Player(0)]);

        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(25f));
            Assert.That(character.ComputeDrawCount(), Is.EqualTo(2), "基础 1 + 抽卡修正 1");
        }
    }

    [Test]
    public void Radio_crackle_scales_magic_defense_with_team_max_health()
    {
        using var sim = BuildSim();
        ExecuteAction(sim, "carter_radio_guard", Player(0), [Player(0)]);
        ExecuteAction(sim, "carter_radio_draw", Player(0), [Player(0)]);

        // 队伍生命上限 100（2 × 50）→ 魔防 +25 + floor(100 × 5%) = 30。
        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.EqualTo(30f));
            Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "基础 1 + 抽卡修正 2");
        }
    }

    [Test]
    public void Heaven_gate_knock_grants_shield_and_heals_after_being_attacked()
    {
        var rawDamage = RawSlimeDamage();
        Assert.That(rawDamage, Is.LessThan(50), "本用例假定 50 点护盾能吃满这一击");

        using var sim = BuildSim();
        var carter = sim.PlayerTeam.Characters[0];
        ExecuteAction(sim, "carter_heaven_gate_shield", Player(0), [Player(0)]);
        ExecuteAction(sim, "carter_heaven_gate_mending", Player(0), [Player(0)]);

        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(50f));
        Assert.That(carter.Buffs.Find(MendingBuff)?.RemainingTurns, Is.EqualTo(1));

        // 账本先压低，避免治疗被上限截断而看不出差额。
        sim.PlayerTeam.ApplySharedDamage(50f);
        var hpBefore = sim.PlayerTeam.SharedHp;

        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(50f - rawDamage), "护盾 1:1 抵扣");
        Assert.That(
            sim.PlayerTeam.SharedHp,
            Is.EqualTo(hpBefore + 30),
            "护盾吃满整击（账本不掉血），「受到攻击后」仍要回 30 点——受击记账与最终伤害解耦");
    }

    [Test]
    public void Taunt_from_carter_pulls_enemy_single_target_attacks()
    {
        using var sim = BuildSim();
        var carter = sim.PlayerTeam.Characters[1];
        sim.Buffs.Apply(sim, Player(1), P3);
        Assert.That(carter.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(5f));

        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        var targets = sim.Presentation.Drain()
            .OfType<DamageDealtEvent>()
            .Where(evt => evt.Source.Side == ECombatSide.Enemy && evt.Target.Side == ECombatSide.Player)
            .Select(evt => evt.Target)
            .ToArray();

        Assert.That(targets, Is.EqualTo(new[] { Player(1) }), "嘲讽 5 把敌方单体攻击吸到 1 号槽");
    }

    #endregion

    #region 装配

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static Dictionary<string, float> DefaultAttributes() => new(StringComparer.Ordinal)
    {
        [AttributeIds.MaxHealth] = 50f,
        [AttributeIds.PhysicalAttack] = 10f,
        [AttributeIds.MagicAttack] = 10f,
        [AttributeIds.MaxEnergy] = 10f,
        [AttributeIds.InitialEnergy] = 10f,
    };

    /// <summary>两个红·动物角色 + 一只史莱姆；4 名角色是内容约定，但两个槽足够验证卡面效果。</summary>
    private static CombatSimulation BuildSim(
        GameDefinitionRegistry? registry = null,
        CharacterBattleInstance[]? characters = null)
    {
        registry ??= BaseGameContent.BuildRegistry();
        characters ??=
        [
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                activeSkillChain: [new ActiveSkillChainEntryDto { SkillId = ActiveSkill, Cooldown = 6 }],
                element: EElement.Red,
                race: ERace.Animal),
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                activeSkillChain: [new ActiveSkillChainEntryDto { SkillId = ActiveSkill, Cooldown = 6 }],
                element: EElement.Red,
                race: ERace.Animal),
        ];

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 500)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260926);
    }

    /// <summary>
    /// 同种子对照跑：无护盾时史莱姆单体一击的原始伤害。
    /// 护盾吃满整击时最终伤害为 0、不发 <c>DamageDealt</c> 事件，只能这样取基准值。
    /// </summary>
    private static int RawSlimeDamage()
    {
        using var baseline = BuildSim();
        var before = baseline.PlayerTeam.SharedHp;
        baseline.TransitionTo(ECombatPhase.Enemy);
        baseline.AdvancePhase();
        return before - baseline.PlayerTeam.SharedHp;
    }

    private static void ExecuteAction(
        CombatSimulation simulation,
        string actionId,
        CombatTargetRef source,
        CombatTargetRef[] targets) =>
        simulation.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = actionId },
            simulation,
            source,
            targets);

    /// <summary>两条团队光环（P2 / P4）共用同一套身份条件：「红 或 动物」。</summary>
    private static void AssertIdentity(BuffDto buff, string expectedEffectId)
    {
        var condition = CombatTestHelper.IdentityParams(buff);
        Assert.That(condition, Is.Not.Null, $"{buff.Id} 持有者条件走 IdentityMatch");
        Assert.That(CombatTestHelper.EnumNames(condition, "elementAny"), Is.EquivalentTo(new[] { "Red" }));
        Assert.That(CombatTestHelper.EnumNames(condition, "raceAny"), Is.EquivalentTo(new[] { "Animal" }));
        Assert.That(CombatTestHelper.EnumNames(condition, "raceAll"), Is.Empty);
        Assert.That(CombatTestHelper.BoolParam(condition, "matchAll"), Is.False, "跨维度默认取或");
        Assert.That(buff.Hooks.OnTurnStart.Concat(buff.Hooks.OnDamaged).Select(reference => reference.EffectId),
            Does.Contain(expectedEffectId));
    }

    private static SkillActionDto Action(ModDefinitionsBundle definitions, string actionId)
    {
        Assert.That(definitions.SkillActions.TryGetValue(actionId, out var action), Is.True, $"动作 {actionId} 不存在");
        return action!;
    }

    private static string[] ActionIds(ModDefinitionsBundle definitions, string skillId)
    {
        Assert.That(definitions.Skills.TryGetValue(skillId, out var skill), Is.True, $"技能 {skillId} 不存在");
        return [.. skill!.ActionRefs.Select(reference => reference.ActionId)];
    }

    private static string? Param(SkillActionDto action, string key) =>
        action.Params is not null && action.Params.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;

    private static AttributeModifierDefDto Modifier(BuffDto buff, string attributeId)
    {
        var modifier = buff.Modifiers.FirstOrDefault(candidate =>
            string.Equals(candidate.AttributeId, attributeId, StringComparison.Ordinal));
        Assert.That(modifier, Is.Not.Null, $"buff {buff.Id} 缺少 {attributeId} 修正");
        return modifier!;
    }

    #endregion
}