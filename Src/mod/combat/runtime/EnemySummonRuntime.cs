using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Combat.Runtime;

internal sealed class EnemySummonRuntime(CombatSimulation simulation)
{
    private int _nextId;

    public bool CanSummon(EnemyUnit source, string enemyId, int maxAlive, int maxSummons) =>
        simulation.EnemyTeam.Enemies.Count(enemy => enemy.IsAlive && enemy.DefinitionId == enemyId) < maxAlive &&
        source.SummonsThisLifeBar(enemyId) < maxSummons;

    public void Summon(CombatTargetRef source, string enemyId, int count, int maxAlive, int maxSummons, bool track)
    {
        if (source.Side != ECombatSide.Enemy || source.Index < 0 || source.Index >= simulation.EnemyTeam.Enemies.Count)
            return;
        var summoner = simulation.EnemyTeam.Enemies[source.Index];
        if (!summoner.IsAlive)
            return;
        count = Math.Min(Math.Clamp(count, 0, 16), Math.Max(0, maxAlive -
            simulation.EnemyTeam.Enemies.Count(enemy => enemy.IsAlive && enemy.DefinitionId == enemyId)));
        if (track)
            count = Math.Min(count, Math.Max(0, maxSummons - summoner.SummonsThisLifeBar(enemyId)));
        for (var i = 0; i < count; i++)
        {
            if (!simulation.EffectBudget.TrySpendStep())
                break;
            var units = CombatSimulationFactory.SpawnWaveEnemies(new BattleWaveDto
            {
                EnemySpawns = [new() { EnemyId = enemyId }],
            }, simulation.Definitions, out _);
            if (units is null)
                break;
            var spawned = units[0];
            spawned.SetSummonRuntimeId($"summon-{++_nextId}");
            spawned.FirstEnemyPhase = simulation.EnemyPhaseNumber + 1;
            var index = simulation.EnemyTeam.Add(spawned);
            if (track)
                summoner.RecordSummon(enemyId);
            simulation.EffectLifecycle.AttachHolders();
            simulation.Presentation.Emit(new EnemySummonedEvent(index, enemyId, spawned.CurrentHp, spawned.MaxHp));
            simulation.InitializeEnemy(index);
        }
        simulation.Buffs.RefreshEnemyPresenceConditions(simulation);
        simulation.DomainManager.RefreshDomainBuffs();
    }
}