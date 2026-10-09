using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>首回合、后续回合及清波补结算共用的边界；不允许重复收尾或覆盖终局。</summary>
internal sealed class CombatTurnCoordinator(CombatSimulation simulation)
{
    private bool _endResolved;

    public void End()
    {
        if (_endResolved)
            return;
        _endResolved = true;
        foreach (var entry in simulation.CardQueue.PeekAllOrdered())
            CombatStateMachine.CancelMarkAndRefund(simulation, entry);
        simulation.Rules.DispatchTurnEnd(simulation.CreateContext());
        simulation.DomainManager.FireTurnEndHooks();
        var orbBonuses = simulation.Orbs.CaptureTurnEndBonuses(simulation);
        simulation.Buffs.FireTurnEnd(simulation);
        simulation.FlushOnDamagedHits();
        simulation.Orbs.GrantTurnEndOrbs(simulation, simulation.TakePlayedThisTurn(), orbBonuses);
    }

    public void BeginNext(bool incrementTurnsIntoWave)
    {
        _endResolved = false;
        simulation.MarkFirstPlayerPhaseDone();
        simulation.IncrementTurnNumber();
        if (incrementTurnsIntoWave)
            simulation.IncrementTurnsIntoWave();
        simulation.ResetOrbsTriggeredThisTurn();
        // 魔法受击账与回合边界对齐：本回合账滚动为"上一回合"账（TookMagicDamageLastTurn 条件读它）。
        // 必须在 Start() 之前——回合开始钩子（onTurnStart）读到的就是上一回合的账。
        simulation.RollMagicDamageTurnLedger();
        Start();
    }

    public void Start()
    {
        var wave = simulation.CurrentWaveIndex;
        simulation.Buffs.ExpireTurnStartBuffs(simulation);
        simulation.Rules.DispatchTurnStart(simulation.CreateContext());
        simulation.DomainManager.FireTurnStartHooks();
        simulation.Buffs.FireTurnStart(simulation, expireBoundary: false);
        simulation.FlushOnDamagedHits();
        simulation.CheckEndConditions();
        if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat || simulation.CurrentWaveIndex != wave)
            return;
        simulation.TransitionTo(ECombatPhase.Player);
        PlayerPhasePipeline.Run(simulation, simulation.IsFirstPlayerPhase);
    }
}