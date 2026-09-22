# Buff 运行时 · 团体潜能 · 连携 系统设计

**日期**：2026-09-19
**状态**：已实装（chalux 角色为首个使用者）
**关系**：服从 [2026-05-11 总规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md) 与 [2026-07-21 战斗规格](../../../superpowers/specs/2026-07-21-combat-system-design.md)；**潜能一节替代总规格 §4.5.2–4.5.3 的旧模型**。

---

## 1. BuffInstance 运行时（原 Buff 系统死代码的实装）

原 `BuffDto`/`buffRefs` 只做内容校验、不参与战斗（`CombatEffectExecutor` 对 ApplyBuff/RemoveBuff no-op）；`HandSlotEffectRef` 为占位。本设计把 buff 实装为**被动/增益/减益/槽位效果的唯一载体**。

### 1.1 数据（BuffDto 扩展）

- `modifiers`: 属性修正列表（镜像 GE modifier：attributeId / op(Add|Multiply|Divide|Override) / magnitude(Scalar|SetByCaller)），幅度 × 层数。
- `condition`: 持有者条件 `{elementAny, raceAny, matchAll}`。列表内"或"、跨列表默认"或"、`matchAll: true` 取"且"。
- `applyScope`: `Self`（默认）/ `AllAllies`（团队型被动挂到每个队友）。
- 钩子节点：`onApply / onTurnStart / onTurnEnd / onStackChanged / onRemove / onWaveStart / onActiveSkillCast / onSlotCardPlayed`。
- `onTurnStart` 效果参数支持 `turnInterval: N`：按**波内回合计数**每 N 回合触发一次。
- 钩子效果目标解析：`hookTargets`（self 缺省 / randomEnemy / allEnemies）与 `targetFilter`（self / elementAny / raceAny 筛选玩家角色），按**单个效果引用**的合并参数（效果参数 + 实例挂载参数）解析。

### 1.2 tag 约定（取代独立字段）

`BuiltinBuffTags`：`buff.passive` / `buff.active` / `buff.leader`（预留队长技与"被动无效/沉默"类 debuff 按类别筛选）、`buff.undispellable`（不可驱散）、`slot.damage`（槽位伤害）、`slot.charge`（充能）、`trait.immune_slot_damage`、`trait.chain_inject_red`。
旧布尔 `dispellable: false` 在读取层归一为 undispellable tag（外部 mod 内容兼容）。**驱散规则**：任何清除效果只移除无 `buff.undispellable` tag 的 buff。

### 1.3 运行时

- 挂点三类容器：角色（含 ASC）、敌人（含 ASC）、手牌槽位（无 ASC，只承载钩子与 tag）。
- 修正经 `Aggregator.SetModifiersForHandle` 走与 GameplayEffect **同一条聚合管线**（Override 优先 → (base+ΣAdd)×ΠMul）。
- **条件休眠**：条件不满足 → 撤销句柄、不参与聚合、不触发钩子、UI 不显示；**不移除**。每回合开始重估，持有者属性/种族变化下一回合自动切换。休眠/移除后必须 `RecalculateAll`。
- **时长**：`durationType: Turns` 由 BuffRuntime 在回合结束统一 tick（先 onTurnEnd → 递减 → 到期 onRemove → 移除），覆盖角色/敌人/槽位全部容器（角色/敌方 ASC 回合钩子的历史缺口由本层接管）。
- **快照语义**（2026-09-19 评审修正）：钩子分发一律在容器**快照**上枚举——钩子可能对自己的容器挂/删 buff，活列表枚举中修改会抛异常。回合结束以触发前快照为本回合基准（钩子期间新增的 buff 本回合不 tick）；到期补发只对**仍持有**的实例触发 onRemove（先被驱散的不双触发）。
- 叠层：Add（至 MaxStacks）/ Refresh（重置时长）/ Replace（移除重建）；互斥组 exclusiveGroup 先删后挂。
- 开战被动：`RunController.StartBattle` 按各角色**已解锁被动**（槽序 + 潜能档低→高）构造 `BattleStartBuffEntry`，在 BattleStart 管线中于技能注入之后、冻结 SharedHp 之前挂载。
- **敌人开战 buff（2026-09-20 接线）**：`EnemyDto.buffRefs` 声明的 buff 在 `RunBattleStart` 中（冻结 SharedHp 之后、`onWaveStart` 之前）逐敌挂到其自身，与玩家侧开战注入对称。此前该字段只做内容引用校验、从不生效。首个使用者是训练沙包「木桩」（`enemies/training_dummy.json` + `battles/training_dummy.json`：单波两个木桩、10000 血、每回合开始恢复 10000，无行动意图）。
  > 注：角色侧 `CharacterDto.buffRefs`（如 `kemo_talent`）仍是仅校验的旧通道，未接线（角色被动已统一走 `passives`）。

## 2. 槽位效果（伤害 / 充能）

- **槽位伤害**（`slot.damage` tag）：该槽打出卡牌时（`SettleQueuedCard` 结算前触发，含空放），对打出者造成参数伤害（玩家槽位 → 共享血量账本）。打出者持有 `trait.immune_slot_damage` 时跳过（chalux 被动1）。
- **充能**（`slot.charge` tag）：实例参数 `charge`（罗马序号计数，充能 I = 1）。该槽每打出一张牌计数递减，归零触发 `onSlotCardPlayed` 载荷并**重置计数**（持续期内可反复触发）；到期移除前不因触发消失。chalux 专属卡「绝念」用同一机制投放**充能 II**（`charge: 2`，见 [充能球规格 §7](../../../superpowers/specs/2026-09-19-charge-orb-system-design.md)）。
- **充能互斥（2026-09-21 决议）**：同一手牌槽**只允许存在 1 个充能**。新的充能**无条件覆盖**旧的，并**重置进度**：
  - 覆盖无视 id 与 `stackRule`——即使新旧完全同 id、即使写下的是 `Refresh`/`Add`，也是"移除旧的 + 新建实例"，不会叠层、不会保留已积累的计数；
  - 覆盖时对旧实例补发 `onRemove`（与驱散/到期路径同口径）；
  - 实现位置 `BuffRuntime.StackOrAdd` 的最前置分支（`RemoveExistingCharges`），因此槽位与角色容器的充能投放走同一规则。
  - 语义理由：充能是"这一槽当前在读哪个序列"的唯一状态，允许并存会让"归零触发"的判定与玩家预期脱节。
- 载荷示例：`{kind: Damage, params: {amount: 12, damageGameplayEffectId: ..., hookTargets: "randomEnemy"}}`；DamageExecution 本就加 100% 源物攻，因此"12+100%物攻"即 `amount: 12`，元素（蓝）仅为 damageType 标签，不参与数值。

## 3. 连携（乖离性 MA 式，批量定档）

- 出牌是"标记 → CardExecution 统一结算"批处理：**结算阶段开始**按完整出牌队列一次性统计各属性的**不同角色数**（同一角色多张只计 1 人），档位作用于**本回合全部**该属性伤害/治疗卡——无次序、无首角色惩罚、无回溯。**统计侧与加成侧口径不同（2026-09-21 修正）**：统计侧统计队列里的**所有**卡——Support / Curse 等非输出卡同样把打出它们的角色计入人头；加成侧只作用于连携适用的卡牌类型（Physics/Magical/Healing），即非输出卡堆人头但不吃加成。
- 档位：**2 人 +25% / 3 人 +50% / 4 人 +100%**（1 人无增益；数值由 `ChainCalculator.TwoChainScale / ThreeChainScale / FourChainScale` 三个常量控制，2026-09-20 调档后写死在这三处，改档只改常量）。加成仅对卡牌类型 Physics/Magical/Healing 生效；多属性卡取各属性最高档。
- 加成注入：结算单卡时设 `simulation.CurrentChainBonus`（try/finally 归零，卡牌上下文之外恒 0）；GAS 路径经 SetByCaller `ChainBonusScale`，直伤/治疗路径直接缩放。
- **加算规则（2026-09-21 修订）**：`伤害 = base × (1 + Σ增伤 + Σ受到伤害增加) × (1 + 连携)`——**增伤与受伤增加一律加算，只有连携乘算**（权威表述见 [战斗规格「增伤与受伤增加一律加算」](../../../superpowers/specs/2026-07-21-combat-system-design.md)）。**三条伤害通道（GAS 公式 / 直伤定值 / 充能球）共用同一套缩放与伤害包管线**（2026-09-19 统一），通道差异只在 `base` 怎么算。治疗 = `(amount + 源 HealPower) × (1 + 连携)`，不受增伤影响。`MagicAttack` / `MagicDefense` 的消费公式已于 2026-09-21 落地（GAS 通道 `damageType: "Magical"` → `魔攻 − 魔防`）。
- 注入红（chalux 被动2）：持有 `trait.chain_inject_red` 的角色打出的卡在统计上额外计入红属性（双属性卡 = 各属性 + 红各自计入）。

## 4. 团体潜能（替代总规格 §4.5.2–4.5.3）

### 4.1 模型

- **团体资源**：`TeamPotentialPool` 全队共享；消费记到**玩家槽位**账本（切换角色不丢失数据）。
- 槽位账本：`PotentialDirectCredit`（直充余额）+ `PotentialSpent`（消费流水，逐笔）。
- **被动定义**：`CharacterDto.passives: [{buffId, requiredPotential}]`；`requiredPotential` 即解锁成本，**档位任意数值**（放宽旧规格的 0/20/…/100 六档限制）。0 = 默认解锁。
- 解锁判定 = 存在匹配 (characterInstanceId, buffId) 的流水记录（或成本 0）；返还即重锁。

### 4.2 消费与返还（PotentialService）

- 消费顺序：**先扣本槽位直充（无需表决）再扣团队池**；跨来源拆多笔记账，每笔记录来源（credit/pool）。
- 返还：**按笔（同一角色实例 + 同一被动的全部流水）原子退回原来源**（直充回槽位、池回团队池），对应被动自动重锁。只退一部分会留下"打折解锁"漏洞（存在匹配流水即解锁），因此跨来源拆账的消费不允许部分返还（2026-09-19 评审修正）。
- 任意数额入账走 `Potential.Grant(amount, slotIndex)`（槽位有效直充、否则入团队池）；调试通道与后置的奖励管线共用。
- 奖励入账：重复获得角色 +20 → 团队池；若重复的是该槽位自己已有的角色 → 直充该槽位。**接线在 `RunController.AddToCharacterPool`**（角色定义唯一：重复定义不入第二实例，返回 false 表示已转化；`RunMod.AddToCharacterPool` 保持裸加入语义）。

### 4.3 联机设置（全局，房主同样受约束）

- `multiplayer.potential.consume_mode`：free（缺省）/ vote。
- vote 模式：消费**团队池**部分需发起提议并经团队表决（3 人同意，含提议者自己）；纯直充消费不需表决。表决网络交互走 `IPotentialProposalApprover`（单机实现直接放行；联机协议后置接入）。
- `multiplayer.potential.proposals_per_ring`：每环每槽位提议次数（默认 2）+ `multiplayer.potential.proposals_unlimited`（无限制勾选）。提议计数为运行态，换环（`NextRing`）重置。
- Run 存档 schema **v1 → v2**：新增池与账本字段，`RunDto.Normalize()` 迁移老档补默认值。

## 5. 新触发点

| 节点 | 触发时机 | 接线位置 |
|---|---|---|
| onWaveStart | 每个波次（阶层）开始；第一波在 RunBattleStart 补发 | `AdvanceToNextWave` / `RunBattleStart` |
| onTurnStart(+turnInterval) | 回合开始，波内每 N 回合 | `FireTurnStartHooks` 三处伴随调用 |
| onActiveSkillCast | 持有者释放主动技载荷执行后 | `ApplyCastActiveSkill` |
| onSlotCardPlayed | 该槽打出卡牌（结算前） | `SettleQueuedCard` 前置钩子 |
| 回合结束 tick | 全容器时长递减与到期 | `ExecuteEnemyPhase` DispatchTurnEnd 后 |

波内回合计数 `TurnsIntoWave`：换波清零、每回合递增；`TurnNumber` 保持全场累计。

## 6. 首个使用者：chalux

`characters/chalux.json`：蓝 / Warrior / Animal + Dragon（2026-09-21 种族收敛后为动物·龙族双种族），能量 8/3（角色定义级能量保底加算在卡组贡献之上），主动技蓄力链单档 [超限极寒 ×8]，卡组为四张专属卡（占位 strike / strike_plus 已于 2026-09-21 删除）。六条潜能被动（0/10/30/50/70/99）全部为 buff：

| 被动 | 实现 |
|---|---|
| P1 寒躯 | trait.immune_slot_damage tag |
| P2 寒火同源 | trait.chain_inject_red tag |
| P3 凛冬节拍 | onWaveStart + onTurnStart(turnInterval:8)：BoostSkillCounter 自身+3 / 蓝+1 / 动物+1 / 龙族+1（走 GainResource resource:skillcounter，叠加计算，超阈值丢弃由 GainSkillCounter 的 Cap 语义保证） |
| P4 极地血脉 | AllAllies + 条件(蓝或动物或龙族)：物攻/魔攻/治疗 +6 |
| P5 永冻威压 | AllAllies + 条件(蓝或动物或龙族)：DamageDealtScale +0.25 |
| P6 零度领域 | onActiveSkillCast：敌方全体 PDef/MDef Override 0 持续 1 回合 + 自身 +3 可用能量 |

主动技超限极寒：自身物攻 +6（3 回合）+ 3 号槽（索引 2）附加充能 I（3 回合，载荷 12+100%物攻蓝伤随机 1 敌）。

> **展示命名约定（2026-09-19）**：被动没有独立技能名，UI（角色详情被动列表）一律按序号显示「被动技能1~N」（`UI_CHARACTER_PASSIVE_NAME` 格式键），描述仍取各 buff 的 `descId`；被动 buff 的 JSON 不再声明 `displayNameId`（主动技增益类 buff 不受影响）。本节表格中的 P1 寒躯 / P2 寒火同源等仅是设计期代号。

> **专属卡（2026-09-19）**：chalux 另有四张蓝属性 Epic 专属卡（逆戟冰冲 / 璨华长路 / 才煌的绝剑 / 绝念），已加入其初始卡组；其中「才煌的绝剑」授予充能球、「绝念」投放槽位充能 II。详见 [充能球系统规格 §7](../../../superpowers/specs/2026-09-19-charge-orb-system-design.md)。

## 7. 明确后置项

- 联机表决网络同步（`IPotentialProposalApprover` 联机实现）
- 正式战斗界面（槽位 buff / 连携的玩家侧 UI；当前可视化在 RunDebugDlg"战斗检查"）
- chalux 正式卡组（2026-09-21 已落地四张专属卡；占位 strike/strike_plus 已删除）
- **潜能消费玩家 UI**：解锁/返还目前只有 RunDebugDlg 调试面板可达，正式的潜能消费界面未实装
- **重复角色正式奖励管线**：+20 转化已接 `RunController.AddToCharacterPool`，但角色获取（战斗奖励/商店/事件）发放重复角色时的调用方接线未实装
- **魔攻 / 魔防数值通道**：`MagicAttack` / `MagicDefense` 属性已定义但没有任何战斗公式消费（魔法伤害公式后置；治疗已改为吃 `HealPower`，见 §3）
