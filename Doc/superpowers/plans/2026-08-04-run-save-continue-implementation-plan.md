# Run 存档闭环 + Toast 组件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 Run 存档完整闭环：RunMainWin 新增「保存 / 保存并返回主菜单 / 快速读取存档」按钮（战斗禁用），MenuWin「继续游戏」接线（无档置灰），阶段切换 + 退出自动保存（静默），放弃 Run 删档；新建通用 Toast 组件（对象池 / 纵向堆叠 / 上移淡出）。

## 实现状态（2026-08-04）

**设计阶段：** grilling 会话已完成全部决策（见 `../specs/2026-08-04-run-save-continue-design.md` 与 `../specs/2026-08-04-toast-component-design.md`）。代码未开始。

**权威规格:**
- [run-save-continue-design](../specs/2026-08-04-run-save-continue-design.md)（本任务权威）
- [toast-component-design](../specs/2026-08-04-toast-component-design.md)
- [multiplayer-save-impact](../specs/2026-08-04-multiplayer-save-impact.md)
- [run-mod-design](../specs/2026-06-22-run-mod-design.md)
- [ui-manager-design](../specs/2026-05-15-ui-manager-design.md)

**Tech Stack:** C# / Godot 4.6 Mono / NUnit（`Tests/kemo_card.Ui.Tests`）

## Global Constraints

- `Src/frame/` 不得引用 `KemoCard.Mod.*`
- 面向用户文案用翻译键（`Resource/Locale/strings.csv`）；日志 / `GD.Print` 可用明文
- 不创建 `.uid` 文件；格式化仅针对本任务改过的文件
- 布局用 Godot 场景（`.tscn`），代码只写逻辑
- 连续大段同业务代码（>5 函数）用 `#region` / `#endregion`
- `git commit` 仅在用户明确要求时执行；提交说明用简体中文
- 验证命令：

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q
```

- 格式化示例：`dotnet format kemo_card.csproj --include Src/mod/run/Ui/RunMainWin.cs`

---

## File Structure

| 路径 | 职责 | 操作 |
|------|------|------|
| `Src/mod/global/Ui/Toast/ToastService.cs` | Toast 静态门面：池、堆叠、生命周期调度 | Create |
| `Src/mod/global/Ui/Toast/ToastItem.cs` | 单条 Toast 节点逻辑（动画） | Create |
| `Src/mod/global/Ui/Toast/ToastItem.tscn` | Toast 场景（Panel + Label，25% 定位） | Create |
| `Src/MainRoot.cs` | 初始化 ToastService（注入 Notice 层） | Modify |
| `Src/mod/global/GlobalMod.cs` | 注册 Toast UI？——否，Toast 不经 UIManager，仅 MainRoot 注入 | 无 |
| `Src/mod/run/RunRuntime.cs` | `SaveService` / `HasSave` / `SaveCurrent` / `TryLoadLatest` / `ClearSave`；`Abandon` 加删档 | Modify |
| `Src/mod/run/RunController.cs` | `EnableAutoSave` + 阶段切换自动保存 | Modify |
| `Src/mod/run/Ui/RunMainWin.cs` | 新增三按钮 + 战斗禁用 + `OnClose` 自动保存 | Modify |
| `Src/mod/run/Ui/RunMainWin.tscn` | 右下角四按钮布局 | Modify |
| `Src/mod/global/Ui/MenuWin.cs` | 继续游戏按钮接线 + 无档置灰 | Modify |
| `Resource/Locale/strings.csv` | 新增 5 个翻译键 | Modify |
| `Tests/kemo_card.Ui.Tests/Run/RunSaveClosureTests.cs` | 自动保存触发点 / 单槽覆盖 / 放弃删档 测试 | Create |
| `Doc/superpowers/specs/2026-06-22-run-mod-design.md` | 对齐实现（存档闭环） | Modify |
| `Doc/INDEX.md` | 登记三份新文档 | Modify |

---

### Task 1: ToastService + ToastItem（通用组件）

**Files:**
- Create: `Src/mod/global/Ui/Toast/ToastService.cs`
- Create: `Src/mod/global/Ui/Toast/ToastItem.cs`
- Create: `Src/mod/global/Ui/Toast/ToastItem.tscn`
- Modify: `Src/MainRoot.cs`

**Interfaces:**
- Produces: `ToastService.Show(string textKey)`、`ToastService.Configure(Control noticeLayer)`
- Consumes: `EUILayer.Notice`、`Localization.Tr`、`UIManager.Instance.GetLayer`

设计要点（grilling 决议）：
- 挂 `EUILayer.Notice` 层（TopLayer），不经 UIManager 状态机（UIVoRegistry 单实例模型与堆叠冲突）。
- 对象池 `Stack<ToastItem>`（默认上限 4）+ 活跃 `List<ToastItem>`。
- 动画：瞬间弹出 → 停留 1000ms → 上移 100px + 淡出 2000ms（并行 Tween）。
- 容器 `VBoxContainer` 置于 Notice 层，屏幕上方 25% 定位（场景/代码二选一，本计划选**代码锚点**：`AnchorTop=0.25`，水平居中；因容器由 Service 运行时创建）。

- [ ] **Step 1: 实现 ToastItem.cs**

```csharp
// Src/mod/global/Ui/Toast/ToastItem.cs
using Godot;

namespace KemoCard.Mod.Global.Ui.Toast;

public partial class ToastItem : Control
{
    [Export] private Label? _lblText;

    public void ShowText(string text)
    {
        if (_lblText != null)
        {
            _lblText.Text = text;
        }
    }

    public void PlayRecycle(float stayMs, float fadeMs, float risePx, Action onRecycle)
    {
        // Tween: 延迟 stayMs → 并行 TweenProperty(Modulate.a→0, fadeMs) + TweenProperty(Position.y→y-risePx, fadeMs)
        // 完成回调 onRecycle
    }
}
```

- [ ] **Step 2: 实现 ToastService.cs**

```csharp
// Src/mod/global/Ui/Toast/ToastService.cs
using Godot;
using KemoCard.Fixed.Godot;

namespace KemoCard.Mod.Global.Ui.Toast;

public static class ToastService
{
    private const string ScenePath = "res://Src/mod/global/Ui/Toast/ToastItem.tscn";
    private const int MaxPool = 4;
    private static readonly Stack<ToastItem> Pool = new();
    private static readonly List<ToastItem> Active = [];
    private static VBoxContainer? _container;

    public static void Configure(Control noticeLayer) { /* 创建容器并 AddChild */ }
    public static void Show(string textKey) { /* 从池取/实例化 → ShowText(Localization.Tr) → 挂容器 → PlayRecycle */ }
}
```

- [ ] **Step 3: 编写 ToastItem.tscn**

```
[gd_scene format=3]

[ext_resource type="Script" path="res://Src/mod/global/Ui/Toast/ToastItem.cs" id="1_toast"]

[node name="ToastItem" type="Control" node_paths=PackedStringArray("_lblText")]
script = ExtResource("1_toast")
_lblText = NodePath("Panel/Label")

[node name="Panel" type="Panel" parent="."]
...
[node name="Label" type="Label" parent="Panel"]
...
```

- [ ] **Step 4: MainRoot 注入**

```csharp
// Src/MainRoot.cs —— InitUIManager() 末尾
ToastService.Configure(UIManager.Instance!.GetLayer(EUILayer.Notice)!);
```

- [ ] **Step 5: 编译验证**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED

---

### Task 2: RunRuntime 扩展（存档门面）

**Files:**
- Modify: `Src/mod/run/RunRuntime.cs`
- Modify: `Src/mod/run/RunController.cs`

**Interfaces:**
- Produces: `RunRuntime.SaveService` / `HasSave` / `SaveCurrent()` / `TryLoadLatest()` / `ClearSave()`；`RunController.EnableAutoSave(RunSaveService)`
- Consumes: `RunSaveService`、`RunController.Save` / `TryLoad`

- [ ] **Step 1: RunRuntime 加存档门面**

```csharp
// Src/mod/run/RunRuntime.cs —— using 区追加
using KemoCard.Mod.Run.Save;

// 类型内追加
private static RunSaveService? _saveService;

public static RunSaveService SaveService => _saveService ??= new(
    ProjectSettings.GlobalizePath("user://saves/run"));

public static bool HasSave => SaveService.Exists;

public static void SaveCurrent()
{
    _current?.Save(SaveService);
}

public static bool TryLoadLatest()
{
    if (_current == null || !_current.TryLoad(SaveService, out var dto))
        return false;
    return true;
}

public static void ClearSave()
{
    SaveService.Delete();
}
```

> 注意：`RunRuntime` 当前是静态类，`ProjectSettings` 是 Godot 静态 API，测试环境无 Godot 上下文会抛错。**测试用临时目录注入**：将 `SaveService` 改为可注入（`internal static void InjectSaveService(RunSaveService service)` 或构造函数参数由调用方传入）。本计划采用：`SaveService` 属性支持测试注入。

```csharp
// Abandon() 改为：Dispose + ClearSave + _current = null
public static void Abandon()
{
    _current?.Dispose();
    _current = null;
    ClearSave();
}
```

- [ ] **Step 2: RunController 自动保存**

```csharp
// Src/mod/run/RunController.cs —— 追加字段与方法
private RunSaveService? _autoSaveService;

public void EnableAutoSave(RunSaveService saveService)
{
    _autoSaveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
}

private void AutoSaveIfSettled()
{
    if (_autoSaveService == null)
        return;
    if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
        return;
    Save(_autoSaveService);
}
```

- [ ] **Step 3: 阶段切换点插入 AutoSaveIfSettled()**

| 位置 | 插入时机 |
|------|----------|
| `CreateRun` 末尾（`Model.Phase = ERunPhase.Event` 之后） | 新建即落盘 |
| `NextRing()` 末尾（`Model.Phase = ERunPhase.Event` 之后） | 环切换落盘 |
| `EndBattle` 胜利分支（`Phase = RingEnd` 之后） | 胜利落盘 |
| `EndBattle` 失败分支（`Phase = Event` 之后） | **不保存**（失败不做处理，存档保持战前状态） |

> `StartBattle`（Phase=Battle）与 `AbandonRun`（Phase=Finished）**不**调用——战斗禁存；放弃由 `RunRuntime.Abandon` 删档。

- [ ] **Step 4: 编译验证**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED

---

### Task 3: RunMainWin 新增按钮 + 战斗禁用 + 退出自动保存

**Files:**
- Modify: `Src/mod/run/Ui/RunMainWin.cs`
- Modify: `Src/mod/run/Ui/RunMainWin.tscn`

**Interfaces:**
- Consumes: `RunRuntime.SaveCurrent` / `TryLoadLatest` / `HasSave`、`ToastService.Show`、`GlobalModController.OpenMenuAsync`
- Produces: `_btnSave` / `_btnSaveExit` / `_btnQuickLoad` 三个导出字段 + 三个处理方法

- [ ] **Step 1: RunMainWin.cs 加按钮字段与事件**

```csharp
[Export] private Button? _btnSave;
[Export] private Button? _btnSaveExit;
[Export] private Button? _btnQuickLoad;
```

```csharp
// InitEvent() 内追加
if (_btnSave != null) { OnClicks(_btnSave, OnSave); }
if (_btnSaveExit != null) { OnClicks(_btnSaveExit, OnSaveExit); }
if (_btnQuickLoad != null) { OnClicks(_btnQuickLoad, OnQuickLoad); }
```

```csharp
#region 保存

private void OnSave()
{
    RunRuntime.SaveCurrent();
    ToastService.Show("UI_RUN_SAVED");
}

private void OnSaveExit()
{
    RunRuntime.SaveCurrent();
    Close();
    _ = GlobalModController.OpenMenuAsync();
}

private void OnQuickLoad()
{
    if (RunRuntime.TryLoadLatest())
    {
        UpdateView();
    }
    else
    {
        ToastService.Show("UI_RUN_LOAD_FAILED");
    }
}

#endregion
```

- [ ] **Step 2: 战斗禁用**

```csharp
// UpdateView() 末尾追加
var inCombat = state.Phase is ERunPhase.Battle or ERunPhase.BattleEnd;
if (_btnSave != null) { _btnSave.Disabled = inCombat; }
if (_btnSaveExit != null) { _btnSaveExit.Disabled = inCombat; }
if (_btnQuickLoad != null) { _btnQuickLoad.Disabled = inCombat; }
```

- [ ] **Step 3: OnClose 自动保存（正常退出非战斗）**

```csharp
protected override void OnClose()
{
    var run = RunRuntime.Current;
    if (run == null)
        return;
    if (run.State.Phase is ERunPhase.Battle or ERunPhase.BattleEnd or ERunPhase.Finished)
        return;
    RunRuntime.SaveCurrent();
}
```

- [ ] **Step 4: RunMainWin.tscn 布局**

右下角 `VBoxContainer` 内追加三个按钮（置于 BtnAbandon 之前，从上到下：BtnSave → BtnSaveExit → BtnQuickLoad → BtnAbandon）：

```
[node name="BtnSave" type="Button" parent="Panel/VBoxContainer"]
custom_minimum_size = Vector2(180, 48)
layout_mode = 2
size_flags_horizontal = 4
text = "UI_RUN_SAVE"

[node name="BtnSaveExit" type="Button" parent="Panel/VBoxContainer"]
custom_minimum_size = Vector2(180, 48)
layout_mode = 2
size_flags_horizontal = 4
text = "UI_RUN_SAVE_AND_EXIT"

[node name="BtnQuickLoad" type="Button" parent="Panel/VBoxContainer"]
custom_minimum_size = Vector2(180, 48)
layout_mode = 2
size_flags_horizontal = 4
text = "UI_RUN_QUICK_LOAD"
```

> 更新根节点 `node_paths=PackedStringArray(...)` 加入 `_btnSave` / `_btnSaveExit` / `_btnQuickLoad`，并设置三个导出属性 NodePath。

- [ ] **Step 5: 编译验证**

Run: `dotnet build kemo_card.csproj --nologo`
Expected: BUILD SUCCEEDED

---

### Task 4: MenuWin「继续游戏」接线

**Files:**
- Modify: `Src/mod/global/Ui/MenuWin.cs`

**Interfaces:**
- Consumes: `RunRuntime.HasSave` / `TryLoadLatest`、`RunUiController.OpenRunMainAsync`
- Produces: LoadBtn 点击处理 + OnOpen 置灰

- [ ] **Step 1: 绑定 LoadBtn + 无档置灰**

```csharp
// MenuWin.cs —— InitEvent() 内追加
if (LoadBtn != null)
{
    OnClicks(LoadBtn, OnContinue);
}
```

```csharp
// MenuWin.cs —— OnOpen() 内追加
if (LoadBtn != null)
{
    LoadBtn.Disabled = !RunRuntime.HasSave;
}
```

```csharp
private void OnContinue()
{
    if (RunRuntime.TryLoadLatest())
    {
        _ = RunUiController.OpenRunMainAsync();
    }
}
```

- [ ] **Step 2: 编译 + 全量测试**

Run: `dotnet build kemo_card.csproj --nologo`
Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: BUILD SUCCEEDED + PASS

---

### Task 5: 翻译键

**Files:**
- Modify: `Resource/Locale/strings.csv`

- [ ] **Step 1: 追加翻译键**

```csv
// Resource/Locale/strings.csv —— 文件末尾追加
UI_RUN_SAVE,保存,Save
UI_RUN_SAVE_AND_EXIT,保存并返回主菜单,Save & Return to Menu
UI_RUN_QUICK_LOAD,快速读取存档,Quick Load
UI_RUN_SAVED,已保存,Saved
UI_RUN_LOAD_FAILED,没有可读取的存档,No save to load
```

> `UI_MENU_CONTINUE` 已存在（MenuWin.tscn LoadBtn 使用），无需新增。

---

### Task 6: 单元测试（Run 存档闭环）

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Run/RunSaveClosureTests.cs`

**Interfaces:**
- Consumes: `RunController`、`RunSaveService`（临时目录）、`RunMod`
- Produces: 自动保存触发 / 战斗中不存 / 单槽覆盖 / 放弃删档 的断言

- [ ] **Step 1: 写测试**

```csharp
// Tests/kemo_card.Ui.Tests/Run/RunSaveClosureTests.cs
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunSaveClosureTests
{
    [Test]
    public void AutoSave_writes_save_on_phase_transition()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var saveService = new RunSaveService(dir);
        var controller = new RunController(new RunMod());
        controller.EnableAutoSave(saveService);

        var rng = new HostRng(1, "battle");
        controller.CreateRun("story_a", rng, [], isMultiplayer: false);

        Assert.That(saveService.Exists, Is.True);
        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void AutoSave_skips_battle_phase()
    {
        // CreateRun → StartBattle(registry) → 战斗中不应落盘覆盖
        // （battle 阶段 AutoSaveIfSettled 直接 return）
    }

    [Test]
    public void Single_slot_overwrites_previous_run()
    {
        // 两次 CreateRun → 目录内仅一个有效 .json
    }

    [Test]
    public void Delete_clears_save()
    {
        // CreateRun + EnableAutoSave → Delete() → Exists == false
    }
}
```

> `StartBattle` 需要完整 registry（参考既有 `RunControllerTests` 的 `CombatTestHelper.CreateFullRegistry()`）；若太繁琐可在测试中仅断言「Battle 阶段 Save 被跳过」。

- [ ] **Step 2: 运行测试**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunSaveClosureTests" --nologo -v q`
Expected: PASS

- [ ] **Step 3: 全量测试确认无回归**

Run: `$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"; dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q`
Expected: PASS

---

### Task 7: 规格与文档回写

**Files:**
- Modify: `Doc/superpowers/specs/2026-06-22-run-mod-design.md`
- Modify: `Doc/INDEX.md`

- [ ] **Step 1: run-mod-design 对齐实现**

`2026-06-22-run-mod-design.md`：
- Run 界面流程第 5 步补充：主界面壳新增「保存 / 保存并返回 / 快速读取」按钮、战斗中禁存。
- §4 RunSaveService 补充：运行时接线（`RunRuntime` 静态持有，目录 `user://saves/run`，单槽覆盖写）。
- 新增 §10 或并入 §4：「存档闭环」——自动保存（阶段切换 + 退出）、放弃删档、主菜单继续游戏。

- [ ] **Step 2: INDEX.md 登记**

`Doc/INDEX.md`：
- 活规格表追加三份：`2026-08-04-run-save-continue-design.md`、`2026-08-04-toast-component-design.md`、`2026-08-04-multiplayer-save-impact.md`
- 进行中计划表追加：`2026-08-04-run-save-continue-implementation-plan.md`

- [ ] **Step 3: Commit（仅当用户要求）**

---

## Self-Review

**规格覆盖**
- 单槽存档 / `user://saves/run` / 原子写复用 → Task 2 / 6
- 三按钮（保存 Toast / 保存并返回 / 快速读取）→ Task 3 / 5
- 战斗中禁存（保存系按钮 Disabled）→ Task 3
- 自动保存（阶段切换 + 退出前，静默）→ Task 2 / 3
- 放弃删档 → Task 2（RunRuntime.Abandon）
- 主菜单继续游戏（无档置灰）→ Task 4
- Toast 组件（对象池 / 堆叠 / 上移淡出 / 25% 位置）→ Task 1
- 联机影响 → 评估文档（非本次实现）
- 翻译键 5 个 → Task 5

**占位符扫描**：无 TBD / 待定；`RunRuntime` 测试注入方案（`InjectSaveService`）在 Task 2 标注，需实现时落地（否则测试环境 `ProjectSettings` 不可用）。

**类型一致性**
- `RunRuntime.SaveCurrent / TryLoadLatest / HasSave / ClearSave` 在 Task 2 定义，Task 3/4 使用。
- `ToastService.Show` 在 Task 1 定义，Task 3 使用。
- `RunController.EnableAutoSave` 在 Task 2 定义，Task 6 测试使用。
- `RunSaveClosureTests` 复用既有 `CombatTestHelper`（如需要）。
