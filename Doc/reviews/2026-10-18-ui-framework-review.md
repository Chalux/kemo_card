# UI 框架代码审查（2026-10-18）

审查范围：`Src/frame/ui/`（UiManager、状态机七件套、Def、Base、工具类）、`Src/frame/stateMachine/StateMachine.cs`、`Src/frame/util/{BoolVal,UIResourceLoader,GodotMainThreadSyncContext}.cs`，并以 `Src/mod/{global,run}/Ui/`、`Src/mod/run/RunMod.cs` 作为消费侧抽样。

方法：逐文件通读 + 交叉核对调用点与测试。**未**运行 `dotnet build` / `dotnet test`（本次仅静态审查），因此下文不涉及编译告警级别的问题。

结论：整体骨架质量高于同类 Godot 项目——状态机把"加载/预加载/创建/打开/关闭完成/销毁"拆成独立 handler，`BindingScope` 用账本替代散落的 `_eventsBound` 守卫，`UIAnimController` 用 version/cancel 账本处理"回调同步完成"这种经典竞态，`UILayerManager.UpdateLayers` 也有重入版本号。下面这些是仍然存在的口子，按严重度排列。

---

## 一、高优先级（会表现为"界面错乱 / 卡死 / 载荷丢失"，建议本迭代修）

### H1. `UIEvent.Open` 的订阅者抛异常会把**已经打开**的界面销毁

`Src/frame/ui/States/UIOpenStateHandler.cs:96-104`

```csharp
vo.CompleteOpen(vo, task);                       // 96：Task 已以成功结果交付
manager.EventDispatcher.Send(UIEvent.Open, …);   // 97：裸调用，无 try
openOpt.OnOpen?.Invoke(vo);                      // 98：裸调用（通常已被 OpenAsync 包成安全版本）
…
catch (Exception ex) {
    if (IsCurrentRequest()) vo.StateMachine.TransitionTo(EUIState.Destroy, context);  // 103
}
```

`IsCurrentRequest()` 只判断"状态仍是 Open 且 requestFlag 未变"，而这条路径上状态确实是 Open。于是事件订阅者一抛异常：

- `await OpenAsync(...)` 已经拿到非 null（成功），但界面紧接着被 Destroy；
- 界面的 `OnFail` 包装器也会被 Destroy 调一次（此时已完成，`TrySetResult` 无效），业务看到的是"打开成功 → 瞬间消失"。

需要说明的是：98 行的用户 `OnOpen` 其实**已被** `OpenAsync` 的包装器保护（`UiManager.cs:169-175` 内部走 `InvokeCallback`），所以真正裸露的是 97 行的 `EventDispatcher.Send`（以及"有人事后直接改 `vo.OpenOpt.OnOpen`"这种绕过包装的写法）。95 行的注释写的是"事件/观察回调重入不能把已经打开的请求改成失败"——重入防住了，异常没防住。

修复：

```csharp
UIManager.InvokeCallback(() => manager.EventDispatcher.Send(UIEvent.Open, new(vo)), vo.Id, "打开事件");
```

或 catch 里加 `if (vo.Lifecycle.OpenTime > 0 && vo.Runtime.UI != null) return;` 只记日志不再 Destroy。

### H2. `OnPreLoad` 的 `done()` 之后抛异常 → 已打开界面被 Destroy；且前两行在 try 之外

`Src/frame/ui/States/UIPreLoadStateHandler.cs:24-25, 29-35, 45-51`

```csharp
BaseWin win = vo.Runtime.UI!;      // 24：try 之外
win.Payload = vo.Payload;          // 25：try 之外
…
void Abort(Exception ex) {
    if (preloadFlag != vo.Load.PreLoadFlag || vo.IsClose) return;   // 47：不检查 completed
    … TransitionTo(EUIState.Destroy, context);
}
```

两个独立问题：

1. `TryComplete()` 检查 `completed`，`Abort()` 不检查。业务在 `done()` 之后（已经把流程推进到 Create/Open）再抛异常时，`vo.IsClose`（`CurrentState >= Close`）为 false → 已打开的界面被打进 Destroy。
2. 24-25 行在 try 之外。缓存重开时窗口可能已被业务 `QueueFree`，`win.Payload = …` 会抛 `ObjectDisposedException`；该异常穿出 `TransitionTo(PreLoad)`，而调用方可能是 `UIOpenCoordinator.OpenNext()`（`UIOpenCoordinator.cs:48-55`，无 try/catch）→ 异常最终同步冒到 `EnqueueOpen` → `OpenAsync` 的调用点。

修复：`Abort` 加 `if (completed) return;`；把 24-28 行整体挪进 try，或先 `if (!GodotObject.IsInstanceValid(win)) { … Destroy … return; }`。

### H3. 关闭链路没有超时兜底，`OnCloseAnim` 不回调会永久卡在 Close

`Src/frame/ui/States/UICloseStateHandler.cs:35-49`、`Src/frame/ui/UIOpenCoordinator.cs:68-91`

`CheckLoadTimeout` 只覆盖 `Load` / `PreLoad`：

```csharp
case EUIState.Load:
case EUIState.PreLoad:
```

而 Load 实际上是**同步**的（`Src/frame/util/UIResourceLoader.cs:9-51` 的注释记录了 2026-09-19 从 `LoadThreadedRequest` 改回同步的原因：后台线程加载无法创建 C# 脚本；`LoadScenesAsync` 返回的 Task 已完成）。所以 `UI_LOAD_TIMEOUT = 10000` 对 Load 几乎不可能触发，真正会挂起的是：

- 业务 `OnPreLoad(done, fail)` 既不 done 也不 fail —— 这个会被兜住（`LoadTime` 在 Load 阶段记录）；
- `OnCloseAnim(done)` / `OnOpenAnim(done)` 返回的 `done` 永不回调（Tween 被 kill、节点已离树、业务分支漏调）—— **没有任何兜底**。

Close 卡死后的后果比 Open 更糟：VO 状态停在 Close，`IsOpen == false`、`IsClose == true`，因此

- 不会进 `TickCacheDestroy`（只处理 Cache）；
- 不在任何 `CloseAll*` 的快照里（只取 `IsOpen`）；
- 再调 `Close(id)` 会被 `if (!vo.IsClose)` 挡掉。

即：这条 VO 彻底失控且无法回收。

修复：把 `CheckLoadTimeout` 扩展成按状态分组的看门狗，进入 Close 时记时间戳（现在 `Lifecycle.CloseTime` 被 Close 和 CloseDone 写两次，见 M9），超时强制 `TransitionTo(CloseDone)` 并打 Error；Open 阶段同理超时后走 Destroy。

### H4. `OpenChildAsync` 就地改写注册表里的共享 payload；非 Dictionary 时静默丢载荷

`Src/frame/ui/UiManager.cs:203-240`

```csharp
Dictionary<string, object?> voDict =
    curMeta?.ParentOpenPayload as Dictionary<string, object?> ?? [];   // 223
if (voDict.ContainsKey("pgeId")) voDict["pgeId"] = cur;                // 224-227
…
voForCur = voDict;                                                     // 239
```

- 若 `ParentOpenPayload` 本来就是 `Dictionary`，这里拿到的是**注册表里那个共享实例**，直接写入等于运行时改写启动期注册的路由元数据：同一个子界面第二次打开时 `pgeId` 还是上一次的值。
- 若不是 `Dictionary`，则新建空字典覆盖 `voForCur`，父界面收到的是空字典，原载荷被静默丢弃。
- `if (ContainsKey("pgeId"))` 才赋值——判断和写入不一致，形态上像实验残留。

修复：`var voDict = curMeta?.ParentOpenPayload is Dictionary<string,object?> d ? new Dictionary<string,object?>(d) : [];` 然后无条件 `voDict["pgeId"] = cur;`。

### H5. `OpenAsync` 在 `Init` 之前调用会直接 NRE，且没有任何提示

`Src/frame/ui/UiManager.cs:121-125`

`_registry` 在 `Init` 里才赋值，`_inited` 只用于防重入；`Instance` 在 `_Ready` 就赋值了。因此"节点已进树、`Init` 尚未调用"这个窗口期里，任何 `UIManager.Instance?.OpenAsync(...)` 都会在 125 行 `_registry.Get(id)` 抛 `NullReferenceException`，而全仓业务调用点都是 `UIManager.Instance?.`（`Src/mod/run/Ui/RunUiController.cs`、`GlobalModController.cs` 等），可空链在这里不起保护作用。

修复：入口加守卫，返回已失败的任务即可——语义与"路由未注册"分支一致。

```csharp
if (!_inited) { AppLog.Error($"UI 管理器尚未初始化，忽略打开UI<{id}>", "UI"); tcs.SetResult(null); return tcs.Task; }
```

---

## 二、中优先级（当前不触发，但属于"改动即炸"的结构性口子）

### M1. 是否把界面挂到场景树，取决于 `data != null`

`UIOpenStateHandler.cs:45-48`

```csharp
if (data != null) { vo.Runtime.AddToNode(); }
```

正常首开由 `UICreateStateHandler.cs:26` 挂树、重开由 Load 的缓存分支挂树，这里只是第二次调用（`AddToNode` 幂等）。但"挂树"是流程必需项，却由过渡载荷是否为空隐式决定；任何人用 `StateMachine.Enter(...)` 或自定义 handler 直连 Open 且不带 data，界面就不会上树。

建议改成显式判据：

```csharp
if (vo.Runtime.UI is { } w && w.GetParent() == null) vo.Runtime.AddToNode();
```

### M2. 三处 `UpdateLayers` 的调用方式不一致

- `UiManager.UpdateLayers()`（`UiManager.cs:450-453`）刻意走 `CallDeferred`；
- `UIOpenStateHandler.cs:92` 直接 `manager.LayerManager.UpdateLayers()`；
- `UICloseDoneStateHandler.cs:57` 同样直接调。

打开/关闭路径反而是同步重算。现在 `UpdateLayers` 只写 `Visible` 和 `BoolVal`，同步调无害；但它通过 `HideBool.OnChange → UpdateVisible → InvokeLayerVisibleUpdate()` 回调业务，业务一旦在这条链上增删节点，同步调用就落进 Godot 的"parent busy setting up children"窗口。建议三条路径统一走 `manager.UpdateLayers()`。

### M3. mask 有两处挂载点，且 `AddToNode` 的早退分支不迁移已在树上的 mask

- `UIRuntimeData.AddToNode()` 的 Parent 分支（`UIRuntimeData.cs:61-70`）会 `parent.AddChild(Mask)`；
- `UILayer.AddUI()`（`UILayer.cs:44-47`）也会 `AddChild(ui.UIVo.Mask)`。

Godot 4 对"已有父节点的 AddChild"直接报错，目前靠 `Mask.GetParent() == null` 与"两条路径互斥"来避免。另外 `UIRuntimeData.cs:40-49` 的 `UI.GetParent() == parent` 早退分支只补"mask 无父节点"的情况，mask 若挂在旧父下不会被迁移。

建议把 mask 挂载收敛到一处（推荐 `UILayer.AddUI`），Parent 分支改用 `Reparent()`。

### M4. `UILayer` 的字典键用 `ui.UIId`，移除时传的是 `vo.Id`

- `UILayer.cs:35, 41, 56-58`：`_uiMap[ui.UIId]` / `_uiMap.TryGetValue(id, …)`；
- `UIRuntimeData.cs:53, 94`：`oldLayer.RemoveUI(_owner.Id)` / `Layer?.RemoveUI(_owner.Id)`；
- `UiManager.cs:443`：`layerUIs[^1].UIId == Id`。

也就是说框架默认"`BaseUI.UIId` 恒等于路由 id"。业务侧目前确实如此（`RunUiIds.*` / `GlobalUiIds.*` 同时用于注册和 `UIId`），但这是**未强制的不变量**——`BaseCmp.UIId => GetType().Name` 就是反例，任何组件一旦进 `_uiSort` 就不同源。破坏后 `RemoveUI` 找不到条目，`_uiMap/_uiSort` 会残留已销毁节点，`IsUITop` 读到已释放对象。

建议 `AddUI` 里断言 `ui.UIVo?.Id == ui.UIId`（Debug 下抛），或统一以 `vo.Id` 作键。

### M5. `IsOpen` / `IsClose` 依赖枚举区间与顺序

`Src/frame/ui/UIVo.cs:50-51`

```csharp
public bool IsOpen  => StateMachine.CurrentState is >= EUIState.Create and <= EUIState.Open;
public bool IsClose => StateMachine.CurrentState >= EUIState.Close;
```

`EUIState` 现在的顺序（`Wait, Load, PreLoad, Create, Open, Close, CloseDone, Cache, Destroy`）刚好让两条区间成立。一旦有人在 Create/Open 之间插入状态，或在 Destroy 之后追加状态，语义会静默变化。另外 `IsClose` 把 Cache/Destroy 也算"关闭中"，命名有歧义。

建议改成显式集合，并补一条测试锁定状态语义：

```csharp
public bool IsOpen  => CurrentState is EUIState.Create or EUIState.Open;
public bool IsClose => CurrentState is EUIState.Close or EUIState.CloseDone;
```

### M6. `StateMachine.TransitionTo` 先调 OnExit、后写 `_currentState`，且无迁移表/重入保护

`Src/frame/stateMachine/StateMachine.cs:41-56`

```csharp
var prevState = _currentState;
if (…) prevConfig.OnExit(targetState, context);   // 此时 CurrentState 仍是旧值
_currentState = targetState;
if (…) targetConfig.OnEnter(prevState, context, data);
```

- OnExit 里读到 `CurrentState == prevState`，与"正在离开"的直觉相反；
- OnExit 里再 `TransitionTo(...)`，外层的 `_currentState = targetState` 会把内层结果覆盖掉；
- 任意状态可跳到任意状态（`Wait → Open` 也能走）。

当前所有 handler 的 OnExit 都是 null，属于潜在雷。建议先写状态再调 OnExit，并加 `_inTransition` 守卫（重入时打日志忽略）。

### M7. `TickCacheDestroy` 二次索引 `_map[id]`

`Src/frame/ui/UIVoRegistry.cs:61-65`

先收集 `toDestroy` 快照（正确），但执行阶段又 `_map[id]` 取一次。如果第一个 Destroy 的重入路径（`OnFail` 里再开/再关界面）把后面某个 id 移除了，这里会抛 `KeyNotFoundException`，而它发生在 `_Process` 里。

```csharp
foreach (var id in toDestroy)
    if (_map.TryGetValue(id, out var vo) && vo.StateMachine.CurrentState == EUIState.Cache)
        vo.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(vo, _manager));
```

### M8. `BindingScope.Bind` 先订阅后登记，订阅抛异常会留下无主订阅

`Src/frame/ui/BindingScope.cs:37-44`

```csharp
subscribe();
_unbinders.Add(unsubscribe);
```

`subscribe()` 抛异常（Godot 的 "signal already connected"、事件总线重复注册等）时，`unsubscribe` 没进账本；若订阅是**部分成功**（多信号注册中途失败），残留订阅永久漏出框架之外——正是 `BindingScope` 想根除的那类缺陷。建议：

```csharp
_unbinders.Add(unsubscribe);
try { subscribe(); }
catch { _unbinders.Remove(unsubscribe); throw; }
```

（`UnbindAll` 的逆序 + 单条异常隔离 + 递归安全，这三处写得很好，不用动。）

### M9. `Lifecycle.CloseTime` 被写两次，关闭耗时不可测

`UICloseStateHandler.cs:23` 与 `UICloseDoneStateHandler.cs:30` 都写 `CloseTime`。前者是"开始关闭"，后者是"关闭完成"，覆盖后无法区分，任何基于时间戳的诊断/统计都会失真。建议拆成 `CloseStartTime` / `CloseTime`。

顺带：`UILifecycleState.IsOpened/IsClosed`（`UILifecycleState.cs:14-18`）一旦为 true 永不复位，且全仓无读取方，建议删除或改成基于状态机的判断。

### M10. 两套 `IUIStateContext` 实现

`UIVo` 自己实现了 `IUIStateContext`（`UIVo.cs:12, 47-48, 71-74`），另外还有 `UIStateContext` 包装类（`UiManager.cs:551-556`）。调用点混用两者。建议只留 `UIVo` 实现，或让包装类成为唯一入口，减少"两个 context 里 `UIManager` 指向可能不同"的想象空间。

### M11. 早退失败回调的语义不一致

`UiManager.cs:129` 与 `138` 只回调 `openOpt?.OnFail`（调用点传入的那份），而 `145-150` 的 SkipOpenCheck 分支回调 `finalOpt.OnFail`（合并后的）。路由缺失/层级缺失时，注册项或层级上配置的 `OnFail` 不会触发。建议三处统一用 `finalOpt`（路由缺失时用 `MergeOpenOpt` 的前半段结果）。

### M12. `UIOpenOpt` 里有一批"声明了但没人读"的字段

grep 全仓确认：

| 字段 | 读取方 |
| --- | --- |
| `UIOpenOpt.Align` / `EffectiveAlign` | 无（`DefaultUIOpenOpt.cs:33,43,48` 设了值，无人消费） |
| `UIMaskOpt.ClickClose / HideTipText / CloseMinTime / Alpha / Color / Payload` | 仅 `UIOpenOpt.Clone()` 内出现 |
| `UIPopPosOpt`（整类） | 无 |

注释里已经写了"预留参数，框架当前不自动定位"，但调用点写 `Align = Full` 时并不会报错也不会生效。建议：要么实现（`Align` 只需在 `AddToNode` 后调 `SetAnchorsAndOffsetsPreset`），要么加 `[Obsolete("框架未实现")]` 或直接删掉，避免"配置了却不生效"的误判。

### M13. 缓存链路当前是"没有使用者的能力"

业务侧注册全部显式 `CacheTime = 0`（`Src/mod/run/RunMod.cs:177-202`，七处），即关闭即销毁。于是 `Cache` 状态、`TickCacheDestroy`、`CacheTime = -1` 永久缓存分支、`CacheTime == null → 30000` 的默认值，**在当前工程里都不会被走到**。这不是 bug，但意味着这条链路没有真实场景验证；建议要么在文档里标为"未验证能力"，要么补一个 headless 用例（现有 `UiFrameworkHeadlessTests` 主要覆盖 HideBelow/BackAsync/IsUITop/NoCover）。

### M14. `CloseAllExclude` 的两个小问题

`UiManager.cs:282-291`

- `vo.Runtime.Layer?.IsTop` 是"该层是否是顶层组"，不是"该界面是否置顶"，`CloseAllExclude` 因此会跳过**整个 TopLayers 组**里的所有界面（可能符合意图，但命名容易误读）；
- `excludeIds.Contains` 是线性查找。exclude 通常只有 1-2 个，影响可忽略，列出来只是为了让"已读"完整。

### M15. 单位与类型的小坑

- `UIConsts.UI_LOAD_TIMEOUT = 10000` 是 `double`，与 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` 的 `long` 比较（`UIOpenCoordinator.cs:80`），单位靠猜。建议改名 `UI_LOAD_TIMEOUT_MS` 并改 `long`/`int`。
- `UIOpenOpt.CacheTime` 是 `int?`，注释用 `-1` 表示"永久"（`UIOpenOpt.cs:65`），`UICloseDoneStateHandler.cs:41` 用 `== -1` 判定。建议改成具名常量或枚举（`CacheForever`）。
- `SkipOpenCheck` 返回 true 表示"跳过打开"，与"OpenCheck 通过"的直觉相反（`UIOpenOpt.cs:100-105`）。建议改名 `ShouldSkipOpen`。

### M16. `BoolVal` 的两个味道

`Src/frame/util/BoolVal.cs`

- `Destory()`（26 行）拼写错误，且被 `UIDestroyStateHandler.cs:36` 调用。改名会影响调用点，建议新增 `Destroy()` 并把旧名标 `[Obsolete]`；
- `Set(object from, bool value)` 的键是 `object`，`_sources` 只增不减（VO 销毁时会 `Destory` 清空，可接受）。建议键改成 `string` 或强类型 `struct`，配合 `Remove(from)` 的显式调用纪律。

---

## 三、可改进的设计（非缺陷，但会持续产生缺陷）

1. **"请求仍然有效"的守卫有四套实现**：`loadFlag`（Load）、`preloadFlag`（PreLoad）、`requestFlag`（Open/Close 各自的 `IsCurrentRequest`/`IsCurrentClose`）。它们语义相同、失效条件不同，H2 就是其中一套少检查了一个变量。建议在 `UIVo` 上收敛成一个 `UIRequestToken`（自增 int + `IsCurrent(token)`），四个 handler 共用。

2. **`UIManager` 仍是 562 行的半上帝对象**。内部已拆出 `LayerManager/VoRegistry/OpenCoordinator`，但 `OpenAsync` 仍有 6 个早退分支 + 闭包包装，`OpenChildAsync` 里还有循环。建议抽出 `OpenRequestBuilder`（校验 → 合并 → 失败回调）与 `OpenRequest.Commit()`，让 `OpenAsync` 只剩"编排"。

3. **静态单例阻碍测试与多实例**。`BaseWin.Close()`/`IsUITop()`（`BaseWin.cs:124-132`）直接读 `UIManager.Instance`；业务全部 `UIManager.Instance?.`。建议 `BaseWin` 改走 `UIVo.Manager`（已经能拿到），`UIManager` 对外暴露 `IUIManager` 注入。

4. **`GodotMainThreadSyncContext` 是全局单泵**：`Install()` 后全工程所有 `await` 续体都排进这个队列，唯一泵是 `UIManager._Process`，每帧上限 32 条。`Send()` 在泵停止时用 `ManualResetEventSlim.Wait()` 永久阻塞（`GodotMainThreadSyncContext.cs:34-46`）。全仓目前没有 `GetTree().Paused = true`（已 grep 确认），所以还没踩到；但一旦引入暂停或把 UIManager 移出树，所有异步逻辑会一起冻住。建议给 `Pump` 加独立的常驻节点（或 `ProcessMode = Always`），并把 `Send` 改成"检测到非主线程时抛异常"而不是阻塞等待。

5. **`BaseUI.GetScenePath()` 是死代码且与实际加载路径不同源**：它用 `UIDir/UIId` 拼 `res://…`，真正加载走注册表的 `ScenePath`（`UiManager.GetPath`）。全仓无调用者，建议删。

6. **日志写法**：框架里 `AppLog.Error($"…{ex.Message}", "UI")` 遍布，每次失败都有字符串插值分配；且全部中文硬编码（项目规范只约束"用户可见文案"，日志不受限，但中英混排会让 grep 变难）。建议抽 `UiLog` 帮助类统一前缀（`UI 管理器:` / `UI<id>` 两种前缀目前混用）。

7. **`UIStateContext` 与 `UIVo` 双重实现**见 M10；`UIDestroyStateHandler` 里 `HideBool.Destory()` 与 `UpdateLayers()` 的顺序依赖也值得注释固化（销毁后必须重算可见性，否则 HideBelow 链残留）。

---

## 四、测试覆盖现状与建议

已有（`Tests/kemo_card.Ui.Tests/`，dotnet test 可跑）：`UiStateMachineTests`、`UiLifecycleRegressionTests`、`UiFrameworkTests`、`UiManagerDlgSwitchTests`、`UiOpenOptDefaultsTests`、`UiRegistryTryGetTests`、`UiSceneLayoutTests`、`BindingScopeTests`、`FitScaleMathTests` 等。

另有 Godot 场景级集成测试 `Tests/kemo_card.Ui.Headless/UiFrameworkHeadlessTests.cs`，其中 `HideBelowAndBackAsync`（:779-787）、层顶判定（:811-813）覆盖了 `BackAsync` 与 `IsUITop` 的 NoCover/TopLayers 组合。

建议补的用例（对应上文缺陷）：

| 用例 | 覆盖 |
| --- | --- |
| `OnOpen` 回调抛异常后界面仍存活 | H1 |
| `OnPreLoad` 的 done 之后再抛异常 | H2 |
| `OnCloseAnim` 不回调时的超时回收 | H3（需先实现超时） |
| `OpenChildAsync` 连续两次打开同一子界面，父载荷不被污染 | H4 |
| `Init` 之前调用 `OpenAsync` 返回 null 且有日志 | H5 |
| 缓存界面 `Close()` 幂等（当前靠 `IsClose` 兜住，但没有测试锁定） | M5 |

---

## 五、逐文件速览

| 文件 | 评价 |
| --- | --- |
| `UiManager.cs` | 拆分得当，但 OpenAsync/OpenChildAsync 分支多、早退语义不全（M11、H4、H5） |
| `UIOpenCoordinator.cs` | 队列/超时/重入基本正确；超时覆盖面窄（H3） |
| `UIVoRegistry.cs` | 简单清晰；`TickCacheDestroy` 二次索引需改（M7） |
| `UIVo.cs` | 组合容器设计好；`IsOpen/IsClose` 依赖枚举顺序（M5） |
| `UIRuntimeData.cs` | 幂等挂载有考虑，但 mask 双挂载点（M3） |
| `UILayer.cs` | 结构简单；键值不变量未强制（M4） |
| `UILayerManager.cs` | 版本号重入守卫写得很好，是全场最稳的一段 |
| `UIOpenOpt.cs` | 合并/克隆语义正确（显式覆盖），但死字段多（M12） |
| `BindingScope.cs` | 账本设计正确，逆序+异常隔离到位；`Bind` 顺序需调整（M8） |
| `BindingScopeSignals.cs` | 语义化糖，未见问题 |
| `UIAnimController.cs` | version/cancel 账本处理了"同步完成覆盖"这个经典 bug，质量高 |
| `StateMachine.cs` | 极简，缺迁移表与重入保护（M6） |
| `States/*`（7 个 handler） | 失效守卫细致，但四套 flag 不统一（H1/H2 的直接成因） |
| `BaseUI/BaseWin/BaseMask/BaseCmp` | 生命周期收敛干净，`sealed _ExitTree` + `OnExitTree` 的约定很好 |
| `UIResourceLoader.cs` | 同步加载的决策与原因有注释，可维护性好 |
| `BoolVal.cs` | 逻辑正确，命名/类型可收敛（M16） |
| `GodotMainThreadSyncContext.cs` | 全局单泵 + `Send` 阻塞是隐患（设计建议 4） |

---

## 六、如果要我接着做

按性价比排序：H1 → H2 → M7 → M11 → H5（都是小改动、低风险），然后是 H4 与 H3（需要改行为，建议先补测试）。M12/M13 属于"删能力"或"补实现"的产品决策，需要你拍板。

---

## 七、修复记录（2026-10-18 当日实施）

上文 H1–H5、M1–M16 已全部落地；未做 git commit（按 `Doc/AGENT.md`）。

验证：`dotnet build kemo_card.csproj` 0 错误（仅 `addons/dialogue_manager` 既有 23 条警告）；`dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj` **1681/1681 通过**；Godot 场景级集成套件 `Tests/kemo_card.Ui.Headless/run.ps1` → `UI_HEADLESS_SUMMARY total=36 failed=0`、退出码 0。

| 编号 | 修复方式 |
| --- | --- |
| H1 | `UIOpenStateHandler` 新增 `published` 标记（`CompleteOpen` 之后置真）：`catch` 只在 `!published` 时转 `Destroy`；`EventDispatcher.Send(UIEvent.Open, …)` 与 `OnOpen` 都包进 `UIManager.InvokeCallback`。 |
| H2 | `UIPreLoadStateHandler` 把 `Runtime.UI` 取用与 `win.Payload = …` 移进 try，并先做 `IsInstanceValid` 检查；`Abort` 增加「已完成且 `OpenTime > 0` 则不回滚」。 |
| H3 | 新增 `UIConsts.UI_CLOSE_TIMEOUT_MS = 5000` 与 `UILifecycleState.CloseStartTime`；`UIVoRegistry.Tick` 里 `ForceCloseTimeout` 对停在 `Close` 超过阈值的 VO 强制 `CloseDone` 并记 `AppLog.Error`。 |
| H4 | `OpenChildAsync` 不再就地改写注册表里共享的 `ParentOpenPayload`：命中 `Dictionary<string, object?>` 时先 `new(...)` 复制再写 `pgeId`；非字典载荷不再被静默替换为空字典。 |
| H5 | `OpenAsync`/`OpenChildAsync` 开头加 `_inited` 守卫：记 `AppLog.Error` 并回调 `OnFail`（返回 `null` 的 Task），不再 NRE。 |
| M1 | 挂树判据由 `data != null` 改为「`Runtime.UI` 存在且当前无父节点」，直连 `Open` 也能正确上树。 |
| M2 | 三处可见性刷新统一走 `UIManager.UpdateLayers()`；该方法由 `CallDeferred` 改为**同步 + `_updatingLayers` 重入守卫**（集成测试 `HideBelowAndBackAsync` 要求 `await OpenAsync(...)` 之后同帧可见，异步化会破坏该契约）。 |
| M3 | `UIRuntimeData` 新增 `MountMask(parent)`：mask 不在同一父节点下才挂载，并按 `GetIndex()` 调整到界面之前（`MoveChild`），`UILayer.AddUI` 不再挂 mask，消除双重挂载点。 |
| M4 | `UILayer` 统一键为 `vo.Id`（`KeyOf(BaseWin) => UIVo?.Id ?? UIId`）；`AddUI` 先删同键旧条目，避免 `UISort` 重复。 |
| M5 | `UIVo.IsOpen/IsClose` 改为显式枚举成员判断（`Create/Open`、`Close/CloseDone/Cache/Destroy`），不再依赖 `EUIState` 的声明顺序。 |
| M6 | `StateMachine.TransitionTo` 改为**先写 `_currentState` 再调 `OnExit`**（`OnExit(targetState, …)` 里读到的状态与参数一致），并补 remarks 说明无迁移表。 |
| M7 | `UIVoRegistry.TickCacheDestroy` → `Tick(delta)`：先取 `UIVo[]` 快照，再用 `TryGetValue` + `ReferenceEquals` 二次校验后才 `TransitionTo(Destroy)`。 |
| M8 | `BindingScope.Bind` 改为**先登记 unbinder 再 subscribe**，subscribe 抛异常时回滚登记并重新抛出。 |
| M9 | 关闭开始写 `CloseStartTime`，`CloseTime` 只在 `CloseDone` 写一次，关闭耗时可测（也是 H3 超时的依据）。 |
| M10 | 删除 `UiManager.UIStateContext`，一律直传 `UIVo`（其本身实现 `IUIStateContext`）；`IUIManager.IsUITop` 参数名统一为 `id`。 |
| M11 | 新增 `UIManager.InvokeOpenFail`：路由/层级早退失败也回调「合并后」的 `OnFail`，与加载失败路径语义一致。 |
| M12 | 只加文档：`UIMaskOpt` 的 `Alpha/ClickClose/HideTipText/CloseMinTime/Color/Payload` 标注「框架未消费，需在 `BaseMask` 子类自行实现」。未补实现（属产品决策）。 |
| M13 | 保持原状：缓存链路（`CacheTime > 0`）目前无业务使用者，本次不动，避免引入无人验证的路径。 |
| M14 | `CloseAllExclude` 改用 `HashSet<string>(excludeIds, StringComparer.Ordinal)`。 |
| M15 | `UIConsts` 改为 `long` 且单位入名（`UI_LOAD_TIMEOUT_MS`），与 `Lifecycle` 的毫秒时间戳同类型比较。 |
| M16 | `BoolVal` 新增 `Destroy()`；旧 `Destory()` 保留为 `[Obsolete]` 转发，调用点已切换。 |

顺带做掉的低风险设计建议：

- 建议 3（部分）：`BaseWin.Close()`/`IsUITop()` 优先走 `UIVo.Manager`，仅在 `UIVo` 缺失时才回退 `UIManager.Instance`。
- 建议 4（部分）：`UIManager._Ready` 设 `ProcessMode = ProcessModeEnum.Always`，宿主暂停时同步上下文泵不会停；`Send()` 的阻塞语义未改。
- 建议 5：删除死代码 `BaseUI.GetScenePath()`。
- 建议 7：随 M10 解决；`UIDestroyStateHandler` 末尾改为走 `manager.UpdateLayers()` 统一入口。

未做（需要产品/架构决策）：建议 1（统一请求 token）、建议 2（`OpenAsync` 拆 `OpenRequestBuilder`）、建议 6（日志帮助类）、M12 的字段实现、M13 的缓存链路。

测试基建顺手修了一处：`Tests/kemo_card.Ui.Headless/run.ps1` 以前在「全部通过」时也可能报退出码 1 —— Godot 会把刻意注入的业务异常写到 stderr，PowerShell 把原生 stderr 转成终止性 `NativeCommandError`。现在调用引擎时临时降级 `$ErrorActionPreference`，成败只由退出码与 `UI_HEADLESS_SUMMARY` 判定；另注意该文件必须保留 UTF-8 BOM（Windows PowerShell 5.1 会按 ANSI 解码，无 BOM 时中文注释会破坏解析）。
