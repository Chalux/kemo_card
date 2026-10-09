# 2026-10-06 Git 差异审查

审查基准：`HEAD 111b3f7d13511cb78ab082789eb1ef80e260c9cc` 与当前工作区，包含未跟踪的战斗服务、回归测试、爱因斯坦及雷诺内容。`.vscode/settings.json` 的既有环境配置不纳入业务审查。

初审结论：职责拆分、实际损血统一结算、脚本 owner 修正和输入快照的方向合理，但初审差异仍有 **7 类需要修正的问题**；另确认 **2 类 HEAD 已存在的遗留问题**。不能仅以现有全量测试通过作为这次重构完成的依据。下方问题段落保留初审时的证据，最新修复状态见本节。

初审阶段只新增文档、测试结果与复现源码文本；随后按用户授权完成业务修复、实现优化与正式回归测试。

## 修复与改进状态（2026-10-06）

初审 9 类问题均已修复，另修复条件钩子的次数门闩组合缺陷。原来失败的 10 个复现用例已纳入 `CombatDiffReviewRegressionTests.cs`，另补充 `CombatDiffReviewBoundaryTests.cs` 验证组合边界。

| 问题 | 修复后的契约 |
|------|-------------|
| 攻击次数与冻结风险 | 直伤/GAS 共用 999 次上限，浮点夹紧先于整数转换；次数叠加使用安全范围，显式 0 保留；每次命中消费共享预算。 |
| 有限 GE 叠层误拒绝 | 区分首次创建与聚合叠层，AggregateByTarget/Source 的有限 onApply/onStackChanged 自反馈可以正常达到 MaxStacks。 |
| Refresh 移除再创建漏检 | 保留首次创建、叠层和移除边，显式移除再创建的 Buff/GE 同步环在准入时拒绝；状态相关的跨组替换仍由运行时预算兜底。 |
| 吸血归属 | 只累计本 GE 每次结算的 HealthLoss，兼容 Health modifier 返回自己的损血；钩子的独立伤害与治疗不混入。 |
| 嵌套球上下文 | 批次栈恢复外层；内层逐球效果遮蔽外层批次，仅自己的 onOrbTriggered 钩子能读取内层批次。 |
| 直伤回落维度 | 从声明 GE 解析完整 DamageTypeSpec，kind/element 一直传到规则、表现与受击账。 |
| 条件身份 | 上下文保留阵营与主体引用；目标筛选读取候选，敌方不会读取同索引玩家账本，也不会在 self/excludeSelf 中误匹配玩家。 |
| 可消耗护盾 | 聚合器独立记录最终值的消耗；GainShield 增加 base 授予量，重算与 Add/Multiply/Override 不会补回消耗或重复增加修饰值。额度移除不形成后续授予债务。 |
| 互斥 Buff 重入 | 先提交替换结果，再发旧实例 onRemove；重入以最后提交为准，仍在容器中的新实例才发 onApply。回归同时断言钩子结果，避免只检查数量的假通过。 |
| 条件钩子次数门闩（优化阶段补充发现） | 条件和预算通过后才登记 oncePerTurn/oncePerWave，在载荷开始前登记以阻断重入；不含黄球的批次不会耗掉凝聚态的机会。已用尽次数的钩子在随机选目标前退出，多个门闩原子登记。 |

实现改进：

- 原生数值直接解析，JSON 与内存整数统一拒绝小数截断、非有限值和越界。
- 技能覆盖、引用参数与定义默认值在执行边界只合并一次，保持覆盖优先级，不复制临时 DTO。
- 卡组和待播放事件的只读视图缓存复用；预算作用域使用值类型，减少逐效果分配。
- 旧自动目标接口委托统一算法，RandomN 抽样只有一套实现；界面查询继续不消费随机流。
- 卡牌上下文用作用域恢复，覆盖正常退出、嵌套和异常；整个技能载荷与球触发共用预算。
- 修正弃牌记录注释，并将新通用机制及爱因斯坦/雷诺接线补入现有战斗规格。

验证结果和限制：

- 完整回归 **1,325 通过，0 失败，0 跳过**，相较初审新增 39 个正式用例。见 [修复后 TRX](2026-10-06-diff-review-results/diff-review-fixes.trx)。
- 保留初审 [失败复现 TRX](2026-10-06-diff-review-results/diff-review-probes.trx) 用于对照；最新定向回归见 [targeted TRX](2026-10-06-diff-review-results/diff-review-fixes-targeted.trx)。
- 本轮修改文件已定向格式化，`git diff --check` 通过；未提交或推送 Git。
- 未实测 Godot 场景回放、真实 V8 或长期性能；分配改进依据代码路径，没有宣称已测得性能收益。构建的已有警告位于 `addons/dialogue_manager`。

## 验证证据

- 当前正式测试：**1,286 通过，0 失败，0 跳过**。见 [baseline TRX](2026-10-06-diff-review-results/diff-review-baseline.trx)。
- 临时复现：**10 个预期行为断言全部失败**，覆盖下述 7 类差异问题及 2 类遗留问题；攻击次数类别包含两个用例。见 [probes TRX](2026-10-06-diff-review-results/diff-review-probes.trx)。
- 临时测试已移出编译目录，保存为 [DiffReviewProbeTests.cs.txt](2026-10-06-diff-review-results/DiffReviewProbeTests.cs.txt)，便于后续修复时恢复为回归测试。归档后正式测试工程重新构建成功，0 警告、0 错误。
- `git diff --check` 通过。
- 未验证 Godot 场景完整回放、真实 V8 脚本运行与长期性能。本次结论针对代码、内容接线和纯 C# 结算行为。

## 当前差异问题

### 1. [P1] 攻击次数没有统一的安全边界

位置：[CombatEffectExecutor.cs:356](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/CombatEffectExecutor.cs:356)、[DamageExecution.cs:117](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/frame/gas/Executions/DamageExecution.cs:117)、[DamageScaling.cs](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/DamageScaling.cs)。

直伤循环直接使用 `ResolveAttackCount` 的结果，没有 GAS 通道的 999 次上限。准入只要求非负整数，`AttackCount = int.MaxValue` 可以进入约 21 亿次同步循环；执行预算在动作入口计数，不会在每次命中消费，因此无法中断这种循环。这里没有实际运行十亿次循环，冻结风险由循环与校验路径直接确认。

另一方面，GAS 的 `Math.Clamp((int)MathF.Round(raw), ...)` 先转整数再夹紧；超过整数范围的有限 float 会转成负数，再夹为 0。经 SetByCaller 传入 `int.MaxValue`，攻击反而消失。

复现：

- `DiffReview_direct_damage_attack_count_should_have_same_limit_as_gas`：1000 次直伤从 2000 HP 打到 **1000**，若沿用 GAS 的上限应为 **1001**。
- `DiffReview_large_finite_gas_attack_count_should_clamp_before_integer_conversion`：大值请求后仍为 **2000 HP**，应按安全上限结算至 **1001**。

建议：共享同一个次数解析与范围约束；在浮点范围内夹紧后再转整数；内容准入校验同一上限，并让每次命中参与总执行预算。保留显式 0 次的语义，同时检查额外次数叠加的整数溢出。

### 2. [P2] 同步环校验会删除有限的 GE 自叠层

位置：[ContentSynchronousCycleValidator.cs:70](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/frame/content/ContentSynchronousCycleValidator.cs:70)。

`ApplyGameplayEffect` 一律连到 GE 的 `apply` 钩子，忽略 `StackingPolicy`。对于 `AggregateByTarget`、`MaxStacks = 2` 的 GE，`onApply` 再挂自己只会把现有实例加到两层；ASC 不再次发送 `onApply`，执行会自然结束。新校验却把它判为同步环，删除 GE 和关联动作，可能继续导致引用它们的技能、卡牌被剔除。

复现：`DiffReview_finite_ge_self_stack_should_survive_validation`。准入后定义不存在；测试绕过准入恢复定义后，实际执行得到 **一个实例、两层、预算拒绝次数 0**。

建议：按首次创建、已有实例叠层、替换移除分别建边，不能把所有 GE 挂载当成再次触发 `onApply`。对于目标/来源无法静态确定的路径，保留运行时预算兜底。

### 3. [P2] 同一校验漏掉 Refresh 的移除再挂载循环

位置：[ContentSynchronousCycleValidator.cs:26](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/frame/content/ContentSynchronousCycleValidator.cs:26)。

跳过 Refresh/Add 的全部挂载边，只适用于实例一直留在容器里的重复挂载。若 `onApply` 移除自己，而 `onRemove` 又挂自己，实例每次都已被移除，Refresh 会不断创建新实例并再次发送 `onApply`。这是一条静态可确认的同步循环，仍被当前准入接受；最终靠预算截断，留下部分结算结果。

复现：`DiffReview_refresh_apply_remove_loop_should_be_rejected`。定义仍获准入，执行后 **预算拒绝次数为 1**。

建议：保留首次挂载到 `onApply` 的边，再结合移除再创建的状态判断循环；区分“已有实例刷新”与“移除后重新创建”。不要通过跳过一整类边来处理有限反馈。

### 4. [P2] Instant GE 吸血会算入钩子的独立伤害

位置：[GameplayEffectApplicator.cs:59](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/GameplayEffectApplicator.cs:59)、[GameplayEffectApplicator.cs:70](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/GameplayEffectApplicator.cs:70)。

新的 `onApply` 调度发生在 `ApplyGameplayEffect` 返回前，但吸血仍取整个调用前后的目标 Health 差值。因此父 GE 的吸血会算入钩子另一个 GE 造成的伤害；钩子治疗也可能反过来抵消父 GE 应得的吸血。统一管线已经返回 `DamageSettlementResult.HealthLoss`，应用器却没有使用这个结果。

复现：`DiffReview_lifesteal_should_count_only_this_ge_damage`。父 GE 打 10、吸血 100%；子 GE 打 20、无吸血。敌方从 100 到 70 正确，己方从 50 回到 **80**，应为 **60**。

建议：累计本 GE 每次 `Settle` 返回的 `HealthLoss`，不要跨同步钩子用目标总血量差推断伤害归属。兼容 Health modifier 的结算也应返回独立结果，再明确是否计入该 GE 的吸血。

### 5. [P2] 嵌套充能球触发丢失外层条件上下文

位置：[CombatSimulation.cs:351](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/runtime/CombatSimulation.cs:351)、[OrbRuntime.cs:232](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/orbs/OrbRuntime.cs:232)。

批次上下文只有一份可变集合。外层 `onOrbTriggered` 的效果授予 7 个球，可立即触发内层批次；内层结束清空上下文后，外层剩余钩子读不到原批次，`OrbTriggered` 条件错误地失败。代码注释承认此限制，但内容准入和运行时均允许这条有限路径，不应把它作为可复用条件系统的正常语义。

复现：`DiffReview_nested_orb_batch_should_restore_outer_context`。外层触发黄球，首个钩子仅一次授予 7 个绿球；后续钩子在外层黄球条件下应加 1 护盾，实际为 **0**。内层触发次数有限，没有依赖无限递归。

建议：批次使用不可变值上下文和作用域栈；进入内层保存外层，`finally` 恢复外层。该模式也适合卡牌结算上下文，避免一份 Simulation 全局字段承担嵌套执行状态。

### 6. [P2] 魔法 GE 的直伤回落丢失伤害维度

位置：[CombatEffectExecutor.cs:346](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/CombatEffectExecutor.cs:346)、[CombatEffectExecutor.cs:357](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/CombatEffectExecutor.cs:357)。

回落路径新增了 `ResolveDeclaredDamageKind`，并据此计算 `MagicDamageTakenScale`，但调用 `DamagePipeline.Settle` 时没传 `kind`，仍广播默认物理伤害。于是同一次命中按魔法减伤、按物理分发规则与表现，也不会登记上一回合魔法受击。

复现：`DiffReview_magical_fallback_should_keep_its_damage_kind`。魔法 GE 因缺少 application-required tag 未应用，回落直伤按魔法减伤从 10 降到 5，共享 HP 正确减到 95；但下一回合 `TookMagicDamageLastTurn(0)` 为 **false**，应为 **true**。

建议：解析完整 `DamageTypeSpec`，把 kind/element 一直传到结算入口。避免数额缩放、伤害包和条件记账各自重建维度。

### 7. [P2] 魔法受击条件把敌方索引当成玩家身份

位置：[BuiltinCombatConditions.cs:350](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/Condition/BuiltinCombatConditions.cs:350)、[CombatEffectExecutor.cs:100](D:/Godot_v4.7.2-stable_mono_win64/Projects/kemo_card/Src/mod/combat/effects/CombatEffectExecutor.cs:100)。

效果条件上下文只传 `source.Index`；新条件用它查询玩家侧受击账。敌方 0 号和玩家 0 号索引相同，但不是同一个单位。玩家 0 号上回合受到魔法攻击后，敌方 0 号发出的条件效果也会错误地通过。

复现：`DiffReview_enemy_source_should_not_read_player_magic_damage_ledger`。玩家受击后，以敌方 0 号为来源执行该条件效果，错误地发放 **1 护盾**，预期为 **0**。

建议：条件上下文保留主体的 side 与 index，查询玩家账本前明确确认玩家身份。对于目标筛选还应区分来源与条件主体，避免再增加依赖索引猜身份的条件。

## 确认的遗留问题

这两项相关实现已在 HEAD 中存在，不应归咎于本次差异；但它们仍影响“统一管线”和“可复用 Buff”是否可靠。

1. **[P2] 护盾修饰符参与时，吸收伤害反而增加护盾。** `DamagePipeline.AbsorbByShield` 把 current 减吸收量后写回 base，聚合器又加一次修饰符。基础 0、Buff +50，吸收 10 后变成 **90**，应为 **40**。授予护盾也采用 current 写 base，有同类问题。复现：`DiffReview_consuming_modifier_shield_should_decrease_current_shield`。建议明确可消耗余量与属性修正的关系；在支持前拒绝持续 Shield 修饰，或把余量独立为资源账本。
2. **[P2] 互斥组移除钩子能留下两个同组 Buff。** A、B、C 同组，A 的 `onRemove` 挂 B；外层用 C 替换 A 后，B、C 同时存在，数量为 **2**。复现：`DiffReview_removal_hook_should_preserve_exclusive_group`。建议先提交容器的新状态，再分发移除钩子；重入变更以最后一次提交为准，外层不要再盲目追加。发送新实例 `onApply` 前也应确认它仍在容器中。

## 更好的实现方式与代码质量

1. **先统一契约，再继续拆类。** 当前阶段机拆出回合、目标、载荷服务是有效改进。下一步应共享攻击次数策略、完整伤害维度和实际损血结果；为钩子使用有身份的执行上下文。新增服务数量本身不是复用能力的指标。
2. **参数只在边界解析，执行时消费类型值。** `ContentParameters.TryFloat` 对原生数字转字符串再解析；`TryInt` 对原生浮点截断，却对 JSON 小数直接拒绝，语义不一致。建议统一整数规则，原生数字直接处理，并把攻击次数、目标选择、领域时长等解析成明确的参数类型。
3. **减少重复分配。** `SkillPayloadExecutor` 先 `Merge` 创建字典与只读包装，再复制成 Dictionary，执行器又合并一次。应在正确覆盖优先级下只合并一次。`CharacterInstance.Decks`、`DeckPreset.CardIds`、`Presentation.Pending` 可像 `CharacterBattleInstance` 一样缓存只读视图。这里是实现简化建议，没有用未做的性能测试声称瓶颈。
4. **目标算法保留一个权威入口。** UI 已改为提交空目标让模拟器展开，但公开 `TryResolveAutoTargets` 还保留另一套随机抽样，且可推进 RNG。建议拆出不消费 RNG 的查询接口，将抽样统一交给命令结算入口；废弃或委托旧入口。
5. **修正文档与测试表达。** `ESkillActionKind.ModifyDrawCountByDiscard` 注释仍说读取后清零，实现和其它注释却明确不清账。同步环、批次嵌套等测试应验证终态和资源守恒，不能只验证定义形状或某一次调用不抛异常。爱因斯坦/雷诺新增机制也应并入现有权威规格，避免只在 XML 注释和内容测试里定义玩法。

建议修复顺序：攻击次数边界 → GE 自身损血与维度 → 条件身份和作用域 → 同步环准入 → 遗留资源与互斥约束。完成这些后再处理参数与分配简化。无需推翻当前职责拆分。
