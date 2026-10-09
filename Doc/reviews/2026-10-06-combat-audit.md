# 战斗、角色与卡牌实现审查

日期：2026-10-06。范围：战斗阶段机与指令、角色/卡组/手牌实例、伤害与共享生命、Buff/GAS 生命周期、连携、普攻与充能球、内容准入、Run 开战接线和战斗 UI 的相关边界。

修复前结论：**架构方向合理，有复用基础，但还不能评价为语义一致、边界可靠的实现。** 主要问题集中在回合收尾、重入、目标校验和多条效果路径之间的行为差异。建议保留现有模块划分，优先修正下面的缺陷，再做局部收敛。

原始审查阶段未修改业务代码或玩法规格；后续修复与改进见下节。原有 `.vscode/settings.json` 修改未触碰。

## 修复与改进状态（2026-10-06）

**以下 14 类问题已修复；原始发现正文保留作为历史依据。** 正式测试由 `CombatAuditRegressionTests`（含组合集成文件）与 `RunBattleWiringTests` 承载，原 `.cs.txt` 探针及失败 TRX 不参与当前构建。

| 原问题 | 修复落点与契约 |
| --- | --- |
| 1 | Run → 工厂 → CharacterBattleInstance 传入获得卡账本；同时验证未拥有卡仍被拒绝 |
| 2 / 9 / 14 | GAS、直伤与定值通道共用 DamagePipeline.Settle；先规则/护盾，再写生命与实际损血事件；完全吸收仍记命中 |
| 3 | 同步触发引用图扩展到 Buff/GE 钩子；动态脚本和绕过准入的内容共享执行预算 |
| 4 / 8 | 整批快照与成员检查；先移除容器/句柄再发移除钩子；已移除 Buff 不重新注册 |
| 5 | 回合收尾清队列与手牌标记，原账期退款；重复取消不重复退款 |
| 6 | 全持有者 GE 生命周期、周期执行、apply/stack/remove 钩子接线；保留已死亡来源身份 |
| 7 | 共用目标校验与自动展开；唯一性、All 集合与 Single 数量约束；UI 抽样移到模拟器 |
| 10 | ASC 基础值写入只通知最终聚合结果；共享生命按有效 MaxHealth 裁剪 |
| 11 / 12 | 统一首回合/后续回合 OnTurnStart 编排，钩子后终局检查先于资源与输入 |
| 13 | Action、兼容 Effect 和 Enemy AI 根据各自定义类别/ID 取脚本 owner；同 ID 跨类别不会混淆 |

随后完成的局部改进：

- 抽出命令验证、目标重选、技能载荷、回合边界及 GE 生命周期职责，缩小阶段机；保留逻辑/表现分离、组合与独立 RNG 流。
- 统一参数覆盖和数值解析，使用 invariant culture、非有限值与溢出检查；没有引入通用 DSL。
- 收紧 HandSlot、CardExecutionQueue、TransitionTo 写入口；命令、队列、表现和卡牌状态视图使用只读集合。卡组锁覆盖旧引用与快照替换。
- 清理连携同桶、旧 trait 注释；ModifyStat 与不支持的 Buff magnitude 明确准入拒绝，基础内容历史空标记改为显式空链。
- 补齐重入预算恢复、低血量×护盾×GAS、跨持有者新增效果、领域钩子替换、持续来源死亡、付费/卡牌身份守恒、只读快照、跨 Mod 类别归属、Run 拥有权和终局输入等组合测试。

最终回归：**1,199 通过，0 失败，0 跳过**（原 1,157 项 + 42 项正式回归）；原始 15 个探针全部转为通过的正式测试。结果：[combat-fixes.trx](2026-10-06-combat-audit-results/combat-fixes.trx)。既有测试的 All 部分集合场景改为 RandomN，领域派发器断言改为行为与全持有者接线，手牌视图断言使用 IReadOnlyList 的 Count；两张旧治疗卡测试改为传入真实 Team 账本目标。

已同步战斗规格与 Agent 入口地图。尚未进行 Godot 界面完整交互回放、真实 V8 跨 Mod 联调或性能基准；构建仍有既有 dialogue_manager 插件的可空性警告。

## 验证结果与限制

- 原有测试集：**1,157 通过，0 失败，0 跳过**，在移出临时探针后重新验证。
- 新增临时审查探针：**15 个断言全部失败**，验证下面的 **14 类缺陷**。卡牌与主动技目标集合各有一个探针，合并为一类发现。
- 探针中的断言表达期望契约，因此失败是缺陷复现证据，不是声称原有测试集失败。
- 递归环只运行了准入验证，确认循环配置被接纳；进程级 StackOverflow 后果来自调用链分析，没有主动执行无限递归。
- 跨 Mod 脚本用记录宿主验证收到的归属参数，没有启动真实 V8 跨 Mod 场景。
- 本次没有执行 Godot 界面的完整交互回放，也没有性能基准。UI 外观、动画时序和长时间压力场景不在运行验证结论内。

证据保存在 [审查探针](2026-10-06-combat-audit-results/CombatAuditProbeTests.cs.txt)、[探针结果](2026-10-06-combat-audit-results/combat-audit.trx)、[基线结果](2026-10-06-combat-audit-results/baseline.trx)。源码以 `.cs.txt` 保存，不参与正常构建。

P1 表示优先修复：阻止主要流程、改变致死结果或存在进程/结算崩溃路径。P2 表示条件组合下的明确错误或公开扩展契约失效。潜在崩溃的触发配置和已出货内容是否使用该组合分别说明。

## 修复前确认的问题（历史依据）

### 1. [P1] 已获得的通用卡会在创建战斗实例时被拒绝

位置：[CharacterBattleInstance.cs:191](../../Src/mod/combat/CharacterBattleInstance.cs#L191)，调用接线：[CombatSimulationFactory.cs:75](../../Src/mod/combat/runtime/CombatSimulationFactory.cs#L75)。

开战校验调用 `GetBuildableCardIds(new HashSet<string>())`，获得卡集合恒为空。角色卡组编辑时可以合法加入 Run 已获得的通用卡，但开战只认可角色定义中的专属卡。工厂和 `RunController.StartBattle` 也没有把 Run 的获得卡账本传入。

复现：角色专属卡为 `exclusive`，已获得 `obtained`，构筑校验通过；同一卡组调用战斗实例工厂返回 null，错误为“当前卡组构筑非法”。

影响：奖励卡、卡组编辑与战斗形成断链，玩家合法换入通用卡后无法开战。

建议：由 Run 把可构筑卡集合或已验证的开战卡组快照传入工厂，保持编辑、读档和开战的校验依据一致；不能用“允许所有注册卡”代替拥有权校验。

探针：`Obtained_generic_card_should_be_accepted_at_battle_creation`。

### 2. [P1] GAS 伤害先被生命余额截断，再经过护盾和伤害规则

位置：[SharedHpSettlement.cs:84](../../Src/mod/combat/effects/SharedHpSettlement.cs#L84)，上游：[DamageExecution.cs:68](../../Src/frame/gas/Executions/DamageExecution.cs#L68)。

GAS 先写入目标 Health，`DamageExecution` 把负值截为零。共享生命转移再取写入前后的差值，之后才运行规则与护盾。因此规则看到的是 `min(伤害, 当前生命)`，原始超额伤害已丢失。敌方目标的 `RunOnTarget` 也采用先写入再取差值的方式。

复现：队伍剩余 20 HP，角色持有 50 护盾，敌方 GAS 攻击造成 100 伤害。正确结果是护盾耗尽后队伍死亡；实际队伍仍有 20 HP，护盾只承担被截断的伤害。

影响：低血量时护盾和百分比减伤可异常保命；同一数值在直伤与 GAS 通道中可能得到不同生死结果。

建议：伤害公式先产生未按生命余额截断的伤害包，经规则、护盾后再写账本，最后计算真实生命损失。避免通过临时改写 Health 反推待结算伤害。

探针：`Gas_overkill_should_apply_shield_before_health_clipping`。

### 3. [P1] Buff 钩子递归环能通过内容准入，并绕过深度保护

位置：[ContentDefinitionValidator.cs:1360](../../Src/frame/content/ContentDefinitionValidator.cs#L1360)、[BuffRuntime.cs:647](../../Src/mod/combat/buffs/BuffRuntime.cs#L647)、[CombatEffectExecutor.cs:58](../../Src/mod/combat/effects/CombatEffectExecutor.cs#L58)。

循环检测只遍历 `ChainEffects` 和 `ChainActions`。合法形状的 `Buff A.onApply → ApplyBuff(A)`、`stackRule: Replace` 可以入库。每次 Buff 钩子又调用公开的 `ExecuteEffectRef`，将递归深度重置为零；替换后再次触发 onApply，循环无法被 `MaxChainDepth` 截断。

复现：上述 buff 与 effect 均保留在注册表中，校验器没有 cycle 错误。未执行无限递归以避免终止测试进程。

影响：内容 Mod 的配置错误可以在挂载时使游戏进程 StackOverflow。脚本提案再次调用 effect/action 的入口也重置深度，存在相同防护缺口。未发现基础内容当前使用该循环配置。

建议：对同步触发边构建包含 Buff 钩子、ApplyBuff、相关 GE/Action 的引用图；运行时使用贯穿一次结算的执行上下文，统一累计深度/步骤预算，不在钩子和脚本边界清零。

探针：`Buff_apply_hook_cycle_should_be_rejected_by_content_validator`。

### 4. [P1] 领域钩子增删 GameplayEffect 会使活列表枚举抛异常

位置：[AbilitySystemComponent.cs:52](../../Src/frame/gas/AbilitySystemComponent.cs#L52)，回合结束同类枚举在第 79 行；接线：[TeamDomainManager.cs:92](../../Src/mod/combat/runtime/TeamDomainManager.cs#L92)。

ASC 遍历 `_activeEffects` 时立即执行钩子。领域 onTurnStart 的 `SetDomain` 会移除旧 GE 并新增 GE，修改正在枚举的 List。

复现：旧领域的 onTurnStart 配置替换新领域；`FireTurnStartHooks()` 抛出 `InvalidOperationException: Collection was modified`，栈定位到 ASC 第 52 行。

影响：公开允许的内容组合中断回合结算，可能留下已局部修改的状态。基础内容是否实际采用领域在钩子中替换的组合，未作为本次断言前提。

建议：使用快照或延迟增删队列，明确本批次新增效果是否参与本次钩子；继续执行快照对象前确认它仍属于容器。

探针：`Domain_turn_start_hook_may_replace_domain_without_collection_exception`。

### 5. [P2] 玩家阶段清波后，己方目标的付费标记跨回合残留

位置：[CombatSimulation.cs:400](../../Src/mod/combat/runtime/CombatSimulation.cs#L400)、[CombatSimulation.cs:465](../../Src/mod/combat/runtime/CombatSimulation.cs#L465)。

目标丢失处理只取消包含阵亡敌人的标记；主动技/充能球清波后，换波直接进入下一回合并重新灌入可用能量，没有清理其他标记。Self/Ally/Team 类已付费标记可继续存在。

复现：先标记一张费用 1 的 Self 卡，再释放主动技清波。换波及回合推进成功，但队列仍有 1 项，手牌仍标记。

影响：旧 Paid 与新回合能量池混用。继续结算可以绕过新回合扣费；取消则把旧回合 Paid 退款到新能量池，打破费用守恒。

建议：在跨回合前明确统一处理剩余标记，清队列并清手牌标记，退款必须属于原账期。给回合结尾增加队列与标记一致性断言。

探针：`Self_target_mark_should_not_survive_player_phase_wave_clear`。

### 6. [P2] 玩家、敌人及非领域队伍上的持续 GE 缺少完整回合驱动

位置：[TeamDomainManager.cs:90](../../Src/mod/combat/runtime/TeamDomainManager.cs#L90)、[TeamDomainManager.cs:96](../../Src/mod/combat/runtime/TeamDomainManager.cs#L96)。

回合开始只驱动两个队伍 ASC；回合结束还以 `ActiveDomain != null` 为条件。角色和敌人 ASC 完全没有战斗级 OnTurnStart/OnTurnEnd 接线，HookDispatcher 也只安装在队伍 ASC 上。

复现：通过正式应用器给角色挂持续 2 回合的 GE，推进两次敌方阶段和回合开始，GE 仍在 ActiveEffects 中。

影响：通过 ApplyGameplayEffect 扩展的持续增减益、封印标签不会如期到期；角色/敌人 GE 的周期和钩子契约失效。基础内容已定义 `weak.json` 的持续 GE，但本次没有声称它目前一定被某张出货卡使用。普通 Buff 的时长管线是另一条已接入的路径，不能替代 GE 的契约。

建议：建立统一的战斗 ASC 生命周期驱动，涵盖队伍、角色、存活敌人；领域只负责领域槽的替换/时长，不承包全局 GE tick。

探针：`Character_gameplay_effect_should_expire_after_two_real_turns`。

### 7. [P2] 目标校验不保证集合唯一性和卡牌目标数量

位置：[CombatStateMachine.cs:251](../../Src/mod/combat/statemachine/CombatStateMachine.cs#L251)、[CombatStateMachine.cs:371](../../Src/mod/combat/statemachine/CombatStateMachine.cs#L371)。

PlayCard 直接保存 `command.Targets`，没有校验数量、唯一性与整张牌目标规格；结算只逐项过滤合法性。主动技 All 只检查“数量相同且每项都合法”，没有检查与合法集合相等。

复现一：Single 卡传入 `[敌人0, 敌人0]`，指令成功。两个目标在结算阶段都会通过合法性过滤。

复现二：存在敌人0/1时，All 主动技传入 `[敌人0, 敌人0]`，指令也成功，敌人1被漏掉而敌人0可重复吃效果。

影响：UI 的正常单体点选部分掩盖了缺陷，但模拟器公开指令、调试调用和未来联机入口均缺乏可信边界；重复目标可放大伤害/增益。

建议：在扣费前统一规范化/验证目标集；All 使用集合等价或直接由模拟器展开；Single/Self 验证固定数量；RandomN 明确由哪一层抽样。队列应复制目标快照。

探针：`Single_target_card_should_reject_duplicate_targets`、`All_target_active_skill_should_reject_duplicate_subset`。

### 8. [P2] 被钩子移除的叠层 Buff 可以重新留下永久属性修正

位置：[BuffRuntime.cs:301](../../Src/mod/combat/buffs/BuffRuntime.cs#L301)，重新注册点在第 324 行。

FireTurnEnd 先快照、触发钩子，再对快照全部 tick。当 onTurnEnd 驱散某个实例，后续仍会 tick 它；层数减少时又调用 RegisterModifiers。是否仍属于容器的检查只在最终移除过期实例时执行。

复现：构造剩余 1/2 回合的两层 +6 物攻 Buff，在回合结束钩子里驱散自己。容器已经为空，物攻却从基础 10 变为 16。

影响：产生无法通过容器显示或驱散的残留增益；其持久化到本场战斗剩余时间的行为不符合 Buff 移除契约。

建议：tick 与重新注册之前确认实例仍在容器，钩子枚举也要跳过已经移除的实例；修饰符注册/注销必须由同一个状态转移管理。

探针：`Buff_removed_by_turn_end_hook_should_not_reregister_modifiers`。

### 9. [P2] 完全被护盾抵消的直伤漏触发 onDamaged

位置：[CombatEffectExecutor.cs:356](../../Src/mod/combat/effects/CombatEffectExecutor.cs#L356)、[DamagePipeline.cs:99](../../Src/mod/combat/effects/DamagePipeline.cs#L99)。

DamagePipeline.NotifyAfter 已规定敌方攻击命中即记录受击，与掉血解耦；GAS 转移路径会调用它，即使护盾后 applied 为零。直伤和 FixedDamage 则在 applied <= 0 时提前 continue，漏记受击。

复现：队伍 90/100 HP，角色有 20 护盾和受击回复 5 HP 的被动；敌方直伤 10 被挡住后，清算受击仍为 90 HP，预期为 95。

影响：受击回血、受击得盾等被动取决于攻击的实现通道，形成隐藏的内容差异。

建议：把“命中事件”和“实际生命损失”分开，由全部伤害入口统一完成命中记账。不要以 applied > 0 代替命中判定。

探针：`Shielded_direct_hit_should_still_fire_on_damaged`。

### 10. [P2] 共享生命上限协调器按未加领域修正的基础上限裁剪生命

位置：[TeamMaxHealthCoordinator.cs:47](../../Src/mod/combat/gas/TeamMaxHealthCoordinator.cs#L47)。

协调器先根据角色 MaxHealth 总和计算 newHealth，再调用队伍 ASC 的聚合。领域等队伍 MaxHealth 修饰符虽然被保留，裁剪生命却已经用了不包含它们的 newMax。

复现：角色合计上限 100，领域加 100，队伍当前生命 150；角色上限提高 10 后，最终 MaxHp 正确变为 210，当前生命却错误下降为 110，预期仍为 150。

影响：角色生命增益变化可能凭空扣掉队伍生命；负向队伍上限修正还可能使生命超过最终有效上限。

建议：先重算有效 MaxHealth，再用 `Asc.GetCurrentValue(MaxHealth)` 裁剪旧生命，并避免基础值写入期间的中间通知被当成最终状态。

探针：`Team_hp_clamp_should_use_effective_max_including_domain`。

### 11. [P2] 规则引擎的 OnTurnStart 在实际战斗里从未派发

位置：[CombatStateMachine.cs:86](../../Src/mod/combat/statemachine/CombatStateMachine.cs#L86)、[CombatSimulation.cs:465](../../Src/mod/combat/runtime/CombatSimulation.cs#L465)。

规则接口与引擎提供了 DispatchTurnStart，但首回合和后续回合只调用领域/Buff 钩子；生产战斗代码没有调用规则入口。

复现：安装计数规则，开战再推进到第二回合，预期收到 2 次 OnTurnStart，实际为 0。

影响：自定义战斗规则的回合开始行为全部静默失效。现有引擎单测直接调用 DispatchTurnStart，所以没有验证实际接线。

建议：统一首回合与后续回合的 TurnStart 编排，并对规定顺序添加整条模拟路径的集成测试。

探针：`Rules_should_receive_first_and_subsequent_turn_start`。

### 12. [P2] 回合开始钩子造成死亡后仍进入玩家输入阶段

位置：[CombatStateMachine.cs:88](../../Src/mod/combat/statemachine/CombatStateMachine.cs#L88)、[CombatSimulation.cs:478](../../Src/mod/combat/runtime/CombatSimulation.cs#L478)。

首回合/后续回合开始钩子跑完后，没有结束判定；后续路径还会无条件 TransitionTo(Player) 并运行资源管线。

复现：永久 Buff 的 onTurnStart 对队伍造成致死伤害；开战结束时 IsDefeated=true，但 Phase=Player，预期为 Defeat。

影响：死亡后需要再发一条成功指令才能触发结算；阶段状态与生命状态不一致。回合开始直接清空敌方也存在漏判定边界。

建议：在规定的钩子结算边界检查终局/换波；终局后不能继续资源灌入或覆盖终局阶段。

探针：`Lethal_turn_start_hook_should_end_battle_before_player_input`。

### 13. [P2] 脚本按战斗归属 Mod 执行，忽略动作/效果自身归属

位置：[SkillActionExecutor.cs:265](../../Src/mod/combat/effects/SkillActionExecutor.cs#L265)，归属选择来源：[RunController.cs:470](../../Src/mod/run/RunController.cs#L470)。

所有脚本请求使用 simulation.ModId，生产中它取自 Battle 的归属。合并后的内容可以引用其他 Mod 的角色、卡牌和动作，而脚本路径是各 Mod 内部相对路径。

复现：动作 owner 为 test.mod，模拟器 battle owner 为 battle.mod；记录宿主收到 battle.mod，预期为 test.mod。

影响：跨 Mod 复用的脚本动作找不到脚本或误执行战斗 Mod 的同名脚本。EnemyAiController 也持有单一 ModId，存在对应的归属设计问题。

建议：执行带脚本的 Effect/SkillAction/Enemy 时，通过 registry 的 `(category,id)` 查询 owner；把来源定义类别和 ID 保留在执行上下文，尤其是兼容 Effect 转 Action 时。

探针：`Scripted_action_should_execute_in_its_definition_owner_mod`。

### 14. [P2] 定值伤害返回值和表现事件把超额伤害当成实际损血

位置：[CombatEffectExecutor.cs:428](../../Src/mod/combat/effects/CombatEffectExecutor.cs#L428)，返回点在第 432 行；队伍分支第 392 行有同类问题。

ApplyAmountToTarget 按生命截断写入，但返回和 NotifyAfter 仍使用完整 amount。其公开注释承诺“实际写入”，普通攻击结果又对返回值求和。

复现：目标有 100 HP，ApplyFixedDamage 传 1000；目标正确归零，返回值却是 1000，预期实际损血为 100。

影响：普攻/球伤战报、表现飘字和消费 OnAfterDamage 的扩展规则与真实生命变化不一致。GAS 吸血使用前后生命差值，与此路径统计口径不同。

建议：明确区分计算伤害、护盾吸收、实际损血、超额伤害，统一返回结构和事件字段；与第 2/9 项一起修正管线。

探针：`Fixed_damage_result_should_report_actual_hp_loss`。

## 修复前的优雅性、复用与代码质量评价

| 领域 | 已做好的部分 | 主要限制 |
| --- | --- | --- |
| 逻辑与表现 | Combat 不直接控制 Godot 节点，值事件日志与播放器分离；UI 播放后对账 | 事件的 IReadOnlyList 并非真正不可变快照；部分事件引用队列输入的目标列表 |
| 角色与卡牌 | CharacterInstance / CharacterBattleInstance 分离；CardDto / CardRuntimeEntry / HandSlot 分层；手牌标记保留身份 | 开战缺少获得卡账本上下文；可变 DeckPreset/HandSlot 暴露，锁可被绕过；快照和定义引用边界不够明确 |
| 规则扩展 | 小接口、规则目录、确定性排序；集中费用计算与队列对账 | 回合入口未完整接线；UI 与模拟器重复承担目标责任 |
| 属性与效果 | Buff/GE 共用聚合器，修饰符按 handle 管理；连携、普攻、球独立组合 | 两套时长/钩子引擎重复承担相似职责，生命周期和伤害语义已经分叉 |
| 随机与测试 | AI/抽牌/重选/弃牌/球有独立随机流；已有大量机制及内容测试 | 构造辅助常跳过完整开战/回合路径，直接测试单个服务，容易漏掉接线和组合缺陷 |

值得优先做的局部改进：

1. **统一战斗编排边界。** CombatStateMachine 同时处理命令验证、费用、目标、载荷执行、阶段推进、Buff 与表现编排，文件体量很大。按 `CommandValidator`、`TargetResolver`、`SkillPayloadExecutor`、回合编排拆出明确职责，避免只是把分支搬到更多工具类。
2. **统一伤害结算结果。** 目前 GAS 用 Health 差值，直伤/普攻/球写账本，受击记账又在另一层；集中为“计算 → 修改 → 吸收 → 写入 → 事件”的一个入口，保留公式来源差异。
3. **生命周期由一个编排器负责。** Buff 和 GE 可以保留不同数据模型，但统一回合/波次驱动和重入策略。快照枚举必须配合成员存活检查，单独 ToArray 不够。
4. **解析与执行分离。** 多处重复 MergeParams、数字解析、目标展开；Dictionary<string,object>/JsonElement 在热路径中反复分支。将内容准入时可确定的参数转成有类型的计划，再让运行时执行。先统一数字解析和 invariant culture；不必立即搭建庞大的通用 DSL。
5. **收紧写入口。** TransitionTo、队列增删、HandSlot.Mark/PlaceCard、卡组编辑等公开可变入口允许调用方绕过不变量。控制可见性，给 UI/调试提供有意设计的门面；命令和事件入口复制集合。
6. **用组合集成测试补薄弱点。** 现有测试数量充足，缺的是完整工厂/Run 接线、低血量×护盾、多波×已标记队列、钩子×驱散/替换、跨 Mod 执行的交叉矩阵。增加“卡牌数量/身份守恒、Paid 与能量守恒、移除后无修饰符、终局后无输入”这些不变量检查，比重复分支单测更有价值。
7. **清理过时注释和空操作。** ChainCalculator、CombatSimulation 的部分注释仍称连携与增伤同桶，实际 DamageScaling/测试已采用单独乘算；有旧 trait 名称，ModifyStat 分支仍为空。把明确未实现能力在准入层拒绝或显式诊断，避免定义存在被误认为可用。

上述结构性建议不是额外已复现缺陷的计数，不意味着需要整体重写。当前表现事件、独立运行时、属性聚合和内容定义分层应继续保留。

## 建议修复顺序

1. 先修第 1–4 项：合法构筑开战、伤害管线的生死语义、递归与重入崩溃。
2. 再修第 5/6/8/10/11/12 项：统一回合与换波收尾、效果生命周期和有效生命上限；为完整管线建立不变量测试。
3. 收敛第 7/9/13/14 项：目标入口、受击与真实伤害契约、跨 Mod 归属。随后再拆解体量大的编排类与参数解析。

## 当前回归运行

原 `.cs.txt` 是修复前的历史复现，保留其原貌；当前请运行正式测试，不把原探针复制回编译目录。

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --no-restore --nologo -v q
```

聚焦新增回归可使用 `--filter FullyQualifiedName~CombatAuditRegressionTests`，Run 获得卡接线在 `RunBattleWiringTests` 中。