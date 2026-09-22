# ESC 系统菜单 + 词典 设计

**日期**：2026-09-21
**状态**：已实装
**关系**：服从[总规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md)与 [UI 管理器规格](../../../superpowers/specs/2026-05-15-ui-manager-design.md)（§13 场景约定、§13.2 `node_paths`）；界面归属与订阅生命周期见 [ui-mod-binding 规格](../../../superpowers/specs/2026-09-15-ui-mod-binding-design.md)；视觉沿用 [UI 主题规格](../../../superpowers/specs/2026-09-19-ui-theme-and-debug-panel.md) 与[羊皮纸重设计](../../../superpowers/specs/2026-09-21-ui-parchment-redesign-design.md)。

---

## 1. 目标

1. **Run 内随时按 ESC** 打开一个系统菜单；再按一次关闭（切换语义，不做"按 ESC 一定关掉最上层"的隐式栈操作）。
2. 菜单提供五个入口：**打开设置 / 打开图鉴 / 打开词典 / 保存并退出到主菜单 / 退出到桌面**。
3. **词典**：把关键词（充能、属性球、共享血量、团体潜能……）汇总成可查阅的界面——这些机制此前只存在于代码与规则里，玩家没有任何入口读到它们。

## 2. ESC 系统菜单（`RunPauseDlg`，归属 Run 功能）

- **输入捕获**：挂在 `RunMainWin._UnhandledInput`（Run 会话期间它常驻）。用 `_UnhandledInput` 而不是 `_Input`：被弹窗/下拉框消费掉的 ESC（例如关掉 `OptionButton` 的弹出列表）不该同时把系统菜单也开起来；命中后 `SetInputAsHandled()`。
- **开/关的唯一判据**：`RunUiController.IsPauseMenuOpen()`（`UIManager.GetUIVo(id) is { IsOpen: true }`）。界面侧不另存状态，避免"两份状态不同步"。`UIVo.IsOpen` 覆盖「正在创建 → 已打开」的窗口期，因此连按 ESC 不会叠出两个实例。
- **注册**：`UIRegistration.Dialog(FeatureId, RunUiIds.PauseMenu, "Src/mod/run/Ui")` + `CacheTime = 0`（每次打开都重读当前阶段）。
- **按钮**：

| 按钮 | 行为 |
| --- | --- |
| 继续游戏 | 关闭菜单（`BaseWin.Close`） |
| 打开设置 / 打开图鉴 / 打开词典 | `GlobalModController.OpenSettingAsync / OpenCodexAsync / OpenGlossaryAsync`——三者都叠在菜单之上，关掉后回到菜单 |
| 保存并退出到主菜单 | `RunUiController.SaveAndExitToMenuAsync`：落盘 → `CloseByOwner(run, destroy: true)`（连菜单一起关）→ 回主菜单 |
| 退出到桌面 | 先 `AlertDlg` 确认（说明"最近一次自动保存之后的进度会丢失"），确认后 `GetTree().Quit()` |

- **战斗阶段禁用「保存并退出」**并说明原因：战斗态（模拟器、手牌、充能球队列）不在 Run 存档模型里，中途落盘得到的是读不回来的档。判定统一走 `ERunPhaseExtensions.IsCombatPhase()`（与 `RunMainWin` 的保存按钮同一处定义）。
- **跨功能调用**：Run 的界面直接调 Global 的静态打开入口。这与 `MenuWin`（Global）调 `RunUiController.OpenStorySelectAsync` 是对称的既有做法；本轮不引入新的门面层，避免为 3 个入口造一套路由。

## 3. 词典（`GlossaryDlg`，归属 Global 功能）

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

## 4. 文案与导入

- 新增键写在 `Resource/Locale/strings.csv`（33 行：`UI_PAUSE_*`、`UI_GLOSSARY_*`、`KW_*`）。
- **CSV 改动必须重新导入**才会进 `.translation`（`project.godot` 的 `locale/translations` 指向生成物）：
  `godot --headless --path . --import`。忘记这一步的表现是界面显示原始键名。
- 守卫：`LocaleIntegrityTests` 原有扫描覆盖 `Tr("KEY")` 与场景 `text = "KEY"`；本轮补一条
  `Pause_and_glossary_literal_keys_exist_in_resource_csv`，覆盖"先存进常量/局部变量再翻译"的键
  （`GlossaryBuilder` 的分组/正文键、`RunPauseDlg` 的禁用原因键），这类键原有扫描匹配不到。

## 5. 已知边界

- ESC 在**任何** Run 界面之上都能开菜单（含图鉴/词典/详情弹窗之上），这是"随时"的字面语义；若要改成"先关最上层"，应走 `UIStack`/`BackAsync` 的导航栈语义，另案处理。
- 词典内容在**打开时**现算，运行中热更内容后重开即可看到新条目（无需重启）。
- 「退出到桌面」不做保存：它的语义就是退出，确认框负责告知代价；战斗外每次阶段推进已有自动保存兜底。
