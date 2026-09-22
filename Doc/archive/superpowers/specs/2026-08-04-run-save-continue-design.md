# Run 存档闭环（保存 / 继续 / 自动保存）设计

## 概述

将当前「只写 `RunSaveService` 却从未接线」的存档基础设施落地为完整闭环：Run 主界面新增保存/快速读取/保存并返回按钮，主菜单「继续游戏」入口接线，阶段切换与退出自动保存，放弃 Run 删除存档。战斗中禁止保存。

> 本规格是 grilling 会话（2026-08-04）的决议固化；联机影响另见 `2026-08-04-multiplayer-save-impact.md`。

---

## 1. 目标与非目标

### 目标

- **单存档槽**：`user://saves/run/`，覆盖式写入，原子写 + 备份（复用既有 `RunSaveService`）。
- **RunMainWin 新增按钮**（右下角纵向四连）：
  1. 保存（弹 Toast）
  2. 保存并返回主菜单（不弹 Toast）
  3. 快速读取存档（不弹 Toast；失败弹 Toast）
  4. 放弃 Run（既有，确认弹窗）
- **MenuWin「继续游戏」**：绑定加载最近存档；无存档时按钮置灰。
- **自动保存**：阶段切换逐处调用 + 退出前非战斗再存一次；**静默不弹 Toast**。
- **放弃 Run → 删档**。
- **战斗（Battle/BattleEnd）禁存**：保存系按钮禁用。

### 非目标

- 多存档槽位、存档命名/列表 UI。
- 战斗中保存（战斗进度序列化）——战斗中按钮禁用。
- 自动保存的 Toast / 保存时间角标。
- 联机存档权威、掉线恢复（见独立影响文档）。

---

## 2. 存储模型

### 目录与文件

```
user://saves/run/          ← RunRuntime 静态持有
├── <run_id>.json          # 运行中存档
├── <run_id>.bak.json      # 备份（原子替换保留）
└── <run_id>.tmp.json      # 临时文件
```

- 复用 `RunSaveService`（原子写：tmp → File.Replace → bak；损坏降级 bak）。
- `RunRuntime` 静态持有 `RunSaveService`，构造目录 `ProjectSettings.GlobalizePath("user://saves/run")`。

### 单槽语义

- `RunSaveService.Exists` 判定是否有存档。
- 新增 Run 时覆盖写（同一个 RunId 文件），同一时刻只有最近一局有效。
- 加载走 `RunSaveService.LoadOrDefault()`。

---

## 3. 运行时会话门面（RunRuntime 扩展）

`RunRuntime`（`Src/mod/run/RunRuntime.cs`）新增：

```csharp
private static RunSaveService? _saveService;

public static RunSaveService SaveService => _saveService ??= new(GetRunSaveDir());

public static bool HasSave => SaveService.Exists;

public static void SaveCurrent()   // 若 Current != null：Current.Save(SaveService)
public static bool TryLoadLatest() // 读档恢复，成功返回 true
public static void ClearSave()     // 删除存档
```

### 行为约定

| 方法 | 行为 |
|------|------|
| `CreateNew` | 保留现有：`_current?.Dispose()` → 新建。可选：创建成功后立即自动保存一次（见 §4 阶段切换） |
| `Abandon()` | 保留现有：`_current?.Dispose()`；**新增 `ClearSave()` 删档** |
| `TryLoadLatest()` | 读 `SaveService.LoadOrDefault()`；无 RunId 返回 false；有则 `LoadRun` 恢复为当前会话 |
| `SaveCurrent()` | 当前会话 `Save(SaveService)` |

---

## 4. 自动保存时机

grilling 决议（问题 10 / 11 / 12 / 13）：**选项 C** —— 阶段切换自动存 + 退出前非战斗再存；**方案 A** 逐处调用；**静默不弹 Toast**。

### 4.1 阶段切换自动存

在 `RunController` 的阶段切换点逐处调用（方案 A）：

- `CreateRun` 末尾（新建后立即落盘）
- `NextRing`（进入新环）
- `EndBattle` 胜利（Phase = RingEnd）后落盘；**失败不做自动保存**（存档保持战前状态，失败视为纯时间成本、进度不推进，不落盘）

实现方式：`RunController` 新增可选 `RunSaveService? _autoSaveService` 字段 + `EnableAutoSave(RunSaveService)` 方法；各切换点调用 `AutoSaveIfSettled()`（非战斗阶段才真正 Save）。测试注入临时目录 RunSaveService。

### 4.2 退出前自动存

- **正常返回主菜单（含「保存并返回」按钮路径）**：`RunMainWin.OnClose()` 中若 `RunRuntime.Current != null` 且 Phase 非 Battle/BattleEnd 且非 Finished → `RunRuntime.SaveCurrent()`。
- **放弃 Run**：走 `RunRuntime.Abandon()` → 删档，不保存。

### 4.3 静默

- 自动保存不弹 Toast，不打断流程。

---

## 5. RunMainWin 按钮布局与交互

### 布局（右下角 `Panel/VBoxContainer`）

```
VBoxContainer（右下角）
├── BtnSave           # 保存
├── BtnSaveExit       # 保存并返回主菜单
├── BtnQuickLoad      # 快速读取存档
└── BtnAbandon        # 放弃 Run（既有）
```

### 交互

| 按钮 | 逻辑 | Toast |
|------|------|-------|
| 保存 | `RunRuntime.SaveCurrent()` | `UI_RUN_SAVED` |
| 保存并返回主菜单 | `RunRuntime.SaveCurrent()` → `Close()` → `GlobalModController.OpenMenuAsync()` | 无 |
| 快速读取 | `RunRuntime.TryLoadLatest()` → 成功刷新视图；失败 Toast | 失败时 `UI_RUN_LOAD_FAILED` |
| 放弃 | 既有确认弹窗 → `RunRuntime.Abandon()` → `Close()` → 主菜单 | 无 |

### 战斗中禁用

- `UpdateView()` 中依据 `state.Phase`：Battle / BattleEnd 时禁用 **保存、保存并返回、快速读取** 三按钮（`Disabled = true`）。
- 快速读取在战斗中禁用：战斗状态在 `CombatSimulation` 中，不在 RunDto，读档会丢战斗进度；且战斗中本由新 Win 覆盖 RunMainWin，按钮不可见为常态。

---

## 6. MenuWin「继续游戏」

- `MenuWin.LoadBtn`（场景已存在，翻译键 `UI_MENU_CONTINUE`）接线。
- `OnOpen`：`LoadBtn.Disabled = !RunRuntime.HasSave`（无档置灰）。
- 点击：`RunRuntime.TryLoadLatest()` → 成功则 `Close()` 菜单并 `RunUiController.OpenRunMainAsync()`；失败不动作（理论上不会发生，因无档时按钮已禁用）。

---

## 7. Toast 对接

- 新建 Toast 组件（见 `2026-08-04-toast-component-design.md`）。
- RunMainWin 手动保存成功 → `ToastService.Show("UI_RUN_SAVED")`。
- 快速读取失败 → `ToastService.Show("UI_RUN_LOAD_FAILED")`。

---

## 8. 翻译键新增

`Resource/Locale/strings.csv`：

```csv
UI_RUN_SAVE,保存,Save
UI_RUN_SAVE_AND_EXIT,保存并返回主菜单,Save & Return to Menu
UI_RUN_QUICK_LOAD,快速读取存档,Quick Load
UI_RUN_SAVED,已保存,Saved
UI_RUN_LOAD_FAILED,没有可读取的存档,No save to load
```

> `UI_MENU_CONTINUE`（继续游戏）已存在。

---

## 9. 边界与异常

- **战斗中禁存**：任何保存入口在 Battle/BattleEnd 阶段不落盘。
- **放弃删档**：`Abandon()` 连带 `ClearSave()`，避免「继续游戏」读到已放弃的档。
- **损坏档降级**：`RunSaveService` 已处理（primary 坏读→bak，仍坏→默认空 RunDto）。
- **无存档加载**：`LoadOrDefault` 返回空 RunDto → `TryLoadLatest` 返回 false → MenuWin 按钮置灰 / 快速读取弹 Toast。
- **单槽覆盖**：新 run 覆盖写同目录，不残留多档。
