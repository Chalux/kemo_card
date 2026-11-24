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
                ResolveEndPlayerTurn(simulation);
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
            foreach (var id in simulation.Items.Relics)
            {
                if (!simulation.Definitions.Store.TryGetItem(id, out var relic) || relic.Kind != EItemKind.Relic)
                    continue;
                foreach (var reference in relic.BattleStartSkillRefs)
                    ExecuteBattleStartSkill(simulation, new BattleStartSkillEntry(reference.SkillId, -1, reference.Params));
            }
        }
        finally
        {
            team.SharedHpLocked = false;
        }

        team.FreezeAndFillSharedHp();

        PresentationEmitter.DrawTeamAndEmit(simulation, CombatConstants.TeamHandLimit);

        // 敌人开战 buff：内容在 EnemyDto.buffRefs 声明（木桩的"每回合回血"等）。
        // 必须在 onWaveStart / 首个 onTurnStart 之前挂载，否则第一次钩子会漏。
        // 换波时也通过 ApplyEnemyInitialBuffs 挂载。
        simulation.ApplyEnemyInitialBuffs();
        simulation.Mechanisms.WaveEntry();
        if (team.IsDefeated)
            return;

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
        SkillPayloadExecutor.Execute(simulation, skill, source, ResolveBattleStartTargets(simulation, skill, source), entry.Params);
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
            ECombatPhase.Marble => TryApplyMarblePhase(simulation, command),
            _ => new CombatApplyResult(false, $"阶段 {Phase} 不支持该指令。"),
        };
    }

    private static CombatApplyResult TryApplyMarblePhase(CombatSimulation simulation, ICombatCommand command)
    {
        if (command is LaunchMarbleCommand launch)
            return simulation.LaunchMarble(launch.AngleRadians);
        if (command is MarkMarbleTargetCommand mark)
            return simulation.MarkMarbleTarget(mark.EnemyIndex);
        if (command is EndMarbleCommand)
            return simulation.EndMarblesEarly();

        var result = command switch
        {
            CastActiveSkillCommand cast => ApplyCastActiveSkill(simulation, cast),
            UseConsumableCommand item => ApplyUseConsumable(simulation, item),
            _ => new CombatApplyResult(false, "UI_COMBAT_MARBLE_ACTION_INVALID"),
        };
        if (result.Success)
        {
            simulation.FlushOnDamagedHits();
            simulation.CheckEndConditions();
            if (simulation.Phase == ECombatPhase.Marble)
            {
                simulation.RefreshMarbleMark();
                simulation.EnemyIntents.RefreshAll();
            }
        }
        return result;
    }

    private static CombatApplyResult TryApplyPlayerPhase(CombatSimulation simulation, ICombatCommand command)
    {
        var result = command switch
        {
            PlayCardCommand playCard => ApplyPlayCard(simulation, playCard),
            CastActiveSkillCommand castSkill => ApplyCastActiveSkill(simulation, castSkill),
            UseConsumableCommand consumable => ApplyUseConsumable(simulation, consumable),
            EndTurnCommand => ApplyEndTurn(simulation),
            TriggerOrbsCommand => ApplyTriggerOrbs(simulation),
            _ => new CombatApplyResult(false, "玩家阶段尚未实现该指令。"),
        };

        if (result.Success && simulation.Phase == ECombatPhase.Player)
            simulation.CheckEndConditions();

        return result;
    }

    private static CombatApplyResult ApplyUseConsumable(CombatSimulation simulation, UseConsumableCommand command)
    {
        if (command.ConsumableSlot < 0 || command.ConsumableSlot >= CombatItemInventory.ConsumableCapacity ||
            simulation.Items.Consumables[command.ConsumableSlot] != command.ItemId ||
            !simulation.Definitions.Store.TryGetItem(command.ItemId, out var item) || item.Kind != EItemKind.Consumable ||
            !CombatCommandValidator.TryGetCharacter(simulation, command.CharacterIndex, out _, out _))
            return new CombatApplyResult(false, "UI_ITEM_INVALID");
        if (item.UseSkillRefs.Count == 0 || item.UseSkillRefs.Any(reference =>
            !simulation.Definitions.Store.TryGetSkill(reference.SkillId, out _)))
            return new CombatApplyResult(false, "UI_ITEM_INVALID");
        if (!simulation.CanUseItemInCurrentPhase(item))
            return new CombatApplyResult(false, "UI_COMBAT_MARBLE_ACTION_INVALID");

        var spec = item.TargetSpec ?? new TargetSpecDto { Side = ETargetSide.Self, Scope = ETargetScope.Self };
        var targets = CombatTargeting.ResolvePlayerTargets(simulation, spec.Side, spec.Scope, spec.TargetCount,
            command.CharacterIndex, command.Targets, out _);
        if (targets is null)
            return new CombatApplyResult(false, "UI_ITEM_INVALID_TARGET");

        simulation.Items.RemoveConsumable(command.ConsumableSlot, command.ItemId);
        foreach (var reference in item.UseSkillRefs)
        {
            simulation.Definitions.Store.TryGetSkill(reference.SkillId, out var skill);
            SkillPayloadExecutor.Execute(simulation, skill, new(ECombatSide.Player, command.CharacterIndex), targets, reference.Params);
        }
        return new CombatApplyResult(true);
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

    private static CombatApplyResult ApplyPlayCard(CombatSimulation simulation, PlayCardCommand command)
    {
        var validation = CombatCommandValidator.ValidateCardPlay(simulation, command,
            out var character, out var slot, out var card, out var targets, out var paid);
        if (!validation.Success)
            return validation;

        if (!character.TryConsumeAvailableEnergy(paid))
            return new CombatApplyResult(false, "可用能量不足。");

        var previousChannel = simulation.CurrentDiscardChannel;
        simulation.SetDiscardChannel(EDiscardChannel.CardExecution);
        simulation.RecordPlayedCard(card.Id, command.CharacterIndex);
        simulation.Presentation.Emit(new CardSettleStartedEvent(
            command.CharacterIndex, slot.SlotIndex, card.Id, targets));
        var runtimeId = slot.RuntimeInstanceId!;
        character.BeginResolvingCard(slot);
        PresentationEmitter.EmitEnergy(simulation, command.CharacterIndex);
        simulation.Presentation.Emit(new CardDiscardedEvent(
            command.CharacterIndex, slot.SlotIndex, card.Id, EDiscardChannel.CardExecution, runtimeId));
        try
        {
            using var cardScope = simulation.EnterCardContext(card.CardType, command.CharacterIndex, card.Element, 0f);
            var source = new CombatTargetRef(ECombatSide.Player, command.CharacterIndex);
            foreach (var reference in card.SkillRefs)
            {
                if (simulation.Definitions.Store.TryGetSkill(reference.SkillId, out var skill))
                    SkillPayloadExecutor.Execute(simulation, skill, source, targets, reference.Params);
            }

            simulation.Buffs.FireCardSettled(simulation, command.CharacterIndex);
            simulation.FlushOnDamagedHits();
        }
        finally
        {
            character.FinishResolvingCard();
            simulation.SetDiscardChannel(previousChannel);
        }

        simulation.Presentation.Emit(new CardSettleEndedEvent(command.CharacterIndex, slot.SlotIndex, card.Id));
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
        if (simulation.Phase == ECombatPhase.Marble && skill.CardPhaseOnly)
            return new CombatApplyResult(false, "UI_COMBAT_MARBLE_ACTION_INVALID");

        var resolvedTargets = CombatTargeting.ResolveActiveSkillTargets(
            simulation,
            skill,
            command.CharacterIndex,
            command.Targets,
            out var targetError);
        if (resolvedTargets is null)
            return new CombatApplyResult(false, targetError);

        if (command.DiscardSlots is { Count: > 0 } slots && (command.DiscardCards is { Count: > 0 } ||
            slots.Any(index => index < 0 || index >= character.HandSlots.Count || character.HandSlots[index].IsEmpty)))
            return new CombatApplyResult(false, "请选择合法数量的手牌进行弃置。");

        var chosen = command.DiscardCards ?? command.DiscardSlots?.Select(index =>
            new HandCardChoice(command.CharacterIndex, character.HandSlots[index].RuntimeInstanceId!)).ToArray() ?? [];
        var plan = DiscardSelection.BuildPlan(simulation, skill, command.CharacterIndex, resolvedTargets);
        List<AssignedDiscard>? plannedDiscards = null;
        if (plan is not null && !plan.TryAssign(chosen, out plannedDiscards) ||
            plan is null && chosen.Count > 0)
            return new CombatApplyResult(false, "请选择合法数量的手牌进行弃置。");

        // 先扣阈值再跑载荷：载荷里可显式 +S 做连发（规格 §5.3）。
        character.PaySkillCounter(character.GetTierThreshold(tierIndex));
        if (skill.TargetOverride?.Scope == ETargetScope.RandomN)
            simulation.RecordRandomTargetSkillCast();

        var source = new CombatTargetRef(ECombatSide.Player, command.CharacterIndex);
        var previousChannel = simulation.CurrentDiscardChannel;
        var previousSelection = simulation.SelectedDiscardCards;
        var previousPlan = simulation.PlannedDiscards;
        simulation.SelectedDiscardCards = null;
        simulation.PlannedDiscards = plannedDiscards;
        simulation.SetDiscardChannel(EDiscardChannel.ActiveSkill);
        try
        {
            SkillPayloadExecutor.Execute(simulation, skill, source, resolvedTargets);
        }
        finally
        {
            simulation.SetDiscardChannel(previousChannel);
            simulation.SelectedDiscardCards = previousSelection;
            simulation.PlannedDiscards = previousPlan;
        }

        // 被动钩子：持有者释放主动技后触发（如 chalux 被动6）。
        simulation.Buffs.FireActiveSkillCast(simulation, command.CharacterIndex);

        return new CombatApplyResult(true);
    }

    #endregion

    private static CombatApplyResult ApplyEndTurn(CombatSimulation simulation)
    {
        simulation.BeginMarblePhase();
        return new CombatApplyResult(true);
    }

    private static void ResolveEndPlayerTurn(CombatSimulation simulation)
    {
        simulation.NormalAttacks.SettleMarbles(simulation, simulation.Marble.Count, simulation.MarbleMarkedEnemyIndex);
        simulation.FlushOnDamagedHits();
        simulation.CheckEndConditions();
        if (simulation.Phase == ECombatPhase.CardExecution)
            simulation.TransitionTo(ECombatPhase.Enemy);
    }

    private static void ExecuteEnemyPhase(CombatSimulation simulation)
    {
        simulation.EnemyPhaseNumber++;
        var enemyCount = simulation.EnemyTeam.Enemies.Count;
        simulation.SetDiscardChannel(EDiscardChannel.Other);
        for (var enemyIndex = 0; enemyIndex < enemyCount; enemyIndex++)
        {
            var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
            if (!enemy.IsAlive || enemy.FirstEnemyPhase > simulation.EnemyPhaseNumber)
                continue;

            simulation.EnemyActionIndex = enemyIndex;
            simulation.EnemyLifeBars.PrepareAction(enemyIndex);
            enemy.RefreshPriorityAction(simulation);

            // 行动计数（2026-09-23）：> 1 时本回合只递减、不行动——把它设为 2 即"推迟到下个回合"。
            if (enemy.ActionCount > 1)
            {
                enemy.ActionCount--;
                enemy.IntentSkillId = null;
                continue;
            }

            var skillId = enemy.NextAction?.SkillId ?? enemy.PlannedSkillId ?? simulation.EnemyAi.ChooseSkill(enemy, simulation.EnemyAiRng);
            enemy.PlannedSkillId = null;
            enemy.IntentSkillId = skillId;
            if (skillId is null)
                continue;

            simulation.Presentation.Emit(new EnemyActionStartedEvent(enemyIndex, skillId));
            var lifeBarBefore = enemy.LifeBarIndex;
            var sequenceBefore = enemy.ActionSequenceIndex;
            ExecuteEnemySkill(simulation, enemyIndex, skillId);
            simulation.Presentation.Emit(new EnemyActionEndedEvent(enemyIndex, skillId));
            enemy.CompleteSequenceAction(lifeBarBefore, sequenceBefore);
        }

        // 回合结束管线（规则 / 领域 / buff 时长 / 充能球产出）与玩家阶段清波共用 ResolveTurnEnd，
        // 每回合只结算一次（幂等）。
        simulation.ResolveTurnEnd();

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
