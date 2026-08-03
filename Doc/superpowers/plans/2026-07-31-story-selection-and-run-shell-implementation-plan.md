# 故事选择与 Run 基础壳 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 Story 内容定义与管道接入，新增「选择故事」界面（ItemList + 详情 + Seed 输入 + 确定），创建 Run 并进入 Run 基础状态壳（故事/环/阶段/Seed/金币 + 放弃）。

## 实现状态（2026-07-31）

**已完成并验证（`dotnet test` 全量 518 通过）：** Task 1（Story 管道）、Task 2（unlock 校验 + Bootstrap 顺序）、Task 3（GlobalPersistentCondContext）、Task 4（StoryId 贯通 + seed 语义）、Task 5（RunRuntime + UI 注册）、Task 6（StorySelectDlg + MenuWin 绑定）、Task 7 代码与场景、Task 8 翻译键与样例故事、Task 9 规格/AGENT 回写。

**待人工验证：** Godot 编辑器内手动冒烟（Task 7 Step 4 / Task 8 Step 4）——翻译 `.translation` 文件由 Godot 编辑器导入 CSV 时自动重新生成；故事解锁态需在调试器调用 `GlobalModController.UnlockContent("story.kemo_first.clear")` 后重新打开选故事界面观察。

**实现偏差（较计划文本）：**
1. `StorySelectDlg.OnStorySelected(long index)` —— Godot `ItemList.ItemSelected` 信号实参类型为 `long`，非计划的 `int`。
2. `StorySelectDlg.InitEvent` 增加 `_eventsBound` 防重复绑定 guard（重复打开 Dialog 时不重复订阅）。
3. `StorySelectDlg.EvaluatePlayable` 增加 `unlock.ValueKind == JsonValueKind.Null` 显式返回可玩，与 `ContentDefinitionValidator.ValidateStories` 的 null 语义保持一致（显式 `"unlock": null` = 无门槛）。
4. 步骤中的 Commit 步骤（`Step N: Commit（仅当用户要求）`）均保持未勾选，等待用户明确指示。

**Architecture:** Story 走现有内容管道（`EContentCategory.Story` → `ContentModLoader` → `GameDefinitionStore`），`StoryDto.unlock` 内联条件由 `ConditionParser` 在校验期解析、运行期用 `ConditionEvaluator` 求值；`GlobalPersistentCondContext` 把 `HasFlag` 映射到全局存档 `Unlocks`。UI 侧新增 `RunRuntime` 会话门面持有当前 `RunController`，`StorySelectDlg`（Dialog）→ `RunMainWin`（Window）导航，Run 的 `StoryId` 固化进 `RunMod` / `RunDto`。

**Tech Stack:** C# / Godot 4.6 Mono / System.Text.Json / NUnit（`Tests/kemo_card.Ui.Tests`）

**权威规格:** [run-mod-design](../specs/2026-06-22-run-mod-design.md)、[content-mod-manager](../specs/2026-05-17-content-mod-manager-design.md)、[condition-system-design](../specs/2026-07-30-condition-system-design.md)、[kemo-card-design](../specs/2026-05-11-kemo-card-design.md)

## Global Constraints

- `Src/frame/` 不得引用 `KemoCard.Mod.*`
- 面向用户文案用翻译键（`Resource/Locale/strings.csv` 与 mod `content/translations/strings.csv`）；日志 / `GD.Print` 可用明文
- 不创建 `.uid` 文件；格式化仅针对本任务改过的文件
- `git commit` 仅在用户明确要求时执行；提交说明用简体中文
- 验证命令：

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q
```

- 格式化示例：`dotnet format kemo_card.csproj --include Src/mod/run/Ui/StorySelectDlg.cs`
- 本轮不做：环地图、事件/奖励/编队/战斗入口、开局选人、联机入口（固定单人）

---

## File Structure

| 路径 | 职责 | 操作 |
|------|------|------|
| `Src/frame/content/definitions/StoryDto.cs` | Story 定义（`ModDefinitionsBundle` 已引用，类缺失导致编译失败） | Create |
| `Src/frame/content/ContentCategory.cs` | 加 `Story` 枚举 | Modify |
| `Src/frame/content/ContentCategoryPaths.cs` | 加 `"stories"` 目录映射 | Modify |
| `Src/frame/content/ContentModLoader.cs` | 加载 `StoryDto` | Modify |
| `Src/frame/content/GameDefinitionStore.cs` | `_stories` 字典 + API | Modify |
| `Src/frame/content/ContentRegistryMerger.cs` | 合并 Story（含冲突） | Modify |
| `Src/frame/content/ContentDefinitionValidator.cs` | `ValidateStories`：解析 `unlock` 条件 | Modify |
| `Src/mod/ModFactory.cs` | `RegisterBuiltinConditions()` 提前到内容 Rebuild 前 | Modify |
| `Src/frame/scripting/HostRng.cs` | 暴露 `RunSeed`（手写 seed 精确落库） | Modify |
| `Src/mod/run/RunDto.cs` | 加 `StoryId` | Modify |
| `Src/mod/run/RunMod.cs` | 加 `StoryId` + ToDto/RestoreFrom + UI 注册静态方法 | Modify |
| `Src/mod/run/RunController.cs` | `CreateRun` 加 `storyId`，`RunSeed = rng.RunSeed` | Modify |
| `Src/mod/run/RunRuntime.cs` | 当前 Run 会话门面 | Create |
| `Src/mod/global/Condition/GlobalPersistentCondContext.cs` | `IPersistentCondContext` 生产适配（HasFlag → Unlocks） | Create |
| `Src/mod/run/Ui/RunUiIds.cs` | UI id 常量 | Create |
| `Src/mod/run/Ui/RunUiController.cs` | 静态打开 StorySelect / RunMain | Create |
| `Src/mod/run/Ui/StorySelectDlg.cs` | 选故事 Dialog（ItemList + 详情 + Seed + 确定） | Create |
| `Src/mod/run/Ui/StorySelectDlg.tscn` | 选故事场景 | Create |
| `Src/mod/run/Ui/RunMainWin.cs` | Run 基础壳 Window | Create |
| `Src/mod/run/Ui/RunMainWin.tscn` | Run 主界面场景 | Create |
| `Src/mod/global/Ui/MenuWin.cs` | `StartBtn` 绑定打开选故事 | Modify |
| `Src/MainRoot.cs` | `RunMod.RegisterUi(registry)` | Modify |
| `Resource/Locale/strings.csv` | UI 翻译键 | Modify |
| `Config/mods/base-game/content/stories/story_kemo_first.json` | 无门槛样例故事 | Create |
| `Config/mods/base-game/content/stories/story_kemo_second.json` | 依赖 unlock 样例故事 | Create |
| `Config/mods/base-game/content/translations/strings.csv` | 故事名/描述翻译键 | Modify |
| `Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs` | `stories` 目录 + `AddStory` | Modify |
| `Tests/kemo_card.Ui.Tests/ContentStoryTests.cs` | 加载/合并/校验/条件测试 | Create |
| `Tests/kemo_card.Ui.Tests/Condition/GlobalPersistentCondContextTests.cs` | HasFlag 映射测试 | Create |
| `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs` | `CreateRun` 调用点 + StoryId/seed 断言 | Modify |
| `Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs` | `CreateRun` 调用点 | Modify |
| `Doc/superpowers/specs/2026-05-17-content-mod-manager-design.md` | Story 分类与 DTO 字段 | Modify |
| `Doc/superpowers/specs/2026-07-30-condition-system-design.md` | 注明 Story.unlock 已接入 | Modify |
| `Doc/superpowers/specs/2026-06-22-run-mod-design.md` | 对齐实现（StoryId 固化 / seed 语义） | Modify |

---

### Task 1: Story 内容定义与管道接入（修复编译）

**Files:**
- Create: `Src/frame/content/definitions/StoryDto.cs`
- Modify: `Src/frame/content/ContentCategory.cs`
- Modify: `Src/frame/content/ContentCategoryPaths.cs`
- Modify: `Src/frame/content/ContentModLoader.cs`
- Modify: `Src/frame/content/GameDefinitionStore.cs`
- Modify: `Src/frame/content/ContentRegistryMerger.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs`
- Create: `Tests/kemo_card.Ui.Tests/ContentStoryTests.cs`

**Interfaces:**
- Produces: `StoryDto`（`Id`/`DisplayNameId`/`DescId`/`Author`/`Unlock`/`ScriptPath`/`ScriptEntry`/`SinglePlayerOnly`）、`GameDefinitionStore.TryGetStory`/`Stories`、`EContentCategory.Story`、`ContentModTestHelper.AddStory`
- Consumes: `ModDefinitionsBundle.Stories`（已存在）、`ContentDefinitionJson.Options`

- [x] **Step 1: 写加载/合并失败的测试**

```csharp
// Tests/kemo_card.Ui.Tests/ContentStoryTests.cs
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentStoryTests
{
    [Test]
    public void Rebuild_loads_stories_into_store()
    {
        var root = Directory.CreateTempSubdirectory("story_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "story_one", $$"""
                {
                  "displayNameId": "story.one.name",
                  "descId": "story.one.desc",
                  "author": "Tester",
                  "singlePlayerOnly": true
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod_a")], out _);

            Assert.That(registry.Store.TryGetStory("story_one", out var story), Is.True);
            Assert.That(story.Author, Is.EqualTo("Tester"));
            Assert.That(story.SinglePlayerOnly, Is.True);
            Assert.That(story.Id, Is.EqualTo("story_one"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_reports_story_id_conflict()
    {
        var root = Directory.CreateTempSubdirectory("story_conflict_test").FullName;
        try
        {
            var modA = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a", loadOrder: 1);
            ContentModTestHelper.AddStory(modA, "dup", "{}");
            var modB = ContentModTestHelper.CreateModFolder(root, "mod_b", "mod.b", loadOrder: 2);
            ContentModTestHelper.AddStory(modB, "dup", "{}");

            var registry = new GameDefinitionRegistry();
            registry.Rebuild(
                [ContentModTestHelper.CreateBundleFromFolder(root, "mod.a"),
                 ContentModTestHelper.CreateBundleFromFolder(root, "mod.b")],
                out var report);

            Assert.That(report.IdConflicts, Has.Some.Matches<ContentIdConflictEntry>(c =>
                c.Category == EContentCategory.Story && c.ContentId == "dup"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [x] **Step 2: 运行测试验证失败**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ContentStoryTests" --nologo -v q`
Expected: FAIL（`StoryDto` 未定义、`CreateBundleFromFolder` 未定义等编译错误）

- [x] **Step 3: 实现 StoryDto 与管道接入**

```csharp
// Src/frame/content/definitions/StoryDto.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class StoryDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    /// <summary>可选解锁条件（内联表达式，见条件系统规格 §8）。null/JSON null = 无门槛。</summary>
    [JsonPropertyName("unlock")]
    public JsonElement? Unlock { get; init; }

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("scriptEntry")]
    public string? ScriptEntry { get; init; }

    /// <summary>仅允许单人游玩。缺省 true（当前以单人为主）。</summary>
    [JsonPropertyName("singlePlayerOnly")]
    public bool SinglePlayerOnly { get; init; } = true;
}
```

```csharp
// Src/frame/content/ContentCategory.cs —— 枚举尾部追加 Story
public enum EContentCategory
{
    Character,
    Enemy,
    Battle,
    Event,
    Card,
    Item,
    Skill,
    Buff,
    Effect,
    SkillAction,
    Attribute,
    GameplayEffect,
    GameplayTag,
    Story,
}
```

```csharp
// Src/frame/content/ContentCategoryPaths.cs —— switch 追加
        EContentCategory.Story => "stories",
```

```csharp
// Src/frame/content/ContentModLoader.cs —— Load() 内第 14 个参数追加
                LoadDefinitions<SkillActionDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.SkillAction)),
                LoadDefinitions<StoryDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Story)));
```

```csharp
// Src/frame/content/GameDefinitionStore.cs —— 追加以下成员
    private readonly Dictionary<string, StoryDto> _stories = new(StringComparer.Ordinal);

    public bool TryGetStory(string id, out StoryDto dto) => _stories.TryGetValue(id, out dto!);

    public IReadOnlyDictionary<string, StoryDto> Stories => _stories;
```

```csharp
// Src/frame/content/GameDefinitionStore.cs —— Contains() switch 追加
        EContentCategory.Story => _stories.ContainsKey(id),
```

```csharp
// Src/frame/content/GameDefinitionStore.cs —— Clear() 追加
        _stories.Clear();
```

```csharp
// Src/frame/content/GameDefinitionStore.cs —— Remove() switch 追加
            case EContentCategory.Story:
                _stories.Remove(id);
                break;
```

```csharp
// Src/frame/content/GameDefinitionStore.cs —— 文件尾部追加
    internal Dictionary<string, StoryDto> StoriesMutable => _stories;
```

```csharp
// Src/frame/content/ContentRegistryMerger.cs —— MergeBundle() 末尾追加
        TryAddAll(bundle.ModId, EContentCategory.Story, definitions.Stories, store.StoriesMutable, ownerById, conflicts);
```

- [x] **Step 4: 扩展测试基建**

```csharp
// Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs —— CreateModFolder() 目录创建处追加
        Directory.CreateDirectory(Path.Combine(dir, "content", "stories"));
```

```csharp
// Tests/kemo_card.Ui.Tests/ContentModTestHelper.cs —— 追加
    public static void AddStory(string modDir, string storyId, string json = "{}")
    {
        WriteJson(modDir, "stories", storyId, json);
    }

    /// <summary>从磁盘扫描并加载指定 mod 的 bundle（复用 ContentModLoader 加载路径）。</summary>
    public static ModContentBundle CreateBundleFromFolder(string root, string modId)
    {
        var discovery = new ContentModDiscovery();
        var scan = discovery.Scan(root);
        var entry = scan.ValidMods.First(m => m.Manifest.ModId == modId);
        return ContentModLoader.Load(entry);
    }
```

- [x] **Step 5: 运行测试验证通过**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ContentStoryTests" --nologo -v q`
Expected: PASS（编译恢复 + 两个测试通过）

- [x] **Step 6: 全量测试确认无回归**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 7: Commit（仅当用户要求）**

---

### Task 2: Story unlock 条件校验 + Bootstrap 顺序修正

**Files:**
- Modify: `Src/frame/content/ContentDefinitionValidator.cs`
- Modify: `Src/mod/ModFactory.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentStoryTests.cs`

**Interfaces:**
- Consumes: `ConditionParser.TryParse<IPersistentCondContext>`、`ConditionDomains.Persistent`（frame/condition）
- Produces: `ContentDefinitionValidationError`（Category=Story，Message 带 `stories/<id>.json:unlock` 来源路径）

- [x] **Step 1: 写失败测试**

```csharp
// Tests/kemo_card.Ui.Tests/ContentStoryTests.cs —— 顶部 using 追加
using KemoCard.Frame.Condition;
using KemoCard.Mod.Global.Condition;
```

```csharp
// Tests/kemo_card.Ui.Tests/ContentStoryTests.cs —— 测试类内追加 SetUp（条件注册表不会随测试程序集自动注册）
    [SetUp]
    public void SetUp()
    {
        ConditionDomains.Persistent.Clear();
        BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
    }
```

```csharp
// Tests/kemo_card.Ui.Tests/ContentStoryTests.cs —— 追加
    [Test]
    public void Rebuild_removes_story_with_unknown_cond_type()
    {
        var root = Directory.CreateTempSubdirectory("story_cond_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "bad_story", """
                {
                  "displayNameId": "story.bad.name",
                  "unlock": { "NoSuchCondType": ["x"] }
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out var report);

            Assert.That(report.RemovedValidationErrors, Has.Some.Matches<ContentDefinitionValidationError>(e =>
                e.Category == EContentCategory.Story && e.DefinitionId == "bad_story"));
            Assert.That(registry.Store.Stories.ContainsKey("bad_story"), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_keeps_story_with_valid_has_flag_unlock()
    {
        var root = Directory.CreateTempSubdirectory("story_cond_ok_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "locked_story", """
                {
                  "displayNameId": "story.locked.name",
                  "unlock": { "HasFlag": ["story.prev.clear"] }
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out _);

            Assert.That(registry.Store.Stories.ContainsKey("locked_story"), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
```

- [x] **Step 2: 运行测试验证失败**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ContentStoryTests" --nologo -v q`
Expected: 两个新测试 FAIL（未实现校验，`bad_story` 仍存在）

- [x] **Step 3: 实现 ValidateStories**

```csharp
// Src/frame/content/ContentDefinitionValidator.cs —— using 区追加
using KemoCard.Frame.Condition;
```

```csharp
// Src/frame/content/ContentDefinitionValidator.cs —— Validate() 内追加
        ValidateStories(store, errors);
```

```csharp
// Src/frame/content/ContentDefinitionValidator.cs —— 追加
    private static void ValidateStories(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var story in store.Stories.Values)
        {
            if (story.Unlock is not { } unlock || unlock.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var sourcePath = $"content/stories/{story.Id}.json:unlock";
            if (!ConditionParser.TryParse<IPersistentCondContext>(
                    unlock,
                    ConditionDomains.Persistent,
                    sourcePath,
                    out _,
                    out var error))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Story,
                    story.Id,
                    error ?? "unlock 条件解析失败。"));
            }
        }
    }
```

- [x] **Step 4: 修正 Bootstrap 顺序（先注册条件类型，再重建内容）**

```csharp
// Src/mod/ModFactory.cs —— Bootstrap() 改为
    public static ModStartupResult Bootstrap(ModStartupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var global = BootstrapGlobalMod(context);
        RegisterBuiltinConditions();
        var content = BootstrapContentMods(context, global.Mod);

        var result = new ModStartupResult
        {
            GlobalMod = global.Mod,
            GlobalController = global.Controller,
            GlobalSaveService = global.SaveService,
            ContentModPipeline = content.Pipeline,
            ScriptRuntime = content.ScriptRuntime,
            ContentEffectScriptHost = content.ContentEffectScriptHost,
            StoryScriptInvoker = content.StoryScriptInvoker,
            EventScriptInvoker = content.EventScriptInvoker,
            BattleScriptInvoker = content.BattleScriptInvoker,
            EnemyAiScriptInvoker = content.EnemyAiScriptInvoker,
        };

        AppRoot.Initialize(result);
        RegisterBuiltinKeywords();
        return result;
    }
```

> 删除原 `AppRoot.Initialize(result)` 之后的 `RegisterBuiltinConditions();` 一行。条件系统规格 §8 要求「先 Register CondType，再校验引用它们的定义」。

- [x] **Step 5: 运行测试验证通过**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ContentStoryTests" --nologo -v q`
Expected: PASS

- [ ] **Step 6: Commit（仅当用户要求）**

---

### Task 3: GlobalPersistentCondContext 生产适配

**Files:**
- Create: `Src/mod/global/Condition/GlobalPersistentCondContext.cs`
- Create: `Tests/kemo_card.Ui.Tests/Condition/GlobalPersistentCondContextTests.cs`

**Interfaces:**
- Consumes: `IPersistentCondContext`、`GlobalModController.IsContentUnlocked`
- Produces: `GlobalPersistentCondContext(GlobalModController controller)`，`HasFlag(flagId)` = `IsContentUnlocked(flagId)`，`GetItemCount(itemId)` = 0（物品系统未接入）

- [x] **Step 1: 写失败测试**

```csharp
// Tests/kemo_card.Ui.Tests/Condition/GlobalPersistentCondContextTests.cs
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Condition;
using KemoCard.Mod.Global.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class GlobalPersistentCondContextTests
{
    [Test]
    public void HasFlag_maps_to_global_unlocks()
    {
        var model = new GlobalMod();
        var controller = new GlobalModController(model, new GlobalSaveService(Path.GetTempPath()));
        var context = new GlobalPersistentCondContext(controller);

        Assert.That(context.HasFlag("story.x.clear"), Is.False);

        controller.UnlockContent("story.x.clear");

        Assert.That(context.HasFlag("story.x.clear"), Is.True);
    }

    [Test]
    public void GetItemCount_returns_zero_until_item_system_lands()
    {
        var model = new GlobalMod();
        var controller = new GlobalModController(model, new GlobalSaveService(Path.GetTempPath()));
        var context = new GlobalPersistentCondContext(controller);

        Assert.That(context.GetItemCount("potion"), Is.EqualTo(0));
    }
}
```

- [x] **Step 2: 运行测试验证失败**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~GlobalPersistentCondContextTests" --nologo -v q`
Expected: FAIL（类型未定义）

- [x] **Step 3: 实现适配器**

```csharp
// Src/mod/global/Condition/GlobalPersistentCondContext.cs
using KemoCard.Frame.Condition;
using KemoCard.Mod.Global;

namespace KemoCard.Mod.Global.Condition;

/// <summary>
/// Persistent 条件的生产侧只读上下文。v1：HasFlag → 全局存档 Unlocks（与 IsContentUnlocked 同表）；
/// 物品数量待商店/道具规格落地后接入。
/// </summary>
public sealed class GlobalPersistentCondContext(GlobalModController controller) : IPersistentCondContext
{
    private readonly GlobalModController _controller = controller ?? throw new ArgumentNullException(nameof(controller));

    public bool HasFlag(string flagId) => _controller.IsContentUnlocked(flagId);

    public int GetItemCount(string itemId) => 0;
}
```

- [x] **Step 4: 运行测试验证通过**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~GlobalPersistentCondContextTests" --nologo -v q`
Expected: PASS

- [ ] **Step 5: Commit（仅当用户要求）**

---

### Task 4: Run StoryId 贯通 + seed 语义

**Files:**
- Modify: `Src/frame/scripting/HostRng.cs`
- Modify: `Src/mod/run/RunDto.cs`
- Modify: `Src/mod/run/RunMod.cs`
- Modify: `Src/mod/run/RunController.cs`
- Modify: `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs`
- Modify: `Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs`

**Interfaces:**
- Produces: `HostRng.RunSeed`（构造传入的 runSeed）、`RunDto.StoryId`、`RunMod.StoryId`、`RunController.CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)`
- Consumes: 规格 run-mod-design §3 签名 `CreateRun(string storyId, HostRng rng, ...)`

- [x] **Step 1: 写失败测试（红）**

```csharp
// Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs —— 追加
    [Test]
    public void CreateRun_fixes_story_id_and_uses_host_rng_seed()
    {
        var controller = new RunController(new RunMod());
        var rng = new HostRng(42, "story_select");

        var dto = controller.CreateRun("story_kemo_first", rng, [], isMultiplayer: false);

        Assert.That(dto.StoryId, Is.EqualTo("story_kemo_first"));
        Assert.That(dto.RunSeed, Is.EqualTo(42));
        Assert.That(controller.State.StoryId, Is.EqualTo("story_kemo_first"));
    }
```

- [x] **Step 2: 运行测试验证失败**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CreateRun_fixes_story_id_and_uses_host_rng_seed" --nologo -v q`
Expected: FAIL（`StoryId` 不存在 / `CreateRun` 签名不匹配）

- [x] **Step 3: HostRng 暴露 RunSeed**

```csharp
// Src/frame/scripting/HostRng.cs
public sealed class HostRng
{
    private readonly Random _random;

    public int RunSeed { get; }

    public HostRng(int runSeed, string streamKey)
    {
        RunSeed = runSeed;
        var mixed = HashCode.Combine(runSeed, streamKey);
        _random = new Random(mixed);
    }

    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
```

- [x] **Step 4: RunDto / RunMod 加 StoryId**

```csharp
// Src/mod/run/RunDto.cs —— RunDto 记录内追加
    public string StoryId { get; init; } = "";
```

```csharp
// Src/mod/run/RunMod.cs —— 属性区追加
    public string StoryId { get; set; } = "";
```

```csharp
// Src/mod/run/RunMod.cs —— ToDto() 内追加
            StoryId = StoryId,
```

```csharp
// Src/mod/run/RunMod.cs —— RestoreFrom() 内追加
        StoryId = dto.StoryId;
```

- [x] **Step 5: CreateRun 签名与 seed 语义**

```csharp
// Src/mod/run/RunController.cs —— CreateRun 改为
    public RunDto CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);

        Model.StoryId = storyId;
        Model.RunId = Guid.NewGuid().ToString("N");
        Model.RunSeed = rng.RunSeed;
        Model.IsMultiplayer = isMultiplayer;
        Model.CurrentRing = 1;
        Model.SharedGold = 0;
        // ... 其余保持不变（isMultiplayer 分叉、Phase = Event、ToDto）
    }
```

> 手写 seed 语义：UI 构造 `new HostRng(userSeed, "story_select")`，`RunSeed` 即用户输入；`-1` 随机时传随机种子。确定性与 .NET `Random` 同源可复现。

- [x] **Step 6: 更新既有调用点**

```csharp
// Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs
// CreateRun_sets_initial_state：
        var dto = controller.CreateRun("story_a", rng, candidates, isMultiplayer: false);
// CreateRun_singleplayer_has_local_controller_with_all_slots：
        var dto = controller.CreateRun("story_a", rng, [], isMultiplayer: false);
```

```csharp
// Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs
// 两处调用（约 34、88 行）：
        var dto = controller.CreateRun("story_a", rng, candidates, isMultiplayer: false);
```

- [x] **Step 7: 运行测试验证通过**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 8: Commit（仅当用户要求）**

---

### Task 5: RunRuntime 会话 + Run UI 注册

**Files:**
- Create: `Src/mod/run/RunRuntime.cs`
- Modify: `Src/mod/run/RunMod.cs`
- Modify: `Src/MainRoot.cs`

**Interfaces:**
- Produces: `RunRuntime.CreateNew(string storyId, int seed, IReadOnlyList<CharacterDto> candidates)`、`RunRuntime.Current`、`RunRuntime.Abandon()`、`RunMod.GetUIRegistrations()` / `RunMod.RegisterUi(UIRuntimeRegistry)`
- Consumes: `RunController.CreateRun`、`HostRng`、`UIRegistration`

- [x] **Step 1: 实现 RunRuntime**

```csharp
// Src/mod/run/RunRuntime.cs
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Run;

/// <summary>
/// Run 会话门面：持有当前 RunController，供选故事 / Run 主界面读取与操作。
/// seed &lt; 0 视为随机；&gt;= 0 视为手写 seed（精确成为 RunSeed）。
/// </summary>
public static class RunRuntime
{
    private static RunController? _current;

    public static RunController? Current => _current;

    public static RunController CreateNew(string storyId, int seed, IReadOnlyList<CharacterDto> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentNullException.ThrowIfNull(candidates);

        _current?.Dispose();
        var controller = new RunController(new RunMod());
        var hostRng = seed >= 0
            ? new HostRng(seed, "story_select")
            : new HostRng(Random.Shared.Next(1, int.MaxValue), "story_select");
        controller.CreateRun(storyId, hostRng, candidates, isMultiplayer: false);
        _current = controller;
        return controller;
    }

    public static void Abandon()
    {
        _current?.Dispose();
        _current = null;
    }
}
```

- [x] **Step 2: RunMod 加 UI 注册**

```csharp
// Src/mod/run/RunMod.cs —— using 区追加
using KemoCard.Frame.UI;
using KemoCard.Mod.Run.Ui;
```

```csharp
// Src/mod/run/RunMod.cs —— 类型内追加
    /// <summary>
    /// 声明式注册 run 模块所有 UI。
    /// </summary>
    public static IEnumerable<UIRegistration> GetUIRegistrations()
    {
        yield return UIRegistration.Dialog(RunUiIds.StorySelect, "Src/mod/run/Ui");
        yield return UIRegistration.Window(RunUiIds.RunMain, "Src/mod/run/Ui");
    }

    public static void RegisterUi(UIRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var reg in GetUIRegistrations())
        {
            registry.Register(reg.ToRuntimeEntry());
        }
    }
```

> 注意：`RunUiIds` 在 Task 6 才创建；若按序执行，本步与 Task 6 会短暂编译失败——将本步的 `RunMod` 修改与 Task 6 的 `RunUiIds.cs` 创建合并到同一提交批次即可。

- [x] **Step 3: MainRoot 注册 run UI**

```csharp
// Src/MainRoot.cs —— using 区追加
using KemoCard.Mod.Run;
```

```csharp
// Src/MainRoot.cs —— InitUIManager() 内
        var registry = new UIRuntimeRegistry();
        GlobalMod.RegisterUi(registry);
        RunMod.RegisterUi(registry);
```

- [x] **Step 4: 编译验证**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED（在 Task 6 完成 `RunUiIds.cs` 后整体通过）

- [ ] **Step 5: Commit（仅当用户要求）**

---

### Task 6: StorySelectDlg（选故事界面）

**Files:**
- Create: `Src/mod/run/Ui/RunUiIds.cs`
- Create: `Src/mod/run/Ui/RunUiController.cs`
- Create: `Src/mod/run/Ui/StorySelectDlg.cs`
- Create: `Src/mod/run/Ui/StorySelectDlg.tscn`
- Modify: `Src/mod/global/Ui/MenuWin.cs`

**Interfaces:**
- Consumes: `RunRuntime`、`GameDefinitionRegistry.Store` / `TryGetOwnerModId`、`GlobalModController`、`GlobalPersistentCondContext`、`ConditionParser` / `ConditionEvaluator` / `ConditionDomains.Persistent`、`AlertDlgPayload`（放弃流程在 Task 7）
- Produces: `RunUiIds.StorySelect` / `RunUiIds.RunMain`、`RunUiController.OpenStorySelectAsync()` / `OpenRunMainAsync()`

行为约定（grilling 共识）：
- ItemList 列全部故事；条件未通过的条目**可选中**但**灰底 + 前缀「未解锁」**，右侧仍显示基础信息 + 条件提示；确定按钮仅在「已选中且可玩」时可用
- Seed 输入默认 `-1`（随机），`>= 0` 视为手写 seed
- 确定：`RunRuntime.CreateNew(storyId, seed, [])` → 关闭本窗 → 打开 RunMainWin

- [x] **Step 1: 实现 UI 常量与控制器**

```csharp
// Src/mod/run/Ui/RunUiIds.cs
namespace KemoCard.Mod.Run.Ui;

public static class RunUiIds
{
    public const string StorySelect = "StorySelectDlg";
    public const string RunMain = "RunMainWin";
}
```

```csharp
// Src/mod/run/Ui/RunUiController.cs
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Mod.Run.Ui;

public static class RunUiController
{
    public static async Task<UIVo?> OpenStorySelectAsync()
    {
        return await (UIManager.Instance?.OpenAsync<StorySelectDlg>(new UiId<StorySelectDlg>(RunUiIds.StorySelect), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenRunMainAsync()
    {
        return await (UIManager.Instance?.OpenAsync<RunMainWin>(new UiId<RunMainWin>(RunUiIds.RunMain), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }
}
```

- [x] **Step 2: 实现 StorySelectDlg 逻辑**

```csharp
// Src/mod/run/Ui/StorySelectDlg.cs
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Condition;

namespace KemoCard.Mod.Run.Ui;

public partial class StorySelectDlg : BaseDlg
{
    [Export] private ItemList? _storyList;
    [Export] private Label? _lblName;
    [Export] private Label? _lblAuthor;
    [Export] private Label? _lblMod;
    [Export] private Label? _lblMode;
    [Export] private Label? _lblDesc;
    [Export] private Label? _lblUnlockHint;
    [Export] private LineEdit? _seedInput;
    [Export] private Button? _btnConfirm;
    [Export] private Button? _btnCancel;

    private readonly List<StoryEntry> _entries = [];
    private int _selectedIndex = -1;
    private GlobalPersistentCondContext? _condContext;

    public override string UIId => RunUiIds.StorySelect;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_storyList != null)
        {
            _storyList.ItemSelected += OnStorySelected;
        }

        if (_btnConfirm != null)
        {
            OnClicks(_btnConfirm, OnConfirm);
        }

        if (_btnCancel != null)
        {
            OnClicks(_btnCancel, Close);
        }
    }

    protected override void OnOpen()
    {
        _condContext = new GlobalPersistentCondContext(AppRoot.Services.GlobalController);
        RebuildList();
    }

    protected override void UpdateView()
    {
    }

    #region 列表与详情

    private void RebuildList()
    {
        _entries.Clear();
        _storyList?.Clear();

        var registry = AppRoot.Services.ContentModPipeline.Registry;
        foreach (var (id, story) in registry.Store.Stories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var playable = EvaluatePlayable(story);
            var displayName = string.IsNullOrWhiteSpace(story.DisplayNameId)
                ? id
                : Localization.Tr(story.DisplayNameId);
            var index = _storyList.AddItem(playable ? displayName : $"{Localization.Tr("UI_STORY_LOCKED")} · {displayName}");
            if (!playable)
            {
                _storyList.SetItemCustomBgColor(index, new Color(0.25f, 0.25f, 0.3f));
            }

            var modId = registry.TryGetOwnerModId(EContentCategory.Story, id, out var owner) ? owner : "?";
            _entries.Add(new StoryEntry(id, story, playable, modId));
        }

        _selectedIndex = -1;
        UpdateDetail();
    }

    private bool EvaluatePlayable(StoryDto story)
    {
        if (story.Unlock is not { } unlock || unlock.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        var registry = ConditionDomains.Persistent;
        if (!ConditionParser.TryParse(unlock, registry, $"story:{story.Id}", out var expr, out _) || expr is null)
        {
            return false;
        }

        return ConditionEvaluator.Evaluate(expr, _condContext!, registry).Passed;
    }

    private void OnStorySelected(int index)
    {
        _selectedIndex = index;
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var hasSelection = _selectedIndex >= 0 && _selectedIndex < _entries.Count;
        var entry = hasSelection ? _entries[_selectedIndex] : null;

        if (entry == null)
        {
            ClearDetail();
            if (_btnConfirm != null)
            {
                _btnConfirm.Disabled = true;
            }

            return;
        }

        var story = entry.Story;
        if (_lblName != null)
        {
            _lblName.Text = string.IsNullOrWhiteSpace(story.DisplayNameId) ? entry.Id : Localization.Tr(story.DisplayNameId);
        }

        if (_lblAuthor != null)
        {
            _lblAuthor.Text = string.IsNullOrWhiteSpace(story.Author) ? "-" : story.Author;
        }

        if (_lblMod != null)
        {
            _lblMod.Text = entry.ModId;
        }

        if (_lblMode != null)
        {
            _lblMode.Text = Localization.Tr(story.SinglePlayerOnly ? "UI_STORY_MODE_SINGLE" : "UI_STORY_MODE_COOP");
        }

        if (_lblDesc != null)
        {
            _lblDesc.Text = string.IsNullOrWhiteSpace(story.DescId) ? "-" : Localization.Tr(story.DescId);
        }

        if (_lblUnlockHint != null)
        {
            _lblUnlockHint.Visible = !entry.Playable;
            _lblUnlockHint.Text = entry.Playable ? "" : Localization.Tr("UI_STORY_UNLOCK_REQUIRED");
        }

        if (_btnConfirm != null)
        {
            _btnConfirm.Disabled = !entry.Playable;
        }
    }

    private void ClearDetail()
    {
        if (_lblName != null)
        {
            _lblName.Text = "-";
        }

        if (_lblAuthor != null)
        {
            _lblAuthor.Text = "-";
        }

        if (_lblMod != null)
        {
            _lblMod.Text = "-";
        }

        if (_lblMode != null)
        {
            _lblMode.Text = "-";
        }

        if (_lblDesc != null)
        {
            _lblDesc.Text = "-";
        }

        if (_lblUnlockHint != null)
        {
            _lblUnlockHint.Visible = false;
        }
    }

    #endregion

    #region 确定与取消

    private void OnConfirm()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
        {
            return;
        }

        var entry = _entries[_selectedIndex];
        if (!entry.Playable)
        {
            return;
        }

        RunRuntime.CreateNew(entry.Id, ParseSeed(), []);
        Close();
        _ = RunUiController.OpenRunMainAsync();
    }

    private int ParseSeed()
    {
        var text = _seedInput?.Text ?? "-1";
        return int.TryParse(text, out var seed) ? seed : -1;
    }

    #endregion

    private sealed record StoryEntry(string Id, StoryDto Story, bool Playable, string ModId);
}
```

- [x] **Step 3: 绑定菜单开始按钮**

```csharp
// Src/mod/global/Ui/MenuWin.cs —— using 区追加
using KemoCard.Mod.Run.Ui;
```

```csharp
// Src/mod/global/Ui/MenuWin.cs —— InitEvent() 内追加
        if (StartBtn != null)
        {
            OnClicks(StartBtn, () => _ = RunUiController.OpenStorySelectAsync());
        }
```

- [x] **Step 4: 编写 StorySelectDlg.tscn 场景**

```
[gd_scene format=3]

[ext_resource type="Script" path="res://Src/mod/run/Ui/StorySelectDlg.cs" id="1_storyselect"]

[node name="StorySelectDlg" type="Control" node_paths=PackedStringArray("_storyList", "_lblName", "_lblAuthor", "_lblMod", "_lblMode", "_lblDesc", "_lblUnlockHint", "_seedInput", "_btnConfirm", "_btnCancel")]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_storyselect")
_storyList = NodePath("Panel/VBox/Content/Left/StoryList")
_lblName = NodePath("Panel/VBox/Content/Right/Form/NameValue")
_lblAuthor = NodePath("Panel/VBox/Content/Right/Form/AuthorValue")
_lblMod = NodePath("Panel/VBox/Content/Right/Form/ModValue")
_lblMode = NodePath("Panel/VBox/Content/Right/Form/ModeValue")
_lblDesc = NodePath("Panel/VBox/Content/Right/DescValue")
_lblUnlockHint = NodePath("Panel/VBox/Content/Right/UnlockHint")
_seedInput = NodePath("Panel/VBox/SeedRow/SeedInput")
_btnConfirm = NodePath("Panel/VBox/BtnRow/BtnConfirm")
_btnCancel = NodePath("Panel/VBox/BtnRow/BtnCancel")

[node name="Panel" type="Panel" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 140.0
offset_top = 70.0
offset_right = -140.0
offset_bottom = -70.0
grow_horizontal = 2
grow_vertical = 2

[node name="VBox" type="VBoxContainer" parent="Panel"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
theme_override_constants/separation = 10

[node name="Title" type="Label" parent="Panel/VBox"]
layout_mode = 2
theme_override_font_sizes/font_size = 30
text = "UI_RUN_STORY_SELECT_TITLE"
horizontal_alignment = 1

[node name="Content" type="HBoxContainer" parent="Panel/VBox"]
layout_mode = 2
size_flags_vertical = 3
theme_override_constants/separation = 24

[node name="Left" type="VBoxContainer" parent="Panel/VBox/Content"]
layout_mode = 2
size_flags_horizontal = 3
theme_override_constants/separation = 6

[node name="LeftTitle" type="Label" parent="Panel/VBox/Content/Left"]
layout_mode = 2
text = "UI_STORY_LIST"

[node name="StoryList" type="ItemList" parent="Panel/VBox/Content/Left"]
custom_minimum_size = Vector2(380, 0)
layout_mode = 2
size_flags_vertical = 3

[node name="Right" type="VBoxContainer" parent="Panel/VBox/Content"]
layout_mode = 2
size_flags_horizontal = 3
theme_override_constants/separation = 8

[node name="RightTitle" type="Label" parent="Panel/VBox/Content/Right"]
layout_mode = 2
text = "UI_STORY_DETAILS"

[node name="Form" type="GridContainer" parent="Panel/VBox/Content/Right"]
layout_mode = 2
columns = 2
theme_override_constants/h_separation = 12

[node name="NameLabel" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2
text = "UI_STORY_NAME"

[node name="NameValue" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2

[node name="AuthorLabel" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2
text = "UI_STORY_AUTHOR"

[node name="AuthorValue" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2

[node name="ModLabel" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2
text = "UI_STORY_MOD"

[node name="ModValue" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2

[node name="ModeLabel" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2
text = "UI_STORY_MODE"

[node name="ModeValue" type="Label" parent="Panel/VBox/Content/Right/Form"]
layout_mode = 2

[node name="DescValue" type="Label" parent="Panel/VBox/Content/Right"]
layout_mode = 2
size_flags_vertical = 3
autowrap_mode = 3

[node name="UnlockHint" type="Label" parent="Panel/VBox/Content/Right"]
layout_mode = 2
theme_override_colors/font_color = Color(1, 0.45, 0.45, 1)
autowrap_mode = 3

[node name="SeedRow" type="HBoxContainer" parent="Panel/VBox"]
layout_mode = 2
theme_override_constants/separation = 12

[node name="SeedLabel" type="Label" parent="Panel/VBox/SeedRow"]
layout_mode = 2
text = "UI_RUN_SEED_INPUT_LABEL"

[node name="SeedInput" type="LineEdit" parent="Panel/VBox/SeedRow"]
custom_minimum_size = Vector2(160, 0)
layout_mode = 2
text = "-1"

[node name="BtnRow" type="HBoxContainer" parent="Panel/VBox"]
layout_mode = 2
alignment = 1
theme_override_constants/separation = 24

[node name="BtnConfirm" type="Button" parent="Panel/VBox/BtnRow"]
custom_minimum_size = Vector2(160, 48)
layout_mode = 2
text = "UI_RUN_START"
disabled = true

[node name="BtnCancel" type="Button" parent="Panel/VBox/BtnRow"]
custom_minimum_size = Vector2(160, 48)
layout_mode = 2
text = "UI_COMMON_BACK"
```

- [x] **Step 5: 编译 + 全量测试**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 6: Commit（仅当用户要求）**

---

### Task 7: RunMainWin（Run 基础壳）+ 放弃流程

**Files:**
- Create: `Src/mod/run/Ui/RunMainWin.cs`
- Create: `Src/mod/run/Ui/RunMainWin.tscn`

**Interfaces:**
- Consumes: `RunRuntime.Current`、`GameDefinitionStore.TryGetStory`、`GlobalModController.OpenAlertAsync`、`AlertDlgPayload`
- Produces: `RunMainWin`（`UI_RUN_PHASE_<UPPER>` 翻译键读法）

行为约定：显示故事名、阶段、环/MaxRing、Seed、金币（单人 `SharedGold`）；放弃 → Alert 确认 → `RunRuntime.Abandon()` → 关闭本窗 → 回主菜单。

- [x] **Step 1: 实现 RunMainWin 逻辑**

```csharp
// Src/mod/run/Ui/RunMainWin.cs
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Run.Ui;

public partial class RunMainWin : BaseWin
{
    [Export] private Label? _lblStory;
    [Export] private Label? _lblPhase;
    [Export] private Label? _lblRing;
    [Export] private Label? _lblSeed;
    [Export] private Label? _lblGold;
    [Export] private Button? _btnAbandon;

    public override string UIId => RunUiIds.RunMain;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_btnAbandon != null)
        {
            OnClicks(_btnAbandon, OnAbandon);
        }
    }

    protected override void OnOpen()
    {
        UpdateView();
    }

    protected override void UpdateView()
    {
        var run = RunRuntime.Current;
        if (run == null)
        {
            Close();
            return;
        }

        var state = run.State;
        var storyName = state.StoryId;
        if (AppRoot.Services.ContentModPipeline.Registry.Store.TryGetStory(state.StoryId, out var story)
            && !string.IsNullOrWhiteSpace(story.DisplayNameId))
        {
            storyName = Localization.Tr(story.DisplayNameId);
        }

        if (_lblStory != null)
        {
            _lblStory.Text = storyName;
        }

        if (_lblPhase != null)
        {
            _lblPhase.Text = Localization.Tr($"UI_RUN_PHASE_{state.Phase.ToString().ToUpperInvariant()}");
        }

        if (_lblRing != null)
        {
            _lblRing.Text = $"{state.CurrentRing} / {state.MaxRing}";
        }

        if (_lblSeed != null)
        {
            _lblSeed.Text = state.RunSeed.ToString();
        }

        if (_lblGold != null)
        {
            _lblGold.Text = run.GetGold().ToString();
        }
    }

    #region 放弃

    private void OnAbandon()
    {
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_RUN_CONFIRM_ABANDON_TITLE",
            DescKey = "UI_RUN_CONFIRM_ABANDON_DESC",
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = AbandonAndExit,
        });
    }

    private void AbandonAndExit()
    {
        RunRuntime.Abandon();
        Close();
        _ = GlobalModController.OpenMenuAsync();
    }

    #endregion
}
```

- [x] **Step 2: 编写 RunMainWin.tscn 场景**

```
[gd_scene format=3]

[ext_resource type="Script" path="res://Src/mod/run/Ui/RunMainWin.cs" id="1_runmain"]

[node name="RunMainWin" type="Control" node_paths=PackedStringArray("_lblStory", "_lblPhase", "_lblRing", "_lblSeed", "_lblGold", "_btnAbandon")]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_runmain")
_lblStory = NodePath("Panel/VBox/StoryLabel")
_lblPhase = NodePath("Panel/VBox/Grid/PhaseValue")
_lblRing = NodePath("Panel/VBox/Grid/RingValue")
_lblSeed = NodePath("Panel/VBox/Grid/SeedValue")
_lblGold = NodePath("Panel/VBox/Grid/GoldValue")
_btnAbandon = NodePath("Panel/VBox/BtnAbandon")

[node name="Panel" type="Panel" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 200.0
offset_top = 120.0
offset_right = -200.0
offset_bottom = -120.0
grow_horizontal = 2
grow_vertical = 2

[node name="VBox" type="VBoxContainer" parent="Panel"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
theme_override_constants/separation = 16

[node name="StoryLabel" type="Label" parent="Panel/VBox"]
layout_mode = 2
theme_override_font_sizes/font_size = 34
horizontal_alignment = 1

[node name="Grid" type="GridContainer" parent="Panel/VBox"]
layout_mode = 2
columns = 2
size_flags_vertical = 3
theme_override_constants/h_separation = 16
theme_override_constants/v_separation = 10

[node name="PhaseLabel" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2
text = "UI_RUN_PHASE_LABEL"

[node name="PhaseValue" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2

[node name="RingLabel" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2
text = "UI_RUN_RING_LABEL"

[node name="RingValue" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2

[node name="SeedLabel" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2
text = "UI_RUN_SEED_VALUE"

[node name="SeedValue" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2

[node name="GoldLabel" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2
text = "UI_RUN_GOLD_LABEL"

[node name="GoldValue" type="Label" parent="Panel/VBox/Grid"]
layout_mode = 2

[node name="BtnAbandon" type="Button" parent="Panel/VBox"]
custom_minimum_size = Vector2(180, 48)
layout_mode = 2
size_flags_horizontal = 4
text = "UI_RUN_ABANDON"
```

- [x] **Step 3: 编译 + 全量测试**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 4: 手动冒烟（Godot 编辑器运行游戏）**（待人工执行，见顶部「实现状态」）

流程：主菜单 →「新游戏」→ 选故事 Dialog（两个故事：科莫的初见可玩、科莫的试炼灰底未解锁）→ 输入 seed `-1` / 手写 `42` → 确定 → RunMainWin（故事名/阶段 Event/环 1/3/Seed/金币 0）→ 放弃 → Alert 确认 → 回主菜单。
Expected: 全部符合；`42` 的 RunSeed 显示 42；选「科莫的试炼」时确定按钮 disabled。

- [ ] **Step 5: Commit（仅当用户要求）**

---

### Task 8: 翻译键 + base-game 样例故事

**Files:**
- Modify: `Resource/Locale/strings.csv`
- Create: `Config/mods/base-game/content/stories/story_kemo_first.json`
- Create: `Config/mods/base-game/content/stories/story_kemo_second.json`
- Modify: `Config/mods/base-game/content/translations/strings.csv`

- [x] **Step 1: 追加 UI 翻译键**

```csv
// Resource/Locale/strings.csv —— 文件末尾追加
UI_RUN_STORY_SELECT_TITLE,选择故事,Select Story
UI_STORY_LIST,故事列表,Stories
UI_STORY_DETAILS,故事详情,Story Details
UI_STORY_NAME,名称,Name
UI_STORY_AUTHOR,作者,Author
UI_STORY_MOD,所属 Mod,Mod
UI_STORY_DESC,描述,Description
UI_STORY_MODE,模式,Mode
UI_STORY_MODE_SINGLE,仅单人,Single-player Only
UI_STORY_MODE_COOP,可联机,Co-op
UI_STORY_LOCKED,未解锁,Locked
UI_STORY_UNLOCK_REQUIRED,未满足开启条件,Requirements not met
UI_RUN_SEED_INPUT_LABEL,种子（-1 随机）,Seed (-1 = Random)
UI_RUN_START,开始 Run,Start Run
UI_RUN_ABANDON,放弃 Run,Abandon Run
UI_RUN_PHASE_LABEL,阶段,Phase
UI_RUN_RING_LABEL,环,Ring
UI_RUN_SEED_VALUE,种子,Seed
UI_RUN_GOLD_LABEL,金币,Gold
UI_RUN_CONFIRM_ABANDON_TITLE,放弃 Run,Abandon Run
UI_RUN_CONFIRM_ABANDON_DESC,确定要放弃本次 Run 吗？,Abandon this run?
UI_RUN_PHASE_INIT,初始,Init
UI_RUN_PHASE_EVENT,事件,Event
UI_RUN_PHASE_REWARD,奖励,Reward
UI_RUN_PHASE_BATTLE,战斗,Battle
UI_RUN_PHASE_BATTLEEND,战斗结算,Battle End
UI_RUN_PHASE_RINGEND,环结束,Ring End
UI_RUN_PHASE_FINISHED,已结束,Finished
```

- [x] **Step 2: 创建样例故事**

```json
// Config/mods/base-game/content/stories/story_kemo_first.json
{
  "displayNameId": "story.kemo_first.name",
  "descId": "story.kemo_first.desc",
  "author": "KemoCard Team",
  "singlePlayerOnly": true
}
```

```json
// Config/mods/base-game/content/stories/story_kemo_second.json
{
  "displayNameId": "story.kemo_second.name",
  "descId": "story.kemo_second.desc",
  "author": "KemoCard Team",
  "unlock": { "HasFlag": ["story.kemo_first.clear"] },
  "singlePlayerOnly": true
}
```

- [x] **Step 3: 追加故事翻译键**

```csv
// Config/mods/base-game/content/translations/strings.csv —— 文件末尾追加
story.kemo_first.name,科莫的初见,Kemo's First Contact
story.kemo_first.desc,科莫的故事由此开始。抵达森林，遭遇史莱姆。……,Kemo's story begins. Reach the forest and meet the slime. ...
story.kemo_second.name,科莫的试炼,Kemo's Trial
story.kemo_second.desc,通关「科莫的初见」后解锁的进阶故事。,An advanced story unlocked after clearing "Kemo's First Contact".
```

- [ ] **Step 4: 全量测试 + 手动冒烟**（自动测试已验证通过；Godot 编辑器手动冒烟待人工执行，见顶部「实现状态」）

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

手动：Godot 运行，选故事列表应显示两条（第二条灰底未解锁）。注意：由于暂无通关系统写入 `story.kemo_first.clear`，验证解锁态可在调试器 `GlobalModController.UnlockContent("story.kemo_first.clear")` 后重新打开选故事界面观察。

- [ ] **Step 5: Commit（仅当用户要求）**

---

### Task 9: 规格与文档回写

**Files:**
- Modify: `Doc/superpowers/specs/2026-05-17-content-mod-manager-design.md`
- Modify: `Doc/superpowers/specs/2026-07-30-condition-system-design.md`
- Modify: `Doc/superpowers/specs/2026-06-22-run-mod-design.md`

- [x] **Step 1: 内容规格补 Story 分类**

`2026-05-17-content-mod-manager-design.md`：在内容分类/DTO 清单中补：
- `EContentCategory.Story`，目录 `content/stories/`
- `StoryDto` 字段：`id`（注入）、`displayNameId`、`descId`、`author`（作者署名，明文非翻译键）、`unlock?`（内联条件表达式）、`scriptPath?`、`scriptEntry?`、`singlePlayerOnly`（缺省 `true`）
- 校验：`unlock` 用 Persistent 域 `ConditionParser` 解析，错误带 `stories/<id>.json:unlock` 来源路径；未知 CondType → 定义移除

- [x] **Step 2: 条件规格注明接入**

`2026-07-30-condition-system-design.md` §8 内容接入：注明首个接入 DTO 为 `StoryDto.unlock`（Persistent 域）；运行期求值在 UI（`StorySelectDlg`）处，`HasFlag` 经 `GlobalPersistentCondContext` 映射到全局存档 `Unlocks`。

- [x] **Step 3: Run 规格对齐实现**

`2026-06-22-run-mod-design.md`：
- 实现对齐 `CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)`（storyId 必选，非法 id 拒绝创建；`RunSeed = rng.RunSeed`，手写 seed 语义）
- 追加界面层说明：选故事 UI（Dialog）→ `RunRuntime` 会话 → Run 主界面壳（Window）；`StoryId` 已固化进 `RunMod`/`RunDto`
- 环地图、事件/奖励/编队/战斗入口明确后置，不在本里程碑

- [x] **Step 4: 更新 AGENT 文档**

按 skill `maintain-agent-doc` 更新 `Doc/AGENT.md` 目录地图：`Src/mod/run/` 备注追加 `Ui/`（StorySelectDlg、RunMainWin）；`Src/mod/global/Condition/` 追加 `GlobalPersistentCondContext`。

- [ ] **Step 5: Commit（仅当用户要求）**

---

## Self-Review

**规格覆盖**
- Story 内容定义 + 管道（content-mod-manager、condition 规格接入）→ Task 1 / 2 / 8 / 9
- 解锁门槛（grilling 共识：Condition 现有框架；HasFlag → Unlocks）→ Task 2 / 3
- 选故事 UI（ItemList、详情、Seed、确定）→ Task 6
- CreateRun storyId / RunSeed（run-mod-design）→ Task 4
- Run 基础壳 + 放弃（grilling 共识 5 / 6）→ Task 7
- 单人 `singlePlayerOnly`（展示不拦截）→ Task 1 DTO / Task 6 详情
- 无环地图 / 无事件 / 无选人（grilling 共识）→ 未实现（显式 out of scope）

**占位符扫描**：无「TBD / 适当处理 / 类似 Task N」；`CreateBundleFromFolder` 已按实际 `ContentModDiscovery.Scan` + `ContentModLoader.Load` 签名落地（见 `ContentModTestHelper.cs`）。

**类型一致性**
- `CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)` 在 Task 4 定义，Task 5 `RunRuntime` 与 Task 6 `StorySelectDlg` 调用一致。
- `HostRng.RunSeed` 在 Task 4 引入，Task 4 测试直接断言。
- `RunUiIds.StorySelect`/`RunUiIds.RunMain` 在 Task 6 定义，Task 5 `RunMod` 注册与 Task 7 使用一致。
- `GlobalPersistentCondContext` 在 Task 3 定义，Task 6 使用。
- `GameDefinitionStore.TryGetStory` 在 Task 1 定义，Task 7 使用。
- `ContentModTestHelper.CreateBundleFromFolder` 在 Task 1 定义，Task 1/2 测试使用。
- 翻译键 `UI_RUN_SEED_INPUT_LABEL`（选故事）与 `UI_RUN_SEED_VALUE`（RunMain）在 Task 8 定义，Task 6/7 使用，命名无冲突。
