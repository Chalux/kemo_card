using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Runtime;

public sealed partial class CombatSimulation
{
    // 首次发射前不计时。暂停时宿主不提交固定步。
    private int _marbleTicksSinceLaunch = -1;
    public const int MarbleEarlyEndTicks = 12 * MarblePhysicsConfig.StepsPerSecond;
    public int MarbleMarkedEnemyIndex { get; private set; } = -1;
    public bool MarbleTimeLimitReached => _marbleTicksSinceLaunch >= MarbleEarlyEndTicks;
    public bool CanEndMarbleEarly => Phase == ECombatPhase.Marble &&
        (MarbleTimeLimitReached ||
         NormalAttacks.CanClearWithMarbles(this, Marble.Count, MarbleMarkedEnemyIndex));

    public bool CanUseItemInCurrentPhase(ItemDto item) => Phase == ECombatPhase.Player ||
        Phase == ECombatPhase.Marble && !item.CardPhaseOnly && item.UseSkillRefs.All(reference =>
            Definitions.Store.TryGetSkill(reference.SkillId, out var skill) && !skill.CardPhaseOnly);

    #region 弹珠阶段

    internal void BeginMarblePhase()
    {
        _marbleTicksSinceLaunch = -1;
        MarbleMarkedEnemyIndex = -1;
        var owner = Marble.OwnerSlotIndex;
        if (owner >= 0)
        {
            var bonus = (int)PlayerTeam.Characters[owner].Asc.GetCurrentValue(AttributeIds.NormalAttackCount);
            for (var i = 0; i < bonus; i++)
                Marble.EnqueueBall(Marble.BaseBallSpec);
        }
        RefreshMarbleMark();
        TransitionTo(ECombatPhase.Marble);
        FinishIfMarblesEmpty();
    }

    internal CombatApplyResult LaunchMarble(float angle)
    {
        if (!Marble.TryLaunch(angle))
            return new(false, "UI_COMBAT_MARBLE_ACTION_INVALID");
        if (_marbleTicksSinceLaunch < 0)
            _marbleTicksSinceLaunch = 0;
        return new(true);
    }

    public void StepMarble()
    {
        if (Phase != ECombatPhase.Marble)
            return;
        if (_marbleTicksSinceLaunch is >= 0 and < MarbleEarlyEndTicks)
            _marbleTicksSinceLaunch++;
        if (Marble.ActiveBalls.Count > 0)
            Marble.StepPhysics();
        FinishIfMarblesEmpty();
    }

    private void FinishIfMarblesEmpty()
    {
        if (Marble.Queue.Count == 0 && Marble.ActiveBalls.Count == 0)
            TransitionTo(ECombatPhase.CardExecution);
    }

    internal CombatApplyResult EndMarblesEarly()
    {
        if (!CanEndMarbleEarly)
            return new(false, "UI_COMBAT_MARBLE_ACTION_INVALID");
        TransitionTo(ECombatPhase.CardExecution);
        return new(true);
    }

    internal CombatApplyResult MarkMarbleTarget(int index)
    {
        if (index < 0 || !LegalMarbleMarks().Contains(index))
            return new(false, "UI_COMBAT_ILLEGAL_TARGET");
        MarbleMarkedEnemyIndex = index;
        return new(true);
    }

    internal void RefreshMarbleMark()
    {
        var legal = LegalMarbleMarks();
        if (!legal.Contains(MarbleMarkedEnemyIndex))
            MarbleMarkedEnemyIndex = legal.FirstOrDefault(-1);
    }

    public IReadOnlyList<int> LegalMarbleMarks() => NormalAttack.NormalAttackRuntime.LegalMarks(this);

    #endregion
}