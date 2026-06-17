# JsEnv Mod 脚本运行时 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Godot Mono 项目中落地单一 PuerTS `ScriptEnv`（概念上称 VM），支持 Mod 预编译 JS 脚本的按需加载、窄面板沙箱调用、`ContentModPipeline.Rebuild()` 全量重建，并覆盖效果/故事/事件/战斗/敌人 AI 全部 `scriptPath` 类型。

**Architecture:** `ModScriptRuntime` 持有唯一 `Puerts.ScriptEnv` + `ModScriptLoader`（`ILoader`）+ `ModScriptCatalog`（`modId → 磁盘文件夹`）。脚本通过 `ExecuteModule<Func<ScriptContextFacade, JSObject>>(specifier, entry)` 纯同步调用；`ScriptContextFacade` 是唯一传入 JS 的 C# 对象（只读 `Contains` + 宿主 RNG + `Log`）。`ContentModPipeline.Rebuild()` 在注册表合并后调用 `IScriptRuntimeResetter.Recreate()` 并可选预热。业务方通过 `PuertsContentEffectScriptHost` 与各 `*ScriptInvoker` 访问，不暴露裸 VM。

**Tech Stack:** Godot 4.6.1 Mono、.NET 8、C# 12、NUnit 4.x、`Puerts.V8.Complete 3.0.2`（使用 `ScriptEnv`，`JsEnv` 已 Obsolete）、esbuild、`System.Text.Json`。

**规格路径（Task 0 创建）：** `Doc/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md`

---

## 文件结构（创建 / 修改）

| 路径 | 职责 |
|------|------|
| `Doc/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md` | 设计规格（brainstorming 已确认决策） |
| `Src/frame/scripting/IScriptRuntimeResetter.cs` | `Recreate()` 窄接口 |
| `Src/frame/scripting/ModScriptCatalog.cs` | `modId → FolderPath`；Rebuild 时更新 |
| `Src/frame/scripting/ModScriptLoader.cs` | `Puerts.ILoader`；解析 `{modId}/{scriptPath}` |
| `Src/frame/scripting/HostRng.cs` | 由种子派生的可复现 RNG |
| `Src/frame/scripting/ScriptCallContext.cs` | 单次调用上下文（种子、层数、调用方 id 等） |
| `Src/frame/scripting/ScriptContextFacade.cs` | 传入 JS 的窄面板对象 |
| `Src/frame/scripting/ModScriptRuntime.cs` | VM 生命周期、缓存、`Invoke`、实现 `IScriptRuntimeResetter` |
| `Src/frame/scripting/ModScriptInvokeResult.cs` | 调用结果 DTO |
| `Src/frame/scripting/ModScriptResultParser.cs` | 解析 JS 返回值为各业务 DTO |
| `Src/frame/scripting/PuertsContentEffectScriptHost.cs` | `IContentEffectScriptHost` 实现 |
| `Src/frame/scripting/StoryScriptInvoker.cs` | 故事/节点选项脚本 |
| `Src/frame/scripting/EventScriptInvoker.cs` | Script 事件 |
| `Src/frame/scripting/BattleScriptInvoker.cs` | 战斗/波次脚本 |
| `Src/frame/scripting/EnemyAiScriptInvoker.cs` | 敌人 AI 脚本 |
| `Src/frame/scripting/ModScriptPathCollector.cs` | 从 `GameDefinitionStore` 收集全部 scriptPath |
| `Src/frame/scripting/ModScriptPrewarmer.cs` | 可选预热（仅 import，不执行入口） |
| `Src/frame/scripting/ScriptLoadError.cs` | 预热/加载错误条目 |
| `Src/frame/scripting/IModScriptLogger.cs` | 脚本日志抽象 |
| `Src/frame/content/GameDefinitionRegistry.cs` | 增加 `TryGetOwnerModId` |
| `Src/frame/content/ContentRegistryMerger.cs` | 输出 `ownerModIds` 字典 |
| `Src/frame/content/ContentLoadReport.cs` | 增加 `ScriptLoadErrors` |
| `Src/frame/content/IContentModLogger.cs` | 增加 `LogScriptLoadError` |
| `Src/frame/content/ContentModPipeline.cs` | 注入 resetter；Rebuild 末尾 Recreate + 预热 |
| `Src/frame/mvc/ModFactory.cs` | 创建 `ModScriptRuntime` 并接线 |
| `Src/frame/mvc/ModStartupResult.cs` | 暴露 `ModScriptRuntime` |
| `Src/MainRoot.cs` | 持有 `ModScriptRuntime`（可选，便于调试） |
| `Src/typescript/src/mods/base-game/effects/script_demo.mts` | 样例 TS 源 |
| `Src/typescript/esbuild.mjs` | 增加 mod 脚本构建目标 |
| `Config/mods/base-game/content/effects/script_demo.json` | `ExecuteScript` 样例 DTO |
| `Config/mods/base-game/scripts/effects/script_demo.js` | esbuild 输出（提交到仓库） |
| `Tests/kemo_card.Ui.Tests/ModScriptLoaderTests.cs` | Loader 单测 |
| `Tests/kemo_card.Ui.Tests/HostRngTests.cs` | RNG 单测 |
| `Tests/kemo_card.Ui.Tests/ModScriptRuntimeTests.cs` | Runtime 单测 |
| `Tests/kemo_card.Ui.Tests/GameDefinitionRegistryOwnerTests.cs` | Owner 映射单测 |
| `Tests/kemo_card.Ui.Tests/ModScriptPrewarmerTests.cs` | 预热单测 |
| `Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs` | 增加 `AddScript` |

**模块标识符约定：** `{modId}/{scriptPath}`，例如 `base.game/effects/script_demo.js`。Loader 查 `ModScriptCatalog` 得文件夹 `{modRoot}/base-game/scripts/effects/script_demo.js`（注意 **modId ≠ 文件夹名**）。

**JS 入口约定：** 默认 `execute`；`scriptEntry` 可覆盖。签名：`export function execute(ctx) { return { ... }; }`（纯同步）。

---

### Task 0: 写入设计规格

**Files:**
- Create: `Doc/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md`

- [ ] **Step 1: 写入规格文档**

内容须包含（来自已确认 brainstorming）：
- 唯一 `ScriptEnv`、显式注入、非静态单例
- 预编译 JS、Lazy 加载 + 模块缓存、可选预热
- 窄面板 `ScriptContextFacade`、纯同步、Rebuild 全量 Recreate
- `modId → folderPath` 映射必要性
- 各脚本类型返回 JSON 形状（见 Task 6）
- 沙箱局限（3.0 无法彻底移除 `CS.*`）

- [ ] **Step 2: 自检**

确认无 TBD；与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界一致。

- [ ] **Step 3: Commit**

```powershell
git add Doc/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md
git commit -m "docs(scripting): add JsEnv mod scripting design spec"
```

---

### Task 1: 内容定义 Owner 映射（脚本加载前置）

**Files:**
- Modify: `Src/frame/content/ContentRegistryMerger.cs`
- Modify: `Src/frame/content/GameDefinitionRegistry.cs`
- Create: `Tests/kemo_card.Ui.Tests/GameDefinitionRegistryOwnerTests.cs`

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Ui.Tests/GameDefinitionRegistryOwnerTests.cs`：

```csharp
using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class GameDefinitionRegistryOwnerTests
{
	[Test]
	public void TryGetOwnerModId_returns_winner_mod_after_merge()
	{
		var registry = new GameDefinitionRegistry();
		var baseBundle = ContentModTestHelper.EmptyBundle("base.game") with
		{
			Effects = ["fx_a"],
			Definitions = ContentModTestHelper.EmptyBundle("base.game").Definitions with
			{
				Effects = new Dictionary<string, KemoCard.Frame.Content.Definitions.EffectDto>
				{
					["fx_a"] = new() { Id = "fx_a", Kind = KemoCard.Frame.Content.Definitions.EEffectKind.Damage },
				},
			},
		};
		var addonBundle = ContentModTestHelper.EmptyBundle("addon.mod") with { Effects = ["fx_a"] };

		registry.Rebuild(new[] { baseBundle, addonBundle }, out _);

		Assert.That(registry.TryGetOwnerModId(EContentCategory.Effect, "fx_a", out var modId), Is.True);
		Assert.That(modId, Is.EqualTo("base.game"));
	}
}
```

（若 `ModContentBundle`/`ModDefinitionsBundle` 不可 `with`，按实际类型构造最小 bundle。）

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~GameDefinitionRegistryOwnerTests
```

Expected: FAIL — `TryGetOwnerModId` 不存在

- [ ] **Step 3: 实现 owner 映射**

`ContentRegistryMerger.Merge` 增加 `out IReadOnlyDictionary<(EContentCategory Category, string Id), string> ownerModIds`（或在 merger 上新增属性）。`GameDefinitionRegistry`：

```csharp
private Dictionary<(EContentCategory Category, string Id), string> _ownerModIds = new();

public bool TryGetOwnerModId(EContentCategory category, string id, out string modId) =>
	_ownerModIds.TryGetValue((category, id), out modId!);
```

在 `Rebuild` 中：合并后写入 `_ownerModIds`；`RemoveInvalidDefinitions` 时同步移除对应 owner 键。

- [ ] **Step 4: 运行测试确认通过**

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/content/ContentRegistryMerger.cs Src/frame/content/GameDefinitionRegistry.cs Tests/kemo_card.Ui.Tests/GameDefinitionRegistryOwnerTests.cs
git commit -m "feat(content): track winning modId per definition for script loading"
```

---

### Task 2: ModScriptCatalog + ModScriptLoader

**Files:**
- Create: `Src/frame/scripting/ModScriptCatalog.cs`
- Create: `Src/frame/scripting/ModScriptLoader.cs`
- Create: `Tests/kemo_card.Ui.Tests/ModScriptLoaderTests.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs`

- [ ] **Step 1: ContentModTestHelper 增加 AddScript**

```csharp
public static void AddScript(string modDir, string relativePath, string jsSource)
{
	var path = Path.Combine(modDir, "scripts", relativePath);
	Directory.CreateDirectory(Path.GetDirectoryName(path)!);
	File.WriteAllText(path, jsSource);
}
```

- [ ] **Step 2: 编写失败测试**

```csharp
[Test]
public void ReadFile_resolves_modId_to_folder_scripts_path()
{
	var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
	var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
	ContentModTestHelper.AddScript(modDir, "effects/demo.js", "export function execute(ctx) { return { proposedEffects: [] }; }");

	var catalog = new ModScriptCatalog();
	catalog.Rebuild(new[] { new DiscoveredModEntry(modDir, new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }) });

	var loader = new ModScriptLoader(catalog);
	Assert.That(loader.FileExists("base.game/effects/demo.js"), Is.True);
	var source = loader.ReadFile("base.game/effects/demo.js", out var debugPath);
	Assert.That(source, Does.Contain("proposedEffects"));
	Assert.That(debugPath, Does.Contain("effects"));
}
```

- [ ] **Step 3: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~ModScriptLoaderTests
```

- [ ] **Step 4: 实现 ModScriptCatalog + ModScriptLoader**

`ModScriptCatalog.cs`：

```csharp
namespace KemoCard.Frame.Scripting;

public sealed class ModScriptCatalog
{
	private readonly Dictionary<string, string> _folderByModId = new(StringComparer.Ordinal);

	public void Rebuild(IReadOnlyList<DiscoveredModEntry> activeMods)
	{
		_folderByModId.Clear();
		foreach (var entry in activeMods)
		{
			_folderByModId[entry.Manifest.ModId] = entry.FolderPath;
		}
	}

	public bool TryGetFolderPath(string modId, out string folderPath) =>
		_folderByModId.TryGetValue(modId, out folderPath!);
}
```

`ModScriptLoader.cs`：

```csharp
using Puerts;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptLoader : ILoader
{
	private readonly ModScriptCatalog _catalog;

	public ModScriptLoader(ModScriptCatalog catalog) => _catalog = catalog;

	public bool FileExists(string filepath)
	{
		return TryResolve(filepath, out _);
	}

	public string ReadFile(string filepath, out string debugpath)
	{
		if (!TryResolve(filepath, out var fullPath))
		{
			debugpath = filepath;
			return string.Empty;
		}

		debugpath = fullPath;
		return File.ReadAllText(fullPath);
	}

	private bool TryResolve(string specifier, out string fullPath)
	{
		fullPath = string.Empty;
		var slash = specifier.IndexOf('/');
		if (slash <= 0 || slash >= specifier.Length - 1)
		{
			return false;
		}

		var modId = specifier[..slash];
		var scriptPath = specifier[(slash + 1)..];
		if (!_catalog.TryGetFolderPath(modId, out var modFolder))
		{
			return false;
		}

		fullPath = Path.Combine(modFolder, "scripts", scriptPath.Replace('/', Path.DirectorySeparatorChar));
		return File.Exists(fullPath);
	}
}
```

- [ ] **Step 5: 运行测试确认通过**

- [ ] **Step 6: Commit**

```powershell
git add Src/frame/scripting/ModScriptCatalog.cs Src/frame/scripting/ModScriptLoader.cs Tests/kemo_card.Ui.Tests/ModScriptLoaderTests.cs Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs
git commit -m "feat(scripting): add mod script catalog and Puerts loader"
```

---

### Task 3: HostRng + ScriptContextFacade

**Files:**
- Create: `Src/frame/scripting/HostRng.cs`
- Create: `Src/frame/scripting/ScriptCallContext.cs`
- Create: `Src/frame/scripting/ScriptContextFacade.cs`
- Create: `Tests/kemo_card.Ui.Tests/HostRngTests.cs`

- [ ] **Step 1: 编写失败测试**

```csharp
[Test]
public void NextInt_is_deterministic_for_same_seed()
{
	var a = new HostRng(12345, "effect:fx_a");
	var b = new HostRng(12345, "effect:fx_a");
	Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
	Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现 HostRng**

```csharp
namespace KemoCard.Frame.Scripting;

public sealed class HostRng
{
	private readonly Random _random;

	public HostRng(int runSeed, string streamKey)
	{
		var mixed = HashCode.Combine(runSeed, streamKey);
		_random = new Random(mixed);
	}

	public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
```

`ScriptCallContext.cs`（单次调用只读快照）：

```csharp
public sealed class ScriptCallContext
{
	public required int RunSeed { get; init; }
	public required string StreamKey { get; init; }
	public required GameDefinitionRegistry Registry { get; init; }
	public int Layer { get; init; }
	public string? CallerId { get; init; }
}
```

`ScriptContextFacade.cs`：

```csharp
public sealed class ScriptContextFacade
{
	private readonly ScriptCallContext _call;
	private readonly HostRng _rng;
	private readonly IModScriptLogger _logger;

	public ScriptContextFacade(ScriptCallContext call, IModScriptLogger logger)
	{
		_call = call;
		_rng = new HostRng(call.RunSeed, call.StreamKey);
		_logger = logger;
	}

	public bool Contains(string category, string id)
	{
		if (!Enum.TryParse<EContentCategory>(category, ignoreCase: true, out var cat))
		{
			return false;
		}

		return _call.Registry.Contains(cat, id);
	}

	public int NextInt(int minInclusive, int maxExclusive) =>
		_rng.NextInt(minInclusive, maxExclusive);

	public void Log(string message) => _logger.Log(message);
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/scripting/HostRng.cs Src/frame/scripting/ScriptCallContext.cs Src/frame/scripting/ScriptContextFacade.cs Src/frame/scripting/IModScriptLogger.cs Tests/kemo_card.Ui.Tests/HostRngTests.cs
git commit -m "feat(scripting): add host RNG and narrow script context facade"
```

`IModScriptLogger.cs`：

```csharp
namespace KemoCard.Frame.Scripting;

public interface IModScriptLogger
{
	void Log(string message);
}

public sealed class NullModScriptLogger : IModScriptLogger
{
	public void Log(string message) { }
}
```

---

### Task 4: ModScriptRuntime（ScriptEnv 生命周期 + Invoke）

**Files:**
- Create: `Src/frame/scripting/IScriptRuntimeResetter.cs`
- Create: `Src/frame/scripting/ModScriptInvokeResult.cs`
- Create: `Src/frame/scripting/ModScriptRuntime.cs`
- Create: `Tests/kemo_card.Ui.Tests/ModScriptRuntimeTests.cs`

- [ ] **Step 1: 编写失败测试**

在临时 mod 目录写入 JS，构造 runtime，调用 `Invoke`：

```csharp
[Test]
public void Invoke_executes_exported_function_and_returns_success()
{
	var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
	var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
	ContentModTestHelper.AddScript(modDir, "effects/demo.js", """
		export function execute(ctx) {
		  return { proposedEffects: [{ kind: "Damage", params: { amount: 3 } }] };
		}
		""");

	var catalog = new ModScriptCatalog();
	catalog.Rebuild(new[] { new DiscoveredModEntry(modDir, new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }) });

	var registry = new GameDefinitionRegistry();
	var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());
	runtime.Recreate();

	var call = new ScriptCallContext
	{
		RunSeed = 1,
		StreamKey = "effect:demo",
		Registry = registry,
	};
	var result = runtime.Invoke("base.game", "effects/demo.js", "execute", call);

	Assert.That(result.Success, Is.True);
	Assert.That(result.RawReturn, Is.Not.Null);
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现 ModScriptRuntime**

要点：
- 使用 `new ScriptEnv(new Backend.V8.BackendV8(), loader)`（Puerts 3.0）
- `Recreate()`：`Dispose` 旧 env → 新建 → `_entryCache.Clear()`
- 注册 delegate：`UsingFunc<ScriptContextFacade, Puerts.JSObject>()`（若 3.0 API 名称不同，按编译器提示调整）
- `Invoke` 流程：构建 `ScriptContextFacade` → 缓存 key `(modId, scriptPath, entry)` → `ExecuteModule<Func<ScriptContextFacade, JSObject>>(specifier, entry)` → 同步调用 → 包装 `ModScriptInvokeResult`
- try/catch：`Success=false`，填 `Error`
- **不**挂 `_Process` Tick（纯同步）

`IScriptRuntimeResetter.cs`：

```csharp
namespace KemoCard.Frame.Scripting;

public interface IScriptRuntimeResetter
{
	void Recreate();
}
```

`ModScriptInvokeResult.cs`：

```csharp
public sealed class ModScriptInvokeResult
{
	public bool Success { get; init; }
	public Puerts.JSObject? RawReturn { get; init; }
	public string? Error { get; init; }
}
```

- [ ] **Step 4: 增加 Recreate 测试**

修改 mod 脚本后 `Recreate()`，断言新行为生效（改 `amount` 值）。

- [ ] **Step 5: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~ModScriptRuntimeTests
```

- [ ] **Step 6: Commit**

```powershell
git add Src/frame/scripting/IScriptRuntimeResetter.cs Src/frame/scripting/ModScriptInvokeResult.cs Src/frame/scripting/ModScriptRuntime.cs Tests/kemo_card.Ui.Tests/ModScriptRuntimeTests.cs
git commit -m "feat(scripting): add ModScriptRuntime with ScriptEnv invoke and recreate"
```

---

### Task 5: PuertsContentEffectScriptHost

**Files:**
- Create: `Src/frame/scripting/ModScriptResultParser.cs`
- Create: `Src/frame/scripting/PuertsContentEffectScriptHost.cs`
- Create: `Tests/kemo_card.Ui.Tests/PuertsContentEffectScriptHostTests.cs`

- [ ] **Step 1: 编写失败测试**

断言 `TryExecute` 返回 `proposedEffects` 且 `kind`/`params` 解析正确；JS throw 时返回 `false`。

- [ ] **Step 2: 实现 ModScriptResultParser（效果段）**

```csharp
public static bool TryParseProposedEffects(
	Puerts.JSObject raw,
	out IReadOnlyList<Dictionary<string, object>> proposedEffects,
	out string? error)
{
	proposedEffects = Array.Empty<Dictionary<string, object>>();
	error = null;
	// 读取 raw["proposedEffects"] 数组；每项为 JSObject → Dictionary<string, object>
	// 必须含 kind (string)；params 可选
	return true;
}
```

- [ ] **Step 3: 实现 PuertsContentEffectScriptHost**

```csharp
public sealed class PuertsContentEffectScriptHost : IContentEffectScriptHost
{
	private readonly ModScriptRuntime _runtime;
	private readonly GameDefinitionRegistry _registry;

	public bool TryExecute(
		string modId,
		string scriptPath,
		string scriptEntry,
		IReadOnlyDictionary<string, object>? context,
		out IReadOnlyList<Dictionary<string, object>> proposedEffects)
	{
		proposedEffects = Array.Empty<Dictionary<string, object>>();
		var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
		var streamKey = $"effect:{modId}:{scriptPath}:{entry}";
		var call = new ScriptCallContext
		{
			RunSeed = ExtractInt(context, "runSeed", 0),
			StreamKey = streamKey,
			Registry = _registry,
			CallerId = ExtractString(context, "effectId"),
		};

		var result = _runtime.Invoke(modId, scriptPath, entry, call);
		if (!result.Success || result.RawReturn is null)
		{
			return false;
		}

		return ModScriptResultParser.TryParseProposedEffects(result.RawReturn, out proposedEffects, out _);
	}
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/scripting/ModScriptResultParser.cs Src/frame/scripting/PuertsContentEffectScriptHost.cs Tests/kemo_card.Ui.Tests/PuertsContentEffectScriptHostTests.cs
git commit -m "feat(scripting): implement content effect script host"
```

---

### Task 6: 各 scriptPath 类型 Invoker + 返回形状

**Files:**
- Create: `Src/frame/scripting/StoryScriptInvoker.cs`
- Create: `Src/frame/scripting/EventScriptInvoker.cs`
- Create: `Src/frame/scripting/BattleScriptInvoker.cs`
- Create: `Src/frame/scripting/EnemyAiScriptInvoker.cs`
- Modify: `Src/frame/scripting/ModScriptResultParser.cs`

**JS 返回约定（写入 spec 与各 Invoker 注释）：**

| 类型 | 返回字段 | C# 结果类型 |
|------|----------|-------------|
| 效果 | `{ proposedEffects: [...] }` | `IReadOnlyList<Dictionary<string, object>>` |
| 故事选项 | `{ options: [{ optionId, labelId, next? }] }` | `IReadOnlyList<StoryScriptOption>`（新建 record） |
| Script 事件 | `{ pages?, options? }` | `EventScriptResult` |
| 战斗/波次 | `{ hooks?: string[] }` 或 `{ phase: string }` | `BattleScriptResult`（首版最小：返回字典供上层解释） |
| 敌人 AI | `{ skillId: string }` 或 `{ weights: { id: number } }` | `EnemyAiScriptResult` |

- [ ] **Step 1: 定义结果 record**（同文件或 `ModScriptResults.cs`）

- [ ] **Step 2: 扩展 ModScriptResultParser** 各 `TryParse*` 方法

- [ ] **Step 3: 各 Invoker 委托 `ModScriptRuntime.Invoke` + Parser**

每个 Invoker 构造注入 `ModScriptRuntime` + `GameDefinitionRegistry`；通过 `TryGetOwnerModId` 解析 modId（调用方也可显式传入）。

- [ ] **Step 4: 单测** — 至少 Story + Event 各一条 happy path + throw 软失败

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/scripting/StoryScriptInvoker.cs Src/frame/scripting/EventScriptInvoker.cs Src/frame/scripting/BattleScriptInvoker.cs Src/frame/scripting/EnemyAiScriptInvoker.cs Src/frame/scripting/ModScriptResultParser.cs Tests/kemo_card.Ui.Tests/ModScriptInvokerTests.cs
git commit -m "feat(scripting): add typed script invokers for story event battle and AI"
```

---

### Task 7: ContentLoadReport 脚本错误 + 预热

**Files:**
- Create: `Src/frame/scripting/ScriptLoadError.cs`
- Create: `Src/frame/scripting/ModScriptPathCollector.cs`
- Create: `Src/frame/scripting/ModScriptPrewarmer.cs`
- Modify: `Src/frame/content/ContentLoadReport.cs`
- Modify: `Src/frame/content/IContentModLogger.cs`
- Modify: `Src/frame/content/NullContentModLogger.cs`
- Modify: `Src/frame/content/GodotContentModLogger.cs`
- Create: `Tests/kemo_card.Ui.Tests/ModScriptPrewarmerTests.cs`

- [ ] **Step 1: ScriptLoadError + 扩展 ContentLoadReport**

```csharp
public sealed record ScriptLoadError(string ModId, string ScriptPath, string Message);

public ContentLoadReport(..., IReadOnlyList<ScriptLoadError> scriptLoadErrors)

public bool HasIssues => ... || ScriptLoadErrors.Count > 0;
```

更新所有 `new ContentLoadReport(...)` 调用点，无错误处传 `Array.Empty<ScriptLoadError>()`。

- [ ] **Step 2: ModScriptPathCollector** — 遍历 Store 中 Effect(EventKind Script)/Event/Battle/Enemy/Skill/Buff 的 scriptPath 与 waveScriptPath；结合 `TryGetOwnerModId` 产出 `(modId, path)` 列表。

- [ ] **Step 3: ModScriptPrewarmer** — 对每条路径调用 `runtime.TryLoadModule(modId, path)`（在 Runtime 上新增仅 import 的公开方法，不执行 entry）；捕获异常写入 `ScriptLoadError`。

- [ ] **Step 4: 测试** — 故意写语法错误 JS，预热后 `ScriptLoadErrors` 非空

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/scripting/ScriptLoadError.cs Src/frame/scripting/ModScriptPathCollector.cs Src/frame/scripting/ModScriptPrewarmer.cs Src/frame/content/ContentLoadReport.cs Src/frame/content/IContentModLogger.cs Tests/kemo_card.Ui.Tests/ModScriptPrewarmerTests.cs
git commit -m "feat(scripting): add script prewarm and ScriptLoadErrors report bucket"
```

---

### Task 8: Pipeline / ModFactory / MainRoot 接线

**Files:**
- Modify: `Src/frame/content/ContentModPipeline.cs`
- Modify: `Src/frame/mvc/ModFactory.cs`
- Modify: `Src/frame/mvc/ModStartupResult.cs`
- Modify: `Src/MainRoot.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentModPipelineTests.cs`

- [ ] **Step 1: ContentModPipeline 构造增加依赖**

```csharp
public ContentModPipeline(
	string modRootDirectory,
	GameDefinitionRegistry registry,
	IContentModLogger logger,
	IContentModUserNotifier notifier,
	IScriptRuntimeResetter scriptRuntimeResetter,
	ModScriptCatalog scriptCatalog,
	ModScriptPrewarmer? prewarmer = null,
	...)
```

`Rebuild` 末尾顺序：
1. `Registry.Rebuild(...)`
2. `scriptCatalog.Rebuild(activation.OrderedActiveMods)`
3. `scriptRuntimeResetter.Recreate()`
4. 若 `prewarmer` 非 null：`prewarmer.Warm(...)` 合并 `ScriptLoadErrors` 到 finalReport
5. `_notifier.OnModLoadCompleted(finalReport)`

- [ ] **Step 2: ModFactory 创建共享实例**

```csharp
var catalog = new ModScriptCatalog();
var scriptLogger = new NullModScriptLogger();
var scriptRuntime = new ModScriptRuntime(catalog, registry, scriptLogger);
var prewarmer = new ModScriptPrewarmer(scriptRuntime, registry);
var pipeline = new ContentModPipeline(..., scriptRuntime, catalog, prewarmer);
// 首次 Rebuild 前 catalog 为空；Rebuild 内会填充
```

`ModStartupResult` 增加 `ModScriptRuntime ScriptRuntime { get; init; }`。

- [ ] **Step 3: MainRoot 持有（可选调试）**

```csharp
public ModScriptRuntime? ScriptRuntime => _modResult?.ScriptRuntime;
```

- [ ] **Step 4: 更新 ContentModPipelineTests** — 传入 `NullScriptRuntimeResetter` 桩或真实 runtime

- [ ] **Step 5: 运行全量测试**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release
```

Expected: PASS

- [ ] **Step 6: Commit**

```powershell
git add Src/frame/content/ContentModPipeline.cs Src/frame/mvc/ModFactory.cs Src/frame/mvc/ModStartupResult.cs Src/MainRoot.cs Tests/kemo_card.Ui.Tests/ContentModPipelineTests.cs
git commit -m "feat(scripting): wire script runtime into mod bootstrap and rebuild pipeline"
```

---

### Task 9: esbuild + base-game 样例

**Files:**
- Create: `Src/typescript/src/mods/base-game/effects/script_demo.mts`
- Modify: `Src/typescript/esbuild.mjs`
- Create: `Config/mods/base-game/content/effects/script_demo.json`
- Create: `Config/mods/base-game/scripts/effects/script_demo.js`（build 产出，提交）

- [ ] **Step 1: 编写 TS 样例**

`Src/typescript/src/mods/base-game/effects/script_demo.mts`：

```typescript
export function execute(ctx: { nextInt(min: number, max: number): number }) {
    const amount = ctx.nextInt(4, 8);
    return {
        proposedEffects: [{ kind: 'Damage', params: { amount } }],
    };
}
```

- [ ] **Step 2: 扩展 esbuild.mjs**

增加第二个 build 目标：`entryPoints` 来自 `src/mods/**/*.mts`，`outdir` 映射到 `Config/mods/base-game/scripts/`（保持相对路径）。

- [ ] **Step 3: 运行构建**

```powershell
cd Src/typescript
npm run build
```

Expected: 生成 `Config/mods/base-game/scripts/effects/script_demo.js`

- [ ] **Step 4: 添加 effect DTO**

`Config/mods/base-game/content/effects/script_demo.json`：

```json
{
  "kind": "ExecuteScript",
  "scriptPath": "effects/script_demo.js",
  "scriptEntry": "execute",
  "tags": ["script", "demo"]
}
```

- [ ] **Step 5: 手动冒烟**（Godot 运行或单测调用 `PuertsContentEffectScriptHost` + 真实 bundle）

- [ ] **Step 6: Commit**

```powershell
git add Src/typescript/esbuild.mjs Src/typescript/src/mods/base-game/effects/script_demo.mts Config/mods/base-game/content/effects/script_demo.json Config/mods/base-game/scripts/effects/script_demo.js
git commit -m "feat(scripting): add base-game execute-script demo and esbuild mod pipeline"
```

---

### Task 10: 全量验证

- [ ] **Step 1: 运行全部单测**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release
```

Expected: PASS

- [ ] **Step 2: TS 类型检查**

```powershell
cd Src/typescript
npm run typecheck
```

Expected: PASS

- [ ] **Step 3: 确认 Rebuild 后 VM 重建**

在 `ModScriptRuntimeTests` 或 pipeline 集成测试中：Rebuild 两次，第二次 mod 脚本变更后行为更新。

---

## 规格覆盖自检

| 规格要求 | 对应 Task |
|----------|-----------|
| 唯一 VM、显式注入 | Task 4, 8 |
| 预编译 JS | Task 9 |
| Lazy + 缓存 | Task 4 |
| 可选预热 | Task 7 |
| 窄面板 ctx | Task 3, 4 |
| 纯同步 | Task 4（无 Tick） |
| Rebuild Recreate | Task 4, 8 |
| modId→folder | Task 2 |
| Owner modId | Task 1 |
| 全部 scriptPath 类型 | Task 6 |
| ExecuteScript 端到端 | Task 5, 9 |
| ScriptLoadErrors | Task 7 |

## 占位扫描

无 TBD / TODO /「后续实现」类步骤；每 Task 含具体路径与代码片段。

---

## Execution Handoff

Plan complete and saved to `Doc/superpowers/plans/2026-06-17-jsenv-mod-scripting-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — 每个 Task 派发独立 subagent，Task 间人工/主 agent 审查
2. **Inline Execution** — 本会话用 executing-plans 按 Task 批量执行并在检查点暂停

**Which approach?**
