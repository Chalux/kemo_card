# 内容 Mod 管理器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `Src/frame/content/` 实现磁盘内容 Mod 的发现、依赖拓扑排序、冲突合并与七大注册表刷新；扩展全局存档 `EnabledModIds`；在 `MainRoot` 冷启动时执行 `Rebuild()`；预留 `IContentModUserNotifier`。

**Architecture:** `ContentModPipeline` 组合 `ContentModDiscovery` → `ContentModActivationPlanner` → `ContentModLoader` → `ContentRegistryMerger` → `GameDefinitionRegistry`。首版内容 id 由 `content/<category>/*.json` 文件名（无扩展名）推导，不解析 JSON 正文。全部代码在 `KemoCard.Frame.Content` 命名空间，不新建独立程序集。

**Tech Stack:** Godot 4.6.1 Mono、.NET 8、C# 12、NUnit 4.x、`System.Text.Json`。

**规格路径：** `Doc/superpowers/specs/2026-05-17-content-mod-manager-design.md`

---

## 文件结构（创建 / 修改）

| 路径 | 职责 |
|------|------|
| `Src/frame/content/ContentCategory.cs` | 七大枚举 |
| `Src/frame/content/ModSkipReason.cs` | 跳过原因枚举 |
| `Src/frame/content/ContentModManifestDto.cs` | manifest DTO + JSON 反序列化 |
| `Src/frame/content/DiscoveredModEntry.cs` | 扫描结果（路径 + manifest 或跳过原因） |
| `Src/frame/content/ModSkipEntry.cs` | 跳过条目 |
| `Src/frame/content/ContentIdConflictEntry.cs` | id 冲突条目 |
| `Src/frame/content/ContentLoadReport.cs` | 合并报告 |
| `Src/frame/content/ContentModActivationResult.cs` | 规划结果 |
| `Src/frame/content/ModContentBundle.cs` | 单 Mod 内容 id 集合 |
| `Src/frame/content/GameDefinitionRegistry.cs` | 七大表 + `DefinitionVersion` + `Contains` |
| `Src/frame/content/ContentRegistryMerger.cs` | 按序合并 + 冲突记录 |
| `Src/frame/content/ContentModDiscovery.cs` | 扫描 `mod.json` |
| `Src/frame/content/ContentModActivationPlanner.cs` | 启用集 + 拓扑 + loadOrder |
| `Src/frame/content/ContentModLoader.cs` | 读 `content/<dir>/*.json` 文件名 |
| `Src/frame/content/IContentModLogger.cs` | 日志抽象 |
| `Src/frame/content/NullContentModLogger.cs` | 单测用 no-op |
| `Src/frame/content/IContentModUserNotifier.cs` | 游戏内提示接口 |
| `Src/frame/content/NullContentModUserNotifier.cs` | 空实现 |
| `Src/frame/content/ContentModPipeline.cs` | 编排 `Rebuild` |
| `Src/frame/content/ContentModRequiredExpander.cs` | 静态：扩展 `required` 依赖 |
| `Src/mod/global/Save/GlobalSaveDto.cs` | 增加 `EnabledModIds`，`SchemaVersion = 2` |
| `Src/mod/global/GlobalMod.cs` | 暴露 `ContentModPipeline` 构造/访问（可选） |
| `Src/MainRoot.cs` | 冷启动 `Rebuild` |
| `Config/mods/base-game/mod.json` | 开发用内置 Mod 样例 |
| `Config/mods/base-game/content/cards/strike.json` | 占位 `{}` |
| `Tests/kemo_card.Ui.Tests/ContentModActivationPlannerTests.cs` | 规划器单测 |
| `Tests/kemo_card.Ui.Tests/ContentRegistryMergerTests.cs` | 合并器单测 |
| `Tests/kemo_card.Ui.Tests/ContentModDiscoveryTests.cs` | 发现器单测 |
| `Tests/kemo_card.Ui.Tests/ContentModPipelineTests.cs` | 端到端管线单测 |
| `Tests/kemo_card.Ui.Tests/GlobalSaveServiceTests.cs` | 增加 `EnabledModIds` 往返 |

**目录名 → `ContentCategory` 映射（首版固定）：**

| 子目录 | `ContentCategory` |
|--------|-------------------|
| `characters` | `Character` |
| `battles` | `Battle` |
| `events` | `Event` |
| `cards` | `Card` |
| `items` | `Item` |
| `skills` | `Skill` |
| `buffs` | `Buff` |

---

### Task 1: 基础类型与注册表（TDD）

**Files:**
- Create: `Src/frame/content/ContentCategory.cs`
- Create: `Src/frame/content/ModSkipReason.cs`
- Create: `Src/frame/content/ModContentBundle.cs`
- Create: `Src/frame/content/GameDefinitionRegistry.cs`
- Create: `Tests/kemo_card.Ui.Tests/GameDefinitionRegistryTests.cs`

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Ui.Tests/GameDefinitionRegistryTests.cs`：

```csharp
using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class GameDefinitionRegistryTests
{
	[Test]
	public void Contains_returns_false_for_unknown_card()
	{
		var reg = new GameDefinitionRegistry();
		reg.Rebuild(Array.Empty<ModContentBundle>(), out _);

		Assert.That(reg.Contains(ContentCategory.Card, "missing"), Is.False);
	}

	[Test]
	public void Contains_returns_true_after_registering_card()
	{
		var reg = new GameDefinitionRegistry();
		var bundle = new ModContentBundle(
			ModId: "base.game",
			Characters: Array.Empty<string>(),
			Battles: Array.Empty<string>(),
			Events: Array.Empty<string>(),
			Cards: new[] { "strike" },
			Items: Array.Empty<string>(),
			Skills: Array.Empty<string>(),
			Buffs: Array.Empty<string>());

		reg.Rebuild(new[] { bundle }, out var report);

		Assert.That(reg.Contains(ContentCategory.Card, "strike"), Is.True);
		Assert.That(report.IdConflicts, Is.Empty);
		Assert.That(reg.DefinitionVersion, Is.EqualTo(1));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter FullyQualifiedName~GameDefinitionRegistryTests
```

**期望：** FAIL（类型不存在）。

- [ ] **Step 3: 实现最小类型**

`ContentCategory.cs`：

```csharp
namespace KemoCard.Frame.Content;

public enum ContentCategory
{
	Character,
	Battle,
	Event,
	Card,
	Item,
	Skill,
	Buff,
}
```

`ModSkipReason.cs`：

```csharp
namespace KemoCard.Frame.Content;

public enum ModSkipReason
{
	InvalidManifest,
	DuplicateModId,
	MissingRequiredDependency,
	CyclicDependency,
	LoadFailed,
}
```

`ModContentBundle.cs`：

```csharp
namespace KemoCard.Frame.Content;

public sealed record ModContentBundle(
	string ModId,
	IReadOnlyList<string> Characters,
	IReadOnlyList<string> Battles,
	IReadOnlyList<string> Events,
	IReadOnlyList<string> Cards,
	IReadOnlyList<string> Items,
	IReadOnlyList<string> Skills,
	IReadOnlyList<string> Buffs);
```

`GameDefinitionRegistry.cs`：

```csharp
using System.Collections.Frozen;

namespace KemoCard.Frame.Content;

public sealed class GameDefinitionRegistry
{
	private readonly Dictionary<ContentCategory, HashSet<string>> _tables = new()
	{
		[ContentCategory.Character] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Battle] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Event] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Card] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Item] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Skill] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Buff] = new HashSet<string>(StringComparer.Ordinal),
	};

	public int DefinitionVersion { get; private set; }

	public void Rebuild(IReadOnlyList<ModContentBundle> bundles, out ContentLoadReport report)
	{
		foreach (var set in _tables.Values)
		{
			set.Clear();
		}

		var merger = new ContentRegistryMerger();
		merger.Merge(bundles, _tables, out report);
		DefinitionVersion++;
	}

	public bool Contains(ContentCategory category, string id) =>
		_tables[category].Contains(id);
}
```

- [ ] **Step 4: 先实现 `ContentRegistryMerger`（Task 2 依赖）或在本步内联最小合并**

若 Step 3 编译失败，先跳到 Task 2 实现 `ContentRegistryMerger` 后返回。

- [ ] **Step 5: 运行测试通过**

```powershell
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter FullyQualifiedName~GameDefinitionRegistryTests
```

**期望：** PASS

---

### Task 2: `ContentRegistryMerger` 与冲突报告（TDD）

**Files:**
- Create: `Src/frame/content/ContentIdConflictEntry.cs`
- Create: `Src/frame/content/ModSkipEntry.cs`
- Create: `Src/frame/content/ContentLoadReport.cs`
- Create: `Src/frame/content/ContentRegistryMerger.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentRegistryMergerTests.cs`

- [ ] **Step 1: 编写失败测试**

```csharp
using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentRegistryMergerTests
{
	[Test]
	public void Merge_skips_later_duplicate_in_same_category()
	{
		var tables = CreateEmptyTables();
		var merger = new ContentRegistryMerger();
		var bundles = new[]
		{
			new ModContentBundle("a", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
				new[] { "strike" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
			new ModContentBundle("b", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
				new[] { "strike" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
		};

		merger.Merge(bundles, tables, out var report);

		Assert.That(tables[ContentCategory.Card], Does.Contain("strike"));
		Assert.That(report.IdConflicts, Has.Count.EqualTo(1));
		Assert.That(report.IdConflicts[0].WinnerModId, Is.EqualTo("a"));
		Assert.That(report.IdConflicts[0].LoserModId, Is.EqualTo("b"));
	}

	[Test]
	public void Merge_allows_same_id_in_different_categories()
	{
		var tables = CreateEmptyTables();
		var merger = new ContentRegistryMerger();
		var bundles = new[]
		{
			new ModContentBundle("a", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
				new[] { "foo" }, Array.Empty<string>(), new[] { "foo" }, Array.Empty<string>()),
		};

		merger.Merge(bundles, tables, out var report);

		Assert.That(tables[ContentCategory.Card], Does.Contain("foo"));
		Assert.That(tables[ContentCategory.Skill], Does.Contain("foo"));
		Assert.That(report.IdConflicts, Is.Empty);
	}

	private static Dictionary<ContentCategory, HashSet<string>> CreateEmptyTables() =>
		new()
		{
			[ContentCategory.Character] = new(StringComparer.Ordinal),
			[ContentCategory.Battle] = new(StringComparer.Ordinal),
			[ContentCategory.Event] = new(StringComparer.Ordinal),
			[ContentCategory.Card] = new(StringComparer.Ordinal),
			[ContentCategory.Item] = new(StringComparer.Ordinal),
			[ContentCategory.Skill] = new(StringComparer.Ordinal),
			[ContentCategory.Buff] = new(StringComparer.Ordinal),
		};
}
```

- [ ] **Step 2: 运行确认 FAIL**

```powershell
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter FullyQualifiedName~ContentRegistryMergerTests
```

- [ ] **Step 3: 实现报告类型与 Merger**

`ContentIdConflictEntry.cs`：

```csharp
namespace KemoCard.Frame.Content;

public sealed record ContentIdConflictEntry(
	ContentCategory Category,
	string ContentId,
	string WinnerModId,
	string LoserModId);
```

`ContentLoadReport.cs`：

```csharp
namespace KemoCard.Frame.Content;

public sealed class ContentLoadReport
{
	public ContentLoadReport(
		IReadOnlyList<ModSkipEntry> skippedMods,
		IReadOnlyList<ContentIdConflictEntry> idConflicts)
	{
		SkippedMods = skippedMods;
		IdConflicts = idConflicts;
	}

	public IReadOnlyList<ModSkipEntry> SkippedMods { get; }
	public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }
	public bool HasIssues => SkippedMods.Count > 0 || IdConflicts.Count > 0;
}
```

`ModSkipEntry.cs`：

```csharp
namespace KemoCard.Frame.Content;

public sealed record ModSkipEntry(string ModId, ModSkipReason Reason, string? Detail = null);
```

`ContentRegistryMerger.cs` 核心逻辑：

```csharp
namespace KemoCard.Frame.Content;

public sealed class ContentRegistryMerger
{
	public void Merge(
		IReadOnlyList<ModContentBundle> bundles,
		Dictionary<ContentCategory, HashSet<string>> tables,
		out ContentLoadReport report)
	{
		var conflicts = new List<ContentIdConflictEntry>();
		foreach (var bundle in bundles)
		{
			TryAddAll(bundle.ModId, ContentCategory.Character, bundle.Characters, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Battle, bundle.Battles, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Event, bundle.Events, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Card, bundle.Cards, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Item, bundle.Items, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Skill, bundle.Skills, tables, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Buff, bundle.Buffs, tables, conflicts);
		}

		report = new ContentLoadReport(Array.Empty<ModSkipEntry>(), conflicts);
	}

	private static void TryAddAll(
		string modId,
		ContentCategory category,
		IReadOnlyList<string> ids,
		Dictionary<ContentCategory, HashSet<string>> tables,
		List<ContentIdConflictEntry> conflicts)
	{
		var set = tables[category];
		foreach (var id in ids)
		{
			if (set.Add(id))
			{
				continue;
			}

			var winner = FindWinnerModId(category, id, tables, modId);
			conflicts.Add(new ContentIdConflictEntry(category, id, winner, modId));
		}
	}

	private static string FindWinnerModId(
		ContentCategory category,
		string contentId,
		Dictionary<ContentCategory, HashSet<string>> tables,
		string loserModId) => loserModId; // 简化：冲突条目由 Merge 顺序保证先注册者已在 set 中；Winner 在 Merge 循环内用 lastWinner 字典追踪更佳
	}
}
```

**实现注意：** 在 `Merge` 内维护 `Dictionary<(ContentCategory, string), string> ownerById`，`set.Add` 失败时从 `ownerById` 取 `WinnerModId`，成功时写入当前 `modId`。

- [ ] **Step 4: 运行测试 PASS**

- [ ] **Step 5: 完成 Task 1 Step 3–5（若尚未通过）**

---

### Task 3: Manifest DTO 与 Discovery（TDD）

**Files:**
- Create: `Src/frame/content/ContentModManifestDto.cs`
- Create: `Src/frame/content/DiscoveredModEntry.cs`
- Create: `Src/frame/content/ContentModDiscovery.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentModDiscoveryTests.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs`

- [ ] **Step 1: 测试辅助**

`ContentModTestHelper.cs`：

```csharp
namespace KemoCard.Ui.Tests;

internal static class ContentModTestHelper
{
	public static string CreateModFolder(string root, string folderName, string modId, int loadOrder = 0, string[]? required = null)
	{
		var dir = Path.Combine(root, folderName);
		Directory.CreateDirectory(Path.Combine(dir, "content", "cards"));
		var manifest = $$"""
		{
		  "modId": "{{modId}}",
		  "displayName": "{{modId}}",
		  "version": "1.0.0",
		  "loadOrder": {{loadOrder}},
		  "dependencies": { "required": [{{string.Join(",", (required ?? Array.Empty<string>()).Select(r => $"\"{r}\""))}}], "optional": [] },
		  "contentRoot": "content"
		}
		""";
		File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
		return dir;
	}

	public static void AddCard(string modDir, string cardId)
	{
		var path = Path.Combine(modDir, "content", "cards", cardId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{}");
	}
}
```

- [ ] **Step 2: 编写失败测试**

```csharp
[Test]
public void Scan_skips_duplicate_modId()
{
	var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
	ContentModTestHelper.CreateModFolder(root, "a", "dup.mod");
	ContentModTestHelper.CreateModFolder(root, "b", "dup.mod");

	var discovery = new ContentModDiscovery();
	var result = discovery.Scan(root);

	Assert.That(result.ValidMods, Has.Count.EqualTo(1));
	Assert.That(result.SkippedMods, Has.Some.Matches<ModSkipEntry>(e =>
		e.Reason == ModSkipReason.DuplicateModId));
}
```

- [ ] **Step 3: 实现 `ContentModManifestDto` 与 `ContentModDiscovery`**

`ContentModManifestDto.cs` 使用 `System.Text.Json`：

```csharp
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content;

public sealed class ContentModManifestDto
{
	[JsonPropertyName("modId")]
	public string ModId { get; init; } = "";

	[JsonPropertyName("displayName")]
	public string DisplayName { get; init; } = "";

	[JsonPropertyName("version")]
	public string Version { get; init; } = "1.0.0";

	[JsonPropertyName("loadOrder")]
	public int LoadOrder { get; init; }

	[JsonPropertyName("dependencies")]
	public ContentModDependenciesDto Dependencies { get; init; } = new();

	[JsonPropertyName("contentRoot")]
	public string ContentRoot { get; init; } = "content";
}

public sealed class ContentModDependenciesDto
{
	[JsonPropertyName("required")]
	public List<string> Required { get; init; } = new();

	[JsonPropertyName("optional")]
	public List<string> Optional { get; init; } = new();
}
```

`DiscoveredModEntry` = `record DiscoveredModEntry(string FolderPath, ContentModManifestDto Manifest)`。

`ContentModDiscovery.Scan` 返回：

```csharp
public sealed record ContentModDiscoveryResult(
	IReadOnlyList<DiscoveredModEntry> ValidMods,
	IReadOnlyList<ModSkipEntry> SkippedMods);
```

- 子目录排序按文件夹名字典序扫描；`modId` 首次出现入 `ValidMods`，重复入 `SkippedMods(DuplicateModId)`。
- `mod.json` 缺失或反序列化失败 → `SkippedMods(InvalidManifest)`，detail 为文件夹名。

- [ ] **Step 4: 运行测试 PASS**

---

### Task 4: `ContentModActivationPlanner`（TDD）

**Files:**
- Create: `Src/frame/content/ContentModActivationResult.cs`
- Create: `Src/frame/content/ContentModActivationPlanner.cs`
- Create: `Src/frame/content/ContentModRequiredExpander.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentModActivationPlannerTests.cs`

- [ ] **Step 1: 失败测试 — 缺依赖**

```csharp
[Test]
public void Plan_skips_mod_when_required_not_enabled()
{
	var root = ...;
	ContentModTestHelper.CreateModFolder(root, "base", "base.game");
	ContentModTestHelper.CreateModFolder(root, "ext", "ext.mod", required: new[] { "missing.dep" });

	var discovery = new ContentModDiscovery();
	var scan = discovery.Scan(root);
	var planner = new ContentModActivationPlanner();
	var result = planner.Plan(scan.ValidMods, new[] { "ext.mod" }, scan.SkippedMods);

	Assert.That(result.OrderedActiveMods, Is.Empty);
	Assert.That(result.SkippedMods, Has.Some.Matches<ModSkipEntry>(e =>
		e.ModId == "ext.mod" && e.Reason == ModSkipReason.MissingRequiredDependency));
}
```

- [ ] **Step 2: 失败测试 — 拓扑 + loadOrder**

```csharp
[Test]
public void Plan_orders_by_dependency_then_loadOrder()
{
	// base.game loadOrder 0, addon loadOrder 10, addon requires base.game
	// 期望 OrderedActiveMods: base 在前 addon 在后
}
```

- [ ] **Step 3: 失败测试 — 环依赖**

```csharp
[Test]
public void Plan_skips_all_mods_in_cycle()
{
	// a requires b, b requires a
}
```

- [ ] **Step 4: 实现 `ContentModRequiredExpander`**

```csharp
namespace KemoCard.Frame.Content;

public static class ContentModRequiredExpander
{
	public static HashSet<string> Expand(
		IEnumerable<string> enabledModIds,
		IReadOnlyDictionary<string, ContentModManifestDto> manifestsById)
	{
		var result = new HashSet<string>(StringComparer.Ordinal);
		var queue = new Queue<string>(enabledModIds);
		while (queue.Count > 0)
		{
			var id = queue.Dequeue();
			if (!result.Add(id))
			{
				continue;
			}

			if (!manifestsById.TryGetValue(id, out var manifest))
			{
				continue;
			}

			foreach (var dep in manifest.Dependencies.Required)
			{
				queue.Enqueue(dep);
			}
		}

		return result;
	}
}
```

- [ ] **Step 5: 实现 `ContentModActivationPlanner.Plan`**

- Kahn 拓扑排序；环检测：无法出队的节点 → `CyclicDependency`。
- 同层稳定排序：`LoadOrder` 升序，再 `ModId` 字典序。
- 输出 `ContentModActivationResult(OrderedActiveMods, SkippedMods, ExpandedEnabledSet)`。

- [ ] **Step 6: 运行全部 Planner 测试 PASS**

---

### Task 5: `ContentModLoader`（TDD）

**Files:**
- Create: `Src/frame/content/ContentModLoader.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentModLoaderTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
[Test]
public void Load_collects_card_ids_from_filenames()
{
	var root = ...;
	var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
	ContentModTestHelper.AddCard(modDir, "strike");

	var loader = new ContentModLoader();
	var bundle = loader.Load(new DiscoveredModEntry(modDir, manifest));

	Assert.That(bundle.Cards, Does.Contain("strike"));
}
```

- [ ] **Step 2: 实现 `ContentModLoader`**

- `contentRoot` 相对 Mod 目录。
- 遍历七个映射子目录；`*.json` 的 `Path.GetFileNameWithoutExtension` = content id。
- 目录不存在 → 空列表；IO 异常 → 抛 `ContentModLoadException`，由 Pipeline 转为 `LoadFailed` 跳过整包。

- [ ] **Step 3: 测试 PASS**

---

### Task 6: `ContentModPipeline` + 日志/通知接口（TDD）

**Files:**
- Create: `Src/frame/content/IContentModLogger.cs`
- Create: `Src/frame/content/NullContentModLogger.cs`
- Create: `Src/frame/content/IContentModUserNotifier.cs`
- Create: `Src/frame/content/NullContentModUserNotifier.cs`
- Create: `Src/frame/content/ContentModPipeline.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentModPipelineTests.cs`

- [ ] **Step 1: 端到端失败测试**

```csharp
[Test]
public void Rebuild_merges_enabled_mods_and_reports_conflict()
{
	var root = ...;
	var baseDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
	ContentModTestHelper.AddCard(baseDir, "strike");
	var addonDir = ContentModTestHelper.CreateModFolder(root, "addon", "addon.mod", required: new[] { "base.game" });
	ContentModTestHelper.AddCard(addonDir, "strike");

	var registry = new GameDefinitionRegistry();
	var pipeline = new ContentModPipeline(root, registry, new NullContentModLogger(), new NullContentModUserNotifier());
	var report = pipeline.Rebuild(new[] { "base.game", "addon.mod" });

	Assert.That(registry.Contains(ContentCategory.Card, "strike"), Is.True);
	Assert.That(report.IdConflicts, Has.Count.EqualTo(1));
}
```

- [ ] **Step 2: 实现 `ContentModPipeline`**

```csharp
public sealed class ContentModPipeline
{
	public ContentModPipeline(
		string modRootDirectory,
		GameDefinitionRegistry registry,
		IContentModLogger logger,
		IContentModUserNotifier notifier) { ... }

	public ContentLoadReport Rebuild(IReadOnlyList<string> enabledModIds)
	{
		// Discovery → Planner → Loader → Registry.Rebuild → merge SkippedMods into report
		// logger 记录每条 skip/conflict
		// notifier.OnModLoadCompleted(finalReport)
	}
}
```

- [ ] **Step 3: 测试 PASS**

---

### Task 7: 全局存档 `EnabledModIds`（Schema v2）

**Files:**
- Modify: `Src/mod/global/Save/GlobalSaveDto.cs`
- Modify: `Tests/kemo_card.Ui.Tests/GlobalSaveServiceTests.cs`

- [ ] **Step 1: 更新 DTO**

```csharp
public sealed record GlobalSaveDto(
	int SchemaVersion,
	Dictionary<string, bool> Achievements,
	Dictionary<string, bool> Unlocks,
	Dictionary<string, bool> CodexEntries,
	Dictionary<string, string> Settings,
	IReadOnlyList<string>? EnabledModIds = null,
	string? ContentVersionHash = null)
{
	public const int CurrentSchemaVersion = 2;

	public static GlobalSaveDto CreateDefault() =>
		new(
			SchemaVersion: CurrentSchemaVersion,
			Achievements: new Dictionary<string, bool>(StringComparer.Ordinal),
			Unlocks: new Dictionary<string, bool>(StringComparer.Ordinal),
			CodexEntries: new Dictionary<string, bool>(StringComparer.Ordinal),
			Settings: new Dictionary<string, string>(StringComparer.Ordinal),
			EnabledModIds: new[] { "base.game" });
}
```

- [ ] **Step 2: 增加测试**

```csharp
[Test]
public void RoundTrip_persists_enabled_mod_ids()
{
	var original = GlobalSaveDto.CreateDefault() with
	{
		EnabledModIds = new[] { "base.game", "addon.mod" }
	};
	// Save / Load / Assert
}
```

- [ ] **Step 3: 旧存档无 `enabledModIds` 时反序列化为 null → `LoadOrDefault` 后归一为 `CreateDefault().EnabledModIds`（在 `GlobalModController.LoadFromDisk` 或 DTO 工厂方法中处理）**

- [ ] **Step 4: 测试 PASS**

---

### Task 8: `MainRoot` 集成与样例 Mod

**Files:**
- Modify: `Src/MainRoot.cs`
- Create: `Config/mods/base-game/mod.json`
- Create: `Config/mods/base-game/content/cards/strike.json`

- [ ] **Step 1: 样例 Mod**

`Config/mods/base-game/mod.json` 使用规格示例（`modId: base.game`）。

`strike.json` 内容：`{}`

- [ ] **Step 2: 修改 `MainRoot._Ready`**

```csharp
using KemoCard.Frame.Content;

// 在 GlobalMod.Bootstrap() 之后：
var modsSource = ProjectSettings.GlobalizePath("res://Config/mods");
var modsUser = ProjectSettings.GlobalizePath("user://mods");
// 首版：若 user://mods 为空，从 res://Config/mods 复制或仅扫描 user；简化方案——同时扫描两个根，user 优先（实现：Pipeline 接受多个根或启动时 CopyDir）
```

**首版简化（写进代码注释）：** 仅扫描 `user://mods`；在 `_Ready` 中若目录为空，将 `res://Config/mods` 全局化路径下 `base-game` **复制**到 `user://mods/base-game`（`Directory.CreateDirectory` + 文件复制，不依赖 Godot 导出后 res 只读问题——开发期 res 可读）。

```csharp
var modRoot = ProjectSettings.GlobalizePath("user://mods");
EnsureDefaultModsCopied(modRoot);
var registry = new GameDefinitionRegistry();
ContentModPipeline = new ContentModPipeline(modRoot, registry, new GodotContentModLogger(), new NullContentModUserNotifier());
var enabled = GlobalMod.SaveService.LoadOrDefault().EnabledModIds ?? GlobalSaveDto.CreateDefault().EnabledModIds!;
ContentModPipeline.Rebuild(enabled);
```

- [ ] **Step 3: 暴露属性**

```csharp
public ContentModPipeline? ContentModPipeline { get; private set; }
public GameDefinitionRegistry? GameDefinitions => ContentModPipeline?.Registry;
```

- [ ] **Step 4: 实现 `GodotContentModLogger`（同文件或 `Src/frame/content/GodotContentModLogger.cs`）**

使用 `GD.Print` / `GD.PushWarning`；仅在 Godot 运行时调用。

- [ ] **Step 5: 手动冒烟**

启动 Godot → 检查输出无 Fatal → `GameDefinitions.Contains(Card, "strike")` 为 true（可加临时 `GD.Print`）。

---

### Task 9: 全量测试与自检

- [ ] **Step 1: 运行全部单测**

```powershell
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug
```

**期望：** 全部 PASS

- [ ] **Step 2: 对照规格自检**

| 规格条目 | 任务 |
|----------|------|
| 文件夹 Mod + mod.json | Task 3 |
| 手动启用集 + required 扩展 | Task 4, 7 |
| 拓扑 + loadOrder | Task 4 |
| 分表 id 冲突跳过 | Task 2 |
| modId 重复整包跳过 | Task 3 |
| 主菜单刷新 / Run 禁止 | Task 8 仅冷启动；主菜单 `Rebuild` 留 `GlobalModController` 方法占位 |
| IContentModUserNotifier | Task 6 |
| Src/frame 不新建包 | 全文 |

- [ ] **Step 3: Commit（用户要求时）**

```bash
git add Src/frame/content Src/mod/global/Save/GlobalSaveDto.cs Src/MainRoot.cs Config/mods Tests/kemo_card.Ui.Tests Doc/superpowers/plans/2026-05-17-content-mod-manager-implementation-plan.md
git commit -m "feat(content): add content mod pipeline and registry"
```

---

## 后续里程碑（本计划不实现）

- 主菜单 Mod 勾选 UI + `ContentModRequiredExpander` 联动存档 + `Rebuild()`
- `GlobalModController.SetEnabledMods(IReadOnlyList<string> ids)` 供主菜单调用
- Run 中禁止 `Rebuild` 的守卫（`IRunSessionGate`）
- JSON 正文解析（卡牌数值等）

---

## 自检（计划 vs 规格）

**1. Spec coverage：** 规格 1～9 节均有对应 Task；主菜单 UI、Run 守卫为后续里程碑并显式列出。

**2. Placeholder scan：** 无 TBD/TODO；`FindWinnerModId` 在 Task 2 要求用 `ownerById` 字典完整实现。

**3. Type consistency：** `ModContentBundle` 字段顺序与 `ContentRegistryMerger.TryAddAll` 一致；`ContentLoadReport` 在 Merger 与 Pipeline 层合并 skip 列表。

---

## 交付执行方式

计划已保存到：`Doc/superpowers/plans/2026-05-17-content-mod-manager-implementation-plan.md`

**1. Subagent-Driven（推荐）**：每个 Task 单独子代理，任务间复核，迭代快。

**2. Inline Execution**：本会话按检查点批量执行（executing-plans 工作流）。

你想用哪一种？
