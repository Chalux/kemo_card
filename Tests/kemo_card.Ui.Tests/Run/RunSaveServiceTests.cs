using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunSaveServiceTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"run_save_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void Save_and_LoadOrDefault_round_trips()
    {
        var service = new RunSaveService(_tempDir);
        var dto = new RunDto
        {
            RunId = "r-test-001",
            CurrentRing = 2,
            RunSeed = 42,
            SharedGold = 100,
        };

        service.Save(dto);
        var loaded = service.LoadOrDefault();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunId, Is.EqualTo("r-test-001"));
        Assert.That(loaded.CurrentRing, Is.EqualTo(2));
        Assert.That(loaded.RunSeed, Is.EqualTo(42));
        Assert.That(loaded.SharedGold, Is.EqualTo(100));
    }

    [Test]
    public void Save_creates_exists()
    {
        var service = new RunSaveService(_tempDir);
        Assert.That(service.Exists, Is.False);

        service.Save(new RunDto { RunId = "r-exists" });
        Assert.That(service.Exists, Is.True);
    }

    [Test]
    public void Delete_removes_file()
    {
        var service = new RunSaveService(_tempDir);
        service.Save(new RunDto { RunId = "r-del" });
        Assert.That(service.Exists, Is.True);

        service.Delete();
        Assert.That(service.Exists, Is.False);
    }

    [Test]
    public void LoadOrDefault_returns_default_when_no_file()
    {
        var service = new RunSaveService(_tempDir);
        var loaded = service.LoadOrDefault();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.RunId, Is.Empty);
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.Init));
    }

    [Test]
    public void LoadOrDefault_falls_back_to_backup_when_primary_corrupt()
    {
        var service = new RunSaveService(_tempDir);
        var validDto = new RunDto { RunId = "r-backup", RunSeed = 99 };
        service.Save(validDto);
        service.Save(validDto with { SharedGold = 1 });

        var primaryPath = Path.Combine(_tempDir, "r-backup.json");
        File.WriteAllText(primaryPath, "not valid json{{{");

        var loaded = service.LoadOrDefault();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunSeed, Is.EqualTo(99));
    }

    [Test]
    public void LoadOrDefault_returns_default_when_both_files_corrupt()
    {
        var service = new RunSaveService(_tempDir);
        var validDto = new RunDto { RunId = "r-both-corrupt", RunSeed = 77 };
        service.Save(validDto);

        var primaryPath = Path.Combine(_tempDir, "r-both-corrupt.json");
        var backupPath = Path.Combine(_tempDir, "r-both-corrupt.bak.json");
        File.WriteAllText(primaryPath, "bad");
        File.WriteAllText(backupPath, "also bad");

        var loaded = service.LoadOrDefault();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.RunId, Is.Empty);
    }
}
