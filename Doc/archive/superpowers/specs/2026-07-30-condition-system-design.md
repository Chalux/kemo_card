# 条件判断系统（Condition）设计

**日期**：2026-07-30  
**最后修订**：2026-07-31  
**状态**：引擎与 Persistent 四件套已实现；首个内容接入 `StoryDto.unlock`（2026-07-31）；Combat CondType / 其余内容 DTO 字段未接  
**范围**：可扩展条件求值引擎、Persistent / Combat 双域 CondType 注册、JSON 组合语法、Explain 结构化结果与提示模板约定  
**非范围**：扣除/支付（Cost）、具名条件包、脚本动态注册 CondType、引擎内脏标记/订阅、Combat 具体 CondType（v1 仅空表）

---

## 1. 目标与非目标

### 1.1 目标

- 用统一引擎解析并求值内容侧内联的条件表达式，供解锁门槛、UI 灰态/详情等**只读检查**使用。
- 以 `CondType`（字符串）为类型 id，可注册检查逻辑、参数解析、短/长提示模板键。
- Persistent 与 Combat **分域注册**，共享求值与提示管道，避免战斗运行时与持久状态耦合成一张大表。
- 加载/合并期校验未知类型与参数形状，错误带**配置来源路径**。
- 面向用户的提示走翻译键；显示名由 UI 查表，引擎不解析本地化显示名。

### 1.2 非目标

- 不负责资源扣除、事务回滚或「检查并通过后支付」。
- 不做具名条件包表（`cond_pack_id`）；v1 仅内联表达式。
- 不做引擎级依赖追踪 / 自动刷新；调用方在适当时机再次 `Evaluate`。
- 不把 NOT 塞进 `{}`/`[]` 组合糖；否定用独立 CondType。
- v1 不实现 Combat 业务 CondType；不实现脚本侧运行时注册。

---

## 2. 架构

```
内容 JSON（内联表达式）
        │  Parse + Validate（指定域注册表）
        ▼
ConditionExpression（And / Or / Leaf）
        │  Evaluate(context)
        ▼
ConditionEvalResult
   ├── Passed
   └── Leaves[]（每叶：type, passed, fill, progress?, refs?）
        │
        ▼
UI：按模板键 + fill/progress/refs 自行拼展示（引擎无默认聚合）
```

| 层 | 位置 | 职责 |
|---|---|---|
| 引擎 | `Src/frame/condition/` | JSON 解析、表达式树、求值、结果结构、域注册表容器 |
| Persistent CondType + Context 适配 | `Src/mod/`（如 `global` / `run` 下条件目录） | 具体类型、背包/旗标等只读查询 |
| Combat CondType + Context 适配 | `Src/mod/combat/`（后续） | v1 仅保留空注册表入口 |
| 启动注册 | `ModFactory` / 对应 Mod Bootstrap | 向域表 `Register` 内置类型（同 KeywordCatalog 模式） |

`frame` 不引用 `KemoCard.Mod.*`。Context 为**接口**；游戏侧提供适配器实例，求值时由调用方传入。

---

## 3. JSON 组合语法

### 3.1 规则

| 节点 | 语义 |
|---|---|
| 对象 `{}` | AND：所有成员通过则通过 |
| 数组 `[]` | OR：任一元素通过则通过 |
| 对象的 **key** | `CondType` 字符串 |
| 对象的 **value** | 该 CondType 的**参数列表**（永远不当子表达式） |
| 数组元素 | 可为对象（AND 子树）或数组（OR 子树） |

递归边界：**仅数组元素可嵌套表达式**；对象 value 一律走该类型的 `TryParse`。

同 CondType 不得在同一 AND 对象中出现两次（JSON key 唯一 + 加载期显式校验更稳妥）。多目标由独立类型消化（如 `HasAllItems` / `HasAnyItem`），不为通用布尔树引入 `$or` 等保留键。

复杂门槛优先**单开 CondType**，而不是堆叠超级表达式。

### 3.2 示例

```json
{
  "HasFlag": ["intro_done"],
  "HasAllItems": [["wood", 5], ["stone", 3]]
}
```

```json
[
  { "HasFlag": ["intro_done"] },
  { "HasAnyItem": [["ticket", 1], ["voucher", 1]] }
]
```

### 3.3 空值与缺失

| 情况 | 语义 |
|---|---|
| 内容字段**缺失**（未写条件） | 无门槛，视为通过（由读取该字段的业务约定；引擎若收到 `null` 可不建表达式） |
| 表达式节点空对象 `{}` | 空 AND，**真** |
| 表达式节点空数组 `[]` | 空 OR，**假** |
| 叶子参数为空或不合法（如 `"HasFlag": []`） | **加载期** `TryParse` 失败，非运行时空组合语义 |

### 3.4 保留 / 非法 key

v1 对象成员 key 必须是已注册 CondType。以 `$` 开头的保留风格 key（如 `$or`）视为**未知 CondType**，加载失败，避免日后语义被野配置占坑。

---

## 4. CondType 注册

每个 CondType 注册项至少包含：

| 字段 | 说明 |
|---|---|
| `Id` | 字符串，域内唯一 |
| `TryParse` | 原始 args → 强类型参数；失败返回错误信息 |
| `Check` | `(TArgs, TContext) → LeafEvalData`（passed + fill + 可选 progress/refs） |
| `ShortTipKey` | 短提示翻译键（如「xxx 不足」） |
| `LongTipKey` | 长提示翻译键（如「拥有 {item}（{y}/{z}）」类句式） |

- Persistent 与 Combat **两张注册表**，校验时按字段所属域选用。
- 跨域引用（Persistent 配置写了 Combat 类型）→ 加载期失败。
- 未知 CondType → 加载期失败。
- 错误信息必须带**配置来源路径**（定义文件 / 字段路径）。

---

## 5. 求值与结果

### 5.1 Context

- `IPersistentCondContext` / `ICombatCondContext`（名称以实现为准）：只读查询接口。
- Checker 不访问全局单例；不通过服务定位器偷依赖。

### 5.2 结果结构

```
ConditionEvalResult
  Passed: bool
  Leaves: LeafResult[]   // 求值走过的全部叶子（含通过与未通过）

LeafResult
  CondType: string
  Passed: bool
  ShortTipKey / LongTipKey  // 来自注册项
  Fill: 有序参数列表        // 供模板按位/约定键填充
  Progress?: { Current, Required }
  Refs?: { ItemIds?, FlagIds?, ... }  // UI 查显示名，不进引擎本地化
```

- 组合层（AND/OR）**不生成**面向用户文案，只汇总 `Passed` 与叶子列表。
- **无默认聚合策略**（不规定「只取第一条失败」）；展示完全由 UI 决定。
- Explain 路径应评估**全部叶子**，避免 UI 拿不到未求值叶。
- `Progress` 可选；无进度的条件可只靠 `Fill`。

### 5.3 提示约定

- 注册时挂模板键；Checker 只产出填充数据与 refs。
- UI 使用 `Localization.Tr` + 查表得到的显示名；引擎不调用 `Tr` 拼最终可见句（测试可不绑语言）。

### 5.4 刷新

纯拉取：`Evaluate(expr, context)`。物品/旗标变更后由 UI 或业务在已知事件点再次求值。引擎不提供订阅或脏标记。

---

## 6. 与 Cost 的边界

条件系统**只读**。支付、扣物、花货币走独立 Cost/Reward 管线：先 `Evaluate` / 展示 → 用户确认 → 外部扣除。CondType 不提供 `Apply` / `consumes`。

---

## 7. v1 内置 CondType（Persistent）

| CondType | 参数要点 | 说明 |
|---|---|---|
| `HasFlag` | `[flagId]` | 已拥有旗标 |
| `NotHasFlag` | `[flagId]` | 未拥有旗标（NOT 独立类型，钉死组合糖不做 NOT） |
| `HasAllItems` | `[[itemId, count], ...]` | 列出的道具均达到数量（AND） |
| `HasAnyItem` | `[[itemId, count], ...]` | 列出的道具任一达到数量（OR） |

Combat 域：v1 建立空注册表与 Context 接口占位，**不注册**业务 CondType。

---

## 8. 内容接入

- 条件写在各内容定义的内联字段中（字段名由具体 DTO/规格定义，如 `unlock`）；**无**独立「条件包」内容类别。
- 校验时机：在 CondType 已注册之后、内容合并/校验流水线中解析表达式（Bootstrap 顺序：先 Register 条件类型，再校验引用它们的定义）。
- 缺失条件字段 = 该内容无门槛；与空 `{}`/`[]` 字面量区分见 §3.3。

**首个接入（2026-07-31）**：`StoryDto.unlock`（Persistent 域，见[内容规格](../../../superpowers/specs/2026-05-17-content-mod-manager-design.md) §3.2）。

- 校验：`ContentDefinitionValidator.ValidateStories` 解析；失败带 `content/stories/<id>.json:unlock` 来源路径，定义移除。
- 运行期：选故事 UI 用 `ConditionEvaluator` 求值，Context 为 `GlobalPersistentCondContext`（`Src/mod/global/Condition/`）——`HasFlag` 映射到全局存档 `Unlocks`（与 `IsContentUnlocked` 同表）；`GetItemCount` 暂恒 0（商店/道具规格未落地）。
- 加载与校验的 Bootstrap 顺序约束由 `ModFactory.Bootstrap` 保证：先 `RegisterBuiltinConditions()`，再 `ContentModPipeline.Rebuild()`。

---

## 9. 测试要点

- 解析：AND/OR/嵌套数组、对象 value 不当表达式、重复 CondType key、`$or` 失败。
- 空节点：`{}` 真、`[]` 假；叶子空 args Parse 失败。
- 未知类型 / 错域 / 错误路径信息。
- `HasAllItems` vs `HasAnyItem` 语义与 LeafResult（passed、progress、refs）。
- `NotHasFlag` 与组合 AND/OR。
- Evaluate 返回全部叶子；UI 聚合不在引擎断言。

---

## 10. 有意延后

- `$and` / `$or` 保留键嵌套糖  
- 具名条件包与 `$ref`  
- 内容 Mod 脚本注册 CondType  
- Cost 管线与 Cond 的声明式绑定  
- 引擎脏标记 / 依赖声明  
- Combat 业务 CondType、货币/进度类 Persistent 类型  

---

## 11. 文档位置

- 权威正文：本文  
- Agent 地图：`Doc/AGENT.md` 仅保留模块入口与权威链指针，不复制本节细节  
- 索引：`Doc/INDEX.md` 活规格表  
