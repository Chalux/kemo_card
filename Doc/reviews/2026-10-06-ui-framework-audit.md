# UI 框架审查（2026-10-06）

## 结论

现有结构值得保留：`UIManager` 作为入口，层级、VO 注册表、打开队列、动画和生命周期处理器各有清楚的职责；`BindingScope` 与 sealed 离场入口也已经统一了订阅模型。没有证据支持整体重写、继续细拆服务或引入新的通用 UI 框架。

发现的缺陷主要在组件之间的连接与生命周期边界。最应先处理的是层级默认值覆盖窗口默认值，以及隐藏状态没有更新实际节点。其余问题大多需要异步预加载、自定义回调、遮罩或功能卸载才会触发，以下明确区分影响范围。

下文保留原始审查结论与复现证据，位置行号对应审查时的源码。用户随后授权修复并明确 D1 为 Dlg 多实例并存；修复状态与正式验证见文末。原有未提交改动保留。

## 范围与验证

- 检查 `Src/frame/ui` 的管理器、状态处理器、层级、缓存、导航、注册表、基础界面、动画、参数合并、订阅及 `FitScaleBox`；同时核对资源加载器、主线程上下文和业务调用。
- 抽查 Global 复用组件、虚拟列表、Run 的界面注册与退出流程，核对 UI 与运行时规格的相关条款。
- 现有 UI 相关测试：**79 通过、0 失败**。覆盖框架纯逻辑、归属、参数合并、状态机、BindingScope、场景路径/布局、缩放数学和虚拟列表布局。
- Godot **4.7.2 Mono headless**：12 个边界检查，11 个观察结果未满足所测试的契约，1 个“上层关闭后下层仍可见”的检查通过。由于此前隐藏失败，这个通过项不能单独证明恢复流程正确。部分检查指向同一个根因，不能把 11 当成独立缺陷数。
- 临时 NUnit 边界测试：3 个，分别复现路由替换残留、Dlg 最新请求未抢占、递归解绑重复执行。Dlg 用例按现有规格断言，属于设计与实现差异；递归解绑属于可选加固。
- headless 探针创建最小 C# 界面并预置缓存 VO，复用实际管理器和默认生命周期处理器，不加载游戏业务场景。验证了真实 Godot 节点的隐藏、释放和离树行为；未做视觉、鼠标/键盘焦点或完整玩家流程验收。
- 编译成功；探针构建中的 23 条警告来自既有 `addons/dialogue_manager`。headless 日志中的 MCP 插件注册目录写入警告与 UI 检查无关；`audit exit failure` 是刻意注入的离场异常。

证据目录：`Doc/reviews/2026-10-06-ui-audit-results/`。

## 已确认的问题

### R1 · P1：未配置层级选项时，窗口类型默认值被覆盖

位置：`Src/frame/ui/UILayerManager.cs:84`；合并处 `Src/frame/ui/UiManager.cs:493`。

`BuildLayers` 把未配置的层级选项设为完整的 `DefaultUIOpenOpt.Value`。合并顺序是基线 → 类型默认 → 层级选项，因此本来由 `ForType(Win)` 设置的 `HideBelow=true`、`Align=Full` 又被层级的 `false`、`Center` 覆盖。

`MainRoot` 正好没有配置 `LayerOpenOpts`，所以这是当前默认路径的问题。`MergeOpenOpt` 最后会把 `Layer` 修正为真实层级，层级本身没有错；错误在其余默认字段。由于 `UIOpenStateHandler` 只在 `HideBelow=true` 时压导航栈，普通窗口也不会自动进入导航栈。

运行结果：`layer=Win, HideBelow=False, Align=Center, inNavStack=False`。

建议：层级没有显式覆盖时使用空覆盖对象或不参与该层合并，完整默认值只作为一次基线。保留现有 `MergeFrom`，增加通过真实 `UIManager.Init/OpenAsync` 的默认窗口测试；目前仅测 `ForType` 和 `MergeFrom` 无法发现装配问题。

### R2 · P2：同层 HideBelow 只更新标志，没有隐藏节点

位置：`Src/frame/ui/UIRuntimeData.cs:19`、`:66`；`Src/frame/ui/UILayerManager.cs:57`。

层级自身给 `HideBool.OnChange` 注册了回调，但每个 VO 的 `Runtime.HideBool` 没有对应连接。`UpdateVisible()` 没有调用者，所以同层下方窗口仍然显示，`OnLayerVisibleUpdate` 也不会被通知。

为与 R1 区分，探针显式给上层窗口传入 `HideBelow=true`。结果仍然是：`hideFlag=True, lower.Visible=True`。跨层的整层隐藏有回调，这一问题专指同层窗口。

建议：把运行时隐藏状态接到 `UpdateVisible`，同时在挂载/重开时同步一次实际状态；测试同层隐藏、上层关闭后的恢复，以及缓存重开。

### R3 · P2：缓存窗口的遮罩被释放，重开抛异常

位置：`Src/frame/ui/UIAnimController.cs:115-116`；缓存入口 `States/UILoadStateHandler.cs:37-42`。

遮罩关闭动画完成后无条件 `QueueFree()`，窗口却可以进入 Cache。`Runtime.Mask` 仍持有被释放的对象；缓存重开时不重新创建遮罩，`AddToNode`/`UILayer.AddUI` 又访问这个引用。

运行结果：窗口处于 `Cache` 且 UI 仍有效，Mask 已失效；重开抛出 `ObjectDisposedException`。

目前没有发现业务配置 `OpenOpt.Mask`，因此不是现有所有弹窗都会发生的问题，而是框架提供的带遮罩缓存能力存在缺陷。

建议：让生命周期处理器统一决定窗口和遮罩的缓存/销毁，动画只负责完成通知。若遮罩选择每次重建，应清空旧引用并在缓存重开时重新创建；若一起缓存，应协调关闭动画结束后的脱离节点，不能只删除 `QueueFree` 而留下遮罩继续拦截输入。

### R4 · P2：旧预加载的失败回调会销毁已成功重开的界面

位置：`Src/frame/ui/States/UIPreLoadStateHandler.cs:39-43`。

成功回调检查了 `preloadFlag` 和当前状态，失败回调却直接转 Destroy。同一 VO 连续打开时，第二轮已经成功 Open，第一轮迟到的 fail 仍然会销毁第二轮的界面。

运行结果：最新打开 Task 已完成，随后旧 fail 令 VO 进入 `Destroy` 并从注册表移除。

当前业务没有覆写异步 `OnPreLoad`；这是该扩展点启用后的确定性竞争问题。建议 done/fail 共用同一轮次和状态检查，并确保每轮只接受一次终态回调，无需新增一套状态机。

### R5 · P2：关闭正在预加载的缓存重开界面，打开 Task 不结束

位置：`Src/frame/ui/UiManager.cs:251-254`。

`Close` 用 `Lifecycle.OpenTime==0` 判断是否需要结束打开任务，但这个时间戳记录的是 VO 曾经成功打开，不是本次请求是否完成。缓存重开处于 PreLoad 时关闭，旧时间戳仍非零；关闭又进入 Cache，后续没有完成此次 Task 的路径。

运行结果：`state=Cache, pending.IsCompleted=False`。默认缓存到期最终可能销毁并结束 Task，但正常关闭不应延迟 30 秒；永久缓存时可以无限等待。

建议：按本次未完成的打开请求收尾，关闭时结束仍挂起的 `OpenTaskSource`，不要用历史时间戳代替请求状态。

### R6 · P2：用户回调异常阻断 Task 完成和队列收尾

位置：`Src/frame/ui/UiManager.cs:169-181`；`States/UIOpenStateHandler.cs:84-85`、`States/UIDestroyStateHandler.cs:36-40`。

OnOpen/OnFail 包装器先调用用户回调，随后才完成 TCS。用户回调抛异常时，TCS 不会被完成；外层后续的 `OpenNext` 或销毁尾部清理也可能被跳过。失败探针的 VO 已 Destroy 并移除，Task 仍未完成，无法由缓存检查补救。

运行结果分别为 `state=Open, pending.IsCompleted=False` 和 `state=Destroy, pending.IsCompleted=False`。

建议：内部任务与队列收尾通过 `finally` 保证执行，并明确用户回调异常的记录/传播策略。可以考虑 `RunContinuationsAsynchronously` 减少任务完成时的同步重入，但它不能代替异常收尾。

### R7 · P2：子类离场清理抛异常时，统一解绑保证失效

位置：`Src/frame/ui/Base/BaseUI.cs:60-63`；同样模式见 `BaseMask.cs:64-67`、`FitScaleBox.cs:38-40` 和部分组合组件。

sealed `_ExitTree` 先调用子类的 `OnExitTree`，之后才执行 Binder 解绑和总线清理。只要子类清理抛异常，框架清理就会被跳过。

探针通过真实 `RemoveChild` 触发离树，在子类 hook 注入异常；结果 `BindingCount=1`。Godot 记录了异常，但没有替框架继续执行剩余 C# 语句。

建议：保持既有 hook 顺序，把框架必须执行的清理放入 `finally`。这是补足当前“统一解绑”契约，无需改动订阅模型。

### R8 · P2：卸载当前预加载界面的 owner 后，打开队列停滞

位置：`Src/frame/ui/UiManager.cs:374-376`；`UIOpenCoordinator.cs:75-96`。

`UnregisterOwner` 对未打开 VO 直接 Destroy，没有移除打开队列项、重置当前项和推进下一项。当前项停在 Destroy 后，`CheckLoadTimeout` 只处理 Load/PreLoad，不会推进等待中的另一个 owner。

运行结果：`current=audit.unregister-current, currentState=Destroy, queuedState=Cache`，下一次打开请求的 Task 仍待完成。需要额外的 Open/Close/OpenNext 才可能恢复调度。

没有发现现有业务调用 `UnregisterOwner`，所以这是功能卸载 API 的缺漏。建议批量移除该 owner 的调度项，再统一推进一次队列，避免销毁过程中重新启动同 owner 的其他排队项。

### R9 · P2：IsUITop 没有纳入 TopLayers

位置：`Src/frame/ui/UiManager.cs:422-427`。

查询只扫描 `_layers`，忽略实际显示在更上面的 Debug/Notice/Guide。查询 TopLayers 中的界面时，`Array.IndexOf` 还会返回 -1，反而把普通层当作上层检查。

探针打开普通 Win 和 `NoCover=false` 的 Debug 层界面：结果 `lowerIsTop=True, debugIsTop=False`。这里证实的是 API 判定错误；未据此断言某个现有业务快捷键一定错误，因为当前 Run 快捷键另有自己的策略。

建议：查询与显示排序使用同一份全层级顺序，继续尊重 `NoCover`；覆盖普通层、顶层和透传顶层三种用例。

### R10 · P3：同 owner 重新注册无父路由的条目时，旧路由残留

位置：`Src/frame/ui/UIRuntimeRegistry.cs:67-74`。

同 owner 替换注册是已有测试明确允许的行为，但新条目移除 `RouteMeta/ParentId` 时，旧 `_parentMap` 不会删除，已生成的 `_childrenMap` 也不会失效。`Get` 返回新条目，父子路由查询却仍返回旧关系。

纯逻辑探针结果：`GetParentId(child)` 仍为 `root`，`GetChildren(root)` 仍包含 `child`。

当前界面没有启用父子路由。建议替换时统一重建该条目的父关系，并使子关系缓存失效；修复很小，无需改路由 API。

## 需要澄清的设计差异

### D1：Dlg 单实例/最新请求优先与现有叠加业务不一致

UI 规格 §3.2 规定同 Dlg 层最多一个打开实例、准备新实例后替换旧实例、最后一次有效请求优先并取消旧加载。实际协调器是 FIFO，只去掉相同 VO 的排队重复，没有不同 Dlg 的抢占或替换；层级也允许多个不同 ID 的 Dlg 同时存在。临时 NUnit 用例确认第二个请求不会取消第一个。

但业务确实从图鉴/角色/卡组弹出详情，也从设置弹出 Alert。如果直接按旧单实例规则关闭底层界面，会改变这些流程和返回体验。因此本次把它列为规格与业务设计差异，而不是机械建议实现单实例。

建议先以当前需要的叠加行为明确 Dlg 契约并补真正的调度测试；若后来确实需要独占替换，再加入明确策略。现有 `UiManagerDlgSwitchTests` 仅验证泛型状态机顺序，名称并不代表已经验证 Dlg 切换。

## 可选改进与暂不处理的事项

1. **BindingScope 重入加固**：纯逻辑探针确认，解绑动作递归调用 `UnbindAll` 时同一动作执行两次；解绑中新增的条目也可能被最后的 Clear 遗忘。现有解绑动作大多是普通事件移除，没有发现这种调用，因此不列为当前业务缺陷。若需要支持重入，可先清空账本、再逆序处理快照，明确新登记条目的归属。
2. **标明预留 API 的实现范围**：`EffectiveAlign` 没有框架消费点；`Pop.Pos/LimitInScreen` 没有定位实现；`NoCover` 目前用于顶部判定，没有在可见性遍历中使用。当前主要依赖场景自身布局，Tooltip/Toast 也有独立实现，不必为了填满选项立刻增加布局服务。可以先注明哪些是预留能力，避免新界面误以为设置即生效。
3. **优先补集成契约测试**：目前纯测试较充分，但管理器装配、真实节点缓存、异常收尾覆盖不足。把 R1/R2、缓存重开和任务收尾变成少量稳定的 headless 测试，比继续增加手动调用状态机的测试更有价值。

未将以下内容列为缺陷：主线程同步加载（规格明确记录的 Godot workaround）、内容注册表的 frame 级共享读取、当前简单继承链、业务组件暂时位于 Global 的目录安排，以及没有当前需求的通用焦点/导航系统。

## 建议的处理顺序

1. 先修 R1、R2，验证 Menu → Run → Combat → 返回的显示与导航行为。
2. 修 R4、R5、R6、R7 的请求与清理边界，保留现有类职责；补实际管理器的异常/重开测试。
3. 按需要补齐遮罩、功能卸载、TopLayers 查询及路由替换（R3、R8、R9、R10）。
4. Dlg 策略先确认业务契约；其余可选项随实际使用推进。

## 证据与复现材料

| 文件 | 内容 |
| --- | --- |
| `ui-audit-baseline.trx` | 审查开始时现有 UI 相关 79 项测试结果 |
| `ui-audit-clean.trx` | 临时探针移除后重跑相同 79 项测试的结果 |
| `godot-ui-probes.log` | 最终 Godot headless 12 个检查及运行日志 |
| `UiFrameworkAuditProbe.cs.txt` | headless 探针源码；复现时还原为原 `Src/frame/ui/UiFrameworkAuditProbe.cs`，编译后运行归档场景 |
| `UiFrameworkAuditProbe.tscn.txt` | headless 场景入口的文本归档 |
| `ui-audit-boundaries.trx` | 临时 NUnit 3 个边界用例的失败结果 |
| `UiFrameworkAuditProbeTests.cs.txt` | 临时 NUnit 用例源码 |
| `build-probes.log` | 最终 headless 探针的构建日志 |

临时 NUnit 失败结果是用于证明问题的审查证据，失败用例没有留在正式测试目录。复现 headless 时需恢复上述临时源码和场景，完成后移除，并重新构建正常项目。

## 修复与改进结果（2026-10-06）

保留管理器、层级、VO、调度器和生命周期处理器的现有分工，修复组件之间的契约，未增加通用布局服务或改写业务场景。

| 项目 | 已实施的处理 | 正式验证 |
| --- | --- | --- |
| R1 | 未配置层级选项时使用空覆盖；VO 持有参数克隆 | `WindowDefaults` |
| R2 | HideBool 变化更新真实 UI/Mask 可见性及生命周期 hook | `HideBelowAndBackAsync`、`CrossLayerVisibility` |
| R3 | 动画控制器不释放遮罩；窗口和遮罩都结束动画后统一移出/缓存/销毁 | `MaskCacheAsync`、两种动画等待顺序、`ColdMaskLifecycleAsync` |
| R4 | done/fail 共用轮次、状态和单次完成检查；重开立即失效旧加载与动画回调 | `StalePreloadFailure`、`ReopenCancelsOldClose`、`DestroyDuringCloseAsync` |
| R5 | 关闭直接结束本次挂起请求，不以历史 OpenTime 判断；缓存重开刷新加载期限 | `CloseCachedPreload`、`CachedPreloadGetsFreshDeadline` |
| R6 | TCS 使用异步 continuation；结果先完成，再安全调用观察回调；旧闭包仅持有旧 TCS，finally 推进队列 | `ThrowingCallbacksAdvanceQueue`、`CallbackReentryPreservesRequest`、`TimeoutCallbackReentry`、`CloseFromOpenBefore` |
| R7 | BaseUI/BaseMask/FitScaleBox 及复用组件在 finally 中执行框架解绑；KeywordTipService 保证移除锚点 watcher | 三种 `ExitFailureStillUnbinds` 检查 |
| R8 | 先删除 owner 声明和全部调度项，再清理 VO，finally 推进其他 owner | `UnregisterOwnerAdvancesQueue`、注册表单元测试 |
| R9 | IsUITop 与显示排序共用全部层级，保留 NoCover 排除规则 | `TopLayerQueries` |
| R10 | 替换条目先清除旧父关系，并失效 children 缓存 | `UiLifecycleRegressionTests` 的路由替换用例 |
| D1 | 同层多个不同 UI Id 的 Dlg 按 FIFO 打开并存；同 Id 复用节点，以最新请求重开，被取代的未完成任务返回 null | `DialogsCoexist`、新增协调器 FIFO/去重测试 |

同时完成三项有依据的改进：

- BindingScope 先清空账本，再处理逆序快照，递归解绑不会重复执行，清理中新登记的订阅留给下一轮；动画取消句柄也先清空并用轮次防止旧回调覆盖新动画。
- 明确 Align、Pop 定位和屏幕限制参数为预留能力；NoCover 只参与顶部判定，可见性由 HideBelow 控制。
- 新增永久 Godot 集成测试和执行脚本，默认构建排除测试节点，脚本结束时恢复正常游戏程序集。

补充集成检查又复现并修正了两个边界：可见性 hook 批量关闭界面时，旧遍历可能访问失效列表；关闭动画 hook 抛异常时，界面可能永久停在 Close。层级刷新现使用快照和重入轮次，关闭动画异常记录后取消当前动画并继续 CloseDone。修复前的两项失败保存在 `2026-10-06-ui-fix-results/extra-boundaries/`，对应正式用例为 `VisibilityCallbackCanCloseWindows` 和 `CloseAnimationFailureStillCloses`。

正式结果：新增 **14 项 NUnit 回归**；定向测试 **54/54 通过**，全量 NUnit **1339/1339 通过**；Godot **4.7.2 Mono headless 28/28 通过**。正常游戏构建成功，无编译错误，23 条警告均来自既有 dialogue_manager 插件。`git diff --check` 通过。

验证日志在 `Doc/reviews/2026-10-06-ui-fix-results/`：`ui-fix-full.trx` / `tests-full.log`、`ui-fix-unit.trx` / `tests-initial.log`、`godot-headless.log`、`runner.log`、`build-headless.log`、`build-normal.log`。headless 日志中的三个 `expected ... exit failure` 是刻意注入的业务离场异常，断言确认框架仍完成解绑。

复现入口：`Tests/kemo_card.Ui.Headless/run.ps1 -GodotPath <Mono Godot 可执行文件>`，参数、日志和条件编译说明见该目录 README。测试覆盖真实节点、资源加载、原生信号及默认处理器；没有进行游戏美术布局、鼠标/键盘焦点或完整玩家流程的视觉验收。
