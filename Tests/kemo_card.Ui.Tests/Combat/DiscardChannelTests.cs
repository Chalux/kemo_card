using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>弃牌范围、选牌和抽牌修正。</summary>
[TestFixture]
public sealed class DiscardChannelTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Self0 = [new CombatTargetRef(ECombatSide.Player, 0)];

    [Test]
    public void ModifyDrawCount_still_adds_draw_modifiers()
    {
        using var sim = BuildDiscardSim(includeModifyDraw: true);
        var character = sim.PlayerTeam.Characters[0];

        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "action.modify_draw" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 0),
            Self0);

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "基准 1 + 最大增益 2");
        Assert.That(sim.BlockedMidDrawCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Selected_discard_accepts_another_owner_only_if_the_effect_targets_that_owner(bool shared)
    {
        using var sim = BuildDiscardSim();
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var ally = new CombatTargetRef(ECombatSide.Player, 1);
        var owner = sim.PlayerTeam.Characters[1];
        var card = new HandCardChoice(1, owner.HandSlots[0].RuntimeInstanceId!);
        sim.Definitions.Store.SkillActionsMutable["action.chosen"] = new()
        {
            Id = "action.chosen", Kind = ESkillActionKind.Discard,
            Params = new Dictionary<string, object> { ["count"] = 1 }
        };
        var action = new SkillActionRefDto { ActionId = "action.chosen" };
        var skill = new SkillDto { Id = "skill.chosen", ActionRefs = [action] };
        var targets = new[] { shared ? CombatTargetRef.PlayerTeam : ally };
        Assert.That(DiscardSelection.BuildPlan(sim, skill, 0, targets)!.TryAssign([card], out _), Is.True);
        Assert.That(DiscardSelection.BuildPlan(sim, skill, 0, Self0)!.TryAssign([card], out _), Is.False);

        sim.SetDiscardChannel(EDiscardChannel.ActiveSkill);
        sim.SelectedDiscardCards = [card];
        sim.EffectExecutor.ExecuteSkillActionRef(action, sim, source, targets);

        Assert.That(owner.Graveyard.Single().RuntimeInstanceId, Is.EqualTo(card.RuntimeInstanceId));
        Assert.That(sim.PlayerTeam.CardPiles.Graveyard.Single().CharacterIndex, Is.EqualTo(card.CharacterIndex));
    }

    [Test]
    public void Per_owner_quotas_cannot_be_met_by_cards_from_only_one_owner()
    {
        using var sim = BuildDiscardSim();
        sim.Definitions.Store.SkillActionsMutable["action.chosen"] = new()
        {
            Id = "action.chosen", Kind = ESkillActionKind.Discard
        };
        var skill = new SkillDto { Id = "skill.chosen", ActionRefs = [new() { ActionId = "action.chosen" }] };
        CombatTargetRef[] targets = [new(ECombatSide.Player, 0), new(ECombatSide.Player, 1)];
        var plan = DiscardSelection.BuildPlan(sim, skill, 0, targets)!;
        var first = new HandCardChoice(0, sim.PlayerTeam.Characters[0].HandSlots[0].RuntimeInstanceId!);
        var sameOwner = new HandCardChoice(0, sim.PlayerTeam.Characters[0].HandSlots[1].RuntimeInstanceId!);
        var otherOwner = new HandCardChoice(1, sim.PlayerTeam.Characters[1].HandSlots[0].RuntimeInstanceId!);

        Assert.That(plan.Limits, Is.EqualTo(new DiscardSelection(2, 2)));
        Assert.That(plan.TryAssign([first, sameOwner], out _), Is.False);
        Assert.That(plan.TryAssign([otherOwner, first], out var assigned), Is.True);
        Assert.That(assigned[0].Cards, Is.EqualTo(new[] { first }));
        Assert.That(assigned[1].Cards, Is.EqualTo(new[] { otherOwner }));
    }

    [Test]
    public void Random_team_discard_draws_one_card_from_the_combined_pool()
    {
        using var sim = BuildDiscardSim(includeDiscardAction: true);
        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "action.discard" }, sim, Self0[0], [CombatTargetRef.PlayerTeam]);

        Assert.That(sim.PlayerTeam.CardPiles.Graveyard, Has.Count.EqualTo(1));
    }

    [Test]
    public void Soft_fail_does_not_stop_remaining_actions_in_chain()
    {
        using var sim = BuildDiscardSim(includeDiscardThenGain: true, handSize: 0);
        var character = sim.PlayerTeam.Characters[0];
        character.RefillAvailableEnergy();
        var energyBefore = character.AvailableEnergy;

        sim.SetDiscardChannel(EDiscardChannel.Other);
        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "action.discard_then_gain" },
            sim,
            new CombatTargetRef(ECombatSide.Player, 0),
            Self0);

        Assert.That(character.Graveyard.Count, Is.Zero);
        Assert.That(character.AvailableEnergy, Is.EqualTo(energyBefore + 2), "弃牌软失败后后续动作仍执行");
    }

    private static CombatSimulation BuildDiscardSim(
        bool includeDiscardAction = false,
        bool includeModifyDraw = false,
        bool includeDiscardThenGain = false,
        int seed = 1,
        int handSize = 3)
    {
        var skillActions = new Dictionary<string, SkillActionDto>(StringComparer.Ordinal);
        if (includeDiscardAction)
        {
            skillActions["action.discard"] = new()
            {
                Id = "action.discard",
                Kind = ESkillActionKind.Discard,
                Params = new Dictionary<string, object> { ["count"] = 1, ["random"] = true },
            };
        }

        if (includeModifyDraw)
        {
            skillActions["action.modify_draw"] = new()
            {
                Id = "action.modify_draw",
                Kind = ESkillActionKind.ModifyDrawCount,
                Params = new Dictionary<string, object> { ["amount"] = 2 },
            };
        }

        if (includeDiscardThenGain)
        {
            skillActions["action.discard"] = new()
            {
                Id = "action.discard",
                Kind = ESkillActionKind.Discard,
                Params = new Dictionary<string, object> { ["count"] = 1, ["random"] = true },
            };
            skillActions["action.gain"] = new()
            {
                Id = "action.gain",
                Kind = ESkillActionKind.GainResource,
                Params = new Dictionary<string, object> { ["amount"] = 2 },
            };
            skillActions["action.discard_then_gain"] = new()
            {
                Id = "action.discard_then_gain",
                Kind = ESkillActionKind.ChainActions,
                ActionRefs =
                [
                    new SkillActionRefDto { ActionId = "action.discard" },
                    new SkillActionRefDto { ActionId = "action.gain" },
                ],
            };
        }

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
            },
            skillActions: skillActions);

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
                Enumerable.Range(0, Math.Max(handSize, 1))
                    .Select(slot => new CardRuntimeEntry(
                        CombatSimulationTestBuilder.MarkableCardId,
                        $"rt-c{i}-{slot}"))))
            .ToArray();
        foreach (var character in characters)
        {
            if (handSize > 0)
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

}