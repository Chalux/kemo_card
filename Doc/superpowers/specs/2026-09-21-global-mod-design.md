# Global 功能规格（主菜单 · 图鉴 · 详情 · 设置 · 词典 · 界面主题）

**日期**：2026-09-21
**最后修订**：2026-09-21（新建：Global 侧下级规格合并；同日按归档后路径重写）
**状态**：已实装
**关系**：服从 [总规格](./2026-05-11-kemo-card-design.md) 与 [UI 与运行时规格](./2026-05-15-ui-manager-design.md)；界面归属与订阅生命周期以后者为准。

---

## 本文承载的下级规格（2026-09-21 合并并归档）

本文是 **Global 功能 Mod**（`Src/mod/global/`，`GlobalMod.FeatureId = "global"`）的**唯一权威文档**。原分散在 5 份下级规格里的 Global 侧内容已整篇合并进来，原件已归档到 `Doc/archive/superpowers/specs/`。

| 原规格 | 归档路径 | 并入本文 |
|---|---|---|
| `2026-09-21-ui-parchment-redesign-design.md` | `Doc/archive/superpowers/specs/2026-09-21-ui-parchment-redesign-design.md` | **§3**（整篇，原 §1–§7）——主题/布局的**现行权威** |
| `2026-09-19-ui-theme-and-debug-panel.md` | `Doc/archive/superpowers/specs/2026-09-19-ui-theme-and-debug-panel.md` | **§4**（原 §1–§2，**已被取代**）、**§5**（原 §3）、**§6**（原 §4）、**§7**（原 §5）、**§8**（原 §6）、**§9**（原 §7） |
| `2026-08-04-toast-component-design.md` | `Doc/archive/superpowers/specs/2026-08-04-toast-component-design.md` | **§10**（整篇，原「概述」+ §1–§8） |
| `2026-09-21-pause-menu-and-glossary-design.md` | `Doc/archive/superpowers/specs/2026-09-21-pause-menu-and-glossary-design.md` | **§11**（仅**词典**章节：原 §3 / §4 / §5 整篇并入） |
| `2026-09-21-reinhardt-and-normal-attack-extensions.md` | `Doc/archive/superpowers/specs/2026-09-21-reinhardt-and-normal-attack-extensions.md` | **§12**（仅**原 §12.2 的 UI 部分**） |

> **段号保真**：被并入的章节一律保留**原段号**（形如「§3.2 调色板（原 §2）」）。原段号 → 本文段号的完整对照见 **§13 段号索引**。`Src/` 与 `Doc/` 里存在按段号引用规格的注释（例如 `2026-09-19-ui-theme-and-debug-panel.md` 的 §3 / §5 / §6 仍被 AGENT 与其它文档引用），因此可定位性必须保住。

> ESC 系统菜单部分归 [Run 规格](./2026-06-22-run-mod-design.md)。

---

## 1. 本文定位与 Global 功能边界

Global 功能 Mod 是**启动期常驻、跨越所有 Run 会话**的那一半：主菜单、图鉴、卡牌/角色详情、设置、通用 Alert、词典、Toast、词条提示，以及被所有界面复用的**主题资源与按钮基类**。Run 侧的界面（选故事、Run 主界面、队伍编辑、卡组编辑、ESC 系统菜单）与调试面板**语义层**不在本文的界面上，归 [Run 规格](./2026-06-22-run-mod-design.md)。

### 1.1 代码归属

```
Src/mod/global/
├── GlobalMod.cs                  # Global 功能 Mod（BaseMod；FeatureId = "global"）+ 界面注册声明
├── GlobalModController.cs        # 门面：存档读写、设置项、解锁、各界面静态打开入口
├── Condition/                    # 全局条件域
├── Def/                          # 全局定义（CharacterDto 等）
├── Events/                       # Global 侧事件
├── Glossary/                     # 词典语义层（GlossaryBuilder 等，可单测）
├── Save/                         # 全局存档（GlobalSaveService / GlobalSaveDto）
└── Ui/                           # 全部界面（见 §2 界面清单）
    ├── Themes/KemoTheme.cs       # 色板代码镜像 + 主题资源路径
    ├── Comp/                     # 复用组件：BaseKemoButton / BaseCardItem / BaseCharacterItem /
    │                             #   BaseDlgComp / BasePager / Setting*Row / CharacterPresenter
    ├── Tip/                      # 词条提示层（KeywordTipLayer.tscn / KeywordTipService / KeywordTipPanel）
    └── Toast/                    # Toast 通用组件（ToastService / ToastItem）
```

> 界面命名与路径由 `GlobalUiIds`（`Src/mod/global/Ui/GlobalUiIds.cs`）与各界面自己的 `UIDir` 决定，**不另立第二套命名**。

### 1.2 与其它规格的关系

- **界面归属与订阅生命周期**：[UI 与运行时规格](./2026-05-15-ui-manager-design.md)（§13 场景约定、§13.2 `node_paths`）与 [ui-mod-binding 规格](./2026-05-15-ui-manager-design.md)（UI 与运行时规格 §14）为上层约束，本文只写 Global 侧的**具体值**。
- **玩法数据**：卡牌/角色/充能球等定义来自内容管线（[内容 Mod 规格](./2026-05-17-content-mod-manager-design.md)），Global 界面只**读**不**写**。
- **Run 侧界面**：[Run 规格](./2026-06-22-run-mod-design.md)；ESC 系统菜单、调试面板归属 Run，本文仅在调用点出现时给出入口名。
- **UI 框架**：本文的全部视觉改造**不改 UI 代码框架**（`Src/frame/ui` 一行不动），见 §3.1。

---

## 2. 界面清单（权威表）

本节由代码逐一核对生成，是「Global 有哪些界面、谁归属、从哪开、缓存多久」的**唯一权威表**。核对依据（文件:行）：

| 依据 | 位置 |
|---|---|
| 功能 Mod 登记入口 | `Src/mod/FeatureModCatalog.cs:30-34`（`FeatureModCatalog.Features`，逐行 `new(GlobalMod.FeatureId, GlobalMod.GetUIRegistrations)`） |
| Global 界面注册声明 | `Src/mod/global/GlobalMod.cs:44-56`（`GlobalMod.GetUIRegistrations()`） |
| 界面 id 常量 | `Src/mod/global/Ui/GlobalUiIds.cs:5-13` |
| 场景路径规则 | `Src/frame/ui/UIRuntimeRegistry.cs:32` 与 `Src/frame/ui/Base/BaseUI.cs:47`：`res://{Dir}/{Id}.tscn` |
| 默认打开参数 | `Src/frame/ui/Def/DefaultUIOpenOpt.cs:9-51`、`Src/frame/ui/Def/UIOpenOpt.cs:51,125` |
| 注册项类型 | `Src/frame/ui/UIRegistration.cs:24-62`（`Page` / `Dialog` / `Window` / `Popup`） |
| 门面解析 | `Src/frame/ui/IUiFacadeProvider.cs:12-19`；实现 `Src/mod/ModFactory.cs:32,65-70` |

### 2.1 权威表

| 界面 id | 类与场景路径 | 归属 Mod（`OwnerModId`） | 打开入口 | 缓存策略 | 备注 |
|---|---|---|---|---|---|
| `MenuWin` | `MenuWin : BaseWin`<br>`res://Src/mod/global/Ui/MenuWin.tscn` | `global` | `GlobalModController.OpenMenuAsync()`（`GlobalModController.cs:86`）；启动期由 `MainRoot` 调一次（`Src/MainRoot.cs:33`） | 默认缓存 30000 ms；`EUIType.Win` → `Layer = Win`、`Align = Full`、`HideBelow = true` | 注册为 `UIRegistration.Window`（`GlobalMod.cs:46`）；按钮：新游戏 / 继续 / 设置 / 图鉴 / 退出（`MenuWin.cs:19-45`）。**当前无词典入口** |
| `CodexDlg` | `CodexDlg : BaseDlg`<br>`res://Src/mod/global/Ui/CodexDlg.tscn` | `global` | `GlobalModController.OpenCodexAsync()`（`GlobalModController.cs:91`）；调用点 `MenuWin.cs:38`、`RunPauseDlg.cs:40` | 默认缓存 30000 ms；`EUIType.Dlg` → `Layer = Dlg`、`Align = Center`、`AnimType = SkipReOpen` | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:48`）。页签页：卡牌页 = 左筛选栏 + 4×2 卡面 + `BasePager`；角色页 = `BaseCharacterItem` 网格（见 §3.4.2） |
| `CardDetailsDlg` | `CardDetailsDlg : BaseDlg`<br>`res://Src/mod/global/Ui/CardDetailsDlg.tscn` | `global` | `GlobalModController.OpenCardDetailsAsync(string cardId, int? displayValue = null)`（`GlobalModController.cs:120`）；另有组件级自开：`BaseCardItem.cs:202` | 默认缓存 30000 ms | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:49`）。载荷 `CardDetailsDlgPayload { CardId, DisplayValue }`；调用点 `RunCharacterDeckDlg.cs:268` |
| `CharacterDetailsDlg` | `CharacterDetailsDlg : BaseDlg`<br>`res://Src/mod/global/Ui/CharacterDetailsDlg.tscn` | `global` | **无 Controller 入口**：由组件 `BaseCharacterItem` 直接 `UIManager.OpenAsync(UiId<CharacterDetailsDlgPayload>(GlobalUiIds.CharacterDetails), …)`（`Comp/BaseCharacterItem.cs:130-135`） | 默认缓存 30000 ms | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:50`）。载荷 `CharacterDetailsDlgPayload { CharacterId }`；专属卡牌区见 §12 |
| `SettingDlg` | `SettingDlg : BaseDlg`<br>`res://Src/mod/global/Ui/SettingDlg.tscn` | `global` | `GlobalModController.OpenSettingAsync()`（`GlobalModController.cs:96`）；调用点 `MenuWin.cs:33`、`RunPauseDlg.cs:39` | 默认缓存 30000 ms | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:51`）。写入走 `GlobalModController.SetSetting(key, value)`；行组件 `SettingToggleRow` / `SettingSliderRow` / `SettingDropdownRow` |
| `AlertDlg` | `AlertDlg : BaseDlg`<br>`res://Src/mod/global/Ui/AlertDlg.tscn` | `global` | `GlobalModController.OpenAlertAsync(AlertDlgPayload payload)`（`GlobalModController.cs:110`）；调用点 `RunPauseDlg.cs:83`、`RunMainWin.cs:263`、`SettingDlg.cs:450` | 默认缓存 30000 ms | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:52`）。`AlertDlgPayload`：`TitleKey` / `DescKey` / `OkTextKey` / `CancelTextKey` / `OkCallback` / `CancelCallback` / `CallbackWhenClose` / `Time`（秒，≤0 = 无倒计时） |
| `GlossaryDlg` | `GlossaryDlg : BaseDlg`<br>`res://Src/mod/global/Ui/GlossaryDlg.tscn` | `global` | `GlobalModController.OpenGlossaryAsync()`（`GlobalModController.cs:104`）；调用点 `RunPauseDlg.cs:41` | 默认缓存（`GlobalMod.cs:54` 注释明确保留默认缓存） | 注册为 `UIRegistration.Dialog`（`GlobalMod.cs:55`）。无载荷；内容在 `OnOpen` 时由 `GlossaryBuilder.Build(store)` 现算（见 §11） |

**缓存策略的统一口径**：`GlobalMod.GetUIRegistrations()` 的 7 条注册**没有任何一条设置 `UIRegistration.OpenOpt`**，因此全部落在 `DefaultUIOpenOpt.ForType(EUIType)` + `UIOpenOpt.DefaultCacheTime = 30000`（`UIOpenOpt.cs:51`、`DefaultUIOpenOpt.cs:12`）上：**关闭后保留 30 秒，超时销毁**；重开（`SkipReOpen`）复用缓存实例，界面生命周期方法照常按缓存语义重跑（订阅挂在 `_EnterTree`，见 §5）。对比 Run 侧：`RunMod` 为其界面显式设 `OpenOpt = new UIOpenOpt { CacheTime = 0 }`（`Src/mod/run/RunMod.cs:140-157`），Run 结束时另有 `UIManager.CloseByOwner(RunMod.FeatureId, destroy: true)`（`Src/mod/run/RunUiController.cs:92`、`Src/mod/run/RunRuntime.cs:75`）——Global 界面**不参与**这套销毁，跨 Run 常驻。

### 2.2 门面与打开入口

- 启动期由 `FeatureModCatalog.Features` 迭代注册（`FeatureModCatalog.cs:30-34`），`GlobalMod` 用 `GlobalMod.FeatureId` 作为 `OwnerModId`（`GlobalMod.cs:33,46-55`）。
- 门面解析：`ModStartupResult : IUiFacadeProvider` 按 `OwnerModId` 分发，`GlobalMod.FeatureId => GlobalController`（`Src/mod/ModFactory.cs:65-70`）；界面侧统一经 `BaseWin.Facade<TFacade>()` 取用，**不得直接访问 `AppRoot.Services`**（`IUiFacadeProvider.cs:10`）。
- 跨功能入口：Run 界面直接调 `GlobalModController` 的静态打开入口（对称地，`MenuWin`（Global）调 `RunUiController.OpenStorySelectAsync`），本轮不引入新的门面层（见 §11.1）。

### 2.3 未纳入注册表的界面件（存目）

以下构件随 Global 一起存在，但**不是** `UIRegistration`，因此不占上表一行：

| 构件 | 挂载方式 | 依据 |
|---|---|---|
| 词条提示层 `KeywordTipLayer.tscn` | 由根节点直接加为子节点（`EnsureKeywordTipLayer()`），不注册 UIId | `Src/MainRoot.cs:20,32`；组件 `Ui/Tip/KeywordTipService.cs` |
| Toast（`ToastService` / `ToastItem`） | 挂 `EUILayer.Notice`，`ToastService.Configure` 时自建容器 `AddChild`，**绕过 UIManager 状态机** | 见 §10.3 / §10.5 |
| 复用组件（`BaseKemoButton` / `BaseCardItem` / `BaseCharacterItem` / `BaseDlgComp` / `BasePager` / `Setting*Row` / `CharacterPresenter` / `VirtualList`） | 场景内实例化，不是独立界面 | 见 §3.4.4 |

---

## 3. UI 主题（羊皮纸）（原 2026-09-21 羊皮纸重构规格 §1–§7）

> 来源（原规格头部）：**日期** 2026-09-21；**状态** 已实装（**取代** `2026-09-19-ui-theme-and-debug-panel.md` §1–§2 的调色板与样式描述，即本文 §4；§3 BaseKemoButton、§5 调试面板、§6 本地化守卫不变，即本文 §5 / §7 / §8）；**关系** 服从 [UI 与运行时规格](./2026-05-15-ui-manager-design.md) 与 [ui-mod-binding 规格](./2026-05-15-ui-manager-design.md)（UI 与运行时规格 §14）。

### 3.1 目标与边界（原 §1）

- **推倒重来**：所有玩家可见界面的布局、按钮编排、配色全部重做；旧的「深蓝夜色 + 琥珀金」「居中面板 + 竖排按钮」不保留。
- **视觉方向**：**暖色卡桌·羊皮纸**——米白/暖灰纸面为底，墨绿（主操作）与酒红（危险/强调）点缀，深墨色 2px 描边与硬投影，像摊在桌上的实体卡牌与索引卡。
- **布局范式**：**全屏页面 + 左侧竖向导航栏（Rail）**：主流程页面（主菜单、Run 主界面、选故事、队伍编辑、卡组编辑）都是「左栏 + 右侧大内容区」；页签类型被代码绑定的页面（图鉴、设置）采用「顶部标题栏 + 活页夹式页签」，页签内部再用左栏承载筛选/表单；仅 Alert、卡牌/角色详情保留为居中小窗。
- **不做**：`RunDebugDlg`（规格明确不追求主题一致）；新增美术贴图/字体/SVG 图标（继续只用 `cancel.svg`）；任何界面 `.cs` 的逻辑改动（仅允许两处颜色常量与 NodePath 同步）。

### 3.2 调色板（原 §2）

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

### 3.3 主题资源（kemo_theme.tres）（原 §3）

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

### 3.4 页面解剖（原 §4）

#### 3.4.1 全屏页（Rail 范式）（原 §4.1）

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

> 表中 `RunMainWin` / `StorySelectDlg` / `RunTeamEditDlg` / `RunCharacterDeckDlg` 属 **Run 功能**（归 [Run 规格](./2026-06-22-run-mod-design.md)）：此处保留是为了让「Rail 范式」这一**全局视觉约定**可对照，不代表 Global 拥有这些界面。

#### 3.4.2 页签页（标题栏 + 活页夹）（原 §4.2）

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

#### 3.4.3 居中小窗（BaseDlgComp）（原 §4.3）

`BaseDlgComp` 重做为：`DlgBg`（`Panel` 卡片）+ 顶部 6px Wine 色带（`ColorRect`）+ 右上 `CloseBtn`（`IconClose` 变体，`cancel.svg`）。宿主子节点仍挂在 `BaseDlgComp` 下，NodePath 不变。

| 窗 | 布局 |
|---|---|
| `AlertDlg` | 560×300 居中；标题 26 左对齐、描述 18、底部按钮右对齐（取消 `Ghost`、确定 `Primary`） |
| `CardDetailsDlg` | 980×580；左 `BaseCardItem` 区；右：名字 30、Mod/作者 `Caption`、发丝线、描述 RichText 20 |
| `CharacterDetailsDlg` | 980×580；左 `CharacterPresenter`；右：名字 30、动画选择行（Caption + OptionButton）、发丝线、简介 RichText 18、被动 RichText 16 |

> `CharacterDetailsDlg` 的「简介」区已在同批变更中改为**专属卡牌区**（横向虚拟列表），以 §12 为准。

#### 3.4.4 组件（原 §4.4）

| 组件 | 重做要点 |
|---|---|
| `BaseCardItem` | `CRPlaceholder`/`CRBg` 两个 ColorRect 换成 `CardFace`（`Panel` 卡片）；`CArtRoot` 内缩 8px；费用/类型/数值 Label 走 Ink；`CRAttr` 元素圆点保留（shader 不变） |
| `BaseCharacterItem` | 同上卡面；`TName` 背后加 `Nameplate` 面板（Ink 底、Paper 字） |
| `BasePager` | 首/上/下/末四键 `Ghost` 变体 + 页码 Label + 页码输入(64) + 跳转键 |
| `Setting*Row` | 根节点 `HBoxContainer` → `PanelContainer(SettingRow)` + 内部 HBox，节点 NodePath 相应加一层 `HBox/` |
| `ToastItem` | Ink 底、左侧 4px Wine 边、Paper 字、radius 4 |
| `KeywordTipPanel` | Ink 底 2px Ink 边 radius 4；标题 Paper 16、描述 `#CDBFA6` 13 |

### 3.5 代码侧改动（受限清单）（原 §5）

1. `KemoPalette`：常量值改为 §2 色板（含新增 `SurfaceSunken` 供列表锁定项底色），文档注释同步；`KemoTheme` 不变。**（本文 §3.2）**
2. `StorySelectDlg.cs:85`：锁定故事的 `SetItemCustomBgColor` 内联色改为 `KemoPalette.SurfaceSunken`（旧色在纸面上不可读）。
3. `project.godot`：`default_clear_color` 改为 Paper。
4. 各界面 `.cs` 不改；`.tscn` 中的 `node_paths` 与 NodePath 随新树同步。

### 3.6 验证（原 §6）

- `dotnet build` 通过；`dotnet test Tests\kemo_card.Ui.Tests` 全绿（尤其 `LocaleIntegrityTests`：不新增翻译键，不引用不存在的键）。
- Godot 以 `--headless --import` 导入无场景解析错误；每个重做场景用 `--headless` 加载 PackedScene 并 `Instantiate()`，断言所有 `node_paths` 解析非 null。
- 编辑器内逐页目测 1280×720 与 1920×1080（expand 拉伸）无溢出、无重叠。

### 3.7 后置项（原 §7）

- 主题热切换与玩家侧主题选择（仍为单主题资源）。
- 战斗正式界面在 `RunMainWin` 卡桌面上的落位。
- 图标资源（导航/分页箭头）——本次按用户决定不新增。

---

## 4. UI 主题统一（历史实现：深蓝夜色 + 琥珀金）（原 2026-09-19 UI 主题规格 §1–§2）

> 2026-09-21：本节调色板与样式描述已被羊皮纸重构取代，现行权威见本文 §3.2（调色板）与 §3.3（主题资源）。
>
> 本节是**历史实现记录**，只用于回溯与段号定位（`KemoPalette` 的旧值、`kemo_theme.tres` 的旧覆盖清单）。**两套调色板不并列有效**：凡是 §4.1 / §4.2 与 §3.2 / §3.3 冲突之处，一律以 §3 为准。本规格其余部分（§5 BaseKemoButton、§7 调试面板、§8 本地化守卫）不受取代影响。

> 来源（原规格头部）：**日期** 2026-09-19；**状态** 已实装，其中 §1–§2 已被羊皮纸重构取代；**关系** 服从 [总规格](./2026-05-11-kemo-card-design.md) 与 [UI 与运行时规格](./2026-05-15-ui-manager-design.md)；调色板与按钮基类约束全部 Run 内界面。

### 4.1 全局调色板（KemoPalette）（原 2026-09-19 §1）

`Src/mod/global/Ui/Themes/KemoTheme.cs::KemoPalette`：深蓝夜色底 + 琥珀金点缀的唯一色源（**代码侧**）。

- 分组：底色/面板（WindowBg / PanelBg / PanelBorder）、普通按钮五态、强调色五态（Accent 琥珀金系）、文本五色、输入与选中。
- **约束**：所有 UI 颜色只允许从这里取，禁止在场景/代码里内联魔法色值（`RunDebugDlg` 的调试专用 Owned/Error/Ok 三色是唯一豁免——调试面板不追求主题一致）。
- 分工：**场景侧样式**（StyleBox / 字体色）写进主题资源；`KemoPalette` 供**代码侧动态着色**（如 `SettingToggleRow` 的开关轨道）与资源维护时对照。`WindowBg` 同时是 `project.godot` 的默认清屏色。

### 4.2 全局主题资源（kemo_theme.tres）（原 2026-09-19 §2）

**主题是资源文件，不在运行时动态构建**（2026-09-19 调整）：

- 资源路径：`Resource/Asset/Theme/kemo_theme.tres`（`Theme` 资源，StyleBox 全部以 `SubResource` 内联，无外部依赖）。
- 挂载方式：`project.godot` 的 `[gui] theme/custom="res://Resource/Asset/Theme/kemo_theme.tres"`——引擎启动即作用于**所有** Control（含弹窗、PopupMenu、Tooltip），代码不再调用 `GetWindow().Theme = ...`。
- `KemoTheme` 只剩两个常量：`ResourcePath`（样式唯一事实源）与 `PrimaryVariation`；改样式只改资源，`KemoTheme.Build()` 与 StyleBox 工厂已删除。
- 覆盖控件：Button（含 `Primary` TypeVariation 主按钮变体，`base_type = Button`）、Label、Panel、LineEdit、TabContainer、ItemList、Slider、PopupMenu、Tooltip。
- 默认字号 16；圆角 6、内容边距 (18,10) 是按钮的统一节奏；页签只圆上沿（下沿半径 0）、TabContainer/Panel 面板不设显式内容边距（沿用引擎自算）。
- `project.godot` 显示基线：1280×720 viewport、`canvas_items` stretch、`expand` aspect。

> 取代说明：上条「圆角 6、内容边距 (18,10)」「覆盖控件清单」均为**旧值**；现行值为按钮圆角 **4**、内容边距 (18,10)、卡片圆角 6，覆盖清单见 §3.3。

---

## 5. BaseKemoButton 重构（原 2026-09-19 §3）

`Src/mod/global/Ui/Comp/BaseKemoButton.cs`：项目按钮基类，**所有界面按钮必须挂载**（原生 Button 一律禁止）。

- 动效：悬停缩放（Back 缓动过冲）+ 提亮、按压缩放（短于悬停）、禁用降不透明度；全部可经 Export 关闭/调参。
- 主按钮：勾选 `Primary` 应用主题资源里的琥珀金变体（`KemoTheme.PrimaryVariation` = 资源中的同名 TypeVariation）。

> 2026-09-21：`PrimaryVariation` 指向的变体底色已由琥珀金改为 **Green**（见 §3.3）；`Primary` 的**语义**（主操作按钮）与本节其余描述不变。

- 词条提示：`KeywordIds` 导出数组 + `KeywordCatalog`，悬停延迟（`TipDelaySec`）后弹出 Tip 组件。
- **订阅生命周期**：全部订阅经 `BindingScope` 登记（ui-mod-binding 规格 §4.3 → [UI 与运行时规格 §14](./2026-05-15-ui-manager-design.md)），且挂在 `_EnterTree` 而非 `_Ready`——界面进缓存走 `RemoveChild`，重开时 `AddChild` 不会再触发 `_Ready`（缓存重开后悬停/词条不失效的关键）。

## 6. EElement 精简：阴阳移除（原 2026-09-19 §4）

`ContentEnums.cs::EElement` 收敛为四色 `[Flags]`：`Red=1 / Blue=2 / Green=4 / Yellow=8`（`None=0`）。

- 移除旧设计的阴/阳二分；内容 JSON（`Config/mods/base-game/content/**`）与连携统计、`targetFilter.elementAny` 筛选同步四色。
- 该变更与连携系统（[战斗规格 §13](./2026-07-21-combat-system-design.md)）同批落地。

> 归属说明：本条是**内容侧**规则，因原规格整篇合并而保留在本文；元素四色本身归内容规格与 `ColorDefinitions`，不在本文范围（见 §3.2 末句）。

## 7. Run 调试面板（RunDebugDlg + RunDebugService）（原 2026-09-19 §5）

- `RunDebugDlg`（`EUILayer.Debug` 顶层，`CacheTime = 0`）：Tab 分组——卡牌（取卡/入卡组/回收）、角色（入池/上阵/一键满编）、战斗（开战/胜/负/充能球授予与触发）、事件（触发）、通用（金币/环数/阶段）、潜能（入账池/入账槽/解锁下一条被动/返还最近一笔/战斗检查）。
- **语义全部在 `RunDebugService`**（可单测、不依赖 Godot）；对话框只做取值 + 显示 + 收集参数。调试操作走正式管线（潜能入账 → `Potential.Grant`，解锁 → `TryUnlock`），不走旁路。
- 入口按钮在 `RunMainWin`：**仅 `OS.IsDebugBuild()` 显示**（release 隐藏，不留死按钮）。
- 语义测试见 `Tests/.../Run/RunDebugServiceTests.cs`（含重复角色转化为潜能、精确数额入账、整笔返还、战斗检查输出）。

> 归属说明：`RunDebugDlg` 界面与 `RunDebugService` **归 Run 功能**（[Run 规格](./2026-06-22-run-mod-design.md)）；本条保留在本文是因为「调试面板明确豁免主题一致」是与主题规格成对的约束，`RunDebugDlg` 的取数也复用本文 §5 的按钮基类。

## 8. 本地化完整性守卫（同批修复）（原 2026-09-19 §6）

`Tests/.../LocaleIntegrityTests.cs` 防回归（由 CSV 追加行未换行导致"前一行吞键"的事故催生）：

- 两份 CSV（`Resource/Locale/strings.csv` UI 键 + `Config/mods/base-game/content/translations/strings.csv` 内容键）结构校验：列数 ≥3、键非空唯一、zh/en 双语非空、文件以换行收尾。
- 引用完整性：场景 `text = "KEY"` 与代码 `Tr("KEY")` 的全大写键 → Resource CSV；`BuiltinKeywords` 词条标题/描述键 → Resource CSV；base-game 内容 JSON 的 `displayNameId/descId/artistNameId/textId/labelId` → mod CSV。
- `.translation` 二进制由 Godot 导入管线从 CSV 重新编译，不得手改。

> 2026-09-21 追加：本轮另补一条 `Pause_and_glossary_literal_keys_exist_in_resource_csv`（覆盖「先存进常量/局部变量再翻译」的键），并入本文 §11.2。

## 9. 明确后置项（原 2026-09-19 §7）

- 战斗界面正式 UI（槽位 buff 图标、连携档位显示；`BuffContainer.Visible` 已预留展示集）。
- 主题热切换 / 玩家侧主题设置（当前为单主题资源写死；多主题时按 `KemoTheme.ResourcePath` 的同类资源切换即可）。
- 主题资源的 UID：当前以路径引用（`gui/theme/custom`），编辑器首次保存会自动补 `uid`，不需要手工写。

---

## 10. Toast 通用组件（原 2026-08-04 Toast 组件规格「概述」+§1–§8）

> 来源（原规格头部）：本文档原文无日期/状态头；代码归属 `Src/mod/global/Ui/Toast/`。本组件由 Global 功能承载（**不是** `UIRegistration` 界面，见 §2.3）。
> 本节内的 `§N` 均为**原规格段号**（本文 §10.1 起一一对应，见 §13 段号索引）；跨节引用处已附本文段号。

### 10.1 概述（原 概述）

提供全局轻量提示组件 `ToastService`，以公共入口拉起一条 Toast。Toast 以**纵向堆叠**方式展示在屏幕上方（约 25% 高度处），支持对象池复用、上移缓动 + 淡出动画。

> 本规格由「Run 存档闭环」任务（见 `2026-08-04-run-save-continue-design.md`）的 grilling 决议引出，定位为通用基础设施，供后续所有模块复用（保存成功、设置已应用等提示）。

### 10.2 目标与非目标（原 §1）

#### 目标

- 公共入口 `ToastService.Show(textKey)`，任何模块可拉起 Toast。
- 纵向堆叠 Toast 列表，显示在屏幕上方约 25% 位置（**不在正中间**）。
- 对象池管理：复用 Toast 实例，避免反复实例化/销毁。
- 动画：**瞬间弹出（无淡入）→ 停留 1 秒 → 上移 100px 并同时淡出 2000ms**。
- 文案走翻译键（`Resource/Locale/strings.csv`）。

#### 非目标

- 不承担「多行文本输入 / 按钮交互」等复杂内容——那是 Alert/弹窗的职责。
- 不做 Toast 与 UIManager 缓存机制的深度集成（见 §4 挂载方式）。**（本文 §10.5）**
- 不做点击穿透控制之外的额外输入处理。

### 10.3 架构（原 §2）

```
ToastService（静态门面）
├── Configure(Control noticeLayer)        # 启动时注入 Notice 层
├── Show(string textKey)                  # 公共入口
└── 内部：
    ├── _pool: Stack<ToastItem>           # 空闲对象池
    ├── _active: List<ToastItem>          # 活跃列表（含堆叠顺序）
    └── _container: VBoxContainer         # 堆叠容器（置于 Notice 层内）
```

#### 核心类型

| 类型 | 位置 | 职责 |
|------|------|------|
| `ToastService` | `Src/mod/global/Ui/Toast/` | 静态门面：池管理、堆叠、生命周期调度 |
| `ToastItem` | `Src/mod/global/Ui/Toast/` | 单条 Toast 节点（Control + 场景），负责自身动画 |

#### 挂载层级

- Toast 挂载到 **`EUILayer.Notice`**（`MainRoot` 已注册为 TopLayer，不受 `HideBelow` 影响，始终可见）。
- `UILayer` 是 `Control`，ToastService 直接 `AddChild` 一个 `VBoxContainer` 容器，**不经过 UIManager 的 Open/Close 状态机**（原因见 §4）。**（本文 §10.5）**

### 10.4 ToastItem（原 §3）

#### 场景结构（`ToastItem.tscn`）

```
ToastItem (Control)
└── Panel
    └── Label
```

- 布局在场景中定义，代码只写逻辑（项目硬性约定）。
- `Label.Text` 由 `Show()` 时从翻译键填充。

#### 动画参数（常量）

| 参数 | 值 | 说明 |
|------|-----|------|
| 入场 | 无 | 瞬间显示，无淡入 |
| 停留时长 | 1000 ms | 显示期间静止 |
| 上移距离 | 100 px | 同时进行 |
| 淡出时长 | 2000 ms | 同时进行 |

- 使用 Godot `Tween`；上移与淡出并行。
- 退场完成后回调 `OnRecycle`，ToastService 将实例回池。

### 10.5 与 UIManager 的关系（原 §4）

#### 为什么不经 UIManager

`UIVoRegistry` 以 UIId 为键维护**单实例**（`GetOrCreate` 返回既有 vo），且 `UILayer.AddUI` 也按 UIId 单实例。Toast 需要**同屏多实例纵向堆叠**，与 UIManager 的单实例模型冲突。

因此 Toast 走独立的轻量容器：

- ToastService 在 `Configure` 时创建 `VBoxContainer` 并 `AddChild` 到 Notice 层。
- 容器锚点：屏幕上 25% 高度、水平居中（`anchors_preset` 在场景或代码设置）。
- 每次 `Show`：从池取（或实例化）ToastItem → 设置文案 → `AddChild` 到容器末尾 → 播放动画 → 完成回池。

#### 生命周期

- Toast 不参与 UIManager 状态机，池化由 ToastService 自管。
- 项目关闭时随场景树整体销毁，无需额外清理。

### 10.6 对象池策略（原 §5）

- 空闲池 `Stack<ToastItem>`：动画完成后 `QueueFree` 前先入池（`Reparent` 到一个隐藏 holder 或 `RemoveChild` 保留节点）。
- 最大池容量（默认 4，可配）：超过上限的回收实例直接销毁。
- 频繁 `Show` 时若池空则新建；瞬时堆叠上限不设死，但视觉上以容器高度为准。

### 10.7 公共入口（原 §6）

```csharp
ToastService.Show("UI_RUN_SAVED");                          // 无参
ToastService.Show("UI_RUN_SAVED_AT", "第 3 环", "事件");    // 格式化占位符 {0} {1}
```

- `Show(string textKey, params string?[]? args)`：`args` 传入时以 `string.Format` 格式化翻译串占位符；无参时直接 `Localization.Tr`（避免含字面量 `{` 的翻译串被 Format 误解析）。
- 幂等、线程安全不做要求（UI 主线程调用）。
- 连续多次 `Show`：每次追加到堆叠容器末尾，已有的 Toast 继续向上淡出（自然让位）。

### 10.8 与 Run 存档闭环的对接（原 §7）

- RunMainWin「保存」按钮点击后调用 `ToastService.Show("UI_RUN_SAVED")`。
- 「保存并返回主菜单」**不弹 Toast**（回主菜单即是最佳反馈，grilling 决议）。
- 「快速读取」失败时 `ToastService.Show("UI_RUN_LOAD_FAILED")`。

### 10.9 待实现确认点（原 §8）

- 容器 25% 定位在 `ToastItem.tscn` 的容器节点上设置，还是 ToastService 运行时设置（二选一，实现计划定）。
- 池最大容量常量值（默认 4）。

---

## 11. 词典（Glossary）（原 2026-09-21 ESC 系统菜单 + 词典规格 §3–§5）

> ESC 系统菜单部分归 [Run 规格](./2026-06-22-run-mod-design.md)。
>
> 来源（原规格头部）：**日期** 2026-09-21；**状态** 已实装；**关系** 服从[总规格](./2026-05-11-kemo-card-design.md)与 [UI 与运行时规格](./2026-05-15-ui-manager-design.md)（§13 场景约定、§13.2 `node_paths`）；界面归属与订阅生命周期见 [ui-mod-binding 规格](./2026-05-15-ui-manager-design.md)（UI 与运行时规格 §14）；视觉沿用本文 §3（羊皮纸）与 §4（历史主题记录）。
> 本节只并入**词典**章节（原 §3 / §4 / §5）。原规格的 §1 目标、§2 ESC 系统菜单（`RunPauseDlg`）**不并入本文**，存目见 §14。

### 11.1 词典（`GlossaryDlg`，归属 Global 功能）（原 §3）

- **数据来源两处**：
  1. **机制词条**：`KeywordCatalog`（本轮新增 `Entries` 枚举，按**注册顺序**输出）。词条本身是既有设施（卡面 `[url=kw:*]` 提示复用同一份），词典只是把它们列出来。
  2. **内容侧玩法元素**：充能球类型（`GameDefinitionStore.OrbTypes`，按 id 字典序），正文按伤害类型/是否纯效果球取不同的文案键。
- **分层**（沿用"语义层可单测、界面只渲染"的约定）：

| 层 | 职责 | 测试 |
| --- | --- | --- |
| `GlossaryBuilder`（`Src/mod/global/Glossary/`） | 把词条表 + 充能球拼成分组；只产出**翻译键 + 形式参数**，不产出成品文案 | `GlossaryBuilderTests`（6 例） |
| `GlossaryView`（`Src/mod/global/Ui/`） | 搜索过滤（标题或正文命中）、空分组不出标题、按 id 定位与回落选中 | `GlossaryViewTests`（6 例） |
| `GlossaryDlg` | 渲染成 `ItemList` + `RichTextLabel`，翻译与跳转 | Godot 探针（接线 + 文案） |

- **只带键不带文案**：一是切语言不必重建模型，二是模型可在无引擎环境下单测。键查不到时 `TranslationServer.Translate` 原样返回键，因此 `TitleKey` 允许直接塞内容 id（没有显示名的球）。
- **正文跳转**：正文里的 `[url=kw:<id>]` 与卡面描述同一格式（解析复用 `CardDescBuilder.TryParseKeywordMeta`），点击后跳词典内对应条目；目标被当前搜索挡住时先清空搜索再定位。
- **新增词条**（`BuiltinKeywords`）：`normal_attack`（普通攻击）、`orb`（充能球）、`orb_element`（属性球）、`shared_hp`（共享血量）、`team_potential`（团体潜能）、`character_passive`（角色被动）。这些机制此前没有任何界面入口。
- **入口**：Run 的 ESC 菜单；`GlobalModController.OpenGlossaryAsync()` 是唯一打开入口（主菜单后续要加入口时直接复用）。

### 11.2 文案与导入（原 §4）

- 新增键写在 `Resource/Locale/strings.csv`（33 行：`UI_PAUSE_*`、`UI_GLOSSARY_*`、`KW_*`）。
- **CSV 改动必须重新导入**才会进 `.translation`（`project.godot` 的 `locale/translations` 指向生成物）：
  `godot --headless --path . --import`。忘记这一步的表现是界面显示原始键名。
- 守卫：`LocaleIntegrityTests` 原有扫描覆盖 `Tr("KEY")` 与场景 `text = "KEY"`；本轮补一条
  `Pause_and_glossary_literal_keys_exist_in_resource_csv`，覆盖"先存进常量/局部变量再翻译"的键
  （`GlossaryBuilder` 的分组/正文键、`RunPauseDlg` 的禁用原因键），这类键原有扫描匹配不到。

### 11.3 已知边界（原 §5）

- ESC 在**任何** Run 界面之上都能开菜单（含图鉴/词典/详情弹窗之上），这是"随时"的字面语义；若要改成"先关最上层"，应走 `UIStack`/`BackAsync` 的导航栈语义，另案处理。
- 词典内容在**打开时**现算，运行中热更内容后重开即可看到新条目（无需重启）。
- 「退出到桌面」不做保存：它的语义就是退出，确认框负责告知代价；战斗外每次阶段推进已有自动保存兜底。

> 边界归属：本节前两条与词典/图鉴相关，保留在本文；第三条「退出到桌面」属 ESC 系统菜单（Run），存目见 §14。

---

## 12. 角色详细界面：专属卡牌区（原 2026-09-21 莱因哈特规格 §12.2 的 UI 部分）

> 来源（原规格）：`2026-09-21-reinhardt-and-normal-attack-extensions.md` §12.2「角色简介移除」。本节只并入其 **UI 部分**；`CharacterDto.descId` 删除属 **DTO/内容**事实，归内容规格，本文仅存目不展开（原句保留如下）。

`CharacterDto.descId` 删除（字段、内容、翻译行、图鉴文本检索、DTO 文档一并清理）。
**角色详细界面改为展示专属卡牌**：`CharacterDetailsDlg` 原简介区改列该角色 `cards` 中 `isExclusive: true` 的卡，
标题键 `UI_CHARACTER_CARDS_TITLE`（无专属卡时连标题一起收起）。

展示形态是**横向虚拟列表**（`VirtualList`，`IsVertical = false`、`ItemSize = 160`、`Spacing = 10`，
条目模板 `BaseCardItem.tscn`），条目尺寸由卡面预制体决定、列表不拉伸它。
单击卡面即打开既有的卡牌详情（`ECardClickAction.OpenDetails`），**不再把卡名与描述拼成文本**——
文字与卡面重复表达同一件事，且描述原文在卡牌详情里能读得更全。

### 12.1 实装值与代码依据

| 事实 | 值 | 依据（文件:行） |
|---|---|---|
| 列表方向 | `IsVertical = false`（横向滚动） | `Src/mod/global/Ui/CharacterDetailsDlg.tscn:163` |
| 步长 / 间距 | `ItemSize = 160.0`、`Spacing = 10.0` | 同上 `:161-162` |
| 条目模板 | `BaseCardItem.tscn`（`ItemTemplate = ExtResource("5_carditem")`，`ScrollArea = NodePath("Scroll")`） | 同上 `:159-160`、`:7` |
| 卡面尺寸固定 | 预制体 `custom_minimum_size = Vector2(160, 208)`、`offset_right = 160.0`、`offset_bottom = 208.0` | `Src/mod/global/Ui/Comp/BaseCardItem.tscn:15,18-19` |
| **列表不拉伸条目** | 条目尺寸由预制体决定；`StretchItemAcrossAxis` 默认关闭，`PositionItem` **只设位置、不改尺寸** | `Src/mod/global/Ui/VirtualList.cs:27-36,49-56,523-566` |
| 数据来源 | 角色的 `Cards` 中 `store.TryGetCard(...) && card.IsExclusive` 才入列 | `Src/mod/global/Ui/CharacterDetailsDlg.cs:132-145` |
| 单击行为 | 渲染回调每次显式重置交互契约后设 `ClickAction = ECardClickAction.OpenDetails` + `EnableHoverTip = true` | `Src/mod/global/Ui/CharacterDetailsDlg.cs:147-161` |
| 打开卡牌详情 | `ECardClickAction.OpenDetails` → `BaseCardItem.TryOpenDetails()` → `UIManager.OpenAsync(UiId<CardDetailsDlgPayload>(GlobalUiIds.CardDetails), …)` | `Src/mod/global/Ui/Comp/BaseCardItem.cs:143-153,188-209` |
| 无专属卡时收起标题 | `_lblCardsCaption.Visible = _cardIds.Count > 0`（不显示空标题） | `Src/mod/global/Ui/CharacterDetailsDlg.cs:114-118` |
| 标题键 | 场景 `text = "UI_CHARACTER_CARDS_TITLE"` | `Src/mod/global/Ui/CharacterDetailsDlg.tscn:145` |

> 对象池提醒：列表条目是**复用**的（`VirtualList` 对象池），因此交互契约（`Clicked` / `LongPressed` / `EnableLongPress` / `ClickAction` / `EnableHoverTip`）必须**每次渲染显式重置**，不能让上一张牌留下的闭包或开关生效（`CharacterDetailsDlg.cs:154-159`）。

---

## 13. 段号索引（原段号 → 本文段号）

被并入的章节保留**原段号**，本表是唯一对照表。`Src/` 与 `Doc/` 中按段号引用原规格的注释（尤其 `2026-09-19-ui-theme-and-debug-panel.md` §3 / §5 / §6）按本表定位。

### 13.1 `2026-09-19-ui-theme-and-debug-panel.md`

| 原段号 | 原标题 | 本文段号 | 有效性 |
|---|---|---|---|
| 头部（日期 / 状态 / 关系） | — | §4 引子 | 已取代（仅存目） |
| §1 | 全局调色板（KemoPalette） | **§4.1** | **已取代** → §3.2 |
| §2 | 全局主题资源（kemo_theme.tres） | **§4.2** | **已取代** → §3.3 |
| §3 | BaseKemoButton 重构 | **§5** | 有效 |
| §4 | EElement 精简：阴阳移除 | **§6** | 有效（内容侧，见节内归属说明） |
| §5 | Run 调试面板（RunDebugDlg + RunDebugService） | **§7** | 有效 |
| §6 | 本地化完整性守卫（同批修复） | **§8** | 有效 |
| §7 | 明确后置项 | **§9** | 有效 |

### 13.2 `2026-09-21-ui-parchment-redesign-design.md`（主题/布局现行权威）

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| 头部（日期 / 状态 / 关系） | — | §3 引子 |
| §1 | 目标与边界 | **§3.1** |
| §2 | 调色板（唯一色源） | **§3.2** |
| §3 | 主题资源（kemo_theme.tres） | **§3.3** |
| §4 | 页面解剖 | **§3.4** |
| §4.1 | 全屏页（Rail 范式） | **§3.4.1** |
| §4.2 | 页签页（标题栏 + 活页夹） | **§3.4.2** |
| §4.3 | 居中小窗（BaseDlgComp） | **§3.4.3** |
| §4.4 | 组件 | **§3.4.4** |
| §5 | 代码侧改动（受限清单） | **§3.5** |
| §6 | 验证 | **§3.6** |
| §7 | 后置项 | **§3.7** |

### 13.3 `2026-08-04-toast-component-design.md`

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| 概述 | 概述 | **§10.1** |
| §1 | 目标与非目标 | **§10.2** |
| §2 | 架构 | **§10.3** |
| §3 | ToastItem | **§10.4** |
| §4 | 与 UIManager 的关系 | **§10.5** |
| §5 | 对象池策略 | **§10.6** |
| §6 | 公共入口 | **§10.7** |
| §7 | 与 Run 存档闭环的对接 | **§10.8** |
| §8 | 待实现确认点 | **§10.9** |

### 13.4 `2026-09-21-pause-menu-and-glossary-design.md`（仅词典章节）

| 原段号 | 原标题 | 本文段号 | 备注 |
|---|---|---|---|
| 头部（日期 / 状态 / 关系） | — | §11 引子 | 视觉引用改指本文 §3 / §4 |
| §1 | 目标 | — | **未并入**（含 ESC 目标），见 §14 |
| §2 | ESC 系统菜单（`RunPauseDlg`，归属 Run 功能） | — | **未并入**，归 [Run 规格](./2026-06-22-run-mod-design.md)，见 §14 |
| §3 | 词典（`GlossaryDlg`，归属 Global 功能） | **§11.1** | 整篇并入 |
| §4 | 文案与导入 | **§11.2** | 整篇并入（ESC 相关键条目保留） |
| §5 | 已知边界 | **§11.3** | 整篇并入；第三条属 ESC，见 §14 |

### 13.5 `2026-09-21-reinhardt-and-normal-attack-extensions.md`（仅 §12.2 的 UI 部分）

| 原段号 | 原标题 | 本文段号 | 备注 |
|---|---|---|---|
| §12.2 | 角色简介移除 | **§12** | 只并入 **UI 部分**；`CharacterDto.descId` 删除的 DTO 事实归内容规格（原句在 §12 存目） |
| §12.1 / §1–§11 / §13 | 稀有度档名 / 交付总览 / 后置项等 | — | **未并入**（非 Global 侧），见 §14 |

> 本文自有段号（§1 定位与边界、§2 界面清单、§13 段号索引、§14 未并入存目）为 2026-09-21 新建，无原段号。

---

## 14. 未并入本文的内容（存目）

以下内容**故意未并入**本文，避免在 Global 规格里出现第二份权威（替代去向已在表中给出）：

| 未并入内容 | 出处 | 理由 / 去向 |
|---|---|---|
| `RunPauseDlg`（ESC 系统菜单）的全部规则：输入捕获、开/关判据、注册项、按钮表、战斗阶段禁用、跨功能调用 | `2026-09-21-pause-menu-and-glossary-design.md` §1–§2 | 归 **Run 功能**（ESC 系统菜单），由 [Run 规格](./2026-06-22-run-mod-design.md) 处理。本文只保留它的**调用点**（`RunPauseDlg.cs:39-41,83` 调 `GlobalModController.OpenSettingAsync / OpenCodexAsync / OpenGlossaryAsync / OpenAlertAsync`） |
| 「退出到桌面」不做保存的语义 | `2026-09-21-pause-menu-and-glossary-design.md` §5 第三条 | 同上，属 ESC 系统菜单 |
| `2026-09-21-reinhardt-and-normal-attack-extensions.md` §1–§11、§12.1、§13 | 同源规格 | 属内容/战斗/潜能侧（种族收敛、普攻扩展、稀有度档名、莱因哈特/巴赫套件等），不在 Global 范围 |
| `CharacterDto.descId` 的 DTO 删除细节 | 同上 §12.2 | DTO 事实归内容规格；本文 §12 仅存原句与界面侧影响 |
| Toast 与 Alert 的颜色值再描述 | 各源规格 | 不重复：Toast/Alert 配色以 §3.3 / §3.4.4 为唯一值源 |

