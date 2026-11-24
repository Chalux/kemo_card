using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class ImmediateCardPlayTests
{
    private static readonly CombatTargetRef[] Enemy = [new(ECombatSide.Enemy, 0)];

    [Test]
    public void Card_pays_the_owners_energy_and_resolves_before_ending_the_turn()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
        var owner = sim.PlayerTeam.Characters[1];
        var runtimeId = owner.HandSlots[0].RuntimeInstanceId;
        var enemy = sim.EnemyTeam.Enemies[0];

        var result = sim.TryApply(new PlayCardCommand(1, 0, Enemy, runtimeId));

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(owner.AvailableEnergy, Is.EqualTo(3));
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(5));
        Assert.That(enemy.CurrentHp, Is.EqualTo(99));
        Assert.That(owner.HandSlots[0].IsEmpty, Is.True);
        Assert.That(owner.Graveyard.Single().RuntimeInstanceId, Is.EqualTo(runtimeId));
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
    }

    [TestCase(ECostType.Health)]
    [TestCase(ECostType.Gold)]
    [TestCase(ECostType.Discard)]
    [TestCase(ECostType.X)]
    public void Unsupported_cost_leaves_the_card_in_hand(ECostType costType)
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(costType: costType, cost: 1, energy: 5);
        var owner = sim.PlayerTeam.Characters[0];

        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy)).Success, Is.False);
        Assert.That(owner.HandSlots[0].IsEmpty, Is.False);
        Assert.That(owner.Graveyard, Is.Empty);
        Assert.That(owner.AvailableEnergy, Is.EqualTo(5));
    }

    [Test]
    public void Insufficient_energy_does_not_move_the_card()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 3, energy: 2);
        var owner = sim.PlayerTeam.Characters[0];

        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy)).Success, Is.False);
        Assert.That(owner.HandSlots[0].IsEmpty, Is.False);
        Assert.That(owner.AvailableEnergy, Is.EqualTo(2));
    }

    [Test]
    public void Draw_during_resolution_can_fill_the_opening_but_cannot_reshuffle_the_resolving_card()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5, handSize: 5, characterCount: 2);
        var owner = sim.PlayerTeam.Characters[0];
        var sourceCardId = owner.HandSlots[0].RuntimeInstanceId;
        var earlierCardId = owner.HandSlots[1].RuntimeInstanceId!;
        Assert.That(owner.MoveHandCardToGraveyard(earlierCardId), Is.True);
        var card = sim.Definitions.Store.Cards[CombatSimulationTestBuilder.MarkableCardId];
        var skill = sim.Definitions.Store.Skills[card.SkillRefs[0].SkillId];
        var effectId = skill.EffectRefs[0].EffectId;
        sim.Definitions.Store.EffectsMutable[effectId] = new EffectDto
        {
            Id = effectId,
            Kind = EEffectKind.Draw,
            Params = new Dictionary<string, object> { ["count"] = 2 },
        };

        var played = sim.TryApply(new PlayCardCommand(0, 0, Enemy, sourceCardId));

        Assert.That(played.Success, Is.True, played.Error);
        Assert.That(owner.HandSlots[0].RuntimeInstanceId, Is.EqualTo(earlierCardId));
        Assert.That(owner.HandSlots[1].IsEmpty, Is.True);
        Assert.That(owner.Graveyard.Single().RuntimeInstanceId, Is.EqualTo(sourceCardId));
        var events = sim.Presentation.Drain().ToList();
        var discarded = events.OfType<CardDiscardedEvent>().Single();
        var drawn = events.OfType<CardsDrawnEvent>().Single();
        Assert.That(discarded.RuntimeInstanceId, Is.EqualTo(sourceCardId));
        Assert.That(drawn.Cards.Single().RuntimeInstanceId, Is.EqualTo(earlierCardId));
        Assert.That(drawn.Cards.Single().SlotIndex, Is.EqualTo(discarded.SlotIndex));
        Assert.That(events.IndexOf(discarded), Is.LessThan(events.IndexOf(drawn)));
        var energy = owner.AvailableEnergy;
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy, sourceCardId)).Success, Is.False);
        Assert.That(owner.AvailableEnergy, Is.EqualTo(energy));
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy, earlierCardId)).Success, Is.True);
        Assert.That(owner.HandSlots[0].RuntimeInstanceId, Is.EqualTo(sourceCardId), "the next card can reshuffle again during the same turn");
    }

    [Test]
    public void End_turn_is_available_with_no_cards_played()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        var turn = sim.TurnNumber;
        var result = sim.TryApply(new EndTurnCommand());

        Assert.That(result.Success, Is.True, result.Error);
        MarblePhaseTests.ShootQueuedBalls(sim, 1.35f);
        sim.AdvanceAutomaticPhases();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.TurnNumber, Is.EqualTo(turn + 1));
        Assert.That(sim.NormalAttacks.LastMarbleSettlement?.TotalCount, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.CardPiles.HandCount, Is.EqualTo(10));
    }
}
