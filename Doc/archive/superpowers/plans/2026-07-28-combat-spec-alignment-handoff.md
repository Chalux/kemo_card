# 战斗系统规格对齐 —— 交接说明（T1–T12 全部完成）

> 本文是**上下文交接**文档,不是计划本体。
> 计划本体:[2026-07-28-combat-spec-alignment-implementation-plan.md](../../../superpowers/plans/2026-07-28-combat-spec-alignment-implementation-plan.md)
> 权威规格:[2026-07-21-combat-system-design.md](../../../superpowers/specs/2026-07-21-combat-system-design.md)(文中「§n」均指此文档)
>
> **接手方式**:计划本体里 T1–T12 的步骤已全部完成,不必重做。读本文的「决议」「已建成的 API」「最终遗留项」即可。

## 当前状态

- **T1–T12 全部完成**,`dotnet test` **486 passed / 0 failed**(起点 321 → T9 后 471 → 收尾 486)。
- **未创建任何 git commit**(用户明确要求:改动直接留在工作区)。
- T7 五条歧义已裁定;T9 的 `HighestHp`/`LowestHp` 歧义**默认保持现状**(用户未另裁)。
- 规格 §7 已回写为「2026-07-28 对齐完成」+ 显式遗留项。

## 工作约定(用户已确认,请沿用)

- **不要 git commit**,除非用户另行要求。
- 交流与文档一律简体中文。
- 测试强制英文输出:

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q 2>&1 | Select-String -Pattern "error CS|Passed!|Failed!"
```

格式化:`--include` 只指向真正改过的文件。缩进与风格以仓库 `.editorconfig` 为准。

## 关键决议(用户逐条拍板)

| # | 议题 | 决议 |
|---|------|------|
| 1 | 抽牌数量修正生命周期 | 每个玩家阶段抽牌结算后无条件清空 |
| 2 | 玩家分槽伤害与 GAS | **应用后转移**(GE → 取 Health 变化 → 复位 → 转扣 SharedHp) |
| 3 | base-game 主动链 | 主动迁移 `kemo` 到 `activeSkillChain`(含蓄力档 `kemo_dash_charged`) |
| 4 | SharedHpLocked 可观测性 | `BlockedSharedHpWriteCount` 计数器(禁止 `GD.Print`) |
| 5 | `TargetOverride` null | 视作 Self 单体 |
| 6 | 玩家侧 HighestHp/LowestHp | **保持现状**,不在内容校验禁止 |

## T10–T12 本批建成

### T10 封印(§2.5)

- `IsSealed` ← GAS 标签 `combat.state.sealed`(`CombatConstants.SealedTag`)。
- `CombatStateMachine.EnforceSeal`:清全部标记、退 Paid、`SetHasActed(true)`。
- 封印中拒绝 `PlayCardCommand` / `CastActiveSkillCommand`;`UnconfirmCharacterCommand` 无效。
- `PlayerPhasePipeline.Run` 末尾接 `EnforceSeal`;玩家阶段每次指令成功后扫描(与对账同位置)。
- `GameplayEffectApplicator` 应用后若目标已封印则立刻 `EnforceSeal`。
- **债务 1 已收**:`ApplyCastActiveSkill` 入口封印检查已接上。

### T11 弃牌分通道与中途抽牌禁令(§4.3 / §4.6)

- `EDiscardChannel { ActiveSkill, CardExecution, Other }`;`CombatSimulation.CurrentDiscardChannel` / `DiscardRng`(`combat.discard`)。
- ActiveSkill:均匀随机可含已标记 → `CancelMarkAndRefund` + 回退未确认。
- CardExecution / Other:`DiscardRandomUnmarked`;池空弃 0、软失败。
- 中途 `Draw` → 无操作 + `BlockedMidDrawCount`;`ModifyDrawCount` → `AddDrawModifier`。
- **债务 3 已收**:旧确定性高槽位 `DiscardFromHand` 已删除。

### T12 收尾

- 固定种子集成脚本:`Full_round_script_is_reproducible_and_reaches_victory`。
- §8 缺口补:`Shared_hp_is_preserved_across_waves`。
- 规格 §7 已回写。

## 已建成的 API(全量摘要)

### 队列与费用

- `CardExecutionQueue`:priority **降序** → sequence **升序**;`Paid` 入队实扣。
- `CardCostCalculator.Compute` / `QueuedCostReconciler.Reconcile`。

### 能量 / 手牌 / 抽牌

- `CurrentEnergy` / `AvailableEnergy`;`Regen` / `Refill` / `TryConsume` / `Refund` / `Gain`。
- `HandSlot.MarkedSequence`;入队不离手;执行后进弃牌。
- `AddDrawModifier` / `ComputeDrawCount` / `DrawWithReshuffle`(每阶段至多洗 1 次)。
- `DiscardRandomUnmarked` / `PickRandomOccupiedSlot`。

### 阶段管线

- `PlayerPhasePipeline.Run`(§6.2,含封印步骤)。
- `RunBattleStart`(§6.1);`BattleStartSkillEntry`;`DrawRng` / `RetargetRng` / `DiscardRng`。

### 主动技 / 目标 / SharedHp / 封印 / 弃牌

- `ActiveSkillChain` + `S` / `T_k` / `CastActiveSkillCommand`。
- `HandleTargetLoss` / 执行期重定向;`SharedHpSettlement.RunTransferred`;`ETargetScope.Team`。
- `IsSealed` / `EnforceSeal`;`EDiscardChannel` / `BlockedMidDrawCount` / `ModifyDrawCount`。

## 最终遗留项

1. **敌方侧 Team scope** 无处结算(空放)。
2. **点选式主动弃牌 UI** 后置;v1 随机含已标记。
3. **`CardCostCalculator` 恒等**;`costScaling` 未接。
4. **Run 层 BattleStart 接线**未做。
5. **链配置加载期校验**缺(仅建实例时 + 出货冒烟测试)。
6. **独立技能 Heal 目标校验**覆盖不到。
7. **诊断计数器无 UI/日志出口**。
8. **`Team_scope_card_fires_blank...` 断言偏弱**(账本已空时空放 vs clamp 0 难分)。
9. **`ApplySharedDamage` 在 `IsDefeated` 后 early return** 吞后续同批伤害。
10. **更窄「仅禁出牌」Debuff** 是否需要——规格 §9 开放。

## 与计划的偏离(仍有效)

1. T6/T10+ 禁止在 `Src/mod/combat/` 用 `GD.Print`(测试宿主无 Godot);诊断用计数器。
2. T1 用显式 `IComparer` 而非元组比较器。
3. `CombatContentValidator` 只能接在 mod 层建实例时。
4. `CreateForTests` 的 `skillCounterCap` 保留为无链回落值。
5. T11 弃牌 RNG 选用独立流 `"combat.discard"`(非复用 `DrawRng`)。

## 测试文件位置(增量)

| 文件 | 覆盖 |
|------|------|
| `SealTests.cs` | T10 封印(7) |
| `DiscardChannelTests.cs` | T11 弃牌通道 / 中途 Draw(6) |
| `CombatSimulationIntegrationTests.cs` | T12 完整回合 + 跨波 SharedHp |
| 其余 T1–T9 文件 | 见前序交接;基线 471 起算 |
