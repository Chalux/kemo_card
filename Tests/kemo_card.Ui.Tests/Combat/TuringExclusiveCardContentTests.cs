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

/// <summary>
/// 图灵的四张专属卡（出货内容）：元数据、接线完整性与端到端效果
/// （单体物攻削弱、受伤增加、魔法伤害 + 降魔防、自身以外的技能进度推进与"不叠加"）。
/// </summary>
[TestFixture]
public sealed class TuringExclusiveCardContentTests
{
    private const string OriginCard = "turing_mechanical_origin";
    private const string LimitCard = "turing_limit_energy_solution";
    private const string ShiftCard = "turing_machine_shift";
    private const string AlgorithmCard = "turing_algorithm_alpha";
    private const string PuzzleSkill = "turing_puzzle_radix_ii";

    private const string AttackDownBuff = "turing_attack_down";
    private const string DamageTakenUpBuff = "turing_damage_taken_up";
    private const string MagicDefDownBuff = "turing_magic_def_down";
    private const string AlgorithmMarkBuff = "turing_algorithm_alpha_mark";
    private const string RadixWeakenBuff = "turing_radix_weaken";
    private const string RadixAdvanceBuff = "turing_radix_advance_mark";

    #region 元数据与接线

    [TestCase(OriginCard, 2, "Weak", 30, 0, 30, "PhysicalDefense", 1)]
    [TestCase(LimitCard, 2, "Weak", 30, 0, 40, "", 0)]
    [TestCase(ShiftCard, 3, "Magical", 10, 12, 20, "MagicAttack", 2)]
    [TestCase(AlgorithmCard, 1, "Support", 20, 0, 40, "", 0)]
    public void Exclusive_cards_match_the_design(
        string cardId,
        int cost,
        string cardType,
        int priority,
        int baseValue,
        int maxHealth,
        string extraAttribute,
        int extraValue)
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");

        Assert.That(card!.Element, Is.EqualTo((int)EElement.Blue), "四张卡都是蓝属性");
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.BaseValue, Is.EqualTo(baseValue));
        Assert.That(card.CardType.ToString(), Is.EqualTo(cardType));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.Role, Is.EqualTo(ERole.Controller), "卡牌定位跟随角色定位");
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.CardGroupId, Is.Null, "无升级链");
        Assert.That(card.UpgradeTier, Is.Zero);
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        if (extraAttribute.Length > 0)
            Assert.That(card.Stats?.Attributes.GetValueOrDefault(extraAttribute), Is.EqualTo(extraValue));
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    /// <summary>
    /// 目标侧口径：两张弱化卡与魔法卡打敌方单体，支援卡打自身（效果目标由动作的
    /// <c>targetFilter</c> 重解析成"自身以外的蓝·人类·学术角色"）。
    /// </summary>
    [TestCase(OriginCard, "Enemy", "Single", "RandomLegal")]
    [TestCase(LimitCard, "Enemy", "Single", "RandomLegal")]
    [TestCase(ShiftCard, "Enemy", "Single", "RandomLegal")]
    [TestCase(AlgorithmCard, "Self", "Self", "Default")]
    public void Exclusive_cards_declare_the_expected_targeting(
        string cardId,
        string side,
        string scope,
        string retargetPolicy)
    {
        var definitions = BaseGameContent.Load();
        var card = definitions.Cards[cardId];

        Assert.That(card.TargetSide.ToString(), Is.EqualTo(side));
        Assert.That(card.TargetScope.ToString(), Is.EqualTo(scope));
        Assert.That(card.RetargetPolicy.ToString(), Is.EqualTo(retargetPolicy));
    }

    [Test]
    public void Cards_are_in_turing_initial_deck_and_their_skill_chains_resolve()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue("turing", out var turing), Is.True, "图灵未随内容出货");

        Assert.That(turing!.Element, Is.EqualTo(EElement.Blue));
        Assert.That(turing.Race, Is.EqualTo(ERace.Human | ERace.Academic));
        Assert.That(turing.Role, Is.EqualTo(ERole.Controller), "定位：控制");

        // 主动技蓄力链单档：谜题-进制II，cooldown 即技能进度阈值（Cap = 7）。
        Assert.That(turing.ActiveSkillChain, Has.Count.EqualTo(1));
        Assert.That(turing.ActiveSkillChain[0].SkillId, Is.EqualTo(PuzzleSkill));
        Assert.That(turing.ActiveSkillChain[0].Cooldown, Is.EqualTo(7));
        Assert.That(definitions.Skills[PuzzleSkill].TargetOverride, Is.Not.Null, "档位技能必须显式声明 targetOverride");

        foreach (var cardId in new[] { OriginCard, LimitCard, ShiftCard, AlgorithmCard })
        {
            Assert.That(turing.Cards, Does.Contain(cardId), $"图灵初始卡组缺少 {cardId}");

            var card = definitions.Cards[cardId];
            foreach (var skillRef in card.SkillRefs)
            {
                Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键（卡牌描述即由技能描述拼接）");

                foreach (var actionRef in skill.ActionRefs)
                {
                    Assert.That(definitions.SkillActions.TryGetValue(actionRef.ActionId, out var action), Is.True,
                        $"{cardId} → {skill.Id} 的动作 {actionRef.ActionId} 不存在");

                    if (action!.Kind != ESkillActionKind.ApplyBuff)
                        continue;

                    var buffId = action.Params?["buffId"]?.ToString();
                    Assert.That(buffId, Is.Not.Null.And.Not.Empty, $"{actionRef.ActionId} 缺少 buffId");
                    Assert.That(definitions.Buffs.ContainsKey(buffId!), Is.True, $"悬空 buffId '{buffId}'");
                }
            }
        }
    }

    /// <summary>三张减益的数值、时长与叠层规则必须与设计一致（2 回合 / 刷新）。</summary>
    [TestCase(AttackDownBuff, "PhysicalAttack", -6)]
    [TestCase(DamageTakenUpBuff, "DamageTakenScale", 0.25)]
    [TestCase(MagicDefDownBuff, "MagicDefense", -6)]
    public void Debuff_buffs_match_the_design(string buffId, string attributeId, double expectedMagnitude)
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Buffs.TryGetValue(buffId, out var buff), Is.True, $"{buffId} 未随内容出货");

        Assert.That(buff!.DurationType, Is.EqualTo(EBuffDurationType.Turns));
        Assert.That(buff.Duration, Is.EqualTo(2));
        Assert.That(buff.StackRule, Is.EqualTo(EBuffStackRule.Refresh));
        Assert.That(buff.Modifiers, Has.Count.EqualTo(1));

        var modifier = buff.Modifiers[0];
        Assert.That(modifier.AttributeId, Is.EqualTo(attributeId));
        Assert.That(modifier.Operation, Is.EqualTo(EAttributeModifierOp.Add));
        Assert.That(modifier.Magnitude.Scalar, Is.EqualTo(expectedMagnitude).Within(0.0001));
    }

    /// <summary>术演算法的推进标记：1 回合 + 刷新叠层（"不叠加"靠 Refresh 不重发 onApply 实现）。</summary>
    [Test]
    public void Algorithm_mark_advances_skill_progress_once_per_turn()
    {
        var definitions = BaseGameContent.Load();
        var mark = definitions.Buffs[AlgorithmMarkBuff];

        Assert.That(mark.DurationType, Is.EqualTo(EBuffDurationType.Turns));
        Assert.That(mark.Duration, Is.EqualTo(1));
        Assert.That(mark.StackRule, Is.EqualTo(EBuffStackRule.Refresh));
        Assert.That(mark.Modifiers, Is.Empty, "标记本体不改属性，收益在 onApply 钩子上");

        Assert.That(mark.Hooks.OnApply, Has.Count.EqualTo(1));
        var boost = mark.Hooks.OnApply[0];
        Assert.That(boost.EffectId, Is.EqualTo("turing_algorithm_alpha_boost"));
        Assert.That(boost.Params?["targetFilter"], Is.Not.Null, "钩子必须显式锁定持有者自己");

        var effect = definitions.Effects[boost.EffectId];
        Assert.That(effect.Kind, Is.EqualTo(EEffectKind.GainResource));
        Assert.That(effect.Params?["resource"]?.ToString(), Is.EqualTo("skillcounter"));
        // 内容 JSON 的 params 取值是 JsonElement（不实现 IConvertible），按文本比对。
        Assert.That(effect.Params?["amount"]?.ToString(), Is.EqualTo("1"));
    }

    #endregion

    #region 端到端

    [Test]
    public void Mechanical_origin_lowers_the_enemy_physical_attack_for_two_turns()
    {
        using var sim = BuildPlayerPhase(OriginCard, enemyExtraAttributes: new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.PhysicalAttack] = 20f,
        });
        var enemy = sim.EnemyTeam.Enemies[0];

        MarkAndExecute(sim, OriginCard, [Enemy(0)]);

        var debuff = enemy.Buffs.Find(AttackDownBuff);
        Assert.That(debuff, Is.Not.Null, "物攻 -6 必须落在被点选的敌人身上");
        Assert.That(debuff!.RemainingTurns, Is.EqualTo(2), "持续 2 回合");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(14f).Within(0.001f));
    }

    [Test]
    public void Limit_energy_solution_increases_all_damage_taken_for_two_turns()
    {
        using var sim = BuildPlayerPhase(LimitCard);
        var enemy = sim.EnemyTeam.Enemies[0];

        MarkAndExecute(sim, LimitCard, [Enemy(0)]);

        var debuff = enemy.Buffs.Find(DamageTakenUpBuff);
        Assert.That(debuff, Is.Not.Null);
        Assert.That(debuff!.RemainingTurns, Is.EqualTo(2));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(0.25f).Within(0.001f));

        // 增伤同桶加算（战斗规格「增伤与受伤增加一律加算」）：结算末尾的普通攻击应当被 +25%。
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null);
        Assert.That(normalAttack!.TotalDamage, Is.EqualTo(12.5f).Within(0.001f), "10 × (1 + 25%)");
    }

    [Test]
    public void Machine_shift_deals_magic_damage_and_lowers_magic_defense()
    {
        using var sim = BuildPlayerPhase(
            ShiftCard,
            enemyExtraAttributes: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalDefense] = 5f,
                [AttributeIds.MagicDefense] = 1f,
            });
        var enemy = sim.EnemyTeam.Enemies[0];
        var healthBefore = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        MarkAndExecute(sim, ShiftCard, [Enemy(0)]);

        // 卡牌：12 + 100% 施法者魔攻 10 − 目标魔防 1 = 21；随后本回合普通攻击另行结算。
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null, "卡牌结算完成后应执行普通攻击");
        Assert.That(
            enemy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(healthBefore - 21f - normalAttack!.TotalDamage).Within(0.001f));

        var debuff = enemy.Buffs.Find(MagicDefDownBuff);
        Assert.That(debuff, Is.Not.Null, "降魔防与被伤害在同一张卡上");
        Assert.That(debuff!.RemainingTurns, Is.EqualTo(2));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.EqualTo(-5f).Within(0.001f));
    }

    /// <summary>
    /// 术演算法-α：只推进"自身以外的蓝属性·人类·学术"角色，且同一回合内不叠加。
    /// 槽位 2（蓝·人类，非学术）与槽位 3（红·人类·学术）都必须被排除。
    /// </summary>
    [Test]
    public void Algorithm_alpha_advances_only_other_blue_human_academic_characters()
    {
        using var sim = BuildAlgorithmParty();

        MarkAndExecute(sim, AlgorithmCard, [Player(0)]);

        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.Zero, "自身不在范围内");
        Assert.That(sim.PlayerTeam.Characters[1].SkillCounter, Is.EqualTo(1), "红·人类·学术的队友 +1");
        Assert.That(sim.PlayerTeam.Characters[2].SkillCounter, Is.Zero, "红·人类的角色不满足学术");
        Assert.That(sim.PlayerTeam.Characters[3].SkillCounter, Is.Zero, "红·动物角色不在范围内");

        var mark = sim.PlayerTeam.Characters[1].Buffs.Find(AlgorithmMarkBuff);
        Assert.That(mark, Is.Not.Null, "推进标记落在受影响的队友身上");
        Assert.That(mark!.RemainingTurns, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(AlgorithmMarkBuff), Is.Null);
        Assert.That(sim.PlayerTeam.Characters[2].Buffs.Find(AlgorithmMarkBuff), Is.Null);
    }

    [Test]
    public void Algorithm_alpha_does_not_stack_twice_in_the_same_turn()
    {
        using var sim = BuildAlgorithmParty();
        var actionRef = new SkillActionRefDto { ActionId = "turing_apply_algorithm_alpha" };

        sim.EffectExecutor.ExecuteSkillActionRef(actionRef, sim, Player(0), [Player(0)]);
        sim.EffectExecutor.ExecuteSkillActionRef(actionRef, sim, Player(0), [Player(0)]);

        Assert.That(
            sim.PlayerTeam.Characters[1].SkillCounter,
            Is.EqualTo(1),
            "第二次投放只刷新标记，不再重复推进技能进度");
    }

    #endregion

    #region 潜能被动（0 / 20 / 50 / 99 四档）

    [Test]
    public void Turing_has_four_potential_passives()
    {
        var definitions = BaseGameContent.Load();
        var turing = definitions.Characters["turing"];

        Assert.That(
            turing.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        foreach (var passive in turing.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    [Test]
    public void Passive_one_grants_poison_immunity()
    {
        var definitions = BaseGameContent.Load();
        var buff = definitions.Buffs["turing_passive_p1"];

        Assert.That(buff.Tags, Does.Contain(BuiltinBuffTags.Passive));
        Assert.That(buff.Tags, Does.Contain(BuiltinBuffTags.TraitImmunePoison));
    }

    /// <summary>P2「停机判定」：阶层开始时随机一名敌人行动计数变 2（推迟到下个回合）。</summary>
    [Test]
    public void Passive_two_delays_one_enemy_at_each_wave_start()
    {
        using var sim = BuildPassiveParty(enemyCount: 2);

        sim.Buffs.Apply(sim, Player(0), "turing_passive_p2");
        sim.Buffs.FireWaveStart(sim);

        Assert.That(
            sim.EnemyTeam.Enemies.Count(enemy => enemy.ActionCount == 2),
            Is.EqualTo(1),
            "随机单体：两名敌人里恰好一名被推迟");
    }

    /// <summary>P3「进制推进」：阶层开始与阶层内每 8 回合推进技能进度（自身 +2 / 蓝 / 人类 / 学术 各 +1）。</summary>
    [Test]
    public void Passive_three_advances_skill_progress_at_wave_start_and_every_eighth_turn()
    {
        using var sim = BuildPassiveParty();

        sim.Buffs.Apply(sim, Player(0), "turing_passive_p3");
        sim.Buffs.FireWaveStart(sim);

        // 自身 +2（蓝·人类·学术三项也命中）＝ 5；其余按"蓝 / 人类 / 学术"三项叠加。
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(5), "自身：+2 起步，再吃蓝·人类·学术");
        Assert.That(sim.PlayerTeam.Characters[1].SkillCounter, Is.EqualTo(2), "红·人类·学术 队友：人类 +1 + 学术 +1");
        Assert.That(sim.PlayerTeam.Characters[2].SkillCounter, Is.EqualTo(1), "红·人类：只吃人类 +1");
        Assert.That(sim.PlayerTeam.Characters[3].SkillCounter, Is.Zero, "红·动物：三项全不命中");

        for (var i = 0; i < 8; i++)
            sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(10), "第 8 回合再 +5");
    }

    /// <summary>P4「不可判定」：释放主动技时敌方全体被推迟，每个阶层仅 1 次。</summary>
    [Test]
    public void Passive_four_delays_every_enemy_once_per_wave()
    {
        using var sim = BuildPassiveParty(enemyCount: 3);

        sim.Buffs.Apply(sim, Player(0), "turing_passive_p4");
        sim.Buffs.FireActiveSkillCast(sim, 0);

        Assert.That(
            sim.EnemyTeam.Enemies.Select(enemy => enemy.ActionCount),
            Is.All.EqualTo(2),
            "敌方全体行动计数变 2");

        sim.EnemyTeam.Enemies[0].ActionCount = 1;
        sim.Buffs.FireActiveSkillCast(sim, 0);
        Assert.That(sim.EnemyTeam.Enemies[0].ActionCount, Is.EqualTo(1), "同一阶层内不再触发（oncePerWave）");

        sim.Buffs.FireWaveStart(sim);
        sim.Buffs.FireActiveSkillCast(sim, 0);
        Assert.That(sim.EnemyTeam.Enemies[0].ActionCount, Is.EqualTo(2), "新阶层重新可用");
    }

    #endregion

    #region 主动技（谜题-进制II）

    [Test]
    public void Puzzle_radix_ii_weakens_every_enemy_for_one_turn()
    {
        using var sim = BuildActiveSkillParty();
        var caster = sim.PlayerTeam.Characters[0];
        caster.GainSkillCounter(7);

        var cast = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(cast.Success, Is.True, cast.Error);

        foreach (var enemy in sim.EnemyTeam.Enemies)
        {
            var weaken = enemy.Buffs.Find(RadixWeakenBuff);
            Assert.That(weaken, Is.Not.Null, "敌方全体都要吃减益");
            Assert.That(weaken!.RemainingTurns, Is.EqualTo(1), "持续 1 回合");
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f).Within(0.001f), "40 - 30");
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(10f).Within(0.001f), "40 - 30");
        }

        Assert.That(caster.SkillCounter, Is.Zero, "释放时扣掉阈值 7，自身不吃这次推进");
    }

    [Test]
    public void Puzzle_radix_ii_advances_only_other_blue_human_academic_allies()
    {
        using var sim = BuildActiveSkillParty();
        sim.PlayerTeam.Characters[0].GainSkillCounter(7);

        var cast = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(cast.Success, Is.True, cast.Error);

        Assert.That(sim.PlayerTeam.Characters[1].SkillCounter, Is.EqualTo(1), "红·人类·学术的队友 +1");
        Assert.That(sim.PlayerTeam.Characters[2].SkillCounter, Is.Zero, "红·人类的角色不满足学术");
        Assert.That(sim.PlayerTeam.Characters[3].SkillCounter, Is.Zero, "红·动物角色不在范围内");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(RadixAdvanceBuff), Is.Not.Null);
    }

    /// <summary>
    /// 主动技与「术演算法-α」各用一份标记：两个来源在同一回合各自推进 1 次
    /// （卡面的"不叠加"只约束这张卡自己）。
    /// </summary>
    [Test]
    public void Puzzle_radix_ii_and_algorithm_card_advance_from_separate_sources()
    {
        using var sim = BuildActiveSkillParty();
        var caster = sim.PlayerTeam.Characters[0];
        caster.GainSkillCounter(7);

        // 先打「术演算法-α」：队友 +1。
        var actionRef = new SkillActionRefDto { ActionId = "turing_apply_algorithm_alpha" };
        sim.EffectExecutor.ExecuteSkillActionRef(actionRef, sim, Player(0), [Player(0)]);
        Assert.That(sim.PlayerTeam.Characters[1].SkillCounter, Is.EqualTo(1));

        // 再放主动技：同一回合内再 +1（另一个来源）。
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(sim.PlayerTeam.Characters[1].SkillCounter, Is.EqualTo(2));
    }

    #endregion

    #region 装配

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatSimulation BuildPlayerPhase(
        string handCardId,
        int enemyCount = 1,
        int enemyHp = 300,
        IReadOnlyDictionary<string, float>? enemyExtraAttributes = null)
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "turing",
                DefaultAttributes(),
                drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-{index}")],
                element: EElement.Blue,
                race: ERace.Human | ERace.Academic))
            .ToArray();

        return BuildSimulation(registry, characters, enemyCount, enemyHp, enemyExtraAttributes);
    }

    /// <summary>
    /// 槽位身份：「红属性·人类·学术」是 2026-09-24 起的家族筛选口径（冯·诺依曼改红）。
    /// 0 = 图灵本人（蓝·人类·学术）、1 = 红·人类·学术（家族命中）、2 = 红·人类（学术不命中）、
    /// 3 = 红·动物（三项筛选全不命中）。
    /// </summary>
    private static readonly (EElement Element, ERace Race)[] PartyIdentities =
    [
        (EElement.Blue, ERace.Human | ERace.Academic),
        (EElement.Red, ERace.Human | ERace.Academic),
        (EElement.Red, ERace.Human),
        (EElement.Red, ERace.Animal),
    ];

    private static CombatSimulation BuildAlgorithmParty()
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = PartyIdentities
            .Select((identity, index) => CharacterBattleInstance.CreateForTests(
                "turing",
                DefaultAttributes(),
                drawPile: index == 0
                    ? [new CardRuntimeEntry(AlgorithmCard, $"rt-{AlgorithmCard}-0")]
                    : null,
                skillCounterCap: 5,
                element: identity.Element,
                race: identity.Race))
            .ToArray();

        return BuildSimulation(registry, characters, enemyCount: 1, enemyHp: 300, enemyExtraAttributes: null);
    }

    /// <summary>被动测试队伍：技能进度上限放大到 20（无主动链时走显式 Cap），敌人数量可调。</summary>
    private static CombatSimulation BuildPassiveParty(int enemyCount = 1)
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = PartyIdentities
            .Select(identity => CharacterBattleInstance.CreateForTests(
                "turing",
                DefaultAttributes(),
                skillCounterCap: 20,
                element: identity.Element,
                race: identity.Race))
            .ToArray();

        return BuildSimulation(registry, characters, enemyCount, enemyHp: 300, enemyExtraAttributes: null);
    }

    /// <summary>主动技测试队伍：全员带真实蓄力链（Cap = 7），敌人攻/魔攻 40 便于断言 -30。</summary>
    private static CombatSimulation BuildActiveSkillParty()
    {
        var registry = BaseGameContent.BuildRegistry();
        var chain = new[] { new ActiveSkillChainEntryDto { SkillId = PuzzleSkill, Cooldown = 7 } };
        var characters = PartyIdentities
            .Select(identity => CharacterBattleInstance.CreateForTests(
                "turing",
                DefaultAttributes(),
                activeSkillChain: chain,
                element: identity.Element,
                race: identity.Race))
            .ToArray();

        return BuildSimulation(
            registry,
            characters,
            enemyCount: 2,
            enemyHp: 300,
            enemyExtraAttributes: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 40f,
                [AttributeIds.MagicAttack] = 40f,
            });
    }

    private static CombatSimulation BuildSimulation(
        GameDefinitionRegistry registry,
        CharacterBattleInstance[] characters,
        int enemyCount,
        int enemyHp,
        IReadOnlyDictionary<string, float>? enemyExtraAttributes)
    {
        foreach (var character in characters)
        {
            character.DrawCards(1);
            character.RefillAvailableEnergy();
        }

        var enemyAttributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = enemyHp,
        };
        foreach (var (key, value) in enemyExtraAttributes ?? new Dictionary<string, float>(StringComparer.Ordinal))
            enemyAttributes[key] = value;

        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", enemyAttributes))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260925);
    }

    private static Dictionary<string, float> DefaultAttributes() => new(StringComparer.Ordinal)
    {
        [AttributeIds.MaxHealth] = 50f,
        [AttributeIds.PhysicalAttack] = 10f,
        [AttributeIds.MagicAttack] = 10f,
        [AttributeIds.MaxEnergy] = 10f,
        [AttributeIds.InitialEnergy] = 10f,
    };

    /// <summary>标记入队 → 切结算阶段执行（与 <c>ChaluxExclusiveCardContentTests</c> 同约定）。</summary>
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