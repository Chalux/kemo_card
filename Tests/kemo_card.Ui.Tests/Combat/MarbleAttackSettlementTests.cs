using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 弹珠阶段轮值普攻结算（2026-11-23 设计）：标记优先 / 最少 Count 击杀 / 站位平手 /
/// 单目标一包 / 零分空过 / 当前血条与护盾 / 免疫 / 追打与实际损血转盾 / 剩余与溢出记录，
/// 以及预览与实弹共用同一套无副作用分配。
/// </summary>
[TestFixture]
public sealed class MarbleAttackSettlementTests
{
    private const string FollowUpBuffId = "test.marble.follow_up";
    private const string ImmuneBuffId = "test.marble.immune";
    private const float Epsilon = 1e-3f;

    #region 分配算法

    [Test]
    public void Unmarked_allocation_kills_the_cheapest_enemy_then_spends_remaining_count()
    {
        using var sim = Build([Character(0)], [Enemy(0, 18f), Enemy(1, 70f)]);

        sim.NormalAttacks.SettleMarbles(sim, 6, markedEnemyIndex: -1);

        var result = sim.NormalAttacks.LastMarbleSettlement;
        Assert.That(result, Is.Not.Null);
        Assert.That(
            result!.Allocations.Select(allocation => (allocation.EnemyIndex, allocation.AssignedCount)),
            Is.EqualTo(new[] { (0, 2), (1, 4) }),
            "A 需要 2 点击杀、B 需要 7 点；先杀 A，剩余 4 点全部给预计扣血最多的 B");
        Assert.That(Hp(sim, 0), Is.Zero);
        Assert.That(Hp(sim, 1), Is.EqualTo(30f).Within(Epsilon));
        Assert.That(result.TotalCount, Is.EqualTo(6));
        Assert.That(result.AllocatedCount, Is.EqualTo(6));
        Assert.That(result.RemainingCount, Is.Zero);
        Assert.That(result.RemainingReason, Is.EqualTo(MarbleRemainderReason.None));
        Assert.That(sim.NormalAttacks.ExecutionCount, Is.Zero, "弹珠结算不得调用旧的自动全体普攻");
        Assert.That(sim.NormalAttacks.LastResult, Is.Null);
    }

    [Test]
    public void Marked_target_consumes_count_first_and_only_the_minimum_for_a_kill()
    {
        using var sim = Build([Character(0)], [Enemy(0, 18f), Enemy(1, 70f)]);

        sim.NormalAttacks.SettleMarbles(sim, 6, markedEnemyIndex: 1);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        var allocation = result.Allocations.Single();
        Assert.That(allocation.EnemyIndex, Is.EqualTo(1));
        Assert.That(allocation.AssignedCount, Is.EqualTo(6), "不足以击杀标记目标时拿走全部剩余 Count");
        Assert.That(allocation.IsMarked, Is.True);
        Assert.That(Hp(sim, 0), Is.EqualTo(18f).Within(Epsilon), "未分配的目标不吃伤害");
        Assert.That(Hp(sim, 1), Is.EqualTo(10f).Within(Epsilon));
    }

    [Test]
    public void Marked_target_keeps_only_the_minimum_kill_count_and_the_rest_goes_to_others()
    {
        using var sim = Build([Character(0)], [Enemy(0, 18f), Enemy(1, 70f)]);

        sim.NormalAttacks.SettleMarbles(sim, 6, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(
            result.Allocations.Select(allocation => (allocation.EnemyIndex, allocation.AssignedCount)),
            Is.EqualTo(new[] { (0, 2), (1, 4) }),
            "标记 A 已足够击杀时只拿 2 点，余下 4 点继续按最少击杀 / 最大扣血分配");
        Assert.That(result.Allocations[0].IsMarked, Is.True);
        Assert.That(result.Allocations[1].IsMarked, Is.False);
    }

    [Test]
    public void Kill_cost_ties_resolve_by_enemy_station_order()
    {
        using var sim = Build([Character(0)], [Enemy(0, 15f), Enemy(1, 15f)]);

        sim.NormalAttacks.SettleMarbles(sim, 2, markedEnemyIndex: -1);

        var allocation = sim.NormalAttacks.LastMarbleSettlement!.Allocations.Single();
        Assert.That(allocation.EnemyIndex, Is.Zero, "平手按敌方站位升序");
        Assert.That(allocation.AssignedCount, Is.EqualTo(2));
        Assert.That(Hp(sim, 1), Is.EqualTo(15f).Within(Epsilon));
    }

    [Test]
    public void Damage_floor_ties_also_resolve_by_station_order()
    {
        using var sim = Build([Character(0)], [Enemy(0, 100f), Enemy(1, 100f)]);

        sim.NormalAttacks.SettleMarbles(sim, 3, markedEnemyIndex: -1);

        var allocation = sim.NormalAttacks.LastMarbleSettlement!.Allocations.Single();
        Assert.That(allocation.EnemyIndex, Is.Zero);
        Assert.That(allocation.AssignedCount, Is.EqualTo(3));
        Assert.That(Hp(sim, 0), Is.EqualTo(70f).Within(Epsilon));
        Assert.That(Hp(sim, 1), Is.EqualTo(100f).Within(Epsilon));
    }

    [Test]
    public void Taunt_limits_the_legal_mark_and_an_illegal_mark_is_ignored()
    {
        using var illegalMark = Build(
            [Character(0)],
            [Enemy(0, 100f, taunt: 0f), Enemy(1, 100f, taunt: 5f)]);
        illegalMark.NormalAttacks.SettleMarbles(illegalMark, 3, markedEnemyIndex: 0);

        Assert.That(
            illegalMark.NormalAttacks.LastMarbleSettlement!.Allocations.Single().EnemyIndex,
            Is.Zero,
            "低嘲讽目标的标记非法：忽略标记后按站位平手选择 0 号");

        using var legalMark = Build(
            [Character(0)],
            [Enemy(0, 100f, taunt: 0f), Enemy(1, 100f, taunt: 5f)]);
        legalMark.NormalAttacks.SettleMarbles(legalMark, 3, markedEnemyIndex: 1);

        var allocation = legalMark.NormalAttacks.LastMarbleSettlement!.Allocations.Single();
        Assert.That(allocation.EnemyIndex, Is.EqualTo(1), "最高嘲讽目标才是合法标记");
        Assert.That(allocation.IsMarked, Is.True);
    }

    #endregion

    #region 口径：防御 / 护盾 / 当前血条 / 免疫

    [Test]
    public void Defense_is_subtracted_once_per_target_packet()
    {
        using var sim = Build([Character(0)], [Enemy(0, 100f, physicalDefense: 6f)]);

        sim.NormalAttacks.SettleMarbles(sim, 3, markedEnemyIndex: 0);

        // 一包：(3 × 10 − 6) = 24；若逐点扣防会只剩 12。
        Assert.That(Hp(sim, 0), Is.EqualTo(76f).Within(Epsilon));
        Assert.That(sim.NormalAttacks.LastMarbleSettlement!.Allocations.Single().HealthLoss,
            Is.EqualTo(24f).Within(Epsilon));
    }

    [Test]
    public void Magic_attack_uses_magic_defense_and_sets_the_damage_kind()
    {
        using var sim = Build(
            [Character(0, physicalAttack: 5f, magicAttack: 20f)],
            [Enemy(0, 100f, physicalDefense: 99f, magicDefense: 3f)]);

        sim.NormalAttacks.SettleMarbles(sim, 2, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.Kind, Is.EqualTo(EDamageKind.Magical));
        Assert.That(Hp(sim, 0), Is.EqualTo(63f).Within(Epsilon), "(2 × 20 − 3)");
    }

    [Test]
    public void Enemy_shield_is_counted_in_kill_cost_and_consumed_by_the_packet()
    {
        using var sim = Build(
            [Character(0)],
            [Enemy(0, 18f, shield: 5f), Enemy(1, 100f)]);

        sim.NormalAttacks.SettleMarbles(sim, 6, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        var marked = result.Allocations.Single(allocation => allocation.EnemyIndex == 0);
        Assert.That(marked.AssignedCount, Is.EqualTo(3), "18 血 + 5 盾需要 3 点（30 伤害）");
        Assert.That(marked.ShieldAbsorbed, Is.EqualTo(5f).Within(Epsilon));
        Assert.That(marked.HealthLoss, Is.EqualTo(18f).Within(Epsilon));
        Assert.That(marked.Overflow, Is.EqualTo(7f).Within(Epsilon));
        Assert.That(Hp(sim, 0), Is.Zero);
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
        Assert.That(result.Allocations.Single(allocation => allocation.EnemyIndex == 1).AssignedCount,
            Is.EqualTo(3), "余下 3 点继续分配");
    }

    [Test]
    public void Boss_minimum_kill_stops_at_the_current_life_bar_and_records_overflow()
    {
        var boss = Boss(0, 25, 50);
        using var sim = Build([Character(0)], [boss]);

        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 2, 0), Is.False);
        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 3, 0), Is.True,
            "3 点足以打空当前 25 血条并将触发换条保护");

        sim.NormalAttacks.SettleMarbles(sim, 5, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        var allocation = result.Allocations.Single();
        Assert.That(allocation.AssignedCount, Is.EqualTo(3), "Boss 以当前血条为止，只分配最小击杀 Count");
        Assert.That(allocation.HealthLoss, Is.EqualTo(25f).Within(Epsilon));
        Assert.That(allocation.Overflow, Is.EqualTo(5f).Within(Epsilon), "30 伤害打 25 血条，溢出 5");
        Assert.That(allocation.DepletedLifeBar, Is.True);
        Assert.That(allocation.EntersLifeBarProtection, Is.True);
        Assert.That(result.RemainingCount, Is.EqualTo(2));
        Assert.That(result.RemainingReason, Is.EqualTo(MarbleRemainderReason.AllTargetsProtected),
            "Boss 当前血条会被打空并触发换条保护，余下 Count 无法继续分配");
        Assert.That(boss.IsAwaitingLifeBar, Is.True);
        Assert.That(boss.LifeBarIndex, Is.Zero, "只破当前条，不传递溢出到下一血条");
        Assert.That(Hp(sim, 0), Is.Zero);

        sim.NormalAttacks.SettleMarbles(sim, 5, markedEnemyIndex: 0);
        var blocked = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(blocked.Allocations, Is.Empty, "换条保护中的 Boss 跳过");
        Assert.That(blocked.RemainingCount, Is.EqualTo(5));
        Assert.That(blocked.RemainingReason, Is.EqualTo(MarbleRemainderReason.AllTargetsProtected));
        Assert.That(boss.LifeBarIndex, Is.Zero);
    }

    [Test]
    public void Fully_immune_target_is_skipped_and_blocks_clear()
    {
        using var sim = Build([Character(0)], [Enemy(0, 100f), Enemy(1, 10f)]);
        ApplyImmune(sim, 0);

        sim.NormalAttacks.SettleMarbles(sim, 2, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.Allocations.Single().EnemyIndex, Is.EqualTo(1));
        Assert.That(Hp(sim, 0), Is.EqualTo(100f).Within(Epsilon), "免疫目标完全跳过");
        Assert.That(Hp(sim, 1), Is.Zero);
        Assert.That(result.RemainingCount, Is.EqualTo(1));
        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 10, 0), Is.False,
            "存活且完全免疫的目标不可能被本次伤害清掉");
    }

    [Test]
    public void No_living_enemy_records_all_count_as_remaining()
    {
        using var sim = Build([Character(0)], [Enemy(0, 10f)]);
        sim.EnemyTeam.Enemies[0].ApplyDamage(10);

        sim.NormalAttacks.SettleMarbles(sim, 5, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.Allocations, Is.Empty);
        Assert.That(result.AllocatedCount, Is.Zero);
        Assert.That(result.RemainingCount, Is.EqualTo(5));
        Assert.That(result.RemainingReason, Is.EqualTo(MarbleRemainderReason.AllEnemiesDefeated));
    }

    [Test]
    public void Killing_the_last_enemy_records_leftover_count_with_the_defeated_reason()
    {
        using var sim = Build([Character(0)], [Enemy(0, 10f)]);

        sim.NormalAttacks.SettleMarbles(sim, 5, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.Allocations.Single().AssignedCount, Is.EqualTo(1), "只拿击杀所需的 1 点");
        Assert.That(result.RemainingCount, Is.EqualTo(4));
        Assert.That(result.RemainingReason, Is.EqualTo(MarbleRemainderReason.AllEnemiesDefeated));
    }

    #endregion

    #region 零分空过 / 单包 / 预览

    [Test]
    public void Zero_count_is_a_complete_no_op()
    {
        using var sim = Build([Character(0)], [Enemy(0, 100f)]);
        sim.Presentation.Clear();

        sim.NormalAttacks.SettleMarbles(sim, 0, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.TotalCount, Is.Zero);
        Assert.That(result.Allocations, Is.Empty);
        Assert.That(result.RemainingReason, Is.EqualTo(MarbleRemainderReason.ZeroCount));
        Assert.That(Hp(sim, 0), Is.EqualTo(100f).Within(Epsilon));
        Assert.That(sim.Presentation.Count, Is.Zero, "零 Count 不触发命中 / 伤害表现");
    }

    [Test]
    public void Each_target_receives_exactly_one_base_packet_regardless_of_count()
    {
        using var sim = Build([Character(0)], [Enemy(0, 100f)]);
        sim.Presentation.Clear();

        sim.NormalAttacks.SettleMarbles(sim, 4, markedEnemyIndex: 0);

        var damageEvents = sim.Presentation.Drain()
            .OfType<DamageDealtEvent>()
            .Where(presentationEvent => presentationEvent.Target.Side == ECombatSide.Enemy)
            .ToList();
        Assert.That(damageEvents, Has.Count.EqualTo(1), "同一目标整轮只产生一包普攻伤害");
        Assert.That(damageEvents[0].Amount, Is.EqualTo(40f).Within(Epsilon));
    }

    [Test]
    public void Preview_is_pure_and_matches_the_actual_settlement()
    {
        using var sim = Build(
            [Character(0)],
            [Enemy(0, 100f, physicalDefense: 2f, shield: 10f)]);
        sim.Presentation.Clear();

        var preview = sim.NormalAttacks.PreviewMarbles(sim, 4, markedEnemyIndex: 0);
        var canClear = sim.NormalAttacks.CanClearWithMarbles(sim, 4, 0);

        Assert.That(preview.Allocations.Single().ShieldAbsorbed, Is.EqualTo(10f).Within(Epsilon));
        Assert.That(preview.Allocations.Single().HealthLoss, Is.EqualTo(28f).Within(Epsilon),
            "(4 × 10 − 2) − 10 护盾");
        Assert.That(preview.Allocations.Single().RemainingHealth, Is.EqualTo(72f).Within(Epsilon));
        Assert.That(canClear, Is.False);
        Assert.That(Hp(sim, 0), Is.EqualTo(100f).Within(Epsilon), "预览不得写生命");
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo(10f).Within(Epsilon), "预览不得写护盾");
        Assert.That(sim.Presentation.Count, Is.Zero, "预览不得发表现事件");
        Assert.That(sim.NormalAttacks.LastMarbleSettlement, Is.Null, "预览不得写结算记录");

        sim.NormalAttacks.SettleMarbles(sim, 4, markedEnemyIndex: 0);

        var actual = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(actual.Allocations.Single().AssignedCount,
            Is.EqualTo(preview.Allocations.Single().AssignedCount));
        Assert.That(actual.Allocations.Single().HealthLoss,
            Is.EqualTo(preview.Allocations.Single().HealthLoss).Within(Epsilon));
        Assert.That(actual.Allocations.Single().ShieldAbsorbed,
            Is.EqualTo(preview.Allocations.Single().ShieldAbsorbed).Within(Epsilon));
        Assert.That(Hp(sim, 0), Is.EqualTo(72f).Within(Epsilon));
    }

    [Test]
    public void Clear_preview_includes_damage_rules_and_matches_the_actual_kill()
    {
        var rule = new HalvingRule();
        using var sim = Build(
            [Character(0)],
            [Enemy(0, 10f)],
            rules: [rule]);

        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 1, 0), Is.False,
            "规则把 10 点减半后不足以击杀 10 血目标");
        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 2, 0), Is.True);

        Assert.That(rule.AppliedHits, Is.Zero);
        sim.NormalAttacks.SettleMarbles(sim, 2, markedEnemyIndex: 0);
        Assert.That(rule.AppliedHits, Is.EqualTo(1));

        Assert.That(sim.NormalAttacks.LastMarbleSettlement!.Allocations.Single().AssignedCount,
            Is.EqualTo(2), "最少击杀 Count 必须按规则修正后的伤害计算");
        Assert.That(Hp(sim, 0), Is.Zero);
    }

    [Test]
    public void Clear_requires_every_normal_enemy_and_boss_to_die_or_enter_protection()
    {
        using var normal = Build([Character(0)], [Enemy(0, 20f)]);
        Assert.That(normal.NormalAttacks.CanClearWithMarbles(normal, 1, 0), Is.False);
        Assert.That(normal.NormalAttacks.CanClearWithMarbles(normal, 2, 0), Is.True);

        using var bossOnly = Build([Character(0)], [Boss(0, 25, 50)]);
        Assert.That(bossOnly.NormalAttacks.CanClearWithMarbles(bossOnly, 3, 0), Is.True);

        using var mixed = Build([Character(0)], [Boss(0, 25, 50), Enemy(1, 20f)]);
        Assert.That(mixed.NormalAttacks.CanClearWithMarbles(mixed, 3, 0), Is.False,
            "还有普通敌人存活时，即使 Boss 会换条也不能提前结束");
    }

    [Test]
    public void Clear_preview_accounts_for_follow_up_damage()
    {
        using var sim = Build([Character(0), Character(1)], [Enemy(0, 20f)]);
        ApplyFollowUp(sim, 1, 100f);

        Assert.That(sim.NormalAttacks.CanClearWithMarbles(sim, 1, 0), Is.True,
            "基础普攻 10 + 追打 10 足以击杀 20 血目标");

        sim.NormalAttacks.SettleMarbles(sim, 1, markedEnemyIndex: 0);

        Assert.That(Hp(sim, 0), Is.Zero);
        Assert.That(sim.NormalAttacks.LastMarbleSettlement!.TotalFollowUpDamage,
            Is.EqualTo(10f).Within(Epsilon));
    }

    #endregion

    #region 追打与转盾

    [Test]
    public void Follow_up_hits_only_allocated_survivors_with_the_followers_own_attack()
    {
        using var sim = Build(
            [Character(0), Character(1, physicalAttack: 20f, normalAttackShieldGainScale: 1f)],
            [Enemy(0, 100f)]);
        ApplyFollowUp(sim, 1, 50f);

        sim.NormalAttacks.SettleMarbles(sim, 3, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        var allocation = result.Allocations.Single();
        Assert.That(allocation.HealthLoss, Is.EqualTo(30f).Within(Epsilon), "归属者 3 × 10");
        Assert.That(allocation.FollowUpDamage, Is.EqualTo(30f).Within(Epsilon), "追打 3 × 20 × 50%");
        Assert.That(allocation.RemainingHealth, Is.EqualTo(40f).Within(Epsilon));
        Assert.That(result.TotalFollowUpDamage, Is.EqualTo(30f).Within(Epsilon));
        Assert.That(
            sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo(30f).Within(Epsilon),
            "追打者按自己的实际损血转盾");
    }

    [Test]
    public void Follow_up_skips_targets_killed_or_protected_by_the_base_attack()
    {
        using var killed = Build(
            [Character(0), Character(1, physicalAttack: 100f)],
            [Enemy(0, 20f)]);
        ApplyFollowUp(killed, 1, 100f);

        killed.NormalAttacks.SettleMarbles(killed, 2, markedEnemyIndex: 0);

        Assert.That(killed.NormalAttacks.LastMarbleSettlement!.TotalFollowUpDamage, Is.Zero,
            "基础普攻已经打死目标，追打跳过");
        Assert.That(Hp(killed, 0), Is.Zero);

        var boss = Boss(0, 10, 50);
        using var protectedBoss = Build(
            [Character(0), Character(1, physicalAttack: 100f)],
            [boss]);
        ApplyFollowUp(protectedBoss, 1, 100f);

        protectedBoss.NormalAttacks.SettleMarbles(protectedBoss, 1, markedEnemyIndex: 0);

        Assert.That(protectedBoss.NormalAttacks.LastMarbleSettlement!.TotalFollowUpDamage, Is.Zero,
            "基础普攻触发换条保护后追打跳过，不得打到下一条血");
        Assert.That(boss.IsAwaitingLifeBar, Is.True);
        Assert.That(boss.LifeBarIndex, Is.Zero);
    }

    [Test]
    public void Shield_gain_uses_actual_health_loss_not_overflow()
    {
        using var sim = Build(
            [Character(0, normalAttackShieldGainScale: 1f)],
            [Enemy(0, 25f)]);

        sim.NormalAttacks.SettleMarbles(sim, 5, markedEnemyIndex: 0);

        var result = sim.NormalAttacks.LastMarbleSettlement!;
        Assert.That(result.TotalOverflow, Is.EqualTo(5f).Within(Epsilon), "30 伤害打 25 血");
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo(25f).Within(Epsilon),
            "只按实际生命损失 25 转盾，不把 5 点溢出算进去");
    }

    [Test]
    public void Shield_gain_excludes_enemy_shield_absorption()
    {
        using var sim = Build(
            [Character(0, normalAttackShieldGainScale: 1f)],
            [Enemy(0, 100f, shield: 20f)]);

        sim.NormalAttacks.SettleMarbles(sim, 3, markedEnemyIndex: 0);

        Assert.That(sim.NormalAttacks.LastMarbleSettlement!.Allocations.Single().ShieldAbsorbed,
            Is.EqualTo(20f).Within(Epsilon));
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo(10f).Within(Epsilon),
            "30 伤害中 20 被敌方护盾吸收，只为实际损血 10 转盾");
    }

    #endregion

    #region 装配

    private sealed class HalvingRule : ICombatRule
    {
        public string Id => "test.marble.halving";
        public int AppliedHits { get; private set; }
        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) => AppliedHits++;

        public int Priority => 0;

        public void ModifyDamage(CombatContext ctx, ref DamagePacket packet) =>
            packet.Amount *= 0.5f;
    }

    private static CharacterBattleInstance Character(
        int index,
        float physicalAttack = 10f,
        float magicAttack = 0f,
        float normalAttackShieldGainScale = 0f)
    {
        var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = physicalAttack,
            [AttributeIds.MagicAttack] = magicAttack,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        if (normalAttackShieldGainScale != 0f)
            attributes[AttributeIds.NormalAttackShieldGainScale] = normalAttackShieldGainScale;

        return CharacterBattleInstance.CreateForTests($"c{index}", attributes);
    }

    private static EnemyUnit Enemy(
        int index,
        float maxHp,
        float physicalDefense = 0f,
        float magicDefense = 0f,
        float shield = 0f,
        float taunt = 0f)
    {
        var enemy = new EnemyUnit(
            $"e{index}",
            "slime",
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = maxHp,
            });

        enemy.Asc.SetBaseValue(AttributeIds.PhysicalDefense, physicalDefense);
        enemy.Asc.SetBaseValue(AttributeIds.MagicDefense, magicDefense);
        enemy.Asc.SetBaseValue(AttributeIds.Shield, shield);
        enemy.Asc.SetBaseValue(AttributeIds.Taunt, taunt);
        return enemy;
    }

    private static EnemyUnit Boss(int index, params int[] lifeBars)
    {
        Assert.That(lifeBars, Is.Not.Empty);
        var boss = Enemy(index, lifeBars[0]);
        boss.ConfigureLifeBars(
            lifeBars.Select(maxHp => new EnemyLifeBarDto { MaxHp = maxHp }).ToArray(),
            hpScale: 1.0);
        return boss;
    }

    private static CombatSimulation Build(
        IReadOnlyList<CharacterBattleInstance> characters,
        IReadOnlyList<EnemyUnit> enemies,
        ICombatRule[]? rules = null)
    {
        var buffs = new Dictionary<string, BuffDto>(StringComparer.Ordinal)
        {
            [FollowUpBuffId] = new()
            {
                Id = FollowUpBuffId,
                DurationType = EBuffDurationType.Permanent,
                Tags = [BuiltinBuffTags.TraitFollowUp],
            },
            [ImmuneBuffId] = new()
            {
                Id = ImmuneBuffId,
                DurationType = EBuffDurationType.Permanent,
                Tags = [BuiltinBuffTags.TraitImmuneDamage],
            },
        };
        var registry = CombatTestHelper.CreateFullRegistry(buffs: buffs);

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState(enemies),
            new CombatRuleEngine(rules ?? Array.Empty<ICombatRule>()),
            registry,
            initialPhase: ECombatPhase.CardExecution,
            runSeed: 20261123);
    }

    private static void ApplyFollowUp(CombatSimulation simulation, int characterIndex, float percent)
    {
        var result = simulation.Buffs.Apply(
            simulation,
            new CombatTargetRef(ECombatSide.Player, characterIndex),
            FollowUpBuffId,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["percent"] = percent });
        Assert.That(result.Success, Is.True, result.Error ?? "buff 投放失败");
    }

    private static void ApplyImmune(CombatSimulation simulation, int enemyIndex)
    {
        var result = simulation.Buffs.Apply(
            simulation,
            new CombatTargetRef(ECombatSide.Enemy, enemyIndex),
            ImmuneBuffId);
        Assert.That(result.Success, Is.True, result.Error ?? "buff 投放失败");
    }

    private static float Hp(CombatSimulation simulation, int enemyIndex) =>
        simulation.EnemyTeam.Enemies[enemyIndex].Asc.GetCurrentValue(AttributeIds.Health);

    #endregion
}
