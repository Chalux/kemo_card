using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 规格 §8 收尾：固定种子完整回合脚本 + 跨波 SharedHp 保留。
/// </summary>
[TestFixture]
public sealed class CombatSimulationIntegrationTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

    private readonly record struct CombatOutcome(int SharedHp, int EnemyHp, ECombatPhase Phase);

    private sealed class RecordingScriptHost : IContentEffectScriptHost
    {
        public List<string> Invocations { get; } = [];

        public bool TryExecute(
            string modId,
            string scriptPath,
            string scriptEntry,
            IReadOnlyDictionary<string, object>? context,
            out IReadOnlyList<Dictionary<string, object>> proposedEffects)
        {
            Invocations.Add(scriptPath);
            proposedEffects = [];
            return true;
        }
    }

    [Test]
    public void Same_seed_and_commands_produce_identical_outcome()
    {
        CombatOutcome RunOnce()
        {
            var sim = CombatSimulationTestBuilder.FullBattle(seed: 12345);
            sim.TryApply(new ConfirmCharacterCommand(0));
            sim.TryApply(new ConfirmCharacterCommand(1));
            sim.TryApply(new ConfirmCharacterCommand(2));
            sim.TryApply(new ConfirmCharacterCommand(3));
            sim.AdvancePhase();
            return new CombatOutcome(
                sim.PlayerTeam.SharedHp,
                sim.EnemyTeam.Enemies[0].CurrentHp,
                sim.Phase);
        }

        Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
    }

    #region 固定种子完整回合

    [Test]
    public void Full_round_script_is_reproducible_and_reaches_victory()
    {
        var first = RunFullRoundScript(seed: 20260728);
        var second = RunFullRoundScript(seed: 20260728);

        Assert.That(second.SharedHpTrail, Is.EqualTo(first.SharedHpTrail));
        Assert.That(second.ExecutionOrder, Is.EqualTo(first.ExecutionOrder));
        Assert.That(second.FinalPhase, Is.EqualTo(ECombatPhase.Victory));
        Assert.That(second.ExecutionOrder, Is.EqualTo(new[] { "card.high", "card.low" }), "priority 降序");
        Assert.That(second.EnergyAfterSecondPhase, Is.EqualTo(first.EnergyAfterSecondPhase));
        Assert.That(second.HandAfterSecondPhase, Is.EqualTo(first.HandAfterSecondPhase));
        Assert.That(second.ActiveSkillConsumedCounter, Is.True);
        Assert.That(first.SharedHpTrail[0], Is.EqualTo(60), "BattleStart 抬 Max 后补满");
    }

    private sealed record FullRoundSnapshot(
        IReadOnlyList<int> SharedHpTrail,
        IReadOnlyList<string> ExecutionOrder,
        ECombatPhase FinalPhase,
        int EnergyAfterSecondPhase,
        int HandAfterSecondPhase,
        bool ActiveSkillConsumedCounter);

    private static FullRoundSnapshot RunFullRoundScript(int seed)
    {
        using var sim = BuildFullRoundSim(seed);

        var trail = new List<int> { sim.PlayerTeam.SharedHp };
        Assert.That(
            sim.PlayerTeam.Characters.All(c => c.HandSlots.Count(s => !s.IsEmpty) == CombatConstants.HandSlotCount),
            Is.True,
            "开局满手");
        Assert.That(sim.PlayerTeam.MaxHp, Is.EqualTo(60), "每人 MaxHealth 10+10，队伍 MaxSharedHp=60");

        var c0 = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True, "标记 high");
        Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy0)).Success, Is.True, "标记 low");

        for (var i = 0; i < 4; i++)
            c0.TickSkillCounter();
        var counterBefore = c0.SkillCounter;
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, Enemy0)).Success, Is.True);
        var activeConsumed = c0.SkillCounter == counterBefore - 4;

        var executionOrder = sim.CardQueue.PeekAllOrdered().Select(e => e.CardId).ToArray();

        for (var i = 0; i < 4; i++)
            Assert.That(sim.TryApply(new ConfirmCharacterCommand(i)).Success, Is.True);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));

        sim.AdvancePhase();
        trail.Add(sim.PlayerTeam.SharedHp);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(3), "8 - (3+2) = 3");

        if (sim.Phase == ECombatPhase.Enemy)
            sim.AdvancePhase();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(c0.CurrentEnergy, Is.EqualTo(4), "第二回合当前能量 +1");
        Assert.That(c0.AvailableEnergy, Is.EqualTo(4));
        var handAfter = c0.HandSlots.Count(s => !s.IsEmpty);
        Assert.That(handAfter, Is.GreaterThanOrEqualTo(3), "执行耗两张后公式抽牌应补回");
        trail.Add(sim.PlayerTeam.SharedHp);

        // 第二回合：再打出足够伤害清敌取胜（敌方剩 3 HP）。
        var unmarked = c0.HandSlots
            .Select((slot, index) => (slot, index))
            .Where(x => !x.slot.IsEmpty && !x.slot.IsMarked)
            .Take(2)
            .ToArray();
        foreach (var (_, slotIndex) in unmarked)
            Assert.That(sim.TryApply(new PlayCardCommand(0, slotIndex, Enemy0)).Success, Is.True);
        for (var i = 0; i < 4; i++)
            Assert.That(sim.TryApply(new ConfirmCharacterCommand(i)).Success, Is.True);
        if (sim.Phase == ECombatPhase.CardExecution)
            sim.AdvancePhase();
        trail.Add(sim.PlayerTeam.SharedHp);

        return new FullRoundSnapshot(
            trail,
            executionOrder,
            sim.Phase,
            c0.CurrentEnergy,
            handAfter,
            activeConsumed);
    }

    #endregion

    #region §8 缺口：跨波 SharedHp

    [Test]
    public void Shared_hp_is_preserved_across_waves()
    {
        using var sim = BuildTwoWaveSim(seed: 11);
        sim.RunBattleStart();
        var maxHp = sim.PlayerTeam.MaxHp;
        sim.PlayerTeam.ApplySharedDamage(7);
        var afterDamage = sim.PlayerTeam.SharedHp;
        Assert.That(afterDamage, Is.EqualTo(maxHp - 7));

        sim.EnemyTeam.Enemies[0].Asc.Attributes.SetCurrentValue(AttributeIds.Health, 0f);
        sim.CheckEndConditions();

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(afterDamage), "跨波保留 SharedHp，不 FreezeAndFill");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.EnemyTeam.Enemies[0].IsAlive, Is.True, "第二波敌人已生成");
    }

    #endregion

    #region 构建

    private static CombatSimulation BuildFullRoundSim(int seed)
    {
        var host = new RecordingScriptHost();
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                ["card.high"] = new()
                {
                    Id = "card.high",
                    CostType = ECostType.Energy,
                    Cost = 1,
                    Priority = 200,
                    TargetSide = ETargetSide.Enemy,
                    TargetScope = ETargetScope.Single,
                    TargetCount = 1,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.high" }],
                },
                ["card.low"] = new()
                {
                    Id = "card.low",
                    CostType = ECostType.Energy,
                    Cost = 1,
                    Priority = 50,
                    TargetSide = ETargetSide.Enemy,
                    TargetScope = ETargetScope.Single,
                    TargetCount = 1,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.low" }],
                },
            },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.high"] = new()
                {
                    Id = "skill.high",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.high_dmg" }],
                },
                ["skill.low"] = new()
                {
                    Id = "skill.low",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.low_dmg" }],
                },
                ["skill.active"] = new()
                {
                    Id = "skill.active",
                    ActionRefs = [new SkillActionRefDto { ActionId = "action.active" }],
                    TargetOverride = new TargetSpecDto
                    {
                        Side = ETargetSide.Enemy,
                        Scope = ETargetScope.Single,
                        TargetCount = 1,
                    },
                },
                ["skill.boost_a"] = new()
                {
                    Id = "skill.boost_a",
                    ActionRefs = [new SkillActionRefDto { ActionId = "action.boost_a" }],
                },
                ["skill.boost_b"] = new()
                {
                    Id = "skill.boost_b",
                    ActionRefs = [new SkillActionRefDto { ActionId = "action.boost_b" }],
                },
            },
            skillActions: new Dictionary<string, SkillActionDto>
            {
                ["action.active"] = new()
                {
                    Id = "action.active",
                    Kind = ESkillActionKind.ExecuteScript,
                    ScriptPath = "active.js",
                },
                ["action.boost_a"] = new()
                {
                    Id = "action.boost_a",
                    Kind = ESkillActionKind.ApplyGameplayEffect,
                    Params = new Dictionary<string, object> { ["gameplayEffectId"] = "ge.boost_a" },
                },
                ["action.boost_b"] = new()
                {
                    Id = "action.boost_b",
                    Kind = ESkillActionKind.ApplyGameplayEffect,
                    Params = new Dictionary<string, object> { ["gameplayEffectId"] = "ge.boost_b" },
                },
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["ge.boost_a"] = MaxHealthBoost("ge.boost_a", 10f),
                ["ge.boost_b"] = MaxHealthBoost("ge.boost_b", 10f),
            },
            attributes: new Dictionary<string, AttributeDefDto>
            {
                [AttributeIds.MaxHealth] = new() { Id = AttributeIds.MaxHealth },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.high_dmg"] = new()
                {
                    Id = "effect.high_dmg",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 3 },
                },
                ["effect.low_dmg"] = new()
                {
                    Id = "effect.low_dmg",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 2 },
                },
            });

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = 5f,
            [AttributeIds.InitialEnergy] = 3f,
        };
        var chain = new[] { new ActiveSkillChainEntryDto { SkillId = "skill.active", Cooldown = 4 } };
        var characters = Enumerable.Range(0, 4)
            .Select(i =>
            {
                var pile = new List<CardRuntimeEntry>
                {
                    new("card.high", $"rt-{i}-h0"),
                    new("card.low", $"rt-{i}-l0"),
                    new("card.high", $"rt-{i}-h1"),
                    new("card.low", $"rt-{i}-l1"),
                    new("card.high", $"rt-{i}-h2"),
                    new("card.low", $"rt-{i}-l2"),
                    new("card.high", $"rt-{i}-h3"),
                };
                return CharacterBattleInstance.CreateForTests($"c{i}", attrs, pile, activeSkillChain: chain);
            })
            .ToArray();

        var sim = new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 8)]),
            new CombatRuleEngine(
            [
                new SharedHpDefeatRule(),
                new AllEnemiesDefeatedVictoryRule(),
            ]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            runSeed: seed,
            scriptHost: host,
            battleStartSkills:
            [
                new BattleStartSkillEntry("skill.boost_a", 0),
                new BattleStartSkillEntry("skill.boost_b", 1),
            ]);
        sim.RunBattleStart();
        return sim;
    }

    private static CombatSimulation BuildTwoWaveSim(int seed)
    {
        var battle = new BattleDto
        {
            Id = "two_wave",
            CombatRuleIds = [AllEnemiesDefeatedVictoryRule.RuleId, SharedHpDefeatRule.RuleId],
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
            },
            enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = 5 } },
            battles: new Dictionary<string, BattleDto> { [battle.Id] = battle });
        var catalog = CombatRuleCatalog.CreateDefault();
        var party = CombatSimulationTestBuilder.CreateParty(4, registry);
        var sim = CombatSimulationFactory.TryCreate(
            battle,
            party,
            registry,
            new HostRng(seed, "combat"),
            runSeed: seed,
            runRuleIds: null,
            scriptHost: null,
            modId: "test.mod",
            catalog,
            out var error);
        if (sim is null)
            throw new InvalidOperationException(error ?? "两波战斗创建失败。");
        return sim;
    }

    private static GameplayEffectDefDto MaxHealthBoost(string id, float amount) => new()
    {
        Id = id,
        DurationPolicy = EDurationPolicy.Instant,
        StackingPolicy = EStackingPolicy.None,
        MaxStacks = 1,
        Modifiers =
        [
            new AttributeModifierDefDto
            {
                AttributeId = AttributeIds.MaxHealth,
                Operation = EAttributeModifierOp.Add,
                Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = amount },
            },
        ],
    };

    #endregion
}