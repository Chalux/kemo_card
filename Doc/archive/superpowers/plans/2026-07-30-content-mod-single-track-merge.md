# ContentMod 单轨合并 Implementation Plan

> **For agentic workers:** 行为不变；合并 id 表与 DTO Store 双轨；缩窄 MergeResult。

**Goal:** Store 为定义权威；冲突与 owner 在合 DTO 时产出；去掉 `ModContentBundle` 平行 id 列表。

**Architecture:** `ContentRegistryMerger.Merge(bundles, store)` → `ContentRegistryMergeResult`；`GameDefinitionRegistry` 不再维护 `HashSet` id 表，`Contains` 查 Store。

**Tech Stack:** C# / NUnit

**状态**：已完成并归档（2026-07-30）

---

### Task 1: 核心类型 + 合并

- [x] `ModContentBundle(ModId, Definitions)`
- [x] `ContentRegistryMergeResult(IdConflicts, OwnerModIds)`
- [x] Store 单轨 Rebuild / Merger 写入 Store
- [x] Registry 去 `_tables`

### Task 2: Loader + 测试调用点

- [x] Loader 只填 Definitions
- [x] 更新 `ContentModTestHelper` / CombatTestHelper / 相关单测

### Task 3: 验证

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Content|FullyQualifiedName~GameDefinition|FullyQualifiedName~ModScriptPrewarmer|FullyQualifiedName~CombatTest" --nologo -v q
```

全量 `dotnet test`：**486** 通过。
