# Run 队伍编辑界面 设计

**日期**：2026-09-21
**状态**：已实装
**关系**：服从 [2026-05-11 总规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md)（§4.5 角色池/上场、§4.6 卡牌构筑）、[2026-06-22 Run 模块规格](../../../superpowers/specs/2026-06-22-run-mod-design.md)；界面层遵循 [2026-05-15 UI 管理器规格](../../../superpowers/specs/2026-05-15-ui-manager-design.md) 与 [2026-09-19 UI 主题规格](../../../superpowers/specs/2026-09-19-ui-theme-and-debug-panel.md)。

---

## 1. 入口

`RunMainWin` 右下角按钮列表末尾新增 **「队伍」（`UI_RUN_TEAM_EDIT`）** → `RunUiController.OpenTeamEditAsync()`。
入口始终可点（战斗中也能查看当前队伍），**是否允许编辑由服务层门闩判定**。

## 2. 分层（沿用 RunDebugDlg 的成熟分工）

| 层 | 职责 |
|---|---|
| `RunTeamEditService`（`Src/mod/run/team/`，**不依赖 Godot**） | 槽位/角色池视图、上阵/下阵、卡组增删与校验、门闩判定、脏标记 |
| `RunTeamEditDlg` | 一级界面：槽位选项卡 + 当前上阵 + 角色池 + 详情预览；悬停刷新预览、单击进二级 |
| `RunCharacterDeckDlg` | 二级界面：该角色卡组编辑 + 上阵 |

因此全部规则都能被 NUnit 直接覆盖（`RunTeamEditServiceTests`，13 条）。

## 3. 一级界面

- **顶部 4 个槽位选项卡**（`TabBar`）：标题 = 「槽位 N」+（已上阵时）角色名；点击切换当前槽位。
- **左：当前上阵**：该槽角色的头像件（`BaseCharacterItem`）+「下阵」按钮（清空槽位；开战前由 `ValidateParty` 校验满编）。
- **中：可上阵角色**（`VirtualList` + `BaseCharacterItem` 模板）：显示**全部**角色池实例；已在其它槽上阵的加「已在槽位 N」徽标。
- **右：详细信息预览**：`CharacterPresenter` 立绘/动画 + 名字 + 元素/种族/职业 + **由当前卡组换算的属性**（`ComputeAttributeMap`）+ 卡组缩略（水平 `VirtualList` + `BaseCardItem`）。属性名走内容侧翻译键 `attr.<snake_case>.name`，缺键时回落原始 id。
- **悬停**任一角色头像刷新预览（离开时清空 -> 回落为显示当前槽位角色）；**单击**进入二级界面。

## 4. 二级界面（角色 → 卡组 / 上阵）

- 顶部：角色名标题 +「新建卡组」+ **卡组选项卡**（多套卡组，`CharacterInstance.Decks` ≤ 10 套 + `CurrentDeckIndex`），切页签即切换当前卡组。
- 左：当前卡组的卡片（`BaseCardItem`）——**单击移出卡组**、**长按查看卡牌详情**；显示 `n/上限` 计数与"有 N 张牌当前不可用"的校验提示。
- 右：可加入卡牌池 = **收藏 ∪ 角色专属卡**（`GetBuildableCardIds`，与 `DeckPreset` 校验同集合）——**单击加入**、**长按看详情**。
  **已在当前卡组内的牌必须显式标出**：`RunTeamEditService.GetPoolCards` 逐条给出 `InDeck`，界面据此给卡面加
  整卡遮罩（`BaseCardItem.SetOverlay`，纸色半透明 + 居中提示「卡组中已有该牌」，复用 `UI_TEAM_DECK_DUPLICATE` 文案）
  并**不再挂 `Clicked`**——卡组不允许重复，点下去只会拿到一句失败提示。遮罩由 `SetData` 自动清除，
  对象池复用与换卡组都不会残留。
- 底部：「上阵到槽位 N」按钮（已在当前槽位时显示「已在该槽位」）。

## 5. 规则要点

- **门闩**：`Phase ∈ {Battle, BattleEnd, Finished}` 禁止编辑（服务返回 `UI_TEAM_EDIT_BLOCKED`）；同时 `RunController.StartBattle/EndBattle` 会锁定/解锁全部角色卡组（`CharacterInstance.IsDeckLocked`，此前是无人调用的死 API）。
- **自动迁移**：把已在其它槽上阵的角色上阵到当前槽时，原槽自动清空——同一实例永不占多槽（总规格 §4.5.1）。
- **卡组规则**：上限 10 张、不可重复、必须属于可构筑集合、至少保留 1 张；失败一律返回可翻译的原因键（已满/重复/不可构筑/卡组为空/战斗中锁定）。
- **保存**：服务维护 `IsDirty`，一级界面**关闭时**统一 `RunRuntime.SaveCurrent()`（与现有保存按钮同一入口）。

## 6. 复用与新增的公共件

- `VirtualList`（虚拟列表）：角色池（垂直）、卡组缩略（水平）、卡牌列表（垂直）。
  **模板与行高都由场景配置**（`ItemTemplate` / `ItemSize`，编辑器可随时换预制体），代码不写死 `res://` 路径。
- **`VirtualList` 的三个必修缺陷**（首次被本界面真正使用后暴露）：
  1. **节点引用属性必须同时写进 `node_paths`**：`ScrollArea` 是节点类型导出，`.tscn` 里除了
     `ScrollArea = NodePath("Scroll")`，所属节点还必须声明
     `node_paths=PackedStringArray("ScrollArea")`，且路径**相对该节点自身**（不是场景根）。
     缺任一项都会被静默忽略 → `_Ready` 时 `ScrollArea` 为 null → `SetData` 一个列表项都不建（列表全空）。
  2. **入树同帧的可视区尺寸为 0**：宿主界面在 `OnOpen` 同帧调用 `SetData` 时布局尚未跑完，
     只能算出 1 个可见项。现在 `VirtualList` 订阅自身与 `ScrollArea` 的 `Resized`，
     布局完成后自动 `Refresh()` 重算。
  3. **不得按"格子尺寸"给条目 `set_size`**：立绘/卡面是固定尺寸预制体（160×208，子节点 full-rect 锚点），
     按列表宽度拉伸会让卡面变形。现在的契约是：
     **条目尺寸一律由 `ItemTemplate` 决定，`ItemSize` 只是滚动方向的步长（行距/列距）**，
     `Spacing` 为额外间距；只有"整行文本条"这类条目才打开 `StretchItemAcrossAxis`。
     「当前上阵」项也不再锚成 full-rect，保持 160×208 并在槽位区水平居中。
- `BaseCharacterItem` 新增：`Hovered` / `Clicked` 回调、`SetBadge(text)` 名称后缀、`ECharacterClickAction.Emit`（默认 `OpenDetails` 行为不变）。
- `BaseCardItem` 新增：`Hovered` / `Clicked` / `LongPressed` 回调、`EnableLongPress` + `LongPressSec`、`ECardClickAction.Emit`（长按后抬起不再触发单击）。
- 「当前上阵」的角色项也改成**场景内预置实例**（`CurrentHolder/CurrentCharacter`），不再由代码
  从硬编码路径实例化。
- `GlobalModController.OpenCardDetailsAsync(cardId)`：卡牌详情统一入口（组件与二级界面共用）。
- **刷新走 Run 功能内部总线**（`RunMod.InternalBus`，事件表在 `RunEventBus.cs`），界面之间不互相引用：
  - `RunCharacterAssigned`（载荷 `SlotIndex` / `PreviousInstanceId` / `CurrentInstanceId`）：由 `RunController.SetActiveCharacter` / `UnsetActiveCharacter` 广播（调试面板上阵同样经过它）。
  - `RunDeckChanged`（载荷 `InstanceId` / `DeckIndex`）：卡组编辑入口不在 Controller（走 `CharacterInstance.TryEditDeck` / `TryCreateDeck` / `TrySetCurrentDeck`），故由写入方（`RunTeamEditService`、`RunDebugService`）在写成功后调用 `RunController.NotifyDeckChanged` 广播。
  - 一级界面在 `InitEvent` 里以 `caller = this` 订阅，订阅经 `BindingScope` 登记、离场自动 `Off()`；失败写操作不广播。
  - 注意 `RunSlotOwnershipChanged` 描述的是**玩家占槽**（联机时谁坐几号位，`RunMod.SlotOwnership`），与"该槽上了哪个角色"（`PlayerRunState.ActiveCharacter`）是两件事，故不复用。

## 7. 读档时的角色定义还原（本次一并修掉）

`RunMod.RestoreFrom` 原先用「`definitionId` + 存档内卡表」拼一个最小 `CharacterDto` 重建角色实例，
于是读档后 `CharacterInstance.Definition` 丢失**显示名 / 元素 / 种族 / 职业 / 能量 / 被动**，
在队伍编辑界面里就是"没有名字、没有元素的空壳"，角色级能量保底也随之消失。

现在 `RestoreFrom` 接受 `definitionResolver`：读档时按 `definitionId` 从内容注册表取回完整定义，
取不到才回落到存档内的最小快照。组合根（`RunRuntime.CreateController`）注入解析函数，
`RunController` 的 `LoadRun` / `LoadFromSave` / 战斗快照回滚三条路径都会带上它——
Run 层自身仍然不直接访问内容注册表。

## 8. 明确后置项

- **卡组重命名 / 删除**（当前只有新建与切换）。
- **拖拽式编排**（当前是点击式增删）。
- **换人门闩的"仅环间/特定事件"细分**：目前按"非战斗即可编辑"统一处理，尚未区分环内事件阶段。
- **美术**：角色立绘缺失（如 chalux 无 `artPath`）时预览区只有名字与文本（列表项同理，只剩名字与元素底色），等美术补齐。
- **正式战斗界面内的队伍入口**（与战斗 UI 一并设计）。
- **开局的角色池**：`StorySelectDlg` 目前以空候选列表创建 Run，且 `RunController.CreateRun` 忽略 `candidates`，
  因此新开一局角色池为空（只能靠调试面板授予角色）。队伍编辑界面此时会显示"角色池为空"的显式提示；
  真正的开局选人（3 选 1 或直接入池）仍是待做的玩法决定。
