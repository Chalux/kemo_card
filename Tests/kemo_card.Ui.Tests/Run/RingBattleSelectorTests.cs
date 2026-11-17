using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Run;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RingBattleSelectorTests
{
    [Test]
    public void Shipped_brothers_are_only_in_roguelike_ring_one()
    {
        var registry = BaseGameContent.BuildRegistry();
        var story = registry.Store.Stories["base-rogue"];
        Assert.That(story.RingBattlePools.Where(pool => pool.Battles.Any(entry => entry.BattleId == "awakening_brothers"))
            .Select(pool => pool.Ring), Is.EqualTo(new[] { 1 }));
        Assert.That(RingBattleSelector.Select(registry, "base-rogue", 1, 42).Id, Is.EqualTo("awakening_brothers"));
        Assert.That(RingBattleSelector.Select(registry, "base-rogue", 2, 42).Id, Is.EqualTo("forest_black_bears"));
        Assert.That(story.RingBattlePools.Where(pool => pool.Battles.Any(entry => entry.BattleId == "forest_black_bears"))
            .Select(pool => pool.Ring), Is.EqualTo(new[] { 2 }));
        Assert.That(RingBattleSelector.Select(registry, "base-rogue", 3, 42).Id, Is.EqualTo("undead_discovery"));
        Assert.That(story.RingBattlePools.Where(pool => pool.Battles.Any(entry => entry.BattleId == "undead_discovery"))
            .Select(pool => pool.Ring), Is.EqualTo(new[] { 3 }));
        Assert.Throws<InvalidOperationException>(() => RingBattleSelector.Select(registry, "base-rogue", 4, 42));
    }

    [Test]
    public void Weighted_pool_is_reproducible_independent_of_content_order_and_excludes_other_rings()
    {
        var registry = BaseGameContent.BuildRegistry();
        var entries = new List<WeightedBattleRefDto> {
            new() { BattleId = "awakening_brothers", Weight = 1 }, new() { BattleId = "forest_ambush", Weight = 3 } };
        registry.Store.StoriesMutable["pool-test"] = new()
        {
            Id = "pool-test", RingBattlePools = [
            new() { Ring = 1, Battles = entries },
            new() { Ring = 2, Battles = [new() { BattleId = "forest_ambush" }] } ]
        };
        var choices = Enumerable.Range(1, 200).Select(seed => RingBattleSelector.Select(registry, "pool-test", 1, seed).Id).ToArray();
        entries.Reverse();
        var repeated = Enumerable.Range(1, 200).Select(seed => RingBattleSelector.Select(registry, "pool-test", 1, seed).Id).ToArray();
        Assert.That(repeated, Is.EqualTo(choices));
        Assert.That(choices.Distinct(), Is.EquivalentTo(new[] { "awakening_brothers", "forest_ambush" }));
        Assert.That(choices.Count(id => id == "forest_ambush"), Is.InRange(120, 180));
        for (var seed = 1; seed < 20; seed++)
            Assert.That(RingBattleSelector.Select(registry, "pool-test", 2, seed).Id, Is.EqualTo("forest_ambush"));
    }

    [Test]
    public void Run_uses_its_pool_records_the_actual_battle_and_rejects_a_second_win_in_one_ring_after_loading()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        registry.Store.StoriesMutable["pool-test"] = new()
        {
            Id = "pool-test", RingBattlePools = [
            new() { Ring = 1, Battles = [new() { BattleId = RunTestHelper.TestBattleId }] },
            new() { Ring = 2, Battles = [new() { BattleId = RunTestHelper.TestBattleId }] } ]
        };
        var model = new RunMod { StoryId = "pool-test", CurrentRing = 1, Phase = ERunPhase.Reward };
        for (var index = 0; index < 4; index++)
        {
            var character = RunTestHelper.CreateCharacterWithHp(index);
            model.AddToCharacterPool(character);
            model.PlayerStates[index].SetActiveCharacter(character);
        }
        model.AddPlayerController(new PlayerController("local", "Player", true));
        for (var index = 0; index < 4; index++)
            model.AssignSlotInternal(index, "local");
        var controller = new RunController(model);
        controller.StartBattle(registry, new HostRng(42, "run"), 42);
        controller.EndBattle(true);
        Assert.That(model.BattleHistory.Single().BattleId, Is.EqualTo(RunTestHelper.TestBattleId));
        Assert.That(model.BattleHistory.Single().Ring, Is.EqualTo(1));
        controller.LoadRun(model.ToDto());
        model.Phase = ERunPhase.Reward;
        Assert.Throws<InvalidOperationException>(() => controller.StartBattle(registry, new HostRng(42, "run"), 42));
        model.CurrentRing = 2;
        Assert.DoesNotThrow(() => controller.StartBattle(registry, new HostRng(42, "run"), 42));
        controller.AbandonRun();
    }

    [Test]
    public void Validation_rejects_bad_pools_and_bad_life_bar_actions()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.StoriesMutable["invalid"] = new()
        {
            Id = "invalid", RingBattlePools = [
            new() { Ring = 0, Battles = [new() { BattleId = "missing", Weight = 0 }] },
            new() { Ring = 0 } ]
        };
        registry.Store.EnemiesMutable["invalid"] = new()
        {
            Id = "invalid", MaxHp = 1, LifeBars = [
            new() { MaxHp = 0, ActionSequence = [new() { SkillId = "missing", ActionCount = 0 }] } ]
        };
        var errors = new ContentDefinitionValidator().Validate(registry.Store);
        Assert.That(errors.Count(error => error.DefinitionId == "invalid" && error.Category == EContentCategory.Story), Is.GreaterThanOrEqualTo(3));
        Assert.That(errors.Count(error => error.DefinitionId == "invalid" && error.Category == EContentCategory.Enemy), Is.EqualTo(2));
    }
}