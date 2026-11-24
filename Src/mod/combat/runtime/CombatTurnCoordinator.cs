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
        // 回合边界先重置本轮弹珠点数 / 队列，再跑回合开始钩子：
        // 钩子（onTurnStart / 被动）追加的弹珠必须留在队列里，不能被随后的 BeginTurn 清掉。
        simulation.Marble.BeginTurn(simulation.TurnNumber, simulation.PlayerTeam.Characters.Count);
        simulation.Mechanisms.TurnStart();
        if (simulation.PlayerTeam.IsDefeated)
            return;
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
        if (simulation.Phase == ECombatPhase.Player)
            simulation.EnemyIntents.RefreshAll();
    }
}