using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEndConditionTests
{
    [Test]
    public void Shared_hp_zero_triggers_defeat()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void All_enemies_dead_triggers_victory_when_no_more_waves()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
    }

    /// <summary>
    /// 同归于尽必须判负：胜利规则 Priority(900) 低于判负规则(1000)，
    /// 此前它无条件写 Victory，会把先跑出来的判负改写成胜利。
    /// </summary>
    [Test]
    public void Simultaneous_death_resolves_to_defeat_not_victory()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        sim.CheckEndConditions();

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void Victory_rule_does_not_override_an_existing_defeat_decision()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.Defeat };
        new AllEnemiesDefeatedVictoryRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Defeat));
    }

    [Test]
    public void Victory_rule_still_decides_victory_when_nothing_decided_yet()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.None };
        new AllEnemiesDefeatedVictoryRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Victory));
    }

    [Test]
    public void Defeat_rule_marks_defeat_when_ledger_is_empty()
    {
        var sim = CombatSimulationTestBuilder.Standard();
        sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);

        var decision = new EndDecision { Kind = EEndDecisionKind.None };
        new SharedHpDefeatRule().OnCheckEndCondition(new CombatContext(sim, 1), ref decision);

        Assert.That(decision.Kind, Is.EqualTo(EEndDecisionKind.Defeat));
    }

    #region 全灭即结算（2026-09-26）

    /// <summary>
    /// 卡牌打死最后一名敌人：卡牌结算完立即判定胜利，不再空放普攻（普攻已无目标）。
    /// </summary>
    [Test]
    public void Card_kill_of_the_last_enemy_settles_immediately_without_normal_attack()
    {
        using var sim = CombatSimulationTestBuilder.WithQueuedCard();
        sim.EnemyTeam.Enemies[0].ApplyDamage(9); // 10 → 1：卡牌那 1 点就是致命一击

        sim.AdvancePhase(); // CardExecution：结算卡牌 → 全灭判定

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
        Assert.That(sim.NormalAttacks.ExecutionCount, Is.Zero, "全灭后不再空放普攻");
    }

    /// <summary>
    /// 玩家阶段（充能球）打死最后一名敌人：操作成功后立即判定胜利，不必把本回合走完。
    /// </summary>
    [Test]
    public void Player_phase_orb_kill_settles_immediately()
    {
        using var sim = OrbPlayerPhaseSim(enemyHp: 30);
        Assert.That(sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3), Is.True);

        var result = sim.TryApply(new TriggerOrbsCommand());

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory), "最后一名敌人死亡即结算");
    }

    /// <summary>
    /// 换波即切到下一个回合（2026-09-26）：卡牌杀光一波 → 新波立即登场、回合数 +1、
    /// 全员解除已行动，玩家在下一回合行动；被切掉的回合不空放普攻。
    /// </summary>
    [Test]
    public void Wave_change_advances_to_the_next_turn()
    {
        using var sim = TwoWaveBattle(enemyHp: 10, withOrbs: false, queueLethalCard: true, initialPhase: ECombatPhase.CardExecution);
        sim.EnemyTeam.Enemies[0].ApplyDamage(9); // 第一波 10 → 1
        var turnBefore = sim.TurnNumber;

        sim.AdvancePhase(); // CardExecution：卡牌杀光第一波 → 换波 + 切到下一回合

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "换波已发生");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.TurnNumber, Is.EqualTo(turnBefore + 1), "换波即切到下一个回合");
        Assert.That(sim.EnemyTeam.Enemies, Has.Count.EqualTo(1));
        Assert.That(sim.EnemyTeam.Enemies[0].IsAlive, Is.True, "新波敌人已登场");
        Assert.That(sim.NormalAttacks.ExecutionCount, Is.Zero, "被切掉的回合不空放普攻");
        foreach (var character in sim.PlayerTeam.Characters)
            Assert.That(character.HasActed, Is.False, "新回合全员解除已行动");
    }

    /// <summary>
    /// 玩家阶段全灭 + 还有下一波：同样切到下一个回合（新波登场、回合 +1、全员解除已行动）。
    /// </summary>
    [Test]
    public void Player_phase_wave_clear_advances_to_the_next_turn()
    {
        using var sim = TwoWaveBattle(enemyHp: 15, withOrbs: true, queueLethalCard: false, initialPhase: ECombatPhase.Player);
        Assert.That(sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3), Is.True);
        var turnBefore = sim.TurnNumber;

        var result = sim.TryApply(new TriggerOrbsCommand());

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "换波已发生");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.TurnNumber, Is.EqualTo(turnBefore + 1), "换波即切到下一个回合");
        Assert.That(sim.EnemyTeam.Enemies[0].IsAlive, Is.True, "新波敌人已登场");
        foreach (var character in sim.PlayerTeam.Characters)
            Assert.That(character.HasActed, Is.False, "新回合全员解除已行动");
    }

    #region 换波补跑回合结束（2026-09-26）

    /// <summary>
    /// 卡牌清波：本回合的回合结束管线必须在换波前补跑一次（buff 时长 tick 一次、不重复结算）。
    /// </summary>
    [Test]
    public void Card_wave_clear_resolves_turn_end_exactly_once()
    {
        using var sim = TwoWaveBattle(
            enemyHp: 10,
            withOrbs: false,
            queueLethalCard: true,
            initialPhase: ECombatPhase.CardExecution,
            buffs: WaveTurnEndBuffs());
        var ticking = ApplyTickingBuff(sim);
        sim.EnemyTeam.Enemies[0].ApplyDamage(9); // 第一波 10 → 1
        var turnBefore = sim.TurnNumber;

        sim.AdvancePhase(); // CardExecution：卡牌杀光第一波 → 补跑回合结束 → 换波

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "换波已发生");
        Assert.That(sim.TurnNumber, Is.EqualTo(turnBefore + 1));
        Assert.That(ticking.RemainingTurns, Is.EqualTo(1), "回合结束只结算一次（时长 2 → 1）");
    }

    /// <summary>
    /// 玩家阶段（充能球）清波：与卡牌清波同口径，补跑回合结束且只跑一次。
    /// </summary>
    [Test]
    public void Player_phase_wave_clear_resolves_turn_end_exactly_once()
    {
        using var sim = TwoWaveBattle(
            enemyHp: 15,
            withOrbs: true,
            queueLethalCard: false,
            initialPhase: ECombatPhase.Player,
            buffs: WaveTurnEndBuffs());
        var ticking = ApplyTickingBuff(sim);
        Assert.That(sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3), Is.True);
        var turnBefore = sim.TurnNumber;

        var result = sim.TryApply(new TriggerOrbsCommand());

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "换波已发生");
        Assert.That(sim.TurnNumber, Is.EqualTo(turnBefore + 1));
        Assert.That(ticking.RemainingTurns, Is.EqualTo(1), "玩家阶段清波同样补跑回合结束（只一次）");
    }

    /// <summary>
    /// 敌方阶段清波：回合结束管线已在敌方阶段末尾跑过，换波时不得再跑一次（时长 2 → 1，而非 0）。
    /// </summary>
    [Test]
    public void Enemy_phase_wave_clear_does_not_double_resolve_turn_end()
    {
        using var sim = TwoWaveBattle(
            enemyHp: 10,
            withOrbs: false,
            queueLethalCard: false,
            initialPhase: ECombatPhase.CardExecution,
            buffs: WaveTurnEndBuffs());
        var ticking = ApplyTickingBuff(sim);
        // 回合结束钩子击杀木桩：让"换波"发生在敌方阶段的回合结束管线之后。
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), "buff.turn_end_strike");

        sim.AdvancePhase(); // CardExecution：空队列 + 普攻（0 攻）→ 木桩存活 → 敌方阶段
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Enemy));
        var turnBefore = sim.TurnNumber;

        sim.AdvancePhase(); // Enemy：回合结束钩子击杀木桩 → 换波

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "换波已发生");
        Assert.That(sim.TurnNumber, Is.EqualTo(turnBefore + 1));
        Assert.That(ticking.RemainingTurns, Is.EqualTo(1), "回合结束不因换波重复结算（时长 2 → 1）");
    }

    private const string TickingBuffId = "buff.ticking_two_turns";

    /// <summary>时长 2 的 Turns buff（观察回合结束是否只 tick 一次）+ 回合结束击杀木桩的钩子。</summary>
    private static Dictionary<string, BuffDto> WaveTurnEndBuffs() => new(StringComparer.Ordinal)
    {
        [TickingBuffId] = new()
        {
            Id = TickingBuffId,
            DurationType = EBuffDurationType.Turns,
            Duration = 2,
        },
        ["buff.turn_end_strike"] = new()
        {
            Id = "buff.turn_end_strike",
            DurationType = EBuffDurationType.Permanent,
            Hooks = new BuffEffectHooksDto
            {
                OnTurnEnd =
                [
                    new EffectRefDto
                    {
                        EffectId = "effect.turn_end_strike",
                        Params = new Dictionary<string, object> { ["hookTargets"] = "allEnemies" },
                    },
                ],
            },
        },
    };

    private static BuffInstance ApplyTickingBuff(CombatSimulation sim)
    {
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), TickingBuffId);
        return sim.PlayerTeam.Characters[0].Buffs.Find(TickingBuffId)!;
    }

    #endregion

    /// <summary>玩家阶段 + 蓝球内容（单球 6 + 100%×物攻 10 = 16）的最小战场。</summary>
    private static CombatSimulation OrbPlayerPhaseSim(int enemyHp)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            orbs: new Dictionary<string, OrbTypeDto>(StringComparer.Ordinal)
            {
                [BuiltinOrbTypes.Blue] = new()
                {
                    Id = BuiltinOrbTypes.Blue,
                    DisplayNameId = "orb.blue.name",
                    DamageKind = EDamageKind.Elemental,
                    Element = EElement.Blue,
                    PerOrbAmount = 6f,
                    AttackBonusScale = 1f,
                    AttackSource = EOrbAttackSource.Higher,
                },
            });

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MagicAttack] = 0f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(i => CharacterBattleInstance.CreateForTests($"c{i}", attrs))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: enemyHp)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player);
    }

    /// <summary>
    /// 两波各 1 只史莱姆的战场：可选带蓝球内容、可选队列里预置一张 1 点伤害卡；
    /// 相位直接摆到指定相位（跳过开战管线），供"击杀 → 换波 / 结算"用例使用。
    /// <paramref name="buffs"/> 供"换波补跑回合结束"用例注入时长 / 钩子 buff。
    /// </summary>
    private static CombatSimulation TwoWaveBattle(
        int enemyHp,
        bool withOrbs,
        bool queueLethalCard,
        ECombatPhase initialPhase,
        IReadOnlyDictionary<string, BuffDto>? buffs = null)
    {
        var card = new CardDto
        {
            Id = "card.hit",
            DisplayNameId = "card.hit",
            TargetSide = ETargetSide.Enemy,
            TargetScope = ETargetScope.Single,
            TargetCount = 1,
            Priority = 1,
            SkillRefs = [new SkillRefDto { SkillId = "skill.hit" }],
        };
        var skill = new SkillDto
        {
            Id = "skill.hit",
            DisplayNameId = "skill.hit",
            EffectRefs = [new EffectRefDto { EffectId = "effect.hit" }],
        };
        var effect = new EffectDto
        {
            Id = "effect.hit",
            Kind = EEffectKind.Damage,
            Params = new Dictionary<string, object> { ["amount"] = 1 },
        };
        // 回合结束击杀木桩的钩子载荷：供"敌方阶段清波不重复结算回合结束"用例使用。
        var turnEndStrike = new EffectDto
        {
            Id = "effect.turn_end_strike",
            Kind = EEffectKind.Damage,
            Params = new Dictionary<string, object> { ["amount"] = 10000 },
        };
        var battle = new BattleDto
        {
            Id = "test_two_waves",
            CombatRuleIds = [SharedHpDefeatRule.RuleId, AllEnemiesDefeatedVictoryRule.RuleId],
            Waves =
            [
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = "slime", Count = 1 }] },
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = "slime", Count = 1 }] },
            ],
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.PartyHpCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
                [card.Id] = card,
            },
            skills: new Dictionary<string, SkillDto> { [skill.Id] = skill },
            effects: new Dictionary<string, EffectDto>
            {
                [effect.Id] = effect,
                [turnEndStrike.Id] = turnEndStrike,
            },
            buffs: buffs,
            enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = enemyHp } },
            battles: new Dictionary<string, BattleDto> { [battle.Id] = battle },
            orbs: withOrbs ? BuiltinBlueOrb() : null);

        var sim = CombatSimulationFactory.TryCreate(
            battle,
            [.. CombatSimulationTestBuilder.CreateParty(CombatConstants.SlotCount, registry)],
            registry,
            new HostRng(7, "combat"),
            runSeed: 7,
            runRuleIds: null,
            scriptHost: null,
            modId: "test.mod",
            CombatRuleCatalog.CreateDefault(),
            out var error);
        if (sim is null)
            throw new InvalidOperationException(error ?? "两波战斗模拟创建失败。");

        sim.TransitionTo(initialPhase);
        if (queueLethalCard)
        {
            sim.CardQueue.Enqueue(new QueuedCardEntry(
                CharacterIndex: 0,
                CardId: card.Id,
                RuntimeInstanceId: "rt-card-hit",
                Priority: card.Priority,
                Targets: [new CombatTargetRef(ECombatSide.Enemy, 0)],
                Sequence: sim.AllocateQueueSequence()));
        }

        return sim;
    }

    private static Dictionary<string, OrbTypeDto> BuiltinBlueOrb() => new(StringComparer.Ordinal)
    {
        [BuiltinOrbTypes.Blue] = new()
        {
            Id = BuiltinOrbTypes.Blue,
            DisplayNameId = "orb.blue.name",
            DamageKind = EDamageKind.Elemental,
            Element = EElement.Blue,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
            AttackSource = EOrbAttackSource.Higher,
        },
    };

    #endregion
}