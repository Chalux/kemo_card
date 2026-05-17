# 内容 Mod 管理器设计规格

**日期**：2026-05-17  
**状态**：已定稿（对话评审合并）  
**范围**：磁盘内容包 Mod 的发现、启用、依赖排序、冲突合并、七大注册表刷新；与 C# 功能 Mod 分界；不含脚本宿主运行时与 Mod 设置 UI 具体实现。

---

## 1. 目标与非目标

### 1.1 目标

- 提供 **内容 Mod 管线**：游戏本体仅保留流程逻辑，卡牌/技能/Buff 等定义经 Mod 注册进权威表。
- **文件夹 Mod**：`user://mods/<folder>/`（根路径可配置），含 `mod.json` manifest + `content/` 数据 + 可选 `scripts/`。
- **玩家启用集**：扫描全部 Mod，由玩家在 **主菜单** 勾选；保存后 **立即重建** 注册表；**Run 中不可修改**。
- **依赖**：manifest 声明 `required` / `optional`；缺 `required` 的 Mod **无法启动**；勾选 Mod 时 UI **自动连带启用** 全部 `required`。
- **加载顺序**：依赖拓扑优先；`loadOrder` 仅用于同拓扑层内排序。
- **冲突**：同 **七大管理器分表** 下内容 id 重复时，**后注册条目跳过**并记日志；`modId` 全局重复则 **整包跳过**。
- **游戏内提示**：预留 `IContentModUserNotifier`，首版可为空实现。

### 1.2 非目标（YAGNI）

- 不新建独立 NuGet/类库包（如 `KemoCard.Core`）；全部落在 **`Src/frame/`**。
- Mod 压缩包、Workshop、Run 内热切换。
- 脚本宿主真实加载（仅保留目录约定）。
- 主菜单 Mod 列表面板的具体 UI（仅约定调用时机与 report 结构）。

### 1.3 与 C# 功能 Mod 的分工

| 层级 | 路径 | 职责 |
|------|------|------|
| **C# 功能 Mod** | `Src/mod/*`（如 `GlobalMod`） | UI、存档、MVC、业务流程组合根 |
| **内容 Mod** | 磁盘 `user://mods/` | 七大管理器定义 + 可选脚本/资源引用 |
| **框架** | `Src/frame/content/` | 发现、激活规划、加载、合并、注册表 |

---

## 2. 目录与代码布局

```
Src/frame/content/
  ContentCategory.cs
  ContentModManifestDto.cs
  ContentModDiscovery.cs          # IContentModDiscovery
  ContentModActivationPlanner.cs
  ContentModLoader.cs             # IContentModLoader
  ContentRegistryMerger.cs        # IContentRegistryMerger
  GameDefinitionRegistry.cs
  ModContentBundle.cs
  ContentModPipeline.cs
  ContentLoadReport.cs
  IContentModUserNotifier.cs
  IContentModLogger.cs
  NullContentModUserNotifier.cs

Tests/kemo_card.Ui.Tests/         # 或同级新测试文件
  ContentModActivationPlannerTests.cs
  ContentRegistryMergerTests.cs
  ...
```

命名空间建议：`KemoCard.Frame.Content`。

---

## 3. Mod 文件夹约定

```
user://mods/
  base-game/
    mod.json
    content/
      cards/*.json
      skills/*.json
      ...
    scripts/          # 可选，后续脚本宿主消费
```

### 3.1 `mod.json`（`ContentModManifestDto`）

```json
{
  "modId": "base.game",
  "displayName": "基础内容",
  "version": "1.0.0",
  "loadOrder": 0,
  "dependencies": {
    "required": [],
    "optional": []
  },
  "contentRoot": "content"
}
```

| 字段 | 说明 |
|------|------|
| `modId` | 全局唯一；扫描时发现重复 → **后发现的整个 Mod 包跳过** |
| `loadOrder` | 升序；仅在同拓扑层内生效 |
| `dependencies.required` | 必须在 **最终启用集** 中，否则本 Mod 跳过 |
| `dependencies.optional` | 缺失不阻止本 Mod 启动 |
| `contentRoot` | 相对 Mod 目录，默认 `content` |

---

## 4. 管线架构（方案 2：组合拆分）

`ContentModPipeline` 编排下列组件，**组合优先于继承**：

```
ContentModPipeline.Rebuild(enabledModIds)
  → Discovery.Scan(modRoot)           → 全部 manifest + 扫描错误
  → ActivationPlanner.Plan(...)         → OrderedActiveMods + SkippedMods
  → Loader.LoadEach(ordered)            → ModContentBundle[]
  → RegistryMerger.Merge(bundles)       → GameDefinitionRegistry + ContentLoadReport
  → IContentModUserNotifier.OnModLoadCompleted(report)
```

### 4.1 `ContentModDiscovery`

- 遍历 Mod 根目录下子文件夹。
- 读取 `mod.json`；解析失败 → `SkippedMods(InvalidManifest)`。
- 同一 `modId` 出现两次 → 后扫描的文件夹 **整包跳过**（`DuplicateModId`）。

### 4.2 `ContentModActivationPlanner`

**输入**：全部 manifest、玩家 `enabledModIds`（UI 已扩展 `required` 后的最终集）。

**步骤**：

1. 过滤：仅保留 `enabledModIds` 中的 Mod。
2. 对每个启用 Mod：若任一 `required` 不在启用集或磁盘不存在 → `SkippedMods(MissingRequiredDependency)`。
3. 对剩余 Mod 建图，`required` 边指向依赖方，**拓扑排序**；发现环 → 环内 Mod 全部 `SkippedMods(CyclicDependency)`。
4. 同层内按 `loadOrder` 升序，再按 `modId` 字典序稳定排序。

**输出**：`ContentModActivationResult`（`OrderedActiveMods`、`SkippedMods`、`ExpandedEnabledSet`）。

### 4.3 `ContentModLoader`

- 按 `OrderedActiveMods` 顺序读取各 Mod 的 `content/` 下 JSON（首版可按类别目录聚合 id）。
- 单 Mod 加载失败 → 该 Mod **整包跳过**（`LoadFailed`），不中断其他 Mod。

### 4.4 `ContentRegistryMerger` + `GameDefinitionRegistry`

- 按序将各 `ModContentBundle` 合并进七大表（`ContentCategory`：Character、Battle、Event、Card、Item、Skill、Buff）。
- **同表内容 id 冲突**：保留先注册者，跳过后来者，写入 `ContentLoadReport.IdConflicts`（含 `winnerModId`、`loserModId`、`category`、`contentId`）。
- **异表同名 id**：允许。
- 合并完成后 `GameDefinitionRegistry.DefinitionVersion++`。
- 对外提供 `Contains(category, id)` 等只读查询（与主规格非法语义对齐）。

---

## 5. 启用集、刷新时机与持久化

| 规则 | 说明 |
|------|------|
| 修改入口 | **仅主菜单** Mod 设置 |
| UI 连带 | 勾选 Mod 时递归并入 `required` 到启用集 |
| 保存后 | 写 `GlobalSaveDto.EnabledModIds`，立即 `ContentModPipeline.Rebuild()` |
| Run 中 | 不提供 Mod 开关；若误调用 `Rebuild`，拒绝并记日志 |
| 冷启动 | `MainRoot._Ready` 在 `GlobalMod.Bootstrap` 之后执行一次 `Rebuild()` |

`GlobalSaveDto` 增加字段：

```csharp
IReadOnlyList<string> EnabledModIds  // 默认含 "base.game" 等内置 id
```

`ContentVersionHash` 可与 `GameDefinitionRegistry.DefinitionVersion` 或内容哈希联动（实现期细节）。

---

## 6. 日志与游戏内提示

### 6.1 日志（`IContentModLogger`）

每条记录：`modId`、原因枚举、可选 `contentCategory` + `contentId`、冲突胜方 `winnerModId`。

实现可委托 Godot `GD.Print` / `GD.PushWarning`，单测注入 no-op。

### 6.2 预留接口

```csharp
public interface IContentModUserNotifier
{
    void OnModLoadCompleted(ContentLoadReport report);
}

public sealed class ContentLoadReport
{
    public IReadOnlyList<ModSkipEntry> SkippedMods { get; }
    public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }
    public bool HasIssues { get; }
}
```

首版：`NullContentModUserNotifier`。主菜单后续可注入实现，展示摘要列表。

---

## 7. 错误分级

| 级别 | 场景 | 策略 |
|------|------|------|
| Recoverable | 单 Mod 跳过、单 id 冲突 | 继续加载其余 Mod；report + 日志 |
| Fatal | Mod 根目录不可读，或合并后无有效基础内容 | 阻止开新 Run，主菜单强提示 |

与主规格 5.1 对齐：非法 id 在运行期由注册表查询失败处理（软失败/可恢复）。

---

## 8. 集成点

| 位置 | 行为 |
|------|------|
| `MainRoot._Ready` | 构造 `ContentModPipeline`，注入 Mod 根路径（`ProjectSettings.GlobalizePath("user://mods")`）、`IContentModUserNotifier` |
| `GlobalMod` / `GlobalSaveService` | 读写 `EnabledModIds`；**不**承担内容注册职责 |
| 主菜单（未来） | Mod 勾选 UI → 扩展 required → 存档 → `Rebuild()` |

---

## 9. 测试策略

放在 `Tests/kemo_card.Ui.Tests/`（纯 CLR，不实例化 Godot 控件），覆盖：

| 用例 | 断言 |
|------|------|
| 拓扑 + `loadOrder` 同层 | `OrderedActiveMods` 顺序正确 |
| 缺 `required` | Mod ∈ `SkippedMods` |
| 环依赖 | 环内 Mod 均跳过 |
| 同表 id 冲突 | 先加载者保留，`IdConflicts` 含后者 |
| 异表同名 | 不冲突 |
| 重复 `modId` 目录 | 后扫描整包跳过 |

使用临时目录 + 最小 `mod.json` / 占位 JSON 做 Discovery + Planner + Merger 集成测。

---

## 10. 明确不做

- 独立 `KemoCard.Core` 程序集。
- Mod 压缩包与 Run 内热切换。
- 脚本宿主运行时。
- 可选依赖的内容占位替换逻辑。

---

## 11. 自检记录

- **占位扫描**：无 TBD/TODO 强制项。
- **一致性**：与 `2026-05-11-kemo-card-design.md` 七大管理器、启动/启用时刷新、分表非法语义一致；代码统一在 `Src/frame/content/`。
- **范围**：单实现计划可覆盖管线 + 最小注册表 + 存档字段 + 空 notifier；主菜单 UI 为后续里程碑。
- **歧义消除**：注册顺序 = 激活规划器输出顺序；内容冲突不跳过整个 Mod（除非加载失败）。
