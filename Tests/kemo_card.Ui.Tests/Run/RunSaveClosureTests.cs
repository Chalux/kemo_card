using KemoCard.Frame.Scripting;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunSaveClosureTests
{
    [Test]
    public void CreateRun_auto_saves_initial_event_phase()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = new RunController(new RunMod());
        controller.EnableAutoSave(saveService);

        var dto = controller.CreateRun("story_a", new HostRng(1, "create"), [], isMultiplayer: false);

        Assert.That(saveService.Exists, Is.True);
        var loaded = saveService.LoadOrDefault();
        Assert.That(loaded.RunId, Is.EqualTo(dto.RunId));
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.Event));
    }

    [Test]
    public void NextRing_auto_saves_new_ring()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = new RunController(new RunMod());
        controller.EnableAutoSave(saveService);

        controller.CreateRun("story_a", new HostRng(1, "create"), [], isMultiplayer: false);
        controller.NextRing();

        var loaded = saveService.LoadOrDefault();
        Assert.That(loaded.CurrentRing, Is.EqualTo(2));
    }

    [Test]
    public void StartBattle_does_not_overwrite_save_during_combat()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = BuildBattleReadyController(saveService);
        controller.Save(saveService); // 战前存档（Phase=Reward）

        var simulation = controller.StartBattle(
            RunTestHelper.CreateRegistryWithHpCard(), new HostRng(1, "battle"), 1);

        // 战斗中不应落盘：存档保持战前的 Reward，而非 Battle
        var loaded = saveService.LoadOrDefault();
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.Reward));
        simulation.Dispose();
    }

    [Test]
    public void EndBattle_victory_auto_saves_ring_end()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = BuildBattleReadyController(saveService);
        controller.Save(saveService);

        var simulation = controller.StartBattle(
            RunTestHelper.CreateRegistryWithHpCard(), new HostRng(1, "battle"), 1);
        controller.EndBattle(won: true);
        simulation.Dispose();

        var loaded = saveService.LoadOrDefault();
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.RingEnd));
    }

    [Test]
    public void EndBattle_defeat_does_not_overwrite_save()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = BuildBattleReadyController(saveService);
        controller.Save(saveService); // 战前存档（Phase=Reward）

        var simulation = controller.StartBattle(
            RunTestHelper.CreateRegistryWithHpCard(), new HostRng(1, "battle"), 1);
        controller.EndBattle(won: false);
        simulation.Dispose();

        // 失败不做自动保存：存档保持战前 Reward，而非回滚后的 Event
        var loaded = saveService.LoadOrDefault();
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.Reward));
    }

    [Test]
    public void Delete_clears_save()
    {
        using var dir = TempDir();
        var saveService = new RunSaveService(dir.Path);
        var controller = new RunController(new RunMod());
        controller.EnableAutoSave(saveService);

        controller.CreateRun("story_a", new HostRng(1, "create"), [], isMultiplayer: false);
        Assert.That(saveService.Exists, Is.True);

        saveService.Delete();
        Assert.That(saveService.Exists, Is.False);
    }

    private static RunController BuildBattleReadyController(RunSaveService saveService)
    {
        var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false };
        for (var i = 0; i < 4; i++)
        {
            var c = RunTestHelper.CreateCharacterWithHp(i);
            mod.AddToCharacterPool(c);
            mod.PlayerStates[i].SetActiveCharacter(c);
        }

        mod.AddPlayerController(new PlayerController("local", "Player", true));
        for (var i = 0; i < 4; i++)
        {
            mod.AssignSlotInternal(i, "local");
        }

        var controller = new RunController(mod);
        controller.EnableAutoSave(saveService);
        return controller;
    }

    private static TempSaveDir TempDir()
    {
        return new TempSaveDir(Path.Combine(Path.GetTempPath(), $"run_save_closure_{Guid.NewGuid():N}"));
    }

    private sealed class TempSaveDir(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // 测试清理失败不掩盖断言结果
            }
        }
    }
}
