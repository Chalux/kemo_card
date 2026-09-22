# 莱因哈特套件与战斗机制扩展

**日期**：2026-09-21
**状态**：已实装
**关系**：服从 [总规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md) 与 [战斗规格](../../../superpowers/specs/2026-07-21-combat-system-design.md)；本文扩充 [普通攻击规格](../../../superpowers/specs/2026-09-20-normal-attack-design.md)（次数 / 追打 / 专项倍率）与 [Buff/潜能/连携规格](../../../superpowers/specs/2026-09-19-buff-potential-chain-system-design.md)（统计口径、条件域、取值方式），并落地战斗规格 §7 遗留的"魔法伤害"后置项。

---

## 1. 本次交付总览

| 主题 | 结论 |
|---|---|
| 种族收敛 | 5 族并 1（动物）、2 族并 1（恶魔族）、新增 5 族、`UnKnown` → `Unknown` |
| 常驻天赋 | **移除** `CharacterDto.buffRefs`，角色唯一常驻增益通道 = `passives` |
| 连携统计 | 统计侧统计**所有**卡；加成侧仍只作用于物理/魔法/治疗卡 |
| 魔法伤害 | `damageType: "Magical"` → `魔攻 − 魔防`（战斗规格后置项清账） |
| 普攻扩展 | 普攻次数、追打、普攻专属增伤/受伤倍率 |
| 伤害执行 | 新增 `attackScale`（源攻击力系数） |
| 条件域 | Combat 域启用，新增 `CardPlayedThisTurn`；效果 `conditions` 正式求值 |
| 取值方式 | 新增 `PartyCountScaled`（按队伍匹配人数缩放） |
| 槽位机制 | 新增"随机手牌槽费用归零"；封印免疫特征 |
| 新角色 | 莱因哈特（黄 / SwordMan / 动物）+ 4 张专属卡 + 6 条被动 |

---

## 2. 种族收敛（2026-09-21）

`ERace` 位标志重排（安全：种族只存在于内容 JSON 与运行期 DTO，不落存档）：

| 旧 | 新 |
|---|---|
| Canine / Feline / Bird / Beast / Reptile | **Animal**（动物） |
| Demonic / Devil | **Demon**（恶魔族） |
| — | **Academic / Fantasy / Astronomy / Hero / Calamity** |
| UnKnown | **Unknown**（更名） |
| 未提及 | 保留：Human / Insect / Fish / Plant / Machine / Angel / Dragon / God / Undead |

- 本地化键随枚举走：`UI_RACE_ANIMAL` / `UI_RACE_DEMON` / `UI_RACE_ACADEMIC` …（图鉴筛选下拉与队伍编辑界面共用）。
- `RunTeamEditDlg` 的元素/种族显示从"枚举 ToString()"改为本地化键（多标志用「、」连接）——否则双种族角色会显示 `Animal, Dragon`。

---

## 3. 常驻天赋移除

`CharacterDto.buffRefs` 字段、校验器接线与文档全部删除：该机制与 `passives` 重复却走另一条挂载路径。角色常驻增益一律用 `passives`（潜能门闩 → 开战挂 buff）。

> 敌人侧 `EnemyDto.buffRefs` **保留**：木桩的"每回合回血"等开战 buff 靠它，与角色被动是两件事。

---

## 4. 连携统计口径修正

- **统计侧**：`ChainCalculator.CountDistinctCharacters` 统计出牌队列里的**所有**卡——Support / Curse 等非输出卡同样把打出它们的角色计入人头。理由：否则队友一张增益卡"白出"，档位无法反映这一回合有多少人参与了该属性。
- **加成侧**：`AppliesToCard`（Physics / Magical / Healing）不变，只影响 `BonusForCard`。
- 档位不变：2 人 +25% / 3 人 +50% / 4 人 +100%。

---

## 5. 魔法伤害落地

战斗规格 §7 的后置项（`ExecutionDefDto.damageType` 未被读取）在本轮清账：

- `Src/frame/content/definitions/DamageTypeSpec.cs`（新增）：把 `damageType` + `element` 解析成伤害包的两维 `(EDamageKind, EElement)`。
  - `damageType`：`Physical`（缺省）/ `Magical` / `Elemental`，大小写不敏感；**兼容旧写法**——直接写属性名（如 `"Blue"`）等价于"物理 + 该属性"。
  - `element`：`None` / `Red` / `Blue` / `Green` / `Yellow`，多属性用 `,` 或 `|` 分隔。
- `DamageExecution`：`Physical` → `物攻 − 物防`；`Magical` → `魔攻 − 魔防`；`Elemental` → 不吃攻防（只 `Amount + 源 Damage`）。
- `GameplayEffectApplicator` 把维度随 `SharedHpSettlement` 传进伤害包管线——规则侧（分槽护盾、抗性）不再一律看到"物理 + 无属性"。
- 内容准入：无法解析的 `damageType` / `element` 直接判非法，不会静默退化成物理伤害。
- 出货内容迁移：`blue_damage` / `chalux_charge_damage` 改为显式 `{"damageType":"Physical","element":"Blue"}`（行为不变）。

### 5.1 `attackScale`（源攻击力系数）

`ExecutionDefDto.attackScale`（缺省 `1.0` = 100% 攻击力）只缩放攻击力项，不影响 `Amount` 与源 `Damage` 属性。
「辉耀宝刀」的 `3 + 25% 物攻` 即 `Amount: 3` + `attackScale: 0.25`。

---

## 6. 普通攻击扩展

在 [普通攻击规格](../../../superpowers/specs/2026-09-20-normal-attack-design.md) 之上新增三项，全部**只作用于普通攻击**：

### 6.1 普攻次数（`NormalAttackCount`）

- 本回合普攻执行次数 = `1 + max(0, NormalAttackCount)`；该属性默认 0，buff 用 `Add` 叠加（不同 buff 的加成天然可加）。
- 每次执行都是完整的"归属者 + 追打者"，目标重新按存活敌人取（上一轮打死的不会重复吃伤害）。

### 6.2 追打（`trait.follow_up`）

- 持有者**不是**本回合普攻归属角色时，仍以 `params.percent`%（缺省 100）的攻击力参与该次普攻。
- 数值口径：`max(0, 攻击力 × percent × NormalAttackScale − 目标对应防御)`；带攻击者元素、走同一伤害包管线、不吃连携。
- **多个追打只取最高值**（同名 buff 在容器里是同一实例，因此"多个"指不同 buff 定义——出货内容用 `follow_up_1/_2/_4` 三个 id 表达不同时长）。
- 归属者自己持追打**不重复出手**。
- 每个角色每次普攻都重新判定，因此普攻次数 +1 时追打者每轮都会补打。

### 6.3 普攻专属倍率

| 属性 | 作用 | 位置 |
|---|---|---|
| `NormalAttackDamageDealtScale` | 攻击者：普攻伤害 `× (1 + 本属性)`，与 `DamageDealtScale` 同桶加算 | 攻击侧 |
| `NormalAttackDamageTakenScale` | 目标：被普攻时 `× (1 + 本属性)`，与 `DamageTakenScale` 同桶加算 | 受击侧 |

卡牌伤害两者都不吃——这正是"受到的普通攻击伤害 +25%"与"自身普攻伤害 +50%"必须与全伤害增加区分的原因。

### 6.4 可观测

`NormalAttackResult` 扩展为：`Strikes`（逐次明细，含是否归属打击与百分比）、`Executions`（归属轮数）、`ParticipantCount`、`FollowUpDamage`；`TotalDamage` 升级为**本回合普攻总输出**（含追打与多轮）。原字段 `SlotIndex` / `Kind` / `Element` / `TargetCount` 保持"归属者第一次打击"口径。

---

## 7. Combat 条件域启用

`ICombatCondContext` 从空占位变为可用上下文（`TurnNumber` / `TurnsIntoWave` / `SourceCharacterIndex` / `CountCardsPlayedThisTurn`），由 `CombatCondContext` 在求值时构造。

- 新增 CondType **`CardPlayedThisTurn`**：参数 `{ count, elementAny? }`，判定"本回合该角色打出过 N 张命中指定属性的卡"（含空放，读 `CombatSimulation.PlayedThisTurn`）。
- `EffectDto.conditions` 正式求值（AND，未知类型 / 参数非法 = 不通过）；内容准入阶段用同一套 parser 提前报错。
- 接口刻意只暴露基础类型（属性位标志用 `int`），避免 `Frame.Condition` 反向依赖 `Frame.Content`。

---

## 8. 新增取值与槽位机制

### 8.1 `PartyCountScaled`（`EMagnitudeKind`）

`perCount × 队伍中命中筛选项的角色数`，筛选项为 `countElementAny`（元素）与 `countRaceAny`（种族），两者都配时取"且"。

- 人数统计**含自己**、**不封顶**、只算**已上阵**角色（队伍名单战斗内固定 → 最多 4 人）。
- 实现：`BuffContainer` 接受一个自定义取值委托（`CharacterBattleInstance.ResolveCustomMagnitude`），队伍查询由 `PlayerTeamState` 在组队时注入；buff 修正随回合开始的重估自动刷新。
- 「每有 1 名黄属性·动物角色，自身最大生命 +40」即 `perCount: 40` + 双筛选（4 人 = +160）。

### 8.2 手牌槽费用归零

- 新 tag **`slot.free_cost`**：槽位上的该 buff 使**该槽当前那张牌**的费用视为 0。
- `CardCostCalculator.Compute` 增加 `runtimeInstanceId` 参数（标记入队后牌仍在槽内，按实例反查槽位），队列对账 `QueuedCostReconciler` 因此自动同口径退补。
- `AttachSlotBuff`（技能动作与同名效果）新增 `params.slotSelection: "randomNonEmpty"`：在当前有牌的槽里随机一个；`slotIndex` 与 `slotSelection` 二选一。
- `EEffectKind` 新增同名 `AttachSlotBuff`，让 **buff 钩子**也能挂槽位 buff（此前只有技能动作能做），以便"释放主动技时随机一张手牌免费"这类被动。
- 时长用 `durationType: Turns` + `duration: 1` → 回合结束自动到期。

### 8.3 封印免疫

- 新特征 **`trait.immune_seal`**（与 `trait.immune_slot_damage` 同模式）。
- 实现：GE 挂上后 `GameplayEffectApplicator` 立刻摘掉授予 `combat.state.sealed` 的 Gameplay Effect——只抵消封印本身，同一 GE 的其它修饰/标签照常生效。

---

## 9. 卡组属性预算

一张卡的 `stats.attributes` 折算总值 = **40 最大生命**，其中 **1 点物理攻击 = 10 点最大生命**。

- 对齐方式固定为**保留最大生命、削减物攻**，不要反过来削生命去换物攻。
- chalux：逆戟冰冲 `20生命/2物攻`、璨华长路 `40生命`、才煌的绝剑 `30生命/1物攻`、绝念 `20生命/2物攻`。
- 莱因哈特：黑船宝藏 `20/2`、呼啸激攻 `30/1`、碧蓝大海航行 `40/0`、辉耀宝刀 `30/1`。

---

## 10. 莱因哈特（`reinhardt`）

黄 / SwordMan / 动物；能量 8/3；主动技单档 CD 10。

| 卡 | 费 | 类型 | 效果 | 属性贡献 | 优先级 |
|---|---|---|---|---|---|
| 黑船宝藏 `reinhardt_black_ship_treasure` | 2 | Support | 自身 2 回合【普攻次数 +1】 | 20生命 / 2物攻 | 50 |
| 呼啸激攻 `reinhardt_howling_onslaught` | 3 | Weak | 敌方单体 2 回合【受到普攻伤害 +25%】 | 30生命 / 1物攻 | 30 |
| 碧蓝大海航行 `reinhardt_azure_voyage` | 4 | Support | 自身 4 回合【追打 100%】 | 40生命 | 50 |
| 辉耀宝刀 `reinhardt_radiant_blade` | 2 | Physics | 敌方单体 `3 + 25% 物攻` 物理伤害；自身 1 回合【追打 100%】 | 30生命 / 1物攻 | 4 |

主动技「黄潮号令」`reinhardt_yellow_tide_command`：黄属性·动物的友方角色 2 回合物攻 +15（`targetFilter` 双筛选取"且"）；自身 2 回合 100% 追打。

被动（0/10/30/50/70/99）：

| 被动 | 实现 |
|---|---|
| P1 免疫封印 | `trait.immune_seal` |
| P2 二连黄潮 | `onCardSettled` + 条件 `CardPlayedThisTurn{count:2, elementAny:["Yellow"]}` → 自身 1 回合物攻 +15 |
| P3 潮汐节拍 | `onWaveStart` + `onTurnStart(turnInterval:10)`：自身技能进度 +2 / 黄 +2 / 动物 +2 |
| P4 兽群庇佑 | `PartyCountScaled`：命中黄·动物的角色数 × 40 加到自身最大生命 |
| P5 群猎本能 | `NormalAttackDamageDealtScale +0.5` |
| P6 一掷千金 | `onActiveSkillCast` → `AttachSlotBuff{slotSelection: randomNonEmpty}` 挂 `slot.free_cost`（1 回合） |

---

## 11. 巴赫（`bach`，角色3）

绿 / Elementist / 人类·神族；能量 8/3；主动技**两档**蓄力链。

| 档 | 技能 | CD | 效果 |
|---|---|---|---|
| 1 | 天国神启 `bach_heavenly_revelation` | 6 | 获得红·黄·蓝·绿属性球各 1 个 |
| 2 | 天国神启·充能II `bach_charge_two` | 4 | 自身 2 回合【元素球伤害 +25%】；获得红·黄·蓝·绿·物理·魔法球各 1 个 |

被动（0/10/30/50/70/99）：

| 被动 | 实现 |
|---|---|
| P1 免疫中毒 | `trait.immune_poison`（GE 挂 `debuff.poison` 后立刻摘掉标签） |
| P2 触发回响 | `onOrbTriggered` + `oncePerTurn: true` → `GainOrb{green, 2}` |
| P3 启蒙节拍 | `onWaveStart` + `onTurnStart(turnInterval:6)`：自身技能进度 +2 / 绿 +1 / 人类 +1 / 神族 +1 |
| P4 绿意共鸣 | `condition.partyMinCount 2 + partyElementAny [Green]` → `GreenOrbDamageScale +0.5`（全队） |
| P5 满载启示 | `onCardExecutionEnd` → `GainOrbPerPlayedCard{green, perCard:1, offset:-1}` |
| P6 神启之威 | `onActiveSkillCast` → 自身 1 回合物攻/魔攻 +30 |

> 卡组为四张专属卡（占位卡 `strike` / `strike_plus` 已随 2026-09-21 收尾删除）。

### 11.0 巴赫的专属卡（已出货 2 张）

| 卡 | 费 | 类型 | 效果 | 属性贡献 | 优先级 |
|---|---|---|---|---|---|
| 康塔塔 `bach_cantata` | 3 | Support | 自身卡组内绿属性卡牌达到 0/4/7 张时，获得 2/3/4 个绿属性球 | 30生命 / 1魔攻 | 10 |
| 赞歌 `bach_hymn` | 2 | Support | 赋予自身【2 回合 / 魔攻 +6】 | 20生命 / 2魔攻 | 50 |
| 三重奏 `bach_trio` | 3 | Magical | 敌方单体 6 点魔法伤害；直到此卡打出前，本回合每触发 1 个绿属性球额外 +100% 魔攻（最多 +300%） | 30生命 / 1魔攻 | 1 |
| 赋格 `bach_fugue` | 5 | Support | 自身 1·2·3·4·5 号手牌槽获得【2 回合 / 充能I】（该槽每打出一张牌获得 1 个绿属性球）| 40生命 | 10 |

- 康塔塔用新效果 **`GainOrbByDeckCount`**：`params.tiers: [[0,2],[4,3],[7,4]]` + `elementMask: 4`（只数绿卡）。
  "卡组内"口径 = 该角色本场战斗持有的全部卡（抽牌堆 + 手牌 + 弃牌堆，`CharacterBattleInstance.OwnedCardIds`）。
  ⚠️ 需要走**效果通道**（`SkillDto.effectRefs`）而不是技能动作——`GainOrbByDeckCount` 是 `EEffectKind`。
- 三重奏用动态攻击系数：技能动作参数 **`attackScaleFromOrbs = { elementMask, perOrb, maxBonus }`** →
  `attackScale = 1 + min(maxBonus, perOrb × 本回合已触发的命中球数)`，算好后经
  SetByCaller **`AttackScale`** 覆盖 GE 的静态 `attackScale`（`DamageExecution` 优先读它）。
  回合内球数由 `OrbRuntime.Trigger` → `CombatSimulation.RecordOrbsTriggered` 累计，
  **账期与回合边界对齐**（`CombatStateMachine` 在回合开始时 `ResetOrbsTriggeredThisTurn`）。
  注意不能在"回合结束产球"时清账：产球会即时触发并再次记账，那样上一回合结束时产出的球会被算进下一回合。
- 赋格用 `slotSelection: "all"`（本轮新增）：给**全部 5 个手牌槽**各挂一份 `slot.charge` buff（空槽也挂），
  载荷复用既有充能机制（`onSlotCardPlayed` → `GainOrb{green,1}`），充能 I = 计数 1、2 回合后到期。
- 充能互斥（本轮决议，见 [buff 运行时规格 §2](../../../superpowers/specs/2026-09-19-buff-potential-chain-system-design.md)）：
  同一槽位只允许 1 个充能，新充能**无条件覆盖**旧的并**重置进度**（同 id、`Refresh`/`Add` 也一样）。
- 四张专属卡已全部落盘。

### 11.1 本轮新增的通用机制

| 机制 | 说明 |
|---|---|
| `OrbDamageScale` + `elementMask` | 球伤害增加的**两参数**写法：掩码 0 = 所有球（含物理/魔法球），否则为 `EElement` 位掩码（15 = 四色属性球，4 = 仅绿球）。带掩码的修正按元素拆成 `OrbDamageScale:<Element>` 分别记账，球结算时只吃自己那一份 |
| `condition.partyMinCount` + `partyElementAny` / `partyRaceAny` | 队伍人数门闩：命中筛选的上阵角色数 ≥ 阈值时满足；与持有者维度取"且"，只配人数时完全由人数决定 |
| `onOrbTriggered` | 充能球触发结算后，对参与产球的角色（去重）各触发一次 |
| `oncePerTurn`（钩子参数） | 同一 buff 实例的同一效果每回合只触发一次，回合开始清账 |
| `onCardExecutionEnd` | 本回合全部卡牌结算结束后触发一次（普攻之前） |
| `GainOrbPerPlayedCard` | 按本回合出牌数发球：`max(0, 出牌数 × perCard + offset)` |
| `GainOrbByDeckCount` | 按卡组内命中筛选项的卡牌数分档发球：`tiers: [[最小张数, 球数], …]` 取最高档 |
| `attackScaleFromOrbs` + SetByCaller `AttackScale` | 动态攻击系数：按本回合已触发的命中球数提升源攻击力系数，算好后覆盖 GE 的静态 `attackScale`（效果通道与技能动作通道共用同一解析） |
| `AttachSlotBuff` 的 `slotSelection` | `randomNonEmpty`（随机一张有牌的手牌）/ `all`（全部手牌槽，空槽也挂）；与显式 `slotIndex` 三选一 |
| `trait.immune_poison` | 中毒免疫（约定标签 `debuff.poison`） |

### 11.2 伤害缩放口径（全局，2026-09-21）

**所有增伤与所有受到伤害增加一律加算，只有连携乘算**：

```
最终伤害 = base × (1 + Σ增伤 + Σ受到伤害增加) × (1 + 连携)
```

权威表述已写入 [战斗规格](../../../superpowers/specs/2026-07-21-combat-system-design.md)「增伤与受伤增加一律加算」条；
mod 侧统一入口是 `Src\mod\combat\effects\DamageScaling.cs`（直伤 / 普攻 / 充能球共用），GAS 通道在 `DamageExecution` 内联同一公式。

### 11.3 被动不需要名字（2026-09-21）

撤销 2026-09-19 的「被动技能1~N」序号命名约定：角色详情界面的被动列表只显示**潜能门槛 + 描述**
（不再拼 `UI_CHARACTER_PASSIVE_NAME`）；被动载荷 buff 也不再声明 `displayNameId`。

---

## 12. 同批全局收敛

### 12.1 稀有度档名

`ERarity` 收敛为 `Common` / `Special`（原 Uncommon）/ `Rare` / `Exclusive`（原 Epic）/ `Legendary`。
卡框资源路径同步为 `Resource/Assets/CardFrame/{Common,Special,Rare,Exclusive,Legendary}.png`；
出货内容里 8 张专属卡已从 `Epic` 迁到 `Exclusive`。

### 12.2 角色简介移除

`CharacterDto.descId` 删除（字段、内容、翻译行、图鉴文本检索、DTO 文档一并清理）。
**角色详细界面改为展示专属卡牌**：`CharacterDetailsDlg` 原简介区改列该角色 `cards` 中 `isExclusive: true` 的卡，
标题键 `UI_CHARACTER_CARDS_TITLE`（无专属卡时连标题一起收起）。

展示形态是**横向虚拟列表**（`VirtualList`，`IsVertical = false`、`ItemSize = 160`、`Spacing = 10`，
条目模板 `BaseCardItem.tscn`），条目尺寸由卡面预制体决定、列表不拉伸它。
单击卡面即打开既有的卡牌详情（`ECardClickAction.OpenDetails`），**不再把卡名与描述拼成文本**——
文字与卡面重复表达同一件事，且描述原文在卡牌详情里能读得更全。

---

## 13. 明确后置项

- **普攻次数上限**：`reinhardt_extra_normal_attack` 目前 `maxStacks: 1` + `Refresh`，即"同一张卡的 buff 不自我叠层，不同 buff 之间靠属性相加"。若要让同一张卡反复打出也能叠，需要抬 `maxStacks` 并改叠层规则。
- **追打的独立可观测 UI**：伤害飘字与回合摘要仍属后置项。
- **元素克制 / 抗性**：`Element` 维度已贯通到 GAS 通道，但仍无消费方。
- **敌方 buffRefs**：仍与角色被动分属两条路径，未统一。
