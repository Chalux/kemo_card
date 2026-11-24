using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class TeamCardPilesTests
{
    [Test]
    public void Factory_merges_all_loadouts_and_shuffles_the_combined_order_reproducibly()
    {
        static int[] OpeningOrder(int seed)
        {
            using var sim = CombatSimulationTestBuilder.FullBattle(seed);
            var deck = sim.PlayerTeam.CardPiles.DrawPile;
            Assert.That(deck.Select(c => c.CharacterIndex), Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(deck.Select(c => c.RuntimeInstanceId).Distinct().Count(), Is.EqualTo(3));
            return deck.Select(c => c.CharacterIndex).ToArray();
        }

        Assert.That(OpeningOrder(7), Is.EqualTo(OpeningOrder(7)));
        Assert.That(Enumerable.Range(1, 8).Select(seed => string.Join(",", OpeningOrder(seed))).Distinct().Count(),
            Is.GreaterThan(1));
    }

    [Test]
    public void Drawing_the_team_top_card_preserves_its_owner_for_animation_and_payment()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5, handSize: 0);
        var piles = sim.PlayerTeam.CardPiles;
        var top = piles.DrawPile[^1];
        var characters = sim.PlayerTeam.Characters;
        var owner = characters[top.CharacterIndex];
        var other = characters[(top.CharacterIndex + 1) % characters.Count];
        var originalCount = piles.DrawPile.Count;

        Assert.That(PresentationEmitter.DrawTeamAndEmit(sim, 1), Is.EqualTo(1));
        var evt = sim.Presentation.Drain().OfType<CardsDrawnEvent>().Single();
        Assert.That(evt.CharacterIndex, Is.EqualTo(top.CharacterIndex));
        Assert.That(evt.Cards.Single().RuntimeInstanceId, Is.EqualTo(top.RuntimeInstanceId));
        Assert.That(piles.DrawPile.Count, Is.EqualTo(originalCount - 1));
        var command = new PlayCardCommand(top.CharacterIndex, evt.Cards[0].SlotIndex,
            [new CombatTargetRef(ECombatSide.Enemy, 0)], top.RuntimeInstanceId);
        owner.TryConsumeAvailableEnergy(5);
        Assert.That(sim.TryApply(command).Success, Is.False, "teammates cannot cover a shortfall");
        Assert.That(other.AvailableEnergy, Is.EqualTo(5));
        Assert.That(piles.Graveyard, Is.Empty);
        owner.GainAvailableEnergy(3);
        Assert.That(sim.TryApply(command).Success, Is.True);
        Assert.That(owner.AvailableEnergy, Is.EqualTo(1));
        Assert.That(other.AvailableEnergy, Is.EqualTo(5));
        Assert.That(piles.Graveyard.Single(), Is.EqualTo(top));
    }

    [Test]
    public void One_owner_can_hold_all_ten_cards_and_discard_to_make_room_for_a_teammate()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(handSize: 0, characterCount: 2, deckSize: 10);
        var team = sim.PlayerTeam;
        var piles = team.CardPiles;
        Assert.That(PresentationEmitter.DrawAndEmit(sim, 0, int.MaxValue), Is.EqualTo(10));
        var remaining = piles.DrawPile.ToArray();
        Assert.That(PresentationEmitter.DrawAndEmit(sim, 1, 1), Is.Zero);
        Assert.That(PresentationEmitter.DrawTeamAndEmit(sim, int.MaxValue), Is.Zero);
        Assert.That(piles.DrawPile, Is.EqualTo(remaining));

        var lastCard = team.Characters[0].HandSlots[9].RuntimeInstanceId!;
        Assert.That(team.Characters[0].MoveHandCardToGraveyard(lastCard), Is.True);
        Assert.That(PresentationEmitter.DrawAndEmit(sim, 1, int.MaxValue), Is.EqualTo(1));
        Assert.That(team.Characters[1].HandSlots[0].RuntimeInstanceId, Is.EqualTo(remaining[^1].RuntimeInstanceId));
        Assert.That(piles.HandCount, Is.EqualTo(10));
    }

    [Test]
    public void Assembling_a_team_with_predealt_hands_returns_excess_instances_to_the_deck()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(handSize: 5, characterCount: 3);
        var team = sim.PlayerTeam;
        Assert.That(team.CardPiles.HandCount, Is.EqualTo(10));
        Assert.That(team.CardPiles.DrawPile, Has.Count.EqualTo(5));
        for (var i = 0; i < team.Characters.Count; i++)
        {
            var hand = team.Characters[i].HandSlots.Where(s => !s.IsEmpty).Select(s => s.RuntimeInstanceId);
            var deck = team.CardPiles.DrawPile.Where(c => c.CharacterIndex == i).Select(c => c.RuntimeInstanceId);
            Assert.That(hand.Concat(deck), Is.EquivalentTo(Enumerable.Range(0, 5).Select(j => $"rt-c{i}-{j}")));
        }
    }

    [Test]
    public void Targeted_draw_cannot_force_a_reshuffle_while_a_teammate_can_draw_the_remaining_cards()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(handSize: 0);
        var team = sim.PlayerTeam;
        var owner = team.Characters[0];
        Assert.That(PresentationEmitter.DrawAndEmit(sim, 0, 5), Is.EqualTo(5));
        var id = owner.HandSlots[0].RuntimeInstanceId!;
        Assert.That(owner.MoveHandCardToGraveyard(id), Is.True);
        var order = team.CardPiles.DrawPile.ToArray();
        Assert.That(PresentationEmitter.DrawAndEmit(sim, 0, 1), Is.Zero);
        Assert.That(team.CardPiles.DrawPile, Is.EqualTo(order));
        Assert.That(team.CardPiles.Graveyard.Single().RuntimeInstanceId, Is.EqualTo(id));
        Assert.That(PresentationEmitter.DrawTeamAndEmit(sim, 1), Is.EqualTo(1));
        Assert.That(team.Characters[order[^1].CharacterIndex].HandSlots[0].RuntimeInstanceId, Is.EqualTo(order[^1].RuntimeInstanceId));
    }

    [Test]
    public void A_single_reshuffle_recycles_discards_from_two_owners_and_preserves_instance_ids()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(handSize: 5, characterCount: 2);
        var team = sim.PlayerTeam;
        var discarded = new[] { 0, 1 }.Select(i =>
        {
            var owner = team.Characters[i];
            var id = owner.HandSlots[0].RuntimeInstanceId!;
            Assert.That(owner.MoveHandCardToGraveyard(id), Is.True);
            return (i, id);
        }).ToArray();
        Assert.That(team.CardPiles.DrawPile, Is.Empty);
        Assert.That(team.CardPiles.Graveyard, Has.Count.EqualTo(2));
        Assert.That(PresentationEmitter.DrawTeamAndEmit(sim, 1), Is.EqualTo(1));
        Assert.That(team.CardPiles.Graveyard, Is.Empty);
        Assert.That(team.CardPiles.DrawPile, Has.Count.EqualTo(1));
        Assert.That(PresentationEmitter.DrawTeamAndEmit(sim, 1), Is.EqualTo(1));
        foreach (var (index, id) in discarded)
            Assert.That(team.Characters[index].HandSlots[0].RuntimeInstanceId, Is.EqualTo(id));
    }
}