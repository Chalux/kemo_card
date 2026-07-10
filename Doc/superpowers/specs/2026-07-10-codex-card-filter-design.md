# 图鉴卡牌过滤与分页设计

**日期：** 2026-07-10  
**状态：** 已确认

## 1. 目标

完成 `CodexDlg` 卡牌 Tab（`UI_CARD`）的图鉴列表：支持多条件过滤、文本搜索、条件列表管理，并按固定 8 格网格 + `BasePager` 分页展示 `BaseCardItem`。

## 2. 范围

### 做

- 在 `CodexDlg.cs` 绑定并驱动现有场景节点：
  - `OBCardTypeSelector` / `OBCardOperateSelector` / `OBCardValSelector`
  - `ItemListConditions`、`BAdd`、`IptTxtFilter`
  - `GridContainerCardList`（8 个 `BaseCardItem`）、`BasePager`
- 条件过滤字段：卡牌类型、费用、元素、角色（Role）、费用类型、标签（Tags）
- 文本搜索：匹配本地化卡名与关联技能效果文案（`skillRefs` → `SkillDto.DescId`）
- 条件列表展示 / 添加 / 点击移除；变更后立刻刷新列表与分页
- 排除 `HideInDex == true` 的卡牌
- 新增必要本地化键（字段名、操作符、角色/费用类型/元素等展示文案）
- 纯过滤逻辑可单测（不依赖 Godot 节点）

### 不做

- 不抽独立 Filter 组件场景
- 不改用 `VirtualList`
- 不实现其它 Tab（角色/敌人等）
- 不给 `CardDto` 增加 `race` / `descId`
- 不实现图鉴解锁灰显（本期全部可见卡均正常展示；解锁状态可后续接）

## 3. 架构

采用方案 1：`CodexDlg` 负责 UI 编排；小型纯逻辑辅助负责匹配与切片。

```
CodexDlg
  ├─ 绑定控件 / 填充 OptionButton
  ├─ 维护 List<CardFilterCondition> + TextQuery
  ├─ 调用 CardCodexQuery.Filter(...) → 过滤后列表
  ├─ 按 PageSize=8 切片 → BaseCardItem.SetData
  └─ 同步 BasePager.TotalPages / CurrentPage
```

数据源：`AppRoot.Services.ContentModPipeline.Registry.Store.Cards`（及 `TryGetSkill` 解析效果文案）。

## 4. 过滤模型

### 4.1 条件字段（`OBCardTypeSelector`）

| 字段 | 对应 `CardDto` | 值来源（`OBCardValSelector`） |
|------|----------------|------------------------------|
| 卡牌类型 | `CardType` | `ECardType` 枚举 |
| 费用 | `Cost` | 固定数值选项（如 0–10，另可含常见上限；缺省覆盖常见费用） |
| 元素 | `Element`（int Flags） | `EElement` 单元素位（不含 None） |
| 角色 | `Role` | `ERole`（可含 None） |
| 费用类型 | `CostType` | `ECostType` |
| 标签 | `Tags` | 当前卡池 tags 去重排序 |

切换字段时：重建操作符下拉与值下拉，并重置选中项。

### 4.2 操作符（`OBCardOperateSelector`）

| 字段 | 可用操作 |
|------|----------|
| 费用 | `<=` / `=` / `>=` |
| 卡牌类型 / 角色 / 费用类型 | `=` / `≠` |
| 元素 | `包含` / `等于`（按 Flags：包含=有该位；等于=精确等于该位值） |
| 标签 | `包含` / `等于`（包含=Tags 含该字符串；等于=Tags 集合与单值列表完全一致，即仅含该标签且仅一个） |

### 4.3 条件记录

```csharp
readonly record struct CardFilterCondition(
    ECardFilterField Field,
    ECardFilterOp Op,
    string ValueId,   // 枚举名 / 数字字符串 / tag 原文
    string DisplayText);
```

- `DisplayText`：写入 `ItemListConditions` 的本地化可读串（如「卡牌类型 = 物」）。
- 多条件之间 **AND**。
- 「添加」：校验当前三项有效后追加；立即 `RefreshCardList(resetPage: true)`。
- 点击 `ItemListConditions` 某项：移除对应条件；立即刷新（重置到第 0 页）。

### 4.4 文本搜索（`IptTxtFilter`）

- 不进入条件列表。
- 与条件列表 **AND**。
- 空串 = 不过滤文本。
- 匹配目标（子串、忽略大小写）：
  1. `Localization.Tr(card.DisplayNameId)`（若翻译结果仍为键，则同时尝试原始 `DisplayNameId`）
  2. 每个 `skillRefs` 对应技能的 `Localization.Tr(skill.DescId)`
- 触发：`TextSubmitted`（回车）与 `FocusExited`（失焦）时刷新。

## 5. 列表与分页

- `PageSize = 8`，对应场景中已有 8 个 `BaseCardItem` 子节点（按子节点顺序复用，不动态增删）。
- 打开对话框 / 过滤变更：过滤全量 → 计算 `TotalPages = ceil(count / 8)`（0 条时 TotalPages=0）→ `SetPage(0)` → 填充当前页。
- 翻页：`BasePager.OnPageChanged` / `PageChanged` → 仅重填当前页槽位。
- 槽位：有数据 `SetData(card)`；无数据 `SetData(null)` 并保持节点可见占位（或隐藏由 `SetData(null)` 现有清空逻辑处理；保持 8 格布局不增删节点）。

排序：按 `Id` 序（稳定、可预期）；不做稀有度排序。

## 6. UI 绑定约定

- 使用 `[Export]` + 场景 `node_paths` 绑定控件（与 `BaseCardItem` / `BasePager` 一致）。
- 布局仍在 `CodexDlg.tscn`；C# 只写逻辑。
- 所有面向用户文案走本地化键（`Resource/Locale/strings.csv`）。

建议新增键（实现时可微调命名，但需覆盖）：

- 字段：`UI_CODEX_FILTER_CARD_TYPE` / `COST` / `ELEMENT` / `ROLE` / `COST_TYPE` / `TAG`
- 操作：`UI_CODEX_OP_EQ` / `NE` / `LE` / `GE` / `CONTAINS` / `EXACT`
- 元素 / 角色 / 费用类型展示键（若尚无）
- 已有：`UI_CODEX_TXT_FILTER`、`UI_OP_ADD`、`UI_CARD_TYPE_*`

## 7. 文件变更（预期）

| 文件 | 变更 |
|------|------|
| `Src/mod/global/Ui/CodexDlg.cs` | 绑定、交互、分页刷新 |
| `Src/mod/global/Ui/CodexDlg.tscn` | Export `node_paths`；必要时去掉占位无关项 |
| `Src/mod/global/Ui/CardCodexQuery.cs`（或 `Def/` 旁纯逻辑） | 过滤 / 匹配纯函数 |
| `Src/mod/global/Def/Definitions.cs`（或同目录） | 字段/操作/角色等 locale 映射 |
| `Resource/Locale/strings.csv` | 新增键 |
| `Tests/...` | `CardCodexQuery` 单测 |

## 8. 验收

1. 打开图鉴卡牌 Tab，默认显示全部非 `HideInDex` 卡，分页正确。
2. 选择字段/操作/值后点「添加」，条件出现在 ItemList，列表与分页立即更新。
3. 点击条件项可移除，列表立即恢复对应结果。
4. 多条件 AND；文本搜索与条件 AND。
5. 标签条件「包含」可筛出带该 tag 的卡。
6. 翻页不丢失当前过滤；过滤变更回到第 1 页。
7. 无硬编码用户可见文案。
