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

/// <summary>规格 §2.5 / §6.2：封印——封印角色无法出牌或释放主动技。</summary>
[TestFixture]
public sealed class SealTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

    #region 挂封印立刻封锁

    [Test]
    public void Sealed_character_rejects_play_card_and_active_skill()
    {
        using var sim = BuildWithActiveSkill();
        var character = sim.PlayerTeam.Characters[0];
        ApplySeal(character);

        var play = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));
        var cast = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

        Assert.That(play.Success, Is.False);
        Assert.That(cast.Success, Is.False);
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].IsEmpty, Is.False);
        Assert.That(character.SkillCounter, Is.EqualTo(4), "拒绝主动时不扣 S");
    }

    #endregion

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
                Enumerable.Range(0, 5)
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
}
