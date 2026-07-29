# 战斗系统规格对齐实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，按任务逐步实现。步骤使用 checkbox（`- [ ]`）追踪。

**Goal:** 将 `Src/mod/combat/` 现有实现对齐 [战斗系统规格](../specs/2026-07-21-combat-system-design.md)（含 2026-07-28 开放项决议），消除第 7 节及本次全文审计发现的全部差距。

**Architecture:** 保持 `CombatSimulation` 聚合根 + `ICombatCommand` 指令模型不变，重写其内的经济与手牌模型：能量二分（当前能量 / 当前可用能量）、手牌标记入队（不离手）、回合开始管线、主动技蓄力链（`S` 计数器 + `T_k` 扣费）、玩家侧 Shared 结算（D2 分槽 / Team 账本一次）、封印与弃牌分通道。BattleStart 被动/修饰通过**通用开战技能注入入口**（有序列表由调用方传入）执行，Run 层接线不在本计划内。

**Tech Stack:** Godot 4.6.1 Mono + .NET 8 + C# + 现有测试框架（`Tests/kemo_card.Ui.Tests`）+ `HostRng` / `GameDefinitionRegistry` / GAS（ASC）

**规格来源：**
- [战斗系统规格 2026-07-21](../specs/2026-07-21-combat-system-design.md)（权威；本计划中「§n」均指该文档章节）
- [总规格 2026-05-11](../specs/2026-05-11-kemo-card-design.md) 第 3 节摘要

**范围排除（另开任务）：** Run 层潜能/修饰数据源与 BattleStart 接线、战斗 UI、商店/道具、非 Energy 费用类型实装（v1 仅拒绝入队）。

## Global Constraints

- 代码纯 C#，禁止 GDScript（仓库规则）。
- 面向用户文案一律走翻译键；`CombatApplyResult` 的失败消息当前为明文中文——本计划维持现状（模拟层消息现阶段仅用于日志/测试断言），UI 接入时再换键。诊断日志用 `GD.Print`/宿主 logger 明文即可。
- 所有随机必须走 `HostRng` 派生流（§总规格 2.3）；禁止 `System.Random`。
- 手牌槽固定 5（`CombatConstants.HandSlotCount`）；卡组 **[1, 10]** 张（写死）。
- v1 实装费用类型仅 **Energy / None**，其余 `ECostType` 入队拒绝（软失败）。
- v1 禁止一切战斗中途即时抽牌效果（§4.3）。
- 队列排序：`priority` **降序**（越大越先），同优先级按入队序号 **升序**；**无 RNG 破平**（§2.1）。
- 每完成一个代码文件的逻辑改动后对该文件执行 `dotnet format`（仓库规则）。
- 提交信息一律简体中文。
- 每个任务结束时全部测试必须通过：`dotnet test`（解决方案根目录）。

---

## 全文审计差距清单（2026-07-28 核实，取代规格 §7 的旧清单）

| # | 规格 | 现状 | 对应任务 |
|---|------|------|---------|
| 1 | §3 能量二分：当前能量 / 当前可用能量；每回合当前能量 +1（非首回合）；可用能量覆盖灌入且可超上限 | 单一 `CurrentEnergy`；`GainEnergy` clamp 到上限；无回合灌入 | T2 |
| 2 | §2.2/§4.1 入队=手牌标记不离手；队列项含 `paid` | `ApplyPlayCard` 直接 `slot.ClearCard()` 离手；无 `paid` | T3 |
| 3 | §2.1 执行后进弃牌堆（含空放） | 执行/取消后卡牌**直接消失**，不进弃牌堆 | T3/T8 |
| 4 | §2.1 队列 priority 降序 + 入队序升序，无 RNG | `SortedSet` 升序（小者先）+ RNG tiebreak | T1 |
| 5 | §2.2/§3.3 取消标记退 `paid`；涨费不够自动取消并回退未确认；非 Energy 拒绝入队 | 取消不退费；费用变化无处理；非 Energy **免费入队** | T3/T4 |
| 6 | §2.2 确认锁定队列，须显式取消确认才能改 | 无取消确认指令；`Cancel` 随时可用且自动置未确认 | T3 |
| 7 | §6.2 玩家阶段开始管线（能量/S/公式抽牌/封印） | 完全缺失 | T5 |
| 8 | §4.2/§4.4 每回合公式抽牌；空堆洗牌（每阶段≤1 次） | 无自动抽牌；无洗牌 | T5 |
| 9 | §6.1 BattleStart：注入技能（禁碰 SharedHp）→ 冻结补满 → 开局抽满 5 | BattleStart 直接切 Player，无任何步骤 | T6 |
| 10 | §5 主动链：`activeSkillChain` + `S` 计数器 + 自动最高档 + `T_k` 扣费 | `CastInstantSkillCommand` 任意技能无限次 | T7 |
| 11 | §1.2/§1.3 玩家角色无 Health 当前值；分槽伤害结算后扣 Shared；Team 账本一次；Heal 仅 Team | 伤害/治疗直接写槽位角色 ASC Health | T9 |
| 12 | §1.2 重算 MaxSharedHp 不改 SharedHp，仅越界 clamp | `TeamMaxHealthCoordinator` 上升时把 SharedHp **+delta** | T9 |
| 13 | §1.3 决议：`ETargetScope.Team` 内容声明 + Heal 校验 | `ETargetScope` 无 Team；无校验 | T9 |
| 14 | §2.5 封印（清标记退费+已行动+禁主动，资源照跑） | 完全缺失 | T10 |
| 15 | §4.6 弃牌分通道（主动可弃标记→回退；其余随机未标记） | `DiscardFromHand` 从高槽位确定性弃 | T11 |
| 16 | §4.3 中途 Draw 拒绝；抽牌数量修正通道 | `EEffectKind.Draw` 即时抽牌仍开放 | T11 |
| 17 | §2.3/§2.4 目标丢失：玩家阶段取消相关标记退费回退未确认；执行期单体 RNG 重选 / 多目标合法子集 | 即时技能后仅置 `HasActed=false`；单体 Default 取 `legal[0]`；多目标含非法即整体放弃 | T8 |
| 18 | §总规格 4.6.5 卡组 [1,10]，最少 1 张 | `DeckPreset.Validate` 只查上限与重复 | T2 |
| 19 | §5.4 决议：`targets` 按将释放档位目标规格校验 | 不适用（无主动链） | T7 |

---

## 文件结构

| 路径 | 操作 | 职责 |
|------|------|------|
| `Src/mod/combat/statemachine/CardExecutionQueue.cs` | 改 | 排序改 priority 降序 + 入队序升序，去 RNG |
| `Src/mod/combat/statemachine/QueuedCardEntry.cs` | 改 | 增加 `Paid` 字段 |
| `Src/mod/combat/CharacterBattleInstance.cs` | 改 | 能量二分、`S` 计数器、封印态、标记辅助、洗牌抽牌 |
| `Src/mod/combat/HandSlot.cs` | 改 | 标记态（`MarkedSequence`） |
| `Src/mod/combat/DeckPreset.cs` | 改 | 最少 1 张校验 |
| `Src/mod/combat/CombatConstants.cs` | 改 | `MinCardsPerDeck = 1` |
| `Src/mod/combat/commands/PlayCardCommand.cs` | 保留 | 语义改为「标记入队」 |
| `Src/mod/combat/commands/UnconfirmCharacterCommand.cs` | 增 | 显式取消确认 |
| `Src/mod/combat/commands/CastActiveSkillCommand.cs` | 增 | 主动链指令（无 skillId） |
| `Src/mod/combat/commands/CastInstantSkillCommand.cs` | 删 | 被 CastActiveSkillCommand 取代 |
| `Src/mod/combat/statemachine/CombatStateMachine.cs` | 改 | 指令处理、BattleStart/玩家阶段管线、执行阶段结算 |
| `Src/mod/combat/statemachine/PlayerPhasePipeline.cs` | 增 | §6.2 管线（能量/S/抽牌/封印） |
| `Src/mod/combat/statemachine/QueuedCostReconciler.cs` | 增 | §3.3 费用变化即时处理 |
| `Src/mod/combat/runtime/CombatSimulation.cs` | 改 | BattleStart 注入列表、首回合标记、`RunBattleStart()` |
| `Src/mod/combat/runtime/BattleStartSkillEntry.cs` | 增 | 开战技能注入条目 |
| `Src/mod/combat/runtime/CombatSimulationFactory.cs` | 改 | 注入列表参数、卡组张数校验 |
| `Src/mod/combat/runtime/PlayerTeamState.cs` | 改 | SharedHp 写锁（BattleStart 禁碰）、冻结补满 |
| `Src/mod/combat/gas/TeamMaxHealthCoordinator.cs` | 改 | 重算仅 clamp，不 +delta |
| `Src/mod/combat/effects/CombatEffectExecutor.cs` | 改 | 玩家侧 D2/Team 结算、Heal 仅 Team、Draw 拒绝、弃牌分通道 |
| `Src/mod/combat/effects/SkillActionExecutor.cs` | 改 | Draw 拒绝、Discard 通道化、ModifyDrawCount、能量给可用池 |
| `Src/mod/combat/effects/EDiscardChannel.cs` | 增 | 弃牌通道枚举 |
| `Src/frame/content/definitions/ContentEnums.cs` | 改 | `ETargetScope.Team` |
| `Src/frame/content/definitions/CharacterDto.cs` | 改 | `activeSkillChain` 字段 |
| `Src/frame/content/definitions/SharedDefinitionDtos.cs` | 改 | `ActiveSkillChainEntryDto` |
| `Src/mod/combat/CombatContentValidator.cs` | 增 | Heal 目标 / 主动链配置的内容校验 |
| `Tests/kemo_card.Ui.Tests/Combat/*` | 改/增 | 按任务逐项列出 |

**依赖顺序**：T1 → T2 → T3 → T4 → T5 → T6 → T7 → T8 → T9 → T10 → T11 → T12。T1/T2 无前置可并行；T3 起依赖前序。

---

### Task 1：卡牌队列排序对齐（§2.1）

**Files:**
- Modify: `Src/mod/combat/statemachine/CardExecutionQueue.cs`
- Modify: `Src/mod/combat/runtime/CombatSimulation.cs`（构造处去掉队列 RNG）
- Test: `Tests/kemo_card.Ui.Tests/Combat/CardExecutionQueueTests.cs`

**Interfaces:**
- Produces: `CardExecutionQueue`（无 RNG 构造）：`Enqueue` / `TryDequeue` / `PeekAllOrdered` / `TryRemove` 签名不变；出队顺序 = `Priority` 降序，再 `Sequence` 升序。

- [ ] **Step 1: 重写测试**：删除依赖 RNG tiebreak 的用例，新增：
  - `priority 大者先出队`（100 vs 200 → 200 先）；
  - `同 priority 按入队序号先入先出`；
  - `混合序列全序断言`（构造 (p=100,s=1),(p=200,s=2),(p=100,s=3) → 出队 s2,s1,s3）。
- [ ] **Step 2: 跑测试确认失败**（现实现小者先 + RNG）。
- [ ] **Step 3: 实现**：

```csharp
public sealed class CardExecutionQueue
{
	private readonly SortedSet<(int NegPriority, long Sequence, QueuedCardEntry Entry)> _heap = [];

	public void Enqueue(QueuedCardEntry entry) =>
		_heap.Add((-entry.Priority, entry.Sequence, entry));
	// TryDequeue / PeekAllOrdered / TryRemove 结构不变，元组去掉 TieBreak
}
```

  同步删除 `CombatSimulation` 构造里的 `new HostRng(runSeed, "combat.queue")`。
- [ ] **Step 4: 全量测试通过后 `dotnet format`，提交**：`修正卡牌执行队列排序为 priority 降序加入队序，去除随机破平`

---

### Task 2：能量二分与卡组下限（§3.1–3.2、总规格 4.6.5）

**Files:**
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`
- Modify: `Src/mod/combat/DeckPreset.cs`、`Src/mod/combat/CombatConstants.cs`
- Modify: `Src/mod/combat/effects/SkillActionExecutor.cs`（`ApplyGainResource` 改灌可用池）
- Test: `Tests/kemo_card.Ui.Tests/Combat/CharacterBattleInstanceTests.cs`、`DeckPresetTests.cs`

**Interfaces:**
- Produces（后续任务全部依赖）：

```csharp
public int CurrentEnergy { get; }             // 当前能量：每回合灌入额度，clamp [0, MaxEnergy]
public int AvailableEnergy { get; }           // 当前可用能量：出牌扣费池，可超 MaxEnergy
public void RegenCurrentEnergy();             // 当前能量 = min(当前+1, MaxEnergy)
public void RefillAvailableEnergy();          // 可用 = 当前（覆盖，不累加）
public bool TryConsumeAvailableEnergy(int amount);
public void RefundAvailableEnergy(int amount);   // 无上限
public void GainAvailableEnergy(int amount);     // 无上限（技能/效果加的都是可用池）
```

- [ ] **Step 1: 写失败测试**：
  - 开战 `CurrentEnergy = clamp(initialEnergy,0,max)`，`AvailableEnergy = 0`（灌入由管线做，T5 前手动调 `RefillAvailableEnergy` 断言）；
  - `RegenCurrentEnergy` 达上限不再涨；
  - `GainAvailableEnergy` 可将可用池抬过 `MaxEnergy`；
  - `RefillAvailableEnergy` 覆盖而非累加（先 Gain 抬高，再 Refill → 等于 CurrentEnergy）；
  - `Refund` 后可用池增加且无上限；
  - `DeckPreset.Validate`：0 张 → Fail；1 张与 10 张 → Ok。
- [ ] **Step 2: 跑测试失败。**
- [ ] **Step 3: 实现**：`CurrentEnergy` 保留原字段语义、新增 `AvailableEnergy`；删除旧 `TryConsumeEnergy` / `GainEnergy`（调用点改到新方法；`ApplyGainResource` → `GainAvailableEnergy`）。`CombatConstants` 加 `public const int MinCardsPerDeck = 1;`，`DeckPreset.Validate` 开头加 `if (_cardIds.Count < CombatConstants.MinCardsPerDeck) return DeckValidationResult.Fail([]);`
- [ ] **Step 4: 全绿 → format → 提交**：`能量拆分为当前能量与当前可用能量，补卡组最少一张校验`

---

### Task 3：手牌标记入队模型与确认锁定（§2.2、§4.1）

**Files:**
- Modify: `Src/mod/combat/HandSlot.cs`、`Src/mod/combat/statemachine/QueuedCardEntry.cs`
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`（`ApplyPlayCard` / `ApplyCancelQueuedCard` / `ApplyConfirmCharacter`）
- Create: `Src/mod/combat/commands/UnconfirmCharacterCommand.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatCommandTests.cs`、`HandSlotTests.cs`

**Interfaces:**
- Produces:

```csharp
// HandSlot 新增
public long? MarkedSequence { get; }     // null = 未标记
public bool IsMarked => MarkedSequence.HasValue;
public void Mark(long sequence);
public void Unmark();

// QueuedCardEntry 增加 Paid（Energy 入队实扣）
public sealed record QueuedCardEntry(
	int CharacterIndex, string CardId, string RuntimeInstanceId,
	int Priority, IReadOnlyList<CombatTargetRef> Targets, long Sequence, int Paid);

public sealed record UnconfirmCharacterCommand(int CharacterIndex) : ICombatCommand;
```

- [ ] **Step 1: 写失败测试**：
  - 出牌后**牌仍在手牌槽**（`slot.IsEmpty == false` 且 `IsMarked == true`），队列有对应项且 `Paid == cost`；
  - `CostType == None` 入队 `Paid == 0`；`Health/Gold/Discard/X` 入队**拒绝**（返回失败，软失败语义）；
  - 已标记槽不可重复标记；
  - 取消标记：退 `Paid` 到可用池、槽位去标、牌仍在手；
  - **已确认（HasActed）角色**：出牌与取消标记均拒绝；
  - `UnconfirmCharacterCommand` 后可再编辑，且**不清**已有标记；
  - 空确认允许（零标记直接确认）；四人确认切执行阶段。
- [ ] **Step 2: 跑测试失败。**
- [ ] **Step 3: 实现**：
  - `ApplyPlayCard`：校验（未确认、槽有牌未标记、费用类型 v1 白名单）→ Energy 扣可用池 → `slot.Mark(sequence)` → 入队（含 `Paid`），**不再 `ClearCard`**；
  - `ApplyCancelQueuedCard`：要求角色**未确认**（已确认返回失败「须先取消确认」）→ 出队 → `RefundAvailableEnergy(entry.Paid)` → 按 `RuntimeInstanceId` 找槽 `Unmark()`；**不再**自动置未确认；
  - `ApplyUnconfirmCharacter`：`SetHasActed(false)`，仅玩家阶段；
  - 提取共用方法 `CancelMarkAndRefund(simulation, entry)`（出队+退费+去标），供 T8/T10/T11 复用，放在 `CombatStateMachine` 内 `internal static`。
- [ ] **Step 4: 修复受影响的既有测试（出牌离手断言等）→ 全绿 → format → 提交**：`出牌改为手牌标记入队，新增取消确认指令与确认锁定`

---

### Task 4：费用即时对账（§3.3）

**Files:**
- Create: `Src/mod/combat/statemachine/QueuedCostReconciler.cs`
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`（玩家阶段每次技能/效果执行后调用）
- Test: `Tests/kemo_card.Ui.Tests/Combat/QueuedCostReconcilerTests.cs`（新建）

**Interfaces:**
- Produces:

```csharp
public static class QueuedCostReconciler
{
	// 返回被自动取消的持有者索引集合（供回退未确认与 UI 提示）
	public static IReadOnlySet<int> Reconcile(CombatSimulation simulation);
}
```

- [ ] **Step 1: 写失败测试**（用可变费用手段：测试中直接改卡牌定义副本或用 GAS 修饰费用属性；若 v1 无动态费用通道，用注册两张同 id 不同 cost 的定义替换 registry 模拟——按现有 `CombatTestHelper` 能力选实现）：
  - 新费用 < `Paid`：退差额、`Paid` 更新；
  - 新费用 > `Paid` 且可用池够：补差、`Paid` 更新；
  - 不够：整项自动取消、全额退 `Paid`、该角色 `HasActed=false`。
- [ ] **Step 2: 失败 → Step 3 实现**：遍历 `PeekAllOrdered()` 快照，对 `CostType==Energy` 的项按当前定义费用对账；自动取消走 T3 的 `CancelMarkAndRefund` 并 `SetHasActed(false)`。玩家阶段内每次 `CastActiveSkill`、每次效果执行后调用（挂在 `TryApplyPlayerPhase` 成功路径尾部）。
- [ ] **Step 4: 全绿 → format → 提交**：`已标记卡牌费用变化即时对账，不足自动取消并回退未确认`

> 注：v1 无动态费用内容，本任务是管线钩子 + 单测保障；`costScaling` 接入时直接复用。

---

### Task 5：玩家阶段开始管线与抽牌/洗牌（§3.2、§4.2、§4.4、§6.2）

**Files:**
- Create: `Src/mod/combat/statemachine/PlayerPhasePipeline.cs`
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`（`S` 计数器、洗牌抽牌、抽牌修正）
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`（进入玩家阶段时执行管线）
- Modify: `Src/mod/combat/runtime/CombatSimulation.cs`（`DrawRng`：`new HostRng(runSeed, "combat.draw")`；`IsFirstPlayerPhase` 标记）
- Test: `Tests/kemo_card.Ui.Tests/Combat/PlayerPhasePipelineTests.cs`（新建）

**Interfaces:**
- Produces:

```csharp
// CharacterBattleInstance 新增
public int SkillCounter { get; }                       // S
public int SkillCounterCap { get; }                    // Cap（T7 接 activeSkillChain 前恒 0）
public void TickSkillCounter();                        // S = min(S+1, Cap)
public void AddDrawModifier(int delta);                // 抽牌数量修正（正=增益，负=减益）
public int ComputeDrawCount();                         // max(0, 1 + 最大增益 − 最大减益)
public int DrawWithReshuffle(int count, HostRng rng);  // 空堆时弃牌堆洗回（每阶段≤1次）
public void ResetPhaseShuffleBudget();                 // 阶段开始重置洗牌次数

public static class PlayerPhasePipeline
{
	public static void Run(CombatSimulation simulation, bool isFirstPlayerPhase);
}
```

- [ ] **Step 1: 写失败测试**：
  - 首回合：能量不 +1、可用=当前、`S` +1、**不**公式抽牌；
  - 次回合起：当前能量 +1（到上限止）、可用覆盖灌入、公式抽牌；
  - 公式：无修正抽 1；修正 {+2,+1,−1,−3} → `max(0, 1+2−3) = 0`；
  - 手牌满 5 停止抽；
  - 抽空牌堆 → 弃牌堆洗回再抽；同阶段第二次触发洗牌 → 停止抽牌；
  - 洗牌用 `DrawRng`，固定种子结果可复现。
- [ ] **Step 2: 失败 → Step 3 实现**：

```csharp
public static void Run(CombatSimulation simulation, bool isFirstPlayerPhase)
{
	foreach (var character in simulation.PlayerTeam.Characters)
	{
		character.ResetPhaseShuffleBudget();
		if (!isFirstPlayerPhase)
			character.RegenCurrentEnergy();
		character.RefillAvailableEnergy();
		character.TickSkillCounter();
		if (!isFirstPlayerPhase)
			character.DrawWithReshuffle(character.ComputeDrawCount(), simulation.DrawRng);
		// 封印处理在 T10 追加到此处（第 5 步）
	}
}
```

  抽牌修正内部存 `List<int> _drawModifiers`；`ComputeDrawCount` 取 `max(正数)` 与 `max(−负数)`，不叠加。`CombatStateMachine.Advance` 的 `BattleStart→Player` 与 `Enemy→Player` 两处调用管线并维护 `IsFirstPlayerPhase`。
- [ ] **Step 4: 全绿 → format → 提交**：`实现玩家阶段开始管线：能量灌入、技能计数、公式抽牌与弃牌堆洗回`

---

### Task 6：BattleStart 管线与开战技能注入入口（§6.1、§1.2）

**Files:**
- Create: `Src/mod/combat/runtime/BattleStartSkillEntry.cs`
- Modify: `Src/mod/combat/runtime/CombatSimulation.cs`、`CombatSimulationFactory.cs`
- Modify: `Src/mod/combat/runtime/PlayerTeamState.cs`（SharedHp 写锁 + 冻结补满）
- Modify: `Src/mod/combat/gas/TeamMaxHealthCoordinator.cs`（去 +delta，仅 clamp）
- Test: `Tests/kemo_card.Ui.Tests/Combat/BattleStartPipelineTests.cs`（新建）、`Gas/TeamMaxHealthCoordinatorTests.cs`

**Interfaces:**
- Produces:

```csharp
// 调用方（Run 层，后续任务）负责排序：被动槽 0→3 且潜能档低→高在前，修饰按插入序在后
public sealed record BattleStartSkillEntry(string SkillId, int SourceCharacterIndex); // -1 = 队伍来源（修饰）

// CombatSimulation
public IReadOnlyList<BattleStartSkillEntry> BattleStartSkills { get; }   // 构造注入，默认空
public void RunBattleStart();   // 注入技能 → 冻结补满 → 开局抽满 5 → 进入首玩家阶段管线

// PlayerTeamState
public bool SharedHpLocked { get; set; }   // true 时 ApplySharedDamage/HealShared 无操作+诊断日志
public void FreezeAndFillSharedHp();       // SharedHp = MaxSharedHp（唯一合法补满入口）
```

- [ ] **Step 1: 写失败测试**：
  - 注入技能按列表顺序各执行一次（用记录执行顺序的测试效果验证）；
  - 注入技能内含 Shared 伤害/治疗 → 无操作（SharedHp 不变），有诊断可断言（日志回调或计数器）；
  - 注入技能改 `MaxHealth` → 结算后 `MaxSharedHp` 重算，`FreezeAndFillSharedHp` 后 `SharedHp == MaxSharedHp`；
  - 开局每人抽满 5（走 T5 的 `DrawWithReshuffle`，不占首玩家阶段洗牌预算）；
  - `RunBattleStart` 后 Phase == Player 且首回合管线已按 T5 规则执行（能量未 +1、S==min(1,Cap)）；
  - 非法技能 id → 跳过（软失败）；
  - `TeamMaxHealthCoordinator`：MaxHp 上升 SharedHp **不变**；下降且 SharedHp 越界 → clamp。
- [ ] **Step 2: 失败 → Step 3 实现**：
  - `RecomputeAndFollowDelta` 改名 `RecomputeAndClamp`：`newHealth = MathF.Min(oldHealth, newMax)`，删除 `+delta` 分支；
  - `RunBattleStart`：`SharedHpLocked = true` → 逐条 `ExecuteSkillPayload`（source 按 `SourceCharacterIndex`，-1 用 `CombatTargetRef.PlayerTeam`；目标解析用技能 `TargetOverride`，缺省 self）→ `SharedHpLocked = false` → `FreezeAndFillSharedHp()` → 每人 `DrawWithReshuffle(5, DrawRng)` → `TransitionTo(Player)` + `PlayerPhasePipeline.Run(this, isFirstPlayerPhase: true)`；
  - `CombatSimulationFactory.TryCreate` 增加可选参数 `IReadOnlyList<BattleStartSkillEntry>? battleStartSkills = null` 透传；
  - 原 `Advance` 的 `BattleStart → Player` 分支改为调 `RunBattleStart()`。
- [ ] **Step 4: 全绿 → format → 提交**：`实现 BattleStart 管线：开战技能注入、SharedHp 冻结补满与开局满手`

---

### Task 7：主动技蓄力链（§5 全节 + 2026-07-28 目标校验决议）

**Files:**
- Modify: `Src/frame/content/definitions/CharacterDto.cs`、`SharedDefinitionDtos.cs`
- Create: `Src/mod/combat/commands/CastActiveSkillCommand.cs`
- Delete: `Src/mod/combat/commands/CastInstantSkillCommand.cs`
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`（链快照、档位解析、`T_k` 扣费、`GainSkillCounter`）
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`（指令替换）
- Modify: `Src/mod/combat/effects/SkillActionExecutor.cs`（`GainResource` 支持 `resource: "SkillCounter"` 显式加 `S`，供连发）
- Create: `Src/mod/combat/CombatContentValidator.cs`（链配置校验：至少 1 项、各档 `cooldown >= 1`）
- Test: `Tests/kemo_card.Ui.Tests/Combat/ActiveSkillChainTests.cs`（新建）；改 `CombatCommandTests.cs`

**Interfaces:**
- Produces:

```csharp
// DTO（JSON：activeSkillChain）
public sealed class ActiveSkillChainEntryDto
{
	[JsonPropertyName("skillId")] public string SkillId { get; init; } = "";
	[JsonPropertyName("cooldown")] public int Cooldown { get; init; }
}
// CharacterDto 新增
[JsonPropertyName("activeSkillChain")]
public List<ActiveSkillChainEntryDto> ActiveSkillChain { get; init; } = [];

public sealed record CastActiveSkillCommand(
	int CharacterIndex, IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand;

// CharacterBattleInstance
public int ResolveCastableTier();     // 当前 S 达标的最高档；-1 = 不可放
public int GetTierThreshold(int k);   // T_k = C0+...+Ck
public void PaySkillCounter(int tk);  // S = max(0, S − T_k)
public void GainSkillCounter(int n);  // S = min(S + n, Cap)
```

- [ ] **Step 1: 写失败测试**（对照 §5.3 示例表 `C0=4,C1=6,C2=6`）：
  - `S=3` 拒绝；`S=4..9` 放基础扣 `T_0=4`；`S=10..15` 放蓄力Ⅰ扣 `T_1=10`；`S=16` 放蓄力Ⅱ扣 `T_2=16`；
  - `S=15` 放蓄力Ⅰ → `S=5`（溢出资保留），同阶段可再放基础；
  - 效果含 `resource:"SkillCounter", amount:4` 的连发：`S=10` 放Ⅰ → 扣 10 → 效果 +4 → `S=4` → 可再放基础；
  - `S` 每阶段 +1 含首回合、达 `Cap` 不涨（T5 已建 Tick，此处断言 Cap 来自链配置）；
  - **不占已行动**：已确认角色可释放；释放不改 `HasActed`；
  - 目标校验按**将释放档位**的技能目标规格（各档配不同 `TargetOverride`，低档单体高档全体：`S` 处于低档时传全体目标 → 拒绝且 `S` 不消耗）；
  - 卡牌 `skillRefs` 执行不动 `S`；
  - 无链配置（空 `activeSkillChain`）→ 指令拒绝；
  - 校验器：空链警告可跳过（角色可以没有主动），`cooldown < 1` → 校验失败。
- [ ] **Step 2: 失败 → Step 3 实现**：链快照在 `CharacterBattleInstance.TryCreate` 时从 `CharacterDto.ActiveSkillChain` 读入（`Cap = sum(cooldown)`）；`ApplyCastActiveSkill`：封印检查（T10 接入）→ `ResolveCastableTier` → 目标按该档技能目标规格校验 → `PaySkillCounter(T_k)` → `ExecuteSkillPayload` → 目标丢失回滚（T8 接入）→ 费用对账（T4）。删除 `CastInstantSkillCommand` 及其分支，重写受影响测试。
- [ ] **Step 4: 全绿 → format → 提交**：`实现主动技蓄力链：S 计数器、自动最高档、累计阈值扣费与档位目标校验`

---

### Task 8：目标丢失与执行期目标失效（§2.3、§2.4）

**Files:**
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/PlayerPhaseRollbackTests.cs`、新增 `CardExecutionRetargetTests.cs`

**Interfaces:**
- Consumes: T3 `CancelMarkAndRefund`；`ERetargetPolicy`（已有枚举）。
- Produces: 玩家阶段目标丢失处理 `HandleTargetLoss(simulation, lostEnemies)`（internal static，主动技与后续即时通道共用）。

- [ ] **Step 1: 写失败测试**：
  - 玩家阶段：主动技击杀敌人 A → 目标含 A 的**已标记牌整张取消**（退 `Paid`、槽去标）、持有者回退未确认；同角色其它合法标记**保留**；
  - 多选牌只要含 A → 整张取消；
  - 执行阶段单体目标失效：`Default` 策略 → 用 Run RNG 在合法池**均匀**重选（固定种子断言可复现）；`Skip` → 空放；
  - 执行阶段多目标：去掉非法目标对**剩余子集**结算（不再整体放弃）；子集空 → 空放；
  - **空放进弃牌堆**：执行阶段无论正常结算/空放，结算完成后牌从手牌槽移入弃牌堆、槽位清空；
  - 空放不回滚已行动。
- [ ] **Step 2: 失败 → Step 3 实现**：
  - `HandleTargetLoss`：替换现 `ApplyInstantSkillTargetLossRollback`——找相关标记 → `CancelMarkAndRefund` → `SetHasActed(false)`；
  - `ResolveCardTargets`：多目标改为过滤后子集直接返回（非空即结算）；单体 `Default`/`RandomLegal` → `simulation.RetargetRng`（`new HostRng(runSeed, "combat.retarget")`）在重算合法池均匀取一；
  - `ExecuteCardExecutionPhase`：每项出队结算后（含 `resolvedTargets.Count==0` 的空放路径）执行「移入弃牌堆 + 槽位 `ClearCard`」（按 `RuntimeInstanceId` 找槽）。
- [ ] **Step 4: 全绿 → format → 提交**：`对齐目标丢失回滚与执行期重定向，空放与结算后卡牌进弃牌堆`

---

### Task 9：玩家侧 Shared 结算模型（§1.2、§1.3、`ETargetScope.Team` 决议）

**Files:**
- Modify: `Src/frame/content/definitions/ContentEnums.cs`（`ETargetScope` 加 `Team`）
- Modify: `Src/mod/combat/effects/CombatEffectExecutor.cs`（伤害/治疗结算通道）
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`（目标解析支持 `Team` scope）
- Modify: `Src/mod/combat/CombatContentValidator.cs`（Heal 目标校验）
- Test: `Tests/kemo_card.Ui.Tests/Combat/SharedSettlementTests.cs`（新建）、`Combat/CombatEffectExecutorTests.cs`、`Gas/DamageExecutionTests.cs` 相关用例重写

**Interfaces:**
- Consumes: `CombatTargetRef.PlayerTeam`（Index=-1 哨兵，保持不变）。
- Produces: 结算规则——
  - 玩家槽位目标伤害：对该槽跑 `DispatchBeforeDamage`（槽位护盾/减伤在此参与），**结果扣 `PlayerTeam.SharedHp`**，不写角色 ASC Health；
  - `scope: Team`（解析为 `CombatTargetRef.PlayerTeam`）伤害：对账本结算一次，**不经**分槽护盾钩子；
  - Heal：目标必须是 Team 引用；玩家槽位目标 Heal → 软失败 + 诊断日志（内容校验兜底）；
  - 敌人目标：维持独立 HP 结算不变。

- [ ] **Step 1: 写失败测试**：
  - AoE 点 4 槽、每槽 10 伤 → SharedHp −40（分算 4 次）；
  - 某槽有减伤规则（测试规则改写 `DamagePacket`）→ 仅该槽次结算被修正；
  - Team 直伤 10 → SharedHp −10 一次，且**不**触发槽位减伤规则；
  - Heal `scope:Team` 20 → SharedHp +20 上限 MaxSharedHp；
  - Heal 点玩家槽 → SharedHp 与角色属性均不变（软失败）；
  - 玩家角色 ASC 的 `Health` 当前值在任何伤害后**不再被写**；
  - 校验器：`EEffectKind.Heal` 的卡牌/技能目标为玩家侧 `Single/All` → 校验失败；`Team` → 通过；
  - BattleStart 锁定下（T6）Team 伤害/治疗无操作（复验）。
- [ ] **Step 2: 失败 → Step 3 实现**：`ApplyDamage` 按目标分流（player slot / player team / enemy）；`ApplyHeal` 仅接受 team 引用；`ResolveCardTargets`/敌人目标解析把 `scope:Team` 解析为 `CombatTargetRef.PlayerTeam`（玩家侧）或敌方全体逐个（敌方侧 v1 无账本，`Team` 解析为全体一次性伤害语义暂不支持 → 校验器限制 `Team` 仅用于玩家侧账本，敌方向内容留开放项）。
- [ ] **Step 4: 全绿 → format → 提交**：`玩家侧伤害治疗对齐共享账本结算：分槽扣 Shared、Team 一次结算、Heal 仅 Team`

---

### Task 10：封印（§2.5）

**Files:**
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`（`IsSealed`）
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`、`PlayerPhasePipeline.cs`
- Modify: `Src/mod/combat/effects/GameplayEffectApplicator.cs` 或执行器（封印生效通知）
- Test: `Tests/kemo_card.Ui.Tests/Combat/SealTests.cs`（新建）

**Interfaces:**
- Produces:

```csharp
// 封印以 GAS GrantedTags 承载：约定标签 "combat.state.sealed"
public bool IsSealed { get; }   // 读 Asc 是否持有该标签
// CombatStateMachine
internal static void EnforceSeal(CombatSimulation simulation, int characterIndex);
// 清全部标记退 paid + SetHasActed(true)；每次技能/效果执行后对新封印角色调用
```

- [ ] **Step 1: 写失败测试**：
  - 挂封印（测试 GameplayEffect 授予标签）→ 该角色全部标记被清、`Paid` 全退、`HasActed == true`；
  - 封印中 `PlayCardCommand` / `CastActiveSkillCommand` → 拒绝（软失败）；`UnconfirmCharacterCommand` 无效；
  - 玩家阶段开始管线：封印角色能量/`S`/抽牌**照常**，管线末尾套用行动封锁（清标记+已行动）；
  - 中途封印**不**回滚本阶段已获得的能量/`S`/牌；
  - 四人确认判定把封印视作已行动（3 人确认 + 1 人封印 → 切执行阶段）。
- [ ] **Step 2: 失败 → Step 3 实现**：`PlayerPhasePipeline.Run` 第 5 步接 `EnforceSeal`；玩家阶段每次指令成功执行后扫描新封印角色（与 T4 对账钩子同位置）。
- [ ] **Step 4: 全绿 → format → 提交**：`实现封印：清标记退费视作已行动并禁主动，资源管线照常`

---

### Task 11：弃牌分通道与中途抽牌禁令（§4.3、§4.6）

**Files:**
- Create: `Src/mod/combat/effects/EDiscardChannel.cs`
- Modify: `Src/mod/combat/effects/SkillActionExecutor.cs`、`CombatEffectExecutor.cs`
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`（`DiscardRandomUnmarked`）
- Test: `Tests/kemo_card.Ui.Tests/Combat/DiscardChannelTests.cs`（新建）

**Interfaces:**
- Produces:

```csharp
public enum EDiscardChannel { ActiveSkill, CardExecution, Other }
// CombatSimulation 增加执行上下文（当前通道），由状态机在进入主动技/执行阶段/敌方阶段时设置
public EDiscardChannel CurrentDiscardChannel { get; }
// CharacterBattleInstance
public int DiscardRandomUnmarked(int count, HostRng rng);   // Run RNG 均匀；池空弃 0（软失败）
```

- [ ] **Step 1: 写失败测试**：
  - **中途 Draw 拒绝**：`EEffectKind.Draw` / `ESkillActionKind.Draw` 执行 → 无操作 + 诊断（`AddDrawModifier` 通道仍可用，走 T7 的 `GainResource` 风格新动作 `ModifyDrawCount`）；
  - `ActiveSkill` 通道弃牌：可弃到**已标记**牌 → 该标记取消、退 `Paid`、持有者回退未确认；
  - `CardExecution` / `Other` 通道：只从**未标记**手牌 Run RNG 均匀弃（固定种子可复现）；已标记牌不受影响；
  - 未标记池空 → 弃 0 张、不中断所属动作其余部分、不改确认态；
  - 弃牌进持有者弃牌堆。
- [ ] **Step 2: 失败 → Step 3 实现**：删除旧 `DiscardFromHand` 的确定性实现，`ApplyDiscard` 按 `CurrentDiscardChannel` 分流（`ActiveSkill`：均匀随机含已标记，命中标记走 `CancelMarkAndRefund` + 回退未确认；其余：`DiscardRandomUnmarked`）；`ApplyDraw` 改为诊断日志 + 无操作；新增 `ESkillActionKind.ModifyDrawCount` → `AddDrawModifier`。弃牌 RNG 用 `simulation.DrawRng` 同流或独立 `"combat.discard"` 流（择一并在测试固定）。
- [ ] **Step 4: 全绿 → format → 提交**：`弃牌按通道分流：主动可弃标记并回退，其余随机未标记；封禁中途即时抽牌`

---

### Task 12：收尾——集成测试、规格 §7 回写、全量核对

**Files:**
- Modify: `Tests/kemo_card.Ui.Tests/Combat/CombatSimulationIntegrationTests.cs`
- Modify: `Doc/superpowers/specs/2026-07-21-combat-system-design.md`（§7 更新为「已对齐 + 遗留项」）

- [ ] **Step 1: 集成测试**：固定种子完整回合脚本——BattleStart（注入 2 条技能改 MaxHealth）→ 开局满手 → 标记 2 张 + 主动技 → 确认 4 人 → 执行（priority 降序断言）→ 敌方阶段 → 次回合能量 +1 / 公式抽牌 → 胜利判定；断言全程 SharedHp 轨迹与手牌/弃牌堆状态可复现。
- [ ] **Step 2: 对照规格 §8 测试要点清单逐条勾稽**，缺失用例补齐（尤其：跨波保留 SharedHp、越界 clamp、非 Energy 拒绝、空确认）。
- [ ] **Step 3: 更新规格 §7**：改写为「2026-07-28 对齐完成」状态，列出显式遗留项（敌方侧 `Team` scope 语义、点选式主动弃牌 UI、`costScaling` 动态费用、Run 层 BattleStart 接线）。
- [ ] **Step 4: 全量 `dotnet test` + 受改动文件 `dotnet format` → 提交**：`战斗系统对齐收尾：集成回归、规格差距清单回写`

---

## Self-Review 记录

- **规格覆盖**：§1（T6/T9/T12）、§2（T1/T3/T4/T8/T10）、§3（T2/T4/T5）、§4（T3/T5/T11）、§5（T7）、§6（T5/T6）、§8 测试要点（各任务 Step 1 + T12 勾稽）。§7 由 T12 回写。
- **决议纳入**：`ETargetScope.Team`（T9）、主动技按档位目标校验（T7）。
- **类型一致性**：`CancelMarkAndRefund`（T3 定义，T8/T10/T11 消费）；`BattleStartSkillEntry` / `RunBattleStart`（T6 定义，Run 层后续消费）；`AvailableEnergy` 系列（T2 定义，T3/T4/T5/T10 消费）；`DrawWithReshuffle`（T5 定义，T6 消费）。
- **已知妥协（写入 T12 遗留项）**：主动技弃牌 v1 为「均匀随机含已标记」，点选后置；敌方侧 `Team` scope v1 校验器禁用；封印以固定标签 `combat.state.sealed` 约定承载。
