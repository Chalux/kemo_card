using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>战斗机制独立扣队伍生命，不经过攻防、倍率、护盾或受击钩子。</summary>
internal sealed class BattleMechanismRuntime(CombatSimulation simulation)
{
    private readonly HashSet<(int Wave, string Enemy, int Bar, string Mechanism)> _triggered = [];

    public void WaveEntry()
    {
        if (simulation.Battle is null)
            return;
        foreach (var mechanism in simulation.Battle.Waves[simulation.CurrentWaveIndex].EntryMechanisms)
            ApplyOnce("", -1, mechanism);
    }

    public void TurnStart()
    {
        var count = simulation.EnemyTeam.Enemies.Count;
        for (var index = 0; index < count && !simulation.PlayerTeam.IsDefeated; index++)
            LifeBarTurnStart(index);
    }

    public void LifeBarTurnStart(int index)
    {
        var enemy = simulation.EnemyTeam.Enemies[index];
        if (!enemy.IsAlive || enemy.IsAwaitingLifeBar || enemy.LifeBars.Count == 0)
            return;
        var turn = simulation.TurnNumber - enemy.LifeBarEnteredTurn + 1;
        foreach (var mechanism in enemy.LifeBars[enemy.LifeBarIndex].TurnStartMechanisms)
            if (turn >= mechanism.Turn)
                ApplyOnce(enemy.RuntimeId, enemy.LifeBarIndex, mechanism);
    }

    private void ApplyOnce(string enemy, int bar, TeamHpMechanismDto mechanism)
    {
        if (simulation.PlayerTeam.IsDefeated || !float.IsFinite(mechanism.MaxHpDamagePercent) ||
            mechanism.MaxHpDamagePercent <= 0 || !_triggered.Add((simulation.CurrentWaveIndex, enemy, bar, mechanism.Id)))
            return;
        var team = simulation.PlayerTeam;
        var before = team.SharedHpExact;
        var amount = team.Asc.GetCurrentValue(AttributeIds.MaxHealth) * (mechanism.MaxHpDamagePercent / 100f);
        team.ApplySharedDamage(amount);
        simulation.Presentation.Emit(new BattleMechanismDamageEvent(mechanism.Id, before - team.SharedHpExact, team.SharedHp, team.MaxHp));
        if (team.IsDefeated)
            simulation.TransitionTo(ECombatPhase.Defeat);
    }
}