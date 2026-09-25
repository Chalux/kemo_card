using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 参宿四（<c>betelgeuse</c>）的出货内容：红 / Guard / 天文·未知；三档主动技（抽卡 + 团队防御）、
/// 四条潜能被动（免疫封印 / 红·天文·未知防御 / 嘲讽 / 受击回血）与四张专属卡的数值、接线与端到端效果。
/// </summary>
[TestFixture]
public sealed class BetelgeuseContentTests
{
    private const string CharacterId = "betelgeuse";
    private const string CrimsonMatrix = "betelgeuse_crimson_matrix";
    private const string Occultation = "betelgeuse_occulation";
    private const string Devour = "betelgeuse_devour_the_heavens";
    private const string KeyClosure = "betelgeuse_key_closure";
    private const string P1 = "betelgeuse_passive_p1";
    private const string P2 = "betelgeuse_passive_p2";
    private const string P3 = "betelgeuse_passive_p3";
    private const string P4 = "betelgeuse_passive_p4";

    #region 元数据与接线

    [TestCase(CrimsonMatrix, 2, 30, 30, "PhysicalDefense", 1)]
    [TestCase(Occultation, 3, 30, 30, "PhysicalDefense", 1)]
    [TestCase(Devour, 3, 30, 40, "", 0)]
    [TestCase(KeyClosure, 1, 21, 40, "", 0)]
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
        Assert.That(card.Role, Is.EqualTo(ERole.Guard), "卡牌定位跟随角色定位");
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        if (extraAttribute.Length > 0)
            Assert.That(card.Stats?.Attributes.GetValueOrDefault(extraAttribute), Is.EqualTo(extraValue));
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    [Test]
    public void Character_metadata_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var character), Is.True, "参宿四未随内容出货");

        Assert.That(character!.Element, Is.EqualTo(EElement.Red));
        Assert.That(character.Role, Is.EqualTo(ERole.Guard));
        Assert.That(character.Race, Is.EqualTo(ERace.Astronomy | ERace.Unknown));
        Assert.That(character.MaxEnergy, Is.EqualTo(8));
        Assert.That(character.InitialEnergy, Is.EqualTo(3));
        Assert.That(
            character.Cards,
            Is.EqualTo(new[] { CrimsonMatrix, Occultation, Devour, KeyClosure }),
            "初始卡组即四张专属卡");

        // 三档主动技：猎户座α(6) → 充能II(4) → 充能III(6)，Cap = 16。
        Assert.That(
            character.ActiveSkillChain.Select(tier => (tier.SkillId, tier.Cooldown)),
            Is.EqualTo(new[]
            {
                ("betelgeuse_orion_alpha", 6),
                ("betelgeuse_charge_two", 4),
                ("betelgeuse_charge_three", 6),
            }));

        Assert.That(
            character.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        foreach (var passive in character.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    [Test]
    public void Active_skill_tiers_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        // 档1：己方全体 1 回合【抽卡 +3】。
        Assert.That(definitions.Skills["betelgeuse_orion_alpha"].Tags, Does.Contain("active"));
        var orionDraw = Action(definitions, "betelgeuse_orion_draw");
        Assert.That(orionDraw.Kind, Is.EqualTo(ESkillActionKind.ModifyDrawCount));
        Assert.That(Param(orionDraw, "amount"), Is.EqualTo("3"));
        Assert.That(Param(orionDraw, "hookTargets"), Is.EqualTo("allies"), "抽卡加成覆盖己方全体");

        // 档2：己方全体 1 回合【抽卡 +3，物防·魔防 +15】。
        Assert.That(
            ActionIds(definitions, "betelgeuse_charge_two"),
            Is.EqualTo(new[] { "betelgeuse_charge_two_draw", "betelgeuse_charge_two_guard" }));
        Assert.That(Param(Action(definitions, "betelgeuse_charge_two_draw"), "amount"), Is.EqualTo("3"));
        var guard15 = definitions.Buffs["betelgeuse_orion_guard_15"];
        Assert.That(guard15.Duration, Is.EqualTo(1));
        Assert.That(Modifier(guard15, "PhysicalDefense").Magnitude.Scalar, Is.EqualTo(15f));
        Assert.That(Modifier(guard15, "MagicDefense").Magnitude.Scalar, Is.EqualTo(15f));

        // 档3：自身技能进度 +6；己方全体 1 回合【抽卡 +4，物防·魔防 +40】。
        var self = Action(definitions, "betelgeuse_charge_three_self");
        Assert.That(self.Kind, Is.EqualTo(ESkillActionKind.GainResource));
        Assert.That(Param(self, "resource"), Is.EqualTo("skillcounter"));
        Assert.That(Param(self, "amount"), Is.EqualTo("6"));
        Assert.That(Param(Action(definitions, "betelgeuse_charge_three_draw"), "amount"), Is.EqualTo("4"));
        var guard40 = definitions.Buffs["betelgeuse_orion_guard_40"];
        Assert.That(guard40.Duration, Is.EqualTo(1));
        Assert.That(Modifier(guard40, "PhysicalDefense").Magnitude.Scalar, Is.EqualTo(40f));
        Assert.That(Modifier(guard40, "MagicDefense").Magnitude.Scalar, Is.EqualTo(40f));
    }

    [Test]
    public void Passives_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        // P1 免疫封印。
        Assert.That(definitions.Buffs[P1].Tags, Does.Contain(BuiltinBuffTags.TraitImmuneSeal));

        // P2 红属性·天文·未知角色的物防·魔防 +15：AllAllies + 条件。
        // 项目约定：效果描述里的「·」= 或（列表内与跨维度都取或），只有显式写「且」才取且；
        // 因此这里是「红 或 天文 或 未知」（无 matchAll）。
        var p2 = definitions.Buffs[P2];
        Assert.That(p2.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies));
        Assert.That(p2.Condition?.ElementAny, Is.EquivalentTo(new[] { EElement.Red }));
        Assert.That(p2.Condition?.RaceAny, Is.EquivalentTo(new[] { ERace.Astronomy, ERace.Unknown }));
        Assert.That(p2.Condition?.RaceAll, Is.Null.Or.Empty);
        Assert.That(p2.Condition?.MatchAll, Is.False, "跨维度默认取或");
        Assert.That(Modifier(p2, "PhysicalDefense").Magnitude.Scalar, Is.EqualTo(15f));
        Assert.That(Modifier(p2, "MagicDefense").Magnitude.Scalar, Is.EqualTo(15f));

        // P3 自身嘲讽 +5（只挂自己）。
        var p3 = definitions.Buffs[P3];
        Assert.That(p3.ApplyScope, Is.EqualTo(EBuffApplyScope.Self));
        Assert.That(Modifier(p3, "Taunt").Magnitude.Scalar, Is.EqualTo(5f));

        // P4 受击回血：onDamaged → 12 点、不吃回复量（healPowerScale 0）、治疗落队伍账本。
        var p4 = definitions.Buffs[P4];
        Assert.That(p4.Hooks.OnDamaged, Has.Count.EqualTo(1));
        Assert.That(p4.Hooks.OnDamaged[0].EffectId, Is.EqualTo("betelgeuse_p4_mending"));
        Assert.That(p4.Hooks.OnDamaged[0].Params?["hookTargets"]?.ToString(), Is.EqualTo("team"));
        var mending = definitions.Effects["betelgeuse_p4_mending"];
        Assert.That(mending.Kind, Is.EqualTo(EEffectKind.Heal));
        Assert.That(mending.Params?["amount"]?.ToString(), Is.EqualTo("12"));
        Assert.That(mending.Params?["healPowerScale"]?.ToString(), Is.EqualTo("0"));
    }

    [Test]
    public void Card_effects_cover_all_allies_and_key_closure_scales_with_team_max_health()
    {
        var definitions = BaseGameContent.Load();

        foreach (var actionId in new[]
                 {
                     "betelgeuse_crimson_matrix_guard",
                     "betelgeuse_occulation_guard",
                     "betelgeuse_key_closure_guard",
                 })
        {
            var action = Action(definitions, actionId);
            Assert.That(action.Kind, Is.EqualTo(ESkillActionKind.ApplyBuff));
            Assert.That(Param(action, "hookTargets"), Is.EqualTo("allies"), $"{actionId} 应覆盖己方全体");
        }

        // 真见·吞食天地：只给自己（嘲讽自己扛）。
        var devour = Action(definitions, "betelgeuse_devour_guard");
        Assert.That(Param(devour, "hookTargets"), Is.Null.Or.Empty);
        var devourBuff = definitions.Buffs["betelgeuse_devour_guard"];
        Assert.That(devourBuff.Duration, Is.EqualTo(3));
        Assert.That(Modifier(devourBuff, "Taunt").Magnitude.Scalar, Is.EqualTo(3f));
        Assert.That(Modifier(devourBuff, "PhysicalDefense").Magnitude.Scalar, Is.EqualTo(30f));
        Assert.That(Modifier(devourBuff, "MagicDefense").Magnitude.Scalar, Is.EqualTo(30f));

        // 操控·键闭：物防 = 1 + floor(队伍生命上限 × 3%)，创建时取快照。
        var magnitude = Modifier(definitions.Buffs["betelgeuse_key_closure_guard"], "PhysicalDefense").Magnitude;
        Assert.That(magnitude.Kind, Is.EqualTo(EMagnitudeKind.TeamMaxHealthScaled));
        Assert.That(magnitude.Flat, Is.EqualTo(1f));
        Assert.That(magnitude.Ratio, Is.EqualTo(0.03f).Within(0.0001f));
    }

    /// <summary>
    /// 敌方攻击必须点名一个角色（否则只会结算到队伍账本，嘲讽与受击钩子都无从判定）；
    /// 敌方视角的 <c>Enemy</c> = 玩家侧。
    /// </summary>
    [Test]
    public void Enemy_attack_skills_target_a_chosen_character()
    {
        var definitions = BaseGameContent.Load();
        var tackle = definitions.Skills["slime_tackle"];

        Assert.That(tackle.TargetOverride, Is.Not.Null, "敌方攻击技能必须声明 targetOverride");
        Assert.That(tackle.TargetOverride!.Side, Is.EqualTo(ETargetSide.Enemy));
        Assert.That(tackle.TargetOverride.Scope, Is.EqualTo(ETargetScope.Single));
    }

    #endregion

    #region 端到端

    [Test]
    public void Taunt_redirects_enemy_single_target_attacks()
    {
        var noTaunt = RunSlimeAttack(tauntSlot: null, out var noTauntTargets);
        var withTaunt = RunSlimeAttack(tauntSlot: 1, out var withTauntTargets);

        Assert.That(noTauntTargets, Is.EqualTo(new[] { new CombatTargetRef(ECombatSide.Player, 0) }),
            "没有嘲讽时按槽位序选中 0 号槽");
        Assert.That(withTauntTargets, Is.EqualTo(new[] { new CombatTargetRef(ECombatSide.Player, 1) }),
            "嘲讽 5 把敌方单体攻击吸到 1 号槽");
        Assert.That(withTaunt - noTaunt, Is.EqualTo(12),
            "两次受击伤害相同，差额正是 1 号槽受击回血的 12 点（P4 只挂在 1 号槽）");
    }

    [Test]
    public void On_damaged_heals_once_per_hit_and_ignores_heal_power()
    {
        using var sim = BuildSim();
        var character = sim.PlayerTeam.Characters[1];
        // 回复量拉满：healPowerScale = 0 时回血仍应恰好 12（不吃回复量）。
        character.Asc.Attributes.SetBaseValue(AttributeIds.HealPower, 100f);
        character.Asc.Aggregator.Recalculate(AttributeIds.HealPower);
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), P3);
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), P4);
        sim.PlayerTeam.ApplySharedDamage(50f); // 账本先压到 50，避免治疗被上限截断而看不出差异

        var before = sim.PlayerTeam.SharedHp;
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        var damage = (int)sim.Presentation.Drain()
            .OfType<DamageDealtEvent>()
            .Where(evt => evt.Target.Side == ECombatSide.Player)
            .Sum(evt => evt.Amount);
        Assert.That(damage, Is.GreaterThan(0), "木桩/史莱姆这一击必须造成伤害");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(before - damage + 12), "每次受击恰好回复 12 点");
    }

    [Test]
    public void All_allies_target_selector_applies_draw_to_every_character()
    {
        using var sim = BuildSim();

        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "betelgeuse_orion_draw" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 0),
            [new CombatTargetRef(ECombatSide.Player, 0)]);

        foreach (var character in sim.PlayerTeam.Characters)
            Assert.That(character.ComputeDrawCount(), Is.EqualTo(4), "基础 1 + 抽卡修正 3，全队都吃");
    }

    [Test]
    public void P2_condition_matches_red_astronomy_or_unknown_characters()
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = new[]
        {
            // 0 号槽：红·天文·未知（三项全中）。
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                element: EElement.Red,
                race: ERace.Astronomy | ERace.Unknown),
            // 1 号槽：蓝·天文（命中"天文"）。
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                element: EElement.Blue,
                race: ERace.Astronomy),
            // 2 号槽：蓝·未知（命中"未知"）。
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                element: EElement.Blue,
                race: ERace.Unknown),
            // 3 号槽：蓝·人类（三项都不命中）。
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                element: EElement.Blue,
                race: ERace.Human),
        };

        using var sim = BuildSim(registry, characters);
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), P2);

        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.EqualTo(15f),
            "红·天文·未知命中");
        Assert.That(
            sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.EqualTo(15f),
            "「·」= 或：蓝·天文命中天文");
        Assert.That(
            sim.PlayerTeam.Characters[2].Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.EqualTo(15f),
            "「·」= 或：蓝·未知命中未知");
        Assert.That(
            sim.PlayerTeam.Characters[3].Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.Zero,
            "蓝·人类三项都不命中");
    }

    [Test]
    public void Team_max_health_scaled_snapshots_the_value_at_application()
    {
        using var sim = BuildSim();
        var character = sim.PlayerTeam.Characters[0];
        var baseDefense = character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense);

        // 队伍生命上限 100 → 物防 +1 + floor(100 × 3%) = +4。
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), "betelgeuse_key_closure_guard");
        Assert.That(
            character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.EqualTo(baseDefense + 4f));

        // 队伍生命上限涨到 200：快照语义下加成不随变（仍是 +4）。
        sim.PlayerTeam.Asc.Attributes.SetBaseValue(AttributeIds.MaxHealth, 200f);
        sim.PlayerTeam.Asc.Aggregator.Recalculate(AttributeIds.MaxHealth);
        character.Buffs.EvaluateDormancy();
        character.Asc.Aggregator.RecalculateAll();

        Assert.That(
            character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense),
            Is.EqualTo(baseDefense + 4f),
            "卡牌结算时取数值，之后不随队伍生命上限变化");
    }

    [Test]
    public void Card_taunt_buff_sets_taunt_attribute()
    {
        using var sim = BuildSim();
        var character = sim.PlayerTeam.Characters[0];

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), "betelgeuse_devour_guard");

        Assert.That(character.Asc.GetCurrentValue(AttributeIds.Taunt), Is.EqualTo(3f));
        Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(30f));
    }

    #endregion

    #region 装配

    private static readonly CombatTargetRef EnemySource = new(ECombatSide.Enemy, 0);

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
                element: EElement.Red,
                race: ERace.Astronomy | ERace.Unknown),
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                DefaultAttributes(),
                element: EElement.Red,
                race: ERace.Astronomy | ERace.Unknown),
        ];

        var enemies = new[]
        {
            new EnemyUnit("e0", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = 300f,
            }),
        };

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260925);
    }

    /// <summary>跑一次敌方阶段（史莱姆只有一招物理攻击），返回结算后的队伍生命与本次攻击的目标槽位。</summary>
    private static int RunSlimeAttack(int? tauntSlot, out CombatTargetRef[] attackTargets)
    {
        using var sim = BuildSim();
        if (tauntSlot is { } slot)
            sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, slot), P3);

        // 受击回血只挂在 1 号槽：只有攻击命中它才会回血。
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), P4);
        // 账本先压到 50：治疗满血时会被上限截断，压低后才能看出 12 点差额。
        sim.PlayerTeam.ApplySharedDamage(50f);
        sim.Presentation.Clear();

        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();

        attackTargets =
        [
            .. sim.Presentation.Drain()
                .OfType<DamageDealtEvent>()
                .Where(evt => evt.Source.Side == ECombatSide.Enemy && evt.Target.Side == ECombatSide.Player)
                .Select(evt => evt.Target),
        ];
        return sim.PlayerTeam.SharedHp;
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

    private static Dictionary<string, float> DefaultAttributes() => new(StringComparer.Ordinal)
    {
        [AttributeIds.MaxHealth] = 50f,
        [AttributeIds.PhysicalAttack] = 10f,
        [AttributeIds.MagicAttack] = 10f,
        [AttributeIds.MaxEnergy] = 10f,
        [AttributeIds.InitialEnergy] = 10f,
    };

    #endregion
}