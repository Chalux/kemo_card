using KemoCard.Mod.Combat.Commands;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed class CombatStateMachine
{
    public ECombatPhase Phase { get; private set; }

    public CombatStateMachine(ECombatPhase initialPhase = ECombatPhase.BattleStart)
    {
        Phase = initialPhase;
    }

    public void TransitionTo(ECombatPhase phase) => Phase = phase;

    public void Advance(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        switch (Phase)
        {
            case ECombatPhase.BattleStart:
                RunBattleStart(simulation);
                break;
            case ECombatPhase.CardExecution:
                ExecuteCardExecutionPhase(simulation);
                break;
            case ECombatPhase.Enemy:
                ExecuteEnemyPhase(simulation);
                break;
        }
    }

    /// <summary>
    /// 规格 §6.1 BattleStart 权威顺序：注入技能（禁读写 SharedHp）→ 冻结补满 SharedHp
    /// → 每人开局抽满手牌 → 进入首个玩家阶段管线。
    /// </summary>
    internal void RunBattleStart(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var team = simulation.PlayerTeam;
        simulation.Presentation.Emit(new BattleStartedEvent());
        team.SharedHpLocked = true;
        try
        {
            foreach (var entry in simulation.BattleStartSkills)
                ExecuteBattleStartSkill(simulation, entry);

            // 已解锁被动在技能注入后、冻结补满前挂载（若被动改 MaxHealth 可正确影响共享血量冻结值）。
            foreach (var entry in simulation.InitialBuffs)
                simulation.Buffs.Apply(
                    simulation,
                    new CombatTargetRef(ECombatSide.Player, entry.CharacterIndex),
                    entry.BuffId,
                    entry.Params);
        }
        finally
        {
            team.SharedHpLocked = false;
        }

        team.FreezeAndFillSharedHp();

        for (var i = 0; i < team.Characters.Count; i++)
            PresentationEmitter.DrawAndEmit(simulation, i, CombatConstants.HandSlotCount);

        // 敌人开战 buff：内容在 EnemyDto.buffRefs 声明（木桩的"每回合回血"等）。
        // 必须在 onWaveStart / 首个 onTurnStart 之前挂载，否则第一次钩子会漏。
        // （换波路径同样要挂：见 CombatSimulation.AdvanceToNextWave。）
        simulation.ApplyEnemyInitialBuffs();

        // 阶层（波次）1 开始：开战被动已由 BattleStartSkills 挂载，onWaveStart 在此补发一次。
        simulation.Buffs.FireWaveStart(simulation);

        // 经仿真切相位：相位变化的表现事件（PhaseChanged）由 CombatSimulation.TransitionTo 统一记录。
        simulation.TransitionTo(ECombatPhase.Player);
        simulation.DomainManager.FireTurnStartHooks();
        simulation.Buffs.FireTurnStart(simulation);
        PlayerPhasePipeline.Run(simulation, isFirstPlayerPhase: true);
    }

    private static void ExecuteBattleStartSkill(CombatSimulation simulation, BattleStartSkillEntry entry)
    {
        // 非法技能 id：无操作（规格 §5.5 软失败）
        if (!simulation.Definitions.Store.TryGetSkill(entry.SkillId, out var skill))
            return;

        var source = entry.SourceCharacterIndex >= 0
            ? new CombatTargetRef(ECombatSide.Player, entry.SourceCharacterIndex)
            : CombatTargetRef.PlayerTeam;
        ExecuteSkillPayload(simulation, skill, source, ResolveBattleStartTargets(simulation, skill, source));
    }

    /// <summary>
    /// BattleStart 注入技能的目标解析：缺省为 self；玩家侧 <see cref="ETargetScope.All"/> 展开为全部槽位，
    /// <see cref="ETargetScope.Team"/> 解析为队伍账本。开战阶段没有敌人行动上下文，其余组合一律退回 self。
    /// </summary>
    private static IReadOnlyList<CombatTargetRef> ResolveBattleStartTargets(
        CombatSimulation simulation,
        SkillDto skill,
        CombatTargetRef source)
    {
        if (skill.TargetOverride is { Scope: ETargetScope.Team })
            return [CombatTargetRef.PlayerTeam];
        if (skill.TargetOverride is not { Scope: ETargetScope.All })
            return [source];

        return [.. Enumerable
            .Range(0, simulation.PlayerTeam.Characters.Count)
            .Select(index => new CombatTargetRef(ECombatSide.Player, index))];
    }

    public CombatApplyResult TryApply(CombatSimulation simulation, ICombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(command);

        return Phase switch
        {
            ECombatPhase.Player => TryApplyPlayerPhase(simulation, command),
            _ => new CombatApplyResult(false, $"阶段 {Phase} 不支持该指令。"),
        };
    }

    private static CombatApplyResult TryApplyPlayerPhase(CombatSimulation simulation, ICombatCommand command)
    {
        var result = command switch
        {
            PlayCardCommand playCard => ApplyPlayCard(simulation, playCard),
            CastActiveSkillCommand castSkill => ApplyCastActiveSkill(simulation, castSkill),
            ConfirmCharacterCommand confirm => ApplyConfirmCharacter(simulation, confirm),
            UnconfirmCharacterCommand unconfirm => ApplyUnconfirmCharacter(simulation, unconfirm),
            CancelQueuedCardCommand cancel => ApplyCancelQueuedCard(simulation, cancel),
            TriggerOrbsCommand => ApplyTriggerOrbs(simulation),
            _ => new CombatApplyResult(false, "玩家阶段尚未实现该指令。"),
        };

        // 规格 §3.3：费用变化不在阶段开始统一扫，而是玩家阶段内每次成功操作后即时对账。
        if (result.Success)
        {
            QueuedCostReconciler.Reconcile(simulation);
            EnforceSealsOnAllCharacters(simulation);
            AdvanceToCardExecutionIfAllActed(simulation);
        }

        return result;
    }

    /// <summary>
    /// 主动触发充能球：不占"已行动"、不消耗能量，因此不会推动阶段推进（出牌阶段内可重复触发）。
    /// </summary>
    private static CombatApplyResult ApplyTriggerOrbs(CombatSimulation simulation)
    {
        var result = simulation.Orbs.TriggerManual(simulation);
        return result.Triggered
            ? new CombatApplyResult(true)
            : new CombatApplyResult(false, result.Error ?? "充能球触发失败。");
    }

    /// <summary>
    /// 规格 §2.1 / §2.5：全员「已行动或封印」即进入结算阶段。
    /// </summary>
    /// <remarks>
    /// 判定必须覆盖<b>所有</b>成功指令与阶段起点，不能只挂在 Confirm 指令上：封印角色的
    /// <c>HasActed</c> 由 <see cref="EnforceSeal"/> 置位，若宿主对封印角色禁用确认按钮，
    /// 就永远不会有那条 Confirm 指令，战斗会永久停在 Player 阶段（硬软锁）。
    /// 非满编队伍维持原语义不自动推进（由 <c>CombatSimulationFactory</c> 拒绝）。
    /// </remarks>
    internal static void AdvanceToCardExecutionIfAllActed(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Phase != ECombatPhase.Player)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (characters.Count != CombatConstants.SlotCount)
            return;
        if (!characters.All(character => character.HasActed || character.IsSealed))
            return;

        simulation.TransitionTo(ECombatPhase.CardExecution);
    }

    /// <summary>
    /// 规格 §2.5：若角色处于封印，清空其全部手牌标记并退还各 <c>paid</c>，再视作已行动。
    /// 未封印时无操作。资源（能量 / <c>S</c> / 牌）一律不回滚。
    /// </summary>
    public static void EnforceSeal(CombatSimulation simulation, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;
        if (!character.IsSealed)
            return;

        var markedEntries = simulation.CardQueue
            .PeekAllOrdered()
            .Where(entry => entry.CharacterIndex == characterIndex)
            .ToList();
        foreach (var entry in markedEntries)
            CancelMarkAndRefund(simulation, entry);

        character.SetHasActed(true);
    }

    private static void EnforceSealsOnAllCharacters(CombatSimulation simulation)
    {
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            EnforceSeal(simulation, i);
    }

    private static CombatApplyResult ApplyPlayCard(CombatSimulation simulation, PlayCardCommand command)
    {
        if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
            return new CombatApplyResult(false, error);
        if (character.IsSealed)
            return new CombatApplyResult(false, "角色处于封印，无法标记卡牌。");
        if (character.HasActed)
            return new CombatApplyResult(false, "角色已确认，须先取消确认才能标记卡牌。");
        if (command.HandSlotIndex < 0 || command.HandSlotIndex >= character.HandSlots.Count)
            return new CombatApplyResult(false, "手牌槽位无效。");

        var slot = character.HandSlots[command.HandSlotIndex];
        if (slot.IsEmpty || slot.CardId is null || slot.RuntimeInstanceId is null)
            return new CombatApplyResult(false, "指定槽位没有卡牌。");
        if (slot.IsMarked)
            return new CombatApplyResult(false, "该卡牌已标记入队。");
        if (!simulation.Definitions.Store.TryGetCard(slot.CardId, out var card))
            return new CombatApplyResult(false, "卡牌定义不存在。");
        if (card.CostType is not (ECostType.None or ECostType.Energy))
            return new CombatApplyResult(false, "该费用类型尚未实装，无法标记入队。");

        var paid = CardCostCalculator.Compute(simulation, command.CharacterIndex, card, slot.RuntimeInstanceId);
        if (!character.TryConsumeAvailableEnergy(paid))
            return new CombatApplyResult(false, "可用能量不足。");

        var sequence = simulation.AllocateQueueSequence();
        simulation.CardQueue.Enqueue(new QueuedCardEntry(
            command.CharacterIndex,
            slot.CardId,
            slot.RuntimeInstanceId,
            card.Priority,
            command.Targets,
            sequence,
            paid));
        slot.Mark(sequence);
        PresentationEmitter.EmitEnergy(simulation, command.CharacterIndex);
        return new CombatApplyResult(true);
    }

    #region 主动技蓄力链（规格 §5）

    /// <summary>
    /// 规格 §5.3 / §5.4：按当前 <c>S</c> 解析出最高可用档 → 按该档目标规格校验 targets
    /// → 扣累计阈值 <c>T_k</c> → 执行技能载荷 → 处理目标丢失。不扣可用能量、不占已行动。
    /// </summary>
    private static CombatApplyResult ApplyCastActiveSkill(CombatSimulation simulation, CastActiveSkillCommand command)
    {
        if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
            return new CombatApplyResult(false, error);
        if (character.IsSealed)
            return new CombatApplyResult(false, "角色处于封印，无法释放主动技。");
        if (character.ActiveSkillChain.Count == 0)
            return new CombatApplyResult(false, "该角色没有配置主动技。");

        var tierIndex = character.ResolveCastableTier();
        if (tierIndex < 0)
            return new CombatApplyResult(false, "技能计数不足，无法释放主动技。");

        var skillId = character.ActiveSkillChain[tierIndex].SkillId;
        if (!simulation.Definitions.Store.TryGetSkill(skillId, out var skill))
            return new CombatApplyResult(false, "主动技档位的技能定义不存在。");

        var resolvedTargets = ResolveActiveSkillTargets(
            simulation,
            skill,
            command.CharacterIndex,
            command.Targets,
            out var targetError);
        if (resolvedTargets is null)
            return new CombatApplyResult(false, targetError);

        // 先扣阈值再跑载荷：载荷里可显式 +S 做连发（规格 §5.3）。
        character.PaySkillCounter(character.GetTierThreshold(tierIndex));

        var aliveEnemiesBefore = SnapshotAliveEnemyIndices(simulation);
        var source = new CombatTargetRef(ECombatSide.Player, command.CharacterIndex);
        var previousChannel = simulation.CurrentDiscardChannel;
        simulation.SetDiscardChannel(EDiscardChannel.ActiveSkill);
        try
        {
            ExecuteSkillPayload(simulation, skill, source, resolvedTargets);
        }
        finally
        {
            simulation.SetDiscardChannel(previousChannel);
        }

        // 被动钩子：持有者释放主动技后触发（如 chalux 被动6）。
        simulation.Buffs.FireActiveSkillCast(simulation, command.CharacterIndex);

        HandleTargetLoss(simulation, aliveEnemiesBefore);
        return new CombatApplyResult(true);
    }

    /// <summary>
    /// 规格 §5.4 决议：<c>targets</c> 按「将释放档位」的技能目标规格校验，各档可配不同规格。
    /// <see cref="SkillDto.TargetOverride"/> 缺省时视作 Self 单体（只接受空 targets 或指向施法者自己）。
    /// </summary>
    /// <returns>校验通过时返回结算用目标集合；失败返回 <c>null</c> 并给出 <paramref name="error"/>。</returns>
    private static IReadOnlyList<CombatTargetRef>? ResolveActiveSkillTargets(
        CombatSimulation simulation,
        SkillDto skill,
        int characterIndex,
        IReadOnlyList<CombatTargetRef> targets,
        out string error)
    {
        error = string.Empty;
        var self = new CombatTargetRef(ECombatSide.Player, characterIndex);
        var spec = skill.TargetOverride;
        if (spec is null || spec.Scope is ETargetScope.Self)
        {
            if (targets.Count == 0 || (targets.Count == 1 && targets[0] == self))
                return [self];

            error = "该档主动技只能指向自己。";
            return null;
        }

        if (spec.Scope is ETargetScope.Team)
        {
            if (spec.Side is ETargetSide.Enemy)
            {
                error = "敌方队伍账本尚未实装，该档主动技不能使用 Team 目标。";
                return null;
            }

            if (targets.Count == 0 || (targets.Count == 1 && targets[0] == CombatTargetRef.PlayerTeam))
                return [CombatTargetRef.PlayerTeam];

            error = "该档主动技结算到己方队伍账本，只接受队伍目标。";
            return null;
        }

        var legal = CollectLegalTargets(simulation, spec.Side, characterIndex);
        if (legal.Count == 0)
        {
            error = "没有合法目标。";
            return null;
        }

        if (spec.Scope is ETargetScope.All)
        {
            if (targets.Count == 0)
                return legal;
            if (targets.Count == legal.Count && targets.All(legal.Contains))
                return targets;

            error = "该档主动技作用于全体，目标集合与合法目标不一致。";
            return null;
        }

        var maxTargets = spec.Scope is ETargetScope.RandomN ? Math.Max(1, spec.TargetCount) : 1;
        if (targets.Count == 0 || targets.Count > maxTargets || targets.Distinct().Count() != targets.Count)
        {
            error = $"该档主动技需要 1 到 {maxTargets} 个互不重复的目标。";
            return null;
        }

        if (!targets.All(legal.Contains))
        {
            error = "目标不在该档主动技的合法目标范围内。";
            return null;
        }

        return targets;
    }

    #endregion

    private static HashSet<int> SnapshotAliveEnemyIndices(CombatSimulation simulation)
    {
        var alive = new HashSet<int>();
        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
        {
            if (simulation.EnemyTeam.Enemies[i].IsAlive)
                alive.Add(i);
        }

        return alive;
    }

    /// <summary>
    /// 规格 §2.3：玩家阶段出现单位丢失后，对目标集合包含丢失单位的**已标记牌整张取消标记**并退还
    /// <c>paid</c>，再把持有者回退未确认。同角色其它仍合法的标记保留。
    /// </summary>
    internal static void HandleTargetLoss(
        CombatSimulation simulation,
        HashSet<int> aliveEnemiesBefore)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(aliveEnemiesBefore);

        var lostEnemies = new HashSet<int>();
        foreach (var index in aliveEnemiesBefore)
        {
            if (!simulation.EnemyTeam.Enemies[index].IsAlive)
                lostEnemies.Add(index);
        }

        if (lostEnemies.Count == 0)
            return;

        var affectedEntries = simulation.CardQueue
            .PeekAllOrdered()
            .Where(entry => entry.Targets.Any(target =>
                target.Side == ECombatSide.Enemy && lostEnemies.Contains(target.Index)))
            .ToList();

        var characters = simulation.PlayerTeam.Characters;
        foreach (var entry in affectedEntries)
        {
            CancelMarkAndRefund(simulation, entry);
            if (entry.CharacterIndex >= 0 && entry.CharacterIndex < characters.Count)
                characters[entry.CharacterIndex].SetHasActed(false);
        }
    }

    private static CombatApplyResult ApplyConfirmCharacter(CombatSimulation simulation, ConfirmCharacterCommand command)
    {
        var index = command.CharacterIndex;
        var characters = simulation.PlayerTeam.Characters;
        if (index < 0 || index >= characters.Count)
            return new CombatApplyResult(false, "角色索引无效。");

        characters[index].SetHasActed(true);
        return new CombatApplyResult(true);
    }

    private static CombatApplyResult ApplyUnconfirmCharacter(
        CombatSimulation simulation,
        UnconfirmCharacterCommand command)
    {
        if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
            return new CombatApplyResult(false, error);
        if (character.IsSealed)
            return new CombatApplyResult(false, "角色处于封印，无法取消确认。");

        character.SetHasActed(false);
        return new CombatApplyResult(true);
    }

    private static CombatApplyResult ApplyCancelQueuedCard(CombatSimulation simulation, CancelQueuedCardCommand command)
    {
        if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
            return new CombatApplyResult(false, error);
        if (character.HasActed)
            return new CombatApplyResult(false, "角色已确认，须先取消确认才能取消标记。");

        var entry = simulation.CardQueue
            .PeekAllOrdered()
            .FirstOrDefault(candidate => MatchesCancelCommand(candidate, command));
        if (entry is null)
            return new CombatApplyResult(false, "未找到可取消的卡牌。");

        CancelMarkAndRefund(simulation, entry);
        return new CombatApplyResult(true);
    }

    /// <summary>
    /// 取消一项手牌标记：出队、退还 <see cref="QueuedCardEntry.Paid"/> 到当前可用能量、清除槽位标记。
    /// 牌保留在原手牌槽（规格 §2.2 / §3.3）。
    /// </summary>
    internal static void CancelMarkAndRefund(CombatSimulation simulation, QueuedCardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(entry);

        simulation.CardQueue.TryRemove(candidate => candidate.Sequence == entry.Sequence, out _);

        var characters = simulation.PlayerTeam.Characters;
        if (entry.CharacterIndex < 0 || entry.CharacterIndex >= characters.Count)
            return;

        var character = characters[entry.CharacterIndex];
        character.RefundAvailableEnergy(entry.Paid);
        foreach (var slot in character.HandSlots)
        {
            if (string.Equals(slot.RuntimeInstanceId, entry.RuntimeInstanceId, StringComparison.Ordinal))
                slot.Unmark();
        }

        PresentationEmitter.EmitEnergy(simulation, entry.CharacterIndex);
    }

    private static bool MatchesCancelCommand(QueuedCardEntry entry, CancelQueuedCardCommand command)
    {
        if (entry.CharacterIndex != command.CharacterIndex)
            return false;
        if (command.QueueSequence.HasValue && entry.Sequence != command.QueueSequence.Value)
            return false;
        if (!string.IsNullOrWhiteSpace(command.CardRuntimeInstanceId) &&
            !string.Equals(entry.RuntimeInstanceId, command.CardRuntimeInstanceId, StringComparison.Ordinal))
            return false;
        return true;
    }

    private static void ExecuteCardExecutionPhase(CombatSimulation simulation)
    {
        simulation.SetDiscardChannel(EDiscardChannel.CardExecution);
        // 连携按完整出牌队列一次性定档：结算循环会逐张出队，统计必须在出队前完成。
        var chainCounts = ChainCalculator.CountDistinctCharacters(simulation);
        // 定档快照同时留在仿真上：效果条件 ChainTierAtLeast 在单卡结算区间内读它
        // （"这张牌所属属性够不够 2 连携档"这类判定在效果求值时没有别的通道能拿到人头数）。
        simulation.SetChainCounts(chainCounts);
        try
        {
            while (simulation.CardQueue.TryDequeue(out var dequeued) && dequeued is not null)
            {
                SettleQueuedCard(simulation, dequeued, chainCounts);
                DiscardSettledCard(simulation, dequeued);
            }
        }
        finally
        {
            simulation.SetChainBonus(0f);
            simulation.SetChainCardElementFlags(0);
            // 连携定档快照只在本回合执行阶段有效：离开区间后条件不应再读到旧人头数。
            simulation.SetChainCounts(new Dictionary<EElement, int>());
            simulation.SetDiscardChannel(EDiscardChannel.Other);
        }

        // 本回合全部卡牌结算结束的钩子（onCardExecutionEnd）：在普攻之前，
        // 让"本回合共打出几张卡"这类终局统计先落地（巴赫被动5）。
        simulation.Buffs.FireCardExecutionEnd(simulation);

        // 普通攻击：本回合卡牌全部结算（含弃牌、连携清零）后自动执行一次，归属槽位 = (回合-1) % 队伍人数。
        // 必须在 CheckEndConditions 之前：普攻打死最后一名敌人时本回合敌人不再行动。
        simulation.NormalAttacks.Execute(simulation);

        simulation.CheckEndConditions();
        if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat or ECombatPhase.Player)
            return;

        // 进入敌方阶段不再补发"回合开始"（2026-09-20 修正）：回合开始只在回合数 +1 之后发生一次，
        // 否则 onTurnStart 钩子每回合触发两次（木桩回血、被动分档都会被翻倍）。
        simulation.TransitionTo(ECombatPhase.Enemy);
    }

    /// <summary>结算一张已标记牌；目标解析后为空集即空放（规格 §2.4），不做任何回滚。</summary>
    private static void SettleQueuedCard(
        CombatSimulation simulation,
        QueuedCardEntry entry,
        IReadOnlyDictionary<EElement, int> chainCounts)
    {
        if (!simulation.Definitions.Store.TryGetCard(entry.CardId, out var card))
            return;

        // 充能球回合结束统计口径：牌一旦进入结算就算"本回合打出"（含随后的空放）。
        simulation.RecordPlayedCard(entry.CardId, entry.CharacterIndex);

        var settleSlotIndex = FindHandSlotIndex(simulation, entry);
        simulation.Presentation.Emit(new CardSettleStartedEvent(
            entry.CharacterIndex,
            settleSlotIndex,
            entry.CardId,
            entry.Targets));

        // 槽位 buff（槽位伤害 / 充能）在此手牌结算前触发（打出即触发，含后续空放）。
        FireSlotBuffsForEntry(simulation, entry);

        // 连携定档需要一个角色实例（读 trait.chain_inject_red）。槽位非法时按"无加成"处理，
        // 既不回落到 0 号角色（口径与实际来源不一致），也不索引越界。
        var characters = simulation.PlayerTeam.Characters;
        var sourceIndexValid = entry.CharacterIndex >= 0 && entry.CharacterIndex < characters.Count;
        // 单卡结算区间：连携条件（ChainTierAtLeast）用"这张牌的属性"取人头数。
        simulation.SetChainCardElementFlags(card.Element);
        simulation.SetChainBonus(sourceIndexValid
            ? ChainCalculator.BonusForCard(chainCounts, card, characters[entry.CharacterIndex])
            : 0f);
        try
        {
            var resolvedTargets = ResolveCardTargets(simulation, entry, card);
            if (resolvedTargets.Count > 0)
            {
                var sourceRef = new CombatTargetRef(ECombatSide.Player, entry.CharacterIndex);
                foreach (var skillRef in card.SkillRefs)
                {
                    if (!simulation.Definitions.Store.TryGetSkill(skillRef.SkillId, out var skill))
                        continue;
                    ExecuteSkillPayload(simulation, skill, sourceRef, resolvedTargets, skillRef?.Params);
                }
            }
        }
        finally
        {
            simulation.SetChainBonus(0f);
        }

        // 结算后钩子（onCardSettled）：本回合出牌表此时已含该卡，判定"打出过 N 张某属性卡"才准确。
        // 空放同样补发——RecordPlayedCard 已把空放计入"本回合打出"（见上方注释），
        // 若这里跳过，莱因哈特被动2 这类"第 N 张牌触发"的判定会与出牌统计口径不一致（延迟到下一张牌）。
        // 单卡连携上下文必须撑到本钩子之后：ChainTierAtLeast 读的就是"这张牌所属属性的连携人头数"。
        simulation.Buffs.FireCardSettled(simulation, entry.CharacterIndex);
        simulation.SetChainCardElementFlags(0);
        simulation.Presentation.Emit(new CardSettleEndedEvent(entry.CharacterIndex, settleSlotIndex, entry.CardId));
    }

    /// <summary>按 RuntimeInstanceId 定位该牌当前所在手牌槽；找不到（已被弃）返回 -1。</summary>
    private static int FindHandSlotIndex(CombatSimulation simulation, QueuedCardEntry entry)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (entry.CharacterIndex < 0 || entry.CharacterIndex >= characters.Count)
            return -1;

        foreach (var slot in characters[entry.CharacterIndex].HandSlots)
        {
            if (string.Equals(slot.RuntimeInstanceId, entry.RuntimeInstanceId, StringComparison.Ordinal))
                return slot.SlotIndex;
        }

        return -1;
    }

    /// <summary>按 RuntimeInstanceId 找到打出卡牌所在的槽位并触发其槽位 buff 钩子。</summary>
    private static void FireSlotBuffsForEntry(CombatSimulation simulation, QueuedCardEntry entry)
    {
        if (entry.CharacterIndex < 0 || entry.CharacterIndex >= simulation.PlayerTeam.Characters.Count)
            return;

        var character = simulation.PlayerTeam.Characters[entry.CharacterIndex];
        foreach (var slot in character.HandSlots)
        {
            if (string.Equals(slot.RuntimeInstanceId, entry.RuntimeInstanceId, StringComparison.Ordinal))
            {
                simulation.Buffs.FireSlotCardPlayed(simulation, entry.CharacterIndex, slot);
                return;
            }
        }
    }

    /// <summary>规格 §2.1：结算完成（含空放）后把牌从手牌槽移入持有者弃牌堆。</summary>
    private static void DiscardSettledCard(CombatSimulation simulation, QueuedCardEntry entry)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (entry.CharacterIndex < 0 || entry.CharacterIndex >= characters.Count)
            return;

        var slotIndex = FindHandSlotIndex(simulation, entry);
        if (characters[entry.CharacterIndex].MoveHandCardToGraveyard(entry.RuntimeInstanceId))
        {
            simulation.Presentation.Emit(new CardDiscardedEvent(
                entry.CharacterIndex,
                slotIndex,
                entry.CardId,
                EDiscardChannel.CardExecution));
        }
    }

    private static void ExecuteEnemyPhase(CombatSimulation simulation)
    {
        simulation.SetDiscardChannel(EDiscardChannel.Other);
        for (var enemyIndex = 0; enemyIndex < simulation.EnemyTeam.Enemies.Count; enemyIndex++)
        {
            var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
            if (!enemy.IsAlive)
                continue;

            // 行动计数（2026-09-23）：> 1 时本回合只递减、不行动——把它设为 2 即"推迟到下个回合"。
            if (enemy.ActionCount > 1)
            {
                enemy.ActionCount--;
                enemy.IntentSkillId = null;
                continue;
            }

            var skillId = simulation.EnemyAi.ChooseSkill(enemy, simulation.EnemyAiRng);
            enemy.IntentSkillId = skillId;
            if (skillId is null)
                continue;

            simulation.Presentation.Emit(new EnemyActionStartedEvent(enemyIndex, skillId));
            ExecuteEnemySkill(simulation, enemyIndex, skillId);
            simulation.Presentation.Emit(new EnemyActionEndedEvent(enemyIndex, skillId));
        }

        simulation.Rules.DispatchTurnEnd(simulation.CreateContext());
        simulation.DomainManager.FireTurnEndHooks();
        // buff 时长统一在回合结束 tick（角色/敌人/槽位容器全部走这里，含到期 onRemove）。
        simulation.Buffs.FireTurnEnd(simulation);
        // 充能球回合结束产出（固定 1 个四属性球 + 1 个物理/魔法球）：满员时会即时自动触发，
        // 因此必须排在结束判定之前——触发伤害可能直接结束战斗。
        simulation.Orbs.GrantTurnEndOrbs(simulation, simulation.TakePlayedThisTurn());
        foreach (var character in simulation.PlayerTeam.Characters)
            character.SetHasActed(false);

        simulation.CheckEndConditions();
        if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat or ECombatPhase.Player)
            return;

        simulation.IncrementTurnNumber();
        simulation.IncrementTurnsIntoWave();
        // "本回合已触发充能球"与回合边界对齐地清账（见 ResetOrbsTriggeredThisTurn 注释）：
        // 上一回合结束产出并即时触发的球不得算进本回合。
        simulation.ResetOrbsTriggeredThisTurn();
        simulation.DomainManager.FireTurnStartHooks();
        simulation.Buffs.FireTurnStart(simulation);
        simulation.TransitionTo(ECombatPhase.Player);
        PlayerPhasePipeline.Run(simulation, simulation.IsFirstPlayerPhase);
    }

    private static void ExecuteEnemySkill(CombatSimulation simulation, int enemyIndex, string skillId)
    {
        if (!simulation.Definitions.Store.TryGetSkill(skillId, out var skill))
            return;

        SkillRefDto? skillRef = null;
        var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
        if (simulation.Definitions.Store.TryGetEnemy(enemy.DefinitionId, out var enemyDef))
        {
            skillRef = enemyDef.SkillRefs.FirstOrDefault(
                entry => string.Equals(entry.SkillId, skillId, StringComparison.Ordinal));
        }

        var source = new CombatTargetRef(ECombatSide.Enemy, enemyIndex);
        var targets = ResolveEnemySkillTargets(simulation, skill, enemyIndex);
        ExecuteSkillPayload(simulation, skill, source, targets, skillRef?.Params);
        // 受击钩子（onDamaged）：一次敌方技能的全部伤害先结算完，再按受击次数补触发。
        simulation.FlushOnDamagedHits();
    }

    private static void ExecuteSkillPayload(
        CombatSimulation simulation,
        SkillDto skill,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        Dictionary<string, object>? skillParams = null)
    {
        if (skill.ActionRefs.Count > 0)
        {
            foreach (var actionRef in skill.ActionRefs)
            {
                var resolvedActionRef = skillParams is not null
                    ? MergeActionRefParams(actionRef, skillParams)
                    : actionRef;
                simulation.EffectExecutor.ExecuteSkillActionRef(resolvedActionRef, simulation, source, targets);
            }
            return;
        }

        foreach (var effectRef in skill.EffectRefs)
        {
            var resolvedEffectRef = skillParams is not null
                ? MergeEffectRefParams(effectRef, skillParams)
                : effectRef;
            simulation.EffectExecutor.ExecuteEffectRef(resolvedEffectRef, simulation, source, targets);
        }
    }

    private static IReadOnlyList<CombatTargetRef> ResolveEnemySkillTargets(
        CombatSimulation simulation,
        SkillDto skill,
        int enemyIndex)
    {
        var spec = skill.TargetOverride;
        if (spec is null)
            return [CombatTargetRef.PlayerTeam];

        return ResolveTargetsFromSpec(simulation, spec, enemyIndex);
    }

    private static IReadOnlyList<CombatTargetRef> ResolveTargetsFromSpec(
        CombatSimulation simulation,
        TargetSpecDto spec,
        int sourceEnemyIndex)
    {
        // 规格 §1.3：scope: Team 对该侧队伍账本一次结算（敌方来源时「Enemy 侧」即玩家队伍）。
        if (spec.Scope is ETargetScope.Team)
            return ResolveTeamLedgerTarget(simulation, spec.Side is ETargetSide.Enemy or ETargetSide.Any);

        var legal = CollectLegalTargetsForEnemy(simulation, spec.Side, sourceEnemyIndex);
        if (legal.Count == 0)
            return [];

        // 嘲讽（2026-09-25）：指向玩家角色的单体 / 随机挑选只从"嘲讽值最高"的合法目标里取；
        // 范围（All）与账本（Team）不受影响——嘲讽吸引的是点名攻击。
        if (spec.Scope is not ETargetScope.All)
            legal = ApplyTauntPriority(simulation, legal);

        return spec.Scope switch
        {
            ETargetScope.All => legal,
            ETargetScope.RandomN => PickRandomTargets(legal, Math.Max(1, spec.TargetCount), simulation.RetargetRng),
            _ => legal.Count <= spec.TargetCount || spec.TargetCount <= 0
                ? [legal[0]]
                : legal.Take(spec.TargetCount).ToList(),
        };
    }

    /// <summary>
    /// 嘲讽优先（2026-09-25，见战斗规格「嘲讽」）：合法目标里存在嘲讽值 &gt; 0 的角色时，
    /// 只保留嘲讽值最高的那些（并列时保持原顺序）；没有嘲讽者时原样返回。
    /// </summary>
    private static List<CombatTargetRef> ApplyTauntPriority(
        CombatSimulation simulation,
        List<CombatTargetRef> legal)
    {
        var highest = 0f;
        foreach (var target in legal)
        {
            if (target.Side != ECombatSide.Player || target.Index < 0 ||
                target.Index >= simulation.PlayerTeam.Characters.Count)
            {
                continue;
            }

            var taunt = simulation.PlayerTeam.Characters[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt);
            if (taunt > highest)
                highest = taunt;
        }

        if (highest <= 0f)
            return legal;

        var filtered = new List<CombatTargetRef>(legal.Count);
        foreach (var target in legal)
        {
            if (target.Side != ECombatSide.Player || target.Index < 0 ||
                target.Index >= simulation.PlayerTeam.Characters.Count)
            {
                // 非玩家侧目标（如敌方自身指向）不参与嘲讽筛选。
                filtered.Add(target);
                continue;
            }

            if (Math.Abs(simulation.PlayerTeam.Characters[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt) - highest) <= 0.0001f)
                filtered.Add(target);
        }

        return filtered.Count > 0 ? filtered : legal;
    }

    /// <summary>
    /// 规格 §6.3：<c>scope: RandomN</c> 从合法目标中<b>无放回随机</b>抽取 N 个。
    /// </summary>
    /// <remarks>
    /// 不能退化成 <c>legal.Take(N)</c>：那样「随机」会变成固定取前 N 个（敌方技能永远打 0 号槽），
    /// 枚举名与规格承诺都与行为不符。
    /// </remarks>
    internal static List<CombatTargetRef> PickRandomTargets(
        List<CombatTargetRef> legal,
        int count,
        HostRng rng)
    {
        if (count >= legal.Count)
            return [.. legal];

        // 部分 Fisher-Yates：只需洗出前 count 个，避免整表全量打乱。
        var pool = legal.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = rng.NextInt(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return [.. pool[..count]];
    }

    private static List<CombatTargetRef> CollectLegalTargetsForEnemy(
        CombatSimulation simulation,
        ETargetSide side,
        int sourceEnemyIndex)
    {
        var legal = new List<CombatTargetRef>();
        if (side is ETargetSide.Self)
        {
            if (sourceEnemyIndex >= 0 && sourceEnemyIndex < simulation.EnemyTeam.Enemies.Count &&
                simulation.EnemyTeam.Enemies[sourceEnemyIndex].IsAlive)
            {
                legal.Add(new CombatTargetRef(ECombatSide.Enemy, sourceEnemyIndex));
            }

            return legal;
        }

        if (side is ETargetSide.Ally or ETargetSide.Any)
        {
            for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
            {
                if (side == ETargetSide.Ally && i == sourceEnemyIndex)
                    continue;
                if (!simulation.EnemyTeam.Enemies[i].IsAlive)
                    continue;
                legal.Add(new CombatTargetRef(ECombatSide.Enemy, i));
            }
        }

        if (side is ETargetSide.Enemy or ETargetSide.Any && !simulation.PlayerTeam.IsDefeated)
        {
            // 规格 §1.3：玩家侧点选的是槽位角色（分槽结算 D2）；要打账本必须显式写 scope: Team。
            for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
                legal.Add(new CombatTargetRef(ECombatSide.Player, i));
        }

        return legal;
    }

    /// <summary>规格 §1.3：v1 只有玩家侧有队伍账本；指向敌方队伍的 Team 目标无处结算，退化为空放。</summary>
    private static IReadOnlyList<CombatTargetRef> ResolveTeamLedgerTarget(
        CombatSimulation simulation,
        bool isPlayerSide)
    {
        if (!isPlayerSide || simulation.PlayerTeam.IsDefeated)
            return [];

        return [CombatTargetRef.PlayerTeam];
    }

    /// <summary>
    /// 规格 §2.4：单体在合法池中按 <see cref="ERetargetPolicy"/> 重选，池空即空放；
    /// 多目标去掉非法目标后对剩余合法子集结算，子集为空即空放。两者都不回滚已行动。
    /// </summary>
    private static IReadOnlyList<CombatTargetRef> ResolveCardTargets(
        CombatSimulation simulation,
        QueuedCardEntry entry,
        CardDto card)
    {
        // 规格 §1.3：scope: Team 的卡牌恒结算到队伍账本，标记时点选的槽位不参与。
        if (card.TargetScope is ETargetScope.Team)
            return ResolveTeamLedgerTarget(simulation, card.TargetSide is not ETargetSide.Enemy);

        if (entry.Targets.Count == 0)
            return [];

        var validTargets = entry.Targets
            .Where(target => IsValidTarget(simulation, card, entry.CharacterIndex, target))
            .ToList();
        if (validTargets.Count == entry.Targets.Count)
            return validTargets;
        if (!IsSingleTargetCard(card))
            return validTargets;
        if (validTargets.Count > 0)
            return [validTargets[0]];

        var retargeted = TryRetargetSingleTarget(simulation, card, entry.CharacterIndex);
        return retargeted.HasValue ? [retargeted.Value] : [];
    }

    private static EffectRefDto MergeEffectRefParams(EffectRefDto effectRef, Dictionary<string, object>? skillParams)
    {
        if (skillParams is null || skillParams.Count == 0)
            return effectRef;
        if (effectRef.Params is null || effectRef.Params.Count == 0)
        {
            return new EffectRefDto
            {
                EffectId = effectRef.EffectId,
                Params = new Dictionary<string, object>(skillParams, StringComparer.Ordinal),
            };
        }

        var merged = new Dictionary<string, object>(effectRef.Params, StringComparer.Ordinal);
        foreach (var (key, value) in skillParams)
            merged[key] = value;
        return new EffectRefDto
        {
            EffectId = effectRef.EffectId,
            Params = merged,
        };
    }

    private static SkillActionRefDto MergeActionRefParams(SkillActionRefDto actionRef, Dictionary<string, object>? skillParams)
    {
        if (skillParams is null || skillParams.Count == 0)
            return actionRef;
        if (actionRef.Params is null || actionRef.Params.Count == 0)
        {
            return new SkillActionRefDto
            {
                ActionId = actionRef.ActionId,
                Params = new Dictionary<string, object>(skillParams, StringComparer.Ordinal),
            };
        }

        var merged = new Dictionary<string, object>(actionRef.Params, StringComparer.Ordinal);
        foreach (var (key, value) in skillParams)
            merged[key] = value;
        return new SkillActionRefDto
        {
            ActionId = actionRef.ActionId,
            Params = merged,
        };
    }

    private static bool TryGetCharacter(
        CombatSimulation simulation,
        int characterIndex,
        out CharacterBattleInstance character,
        out string error)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
        {
            character = null!;
            error = "角色索引无效。";
            return false;
        }

        character = characters[characterIndex];
        error = string.Empty;
        return true;
    }

    private static bool IsSingleTargetCard(CardDto card) =>
        card.TargetScope is ETargetScope.Single or ETargetScope.Self || card.TargetCount <= 1;

    /// <summary>规格 §2.4：缺省与 <see cref="ERetargetPolicy.RandomLegal"/> 都走 Run RNG 在合法池均匀取一。</summary>
    private static CombatTargetRef? TryRetargetSingleTarget(CombatSimulation simulation, CardDto card, int sourceCharacterIndex)
    {
        if (card.RetargetPolicy == ERetargetPolicy.Skip)
            return null;

        var legal = CollectLegalTargets(simulation, card.TargetSide, sourceCharacterIndex);
        if (legal.Count == 0)
            return null;

        return card.RetargetPolicy switch
        {
            ERetargetPolicy.HighestHp => legal.MaxBy(target => GetTargetHp(simulation, target)),
            ERetargetPolicy.LowestHp => legal.MinBy(target => GetTargetHp(simulation, target)),
            _ => legal[simulation.RetargetRng.NextInt(0, legal.Count)],
        };
    }

    /// <summary>合法目标口径与界面共用一份（<see cref="CombatTargeting.CollectLegalTargets"/>）。</summary>
    private static List<CombatTargetRef> CollectLegalTargets(
        CombatSimulation simulation,
        ETargetSide side,
        int sourceCharacterIndex) =>
        CombatTargeting.CollectLegalTargets(simulation, side, sourceCharacterIndex);

    private static bool IsValidTarget(
        CombatSimulation simulation,
        CardDto card,
        int sourceCharacterIndex,
        CombatTargetRef target)
    {
        return card.TargetSide switch
        {
            ETargetSide.Self => target.Side == ECombatSide.Player && target.Index == sourceCharacterIndex,
            ETargetSide.Ally => target.Side == ECombatSide.Player &&
                target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count,
            ETargetSide.Enemy => target.Side == ECombatSide.Enemy &&
                target.Index >= 0 &&
                target.Index < simulation.EnemyTeam.Enemies.Count &&
                simulation.EnemyTeam.Enemies[target.Index].IsAlive,
            ETargetSide.Any => IsValidAnyTarget(simulation, target),
            _ => false,
        };
    }

    private static bool IsValidAnyTarget(CombatSimulation simulation, CombatTargetRef target)
    {
        if (target.Side == ECombatSide.Player)
        {
            return target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count;
        }

        return target.Side == ECombatSide.Enemy &&
            target.Index >= 0 &&
            target.Index < simulation.EnemyTeam.Enemies.Count &&
            simulation.EnemyTeam.Enemies[target.Index].IsAlive;
    }

    private static int GetTargetHp(CombatSimulation simulation, CombatTargetRef target)
    {
        if (target.Side == ECombatSide.Enemy)
            return simulation.EnemyTeam.Enemies[target.Index].CurrentHp;
        return simulation.PlayerTeam.SharedHp;
    }
}