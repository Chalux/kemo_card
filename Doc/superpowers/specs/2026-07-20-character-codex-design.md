# 角色图鉴与角色视觉资源设计

**日期：** 2026-07-20  
**状态：** 已确认  
**关联：** [图鉴卡牌过滤](2026-07-10-codex-card-filter-design.md)、[角色 DTO](2026-06-16-character-battle-event-item-dto-design.md)、[总项目说明](2026-05-11-kemo-card-design.md)

## 1. 目标

在 `CodexDlg` 增加角色 Tab：过滤、分页、卡片展示、悬停摘要 tip。同步扩展角色视觉资源模型（多表情立绘 + 路由 + 序列帧 presentation）。**序列帧动作切换放在角色详情对话框**（镜像卡牌详情打开方式），图鉴列表仅播默认动画。

## 2. 范围

### 做

- 扩展 `CharacterDto`：`portraits`（entries + routes）+ `presentation`；保留并兼容旧 `artPath`
- `EPortraitKey` 标准表情枚举 + `custom:*` 扩展 key
- `PortraitResolver`：按 routeKey / 表情 key 解析路径，失败回退 `Neutral`
- `CharacterPresenter` 组件：优先播 `SpriteFrames`，否则显示默认立绘
- `CharacterCodexQuery`：元素 / 职业 / 种族 / 标签过滤 + 文本搜索 + 分页
- `CharacterSummaryBuilder`：悬停 tip 文案
- `BaseCharacterItem` + `CodexDlg` 角色 Tab（列表只播 `defaultAnim`，点击打开详情）
- `CharacterDetailsDlg`：展示 presenter + 名称/描述等基础信息；**动作切换 UI 仅在此对话框**
- 本地化键（种族、过滤字段、Tab、动作、详情标题等）
- 纯逻辑单测（Resolver / Query / SummaryBuilder）

### 不做

- 战斗 UI / 战斗单位挂接
- Spine（`presentation.kind` 可预留枚举值，本轮不实现）
- Dialogue Manager 插件安装与 balloon 接线（见总项目说明；本轮只保证 Resolver API 可被后续桥接）
- 图鉴列表内的动作切换控件
- 详情内嵌专属卡牌网格 / 完整技能面板等重型内容（可后续加）
- 图鉴解锁灰显

## 3. 架构

```text
CharacterDto
  portraits ──► PortraitResolver ──► path
  presentation ──► CharacterPresenter ──► AnimatedSprite2D | TextureRect
                         ▲
CodexDlg [卡牌 Tab | 角色 Tab]
  CharacterCodexQuery + BaseCharacterItem (defaultAnim only)
                         │ 点击 OpenDetails
                         ▼
              CharacterDetailsDlg
                ├─ CharacterPresenter
                ├─ 动作 OptionButton（ListAnims / Play）
                └─ 名称 / 描述等基础文案
```

依赖方向：`mod` → `frame`。DTO / 枚举在 `Src/frame/content/`；Resolver 纯逻辑优先 `frame`；UI 组件与 Query / Builder 放 `Src/mod/global/Ui/`。打开详情对齐 `CardDetailsDlg` / `BaseCardItem.ClickAction` 模式。

## 4. 角色视觉资源模型

### 4.1 立绘（portraits）

标准枚举 `EPortraitKey`：

`Neutral`、`Smile`、`Angry`、`Sad`、`Surprised`、`Hurt`、`Serious`、`Happy`、`Naughty`

扩展 key：字符串形式 `custom:<name>`（如 `custom:wave`），不进入枚举。

JSON 形态（示意）：

```json
{
  "portraits": {
    "default": "Neutral",
    "entries": [
      { "key": "Neutral", "path": "chars/kemo/neutral.png" },
      { "key": "Happy", "path": "chars/kemo/happy.png" },
      { "key": "custom:wave", "path": "chars/kemo/wave.png" }
    ],
    "routes": {
      "intro": "Happy",
      "tease": "Naughty"
    }
  },
  "artPath": "chars/kemo.png"
}
```

- `default`：缺省表情，通常 `Neutral`
- `entries`：表情 key → 相对资源路径（解析规则对齐卡牌 `ArtPath`：Mod content 根 → `res://Resource/Assets/...`）
- `routes`：**每角色自己的** routeKey → 目标表情 key（标准枚举名或 `custom:*`）

**兼容：** 若无 `portraits` 且存在非空 `artPath`，视为仅含 `Neutral` → `artPath` 的单条 entries，`default = Neutral`。

### 4.2 PortraitResolver

```text
ResolveByRoute(character, routeKey) → resolved path
  1. routes 无 routeKey → Neutral
  2. 得到目标表情 key
  3. entries 无该 key，或资源文件不存在 → Neutral
  4. 否则返回该 path

ResolveByKey(character, portraitKey) → path
  entries 缺失或资源不存在 → Neutral
```

`Neutral` 自身也缺失时：返回空路径，调用方显示占位 / 隐藏贴图，并打 Warning 日志（不抛异常）。

对话侧后续通过 Dialogue Manager 调用本 Resolver（传 routeKey），不在本轮实现。

### 4.3 动态表现（presentation）

```json
{
  "presentation": {
    "kind": "SpriteFrames",
    "path": "chars/kemo/kemo_frames.tres",
    "defaultAnim": "idle",
    "anims": ["idle", "happy", "hurt"]
  }
}
```

| 字段 | 说明 |
|------|------|
| `kind` | 本轮仅实现 `SpriteFrames`；可预留 `Spine` 但不实现 |
| `path` | `SpriteFrames` 资源路径 |
| `defaultAnim` | 默认动画名，通常 `idle` |
| `anims` | **可选白名单**；省略则使用资源内全部动画名 |

动作列表规则（方案 A）：以 `SpriteFrames` 内动画名为准；若配置了 `anims`，取交集（白名单过滤）。

### 4.4 CharacterPresenter

可复用 Control 组合组件（场景 + C#）：

- `Bind(CharacterDto)`：有可用 presentation → 加载并 `Play(defaultAnim)`；否则显示 `ResolveByKey(Neutral)` 立绘
- `Play(animName)`：切换动画；不存在则回退 `defaultAnim`
- `ListAnims()`：供**角色详情**动作切换 UI
- 无 presentation 时详情侧隐藏 / 禁用动作切换

本轮图鉴列表与详情对话框消费；战斗后接同一组件。

## 5. 角色图鉴 UI

### 5.1 Tab

在现有 `CodexDlg` 增加页签：`UI_CARD`（已有）| `UI_CHARACTER`（新增）。切换 Tab 时：

- 切换可见列表网格（卡牌格 / 角色格）与过滤字段选项
- 各自维护条件列表与文本过滤；互不污染
- 打开对话框时默认卡牌 Tab，两边条件均清空

### 5.2 过滤字段

| 字段 | 对应 `CharacterDto` | 操作符 | 值来源 |
|------|---------------------|--------|--------|
| 元素 | `Element`（`EElement` Flags） | Contains / Exact | 单元素位（跳过 None）；语义对齐卡牌元素过滤 |
| 职业 | `Role` | Equal / NotEqual | `ERole` |
| 种族 | `Race`（`ERace` Flags，**可多枚举按位组合**） | Contains / Exact | 单种族位（跳过 None） |
| 标签 | `Tags` | Contains / Exact | 角色池 tags 去重 |

**种族 Flags 约定：**

- 内容 JSON 支持数组写法，按位或合并，例如 `"race": ["Human", "Canine"]` → `Human | Canine`（若当前反序列化仅支持单字符串，实现期补 Flags 数组转换器；单字符串 `"Human"` 仍合法）
- **Contains**：`(raceFlags & bit) != 0`（拥有该种族位即可）
- **Exact**：`raceFlags == bit`（仅含该一种族，不多不少）
- 过滤下拉只列出单个种族位，不列出组合值

文本搜索：本地化显示名、`descId` 译文、关联技能描述（与卡牌图鉴类似）。

分页：`PageSize = 8`，复用 `BasePager` / `SlicePage` / `TotalPages` 模式（可抽公共分页工具或在 `CharacterCodexQuery` 内镜像）。

### 5.3 BaseCharacterItem

展示：立绘或序列帧（仅 `defaultAnim`）+ 名称；元素色条可选（对齐卡牌元素色）。  
悬停 tip：`CharacterSummaryBuilder` → `KeywordTipService.ShowCustomTips`。  
点击：默认打开 `CharacterDetailsDlg`（对齐卡牌 `OpenDetails`；详情内嵌角色展示须关闭二次打开，避免递归）。

### 5.4 CharacterDetailsDlg（动作切换仅在此）

镜像 `CardDetailsDlg`：

| 项 | 约定 |
|----|------|
| Payload | `CharacterId: string`（必填） |
| UI 注册 | `GlobalUiIds.CharacterDetails` |
| Presenter | `Bind(character)`，默认 `Play(defaultAnim)` |
| 动作切换 | `OptionButton`（键 `UI_CODEX_ANIM`）；选项 = `ListAnims()`；变更 → `Play` |
| 无 presentation | 显示默认立绘；动作控件 `Visible = false` 或禁用 |
| 文案 | 名称、描述（`descId`）；本轮不做专属卡网格 / 完整技能列表 |
| 找不到角色 | Warning 并关闭 |

图鉴列表**不提供**动作切换控件。

### 5.5 Tip 文案

| 行 | 内容 |
|----|------|
| Title | 角色名 |
| Body 1 | `元素 职业 种族`（空格连接；None 跳过） |
| Body 2+ | 技能描述纯文本（剥 BBCode）；多技能换行 |

多 Flags 展示：元素、种族均按已置位枚举名本地化后用 `、` 连接（与卡牌 tip 多元素一致），再与职业用空格拼成第 1 行。

## 6. 本地化（新增键示例）

| 键 | zh | en |
|----|----|----|
| `UI_CHARACTER` | 角色 | Character |
| `UI_CODEX_FILTER_RACE` | 种族 | Race |
| `UI_CODEX_CHAR_TXT_FILTER` | 角色名或描述 | Character Name Or Desc |
| `UI_CODEX_ANIM` | 动作 | Anim |
| `UI_CHARACTER_DETAILS_TITLE` | 角色详情 | Character Details |
| `UI_RACE_*` | 各族名称 | … |

过滤字段 Element/Role/Tag 可复用已有 `UI_CODEX_FILTER_*`；操作符复用已有 `UI_CODEX_OP_*`。

## 7. 错误与边界

- Resolver / Presenter：缺资源 Warning，不崩溃；UI 占位或隐藏
- 空角色池：分页 0，网格清空
- Tab 切换中止悬停 tip（`HideTips`）
- `SpriteFrames` 加载失败：回退立绘

## 8. 测试要点

- `artPath` 兼容为 Neutral 单条
- route 缺失 / 目标 entry 缺失 / 文件不存在 → Neutral
- `custom:*` 路由与直取
- CharacterCodexQuery：元素/种族 Flags 的 Contains/Exact、职业 Equal/NotEqual、Tag Contains/Exact、文本搜索、分页
- SummaryBuilder：多元素/多种族 `、` 拼接与技能剥离
- 种族 JSON 数组 → Flags 合并（有转换器时）
- anims 白名单与 SpriteFrames 求交（纯逻辑可测列表计算）

## 9. 后续（非本轮）

- 战斗单位挂 `CharacterPresenter`
- 安装并集成 [Dialogue Manager](https://github.com/nathanhoad/godot_dialogue_manager)（约定见总项目说明），balloon 经 C# 桥接调用 `PortraitResolver.ResolveByRoute`
- Spine `presentation.kind`
- 角色详情扩展：专属卡列表、技能面板等
