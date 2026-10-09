using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 按实际弃牌数增加后续抽牌修正，以及爱因斯坦主动技的即时补抽：
/// <c>DiscardAndRecord</c> 按<b>实际</b>弃置张数记账到 <c>CombatSimulation.LastDiscardCount</c>，
/// <c>ModifyDrawCountByDiscard</c> 读取它 × <c>perCard</c> 投放抽牌数量修正。
/// 账期 = 当前玩家阶段（阶段开始清零），读取不清账，因此同回合的其它读取方仍能看到同一数值。
/// </summary>
[TestFixture]
public sealed class DiscardDrawCountTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Self0 = [new CombatTargetRef(ECombatSide.Player, 0)];

    #region 记账与投放

    [Test]
    public void Discard_and_record_counts_actual_discards_and_feeds_the_next_draw()
    {
        // 主动技通道（真实内容走这条）：均匀随机可含已标记。
        using var sim = BuildSim(handSize: 3, seed: 7);
        var character = sim.PlayerTeam.Characters[0];
        sim.SetDiscardChannel(EDiscardChannel.ActiveSkill);

        ExecuteAction(sim, "action.discard_two");

        Assert.That(sim.LastDiscardCount, Is.EqualTo(2), "请求 2 张、手牌 3 张 ⇒ 记 2");
        Assert.That(HandCount(sim, 0), Is.EqualTo(1));
        Assert.That(character.Graveyard, Has.Count.EqualTo(2));

        ExecuteAction(sim, "action.draw_by_discard");

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "基准 1 + 实际弃牌 2");
        Assert.That(sim.LastDiscardCount, Is.EqualTo(2), "读取不清账：同回合的其它读取方仍看得到");
        Assert.That(
            sim.PlayerTeam.Characters[1].ComputeDrawCount(),
            Is.EqualTo(1),
            "targetFilter.self 只落在来源角色身上");
    }

    [Test]
    public void Short_hand_records_only_what_was_actually_discarded()
    {
        using var sim = BuildSim(handSize: 1);
        var character = sim.PlayerTeam.Characters[0];
        sim.SetDiscardChannel(EDiscardChannel.Other);

        ExecuteAction(sim, "action.discard_three");

        Assert.That(sim.LastDiscardCount, Is.EqualTo(1), "手牌只有 1 张 ⇒ 记 1，不是请求的 3");
        Assert.That(HandCount(sim, 0), Is.Zero);

        ExecuteAction(sim, "action.draw_by_discard");

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(2), "基准 1 + 实际弃牌 1");
    }

    [Test]
    public void Per_card_factor_multiplies_the_recorded_count()
    {
        using var sim = BuildSim(handSize: 3);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.discard_two");
        ExecuteAction(sim, "action.draw_by_discard_per_two");

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(5), "基准 1 + 2 张 × perCard 2");
    }

    [Test]
    public void Chained_discard_then_read_uses_the_recorded_count()
    {
        using var sim = BuildSim(handSize: 2);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.chain_discard_then_draw");

        Assert.That(sim.LastDiscardCount, Is.EqualTo(2));
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3));
    }

    [Test]
    public void Plain_discard_does_not_record_and_the_read_stays_a_no_op()
    {
        using var sim = BuildSim(handSize: 3);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.discard_plain");
        Assert.That(sim.LastDiscardCount, Is.Zero, "普通 Discard 不记账");

        ExecuteAction(sim, "action.draw_by_discard");
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(1), "没有记录 ⇒ 零操作");
    }

    [Test]
    public void Discard_and_record_on_an_empty_hand_records_zero()
    {
        using var sim = BuildSim(handSize: 0);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.discard_two");
        Assert.That(sim.LastDiscardCount, Is.Zero);

        ExecuteAction(sim, "action.draw_by_discard");
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(1));
    }

    #endregion

    #region 效果通道（buff 钩子）

    [Test]
    public void Legacy_effect_kind_adds_the_modifier_through_the_effect_executor()
    {
        using var sim = BuildSim(handSize: 3, includeReadEffect: true);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.discard_two");
        sim.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = "effect.draw_by_discard" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 0),
            Self0);

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "效果通道与技能动作同口径");
    }

    #endregion

    #region 账期：下一回合抽牌步骤消费，玩家阶段开始清账

    [Test]
    public void Modifier_is_consumed_by_the_next_player_phase_draw_step()
    {
        using var sim = BuildSim(handSize: 2, deckSize: 10);
        var character = sim.PlayerTeam.Characters[0];

        ExecuteAction(sim, "action.discard_two");
        ExecuteAction(sim, "action.draw_by_discard");
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3));

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(HandCount(sim, 0), Is.EqualTo(3), "抽牌步骤按修正抽 3 张");
        Assert.That(sim.LastDiscardCount, Is.Zero, "玩家阶段开始即清账（账期 = 本玩家阶段）");

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(HandCount(sim, 0), Is.EqualTo(4), "修正已消费：回到每回合 1 张");
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(1));
    }

    [Test]
    public void Ledger_is_cleared_even_without_a_reader()
    {
        using var sim = BuildSim(handSize: 2);

        ExecuteAction(sim, "action.discard_two");
        Assert.That(sim.LastDiscardCount, Is.EqualTo(2));

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(sim.LastDiscardCount, Is.Zero, "只弃不读的记录不得泄漏到下一回合");
    }

    #endregion

    #region 内容准入校验

    [Test]
    public void Shipped_shapes_pass_validation()
    {
        var errors = Validate(StoreWith(
            skillActions:
            [
                Action("action.discard_two", ESkillActionKind.DiscardAndRecord, Params(("count", 2), ("random", true))),
                Action(
                    "action.draw_by_discard",
                    ESkillActionKind.ModifyDrawCountByDiscard,
                    Params(("targetFilter", new Dictionary<string, object> { ["self"] = true }))),
                Action(
                    "action.draw_by_discard_per_two",
                    ESkillActionKind.ModifyDrawCountByDiscard,
                    Params(("perCard", 2))),
            ],
            effects:
            [
                Effect("effect.draw_by_discard", EEffectKind.ModifyDrawCountByDiscard, Params(("perCard", 1))),
            ]));

        Assert.That(errors, Is.Empty, string.Join("; ", errors.Select(error => error.Message)));
    }

    [Test]
    public void Negative_per_card_is_rejected_on_both_channels()
    {
        var skillActionErrors = Validate(StoreWith(skillActions:
        [
            Action("action.draw_by_discard", ESkillActionKind.ModifyDrawCountByDiscard, Params(("perCard", -1))),
        ]));
        var effectErrors = Validate(StoreWith(effects:
        [
            Effect("effect.draw_by_discard", EEffectKind.ModifyDrawCountByDiscard, Params(("perCard", -1))),
        ]));

        Assert.That(
            skillActionErrors.Any(error => error.Message.Contains("params.perCard")),
            Is.True,
            "负 perCard 会让「弃 X 张抽 X 张」静默反向减抽");
        Assert.That(
            effectErrors.Any(error => error.Message.Contains("params.perCard")),
            Is.True);
    }

    [TestCase(ESkillActionKind.ModifyDrawCountByDiscard, "many")]
    [TestCase(ESkillActionKind.DrawByDiscard, "many")]
    [TestCase(ESkillActionKind.DrawByDiscard, -1)]
    public void Invalid_per_card_is_rejected(ESkillActionKind kind, object perCard)
    {
        var errors = Validate(StoreWith(skillActions:
        [
            Action("action.draw_by_discard", kind, Params(("perCard", perCard))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("params.perCard")), Is.True);
    }

    /// <summary>
    /// <c>Discard</c> 与 <c>DiscardAndRecord</c> 的 <c>count</c> 都由
    /// <c>ValidateScaledRuntimeParams</c> 统一校验（<c>&gt;= 0</c>），这里锁住新动作没有漏网。
    /// </summary>
    [Test]
    public void Negative_count_on_discard_and_record_is_rejected()
    {
        var errors = Validate(StoreWith(skillActions:
        [
            Action("action.discard_two", ESkillActionKind.DiscardAndRecord, Params(("count", -1))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("params.count")), Is.True);
    }

    #endregion

    #region 出货内容端到端（爱因斯坦主动技载荷）

    /// <summary>
    /// 真实内容 <c>einstein_discard_two</c> / <c>einstein_draw_by_discard</c>（主动技通道）：
    /// 弃 2 张 → 施法者立即抽 2 张。这条用例同时锁住内容里的 kind 名与 <c>targetFilter.self</c> 写法。
    /// </summary>
    [Test]
    public void Shipped_einstein_actions_discard_two_and_draw_immediately()
    {
        var registry = BaseGameContent.BuildRegistry();
        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = 5f,
            [AttributeIds.InitialEnergy] = 5f,
        };
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests(
                "einstein",
                attrs,
                Enumerable.Range(0, 6).Select(slot => new CardRuntimeEntry("test.card", $"rt-c{i}-{slot}"))))
            .ToArray();
        foreach (var character in characters)
            character.DrawCards(3);

        using var sim = new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 1);

        // 主动技释放期间状态机把弃牌通道切到 ActiveSkill（见 CombatStateMachine.ApplyCastActiveSkill）。
        sim.SetDiscardChannel(EDiscardChannel.ActiveSkill);
        sim.SelectedDiscardCharacterIndex = 0;
        sim.SelectedDiscardSlots = new Queue<int>([0, 2]);
        ExecuteAction(sim, "einstein_discard_two");
        ExecuteAction(sim, "einstein_draw_by_discard");

        Assert.That(sim.LastDiscardCount, Is.EqualTo(2));
        Assert.That(HandCount(sim, 0), Is.EqualTo(3), "从抽牌堆立即补抽 2 张");
        Assert.That(characters[0].ComputeDrawCount(), Is.EqualTo(1), "没有后续抽牌修正");
        Assert.That(HandCount(sim, 1), Is.EqualTo(3), "targetFilter.self 只落在施法者身上");
        Assert.That(sim.BlockedMidDrawCount, Is.Zero);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(HandCount(sim, 0), Is.EqualTo(4), "下回合只抽基础 1 张");
    }

    #endregion

    #region 辅助

    private static void ExecuteAction(CombatSimulation sim, string actionId) =>
        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = actionId },
            sim,
            new CombatTargetRef(ECombatSide.Player, 0),
            Self0);

    private static int HandCount(CombatSimulation sim, int characterIndex) =>
        sim.PlayerTeam.Characters[characterIndex].HandSlots.Count(slot => !slot.IsEmpty);

    private static CombatSimulation BuildSim(
        int handSize = 3,
        int seed = 1,
        int deckSize = CombatConstants.HandSlotCount + 5,
        bool includeReadEffect = false)
    {
        var skillActions = new Dictionary<string, SkillActionDto>(StringComparer.Ordinal)
        {
            ["action.discard_two"] = new()
            {
                Id = "action.discard_two",
                Kind = ESkillActionKind.DiscardAndRecord,
                Params = new Dictionary<string, object> { ["count"] = 2, ["random"] = true },
            },
            ["action.discard_three"] = new()
            {
                Id = "action.discard_three",
                Kind = ESkillActionKind.DiscardAndRecord,
                Params = new Dictionary<string, object> { ["count"] = 3, ["random"] = true },
            },
            ["action.discard_plain"] = new()
            {
                Id = "action.discard_plain",
                Kind = ESkillActionKind.Discard,
                Params = new Dictionary<string, object> { ["count"] = 2, ["random"] = true },
            },
            ["action.draw_by_discard"] = new()
            {
                Id = "action.draw_by_discard",
                Kind = ESkillActionKind.ModifyDrawCountByDiscard,
                Params = new Dictionary<string, object>
                {
                    ["targetFilter"] = new Dictionary<string, object> { ["self"] = true },
                },
            },
            ["action.draw_by_discard_per_two"] = new()
            {
                Id = "action.draw_by_discard_per_two",
                Kind = ESkillActionKind.ModifyDrawCountByDiscard,
                Params = new Dictionary<string, object> { ["perCard"] = 2 },
            },
            ["action.chain_discard_then_draw"] = new()
            {
                Id = "action.chain_discard_then_draw",
                Kind = ESkillActionKind.ChainActions,
                ActionRefs =
                [
                    new SkillActionRefDto { ActionId = "action.discard_two" },
                    new SkillActionRefDto { ActionId = "action.draw_by_discard" },
                ],
            },
        };

        var effects = includeReadEffect
            ? new Dictionary<string, EffectDto>(StringComparer.Ordinal)
            {
                ["effect.draw_by_discard"] = new()
                {
                    Id = "effect.draw_by_discard",
                    Kind = EEffectKind.ModifyDrawCountByDiscard,
                    Params = new Dictionary<string, object>
                    {
                        ["targetFilter"] = new Dictionary<string, object> { ["self"] = true },
                    },
                },
            }
            : null;

        var registry = CombatTestHelper.CreateFullRegistry(skillActions: skillActions, effects: effects);

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = 5f,
            [AttributeIds.InitialEnergy] = 5f,
        };
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests(
                $"c{i}",
                attrs,
                Enumerable.Range(0, Math.Max(deckSize, 1))
                    .Select(slot => new CardRuntimeEntry("test.card", $"rt-c{i}-{slot}"))))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(handSize);
            character.RefillAvailableEnergy();
        }

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: seed);
    }

    private static List<ContentDefinitionValidationError> Validate(GameDefinitionStore store) =>
        new ContentDefinitionValidator().Validate(store);

    private static SkillActionDto Action(
        string id,
        ESkillActionKind kind,
        Dictionary<string, object> parameters) => new()
        {
            Id = id,
            Kind = kind,
            Params = parameters,
        };

    private static EffectDto Effect(
        string id,
        EEffectKind kind,
        Dictionary<string, object> parameters) => new()
        {
            Id = id,
            Kind = kind,
            Params = parameters,
        };

    private static Dictionary<string, object> Params(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    /// <summary>直接构造 store，绕开 Rebuild 的「先校验后剔除」，以便断言校验器本身的行为。</summary>
    private static GameDefinitionStore StoreWith(
        IReadOnlyList<EffectDto>? effects = null,
        IReadOnlyList<SkillActionDto>? skillActions = null)
    {
        var store = new GameDefinitionStore();
        if (effects is not null)
        {
            foreach (var effect in effects)
                store.EffectsMutable[effect.Id] = effect;
        }

        if (skillActions is not null)
        {
            foreach (var action in skillActions)
                store.SkillActionsMutable[action.Id] = action;
        }

        return store;
    }

    #endregion
}