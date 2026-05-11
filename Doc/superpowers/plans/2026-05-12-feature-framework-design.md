# 功能管理框架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `Src/frame/featurekit` 交付纯 C# 功能包契约与默认实现：显式清单安装、组合上下文、进程级 `FeatureManager`、`GlobalEventBus` / `InternalEventBus`、逆序 `Shutdown`，并附可 `dotnet test` 的单元与集成测试。

**Architecture:** 启动侧构造 `GlobalEventBus`、`FeatureManager` 与 `FeatureCompositionContext`；`FeatureBootstrap` 按清单顺序调用各包 `Install`，在注册阶段完成全局事件 schema 冲突检测；`Shutdown` 时逆序调用包 `Shutdown` 并封存各包内部总线。不提供 `Resolve<T>()` 服务定位器；跨包仅通过 `frame` 契约 DTO 与全局总线协作。

**Tech stack:** Godot 4.6 Mono、`net8.0`、`kemo_card`（Godot.NET.Sdk）、xUnit、`dotnet test`。

**工作区约定：** 计划文件放在 `Doc/superpowers/plans/`（与现有 `Doc/superpowers/specs/` 一致）。实现代码仅用 C#，路径遵循 `Src/frame/` 与 `Src/mod/` 规则。

---

## 文件结构总览

| 路径 | 职责 |
|------|------|
| `Src/frame/featurekit/Contracts/GlobalEventId.cs` | 全局事件 id 单一枚举（含测试用成员） |
| `Src/frame/featurekit/Contracts/TestPingPayload.cs` | 框架测试用全局载荷 DTO（`record`，无业务语义） |
| `Src/frame/featurekit/IGlobalEventBus.cs` | 全局总线契约 |
| `Src/frame/featurekit/IInternalEventBus.cs` | 内部总线契约 |
| `Src/frame/featurekit/GlobalEventBus.cs` | 全局总线默认实现（schema、Pub/Sub、严格类型） |
| `Src/frame/featurekit/InternalEventBus.cs` | 内部总线默认实现（按类型分发；`Shutdown` 后拒绝发布） |
| `Src/frame/featurekit/IFeaturePackage.cs` | 功能包 `Install` / `Shutdown` |
| `Src/frame/featurekit/IFeatureCompositionContext.cs` | 组合上下文窄接口 |
| `Src/frame/featurekit/FeatureCompositionContext.cs` | 上下文实现（当前安装包名、`CreateInternalBus` 每包一次、`RegisterGlobalEventSchema` 委托总线） |
| `Src/frame/featurekit/FeatureManager.cs` | 包列表、可选门面登记、`Shutdown` 逆序 |
| `Src/frame/featurekit/FeatureBootstrap.cs` | 安装编排、安装后轻量校验钩子、调用 `FeatureManager.Initialize` |
| `kemo_card.sln` | 加入测试工程 |
| `Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj` | xUnit 测试工程 |
| `Tests/kemo_card.Frame.Tests/GlobalEventBusTests.cs` | 总线单元测试 |
| `Tests/kemo_card.Frame.Tests/InternalEventBusTests.cs` | 内部总线单元测试 |
| `Tests/kemo_card.Frame.Tests/FeatureBootstrapIntegrationTests.cs` | 双包集成与 schema 冲突测试 |
| `Tests/kemo_card.Frame.Tests/Fakes/FakeCompositionContext.cs` | 假上下文，便于单测 `IFeaturePackage.Install` |
| `Src/MainRoot.cs` | 构造默认依赖并调用 `FeatureBootstrap`（占位空包清单，可后续替换） |

**YAGNI 决策（与规格对齐）：**

- 不提供「宽松模式」：`Publish`/`Subscribe` 与 schema 不一致时一律 `InvalidOperationException`。
- `FeatureManager.GetPackage<T>()`：保留为 `public`，XML 文档标明「仅调试 / `frame` 内编排，业务勿用」；若后续评审要求删除，可改为 `internal` + `InternalsVisibleTo`。
- `FeatureBootstrap` 安装后「schema 校验」：重复与类型冲突已在 `RegisterSchema` 即时失败；安装结束后调用 `FeatureManager.Initialize()`（首版可为空方法体，占位二阶段初始化扩展点）。

---

### Task 1: 建立测试工程与解决方案引用

**Files:**

- Create: `Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj`
- Modify: `kemo_card.sln`（新增 Project 与 `ProjectConfigurationPlatforms` 三段 `Debug|Any CPU` / `ExportDebug|Any CPU` / `ExportRelease|Any CPU` 的 `{GUID}.ActiveCfg` / `.Build.0`，GUID 使用新生成的唯一 GUID）

- [ ] **Step 1: 创建测试 csproj**

创建目录 `Tests/kemo_card.Frame.Tests/`，写入：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>kemo_card.Frame.Tests</RootNamespace>
    <AssemblyName>kemo_card.Frame.Tests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\kemo_card.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 将测试工程加入 sln**

在 `kemo_card.sln` 中 `Project(...)` 块后追加一行（示例 GUID，实现时生成新 GUID 并全文件一致替换 `A1B2C3D4-E5F6-7890-ABCD-EF1234567890`）：

```text
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "kemo_card.Frame.Tests", "Tests\kemo_card.Frame.Tests\kemo_card.Frame.Tests.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
EndProject
```

在 `GlobalSection(ProjectConfigurationPlatforms)` 内为上述 GUID 追加六行（与现有 `kemo_card` 工程相同的三种配置名）：

```text
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.Build.0 = Debug|Any CPU
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.ExportDebug|Any CPU.ActiveCfg = Debug|Any CPU
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.ExportDebug|Any CPU.Build.0 = Debug|Any CPU
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.ExportRelease|Any CPU.ActiveCfg = Release|Any CPU
	{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.ExportRelease|Any CPU.Build.0 = Release|Any CPU
```

- [ ] **Step 3: 还原并编译测试工程**

Run（在仓库根目录 `d:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card`）:

```powershell
dotnet restore Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj
dotnet build Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug
```

Expected: Build succeeded（此时尚无测试源文件则 0 tests — 下一步再添加）。

- [ ] **Step 4: Commit**

```bash
git add kemo_card.sln Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj
git commit -m "test: add frame xUnit project and solution entry"
```

---

### Task 2: 契约枚举与测试 DTO + `GlobalEventBus`（TDD）

**Files:**

- Create: `Src/frame/featurekit/Contracts/GlobalEventId.cs`
- Create: `Src/frame/featurekit/Contracts/TestPingPayload.cs`
- Create: `Src/frame/featurekit/IGlobalEventBus.cs`
- Create: `Src/frame/featurekit/GlobalEventBus.cs`
- Create: `Tests/kemo_card.Frame.Tests/GlobalEventBusTests.cs`

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Frame.Tests/GlobalEventBusTests.cs`：

```csharp
using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class GlobalEventBusTests
{
	[Fact]
	public void RegisterSchema_twice_same_type_succeeds_idempotent()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Test");
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Test");
	}

	[Fact]
	public void RegisterSchema_twice_different_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "A");
		var ex = Assert.Throws<InvalidOperationException>(() =>
			bus.RegisterSchema<TestPongPayload>(GlobalEventId.FrameworkTestPing, "B"));
		Assert.Contains("FrameworkTestPing", ex.Message, StringComparison.Ordinal);
		Assert.Contains("B", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Publish_after_subscribe_delivers_payload()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Pub");
		TestPingPayload? received = null;
		bus.Subscribe<TestPingPayload>(GlobalEventId.FrameworkTestPing, p => received = p);
		var sent = new TestPingPayload(42);
		bus.Publish(GlobalEventId.FrameworkTestPing, sent);
		Assert.NotNull(received);
		Assert.Equal(42, received.Value);
	}

	[Fact]
	public void Subscribe_mismatched_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "R");
		Assert.Throws<InvalidOperationException>(() =>
			bus.Subscribe<TestPongPayload>(GlobalEventId.FrameworkTestPing, _ => { }));
	}

	[Fact]
	public void Publish_mismatched_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "R");
		Assert.Throws<InvalidOperationException>(() =>
			bus.Publish(GlobalEventId.FrameworkTestPing, new TestPongPayload(1)));
	}

	private sealed record TestPongPayload(int Value);
}
```

`Src/frame/featurekit/Contracts/GlobalEventId.cs`：

```csharp
namespace KemoCard.Frame.FeatureKit.Contracts;

public enum GlobalEventId
{
	FrameworkTestPing = 1,
}
```

`Src/frame/featurekit/Contracts/TestPingPayload.cs`：

```csharp
namespace KemoCard.Frame.FeatureKit.Contracts;

public sealed record TestPingPayload(int Value);
```

`Src/frame/featurekit/IGlobalEventBus.cs`：

```csharp
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public interface IGlobalEventBus
{
	void RegisterSchema<TPayload>(GlobalEventId id, string registrantName) where TPayload : class;

	void Publish<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class;

	void Subscribe<TPayload>(GlobalEventId id, Action<TPayload> handler) where TPayload : class;

	void ClearAllSubscriptionsForTests();
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~GlobalEventBusTests"
```

Expected: 编译失败，提示 `GlobalEventBus` 类型不存在或成员缺失。

- [ ] **Step 3: 最小实现通过测试**

`Src/frame/featurekit/GlobalEventBus.cs`：

```csharp
using System.Collections.Concurrent;
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public sealed class GlobalEventBus : IGlobalEventBus
{
	private readonly ConcurrentDictionary<GlobalEventId, Type> _schemaTypes = new();
	private readonly ConcurrentDictionary<GlobalEventId, ConcurrentBag<Delegate>> _handlers = new();

	public void RegisterSchema<TPayload>(GlobalEventId id, string registrantName) where TPayload : class
	{
		var t = typeof(TPayload);
		_schemaTypes.AddOrUpdate(
			id,
			_ => t,
			(_, existing) =>
			{
				if (existing != t)
				{
					throw new InvalidOperationException(
						$"GlobalEventId '{id}' schema conflict: existing '{existing.FullName}', " +
						$"new '{t.FullName}' from '{registrantName}'.");
				}
				return existing;
			});
	}

	public void Publish<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class
	{
		AssertPayloadAssignable(id, payload);
		if (!_handlers.TryGetValue(id, out var bag))
		{
			return;
		}
		foreach (var d in bag)
		{
			if (d is Action<TPayload> typed)
			{
				typed(payload);
			}
		}
	}

	public void Subscribe<TPayload>(GlobalEventId id, Action<TPayload> handler) where TPayload : class
	{
		AssertSchemaMatches<TPayload>(id);
		var bag = _handlers.GetOrAdd(id, _ => new ConcurrentBag<Delegate>());
		bag.Add(handler);
	}

	public void ClearAllSubscriptionsForTests()
	{
		_handlers.Clear();
	}

	private void AssertSchemaMatches<TPayload>(GlobalEventId id) where TPayload : class
	{
		if (!_schemaTypes.TryGetValue(id, out var registered))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' has no registered schema; cannot subscribe as '{typeof(TPayload).FullName}'.");
		}
		if (registered != typeof(TPayload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' schema is '{registered.FullName}'; subscribe type '{typeof(TPayload).FullName}' is invalid.");
		}
	}

	private void AssertPayloadAssignable<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class
	{
		if (!_schemaTypes.TryGetValue(id, out var registered))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' has no registered schema; cannot publish '{typeof(TPayload).FullName}'.");
		}
		if (registered != typeof(TPayload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' schema is '{registered.FullName}'; publish type '{typeof(TPayload).FullName}' is invalid.");
		}
		if (!registered.IsInstanceOfType(payload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' payload runtime type '{payload.GetType().FullName}' is not assignable to '{registered.FullName}'.");
		}
	}
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~GlobalEventBusTests"
```

Expected: Passed。

- [ ] **Step 5: Commit**

```bash
git add Src/frame/featurekit/Contracts/GlobalEventId.cs Src/frame/featurekit/Contracts/TestPingPayload.cs Src/frame/featurekit/IGlobalEventBus.cs Src/frame/featurekit/GlobalEventBus.cs Tests/kemo_card.Frame.Tests/GlobalEventBusTests.cs
git commit -m "feat(frame): add GlobalEventBus with strict schema"
```

---

### Task 3: `InternalEventBus`

**Files:**

- Create: `Src/frame/featurekit/IInternalEventBus.cs`
- Create: `Src/frame/featurekit/InternalEventBus.cs`
- Create: `Tests/kemo_card.Frame.Tests/InternalEventBusTests.cs`

- [ ] **Step 1: 编写失败测试**

`Tests/kemo_card.Frame.Tests/InternalEventBusTests.cs`：

```csharp
using KemoCard.Frame.FeatureKit;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class InternalEventBusTests
{
	private sealed record LocalA(int X);

	[Fact]
	public void Publish_delivers_to_subscriber()
	{
		var bus = new InternalEventBus();
		LocalA? got = null;
		bus.Subscribe<LocalA>(a => got = a);
		bus.Publish(new LocalA(7));
		Assert.NotNull(got);
		Assert.Equal(7, got.X);
	}

	[Fact]
	public void After_shutdown_publish_throws()
	{
		var bus = new InternalEventBus();
		bus.Shutdown();
		Assert.Throws<InvalidOperationException>(() => bus.Publish(new LocalA(1)));
	}

	[Fact]
	public void After_shutdown_subscribe_throws()
	{
		var bus = new InternalEventBus();
		bus.Shutdown();
		Assert.Throws<InvalidOperationException>(() => bus.Subscribe<LocalA>(_ => { }));
	}
}
```

`Src/frame/featurekit/IInternalEventBus.cs`：

```csharp
namespace KemoCard.Frame.FeatureKit;

public interface IInternalEventBus
{
	void Subscribe<TPayload>(Action<TPayload> handler) where TPayload : class;

	void Publish<TPayload>(TPayload payload) where TPayload : class;

	void Shutdown();
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~InternalEventBusTests"
```

Expected: 编译失败（`InternalEventBus` 未实现）。

- [ ] **Step 3: 实现 `InternalEventBus`**

`Src/frame/featurekit/InternalEventBus.cs`：

```csharp
using System.Collections.Concurrent;

namespace KemoCard.Frame.FeatureKit;

public sealed class InternalEventBus : IInternalEventBus
{
	private readonly ConcurrentDictionary<Type, ConcurrentBag<Delegate>> _handlers = new();
	private int _shutdown;

	public void Subscribe<TPayload>(Action<TPayload> handler) where TPayload : class
	{
		EnsureNotShutdown();
		var t = typeof(TPayload);
		var bag = _handlers.GetOrAdd(t, _ => new ConcurrentBag<Delegate>());
		bag.Add(handler);
	}

	public void Publish<TPayload>(TPayload payload) where TPayload : class
	{
		EnsureNotShutdown();
		var t = typeof(TPayload);
		if (!_handlers.TryGetValue(t, out var bag))
		{
			return;
		}
		foreach (var d in bag)
		{
			if (d is Action<TPayload> typed)
			{
				typed(payload);
			}
		}
	}

	public void Shutdown()
	{
		if (Interlocked.Exchange(ref _shutdown, 1) == 1)
		{
			return;
		}
		_handlers.Clear();
	}

	private void EnsureNotShutdown()
	{
		if (Volatile.Read(ref _shutdown) == 1)
		{
			throw new InvalidOperationException("InternalEventBus is shut down.");
		}
	}
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~InternalEventBusTests"
```

Expected: Passed。

- [ ] **Step 5: Commit**

```bash
git add Src/frame/featurekit/IInternalEventBus.cs Src/frame/featurekit/InternalEventBus.cs Tests/kemo_card.Frame.Tests/InternalEventBusTests.cs
git commit -m "feat(frame): add InternalEventBus with shutdown seal"
```

---

### Task 4: `FeatureManager`、`IFeaturePackage`、组合上下文与 `FeatureBootstrap`

**Files:**

- Create: `Src/frame/featurekit/IFeaturePackage.cs`
- Create: `Src/frame/featurekit/IFeatureCompositionContext.cs`
- Create: `Src/frame/featurekit/FeatureCompositionContext.cs`
- Create: `Src/frame/featurekit/FeatureManager.cs`
- Create: `Src/frame/featurekit/FeatureBootstrap.cs`
- Create: `Tests/kemo_card.Frame.Tests/FeatureBootstrapIntegrationTests.cs`

**释放顺序约定（避免与扩展方法重名）：** `FeatureManager.ShutdownPackages` 仅逆序调用各包 `IFeaturePackage.Shutdown`。完整进程收尾由 `FeatureBootstrap.Shutdown(manager, context)` 执行：`ShutdownPackages` → `context.ShutdownInternalBuses()` → `context.ClearGlobalSubscriptions()`（内部调用 `GlobalEventBus.ClearAllSubscriptionsForTests`）。集成测试结束时应调用 `FeatureBootstrap.Shutdown(manager, ctx)`。

- [ ] **Step 1: 编写失败集成测试（双包 + schema 冲突）**

`Tests/kemo_card.Frame.Tests/FeatureBootstrapIntegrationTests.cs`（构造函数每用例重置 `PackageB` 静态计数；跨包投递在 **全部 `Install` 完成后** 由测试显式 `Publish`）：

```csharp
using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class FeatureBootstrapIntegrationTests
{
	public FeatureBootstrapIntegrationTests()
	{
		PackageB.ReceiveCount = 0;
		PackageB.Last = null;
	}

	[Fact]
	public void Two_packages_A_schema_B_subscribe_then_manual_publish_delivers()
	{
		var global = new GlobalEventBus();
		var packages = new IFeaturePackage[] { new PackageA(), new PackageB() };
		var manager = new FeatureManager(packages);
		var ctx = new FeatureCompositionContext(global, manager, packages);
		FeatureBootstrap.Run(packages, ctx, manager);
		global.Publish(GlobalEventId.FrameworkTestPing, new TestPingPayload(99));
		Assert.Equal(1, PackageB.ReceiveCount);
		Assert.NotNull(PackageB.Last);
		Assert.Equal(99, PackageB.Last.Value);
		FeatureBootstrap.Shutdown(manager, ctx);
	}

	[Fact]
	public void Two_packages_conflicting_schema_throws_on_second_register()
	{
		var global = new GlobalEventBus();
		var packages = new IFeaturePackage[] { new PackageBadA(), new PackageBadB() };
		var manager = new FeatureManager(packages);
		var ctx = new FeatureCompositionContext(global, manager, packages);
		Assert.Throws<InvalidOperationException>(() => FeatureBootstrap.Run(packages, ctx, manager));
	}

	private sealed class PackageA : IFeaturePackage
	{
		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageB : IFeaturePackage
	{
		public static int ReceiveCount;
		public static TestPingPayload? Last;

		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.GlobalBus.Subscribe<TestPingPayload>(GlobalEventId.FrameworkTestPing, p =>
			{
				ReceiveCount++;
				Last = p;
			});
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageBadA : IFeaturePackage
	{
		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageBadB : IFeaturePackage
	{
		private sealed record OtherPayload(int X);

		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<OtherPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~FeatureBootstrapIntegrationTests"
```

Expected: 编译失败：`IFeaturePackage` / `FeatureBootstrap` / `FeatureCompositionContext` 等不存在。

- [ ] **Step 3: 实现接口与类型（定稿）**

`Src/frame/featurekit/IFeaturePackage.cs`：

```csharp
namespace KemoCard.Frame.FeatureKit;

public interface IFeaturePackage
{
	void Install(IFeatureCompositionContext ctx);

	void Shutdown(IFeatureCompositionContext ctx);
}
```

`Src/frame/featurekit/IFeatureCompositionContext.cs`：

```csharp
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public interface IFeatureCompositionContext
{
	IGlobalEventBus GlobalBus { get; }

	IFeatureManagerReadOnly Manager { get; }

	string InstallingFeatureName { get; }

	IInternalEventBus CreateInternalBus();

	void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class;

	void RegisterFacade<TFacade>(TFacade facade) where TFacade : class;
}
```

`Src/frame/featurekit/FeatureManager.cs`：

```csharp
using System.Collections.Concurrent;

namespace KemoCard.Frame.FeatureKit;

public interface IFeatureManagerReadOnly
{
	TFacade? TryGetFacade<TFacade>() where TFacade : class;
}

public sealed class FeatureManager : IFeatureManagerReadOnly
{
	private readonly IReadOnlyList<IFeaturePackage> _packages;
	private readonly ConcurrentDictionary<Type, object> _facades = new();

	public FeatureManager(IReadOnlyList<IFeaturePackage> packages)
	{
		_packages = packages;
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
		var t = typeof(TFacade);
		_facades[t] = facade;
	}

	public TFacade? TryGetFacade<TFacade>() where TFacade : class
	{
		if (_facades.TryGetValue(typeof(TFacade), out var obj) && obj is TFacade f)
		{
			return f;
		}
		return null;
	}

	/// <summary>仅调试或 frame 内编排使用；业务代码勿依赖。</summary>
	public TPackage? GetPackage<TPackage>()
		where TPackage : class, IFeaturePackage
	{
		foreach (var p in _packages)
		{
			if (p is TPackage match)
			{
				return match;
			}
		}
		return null;
	}

	public void Initialize()
	{
	}

	public void ShutdownPackages(IFeatureCompositionContext ctx)
	{
		for (var i = _packages.Count - 1; i >= 0; i--)
		{
			_packages[i].Shutdown(ctx);
		}
	}
}
```

`Src/frame/featurekit/FeatureCompositionContext.cs`：

```csharp
using System.Collections.Concurrent;
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public sealed class FeatureCompositionContext : IFeatureCompositionContext
{
	private readonly GlobalEventBus _global;
	private readonly FeatureManager _manager;
	private readonly IReadOnlyList<IFeaturePackage> _packages;
	private readonly ConcurrentDictionary<int, IInternalEventBus> _internalByIndex = new();
	private int _installIndex = -1;
	private readonly ConcurrentDictionary<int, byte> _internalCreated = new();

	public FeatureCompositionContext(
		GlobalEventBus global,
		FeatureManager manager,
		IReadOnlyList<IFeaturePackage> packages)
	{
		_global = global;
		_manager = manager;
		_packages = packages;
	}

	public IGlobalEventBus GlobalBus => _global;

	public IFeatureManagerReadOnly Manager => _manager;

	public string InstallingFeatureName =>
		_installIndex >= 0 && _installIndex < _packages.Count
			? _packages[_installIndex].GetType().Name
			: string.Empty;

	public IInternalEventBus CreateInternalBus()
	{
		if (_installIndex < 0 || _installIndex >= _packages.Count)
		{
			throw new InvalidOperationException("CreateInternalBus is only valid during Install.");
		}
		if (!_internalCreated.TryAdd(_installIndex, 0))
		{
			throw new InvalidOperationException(
				$"Internal bus already created for feature index {_installIndex} ({InstallingFeatureName}).");
		}
		var bus = new InternalEventBus();
		_internalByIndex[_installIndex] = bus;
		return bus;
	}

	public void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class
	{
		_global.RegisterSchema<TPayload>(id, InstallingFeatureName);
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
		_manager.RegisterFacade(facade);
	}

	internal void BeginInstallIndex(int index)
	{
		_installIndex = index;
	}

	internal void EndInstallIndex()
	{
		_installIndex = -1;
	}

	internal void ShutdownInternalBuses()
	{
		for (var i = _packages.Count - 1; i >= 0; i--)
		{
			if (_internalByIndex.TryRemove(i, out var bus))
			{
				bus.Shutdown();
			}
		}
	}

	internal void ClearGlobalSubscriptions()
	{
		_global.ClearAllSubscriptionsForTests();
	}
}
```

`Src/frame/featurekit/FeatureBootstrap.cs`：

```csharp
namespace KemoCard.Frame.FeatureKit;

public static class FeatureBootstrap
{
	public static void Run(
		IReadOnlyList<IFeaturePackage> packages,
		FeatureCompositionContext context,
		FeatureManager manager)
	{
		for (var i = 0; i < packages.Count; i++)
		{
			context.BeginInstallIndex(i);
			try
			{
				packages[i].Install(context);
			}
			finally
			{
				context.EndInstallIndex();
			}
		}
		ValidateInstallationComplete();
		manager.Initialize();
	}

	public static void Shutdown(FeatureManager manager, FeatureCompositionContext context)
	{
		manager.ShutdownPackages(context);
		context.ShutdownInternalBuses();
		context.ClearGlobalSubscriptions();
	}

	private static void ValidateInstallationComplete()
	{
	}
}
```

- [ ] **Step 4: 运行集成测试**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~FeatureBootstrapIntegrationTests"
```

Expected: 全部 `FeatureBootstrapIntegrationTests` 通过。

- [ ] **Step 5: Commit**

```bash
git add Src/frame/featurekit/IFeaturePackage.cs Src/frame/featurekit/IFeatureCompositionContext.cs Src/frame/featurekit/FeatureCompositionContext.cs Src/frame/featurekit/FeatureManager.cs Src/frame/featurekit/FeatureBootstrap.cs Tests/kemo_card.Frame.Tests/FeatureBootstrapIntegrationTests.cs
git commit -m "feat(frame): add FeatureBootstrap, manager, composition context"
```

---

### Task 5: 假上下文单测（规格 §5 Fake）

**Files:**

- Create: `Tests/kemo_card.Frame.Tests/Fakes/FakeCompositionContext.cs`
- Create: `Tests/kemo_card.Frame.Tests/FakeContextPackageTests.cs`

- [ ] **Step 1: 编写假上下文与测试**

`Tests/kemo_card.Frame.Tests/Fakes/FakeCompositionContext.cs`：

```csharp
using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;

namespace kemo_card.Frame.Tests.Fakes;

public sealed class FakeCompositionContext : IFeatureCompositionContext
{
	public FakeCompositionContext(IGlobalEventBus global, IFeatureManagerReadOnly manager)
	{
		GlobalBus = global;
		Manager = manager;
	}

	public IGlobalEventBus GlobalBus { get; }

	public IFeatureManagerReadOnly Manager { get; }

	public string InstallingFeatureName => "Fake";

	public IInternalEventBus CreateInternalBus() => new InternalEventBus();

	public void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class
	{
		if (GlobalBus is GlobalEventBus concrete)
		{
			concrete.RegisterSchema<TPayload>(id, InstallingFeatureName);
			return;
		}
		throw new InvalidOperationException("FakeCompositionContext requires GlobalEventBus instance for schema registration tests.");
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
	}
}
```

`Tests/kemo_card.Frame.Tests/FakeContextPackageTests.cs`：

```csharp
using kemo_card.Frame.Tests.Fakes;
using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class FakeContextPackageTests
{
	private sealed class SamplePackage : IFeaturePackage
	{
		public IInternalEventBus? Bus;

		public void Install(IFeatureCompositionContext ctx)
		{
			Bus = ctx.CreateInternalBus();
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
			TestPingPayload? got = null;
			Bus.Subscribe<TestPingPayload>(p => got = p);
			Bus.Publish(new TestPingPayload(3));
			Assert.NotNull(got);
			Assert.Equal(3, got.Value);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	[Fact]
	public void Install_with_fake_context_uses_internal_bus()
	{
		var global = new GlobalEventBus();
		var manager = new FeatureManager(Array.Empty<IFeaturePackage>());
		var fake = new FakeCompositionContext(global, manager);
		var pkg = new SamplePackage();
		pkg.Install(fake);
	}
}
```

- [ ] **Step 2: 运行测试**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug --filter "FullyQualifiedName~FakeContextPackageTests"
```

Expected: Passed。

- [ ] **Step 3: Commit**

```bash
git add Tests/kemo_card.Frame.Tests/Fakes/FakeCompositionContext.cs Tests/kemo_card.Frame.Tests/FakeContextPackageTests.cs
git commit -m "test(frame): add FakeCompositionContext for package Install tests"
```

---

### Task 6: `MainRoot` 接入占位启动链

**Files:**

- Modify: `Src/MainRoot.cs`

- [ ] **Step 1: 修改 `MainRoot`**

将 `Src/MainRoot.cs` 全文替换为：

```csharp
using Godot;
using KemoCard.Frame.FeatureKit;
using System;
using System.Collections.Generic;

namespace MainRoot;

public partial class MainRoot : Control
{
	public override void _Ready()
	{
		BootstrapFeatures();
	}

	private static void BootstrapFeatures()
	{
		var globalBus = new GlobalEventBus();
		IReadOnlyList<IFeaturePackage> packages = Array.Empty<IFeaturePackage>();
		var manager = new FeatureManager(packages);
		var context = new FeatureCompositionContext(globalBus, manager, packages);
		FeatureBootstrap.Run(packages, context, manager);
		GD.Print("功能框架已启动（当前无功能包）。");
	}
}
```

- [ ] **Step 2: 编译游戏工程**

Run:

```powershell
dotnet build kemo_card.csproj -c Debug
```

Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add Src/MainRoot.cs
git commit -m "chore: wire FeatureBootstrap from MainRoot with empty package list"
```

---

### Task 7: 全量测试与收尾

- [ ] **Step 1: 运行全部测试**

Run:

```powershell
dotnet test Tests/kemo_card.Frame.Tests/kemo_card.Frame.Tests.csproj -c Debug
```

Expected: 所有测试通过。

- [ ] **Step 2: Commit（如有遗漏仅格式化）**

若有仅包含换行或 usings 的微调，可执行：

```bash
git add -A
git commit -m "chore: frame tests green"
```

---

## 自检（Self-Review）

**1. Spec 覆盖对照**

| 规格章节 | 对应 Task |
|----------|-----------|
| 1.1 功能包 / 工厂清单 / 全局管理器 / 双总线 / 全局事件枚举 + 注册载荷 / 严格不一致失败 | Task 2–4 |
| 1.2 约束 A/D/A/A/A | Task 4–6（无 Autoload；显式列表；逆序 Shutdown；无包间引用由工程保证） |
| 2.2 各接口职责 | Task 2–4 |
| 2.3 契约在 `frame`，DTO `TestPingPayload` | Task 2 |
| 2.4 `Shutdown` + 内部总线封存 | Task 3–4 |
| 3 数据流 | Task 4、6 |
| 4 错误表 | Task 2、4 |
| 5 测试策略 Fake + 集成 + 冲突 | Task 2、4、5 |
| 6 非目标 | 未实现动态卸载 / DI / 反射发现（符合 YAGNI） |

**2. Placeholder 扫描：** 计划中不包含 `TBD`、空实现占位句或未给出代码的「编写测试」步骤（测试体已完整写出）。

**3. 类型一致性：** `GlobalEventId.FrameworkTestPing`、`TestPingPayload`、`FeatureBootstrap.Run(packages, context, manager)`、`FeatureManager.ShutdownPackages(ctx)` 与 `FeatureBootstrap.Shutdown(manager, context)` 在同一 Task 内已对齐；集成测试使用「Run 后手动 Publish」模型。

**已知缺口（可接受 / 后续）：** 规格推荐「多 csproj 硬化包边界」— 本计划保持单 `kemo_card` 程序集以降低首版摩擦；后续可拆 `kemo_card.Frame` 类库工程。

---

**计划已保存到 `Doc/superpowers/plans/2026-05-12-feature-framework-design.md`。两种执行方式：**

**1. Subagent-Driven（推荐）** — 每个 Task 派生子代理并在 Task 之间复核；必须使用 **superpowers:subagent-driven-development**。

**2. Inline Execution** — 本会话内按 Task 批量执行并设检查点；必须使用 **superpowers:executing-plans**。

**希望采用哪一种？**
