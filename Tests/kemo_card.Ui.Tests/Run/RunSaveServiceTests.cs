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

    #region P1：损坏存档归档而非静默删除

    [Test]
    public void LoadOrDefault_archives_corrupt_primary_instead_of_deleting_it()
    {
        var warnings = new List<string>();
        var service = new RunSaveService(_tempDir, warnings.Add);
        var validDto = new RunDto { RunId = "r-archive", RunSeed = 99 };
        service.Save(validDto);
        service.Save(validDto with { SharedGold = 1 });

        var primaryPath = Path.Combine(_tempDir, "r-archive.json");
        File.WriteAllText(primaryPath, "not valid json{{{");

        var loaded = service.LoadOrDefault();

        Assert.That(loaded.RunSeed, Is.EqualTo(99), "应回退到备份");
        Assert.That(File.Exists(primaryPath), Is.False, "坏档必须被移走，否则每次读取都重复处理");
        var archives = Directory.GetFiles(_tempDir, "*.corrupt.*.json");
        Assert.That(archives, Has.Length.EqualTo(1), "坏档必须归档保留用户数据（此前实现是 File.Delete，不可逆丢失）");
        Assert.That(File.ReadAllText(archives[0]), Is.EqualTo("not valid json{{{"), "归档必须保留原始内容");
        Assert.That(warnings.Any(w => w.Contains("unusable", StringComparison.Ordinal)), Is.True, "损坏路径必须有日志");
        Assert.That(warnings.Any(w => w.Contains("Archived", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void LoadOrDefault_does_not_treat_archived_corrupt_file_as_primary()
    {
        var service = new RunSaveService(_tempDir);
        service.Save(new RunDto { RunId = "r-stale" });
        File.WriteAllText(Path.Combine(_tempDir, "r-stale.json"), "bad");

        service.LoadOrDefault();

        Assert.That(service.Exists, Is.False, "归档文件不得被当成可继续的存档");
        var second = service.LoadOrDefault();
        Assert.That(second.RunId, Is.Empty);
        Assert.That(
            Directory.GetFiles(_tempDir, "*.corrupt.*.json"),
            Has.Length.EqualTo(1),
            "第二次读取不得再对同一坏档重复归档");
    }

    [Test]
    public void LoadOrDefault_does_not_archive_on_io_error()
    {
        var service = new RunSaveService(_tempDir);
        service.Save(new RunDto { RunId = "r-locked", RunSeed = 5 });
        var primaryPath = Path.Combine(_tempDir, "r-locked.json");

        using (var _ = new FileStream(primaryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = service.LoadOrDefault();

            Assert.That(loaded.RunId, Is.Empty);
            Assert.That(File.Exists(primaryPath), Is.True, "IO 错误（文件被占用）不代表内容损坏，不得归档");
        }

        Assert.That(Directory.GetFiles(_tempDir, "*.corrupt.*.json"), Is.Empty);
        Assert.That(service.LoadOrDefault().RunSeed, Is.EqualTo(5), "释放占用后应能正常读到");
    }

    #endregion

    #region P1：schema 版本语义

    [Test]
    public void LoadOrDefault_rejects_save_with_newer_schema_version()
    {
        var warnings = new List<string>();
        var service = new RunSaveService(_tempDir, warnings.Add);
        File.WriteAllText(
            Path.Combine(_tempDir, "r-newer.json"),
            """{ "runId": "r-newer", "schemaVersion": 999, "runSeed": 7 }""");

        var loaded = service.LoadOrDefault();

        Assert.That(loaded.RunId, Is.Empty, "高于当前版本的存档必须被拒绝，不能按默认值反序列化成「看似合法但错」的 Run");
        Assert.That(File.Exists(Path.Combine(_tempDir, "r-newer.json")), Is.False);
        Assert.That(Directory.GetFiles(_tempDir, "*.corrupt.*.json"), Has.Length.EqualTo(1));
        Assert.That(warnings.Any(w => w.Contains("newer than supported", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void LoadOrDefault_accepts_current_schema_version_and_normalizes()
    {
        var service = new RunSaveService(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "r-current.json"),
            $$"""{ "runId": "r-current", "schemaVersion": {{RunDto.CurrentSchemaVersion}}, "runSeed": 11 }""");

        var loaded = service.LoadOrDefault();

        Assert.That(loaded.RunId, Is.EqualTo("r-current"));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(RunDto.CurrentSchemaVersion));
    }

    [Test]
    public void Save_writes_current_schema_version()
    {
        var service = new RunSaveService(_tempDir);
        service.Save(new RunDto { RunId = "r-version" });

        Assert.That(service.LoadOrDefault().SchemaVersion, Is.EqualTo(RunDto.CurrentSchemaVersion));
    }

    #endregion

    #region P1：写盘失败不抛异常

    [Test]
    public void Save_returns_false_for_empty_run_id()
    {
        var service = new RunSaveService(_tempDir);

        Assert.That(service.Save(new RunDto { RunId = "" }), Is.False);
    }

    [Test]
    public void Save_returns_true_on_success()
    {
        var service = new RunSaveService(_tempDir);

        Assert.That(service.Save(new RunDto { RunId = "r-ok" }), Is.True);
    }

    [Test]
    public void Save_returns_false_instead_of_throwing_when_write_fails()
    {
        var warnings = new List<string>();
        var service = new RunSaveService(_tempDir, warnings.Add);
        // 用一个同名目录占住临时文件路径，让 File.WriteAllText 必然失败。
        Directory.CreateDirectory(Path.Combine(_tempDir, "r-fail.tmp.json"));

        bool saved = true;
        Assert.DoesNotThrow(() => saved = service.Save(new RunDto { RunId = "r-fail" }));

        Assert.That(saved, Is.False, "写盘失败必须返回 false，不得把 IO 异常抛进 Godot 信号回调");
        Assert.That(warnings.Any(w => w.Contains("failed to write", StringComparison.Ordinal)), Is.True);
    }

    #endregion
}