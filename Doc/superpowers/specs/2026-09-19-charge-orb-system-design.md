# 充能球（元素球）系统与 chalux 专属卡 设计

**日期**：2026-09-19
**状态**：已实装
**关系**：服从 [2026-05-11 总规格](2026-05-11-kemo-card-design.md) 与 [2026-07-21 战斗规格](2026-07-21-combat-system-design.md)；与 [buff 运行时规格](2026-09-19-buff-potential-chain-system-design.md) 同批落地（槽位充能、连携、潜能）。

---

## 1. 球类型与内容注册

- 新内容类别 `orbs`（`content/orbs/*.json`，DTO `OrbTypeDto`）：Mod 与 base-game 走同一条内容管道（校验失败即剔除、归属 mod、翻译扫描自动覆盖）。
- 内建 6 种（base-game 声明）：红 / 蓝 / 绿 / 黄（元素球）+ 物理球 + 魔法球。
- 字段：
  - `dealsDamage`：是否造成伤害；`false` = 纯效果球（只跑 `triggerEffects`）。
  - `damageKind` + `element`（2026-09-20 拆维）：元素球 = `Elemental` + 元素（必填）；物理球 = `Physical`；魔法球 = `Magical`（物理/魔法球不带元素）。
  - `perOrbAmount`（内建 6）：每球固定伤害基数。
  - `attackBonusScale`（内建 1 = 100%）：产球者攻击的加成比例。
  - `attackSource`：`Higher`（物攻/魔攻取较高者，元素球）/ `Physical`（物理球）/ `Magic`（魔法球）。
  - `triggerEffects`：每球额外执行一次的效果引用（特殊球用）。
- **特殊球**（Mod 注册）不能由回合结束统计产出，只能经角色 / 卡牌 / 效果（`GainOrb`）授予。
- 校验：悬空 `triggerEffects` 引用、负的伤害基数 / 加成比例、纯效果球却没有触发效果、元素球却没有元素，一律拒绝；`GainOrb` 的 `orbTypeId` 必须存在。

## 2. 队列与触发

- **全队共享**队列（FIFO，容量 7 = `OrbQueue.Capacity`），每个球记录**球类型 + 产球者**（产球时的玩家槽位；`<0` = 无产球者）。
- **主动触发**：出牌阶段球数 ≥ 3（`OrbQueue.ManualTriggerThreshold`）时可触发，**可重复**（只要仍 ≥ 3）；走正式玩家命令 `TriggerOrbsCommand`（不占"已行动"、不消耗能量，因此不推动阶段推进）。
- **被动触发**：球数达到容量上限时**获得即触发**，立即结算后继续原流程（回合结束产出导致满员 → 就在回合结束时结算）。
- **触发结算**：一次清空队列全部球，按入队顺序（FIFO）**逐球**结算 —— 每个球执行一次自身类型的效果，源为**该球的产球者**。
- 敌方无存活目标时**照常清空、不产生伤害**（避免满员后卡死队列）。

## 3. 单球伤害公式

```
单球伤害 = (perOrbAmount + attackBonusScale × 产球者攻击)
          × (1 + 产球者全伤害增加 DamageDealtScale)
          × (1 + 目标受伤倍率 DamageTakenScale)
```

- 产球者攻击按 `attackSource` 取物攻 / 魔攻 / 两者较高者（当前有效值，含 buff 与被动）。
- **不吃目标物防 / 魔防，不吃连携**（充能球是团队触发，不属于任何单卡的连携区间）。
- 伤害走统一的**伤害包管线**（`DamagePipeline`：`OnBeforeDamage` 可改数额、可完全抵消 → 写入 → `OnAfterDamage` 观测最终数额），
  写入方式与其它伤害一致（敌方写目标 ASC、玩家槽位转共享账本、账本目标一次结算）。
- 与 GAS 路径的差异说明：GAS `DamageExecution` 是 `Amount + 物攻 + Damage − 物防`；球伤害走定值通道
  （`CombatEffectExecutor.ApplyFixedDamage`），只做规则与写入。自 2026-09-19 统一后，三条通道的
  受伤倍率口径与规则管线完全一致（见 [战斗规格 §1.3](2026-07-21-combat-system-design.md)「伤害包管线」）。

## 4. 回合结束产出

每个回合结束固定产出 **1 个四属性球 + 1 个物理/魔法球**：

- 统计口径 = **本回合打出的卡牌**（含空放；卡牌结算入口登记，回合结束取走并清空）。
  - 四属性球：按卡牌**自身 `element`** 计红/蓝/绿/黄出现次数，取最多者。chalux 被动2 的"注入红"**不**计入（统计只认卡面属性）。
  - 物理/魔法球：按卡牌 `cardType`（`Physics` / `Magical`）计数，取最多者；其它类型（Support/Healing/Curse…）不计。
  - 平局或本回合未打出任何卡牌：各在自己那一组里**随机**（走 `combat.orb` 独立随机流，同种子可复现）。
- 产出顺序固定：先四属性球，后物理/魔法球。
- **产球者**（回合结束产出的球）：
  - 四属性球 = 全队 `max(物攻, 魔攻)` 最高者；
  - 物理球 = 全队物攻最高者；魔法球 = 全队魔攻最高者；
  - 按当前有效值判定，并列取**槽序最小**（确定性）。

## 5. 内容授予（`GainOrb`）

- 新增效果种类 `EEffectKind.GainOrb` 与技能动作 `ESkillActionKind.GainOrb`，参数 `orbTypeId`（必填）+ `count`（可选，默认 1）。
- **产球者 = 来源角色**（卡片授予时为打出者；非玩家槽位来源视为无产球者，触发时按"全队最高攻击者"解析）。

## 6. UI

- 正式战斗界面尚未实装（后置项），因此球指示器暂挂 **Run 主界面右上角**（`RunMainWin` 的 `OrbPanel`）：标题 + 各球数量（按球类型的翻译名）+ 提示（满 7 自动 / ≥3 可手动）+ 触发按钮（球数不足时禁用）。
- 触发按钮走正式命令管线；失败弹 Toast。
- 调试面板：`GrantOrb`（指定球类型 / 数量 / 产球者槽位）、`TriggerOrbs`，`InspectBattle` 输出 `充能球 n/7（各类型数量；可否主动触发）`。
- 球无美术图标（用文字与主题配色），等美术替换。

## 7. chalux 四张专属卡

「专属」= `isExclusive: true`（DTO 无 `ownerCharacterId`，归属经角色初始卡组体现）；四张均为蓝属性、Epic、无升级链（不设 `cardGroupId`）、`artPath` 留空等美术。

| 卡 | 费用 | 类型 | 效果 | 属性贡献 | 优先级 |
|---|---|---|---|---|---|
| 逆戟冰冲 `chalux_orca_ice_rush` | 3 | Physics | 敌方单体蓝属性 12 点物理伤害 | +20 最大生命 / 物攻 +3 | 4 |
| 璨华长路 `chalux_resplendent_path` | 4 | Physics | 敌方**全体** 12 点物理伤害 + 自身【2 回合 / 物攻 +6】 | +40 最大生命 | 9 |
| 才煌的绝剑 `chalux_brilliant_sword` | 2 | Support | 自身【2 回合 / 物攻 +9】+ 获得 2 个蓝属性球 | +30 最大生命 / 物攻 +2 | 44 |
| 绝念 `chalux_absolute_resolve` | 2 | Support | 2 号手牌槽【5 回合 / 充能 II】，载荷【3 回合 / 物攻 +6】 | +20 最大生命 / 物攻 +3 | 50 |

- 「12 点物理伤害」沿用现有约定：`Amount: 12` 走 `DamageExecution`（实战 = 12 + 100% 施法者物攻 − 目标物防），与 strike / 充能载荷同构。
- Support 类型不参与连携人头统计（连携规格 §3）。
- 卡4 的充能载荷复用 `chalux_active_frost`（超限增幅：物攻 +6 / 3 回合）；「2 号手牌槽」= `slotIndex: 1`（与主动技「3 号槽 = 索引 2」同口径）。
- 增益投放靠 `ApplyBuff` 的 `hookTargets` / `targetFilter` 目标选择器（**新增能力**）：同一个技能里"打敌方全体 + 增益自身"因此无需额外机制。
- 四张卡已加入 `characters/chalux.json` 的 `cards`（与占位 strike / strike_plus 并存）。

### 卡牌目标解析的两条既有约束（内容侧须知）

1. **卡牌技能一律使用卡牌级目标**：`skill.targetOverride` 只对主动技 / 开战注入生效，对卡牌技能无效。
2. `targetSide: Self` 的卡标记入队时必须带上自身目标（`targets: [self]`）；`targetScope: All` 的卡按"全体"语义需要带上全部合法敌人。正式战斗 UI 必须自动填充这两类默认目标，否则会退化成空放。

## 8. 明确后置项

- 正式战斗界面（球的图标化展示与触发交互从 Run 主界面迁入）。
- 球的正式美术（图标 / 特效 / 数字动画）。
- 特殊球的正式内容（当前只有内建 6 种；注册通道已就绪）。
- 球与"元素师"角色定位的联动（`ERole.Elementist` 目前只是角色枚举注释）。
