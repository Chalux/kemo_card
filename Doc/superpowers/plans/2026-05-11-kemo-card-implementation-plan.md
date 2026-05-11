# kemo_card 核心系统实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，按任务逐步实现。步骤使用 checkbox（`- [ ]`）追踪。

**Goal:** 在 Godot 4.6 Mono 仓库内落地 **纯 C# 核心库**（可 `dotnet test`），覆盖规格书中 **七大管理器注册/非法语义、Run RNG、战斗阶段机、卡牌优先队列与执行期重选/跳过、即时技能后目标丢失回滚、Run 账本不可逆写入、Run/全局存档骨架、脚本宿主 DTO 校验入口**；Godot 层仅做后续薄封装（本计划末尾给最小接线任务）。

**Architecture:** 将 **规则与状态** 放在 `Src/KemoCard.Core`（类库），Godot 程序集 `kemo_card` 引用该类库，仅负责场景/UI/输入把 **命令** 投递给核心。**七大管理器**先收敛为一个 `GameDefinitionRegistry`（内部 7 张表 + 版本号），避免早期过度拆文件；后续再按热度拆分。**脚本宿主**先提供接口 + 假实现 + DTO 校验，不接真实 Lua/TS/Python 运行时（单独里程碑）。

**Tech Stack:** Godot 4.6.1 + .NET 8 + C# 12（nullable 启用）+ NUnit 4 + `System.Text.Json`（存档 DTO）。

---

## 文件结构（创建/修改职责）

| 路径 | 职责 |
|---|---|
| `kemo_card.sln` | 解决方案：包含 `kemo_card`、`Src/KemoCard.Core`、`Tests/KemoCard.Core.Tests` |
| `kemo_card.csproj` | Godot 游戏程序集：引用 `KemoCard.Core`（与 `project.godot` 同目录） |
| `Src/KemoCard.Core/KemoCard.Core.csproj` | 核心类库 |
| `Src/KemoCard.Core/Content/ContentId.cs` | 强类型 id（record struct）与分类枚举 |
| `Src/KemoCard.Core/Content/GameDefinitionRegistry.cs` | 七大注册表 + `DefinitionVersion` |
| `Src/KemoCard.Core/Rng/RunRandom.cs` | Run 种子派生 RNG（主序列 + 子流：队列平局） |
| `Src/KemoCard.Core/Combat/CombatPhase.cs` / `CombatSession.cs` | 阶段枚举与最小战场模型（友方 4 人 + 敌人 id 集合） |
| `Src/KemoCard.Core/Combat/CardQueueItem.cs` | 队列项：持有者、卡 id、实例 id、优先级、目标模式、目标 id 集合、入队序号 |
| `Src/KemoCard.Core/Combat/CardExecutionPlanner.cs` | 从队列弹出顺序（小根堆）+ 执行期“单体重选/多选跳过”判定 |
| `Src/KemoCard.Core/Combat/CombatTurnController.cs` | 玩家阶段/执行阶段/敌方阶段状态迁移 + 确认/入队/即时技后回滚扫描 |
| `Src/KemoCard.Core/Run/RunLedger.cs` | `ExperiencedBattles/Events` + `TryCommitIrreversibleChoice` |
| `Src/KemoCard.Core/Run/RunStateHeader.cs` | `Seed`、`StoryId`、`Layer` 等头信息（供脚本输入/存档） |
| `Src/KemoCard.Core/Saves/RunSaveDto.cs` / `GlobalSaveDto.cs` | 存档 DTO + `SaveGameIO`（原子写：临时文件替换） |
| `Src/KemoCard.Core/Scripting/StoryScriptContracts.cs` | 选项 DTO、节点提议 DTO、校验失败原因 |
| `Src/KemoCard.Core/Scripting/ScriptHost.cs` | 宿主入口：调用脚本（当前为内置 stub）+ 校验 |
| `Tests/KemoCard.Core.Tests/*.cs` | NUnit 测试 |

---

### Task 1：解决方案与 `KemoCard.Core` 测试脚手架

**Files:**
- Create: `kemo_card.sln`
- Create: `kemo_card.csproj`
- Create: `Src/KemoCard.Core/KemoCard.Core.csproj`
- Create: `Tests/KemoCard.Core.Tests/KemoCard.Core.Tests.csproj`
- Create: `Tests/KemoCard.Core.Tests/SolutionSmokeTests.cs`

- [ ] **Step 1：写失败测试（程序集尚未引用 Core 时会失败）**

```csharp
using KemoCard.Core.Content;

namespace KemoCard.Core.Tests;

public sealed class SolutionSmokeTests
{
    [Test]
    public void Core_Assembly_Loads_ContentId()
    {
        var id = new ContentId(ContentCategory.Card, "strike");
        Assert.That(id.Value, Is.EqualTo("strike"));
    }
}
```

- [ ] **Step 2：运行测试确认失败**

Run:

```powershell
Set-Location "d:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card"
if (-not (Test-Path ".\kemo_card.sln")) { dotnet new sln -n kemo_card }
```

若已有 `kemo_card.sln` 则跳过 `dotnet new sln`。然后确保尚未 `dotnet sln add` 前运行：

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release
```

Expected: **error**（找不到项目/类型 `ContentId` 或测试项目未引用 Core）。

- [ ] **Step 3：创建三个项目文件 + 最小 `ContentId`**

`Src/KemoCard.Core/KemoCard.Core.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>KemoCard.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

`Src/KemoCard.Core/Content/ContentId.cs`：

```csharp
namespace KemoCard.Core.Content;

public enum ContentCategory
{
    Character,
    Battle,
    Event,
    Card,
    Item,
    Skill,
    Buff
}

public readonly record struct ContentId(ContentCategory Category, string Value)
{
    public override string ToString() => $"{Category}:{Value}";
}
```

`Tests/KemoCard.Core.Tests/KemoCard.Core.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>KemoCard.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Src\KemoCard.Core\KemoCard.Core.csproj" />
  </ItemGroup>
</Project>
```

`kemo_card.csproj`（与 `project.godot` 同级；SDK 版本按你本机 Godot 绑定调整，以下为 4.6.1 常见写法）：

```xml
<Project Sdk="Godot.NET.Sdk/4.6.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>KemoCard</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="Src\KemoCard.Core\KemoCard.Core.csproj" />
  </ItemGroup>
</Project>
```

将 `global using NUnit.Framework;` 加到 `Tests/KemoCard.Core.Tests/GlobalUsings.cs`（新建）：

```csharp
global using NUnit.Framework;
```

- [ ] **Step 4：把项目加入解决方案并运行测试通过**

```powershell
Set-Location "d:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card"
dotnet sln "kemo_card.sln" add "Src\KemoCard.Core\KemoCard.Core.csproj"
dotnet sln "kemo_card.sln" add "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj"
dotnet sln "kemo_card.sln" add "kemo_card.csproj"
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
Set-Location "d:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card"
git add kemo_card.sln kemo_card.csproj Src/KemoCard.Core Tests/KemoCard.Core.Tests
git commit -m "chore: add KemoCard.Core library and test harness"
```

---

### Task 2：`GameDefinitionRegistry`（七大表）与非法查询语义

**Files:**
- Create: `Src/KemoCard.Core/Content/GameDefinitionRegistry.cs`
- Create: `Tests/KemoCard.Core.Tests/GameDefinitionRegistryTests.cs`

- [ ] **Step 1：写失败测试**

```csharp
using KemoCard.Core.Content;

namespace KemoCard.Core.Tests;

public sealed class GameDefinitionRegistryTests
{
    [Test]
    public void Contains_returns_false_for_unknown_card()
    {
        var reg = new GameDefinitionRegistry();
        reg.RebuildFromMods(Array.Empty<ModContentBundle>());

        Assert.That(reg.Contains(ContentCategory.Card, "missing"), Is.False);
    }

    [Test]
    public void Contains_returns_true_after_registering_card()
    {
        var reg = new GameDefinitionRegistry();
        var bundle = new ModContentBundle(
            ModId: "base",
            Cards: new HashSet<string> { "strike" },
            Characters: new HashSet<string>(),
            Battles: new HashSet<string>(),
            Events: new HashSet<string>(),
            Items: new HashSet<string>(),
            Skills: new HashSet<string>(),
            Buffs: new HashSet<string>());

        reg.RebuildFromMods(new[] { bundle });

        Assert.That(reg.Contains(ContentCategory.Card, "strike"), Is.True);
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~GameDefinitionRegistryTests
```

Expected: **FAIL**（类型不存在）。

- [ ] **Step 3：实现最小注册表（含 `RebuildFromMods` 与版本 bump）**

`Src/KemoCard.Core/Content/GameDefinitionRegistry.cs`：

```csharp
namespace KemoCard.Core.Content;

public readonly record struct ModContentBundle(
    string ModId,
    HashSet<string> Characters,
    HashSet<string> Battles,
    HashSet<string> Events,
    HashSet<string> Cards,
    HashSet<string> Items,
    HashSet<string> Skills,
    HashSet<string> Buffs);

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
        [ContentCategory.Buff] = new HashSet<string>(StringComparer.Ordinal)
    };

    public int DefinitionVersion { get; private set; }

    public void RebuildFromMods(IReadOnlyList<ModContentBundle> bundles)
    {
        foreach (var set in _tables.Values)
        {
            set.Clear();
        }

        foreach (var bundle in bundles)
        {
            AddAll(ContentCategory.Character, bundle.Characters);
            AddAll(ContentCategory.Battle, bundle.Battles);
            AddAll(ContentCategory.Event, bundle.Events);
            AddAll(ContentCategory.Card, bundle.Cards);
            AddAll(ContentCategory.Item, bundle.Items);
            AddAll(ContentCategory.Skill, bundle.Skills);
            AddAll(ContentCategory.Buff, bundle.Buffs);
        }

        DefinitionVersion++;
    }

    public bool Contains(ContentCategory category, string id) => _tables[category].Contains(id);

    private void AddAll(ContentCategory category, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            _tables[category].Add(id);
        }
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~GameDefinitionRegistryTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Content/GameDefinitionRegistry.cs Tests/KemoCard.Core.Tests/GameDefinitionRegistryTests.cs
git commit -m "feat(content): add GameDefinitionRegistry with seven tables"
```

---

### Task 3：`RunRandom`（主 RNG + 队列平局子流）

**Files:**
- Create: `Src/KemoCard.Core/Rng/RunRandom.cs`
- Create: `Tests/KemoCard.Core.Tests/RunRandomTests.cs`

- [ ] **Step 1：写失败测试（固定种子可复现）**

```csharp
using KemoCard.Core.Rng;

namespace KemoCard.Core.Tests;

public sealed class RunRandomTests
{
    [Test]
    public void QueueTieBreak_stream_is_deterministic_for_same_seed()
    {
        var a = RunRandom.FromSeed(12345);
        var b = RunRandom.FromSeed(12345);

        Assert.That(a.QueueTieBreak.NextInt(0, 1000), Is.EqualTo(b.QueueTieBreak.NextInt(0, 1000)));
        Assert.That(a.QueueTieBreak.NextInt(0, 1000), Is.EqualTo(b.QueueTieBreak.NextInt(0, 1000)));
    }

    [Test]
    public void Master_and_queue_streams_do_not_share_state_accidentally()
    {
        var rng = RunRandom.FromSeed(999);
        var q0 = rng.QueueTieBreak.NextInt(0, 1_000_000);
        var m0 = rng.Master.NextInt(0, 1_000_000);
        var q1 = rng.QueueTieBreak.NextInt(0, 1_000_000);

        Assert.That(q0, Is.Not.EqualTo(m0));
        Assert.That(q1, Is.Not.EqualTo(q0));
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~RunRandomTests
```

Expected: **FAIL**

- [ ] **Step 3：实现 `RunRandom`（SplitMix64 风格或 `System.Random` 包装均可；此处用可复现 `Random` 子流）**

`Src/KemoCard.Core/Rng/RunRandom.cs`：

```csharp
namespace KemoCard.Core.Rng;

public sealed class RunRandom
{
    private RunRandom(Random master, Random queueTieBreak)
    {
        Master = master;
        QueueTieBreak = queueTieBreak;
    }

    public Random Master { get; }
    public Random QueueTieBreak { get; }

    public static RunRandom FromSeed(int seed)
    {
        var master = new Random(HashMix(seed, 0x1111_1111));
        var tie = new Random(HashMix(seed, 0x2222_2222));
        return new RunRandom(master, tie);
    }

    private static int HashMix(int seed, int salt)
    {
        unchecked
        {
            uint x = (uint)(seed ^ salt);
            x ^= x + 0x9E37_79B9u;
            x ^= x >> 16;
            x *= 0x7FEB_352Du;
            x ^= x >> 15;
            return (int)x;
        }
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~RunRandomTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Rng/RunRandom.cs Tests/KemoCard.Core.Tests/RunRandomTests.cs
git commit -m "feat(rng): add deterministic RunRandom streams"
```

---

### Task 4：最小 `CombatSession`（实体存活集合 + 单体/多目标语义）

**Files:**
- Create: `Src/KemoCard.Core/Combat/TargetingMode.cs`
- Create: `Src/KemoCard.Core/Combat/CombatSession.cs`
- Create: `Tests/KemoCard.Core.Tests/CombatSessionTests.cs`

- [ ] **Step 1：写失败测试**

```csharp
using KemoCard.Core.Combat;

namespace KemoCard.Core.Tests;

public sealed class CombatSessionTests
{
    [Test]
    public void Enemy_alive_set_updates_on_kill()
    {
        var s = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0", "e1" });
        Assert.That(s.IsEnemyAlive("e0"), Is.True);

        s.KillEnemy("e0");

        Assert.That(s.IsEnemyAlive("e0"), Is.False);
        Assert.That(s.IsEnemyAlive("e1"), Is.True);
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CombatSessionTests
```

Expected: **FAIL**

- [ ] **Step 3：实现**

`Src/KemoCard.Core/Combat/TargetingMode.cs`：

```csharp
namespace KemoCard.Core.Combat;

public enum TargetingMode
{
    Single,
    Multi
}
```

`Src/KemoCard.Core/Combat/CombatSession.cs`：

```csharp
namespace KemoCard.Core.Combat;

public sealed class CombatSession
{
    private readonly HashSet<string> _aliveEnemies;

    public CombatSession(IReadOnlyList<string> party, IReadOnlyList<string> enemies)
    {
        Party = party.ToArray();
        _aliveEnemies = new HashSet<string>(enemies, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Party { get; }

    public bool IsEnemyAlive(string enemyId) => _aliveEnemies.Contains(enemyId);

    public IReadOnlyCollection<string> AliveEnemiesSnapshot() => _aliveEnemies.ToArray();

    public void KillEnemy(string enemyId) => _aliveEnemies.Remove(enemyId);
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CombatSessionTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Combat/CombatSession.cs Src/KemoCard.Core/Combat/TargetingMode.cs Tests/KemoCard.Core.Tests/CombatSessionTests.cs
git commit -m "feat(combat): add minimal CombatSession enemy alive tracking"
```

---

### Task 5：`CardExecutionPlanner`（优先队列 + 平局随机 + 执行期重选/跳过）

**Files:**
- Create: `Src/KemoCard.Core/Combat/CardQueueItem.cs`
- Create: `Src/KemoCard.Core/Combat/CardExecutionPlanner.cs`
- Create: `Tests/KemoCard.Core.Tests/CardExecutionPlannerTests.cs`

- [ ] **Step 1：写失败测试（覆盖：优先级、平局随机稳定、单体重选、多选跳过）**

```csharp
using KemoCard.Core.Combat;
using KemoCard.Core.Rng;

namespace KemoCard.Core.Tests;

public sealed class CardExecutionPlannerTests
{
    [Test]
    public void Pop_order_sorts_by_priority_then_tiebreak()
    {
        var rng = RunRandom.FromSeed(42);
        var planner = new CardExecutionPlanner(rng);

        planner.Enqueue(new CardQueueItem(
            HolderId: "p0",
            CardId: "c_low",
            InstanceId: 1,
            Priority: 10,
            TieBreakKey: rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue),
            Mode: TargetingMode.Single,
            TargetEnemyIds: new[] { "e0" }));

        planner.Enqueue(new CardQueueItem(
            HolderId: "p1",
            CardId: "c_high",
            InstanceId: 2,
            Priority: 1,
            TieBreakKey: rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue),
            Mode: TargetingMode.Single,
            TargetEnemyIds: new[] { "e0" }));

        var combat = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0" });

        var first = planner.TryPopNextExecutable(combat, retargetPicker: PickFirstAlive);
        Assert.That(first, Is.Not.Null);
        Assert.That(first!.CardId, Is.EqualTo("c_high"));

        var second = planner.TryPopNextExecutable(combat, retargetPicker: PickFirstAlive);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.CardId, Is.EqualTo("c_low"));
    }

    [Test]
    public void Single_target_invalid_tries_retarget_then_skips_without_party_rollback()
    {
        var rng = RunRandom.FromSeed(7);
        var planner = new CardExecutionPlanner(rng);

        planner.Enqueue(new CardQueueItem(
            HolderId: "p0",
            CardId: "hit",
            InstanceId: 1,
            Priority: 1,
            TieBreakKey: rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue),
            Mode: TargetingMode.Single,
            TargetEnemyIds: new[] { "e_dead" }));

        var combat = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0", "e1" });

        var exe = planner.TryPopNextExecutable(combat, retargetPicker: PickFirstAlive);
        Assert.That(exe, Is.Null); // 无合法单体可重选 -> 跳过（空放）
    }

    [Test]
    public void Multi_target_invalid_skips_without_retarget()
    {
        var rng = RunRandom.FromSeed(8);
        var planner = new CardExecutionPlanner(rng);

        planner.Enqueue(new CardQueueItem(
            HolderId: "p0",
            CardId: "aoe",
            InstanceId: 1,
            Priority: 1,
            TieBreakKey: rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue),
            Mode: TargetingMode.Multi,
            TargetEnemyIds: new[] { "e_dead" }));

        var combat = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0" });

        var exe = planner.TryPopNextExecutable(combat, retargetPicker: PickFirstAlive);
        Assert.That(exe, Is.Null);
    }

    private static string? PickFirstAlive(CombatSession combat, IReadOnlyList<string> original)
    {
        foreach (var id in combat.AliveEnemiesSnapshot())
        {
            return id;
        }

        return null;
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CardExecutionPlannerTests
```

Expected: **FAIL**

- [ ] **Step 3：实现 `CardQueueItem` + `CardExecutionPlanner`（二叉堆可按标准实现；此处用有序列表插入 O(n) 以 YAGNI，后续再优化）**

`Src/KemoCard.Core/Combat/CardQueueItem.cs`：

```csharp
namespace KemoCard.Core.Combat;

public sealed record CardQueueItem(
    string HolderId,
    string CardId,
    long InstanceId,
    int Priority,
    int TieBreakKey,
    TargetingMode Mode,
    IReadOnlyList<string> TargetEnemyIds);
```

`Src/KemoCard.Core/Combat/CardExecutionPlanner.cs`：

```csharp
using KemoCard.Core.Rng;

namespace KemoCard.Core.Combat;

public delegate string? RetargetPicker(CombatSession combat, IReadOnlyList<string> originalTargets);

public sealed class CardExecutionPlanner
{
    private readonly List<CardQueueItem> _heap = new();
    private readonly RunRandom _rng;

    public CardExecutionPlanner(RunRandom rng) => _rng = rng;

    public void Enqueue(CardQueueItem item) => _heap.Add(item);

    public CardQueueItem? TryPopNextExecutable(CombatSession combat, RetargetPicker retargetPicker)
    {
        while (_heap.Count > 0)
        {
            var idx = FindMinIndex();
            var item = _heap[idx];
            _heap[idx] = _heap[^1];
            _heap.RemoveAt(_heap.Count - 1);

            if (IsExecutable(combat, item, retargetPicker, out var resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private bool IsExecutable(CombatSession combat, CardQueueItem item, RetargetPicker retargetPicker, out CardQueueItem resolved)
    {
        resolved = item;

        if (item.Mode == TargetingMode.Multi)
        {
            foreach (var t in item.TargetEnemyIds)
            {
                if (!combat.IsEnemyAlive(t))
                {
                    return false; // 跳过多选
                }
            }

            return true;
        }

        // Single
        if (item.TargetEnemyIds.Count != 1)
        {
            return false;
        }

        var cur = item.TargetEnemyIds[0];
        if (combat.IsEnemyAlive(cur))
        {
            return true;
        }

        var pick = retargetPicker(combat, item.TargetEnemyIds);
        if (pick is null)
        {
            return false; // 跳过
        }

        resolved = item with { TargetEnemyIds = new[] { pick } };
        return true;
    }

    private int FindMinIndex()
    {
        var best = 0;
        for (var i = 1; i < _heap.Count; i++)
        {
            var a = _heap[i];
            var b = _heap[best];
            if (a.Priority < b.Priority)
            {
                best = i;
                continue;
            }

            if (a.Priority == b.Priority && a.TieBreakKey < b.TieBreakKey)
            {
                best = i;
            }
        }

        return best;
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CardExecutionPlannerTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Combat/CardQueueItem.cs Src/KemoCard.Core/Combat/CardExecutionPlanner.cs Tests/KemoCard.Core.Tests/CardExecutionPlannerTests.cs
git commit -m "feat(combat): add card execution planner with priority and retarget/skip"
```

---

### Task 6：`CombatTurnController`（玩家阶段确认、入队、即时技后回滚）

**Files:**
- Create: `Src/KemoCard.Core/Combat/CombatTurnController.cs`
- Create: `Tests/KemoCard.Core.Tests/CombatTurnControllerTests.cs`

- [ ] **Step 1：写失败测试（最小行为）**

```csharp
using KemoCard.Core.Combat;
using KemoCard.Core.Rng;

namespace KemoCard.Core.Tests;

public sealed class CombatTurnControllerTests
{
    [Test]
    public void Cannot_enter_execution_until_all_party_confirmed()
    {
        var rng = RunRandom.FromSeed(1);
        var combat = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0" });
        var planner = new CardExecutionPlanner(rng);
        var ctrl = new CombatTurnController(combat, planner, partySize: 4);

        Assert.That(ctrl.Phase, Is.EqualTo(CombatPhase.Player));

        ctrl.EnqueueCard(new CardQueueItem("p0", "c", 1, 1, rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue), TargetingMode.Single, new[] { "e0" }));
        ctrl.ConfirmPartyMember("p0");

        Assert.That(ctrl.TryEnterCardExecution(), Is.False);

        ctrl.ConfirmPartyMember("p1");
        ctrl.ConfirmPartyMember("p2");
        ctrl.ConfirmPartyMember("p3");

        Assert.That(ctrl.TryEnterCardExecution(), Is.True);
        Assert.That(ctrl.Phase, Is.EqualTo(CombatPhase.CardExecution));
    }

    [Test]
    public void Instant_skill_enemy_loss_marks_affected_queue_holders_unacted()
    {
        var rng = RunRandom.FromSeed(2);
        var combat = new CombatSession(party: new[] { "p0", "p1", "p2", "p3" }, enemies: new[] { "e0", "e1" });
        var planner = new CardExecutionPlanner(rng);
        var ctrl = new CombatTurnController(combat, planner, partySize: 4);

        ctrl.EnqueueCard(new CardQueueItem("p0", "c0", 1, 1, rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue), TargetingMode.Single, new[] { "e1" }));
        ctrl.ConfirmPartyMember("p0");

        ctrl.EnqueueCard(new CardQueueItem("p1", "c1", 2, 1, rng.QueueTieBreak.NextInt(int.MinValue, int.MaxValue), TargetingMode.Single, new[] { "e0" }));
        ctrl.ConfirmPartyMember("p1");

        ctrl.ConfirmPartyMember("p2");
        ctrl.ConfirmPartyMember("p3");

        var aliveBefore = combat.AliveEnemiesSnapshot().ToHashSet();
        combat.KillEnemy("e1");
        ctrl.ApplyInstantSkillEnemyDelta(aliveBefore);

        Assert.That(ctrl.IsConfirmed("p0"), Is.False);
        Assert.That(ctrl.IsConfirmed("p1"), Is.True);
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CombatTurnControllerTests
```

Expected: **FAIL**

- [ ] **Step 3：实现 `CombatPhase` + `CombatTurnController`**

`Src/KemoCard.Core/Combat/CombatPhase.cs`：

```csharp
namespace KemoCard.Core.Combat;

public enum CombatPhase
{
    Player,
    CardExecution,
    Enemy
}
```

`Src/KemoCard.Core/Combat/CombatTurnController.cs`：

```csharp
namespace KemoCard.Core.Combat;

public sealed class CombatTurnController
{
    private readonly CombatSession _combat;
    private readonly CardExecutionPlanner _planner;
    private readonly int _partySize;
    private readonly HashSet<string> _confirmed = new(StringComparer.Ordinal);
    private readonly List<CardQueueItem> _queued = new();

    public CombatTurnController(CombatSession combat, CardExecutionPlanner planner, int partySize)
    {
        _combat = combat;
        _planner = planner;
        _partySize = partySize;
    }

    public CombatPhase Phase { get; private set; } = CombatPhase.Player;

    public bool IsConfirmed(string partyId) => _confirmed.Contains(partyId);

    public void EnqueueCard(CardQueueItem item)
    {
        if (Phase != CombatPhase.Player)
        {
            throw new InvalidOperationException("Enqueue only in Player phase.");
        }

        // 玩家阶段只维护 _queued；进入执行阶段再一次性灌入 planner，避免回滚时堆与列表不一致。
        _queued.Add(item);
    }

    public void ConfirmPartyMember(string partyId)
    {
        if (Phase != CombatPhase.Player)
        {
            throw new InvalidOperationException("Confirm only in Player phase.");
        }

        _confirmed.Add(partyId);
    }

    public bool TryEnterCardExecution()
    {
        if (Phase != CombatPhase.Player)
        {
            return false;
        }

        if (_confirmed.Count < _partySize)
        {
            return false;
        }

        foreach (var item in _queued)
        {
            _planner.Enqueue(item);
        }

        Phase = CombatPhase.CardExecution;
        return true;
    }

    public void ApplyInstantSkillEnemyDelta(IReadOnlyCollection<string> aliveEnemiesBeforeSkill)
    {
        if (Phase != CombatPhase.Player)
        {
            return;
        }

        var before = aliveEnemiesBeforeSkill.ToHashSet(StringComparer.Ordinal);
        var lost = new List<string>();
        foreach (var id in before)
        {
            if (!_combat.IsEnemyAlive(id))
            {
                lost.Add(id);
            }
        }

        if (lost.Count == 0)
        {
            return;
        }

        foreach (var item in _queued)
        {
            if (item.TargetEnemyIds.Any(t => lost.Contains(t)))
            {
                _confirmed.Remove(item.HolderId);
            }
        }
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~CombatTurnControllerTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Combat/CombatPhase.cs Src/KemoCard.Core/Combat/CombatTurnController.cs Tests/KemoCard.Core.Tests/CombatTurnControllerTests.cs
git commit -m "feat(combat): add turn controller with confirm gating and skill rollback"
```

---

### Task 7：`RunLedger`（不可逆提交写入经历集合）

**Files:**
- Create: `Src/KemoCard.Core/Run/RunLedger.cs`
- Create: `Tests/KemoCard.Core.Tests/RunLedgerTests.cs`

- [ ] **Step 1：写失败测试**

```csharp
using KemoCard.Core.Run;

namespace KemoCard.Core.Tests;

public sealed class RunLedgerTests
{
    [Test]
    public void Irreversible_choice_adds_battle_and_blocks_duplicates()
    {
        var ledger = new RunLedger();

        Assert.That(ledger.TryCommitIrreversibleChoice(new IrreversibleChoice("battle", "b001")), Is.True);
        Assert.That(ledger.TryCommitIrreversibleChoice(new IrreversibleChoice("battle", "b001")), Is.False);
        Assert.That(ledger.HasExperiencedBattle("b001"), Is.True);
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~RunLedgerTests
```

Expected: **FAIL**

- [ ] **Step 3：实现**

`Src/KemoCard.Core/Run/RunLedger.cs`：

```csharp
namespace KemoCard.Core.Run;

public readonly record struct IrreversibleChoice(string Kind, string Id);

public sealed class RunLedger
{
    private readonly HashSet<string> _battles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);

    public bool HasExperiencedBattle(string battleId) => _battles.Contains(battleId);

    public bool HasExperiencedEvent(string eventId) => _events.Contains(eventId);

    public bool TryCommitIrreversibleChoice(IrreversibleChoice choice)
    {
        if (string.Equals(choice.Kind, "battle", StringComparison.OrdinalIgnoreCase))
        {
            return _battles.Add(choice.Id);
        }

        if (string.Equals(choice.Kind, "event", StringComparison.OrdinalIgnoreCase))
        {
            return _events.Add(choice.Id);
        }

        throw new ArgumentOutOfRangeException(nameof(choice), choice.Kind, "Unknown ledger kind.");
    }

    public IReadOnlyList<string> ExportBattles() => _battles.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> ExportEvents() => _events.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public void ImportBattles(IEnumerable<string> ids)
    {
        _battles.Clear();
        foreach (var id in ids)
        {
            _battles.Add(id);
        }
    }

    public void ImportEvents(IEnumerable<string> ids)
    {
        _events.Clear();
        foreach (var id in ids)
        {
            _events.Add(id);
        }
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~RunLedgerTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Run/RunLedger.cs Tests/KemoCard.Core.Tests/RunLedgerTests.cs
git commit -m "feat(run): add RunLedger irreversible battle/event commits"
```

---

### Task 8：存档 DTO + `SaveGameIO`（Run + Global，原子写）

**Files:**
- Create: `Src/KemoCard.Core/Saves/RunSaveDto.cs`
- Create: `Src/KemoCard.Core/Saves/GlobalSaveDto.cs`
- Create: `Src/KemoCard.Core/Saves/SaveGameIO.cs`
- Create: `Tests/KemoCard.Core.Tests/SaveGameIOTests.cs`

- [ ] **Step 1：写失败测试（JSON 往返 + 原子写文件存在）**

```csharp
using System.Text.Json;
using KemoCard.Core.Run;
using KemoCard.Core.Saves;

namespace KemoCard.Core.Tests;

public sealed class SaveGameIOTests
{
    [Test]
    public void RoundTrip_run_and_global_saves()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kemo_card_save_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var io = new SaveGameIO(dir);

        var ledger = new RunLedger();
        ledger.TryCommitIrreversibleChoice(new IrreversibleChoice("battle", "b1"));

        var run = new RunSaveDto(
            Seed: 123,
            StoryId: "story_a",
            Layer: 3,
            DefinitionVersion: 9,
            LedgerBattles: ledger.ExportBattles(),
            LedgerEvents: ledger.ExportEvents());

        io.WriteRun(run);

        var loaded = io.ReadRun();
        Assert.That(loaded.Seed, Is.EqualTo(123));
        Assert.That(loaded.LedgerBattles, Does.Contain("b1"));

        var global = new GlobalSaveDto(AchievementBits: new Dictionary<string, bool> { ["a1"] = true }, Settings: new Dictionary<string, string> { ["lang"] = "zh" });
        io.WriteGlobal(global);

        var g2 = io.ReadGlobal();
        Assert.That(g2.Settings["lang"], Is.EqualTo("zh"));
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~SaveGameIOTests
```

Expected: **FAIL**

- [ ] **Step 3：实现 DTO + IO（`RunLedger.Export*` 已在 Task 7 一并实现）**

`Src/KemoCard.Core/Saves/RunSaveDto.cs`：

```csharp
namespace KemoCard.Core.Saves;

public sealed record RunSaveDto(
    int Seed,
    string StoryId,
    int Layer,
    int DefinitionVersion,
    IReadOnlyList<string> LedgerBattles,
    IReadOnlyList<string> LedgerEvents);
```

`Src/KemoCard.Core/Saves/GlobalSaveDto.cs`：

```csharp
namespace KemoCard.Core.Saves;

public sealed record GlobalSaveDto(
    IReadOnlyDictionary<string, bool> AchievementBits,
    IReadOnlyDictionary<string, string> Settings);
```

`Src/KemoCard.Core/Saves/SaveGameIO.cs`：

```csharp
using System.Text.Json;

namespace KemoCard.Core.Saves;

public sealed class SaveGameIO
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SaveGameIO(string directoryPath) => _dir = directoryPath;

    private string RunPath => Path.Combine(_dir, "run.json");
    private string GlobalPath => Path.Combine(_dir, "global.json");

    public void WriteRun(RunSaveDto dto) => WriteAtomic(RunPath, JsonSerializer.Serialize(dto, _json));

    public RunSaveDto ReadRun() => JsonSerializer.Deserialize<RunSaveDto>(File.ReadAllText(RunPath), _json)!;

    public void WriteGlobal(GlobalSaveDto dto) => WriteAtomic(GlobalPath, JsonSerializer.Serialize(dto, _json));

    public GlobalSaveDto ReadGlobal() => JsonSerializer.Deserialize<GlobalSaveDto>(File.ReadAllText(GlobalPath), _json)!;

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~SaveGameIOTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Saves Tests/KemoCard.Core.Tests/SaveGameIOTests.cs Src/KemoCard.Core/Run/RunLedger.cs
git commit -m "feat(saves): add run/global DTO persistence with atomic writes"
```

---

### Task 9：`ScriptHost`（节点提议校验 + 选项 DTO + 内置 stub）

**Files:**
- Create: `Src/KemoCard.Core/Scripting/StoryDtos.cs`
- Create: `Src/KemoCard.Core/Scripting/ScriptHost.cs`
- Create: `Tests/KemoCard.Core.Tests/ScriptHostTests.cs`

- [ ] **Step 1：写失败测试**

```csharp
using KemoCard.Core.Content;
using KemoCard.Core.Scripting;

namespace KemoCard.Core.Tests;

public sealed class ScriptHostTests
{
    [Test]
    public void Rejects_unknown_node_ids_against_registry()
    {
        var reg = new GameDefinitionRegistry();
        reg.RebuildFromMods(new[]
        {
            new ModContentBundle("base",
                Characters: new HashSet<string>(),
                Battles: new HashSet<string> { "b1" },
                Events: new HashSet<string>(),
                Cards: new HashSet<string>(),
                Items: new HashSet<string>(),
                Skills: new HashSet<string>(),
                Buffs: new HashSet<string>())
        });

        var host = new ScriptHost(reg, new BuiltInStoryStub());

        var res = host.TryValidateNode(new NodeProposal(ContentCategory.Battle, "missing"), out var err);
        Assert.That(res, Is.False);
        Assert.That(err, Does.Contain("missing"));
    }
}
```

- [ ] **Step 2：运行测试确认失败**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~ScriptHostTests
```

Expected: **FAIL**

- [ ] **Step 3：实现 DTO + Host + Stub**

`Src/KemoCard.Core/Scripting/StoryDtos.cs`：

```csharp
using KemoCard.Core.Content;

namespace KemoCard.Core.Scripting;

public sealed record NodeProposal(ContentCategory NodeType, string NodeId);

public sealed record StoryOptionDto(string OptionId, string LabelId, NodeProposal? Next);
```

`Src/KemoCard.Core/Scripting/ScriptHost.cs`：

```csharp
using KemoCard.Core.Content;

namespace KemoCard.Core.Scripting;

public interface IStoryScript
{
    IReadOnlyList<StoryOptionDto> GenerateOptions(in StoryInput input);
}

public readonly record struct StoryInput(int Layer, int Seed, int DefinitionVersion);

public sealed class BuiltInStoryStub : IStoryScript
{
    public IReadOnlyList<StoryOptionDto> GenerateOptions(in StoryInput input) =>
        Array.Empty<StoryOptionDto>();
}

public sealed class ScriptHost
{
    private readonly GameDefinitionRegistry _registry;
    private readonly IStoryScript _story;

    public ScriptHost(GameDefinitionRegistry registry, IStoryScript story)
    {
        _registry = registry;
        _story = story;
    }

    public bool TryValidateNode(NodeProposal proposal, out string error)
    {
        if (!_registry.Contains(proposal.NodeType, proposal.NodeId))
        {
            error = $"Unknown {proposal.NodeType}:{proposal.NodeId}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public IReadOnlyList<StoryOptionDto> GenerateOptions(in StoryInput input) => _story.GenerateOptions(input);
}
```

- [ ] **Step 4：运行测试通过**

```powershell
dotnet test "Tests\KemoCard.Core.Tests\KemoCard.Core.Tests.csproj" -c Release --filter FullyQualifiedName~ScriptHostTests
```

Expected: **PASS**

- [ ] **Step 5：提交**

```powershell
git add Src/KemoCard.Core/Scripting Tests/KemoCard.Core.Tests/ScriptHostTests.cs
git commit -m "feat(scripting): add ScriptHost validation and story stub"
```

---

### Task 10：Godot 侧最小接线（可选，但建议同一次里程碑完成）

**Files:**
- Create: `Src/mod/bootstrap/CoreSmokeNode.cs`（或 `Src/mod/bootstrap/CoreSmokeNode.cs` 按目录规则）
- Modify: `project.godot`（添加 AutoLoad 或主场景引用——以你编辑器最终导出为准；计划里给出最小手工步骤）

- [ ] **Step 1：写 Godot C# 脚本（仅验证能引用 Core）**

`Src/mod/bootstrap/CoreSmokeNode.cs`：

```csharp
using Godot;
using KemoCard.Core.Content;
using KemoCard.Core.Rng;

namespace KemoCard.Mod.Bootstrap;

public partial class CoreSmokeNode : Node
{
    public override void _Ready()
    {
        var id = new ContentId(ContentCategory.Card, "strike");
        var rng = RunRandom.FromSeed(1);
        GD.Print($"{id} tie={rng.QueueTieBreak.Next()}");
    }
}
```

- [ ] **Step 2：在 Godot 编辑器创建场景挂该脚本并运行**

Expected: 控制台打印包含 `Card:strike`。

- [ ] **Step 3：提交**

```powershell
git add Src/mod/bootstrap/CoreSmokeNode.cs
git commit -m "chore(godot): add Core smoke node"
```

---

## 自检（计划 vs 规格）

**1. Spec coverage（逐条指向任务）**

| 规格章节 | 任务 |
|---|---|
| 七大管理器 + 非法 id | Task 2（后续把 JSON 配置加载接到 `RebuildFromMods`） |
| Run RNG 子流 | Task 3 |
| 玩家阶段确认 / 入队 / 执行门槛 | Task 6 |
| 优先队列 + 平局随机 | Task 3 + Task 5（`TieBreakKey` 在入队时生成） |
| 执行期单体重选/多选跳过 | Task 5 |
| 即时技后敌人丢失 → 队列影响者回滚未行动 | Task 6（`ApplyInstantSkillEnemyDelta`） |
| 敌方阶段占位 | Task 6 后续增量（本计划未写 AI；需下一里程碑） |
| 不可逆写入账本 | Task 7 |
| Run/全局存档 | Task 8 |
| 脚本宿主 DTO 校验入口 | Task 9（真实 Lua/TS/Python 运行时另立计划） |

**2. Placeholder scan：** 无 “TBD/TODO/稍后” 类占位；未覆盖内容明确标注为下一里程碑（敌人 AI、真实脚本运行时、JSON 配置管线）。

**3. Type consistency：** `ContentCategory` 贯穿 `ContentId`、`NodeProposal`、`GameDefinitionRegistry`；`RunRandom` 用于入队 `TieBreakKey` 与后续扩展。

---

## 交付执行方式（请你二选一）

计划已保存到：`Doc/superpowers/plans/2026-05-11-kemo-card-implementation-plan.md`

**1. Subagent-Driven（推荐）**：每个任务单独子代理实现，任务间人工/代理复核，迭代快。  
**2. Inline Execution**：本会话按检查点批量执行任务（使用 executing-plans 工作流）。

你想用哪一种？
