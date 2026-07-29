using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>规格 §2.5 / §6.2：封印——清标记退费、视作已行动、禁主动；资源管线仍跑。</summary>
[TestFixture]
public sealed class SealTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

    #region 挂封印立刻封锁

    [Test]
    public void EnforceSeal_clears_marks_refunds_paid_and_marks_acted()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy0)).Success, Is.True);
        Assert.That(character.AvailableEnergy, Is.EqualTo(1));
        Assert.That(sim.CardQueue.Count, Is.EqualTo(2));

        ApplySeal(character);
        CombatStateMachine.EnforceSeal(sim, 0);

        Assert.That(character.IsSealed, Is.True);
        Assert.That(character.HasActed, Is.True);
        Assert.That(sim.CardQueue.Count, Is.Zero);
        Assert.That(character.HandSlots.Count(s => s.IsMarked), Is.Zero);
        Assert.That(character.AvailableEnergy, Is.EqualTo(5), "全部 paid 退回可用能量");
        Assert.That(character.HandSlots[0].IsEmpty, Is.False, "牌仍留在手牌");
    }

    [Test]
    public void Sealed_character_rejects_play_card_and_active_skill()
    {
        using var sim = BuildWithActiveSkill();
        var character = sim.PlayerTeam.Characters[0];
        ApplySeal(character);
        CombatStateMachine.EnforceSeal(sim, 0);

        var play = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));
        var cast = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

        Assert.That(play.Success, Is.False);
        Assert.That(cast.Success, Is.False);
        Assert.That(sim.CardQueue.Count, Is.Zero);
        Assert.That(character.SkillCounter, Is.EqualTo(4), "拒绝主动时不扣 S");
    }

    [Test]
    public void Unconfirm_is_ineffective_while_sealed()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        var character = sim.PlayerTeam.Characters[0];
        ApplySeal(character);
        CombatStateMachine.EnforceSeal(sim, 0);
        Assert.That(character.HasActed, Is.True);

        var result = sim.TryApply(new UnconfirmCharacterCommand(0));

        Assert.That(result.Success, Is.False);
        Assert.That(character.HasActed, Is.True, "封印中取消确认无效");
    }

    #endregion

    #region 阶段管线与中途封印

    [Test]
    public void Player_phase_pipeline_still_grants_resources_then_enforces_seal()
    {
        using var sim = BuildPipelineSim(deckSize: 3, initialEnergy: 2, maxEnergy: 5, skillCounterCap: 3);
        var character = sim.PlayerTeam.Characters[0];
        ApplySeal(character);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(character.CurrentEnergy, Is.EqualTo(3), "封印角色当前能量仍 +1");
        Assert.That(character.AvailableEnergy, Is.EqualTo(3), "可用能量仍覆盖灌入");
        Assert.That(character.SkillCounter, Is.EqualTo(1), "S 仍 +1");
        Assert.That(character.HandSlots.Count(s => !s.IsEmpty), Is.EqualTo(1), "公式抽牌仍跑");
        Assert.That(character.HasActed, Is.True, "管线末尾套用行动封锁");
    }

    [Test]
    public void Mid_phase_seal_does_not_roll_back_already_gained_resources()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        character.GainAvailableEnergy(3);
        character.GainSkillCounter(2);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        var energyBeforeSeal = character.AvailableEnergy;
        var counterBeforeSeal = character.SkillCounter;
        var handBeforeSeal = character.HandSlots.Count(s => !s.IsEmpty);

        ApplySeal(character);
        CombatStateMachine.EnforceSeal(sim, 0);

        Assert.That(character.AvailableEnergy, Is.EqualTo(energyBeforeSeal + 1), "退 paid，不收回本阶段额外获得的可用能量");
        Assert.That(character.SkillCounter, Is.EqualTo(counterBeforeSeal), "不回滚本阶段已获得的 S");
        Assert.That(character.HandSlots.Count(s => !s.IsEmpty), Is.EqualTo(handBeforeSeal), "不回滚本阶段已抽的牌");
    }

    [Test]
    public void Successful_player_command_scans_newly_sealed_characters()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
        var sealedCharacter = sim.PlayerTeam.Characters[1];
        Assert.That(sim.TryApply(new PlayCardCommand(1, 0, Enemy0)).Success, Is.True);
        ApplySeal(sealedCharacter);

        // 任意其它角色成功出牌后应扫描到角色 1 的新封印。
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);

        Assert.That(sealedCharacter.HasActed, Is.True);
        Assert.That(sim.CardQueue.PeekAllOrdered().All(e => e.CharacterIndex != 1), Is.True);
        Assert.That(sealedCharacter.HandSlots.Count(s => s.IsMarked), Is.Zero);
    }

    #endregion

    #region 四人确认判定

    [Test]
    public void Three_confirmed_plus_one_sealed_enters_card_execution()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        ApplySeal(sim.PlayerTeam.Characters[3]);
        CombatStateMachine.EnforceSeal(sim, 3);

        Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);
        Assert.That(sim.TryApply(new ConfirmCharacterCommand(1)).Success, Is.True);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.TryApply(new ConfirmCharacterCommand(2)).Success, Is.True);

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    #endregion

    #region 辅助

    private static void ApplySeal(CharacterBattleInstance character)
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.test.seal",
            DurationPolicy = EDurationPolicy.Infinite,
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
            GrantedTags = [CombatConstants.SealedTag],
        };
        var result = character.Asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: character.Asc));
        Assert.That(result.Success, Is.True);
        Assert.That(character.Asc.Tags.HasTag(CombatConstants.SealedTag), Is.True);
    }

    private static CombatSimulation BuildWithActiveSkill()
    {
        var chain = new[]
        {
            new ActiveSkillChainEntryDto { SkillId = "skill.seal_probe", Cooldown = 4 },
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.MarkableCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.MarkableCardId,
                    CostType = ECostType.Energy,
                    Cost = 1,
                    Priority = 100,
                    TargetSide = ETargetSide.Enemy,
                    TargetScope = ETargetScope.Single,
                    TargetCount = 1,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.markable" }],
                },
            },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.markable"] = new() { Id = "skill.markable" },
                ["skill.seal_probe"] = new()
                {
                    Id = "skill.seal_probe",
                    TargetOverride = new TargetSpecDto
                    {
                        Side = ETargetSide.Enemy,
                        Scope = ETargetScope.Single,
                        TargetCount = 1,
                    },
                },
            });

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
                Enumerable.Range(0, CombatConstants.HandSlotCount)
                    .Select(slot => new CardRuntimeEntry(
                        CombatSimulationTestBuilder.MarkableCardId,
                        $"rt-c{i}-{slot}")),
                activeSkillChain: chain))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(3);
            character.RefillAvailableEnergy();
            for (var i = 0; i < 4; i++)
                character.TickSkillCounter();
        }

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player);
    }

    private static CombatSimulation BuildPipelineSim(
        int deckSize,
        int initialEnergy,
        int maxEnergy,
        int skillCounterCap)
    {
        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = maxEnergy,
            [AttributeIds.InitialEnergy] = initialEnergy,
        };
        var drawPile = Enumerable.Range(0, deckSize)
            .Select(i => new CardRuntimeEntry("card.x", $"rt-{i}"))
            .ToArray();
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests(
                $"c{i}",
                attrs,
                i == 0 ? drawPile : [],
                skillCounterCap: skillCounterCap))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 10)]),
            new CombatRuleEngine([]),
            CombatTestHelper.CreateFullRegistry(),
            initialPhase: ECombatPhase.Player,
            runSeed: 7);
    }

    #endregion
}