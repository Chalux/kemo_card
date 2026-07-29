using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunIntegrationTests
{
    [Test]
    public void Full_run_cycle_create_event_battle_save_load()
    {
        var mod = new RunMod();
        var controller = new RunController(mod);
        var rng = new HostRng(42, "integration");
        var tempDir = Path.Combine(Path.GetTempPath(), $"run_int_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var registry = RunTestHelper.CreateRegistryWithHpCard();
            var candidates = new List<CharacterDto>
            {
                new() { Id = "hero_a", Cards = [CombatSimulationTestBuilder.PartyHpCardId] },
                new() { Id = "hero_b", Cards = [CombatSimulationTestBuilder.PartyHpCardId] },
                new() { Id = "hero_c", Cards = [CombatSimulationTestBuilder.PartyHpCardId] },
                new() { Id = "hero_d", Cards = [CombatSimulationTestBuilder.PartyHpCardId] },
            };

            var dto = controller.CreateRun(rng, candidates, isMultiplayer: false);
            Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
            Assert.That(dto.CurrentRing, Is.EqualTo(1));

            for (var i = 0; i < 4; i++)
            {
                var character = RunTestHelper.CreateCharacterWithHp(i);
                controller.AddToCharacterPool(character);
                controller.SetActiveCharacter(i, poolIndex: i);
            }

            controller.AddGold(slotIndex: 0, amount: 200);
            Assert.That(controller.GetGold(), Is.EqualTo(200));
            Assert.That(controller.ValidateParty(), Is.True);

            mod.Phase = ERunPhase.Reward;
            var simulation = controller.StartBattle(registry, rng, 1);
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Battle));
            Assert.That(simulation, Is.Not.Null);

            mod.Phase = ERunPhase.BattleEnd;
            controller.EndBattle(won: true);
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.RingEnd));

            controller.NextRing();
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Event));
            Assert.That(mod.CurrentRing, Is.EqualTo(2));

            var saveService = new RunSaveService(tempDir);
            controller.Save(saveService);
            Assert.That(saveService.Exists, Is.True);

            var loaded = saveService.LoadOrDefault();
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.RunId, Is.EqualTo(mod.RunId));
            Assert.That(loaded.CurrentRing, Is.EqualTo(2));
            Assert.That(loaded.SharedGold, Is.EqualTo(200));

        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Battle_defeat_rollback_preserves_pre_battle_state()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var mod = new RunMod();
        var controller = new RunController(mod);
        var rng = new HostRng(99, "rollback");

        controller.CreateRun(rng, [], isMultiplayer: false);
        mod.Phase = ERunPhase.Reward;

        for (var i = 0; i < 4; i++)
        {
            var character = RunTestHelper.CreateCharacterWithHp(i);
            controller.AddToCharacterPool(character);
            controller.SetActiveCharacter(i, poolIndex: i);
        }

        controller.AddGold(slotIndex: 0, amount: 300);
        var goldBefore = controller.GetGold();
        controller.AddCard("card.test");

        controller.StartBattle(registry, rng, 1);
        controller.SpendGold(slotIndex: 0, amount: 100);
        controller.RemoveCard("card.test");

        controller.EndBattle(won: false);

        Assert.That(controller.GetGold(), Is.EqualTo(goldBefore));
        Assert.That(mod.CardCollection, Does.Contain("card.test"));
    }
}