# Toast 通用组件设计

## 概述

提供全局轻量提示组件 `ToastService`，以公共入口拉起一条 Toast。Toast 以**纵向堆叠**方式展示在屏幕上方（约 25% 高度处），支持对象池复用、上移缓动 + 淡出动画。

> 本规格由「Run 存档闭环」任务（见 `2026-08-04-run-save-continue-design.md`）的 grilling 决议引出，定位为通用基础设施，供后续所有模块复用（保存成功、设置已应用等提示）。

---

## 1. 目标与非目标

### 目标

- 公共入口 `ToastService.Show(textKey)`，任何模块可拉起 Toast。
- 纵向堆叠 Toast 列表，显示在屏幕上方约 25% 位置（**不在正中间**）。
- 对象池管理：复用 Toast 实例，避免反复实例化/销毁。
- 动画：**瞬间弹出（无淡入）→ 停留 1 秒 → 上移 100px 并同时淡出 2000ms**。
- 文案走翻译键（`Resource/Locale/strings.csv`）。

### 非目标

- 不承担「多行文本输入 / 按钮交互」等复杂内容——那是 Alert/弹窗的职责。
- 不做 Toast 与 UIManager 缓存机制的深度集成（见 §4 挂载方式）。
- 不做点击穿透控制之外的额外输入处理。

---

## 2. 架构

```
ToastService（静态门面）
├── Configure(Control noticeLayer)        # 启动时注入 Notice 层
├── Show(string textKey)                  # 公共入口
└── 内部：
    ├── _pool: Stack<ToastItem>           # 空闲对象池
    ├── _active: List<ToastItem>          # 活跃列表（含堆叠顺序）
    └── _container: VBoxContainer         # 堆叠容器（置于 Notice 层内）
```

### 核心类型

| 类型 | 位置 | 职责 |
|------|------|------|
| `ToastService` | `Src/mod/global/Ui/Toast/` | 静态门面：池管理、堆叠、生命周期调度 |
| `ToastItem` | `Src/mod/global/Ui/Toast/` | 单条 Toast 节点（Control + 场景），负责自身动画 |

### 挂载层级

- Toast 挂载到 **`EUILayer.Notice`**（`MainRoot` 已注册为 TopLayer，不受 `HideBelow` 影响，始终可见）。
- `UILayer` 是 `Control`，ToastService 直接 `AddChild` 一个 `VBoxContainer` 容器，**不经过 UIManager 的 Open/Close 状态机**（原因见 §4）。

---

## 3. ToastItem

### 场景结构（`ToastItem.tscn`）

```
ToastItem (Control)
└── Panel
    └── Label
```

- 布局在场景中定义，代码只写逻辑（项目硬性约定）。
- `Label.Text` 由 `Show()` 时从翻译键填充。

### 动画参数（常量）

| 参数 | 值 | 说明 |
|------|-----|------|
| 入场 | 无 | 瞬间显示，无淡入 |
| 停留时长 | 1000 ms | 显示期间静止 |
| 上移距离 | 100 px | 同时进行 |
| 淡出时长 | 2000 ms | 同时进行 |

- 使用 Godot `Tween`；上移与淡出并行。
- 退场完成后回调 `OnRecycle`，ToastService 将实例回池。

---

## 4. 与 UIManager 的关系

### 为什么不经 UIManager

`UIVoRegistry` 以 UIId 为键维护**单实例**（`GetOrCreate` 返回既有 vo），且 `UILayer.AddUI` 也按 UIId 单实例。Toast 需要**同屏多实例纵向堆叠**，与 UIManager 的单实例模型冲突。

因此 Toast 走独立的轻量容器：

- ToastService 在 `Configure` 时创建 `VBoxContainer` 并 `AddChild` 到 Notice 层。
- 容器锚点：屏幕上 25% 高度、水平居中（`anchors_preset` 在场景或代码设置）。
- 每次 `Show`：从池取（或实例化）ToastItem → 设置文案 → `AddChild` 到容器末尾 → 播放动画 → 完成回池。

### 生命周期

- Toast 不参与 UIManager 状态机，池化由 ToastService 自管。
- 项目关闭时随场景树整体销毁，无需额外清理。

---

## 5. 对象池策略

- 空闲池 `Stack<ToastItem>`：动画完成后 `QueueFree` 前先入池（`Reparent` 到一个隐藏 holder 或 `RemoveChild` 保留节点）。
- 最大池容量（默认 4，可配）：超过上限的回收实例直接销毁。
- 频繁 `Show` 时若池空则新建；瞬时堆叠上限不设死，但视觉上以容器高度为准。

---

## 6. 公共入口

```csharp
ToastService.Show("UI_RUN_SAVED");                          // 无参
ToastService.Show("UI_RUN_SAVED_AT", "第 3 环", "事件");    // 格式化占位符 {0} {1}
```

- `Show(string textKey, params string?[]? args)`：`args` 传入时以 `string.Format` 格式化翻译串占位符；无参时直接 `Localization.Tr`（避免含字面量 `{` 的翻译串被 Format 误解析）。
- 幂等、线程安全不做要求（UI 主线程调用）。
- 连续多次 `Show`：每次追加到堆叠容器末尾，已有的 Toast 继续向上淡出（自然让位）。

---

## 7. 与 Run 存档闭环的对接

- RunMainWin「保存」按钮点击后调用 `ToastService.Show("UI_RUN_SAVED")`。
- 「保存并返回主菜单」**不弹 Toast**（回主菜单即是最佳反馈，grilling 决议）。
- 「快速读取」失败时 `ToastService.Show("UI_RUN_LOAD_FAILED")`。

---

## 8. 待实现确认点

- 容器 25% 定位在 `ToastItem.tscn` 的容器节点上设置，还是 ToastService 运行时设置（二选一，实现计划定）。
- 池最大容量常量值（默认 4）。
