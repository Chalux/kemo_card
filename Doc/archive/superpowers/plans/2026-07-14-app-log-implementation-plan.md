# AppLog 开发向日志 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 `IAppLog` / `AppLog` / `GodotAppLog`，领域 Logger 委托到 `IAppLog`，并将 `Src/` 内诊断用 `GD.PushError`/`PushWarning` 一次迁完。

**Architecture:** frame 层提供可测的 `IAppLog` + 静态门面 `AppLog.Configure`；`GodotAppLog` 在 `fixed/godot` 映射到 Godot 输出；领域 Logger 保留接口、构造注入 `IAppLog`；散落调用点改用 `AppLog.*`。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit

**Spec:** `Doc/superpowers/specs/2026-07-14-app-log-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/logging/IAppLog.cs` | 日志接口 |
| `Src/frame/logging/NullAppLog.cs` | 空实现 + `Instance` |
| `Src/frame/logging/AppLog.cs` | 静态门面 `Configure` / `Debug`/`Info`/`Warning`/`Error` |
| `Src/fixed/godot/GodotAppLog.cs` | Godot sink（Push*/Print） |
| `Src/fixed/godot/GodotEventDispatcherLogger.cs` | 注入 `IAppLog`，`Error(..., "Mvc")` |
| `Src/frame/content/GodotContentModLogger.cs` | 去掉 `GD.*`，注入 `IAppLog`，category `"ContentMod"` |
| `Src/frame/scripting/AppLogModScriptLogger.cs` | 新增：`Log` → `Info(..., "Script")` |
| `Src/MainRoot.cs` | 最先 `AppLog.Configure`；警告改 `AppLog` |
| `Src/mod/ModFactory.cs` | KeywordHandler + 注入 Content/Script logger |
| 若干 frame/mod/fixed 调用点 | `GD.Push*` → `AppLog.*` |
| `Tests/kemo_card.Ui.Tests/AppLogTests.cs` | 门面 + Null 行为 |
| `Tests/kemo_card.Ui.Tests/DomainLoggerAppLogAdapterTests.cs` | 领域适配器委托断言 |
| `Tests/kemo_card.Ui.Tests/RecordingAppLog.cs` | 测试用假 logger（可放测试项目内） |

**不改：** Toast / 战斗日志 / `IContentModUserNotifier` / 内容加载器布局。

---

### Task 1: IAppLog + NullAppLog + AppLog 门面（TDD）

**Files:**
- Create: `Src/frame/logging/IAppLog.cs`
- Create: `Src/frame/logging/NullAppLog.cs`
- Create: `Src/frame/logging/AppLog.cs`
- Create: `Tests/kemo_card.Ui.Tests/RecordingAppLog.cs`
- Create: `Tests/kemo_card.Ui.Tests/AppLogTests.cs`

- [ ] **Step 1: 写失败测试（RecordingAppLog + AppLog 行为）**

`Tests/kemo_card.Ui.Tests/RecordingAppLog.cs`：

```csharp
using System.Collections.Generic;
using KemoCard.Frame.Logging;

namespace KemoCard.Ui.Tests;

public enum AppLogLevel
{
	Debug,
	Info,
	Warning,
	Error,
}

public sealed record AppLogEntry(AppLogLevel Level, string Message, string? Category);

public sealed class RecordingAppLog : IAppLog
{
	public List<AppLogEntry> Entries { get; } = new();

	public void Debug(string message, string? category = null) =>
		Entries.Add(new AppLogEntry(AppLogLevel.Debug, message, category));

	public void Info(string message, string? category = null) =>
		Entries.Add(new AppLogEntry(AppLogLevel.Info, message, category));

	public void Warning(string message, string? category = null) =>
		Entries.Add(new AppLogEntry(AppLogLevel.Warning, message, category));

	public void Error(string message, string? category = null) =>
		Entries.Add(new AppLogEntry(AppLogLevel.Error, message, category));
}
```

`Tests/kemo_card.Ui.Tests/AppLogTests.cs`：

```csharp
using KemoCard.Frame.Logging;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class AppLogTests
{
	[TearDown]
	public void TearDown()
	{
		AppLog.Configure(NullAppLog.Instance);
	}

	[Test]
	public void Configure_forwards_calls_to_implementation()
	{
		var recording = new RecordingAppLog();
		AppLog.Configure(recording);

		AppLog.Debug("d", "Cat");
		AppLog.Info("i");
		AppLog.Warning("w", "UI");
		AppLog.Error("e", "MainRoot");

		Assert.That(recording.Entries, Has.Count.EqualTo(4));
		Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Debug, "d", "Cat")));
		Assert.That(recording.Entries[1], Is.EqualTo(new AppLogEntry(AppLogLevel.Info, "i", null)));
		Assert.That(recording.Entries[2], Is.EqualTo(new AppLogEntry(AppLogLevel.Warning, "w", "UI")));
		Assert.That(recording.Entries[3], Is.EqualTo(new AppLogEntry(AppLogLevel.Error, "e", "MainRoot")));
	}

	[Test]
	public void Without_configure_does_not_throw()
	{
		AppLog.Configure(NullAppLog.Instance);
		Assert.DoesNotThrow(() =>
		{
			AppLog.Debug("x");
			AppLog.Info("x");
			AppLog.Warning("x");
			AppLog.Error("x");
		});
	}

	[Test]
	public void Configure_null_throws()
	{
		Assert.Throws<System.ArgumentNullException>(() => AppLog.Configure(null!));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~AppLogTests" -v n`

Expected: FAIL（缺少 `KemoCard.Frame.Logging` 类型）

- [ ] **Step 3: 实现 IAppLog / NullAppLog / AppLog**

`Src/frame/logging/IAppLog.cs`：

```csharp
namespace KemoCard.Frame.Logging;

public interface IAppLog
{
	void Debug(string message, string? category = null);
	void Info(string message, string? category = null);
	void Warning(string message, string? category = null);
	void Error(string message, string? category = null);
}
```

`Src/frame/logging/NullAppLog.cs`：

```csharp
namespace KemoCard.Frame.Logging;

public sealed class NullAppLog : IAppLog
{
	public static NullAppLog Instance { get; } = new();

	public void Debug(string message, string? category = null)
	{
	}

	public void Info(string message, string? category = null)
	{
	}

	public void Warning(string message, string? category = null)
	{
	}

	public void Error(string message, string? category = null)
	{
	}
}
```

`Src/frame/logging/AppLog.cs`：

```csharp
using System;

namespace KemoCard.Frame.Logging;

public static class AppLog
{
	private static IAppLog _implementation = NullAppLog.Instance;

	public static void Configure(IAppLog implementation)
	{
		ArgumentNullException.ThrowIfNull(implementation);
		_implementation = implementation;
	}

	public static void Debug(string message, string? category = null) =>
		_implementation.Debug(message, category);

	public static void Info(string message, string? category = null) =>
		_implementation.Info(message, category);

	public static void Warning(string message, string? category = null) =>
		_implementation.Warning(message, category);

	public static void Error(string message, string? category = null) =>
		_implementation.Error(message, category);
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~AppLogTests" -v n`

Expected: PASS

- [ ] **Step 5: 格式化改动过的 .cs，然后 Commit**

```bash
dotnet format kemo_card.csproj --include Src/frame/logging/IAppLog.cs Src/frame/logging/NullAppLog.cs Src/frame/logging/AppLog.cs
dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include Tests/kemo_card.Ui.Tests/RecordingAppLog.cs Tests/kemo_card.Ui.Tests/AppLogTests.cs
git add Src/frame/logging Tests/kemo_card.Ui.Tests/RecordingAppLog.cs Tests/kemo_card.Ui.Tests/AppLogTests.cs
git commit -m "feat(logging): 新增 IAppLog 与 AppLog 静态门面"
```

---

### Task 2: GodotAppLog + MainRoot 最先 Configure

**Files:**
- Create: `Src/fixed/godot/GodotAppLog.cs`
- Modify: `Src/MainRoot.cs`

- [ ] **Step 1: 实现 GodotAppLog**

```csharp
using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Fixed.Godot;

public sealed class GodotAppLog : IAppLog
{
	private readonly bool _enableVerbose;

	public GodotAppLog(bool? enableVerbose = null)
	{
		_enableVerbose = enableVerbose ?? OS.IsDebugBuild();
	}

	public void Debug(string message, string? category = null)
	{
		if (_enableVerbose)
			GD.Print(Format(message, category));
	}

	public void Info(string message, string? category = null)
	{
		if (_enableVerbose)
			GD.Print(Format(message, category));
	}

	public void Warning(string message, string? category = null) =>
		GD.PushWarning(Format(message, category));

	public void Error(string message, string? category = null) =>
		GD.PushError(Format(message, category));

	private static string Format(string message, string? category) =>
		string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
}
```

说明：单测不直接测 `GodotAppLog`（依赖 Godot 运行时）；格式与级别由适配器测试 + 手工启动验证。

- [ ] **Step 2: MainRoot 调整启动顺序**

在 `BootstrapServices()` / `_Ready` 中保证 **最先** Configure。推荐结构：

```csharp
public override void _Ready()
{
	var appLog = new GodotAppLog();
	AppLog.Configure(appLog);

	BootstrapServices();
	InitUIManager();
	EnsureKeywordTipLayer();
	_ = GlobalModController.OpenMenuAsync();

	EventDispatcher.Configure(new GodotEventDispatcherLogger(appLog));
}
```

并改 `EnsureKeywordTipLayer`：

```csharp
AppLog.Warning($"未找到词条提示层场景 {KeywordTipLayerPath}", "MainRoot");
```

（Task 2 可先只做 Configure + Keyword 警告；若 `GodotEventDispatcherLogger` 尚未改构造，可暂时 `new GodotEventDispatcherLogger()`，Task 3 再改注入——**本 Task 若编译失败则与 Task 3 Step 1 合并完成 EventDispatcher 适配后再编译。**）

推荐本 Task 结束时编译通过：可先把 `GodotEventDispatcherLogger` 改为可选注入（见 Task 3），或本 Task 只加 `GodotAppLog` + `AppLog.Configure`，`EventDispatcher.Configure` 仍用旧构造，Task 3 再改。

**选定：** Task 2 仅：`GodotAppLog` + `AppLog.Configure(new GodotAppLog())` 放在 `_Ready` 最前；`EnsureKeywordTipLayer` 改 `AppLog.Warning`；`EventDispatcher.Configure` 仍 `new GodotEventDispatcherLogger()` 直到 Task 3。

```csharp
public override void _Ready()
{
	AppLog.Configure(new GodotAppLog());

	BootstrapServices();
	InitUIManager();
	EnsureKeywordTipLayer();
	_ = GlobalModController.OpenMenuAsync();

	EventDispatcher.Configure(new GodotEventDispatcherLogger());
}
```

需要 `using KemoCard.Frame.Logging;`。

- [ ] **Step 3: 编译确认**

Run: `dotnet build kemo_card.csproj -v q`

Expected: 成功（0 Error）

- [ ] **Step 4: 格式化并 Commit**

```bash
dotnet format kemo_card.csproj --include Src/fixed/godot/GodotAppLog.cs Src/MainRoot.cs
git add Src/fixed/godot/GodotAppLog.cs Src/MainRoot.cs
git commit -m "feat(logging): 新增 GodotAppLog 并在 MainRoot 最先配置"
```

---

### Task 3: 领域 Logger 适配（TDD）

**Files:**
- Modify: `Src/fixed/godot/GodotEventDispatcherLogger.cs`
- Modify: `Src/frame/content/GodotContentModLogger.cs`
- Create: `Src/frame/scripting/AppLogModScriptLogger.cs`
- Modify: `Src/mod/ModFactory.cs`
- Modify: `Src/MainRoot.cs`（EventDispatcher 注入 appLog）
- Modify: `Src/mod/global/Def/BuiltinKeywords.cs`（若仍兜底 GD）
- Modify: `Src/frame/content/keywords/KeywordCatalog.cs`（注释）
- Create: `Tests/kemo_card.Ui.Tests/DomainLoggerAppLogAdapterTests.cs`

- [ ] **Step 1: 写失败测试**

`DomainLoggerAppLogAdapterTests.cs`：

```csharp
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class DomainLoggerAppLogAdapterTests
{
	[Test]
	public void EventDispatcherLogger_forwards_error_with_Mvc_category()
	{
		var recording = new RecordingAppLog();
		var logger = new GodotEventDispatcherLogger(recording);

		logger.LogError("boom");

		Assert.That(recording.Entries, Has.Count.EqualTo(1));
		Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Error, "boom", "Mvc")));
	}

	[Test]
	public void ContentModLogger_uses_ContentMod_category_without_duplicate_prefix()
	{
		var recording = new RecordingAppLog();
		var logger = new GodotContentModLogger(recording);

		logger.LogSkipped(new ModSkipEntry("mod.a", ModSkipReason.InvalidManifest, "bad"));
		logger.LogConflict(new ContentIdConflictEntry(EContentCategory.Cards, "strike", "winner", "loser"));
		logger.LogValidationError(new ContentDefinitionValidationError(EContentCategory.Cards, "strike", "missing"));
		logger.LogScriptLoadError(new ScriptLoadError("mod.a", "main.js", "syntax"));

		Assert.That(recording.Entries, Has.Count.EqualTo(4));
		Assert.That(recording.Entries, Has.All.Property("Category").EqualTo("ContentMod"));
		Assert.That(recording.Entries, Has.All.Property("Level").EqualTo(AppLogLevel.Warning));
		Assert.That(recording.Entries[0].Message, Does.StartWith("Skipped "));
		Assert.That(recording.Entries[0].Message, Does.Not.Contain("[ContentMod]"));
		Assert.That(recording.Entries[1].Message, Does.Contain("Id conflict"));
		Assert.That(recording.Entries[2].Message, Does.Contain("Validation"));
		Assert.That(recording.Entries[3].Message, Does.Contain("Script load"));
	}

	[Test]
	public void ModScriptLogger_forwards_as_Info_with_Script_category()
	{
		var recording = new RecordingAppLog();
		var logger = new AppLogModScriptLogger(recording);

		logger.Log("hello");

		Assert.That(recording.Entries, Has.Count.EqualTo(1));
		Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Info, "hello", "Script")));
	}
}
```

若 `ModSkipReason` / `EContentCategory` 命名空间需补 `using`，按编译器提示添加。

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~DomainLoggerAppLogAdapterTests" -v n`

Expected: FAIL（构造签名 / 类型不存在）

- [ ] **Step 3: 实现适配器**

`GodotEventDispatcherLogger.cs`：

```csharp
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;

namespace KemoCard.Fixed.Godot;

public sealed class GodotEventDispatcherLogger : IEventDispatcherLogger
{
	private readonly IAppLog _log;

	public GodotEventDispatcherLogger(IAppLog log)
	{
		_log = log;
	}

	public void LogError(string message) => _log.Error(message, "Mvc");
}
```

`GodotContentModLogger.cs`（去掉 `using Godot`）：

```csharp
using System;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Content;

public sealed class GodotContentModLogger : IContentModLogger
{
	private readonly IAppLog _log;

	public GodotContentModLogger(IAppLog log)
	{
		_log = log ?? throw new ArgumentNullException(nameof(log));
	}

	public void LogSkipped(ModSkipEntry entry)
	{
		_log.Warning($"Skipped {entry.ModId}: {entry.Reason} {entry.Detail}".Trim(), "ContentMod");
	}

	public void LogConflict(ContentIdConflictEntry entry)
	{
		_log.Warning(
			$"Id conflict {entry.Category}/{entry.ContentId}: kept {entry.WinnerModId}, skipped {entry.LoserModId}",
			"ContentMod");
	}

	public void LogValidationError(ContentDefinitionValidationError entry)
	{
		_log.Warning(
			$"Validation {entry.Category}/{entry.DefinitionId}: {entry.Message}",
			"ContentMod");
	}

	public void LogScriptLoadError(ScriptLoadError entry)
	{
		_log.Warning($"Script load {entry.ModId}/{entry.ScriptPath}: {entry.Message}", "ContentMod");
	}
}
```

`AppLogModScriptLogger.cs`：

```csharp
using System;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Scripting;

public sealed class AppLogModScriptLogger : IModScriptLogger
{
	private readonly IAppLog _log;

	public AppLogModScriptLogger(IAppLog log)
	{
		_log = log ?? throw new ArgumentNullException(nameof(log));
	}

	public void Log(string message) => _log.Info(message, "Script");
}
```

- [ ] **Step 4: 接线 ModFactory + MainRoot**

`ModFactory.RegisterBuiltinKeywords`：

```csharp
KeywordCatalog.Shared.WarningHandler = msg => AppLog.Warning(msg, "Keyword");
```

`BootstrapContentMods` 中：

```csharp
var appLog = /* 见下 */;
```

因 `ModFactory` 无现成 `IAppLog` 参数，本轮约定：**领域适配器使用静态门面背后的实现不便取出**。两种接法：

**选定 A（推荐）：** `ModFactory.Bootstrap` / `BootstrapContentMods` 增加可选参数，或在方法内使用包装器：

```csharp
// 适配器持有 IAppLog；Bootstrap 内用「转发到 AppLog 静态门面」的桥接实现，避免 ModFactory 依赖 GodotAppLog。
internal sealed class StaticAppLogBridge : IAppLog
{
	public void Debug(string message, string? category = null) => AppLog.Debug(message, category);
	public void Info(string message, string? category = null) => AppLog.Info(message, category);
	public void Warning(string message, string? category = null) => AppLog.Warning(message, category);
	public void Error(string message, string? category = null) => AppLog.Error(message, category);
}
```

放到 `Src/frame/logging/StaticAppLogBridge.cs`（可 `public` 供工厂使用）。然后：

```csharp
var appLog = new StaticAppLogBridge();
var scriptRuntime = new ModScriptRuntime(catalog, registry, new AppLogModScriptLogger(appLog));
var pipeline = new ContentModPipeline(
	...,
	new GodotContentModLogger(appLog),
	...);
```

`MainRoot`：

```csharp
var appLog = new GodotAppLog();
AppLog.Configure(appLog);
// ...
EventDispatcher.Configure(new GodotEventDispatcherLogger(appLog));
```

`BuiltinKeywords.cs` 中若有 `GD.PushWarning` 兜底，改为 `AppLog.Warning(msg, "Keyword")`。

`KeywordCatalog.cs` 注释改为：`运行时可接到 AppLog.Warning`。

- [ ] **Step 5: 跑适配器测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~DomainLoggerAppLogAdapterTests|FullyQualifiedName~AppLogTests" -v n`

Expected: PASS

- [ ] **Step 6: 回归内容/脚本相关测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ContentModPipelineTests|FullyQualifiedName~ModScriptRuntimeTests|FullyQualifiedName~EventDispatcherTests" -v n`

Expected: PASS

- [ ] **Step 7: 格式化并 Commit**

```bash
dotnet format kemo_card.csproj --include Src/fixed/godot/GodotEventDispatcherLogger.cs Src/frame/content/GodotContentModLogger.cs Src/frame/scripting/AppLogModScriptLogger.cs Src/frame/logging/StaticAppLogBridge.cs Src/mod/ModFactory.cs Src/MainRoot.cs Src/mod/global/Def/BuiltinKeywords.cs Src/frame/content/keywords/KeywordCatalog.cs
dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include Tests/kemo_card.Ui.Tests/DomainLoggerAppLogAdapterTests.cs
git add Src/fixed/godot/GodotEventDispatcherLogger.cs Src/frame/content/GodotContentModLogger.cs Src/frame/scripting/AppLogModScriptLogger.cs Src/frame/logging/StaticAppLogBridge.cs Src/mod/ModFactory.cs Src/MainRoot.cs Src/mod/global/Def/BuiltinKeywords.cs Src/frame/content/keywords/KeywordCatalog.cs Tests/kemo_card.Ui.Tests/DomainLoggerAppLogAdapterTests.cs
git commit -m "feat(logging): 领域 Logger 委托到 IAppLog"
```

---

### Task 4: 迁移 frame / fixed 中的 GD.Push*

**Files:**
- Modify: `Src/frame/util/UIResourceLoader.cs`
- Modify: `Src/frame/util/GodotMainThreadSyncContext.cs`
- Modify: `Src/frame/ui/UIRuntimeData.cs`
- Modify: `Src/frame/ui/UIRuntimeRegistry.cs`
- Modify: `Src/frame/ui/UiManager.cs`
- Modify: `Src/frame/ui/States/UILoadStateHandler.cs`
- Modify: `Src/frame/ui/States/UIPreLoadStateHandler.cs`
- Modify: `Src/fixed/godot/GodotContentModTranslationLoader.cs`

- [ ] **Step 1: 逐文件替换**

规则：
- `GD.PushError(...)` → `AppLog.Error(..., "UI")`（UI 相关）
- `GodotMainThreadSyncContext`：`AppLog.Error(..., "SyncContext")`
- `GodotContentModTranslationLoader`：去掉 message 内 `[ContentMod]` 前缀，改 `AppLog.Warning(..., "ContentMod")`
- 每个文件加 `using KemoCard.Frame.Logging;`
- 若文件因此不再需要 `using Godot;` 且无其它 Godot 符号，再删该 using（多数 UI 文件仍依赖 Godot，保留）

示例（`UIResourceLoader`）：

```csharp
AppLog.Error("资源路径不能为空.", "UIResourceLoader");
AppLog.Error($"请求加载资源失败 '{path}': {err}.", "UIResourceLoader");
AppLog.Error($"加载资源失败 '{path}': {status}.", "UIResourceLoader");
```

`UiManager` / `UILoadStateHandler` / `UIPreLoadStateHandler` / `UIRuntimeRegistry` / `UIRuntimeData`：category 用 `"UI"`。

Translation loader：

```csharp
AppLog.Warning($"Mod '{modId}': cannot localize translation path '{absolutePath}'.", "ContentMod");
AppLog.Warning($"Mod '{modId}': failed to load translation '{resourcePath}'.", "ContentMod");
```

- [ ] **Step 2: 编译**

Run: `dotnet build kemo_card.csproj -v q`

Expected: 成功

- [ ] **Step 3: 跑 UI 相关测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~UiManager|FullyQualifiedName~UiFramework|FullyQualifiedName~UiRegistry|FullyQualifiedName~UiStateMachine" -v n`

Expected: PASS

- [ ] **Step 4: 格式化并 Commit**

```bash
dotnet format kemo_card.csproj --include Src/frame/util/UIResourceLoader.cs Src/frame/util/GodotMainThreadSyncContext.cs Src/frame/ui/UIRuntimeData.cs Src/frame/ui/UIRuntimeRegistry.cs Src/frame/ui/UiManager.cs Src/frame/ui/States/UILoadStateHandler.cs Src/frame/ui/States/UIPreLoadStateHandler.cs Src/fixed/godot/GodotContentModTranslationLoader.cs
git add Src/frame/util/UIResourceLoader.cs Src/frame/util/GodotMainThreadSyncContext.cs Src/frame/ui/UIRuntimeData.cs Src/frame/ui/UIRuntimeRegistry.cs Src/frame/ui/UiManager.cs Src/frame/ui/States/UILoadStateHandler.cs Src/frame/ui/States/UIPreLoadStateHandler.cs Src/fixed/godot/GodotContentModTranslationLoader.cs
git commit -m "refactor(logging): frame/fixed 诊断日志改走 AppLog"
```

---

### Task 5: 迁移 mod UI 调用点

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseButton.cs`
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.cs`
- Modify: `Src/mod/global/Ui/Comp/BaseDlgComp.cs`
- Modify: `Src/mod/global/Ui/VirtualList.cs`
- Modify: `Src/mod/global/Ui/Tip/KeywordTipService.cs`
- Modify: `Src/mod/global/Ui/CardDetailsDlg.cs`

- [ ] **Step 1: 替换为 AppLog**

| 文件 | category |
|------|----------|
| `BaseButton` | `"BaseButton"` |
| `BaseCardItem` | `"BaseCardItem"` |
| `BaseDlgComp` | `"BaseDlgComp"` |
| `VirtualList` | `"VirtualList"` |
| `KeywordTipService` | `"Keyword"` |
| `CardDetailsDlg` | `"CardDetailsDlg"` |

全部为 Warning 或 Error，与原 `PushWarning`/`PushError` 对应。

- [ ] **Step 2: 编译 + 全量测试**

Run: `dotnet build kemo_card.csproj -v q`  
Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj -v n`

Expected: 构建成功，测试 PASS（允许既有无关失败需先记录；本改动不应引入新失败）

- [ ] **Step 3: 格式化并 Commit**

```bash
dotnet format kemo_card.csproj --include Src/mod/global/Ui/Comp/BaseButton.cs Src/mod/global/Ui/Comp/BaseCardItem.cs Src/mod/global/Ui/Comp/BaseDlgComp.cs Src/mod/global/Ui/VirtualList.cs Src/mod/global/Ui/Tip/KeywordTipService.cs Src/mod/global/Ui/CardDetailsDlg.cs
git add Src/mod/global/Ui/Comp/BaseButton.cs Src/mod/global/Ui/Comp/BaseCardItem.cs Src/mod/global/Ui/Comp/BaseDlgComp.cs Src/mod/global/Ui/VirtualList.cs Src/mod/global/Ui/Tip/KeywordTipService.cs Src/mod/global/Ui/CardDetailsDlg.cs
git commit -m "refactor(logging): mod UI 诊断日志改走 AppLog"
```

---

### Task 6: 验收扫描 + Spec 状态

**Files:**
- Modify: `Doc/superpowers/specs/2026-07-14-app-log-design.md`（状态改为已实现）

- [ ] **Step 1: Grep 验收**

Run（PowerShell）：

```powershell
rg "GD\.(PushError|PushWarning)" Src --glob "*.cs"
```

Expected: **无匹配**（`GodotAppLog.cs` 内的 `GD.Push*` 是唯一允许处——若 rg 扫到它，确认仅此文件）。

再确认：

```powershell
rg "GD\.(PushError|PushWarning)" Src --glob "*.cs" --glob "!**/GodotAppLog.cs"
```

Expected: 无输出。

- [ ] **Step 2: 更新 spec 状态**

将文首 `**状态**：待实现` 改为 `**状态**：已实现`。

- [ ] **Step 3: Commit**

```bash
git add Doc/superpowers/specs/2026-07-14-app-log-design.md
git commit -m "docs: 将 AppLog 设计规格标记为已实现"
```

---

## Spec 覆盖自检

| Spec 要求 | Task |
|-----------|------|
| `IAppLog` / Null / 静态门面 | Task 1 |
| `GodotAppLog` 级别映射 + Format | Task 2 |
| MainRoot 最先 Configure | Task 2 |
| EventDispatcher / ContentMod / ModScript 适配 | Task 3 |
| KeywordHandler → AppLog | Task 3 |
| ContentMod 无双重前缀 | Task 3 测试 |
| 迁完 frame/fixed/mod Push* | Task 4–5 |
| Grep 验收 | Task 6 |
| 不做 Toast/战斗日志/文件 sink | 全计划未引入 |

---

## 执行说明

实现时每个 Task 结束后格式化该 Task 改过的 `.cs`，再提交。提交信息使用简体中文。
