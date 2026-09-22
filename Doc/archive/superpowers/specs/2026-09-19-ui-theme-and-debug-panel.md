# UI 主题统一与 Run 调试面板 设计

**日期**：2026-09-19
**状态**：已实装；**§1–§2 的调色板与样式描述已被 [2026-09-21 羊皮纸重构规格](../../../superpowers/specs/2026-09-21-ui-parchment-redesign-design.md) 取代**（主题资源、`KemoPalette`、页面布局以新规格为准；§3–§7 仍有效）
**关系**：服从 [2026-05-11 总规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md) 与 [2026-05-15 UI 管理器规格](../../../superpowers/specs/2026-05-15-ui-manager-design.md)；调色板与按钮基类约束全部 Run 内界面。

---

## 1. 全局调色板（KemoPalette）

`Src/mod/global/Ui/Themes/KemoTheme.cs::KemoPalette`：深蓝夜色底 + 琥珀金点缀的唯一色源（**代码侧**）。

- 分组：底色/面板（WindowBg / PanelBg / PanelBorder）、普通按钮五态、强调色五态（Accent 琥珀金系）、文本五色、输入与选中。
- **约束**：所有 UI 颜色只允许从这里取，禁止在场景/代码里内联魔法色值（`RunDebugDlg` 的调试专用 Owned/Error/Ok 三色是唯一豁免——调试面板不追求主题一致）。
- 分工：**场景侧样式**（StyleBox / 字体色）写进主题资源；`KemoPalette` 供**代码侧动态着色**（如 `SettingToggleRow` 的开关轨道）与资源维护时对照。`WindowBg` 同时是 `project.godot` 的默认清屏色。

## 2. 全局主题资源（kemo_theme.tres）

**主题是资源文件，不在运行时动态构建**（2026-09-19 调整）：

- 资源路径：`Resource/Asset/Theme/kemo_theme.tres`（`Theme` 资源，StyleBox 全部以 `SubResource` 内联，无外部依赖）。
- 挂载方式：`project.godot` 的 `[gui] theme/custom="res://Resource/Asset/Theme/kemo_theme.tres"`——引擎启动即作用于**所有** Control（含弹窗、PopupMenu、Tooltip），代码不再调用 `GetWindow().Theme = ...`。
- `KemoTheme` 只剩两个常量：`ResourcePath`（样式唯一事实源）与 `PrimaryVariation`；改样式只改资源，`KemoTheme.Build()` 与 StyleBox 工厂已删除。
- 覆盖控件：Button（含 `Primary` TypeVariation 主按钮变体，`base_type = Button`）、Label、Panel、LineEdit、TabContainer、ItemList、Slider、PopupMenu、Tooltip。
- 默认字号 16；圆角 6、内容边距 (18,10) 是按钮的统一节奏；页签只圆上沿（下沿半径 0）、TabContainer/Panel 面板不设显式内容边距（沿用引擎自算）。
- `project.godot` 显示基线：1280×720 viewport、`canvas_items` stretch、`expand` aspect。

## 3. BaseKemoButton 重构

`Src/mod/global/Ui/Comp/BaseKemoButton.cs`：项目按钮基类，**所有界面按钮必须挂载**（原生 Button 一律禁止）。

- 动效：悬停缩放（Back 缓动过冲）+ 提亮、按压缩放（短于悬停）、禁用降不透明度；全部可经 Export 关闭/调参。
- 主按钮：勾选 `Primary` 应用主题资源里的琥珀金变体（`KemoTheme.PrimaryVariation` = 资源中的同名 TypeVariation）。
- 词条提示：`KeywordIds` 导出数组 + `KeywordCatalog`，悬停延迟（`TipDelaySec`）后弹出 Tip 组件。
- **订阅生命周期**：全部订阅经 `BindingScope` 登记（ui-mod-binding 规格 §4.3），且挂在 `_EnterTree` 而非 `_Ready`——界面进缓存走 `RemoveChild`，重开时 `AddChild` 不会再触发 `_Ready`（缓存重开后悬停/词条不失效的关键）。

## 4. EElement 精简：阴阳移除

`ContentEnums.cs::EElement` 收敛为四色 `[Flags]`：`Red=1 / Blue=2 / Green=4 / Yellow=8`（`None=0`）。

- 移除旧设计的阴/阳二分；内容 JSON（`Config/mods/base-game/content/**`）与连携统计、`targetFilter.elementAny` 筛选同步四色。
- 该变更与连携系统（[buff 运行时规格 §3](../../../superpowers/specs/2026-09-19-buff-potential-chain-system-design.md)）同批落地。

## 5. Run 调试面板（RunDebugDlg + RunDebugService）

- `RunDebugDlg`（`EUILayer.Debug` 顶层，`CacheTime = 0`）：Tab 分组——卡牌（取卡/入卡组/回收）、角色（入池/上阵/一键满编）、战斗（开战/胜/负/充能球授予与触发）、事件（触发）、通用（金币/环数/阶段）、潜能（入账池/入账槽/解锁下一条被动/返还最近一笔/战斗检查）。
- **语义全部在 `RunDebugService`**（可单测、不依赖 Godot）；对话框只做取值 + 显示 + 收集参数。调试操作走正式管线（潜能入账 → `Potential.Grant`，解锁 → `TryUnlock`），不走旁路。
- 入口按钮在 `RunMainWin`：**仅 `OS.IsDebugBuild()` 显示**（release 隐藏，不留死按钮）。
- 语义测试见 `Tests/.../Run/RunDebugServiceTests.cs`（含重复角色转化为潜能、精确数额入账、整笔返还、战斗检查输出）。

## 6. 本地化完整性守卫（同批修复）

`Tests/.../LocaleIntegrityTests.cs` 防回归（由 CSV 追加行未换行导致"前一行吞键"的事故催生）：

- 两份 CSV（`Resource/Locale/strings.csv` UI 键 + `Config/mods/base-game/content/translations/strings.csv` 内容键）结构校验：列数 ≥3、键非空唯一、zh/en 双语非空、文件以换行收尾。
- 引用完整性：场景 `text = "KEY"` 与代码 `Tr("KEY")` 的全大写键 → Resource CSV；`BuiltinKeywords` 词条标题/描述键 → Resource CSV；base-game 内容 JSON 的 `displayNameId/descId/artistNameId/textId/labelId` → mod CSV。
- `.translation` 二进制由 Godot 导入管线从 CSV 重新编译，不得手改。

## 7. 明确后置项

- 战斗界面正式 UI（槽位 buff 图标、连携档位显示；`BuffContainer.Visible` 已预留展示集）。
- 主题热切换 / 玩家侧主题设置（当前为单主题资源写死；多主题时按 `KemoTheme.ResourcePath` 的同类资源切换即可）。
- 主题资源的 UID：当前以路径引用（`gui/theme/custom`），编辑器首次保存会自动补 `uid`，不需要手工写。
