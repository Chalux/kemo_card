# 内容与数据规格

（原「内容 Mod 管道」规格；2026-09-21 并入两份内容 DTO 与角色/战斗实例规格）

**日期**：2026-05-17  
**最后修订**：2026-09-21（内容与数据域下级规格合并）  
**状态**：已定稿（对话评审合并；2026-07-30 单轨合并修订；2026-09-21 内容与数据域下级规格合并）  
**关系**：服从 [总规格](2026-05-11-kemo-card-design.md)。本文是**内容与数据域**的唯一权威文档 —— **内容 Mod 管道 + 内容定义 DTO + 角色/战斗实例**。战斗运行时（阶段机 / 伤害 / GAS 结算）归 [战斗规格](2026-07-21-combat-system-design.md)；Buff 运行时与连携归 [战斗规格](2026-07-21-combat-system-design.md) §13，团体潜能归 [Run 规格](2026-06-22-run-mod-design.md) §13；条件域与 CondType 归 [UI 与运行时规格](2026-05-15-ui-manager-design.md) §16；Mod 脚本宿主归 [jsenv-mod-scripting](2026-05-15-ui-manager-design.md)；普通攻击运行时归 [普通攻击规格](2026-07-21-combat-system-design.md)。  
**范围**：① 磁盘内容包 Mod 的发现、启用、依赖排序、冲突合并、定义注册表刷新（原「内容 Mod 管道」职责）；② 卡牌 / 技能 / Buff / 效果 / 角色 / 敌人 / 战斗 / 事件 / 道具 / 故事的内容定义 DTO、枚举、加载与校验；③ `DeckPreset` / `CharacterInstance` / `HandSlot` / `CharacterBattleInstance` 及直接依赖类型；④ 内容域的收敛事实（种族 / 稀有度档名 / 字段移除）与卡组属性预算。与 C# 功能 Mod 分界；不含脚本宿主细节与 Mod 设置 UI 具体实现。  
**非范围**：Mod 压缩包 / Workshop / Run 内热切换；主菜单 Mod 列表面板的具体 UI；Buff 结算管线（`BuffInstance`）与局内存档（`RunSaveDto` / `RunSaveService` / `ToSaveDto` / `FromSaveDto`）；队伍共用 HP（`TeamBattleState`）与战斗阶段机、`CombatTurnController`；战斗数值公式与伤害包管线；内容包 UI（角色详细界面 / 横向卡牌列表等展示形态）。

**本文承载的下级规格（2026-09-21 合并并归档）**：

| 原规格文件（`Doc/superpowers/specs/`） | 归档路径 | 并入本文 |
|---|---|---|
| `2026-06-16-content-definition-dto-design.md`（卡牌 / 技能 / Buff / 效果 内容 DTO 设计规格） | `Doc/archive/superpowers/specs/2026-06-16-content-definition-dto-design.md` | 全文（原 §1–§7）→ 本文 §12 |
| `2026-06-16-character-battle-event-item-dto-design.md`（角色 / 敌人 / 战斗 / 事件 / 道具 内容 DTO 设计规格） | `Doc/archive/superpowers/specs/2026-06-16-character-battle-event-item-dto-design.md` | 全文（原 §1–§9）→ 本文 §13 |
| `2026-06-18-character-instance-design.md`（角色实例 / 战斗实例 / 卡组预设 / 手牌槽 设计规格） | `Doc/archive/superpowers/specs/2026-06-18-character-instance-design.md` | 全文（原 §1–§4）→ 本文 §14 |
| `2026-09-21-reinhardt-and-normal-attack-extensions.md`（莱因哈特套件与战斗机制扩展） | **不归档**（该规格仍存活：其余章节归战斗规格与 Global 功能规格维护） | 仅原 §2 种族收敛 / §3 常驻天赋移除 / §9 卡组属性预算 / §12.1 稀有度档名收敛 / §12.2 的 `CharacterDto.descId` 删除 → 本文 §15 |

**段号映射（原规格 §N → 本文 §M.K）**：`Src/` 与 `Doc/` 里的注释按原段号引用规格（如 `ContentDefinitionValidator`、各 `*Dto.cs` 引「内容 DTO 规格」），下表用于继续定位。

| 原规格 | 原段号 | 本文段号 | 主题 |
|---|---|---|---|
| 卡牌 / 技能 / Buff / 效果 内容 DTO 规格 | §1 | §12.1 | 引用链与时机分层 |
| 同上 | §2 | §12.2 | DTO 类型约束 |
| 同上 | §3 | §12.3 | CardDto 字段 |
| 同上 | §4 | §12.4 | 枚举 |
| 同上 | §5 | §12.5 | Mod 目录 |
| 同上 | §6 | §12.6 | 脚本 |
| 同上 | §7 | §12.7 | 自检 |
| 角色 / 敌人 / 战斗 / 事件 / 道具 内容 DTO 规格 | §1 | §13.1 | 已确认边界 |
| 同上 | §2 | §13.2 | 引用链 |
| 同上 | §3 | §13.3 | Mod 目录与加载 |
| 同上 | §4 | §13.4 | 枚举（含 §13.4.1–§13.4.3） |
| 同上 | §5 | §13.5 | DTO 字段（含 §13.5.1–§13.5.6） |
| 同上 | §6 | §13.6 | 校验规则 |
| 同上 | §7 | §13.7 | 示例内容 |
| 同上 | §8 | §13.8 | 开放项 |
| 同上 | §9 | §13.9 | 自检 |
| 角色实例设计规格 | §1 | §14.1 | 首期实现边界 |
| 同上 | §2 | §14.2 | 已确认玩法规则 |
| 同上 | §3 | §14.3 | 类型职责 |
| 同上 | §4 | §14.4 | 自检 |
| 莱因哈特套件与战斗机制扩展 | §2 | §15.1 | 种族收敛 |
| 同上 | §3 | §15.2 | 常驻天赋移除（`CharacterDto.buffRefs`） |
| 同上 | §9 | §15.3 | 卡组属性预算 |
| 同上 | §12.1 | §15.4 | 稀有度档名收敛 |
| 同上 | §12.2（DTO 部分） | §15.5 | 角色简介字段移除（`CharacterDto.descId`） |

**修订记录**：
- **2026-07-30**：合并「id HashSet 表 + DTO Store」双轨为单轨；`ModContentBundle` 仅持 `Definitions`；Merger 写入 Store 并返回 `ContentRegistryMergeResult`。实现计划见归档 [content-mod-single-track-merge](../../archive/superpowers/plans/2026-07-30-content-mod-single-track-merge.md)。
- **2026-07-31**：新增 **Story** 内容类别与 `StoryDto`（作者、解锁条件、仅单人等字段）；`unlock` 走 Persistent 域条件解析校验，运行期在选故事 UI 求值。
- **2026-09-21**：并入三份下级规格（两份内容 DTO 规格 + 角色实例规格，见上表）为本文 §12–§14；并入「莱因哈特套件与战斗机制扩展」的**内容域**章节为本文 §15（种族收敛、`CharacterDto.buffRefs` 移除、卡组属性预算、`ERarity` 收敛、`CharacterDto.descId` 移除）。原规格段号经上表映射，仍可定位。

---

## 1. 目标与非目标

### 1.1 目标

- 提供 **内容 Mod 管线**：游戏本体仅保留流程逻辑，卡牌/技能/Buff 等定义经 Mod 注册进权威表。
- **文件夹 Mod**：`user://mods/<folder>/`（根路径可配置），含 `mod.json` manifest + `content/` 数据 + 可选 `scripts/`。
- **玩家启用集**：扫描全部 Mod，由玩家在 **主菜单** 勾选；保存后 **立即重建** 注册表；**Run 中不可修改**。
- **依赖**：manifest 声明 `required` / `optional`；缺 `required` 的 Mod **无法启动**；勾选 Mod 时 UI **自动连带启用** 全部 `required`。
- **加载顺序**：依赖拓扑优先；`loadOrder` 仅用于同拓扑层内排序。
- **冲突**：同 **内容类别分表** 下内容 id 重复时，**后注册条目跳过**并记日志；`modId` 全局重复则 **整包跳过**。
- **游戏内提示**：预留 `IContentModUserNotifier`，首版可为空实现。

### 1.2 非目标（YAGNI）

- 不新建独立 NuGet/类库包（如 `KemoCard.Core`）；全部落在 **`Src/frame/`**。
- Mod 压缩包、Workshop、Run 内热切换。
- 主菜单 Mod 列表面板的具体 UI（仅约定调用时机与 report 结构）。

### 1.3 与 C# 功能 Mod 的分工

| 层级 | 路径 | 职责 |
|------|------|------|
| **C# 功能 Mod** | `Src/mod/*`（如 `GlobalMod`） | UI、存档、MVC、业务流程组合根 |
| **内容 Mod** | 磁盘 `user://mods/` | 各内容类别定义 + 可选脚本/资源引用 |
| **框架** | `Src/frame/content/` | 发现、激活规划、加载、合并、注册表 |

---

## 2. 目录与代码布局

```
Src/frame/content/
  ContentCategory.cs / ContentCategoryPaths.cs
  ContentModManifestDto.cs
  ContentModDiscovery.cs
  ContentModActivationPlanner.cs
  ContentModLoader.cs
  ContentRegistryMerger.cs
  ContentRegistryMergeResult.cs
  GameDefinitionRegistry.cs      # 版本、owner、Contains 门面
  GameDefinitionStore.cs         # 定义权威（DTO 字典）
  ModContentBundle.cs            # (ModId, Definitions)
  ModDefinitionsBundle.cs
  ContentModPipeline.cs
  ContentLoadReport.cs
  ContentDefinitionValidator.cs
  IContentModUserNotifier.cs / Null*
  IContentModLogger.cs / Null* / Godot*
  IContentModTranslationLoader.cs / Null*
  definitions/                   # 各内容 DTO
  keywords/                      # 词条提示（展示侧）

Tests/kemo_card.Ui.Tests/
  ContentMod*Tests.cs
  ContentRegistryMergerTests.cs
  GameDefinitionRegistry*Tests.cs
  ...
```

命名空间：`KemoCard.Frame.Content`。

---

## 3. Mod 文件夹约定

```
user://mods/
  base-game/
    mod.json
    content/
      cards/*.json
      skills/*.json
      stories/*.json
      ...
    scripts/          # 可选，脚本宿主消费
```

类别文件夹名由 `ContentCategoryPaths.Folder(category)` 约定（如 `tags` → `GameplayTag`、`stories` → `Story`）。

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

### 3.2 故事（Story）类别

目录：`content/stories/*.json`；`EContentCategory.Story` → `ContentCategoryPaths.Folder` = `"stories"`。

`StoryDto` 字段：

| 字段 | JSON | 说明 |
|------|------|------|
| `Id` | `id`（注入） | 文件名推导；固化进 Run（`RunMod.StoryId`） |
| `DisplayNameId` | `displayNameId` | 故事名翻译键 |
| `DescId` | `descId` | 描述翻译键 |
| `Author` | `author` | 作者署名，**明文**（非翻译键） |
| `Unlock` | `unlock?` | 可选解锁条件（Persistent 域内联表达式，见[条件规格](2026-05-15-ui-manager-design.md) §8） |
| `ScriptPath` | `scriptPath?` | 故事脚本入口（可空，宿主保底后补） |
| `ScriptEntry` | `scriptEntry?` | 脚本函数名（可空） |
| `SinglePlayerOnly` | `singlePlayerOnly` | 缺省 `true`；仅允许单人游玩 |

- **所属 Mod**：由 `GameDefinitionRegistry.TryGetOwnerModId(Story, id)` 读取，不写入 DTO。
- **校验**：`unlock` 存在且非 JSON null 时，用 `ConditionParser.TryParse<IPersistentCondContext>` 在 Persistent 域解析；失败 → `ContentDefinitionValidationError(Category=Story)`，错误带 `content/stories/<id>.json:unlock` 来源路径，该定义从 Store / owner 映射移除。
- **运行期求值**：选故事 UI 用 `ConditionEvaluator` + `GlobalPersistentCondContext`（`HasFlag` → 全局存档 `Unlocks`）判定可玩性。
- **样例**：

```json
{
  "displayNameId": "story.kemo_first.name",
  "descId": "story.kemo_first.desc",
  "author": "KemoCard Team",
  "singlePlayerOnly": true
}
```

```json
{
  "displayNameId": "story.kemo_second.name",
  "descId": "story.kemo_second.desc",
  "author": "KemoCard Team",
  "unlock": { "HasFlag": ["story.kemo_first.clear"] },
  "singlePlayerOnly": true
}
```

---

## 4. 管线架构（组合拆分）

`ContentModPipeline` 编排下列组件，**组合优先于继承**：

```
ContentModPipeline.Rebuild(enabledModIds)
  → Discovery.Scan(modRoot)           → 全部 manifest + 扫描错误
  → ActivationPlanner.Plan(...)         → OrderedActiveMods + SkippedMods
  → Loader.LoadEach(ordered)            → ModContentBundle[]（仅 Definitions）
  → Registry.Rebuild(bundles)
       → Merger.Merge(bundles, Store)   → ContentRegistryMergeResult（冲突 + owner）
       → Validator.Validate(Store)      → 剔除非法定义
  → 脚本 catalog / runtime reset / 可选预热
  → IContentModUserNotifier.OnModLoadCompleted(ContentLoadReport)
```

### 4.1 `ContentModDiscovery`

- 遍历 Mod 根目录下子文件夹。
- 读取 `mod.json`；解析失败 → `SkippedMods(InvalidManifest)`。
- 同一 `modId` 出现两次 → 后扫描的文件夹 **整包跳过**（`DuplicateModId`）。

### 4.2 `ContentModActivationPlanner`

**输入**：全部 manifest、玩家 `enabledModIds`（UI 已扩展 `required` 后的最终集）。

**步骤**：

1. 过滤：仅保留 `enabledModIds` 中的 Mod（含 `ContentModRequiredExpander` 连带）。
2. 对每个启用 Mod：若任一 `required` 不在启用集或磁盘不存在 → `SkippedMods(MissingRequiredDependency)`。
3. 对剩余 Mod 建图，`required` 边指向依赖方，**拓扑排序**；发现环 → 环内 Mod 全部 `SkippedMods(CyclicDependency)`。
4. 同层内按 `loadOrder` 升序，再按 `modId` 字典序稳定排序。

**输出**：`ContentModActivationResult`（`OrderedActiveMods`、`SkippedMods`、`ExpandedEnabledSet`）。

### 4.3 `ContentModLoader`

- 按 `OrderedActiveMods` 顺序读取各 Mod 的 `content/<folder>/*.json`，反序列化为对应 DTO，组装 `ModDefinitionsBundle`（含 `StoryDto`）。
- 内容 id 由文件名（无扩展名）推导，并写入 DTO 的 `id`。
- 单 Mod 加载失败 → 该 Mod **整包跳过**（`LoadFailed`），不中断其他 Mod。

### 4.4 `ContentRegistryMerger` + `GameDefinitionStore` + `GameDefinitionRegistry`

**单轨权威**：`GameDefinitionStore` 持有各类 DTO 字典；不再维护平行的 id-only `HashSet` 表。

- `ModContentBundle` = `(ModId, ModDefinitionsBundle)`。
- `ContentRegistryMerger.Merge(bundles, store)`：按序将 DTO **TryAdd** 进 Store；同表 id 冲突时保留先注册者，写入 `ContentRegistryMergeResult.IdConflicts`，并记录 `OwnerModIds`。
- **异表同名 id**：允许。
- `GameDefinitionRegistry`：持有 Store、owner 映射、`DefinitionVersion`；`Contains(category, id)` 委托 Store；校验失败的定义从 Store 与 owner 中移除。
- 管线最终汇总为 `ContentLoadReport`（跳过、冲突、校验剔除、脚本错误）。

---

## 5. 启用集、刷新时机与持久化

| 规则 | 说明 |
|------|------|
| 修改入口 | **仅主菜单** Mod 设置 |
| UI 连带 | 勾选 Mod 时递归并入 `required` 到启用集 |
| 保存后 | 写 `GlobalSaveDto.EnabledModIds`，立即 `ContentModPipeline.Rebuild()` |
| Run 中 | 不提供 Mod 开关；若误调用 `Rebuild`，拒绝并记日志 |
| 冷启动 | Bootstrap 之后执行一次 `Rebuild()` |

**随包 Mod 的暂存（`ContentModBootstrap.EnsureDefaultModsCopied`）**：

- 启动时把 `res://Config/mods/*` 拷贝到 `user://content_mods/*`（经 `.staging` 目录原子替换，不留残目录），游戏实际加载的是用户目录那份。
- **版本号相同即跳过**（发布语义：不必每次启动都重拷）。因此**改动随包内容必须抬 `mod.json` 的 `version`**，否则游戏里看不到——2026-09-20 就踩过一次（新增角色 chalux / 充能球 / 四张专属卡全部未生效）。
- **调试构建强制刷新**：`MainRoot` 用 `OS.IsDebugBuild()` 填充 `ModStartupContext.ForceContentModRefresh`，为 true 时忽略版本号每次启动重新拷贝，开发期改内容无需抬版本号。该开关由调用方注入，`ContentModBootstrap` 本身不读引擎标记（保持逻辑层可被 NUnit 直接覆盖）。

`GlobalSaveDto` 字段：

```csharp
IReadOnlyList<string> EnabledModIds  // 默认含 "base.game" 等内置 id
```

`ContentVersionHash` 可与 `GameDefinitionRegistry.DefinitionVersion` 或内容哈希联动（实现期细节）。

---

## 6. 日志与游戏内提示

### 6.1 日志（`IContentModLogger`）

每条记录：`modId`、原因枚举、可选 `contentCategory` + `contentId`、冲突胜方 `winnerModId`。

实现可委托统一 `AppLog` / Godot 输出，单测注入 no-op。

### 6.2 预留接口

```csharp
public interface IContentModUserNotifier
{
    void OnModLoadCompleted(ContentLoadReport report);
}
```

`ContentLoadReport` 含：`SkippedMods`、`IdConflicts`、`ValidationErrors` / `RemovedValidationErrors`、`ScriptLoadErrors`、`HasIssues`。

首版：`NullContentModUserNotifier`。主菜单后续可注入实现，展示摘要列表。

---

## 7. 错误分级

| 级别 | 场景 | 策略 |
|------|------|------|
| Recoverable | 单 Mod 跳过、单 id 冲突、定义校验剔除 | 继续加载其余；report + 日志 |
| Fatal | Mod 根目录不可读，或合并后无有效基础内容 | 阻止开新 Run，主菜单强提示 |

与主规格对齐：非法 id 在运行期由注册表查询失败处理（软失败/可恢复）。

---

## 8. 集成点

| 位置 | 行为 |
|------|------|
| `ModFactory` / 启动链 | 构造 `ContentModPipeline`，注入 Mod 根路径、logger、notifier、翻译加载器、脚本 resetter |
| 全局存档 | 读写 `EnabledModIds`；**不**承担内容注册职责 |
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
| Store / Contains | 合并后可查 DTO；校验剔除后 `Contains` 为 false |

使用临时目录 + 最小 `mod.json` / JSON 做 Discovery + Planner + Merger 集成测。

---

## 10. 明确不做

- 独立 `KemoCard.Core` 程序集。
- Mod 压缩包与 Run 内热切换。
- 可选依赖的内容占位替换逻辑。
- 为尚未到来的「新内容类别」过度抽象 13 路样板（加类别时再收拢）。

---

## 11. 自检记录

- **占位扫描**：无 TBD/TODO 强制项。
- **一致性**：与总规格宿主/Mod 分界、启动/启用时刷新、分表非法语义一致；代码统一在 `Src/frame/content/`。
- **单轨**：定义权威仅 `GameDefinitionStore`；冲突与 owner 在合 DTO 时产出。
- **歧义消除**：注册顺序 = 激活规划器输出顺序；内容冲突不跳过整个 Mod（除非加载失败）。

---

## 12. 卡牌 / 技能 / Buff / 效果 DTO（原 2026-06-16 内容 DTO 规格 §1–§7）

> **来源**：`2026-06-16-content-definition-dto-design.md`（**日期** 2026-06-16；**最后同步** 2026-06-17，与代码 `Src/frame/content/definitions/` 对齐；**状态** 已实现；**范围** Card / Skill / Buff / Effect 四类 Mod JSON DTO、枚举、加载与校验）。已归档至 `Doc/archive/superpowers/specs/2026-06-16-content-definition-dto-design.md`。目录与代码布局见本文 §2；其余类别 DTO 见本文 §13。

### 12.1 引用链与时机分层（原 §1）

- 出牌 → 卡牌按 `skillRefs` 顺序触发技能
- 技能一旦被调用，`effectRefs` 同步即时执行完毕
- 延时 / 跨回合效果：`ApplyBuff` + Buff `Hooks`
- Buff 仅由效果或宿主白名单 API 添加

| 层级 | 是否延时 | 说明 |
|------|----------|------|
| 卡牌执行队列 | 可延时 | 按 `priority` 排队，轮到才调用技能 |
| 技能 | 始终即时 | 调用瞬间跑完 `effectRefs` |
| Buff | 可延时 | 钩子驱动持续与延时效果 |

Skill DTO **不含** `ESkillTrigger` / `IsInstant`。

---

### 12.2 DTO 类型约束（原 §2）

- 可 JSON 往返：`string`, `int`, `bool`, `double`, 枚举, `List<T>`, `Dictionary<string, object>?`（仅 params）
- `tags` 使用 `List<string>`
- 形态：`sealed class` + `JsonPropertyName` + `init`

---

### 12.3 CardDto 字段（原 §3）

见 `CardDto.cs`。无 `descId`；描述由 UI 组合 `skillRefs` 对应技能的 `descId`。升级链：`cardGroupId` + `upgradeTier`。

| 字段 | 类型 | 说明 |
|------|------|------|
| `element` | int | 位标志（`ElementFlags`）；尚未迁移为 `EElement` |
| `role` | ERole | 与 Character / Enemy 共用枚举 |
| `costType` | ECostType | |
| `cost` | int | |
| `skillRefs` | SkillRefDto[] | |
| `cardType` | ECardType | |
| `targetSide` / `targetScope` / `targetCount` / `retargetPolicy` | | 目标规格 |
| `rarity` | ERarity | |
| `cardGroupId` | string? | 升级链分组 |
| `upgradeTier` | int | |
| `playConditions` | ConditionRefDto[] | |
| `costScaling` | CostScalingDto? | |
| `hideInDex` / `isExclusive` / `priority` | | |
| `animationId` / `sfxId` | string? | |
| `artPath` / `tags` | | |

---

### 12.4 枚举（原 §4）

`ECostType`, `ECardType`, `ECostScalingKind`, `ETargetSide`, `ETargetScope`, `ERetargetPolicy`, `ERarity`, `EBuffDurationType`, `EBuffStackRule`, `EEffectKind`, `ERole`。

`EElement` / `ERace` / `EEventKind` / `ERewardKind` 定义于同一文件，供 Character / Battle / Event 等 DTO 使用（见本文 §13.4）。

> **2026-09-23 新增成员**（冯·诺依曼套件）：`EEffectKind` 与 `ESkillActionKind` 各加三个——`SetActionCount`（敌人行动计数，`params.count` 缺省 2）、`SetDomain`（展开队伍领域，`params.gameplayEffectId` + 可选 `turns`）、`DiscardSlot`（弃置指定手牌槽，`params.slotIndex`）。前者与 `Damage` 一样两条通道（效果 / 技能动作）同源，`EffectDto` 与 `SkillActionDto` 都可用。

> 2026-09-21 合并：本节的 `ERace` / `ERarity` 成员清单以本文 §13.4.1 为准（`ERace` 收敛见本文 §15.1，`ERarity` 档名收敛见本文 §15.4）。

---

### 12.5 Mod 目录（原 §5）

```
content/cards/*.json
content/skills/*.json
content/buffs/*.json
content/effects/*.json
```

`id` = 文件名（无扩展名）；加载时注入 DTO `Id`。

---

### 12.6 脚本（原 §6）

- 主通道：TypeScript（`ExecuteScript` + `ScriptPath`）
- 进阶：HybridCLR（信任 Mod）
- 接口桩：`IContentEffectScriptHost`

---

### 12.7 自检（原 §7）

- 无 TBD；与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界一致
- 非法引用：加载合并后校验，失败项不进入 `GameDefinitionStore`

---

## 13. 角色 / 敌人 / 战斗 / 事件 / 道具 DTO（原 2026-06-16 角色·战斗·事件·道具 DTO 规格 §1–§9）

> **来源**：`2026-06-16-character-battle-event-item-dto-design.md`（**日期** 2026-06-16；**最后同步** 2026-06-17，与代码 `Src/frame/content/definitions/` 对齐；**状态** 已实现；**范围** Character / Enemy / Battle / Event / Item 五类 Mod JSON DTO、枚举、加载与校验）。已归档至 `Doc/archive/superpowers/specs/2026-06-16-character-battle-event-item-dto-design.md`。类型约束见本文 §12.2；管道与校验接线见本文 §4.3 / §4.4。

### 13.1 已确认边界（原 §1）

| 决策点 | 结论 |
|--------|------|
| CharacterDto | 仅可操控友方角色（4 人小队） |
| 敌人 | 独立 `EnemyDto`（`content/enemies/*.json`），`BattleDto` 引用 |
| ItemDto | 仅消耗品（使用后消失） |
| EventDto | 混合：`eventKind` 区分数据驱动 vs 脚本驱动 |
| Battle | 多波次 + 可选 `scriptPath` + 战后奖励 |

---

### 13.2 引用链（原 §2）

- Character：`cards` → Card；`skillRefs` → Skill；`activeSkillChain[].skillId` → Skill；`passives[].buffId` → Buff
- Enemy：`skillRefs` → Skill；`buffRefs` → Buff
- Battle：`waves[].enemySpawns[].enemyId` → Enemy；`enemySpawns[].skillOverrides` → Skill；奖励 `ItemGrant`/`Effect`/`CardChoice` 引用 Item/Effect/Card
- Event（Data）：`options[].effectRefs` → Effect
- Item：`useSkillRefs` → Skill

---

### 13.3 Mod 目录与加载（原 §3）

```
content/characters/*.json
content/enemies/*.json
content/battles/*.json
content/events/*.json
content/items/*.json
```

- `id` = 文件名（无扩展名）；`ContentModLoader.LoadDefinitions<T>()` 加载时注入 DTO `Id`
- `EContentCategory` 新增 `Enemy`，与 Character / Battle / Event / Item 并列
- 合并后由 `GameDefinitionRegistry.Rebuild()` 调用 `ContentDefinitionValidator`，非法定义从 `GameDefinitionStore` 剔除

---

### 13.4 枚举（`ContentEnums.cs`）（原 §4）

#### 13.4.1 本规格直接使用（原 §4「本规格直接使用」）

| 枚举 | 说明 |
|------|------|
| `EElement` | `[Flags]`：None, Fire, Water, Wind, Earth, Dark, Light |
| `ERole` | None, Warrior, Wizard, Healer, Guard, Shield, Controller, Support, CardPlayer, SwordMan, Mage, Alchemist |
| `ERace` | `[Flags]`：None, Human, Animal（动物：原犬/猫/鸟/兽/爬行合并）, Insect, Fish, Plant, Machine, Demon（恶魔族：原 Demonic + Devil）, Angel, Dragon, God, Undead, Academic（学术）, Fantasy（幻想）, Astronomy（天文）, Hero（英雄）, Calamity（灾厄）, Unknown（未知，2026-09-21 由 UnKnown 更名） |
| `EEventKind` | Data, Script |
| `ERewardKind` | Gold, CardChoice, ItemGrant, Effect |
| `ERarity` | ItemDto 稀有度：Common, Special（原 Uncommon）, Rare, Exclusive（原 Epic）, Legendary |

> 2026-09-21 合并：原表述（`ERarity`：Common, Uncommon, Rare, Epic, Legendary）已收敛为（`Common` / `Special`（原 Uncommon）/ `Rare` / `Exclusive`（原 Epic）/ `Legendary`）；详见本文 §15.4。
>
> 2026-09-21 合并：原表述（`ERace` 旧成员清单含 Canine / Feline / Bird / Beast / Reptile 与 Demonic / Devil、并由 `UnKnown` 拼写）已收敛为（5 族并 1 → `Animal`、2 族并 1 → `Demon`、新增 `Academic` / `Fantasy` / `Astronomy` / `Hero` / `Calamity`、`UnKnown` → `Unknown`，即上表清单）；详见本文 §15.1。

#### 13.4.2 共用（定义于同一文件，被嵌套 DTO 引用）（原 §4「共用」）

`ETargetSide`, `ETargetScope`, `ERetargetPolicy`（ItemDto `targetSpec` 与 CardDto 目标字段共用）。

#### 13.4.3 与 CardDto 的差异（原 §4「与 CardDto 的差异」）

- Character / Enemy 的 `element`、`role` 已使用 `EElement` / `ERole`
- CardDto 的 `element` 仍为 `int`（位标志，见 `ElementFlags`）；`role` 已迁移为 `ERole`

---

### 13.5 DTO 字段（原 §5）

#### 13.5.1 CharacterDto（`CharacterDto.cs`）（原 §5 CharacterDto）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | 加载时注入 |
| `displayNameId` | string | 本地化键 |
| `element` | EElement | 元素 |
| `role` | ERole | 职业/定位 |
| `race` | ERace | 种族（可组合 Flags） |
| `skillRefs` | SkillRefDto[] | 角色技能池 |
| `activeSkillChain` | ActiveSkillChainEntryDto[] | 主动技蓄力链（战斗主动按钮只读本字段） |
| `passives` | PassiveRefDto[] | 潜能门闩被动：解锁后开战挂对应 buff（**角色唯一的"常驻增益"通道**，2026-09-21 起取代已移除的 `buffRefs`） |
| `cards` | string[] | 角色专属构筑池（非 Run 初始牌库） |
| `maxEnergy` | int | 能量上限 |
| `initialEnergy` | int | 初始能量 |
| `artPath` | string | 立绘路径 |
| `tags` | string[] | 标签 |

> 2026-09-21 合并：原表述（CharacterDto 含 `descId` 角色简介翻译键字段）已收敛为（`CharacterDto.descId` 删除；详见本文 §15.5）。
>
> 2026-09-21 合并：原表述（CharacterDto 含 `buffRefs` 常驻天赋增益引用字段）已收敛为（`CharacterDto.buffRefs` 删除，角色常驻增益唯一通道 = `passives`；详见本文 §15.2）。敌人侧 `EnemyDto.buffRefs` **保留**。

#### 13.5.2 EnemyDto（`EnemyDto.cs`）（原 §5 EnemyDto）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | 加载时注入 |
| `displayNameId` | string | |
| `descId` | string | |
| `maxHp` | int | 必须 > 0 |
| `element` | EElement | |
| `role` | ERole | |
| `race` | ERace | 种族（可组合 Flags，**2026-09-23 新增**，与 `CharacterDto.race` 同口径）。此前敌人只有属性/职业，无法被「蓝属性·人类·学术」这类跨属性与种族的筛选命中；未声明时为 `ERace.None`。运行时 `EnemyUnit.Element` / `EnemyUnit.Race` 同时作为 buff 持有者条件（`condition.elementAny` / `raceAny`）的 provider |
| `skillRefs` | SkillRefDto[] | AI 技能池（运行时规范见开放项） |
| `buffRefs` | BuffRefDto[] | |
| `artPath` | string | |
| `scriptPath` | string? | 可选自定义 AI 脚本 |
| `tags` | string[] | |

#### 13.5.3 BattleDto（`BattleDto.cs`）（原 §5 BattleDto）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `waves` | BattleWaveDto[] | 至少一波 |
| `scriptPath` | string? | 可选战斗脚本 |
| `rewards` | BattleRewardDto | 战后奖励 |
| `backgroundPath` | string? | |
| `musicId` | string? | |
| `tags` | string[] | |

**嵌套类型**（`SharedDefinitionDtos.cs`）：

- `BattleWaveDto`：`enemySpawns`（非空）、`waveScriptPath?`
- `EnemySpawnDto`：`enemyId`、`count`（默认 1）、`hpScale?`、`skillOverrides?`
- `BattleRewardDto`：`entries`
- `RewardEntryDto`：`kind`、`weight`（默认 1）、`params?`

**奖励 `params` 约定**（首版 `Dictionary<string, object>`，加载期部分校验）：

| kind | params 键 | 校验 |
|------|-----------|------|
| Gold | `min`, `max` | 无引用校验 |
| CardChoice | `count`, `pool`；可选 `cardIds` | 若提供 `cardIds`，逐项校验 Card 存在 |
| ItemGrant | `itemId` | 必填，校验 Item 存在 |
| Effect | `effectId` | 必填，校验 Effect 存在 |

#### 13.5.4 EventDto（`EventDto.cs`）（原 §5 EventDto）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `eventKind` | EEventKind | Data 或 Script |
| `artPath` | string | |
| `pages` | EventPageDto[] | 叙事页 |
| `options` | EventOptionDto[] | 选项（Data 事件必填） |
| `scriptPath` | string? | Script 事件必填 |
| `tags` | string[] | |

**嵌套类型**：

- `EventPageDto`：`textId`、`imagePath?`
- `EventOptionDto`：`optionId`、`labelId`、`descId?`、`conditions`、`effectRefs`、`nextPageIndex?`

**eventKind 约束**：

- `Data`：`options` 至少一项；各 `effectRefs` 校验 Effect 存在
- `Script`：`scriptPath` 非空

#### 13.5.5 ItemDto（`ItemDto.cs`）（原 §5 ItemDto）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `rarity` | ERarity | |
| `useSkillRefs` | SkillRefDto[] | 非空；使用后触发技能链 |
| `targetSpec` | TargetSpecDto? | 目标规格 |
| `maxStack` | int | 默认 1 |
| `artPath` | string | |
| `shopPrice` | int? | 商店价格 |
| `tags` | string[] | |

#### 13.5.6 共用嵌套 DTO（`SharedDefinitionDtos.cs`）（原 §5 共用嵌套 DTO）

| 类型 | 字段 |
|------|------|
| `SkillRefDto` | `skillId`, `params?` |
| `BuffRefDto` | `buffId`, `params?` |
| `EffectRefDto` | `effectId`, `params?` |
| `TargetSpecDto` | `side`, `scope`, `targetCount`（默认 1）, `retargetPolicy` |
| `ConditionRefDto` | `kind`, `params?`（Event 选项条件） |

---

### 13.6 校验规则（`ContentDefinitionValidator`）（原 §6）

加载合并后执行；失败项从 `GameDefinitionStore` 与 `_tables` 同时移除，错误写入 `ContentLoadReport.ValidationErrors`。

| 类别 | 规则 |
|------|------|
| Character | `skillRefs` / `cards` / `passives[].buffId` 引用存在；`requiredPotential` 非负 |
| Enemy | `maxHp > 0`；`skillRefs` / `buffRefs` 引用存在 |
| Battle | `waves` 非空；每波 `enemySpawns` 非空；`enemyId` 存在；`skillOverrides` 技能存在；奖励按 kind 校验 |
| Event | Data：`options` 非空 + effect 引用；Script：`scriptPath` 必填 |
| Item | `useSkillRefs` 非空；技能引用存在 |

---

### 13.7 示例内容（`Config/mods/base-game/content/`）（原 §7）

| 文件 | 说明 |
|------|------|
| `characters/kemo.json` | 角色 + 卡牌池 + 技能/Buff |
| `enemies/slime.json`, `slime_elite.json` | 敌人定义 |
| `battles/forest_ambush.json` | 两波战斗 + Gold/CardChoice 奖励 |
| `events/shrine.json` | Data 事件 + 选项效果 |
| `items/health_potion.json` | 消耗品 + targetSpec |

---

### 13.8 开放项（原 §8）

- `EElement` 与 CardDto `int element` 统一迁移（CardDto 仍用 `ElementFlags` 位标志）
- Enemy AI 运行时规范（DTO 仅提供 `skillRefs` 池与可选 `scriptPath`）
- `RewardEntryDto.params` 强类型化（首版 `Dictionary<string, object>`）

---

### 13.9 自检（原 §9）

- 与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界一致
- 与本文 §12.2 类型约束（sealed class + JsonPropertyName + init）一致
- 实现文件：`CharacterDto.cs`, `EnemyDto.cs`, `BattleDto.cs`, `EventDto.cs`, `ItemDto.cs`, `SharedDefinitionDtos.cs`, `ContentEnums.cs`, `ContentDefinitionValidator.cs`, `ContentModLoader.cs`, `GameDefinitionRegistry.cs`

---

## 14. 角色实例 / 战斗实例 / 卡组预设 / 手牌槽（原 2026-06-18 角色实例设计规格 §1–§4）

> **来源**：`2026-06-18-character-instance-design.md`（**日期** 2026-06-18；**状态** 已确认（首期实现范围）；**范围** `DeckPreset`、`CharacterInstance`、`HandSlot`、`CharacterBattleInstance` 及直接依赖的类型（`CharacterAttributes`、`CardStatBlockDto`））。已归档至 `Doc/archive/superpowers/specs/2026-06-18-character-instance-design.md`。角色 DTO 见本文 §13.5.1；卡牌 DTO 见本文 §12.3。

### 14.1 首期实现边界（原 §1）

#### 本期实现

| 类型 | 路径 |
|------|------|
| `DeckPreset` | `Src/mod/combat/DeckPreset.cs` |
| `CharacterInstance` | `Src/mod/combat/CharacterInstance.cs` |
| `HandSlot` | `Src/mod/combat/HandSlot.cs` |
| `CharacterBattleInstance` | `Src/mod/combat/CharacterBattleInstance.cs` |
| `CharacterAttributes` | `Src/mod/combat/CharacterAttributes.cs` |
| `CardStatBlockDto` | `Src/frame/content/definitions/CardStatBlockDto.cs`（扩展 `CardDto`） |

#### 后续单独文档实现（本期不做）

- `BuffInstance` 及 Buff 结算管线
- 局内存档（`RunSaveDto`、`RunSaveService`、`ToSaveDto` / `FromSaveDto`）
- `ObtainedCardPool` 类型（本期构筑校验通过方法参数传入已获得卡 id 集合）
- `TeamBattleState`、队伍共用 HP
- `CharacterBattleFactory` 独立类（本期用 `CharacterBattleInstance.TryCreate` 静态方法）
- 战斗阶段机、`CombatTurnController`

---

### 14.2 已确认玩法规则（原 §2）

| 决策 | 结论 |
|------|------|
| `CharacterDto` | 无战斗属性；`cards` 为角色专属构筑池 |
| 角色属性 | 当前卡组内卡牌 `stats` 字段 **逐项求和** |
| 卡组数量 | 初始 **1 套**；`TryCreateDeck()` 最多 **10 套**；**不可删除** |
| 新建卡组默认 | 填入专属卡；超过 10 张取 `CharacterDto.Cards` **前 10 张** |
| 每套卡组 | 最多 10 张、不可重复 |
| 构筑卡来源 | 已获得卡 id 集合（调用方传入）∪ 角色专属卡 |
| 构筑时机 | 战斗外；`IsDeckLocked == true` 时禁止编辑 |
| 手牌 | 固定 5 槽；槽位效果本期用 `HandSlotEffectRef`（仅 `buffId` + `params`）占位，后续替换为 `BuffInstance` |
| 战斗实例 | 不参与存档；由 `CharacterInstance` 当前卡组生成牌库并洗牌 |

卡组内卡牌 `stats` 的属性预算口径见本文 §15.3。

---

### 14.3 类型职责（原 §3）

#### `DeckPreset`

- `DeckId`、`DisplayName?`、`CardIds`（≤10，无重复）
- `CreateWithExclusiveCards(CharacterDto)`
- `TryAddCard` / `TryRemoveCard`
- `Validate(IReadOnlySet<string> buildableCardIds)`

#### `CharacterInstance`

- 构造：`CharacterInstance(CharacterDto)` 与无参 `CharacterInstance()`
- `TryCreateDeck()`、`TryEditDeck`、`TrySetCurrentDeck`
- `GetBuildableCardIds(IReadOnlySet<string> obtainedCardIds)`
- `ComputeAttributes(GameDefinitionRegistry)` — 基于当前卡组
- `SetDeckLocked(bool)` — 由上层战斗流程调用

#### `HandSlot`

- `PlaceCard` / `ClearCard`
- `SlotEffects`：`List<HandSlotEffectRef>`（后续对接 Buff 系统）

#### `CharacterBattleInstance`

- `TryCreate(CharacterInstance, GameDefinitionRegistry, HostRng, out error)`
- 牌库 / 5 手牌槽 / 墓地、`CardRuntimeEntry`
- 能量三元组与 `BaseAttributes` 快照
- `HasActed`

---

### 14.4 自检（原 §4）

- 无局内存档、无 BuffInstance、无队伍 HP — 与首期范围一致  
- 手牌槽位效果可扩展 — 通过 `HandSlotEffectRef` 预留  
- 属性唯一定义在 `CardDto.stats` — 与「CharacterDto 无属性」一致

---

## 15. 内容域收敛与卡组预算（并入自 2026-09-21 莱因哈特套件与战斗机制扩展规格）

> **来源**：`2026-09-21-reinhardt-and-normal-attack-extensions.md` 的**内容域**章节 —— 原 §2 种族收敛、§3 常驻天赋移除、§9 卡组属性预算、§12.1 稀有度档名收敛、§12.2 的 `CharacterDto.descId` 删除。该规格**仍存活**（普攻扩展、魔法伤害、Combat 条件域、取值与槽位机制、莱因哈特/巴赫套件归战斗规格与 Buff 规格维护；角色详细界面展示形态归 Global 功能规格维护），本规格不归档。本文只承载上述内容域事实；未并入清单见本文 §16。

### 15.1 种族收敛（2026-09-21）（原 §2）

`ERace` 位标志重排（安全：种族只存在于内容 JSON 与运行期 DTO，不落存档）：

| 旧 | 新 |
|---|---|
| Canine / Feline / Bird / Beast / Reptile | **Animal**（动物） |
| Demonic / Devil | **Demon**（恶魔族） |
| — | **Academic / Fantasy / Astronomy / Hero / Calamity** |
| UnKnown | **Unknown**（更名） |
| 未提及 | 保留：Human / Insect / Fish / Plant / Machine / Angel / Dragon / God / Undead |

- 本地化键随枚举走：`UI_RACE_ANIMAL` / `UI_RACE_DEMON` / `UI_RACE_ACADEMIC` …（图鉴筛选下拉与队伍编辑界面共用）。
- `RunTeamEditDlg` 的元素/种族显示从"枚举 ToString()"改为本地化键（多标志用「、」连接）——否则双种族角色会显示 `Animal, Dragon`。

生效清单见本文 §13.4.1 的 `ERace` 行。

---

### 15.2 常驻天赋移除（原 §3）

`CharacterDto.buffRefs` 字段、校验器接线与文档全部删除：该机制与 `passives` 重复却走另一条挂载路径。角色常驻增益一律用 `passives`（潜能门闩 → 开战挂 buff）。

> 敌人侧 `EnemyDto.buffRefs` **保留**：木桩的"每回合回血"等开战 buff 靠它，与角色被动是两件事。

生效字段表见本文 §13.5.1（`CharacterDto.passives`）与 §13.5.2（`EnemyDto.buffRefs`）；校验规则见本文 §13.6。

---

### 15.3 卡组属性预算（原 §9）

一张卡的 `stats.attributes` 折算总值 = **40 最大生命**，其中 **1 点物理攻击 = 10 点最大生命**；
其余属性（物防 / 魔防 / 魔攻 / 回复量等）同样按 **1 点 = 10 点最大生命** 折算。

- 对齐方式固定为**保留最大生命、削减物攻**，不要反过来削生命去换物攻。
- chalux：逆戟冰冲 `20生命/2物攻`、璨华长路 `40生命`、才煌的绝剑 `30生命/1物攻`、绝念 `20生命/2物攻`。
- 莱因哈特：黑船宝藏 `20/2`、呼啸激攻 `30/1`、碧蓝大海航行 `40/0`、辉耀宝刀 `30/1`。
- 参宿四（2026-09-25）：干涉·赤红矩阵 `30生命/1物防`、掩星·不可预知 `30生命/1物防`、
  真见·吞食天地 `40生命`、操控·键闭 `40生命`。

卡组属性求和口径见本文 §14.2（`CardStatBlockDto` 路径见 §14.1）。

---

### 15.4 稀有度档名收敛（原 §12.1）

`ERarity` 收敛为 `Common` / `Special`（原 Uncommon）/ `Rare` / `Exclusive`（原 Epic）/ `Legendary`。
卡框资源路径同步为 `Resource/Assets/CardFrame/{Common,Special,Rare,Exclusive,Legendary}.png`；
出货内容里 8 张专属卡已从 `Epic` 迁到 `Exclusive`。

> 2026-09-21 合并：原表述（`ERarity`：Common, Uncommon, Rare, Epic, Legendary）已收敛为（`Common` / `Special` / `Rare` / `Exclusive` / `Legendary`）。

生效清单见本文 §13.4.1 的 `ERarity` 行。

---

### 15.5 角色简介字段移除（原 §12.2 的 DTO 部分）

`CharacterDto.descId` 删除（字段、内容、翻译行、图鉴文本检索、DTO 文档一并清理）。

> 2026-09-21 合并：原表述（`CharacterDto` 含 `descId` 简介字段）已收敛为（`CharacterDto.descId` 删除，角色详细界面不再读取该字段）。

生效字段表见本文 §13.5.1。

> **范围外指路**：同批的「角色详细界面改为展示专属卡牌 / 横向卡牌列表」属 **UI 展示形态**，归 Global 功能规格维护，本文不承载。

---

## 16. 合并自检与范围外指路（2026-09-21）

### 16.1 合并自检

- **占位扫描**：无 TBD/TODO 强制项。
- **保真**：三份下级规格的规则句、字段表（含每个 DTO 的全部字段行）、枚举成员清单、数值、代码标识符、明确后置项均原样并入 §12–§14；仅追加收敛注记、段号映射表、来源说明与本节。
- **段号锚点**：§12–§15 的各小节标题均标注原段号（如 `### 15.1 种族收敛（2026-09-21）（原 §2）`），并按文档头「段号映射」表与原规格 §N 一一对应。
- **链接处理**：指向本次被并入规格的链接已改写为本文 §N；指向仍存活规格（总规格 / 战斗规格 / 条件规格 / Buff 规格 / 普攻规格 / jsenv-mod-scripting）的链接保持原样。
- **单轨**：定义权威仅 `GameDefinitionStore`；DTO 字段表与校验规则（§13.6）描述同一套定义。

### 16.2 未并入本文的内容（含理由）

| 来源 | 未并入章节 | 归属 |
|---|---|---|
| 2026-09-21 莱因哈特套件与战斗机制扩展 | §1 本次交付总览 | 汇总表，非规则；下属各章已分别归档（内容域 → 本文 §15，其余归战斗规格） |
| 同上 | §4 连携统计口径修正 | 战斗运行时（连携档位与 `ChainCalculator` / `AppliesToCard`） |
| 同上 | §5 魔法伤害落地（含 §5.1 `attackScale`） | 战斗规格 §7 后置项清账；伤害包管线 |
| 同上 | §6 普通攻击扩展（§6.1–§6.4） | 扩充普通攻击规格（次数 / 追打 / 专项倍率 / 可观测） |
| 同上 | §7 Combat 条件域启用 | 条件规格 + 战斗运行时（`ICombatCondContext`、`CardPlayedThisTurn`） |
| 同上 | §8 新增取值与槽位机制（`PartyCountScaled`、`slot.free_cost`、`trait.immune_seal`） | Buff 运行时 / GAS / 战斗取值 |
| 同上 | §10 莱因哈特、§11 巴赫（含 §11.0–§11.3） | 具体出货角色与卡牌套件；其**卡组属性贡献**数值口径已由本文 §15.3 承载 |
| 同上 | §12.2 的 UI 部分（角色详细界面改为展示专属卡牌 / 横向虚拟列表） | Global 功能规格（界面展示形态） |
| 同上 | §13 明确后置项 | 归属各自运行时规格（普攻次数上限 / 追打可观测 UI / 元素克制抗性 / 敌方 `buffRefs` 未统一） |

### 16.3 来源冲突修正（四处）

| 冲突 | 旧表述 | 本文处置 |
|---|---|---|
| ① `ERace` | 5 族分散（Canine/Feline/Bird/Beast/Reptile）、Demonic + Devil 分列、`UnKnown` 拼写 | 收敛为 `Animal` / `Demon` / 新增 5 族 / `Unknown`；注记见 §13.4.1，规则见 §15.1 |
| ② `ERarity` | Common, Uncommon, Rare, Epic, Legendary | 收敛为 `Common` / `Special`（原 Uncommon）/ `Rare` / `Exclusive`（原 Epic）/ `Legendary`；注记见 §13.4.1，规则见 §15.4 |
| ③ `CharacterDto.buffRefs` | 角色常驻天赋引用字段 | 删除；角色常驻增益唯一通道 = `passives`；`EnemyDto.buffRefs` 保留；注记见 §13.5.1，规则见 §15.2 |
| ④ `CharacterDto.descId` | 角色简介翻译键字段 | 删除；注记见 §13.5.1，规则见 §15.5 |
