# 开发向 AppLog 设计

**日期**：2026-07-14  
**状态**：待实现  
**范围**：统一开发向错误/警告日志出口（`IAppLog` / `AppLog` / `GodotAppLog`），迁移现有 `GD.Push*` 与领域 Logger 实现；不含玩家向 Toast / 战斗日志。

---

## 1. 背景与决策摘要

此前讨论了两类独立议题：

1. **「全局日志系统」**：实为开发诊断、战斗操作日志、获得道具提示三类不同能力；本 spec **只落地第一类（开发向）**。
2. **内容配置单文件 vs 大数组**：维持既有约定（见附录 A）。

现状：`IContentModLogger` / `IEventDispatcherLogger` / `IModScriptLogger` 已按领域拆分；大量调用点仍直调 `GD.PushError` / `PushWarning`；无统一级别与 category。

选定方案：**薄门面 `IAppLog` + 静态 `AppLog.Configure`，本轮一次迁完 `Src/` 内诊断用 `GD.Push*`；领域 Logger 接口保留，实现委托 `IAppLog`。**

---

## 2. 目标与非目标

### 2.1 目标

- 提供 frame 层可测的 `IAppLog`：`Debug` / `Info` / `Warning` / `Error`（可选 `category`）。
- Godot sink：`Warning` → `GD.PushWarning`，`Error` → `GD.PushError`；`Debug`/`Info` 仅在 Debug 构建（或显式开启 verbose）时 `GD.Print`。
- 一次迁完：`Src/frame/`、`Src/fixed/`、`Src/mod/`、`MainRoot` 中诊断用途的 `GD.Push*`。
- 领域 Logger **保留接口语义**，实现改为委托 `IAppLog`（显式注入优先）。
- 静态门面 `AppLog` 服务散落调用点（与 `EventDispatcher.Configure` 同模式）。

### 2.2 非目标

- 玩家 Toast / 横幅 / 飘字。
- 战斗内操作日志面板。
- 将 `IContentModUserNotifier` 做成弹窗或用户可见提示。
- 文件落盘、远程上报、复杂过滤/异步队列。

---

## 3. 架构

```
调用方（UI / Content / MainRoot …）
        │
        ├─ AppLog.Warning/Error/…     （静态门面，散落调用）
        │
        └─ 领域 Logger 接口
              ├─ IEventDispatcherLogger
              ├─ IContentModLogger
              └─ IModScriptLogger
                    │
                    ▼
              IAppLog  ←── NullAppLog（未配置 / 单测）
                    │
                    ▼
              GodotAppLog（Src/fixed/godot）
                    │
                    ▼
              GD.PushError / PushWarning / Print
```

### 3.1 文件布局

| 类型 | 路径 |
|------|------|
| `IAppLog`、`NullAppLog`、静态 `AppLog` | `Src/frame/logging/` |
| `GodotAppLog` | `Src/fixed/godot/GodotAppLog.cs` |
| 适配后的领域 Logger 实现 | 现有路径；构造注入 `IAppLog`；可新增 `GodotModScriptLogger` |

`frame` 不依赖 Godot；Godot 绑定仅在 `fixed`。

---

## 4. 接口形状

```csharp
namespace KemoCard.Frame.Logging;

public interface IAppLog
{
    void Debug(string message, string? category = null);
    void Info(string message, string? category = null);
    void Warning(string message, string? category = null);
    void Error(string message, string? category = null);
}
```

### 4.1 静态门面

- `AppLog.Configure(IAppLog implementation)`：进程内设置当前实现。
- `AppLog.Debug/Info/Warning/Error(...)`：转发到当前实现。
- 未 Configure 时使用 `NullAppLog.Instance`（不抛、不写），迫使启动路径显式配置。

### 4.2 消息格式

- 有 `category`：`[Category] message`
- 无 `category`：纯 `message`
- 明文日志，**不走本地化**（与项目「日志除外」规则一致）。

### 4.3 GodotAppLog 行为

| 级别 | 行为 |
|------|------|
| Error | 始终 `GD.PushError(formatted)` |
| Warning | 始终 `GD.PushWarning(formatted)` |
| Info / Debug | 默认仅 `OS.IsDebugBuild()` 时 `GD.Print`；可通过构造参数 `enableVerbose` 覆盖 |

---

## 5. 注入与启动顺序

在 `MainRoot._Ready`（或等价最早启动点）中：

1. **`AppLog.Configure(new GodotAppLog())`** — 必须最先。
2. `EventDispatcher.Configure(new GodotEventDispatcherLogger(appLog))`。
3. `ModFactory.Bootstrap(...)` — ContentMod / Script logger 注入同一 `IAppLog`（或依赖已 Configure 的 `AppLog`；**推荐构造注入**便于单测）。

`KeywordCatalog.Shared.WarningHandler` 改为：`msg => AppLog.Warning(msg, "Keyword")`（或注入的 `IAppLog`）。

---

## 6. 领域 Logger 适配

| 现有接口 | 改法 |
|----------|------|
| `IEventDispatcherLogger` | 保留；实现调用 `_log.Error(message)`（category 可用 `"Mvc"`） |
| `IContentModLogger` | 保留语义方法；实现内 `Warning`/`Error` + category `"ContentMod"`，文案格式可与现有 `[ContentMod] ...` 对齐（category 已带前缀时避免双重括号，二选一：只用 category，或 message 内自带前缀且 category 为 null） |
| `IModScriptLogger` | 保留；新增基于 `IAppLog` 的实现并在 Bootstrap 接入，替换当前 `NullModScriptLogger` |

**ContentMod 前缀约定（本 spec 选定）**：使用 `category: "ContentMod"`，message 本体不再重复 `[ContentMod]` 前缀，避免 `[ContentMod] [ContentMod] ...`。

---

## 7. 迁移规则

- `Src/` 业务代码（测试项目除外）禁止再直接使用诊断用途的 `GD.PushError` / `GD.PushWarning`；改为 `AppLog.*` 或经领域 Logger。
- 常用 category：`UI`、`ContentMod`、`Mvc`、`Keyword`、`Script`、`MainRoot`；组件可细分（如 `VirtualList`）。
- 迁移覆盖约：`UiManager`、`UIRuntime*`、`UILoad*`、`UIResourceLoader`、`GodotMainThreadSyncContext`、`GodotContentModTranslationLoader`、`CardDetailsDlg`、`BaseCardItem`、`BaseButton`、`BaseDlgComp`、`VirtualList`、`KeywordTipService`、`BuiltinKeywords`、`ModFactory`、`MainRoot` 等现有调用点。

---

## 8. 测试与验收

### 8.1 测试

- 假 `IAppLog` 记录调用；验证领域 Logger 适配器级别与 category 正确。
- `AppLog` 未 Configure 时不抛、不写。
- 现有若依赖 `GD` 副作用的测试，改为断言假 logger 或保持行为等价。

### 8.2 验收标准

1. 启动路径显式 `AppLog.Configure(GodotAppLog)`，且早于其它会打日志的子系统。
2. 领域 Logger 接口未删除；实现均经 `IAppLog`。
3. `Src/` 业务代码无残留诊断用 `GD.PushError` / `PushWarning`。
4. Debug/Info 仅 Debug 构建（或 verbose）打印；Warning/Error 始终进入 Godot 输出。
5. 不引入 Toast / 战斗日志 / 文件 sink。

---

## 附录 A：内容配置布局（相关议题结论）

**维持** `content/<类别>/*.json`：一对象一文件，文件名（无扩展名）为 `id`，目录扫描加载。

- **不**引入同类定义的大数组聚合文件（如 `cards.json` 装全部卡牌）。
- `strike.json` / `strike_plus.json` 为独立定义，经 `cardGroupId` + `upgradeTier` 关联升级链。
- **例外**：商店池、关卡表等「引用 id 的清单」可用数组文件；**定义本体**仍一文件一对象。

本附录仅为设计结论存档，不在本轮 AppLog 实现范围内改动加载器。
