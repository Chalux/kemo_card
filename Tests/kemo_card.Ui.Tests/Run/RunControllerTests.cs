using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunControllerTests
{
    [Test]
    public void LoadRun_restores_the_full_character_definition()
    {
        // 存档只记 definitionId / definitionCardIds；读档若不按 id 取回完整定义，
        // 角色会退化成"无名无元素、无能量保底"的空壳（队伍编辑界面上就是空白条目）。
        var definition = new CharacterDto
        {
            Id = "hero_full",
            DisplayNameId = "char.hero_full.name",
            Element = EElement.Blue,
            Race = ERace.Animal,
            Cards = [CombatSimulationTestBuilder.PartyHpCardId],
            MaxEnergy = 7,
            InitialEnergy = 4,
        };
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        CombatTestHelper.RebuildInto(
            registry,
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.PartyHpCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
            },
            characters: new Dictionary<string, CharacterDto> { [definition.Id] = definition });

        var source = new RunController(new RunMod());
        source.CreateRun("story_a", new HostRng(11, "load"), []);
        source.AddToCharacterPool(new CharacterInstance(definition, "inst-full"));
        var saved = source.State.ToDto();
        source.Dispose();

        var restored = new RunController(
            new RunMod(),
            characterDefinitionResolver: id =>
                registry.Store.TryGetCharacter(id, out var dto) ? dto : null);
        restored.LoadRun(saved);

        var character = restored.State.CharacterPool.Single();
        Assert.That(character.Definition, Is.Not.Null);
        Assert.That(character.Definition!.DisplayNameId, Is.EqualTo("char.hero_full.name"));
        Assert.That(character.Definition.Element, Is.EqualTo(EElement.Blue));
        Assert.That(character.Definition.MaxEnergy, Is.EqualTo(7));
        Assert.That(character.Definition.InitialEnergy, Is.EqualTo(4));

        // 角色级能量保底必须随定义一起回来（此前读档后会整块丢失）。
        var attributes = character.ComputeAttributeMap(registry);
        Assert.That(attributes["MaxEnergy"], Is.GreaterThanOrEqualTo(7f));
    }

    [Test]
    public void LoadRun_without_a_resolver_still_falls_back_to_the_saved_snapshot()
    {
        var source = new RunController(new RunMod());
        source.CreateRun("story_a", new HostRng(12, "load"), []);
        source.AddToCharacterPool(RunTestHelper.CreateCharacterWithHp(0));
        var saved = source.State.ToDto();
        source.Dispose();

        var restored = new RunController(new RunMod());
        restored.LoadRun(saved);

        var character = restored.State.CharacterPool.Single();
        Assert.That(character.DefinitionId, Is.EqualTo("hero_0"));
        Assert.That(character.Definition, Is.Not.Null, "取不到定义时退回存档内最小快照");
        Assert.That(
            character.Definition!.Cards,
            Is.EquivalentTo(new[] { CombatSimulationTestBuilder.PartyHpCardId }));
        Assert.That(character.GetCurrentDeck()!.CardIds, Is.Not.Empty, "卡组快照仍然生效");
    }

    [Test]
    public void CreateRun_sets_initial_state()
    {
        var controller = new RunController(new RunMod());
        var candidates = new List<CharacterDto>
        {
            new() { Id = "hero_a", Cards = [] },
            new() { Id = "hero_b", Cards = [] },
            new() { Id = "hero_c", Cards = [] },
        };
        var rng = new HostRng(1, "create");

        var dto = controller.CreateRun("story_a", rng, candidates);

        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
        Assert.That(dto.CurrentRing, Is.EqualTo(1));
        Assert.That(dto.RunId, Is.Not.Empty);
    }

    [Test]
    public void CreateRun_fixes_story_id_and_uses_host_rng_seed()
    {
        var controller = new RunController(new RunMod());
        var rng = new HostRng(42, "story_select");

        var dto = controller.CreateRun("base-rogue", rng, []);

        Assert.That(dto.StoryId, Is.EqualTo("base-rogue"));
        Assert.That(dto.RunSeed, Is.EqualTo(42));
        Assert.That(controller.State.StoryId, Is.EqualTo("base-rogue"));
    }

    [Test]
    public void AddToCharacterPool_adds_character()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");

        var result = controller.AddToCharacterPool(character);

        Assert.That(result, Is.True);
        Assert.That(controller.State.CharacterPool, Has.Count.EqualTo(1));
        Assert.That(controller.State.CharacterPool[0].InstanceId, Is.EqualTo("inst-1"));
    }

    [Test]
    public void AddToCharacterPool_duplicate_keeps_original_character()
    {
        var controller = new RunController(new RunMod());
        controller.AddToCharacterPool(new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1"));

        var result = controller.AddToCharacterPool(new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-2"));

        Assert.That(result, Is.False, "重复获得返回 false");
        Assert.That(controller.State.CharacterPool, Has.Count.EqualTo(1), "不入第二实例");
    }


    [Test]
    public void SetActiveCharacter_updates_slot()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");
        controller.AddToCharacterPool(character);

        var result = controller.SetActiveCharacter(slotIndex: 0, poolIndex: 0);

        Assert.That(result, Is.True);
        Assert.That(controller.State.PlayerStates[0].ActiveCharacter, Is.SameAs(character));
    }

    [Test]
    public void SetActiveCharacter_fails_for_out_of_range_pool_index()
    {
        var controller = new RunController(new RunMod());

        var result = controller.SetActiveCharacter(slotIndex: 0, poolIndex: 99);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnsetActiveCharacter_clears_slot()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");
        controller.AddToCharacterPool(character);
        controller.SetActiveCharacter(0, 0);

        var result = controller.UnsetActiveCharacter(0);

        Assert.That(result, Is.True);
        Assert.That(controller.State.PlayerStates[0].ActiveCharacter, Is.Null);
    }

    [Test]
    public void Gold_operations_singleplayer_use_shared_gold()
    {
        var mod = new RunMod();
        var controller = new RunController(mod);

        controller.AddGold(100);
        var gold = controller.GetGold();

        Assert.That(gold, Is.EqualTo(100));
        Assert.That(controller.State.SharedGold, Is.EqualTo(100));
    }

    [Test]
    public void SpendGold_singleplayer_deducts_shared_gold()
    {
        var mod = new RunMod { SharedGold = 200 };
        var controller = new RunController(mod);

        var result = controller.SpendGold(50);

        Assert.That(result, Is.True);
        Assert.That(controller.State.SharedGold, Is.EqualTo(150));
    }

    [Test]
    public void SpendGold_fails_when_insufficient()
    {
        var mod = new RunMod { SharedGold = 30 };
        var controller = new RunController(mod);

        var result = controller.SpendGold(100);

        Assert.That(result, Is.False);
        Assert.That(controller.State.SharedGold, Is.EqualTo(30));
    }

    [Test]
    public void NextRing_advances_and_returns_has_next()
    {
        var mod = new RunMod { MaxRing = 3, CurrentRing = 1 };
        var controller = new RunController(mod);

        Assert.That(controller.HasNextRing(), Is.True);
        controller.NextRing();
        Assert.That(controller.State.CurrentRing, Is.EqualTo(2));
        Assert.That(controller.HasNextRing(), Is.True);

        controller.NextRing();
        Assert.That(controller.State.CurrentRing, Is.EqualTo(3));
        Assert.That(controller.HasNextRing(), Is.False);
    }

    [Test]
    public void AbandonRun_sets_phase_to_finished()
    {
        var mod = new RunMod { Phase = ERunPhase.Battle };
        var controller = new RunController(mod);

        var dto = controller.AbandonRun();

        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Finished));
    }

    [Test]
    public void StartBattle_fails_when_party_not_valid()
    {
        var mod = new RunMod { Phase = ERunPhase.Reward };
        var controller = new RunController(mod);

        Assert.Throws<InvalidOperationException>(() =>
            controller.StartBattle(CombatTestHelper.CreateFullRegistry(), new HostRng(1, "battle"), 1));
    }

    [Test]
    public void StartBattle_creates_snapshot_and_changes_phase()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var mod = new RunMod { Phase = ERunPhase.Reward };
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var c = RunTestHelper.CreateCharacterWithHp(i);
            mod.AddToCharacterPool(c);
            mod.PlayerStates[i].SetActiveCharacter(c);
        }
        var controller = new RunController(mod);

        var simulation = controller.StartBattle(registry, new HostRng(1, "battle"), 1);

        Assert.That(simulation, Is.Not.Null);
        Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Battle));
        simulation.Dispose();
    }

    [Test]
    public void EndBattle_victory_sets_phase_to_ring_end()
    {
        var mod = new RunMod { Phase = ERunPhase.BattleEnd };
        var controller = new RunController(mod);

        controller.EndBattle(won: true);

        Assert.That(mod.Phase, Is.EqualTo(ERunPhase.RingEnd));
    }

    [Test]
    public void EndBattle_defeat_rolls_back_to_event()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var mod = new RunMod { Phase = ERunPhase.Reward, SharedGold = 500 };
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var c = RunTestHelper.CreateCharacterWithHp(i);
            mod.AddToCharacterPool(c);
            mod.PlayerStates[i].SetActiveCharacter(c);
        }
        var controller = new RunController(mod);

        controller.StartBattle(registry, new HostRng(1, "battle"), 1);
        mod.SharedGold = 100;
        controller.EndBattle(won: false);

        Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Event));
        Assert.That(mod.SharedGold, Is.EqualTo(500));
    }
}