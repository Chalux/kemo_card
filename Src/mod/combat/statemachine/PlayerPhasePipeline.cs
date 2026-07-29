using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

/// <summary>
/// 规格 §6.2：每个玩家阶段开始（含首个）对每名角色依次执行的权威顺序。
/// 费用变化不在此统一扫描——那是玩家阶段内的即时对账（§3.3 / <see cref="QueuedCostReconciler"/>）。
/// </summary>
public static class PlayerPhasePipeline
{
    public static void Run(CombatSimulation simulation, bool isFirstPlayerPhase)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var characters = simulation.PlayerTeam.Characters;
        for (var characterIndex = 0; characterIndex < characters.Count; characterIndex++)
        {
            var character = characters[characterIndex];
            character.ResetPhaseShuffleBudget();

            if (!isFirstPlayerPhase)
                character.RegenCurrentEnergy();

            character.RefillAvailableEnergy();
            character.TickSkillCounter();

            if (!isFirstPlayerPhase)
                character.DrawWithReshuffle(character.ComputeDrawCount(), simulation.DrawRng);

            // 抽牌数量修正只作用于本阶段的抽牌步骤，用完即弃。
            character.ClearDrawModifiers();

            // 规格 §6.2 步骤 5：资源管线跑完后再套用封印行动封锁。
            CombatStateMachine.EnforceSeal(simulation, characterIndex);
        }

        simulation.MarkFirstPlayerPhaseDone();
    }
}