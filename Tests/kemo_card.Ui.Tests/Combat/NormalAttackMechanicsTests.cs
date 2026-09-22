using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 普通攻击的三项新能力（2026-09-21，莱因哈特套件催生）：
/// 追打（<see cref="BuiltinBuffTags.TraitFollowUp"/>）、普攻次数（<c>NormalAttackCount</c>）、
/// 普攻专属增伤 / 受伤倍率（<c>NormalAttackDamageDealtScale</c> / <c>NormalAttackDamageTakenScale</c>）。
/// 关键契约：这些只作用于普通攻击，绝不外溢到卡牌伤害。
/// </summary>
[TestFixture]
public sealed class NormalAttackMechanicsTests
{
    private const string FollowUpBuff = "buff.test_follow_up";
    private const string FollowUpBuff2 = "buff.test_follow_up_2";
    private const string ExtraAttackBuff = "buff.test_extra_attack";
    private const string ExtraAttackBuff2 = "buff.test_extra_attack_2";
    private const string DealtScaleBuff = "buff.test_dealt";

    #region 追打

    [Test]
    public void Follow_up_holder_joins_the_attack_at_his_percentage()
    {
        using var sim = Build(
            [Character("owner", 10f), Character("helper", 20f)],
            followUp: [(1, 100f, null)]);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SlotIndex, Is.EqualTo(0), "第 1 回合归属槽位 1（索引 0）");
        Assert.That(result.ParticipantCount, Is.EqualTo(2));
        Assert.That(result.FollowUpDamage, Is.EqualTo(20f).Within(0.001f));
        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 10f - 20f).Within(0.001f));
    }

    [Test]
    public void Only_the_highest_follow_up_percentage_applies()
    {
        // 追打的"多取最高"发生在**不同 buff** 之间（同名 buff 在容器里是同一实例，会按叠层规则合并）；
        // 出货内容正是用 follow_up_1 / _2 / _4 三个不同 id 表达不同时长的追打。
        using var sim = Build(
            [Character("owner", 10f), Character("helper", 20f)],
            followUp: [(1, 50f, null), (1, 80f, FollowUpBuff2)]);

        var result = sim.NormalAttacks.Execute(sim);

        // 两个追打 buff 只取最高：80% × 20 = 16（不与 50% 叠加）。
        Assert.That(result!.FollowUpDamage, Is.EqualTo(16f).Within(0.001f));
        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 10f - 16f).Within(0.001f));
    }

    [Test]
    public void Owner_holding_follow_up_does_not_strike_twice()
    {
        using var sim = Build([Character("owner", 10f)], followUp: [(0, 100f, null)]);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.ParticipantCount, Is.EqualTo(1), "归属者自己持追打不重复出手");
        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 10f).Within(0.001f));
    }

    [Test]
    public void Follow_up_uses_the_normal_attack_formula()
    {
        // 追打 50%：0.5 × 物攻 20 = 10，再减目标物防 4 → 6；归属者 10 − 4 = 6。
        using var sim = Build(
            [Character("owner", 10f), Character("helper", 20f)],
            followUp: [(1, 50f, null)],
            enemyPhysicalDefense: 4f);

        sim.NormalAttacks.Execute(sim);

        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 6f - 6f).Within(0.001f));
    }

    [Test]
    public void Follow_up_defaults_to_one_hundred_percent_without_a_percent_param()
    {
        using var sim = Build(
            [Character("owner", 10f), Character("helper", 20f)],
            followUp: [(1, null, null)]);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.FollowUpDamage, Is.EqualTo(20f).Within(0.001f), "缺省 100%");
    }

    #endregion

    #region 普攻次数

    [Test]
    public void Extra_normal_attack_repeats_the_whole_attack_including_follow_ups()
    {
        using var sim = Build(
            [Character("owner", 10f), Character("helper", 20f)],
            followUp: [(1, 100f, null)],
            extraAttacks: [0]);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.Executions, Is.EqualTo(2), "1 + NormalAttackCount");
        Assert.That(result.Strikes, Has.Count.EqualTo(4), "每轮：归属者 + 追打者");
        Assert.That(EnemyHp(sim), Is.EqualTo(500f - (2 * 10f) - (2 * 20f)).Within(0.001f));
    }

    [Test]
    public void Extra_attack_count_stacks_across_buffs()
    {
        // 两个**不同**的普攻次数 buff（各自的 id）叠在同一个角色上：属性口径天然可加。
        using var sim = Build([Character("owner", 10f)], extraAttacks: [0, 0]);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.Executions, Is.EqualTo(3), "1 + 两个各 +1 的 buff");
        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 30f).Within(0.001f));
    }

    #endregion

    #region 普攻专属增伤 / 受伤倍率

    [Test]
    public void Normal_attack_dealt_scale_boosts_the_normal_attack()
    {
        using var sim = Build([Character("owner", 10f)], dealtScale: [0]);

        sim.NormalAttacks.Execute(sim);

        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 15f).Within(0.001f), "10 × (1 + 0.5)");
    }

    [Test]
    public void Normal_attack_taken_scale_boosts_damage_against_the_marked_target()
    {
        using var sim = Build([Character("owner", 10f)], enemyNormalAttackTakenScale: 0.25f);

        sim.NormalAttacks.Execute(sim);

        Assert.That(EnemyHp(sim), Is.EqualTo(500f - 12.5f).Within(0.001f), "10 × (1 + 0.25)");
    }

    #endregion

    #region attackScale（伤害执行的攻击力系数）

    [Test]
    public void Attack_scale_scales_only_the_attack_term()
    {
        var source = new AbilitySystemComponent();
        source.SetBaseValue(AttributeIds.PhysicalAttack, 20f);
        var target = new AbilitySystemComponent();
        target.SetBaseValue(AttributeIds.MaxHealth, 100f);
        target.SetBaseValue(AttributeIds.Health, 100f);
        target.SetBaseValue(AttributeIds.PhysicalDefense, 5f);

        var spec = new GameplayEffectSpec(
            new GameplayEffectDefDto { Id = "ge.scaled", DurationPolicy = EDurationPolicy.Instant },
            sourceAsc: source,
            targetAsc: target,
            setByCaller: new Dictionary<string, float> { ["Amount"] = 3f });

        new DamageExecution().Execute(
            new ExecutionDefDto { Kind = "Damage", DamageType = "Physical", AttackScale = 0.25f },
            spec,
            target);

        // base = Amount 3 + 0.25 × 物攻 20 − 物防 5 = 3（Amount 不参与缩放）。
        Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(97f).Within(0.001f));
    }

    #endregion

    #region 装配

    private static CharacterBattleInstance Character(string id, float physicalAttack) =>
        CharacterBattleInstance.CreateForTests(
            id,
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = 50f,
                [AttributeIds.PhysicalAttack] = physicalAttack,
                [AttributeIds.MaxEnergy] = 10f,
                [AttributeIds.InitialEnergy] = 10f,
            });

    private static float EnemyHp(CombatSimulation simulation) =>
        simulation.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);

    /// <summary>
    /// 造一个最小战场：追打（可选百分比）、普攻次数（每个索引 = 挂一个独立 buff，因此可叠）、
    /// 普攻增伤、目标普攻受伤倍率与物防。
    /// </summary>
    private static CombatSimulation Build(
        IReadOnlyList<CharacterBattleInstance> characters,
        IReadOnlyList<(int Index, float? Percent, string? BuffId)>? followUp = null,
        IReadOnlyList<int>? extraAttacks = null,
        IReadOnlyList<int>? dealtScale = null,
        float enemyNormalAttackTakenScale = 0f,
        float enemyPhysicalDefense = 0f)
    {
        var buffs = new Dictionary<string, BuffDto>(StringComparer.Ordinal)
        {
            [FollowUpBuff] = FollowUpBuffDefinition(FollowUpBuff),
            [FollowUpBuff2] = FollowUpBuffDefinition(FollowUpBuff2),
            [ExtraAttackBuff] = ExtraAttackBuffDefinition(ExtraAttackBuff),
            [ExtraAttackBuff2] = ExtraAttackBuffDefinition(ExtraAttackBuff2),
            [DealtScaleBuff] = new()
            {
                Id = DealtScaleBuff,
                DurationType = EBuffDurationType.Permanent,
                Modifiers =
                [
                    Modifier(AttributeIds.NormalAttackDamageDealtScale, 0.5f),
                ],
            },
        };

        var registry = CombatTestHelper.CreateFullRegistry(buffs: buffs);
        var enemy = new EnemyUnit("e0", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 500f,
            [AttributeIds.PhysicalDefense] = enemyPhysicalDefense,
            [AttributeIds.NormalAttackDamageTakenScale] = enemyNormalAttackTakenScale,
        });

        var sim = new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState([enemy]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260921);

        foreach (var (index, percent, buffId) in followUp ?? [])
        {
            var parameters = percent is { } value
                ? new Dictionary<string, object>(StringComparer.Ordinal) { ["percent"] = value }
                : null;
            sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, index), buffId ?? FollowUpBuff, parameters);
        }

        var extraIndex = 0;
        foreach (var index in extraAttacks ?? [])
        {
            sim.Buffs.Apply(
                sim,
                new CombatTargetRef(ECombatSide.Player, index),
                extraIndex++ == 0 ? ExtraAttackBuff : ExtraAttackBuff2);
        }

        foreach (var index in dealtScale ?? [])
            sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, index), DealtScaleBuff);

        return sim;
    }

    private static BuffDto FollowUpBuffDefinition(string id) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        Tags = [BuiltinBuffTags.TraitFollowUp],
    };

    private static BuffDto ExtraAttackBuffDefinition(string id) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        Modifiers = [Modifier(AttributeIds.NormalAttackCount, 1f)],
    };

    private static AttributeModifierDefDto Modifier(string attributeId, float amount) => new()
    {
        AttributeId = attributeId,
        Operation = EAttributeModifierOp.Add,
        Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = amount },
    };

    #endregion
}
