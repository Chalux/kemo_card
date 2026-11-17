using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>玩家阶段预定技能；界面刷新不消耗随机数，敌方阶段复用预定结果。</summary>
internal sealed class EnemyIntentRuntime(CombatSimulation simulation)
{
    private readonly Dictionary<string, EnemyIntentChangedEvent> _published = new();

    public void RefreshAll()
    {
        for (var index = 0; index < simulation.EnemyTeam.Enemies.Count; index++)
            Refresh(index);
    }

    public void Refresh(int index)
    {
        var enemy = simulation.EnemyTeam.Enemies[index];
        if (!enemy.IsAlive || enemy.IsAwaitingLifeBar)
            enemy.PlannedSkillId = null;
        else
        {
            enemy.RefreshPriorityAction(simulation);
            enemy.PlannedSkillId = enemy.NextAction?.SkillId ?? enemy.PlannedSkillId ??
                simulation.EnemyAi.ChooseSkill(enemy, simulation.EnemyAiRng);
        }
        var name = "";
        var description = "";
        if (enemy.PlannedSkillId is { } skillId && simulation.Definitions.Store.TryGetSkill(skillId, out var skill))
        {
            name = skill.DisplayNameId;
            description = skill.DescId;
        }
        var intent = new EnemyIntentChangedEvent(index, name, description, enemy.ActionCount, enemy.IsAwaitingLifeBar);
        enemy.Intent = intent;
        if (!_published.TryGetValue(enemy.RuntimeId, out var previous) || previous != intent)
        {
            _published[enemy.RuntimeId] = intent;
            if (previous is not null || name.Length > 0 || enemy.IsAwaitingLifeBar)
                simulation.Presentation.Emit(intent);
        }
    }
}