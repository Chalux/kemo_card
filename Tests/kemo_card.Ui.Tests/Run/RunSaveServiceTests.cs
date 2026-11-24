using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Adventure;
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
    public void V5_three_slot_save_preserves_positions_and_round_trips_fifth_slot()
    {
        var service = new RunSaveService(_tempDir);
        var path = Path.Combine(_tempDir, "legacy.json");
        File.WriteAllText(path, """
            {"runId":"legacy","schemaVersion":5,"potions":["a",null,"b"],"teamPotentialPool":20,
             "playerStates":[{"allocatedPotential":10}],"relics":["old-relic"]}
            """);
        var state = new RunMod();
        state.RestoreFrom(service.LoadOrDefault());
        Assert.That(state.Items.Consumables, Is.EqualTo(new string?[] { "a", null, "b", null, null }));
        Assert.That(state.Items.TryAdd(new() { Id = "new-item" }, 4), Is.True);
        service.Save(state.ToDto());

        var saved = service.LoadOrDefault();
        Assert.That(saved.Consumables, Is.EqualTo(new string?[] { "a", null, "b", null, "new-item" }));
        Assert.That(saved.Relics, Is.EqualTo(new[] { "old-relic" }));
        Assert.That(File.ReadAllText(path), Does.Not.Contain("Potential"));
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

        Assert.That(service.Exists, Is.True);
        var loaded = service.LoadOrDefault();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunSeed, Is.EqualTo(99));
        Assert.That(File.Exists(primaryPath), Is.False);
        Assert.That(service.Exists, Is.True, "A readable backup alone can be continued.");
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

    [TestCase(4)]
    [TestCase(999)]
    public void LoadOrDefault_rejects_save_with_a_different_schema_version(int version)
    {
        var warnings = new List<string>();
        var service = new RunSaveService(_tempDir, warnings.Add);
        File.WriteAllText(
            Path.Combine(_tempDir, "r-newer.json"),
            $$"""{ "runId": "r-newer", "schemaVersion": {{version}}, "runSeed": 7 }""");

        Assert.That(service.Exists, Is.False);
        Assert.That(File.Exists(Path.Combine(_tempDir, "r-newer.json")), Is.True, "Availability checks do not archive saves.");
        var loaded = service.LoadOrDefault();

        Assert.That(loaded.RunId, Is.Empty, "版本不兼容的存档必须被拒绝");
        Assert.That(File.Exists(Path.Combine(_tempDir, "r-newer.json")), Is.False);
        Assert.That(Directory.GetFiles(_tempDir, "*.corrupt.*.json"), Has.Length.EqualTo(1));
        Assert.That(warnings.Any(w => w.Contains("does not match supported version", StringComparison.Ordinal)), Is.True);
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
    public void Normalize_pads_player_states_and_accepts_a_new_run_without_characters()
    {
        // 新 Run 在队伍编辑前角色池为空是正常状态；PlayerStates 必须补齐到 3 槽。
        var dto = new RunDto { RunId = "r-new", Phase = ERunPhase.Event }.Normalize();

        Assert.That(dto.PlayerStates, Has.Count.EqualTo(RunConstants.SlotCount));
        Assert.That(dto.HasRestorableStructure(), Is.True);
    }

    [Test]
    public void Empty_character_pool_is_rejected_outside_the_starting_phases()
    {
        var dto = new RunDto { RunId = "r-reward", Phase = ERunPhase.Reward }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False, "Reward 阶段的空角色池永远无法开战，必须拒绝");
    }

    [TestCase(2, 0)]
    [TestCase(0, 3)]
    [TestCase(-1, 0)]
    public void Map_node_outside_the_2x3_grid_is_unrestorable(int branch, int step)
    {
        var dto = new RunDto
        {
            RunId = "r-grid",
            Map = new RunMapState
            {
                Nodes = [new RunMapNode { Branch = branch, Step = step, Kind = ERunNodeKind.Shop }],
            },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False);
    }

    [Test]
    public void Duplicate_map_cells_are_unrestorable()
    {
        var dto = new RunDto
        {
            RunId = "r-dup",
            Map = new RunMapState
            {
                Nodes =
                [
                    new RunMapNode { Branch = 0, Step = 0, Kind = ERunNodeKind.Event },
                    new RunMapNode { Branch = 0, Step = 0, Kind = ERunNodeKind.Shop },
                ],
            },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False);
    }

    [Test]
    public void Visible_event_without_a_reward_is_unrestorable()
    {
        var dto = new RunDto
        {
            RunId = "r-visible",
            Map = new RunMapState
            {
                Nodes = [new RunMapNode { Branch = 0, Step = 2, Kind = ERunNodeKind.VisibleEvent }],
            },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False, "没有奖励的明文事件无法领取也无法离开");
    }

    [Test]
    public void Well_formed_2x3_map_is_restorable()
    {
        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = FullMapNodes() },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.True);
    }

    [Test]
    public void Incomplete_2x3_map_is_unrestorable()
    {
        var nodes = FullMapNodes();
        nodes.RemoveAt(nodes.Count - 1);

        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = nodes },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False, "缺格会让对应按钮永远不存在，Boss 无法解锁");
    }

    [TestCase(0, ERunNodeKind.Shop)]
    [TestCase(1, ERunNodeKind.Event)]
    [TestCase(2, ERunNodeKind.Shop)]
    public void Map_node_kind_must_match_its_step(int step, ERunNodeKind kind)
    {
        var nodes = FullMapNodes();
        var index = nodes.FindIndex(node => node.Step == step);
        nodes[index] = nodes[index] with { Kind = kind };

        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = nodes },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False);
    }

    [Test]
    public void Open_node_cursor_must_point_to_an_openable_node()
    {
        var nodes = FullMapNodes();
        nodes[0] = nodes[0] with { Completed = true };

        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = nodes, NodeOpen = true, SelectedBranch = 0, Position = 0 },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False, "已完成的节点再次打开会同时失去领取与离开入口");
    }

    [Test]
    public void Open_node_cursor_without_a_matching_node_is_unrestorable()
    {
        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = FullMapNodes(), NodeOpen = true, SelectedBranch = -1, Position = 0 },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.False);
    }

    [Test]
    public void Open_node_cursor_on_a_shop_is_restorable()
    {
        var dto = new RunDto
        {
            RunId = "r-map",
            Map = new RunMapState { Nodes = FullMapNodes(), NodeOpen = true, SelectedBranch = 0, Position = 1 },
        }.Normalize();

        Assert.That(dto.HasRestorableStructure(), Is.True);
    }

    [TestCase("""{"runId":"r"}""")]
    [TestCase("""{"runId":"r","schemaVersion":5,"characterPool":null}""")]
    [TestCase("""{"runId":"r","schemaVersion":5,"map":{"nodes":[null]}}""")]
    public void Unrestorable_primary_yields_to_backup_without_mutating_files_during_preview(string json)
    {
        var service = new RunSaveService(_tempDir);
        var primary = Path.Combine(_tempDir, "r.json");
        File.WriteAllText(primary, json);
        Assert.That(service.Exists, Is.False);
        File.WriteAllText(Path.Combine(_tempDir, "r.bak.json"), """{"runId":"r","schemaVersion":5,"runSeed":123}""");
        Assert.That(service.Exists, Is.True);
        Assert.That(File.ReadAllText(primary), Is.EqualTo(json));
        var restored = service.LoadOrDefault();
        Assert.That(restored.RunSeed, Is.EqualTo(123));
        Assert.DoesNotThrow(() => new RunMod().RestoreFrom(restored));
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

    /// <summary>生成器固定 2×3 拓扑：0 = 事件，1 = 商店，2 = 明文事件（带奖励）。</summary>
    private static List<RunMapNode> FullMapNodes()
    {
        var nodes = new List<RunMapNode>();
        for (var branch = 0; branch < RunMapState.BranchCount; branch++)
        {
            for (var step = 0; step < RunMapState.StepsPerBranch; step++)
            {
                nodes.Add(new RunMapNode
                {
                    Branch = branch,
                    Step = step,
                    Kind = step == 1 ? ERunNodeKind.Shop : step == 2 ? ERunNodeKind.VisibleEvent : ERunNodeKind.Event,
                    VisibleReward = step == 2 ? new RunVisibleReward() : null,
                });
            }
        }

        return nodes;
    }
}