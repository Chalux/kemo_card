using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

/// <summary>
/// 玩家回合开始时依次补充能量、主动技进度和手牌。
/// </summary>
public static class PlayerPhasePipeline
{
    public static void Run(CombatSimulation simulation, bool isFirstPlayerPhase)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var characters = simulation.PlayerTeam.Characters;

        // 弃牌记账（DiscardAndRecord）账期 = 本玩家阶段：主动技在玩家阶段内释放，
        // 其"实际弃置张数"只对本回合的读取方（按弃牌数补抽 / 扣减行动次数）有效。
        simulation.ClearLastDiscardCount();

        long drawCount = 0;
        for (var characterIndex = 0; characterIndex < characters.Count; characterIndex++)
        {
            var character = characters[characterIndex];
            if (!isFirstPlayerPhase)
                character.RegenCurrentEnergy();

            character.RefillAvailableEnergy();
            PresentationEmitter.EmitEnergy(simulation, characterIndex);
            character.TickSkillCounter();

            if (!isFirstPlayerPhase)
                drawCount += character.ComputeDrawCount();

            // 抽牌数量修正只作用于本阶段的抽牌步骤，用完即弃。
            character.ClearDrawModifiers();
        }

        PresentationEmitter.DrawTeamAndEmit(simulation, (int)Math.Min(drawCount, CombatConstants.TeamHandLimit));
        simulation.MarkFirstPlayerPhaseDone();
    }
}