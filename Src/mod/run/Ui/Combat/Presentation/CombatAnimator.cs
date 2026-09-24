using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Global.Ui.Themes;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// 表现事件 → 动画（Run 规格 §14.4）。按事件类型分派到各个小动作；未处理的类型零时长完成。
/// 只依赖 <see cref="ICombatStageView"/>，不读模拟器——数值全部来自事件载荷。
/// 时长常量见 <see cref="CombatAnimationTiming"/>。
/// </summary>
public partial class CombatAnimator : Node, ICombatEventPlayer
{
    private ICombatStageView? _view;

    public void Bind(ICombatStageView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
    }

    public Task PlayAsync(CombatPresentationEvent presentationEvent)
    {
        if (_view is null || !IsInsideTree())
            return Task.CompletedTask;

        return presentationEvent switch
        {
            PhaseChangedEvent evt => PlayPhaseChangedAsync(evt),
            WaveStartedEvent => PlayWaveStartedAsync(),
            CardsDrawnEvent evt => PlayCardsDrawnAsync(evt),
            CardDiscardedEvent evt => PlayCardDiscardedAsync(evt),
            CardSettleStartedEvent evt => PlayCardSettleStartedAsync(evt),
            CardSettleEndedEvent evt => PlayCardSettleEndedAsync(evt),
            DamageDealtEvent evt => PlayDamageDealtAsync(evt),
            HealedEvent evt => PlayHealedAsync(evt),
            BuffAppliedEvent evt => PlayBuffAppliedAsync(evt.Holder, evt.BuffId),
            BuffStacksChangedEvent evt => PlayBuffAppliedAsync(evt.Holder, evt.BuffId),
            BuffRemovedEvent evt => PlayBuffRemovedAsync(evt),
            NormalAttackStrikeEvent evt => PlayNormalAttackAsync(evt),
            EnemyActionStartedEvent evt => PlayEnemyActionStartedAsync(evt),
            EnemyActionEndedEvent evt => PlayEnemyActionEndedAsync(evt),
            OrbGainedEvent evt => PlayOrbGainedAsync(evt),
            OrbsTriggeredEvent => PlayOrbsTriggeredAsync(),
            EnergyChangedEvent evt => PlayEnergyChanged(evt),
            _ => Task.CompletedTask,
        };
    }

    #region 相位 / 波次

    private Task PlayPhaseChangedAsync(PhaseChangedEvent evt)
    {
        // 只对玩家可感知的相位切换打横幅；执行 / 敌方相位切换由后续动作本身表达。
        var key = evt.To switch
        {
            ECombatPhase.Player => "UI_COMBAT_PHASE_PLAYER",
            ECombatPhase.Enemy => "UI_COMBAT_PHASE_ENEMY",
            ECombatPhase.Victory => "UI_COMBAT_PHASE_VICTORY",
            ECombatPhase.Defeat => "UI_COMBAT_PHASE_DEFEAT",
            _ => null,
        };
        if (key is null)
            return Task.CompletedTask;

        var text = evt.To == ECombatPhase.Player
            ? string.Format(Localization.Tr(key), evt.TurnNumber)
            : Localization.Tr(key);
        return _view!.ShowPhaseBannerAsync(text, CombatAnimationTiming.PhaseBanner);
    }

    private async Task PlayWaveStartedAsync()
    {
        _view!.RebuildEnemies();
        await UnitTweens.WaitAsync(this, CombatAnimationTiming.EnemyBeat);
    }

    #endregion

    #region 卡牌

    private async Task PlayCardsDrawnAsync(CardsDrawnEvent evt)
    {
        foreach (var drawn in evt.Cards)
        {
            var slot = _view!.GetHandSlot(evt.CharacterIndex, drawn.SlotIndex);
            if (slot is null)
                continue;

            _view.RefreshHandSlot(evt.CharacterIndex, drawn.SlotIndex);
            await slot.PopInAsync(CombatAnimationTiming.CardDraw);
        }
    }

    private async Task PlayCardDiscardedAsync(CardDiscardedEvent evt)
    {
        var slot = _view!.GetHandSlot(evt.CharacterIndex, evt.SlotIndex);
        if (slot is null || !slot.HasCard)
            return;

        await slot.FlyOutAsync(_view.GraveyardGlobalCenter, CombatAnimationTiming.CardDiscard);
        _view.RefreshHandSlot(evt.CharacterIndex, evt.SlotIndex);
    }

    private async Task PlayCardSettleStartedAsync(CardSettleStartedEvent evt)
    {
        var caster = _view!.GetAllyUnit(evt.CharacterIndex);
        if (caster is null)
            return;

        _view.GetHandSlot(evt.CharacterIndex, evt.SlotIndex)?.SetPending(true);

        var approach = ResolveApproachPosition(caster, evt.Targets);
        if (approach.HasValue)
            await caster.MoveToAsync(approach.Value, CombatAnimationTiming.UnitMove);

        await caster.AttackPulseAsync(CombatAnimationTiming.UnitAttack);
        await UnitTweens.WaitAsync(this, CombatAnimationTiming.StrikeHold);
    }

    private async Task PlayCardSettleEndedAsync(CardSettleEndedEvent evt)
    {
        _view!.GetHandSlot(evt.CharacterIndex, evt.SlotIndex)?.SetPending(false);
        var caster = _view.GetAllyUnit(evt.CharacterIndex);
        if (caster is null)
            return;

        caster.Play("idle");
        await caster.ReturnHomeAsync(CombatAnimationTiming.UnitMove);
    }

    /// <summary>目标面前的落点：首个敌方目标左侧一个间距；无敌方目标（自身 / 队伍）则原地不动。</summary>
    private Vector2? ResolveApproachPosition(Control caster, IReadOnlyList<CombatTargetRef> targets)
    {
        foreach (var target in targets)
        {
            if (target.Side != ECombatSide.Enemy)
                continue;

            var enemy = _view!.GetEnemyUnit(target.Index);
            if (enemy is null)
                continue;

            var x = enemy.GlobalPosition.X - caster.Size.X - CombatAnimationTiming.ApproachGap;
            var y = enemy.GlobalPosition.Y + (enemy.Size.Y - caster.Size.Y) / 2f;
            return new Vector2(x, y);
        }

        return null;
    }

    #endregion

    #region 伤害 / 治疗

    private async Task PlayDamageDealtAsync(DamageDealtEvent evt)
    {
        var amount = Mathf.RoundToInt(evt.Amount);
        if (evt.Target.Side == ECombatSide.Enemy)
        {
            var enemy = _view!.GetEnemyUnit(evt.Target.Index);
            if (enemy is null)
                return;

            SpawnNumber(CenterOf(enemy), $"-{amount}", KemoPalette.Danger);
            var shake = enemy.ShakeAsync(CombatAnimationTiming.UnitShake);
            var hp = enemy.HpBar?.AnimateToAsync(evt.TargetHpAfter, evt.TargetMaxHp, CombatAnimationTiming.HpBar)
                ?? Task.CompletedTask;
            await Task.WhenAll(shake, hp);
            if (!evt.TargetAlive)
            {
                await enemy.FadeOutAsync(CombatAnimationTiming.UnitFadeOut);
                _view.RefreshEnemy(evt.Target.Index);
            }

            return;
        }

        // 玩家侧一律扣队伍账本：飘字挂在被点名的友方单位（或队伍血条）上，血条动到账本终值。
        var ally = evt.Target.Index >= 0 ? _view!.GetAllyUnit(evt.Target.Index) : null;
        var shared = _view!.SharedHpBar;
        var anchor = ally is not null ? CenterOf(ally) : shared is not null ? CenterOf(shared) : (Vector2?)null;
        if (anchor.HasValue)
            SpawnNumber(anchor.Value, $"-{amount}", KemoPalette.Danger);

        var allyShake = ally?.ShakeAsync(CombatAnimationTiming.UnitShake) ?? Task.CompletedTask;
        var sharedHp = shared?.AnimateToAsync(evt.TargetHpAfter, evt.TargetMaxHp, CombatAnimationTiming.HpBar)
            ?? Task.CompletedTask;
        await Task.WhenAll(allyShake, sharedHp);
    }

    private async Task PlayHealedAsync(HealedEvent evt)
    {
        var amount = Mathf.RoundToInt(evt.Amount);
        if (amount <= 0)
            return;

        if (evt.Target.Side == ECombatSide.Enemy)
        {
            var enemy = _view!.GetEnemyUnit(evt.Target.Index);
            if (enemy is null)
                return;

            SpawnNumber(CenterOf(enemy), $"+{amount}", KemoPalette.Accent);
            await (enemy.HpBar?.AnimateToAsync(evt.HpAfter, evt.MaxHp, CombatAnimationTiming.HpBar) ?? Task.CompletedTask);
            return;
        }

        var shared = _view!.SharedHpBar;
        if (shared is null)
            return;

        SpawnNumber(CenterOf(shared), $"+{amount}", KemoPalette.Accent);
        await shared.AnimateToAsync(evt.HpAfter, evt.MaxHp, CombatAnimationTiming.HpBar);
    }

    private void SpawnNumber(Vector2 globalCenter, string text, Color color)
    {
        var number = _view!.SpawnDamageNumber();
        if (number is null)
            return;

        _ = number.PlayAsync(globalCenter, text, color, CombatAnimationTiming.DamageNumber);
    }

    private static Vector2 CenterOf(Control control) => control.GlobalPosition + control.Size / 2f;

    #endregion

    #region Buff

    private async Task PlayBuffAppliedAsync(BuffHolderRef holder, string buffId)
    {
        _view!.RefreshBuffs(holder);
        var icon = _view.GetBuffList(holder)?.FindIcon(buffId);
        if (icon is null)
            return;

        await UnitTweens.PulseAsync(icon, 1.3f, CombatAnimationTiming.BuffPop);
    }

    private async Task PlayBuffRemovedAsync(BuffRemovedEvent evt)
    {
        var icon = _view!.GetBuffList(evt.Holder)?.FindIcon(evt.BuffId);
        if (icon is not null)
        {
            await UnitTweens.FadeAsync(icon, 0f, CombatAnimationTiming.BuffPop);
            icon.Modulate = icon.Modulate with { A = 1f };
        }

        _view.RefreshBuffs(evt.Holder);
    }

    #endregion

    #region 普攻 / 敌人 / 球 / 能量

    private async Task PlayNormalAttackAsync(NormalAttackStrikeEvent evt)
    {
        var attacker = _view!.GetAllyUnit(evt.CharacterIndex);
        if (attacker is null || evt.Targets.Count == 0)
            return;

        await attacker.AttackPulseAsync(CombatAnimationTiming.UnitAttack);
        attacker.Play("idle");
    }

    private async Task PlayEnemyActionStartedAsync(EnemyActionStartedEvent evt)
    {
        var enemy = _view!.GetEnemyUnit(evt.EnemyIndex);
        if (enemy is null)
            return;

        await UnitTweens.WaitAsync(this, CombatAnimationTiming.EnemyBeat);
        await enemy.LungeAsync(CombatAnimationTiming.UnitAttack);
    }

    private Task PlayEnemyActionEndedAsync(EnemyActionEndedEvent evt) =>
        UnitTweens.WaitAsync(this, CombatAnimationTiming.EnemyBeat);

    private Task PlayOrbGainedAsync(OrbGainedEvent evt)
    {
        var orbs = _view!.OrbQueue;
        if (orbs is null)
            return Task.CompletedTask;

        // 按载荷先画这一格再闪：满员自动触发的球在事件到达界面时队列已经清空，
        // 读状态重绘会让刚入队的球不可见（整排闪光的 OrbsTriggeredEvent 随后才清空）。
        _view.PaintOrb(evt.QueueCount - 1, evt.OrbTypeId, evt.QueueCount);
        return orbs.FlashSlotAsync(evt.QueueCount - 1, CombatAnimationTiming.OrbFlash);
    }

    private async Task PlayOrbsTriggeredAsync()
    {
        var orbs = _view!.OrbQueue;
        if (orbs is null)
            return;

        // 触发事件在结算前记录（队列已清空）：先整排闪光，再重绘成空队列。
        await orbs.FlashAllAsync(CombatAnimationTiming.OrbFlash);
        _view.RefreshOrbs();
    }

    private Task PlayEnergyChanged(EnergyChangedEvent evt)
    {
        if (evt.CharacterIndex == _view!.ControlledSlot)
            _view.ActorInfo?.SetEnergy(evt.Current, evt.Available, evt.Max);
        return Task.CompletedTask;
    }

    #endregion
}