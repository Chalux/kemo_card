using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>独立生命条的切换边界；待换条者仍是合法目标，不被清波与阵亡表现提前移除。</summary>
internal sealed class EnemyLifeBarRuntime(CombatSimulation simulation)
{
    private static readonly BuffDto TransitionImmunity = new()
    {
        Id = "combat.life_bar_immunity",
        DisplayNameId = "buff.life_bar_immunity.name",
        DescId = "buff.life_bar_immunity.desc",
        DurationType = EBuffDurationType.Permanent,
        Tags = [BuiltinBuffTags.TraitImmuneDamage, BuiltinBuffTags.Undispellable],
    };

    public bool OnDepleted(int enemyIndex)
    {
        var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
        if (!enemy.HasNextLifeBar || enemy.IsAwaitingLifeBar || enemy.CurrentHpExact > 0f)
            return false;
        enemy.IsAwaitingLifeBar = true;
        if (simulation.Phase == ECombatPhase.CardExecution ||
            (simulation.Phase == ECombatPhase.Enemy && simulation.EnemyActionIndex < enemyIndex))
        {
            enemy.Buffs.Add(TransitionImmunity, null);
            simulation.Presentation.Emit(new BuffAppliedEvent(new(new(ECombatSide.Enemy, enemyIndex)), TransitionImmunity.Id, 1));
            simulation.EnemyIntents.Refresh(enemyIndex);
            return false;
        }
        return true;
    }

    public void PrepareAction(int enemyIndex)
    {
        if (simulation.EnemyTeam.Enemies[enemyIndex].IsAwaitingLifeBar)
            Advance(enemyIndex);
    }

    private void Advance(int enemyIndex)
    {
        var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
        simulation.Buffs.ResetEnemyLifeBar(simulation, enemyIndex);
        enemy.AdvanceLifeBar();
        enemy.LifeBarEnteredTurn = simulation.TurnNumber;
        simulation.Buffs.RefreshEnemyPresenceConditions(simulation);
        simulation.Presentation.Emit(new EnemyLifeBarChangedEvent(enemyIndex, enemy.LifeBarIndex, enemy.LifeBarCount, enemy.CurrentHp, enemy.MaxHp));
        simulation.ExecuteEnemyEntrySkills(enemyIndex, enemy.LifeBars[enemy.LifeBarIndex].EntrySkillRefs);
        simulation.Mechanisms.LifeBarTurnStart(enemyIndex);
        simulation.EnemyIntents.Refresh(enemyIndex);
    }
}