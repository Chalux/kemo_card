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

    internal void TransitionTo(ECombatPhase phase) => Phase = phase;

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
        simulation.RunTurnStart();
    }

    private static void ExecuteBattleStartSkill(CombatSimulation simulation, BattleStartSkillEntry entry)
    {
        // 非法技能 id：无操作（规格 §5.5 软失败）
        if (!simulation.Definitions.Store.TryGetSkill(entry.SkillId, out var skill))
            return;

        var source = entry.SourceCharacterIndex >= 0
            ? new CombatTargetRef(ECombatSide.Player, entry.SourceCharacterIndex)
            : CombatTargetRef.PlayerTeam;
        SkillPayloadExecutor.Execute(simulation, skill, source, ResolveBattleStartTargets(simulation, skill, source));
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
        // 玩家阶段的效果（充能球 / 主动技）可能击杀敌人：先记存活集合，指令成功后统一按规格 §2.3
        // 处理目标丢失——对目标含已阵亡敌人的已标记牌整张取消并退还 paid。
        var aliveEnemiesBefore = SnapshotAliveEnemyIndices(simulation);
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
            HandleTargetLoss(simulation, aliveEnemiesBefore);
            // 全灭即结算（2026-09-26）：玩家阶段的效果（充能球 / 主动技）打死最后一名敌人时立即判定
            // （胜利 / 换波），不再要求玩家把本回合走完。换波会直接把回合切到下一回合，见 AdvanceToNextWave。
            simulation.CheckEndConditions();
            if (simulation.Phase == ECombatPhase.Player)
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
        if (!CombatCommandValidator.TryGetCharacter(simulation, characterIndex, out var character, out _))
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
        var validation = CombatCommandValidator.ValidateCardMark(simulation, command,
            out var character, out var slot, out var card, out var targets, out var paid);
        if (!validation.Success)
            return validation;

        if (!character.TryConsumeAvailableEnergy(paid))
            return new CombatApplyResult(false, "可用能量不足。");

        var sequence = simulation.AllocateQueueSequence();
        simulation.CardQueue.Enqueue(new QueuedCardEntry(
            command.CharacterIndex,
            slot.CardId!,
            slot.RuntimeInstanceId!,
            card.Priority,
            targets,
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
        if (!CombatCommandValidator.TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
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

        var selection = DiscardSelection.Resolve(simulation, skill, command.CharacterIndex);
        if (selection is not null && !selection.Validate(character, command.DiscardSlots) ||
            selection is null && command.DiscardSlots is { Count: > 0 })
            return new CombatApplyResult(false, "请选择合法数量的手牌进行弃置。");

        // 先扣阈值再跑载荷：载荷里可显式 +S 做连发（规格 §5.3）。
        character.PaySkillCounter(character.GetTierThreshold(tierIndex));

        var source = new CombatTargetRef(ECombatSide.Player, command.CharacterIndex);
        var previousChannel = simulation.CurrentDiscardChannel;
        var previousSelection = simulation.SelectedDiscardSlots;
        var previousSelectionOwner = simulation.SelectedDiscardCharacterIndex;
        simulation.SelectedDiscardSlots = new Queue<int>(command.DiscardSlots ?? []);
        simulation.SelectedDiscardCharacterIndex = command.CharacterIndex;
        simulation.SetDiscardChannel(EDiscardChannel.ActiveSkill);
        try
        {
            SkillPayloadExecutor.Execute(simulation, skill, source, resolvedTargets);
        }
        finally
        {
            simulation.SetDiscardChannel(previousChannel);
            simulation.SelectedDiscardSlots = previousSelection;
            simulation.SelectedDiscardCharacterIndex = previousSelectionOwner;
        }

        // 被动钩子：持有者释放主动技后触发（如 chalux 被动6）。
        simulation.Buffs.FireActiveSkillCast(simulation, command.CharacterIndex);

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
        var spec = skill.TargetOverride;
        return CombatTargeting.ResolvePlayerTargets(simulation, spec?.Side ?? ETargetSide.Self,
            spec?.Scope ?? ETargetScope.Self, spec?.TargetCount ?? 1, characterIndex, targets,
            out error);
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
        if (!CombatCommandValidator.TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
            return new CombatApplyResult(false, error);
        if (character.IsSealed)
            return new CombatApplyResult(false, "角色处于封印，无法取消确认。");

        character.SetHasActed(false);
        return new CombatApplyResult(true);
    }

    private static CombatApplyResult ApplyCancelQueuedCard(CombatSimulation simulation, CancelQueuedCardCommand command)
    {
        if (!CombatCommandValidator.TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
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

        if (!simulation.CardQueue.TryRemove(candidate => candidate.Sequence == entry.Sequence, out _))
            return;

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
            simulation.Buffs.FireCardExecutionStart(simulation);
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

        // 全灭即结算（2026-09-26）：卡牌已经杀光敌人时立即判定（胜利 / 换波），跳过空放的普攻——
        // 普攻已无目标，换波则让新波在"本回合不再普攻"的前提下登场（见 AdvanceToNextWave）。
        simulation.CheckEndConditions();
        if (simulation.Phase != ECombatPhase.CardExecution)
            return;

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

        // 连携定档需要角色实例（读取 chainElementInject）。槽位非法时按"无加成"处理，
        // 既不回落到 0 号角色（口径与实际来源不一致），也不索引越界。
        var characters = simulation.PlayerTeam.Characters;
        var sourceIndexValid = entry.CharacterIndex >= 0 && entry.CharacterIndex < characters.Count;
        // 单卡结算区间：连携条件（ChainTierAtLeast）用"这张牌的属性"取人头数；
        // 卡牌类型与打出者供「物理攻击的卡牌攻击次数 +N」（PhysicalCardAttackCount）判定。
        using var cardScope = simulation.EnterCardContext(card.CardType, entry.CharacterIndex, card.Element, sourceIndexValid
            ? ChainCalculator.BonusForCard(chainCounts, card, characters[entry.CharacterIndex])
            : 0f);
        try
        {
            var resolvedTargets = CombatTargetResolver.ResolveCardTargets(simulation, entry, card);
            if (resolvedTargets.Count > 0)
            {
                var sourceRef = new CombatTargetRef(ECombatSide.Player, entry.CharacterIndex);
                foreach (var skillRef in card.SkillRefs)
                {
                    if (!simulation.Definitions.Store.TryGetSkill(skillRef.SkillId, out var skill))
                        continue;
                    SkillPayloadExecutor.Execute(simulation, skill, sourceRef, resolvedTargets, skillRef?.Params);
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

        // 回合结束管线（规则 / 领域 / buff 时长 / 充能球产出）与玩家阶段清波共用 ResolveTurnEnd，
        // 每回合只结算一次（幂等）。
        simulation.ResolveTurnEnd();
        foreach (var character in simulation.PlayerTeam.Characters)
            character.SetHasActed(false);

        simulation.CheckEndConditions();
        if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat or ECombatPhase.Player)
            return;

        simulation.BeginNextTurn(incrementTurnsIntoWave: true);
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
        var targets = CombatTargetResolver.ResolveEnemySkillTargets(simulation, skill, enemyIndex);
        SkillPayloadExecutor.Execute(simulation, skill, source, targets, skillRef?.Params);
        // 受击钩子（onDamaged）：一次敌方技能的全部伤害先结算完，再按受击次数补触发。
        simulation.FlushOnDamagedHits();
    }

    internal static List<CombatTargetRef> PickRandomTargets(List<CombatTargetRef> legal, int count, HostRng rng) =>
        CombatTargetResolver.PickRandomTargets(legal, count, rng);
}