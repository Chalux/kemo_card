using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 充能球（元素球）运行时：队列语义、手动/满员触发、逐球伤害公式（产球者 100% 攻击加算、
/// 无防御、无连携）、特殊球触发效果、回合结束产出规则。
/// </summary>
[TestFixture]
public sealed class ChargeOrbTests
{
    private const string Blue = BuiltinOrbTypes.Blue;
    private const string Physical = BuiltinOrbTypes.Physical;
    private const string Magic = BuiltinOrbTypes.Magic;
    private const string Special = "special";
    private const string SparkBuffId = "buff.spark";

    #region 队列与触发门槛

    [Test]
    public void Queue_constants_are_capacity_seven_and_threshold_three()
    {
        Assert.That(OrbQueue.Capacity, Is.EqualTo(7));
        Assert.That(OrbQueue.ManualTriggerThreshold, Is.EqualTo(3));
    }

    [Test]
    public void Grant_fills_queue_and_reaching_capacity_triggers_immediately()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());
        var enemy = sim.EnemyTeam.Enemies[0];
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        Assert.That(sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 6), Is.True);
        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(6), "未满员不入触发");

        Assert.That(sim.Orbs.Grant(sim, Blue, producerIndex: 0), Is.True);

        Assert.That(sim.Orbs.Queue.Count, Is.Zero, "第 7 个球入队即触发并清空");
        // 单球 = 6 + 100%×物攻 10 = 16，7 球共 112。
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(before - 112f).Within(0.001f));
    }

    [Test]
    public void Manual_trigger_needs_threshold_and_keeps_queue_when_rejected()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());
        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 2);

        var rejected = sim.Orbs.TriggerManual(sim);

        Assert.That(rejected.Triggered, Is.False);
        Assert.That(rejected.Error, Does.Contain("不足"));
        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(2), "不足门槛时不动队列");

        sim.Orbs.Grant(sim, Blue, producerIndex: 0);
        var triggered = sim.Orbs.TriggerManual(sim);

        Assert.That(triggered.Triggered, Is.True);
        Assert.That(triggered.ClearedByType[Blue], Is.EqualTo(3));
        Assert.That(sim.Orbs.Queue.Count, Is.Zero);
    }

    [Test]
    public void Manual_trigger_is_repeatable_within_the_same_phase()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs(), enemyHp: 500);
        var enemy = sim.EnemyTeam.Enemies[0];
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 3);
        Assert.That(sim.Orbs.TriggerManual(sim).Triggered, Is.True);
        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 3);
        Assert.That(sim.Orbs.TriggerManual(sim).Triggered, Is.True);

        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(before - 96f).Within(0.001f), "两次各 3 球");
    }

    [Test]
    public void Unknown_orb_type_is_soft_failure()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());

        Assert.That(sim.Orbs.Grant(sim, "orb.missing", producerIndex: 0), Is.False);
        Assert.That(sim.Orbs.Queue.Count, Is.Zero);
    }

    #endregion

    #region 伤害公式

    [Test]
    public void Element_orb_uses_higher_attack_and_applies_dealt_and_taken_scales()
    {
        // 产球者：物攻 10 / 魔攻 4 / 全伤害增加 25%；目标受伤倍率 50%。
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            producerAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 10f,
                [AttributeIds.MagicAttack] = 4f,
                [AttributeIds.DamageDealtScale] = 0.25f,
            });
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 0.5f);
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);

        // 单球 = (6 + 1.0×10) × (1 + 0.25 增伤 + 0.5 受伤增加) = 28；3 球 = 84。
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(before - 84f).Within(0.001f));
    }

    [Test]
    public void Physical_and_magic_orbs_use_their_own_attack_without_defense()
    {
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            enemyHp: 500,
            producerAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 10f,
                [AttributeIds.MagicAttack] = 20f,
            });
        var enemy = sim.EnemyTeam.Enemies[0];
        // 目标防御必须被忽略（元素/物理/魔法球都不吃物防/魔防）。
        enemy.Asc.SetBaseValue(AttributeIds.PhysicalDefense, 99f);
        enemy.Asc.SetBaseValue(AttributeIds.MagicDefense, 99f);

        sim.Orbs.Grant(sim, Physical, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 48f).Within(0.001f), "3×(6+10)");

        sim.Orbs.Grant(sim, Magic, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 48f - 78f).Within(0.001f), "3×(6+20)");
    }

    [Test]
    public void Producer_is_captured_at_grant_time()
    {
        // 球在获得时记录产球者：由弱角色产的球只吃弱角色的攻击。
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            enemyHp: 500,
            producerAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 30f,
                [AttributeIds.MagicAttack] = 0f,
            },
            secondAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 2f,
                [AttributeIds.MagicAttack] = 0f,
            });
        var enemy = sim.EnemyTeam.Enemies[0];

        sim.Orbs.Grant(sim, Blue, producerIndex: 1, count: 3);
        sim.Orbs.TriggerManual(sim);

        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 24f).Within(0.001f), "3×(6+2)");
    }

    [Test]
    public void Trigger_damages_every_alive_enemy_and_skips_dead_ones()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs(), enemyCount: 2, enemyHp: 200);
        var alive = sim.EnemyTeam.Enemies[0];
        var dead = sim.EnemyTeam.Enemies[1];
        dead.ApplyDamage(dead.MaxHp);
        var aliveBefore = alive.Asc.GetCurrentValue(AttributeIds.Health);

        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 3);
        var result = sim.Orbs.TriggerManual(sim);

        Assert.That(result.Triggered, Is.True);
        Assert.That(alive.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(aliveBefore - 48f).Within(0.001f));
        Assert.That(dead.Asc.GetCurrentValue(AttributeIds.Health), Is.Zero, "已阵亡敌人不结算");
    }

    [Test]
    public void Trigger_without_alive_target_clears_queue_without_damage()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.ApplyDamage(enemy.MaxHp);

        sim.Orbs.Grant(sim, Blue, producerIndex: 0, count: 3);
        var result = sim.Orbs.TriggerManual(sim);

        Assert.That(result.Triggered, Is.True, "无目标也照常触发（避免满员卡死队列）");
        Assert.That(sim.Orbs.Queue.Count, Is.Zero);
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.Zero);
    }

    [Test]
    public void Special_orb_runs_its_trigger_effects_once_per_orb_with_producer_as_source()
    {
        var orbTypes = BuiltinOrbs();
        orbTypes[Special] = new OrbTypeDto
        {
            Id = Special,
            DisplayNameId = "orb.special.name",
            DealsDamage = false,
            TriggerEffects = [new EffectRefDto { EffectId = "effect.spark" }],
        };
        var spark = new BuffDto
        {
            Id = SparkBuffId,
            MaxStacks = 5,
            StackRule = EBuffStackRule.Add,
            DurationType = EBuffDurationType.Permanent,
        };
        using var sim = BuildSim(
            orbTypes: orbTypes,
            buffs: new Dictionary<string, BuffDto> { [SparkBuffId] = spark },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.spark"] = new()
                {
                    Id = "effect.spark",
                    Kind = EEffectKind.ApplyBuff,
                    Params = new Dictionary<string, object> { ["buffId"] = SparkBuffId },
                },
            });

        sim.Orbs.Grant(sim, Special, producerIndex: 1, count: 3);
        var result = sim.Orbs.TriggerManual(sim);

        Assert.That(result.Triggered, Is.True);
        var holder = sim.PlayerTeam.Characters[1].Buffs.Find(SparkBuffId);
        Assert.That(holder, Is.Not.Null, "特殊球效果以产球者为源");
        Assert.That(holder!.Stacks, Is.EqualTo(3), "每球执行一次效果");
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(SparkBuffId), Is.Null, "不落到其他角色");
    }

    #endregion

    #region 回合结束产出

    [Test]
    public void Turn_end_produces_most_played_element_orb_and_attack_orb_with_highest_attack_producer()
    {
        // c0：物攻 5 / 魔攻 30（四属性球产球者）；c1：物攻 20 / 魔攻 2（物理球产球者）。
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            cards: new Dictionary<string, CardDto>
            {
                ["card.blue_phys"] = new() { Id = "card.blue_phys", Element = (int)EElement.Blue, CardType = ECardType.Physics },
                ["card.red_magic"] = new() { Id = "card.red_magic", Element = (int)EElement.Red, CardType = ECardType.Magical },
            },
            producerAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 5f,
                [AttributeIds.MagicAttack] = 30f,
            },
            secondAttrs: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.PhysicalAttack] = 20f,
                [AttributeIds.MagicAttack] = 2f,
            });

        sim.Orbs.GrantTurnEndOrbs(sim,
        [
            new PlayedCardRecord("card.blue_phys", 0),
            new PlayedCardRecord("card.blue_phys", 0),
            new PlayedCardRecord("card.red_magic", 1),
        ]);

        var orbs = sim.Orbs.Queue.Orbs;
        Assert.That(orbs, Has.Count.EqualTo(2), "每回合固定 1 个四属性球 + 1 个物理/魔法球");
        Assert.That(orbs[0].OrbTypeId, Is.EqualTo(Blue), "蓝卡出现最多 → 蓝球");
        Assert.That(orbs[0].ProducerIndex, Is.EqualTo(0), "四属性球取 max(物攻,魔攻) 最高者");
        Assert.That(orbs[1].OrbTypeId, Is.EqualTo(Physical), "物理卡出现最多 → 物理球");
        Assert.That(orbs[1].ProducerIndex, Is.EqualTo(1), "物理球取物攻最高者");
    }

    [Test]
    public void Turn_end_without_played_cards_produces_one_random_element_and_one_random_attack_orb()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());

        sim.Orbs.GrantTurnEndOrbs(sim, []);

        var orbs = sim.Orbs.Queue.Orbs;
        Assert.That(orbs, Has.Count.EqualTo(2));
        Assert.That(
            BuiltinOrbTypes.ElementOrbs.Select(entry => entry.OrbTypeId),
            Does.Contain(orbs[0].OrbTypeId));
        Assert.That(BuiltinOrbTypes.AttackOrbs, Does.Contain(orbs[1].OrbTypeId));
    }

    [Test]
    public void Turn_end_production_is_deterministic_for_the_same_seed()
    {
        static string Describe(int seed)
        {
            using var sim = BuildSim(orbTypes: BuiltinOrbs(), runSeed: seed);
            sim.Orbs.GrantTurnEndOrbs(sim, []);
            return string.Join("|", sim.Orbs.Queue.Orbs.Select(orb => $"{orb.OrbTypeId}:{orb.ProducerIndex}"));
        }

        Assert.That(Describe(20260919), Is.EqualTo(Describe(20260919)), "同种子必须可复现");
    }

    /// <summary>
    /// 回合内球数（<c>CountOrbsTriggeredThisTurn</c>）的账期与<b>回合边界</b>对齐：
    /// 清账发生在回合开始，而不是"回合结束产球"——产球会即时触发并再次记账，
    /// 若在取走出牌记录时清账，上一回合结束产出的球就会被算进下一回合
    /// （「本回合每触发 1 个绿球 +100% 魔攻」凭空多算）。
    /// </summary>
    [Test]
    public void Orb_trigger_count_is_cleared_at_turn_start_not_when_played_cards_are_taken()
    {
        using var sim = BuildSim(orbTypes: BuiltinOrbs());

        sim.RecordOrbsTriggered(new Dictionary<string, int>(StringComparer.Ordinal) { [Blue] = 2 });
        Assert.That(sim.CountOrbsTriggeredThisTurn(0), Is.EqualTo(2));

        // 回合结束产球：取走出牌记录。此时**不得**清球数账。
        sim.TakePlayedThisTurn();
        Assert.That(
            sim.CountOrbsTriggeredThisTurn(0),
            Is.EqualTo(2),
            "取走出牌记录不得清球数账，否则同一时点产出的球会被算进下一回合");

        // 回合开始：清账，本回合只统计本回合触发的球。
        sim.ResetOrbsTriggeredThisTurn();
        Assert.That(sim.CountOrbsTriggeredThisTurn(0), Is.Zero, "回合开始必须清账");

        // 元素掩码筛选不受清账影响。
        sim.RecordOrbsTriggered(new Dictionary<string, int>(StringComparer.Ordinal) { [Blue] = 1 });
        Assert.That(sim.CountOrbsTriggeredThisTurn((int)EElement.Blue), Is.EqualTo(1));
        Assert.That(sim.CountOrbsTriggeredThisTurn((int)EElement.Green), Is.Zero);
    }

    #endregion

    #region 内容授予（效果 / 技能动作）

    [Test]
    public void GainOrb_effect_grants_orbs_with_source_as_producer()
    {
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.gain"] = new()
                {
                    Id = "effect.gain",
                    Kind = EEffectKind.GainOrb,
                    Params = new Dictionary<string, object> { ["orbTypeId"] = Blue, ["count"] = 2 },
                },
            });

        sim.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = "effect.gain" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 1),
            [new CombatTargetRef(ECombatSide.Player, 1)]);

        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(2));
        Assert.That(sim.Orbs.Queue.Orbs.Select(orb => orb.ProducerIndex), Is.All.EqualTo(1));
    }

    [Test]
    public void GainOrb_skill_action_grants_orbs_with_source_as_producer()
    {
        using var sim = BuildSim(
            orbTypes: BuiltinOrbs(),
            skillActions: new Dictionary<string, SkillActionDto>
            {
                ["action.gain"] = new()
                {
                    Id = "action.gain",
                    Kind = ESkillActionKind.GainOrb,
                    Params = new Dictionary<string, object> { ["orbTypeId"] = Blue, ["count"] = 2 },
                },
            });

        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "action.gain" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 2),
            [new CombatTargetRef(ECombatSide.Player, 2)]);

        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(2));
        Assert.That(sim.Orbs.Queue.Orbs.Select(orb => orb.ProducerIndex), Is.All.EqualTo(2));
    }

    [Test]
    public void Validator_rejects_unknown_orb_type_and_empty_orb_definition()
    {
        var store = new GameDefinitionStore();
        store.EffectsMutable["effect.bad"] = new()
        {
            Id = "effect.bad",
            Kind = EEffectKind.GainOrb,
            Params = new Dictionary<string, object> { ["orbTypeId"] = "orb.missing" },
        };
        store.SkillActionsMutable["action.bad"] = new()
        {
            Id = "action.bad",
            Kind = ESkillActionKind.GainOrb,
        };
        store.OrbTypesMutable["orb.empty"] = new() { Id = "orb.empty" };

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.Message.Contains("Unknown orbTypeId 'orb.missing'")), Is.True);
        Assert.That(errors.Any(e => e.DefinitionId == "action.bad" && e.Message.Contains("orbTypeId")), Is.True);
        Assert.That(errors.Any(e => e.DefinitionId == "orb.empty"), Is.True, "空球（无伤害也无触发效果）必须被拒绝");
    }

    #endregion

    #region 装配

    private static Dictionary<string, OrbTypeDto> BuiltinOrbs() => new(StringComparer.Ordinal)
    {
        [Blue] = new()
        {
            Id = Blue,
            DisplayNameId = "orb.blue.name",
            DamageKind = EDamageKind.Elemental,
            Element = EElement.Blue,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
            AttackSource = EOrbAttackSource.Higher,
        },
        [Physical] = new()
        {
            Id = Physical,
            DisplayNameId = "orb.physical.name",
            DamageKind = EDamageKind.Physical,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
            AttackSource = EOrbAttackSource.Physical,
        },
        [Magic] = new()
        {
            Id = Magic,
            DisplayNameId = "orb.magic.name",
            DamageKind = EDamageKind.Magical,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
            AttackSource = EOrbAttackSource.Magic,
        },
    };

    private static IReadOnlyDictionary<string, float> DefaultAttrs(float physicalAttack = 10f, float magicAttack = 0f) =>
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = physicalAttack,
            [AttributeIds.MagicAttack] = magicAttack,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };

    private static CombatSimulation BuildSim(
        IReadOnlyDictionary<string, OrbTypeDto> orbTypes,
        int enemyHp = 200,
        int enemyCount = 1,
        int runSeed = 20260919,
        IReadOnlyDictionary<string, CardDto>? cards = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null,
        IReadOnlyDictionary<string, float>? producerAttrs = null,
        IReadOnlyDictionary<string, float>? secondAttrs = null)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: cards,
            effects: effects,
            buffs: buffs,
            skillActions: skillActions,
            orbs: orbTypes);

        var characters = new List<CharacterBattleInstance>
        {
            CharacterBattleInstance.CreateForTests("c0", producerAttrs ?? DefaultAttrs()),
            CharacterBattleInstance.CreateForTests("c1", secondAttrs ?? DefaultAttrs(physicalAttack: 1f)),
            CharacterBattleInstance.CreateForTests("c2", DefaultAttrs(physicalAttack: 1f)),
            CharacterBattleInstance.CreateForTests("c3", DefaultAttrs(physicalAttack: 1f)),
        };
        var enemies = Enumerable.Range(0, enemyCount)
            .Select(i => new EnemyUnit($"e{i}", "slime", maxHp: enemyHp))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: runSeed);
    }

    #endregion
}
