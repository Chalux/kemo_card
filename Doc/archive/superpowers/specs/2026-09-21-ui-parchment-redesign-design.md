# UI 视觉重构：羊皮纸卡桌 + 左栏导航 设计

**日期**：2026-09-21
**状态**：已实装（本文取代 [2026-09-19 UI 主题规格](../../../superpowers/specs/2026-09-19-ui-theme-and-debug-panel.md) §1–§2 的调色板与样式描述；§3 BaseKemoButton、§5 调试面板、§6 本地化守卫不变）
**关系**：服从 [2026-05-15 UI 管理器规格](../../../superpowers/specs/2026-05-15-ui-manager-design.md) 与 [2026-09-15 ui-mod-binding 规格](../../../superpowers/specs/2026-09-15-ui-mod-binding-design.md)。**不改 UI 代码框架**（`Src/frame/ui` 一行不动），也不改各界面 `.cs` 的业务逻辑；变更只落在场景 `.tscn`、主题资源 `.tres`、`KemoPalette` 代码镜像与个别内联魔法色值。

---

## 1. 目标与边界

- **推倒重来**：所有玩家可见界面的布局、按钮编排、配色全部重做；旧的「深蓝夜色 + 琥珀金」「居中面板 + 竖排按钮」不保留。
- **视觉方向**：**暖色卡桌·羊皮纸**——米白/暖灰纸面为底，墨绿（主操作）与酒红（危险/强调）点缀，深墨色 2px 描边与硬投影，像摊在桌上的实体卡牌与索引卡。
- **布局范式**：**全屏页面 + 左侧竖向导航栏（Rail）**：主流程页面（主菜单、Run 主界面、选故事、队伍编辑、卡组编辑）都是「左栏 + 右侧大内容区」；页签类型被代码绑定的页面（图鉴、设置）采用「顶部标题栏 + 活页夹式页签」，页签内部再用左栏承载筛选/表单；仅 Alert、卡牌/角色详情保留为居中小窗。
- **不做**：`RunDebugDlg`（规格明确不追求主题一致）；新增美术贴图/字体/SVG 图标（继续只用 `cancel.svg`）；任何界面 `.cs` 的逻辑改动（仅允许两处颜色常量与 NodePath 同步）。

## 2. 调色板（唯一色源）

`kemo_theme.tres` 为场景侧唯一事实源，`KemoPalette` 代码侧镜像同步更新（供 `SettingToggleRow` 轨道、状态文案着色）。

| 名称 | Hex | 用途 |
|---|---|---|
| Paper | `#F1E7D3` | 页面底色、清屏色 |
| PaperLight | `#FAF3E3` | 卡片/按钮/输入框面 |
| PaperDark | `#E4D6BC` | 左栏底、悬停底、未选页签 |
| PaperDeep | `#D3C1A0` | 下沉区（列表底、按压态、滑轨）|
| Ink | `#3A2E25` | 描边、主文本、Tooltip/Toast 底 |
| InkSoft | `#6E5E50` | 次级文本、说明 |
| InkFaint | `#A89680` | 禁用文本、发丝线、滚动条 |
| Green / GreenHover / GreenPressed | `#2F5D50` / `#3B6F60` / `#25493F` | 主按钮、选中项、开关开启 |
| GreenDisabled | `#8FA39C` | 主按钮禁用 |
| Wine / WineHover / WinePressed | `#8B2E3A` / `#A33845` / `#6F242E` | 危险按钮、警示文案、焦点环、Toast 侧条 |
| TextOnAccent | `#F5EEDD` | 绿/红底上的文字 |

所有 UI 颜色只能来自上表；元素四色（红/蓝/绿/黄）仍由 `ColorDefinitions` 负责，不在本文范围。

## 3. 主题资源（kemo_theme.tres）

默认字号 16。统一节奏：按钮圆角 4、内容边距 (18,10)；卡片圆角 6；描边 2px Ink；硬投影 `shadow_size 6 / offset (0,3) / Ink@18%`。

| 类型 / 变体 | 样式要点 |
|---|---|
| `Button`（默认） | PaperLight 面 + 2px Ink 描边 + 投影；hover 面 `#FFF9EC` 描边 Wine；pressed PaperDeep 无投影；disabled PaperDark + InkFaint 字 |
| `Primary`（变体，`BaseKemoButton.Primary=true` 应用） | Green 面、TextOnAccent 字；hover/pressed 用 GreenHover/GreenPressed |
| `Danger`（变体，场景直接设置 `theme_type_variation`） | Wine 系，同上结构 |
| `Ghost`（变体） | 透明无描边，InkSoft 字；hover PaperDark；pressed PaperDeep |
| `RailNav`（变体） | 左栏导航：透明底、左对齐（节点 `alignment=0`）、高 48；hover PaperDeep + 左侧 4px Green 指示条；pressed 同 hover 且字色 Green |
| `IconClose`（变体） | 44×44 圆形（radius 22）PaperLight + 2px Ink；供 `BaseDlgComp/CloseBtn` |
| 焦点环（全部按钮） | 2px Wine，`draw_center=false` |
| `Panel`（默认 = 卡片） | PaperLight + 2px Ink + radius 6 + 投影 |
| `Panel` 变体 `PageBg` / `Rail` / `Sunken` / `Nameplate` | 页面底（Paper，无边）/ 左栏（PaperDark，仅右侧 2px Ink 边）/ 下沉区（PaperDeep，1px InkFaint，radius 6，无投影）/ 名牌（Ink 底，radius 4） |
| `PanelContainer` 变体 `SettingRow` | 透明底，仅下边 1px InkFaint 发丝线，内容边距 (8,10) |
| `Label` | 字色 Ink；变体 `Caption`（InkSoft，13）、`Eyebrow`（InkSoft，12）、`Danger`（Wine） |
| `RichTextLabel` | `default_color` Ink |
| `LineEdit` | `#FFFDF7` 面 + 1px InkFaint，radius 4，边距 (10,6)；focus 2px Wine；占位符 InkFaint；选区 Green@30% |
| `ItemList` | 面板 Sunken；hovered PaperDark；selected Green + TextOnAccent 字；guide InkFaint |
| `TabContainer` / `TabBar`（活页夹页签） | selected：PaperLight，2px Ink 三边（无下边），上圆角 6，Ink 字；unselected：PaperDeep，2px Ink，InkSoft 字；hovered：PaperDark；`TabContainer/panel`：PaperLight，2px Ink，左上角 0 其余 6 |
| `HSlider` | 轨 PaperDeep 1px InkFaint 高 6；已填充区 Green；高亮 GreenHover |
| `PopupMenu` | PaperLight + 2px Ink，radius 6；hover Green + TextOnAccent |
| `Tooltip` | Ink 底 radius 4，Paper 字 14 |
| `VScrollBar` / `HScrollBar` | 轨透明；grabber InkFaint / InkSoft / Ink，radius 4 |
| `HSeparator` | 1px InkFaint 线 |

`OptionButton`、`CheckBox` 等未单列的控件沿 Godot 类继承链回落到 `Button`/`Label` 样式。

## 4. 页面解剖

### 4.1 全屏页（Rail 范式）

```
Root(Control, full-rect)
└ Bg(Panel, PageBg)
└ Layout(HBoxContainer, full-rect, separation 0)
   ├ Rail(PanelContainer, Rail 变体, 宽 280；带列表的页面 320)
   │  └ Margin(24) → VBox(separation 12)
   │     ├ Eyebrow / Title(28~30)
   │     ├ HSeparator
   │     ├ 导航或信息（expand）
   │     └ 底部操作（返回 / 放弃 等）
   └ Content(MarginContainer 28) → VBox(separation 12)
      ├ Header(HBox：小标题 + 右侧动作)
      ├ Body(expand)
      └ Footer(HBox，主操作右对齐)
```

| 页面 | Rail | Content |
|---|---|---|
| `MenuWin` | 标题 `UI_MENU_TITLE`；`RailNav` 五键：新游戏（`Primary`）、继续、设置、图鉴；底部 退出（`Ghost`） | 装饰性卡桌：三张倾斜的空白纸卡（`Panel` 旋转 -8°/0°/+8°），无文字、无新增翻译键 |
| `RunMainWin` | 故事名（26，自动换行）；信息账本（`Sunken` 面板内 2 列 Grid：阶段/环/种子/金币，值右对齐）；操作栈：队伍（`Primary`）、保存、保存并退出、快速读取；底部：放弃（`Danger`）、调试（`Ghost`，仅 debug） | `Sunken` 大卡桌面（未来战斗界面挂点）；充能球面板为右上角纸卡（`Panel`），内含标题/列表/提示/触发键（`Primary`） |
| `StorySelectDlg` | 宽 320：标题；`UI_STORY_LIST` 说明；`StoryList`（ItemList，expand）；底部 返回 | 故事卡：名字（34）、元信息 2 列 Grid（作者/Mod/模式）、描述（expand，autowrap）、解锁提示（`Danger` Label 变体）；Footer：种子输入（左） + 开始（`Primary`，右） |
| `RunTeamEditDlg` | 宽 300：标题；当前上阵说明（代码填充）；当前上阵 160×208 居中；下阵按钮；状态文案（expand 底部）；关闭 | Header 放 `SlotTabs`（TabBar）；Body HBox：中列（可上阵说明 + `PoolList` expand，宽 ≥ 360）｜右列（预览卡：`Presenter` 200 高、名字 20、信息、属性 expand；卡组说明 + `DeckStrip` 212 高） |
| `RunCharacterDeckDlg` | 宽 300：标题（角色名）；`LblDeckCount`（30，大数字）；状态文案（expand 底部）；上阵（`Primary`，全宽）；关闭 | Header：`LblDeckCaption` + `DeckTabs` + 新建卡组；Body HBox：`DeckList` ｜ `PoolList`，各带说明 Label |

### 4.2 页签页（标题栏 + 活页夹）

```
Root(full-rect) → Bg(PageBg)
└ Margin(24) → VBox(separation 8)
   ├ TopBar(HBox 高 44：Title(26) + spacer + BaseDlgComp 提供的 CloseBtn)
   └ TabContainer(expand)
```

| 页面 | 页签内容 |
|---|---|
| `CodexDlg` | 每页 HBox：左筛选栏（`Sunken` 面板宽 300：字段/操作/值 三个 OptionButton **竖排全宽**、添加键、条件 ItemList expand、文本筛选 LineEdit + 搜索键）｜右：`GridContainer` 4 列 ×2 行 = 8 张 **原尺寸** 卡（去掉 0.6 缩放），下方 `BasePager` 右对齐 |
| `SettingDlg` | 每页 `ScrollContainer` → 居中「纸页」（`Panel` 卡片，最大宽 760）→ VBox 设置行；行组件改为 `PanelContainer(SettingRow)` 承载发丝线 |

`BaseDlgComp` 在页签页中以 full-rect 实例出现，仅贡献 `CloseBtn`（其背景 Panel 隐去或改为 `PageBg`）。

### 4.3 居中小窗（BaseDlgComp）

`BaseDlgComp` 重做为：`DlgBg`（`Panel` 卡片）+ 顶部 6px Wine 色带（`ColorRect`）+ 右上 `CloseBtn`（`IconClose` 变体，`cancel.svg`）。宿主子节点仍挂在 `BaseDlgComp` 下，NodePath 不变。

| 窗 | 布局 |
|---|---|
| `AlertDlg` | 560×300 居中；标题 26 左对齐、描述 18、底部按钮右对齐（取消 `Ghost`、确定 `Primary`） |
| `CardDetailsDlg` | 980×580；左 `BaseCardItem` 区；右：名字 30、Mod/作者 `Caption`、发丝线、描述 RichText 20 |
| `CharacterDetailsDlg` | 980×580；左 `CharacterPresenter`；右：名字 30、动画选择行（Caption + OptionButton）、发丝线、简介 RichText 18、被动 RichText 16 |

### 4.4 组件

| 组件 | 重做要点 |
|---|---|
| `BaseCardItem` | `CRPlaceholder`/`CRBg` 两个 ColorRect 换成 `CardFace`（`Panel` 卡片）；`CArtRoot` 内缩 8px；费用/类型/数值 Label 走 Ink；`CRAttr` 元素圆点保留（shader 不变） |
| `BaseCharacterItem` | 同上卡面；`TName` 背后加 `Nameplate` 面板（Ink 底、Paper 字） |
| `BasePager` | 首/上/下/末四键 `Ghost` 变体 + 页码 Label + 页码输入(64) + 跳转键 |
| `Setting*Row` | 根节点 `HBoxContainer` → `PanelContainer(SettingRow)` + 内部 HBox，节点 NodePath 相应加一层 `HBox/` |
| `ToastItem` | Ink 底、左侧 4px Wine 边、Paper 字、radius 4 |
| `KeywordTipPanel` | Ink 底 2px Ink 边 radius 4；标题 Paper 16、描述 `#CDBFA6` 13 |

## 5. 代码侧改动（受限清单）

1. `KemoPalette`：常量值改为 §2 色板（含新增 `SurfaceSunken` 供列表锁定项底色），文档注释同步；`KemoTheme` 不变。
2. `StorySelectDlg.cs:85`：锁定故事的 `SetItemCustomBgColor` 内联色改为 `KemoPalette.SurfaceSunken`（旧色在纸面上不可读）。
3. `project.godot`：`default_clear_color` 改为 Paper。
4. 各界面 `.cs` 不改；`.tscn` 中的 `node_paths` 与 NodePath 随新树同步。

## 6. 验证

- `dotnet build` 通过；`dotnet test Tests\kemo_card.Ui.Tests` 全绿（尤其 `LocaleIntegrityTests`：不新增翻译键，不引用不存在的键）。
- Godot 以 `--headless --import` 导入无场景解析错误；每个重做场景用 `--headless` 加载 PackedScene 并 `Instantiate()`，断言所有 `node_paths` 解析非 null。
- 编辑器内逐页目测 1280×720 与 1920×1080（expand 拉伸）无溢出、无重叠。

## 7. 后置项

- 主题热切换与玩家侧主题选择（仍为单主题资源）。
- 战斗正式界面在 `RunMainWin` 卡桌面上的落位。
- 图标资源（导航/分页箭头）——本次按用户决定不新增。
