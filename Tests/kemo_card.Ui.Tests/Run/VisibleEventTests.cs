using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Adventure;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class VisibleEventTests
{
    #region 奖励结算

    [Test]
    public void Gold_is_claimed_once_and_visible_event_counts_toward_boss()
    {
        var service = Service(EVisibleEventKind.Gold);
        Assert.That(service.OpenNode(0, 2), Is.True);
        Assert.That(service.LeaveNode(), Is.False);
        Assert.That(service.ClaimVisibleReward(), Is.True);
        Assert.That(service.State.SharedGold, Is.EqualTo(150));
        Assert.That(service.ClaimVisibleReward(), Is.False);
        Assert.That(service.OpenNode(0, 2), Is.False);
        Assert.That(service.CanStartBoss, Is.False);
        service.OpenNode(0, 0); service.LeaveNode();
        Assert.That(service.CanStartBoss, Is.True);
    }

    [TestCase(EVisibleEventKind.RolePack, 5)]
    [TestCase(EVisibleEventKind.AllRolesPack, 12)]
    public void Packs_grant_every_card_without_duplicates_or_exclusives(EVisibleEventKind kind, int count)
    {
        var service = Service(kind);
        service.OpenNode(0, 2);
        var cards = service.CurrentVisibleReward!.CardIds.ToArray();
        Assert.That(cards.Length, Is.EqualTo(count));
        Assert.That(cards.Distinct().Count(), Is.EqualTo(count));
        Assert.That(cards.All(id => !service.Store.Cards[id].IsExclusive && service.Store.Cards[id].Rarity != ERarity.Exclusive), Is.True);
        Assert.That(cards.Contains("warrior_upgrade"), Is.False, "reward pools only use the lowest card in an upgrade group");
        if (kind == EVisibleEventKind.RolePack)
            Assert.That(cards.All(id => service.Store.Cards[id].Role == ERole.Warrior), Is.True);
        else
            Assert.That(cards.Select(id => service.Store.Cards[id].Role).Distinct().Count(), Is.EqualTo(12));
        Assert.That(service.ClaimVisibleReward(), Is.True);
        Assert.That(service.State.CardCollection, Is.EquivalentTo(cards));
        Assert.That(service.ClaimVisibleReward(), Is.False);
    }

    [Test]
    public void Relics_offer_three_unowned_candidates_then_grant_only_the_selected_one()
    {
        var service = Service(EVisibleEventKind.RelicChoice);
        service.State.Items.TryAdd(service.Store.Items["relic0"]);
        service.OpenNode(0, 2);
        var candidates = service.CurrentVisibleReward!.RelicIds.ToArray();
        Assert.That(candidates.Length, Is.EqualTo(3));
        Assert.That(candidates.Distinct().Count(), Is.EqualTo(3));
        Assert.That(candidates, Does.Not.Contain("relic0"));
        Assert.That(service.ClaimVisibleReward("relic0"), Is.False);
        Assert.That(service.ClaimVisibleReward("missing"), Is.False);
        Assert.That(service.LeaveNode(), Is.False);
        Assert.That(service.ClaimVisibleReward(candidates[1]), Is.True);
        Assert.That(service.State.Items.Relics, Is.EquivalentTo(new[] { "relic0", candidates[1] }));
        Assert.That(service.ClaimVisibleReward(candidates[0]), Is.False);
    }

    [Test]
    public void Owned_upgradeable_cards_remain_in_pool_and_upgrade_every_deck_in_place()
    {
        var service = Service(EVisibleEventKind.RolePack);
        foreach (var id in new[] { "Warrior_0", "Warrior_1", "Warrior_2" }) service.State.AddCard(id);
        var characters = Enumerable.Range(0, 2).Select(index => new CharacterInstance(new CharacterDto
        { Id = $"hero{index}", Role = ERole.Warrior, Cards = ["Warrior_1", "Warrior_0", "Warrior_2"] })).ToArray();
        foreach (var character in characters) service.State.AddToCharacterPool(character);
        service.OpenNode(0, 2);
        Assert.That(service.CurrentVisibleReward!.CardIds, Does.Contain("Warrior_0"));
        Assert.That(service.CurrentVisibleReward.CardIds, Does.Not.Contain("Warrior_1").And.Not.Contain("Warrior_2"));
        Assert.That(service.ClaimVisibleReward(), Is.True);
        Assert.That(service.State.CardCollection, Does.Contain("warrior_upgrade").And.Not.Contain("Warrior_0"));
        foreach (var character in characters)
        {
            Assert.That(character.GetCurrentDeck()!.CardIds, Is.EqualTo(new[] { "Warrior_1", "warrior_upgrade", "Warrior_2" }));
            var rewards = new RunCardPackRewards(new RunController(service.State), service.Store);
            Assert.That(rewards.BuildableCards(character), Does.Not.Contain("Warrior_0"));
            Assert.That(rewards.EligibleCards().Select(card => card.Id), Does.Not.Contain("Warrior_0"));
        }
    }

    [Test]
    public void Cards_that_become_ineligible_after_preparation_do_not_partially_grant_a_pack()
    {
        var service = Service(EVisibleEventKind.RolePack);
        service.OpenNode(0, 2);
        var blocked = service.CurrentVisibleReward!.CardIds.First(id => id != "Warrior_0");
        service.State.AddCard(blocked);
        Assert.That(service.ClaimVisibleReward(), Is.False);
        Assert.That(service.State.CardCollection, Is.EquivalentTo(new[] { blocked }));
        Assert.That(service.State.Map!.NodeOpen, Is.True);
        Assert.That(service.CanSkipVisibleReward, Is.True);
    }

    [TestCase(EVisibleEventKind.RolePack)]
    [TestCase(EVisibleEventKind.AllRolesPack)]
    [TestCase(EVisibleEventKind.RelicChoice)]
    public void Insufficient_pools_allow_explicit_skip_without_partial_rewards(EVisibleEventKind kind)
    {
        var service = Service(kind, completePools: false);
        service.OpenNode(0, 2);
        Assert.That(service.CanSkipVisibleReward, Is.True);
        Assert.That(service.ClaimVisibleReward(), Is.False);
        Assert.That(service.State.CardCollection, Is.Empty);
        Assert.That(service.State.Items.Relics, Is.Empty);
        Assert.That(service.LeaveNode(), Is.True);
        Assert.That(service.State.Map!.Nodes.Single(node => node.Step == 2).Completed, Is.True);
    }

    #endregion

    #region 生成与存档

    [Test]
    public void Reward_contents_survive_serialization_and_snapshots_are_independent()
    {
        var service = Service(EVisibleEventKind.RelicChoice);
        service.OpenNode(0, 2);
        var snapshot = service.State.ToDto();
        var ids = service.CurrentVisibleReward!.RelicIds.ToArray();
        var restored = new RunMod();
        restored.RestoreFrom(JsonSerializer.Deserialize<RunDto>(JsonSerializer.Serialize(snapshot))!.Normalize());
        var reloaded = new RunAdventureService(new RunController(restored), Definitions());
        reloaded.EnsureMap();
        Assert.That(reloaded.CurrentVisibleReward!.RelicIds, Is.EqualTo(ids));
        service.CurrentVisibleReward.RelicIds.Clear();
        Assert.That(snapshot.Map!.CurrentNode!.VisibleReward!.RelicIds, Is.EqualTo(ids));
        Assert.That(reloaded.ClaimVisibleReward(ids[0]), Is.True);
    }

    [Test]
    public void Four_reward_kinds_and_role_are_fixed_on_the_map_and_can_repeat_in_later_rings()
    {
        var service = new RunAdventureService(new RunController(new RunMod { StoryId = "empty", RunSeed = 123 }), Definitions());
        var kinds = new HashSet<EVisibleEventKind>();
        for (var ring = 1; ring <= 8; ring++)
        {
            service.State.CurrentRing = ring;
            service.EnsureMap();
            foreach (var branch in service.State.Map!.Nodes.GroupBy(node => node.Branch))
            {
                Assert.That(branch.Count(node => node.Kind == ERunNodeKind.Event), Is.EqualTo(1));
                var reward = branch.Single(node => node.Kind == ERunNodeKind.VisibleEvent).VisibleReward!;
                kinds.Add(reward.Kind);
                Assert.That(reward.Prepared, Is.False, "only the reward kind and role are revealed before entering");
                if (reward.Kind == EVisibleEventKind.RolePack) Assert.That(reward.Role, Is.Not.EqualTo(ERole.None));
            }
        }
        Assert.That(kinds, Is.EquivalentTo(Enum.GetValues<EVisibleEventKind>()));
        Assert.That(service.State.ReservedEventIds, Is.Empty, "visible rewards are not subject to random event deduplication");
    }

    [Test]
    public void Old_pending_and_completed_events_are_preserved_but_unstarted_second_slots_upgrade()
    {
        var service = Service(EVisibleEventKind.Gold);
        var map = service.State.Map!;
        map.Nodes[2] = new RunMapNode { Branch = 0, Step = 2, Kind = ERunNodeKind.Event, EventId = "legacy" };
        map.NodeOpen = true; map.Position = 2;
        service.EnsureMap();
        Assert.That(map.Nodes[2].EventId, Is.EqualTo("legacy"));
        map.NodeOpen = false; map.Nodes[2].Completed = true;
        service.EnsureMap();
        Assert.That(map.Nodes[2].Kind, Is.EqualTo(ERunNodeKind.Event));
        map.Nodes[2].Completed = false;
        service.EnsureMap();
        Assert.That(map.Nodes[2].Kind, Is.EqualTo(ERunNodeKind.VisibleEvent));
        Assert.That(map.Nodes[2].EventId, Is.Null);
    }

    #endregion

    private static RunAdventureService Service(EVisibleEventKind kind, bool completePools = true) =>
        new(new RunController(new RunMod
        {
            StoryId = "empty", RunSeed = 51, Map = new RunMapState
            {
                Ring = 1, SelectedBranch = 0, Nodes = [
                new() { Branch = 0, Step = 0, Kind = ERunNodeKind.Event },
                new() { Branch = 0, Step = 1, Kind = ERunNodeKind.Shop },
                new() { Branch = 0, Step = 2, Kind = ERunNodeKind.VisibleEvent, VisibleReward = new() { Kind = kind, Role = ERole.Warrior } },
            ]
            }
        }), Definitions(completePools));

    private static GameDefinitionRegistry Definitions(bool completePools = true)
    {
        var cards = new Dictionary<string, CardDto>();
        if (completePools)
            foreach (var role in Enum.GetValues<ERole>().Where(role => role != ERole.None))
                for (var index = 0; index < 7; index++)
                {
                    var id = $"{role}_{index}";
                    cards[id] = new CardDto { Id = id, DisplayNameId = id, Role = role, CardGroupId = role == ERole.Warrior && index == 0 ? "warrior" : null };
                }
        cards["exclusive_flag"] = new() { Id = "exclusive_flag", Role = ERole.Warrior, IsExclusive = true };
        cards["exclusive_rarity"] = new() { Id = "exclusive_rarity", Role = ERole.Warrior, Rarity = ERarity.Exclusive };
        if (completePools) cards["warrior_upgrade"] = new() { Id = "warrior_upgrade", Role = ERole.Warrior, CardGroupId = "warrior", UpgradeTier = 1 };
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", ModDefinitionsBundle.Empty with {
            Cards = cards,
            Items = completePools ? Enumerable.Range(0, 5).ToDictionary(i => $"relic{i}", i =>
                new ItemDto { Id = $"relic{i}", Kind = EItemKind.Relic }) : [],
        })], out var report);
        Assert.That(report.RemovedValidationErrors, Is.Empty);
        return registry;
    }
}