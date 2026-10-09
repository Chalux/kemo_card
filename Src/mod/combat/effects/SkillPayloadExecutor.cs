using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

/// <summary>技能载荷执行与引用参数覆盖；Action 载荷优先于兼容 Effect 载荷。</summary>
internal static class SkillPayloadExecutor
{
    public static void Execute(
        CombatSimulation simulation,
        SkillDto skill,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object>? skillParams = null)
    {
        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return;
        if (skill.ActionRefs.Count > 0)
        {
            foreach (var actionRef in skill.ActionRefs)
            {
                simulation.EffectExecutor.ExecuteSkillActionRef(actionRef, simulation, source, targets, skillParams);
            }
            return;
        }

        foreach (var effectRef in skill.EffectRefs)
        {
            simulation.EffectExecutor.ExecuteEffectRef(effectRef, simulation, source, targets, skillParams);
        }
    }

}