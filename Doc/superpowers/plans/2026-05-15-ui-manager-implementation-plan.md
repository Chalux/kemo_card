# UI 管理器与 BaseUI 体系 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Godot 4.6 Mono 中实现基于字符串 Id 注册、强类型载荷、Dlg 单例替换与 Popup 栈/遮罩策略的 `UiManager` 与 `BaseUI` / `BaseDlg` / `BasePopup`，并由 `MainRoot` 注入层级节点作为唯一 UI 打开入口（与规格 `Doc/superpowers/specs/2026-05-15-ui-manager-design.md` 一致）。

**Architecture:** `UiRegistry` 负责 Id→工厂与类型元数据；`UiStateMachine` 管 `BaseUI` 生命周期状态；`UiManager`（`Node`）执行挂树、`MoveChild` 置顶、栈顶遮罩协调与 Dlg 异步切换（`Task` + 可取消令牌）；可单测部分与 Godot 节点操作分层，测试项目仅引用不含场景实例化的逻辑（注册表、状态机、栈序纯函数）。

**Tech Stack:** Godot 4.6.1、.NET 8、C# 12、`Godot.NET.Sdk/4.6.1`、NUnit 4.x、`Microsoft.NET.Test.Sdk`。

**规格路径：** `Doc/superpowers/specs/2026-05-15-ui-manager-design.md`

---

## 文件结构（创建 / 修改）

| 路径 | 职责 |
|------|------|
| `Src/frame/ui/PopupReopenBehavior.cs` | 同 Id 再开枚举（None=0, ReplayOpenAnimation=1, ReplaceInstance=2；名称可微调，语义与规格表一致） |
| `Src/frame/ui/EmptyUiPayload.cs` | `readonly record struct EmptyUiPayload` 无参窗体载荷 |
| `Src/frame/ui/UiLifecycleState.cs` | `Created / Opening / Opened / Closing / Closed` |
| `Src/frame/ui/UiStateMachine.cs` | 合法转移校验 + `TransitionTo` |
| `Src/frame/ui/UiIdDuplicateGuard.cs` | 纯 CLR：登记 Id，重复则 `InvalidOperationException`（供单测，不含 Godot） |
| `Src/frame/ui/UiRegistry.cs` | 泛型 `RegisterDlg` / `RegisterPopup`、委托 `UiIdDuplicateGuard`、未注册查询 |
| `Src/frame/ui/BaseUI.cs` | `Control` 基类：状态机、`ApplyPayload` 抽象、虚 `PlayOpenAsync`/`PlayCloseAsync` |
| `Src/frame/ui/BaseDlg.cs` | 继承 `BaseUI`，标记 `UiKind.Dlg` |
| `Src/frame/ui/BasePopup.cs` | 继承 `BaseUI`：遮罩节点引用、`MaskClickClosesPopup` 可配置、栈顶时绑定点击 |
| `Src/frame/ui/IUiManager.cs` | 供 Controller 依赖的接口（`OpenDlg`/`OpenPopup`/`CloseTopPopup` 等） |
| `Src/frame/ui/UiManager.cs` | `Node`：挂树、Dlg 切换、Popup 栈、`RefreshPopupMasks` |
| `Src/MainRoot.cs` | `_Ready` 创建或获取 `UiManager`、注入 `DlgHost`/`PopupStack` 引用 |
| `Src/MainRoot.tscn` | 增加 `DlgLayer`（`Control`）、`PopupStack`（`Control`）子节点及 `UiManager` 子 `Node` |
| `Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj` | 测试工程，引用 `kemo_card.csproj` |
| `Tests/kemo_card.Ui.Tests/UiStateMachineTests.cs` | 状态机单测 |
| `Tests/kemo_card.Ui.Tests/UiIdDuplicateGuardTests.cs` | Id 重复守卫单测 |
| `Tests/kemo_card.Ui.Tests/UiRegistryTryGetTests.cs` | 仅测 `TryGet` 未注册（不 `new` 任何 `Control`） |
| `kemo_card.sln` | 移除已删除的 `kemo_card.Frame.Tests` 项，加入 `kemo_card.Ui.Tests` |

---

### Task 1: 测试工程与解决方案

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`
- Modify: `kemo_card.sln`

- [ ] **Step 1: 添加测试项目文件**

`Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj` 完整内容：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>KemoCard.Ui.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\kemo_card.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 更新 sln**

从 `kemo_card.sln` 中删除整个 `Project(...)= "kemo_card.Frame.Tests"` 块及其 `GlobalSection` 中对应 `{E8F4A12C-...}` 的六行 `Debug|Any CPU` / `ExportDebug` / `ExportRelease` 配置（若 GUID 与文件不一致，以当前 sln 为准整块移除 Frame.Tests）。

追加（GUID 可新生成唯一 `{...}`）：

```text
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "kemo_card.Ui.Tests", "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
EndProject
```

并在 `GlobalSection(ProjectConfigurationPlatforms)` 中为该 GUID 复制与 `kemo_card` 相同的 `ActiveCfg` / `Build.0` 三行映射（`Debug|Any CPU`、`ExportDebug|Any CPU`、`ExportRelease|Any CPU`）。

- [ ] **Step 3: 还原并编译测试项目**

运行：

```bash
dotnet restore "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj"
dotnet build "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug
```

**期望：** 生成成功（此时尚无测试源文件，或仅有空类则 NUnit 也可通过）。

- [ ] **Step 4: Commit（若你要求提交）**

```bash
git add Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj kemo_card.sln
git commit -m "test: add kemo_card.Ui.Tests project and fix solution"
```

---

### Task 2: 枚举与空载荷 + 状态机（TDD）

**Files:**
- Create: `Src/frame/ui/PopupReopenBehavior.cs`
- Create: `Src/frame/ui/EmptyUiPayload.cs`
- Create: `Src/frame/ui/UiLifecycleState.cs`
- Create: `Src/frame/ui/UiStateMachine.cs`
- Create: `Tests/kemo_card.Ui.Tests/UiStateMachineTests.cs`

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Ui.Tests/UiStateMachineTests.cs`：

```csharp
using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiStateMachineTests
{
	[Test]
	public void Initial_state_is_Created()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Created));
	}

	[Test]
	public void Created_to_Opening_to_Opened_is_valid()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opening), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opened), Is.True);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Opened));
	}

	[Test]
	public void Opened_to_Closing_to_Closed_is_valid()
	{
		var sm = new UiStateMachine();
		sm.TryTransitionTo(UiLifecycleState.Opening);
		sm.TryTransitionTo(UiLifecycleState.Opened);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closing), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closed), Is.True);
	}

	[Test]
	public void Created_to_Opened_is_invalid()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opened), Is.False);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Created));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter "FullyQualifiedName~UiStateMachineTests" --no-build
```

**期望：** 失败（类型不存在）。若上一步未 build，改用：

```bash
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter "FullyQualifiedName~UiStateMachineTests"
```

**期望：** 编译失败或测试失败。

- [ ] **Step 3: 最小实现**

`Src/frame/ui/UiLifecycleState.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public enum UiLifecycleState
{
	Created,
	Opening,
	Opened,
	Closing,
	Closed,
}
```

`Src/frame/ui/PopupReopenBehavior.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public enum PopupReopenBehavior
{
	None = 0,
	ReplayOpenAnimation = 1,
	ReplaceInstance = 2,
}
```

`Src/frame/ui/EmptyUiPayload.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public readonly record struct EmptyUiPayload
{
	public static EmptyUiPayload Value => default;
}
```

`Src/frame/ui/UiStateMachine.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public sealed class UiStateMachine
{
	public UiLifecycleState Current { get; private set; } = UiLifecycleState.Created;

	public bool TryTransitionTo(UiLifecycleState next)
	{
		if (!IsAllowed(Current, next))
		{
			return false;
		}

		Current = next;
		return true;
	}

	private static bool IsAllowed(UiLifecycleState from, UiLifecycleState to)
	{
		return (from, to) switch
		{
			(UiLifecycleState.Created, UiLifecycleState.Opening) => true,
			(UiLifecycleState.Opening, UiLifecycleState.Opened) => true,
			(UiLifecycleState.Opened, UiLifecycleState.Closing) => true,
			(UiLifecycleState.Closing, UiLifecycleState.Closed) => true,
			_ => false,
		};
	}
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter "FullyQualifiedName~UiStateMachineTests"
```

**期望：** `Passed!` 全部绿色。

- [ ] **Step 5: Commit**

```bash
git add Src/frame/ui/UiLifecycleState.cs Src/frame/ui/UiStateMachine.cs Src/frame/ui/PopupReopenBehavior.cs Src/frame/ui/EmptyUiPayload.cs Tests/kemo_card.Ui.Tests/UiStateMachineTests.cs
git commit -m "feat(ui): add lifecycle state machine and popup reopen enum"
```

---

### Task 3: UiRegistry + Id 守卫（TDD，避免在单测里 `new Control`）

**Files:**
- Create: `Src/frame/ui/UiKind.cs`
- Create: `Src/frame/ui/UiIdDuplicateGuard.cs`
- Create: `Src/frame/ui/UiRegistry.cs`
- Create: `Src/frame/ui/BaseUI.cs`
- Create: `Src/frame/ui/BaseDlg.cs`
- Create: `Src/frame/ui/BasePopup.cs`
- Create: `Tests/kemo_card.Ui.Tests/UiIdDuplicateGuardTests.cs`
- Create: `Tests/kemo_card.Ui.Tests/UiRegistryTryGetTests.cs`

**说明：** `dotnet test` 在未启动 Godot 进程时无法可靠构造 `Godot.Control` 子类实例，故「重复 Id」用纯 CLR 的 `UiIdDuplicateGuard` 覆盖；`UiRegistry` 内部委托该守卫；`TryGet` 用空注册表断言，不调用工厂。

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Ui.Tests/UiIdDuplicateGuardTests.cs`：

```csharp
using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiIdDuplicateGuardTests
{
	[Test]
	public void Add_same_id_twice_throws()
	{
		var g = new UiIdDuplicateGuard();
		g.Add("a");
		Assert.Throws<InvalidOperationException>(() => g.Add("a"));
	}
}
```

`Tests/kemo_card.Ui.Tests/UiRegistryTryGetTests.cs`：

```csharp
using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiRegistryTryGetTests
{
	[Test]
	public void TryGet_unknown_id_returns_false()
	{
		var reg = new UiRegistry();
		Assert.That(reg.TryGet("missing", out _, out _, out _, out _), Is.False);
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter "FullyQualifiedName~UiIdDuplicateGuardTests|FullyQualifiedName~UiRegistryTryGetTests"
```

**期望：** 编译失败或测试失败。

- [ ] **Step 3: 实现守卫、`BaseUI`/`BaseDlg`/`BasePopup` 最小骨架与 `UiRegistry`**

`Src/frame/ui/UiKind.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public enum UiKind
{
	Dlg,
	Popup,
}
```

`Src/frame/ui/UiIdDuplicateGuard.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Ui;

public sealed class UiIdDuplicateGuard
{
	private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

	public void Add(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		if (!_ids.Add(id))
		{
			throw new InvalidOperationException($"UI id already registered: {id}");
		}
	}
}
```

`Src/frame/ui/BaseUI.cs`（最小版，后续 Task 扩）：

```csharp
using Godot;

namespace KemoCard.Frame.Ui;

public abstract partial class BaseUI : Control
{
	public UiStateMachine Lifecycle { get; } = new();

	public abstract void ApplyPayload(object payload);
	public virtual System.Threading.Tasks.Task PlayOpenAsync() => System.Threading.Tasks.Task.CompletedTask;
	public virtual System.Threading.Tasks.Task PlayCloseAsync() => System.Threading.Tasks.Task.CompletedTask;
}
```

`Src/frame/ui/BaseDlg.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public abstract partial class BaseDlg : BaseUI
{
}
```

`Src/frame/ui/BasePopup.cs`：

```csharp
namespace KemoCard.Frame.Ui;

public abstract partial class BasePopup : BaseUI
{
}
```

`Src/frame/ui/UiRegistry.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Ui;

public sealed class UiRegistry
{
	private sealed record Entry(UiKind Kind, Type UiType, Type PayloadType, Func<object, BaseUI> Factory);

	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly UiIdDuplicateGuard _ids = new();

	public void RegisterDlg<TDlg, TPayload>(string id, Func<TPayload, TDlg> factory)
		where TDlg : BaseDlg
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(factory);
		_ids.Add(id);
		_entries[id] = new Entry(
			UiKind.Dlg,
			typeof(TDlg),
			typeof(TPayload),
			p => factory((TPayload)p));
	}

	public void RegisterPopup<TPopup, TPayload>(string id, Func<TPayload, TPopup> factory)
		where TPopup : BasePopup
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(factory);
		_ids.Add(id);
		_entries[id] = new Entry(
			UiKind.Popup,
			typeof(TPopup),
			typeof(TPayload),
			p => factory((TPayload)p));
	}

	public bool TryGet(string id, out UiKind kind, out Type uiType, out Type payloadType, out Func<object, BaseUI> factory)
	{
		if (!_entries.TryGetValue(id, out var e))
		{
			kind = default;
			uiType = typeof(void);
			payloadType = typeof(void);
			factory = null!;
			return false;
		}

		kind = e.Kind;
		uiType = e.UiType;
		payloadType = e.PayloadType;
		factory = e.Factory;
		return true;
	}
}
```

**注意：** 若 `RegisterDlg` 在 `_ids.Add` 成功后、写入 `_entries` 前抛错，应保证不泄漏 Id——当前实现无中间状态；若未来拆分，需保持原子性。

- [ ] **Step 4: 运行测试**

```bash
dotnet test "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Debug --filter "FullyQualifiedName~UiIdDuplicateGuardTests|FullyQualifiedName~UiRegistryTryGetTests"
```

**期望：** 全部通过。

- [ ] **Step 5: Commit**

```bash
git add Src/frame/ui/UiIdDuplicateGuard.cs Src/frame/ui/BaseUI.cs Src/frame/ui/BaseDlg.cs Src/frame/ui/BasePopup.cs Src/frame/ui/UiKind.cs Src/frame/ui/UiRegistry.cs Tests/kemo_card.Ui.Tests/UiIdDuplicateGuardTests.cs Tests/kemo_card.Ui.Tests/UiRegistryTryGetTests.cs
git commit -m "feat(ui): add registry, id guard, and base ui shells"
```

---

### Task 4: `IUiManager` + `UiManager` 核心（Dlg 切换 + Popup 栈）

**Files:**
- Create: `Src/frame/ui/IUiManager.cs`
- Create: `Src/frame/ui/UiManager.cs`
- Modify: `Src/MainRoot.tscn`
- Modify: `Src/MainRoot.cs`

**约定：** `UiManager` 继承 `Node`；`MainRoot.tscn` 子节点顺序（从下到上）：`DlgHost`（`Control`，全屏 anchor）、`PopupStack`（`Control`，全屏）、`UiManager`（`Node`，挂脚本）。`CanvasLayer` 可用两个 `CanvasLayer` 包裹 `DlgHost` 与 `PopupStack`，保证 Popup 永远在 Dlg 之上（`layer` 数值 Popup > Dlg）。

- [ ] **Step 1: 手动验收清单（无自动化）**

在实现 Step 3 代码前，列出本 Task 完成后的手测步骤（执行者照做）：

1. 运行 Godot 打开 `MainRoot.tscn`，确认存在 `DlgHost`、`PopupStack`、`UiManager`。
2. 临时注册两个测试 Popup Id，连续 `OpenPopup`，确认子节点顺序末位为顶。
3. `PopupReopenBehavior.None` 对非顶实例调用 `OpenPopup`，确认 `MoveChild` 后该实例成为最后一个子节点。

（本 Task 不要求 NUnit 覆盖 Godot 树；若 `dotnet test` 因 Godot 本机库在纯 CLI 失败，以 Godot 编辑器运行场景为主验收。）

- [ ] **Step 2: 实现 `IUiManager`**

`Src/frame/ui/IUiManager.cs`：

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace KemoCard.Frame.Ui;

public interface IUiManager
{
	Task OpenDlgAsync<TPayload>(string id, TPayload payload, CancellationToken cancellationToken = default);

	Task OpenPopupAsync<TPayload>(string id, TPayload payload, PopupReopenBehavior behavior, bool maskClickClosesPopup = true, CancellationToken cancellationToken = default);

	void CloseTopPopup();

	void CloseDlg();
}
```

- [ ] **Step 3: 实现 `UiManager`（关键逻辑骨架）**

要点（实现时写在 `UiManager.cs` 内，可用 `#region` 分块）：

1. 字段：`UiRegistry _registry = new()`；`Control? _dlgHost`；`Control? _popupStack`；`BaseDlg? _currentDlg`；`CancellationTokenSource? _dlgLoadCts`；`Dictionary<string, BasePopup> _popupById`。
2. `Configure(Control dlgHost, Control popupStack)` 由 `MainRoot` 调用。
3. `RegisterDlg`/`RegisterPopup` 委托给 `_registry`（包装公开方法）。
4. `OpenDlgAsync`：取消 `_dlgLoadCts` 上一次；`factory(payload)` 在**未挂到 DlgHost 前**可先 `AddChild` 到 `UiManager` 自身且 `Visible = false`，或挂到临时 `Offscreen` 节点；调用 `ApplyPayload`、`Lifecycle.TryTransitionTo(Opening)`、`await PlayOpenAsync()`、`TryTransitionTo(Opened)`；完成后从旧父移除并 `AddChild` 到 `_dlgHost`，`Visible = true`；再 `QueueFree` 旧 `_currentDlg`。若 `cancellationToken` 或内部 CTS 已取消，则 `QueueFree` 新实例并返回。
5. `OpenPopupAsync`：若 `_popupById` 含该 id：按 `PopupReopenBehavior` 分支（None：若索引非最后则 `MoveChild`；Replay：`PlayOpenAsync`；Replace：`QueueFree` 旧并从字典移除后新建）。新建路径：`factory`→`ApplyPayload`→打开流程→`AddChild(_popupStack)`→入字典。最后 `RefreshPopupMasks()`：遍历子节点，仅最后一个 `BasePopup` 显示遮罩并（若允许）绑定遮罩 `GuiInput` 关闭自己。
6. `CloseTopPopup`：取 `_popupStack.GetChildCount()-1`，若为 `BasePopup` 则走关闭动画协程后 `QueueFree` 并从字典移除，`RefreshPopupMasks`。
7. 未注册 Id：`GD.PushError` + 早退。

`UiManager.cs` 类声明示例：

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace KemoCard.Frame.Ui;

public partial class UiManager : Node, IUiManager
{
	// ... 实现见上
}
```

- [ ] **Step 4: 修改 `MainRoot.tscn`**

在 `MainRoot` 节点下添加（名称供脚本 `[Export]` 或 `%NodePath` 绑定）：

- `CanvasLayer` name=`DlgCanvas` layer=10 → 子 `Control` name=`DlgHost` anchors full rect
- `CanvasLayer` name=`PopupCanvas` layer=20 → 子 `Control` name=`PopupStack` anchors full rect  
- `Node` name=`UiManager` script=`res://Src/frame/ui/UiManager.cs`

- [ ] **Step 5: 修改 `MainRoot.cs`**

```csharp
using Godot;
using KemoCard.Frame.Ui;

namespace MainRoot;

public partial class MainRoot : Control
{
	[Export]
	public UiManager? UiManager { get; set; }

	[Export]
	public Control? DlgHost { get; set; }

	[Export]
	public Control? PopupStack { get; set; }

	public override void _Ready()
	{
		if (UiManager is null || DlgHost is null || PopupStack is null)
		{
			GD.PushError("MainRoot: UiManager/DlgHost/PopupStack must be assigned in the inspector.");
			return;
		}

		UiManager.Configure(DlgHost, PopupStack);
	}
}
```

在编辑器中将三个 `[Export]` 指向对应节点。

- [ ] **Step 6: 编译游戏工程**

```bash
dotnet build "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card\kemo_card.csproj" -c Debug
```

**期望：** 0 errors。

- [ ] **Step 7: Commit**

```bash
git add Src/frame/ui/IUiManager.cs Src/frame/ui/UiManager.cs Src/MainRoot.cs Src/MainRoot.tscn
git commit -m "feat(ui): add UiManager and MainRoot wiring"
```

---

### Task 5: `BasePopup` 遮罩与默认点击关闭

**Files:**
- Modify: `Src/frame/ui/BasePopup.cs`
- Modify: `Src/frame/ui/UiManager.cs`（`RefreshPopupMasks` 与遮罩绑定）

- [ ] **Step 1: 扩展 `BasePopup`**

- `[Export] public ColorRect? ModalMask { get; set; }`（或在 `_Ready` 用 `GetNode<ColorRect>("%ModalMask")`）
- 属性 `MaskClickClosesPopup`（bool，默认 true），`SetMaskVisible(bool visible)` 控制 `MouseFilter = Stop` 与 `Color` alpha。
- 虚方法 `OnModalMaskGuiInput(InputEvent @event)`：若 `MaskClickClosesPopup` 且为 `InputEventMouseButton` 左键按下，则 `EmitSignal(SignalName.RequestClose)` 或调用 `IUiManager` 回调——推荐 `EventHandler`：`public event Action? CloseRequested;`，由 `UiManager` 订阅。

- [ ] **Step 2: `UiManager.RefreshPopupMasks`**

对每个 `_popupStack` 子节点：若为 `BasePopup` p，则 `p.SetMaskVisible(p == top)`；仅 top 且 `MaskClickClosesPopup` 为真时处理点击关闭。

- [ ] **Step 3: Godot 手测**

打开带遮罩的测试 Popup，确认仅顶层可点穿；点遮罩关闭；`maskClickClosesPopup: false` 时不关闭但仍挡输入。

- [ ] **Step 4: Commit**

```bash
git add Src/frame/ui/BasePopup.cs Src/frame/ui/UiManager.cs
git commit -m "feat(ui): popup modal mask and click-to-close"
```

---

### Task 6: 文档与规格对齐检查

**Files:**
- Modify: `Doc/superpowers/specs/2026-05-15-ui-manager-design.md`（若实现中有意调整，追加「实现偏差」小节）

- [ ] **Step 1:** 对照规格 §4–§6，在 spec 末尾追加 **实现备注**（例如：Dlg 异步使用 `CancellationTokenSource` 取消上一次加载；`OpenPopupAsync` 签名含 `maskClickClosesPopup`）。

- [ ] **Step 2: Commit**

```bash
git add Doc/superpowers/specs/2026-05-15-ui-manager-design.md
git commit -m "docs(ui): note implementation choices for UiManager"
```

---

## 自检（对照规格）

| 规格章节 | 对应 Task |
|-----------|-----------|
| §1 注册 / 唯一入口 | Task 3 `UiRegistry`；Task 4 `UiManager` + `MainRoot` |
| §2 场景树 / 栈序 | Task 4 `tscn` + `MoveChild` |
| §3 Dlg 单例与替换 B | Task 4 `OpenDlgAsync` 取消 + 后换旧 |
| §4 Popup 遮罩 / 枚举 0/1/2 | Task 4–5 |
| §5 状态机 | Task 2 + Task 4 在 `Open*` 中驱动 `Lifecycle` |
| §5.3 强类型载荷 | Task 3 泛型注册；`Open*` 泛型方法在 `UiManager` 显式声明 |
| §8 未注册错误 | Task 4 `GD.PushError` |
| §9 测试 | Task 2、Task 3（守卫 + `TryGet`）自动化；Task 4–5 手测清单 |

**占位扫描：** 本计划不含 TBD/TODO 式步骤；手测步骤为明确编号行为。

**类型一致性：** `PopupReopenBehavior` 与 `IUiManager.OpenPopupAsync` 参数名一致；`UiLifecycleState` 与 `UiStateMachine` 一致。

---

## Plan complete

Plan complete and saved to `Doc/superpowers/plans/2026-05-15-ui-manager-implementation-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** — 每个 Task 派生子代理并在 Task 间复核，迭代快  

**2. Inline Execution** — 在本会话用 executing-plans 按检查点批量执行  

**Which approach?**
