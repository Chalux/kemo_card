# Mod 脚本运行时（PuerTS ScriptEnv）设计规格

**日期**：2026-06-17  
**状态**：已定稿（brainstorming 确认）  
**范围**：单一 VM、Mod 预编译 JS 加载、窄面板沙箱、Rebuild 重建、全部 `scriptPath` 类型

---

## 1. 目标与非目标

### 1.1 目标

- 全项目维护**唯一** `Puerts.ScriptEnv`（Puerts 3.0；`JsEnv` 已 Obsolete），由 `ModScriptRuntime` 持有。
- Mod 脚本以**预编译 JS** 放在 `user://mods/<folder>/scripts/`；运行期 Lazy 加载 + 模块/入口缓存。
- **窄面板沙箱**：入口函数仅接收 `ScriptContextFacade`（只读 `Contains`、宿主 RNG、`Log`）；纯同步 `return` 结构化结果。
- `ContentModPipeline.Rebuild()` 时**全量销毁重建** VM，并可选预热（仅 import，不执行入口）。
- 覆盖效果、故事、Script 事件、战斗/波次、敌人 AI 等全部 `scriptPath` 引用。

### 1.2 非目标

- 不做静态全局 `ScriptEnv.Instance`。
- 运行期不编译 TS（无内嵌 TS 编译器）。
- 业务脚本不支持 async/Promise（无需 `_Process` Tick）。
- 不实现 HybridCLR 可信 Mod 通道（规格保留，另里程碑）。
- 不实现 LLM Agent 专用 VM（与 Mod 脚本分离）。

---

## 2. 架构

```
ModFactory.Bootstrap
  → ModScriptRuntime (IScriptRuntimeResetter)
  → ModScriptCatalog (modId → folderPath)
  → ModScriptLoader (ILoader)
  → ContentModPipeline.Rebuild → catalog.Rebuild + runtime.Recreate + optional Prewarm

业务调用方
  → PuertsContentEffectScriptHost / *ScriptInvoker
  → ModScriptRuntime.Invoke(modId, scriptPath, entry, ScriptCallContext)
  → ScriptContextFacade → JS execute(ctx)
  → ModScriptResultParser → 宿主校验 → 提交/软失败
```

**组合优先**：Runtime、Loader、Catalog、各 Invoker 为独立类型，通过构造函数注入协作。

---

## 3. 模块标识与路径

| 概念 | 规则 |
|------|------|
| 模块 specifier | `{modId}/{scriptPath}`，如 `base.game/effects/demo.js` |
| 磁盘路径 | `{FolderPath}/scripts/{scriptPath}` |
| modId vs 文件夹 | `mod.json` 的 `modId`（如 `base.game`）≠ 目录名（如 `base-game`）；由 `ModScriptCatalog` 映射 |

---

## 4. 加载策略

- **默认 Lazy**：首次 `Invoke` 时 `ExecuteModule` import；PuerTS 缓存模块；C# 缓存 `(modId, scriptPath, entry)` 入口委托。
- **可选预热**：Rebuild 后遍历 Store 中全部 `scriptPath`，仅 `TryLoadModule`（不调用 `execute`）；错误写入 `ContentLoadReport.ScriptLoadErrors`。
- **Owner 映射**：`GameDefinitionRegistry.TryGetOwnerModId(category, id)` 返回合并胜方 modId，供预热与 Invoker 解析 mod。

---

## 5. 沙箱与通信

### 5.1 ScriptContextFacade（唯一参数）

| 成员 | 说明 |
|------|------|
| `Contains(category, id)` | 只读查询七大注册表 |
| `NextInt(min, max)` | 宿主 RNG，由 `RunSeed + StreamKey` 派生，保证可复现 |
| `Log(message)` | 白名单日志 |

### 5.2 入口约定

- 默认入口名 `execute`；`EffectDto.scriptEntry` 等可覆盖。
- 签名：`export function execute(ctx) { return { ... }; }`（纯同步）。

### 5.3 各类型返回形状

| 类型 | JS 返回 | C# 解析 |
|------|---------|---------|
| 效果 | `{ proposedEffects: [{ kind, params? }] }` | `IContentEffectScriptHost` |
| 故事选项 | `{ options: [{ optionId, labelId, next? }] }` | `StoryScriptInvoker` |
| Script 事件 | `{ pages?, options? }` | `EventScriptInvoker` |
| 战斗/波次 | `{ phase?, hooks? }` 或字典 | `BattleScriptInvoker` |
| 敌人 AI | `{ skillId }` 或 `{ weights: { id: number } }` | `EnemyAiScriptInvoker` |

返回值须经宿主校验（非法 id → 软失败，与设计稿一致）。

### 5.4 沙箱局限

PuerTS 3.0 无法在 JS 全局彻底移除 `CS.*`。工程级隔离策略：仅注入 `ctx`、不向脚本传递 C# 实例引用、Mod 评审。真·隔离（独立 isolate）列为后续。

---

## 6. 生命周期

| 时机 | 行为 |
|------|------|
| `ModFactory.Bootstrap` | 创建 `ModScriptRuntime`；首次 `Rebuild` 填充 Catalog 并 `Recreate` |
| `ContentModPipeline.Rebuild` | 注册表合并 → `catalog.Rebuild(OrderedActiveMods)` → `runtime.Recreate()` → 可选预热 |
| Run 中 | 不提供 Mod 开关；VM 与当前启用集一致 |
| 进程退出 | `Dispose` ScriptEnv |

---

## 7. 错误处理

- JS 异常：捕获，记录上下文，调用方 `TryExecute`/`TryInvoke` 返回 false，主线程不崩溃。
- 预热/加载错误：`ScriptLoadErrors` 写入 report + `IContentModLogger.LogScriptLoadError`。
- 仅主线程调用 `ModScriptRuntime`（PuerTS 非线程安全）。

---

## 8. 构建链

- 源 TS：`Src/typescript/src/mods/<mod-folder>/`
- esbuild 输出：`Config/mods/<mod-folder>/scripts/`
- `ContentModBootstrap.EnsureDefaultModsCopied` 复制到 `user://mods/`

---

## 9. 测试要点

- Loader 路径解析（modId → folder）
- Owner 映射胜方 modId
- Invoke 结构化返回、JS 异常软失败
- 同种子 RNG 可复现
- Rebuild/Recreate 后脚本行为更新
- 预热报告语法错误

---

## 10. 自检

- 与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界、可复现 RNG、脚本只提议/宿主校验一致。
- 与 `2026-06-16-content-definition-dto-design.md` 中 `ExecuteScript` / `IContentEffectScriptHost` 一致。
- 无 TBD。
